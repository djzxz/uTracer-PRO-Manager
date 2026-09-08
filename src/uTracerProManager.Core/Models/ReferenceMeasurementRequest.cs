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
    /// <summary>Oddzielny programowy limit prądu anody. 0 = użyj ComplianceMa dla zgodności wstecznej.</summary>
    public int AnodeComplianceMa { get; init; }

    /// <summary>Oddzielny programowy limit prądu ekranu / prądu siatki w trybie +Vg. 0 = użyj ComplianceMa.</summary>
    public int ScreenComplianceMa { get; init; }

    /// <summary>Operator potwierdził, że zewnętrzny zasilacz żarzenia jest podłączony i ustawiony.</summary>
    public bool ExternalHeaterSupplyConfirmed { get; init; }

    /// <summary>Identyfikator sekcji lampy używany przez zapis krzywych i porównania.</summary>
    public string Section { get; init; } = "A";

    /// <summary>0 = auto PGA; 1..8 = ustawienie ręczne panelu. Domyślnie auto.</summary>
    public int AnodeRangeIndex { get; init; }

    /// <summary>0 = auto PGA; 1..8 = ustawienie ręczne panelu. Domyślnie auto.</summary>
    public int ScreenRangeIndex { get; init; }

    public int EffectiveAnodeComplianceMa => AnodeComplianceMa > 0 ? AnodeComplianceMa : ComplianceMa;
    public int EffectiveScreenComplianceMa => ScreenComplianceMa > 0 ? ScreenComplianceMa : ComplianceMa;

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
        ValidateCompliance(ComplianceMa, "Compliance zgodności wstecznej");
        if (AnodeComplianceMa > 0)
            ValidateCompliance(AnodeComplianceMa, "Compliance anody");
        if (ScreenComplianceMa > 0)
            ValidateCompliance(ScreenComplianceMa, "Compliance ekranu");
        if (AnodeRangeIndex is < 0 or > 8 || ScreenRangeIndex is < 0 or > 8)
            throw new InvalidOperationException("Zakres PGA musi być Auto (0) albo jedną z ośmiu pozycji 1–8.");
        if (DelaySeconds is < 0 or > 120)
            throw new InvalidOperationException("Opóźnienie musi wynosić 0–120 s.");
        if (WarmupSeconds is < 60 or > 1800)
            throw new InvalidOperationException("Rozgrzewanie musi wynosić 60–1800 s.");
        if (UltraLinearKPercent is < 0 or > 100)
            throw new InvalidOperationException("Odczep UL k musi wynosić 0–100%.");
        if (SchadeFeedbackPercent is < 0 or > 100)
            throw new InvalidOperationException("Sprzężenie Schade musi wynosić 0–100%.");
        if (string.IsNullOrWhiteSpace(Section))
            throw new InvalidOperationException("Sekcja pomiarowa nie może być pusta.");

        var definition = ReferenceMeasurementDefinition.For(Kind);
        if (definition.RequiresSpecialWiring && !SpecialWiringConfirmed)
            throw new InvalidOperationException("Ten tryb wymaga potwierdzenia specjalnego okablowania zgodnego z opisem.");
        if (ExternalHeater && !ExternalHeaterSupplyConfirmed)
            throw new InvalidOperationException("Zaznacz potwierdzenie podłączenia i ustawienia zewnętrznego zasilacza żarzenia.");
        if (LogarithmicX && XStart <= 0)
            throw new InvalidOperationException("Skan logarytmiczny wymaga dodatniego początku osi X.");
    }

    private static void ValidateCompliance(int value, string label)
    {
        if (value is not (7 or 12 or 25 or 50 or 100 or 125 or 150 or 175 or 200))
            throw new InvalidOperationException($"{label}: 7, 12, 25, 50, 100, 125, 150, 175 albo 200 mA.");
    }
}
