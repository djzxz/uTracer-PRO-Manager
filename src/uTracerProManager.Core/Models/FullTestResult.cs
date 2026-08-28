using System;
using System.Collections.Generic;

namespace uTracerProManager.Core.Models;

public sealed record FullTestResult(Guid TestId, string TubeInventoryNumber, string Manufacturer, string Notes, TubeProfile Profile, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, FullTestOptions Options, FullTestStatistics Statistics, IReadOnlyList<FullTestSample> Samples, bool Emulator, string ApplicationVersion, string ProductionCodePart1 = "", string ProductionCodePart2 = "", string DeclaredCondition = "Nieznany", TubeTestMode TestMode = TubeTestMode.FullDiagnostic, FullTestStatistics? SectionBStatistics = null, DualSectionComparison? DualComparison = null, IReadOnlyList<DiagnosticCurvePoint>? DiagnosticCurvePoints = null)
{
	public TimeSpan Duration => CompletedAt - StartedAt;
}
