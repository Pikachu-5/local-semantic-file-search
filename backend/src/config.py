import os
import json
from typing import List, Dict, Any

DEFAULT_CONFIG = {
    "active_model": "BGE-Small-EN-v1.5",
    "excluded_dirs": [".git", "node_modules", ".venv", "bin", "obj", "__pycache__", "build", "dist"],
    "included_extensions": [".txt", ".md", ".pdf", ".py", ".cs", ".json", ".cpp", ".h"],
    "watched_folders": []
}

def get_config_path() -> str:
    """
    Resolves the configuration file path at %LOCALAPPDATA%/SwiftSearch/config.json.
    Creates parent directories if necessary.
    """
    local_app_data = os.getenv("LOCALAPPDATA")
    if not local_app_data:
        local_app_data = os.path.expanduser("~")
    config_dir = os.path.join(local_app_data, "SwiftSearch")
    os.makedirs(config_dir, exist_ok=True)
    return os.path.join(config_dir, "config.json")

class ConfigManager:
    """
    Manages loading, updating, and saving SwiftSearch system settings.
    Persistent storage is standard JSON.
    """
    def __init__(self, config_path: str = None):
        self.config_path = config_path or get_config_path()
        self.config = self._load()

    def _load(self) -> Dict[str, Any]:
        """
        Loads the config from disk, falling back to defaults if missing or corrupted.
        """
        if not os.path.exists(self.config_path):
            self._save(DEFAULT_CONFIG)
            return DEFAULT_CONFIG.copy()
        try:
            with open(self.config_path, "r", encoding="utf-8") as f:
                data = json.load(f)
            # Ensure all default keys are present
            merged = DEFAULT_CONFIG.copy()
            merged.update(data)
            return merged
        except Exception as e:
            print(f"[-] Config loading error: {e}. Resetting to defaults.")
            self._save(DEFAULT_CONFIG)
            return DEFAULT_CONFIG.copy()

    def _save(self, data: Dict[str, Any]) -> None:
        """
        Saves the config dict to disk.
        """
        try:
            with open(self.config_path, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=4)
        except Exception as e:
            print(f"[-] Config saving error: {e}")

    def get_all(self) -> Dict[str, Any]:
        return self.config

    def get(self, key: str, default: Any = None) -> Any:
        return self.config.get(key, default)

    def update_settings(self, active_model: str = None, excluded_dirs: List[str] = None, included_extensions: List[str] = None) -> None:
        """
        Updates core application settings.
        """
        if active_model is not None:
            self.config["active_model"] = active_model
        if excluded_dirs is not None:
            # Exclude empty values and standard normalize
            self.config["excluded_dirs"] = [d.strip() for d in excluded_dirs if d.strip()]
        if included_extensions is not None:
            # Ensure extensions start with . and are lowercase
            cleaned_exts = []
            for ext in included_extensions:
                ext = ext.strip().lower()
                if ext:
                    if not ext.startswith("."):
                        ext = "." + ext
                    cleaned_exts.append(ext)
            self.config["included_extensions"] = cleaned_exts
            
        self._save(self.config)

    def add_watched_folder(self, path: str) -> bool:
        """
        Adds a directory path to the watched folder list if not already present.
        Returns True if added, False otherwise.
        """
        abs_path = os.path.abspath(path)
        if abs_path not in self.config["watched_folders"]:
            self.config["watched_folders"].append(abs_path)
            self._save(self.config)
            return True
        return False

    def remove_watched_folder(self, path: str) -> bool:
        """
        Removes a directory path from the watched list.
        Returns True if removed, False otherwise.
        """
        abs_path = os.path.abspath(path)
        if abs_path in self.config["watched_folders"]:
            self.config["watched_folders"].remove(abs_path)
            self._save(self.config)
            return True
        return False
