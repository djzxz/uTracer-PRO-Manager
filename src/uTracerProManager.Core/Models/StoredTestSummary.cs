using System;

namespace uTracerProManager.Core.Models;

public sealed record StoredTestSummary(Guid TestId, DateTimeOffset CompletedAt, string TubeInventoryNumber, string Manufacturer, string ProfileName, string Grade, string Reliability, double ConditionPercent, int ValidSeries, bool Emulator, string ProductionCodePart1 = "", string ProductionCodePart2 = "", string DeclaredCondition = "Nieznany", string TestMode = "Pełna diagnostyka", double SectionMatchPercent = 0.0)
{
    public string SearchLabel =>
        $"{TubeInventoryNumber} • {Manufacturer} • {ProfileName} • {CompletedAt:yyyy-MM-dd HH:mm}";

    public override string ToString() => SearchLabel;
}
