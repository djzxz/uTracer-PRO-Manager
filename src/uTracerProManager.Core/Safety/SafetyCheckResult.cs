using System.Collections.Generic;

namespace uTracerProManager.Core.Safety;

public sealed record SafetyCheckResult(bool IsSafe, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
