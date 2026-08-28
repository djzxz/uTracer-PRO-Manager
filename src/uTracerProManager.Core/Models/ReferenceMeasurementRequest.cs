namespace uTracerProManager.Core.Models;

public sealed record ReferenceMeasurementRequest(
    ReferenceMeasurementKind Kind,
    double XStart,
    double XStop,
    int Intervals,
    IReadOnlyList<double> SteppingValues,
    double ConstantVa,
    double ConstantVs,
    double ConstantVg,
    double ConstantVh,
    double UltraLinearKPercent,
    double SchadeFeedbackPercent,
    int AveragingIndex,
    int ComplianceMa,
    int DelaySeconds,
    int WarmupSeconds,
    bool LogarithmicX,
    bool SpecialWiringConfirmed,
    bool ExternalHeater)
{
    public void Validate()
    {
        if (!double.IsFinite(XStart) || !double.IsFinite(XStop) || XStop <= XStart)
            throw new InvalidOperationException("Koniec skanu musi być większy od początku.");
        if (Intervals is < 1 or > 200)
            throw new InvalidOperationException("Liczba przedziałów musi wynosić 1–200.");
        if (SteppingValues.Count is < 1 or > 40 || SteppingValues.Any(value => !double.IsFinite(value)))
            throw new InvalidOperationException("Podaj od 1 do 40 prawidłowych wartości zmiennej krokowej.");
        if (AveragingIndex is < 0 or > 7)
            throw new InvalidOperationException("Uśrednianie musi mieć poziom 0–7.");
        if (ComplianceMa is not (7 or 12 or 25 or 50 or 100 or 125 or 150 or 175 or 200))
            throw new InvalidOperationException("Compliance: 7, 12, 25, 50, 100, 125, 150, 175 albo 200 mA.");
        if (DelaySeconds is < 0 or > 120)
            throw new InvalidOperationException("Opóźnienie musi wynosić 0–120 s.");
        if (WarmupSeconds is < 60 or > 1800)
            throw new InvalidOperationException("Rozgrzewanie musi wynosić 60–1800 s.");
        if (UltraLinearKPercent is < 0 or > 100)
            throw new InvalidOperationException("Odczep UL k musi wynosić 0–100%.");
        if (SchadeFeedbackPercent is < 0 or > 100)
            throw new InvalidOperationException("Sprzężenie Schade musi wynosić 0–100%.");

        var definition = ReferenceMeasurementDefinition.For(Kind);
        if (definition.RequiresSpecialWiring && !SpecialWiringConfirmed)
            throw new InvalidOperationException("Ten tryb wymaga potwierdzenia specjalnego okablowania zgodnego z opisem.");
        if (LogarithmicX && XStart <= 0)
            throw new InvalidOperationException("Skan logarytmiczny wymaga dodatniego początku osi X.");
    }
}
