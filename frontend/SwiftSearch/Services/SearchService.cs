using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using SwiftSearch.Core;
using SwiftSearch.Core.Protos;
using SwiftSearch.Models;

namespace SwiftSearch.Services
{
    public class SearchService : ISearchService
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private Process? _daemonProcess;
        private GrpcChannel? _channel;
        private SearchEngine.SearchEngineClient? _client;
        private Timer? _statusTimer;
        private readonly SynchronizationContext? _syncContext;

        private bool _isDaemonOnline;
        private bool _isDownloadingModel;
        private int _totalFiles;
        private int _totalVectors;
        private string _activeModel = "BGE-Small-EN-v1.5";
        private List<string> _watchedFolders = new();
        private List<string> _excludedDirs = new();
        private List<string> _includedExtensions = new();

        // Diagnostics fields
        private int _daemonPort;
        private int _daemonPid;
        private readonly List<string> _logHistory = new();
        private readonly object _logLock = new();

        public bool IsDaemonOnline
        {
            get => _isDaemonOnline;
            private set { _isDaemonOnline = value; OnPropertyChanged(); }
        }

        public bool IsDownloadingModel
        {
            get => _isDownloadingModel;
            private set { _isDownloadingModel = value; OnPropertyChanged(); }
        }

        public int TotalFiles
        {
            get => _totalFiles;
            private set { _totalFiles = value; OnPropertyChanged(); }
        }

        public int TotalVectors
        {
            get => _totalVectors;
            private set { _totalVectors = value; OnPropertyChanged(); }
        }

        public string ActiveModel
        {
            get => _activeModel;
            private set { _activeModel = value; OnPropertyChanged(); }
        }

        public List<string> WatchedFolders
        {
            get => _watchedFolders;
            private set { _watchedFolders = value; OnPropertyChanged(); }
        }

        public List<string> ExcludedDirs
        {
            get => _excludedDirs;
            private set { _excludedDirs = value; OnPropertyChanged(); }
        }

        public List<string> IncludedExtensions
        {
            get => _includedExtensions;
            private set { _includedExtensions = value; OnPropertyChanged(); }
        }

        // Diagnostics properties
        public int DaemonPort
        {
            get => _daemonPort;
            private set { _daemonPort = value; OnPropertyChanged(); }
        }

        public int DaemonPid
        {
            get => _daemonPid;
            private set { _daemonPid = value; OnPropertyChanged(); }
        }

        public long DaemonMemoryBytes
        {
            get
            {
                if (_daemonProcess == null || _daemonProcess.HasExited) return 0;
                try
                {
                    _daemonProcess.Refresh();
                    return _daemonProcess.PrivateMemorySize64;
                }
                catch
                {
                    return 0;
                }
            }
        }

        public List<string> LogHistory
        {
            get
            {
                lock (_logLock)
                {
                    return new List<string>(_logHistory);
                }
            }
        }

        public event Action<string>? LogReceived;

        public void ClearLogs()
        {
            lock (_logLock)
            {
                _logHistory.Clear();
            }
        }

