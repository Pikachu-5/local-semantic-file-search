import os
import sys
import time
import unittest
import threading
import shutil
import tempfile
import grpc
from concurrent import futures

# Add src folder to python import path
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src"))

from proto import service_pb2
from proto import service_pb2_grpc
from config import ConfigManager, get_config_path
from db import VectorDatabase, get_db_dir
from model import EmbeddingEngine
from hybrid import HybridSearchEngine
from scanner import IndexScanner, DirectoryWatcher, FileSystemWatchdogHandler
from grpc_server import SearchEngineServicer
from everything import EverythingSearchEngine


class TestServicesAndWatchdog(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Create temp folder for test configuration and database
        cls.temp_dir = tempfile.mkdtemp()
        
        # Initialize modules with local isolated paths
        config_path = os.path.join(cls.temp_dir, "config.json")
        db_dir = os.path.join(cls.temp_dir, "db")
        cls.config = ConfigManager(config_path=config_path)
        cls.db = VectorDatabase(db_dir=db_dir)
        cls.embedder = EmbeddingEngine()
        cls.search_engine = HybridSearchEngine(cls.db, cls.embedder)
        cls.scanner = IndexScanner(cls.db, cls.embedder, cls.config)
        cls.watcher = DirectoryWatcher(cls.db, cls.embedder, cls.config)
        cls.everything_engine = EverythingSearchEngine()
        
        # Speed up debouncing for testing
        cls.watcher.debounce_seconds = 0.5
        cls.watcher.start()
        
        # Setup background gRPC server on dynamic port
        cls.server_port = "50052"
        cls.server = grpc.server(futures.ThreadPoolExecutor(max_workers=2))
        cls.servicer = SearchEngineServicer(
            cls.config, cls.db, cls.embedder, 
            cls.search_engine, cls.scanner, cls.watcher, cls.everything_engine
        )
        service_pb2_grpc.add_SearchEngineServicer_to_server(cls.servicer, cls.server)
        cls.server.add_insecure_port(f"127.0.0.1:{cls.server_port}")
        cls.server.start()
        
        # Initialize gRPC Client Channel
        cls.channel = grpc.insecure_channel(f"127.0.0.1:{cls.server_port}")
        cls.stub = service_pb2_grpc.SearchEngineStub(cls.channel)

    @classmethod
    def tearDownClass(cls):
        # Close channel
        cls.channel.close()
        # Stop gRPC Server
        cls.server.stop(0)
        # Stop Watchdog
        cls.watcher.stop()
        
        # Delete temp folder
        shutil.rmtree(cls.temp_dir, ignore_errors=True)

    def test_grpc_system_status(self):
        # Call status endpoint
        response = self.stub.GetSystemStatus(service_pb2.Empty())
        
        self.assertEqual(response.active_model, "BGE-Small-EN-v1.5")
        self.assertTrue(response.is_watchdog_running)
        self.assertGreaterEqual(len(response.watched_folders), 0)

    def test_grpc_update_settings(self):
        # Call settings update
        request = service_pb2.SettingsRequest(
            active_model="Nomic-Embed-Text-v1.5",
            excluded_dirs=[".git", "node_modules", "custom_exclude"],
            included_extensions=[".md", ".py"]
        )
        response = self.stub.UpdateSettings(request)
        
        self.assertTrue(response.success)
        
        # Verify settings updated in configuration manager
        self.assertEqual(self.config.get("active_model"), "Nomic-Embed-Text-v1.5")
        self.assertIn("custom_exclude", self.config.get("excluded_dirs"))
        self.assertIn(".md", self.config.get("included_extensions"))

        # Reset active model back to BGE-Small for remaining tests
        self.stub.UpdateSettings(service_pb2.SettingsRequest(active_model="BGE-Small-EN-v1.5"))

    def test_directory_crawler_and_exclusions(self):
        # Create a mock indexing root
        test_root = os.path.join(self.temp_dir, "crawl_root")
        sub_folder = os.path.join(test_root, "docs")
        ignored_folder = os.path.join(test_root, "node_modules")
        
        os.makedirs(sub_folder, exist_ok=True)
        os.makedirs(ignored_folder, exist_ok=True)
        
        # Write valid file
        valid_file = os.path.join(sub_folder, "notes.md")
        with open(valid_file, "w", encoding="utf-8") as f:
            f.write("# Valid Notes\nThis file is in a crawler-valid folder.")
            
        # Write excluded file
        ignored_file = os.path.join(ignored_folder, "package.md")
        with open(ignored_file, "w", encoding="utf-8") as f:
            f.write("# Ignored Notes\nThis file should not be crawled.")
            
        # Run Index target via gRPC
        request = service_pb2.IndexRequest(folder_path=test_root, auto_watch=True)
        response = self.stub.IndexTargetFolder(request)
        
        self.assertTrue(response.success)
        self.assertEqual(response.files_processed, 1) # Only valid_file should be crawled
        self.assertTrue(response.chunks_created > 0)
        
        # Verify only valid_file is in database
        indexed_files = self.db.get_indexed_files("BGE-Small-EN-v1.5")
        self.assertIn(os.path.abspath(valid_file), indexed_files)
        self.assertNotIn(os.path.abspath(ignored_file), indexed_files)

    def test_watchdog_debounce_indexing(self):
        # Create a temp file inside a watched folder
        test_root = os.path.join(self.temp_dir, "crawl_root")
        watched_file = os.path.join(test_root, "docs", "live_notes.md")
        
        # Spy on the database add_chunks to count how many times it was called
        original_add_chunks = self.db.add_chunks
        add_call_count = [0]
        
        def mock_add_chunks(chunks, model_name):
            add_call_count[0] += 1
            return original_add_chunks(chunks, model_name)
            
        self.db.add_chunks = mock_add_chunks
        
        try:
            # Touch file to simulate creation
            with open(watched_file, "w", encoding="utf-8") as f:
                f.write("Initial state.")
            
            # Simulate watchdog queueing indexing events 5 times rapidly
            for i in range(5):
                with open(watched_file, "a", encoding="utf-8") as f:
                    f.write(f"\nEdit number {i}.")
                self.watcher.queue_indexing(watched_file)
                time.sleep(0.02) # Very rapid consecutive edits
                
            # Immediately after rapid edits, it should not have indexed yet (due to 0.5s debounce)
            self.assertEqual(add_call_count[0], 0)
            
            # Sleep 0.8 seconds to allow the single debounce timer to fire
            time.sleep(0.8)
            
            # The debounce should have completed, resulting in exactly 1 indexing call
            self.assertEqual(add_call_count[0], 1)
            
            # Check the database got the final state
            results = self.search_engine.search("Edit number 4", top_k=1)
            self.assertTrue(len(results) > 0)
            self.assertIn("Edit number 4", results[0]["chunk_text"])
            
        finally:
            # Restore db class method
            self.db.add_chunks = original_add_chunks
            if os.path.exists(watched_file):
                os.remove(watched_file)


if __name__ == "__main__":
    unittest.main()
