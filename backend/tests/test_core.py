import os
import sys
import unittest
import numpy as np
import shutil

# Add src folder to python import path
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src"))

from parser import RecursiveCharacterSplitter, FileParser
from model import EmbeddingEngine, get_models_dir
from db import VectorDatabase, get_db_dir
from hybrid import HybridSearchEngine


class TestRecursiveCharacterSplitter(unittest.TestCase):
    def test_basic_splitting(self):
        splitter = RecursiveCharacterSplitter(chunk_size=50, chunk_overlap=10)
        text = "This is a long piece of text that should be split recursively. It should split on sentences or spaces."
        chunks = splitter.split_text(text)
        
        self.assertTrue(len(chunks) > 1)
        for chunk in chunks:
            self.assertTrue(len(chunk) <= 50)
            
    def test_overlap(self):
        splitter = RecursiveCharacterSplitter(chunk_size=40, chunk_overlap=15)
        text = "First paragraph of text.\n\nSecond paragraph of text."
        chunks = splitter.split_text(text)
        
        self.assertTrue(len(chunks) >= 2)


class TestEmbeddingEngine(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.engine = EmbeddingEngine()
        
    def test_bge_dimension(self):
        # Generates a quick embedding using the default model BGE-Small
        print("[*] Running TestEmbeddingEngine: BGE-Small (downloads BGE if not in cache)")
        text = "SwiftSearch semantic indexing"
        vector = self.engine.embed(text, model_name="BGE-Small-EN-v1.5")
        
        self.assertEqual(len(vector.shape), 1)
        self.assertEqual(vector.shape[0], 384)
        # Ensure it is normalized (dot product with itself should be close to 1)
        norm = np.dot(vector, vector)
        self.assertAlmostEqual(norm, 1.0, places=5)


class TestVectorDatabaseAndSearch(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Override the database folder to a temporary test folder
        cls.test_db_dir = os.path.join(get_db_dir(), "test_db_run")
        os.makedirs(cls.test_db_dir, exist_ok=True)
        
        cls.db = VectorDatabase(cls.test_db_dir)
        cls.embedder = EmbeddingEngine()
        cls.searcher = HybridSearchEngine(cls.db, cls.embedder)
        cls.model_name = "BGE-Small-EN-v1.5"

    @classmethod
    def tearDownClass(cls):
        # Clean up the test database files
        shutil.rmtree(cls.test_db_dir, ignore_errors=True)

    def test_db_operations_and_hybrid_search(self):
        # Create a mock file path
        mock_file_path = os.path.abspath("test_mock_file.md")
        with open(mock_file_path, "w", encoding="utf-8") as f:
            f.write("# SwiftSearch Notes\nThis is a high-performance local-first vector search application built with WinUI 3 and Python.")
        
        try:
            # 1. Parse and chunk the mock file
            parser = FileParser(chunk_size=1000, chunk_overlap=100)
            chunks = parser.parse(mock_file_path)
            self.assertEqual(len(chunks), 1)
            
            # 2. Vectorize the chunks
            vectors = self.embedder.embed(chunks, self.model_name)
            
            # 3. Format chunks for LanceDB insertion
            db_chunks = []
            for i, (chunk_text, vector) in enumerate(zip(chunks, vectors)):
                db_chunks.append({
                    "vector": vector.tolist(),
                    "file_path": mock_file_path,
                    "file_name": os.path.basename(mock_file_path),
                    "chunk_text": chunk_text,
                    "last_modified": os.path.getmtime(mock_file_path)
                })
                
            # 4. Clear old data and insert (Delete-then-Insert pattern)
            self.db.delete_file_chunks(mock_file_path, self.model_name)
            self.db.add_chunks(db_chunks, self.model_name)
            
            # Rebuild FTS index for keyword search
            self.db.rebuild_fts_index(self.model_name)
            
            # 5. Verify the files are in indexed maps
            indexed_map = self.db.get_indexed_files(self.model_name)
            self.assertIn(mock_file_path, indexed_map)
            
            # 6. Execute Hybrid Search
            query = "high-performance search"
            results = self.searcher.search(query, top_k=5, model_name=self.model_name)
            
            self.assertTrue(len(results) > 0)
            result = results[0]
            self.assertEqual(result["file_path"], mock_file_path)
            self.assertIn("SwiftSearch", result["chunk_text"])
            self.assertTrue(result["relevance_score"] >= 0.0)
            
            # 7. Get stats
            total_files, total_chunks = self.db.get_stats(self.model_name)
            self.assertEqual(total_files, 1)
            self.assertEqual(total_chunks, 1)

        finally:
            # Clean up the mock file
            if os.path.exists(mock_file_path):
                os.remove(mock_file_path)


if __name__ == "__main__":
    unittest.main()
