@echo off
cd /d "%~dp0"
echo =======================================================
echo  SwiftSearch Backend Environment Setup
echo =======================================================

echo [*] Creating Python virtual environment in .venv...
python -m venv .venv
if %errorlevel% neq 0 (
    echo [-] Error: Failed to create virtual environment.
    exit /b %errorlevel%
)

echo [*] Activating virtual environment...
call .venv\Scripts\activate.bat

echo [*] Upgrading pip...
python -m pip install --upgrade pip

echo [*] Installing required python libraries...
pip install -r requirements.txt
if %errorlevel% neq 0 (
    echo [-] Error: Failed to install libraries.
    exit /b %errorlevel%
)

echo [*] Compiling gRPC stubs...
python scripts\compile_proto.py
if %errorlevel% neq 0 (
    echo [-] Error: gRPC compilation failed.
    exit /b %errorlevel%
)

echo =======================================================
echo [+] Success! SwiftSearch Python environment is ready.
echo =======================================================
