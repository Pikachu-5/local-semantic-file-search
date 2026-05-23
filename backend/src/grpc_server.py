import os
import sys
import time
import signal
import grpc
from concurrent import futures

# Add local path to sys.path to ensure absolute imports resolve correctly
src_dir = os.path.dirname(os.path.abspath(__file__))
if src_dir not in sys.path:
    sys.path.insert(0, src_dir)

from proto import service_pb2
from proto import service_pb2_grpc
from config import ConfigManager
from db import VectorDatabase, get_db_dir
from model import EmbeddingEngine, ProgressTracker, get_models_dir
from hybrid import HybridSearchEngine
from scanner import IndexScanner, DirectoryWatcher
from everything import EverythingSearchEngine
import queue
import threading
from huggingface_hub import snapshot_download

def is_model_downloaded(model_name: str) -> bool:
    repo_id = EmbeddingEngine.MODEL_MAP.get(model_name)
    if not repo_id:
        return False
    folder_name = "models--" + repo_id.replace("/", "--")
    models_dir = get_models_dir()
    model_path = os.path.join(models_dir, folder_name)
    if not os.path.isdir(model_path):
        return False
    snapshots_dir = os.path.join(model_path, "snapshots")
    if not os.path.isdir(snapshots_dir):
        return False
    try:
        snapshots = os.listdir(snapshots_dir)
        if not snapshots:
            return False
        for s in snapshots:
            snapshot_path = os.path.join(snapshots_dir, s)
            if os.path.isdir(snapshot_path) and os.listdir(snapshot_path):
                return True
    except Exception:
        pass
    return False

