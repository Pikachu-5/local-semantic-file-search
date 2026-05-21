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
from db import VectorDatabase
from model import EmbeddingEngine
from hybrid import HybridSearchEngine
from scanner import IndexScanner, DirectoryWatcher

class SearchEngineServicer(service_pb2_grpc.SearchEngineServicer):
    """
    gRPC Service Handler providing semantic search and active watchdog control
    to the WinUI C# frontend.
    """
    def __init__(self, config_manager: ConfigManager, db: VectorDatabase, 
                 embedder: EmbeddingEngine, search_engine: HybridSearchEngine, 
                 scanner: IndexScanner, watcher: DirectoryWatcher):
        self.config = config_manager
        self.db = db
        self.embedder = embedder
        self.search_engine = search_engine
        self.scanner = scanner
        self.watcher = watcher

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
            
            return service_pb2.StatusResponse(
                total_indexed_files=total_files,
                total_vectors=total_chunks,
                is_watchdog_running=is_watching,
                active_model=active_model,
                watched_folders=watched_list
            )
        except Exception as e:
            print(f"[-] GetSystemStatus failed: {e}")
            context.set_code(grpc.StatusCode.INTERNAL)
            context.set_details(str(e))
            return service_pb2.StatusResponse()

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


def serve(port: str = "50051") -> None:
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
    
    # 2. Fire up background watcher threads
    watcher.start()
    
    # 3. Create and bind the gRPC server listener
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
    servicer = SearchEngineServicer(config_manager, db, embedder, search_engine, scanner, watcher)
    service_pb2_grpc.add_SearchEngineServicer_to_server(servicer, server)
    
    # Bind to localhost exclusively to prevent firewall popups
    address = f"127.0.0.1:{port}"
    server.add_insecure_port(address)
    
    print(f"[+] SwiftSearch daemon listening securely on {address}...")
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
