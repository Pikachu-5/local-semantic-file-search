import os
import sys

# Add src to sys.path
src_dir = os.path.dirname(os.path.abspath(__file__))
if src_dir not in sys.path:
    sys.path.insert(0, src_dir)

from everything import EverythingSearchEngine

def test():
    print("[*] Initializing Everything Search Engine...")
    engine = EverythingSearchEngine()
    print(f"[*] DLL loaded: {engine._initialized}")
    print(f"[*] Is Everything service/DB running/loaded? {engine.is_service_running()}")
    
    if not engine.is_service_running():
        print("[-] Everything Service is NOT running. Please launch the Everything application from voidtools.")
        return
        
    query = "test"
    print(f"[*] Querying Everything for '{query}'...")
    results = engine.search(query, limit=5)
    print(f"[+] Found {len(results)} matches:")
    for r in results:
        print(f"  - {r['file_name']} -> {r['file_path']}")

if __name__ == "__main__":
    test()