class SearchEngineServicer(service_pb2_grpc.SearchEngineServicer):
    """
    gRPC Service Handler providing semantic search and active watchdog control
    to the WinUI C# frontend.
    """
    def __init__(self, config_manager: ConfigManager, db: VectorDatabase, 
                 embedder: EmbeddingEngine, search_engine: HybridSearchEngine, 
                 scanner: IndexScanner, watcher: DirectoryWatcher, everything_engine: EverythingSearchEngine):
        self.config = config_manager
        self.db = db
        self.embedder = embedder
        self.search_engine = search_engine
        self.scanner = scanner
        self.watcher = watcher
        self.everything_engine = everything_engine

    def EverythingSearch(self, request, context):
        start_time = time.perf_counter()
        query = request.query
        top_k = request.top_k or 20
        
        try:
            print(f"[*] Received EverythingSearch request: query='{query}', top_k={top_k}, filters={request.filters}")
            # If 'global' is passed in filters, bypass watched folder scoping to query MFT globally
            is_global = "global" in request.filters
            watched_folders = [] if is_global else self.config.get("watched_folders", [])
            results = self.everything_engine.search(query, limit=top_k, folder_paths=watched_folders)
            
            proto_results = []
            for r in results:
                proto_results.append(service_pb2.SearchResult(
                    file_path=r["file_path"],
                    file_name=r["file_name"],
                    chunk_text=r["chunk_text"],
                    relevance_score=r["relevance_score"]
                ))
                
            execution_time = (time.perf_counter() - start_time) * 1000
            print(f"[+] Everything found {len(proto_results)} matches in {execution_time:.2f} ms")
            
            return service_pb2.SearchResponse(
                results=proto_results,
                execution_time_ms=execution_time
            )
        except Exception as e:
            print(f"[-] EverythingSearch failed: {e}")
            context.set_code(grpc.StatusCode.INTERNAL)
            context.set_details(f"Everything search failed: {str(e)}")
            return service_pb2.SearchResponse()

    def SemanticSearch(self, request, context):
        start_time = time.perf_counter()
        query = request.query
        top_k = request.top_k or 10
        active_model = self.config.get("active_model", "BGE-Small-EN-v1.5")
        
        try:
            print(f"[*] Recieved SemanticSearch request: query='{query}', top_k={top_k}")
            results = self.search_engine.search(query, top_k=top_k, model_name=active_model)
            
            proto_results = []
            for r in results:
                proto_results.append(service_pb2.SearchResult(
                    file_path=r["file_path"],
                    file_name=r["file_name"],
                    chunk_text=r["chunk_text"],
                    relevance_score=r["relevance_score"]
                ))
                
            execution_time = (time.perf_counter() - start_time) * 1000
            print(f"[+] Found {len(proto_results)} search matches in {execution_time:.2f} ms")
            
            return service_pb2.SearchResponse(
                results=proto_results,
                execution_time_ms=execution_time
            )
        except Exception as e:
            print(f"[-] SemanticSearch failed: {e}")
            context.set_code(grpc.StatusCode.INTERNAL)
            context.set_details(f"Search failed: {str(e)}")
            return service_pb2.SearchResponse()

    def IndexTargetFolder(self, request, context):
        folder_path = request.folder_path
        auto_watch = request.auto_watch
        active_model = self.config.get("active_model", "BGE-Small-EN-v1.5")
        
        try:
            print(f"[*] Received IndexTargetFolder request: path='{folder_path}', auto_watch={auto_watch}")
            
            if folder_path.startswith("LOAD_MODEL:"):
                target_model = folder_path[len("LOAD_MODEL:"):].strip()
                print(f"[*] Pre-loading model into memory: {target_model}")
                success = self.embedder.load_model(target_model)
                return service_pb2.IndexResponse(
                    success=success,
                    files_processed=0,
                    chunks_created=0,
                    error_message="" if success else f"Failed to pre-load model {target_model}"
                )

            if folder_path.startswith("UNLOAD_MODEL:"):
                target_model = folder_path[len("UNLOAD_MODEL:"):].strip()
                print(f"[*] Unloading model from memory: {target_model}")
                success = self.embedder.unload_model(target_model)
                return service_pb2.IndexResponse(
                    success=success,
                    files_processed=0,
                    chunks_created=0,
                    error_message="" if success else f"Failed to unload model {target_model}"
                )

            if folder_path == "DELETE_VECTORS:":
                print("[*] Delete all vectors request received. Resetting search database...")
                try:
                    watched_folders = list(self.config.get("watched_folders", []))
                    for wf in watched_folders:
                        try:
                            self.watcher.unwatch_folder(wf)
                        except Exception as e:
                            print(f"[-] Failed to unwatch folder {wf} during reset: {e}")
                except Exception as e:
                    print(f"[-] Failed to unwatch folders: {e}")
                
                try:
                    self.config.config["watched_folders"] = []
                    self.config._save(self.config.config)
                except Exception as e:
                    print(f"[-] Failed to clear watched folders in config: {e}")

                self.db.reset_database()
                
                return service_pb2.IndexResponse(
                    success=True,
                    files_processed=0,
                    chunks_created=0
                )

            if folder_path.startswith("REMOVE:"):
                target_folder = folder_path[len("REMOVE:"):].strip()
                abs_folder = os.path.abspath(target_folder)
                print(f"[*] Unwatching folder: {abs_folder}")
                
                # 1. Remove from persistent config
                self.config.remove_watched_folder(abs_folder)
                
                # 2. Unschedule from directory watcher
                self.watcher.unwatch_folder(abs_folder)
                
                return service_pb2.IndexResponse(
                    success=True,
                    files_processed=0,
                    chunks_created=0
                )

            abs_folder = os.path.abspath(folder_path)
            if not os.path.exists(abs_folder):
                return service_pb2.IndexResponse(
                    success=False,
                    files_processed=0,
                    chunks_created=0,
                    error_message=f"Folder does not exist: {folder_path}"
                )
                
            # 1. Update persistent config
            self.config.add_watched_folder(abs_folder)
            
            # 2. Perform initial synchronous crawl and index
            files_processed, chunks_created = self.scanner.scan_and_index(abs_folder, active_model)
            
            # 3. Schedule for active live background monitoring
            if auto_watch:
                self.watcher.watch_folder(abs_folder)
                
            return service_pb2.IndexResponse(
                success=True,
                files_processed=files_processed,
                chunks_created=chunks_created
            )
        except Exception as e:
            print(f"[-] IndexTargetFolder failed: {e}")
            return service_pb2.IndexResponse(
                success=False,
                files_processed=0,
                chunks_created=0,
                error_message=str(e)
            )

    def GetSystemStatus(self, request, context):
        active_model = self.config.get("active_model", "BGE-Small-EN-v1.5")
        try:
            total_files, total_chunks = self.db.get_stats(active_model)
            is_watching = self.watcher.is_running()
            watched_list = self.watcher.get_watched_folders()
            downloaded_models = []
            for m in EmbeddingEngine.MODEL_MAP.keys():
                if is_model_downloaded(m):
                    if self.embedder.is_model_loaded(m):
                        downloaded_models.append(f"{m}:loaded")
                    else:
                        downloaded_models.append(m)
            db_dir = get_db_dir()
            
            return service_pb2.StatusResponse(
                total_indexed_files=total_files,
                total_vectors=total_chunks,
                is_watchdog_running=is_watching,
                active_model=active_model,
                watched_folders=watched_list,
                downloaded_models=downloaded_models,
                db_dir=db_dir
            )
        except Exception as e:
            print(f"[-] GetSystemStatus failed: {e}")
            context.set_code(grpc.StatusCode.INTERNAL)
            context.set_details(str(e))
            return service_pb2.StatusResponse()

    def DownloadModel(self, request, context):
        model_name = request.model_name
        repo_id = EmbeddingEngine.MODEL_MAP.get(model_name)
        if not repo_id:
            context.set_code(grpc.StatusCode.INVALID_ARGUMENT)
            context.set_details(f"Unknown model name: {model_name}")
            return
            
        print(f"[*] Starting download request for model: {model_name} ({repo_id})")
        
        # Queue to pass progress updates from download thread to gRPC thread
        progress_queue = queue.Queue()
        
        def progress_callback(n, total):
            pct = 0.0
            if total > 0:
                pct = min(100.0, (float(n) / float(total)) * 100.0)
            progress_queue.put((pct, "Downloading model files...", None))
            
        def download_worker():
            try:
                models_dir = get_models_dir()
                # Execute inside the ProgressTracker context to capture tqdm events
                with ProgressTracker(progress_callback):
                    snapshot_download(
                        repo_id=repo_id,
                        cache_dir=models_dir,
                    )
                # Success
                progress_queue.put((100.0, "Download completed successfully!", None))
            except Exception as e:
                print(f"[-] Error in download worker for {model_name}: {e}")
                progress_queue.put((None, None, str(e)))
                
        # Start download in a daemon thread
        t = threading.Thread(target=download_worker, daemon=True)
        t.start()
        
        # Consume progress queue and yield responses to gRPC client
        last_pct = -1.0
        while t.is_alive() or not progress_queue.empty():
            try:
                # Poll queue with timeout to keep thread responsive
                item = progress_queue.get(timeout=0.1)
                pct, status, error_msg = item
                
                if error_msg is not None:
                    yield service_pb2.DownloadModelResponse(
                        model_name=model_name,
                        progress=0,
                        status="Failed",
                        error_message=error_msg
                    )
                    return
                    
                if pct is not None:
                    # Only yield if progress changed significantly, or at 100%
                    if abs(pct - last_pct) >= 0.5 or pct >= 100.0:
                        last_pct = pct
                        yield service_pb2.DownloadModelResponse(
                            model_name=model_name,
                            progress=pct,
                            status=status,
                            error_message=""
                        )
            except queue.Empty:
                continue
                
        print(f"[+] Finished downloading model: {model_name}")

    def UpdateSettings(self, request, context):
        try:
            active_model = request.active_model or None
            excluded_dirs = list(request.excluded_dirs) if request.excluded_dirs else None
            included_extensions = list(request.included_extensions) if request.included_extensions else None
            
            print(f"[*] Received UpdateSettings: model='{active_model}'")
            
            self.config.update_settings(
                active_model=active_model,
                excluded_dirs=excluded_dirs,
                included_extensions=included_extensions
            )
            return service_pb2.SettingsResponse(success=True)
        except Exception as e:
            print(f"[-] UpdateSettings failed: {e}")
            return service_pb2.SettingsResponse(
                success=False,
                error_message=str(e)
            )


