using Avalonia.Threading;
using uTracerProManager.Core.Models;
using uTracerProManager.Core.Services;
using uTracerProManager.Services;

namespace uTracerProManager.AvaloniaApp.Views;

public sealed partial class MainWindow
{
    private readonly ReferenceMeasurementMetricsService _referenceMetricsV2 = new();
    private readonly List<ReferenceMeasurementPoint> _liveReferencePointsV2 = new();
    private readonly List<ReferenceMeasurementResult> _keptReferenceResultsV2 = new();
    private ReferenceCurveSessionService? _referenceCurveSessionV2;
    private DateTime _lastLiveRefreshUtcV2 = DateTime.MinValue;
    private bool _referenceCurveHooksInitialized;

    private void InitializeReferenceCurveUiHooks()
    {
        if (_referenceCurveHooksInitialized)
            return;
        _referenceCurveHooksInitialized = true;
        ReferenceMeasurementSampleBus.SessionStarted += OnReferenceSessionStartedV2;
        ReferenceMeasurementSampleBus.SampleReceived += OnReferenceSampleReceivedV2;
        ReferenceMeasurementSampleBus.SessionCompleted += OnReferenceSessionCompletedV2;
        ReferencePlot.DoubleTapped += ReferencePlotOnDoubleTappedV2;
    }

    private void DisposeReferenceCurveUiHooks()
    {
        if (!_referenceCurveHooksInitialized)
            return;
        _referenceCurveHooksInitialized = false;
        ReferenceMeasurementSampleBus.SessionStarted -= OnReferenceSessionStartedV2;
        ReferenceMeasurementSampleBus.SampleReceived -= OnReferenceSampleReceivedV2;
        ReferenceMeasurementSampleBus.SessionCompleted -= OnReferenceSessionCompletedV2;
        ReferencePlot.DoubleTapped -= ReferencePlotOnDoubleTappedV2;
    }

    private async Task InitializeReferenceCurveDatabaseAsync()
    {
        try
        {
            _referenceCurveSessionV2 = new ReferenceCurveSessionService(_viewModel.ActiveCatalogPath);
            var migration = await _referenceCurveSessionV2.InitializeAsync();
            _viewModel.ReferenceMeasurement.ComparisonSummary =
                $"Krzywe CP78: {migration.LegacyPoints} udokumentowanych pkt / {migration.LegacySetsWithPoints} serii • " +
                $"migracja v2: +{migration.AddedPoints} pkt / +{migration.AddedSeries} serii • " +
                $"już obecne: {migration.SkippedExistingSeries}.";
        }
        catch (Exception ex)
        {
            _viewModel.ReferenceMeasurement.ComparisonSummary = "Krzywe v2: błąd inicjalizacji — " + ex.Message;
        }
    }

    private void ReferencePlotOnDoubleTappedV2(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        var settings = _viewModel.ReferenceMeasurement;
        settings.LivePlotPaused = !settings.LivePlotPaused;
        settings.Status = settings.LivePlotPaused
            ? "Live plot wstrzymany. Pomiar nadal trwa. Dwuklik wykresu wznawia rysowanie."
            : "Live plot wznowiony.";
        if (!settings.LivePlotPaused)
            RenderLiveReferencePlotV2(force: true);
    }

