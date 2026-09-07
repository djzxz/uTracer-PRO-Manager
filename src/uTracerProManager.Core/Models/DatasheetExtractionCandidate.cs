namespace uTracerProManager.Core.Models;

public sealed record ExtractedField<T>(
    T? Value,
    double Confidence,
    string Evidence,
    bool RequiresHumanReview)
{
    public bool IsUsable => Value is not null && Confidence >= 0.90 && !RequiresHumanReview;
}

public sealed record DatasheetExtractionCandidate(
    string CandidateId,
    string TubeType,
    string Manufacturer,
    string SourceTitle,
    string SourceUrl,
    string SourcePage,
    string ExtractionEngine,
    string ExtractionVersion,
    ExtractedField<double> HeaterVoltage,
    ExtractedField<double> HeaterCurrentAmp,
    ExtractedField<double> AnodeVoltage,
    ExtractedField<double> ScreenVoltage,
    ExtractedField<double> GridVoltage,
    ExtractedField<double> AnodeCurrentMa,
    ExtractedField<double> ScreenCurrentMa,
    ExtractedField<double> MaxAnodePowerW,
    ExtractedField<double> MaxScreenPowerW,
    string PinoutText,
    bool HumanApproved,
    DateTimeOffset CreatedAt)
{
    public bool CanPromoteToProfile =>
        HumanApproved &&
        HeaterVoltage.Value.HasValue &&
        AnodeVoltage.Value.HasValue &&
        GridVoltage.Value.HasValue &&
        AnodeCurrentMa.Value.HasValue &&
        !string.IsNullOrWhiteSpace(PinoutText);
}
