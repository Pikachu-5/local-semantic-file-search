import os
import time
import threading
from typing import List, Dict, Any, Tuple, Set
from watchdog.observers import Observer
from watchdog.events import FileSystemEventHandler, FileSystemEvent

from db import VectorDatabase
from model import EmbeddingEngine
from parser import FileParser
from config import ConfigManager

class IndexScanner:
    """
    Crawls directories recursively, filters by file extension and exclusion lists,
    detects modified files, and performs bulk document chunking and vector indexing.
    """
    def __init__(self, db: VectorDatabase, embedder: EmbeddingEngine, config_manager: ConfigManager):
        self.db = db
        self.embedder = embedder
        self.config = config_manager
        self.parser = FileParser()

    def scan_and_index(self, folder_path: str, model_name: str = "BGE-Small-EN-v1.5") -> Tuple[int, int]:
        """
        Scans a directory recursively and indexes new or modified files.
        Returns a tuple of (files_processed, chunks_created).
        """
        if not os.path.exists(folder_path):
            raise FileNotFoundError(f"Folder not found: {folder_path}")

        excluded_dirs = set(self.config.get("excluded_dirs", []))
        included_exts = set(self.config.get("included_extensions", []))
        
        # 1. Retrieve already indexed files to detect modifications
        indexed_files = self.db.get_indexed_files(model_name)
        
        files_to_index = []
        
        # 2. Walk directory tree efficiently
        for root, dirs, files in os.walk(folder_path):
            # Prune directory search in-place to avoid traversing excluded subdirectories
            dirs[:] = [d for d in dirs if d not in excluded_dirs]
            
            for file in files:
                _, ext = os.path.splitext(file)
                ext = ext.lower()
                
                if ext in included_exts:
                    full_path = os.path.abspath(os.path.join(root, file))
                    
                    try:
                        mtime = os.path.getmtime(full_path)
                        # Skip if file already indexed and unmodified
                        if full_path in indexed_files and indexed_files[full_path] >= mtime:
                            continue
                        files_to_index.append((full_path, mtime))
                    except Exception as e:
                        print(f"[-] Error checking file modification: {full_path}. Exception: {e}")

        if not files_to_index:
            print(f"[+] Directory '{folder_path}' is already up-to-date!")
            return 0, 0

        print(f"[*] Found {len(files_to_index)} files requiring indexing in '{folder_path}'...")

        files_processed = 0
        chunks_created = 0

        # 3. Process each file
        for file_path, mtime in files_to_index:
            try:
                # 4. Parse file
                chunks = self.parser.parse(file_path)
                if not chunks:
                    continue
                
                # 5. Embed chunks
                vectors = self.embedder.embed(chunks, model_name)
                
                # 6. Format and package
                db_chunks = []
                for chunk_text, vector in zip(chunks, vectors):
                    db_chunks.append({
                        "vector": vector.tolist(),
                        "file_path": file_path,
                        "file_name": os.path.basename(file_path),
                        "chunk_text": chunk_text,
                        "last_modified": mtime
                    })

                # 7. Delete-then-Insert to keep LanceDB clean
                self.db.delete_file_chunks(file_path, model_name)
                self.db.add_chunks(db_chunks, model_name)
                
                files_processed += 1
                chunks_created += len(chunks)
                
                # Active garbage collection after each file to keep RAM usage optimized
                import gc
                gc.collect()
                
            except Exception as e:
                print(f"[-] Failed to index file {file_path}: {e}")

        # 8. Rebuild Tantivy index for lexical FTS searches
        if files_processed > 0:
            self.db.rebuild_fts_index(model_name)

        import gc
        gc.collect()
        return files_processed, chunks_created


class FileSystemWatchdogHandler(FileSystemEventHandler):
    """
    Handles filesystem events and relays valid events to the DirectoryWatcher
    with filters applied.
    """
    def __init__(self, watcher: 'DirectoryWatcher'):
        self.watcher = watcher

    def _should_handle(self, path: str) -> bool:
        """
        Verifies if the file matches configuration extensions and is not in excluded directories.
        """
        if os.path.isdir(path):
            return False
            
        _, ext = os.path.splitext(path)
        ext = ext.lower()
        
        # Check extensions
        included_exts = set(self.watcher.config.get("included_extensions", []))
        if ext not in included_exts:
            return False
            
        # Check if any path segments are excluded
        excluded_dirs = set(self.watcher.config.get("excluded_dirs", []))
        normalized_path = os.path.abspath(path)
        path_parts = normalized_path.replace("\\", "/").split("/")
        for part in path_parts:
            if part in excluded_dirs:
                return False
                
        return True

    def on_created(self, event: FileSystemEvent):
        if self._should_handle(event.src_path):
            self.watcher.queue_indexing(event.src_path)

    def on_modified(self, event: FileSystemEvent):
        if self._should_handle(event.src_path):
            self.watcher.queue_indexing(event.src_path)

    def on_deleted(self, event: FileSystemEvent):
        if not event.is_directory:
            self.watcher.process_deletion(event.src_path)

    def on_moved(self, event: FileSystemEvent):
        # Handle rename/move cleanly
        self.watcher.process_deletion(event.src_path)
        if self._should_handle(event.dest_path):
            self.watcher.queue_indexing(event.dest_path)


