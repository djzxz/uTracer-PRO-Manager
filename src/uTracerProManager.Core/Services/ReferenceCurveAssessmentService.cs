using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

public enum ReferenceCurveGrade
{
    NoReference = 0,
    Compatible = 1,
    Weak = 2,
    Shifted = 3,
    Unstable = 4
}

public sealed record ReferenceCurveAssessment(
    ReferenceCurveGrade Grade,
    double IaRmsPercent,
    double IaMaePercent,
    double IsRmsPercent,
    double RejectedPercent,
    string Label,
    string Explanation);

public sealed class ReferenceCurveAssessmentService
{
    public ReferenceCurveAssessment Assess(
        IReadOnlyList<ReferenceCurveDataPoint> measured,
        ReferenceCurveSeries reference,
        ReferenceCurveComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(comparison);

        var comparableReference = reference.Points
            .Where(point => !IsRejected(point))
            .ToArray();
        if (comparison.ComparedPoints <= 0 || comparableReference.Length == 0)
            return NoReference("Brak wystarczającej liczby porównywalnych punktów.");

        var meanIa = comparableReference.Select(point => Math.Abs(point.Ia)).Where(value => value > 0.001).DefaultIfEmpty(0).Average();
        var meanIs = comparableReference.Select(point => Math.Abs(point.Is)).Where(value => value > 0.001).DefaultIfEmpty(0).Average();
        var iaRmsPercent = Percent(comparison.IaRms, meanIa);
        var iaMaePercent = Percent(comparison.IaMae, meanIa);
        var isRmsPercent = Percent(comparison.IsRms, meanIs);
        var rejectedPercent = 100.0 * comparison.RejectedPoints /
                              Math.Max(1, comparison.ComparedPoints + comparison.RejectedPoints);

        var instability = EstimateInstability(measured);
        if (rejectedPercent > 25 || instability > 0.20)
        {
            return new ReferenceCurveAssessment(
                ReferenceCurveGrade.Unstable,
                iaRmsPercent,
                iaMaePercent,
                isRmsPercent,
                rejectedPercent,
                "NIESTABILNA",
                $"Dużo odrzuconych punktów ({rejectedPercent:F1}%) lub niestabilny przebieg charakterystyki ({instability * 100:F1}%).");
        }

        if (iaRmsPercent <= 10 && iaMaePercent <= 8)
        {
            return new ReferenceCurveAssessment(
                ReferenceCurveGrade.Compatible,
                iaRmsPercent,
                iaMaePercent,
                isRmsPercent,
                rejectedPercent,
                "ZGODNA",
                $"Ia RMS {iaRmsPercent:F1}% i MAE {iaMaePercent:F1}% względem krzywej katalogowej.");
        }

        if (iaRmsPercent <= 25)
        {
            return new ReferenceCurveAssessment(
                ReferenceCurveGrade.Weak,
                iaRmsPercent,
                iaMaePercent,
                isRmsPercent,
                rejectedPercent,
                "SŁABA / ZUŻYTA",
                $"Charakterystyka zachowuje kształt, ale odchyłka Ia RMS wynosi {iaRmsPercent:F1}%.");
        }

        return new ReferenceCurveAssessment(
            ReferenceCurveGrade.Shifted,
            iaRmsPercent,
            iaMaePercent,
            isRmsPercent,
            rejectedPercent,
            "PRZESUNIĘTA",
            $"Odchyłka Ia RMS {iaRmsPercent:F1}% wskazuje istotne przesunięcie względem katalogu.");
    }

    public static ReferenceCurveAssessment NoReference(string explanation) => new(
        ReferenceCurveGrade.NoReference, 0, 0, 0, 0, "BRAK PEŁNEJ KRZYWEJ REFERENCYJNEJ", explanation);

    private static double Percent(double error, double referenceMean) =>
        referenceMean <= 0.001 ? 0 : Math.Abs(error) / referenceMean * 100.0;

    private static double EstimateInstability(IReadOnlyList<ReferenceCurveDataPoint> measured)
    {
        var transitions = 0;
        var reversals = 0;
        foreach (var series in measured.GroupBy(point => point.SeriesKey))
        {
            var ordered = series.OrderBy(point => point.VaMeasured).ToArray();
            for (var i = 1; i < ordered.Length; i++)
            {
                var deltaV = ordered[i].VaMeasured - ordered[i - 1].VaMeasured;
                var deltaI = ordered[i].Ia - ordered[i - 1].Ia;
                if (Math.Abs(deltaV) < 0.01)
                    continue;
                transitions++;
                if (deltaI < -Math.Max(0.2, Math.Abs(ordered[i - 1].Ia) * 0.05))
                    reversals++;
            }
        }
        return transitions == 0 ? 0 : (double)reversals / transitions;
    }

    private static bool IsRejected(ReferenceCurveDataPoint point) =>
        point.ComplianceStatus.Contains("COMPLIANCE", StringComparison.OrdinalIgnoreCase) ||
        point.ComplianceStatus.Contains("REJECT", StringComparison.OrdinalIgnoreCase);
}
