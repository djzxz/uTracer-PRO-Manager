using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Globalization;
using System.Text.Json;
using uTracerProManager.AvaloniaApp.ViewModels;
using uTracerProManager.Core.Models;

namespace uTracerProManager.AvaloniaApp.Views;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
        _viewModel.TestCompleted += OnTestCompleted;
        _viewModel.ReferenceMeasurementCompleted += OnReferenceMeasurementCompleted;
        ConfigurePlot();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        FitToScreen();
        LoadLayoutSettings();
        await _viewModel.InitializeAsync();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        SaveLayoutSettings();
        _viewModel.TestCompleted -= OnTestCompleted;
        _viewModel.ReferenceMeasurementCompleted -= OnReferenceMeasurementCompleted;
        await _viewModel.DisposeAsync();
    }

    private void ConfigurePlot()
    {
        LivePlot.Plot.Title("Charakterystyki Ia / Is / gm");
        LivePlot.Plot.XLabel("Napięcie anodowe Va [V]");
        LivePlot.Plot.YLabel("Prąd [mA]");
        LivePlot.Plot.Axes.SetLimits(0, 400, 0, 100);
        LivePlot.Refresh();

        ReferencePlot.Plot.Title("Panel referencyjny — wybierz rodzaj pomiaru");
        ReferencePlot.Plot.XLabel("Zmienna skanowana");
        ReferencePlot.Plot.YLabel("Prąd [mA]");
        ReferencePlot.Plot.Axes.SetLimits(0, 400, 0, 100);
        ReferencePlot.Refresh();
    }

    private void OnReferenceMeasurementCompleted(object? sender, ReferenceMeasurementResult result)
    {
        var settings = _viewModel.ReferenceMeasurement;
        if (!settings.KeepPlot)
            ReferencePlot.Plot.Clear();

        foreach (var group in result.Points.GroupBy(point => point.CurveIndex).OrderBy(group => group.Key))
        {
            var points = group.OrderBy(point => point.XValue).ToArray();
            var step = points[0].StepValue;
            if (!string.Equals(settings.Y1Variable, "Brak", StringComparison.Ordinal))
            {
                var y = string.Equals(settings.Y1Variable, "Is [mA]", StringComparison.Ordinal)
                    ? points.Select(point => point.ScreenCurrentMa).ToArray()
                    : points.Select(point => point.AnodeCurrentMa).ToArray();
                var curve = ReferencePlot.Plot.Add.Scatter(points.Select(point => point.XValue).ToArray(), y);
                curve.LegendText = $"{settings.Y1Variable} • {result.Definition.SteppingLabel}={step:F3}";
                ApplyReferenceStyle(curve, settings.LineStyle, settings.UseColor);
            }

            if (!string.Equals(settings.Y2Variable, "Brak", StringComparison.Ordinal) &&
                !string.Equals(settings.Y2Variable, settings.Y1Variable, StringComparison.Ordinal))
            {
                var y = string.Equals(settings.Y2Variable, "Ia [mA]", StringComparison.Ordinal)
                    ? points.Select(point => point.AnodeCurrentMa).ToArray()
                    : points.Select(point => point.ScreenCurrentMa).ToArray();
                var curve = ReferencePlot.Plot.Add.Scatter(points.Select(point => point.XValue).ToArray(), y);
                curve.LegendText = $"{settings.Y2Variable} • {result.Definition.SteppingLabel}={step:F3}";
                ApplyReferenceStyle(curve, settings.LineStyle, settings.UseColor);
            }
        }

        ReferencePlot.Plot.Title(string.IsNullOrWhiteSpace(settings.PlotTitle)
            ? result.Profile.DisplayName
            : settings.PlotTitle + (result.Emulator ? " — EMULATOR" : string.Empty));
        ReferencePlot.Plot.XLabel(result.Definition.XAxisLabel);
        ReferencePlot.Plot.YLabel("Prąd [mA]");
        ReferencePlot.Plot.ShowLegend();
        ReferencePlot.Plot.Grid.IsVisible = settings.ShowGrid;
        if (settings.ManualScale && settings.XMaximum > settings.XMinimum && settings.YMaximum > settings.YMinimum)
            ReferencePlot.Plot.Axes.SetLimits(settings.XMinimum, settings.XMaximum, settings.YMinimum, settings.YMaximum);
        else
            ReferencePlot.Plot.Axes.AutoScale();
        ReferencePlot.Refresh();
    }

    private static void ApplyReferenceStyle(ScottPlot.Plottables.Scatter curve, string style, bool useColor)
    {
        if (!useColor)
            curve.Color = ScottPlot.Colors.Black;
        if (string.Equals(style, "Punkty", StringComparison.Ordinal))
        {
            curve.LineWidth = 0;
            curve.MarkerSize = 5;
        }
        else if (string.Equals(style, "Linie", StringComparison.Ordinal))
        {
            curve.LineWidth = 2;
            curve.MarkerSize = 0;
        }
        else
        {
            curve.LineWidth = 2;
            curve.MarkerSize = 4;
        }
    }

    private void ClearReferencePlot_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ReferencePlot.Plot.Clear();
        ReferencePlot.Plot.Title("Panel referencyjny — wybierz rodzaj pomiaru");
        ReferencePlot.Plot.XLabel("Zmienna skanowana");
        ReferencePlot.Plot.YLabel("Prąd [mA]");
        ReferencePlot.Refresh();
    }

    private async void SaveReferenceData_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var result = _viewModel.LastReferenceMeasurement;
        if (result is null)
        {
            _viewModel.ReferenceMeasurement.Status = "Najpierw zakończ pomiar krzywych.";
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Zapisz punkty pomiarowe",
            SuggestedFileName = $"uTracer_krzywe_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            DefaultExtension = "csv",
            FileTypeChoices =
            [
                new FilePickerFileType("Dane CSV") { Patterns = ["*.csv"] }
            ]
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        var lines = new List<string>
        {
            "sequence;curve;step;x;Va_command;Va_measured;Vs_command;Vs_measured;Vg;Vh;Ia_mA;Is_mA;status"
        };
        lines.AddRange(result.Points.Select(point => string.Join(";",
            point.Sequence,
            point.CurveIndex,
            Format(point.StepValue),
            Format(point.XValue),
            Format(point.CommandedVa),
            Format(point.MeasuredVa),
            Format(point.CommandedVs),
            Format(point.MeasuredVs),
            Format(point.CommandedVg),
            Format(point.HeaterVoltage),
            Format(point.AnodeCurrentMa),
            Format(point.ScreenCurrentMa),
            point.Status.Replace(';', ','))));
        await File.WriteAllLinesAsync(path, lines, System.Text.Encoding.UTF8);
        _viewModel.ReferenceMeasurement.Status = $"Zapisano dane: {path}";
    }

    private async void SaveReferencePlot_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel.LastReferenceMeasurement is null)
        {
            _viewModel.ReferenceMeasurement.Status = "Najpierw zakończ pomiar krzywych.";
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Zapisz wykres charakterystyk",
            SuggestedFileName = $"uTracer_wykres_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            DefaultExtension = "png",
            FileTypeChoices =
            [
                new FilePickerFileType("Obraz PNG") { Patterns = ["*.png"] }
            ]
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;
        ReferencePlot.Plot.SavePng(path, 1600, 1000);
        _viewModel.ReferenceMeasurement.Status = $"Zapisano wykres: {path}";
    }

    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private void OnTestCompleted(object? sender, FullTestResult result)
    {
        LivePlot.Plot.Clear();

        if (result.DiagnosticCurvePoints is { Count: > 0 })
        {
            foreach (var group in result.DiagnosticCurvePoints
                         .GroupBy(point => point.GridVoltage)
                         .OrderBy(group => group.Key))
            {
                var points = group.OrderBy(point => point.MeasuredAnodeVoltageA).ToArray();
                var curve = LivePlot.Plot.Add.Scatter(
                    points.Select(point => point.MeasuredAnodeVoltageA).ToArray(),
                    points.Select(point => point.AnodeCurrentAMa).ToArray());
                curve.LegendText = $"Vg {group.Key:F2} V";
                curve.LineWidth = 2;
            }

            LivePlot.Plot.Title("Charakterystyki anodowe");
            LivePlot.Plot.XLabel("Napięcie anodowe Va [V]");
            LivePlot.Plot.YLabel("Prąd anodowy Ia [mA]");
        }
        else
        {
            var samples = result.Samples.Where(sample => !sample.Conditioning).OrderBy(sample => sample.Sequence).ToArray();
            var series = samples.Select(sample => (double)sample.Sequence).ToArray();
            var sectionA = LivePlot.Plot.Add.Scatter(series, samples.Select(sample => sample.AnodeCurrentMa).ToArray());
            sectionA.LegendText = "Ia — sekcja A";
            sectionA.LineWidth = 2;

            if (result.SectionBStatistics is not null)
            {
                var sectionB = LivePlot.Plot.Add.Scatter(series, samples.Select(sample => sample.ScreenCurrentMa).ToArray());
                sectionB.LegendText = "Ia — sekcja B";
                sectionB.LineWidth = 2;
            }

            LivePlot.Plot.Title("Stabilność kolejnych serii");
            LivePlot.Plot.XLabel("Numer serii");
            LivePlot.Plot.YLabel("Prąd [mA]");
        }

        LivePlot.Plot.ShowLegend();
        LivePlot.Plot.Axes.AutoScale();
        LivePlot.Refresh();
    }

    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;
        var scale = screen.Scaling;
        Width = Math.Min(1440, screen.WorkingArea.Width / scale - 24);
        Height = Math.Min(900, screen.WorkingArea.Height / scale - 24);
        MinWidth = Math.Min(1100, Width);
        MinHeight = Math.Min(700, Height);
    }

    private static string LayoutSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "uTracerProManagerAvalonia",
        "layout.json");

    private void LoadLayoutSettings()
    {
        try
        {
            if (!File.Exists(LayoutSettingsPath))
                return;
            var settings = JsonSerializer.Deserialize<LayoutSettings>(File.ReadAllText(LayoutSettingsPath));
            if (settings is null)
                return;

            var maximumWidth = Width;
            var maximumHeight = Height;
            Width = Math.Clamp(settings.WindowWidth, MinWidth, maximumWidth);
            Height = Math.Clamp(settings.WindowHeight, MinHeight, maximumHeight);
            SetColumnWidth(MeasurementLayout, settings.MeasurementLeftWidth, 430, Math.Max(430, Width - 520));
            SetRowHeight(MeasurementLeftLayout, settings.MeasurementTopHeight, 310, Math.Max(310, Height - 390));
            SetColumnWidth(DatabaseLayout, settings.DatabaseLeftWidth, 360, Math.Max(360, Width - 400));
            SetColumnWidth(ReferenceLayout, settings.ReferenceLeftWidth, 480, Math.Max(480, Width - 480));
        }
        catch
        {
            // Uszkodzony plik układu nie może zablokować uruchomienia programu.
        }
    }

    private void SaveLayoutSettings()
    {
        try
        {
            var settings = new LayoutSettings
            {
                WindowWidth = Width,
                WindowHeight = Height,
                MeasurementLeftWidth = MeasurementLayout.ColumnDefinitions[0].ActualWidth,
                MeasurementTopHeight = MeasurementLeftLayout.RowDefinitions[0].ActualHeight,
                DatabaseLeftWidth = DatabaseLayout.ColumnDefinitions[0].ActualWidth,
                ReferenceLeftWidth = ReferenceLayout.ColumnDefinitions[0].ActualWidth
            };
            var directory = Path.GetDirectoryName(LayoutSettingsPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                LayoutSettingsPath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Zapis układu jest wygodą, nie częścią bezpiecznego toru pomiarowego.
        }
    }

    private static void SetColumnWidth(Grid grid, double value, double minimum, double maximum)
    {
        if (double.IsFinite(value) && value > 0)
            grid.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(value, minimum, maximum));
    }

    private static void SetRowHeight(Grid grid, double value, double minimum, double maximum)
    {
        if (double.IsFinite(value) && value > 0)
            grid.RowDefinitions[0].Height = new GridLength(Math.Clamp(value, minimum, maximum));
    }

    private sealed class LayoutSettings
    {
        public double WindowWidth { get; set; } = 1440;
        public double WindowHeight { get; set; } = 900;
        public double MeasurementLeftWidth { get; set; } = 560;
        public double MeasurementTopHeight { get; set; } = 420;
        public double DatabaseLeftWidth { get; set; } = 700;
        public double ReferenceLeftWidth { get; set; } = 610;
    }

    private async void ImportCalibration_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz plik kalibracji uTracera",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Kalibracja uTracer") { Patterns = new[] { "*.cal", "*.json", "*.txt" } },
                FilePickerFileTypes.All
            }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await _viewModel.ImportCalibrationAsync(path);
    }

    private async void ExportOriginalCalibration_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Zapisz kalibrację dla oryginalnego GUI uTracer",
            SuggestedFileName = "uTracer_3p12p6.cal",
            DefaultExtension = "cal",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Oryginalna kalibracja uTracer") { Patterns = new[] { "*.cal" } }
            }
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await _viewModel.ExportOriginalCalibrationAsync(path);
    }

    private async void ImportOriginalSetup_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz ustawienia oryginalnego programu uTracer",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Ustawienia uTracer") { Patterns = new[] { "*.uts" } },
                FilePickerFileTypes.All
            }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await _viewModel.ImportOriginalSetupAsync(path);
    }

    private async void ExportOriginalSetup_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Zapisz ustawienia dla oryginalnego programu uTracer",
            SuggestedFileName = "uTracer_3p12p6.uts",
            DefaultExtension = "uts",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Ustawienia uTracer") { Patterns = new[] { "*.uts" } }
            }
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await _viewModel.ExportOriginalSetupAsync(path);
    }

    private async void ImportDatabase_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz nową bazę wartości pomiarowych",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Baza SQLite") { Patterns = new[] { "*.db", "*.sqlite", "*.sqlite3" } },
                FilePickerFileTypes.All
            }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await _viewModel.ImportDatabaseAsync(path);
    }

    private async void ExportResult_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz folder raportów",
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await _viewModel.ExportLastResultAsync(path);
    }

    private async void OpenCalibrationWizard_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var wizard = new CalibrationWizardWindow(_viewModel);
        await wizard.ShowDialog(this);
    }
}
