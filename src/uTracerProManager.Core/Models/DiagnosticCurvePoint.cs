using System;

namespace uTracerProManager.Core.Models;

public sealed record DiagnosticCurvePoint(int Sequence, DateTimeOffset Timestamp, double GridVoltage, double TargetAnodeVoltageA, double MeasuredAnodeVoltageA, double AnodeCurrentAMa, double TargetAnodeVoltageB, double MeasuredAnodeVoltageB, double AnodeCurrentBMa, string Status);
