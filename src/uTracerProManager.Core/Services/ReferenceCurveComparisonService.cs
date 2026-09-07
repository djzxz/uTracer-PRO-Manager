using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

/// <summary>
/// Porównuje zmierzoną krzywą z referencją. Interpolacja jest jawna: punkt
/// interpolowany jest oznaczony i nigdy nie zastępuje brakującego punktu w danych źródłowych.
/// Punkty z compliance są pomijane w statystykach.
/// </summary>
public sealed class ReferenceCurveComparisonService
{
    public ReferenceCurveComparison Compare(
        IReadOnlyList<ReferenceCurveDataPoint> measured,
        ReferenceCurveSeries reference)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(reference);
        if (measured.Count == 0 || reference.Points.Count == 0)
            throw new InvalidOperationException("Do porównania potrzebne są dwie niepuste serie.");

        var iaErrors = new List<double>();
        var isErrors = new List<double>();
        var gmErrors = new List<double>();
        var rpErrors = new List<double>();
        var rejected = 0;

        foreach (var sample in measured.OrderBy(point => point.VaMeasured))
        {
            if (IsRejected(sample))
            {
                rejected++;
                continue;
            }

            var referencePoint = Interpolate(reference.Points, sample);
            if (referencePoint is null || IsRejected(referencePoint))
            {
                rejected++;
                continue;
            }

            iaErrors.Add(sample.Ia - referencePoint.Ia);
            isErrors.Add(sample.Is - referencePoint.Is);
            if (sample.Gm.HasValue && referencePoint.Gm.HasValue)
                gmErrors.Add(sample.Gm.Value - referencePoint.Gm.Value);
            if (sample.Rp.HasValue && referencePoint.Rp.HasValue)
                rpErrors.Add(sample.Rp.Value - referencePoint.Rp.Value);
        }

        if (iaErrors.Count == 0)
            throw new InvalidOperationException("Brak porównywalnych punktów po odrzuceniu compliance/braków.");

        return new ReferenceCurveComparison(
            reference.Id,
            iaErrors.Count,
            rejected,
            Mae(iaErrors),
            Rms(iaErrors),
            Mae(isErrors),
            Rms(isErrors),
            gmErrors.Count == 0 ? null : Mae(gmErrors),
            rpErrors.Count == 0 ? null : Mae(rpErrors),
            $"Porównano {iaErrors.Count} pkt; odrzucono {rejected}; Ia MAE={Mae(iaErrors):F3} mA, RMS={Rms(iaErrors):F3} mA.");
    }

    public ReferenceCurveDataPoint? Interpolate(
        IReadOnlyList<ReferenceCurveDataPoint> reference,
        ReferenceCurveDataPoint measured)
    {
        var sameSeries = reference
            .Where(point => string.Equals(point.SeriesKey, measured.SeriesKey, StringComparison.OrdinalIgnoreCase))
            .Where(point => Math.Abs(point.Vg - measured.Vg) <= 0.25)
            .Where(point => Math.Abs(point.Vh - measured.Vh) <= 0.15)
            .OrderBy(point => X(point))
            .ToArray();

        if (sameSeries.Length == 0)
            return null;

        var x = X(measured);
        var exact = sameSeries.FirstOrDefault(point => Math.Abs(X(point) - x) <= 1e-9);
        if (exact is not null)
            return exact;

        var lower = sameSeries.LastOrDefault(point => X(point) < x);
        var upper = sameSeries.FirstOrDefault(point => X(point) > x);
        if (lower is null || upper is null)
            return null;
        if (IsRejected(lower) || IsRejected(upper))
            return null;

        var span = X(upper) - X(lower);
        if (span <= 0)
            return null;
        var f = (x - X(lower)) / span;
        return new ReferenceCurveDataPoint(
            measured.Sequence,
            measured.SeriesKey,
            Lerp(lower.VaSet, upper.VaSet, f),
            x,
            Lerp(lower.VsSet, upper.VsSet, f),
            Lerp(lower.VsMeasured, upper.VsMeasured, f),
            Lerp(lower.Vg, upper.Vg, f),
            Lerp(lower.Vh, upper.Vh, f),
            Lerp(lower.Ia, upper.Ia, f),
            Lerp(lower.Is, upper.Is, f),
            LerpNullable(lower.Gm, upper.Gm, f),
            LerpNullable(lower.Rp, upper.Rp, f),
            "INTERPOLATED",
            true);
    }

    private static bool IsRejected(ReferenceCurveDataPoint point) =>
        point.ComplianceStatus.Contains("COMPLIANCE", StringComparison.OrdinalIgnoreCase) ||
        point.ComplianceStatus.Contains("REJECT", StringComparison.OrdinalIgnoreCase);

    private static double X(ReferenceCurveDataPoint point) =>
        point.VaMeasured > 0 ? point.VaMeasured : point.VaSet;

    private static double Lerp(double a, double b, double f) => a + (b - a) * f;

    private static double? LerpNullable(double? a, double? b, double f) =>
        a.HasValue && b.HasValue ? Lerp(a.Value, b.Value, f) : null;

    private static double Mae(IReadOnlyList<double> errors) =>
        errors.Count == 0 ? 0 : errors.Average(value => Math.Abs(value));

    private static double Rms(IReadOnlyList<double> errors) =>
        errors.Count == 0 ? 0 : Math.Sqrt(errors.Average(value => value * value));
}
