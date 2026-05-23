using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using SwiftSearch.Services;

namespace SwiftSearch.Views
{
    public sealed partial class SettingsView : Page
    {
        private bool _isSyncing = false;

        public SettingsView()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            App.SearchService.PropertyChanged += SearchService_PropertyChanged;
            App.SearchService.ModelDownloadProgressChanged += SearchService_ModelDownloadProgressChanged;
            
            // Initial UI sync
            SyncSettingsToUI();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            App.SearchService.PropertyChanged -= SearchService_PropertyChanged;
            App.SearchService.ModelDownloadProgressChanged -= SearchService_ModelDownloadProgressChanged;
        }

        private void SearchService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                SyncSettingsToUI();
            });
        }

        private void SearchService_ModelDownloadProgressChanged(string modelName, float progress, string status)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (progress < 100.0 && !status.StartsWith("Failed"))
                {
                    DownloadProgressPanel.Visibility = Visibility.Visible;
                    DownloadStatusText.Text = $"{status} ({modelName})";
                    DownloadProgressPercent.Text = $"{progress:F1}%";
                    DownloadProgressBar.Value = progress;
                }
                else
                {
                    DownloadProgressPanel.Visibility = Visibility.Collapsed;
                }
            });
        }

        private bool AreListsEqual(List<string> list1, List<string> list2)
        {
            if (list1.Count != list2.Count) return false;
            for (int i = 0; i < list1.Count; i++)
            {
                if (list1[i] != list2[i]) return false;
            }
            return true;
        }

        private void SyncSettingsToUI()
        {
            if (_isSyncing) return;
            _isSyncing = true;

            try
            {
                var service = App.SearchService;
                
                // 1. Stats and Status
                StatsFilesText.Text = $"{service.TotalFiles} files";
                StatsVectorsText.Text = $"{service.TotalVectors:N0} vectors";
                
                if (service.IsDaemonOnline)
                {
                    StatsWatchdogText.Text = "Active Monitoring";
                    StatsWatchdogText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 199, 89));
                }
                else
                {
                    StatsWatchdogText.Text = "Offline";
                    StatsWatchdogText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 59, 48));
                }

                // 2. Model Selection
                if (service.ActiveModel == "BGE-Small-EN-v1.5")
                {
                    BgeModelRadio.IsChecked = true;
                }
                else if (service.ActiveModel == "Nomic-Embed-Text-v1.5")
                {
                    NomicModelRadio.IsChecked = true;
                }

                // Model Downloaded & Loaded status sync
                bool isBgeDownloaded = service.DownloadedModels.Contains("BGE-Small-EN-v1.5");
                bool isBgeLoaded = service.LoadedModels.Contains("BGE-Small-EN-v1.5");
                
                if (isBgeLoaded)
                {
                    BgeStatusText.Text = "Active (RAM)";
                    BgeStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 199, 89));
                    BgeStatusText.Visibility = Visibility.Visible;
                    BgeDownloadButton.Visibility = Visibility.Collapsed;
                    BgeMemoryButton.Content = "Unload";
                    BgeMemoryButton.Visibility = Visibility.Visible;
                    BgeMemoryButton.IsEnabled = !service.IsLoadingModel;
                }
                else if (isBgeDownloaded)
                {
                    BgeStatusText.Text = "Downloaded";
                    BgeStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128));
                    BgeStatusText.Visibility = Visibility.Visible;
                    BgeDownloadButton.Visibility = Visibility.Collapsed;
                    BgeMemoryButton.Content = "Load";
                    BgeMemoryButton.Visibility = Visibility.Visible;
                    BgeMemoryButton.IsEnabled = !service.IsLoadingModel;
                }
                else
                {
                    BgeStatusText.Text = "Not Downloaded";
                    BgeStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128));
                    BgeStatusText.Visibility = Visibility.Visible;
                    BgeDownloadButton.Visibility = Visibility.Visible;
                    BgeMemoryButton.Visibility = Visibility.Collapsed;
                }

                bool isNomicDownloaded = service.DownloadedModels.Contains("Nomic-Embed-Text-v1.5");
                bool isNomicLoaded = service.LoadedModels.Contains("Nomic-Embed-Text-v1.5");
                
                if (isNomicLoaded)
                {
                    NomicStatusText.Text = "Active (RAM)";
                    NomicStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 199, 89));
                    NomicStatusText.Visibility = Visibility.Visible;
                    NomicDownloadButton.Visibility = Visibility.Collapsed;
                    NomicMemoryButton.Content = "Unload";
                    NomicMemoryButton.Visibility = Visibility.Visible;
                    NomicMemoryButton.IsEnabled = !service.IsLoadingModel;
                }
                else if (isNomicDownloaded)
                {
                    NomicStatusText.Text = "Downloaded";
                    NomicStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128));
                    NomicStatusText.Visibility = Visibility.Visible;
                    NomicDownloadButton.Visibility = Visibility.Collapsed;
                    NomicMemoryButton.Content = "Load";
                    NomicMemoryButton.Visibility = Visibility.Visible;
                    NomicMemoryButton.IsEnabled = !service.IsLoadingModel;
                }
                else
                {
                    NomicStatusText.Text = "Not Downloaded";
                    NomicStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128));
                    NomicStatusText.Visibility = Visibility.Visible;
                    NomicDownloadButton.Visibility = Visibility.Visible;
                    NomicMemoryButton.Visibility = Visibility.Collapsed;
                }

                DbDirText.Text = string.IsNullOrEmpty(service.DbDir) ? "Not loaded" : service.DbDir;

                // Theme Sync
                if (App.Current is App app && ThemeComboBox != null)
                {
                    var currentTheme = app.GetCurrentTheme();
                    int selectIndex = 0; // Default
                    if (currentTheme == ElementTheme.Light) selectIndex = 1;
                    else if (currentTheme == ElementTheme.Dark) selectIndex = 2;

                    if (ThemeComboBox.SelectedIndex != selectIndex)
                    {
                        ThemeComboBox.SelectedIndex = selectIndex;
                    }
                }

                // DevLogs Sync
                if (DevLogsToggle != null)
                {
                    DevLogsToggle.IsOn = App.IsDevLogsEnabled;
                }

                // 3. Watched Folders ListView (Avoid reset if structural content matches)
                var currentItems = FoldersListView.ItemsSource as List<string>;
                if (currentItems == null || !AreListsEqual(currentItems, service.WatchedFolders))
                {
                    FoldersListView.ItemsSource = null;
                    FoldersListView.ItemsSource = service.WatchedFolders;
                }

                // 4. Exclusions and Extensions TextBoxes (only update if not currently focused to avoid typing interruptions)
                var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(this.XamlRoot);
                if (!ReferenceEquals(focusedElement, ExclusionsTextBox))
                {
                    ExclusionsTextBox.Text = string.Join(", ", service.ExcludedDirs);
                }
                if (!ReferenceEquals(focusedElement, ExtensionsTextBox))
                {
                    ExtensionsTextBox.Text = string.Join(", ", service.IncludedExtensions);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsView] SyncSettingsToUI failed: {ex.Message}");
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private async void ModelRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isSyncing) return;
            if (NomicModelRadio == null || BgeModelRadio == null) return;
            
            string selectedModel = "BGE-Small-EN-v1.5";
            if (NomicModelRadio.IsChecked == true)
            {
                selectedModel = "Nomic-Embed-Text-v1.5";
            }

            await SaveSettingsAsync(selectedModel);
        }

        private async void Rules_Changed(object sender, RoutedEventArgs e)
        {
            if (_isSyncing) return;
            if (ExclusionsTextBox == null || ExtensionsTextBox == null) return;
            await SaveSettingsAsync();
        }

        private async Task SaveSettingsAsync(string? forceModel = null)
        {
            if (NomicModelRadio == null || ExclusionsTextBox == null || ExtensionsTextBox == null) return;
            
            var service = App.SearchService;
            
            string model = forceModel ?? (NomicModelRadio.IsChecked == true ? "Nomic-Embed-Text-v1.5" : "BGE-Small-EN-v1.5");
            
            // Parse excluded dirs list
            var exclusions = new List<string>();
            foreach (var part in ExclusionsTextBox.Text.Split(','))
            {
                string cleaned = part.Trim();
                if (!string.IsNullOrEmpty(cleaned))
                {
                    exclusions.Add(cleaned);
                }
            }

            // Parse extensions list
            var extensions = new List<string>();
            foreach (var part in ExtensionsTextBox.Text.Split(','))
            {
                string cleaned = part.Trim();
                if (!string.IsNullOrEmpty(cleaned))
                {
                    extensions.Add(cleaned);
                }
            }

            await service.UpdateSettingsAsync(model, exclusions, extensions);
        }

        private async void AddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            string? pickedPath = await DialogService.PickFolderAsync();
            if (!string.IsNullOrEmpty(pickedPath))
            {
                AddFolderButton.IsEnabled = false;
                try
                {
                    await App.SearchService.IndexFolderAsync(pickedPath);
                    // Force immediate status refresh to reflect the changes in watched list immediately
                    await App.SearchService.RefreshStatusAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SettingsView] Failed to add watched folder: {ex.Message}");
                }
                finally
                {
                    AddFolderButton.IsEnabled = true;
                }
            }
        }

        private async void RemoveFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is string path)
            {
                button.IsEnabled = false;
                try
                {
                    await App.SearchService.RemoveFolderAsync(path);
                    // Force immediate status refresh to reflect the changes in watched list immediately
                    await App.SearchService.RefreshStatusAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SettingsView] Failed to remove watched folder: {ex.Message}");
                }
                finally
                {
                    button.IsEnabled = true;
                }
            }
        }

        private async void DownloadBgeButton_Click(object sender, RoutedEventArgs e)
        {
            BgeDownloadButton.IsEnabled = false;
            try
            {
                await App.SearchService.DownloadModelAsync("BGE-Small-EN-v1.5");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsView] Failed to download BGE: {ex.Message}");
            }
            finally
            {
                BgeDownloadButton.IsEnabled = true;
            }
        }

        private async void DownloadNomicButton_Click(object sender, RoutedEventArgs e)
        {
            NomicDownloadButton.IsEnabled = false;
            try
            {
                await App.SearchService.DownloadModelAsync("Nomic-Embed-Text-v1.5");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsView] Failed to download Nomic: {ex.Message}");
            }
            finally
            {
                NomicDownloadButton.IsEnabled = true;
            }
        }

        private async void BgeMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            var service = App.SearchService;
            bool isBgeLoaded = service.LoadedModels.Contains("BGE-Small-EN-v1.5");
            BgeMemoryButton.IsEnabled = false;
            try
            {
                if (isBgeLoaded)
                {
                    await service.UnloadModelFromMemoryAsync("BGE-Small-EN-v1.5");
                }
                else
                {
                    await service.LoadModelIntoMemoryAsync("BGE-Small-EN-v1.5");
                }
                await service.RefreshStatusAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsView] BgeMemoryButton_Click failed: {ex.Message}");
            }
            finally
            {
                BgeMemoryButton.IsEnabled = true;
            }
        }

        private async void NomicMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            var service = App.SearchService;
            bool isNomicLoaded = service.LoadedModels.Contains("Nomic-Embed-Text-v1.5");
            NomicMemoryButton.IsEnabled = false;
            try
            {
                if (isNomicLoaded)
                {
                    await service.UnloadModelFromMemoryAsync("Nomic-Embed-Text-v1.5");
                }
                else
                {
                    await service.LoadModelIntoMemoryAsync("Nomic-Embed-Text-v1.5");
                }
                await service.RefreshStatusAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsView] NomicMemoryButton_Click failed: {ex.Message}");
            }
            finally
            {
                NomicMemoryButton.IsEnabled = true;
            }
        }

        private void OpenDbDirButton_Click(object sender, RoutedEventArgs e)
        {
            string dbDir = App.SearchService.DbDir;
            if (!string.IsNullOrEmpty(dbDir) && System.IO.Directory.Exists(dbDir))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{dbDir}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SettingsView] Failed to open folder: {ex.Message}");
                }
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncing) return;
            if (ThemeComboBox == null) return;

            if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string tag)
            {
                if (Enum.TryParse(tag, out ElementTheme theme))
                {
                    if (App.Current is App app)
                    {
                        app.ApplyTheme(theme);
                    }
                }
            }
        }

        private void DevLogsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isSyncing) return;
            if (DevLogsToggle == null) return;
            App.IsDevLogsEnabled = DevLogsToggle.IsOn;
        }

        private async void DeleteVectorsButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog confirmDialog = new ContentDialog
            {
                Title = "Delete Indexed Vectors?",
                Content = "This will completely erase all local vector databases and reset all watched folder configurations. This action is permanent.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await confirmDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                DeleteVectorsButton.IsEnabled = false;
                try
                {
                    bool success = await App.SearchService.DeleteAllVectorsAsync();
                    if (success)
                    {
                        ContentDialog successDialog = new ContentDialog
                        {
                            Title = "Database Reset Complete",
                            Content = "All vectors have been successfully deleted, and watched folders have been cleared.",
                            CloseButtonText = "OK",
                            XamlRoot = this.XamlRoot
                        };
                        await successDialog.ShowAsync();
                    }
                    else
                    {
                        ContentDialog errorDialog = new ContentDialog
                        {
                            Title = "Reset Failed",
                            Content = "An error occurred while resetting the vector database. Please see the diagnostic logs.",
                            CloseButtonText = "OK",
                            XamlRoot = this.XamlRoot
                        };
                        await errorDialog.ShowAsync();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SettingsView] DeleteVectorsButton_Click failed: {ex.Message}");
                }
                finally
                {
                    DeleteVectorsButton.IsEnabled = true;
                }
            }
        }
    }
}
