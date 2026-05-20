import os
import sys
import subprocess
import shutil

def main():
    # Root of backend directory
    backend_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    proto_dir = os.path.join(backend_dir, "proto")
    src_proto_dir = os.path.join(backend_dir, "src", "proto")
    proto_file = os.path.join(proto_dir, "service.proto")

    print(f"[*] Backend Directory: {backend_dir}")
    print(f"[*] Proto Directory: {proto_dir}")
    print(f"[*] Output Directory: {src_proto_dir}")

    # Ensure output directory exists and is a python package
    os.makedirs(src_proto_dir, exist_ok=True)
    init_file = os.path.join(src_proto_dir, "__init__.py")
    if not os.path.exists(init_file):
        with open(init_file, "w") as f:
            f.write("# Generated gRPC stubs package\n")

    if not os.path.exists(proto_file):
        print(f"[-] Error: Proto file not found at {proto_file}")
        sys.exit(1)

    # Command to run grpc_tools compiler
    cmd = [
        sys.executable, "-m", "grpc_tools.protoc",
        f"-I{proto_dir}",
        f"--python_out={src_proto_dir}",
        f"--grpc_python_out={src_proto_dir}",
        proto_file
    ]

    print(f"[*] Running command: {' '.join(cmd)}")
    result = subprocess.run(cmd, cwd=backend_dir, capture_output=True, text=True)

    if result.returncode != 0:
        print("[-] gRPC compilation failed!")
        print(f"STDOUT:\n{result.stdout}")
        print(f"STDERR:\n{result.stderr}")
        sys.exit(result.returncode)

    print("[+] gRPC compilation succeeded!")

    # Fix absolute import in generated _grpc file
    # protoc generated code imports `import service_pb2 as service__pb2`
    # which fails if we import the stubs package locally as a submodule.
    # We change it to `from . import service_pb2 as service__pb2`.
    grpc_stub_file = os.path.join(src_proto_dir, "service_pb2_grpc.py")
    if os.path.exists(grpc_stub_file):
        with open(grpc_stub_file, "r") as f:
            content = f.read()
        
        # Replace the absolute import with a relative one
        fixed_content = content.replace("import service_pb2 as service__pb2", "from . import service_pb2 as service__pb2")
        
        with open(grpc_stub_file, "w") as f:
            f.write(fixed_content)
        print("[+] Fixed relative imports in service_pb2_grpc.py")

if __name__ == "__main__":
    main()
