using System;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SwiftSearch;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Load and apply stored theme preference on boot
        if (App.Current is App app && this.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = app.GetCurrentTheme();
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Capture and store the window handle globally for WinRT Pickers
        App.MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // Start the backend gRPC daemon asynchronously
        try
        {
            App.SearchService.StartDaemon();
        }
        catch (Exception ex)
        {
            try
            {
                string crashPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
                System.IO.File.WriteAllText(crashPath, $"Time: {DateTime.Now}\nException: {ex}\nStackTrace:\n{ex.StackTrace}");
            }
            catch { }
            throw;
        }

        // Ensure the daemon is gracefully stopped when the window closes
        this.Closed += (s, e) => App.SearchService.StopDaemon();

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }
}
