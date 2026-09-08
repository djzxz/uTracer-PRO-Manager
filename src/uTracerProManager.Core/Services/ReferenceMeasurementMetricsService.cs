using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

public sealed record ReferencePointMetrics(
    int Sequence,
    double? GmMaV,
    double? RpKohm,
    double? Mu);

public sealed class ReferenceMeasurementMetricsService
{
    public IReadOnlyDictionary<int, ReferencePointMetrics> Calculate(ReferenceMeasurementResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Calculate(result.Definition.Kind, result.Points);
    }

    public IReadOnlyDictionary<int, ReferencePointMetrics> Calculate(
        ReferenceMeasurementKind kind,
        IReadOnlyList<ReferenceMeasurementPoint> points)
    {
        var rp = CalculateRp(points);
        var gm = CalculateGm(kind, points);
        var result = new Dictionary<int, ReferencePointMetrics>();

        foreach (var point in points)
        {
            rp.TryGetValue(point.Sequence, out var rpValue);
            gm.TryGetValue(point.Sequence, out var gmValue);
            double? mu = null;
            if (gmValue.HasValue && rpValue.HasValue &&
                double.IsFinite(gmValue.Value) && double.IsFinite(rpValue.Value))
                mu = gmValue.Value * rpValue.Value;

            result[point.Sequence] = new ReferencePointMetrics(
                point.Sequence,
                gmValue,
                rpValue,
                mu);
        }

        return result;
    }

    private static Dictionary<int, double?> CalculateRp(IReadOnlyList<ReferenceMeasurementPoint> points)
    {
        var result = points.ToDictionary(point => point.Sequence, _ => (double?)null);

        foreach (var curve in points.GroupBy(point => point.CurveIndex))
        {
            var ordered = curve
                .Where(point => double.IsFinite(point.MeasuredVa) && double.IsFinite(point.AnodeCurrentMa))
                .OrderBy(point => point.MeasuredVa)
                .ToArray();

            for (var index = 0; index < ordered.Length; index++)
            {
                var left = index == 0 ? ordered[index] : ordered[index - 1];
                var right = index == ordered.Length - 1 ? ordered[index] : ordered[index + 1];
                if (left.Sequence == right.Sequence)
                    continue;

                var deltaI = right.AnodeCurrentMa - left.AnodeCurrentMa;
                var deltaV = right.MeasuredVa - left.MeasuredVa;
                if (Math.Abs(deltaI) < 1e-9 || Math.Abs(deltaV) < 1e-9)
                    continue;

                // V / mA = kΩ
                var value = deltaV / deltaI;
                if (double.IsFinite(value) && value > 0)
                    result[ordered[index].Sequence] = value;
            }
        }

        return result;
    }

    private static Dictionary<int, double?> CalculateGm(
        ReferenceMeasurementKind kind,
        IReadOnlyList<ReferenceMeasurementPoint> points)
    {
        var result = points.ToDictionary(point => point.Sequence, _ => (double?)null);
        if (!GridIsSteppingVariable(kind))
            return result;

        foreach (var xGroup in points.GroupBy(point => Math.Round(point.XValue, 6)))
        {
            var ordered = xGroup
                .OrderBy(point => point.StepValue)
                .ThenBy(point => point.CurveIndex)
                .ToArray();

            for (var index = 0; index < ordered.Length; index++)
            {
                var left = index == 0 ? ordered[index] : ordered[index - 1];
                var right = index == ordered.Length - 1 ? ordered[index] : ordered[index + 1];
                if (left.Sequence == right.Sequence)
                    continue;

                var deltaVg = right.StepValue - left.StepValue;
                var deltaIa = right.AnodeCurrentMa - left.AnodeCurrentMa;
                if (Math.Abs(deltaVg) < 1e-9)
                    continue;

                var value = deltaIa / deltaVg;
                if (double.IsFinite(value))
                    result[ordered[index].Sequence] = Math.Abs(value);
            }
        }

        return result;
    }

    private static bool GridIsSteppingVariable(ReferenceMeasurementKind kind) => kind is
        ReferenceMeasurementKind.AnodeSweepSteppedGrid or
        ReferenceMeasurementKind.TiedAnodeScreenSweepSteppedGrid or
        ReferenceMeasurementKind.ScreenSweepSteppedGrid or
        ReferenceMeasurementKind.HeaterSweepSteppedGrid or
        ReferenceMeasurementKind.AnodeSweepSteppedPositiveGrid or
        ReferenceMeasurementKind.AnodeSweepSteppedGridUltraLinear or
        ReferenceMeasurementKind.AnodeSweepSteppedGridSchadeFeedback;
}
