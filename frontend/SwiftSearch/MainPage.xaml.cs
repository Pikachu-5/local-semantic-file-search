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
            UpdateStatusUI();
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
            switch (tag)
            {
                case "Search":
                    MainNavView.Header = "Search Dashboard";
                    ContentFrame.Navigate(typeof(Views.SearchView));
                    break;
                case "Settings":
                    MainNavView.Header = "Settings Manager";
                    ContentFrame.Navigate(typeof(Views.SettingsView));
                    break;
            }
        }
    }
}
