namespace uTracerProManager.Core.Services;

public sealed record VoltageCorrection(double TargetVoltage, double PreviousCommandVoltage, double MeasuredVoltage, double NewCommandVoltage, double ErrorVoltage, bool InTolerance, bool Limited);
