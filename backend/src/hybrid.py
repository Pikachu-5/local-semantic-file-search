import os
from typing import List, Dict, Any
from lancedb.rerankers import RRFReranker
from db import VectorDatabase
from model import EmbeddingEngine

class HybridSearchEngine:
    """
    Coordinates semantic vector search and BM25 lexical search using 
    LanceDB's native Rust-backed RRF (Reciprocal Rank Fusion) reranker.
    """
    def __init__(self, db: VectorDatabase, embedder: EmbeddingEngine):
        self.db = db
        self.embedder = embedder
        # Initialize the built-in RRF reranker (defaults to k=60)
        self.reranker = RRFReranker()

    def search(self, query: str, top_k: int = 10, model_name: str = "BGE-Small-EN-v1.5") -> List[Dict[str, Any]]:
        """
        Executes a native hybrid search using dense vectors and full-text keyword index.
        Merges results using Rust-backed RRF.
        Returns a formatted list of results matching the gRPC search spec.
        """
        if not query.strip():
            return []

        # 1. Generate the dense query vector using the active model
        query_vector = self.embedder.embed(query, model_name)

        # 2. Retrieve the active LanceDB table
        table = self.db.get_table(model_name)

        try:
            # 3. Perform native concurrent hybrid search
            # We search for the query text, supply the dense vector, and rerank with RRF
            results = (
                table.search(None, query_type="hybrid")
                .vector(query_vector)
                .text(query)
                .rerank(reranker=self.reranker)
                .limit(top_k)
                .to_arrow()
                .to_pylist()
            )
            
            if not results:
                return []

            # 4. Format the output and apply safety checks (e.g. check if file still exists)
            raw_valid_results = []
            for row in results:
                file_path = row.get("file_path", "")
                # Check if the file still exists locally (safety check)
                if not os.path.exists(file_path):
                    continue
                raw_valid_results.append(row)

            if not raw_valid_results:
                return []

            formatted_results = []
            for row in raw_valid_results:
                file_path = row.get("file_path", "")
                score = float(row.get("_score", 0.0))
                
                # Absolute mapping of RRF score: 0.0 maps to 50%, rank 1 (~0.033) maps to 99%
                scaled_score = 0.50 + 15.0 * score
                scaled_score = min(0.99, max(0.50, scaled_score))
                
                formatted_results.append({
                    "file_path": file_path,
                    "file_name": row.get("file_name", os.path.basename(file_path)),
                    "chunk_text": row.get("chunk_text", ""),
                    "relevance_score": scaled_score
                })
                
            return formatted_results

        except Exception as e:
            print(f"[-] Hybrid search error: {e}")
            # Fallback search option: if hybrid search is not fully initialized (e.g. empty FTS index)
            # we perform a pure vector search as a bulletproof fallback.
            return self._fallback_vector_search(query_vector, table, top_k)

    def _fallback_vector_search(self, query_vector: Any, table: Any, top_k: int) -> List[Dict[str, Any]]:
        """
        Fallback search executing pure dense vector scan. 
        Ensures search remains functional even if FTS index isn't ready.
        """
        print("[*] Running fallback vector search...")
        try:
            results = table.search(query_vector).limit(top_k).to_arrow().to_pylist()
            if not results:
                return []

            raw_valid_results = []
            for row in results:
                file_path = row.get("file_path", "")
                if not os.path.exists(file_path):
                    continue
                raw_valid_results.append(row)
                
            if not raw_valid_results:
                return []

            formatted_results = []
            for row in raw_valid_results:
                file_path = row.get("file_path", "")
                distance = float(row.get("_distance", 1.0))
                # Cosine similarity derived from L2 squared distance: sim = 1.0 - (distance / 2.0)
                sim = max(0.0, min(1.0, 1.0 - (distance / 2.0)))
                
                # Absolute mapping of vector similarity: maps 0.0 to 50%, 1.0 to 99%
                scaled_score = 0.50 + 0.49 * sim
                scaled_score = min(0.99, max(0.50, scaled_score))
                    
                formatted_results.append({
                    "file_path": file_path,
                    "file_name": row.get("file_name", os.path.basename(file_path)),
                    "chunk_text": row.get("chunk_text", ""),
                    "relevance_score": scaled_score
                })
            return formatted_results
        except Exception as e:
            print(f"[-] Fallback search error: {e}")
            return []