    private void OnReferenceSessionStartedV2(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var settings = _viewModel.ReferenceMeasurement;
            settings.LivePlotPaused = false;
            if (settings.KeepPlot && _viewModel.LastReferenceMeasurement is { } previous &&
                !_keptReferenceResultsV2.Any(item => item.StartedAt == previous.StartedAt))
                _keptReferenceResultsV2.Add(previous);
            if (!settings.KeepPlot)
                _keptReferenceResultsV2.Clear();

            _liveReferencePointsV2.Clear();
            _lastLiveRefreshUtcV2 = DateTime.MinValue;
            settings.Status = "Skan trwa — live plot ~150 ms. Dwuklik wykresu pauzuje tylko rysowanie, nie pomiar.";
            RenderLiveReferencePlotV2(force: true);
        });
    }

    private void OnReferenceSampleReceivedV2(object? sender, ReferenceMeasurementPoint point)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _liveReferencePointsV2.Add(point);
            RenderLiveReferencePlotV2(force: false);
        });
    }

    private void OnReferenceSessionCompletedV2(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => RenderLiveReferencePlotV2(force: true));

    private void RenderLiveReferencePlotV2(bool force)
    {
        var settings = _viewModel.ReferenceMeasurement;
        if (settings.LivePlotPaused)
            return;
        if (!force && (DateTime.UtcNow - _lastLiveRefreshUtcV2).TotalMilliseconds < 150)
            return;
        _lastLiveRefreshUtcV2 = DateTime.UtcNow;

        ReferencePlot.Plot.Clear();
        foreach (var kept in _keptReferenceResultsV2)
            DrawMeasurementResultV2(kept, "KEEP");

        if (_liveReferencePointsV2.Count > 0)
            DrawLivePointsV2(_liveReferencePointsV2, _viewModel.ReferenceMeasurement.SelectedMeasurement);

        ConfigureReferenceAxesV2(_viewModel.ReferenceMeasurement.SelectedMeasurement.XAxisLabel);
        ReferencePlot.Refresh();
    }

    private async void OnReferenceMeasurementCompletedV2(object? sender, ReferenceMeasurementResult result)
    {
        var settings = _viewModel.ReferenceMeasurement;
        ReferencePlot.Plot.Clear();
        foreach (var kept in _keptReferenceResultsV2)
            DrawMeasurementResultV2(kept, "KEEP");
        DrawMeasurementResultV2(result, result.Emulator ? "EMULATOR" : "POMIAR");

        settings.ComparisonSummary = "Zapisywanie krzywej i wyszukiwanie referencji katalogowej…";
        if (_referenceCurveSessionV2 is not null)
        {
            try
            {
                var session = await _referenceCurveSessionV2.SaveAndCompareAsync(result);
                var assessment = session.Assessment;
                settings.ComparisonSummary =
                    $"{assessment.Label} • Ia RMS {assessment.IaRmsPercent:F1}% • MAE {assessment.IaMaePercent:F1}% • " +
                    $"odrzucone {assessment.RejectedPercent:F1}% • {assessment.Explanation}";
                settings.Status = settings.ComparisonSummary;

                if (settings.ShowCatalogReference && session.BestReference is { } reference)
                    DrawCatalogReferenceV2(reference);
                else if (settings.ShowCatalogReference && session.CatalogCandidates.Count > 0)
                    DrawDocumentedCatalogPointsV2(session.CatalogCandidates);
            }
            catch (Exception ex)
            {
                settings.ComparisonSummary = "Porównanie katalogowe nie powiodło się: " + ex.Message;
                settings.Status = settings.ComparisonSummary;
            }
        }

        ConfigureReferenceAxesV2(result.Definition.XAxisLabel);
        ReferencePlot.Plot.Title(string.IsNullOrWhiteSpace(settings.PlotTitle)
            ? result.Profile.DisplayName
            : settings.PlotTitle + (result.Emulator ? " — EMULATOR" : string.Empty));
        ReferencePlot.Plot.ShowLegend();
        ReferencePlot.Plot.Grid.IsVisible = settings.ShowGrid;
        if (settings.ManualScale && settings.XMaximum > settings.XMinimum && settings.YMaximum > settings.YMinimum)
        {
            ReferencePlot.Plot.Axes.Bottom.Min = settings.XMinimum;
            ReferencePlot.Plot.Axes.Bottom.Max = settings.XMaximum;
            ReferencePlot.Plot.Axes.Left.Min = settings.YMinimum;
            ReferencePlot.Plot.Axes.Left.Max = settings.YMaximum;
        }
        else
        {
            ReferencePlot.Plot.Axes.AutoScale();
        }
        ReferencePlot.Refresh();
        _liveReferencePointsV2.Clear();
    }

    private void DrawMeasurementResultV2(ReferenceMeasurementResult result, string sourceLabel)
    {
        var metrics = _referenceMetricsV2.Calculate(result);
        foreach (var group in result.Points.GroupBy(point => point.CurveIndex).OrderBy(group => group.Key))
            DrawMeasurementGroupV2(group.OrderBy(point => point.XValue).ToArray(), result.Definition, metrics, sourceLabel);
    }

    private void DrawLivePointsV2(IReadOnlyList<ReferenceMeasurementPoint> points, ReferenceMeasurementDefinition definition)
    {
        var metrics = _referenceMetricsV2.Calculate(definition.Kind, points);
        foreach (var group in points.GroupBy(point => point.CurveIndex).OrderBy(group => group.Key))
            DrawMeasurementGroupV2(group.OrderBy(point => point.XValue).ToArray(), definition, metrics, "LIVE");
    }

    private void DrawMeasurementGroupV2(
        ReferenceMeasurementPoint[] points,
        ReferenceMeasurementDefinition definition,
        IReadOnlyDictionary<int, ReferencePointMetrics> metrics,
        string sourceLabel)
    {
        if (points.Length == 0)
            return;

        var settings = _viewModel.ReferenceMeasurement;
        var xs = points.Select(point => point.XValue).ToArray();
        var step = points[0].StepValue;

        if (!string.Equals(settings.Y1Variable, "Brak", StringComparison.Ordinal))
        {
            var y = SelectReferenceValuesV2(points, metrics, settings.Y1Variable);
            var curve = ReferencePlot.Plot.Add.Scatter(xs, y);
            curve.LegendText = $"{sourceLabel} {settings.Y1Variable} • {definition.SteppingLabel}={step:F3}";
            curve.Axes.YAxis = ReferencePlot.Plot.Axes.Left;
            ApplyReferenceStyle(curve, settings.LineStyle, settings.UseColor);
        }

        if (!string.Equals(settings.Y2Variable, "Brak", StringComparison.Ordinal) &&
            !string.Equals(settings.Y2Variable, settings.Y1Variable, StringComparison.Ordinal))
        {
            var y = SelectReferenceValuesV2(points, metrics, settings.Y2Variable);
            var curve = ReferencePlot.Plot.Add.Scatter(xs, y);
            curve.LegendText = $"{sourceLabel} {settings.Y2Variable} • {definition.SteppingLabel}={step:F3}";
            curve.Axes.YAxis = ReferencePlot.Plot.Axes.Right;
            ApplyReferenceStyle(curve, settings.LineStyle, settings.UseColor);
        }
    }

    private void DrawCatalogReferenceV2(ReferenceCurveSeries reference)
    {
        foreach (var group in reference.Points.GroupBy(point => point.SeriesKey))
        {
            var points = group.OrderBy(point => point.VaMeasured > 0 ? point.VaMeasured : point.VaSet).ToArray();
            if (points.Length < 2)
                continue;
            var xs = points.Select(point => point.VaMeasured > 0 ? point.VaMeasured : point.VaSet).ToArray();
            var curve = ReferencePlot.Plot.Add.Scatter(xs, points.Select(point => point.Ia).ToArray());
            curve.LegendText = $"KATALOG Ia • {group.Key} • {reference.SourceTitle}";
            curve.LineWidth = 2;
            curve.MarkerSize = 0;
            curve.LinePattern = ScottPlot.LinePattern.DenselyDashed;
            curve.Axes.YAxis = ReferencePlot.Plot.Axes.Left;
        }
    }

    private void DrawDocumentedCatalogPointsV2(IReadOnlyList<ReferenceCurveSeries> candidates)
    {
        foreach (var series in candidates.Where(item => item.Points.Count == 1).Take(20))
        {
            var point = series.Points[0];
            var x = point.VaMeasured > 0 ? point.VaMeasured : point.VaSet;
            var marker = ReferencePlot.Plot.Add.Scatter(new[] { x }, new[] { point.Ia });
            marker.LegendText = $"KATALOG PUNKT • {series.SourceTitle}";
            marker.LineWidth = 0;
            marker.MarkerSize = 7;
            marker.Axes.YAxis = ReferencePlot.Plot.Axes.Left;
        }
    }

    private static double[] SelectReferenceValuesV2(
        ReferenceMeasurementPoint[] points,
        IReadOnlyDictionary<int, ReferencePointMetrics> metrics,
        string variable) => variable switch
        {
            "Is [mA]" => points.Select(point => point.ScreenCurrentMa).ToArray(),
            "gm [mA/V]" => points.Select(point => metrics.GetValueOrDefault(point.Sequence)?.GmMaV ?? double.NaN).ToArray(),
            "Rp [kΩ]" => points.Select(point => metrics.GetValueOrDefault(point.Sequence)?.RpKohm ?? double.NaN).ToArray(),
            "μ" => points.Select(point => metrics.GetValueOrDefault(point.Sequence)?.Mu ?? double.NaN).ToArray(),
            _ => points.Select(point => point.AnodeCurrentMa).ToArray()
        };

    private void ConfigureReferenceAxesV2(string xLabel)
    {
        var settings = _viewModel.ReferenceMeasurement;
        ReferencePlot.Plot.XLabel(xLabel);
        ReferencePlot.Plot.Axes.Left.Label.Text = settings.Y1Variable;
        ReferencePlot.Plot.Axes.Right.Label.Text = settings.Y2Variable;
        ReferencePlot.Plot.Axes.Right.IsVisible =
            !string.Equals(settings.Y2Variable, "Brak", StringComparison.Ordinal) &&
            !string.Equals(settings.Y2Variable, settings.Y1Variable, StringComparison.Ordinal);
        ReferencePlot.Plot.Grid.IsVisible = settings.ShowGrid;
        ReferencePlot.Plot.ShowLegend();
        if (!settings.ManualScale)
            ReferencePlot.Plot.Axes.AutoScale();
    }
}
