import os
import threading
from typing import List, Union
import numpy as np
from sentence_transformers import SentenceTransformer

# Thread-safe model cache
_models_cache = {}
_cache_lock = threading.Lock()

def get_models_dir() -> str:
    """
    Resolves the models directory located in %LOCALAPPDATA%/SwiftSearch/models.
    Creates the directory if it does not already exist.
    """
    local_app_data = os.getenv("LOCALAPPDATA")
    if not local_app_data:
        # Fallback to user home directory if LOCALAPPDATA is missing
        local_app_data = os.path.expanduser("~")
        
    models_dir = os.path.join(local_app_data, "SwiftSearch", "models")
    os.makedirs(models_dir, exist_ok=True)
    return models_dir

class EmbeddingEngine:
    """
    Manages loading of local SentenceTransformer models and generating dense embeddings.
    Downloads models on first boot using cache_folder parameter.
    """
    
    MODEL_MAP = {
        "BGE-Small-EN-v1.5": "BAAI/bge-small-en-v1.5",
        "Nomic-Embed-Text-v1.5": "nomic-ai/nomic-embed-text-v1.5"
    }
    
    def __init__(self):
        self.models_dir = get_models_dir()
        
    def _load_model(self, model_display_name: str) -> SentenceTransformer:
        """
        Loads the SentenceTransformer model from local storage (or downloads it if missing).
        Maintains a global thread-safe cache.
        """
        repo_id = self.MODEL_MAP.get(model_display_name)
        if not repo_id:
            raise ValueError(f"Unknown model name: {model_display_name}. Supported models: {list(self.MODEL_MAP.keys())}")
            
        with _cache_lock:
            if model_display_name not in _models_cache:
                print(f"[*] Loading model '{model_display_name}' (ID: {repo_id}) into memory...")
                print(f"[*] Using local cache directory: {self.models_dir}")
                
                # Nomic model requires trust_remote_code=True
                trust_remote = "nomic" in repo_id.lower()
                
                model = SentenceTransformer(
                    repo_id, 
                    cache_folder=self.models_dir,
                    trust_remote_code=trust_remote
                )
                _models_cache[model_display_name] = model
                print(f"[+] Model '{model_display_name}' loaded successfully!")
                
            return _models_cache[model_display_name]

    def embed(self, texts: Union[str, List[str]], model_name: str = "BGE-Small-EN-v1.5") -> np.ndarray:
        """
        Generates dense embeddings for the provided text or list of texts.
        Returns a numpy array of dimensions (N, D).
        """
        model = self._load_model(model_name)
        
        # BGE models work best with a query/passage prefix, but for general similarity
        # they perform exceptionally well with standard text inputs.
        # We perform native embedding extraction.
        embeddings = model.encode(
            texts, 
            show_progress_bar=False,
            convert_to_numpy=True,
            normalize_embeddings=True  # Cosine similarity is equivalent to dot product on normalized vectors
        )
        return embeddings

    def get_dimension(self, model_name: str) -> int:
        """
        Returns the vector dimension for the selected model.
        """
        if model_name == "BGE-Small-EN-v1.5":
            return 384
        elif model_name == "Nomic-Embed-Text-v1.5":
            return 768
        else:
            raise ValueError(f"Unknown model name: {model_name}")