def serve(port: str = "0") -> None:
    """
    Spins up the gRPC listener bound exclusively to loopback 127.0.0.1.
    """
    # 1. Initialize core system layers
    config_manager = ConfigManager()
    db = VectorDatabase()
    embedder = EmbeddingEngine()
    search_engine = HybridSearchEngine(db, embedder)
    scanner = IndexScanner(db, embedder, config_manager)
    watcher = DirectoryWatcher(db, embedder, config_manager)
    everything_engine = EverythingSearchEngine()
    
    # 2. Fire up background watcher threads
    watcher.start()
    
    # 3. Create and bind the gRPC server listener
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
    servicer = SearchEngineServicer(config_manager, db, embedder, search_engine, scanner, watcher, everything_engine)
    service_pb2_grpc.add_SearchEngineServicer_to_server(servicer, server)
    
    # Bind to localhost exclusively to prevent firewall popups
    address = f"127.0.0.1:{port}"
    bound_port = server.add_insecure_port(address)
    
    print(f"[+] SwiftSearch daemon listening securely on 127.0.0.1:{bound_port}...")
    print(f"GRPC_READY:{bound_port}", flush=True)
    sys.stdout.flush()
    server.start()
    
    # Set up signal handlers for clean shut downs
    def shutdown_handler(signum, frame):
        print("\n[*] Termination signal received. Halting SwiftSearch daemon...")
        watcher.stop()
        server.stop(0)
        sys.exit(0)
        
    signal.signal(signal.SIGINT, shutdown_handler)
    signal.signal(signal.SIGTERM, shutdown_handler)
    
    try:
        while True:
            time.sleep(3600)
    except KeyboardInterrupt:
        shutdown_handler(None, None)

if __name__ == "__main__":
    serve()
