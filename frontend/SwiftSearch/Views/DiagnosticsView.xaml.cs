using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace SwiftSearch.Views
{
    public sealed partial class DiagnosticsView : Page
    {
        private readonly DispatcherTimer _pollTimer;
        private bool _isUnloaded;

        public DiagnosticsView()
        {
            this.InitializeComponent();

            // Populate historical logs
            var history = App.SearchService.LogHistory;
            var sb = new StringBuilder();
            foreach (var line in history)
            {
                sb.AppendLine(line);
            }
            TerminalTextBox.Text = sb.ToString();

            // Subscribe to live log notifications
            App.SearchService.LogReceived += SearchService_LogReceived;
            this.Unloaded += DiagnosticsView_Unloaded;

            // Setup polling timer (updates every 2 seconds)
            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _pollTimer.Tick += PollTimer_Tick;
            _pollTimer.Start();

            // Initial refresh of metrics
            RefreshMetrics();
        }

        private void DiagnosticsView_Unloaded(object sender, RoutedEventArgs e)
        {
            _isUnloaded = true;
            _pollTimer.Stop();
            App.SearchService.LogReceived -= SearchService_LogReceived;
        }

        private void SearchService_LogReceived(string logLine)
        {
            if (_isUnloaded) return;

            DispatcherQueue.TryEnqueue(() =>
            {
                var filter = SearchLogTextBox.Text;
                
                // If filter is active and does not match line, skip
                if (!string.IsNullOrEmpty(filter) && !logLine.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                TerminalTextBox.Text += logLine + "\n";

                if (AutoScrollCheck.IsChecked == true)
                {
                    TerminalTextBox.Select(TerminalTextBox.Text.Length, 0);
                }
            });
        }

        private void PollTimer_Tick(object? sender, object e)
        {
            RefreshMetrics();
        }

        private void RefreshMetrics()
        {
            var service = App.SearchService;

            // 1. Connection Status Card
            if (service.IsDaemonOnline)
            {
                if (service.IsDownloadingModel)
                {
                    // Orange / Weights Downloading state
                    ConnGlow.Fill = new SolidColorBrush(Color.FromArgb(255, 255, 149, 0));
                    ConnCore.Fill = new SolidColorBrush(Color.FromArgb(255, 255, 149, 0));
                    ConnStatusText.Text = "Downloading...";
                }
                else
                {
                    // Green / Connected state
                    ConnGlow.Fill = new SolidColorBrush(Color.FromArgb(255, 52, 199, 89));
                    ConnCore.Fill = new SolidColorBrush(Color.FromArgb(255, 52, 199, 89));
                    ConnStatusText.Text = "Connected";
                }

                // 2. Socket Details Card
                PortText.Text = $"Port: {service.DaemonPort}";
                PidText.Text = $"Process ID (PID): {service.DaemonPid}";

                // 3. Memory Card
                double memMb = service.DaemonMemoryBytes / (1024.0 * 1024.0);
                MemoryText.Text = $"{memMb:F1} MB";

                // 4. Watchdog Card
                WatchdogText.Text = "Running";
                WatchdogWatchedText.Text = $"{service.WatchedFolders.Count} folders monitored";
            }
            else
            {
                // Red / Disconnected state
                ConnGlow.Fill = new SolidColorBrush(Color.FromArgb(255, 255, 59, 48));
                ConnCore.Fill = new SolidColorBrush(Color.FromArgb(255, 255, 59, 48));
                ConnStatusText.Text = "Disconnected";

                PortText.Text = "Port: ---";
                PidText.Text = "PID: ---";
                MemoryText.Text = "0.0 MB";
                WatchdogText.Text = "Inactive";
                WatchdogWatchedText.Text = "0 folders monitored";
            }
        }

        private async void PingButton_Click(object sender, RoutedEventArgs e)
        {
            PingButton.IsEnabled = false;
            PingLatencyText.Text = "Pinging...";

            long latency = await App.SearchService.PingDaemonAsync();

            if (latency >= 0)
            {
                PingLatencyText.Text = $"Latency: {latency} ms";
            }
            else
            {
                PingLatencyText.Text = "Latency: Timeout";
            }

            PingButton.IsEnabled = true;
        }

        private async void RecycleButton_Click(object sender, RoutedEventArgs e)
        {
            RecycleButton.IsEnabled = false;
            TerminalTextBox.Text += "\n[System Controller] Stopping dynamic backend daemon...\n";
            App.SearchService.StopDaemon();
            
            RefreshMetrics();
            await Task.Delay(1000);

            TerminalTextBox.Text += "[System Controller] Initiating standard process restart...\n";
            try
            {
                App.SearchService.StartDaemon();
                TerminalTextBox.Text += "[System Controller] Backend daemon re-established successfully!\n";
            }
            catch (Exception ex)
            {
                TerminalTextBox.Text += $"[System Controller ERROR] Failed to bind backend daemon: {ex.Message}\n";
            }

            RefreshMetrics();
            RecycleButton.IsEnabled = true;
        }

        private void OpenLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diag_log.txt");
                if (File.Exists(logPath))
                {
                    Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
                }
                else
                {
                    TerminalTextBox.Text += $"[System] Diagnostics log file not found at: {logPath}\n";
                }
            }
            catch (Exception ex)
            {
                TerminalTextBox.Text += $"[System] Failed to launch log editor: {ex.Message}\n";
            }
        }

        private void OpenConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string configFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwiftSearch");
                if (Directory.Exists(configFolder))
                {
                    Process.Start(new ProcessStartInfo(configFolder) { UseShellExecute = true });
                }
                else
                {
                    TerminalTextBox.Text += $"[System] AppData configuration directory does not exist yet.\n";
                }
            }
            catch (Exception ex)
            {
                TerminalTextBox.Text += $"[System] Failed to open folder: {ex.Message}\n";
            }
        }

        private void SearchLogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var filter = SearchLogTextBox.Text;
            var history = App.SearchService.LogHistory;
            var sb = new StringBuilder();

            foreach (var line in history)
            {
                if (string.IsNullOrEmpty(filter) || line.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine(line);
                }
            }

            TerminalTextBox.Text = sb.ToString();

            if (AutoScrollCheck.IsChecked == true)
            {
                TerminalTextBox.Select(TerminalTextBox.Text.Length, 0);
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            App.SearchService.ClearLogs();
            TerminalTextBox.Text = string.Empty;
        }

        private async void CopyLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var package = new DataPackage();
                package.SetText(TerminalTextBox.Text);
                Clipboard.SetContent(package);

                // Transient visual feedback on button text
                string originalText = "Copy All";
                CopyLogButton.Content = "Copied!";
                CopyLogButton.IsEnabled = false;

                await Task.Delay(1500);

                CopyLogButton.Content = originalText;
                CopyLogButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                TerminalTextBox.Text += $"[System] Clipboard copy failed: {ex.Message}\n";
            }
        }
    }
}