class DirectoryWatcher:
    """
    Manages live background observers using the watchdog library.
    Implements a thread-safe debounce timer to delay indexing while typing.
    """
    def __init__(self, db: VectorDatabase, embedder: EmbeddingEngine, config_manager: ConfigManager):
        self.db = db
        self.embedder = embedder
        self.config = config_manager
        self.parser = FileParser()
        
        self.observer = None
        self.watch_handles = {}  # folder_path -> Watch object
        self.timers = {}         # file_path -> threading.Timer
        self.lock = threading.Lock()
        self.debounce_seconds = 2.0

    def start(self) -> None:
        """
        Starts the background observer and loads watched folders from config.
        """
        with self.lock:
            if self.observer is not None:
                return
            
            self.observer = Observer()
            self.observer.start()
            
            # Auto-load watched folders from config
            watched = self.config.get("watched_folders", [])
            for folder in watched:
                self._watch_folder_unsafe(folder)

    def watch_folder(self, folder_path: str) -> None:
        """
        Registers a directory path for active live monitoring.
        """
        with self.lock:
            if self.observer is None:
                self.observer = Observer()
                self.observer.start()
            self._watch_folder_unsafe(folder_path)

    def _watch_folder_unsafe(self, folder_path: str) -> None:
        abs_path = os.path.abspath(folder_path)
        if abs_path in self.watch_handles:
            return
            
        if not os.path.exists(abs_path):
            print(f"[-] Cannot watch missing directory: {abs_path}")
            return
            
        print(f"[*] Starting watchdog observer for: {abs_path}")
        handler = FileSystemWatchdogHandler(self)
        try:
            watch = self.observer.schedule(handler, abs_path, recursive=True)
            self.watch_handles[abs_path] = watch
        except Exception as e:
            print(f"[-] Failed to schedule watchdog for {abs_path}: {e}")

    def unwatch_folder(self, folder_path: str) -> None:
        """
        Removes monitoring from a directory.
        """
        with self.lock:
            abs_path = os.path.abspath(folder_path)
            if abs_path in self.watch_handles and self.observer:
                print(f"[*] Stopping watchdog observer for: {abs_path}")
                self.observer.unschedule(self.watch_handles[abs_path])
                del self.watch_handles[abs_path]

    def stop(self) -> None:
        """
        Cancels all pending debounce timers and halts background threads.
        """
        with self.lock:
            # 1. Cancel all active debounce timers
            for timer in self.timers.values():
                timer.cancel()
            self.timers.clear()
            
            # 2. Stop the observer thread
            if self.observer:
                self.observer.stop()
                self.observer.join()
                self.observer = None
            self.watch_handles.clear()
            print("[+] Active directory watchdogs stopped successfully.")

    def is_running(self) -> bool:
        with self.lock:
            return self.observer is not None and self.observer.is_alive()

    def get_watched_folders(self) -> List[str]:
        with self.lock:
            return list(self.watch_handles.keys())

    def queue_indexing(self, file_path: str) -> None:
        """
        Queues a file path for indexing with debouncing.
        """
        with self.lock:
            # Cancel any existing active timer to restart the debounce window
            if file_path in self.timers:
                self.timers[file_path].cancel()
                
            # Create and trigger a new timer
            timer = threading.Timer(self.debounce_seconds, self._debounced_index, args=[file_path])
            self.timers[file_path] = timer
            timer.start()

    def process_deletion(self, file_path: str) -> None:
        """
        Instantly purges a deleted file path's vectors from LanceDB.
        """
        with self.lock:
            if file_path in self.timers:
                self.timers[file_path].cancel()
                del self.timers[file_path]
                
        model_name = self.config.get("active_model", "BGE-Small-EN-v1.5")
        try:
            print(f"[*] Watchdog detected file deletion: {file_path}")
            self.db.delete_file_chunks(file_path, model_name)
            self.db.rebuild_fts_index(model_name)
        except Exception as e:
            print(f"[-] Failed to process deleted file {file_path}: {e}")

    def _debounced_index(self, file_path: str) -> None:
        """
        Worker callback executing the indexing operation after debounce expiry.
        """
        with self.lock:
            # Clear our reference
            if file_path in self.timers:
                del self.timers[file_path]

        if not os.path.exists(file_path):
            return

        model_name = self.config.get("active_model", "BGE-Small-EN-v1.5")
        print(f"[*] Watchdog executing debounced indexing for: {file_path}")
        
        try:
            mtime = os.path.getmtime(file_path)
            chunks = self.parser.parse(file_path)
            if not chunks:
                # File might be empty, perform purge
                self.db.delete_file_chunks(file_path, model_name)
                self.db.rebuild_fts_index(model_name)
                return
                
            vectors = self.embedder.embed(chunks, model_name)
            db_chunks = []
            for chunk_text, vector in zip(chunks, vectors):
                db_chunks.append({
                    "vector": vector.tolist(),
                    "file_path": file_path,
                    "file_name": os.path.basename(file_path),
                    "chunk_text": chunk_text,
                    "last_modified": mtime
                })
                
            self.db.delete_file_chunks(file_path, model_name)
            self.db.add_chunks(db_chunks, model_name)
            self.db.rebuild_fts_index(model_name)
            print(f"[+] Watchdog successfully updated index for: {file_path}")
            
        except Exception as e:
            print(f"[-] Watchdog debounced index error on {file_path}: {e}")
