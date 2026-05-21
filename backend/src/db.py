import os
import pydantic
from typing import List, Dict, Any, Optional, Tuple
import lancedb
from lancedb.pydantic import LanceModel, Vector

def get_db_dir() -> str:
    """
    Resolves the database directory located in %LOCALAPPDATA%/SwiftSearch/db.
    Creates the directory if it does not already exist.
    """
    local_app_data = os.getenv("LOCALAPPDATA")
    if not local_app_data:
        local_app_data = os.path.expanduser("~")
        
    db_dir = os.path.join(local_app_data, "SwiftSearch", "db")
    os.makedirs(db_dir, exist_ok=True)
    return db_dir


class VectorDatabase:
    """
    Manages LanceDB instance, table schemas, and crud operations.
    Handles the BGE (384) and Nomic (768) vector tables.
    """
    def __init__(self, db_dir: str = None):
        self.db_dir = db_dir or get_db_dir()
        self.db = lancedb.connect(self.db_dir)
        
    def _get_table_name(self, model_name: str) -> str:
        """
        Returns the table name based on the active model.
        """
        if "bge" in model_name.lower():
            return "chunks_bge_384"
        elif "nomic" in model_name.lower():
            return "chunks_nomic_768"
        else:
            # Fallback
            return "chunks_bge_384"

    def _get_schema(self, model_name: str) -> Any:
        """
        Returns the Pydantic LanceModel schema for the selected model.
        """
        if "bge" in model_name.lower():
            class ChunkModelBGE(LanceModel):
                vector: Vector(384)
                file_path: str
                file_name: str
                chunk_text: str
                last_modified: float
            return ChunkModelBGE
        else:
            class ChunkModelNomic(LanceModel):
                vector: Vector(768)
                file_path: str
                file_name: str
                chunk_text: str
                last_modified: float
            return ChunkModelNomic

    def get_table(self, model_name: str) -> lancedb.db.LanceTable:
        """
        Retrieves the LanceDB table for the model. Creates it if it doesn't exist.
        """
        table_name = self._get_table_name(model_name)
        schema = self._get_schema(model_name)
        
        try:
            return self.db.open_table(table_name)
        except Exception:
            # Table does not exist, create it with empty schema
            print(f"[*] Creating LanceDB table '{table_name}'...")
            table = self.db.create_table(table_name, schema=schema)
            # Initialize empty FTS index
            try:
                table.create_fts_index("chunk_text")
            except Exception as e:
                print(f"[-] FTS Initialization warning: {e}")
            return table

    def delete_file_chunks(self, file_path: str, model_name: str) -> None:
        """
        Implements incremental indexing cleanup by deleting all chunks for a file path.
        Uses SQL escaping for single quotes.
        """
        table = self.get_table(model_name)
        # Escape single quotes in Windows paths to prevent SQL syntax errors
        escaped_path = file_path.replace("'", "''")
        print(f"[*] Purging existing index entry for file: {file_path}")
        table.delete(f"file_path = '{escaped_path}'")

    def add_chunks(self, chunks: List[Dict[str, Any]], model_name: str) -> int:
        """
        Appends new chunks to the table.
        chunks is a list of dicts containing keys: vector, file_path, file_name, chunk_text, last_modified.
        """
        if not chunks:
            return 0
            
        table = self.get_table(model_name)
        table.add(chunks)
        print(f"[+] Added {len(chunks)} chunks to {self._get_table_name(model_name)}")
        return len(chunks)

    def rebuild_fts_index(self, model_name: str) -> None:
        """
        Rebuilds/Creates the Tantivy full-text index on chunk_text.
        Call this after bulk insertions to update lexical search capabilities.
        """
        table = self.get_table(model_name)
        print(f"[*] Rebuilding FTS index on chunk_text for {self._get_table_name(model_name)}...")
        try:
            table.create_fts_index("chunk_text", replace=True)
            print("[+] FTS index rebuilt successfully!")
        except Exception as e:
            print(f"[-] FTS indexing error: {e}")

    def get_indexed_files(self, model_name: str) -> Dict[str, float]:
        """
        Retrieves a map of all indexed files and their last_modified times.
        Helps the watchdog or scanner skip unmodified files.
        """
        table_name = self._get_table_name(model_name)
        if table_name not in self.db.list_tables().tables:
            return {}
            
        table = self.get_table(model_name)
        try:
            # We can query all records as standard Arrow format and convert to dictionaries
            records = table.to_arrow().to_pylist()
            if not records:
                return {}
            # Group by file_path and take the maximum modification time in pure Python
            result = {}
            for r in records:
                path = r.get("file_path")
                mtime = r.get("last_modified", 0.0)
                if path:
                    result[path] = max(result.get(path, 0.0), mtime)
            return result
        except Exception as e:
            print(f"[-] Error querying indexed files: {e}")
            return {}

    def get_stats(self, model_name: str) -> Tuple[int, int]:
        """
        Returns (total_unique_files, total_vectors/chunks) for the selected table.
        """
        table_name = self._get_table_name(model_name)
        if table_name not in self.db.list_tables().tables:
            return 0, 0
            
        table = self.get_table(model_name)
        try:
            records = table.to_arrow().to_pylist()
            if not records:
                return 0, 0
            unique_files = {r.get("file_path") for r in records if r.get("file_path")}
            return len(unique_files), len(records)
        except Exception as e:
            print(f"[-] Error getting stats: {e}")
            return 0, 0
