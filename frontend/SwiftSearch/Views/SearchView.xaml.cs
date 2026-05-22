using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SwiftSearch.Views
{
    public sealed partial class SearchView : Page
    {
        public SearchView()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            App.SearchService.PropertyChanged += SearchService_PropertyChanged;
            UpdateOverlayState();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            App.SearchService.PropertyChanged -= SearchService_PropertyChanged;
        }

        private void SearchService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(App.SearchService.IsDownloadingModel) || 
                e.PropertyName == nameof(App.SearchService.IsDaemonOnline))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateOverlayState();
                });
            }
        }

        private void UpdateOverlayState()
        {
            var service = App.SearchService;
            if (!service.IsDaemonOnline)
            {
                OverlayStatusText.Text = "Connecting to Local Search Engine...";
                OverlayPanel.Visibility = Visibility.Visible;
                SearchTextBox.IsEnabled = false;
                SearchButton.IsEnabled = false;
            }
            else if (service.IsDownloadingModel)
            {
                OverlayStatusText.Text = "Downloading Embedding Engine...";
                OverlayPanel.Visibility = Visibility.Visible;
                SearchTextBox.IsEnabled = false;
                SearchButton.IsEnabled = false;
            }
            else
            {
                OverlayPanel.Visibility = Visibility.Collapsed;
                SearchTextBox.IsEnabled = true;
                SearchButton.IsEnabled = true;
            }
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await TriggerSearchAsync();
        }

        private async void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                await TriggerSearchAsync();
            }
        }

        private async Task TriggerSearchAsync()
        {
            string query = SearchTextBox.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            SearchButton.IsEnabled = false;
            SearchTextBox.IsEnabled = false;
            
            OverlayStatusText.Text = "Searching conceptual database...";
            OverlayPanel.Visibility = Visibility.Visible;

            try
            {
                int topK = (int)TopKSlider.Value;
                var results = await App.SearchService.SearchAsync(query, topK);
                ResultsListView.ItemsSource = results;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchView] Search execution failed: {ex.Message}");
            }
            finally
            {
                OverlayPanel.Visibility = Visibility.Collapsed;
                SearchButton.IsEnabled = true;
                SearchTextBox.IsEnabled = true;
            }
        }

        private void ResultsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Models.SearchItem item)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(item.FilePath)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SearchView] Failed to open file {item.FilePath}: {ex.Message}");
                }
            }
        }
    }
}
