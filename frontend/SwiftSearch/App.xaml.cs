using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SwiftSearch;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    public static IntPtr MainWindowHandle { get; set; } = IntPtr.Zero;
    public static Services.ISearchService SearchService { get; } = new Services.SearchService();
    private Window? _window;
    public Window? MainWindow => _window;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        this.UnhandledException += (sender, e) =>
        {
            try
            {
                string crashPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
                System.IO.File.WriteAllText(crashPath, $"UnhandledException at {DateTime.Now}:\n{e.Exception}\nMessage: {e.Message}\nStackTrace:\n{e.Exception?.StackTrace}");
            }
            catch { }
        };
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    public void ApplyTheme(ElementTheme theme)
    {
        if (_window?.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = theme;
        }

        try
        {
            string themePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme_preference.txt");
            System.IO.File.WriteAllText(themePath, theme.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Failed to save theme preference: {ex.Message}");
        }
    }

    public ElementTheme GetCurrentTheme()
    {
        string themePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme_preference.txt");
        if (System.IO.File.Exists(themePath))
        {
            try
            {
                string stored = System.IO.File.ReadAllText(themePath).Trim();
                if (Enum.TryParse(stored, out ElementTheme theme))
                {
                    return theme;
                }
            }
            catch { }
        }
        return ElementTheme.Default;
    }

    public static event EventHandler? DevLogsSettingChanged;

    public static bool IsDevLogsEnabled
    {
        get
        {
            string devLogsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dev_logs_preference.txt");
            if (System.IO.File.Exists(devLogsPath))
            {
                try
                {
                    string stored = System.IO.File.ReadAllText(devLogsPath).Trim();
                    return bool.TryParse(stored, out bool enabled) && enabled;
                }
                catch { }
            }
            return false;
        }
        set
        {
            try
            {
                string devLogsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dev_logs_preference.txt");
                System.IO.File.WriteAllText(devLogsPath, value.ToString());
                DevLogsSettingChanged?.Invoke(null, EventArgs.Empty);
            }
            catch { }
        }
    }
}
