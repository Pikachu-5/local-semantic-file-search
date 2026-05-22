import os
import sys
import ctypes
from ctypes import wintypes

# Everything Request Flags
EVERYTHING_REQUEST_FILE_NAME = 0x00000001
EVERYTHING_REQUEST_PATH = 0x00000002
EVERYTHING_REQUEST_FULL_PATH_AND_FILE_NAME = 0x00000004

class EverythingSearchEngine:
    """
    Python wrapper around the Voidtools Everything SDK DLL using ctypes.
    Provides extremely fast, low-level NTFS search capability.
    """
    def __init__(self):
        self.dll = None
        self._initialized = False
        self._load_dll()

    def _load_dll(self):
        # Resolve the DLL path relative to this file
        src_dir = os.path.dirname(os.path.abspath(__file__))
        backend_dir = os.path.dirname(src_dir)
        root_dir = os.path.dirname(backend_dir)
        
        # Decide DLL based on Python interpreter architecture
        is_64bit = sys.maxsize > 2**32
        dll_name = "Everything64.dll" if is_64bit else "Everything32.dll"
        dll_path = os.path.join(root_dir, "Everything-SDK", "dll", dll_name)

        if not os.path.exists(dll_path):
            print(f"[-] Everything DLL not found at: {dll_path}")
            return

        try:
            # Load the Windows DLL using WinDLL (stdcall calling convention)
            self.dll = ctypes.WinDLL(dll_path)
            
            # 1. Define Everything_SetSearchW
            # void Everything_SetSearchW(const wchar_t* lpString)
            self.dll.Everything_SetSearchW.argtypes = [ctypes.c_wchar_p]
            self.dll.Everything_SetSearchW.restype = None

            # 2. Define Everything_SetRequestFlags
            # void Everything_SetRequestFlags(DWORD dwRequestFlags)
            self.dll.Everything_SetRequestFlags.argtypes = [wintypes.DWORD]
            self.dll.Everything_SetRequestFlags.restype = None

            # 3. Define Everything_QueryW
            # BOOL Everything_QueryW(BOOL bWait)
            self.dll.Everything_QueryW.argtypes = [wintypes.BOOL]
            self.dll.Everything_QueryW.restype = wintypes.BOOL

            # 4. Define Everything_GetNumResults
            # DWORD Everything_GetNumResults(void)
            self.dll.Everything_GetNumResults.argtypes = []
            self.dll.Everything_GetNumResults.restype = wintypes.DWORD

            # 5. Define Everything_GetResultFullPathNameW
            # DWORD Everything_GetResultFullPathNameW(DWORD nIndex, LPWSTR lpBuffer, DWORD nBufferLength)
            self.dll.Everything_GetResultFullPathNameW.argtypes = [wintypes.DWORD, ctypes.c_wchar_p, wintypes.DWORD]
            self.dll.Everything_GetResultFullPathNameW.restype = wintypes.DWORD

            # 6. Define Everything_IsDBLoaded
            # BOOL Everything_IsDBLoaded(void)
            self.dll.Everything_IsDBLoaded.argtypes = []
            self.dll.Everything_IsDBLoaded.restype = wintypes.BOOL

            # 7. Define Everything_GetLastError
            # DWORD Everything_GetLastError(void)
            self.dll.Everything_GetLastError.argtypes = []
            self.dll.Everything_GetLastError.restype = wintypes.DWORD

            self._initialized = True
            print(f"[+] Loaded Everything SDK DLL: {dll_name}")
        except Exception as e:
            print(f"[-] Failed to initialize Everything ctypes SDK: {e}")

    def is_service_running(self) -> bool:
        """Checks if the Everything service/DB is loaded in the background."""
        if not self._initialized or not self.dll:
            return False
        try:
            return bool(self.dll.Everything_IsDBLoaded())
        except Exception:
            return False

    def search(self, query: str, limit: int = 10, folder_paths: list = None) -> list:
        """
        Executes a rapid search query against Voidtools Everything.
        Optionally constrains matches to a list of allowed folder paths.
        Returns a list of dictionaries with matching file_path and file_name.
        """
        if not self._initialized or not self.dll:
            print("[-] Everything SDK not initialized.")
            return []

        if not self.is_service_running():
            print("[-] Everything background service is not running or DB is not loaded.")
            return []

        if not query.strip():
            return []

        # If allowed folder paths are specified, construct a parent directory filter query
        if folder_paths:
            # Filter out empty or invalid paths
            valid_paths = [os.path.normpath(p) for p in folder_paths if p.strip()]
            if valid_paths:
                path_clause = " | ".join(f'"{p}"' for p in valid_paths)
                query = f"<{path_clause}> {query}"

        try:
            # Set search term (using Unicode version)
            self.dll.Everything_SetSearchW(query)
            
            # Configure requests to fetch both path and filename
            self.dll.Everything_SetRequestFlags(EVERYTHING_REQUEST_FULL_PATH_AND_FILE_NAME)
            
            # Run the blocking query
            if not self.dll.Everything_QueryW(True):
                err = self.dll.Everything_GetLastError()
                print(f"[-] Everything query returned error code: {err}")
                return []

            # Retrieve result count and cap it by the requested limit
            total_results = self.dll.Everything_GetNumResults()
            count = min(total_results, limit)
            
            results = []
            MAX_PATH = 32768  # Support long Windows paths
            buffer = ctypes.create_unicode_buffer(MAX_PATH)

            for i in range(count):
                self.dll.Everything_GetResultFullPathNameW(i, buffer, MAX_PATH)
                file_path = buffer.value
                if file_path:
                    results.append({
                        "file_path": file_path,
                        "file_name": os.path.basename(file_path),
                        "chunk_text": "",  # Empty snippet for standard filename search
                        "relevance_score": 1.0  # NTFS match relevance score (perfect by default)
                    })
                    
            return results
        except Exception as e:
            print(f"[-] Error querying Everything SDK: {e}")
            return []
