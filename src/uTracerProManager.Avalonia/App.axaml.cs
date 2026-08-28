using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using uTracerProManager.AvaloniaApp.ViewModels;
using uTracerProManager.AvaloniaApp.Views;
using uTracerProManager.Services;

namespace uTracerProManager.AvaloniaApp;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var bundledCatalog = Path.Combine(AppContext.BaseDirectory, "Data", "tube_measurements.db");
                StartupDiagnostics.Write($"Przygotowanie danych; katalog w pakiecie={bundledCatalog}; istnieje={File.Exists(bundledCatalog)}.");
                var paths = ApplicationDataBootstrapper.Prepare(bundledCatalog);
                StartupDiagnostics.Write($"Dane użytkownika przygotowane w {paths.RootDirectory}.");
                var viewModel = new MainWindowViewModel(paths);
                desktop.MainWindow = new MainWindow(viewModel);
                StartupDiagnostics.Write("Okno główne utworzone.");
            }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.ShowFatal("App.OnFrameworkInitializationCompleted", ex);
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown(1);
        }
    }
}
