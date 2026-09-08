namespace uTracerProManager.Core.Models;

public static class ProfileWorkQueueCategories
{
    public const string All = "WSZYSTKIE";
    public const string ReadyToApprove = "GOTOWY DO ZATWIERDZENIA";
    public const string SourcePdf = "ŹRÓDŁO / PDF";
    public const string Pinout = "BRAK PINOUT";
    public const string Heater = "BRAK ŻARZENIA";
    public const string OperatingPoint = "BRAK PUNKTU PRACY";
    public const string Limits = "BRAK LIMITÓW";
    public const string Curve = "BRAK KRZYWEJ";
    public const string Hardware = "SPRZĘT DO WERYFIKACJI";
    public const string PowerBlocked = "BLOKADA MOCY 95%";

    public static IReadOnlyList<string> AllOptions { get; } =
    [
        All,
        ReadyToApprove,
        SourcePdf,
        Pinout,
        Heater,
        OperatingPoint,
        Limits,
        Curve,
        Hardware,
        PowerBlocked
    ];
}

public sealed record ProfileWorkQueueItem(
    string ProfileId,
    string DisplayName,
    string TubeTypes,
    string ManufacturerScope,
    string PrimaryCategory,
    string MissingFlags,
    int Priority,
    int CompletenessPercent,
    string HardwareStatus,
    string ReviewStatus,
    string QueueClass,
    string SourceTitle,
    string SourceUrl,
    string SourcePage,
    string Reason,
    bool ReadyCandidate,
    bool PowerBlocked)
{
    public string ListForeground => ReadyCandidate ? "#1B5E20" : PowerBlocked ? "#C62828" : "#17395C";
    public string CompletionLabel => $"{CompletenessPercent}%";
    public string PriorityLabel => $"P{Priority}";
    public string SourceLabel => string.IsNullOrWhiteSpace(SourcePage) ? SourceTitle : $"{SourceTitle} • s. {SourcePage}";
}

public sealed record ProfileWorkQueueSummary(
    int TotalBlocked,
    int ReadyToApprove,
    int SourcePdf,
    int Pinout,
    int Heater,
    int OperatingPoint,
    int Limits,
    int Curve,
    int Hardware,
    int PowerBlocked,
    int PendingCurveTargets)
{
    public string Headline => $"BLOCKED {TotalBlocked:N0} • gotowe do zatwierdzenia {ReadyToApprove:N0} • PENDING krzywych {PendingCurveTargets:N0}";
    public string Detail =>
        $"PDF/źródło {SourcePdf:N0} • pinout {Pinout:N0} • żarzenie {Heater:N0} • punkt pracy {OperatingPoint:N0} • " +
        $"limity {Limits:N0} • krzywa {Curve:N0} • sprzęt {Hardware:N0} • blokada mocy {PowerBlocked:N0}";
}
