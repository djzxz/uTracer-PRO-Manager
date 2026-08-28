namespace uTracerProManager.Core.Models;

public sealed record MeasurementRequest(double AnodeVoltage, double ScreenVoltage, double GridVoltage, double HeaterVoltage, double AnodeComplianceMa, double ScreenComplianceMa);
