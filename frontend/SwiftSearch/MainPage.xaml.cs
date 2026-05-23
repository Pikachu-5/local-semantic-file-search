using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace SwiftSearch
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            App.SearchService.PropertyChanged += SearchService_PropertyChanged;
            App.DevLogsSettingChanged += App_DevLogsSettingChanged;
            UpdateStatusUI();
            UpdateSidebarVisibility();
        }

        private void App_DevLogsSettingChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateSidebarVisibility();
            });
        }

        private void UpdateSidebarVisibility()
        {
            if (DiagnosticsItem != null)
            {
                DiagnosticsItem.Visibility = App.IsDevLogsEnabled ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SearchService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(App.SearchService.IsDaemonOnline) || 
                e.PropertyName == nameof(App.SearchService.IsDownloadingModel))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateStatusUI();
                });
            }
        }

        private void UpdateStatusUI()
        {
            var service = App.SearchService;
            if (service.IsDaemonOnline)
            {
                if (service.IsDownloadingModel)
                {
                    // Orange / Yellow
                    StatusGlow.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 149, 0));
                    StatusCore.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 149, 0));
                    StatusText.Text = "Downloading weights...";
                }
                else
                {
                    // Green
                    StatusGlow.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 199, 89));
                    StatusCore.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 199, 89));
                    StatusText.Text = "Daemon: Online";
                }
            }
            else
            {
                // Red
                StatusGlow.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 59, 48));
                StatusCore.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 59, 48));
                StatusText.Text = "Daemon: Offline";
            }
        }

        private void MainNavView_Loaded(object sender, RoutedEventArgs e)
        {
            // Default navigate to Search Dashboard on first load
            if (MainNavView.MenuItems.Count > 0)
            {
                MainNavView.SelectedItem = MainNavView.MenuItems[0];
                NavigateToTab("Search");
            }
        }

        private void MainNavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer != null && args.InvokedItemContainer.Tag is string tag)
            {
                NavigateToTab(tag);
            }
        }

        private void NavigateToTab(string tag)
        {
            MainNavView.Header = null;
            switch (tag)
            {
                case "Search":
                    ContentFrame.Navigate(typeof(Views.SearchView));
                    break;
                case "GlobalSearch":
                    ContentFrame.Navigate(typeof(Views.GlobalSearchView));
                    break;
                case "Settings":
                    ContentFrame.Navigate(typeof(Views.SettingsView));
                    break;
                case "Diagnostics":
                    ContentFrame.Navigate(typeof(Views.DiagnosticsView));
                    break;
            }
        }
    }
}
