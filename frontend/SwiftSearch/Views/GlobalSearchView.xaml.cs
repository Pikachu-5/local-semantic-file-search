using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SwiftSearch.Views
{
    public sealed partial class GlobalSearchView : Page
    {
        private readonly DispatcherTimer _debounceTimer;
        private string _lastQuery = string.Empty;
        private bool _isInitialLoaded = false;
        private bool _wasDaemonOnline = false;

        public GlobalSearchView()
        {
            this.InitializeComponent();

            // Set up search debouncing to prevent channel flooding
            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _debounceTimer.Tick += DebounceTimer_Tick;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            App.SearchService.PropertyChanged += SearchService_PropertyChanged;
            
            // Set initial state
            _wasDaemonOnline = App.SearchService.IsDaemonOnline;

            // Automatically trigger the initial browse of 100 files on load
            if (!_isInitialLoaded)
            {
                _isInitialLoaded = true;
                await TriggerSearchAsync(string.Empty, 100);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            App.SearchService.PropertyChanged -= SearchService_PropertyChanged;
            _debounceTimer.Stop();
        }

        private void SearchService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(App.SearchService.IsDaemonOnline))
            {
                bool isOnline = App.SearchService.IsDaemonOnline;
                if (isOnline == _wasDaemonOnline) return;
                _wasDaemonOnline = isOnline;

                DispatcherQueue.TryEnqueue(async () =>
                {
                    if (isOnline)
                    {
                        await TriggerSearchAsync(SearchTextBox.Text, 200);
                    }
                    else
                    {
                        ShowOfflineOverlay();
                    }
                });
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private async void DebounceTimer_Tick(object? sender, object e)
        {
            _debounceTimer.Stop();
            string query = SearchTextBox.Text.Trim();
            
            if (query == _lastQuery) return;
            _lastQuery = query;

            // Empty query lists the top 100 files, otherwise search lists 200 files
            int topK = string.IsNullOrEmpty(query) ? 100 : 200;
            await TriggerSearchAsync(query, topK);
        }

        private async Task TriggerSearchAsync(string query, int topK)
        {
            if (!App.SearchService.IsDaemonOnline)
            {
                ShowOfflineOverlay();
                return;
            }

            SearchProgressRing.IsActive = true;
            ConnectingPanel.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
            NoResultsPanel.Visibility = Visibility.Collapsed;
            OfflinePanel.Visibility = Visibility.Collapsed;

            try
            {
                var results = await App.SearchService.SearchEverythingGlobalAsync(query, topK);
                SearchProgressRing.IsActive = false;
                ConnectingPanel.Visibility = Visibility.Collapsed;

                if (results != null && results.Count > 0)
                {
                    ResultsListView.ItemsSource = results;
                    ResultsListView.Visibility = Visibility.Visible;
                    OfflinePanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ResultsListView.Visibility = Visibility.Collapsed;
                    ResultsListView.ItemsSource = null;

                    // If empty query returns 0 results, Everything background app is 100% offline
                    if (string.IsNullOrEmpty(query))
                    {
                        OfflinePanel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        NoResultsPanel.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalSearchView] Search failed: {ex.Message}");
                SearchProgressRing.IsActive = false;
                ConnectingPanel.Visibility = Visibility.Collapsed;
                ResultsListView.Visibility = Visibility.Collapsed;
                NoResultsPanel.Visibility = Visibility.Visible;
            }
        }

        private void ShowOfflineOverlay()
        {
            SearchProgressRing.IsActive = false;
            ConnectingPanel.Visibility = Visibility.Collapsed;
            ResultsListView.Visibility = Visibility.Collapsed;
            NoResultsPanel.Visibility = Visibility.Collapsed;
            OfflinePanel.Visibility = Visibility.Visible;
        }

        private void ResultsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Models.SearchItem item)
            {
                OpenFile(item.FilePath);
            }
        }

        private void OpenFile_ContextClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.DataContext is Models.SearchItem item)
            {
                OpenFile(item.FilePath);
            }
        }

        private void OpenFolder_ContextClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.DataContext is Models.SearchItem item)
            {
                try
                {
                    // Selects and highlights the file inside Windows Explorer
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FilePath}\"")
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GlobalSearchView] Failed to highlight file in folder: {ex.Message}");
                }
            }
        }

        private void OpenFile(string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo(filePath)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalSearchView] Failed to open file: {ex.Message}");
            }
        }

        private void LaunchEverything_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Try launching Everything via system path command
                Process.Start(new ProcessStartInfo("Everything.exe") { UseShellExecute = true });
            }
            catch
            {
                try
                {
                    // Fallback to common Program Files locations
                    string path64 = @"C:\Program Files\Everything\Everything.exe";
                    string path32 = @"C:\Program Files (x86)\Everything\Everything.exe";

                    if (System.IO.File.Exists(path64))
                    {
                        Process.Start(new ProcessStartInfo(path64) { UseShellExecute = true });
                    }
                    else if (System.IO.File.Exists(path32))
                    {
                        Process.Start(new ProcessStartInfo(path32) { UseShellExecute = true });
                    }
                    else
                    {
                        // Open Voidtools website to download if it is missing entirely
                        Process.Start(new ProcessStartInfo("https://www.voidtools.com/") { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GlobalSearchView] Failed to launch Everything app: {ex.Message}");
                }
            }
        }
    }
}
