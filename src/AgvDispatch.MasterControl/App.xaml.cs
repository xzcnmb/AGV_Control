using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AgvDispatch.MasterControl;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogCrash("DispatcherUnhandledException", args.Exception);
            // Allow app to continue if possible, or exit cleanly
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "master_control_crash.log");
            var text = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{source}] {ex}\n\n";
            File.AppendAllText(logPath, text);
        }
        catch
        {
            // Ignore logging errors
        }
    }
}
