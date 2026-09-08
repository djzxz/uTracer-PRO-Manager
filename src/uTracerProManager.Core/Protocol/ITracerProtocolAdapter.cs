using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Protocol;

public interface ITracerProtocolAdapter
{
    string Id { get; }
    string DisplayName { get; }
    bool MeasurementVerified { get; }
    bool SupportsPositiveGridViaScreen { get; }
    bool SupportsExternalHeater { get; }
    bool SupportsAcHeaterSynchronization { get; }

    byte RangeCode(int rangeIndex);
    byte AveragingCode(int averagingIndex);
    ushort AnodeCode(double voltage, double supplyVoltage, CalibrationProfile calibration);
    ushort ScreenCode(double voltage, double supplyVoltage, CalibrationProfile calibration);
    ushort GridCode(double voltage, CalibrationProfile calibration);
    ushort HeaterCode(double voltage, double supplyVoltage);

    void EnsureMeasurementAllowed();
}

public static class TracerProtocolAdapterFactory
{
    public static ITracerProtocolAdapter For(HardwareCapabilities hardware) => hardware.Generation switch
    {
        HardwareGeneration.StockUTracer3Plus => new UTracer3PlusProtocolAdapter(hardware.DisplayName),
        HardwareGeneration.UMaxFirmware => new UTracer3PlusProtocolAdapter(hardware.DisplayName),
        HardwareGeneration.UTracerNxt => new CatalogOnlyProtocolAdapter("uTracerNXT"),
        HardwareGeneration.UTracer6 => new CatalogOnlyProtocolAdapter("uTracer6"),
        _ => new CatalogOnlyProtocolAdapter(hardware.DisplayName)
    };
}

internal sealed class UTracer3PlusProtocolAdapter : ITracerProtocolAdapter
{
    public UTracer3PlusProtocolAdapter(string displayName) => DisplayName = displayName;

    public string Id => "UTRACER3_PLUS_PROTOCOL";
    public string DisplayName { get; }
    public bool MeasurementVerified => true;
    public bool SupportsPositiveGridViaScreen => true;
    public bool SupportsExternalHeater => true;
    public bool SupportsAcHeaterSynchronization => false;

    public byte RangeCode(int rangeIndex) => rangeIndex switch
    {
        0 => 0x08,
        >= 1 and <= 8 => (byte)(rangeIndex - 1),
        _ => throw new ArgumentOutOfRangeException(nameof(rangeIndex))
    };

    public byte AveragingCode(int averagingIndex) => averagingIndex switch
    {
        0 => 0x40,
        1 => 1,
        2 => 2,
        3 => 4,
        4 => 8,
        5 => 16,
        6 => 32,
        7 => 0x40, // zgodność ze starszymi zapisami aplikacji
        _ => throw new ArgumentOutOfRangeException(nameof(averagingIndex))
    };

    public ushort AnodeCode(double voltage, double supplyVoltage, CalibrationProfile calibration) =>
        CommandCodeConverter.AnodeCode(voltage, supplyVoltage, calibration);

    public ushort ScreenCode(double voltage, double supplyVoltage, CalibrationProfile calibration) =>
        CommandCodeConverter.ScreenCode(voltage, supplyVoltage, calibration);

    public ushort GridCode(double voltage, CalibrationProfile calibration) =>
        CommandCodeConverter.GridCode(voltage, calibration);

    public ushort HeaterCode(double voltage, double supplyVoltage) =>
        CommandCodeConverter.HeaterCode(voltage, supplyVoltage);

    public void EnsureMeasurementAllowed() { }
}

internal sealed class CatalogOnlyProtocolAdapter : ITracerProtocolAdapter
{
    public CatalogOnlyProtocolAdapter(string displayName) => DisplayName = displayName;

    public string Id => "CATALOG_ONLY";
    public string DisplayName { get; }
    public bool MeasurementVerified => false;
    public bool SupportsPositiveGridViaScreen => false;
    public bool SupportsExternalHeater => false;
    public bool SupportsAcHeaterSynchronization => false;

    public byte RangeCode(int rangeIndex) => throw Unsupported();
    public byte AveragingCode(int averagingIndex) => throw Unsupported();
    public ushort AnodeCode(double voltage, double supplyVoltage, CalibrationProfile calibration) => throw Unsupported();
    public ushort ScreenCode(double voltage, double supplyVoltage, CalibrationProfile calibration) => throw Unsupported();
    public ushort GridCode(double voltage, CalibrationProfile calibration) => throw Unsupported();
    public ushort HeaterCode(double voltage, double supplyVoltage) => throw Unsupported();
    public void EnsureMeasurementAllowed() => throw Unsupported();

    private NotSupportedException Unsupported() => new(
        $"{DisplayName}: wariant pozostaje filtrem katalogu. Adapter rzeczywistego protokołu musi przejść test fizyczny przed włączeniem pomiaru.");
}
