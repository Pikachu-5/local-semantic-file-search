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
            
            // Initial UI sync
            SyncSettingsToUI();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            App.SearchService.PropertyChanged -= SearchService_PropertyChanged;
        }

        private void SearchService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                SyncSettingsToUI();
            });
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

                // 3. Watched Folders ListView
                FoldersListView.ItemsSource = null;
                FoldersListView.ItemsSource = service.WatchedFolders;

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
    }
}
