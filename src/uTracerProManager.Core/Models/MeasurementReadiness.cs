namespace uTracerProManager.Core.Models;

public enum ProfileDataReadiness
{
    Blocked = 0,
    Ready = 1
}

public enum HardwareReadiness
{
    Unsupported = 0,
    CatalogOnly = 1,
    RequiresConfirmation = 2,
    Compatible = 3
}

public enum MeasurementPlanReadiness
{
    NotValidated = 0,
    Blocked = 1,
    Validated = 2
}

/// <summary>
/// Trzy niezależne stany. READY danych profilu nigdy nie oznacza automatycznie,
/// że wybrany sprzęt jest zgodny ani że konkretny plan skanu jest bezpieczny.
/// </summary>
public sealed record MeasurementReadiness(
    ProfileDataReadiness ProfileData,
    HardwareReadiness Hardware,
    MeasurementPlanReadiness Plan,
    string Reason)
{
    public bool CanMeasure =>
        ProfileData == ProfileDataReadiness.Ready &&
        Hardware == HardwareReadiness.Compatible &&
        Plan == MeasurementPlanReadiness.Validated;
}