        private void Log(string message)
        {
            Debug.WriteLine(message);
            try
            {
                string diagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diag_log.txt");
                File.AppendAllText(diagPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch { }

            lock (_logLock)
            {
                _logHistory.Add(message);
                if (_logHistory.Count > 300)
                {
                    _logHistory.RemoveAt(0);
                }
            }
            
            _syncContext?.Post(_ => LogReceived?.Invoke(message), null);
        }

        public SearchService()
        {
            _syncContext = SynchronizationContext.Current;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            if (_syncContext != null && SynchronizationContext.Current != _syncContext)
            {
                _syncContext.Post(_ => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)), null);
            }
            else
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public void StartDaemon()
        {
            if (IsDaemonOnline) return;

            try
            {
                try
                {
                    string diagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diag_log.txt");
                    File.WriteAllText(diagPath, $"=== SwiftSearch StartDaemon at {DateTime.Now} ===\n");
                }
                catch { }

                Log("[SearchService] Starting daemon process discovery...");
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string? rootDir = null;
                string current = baseDir;
                string diagLog = $"baseDir: {baseDir}\n";

                // Walk up to find the project root containing 'backend' folder
                for (int i = 0; i < 10; i++)
                {
                    string testPath = Path.Combine(current, "backend");
                    bool exists = Directory.Exists(testPath);
                    diagLog += $"Loop {i}: current='{current}', exists={exists}\n";
                    if (exists)
                    {
                        rootDir = current;
                        break;
                    }
                    string? parent = Path.GetDirectoryName(current);
                    if (parent == null || parent == current) break;
                    current = parent;
                }

                string pythonExe = string.Empty;
                string scriptFile = string.Empty;
                bool isDev = false;

                if (rootDir != null)
                {
                    pythonExe = Path.Combine(rootDir, "backend", ".venv", "Scripts", "python.exe");
                    scriptFile = Path.Combine(rootDir, "backend", "src", "grpc_server.py");
                    isDev = File.Exists(pythonExe) && File.Exists(scriptFile);
                    diagLog += $"pythonExe: {pythonExe} (exists={File.Exists(pythonExe)})\n";
                    diagLog += $"scriptFile: {scriptFile} (exists={File.Exists(scriptFile)})\n";
                }
                else
                {
                    diagLog += "rootDir is NULL\n";
                }
                diagLog += $"isDev: {isDev}\n";

                try
                {
                    string diagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diag_log.txt");
                    File.WriteAllText(diagPath, diagLog);
                }
                catch { }

                ProcessStartInfo psi = new();
                if (isDev)
                {
                    Log($"[SearchService] Running in Development Mode. Loader: {pythonExe}");
                    psi.FileName = pythonExe;
                    psi.Arguments = $"\"{scriptFile}\"";
                    psi.WorkingDirectory = Path.Combine(rootDir!, "backend");
                }
                else
                {
                    Log("[SearchService] Running in Production Mode. Searching for bundled daemon...");
                    string productionExe = Path.Combine(baseDir, "daemon", "grpc_server.exe");
                    if (!File.Exists(productionExe))
                    {
                        // Direct base path fall back
                        productionExe = Path.Combine(baseDir, "grpc_server.exe");
                    }
                    
                    psi.FileName = productionExe;
                    psi.Arguments = "";
                    psi.WorkingDirectory = baseDir;
                }

                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

                _daemonProcess = new Process { StartInfo = psi };
                _daemonProcess.Start();
                DaemonPid = _daemonProcess.Id;

                // Assign immediately to Windows Job Objects for robust auto-termination on exit
                ChildProcessTracker.AddProcess(_daemonProcess);

                Log($"[SearchService] Daemon started (PID: {DaemonPid}). Reading stdout dynamic port...");

                // Read standard output to catch dynamic gRPC port
                int port = 0;
                string? line;
                
                // Read handshake line-by-line
                while ((line = _daemonProcess.StandardOutput.ReadLine()) != null)
                {
                    Log($"[Daemon stdout] {line}");
                    if (line.StartsWith("GRPC_READY:"))
                    {
                        string portStr = line.Substring("GRPC_READY:".Length).Trim();
                        if (int.TryParse(portStr, out int parsedPort))
                        {
                            port = parsedPort;
                            break;
                        }
                    }
                }

                if (port == 0)
                {
                    // Catch premature termination
                    if (_daemonProcess.HasExited)
                    {
                        string stderr = _daemonProcess.StandardError.ReadToEnd();
                        throw new Exception($"Daemon exited prematurely. Stderr:\n{stderr}");
                    }
                    throw new Exception("Failed to intercept dynamic port from daemon output.");
                }

                DaemonPort = port;
                Log($"[SearchService] Connected to dynamic port: {DaemonPort}");

                // Connect gRPC client channel securely via localhost loopback
                _channel = GrpcChannel.ForAddress($"http://127.0.0.1:{DaemonPort}");
                _client = new SearchEngine.SearchEngineClient(_channel);

                IsDaemonOnline = true;
                IsDownloadingModel = false;

                // Continuously exhaust stdout and stderr in background tasks to prevent OS pipe hangs and track download state
                Task.Run(() => ReadDaemonOutput(_daemonProcess));
                Task.Run(() => ReadDaemonError(_daemonProcess));

                // Begin background polling checks (every 3 seconds)
                _statusTimer = new Timer(async _ => await RefreshStatusAsync(), null, 100, 3000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchService] Daemon initialization crashed: {ex.Message}");
                IsDaemonOnline = false;
                IsDownloadingModel = false;
                DaemonPid = 0;
                DaemonPort = 0;
                throw;
            }
        }

        public void StopDaemon()
        {
            _statusTimer?.Dispose();
            _statusTimer = null;

            _channel?.Dispose();
            _channel = null;
            _client = null;

            if (_daemonProcess != null && !_daemonProcess.HasExited)
            {
                try
                {
                    _daemonProcess.Kill();
                }
                catch { }
                _daemonProcess.Dispose();
                _daemonProcess = null;
            }

            IsDaemonOnline = false;
            DaemonPid = 0;
            DaemonPort = 0;
        }

        public async Task<List<SearchItem>> SearchAsync(string query, int topK)
        {
            if (_client == null || !IsDaemonOnline) return new List<SearchItem>();

            try
            {
                var request = new SearchRequest { Query = query, TopK = topK };
                var response = await _client.SemanticSearchAsync(request);
                
                var results = new List<SearchItem>();
                foreach (var r in response.Results)
                {
                    results.Add(new SearchItem
                    {
                        FilePath = r.FilePath,
                        FileName = r.FileName,
                        ChunkText = r.ChunkText,
                        RelevanceScore = r.RelevanceScore
                    });
                }
                return results;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchService] SemanticSearchAsync failed: {ex.Message}");
                return new List<SearchItem>();
            }
        }

