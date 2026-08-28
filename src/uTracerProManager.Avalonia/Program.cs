using Avalonia;

namespace uTracerProManager.AvaloniaApp;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        StartupDiagnostics.RegisterGlobalHandlers();
        StartupDiagnostics.WriteEnvironment();
        try
        {
            using var guard = new Mutex(initiallyOwned: true, "Local\\uTracerProManager.Avalonia.SingleInstance", out var firstInstance);
            if (!firstInstance)
            {
                StartupDiagnostics.Write("Zakończono start: druga kopia programu jest już uruchomiona.");
                return 0;
            }

            StartupDiagnostics.Write("Uruchamianie środowiska Avalonia w programowym trybie renderowania.");
            var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            StartupDiagnostics.Write("Prawidłowe zamknięcie aplikacji.");
            return exitCode;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.ShowFatal("Program.Main", ex);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode = new[] { Win32RenderingMode.Software }
            })
            .WithInterFont()
            .LogToTrace();
}
