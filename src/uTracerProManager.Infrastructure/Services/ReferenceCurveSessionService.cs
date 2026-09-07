using System.Globalization;
using uTracerProManager.Core.Models;
using uTracerProManager.Core.Services;

namespace uTracerProManager.Services;

public sealed record ReferenceCurveSessionResult(
    ReferenceCurveSeries Measurement,
    IReadOnlyList<ReferenceCurveSeries> CatalogCandidates,
    ReferenceCurveSeries? BestReference,
    ReferenceCurveComparison? Comparison,
    ReferenceCurveAssessment Assessment,
    LegacyCurveMigrationResult Migration);

public sealed class ReferenceCurveSessionService
{
    private readonly IReferenceCurveRepository _repository;
    private readonly LegacyReferenceCurveMigrationService _legacyMigration;
    private readonly ReferenceMeasurementMetricsService _metrics = new();
    private readonly ReferenceCurveComparisonService _comparison = new();
    private readonly ReferenceCurveAssessmentService _assessment = new();

    public ReferenceCurveSessionService(string databasePath)
    {
        _repository = new ReferenceCurveRepository(databasePath);
        _legacyMigration = new LegacyReferenceCurveMigrationService(databasePath);
    }

    public async Task<LegacyCurveMigrationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _repository.InitializeAsync(cancellationToken);
        return await _legacyMigration.MigrateAsync(cancellationToken);
    }

    public async Task<ReferenceCurveSessionResult> SaveAndCompareAsync(
        ReferenceMeasurementResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        var migration = await InitializeAsync(cancellationToken);
        var measurement = BuildMeasurementSeries(result);
        await _repository.SaveAsync(measurement, cancellationToken);

        var request = BuildMatchRequest(result);
        var candidates = await _repository.FindMatchingAsync(request, cancellationToken);
        candidates = candidates
            .Where(series => string.Equals(series.SourceKind, "CATALOG", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(series => series.HumanReviewed)
            .ThenByDescending(series => series.Quality)
            .ToArray();

        ReferenceCurveSeries? bestReference = null;
        ReferenceCurveComparison? bestComparison = null;
        ReferenceCurveAssessment? bestAssessment = null;

        foreach (var candidate in candidates.Where(series => series.Points.Count >= 2))
        {
            try
            {
                var comparison = _comparison.Compare(measurement.Points, candidate);
                var assessment = _assessment.Assess(measurement.Points, candidate, comparison);
                if (bestComparison is null || comparison.IaRms < bestComparison.IaRms)
                {
                    bestReference = candidate;
                    bestComparison = comparison;
                    bestAssessment = assessment;
                }
            }
            catch (InvalidOperationException)
            {
                // Kandydat bez wspólnych punktów pozostaje widoczny w bazie, ale nie jest oceniany.
            }
        }

        if (bestAssessment is null)
        {
            var documented = candidates.Count(series => series.Points.Count == 1);
            bestAssessment = ReferenceCurveAssessmentService.NoReference(
                documented > 0
                    ? $"Baza zawiera {documented} udokumentowanych punktów katalogowych, ale nie pełną krzywą do RMS/MAE."
                    : "Brak źródłowej krzywej katalogowej dla tych warunków.");
        }

        return new ReferenceCurveSessionResult(
            measurement,
            candidates,
            bestReference,
            bestComparison,
            bestAssessment,
            migration);
    }

    public ReferenceCurveSeries BuildMeasurementSeries(ReferenceMeasurementResult result)
    {
        var metrics = _metrics.Calculate(result);
        var points = result.Points.Select(point =>
        {
            metrics.TryGetValue(point.Sequence, out var derived);
            return new ReferenceCurveDataPoint(
                point.Sequence,
                SeriesKey(result.Definition, point),
                point.CommandedVa,
                point.MeasuredVa,
                point.CommandedVs,
                point.MeasuredVs,
                point.CommandedVg,
                point.HeaterVoltage,
                point.AnodeCurrentMa,
                point.ScreenCurrentMa,
                derived?.GmMaV,
                derived?.RpKohm,
                IsCompliance(point.Status) ? "COMPLIANCE" : "OK",
                false);
        }).ToArray();

        return new ReferenceCurveSeries(
            "MEAS:" + Guid.NewGuid().ToString("N"),
            result.Profile.Id,
            string.IsNullOrWhiteSpace(result.Request.Section) ? "A" : result.Request.Section,
            result.Emulator ? "EMULATOR" : "MEASUREMENT",
            result.Emulator ? "uTracer PRO Manager — emulator" : "uTracer PRO Manager — pomiar użytkownika",
            string.Empty,
            string.Empty,
            result.Definition.Kind.ToString(),
            result.CompletedAt,
            "MEASURED",
            result.Emulator ? 0.25 : 1.0,
            "MEASUREMENT_V2",
            !result.Emulator,
            points);
    }

    private static ReferenceCurveMatchRequest BuildMatchRequest(ReferenceMeasurementResult result)
    {
        var points = result.Points;
        var varyingVa = Range(points.Select(point => point.CommandedVa)) > 0.5;
        var varyingVs = Range(points.Select(point => point.CommandedVs)) > 0.5;
        var varyingVg = Range(points.Select(point => point.CommandedVg)) > 0.05 || points.Select(point => point.StepValue).Distinct().Count() > 1;
        var varyingVh = Range(points.Select(point => point.HeaterVoltage)) > 0.05;

        return new ReferenceCurveMatchRequest(
            result.Profile.Id,
            string.IsNullOrWhiteSpace(result.Request.Section) ? "A" : result.Request.Section,
            varyingVa ? null : points.Select(point => point.CommandedVa).DefaultIfEmpty().Average(),
            varyingVs ? null : points.Select(point => point.CommandedVs).DefaultIfEmpty().Average(),
            varyingVg ? null : points.Select(point => point.CommandedVg).DefaultIfEmpty().Average(),
            varyingVh ? null : points.Select(point => point.HeaterVoltage).DefaultIfEmpty().Average(),
            2.0,
            null,
            "CATALOG");
    }

    private static string SeriesKey(ReferenceMeasurementDefinition definition, ReferenceMeasurementPoint point) =>
        $"{definition.SteppingLabel}={point.StepValue.ToString("0.######", CultureInfo.InvariantCulture)}";

    private static bool IsCompliance(string status) =>
        status.Contains("11", StringComparison.OrdinalIgnoreCase) ||
        status.Contains("COMPLIANCE", StringComparison.OrdinalIgnoreCase) ||
        status.Contains("LIMIT", StringComparison.OrdinalIgnoreCase);

    private static double Range(IEnumerable<double> values)
    {
        var finite = values.Where(double.IsFinite).ToArray();
        return finite.Length == 0 ? 0 : finite.Max() - finite.Min();
    }
}