        public async Task<bool> IndexFolderAsync(string folderPath)
        {
            if (_client == null || !IsDaemonOnline) return false;

            try
            {
                var request = new IndexRequest { FolderPath = folderPath, AutoWatch = true };
                var response = await _client.IndexTargetFolderAsync(request);
                return response.Success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchService] IndexTargetFolderAsync failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UpdateSettingsAsync(string activeModel, List<string> excludedDirs, List<string> includedExtensions)
        {
            if (_client == null || !IsDaemonOnline) return false;

            try
            {
                var request = new SettingsRequest { ActiveModel = activeModel };
                if (excludedDirs != null) request.ExcludedDirs.AddRange(excludedDirs);
                if (includedExtensions != null) request.IncludedExtensions.AddRange(includedExtensions);

                var response = await _client.UpdateSettingsAsync(request);
                if (response.Success)
                {
                    ActiveModel = activeModel;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchService] UpdateSettingsAsync failed: {ex.Message}");
                return false;
            }
        }

        public async Task RefreshStatusAsync()
        {
            if (_client == null) return;

            try
            {
                var response = await _client.GetSystemStatusAsync(new Empty());
                
                // Safe UI thread properties updates
                IsDaemonOnline = true;
                TotalFiles = response.TotalIndexedFiles;
                TotalVectors = response.TotalVectors;
                ActiveModel = response.ActiveModel;
                
                var watched = new List<string>(response.WatchedFolders);
                WatchedFolders = watched;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchService] GetSystemStatusAsync poller failed: {ex.Message}");
                IsDaemonOnline = false;
            }
        }

        public async Task<bool> RemoveFolderAsync(string folderPath)
        {
            if (_client == null || !IsDaemonOnline) return false;

            try
            {
                var request = new IndexRequest { FolderPath = $"REMOVE:{folderPath}", AutoWatch = false };
                var response = await _client.IndexTargetFolderAsync(request);
                return response.Success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchService] RemoveFolderAsync failed: {ex.Message}");
                return false;
            }
        }

        private void ReadDaemonOutput(Process process)
        {
            try
            {
                string? line;
                while (!process.HasExited && (line = process.StandardOutput.ReadLine()) != null)
                {
                    Log($"[Daemon stdout] {line}");
                    if (line.Contains("[*] Loading model"))
                    {
                        _syncContext?.Post(_ => IsDownloadingModel = true, null);
                    }
                    else if (line.Contains("loaded successfully!"))
                    {
                        _syncContext?.Post(_ => IsDownloadingModel = false, null);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[SearchService] Error reading daemon stdout: {ex.Message}");
            }
        }

        private void ReadDaemonError(Process process)
        {
            try
            {
                string? line;
                while (!process.HasExited && (line = process.StandardError.ReadLine()) != null)
                {
                    Log($"[Daemon stderr] {line}");
                }
            }
            catch (Exception ex)
            {
                Log($"[SearchService] Error reading daemon stderr: {ex.Message}");
            }
        }

        public async Task<long> PingDaemonAsync()
        {
            if (_client == null || !IsDaemonOnline) return -1;
            var sw = Stopwatch.StartNew();
            try
            {
                await _client.GetSystemStatusAsync(new Empty());
                sw.Stop();
                return sw.ElapsedMilliseconds;
            }
            catch (Exception ex)
            {
                Log($"[SearchService] Ping failed: {ex.Message}");
                return -1;
            }
        }
    }
}
