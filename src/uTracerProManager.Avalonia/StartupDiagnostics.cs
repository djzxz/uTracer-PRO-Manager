using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace uTracerProManager.AvaloniaApp;

internal static class StartupDiagnostics
{
    private static readonly object Sync = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "uTracerProManager",
        "Logs",
        "startup_avalonia.log");

    public static void RegisterGlobalHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Write("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Write("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    public static void Write(string stage, Exception? exception = null)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                using var stream = new FileStream(
                    LogPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {stage}");
                if (exception is not null)
                    writer.WriteLine(exception);
            }
        }
        catch
        {
            // Diagnostyka nie może wywołać kolejnej awarii programu.
        }
    }

    public static void WriteEnvironment()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "nieznana";
        Write($"Start v{version}; OS={RuntimeInformation.OSDescription}; arch={RuntimeInformation.ProcessArchitecture}; base={AppContext.BaseDirectory}");
    }

    public static void ShowFatal(string stage, Exception exception)
    {
        Write(stage, exception);
        var message =
            "uTracer PRO Manager nie może się uruchomić.\n\n" +
            exception.GetType().Name + ": " + exception.Message + "\n\n" +
            "Pełny raport zapisano tutaj:\n" + LogPath;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                _ = MessageBoxW(IntPtr.Zero, message, "uTracer PRO Manager — błąd uruchamiania", 0x10);
                return;
            }
            catch
            {
                // Pozostaje stderr, przydatny również przy uruchomieniu z konsoli.
            }
        }

        Console.Error.WriteLine(message);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
