namespace uTracerProManager.Core.Models;

public sealed record HardwareCapabilities(
    string DatabaseId,
    HardwareGeneration Generation,
    string DisplayName,
    double MaxAnodeVoltage,
    double MaxScreenVoltage,
    double MinGridVoltage,
    double MaxPulseCurrentMa,
    int GridResolutionBits,
    bool SupportsFastCapture,
    bool SupportsOnChipCalibration,
    bool SupportsLowCurrentRange,
    bool SupportsFirmwareUpdate,
    bool RequiresHardwareModification,
    bool SupportsCurrentProtocol,
    bool SupportsPositiveGrid,
    string SafetyNote)
{
    public static HardwareCapabilities StockSafe { get; } = new(
        "UTRACER3_PLUS_STOCK",
        HardwareGeneration.StockUTracer3Plus,
        "uTracer 3+ — firmware fabryczny",
        400,
        400,
        -50,
        200,
        10,
        SupportsFastCapture: false,
        SupportsOnChipCalibration: false,
        SupportsLowCurrentRange: false,
        SupportsFirmwareUpdate: false,
        RequiresHardwareModification: false,
        SupportsCurrentProtocol: true,
        SupportsPositiveGrid: false,
        "Tryb domyślny. Nie włącza funkcji wymagających alternatywnego procesora ani zmian sprzętowych.");

    public static HardwareCapabilities Stock600Ma { get; } = new(
        "UTRACER3_PLUS_600MA_MOD",
        HardwareGeneration.StockUTracer3Plus,
        "uTracer 3+ — modyfikacja toru 600 mA",
        400,
        400,
        -50,
        600,
        10,
        SupportsFastCapture: false,
        SupportsOnChipCalibration: false,
        SupportsLowCurrentRange: false,
        SupportsFirmwareUpdate: false,
        RequiresHardwareModification: true,
        SupportsCurrentProtocol: true,
        SupportsPositiveGrid: false,
        "Wybierz tylko po fizycznej modyfikacji toru prądowego. Zwykłego uTracera 3+ nie można programowo zmienić w wersję 600 mA.");

    public static HardwareCapabilities UMax307 { get; } = new(
        "UTRACER3_PLUS_UTMAX",
        HardwareGeneration.UMaxFirmware,
        "uTracer 3+ — uTmax firmware 3.07",
        400,
        400,
        -50,
        200,
        12,
        SupportsFastCapture: true,
        SupportsOnChipCalibration: true,
        SupportsLowCurrentRange: true,
        SupportsFirmwareUpdate: true,
        RequiresHardwareModification: true,
        SupportsCurrentProtocol: true,
        SupportsPositiveGrid: false,
        "Wymaga potwierdzonego procesora uTmax i właściwych modyfikacji. Sam program nie odblokowuje tych funkcji w fabrycznym uTracerze.");

    public static HardwareCapabilities UTracerNxt { get; } = new(
        "UTRACER_NXT",
        HardwareGeneration.UTracerNxt,
        "uTracerNXT — konfiguracja standardowa",
        500,
        500,
        -120,
        350,
        12,
        SupportsFastCapture: true,
        SupportsOnChipCalibration: true,
        SupportsLowCurrentRange: true,
        SupportsFirmwareUpdate: true,
        RequiresHardwareModification: false,
        SupportsCurrentProtocol: false,
        SupportsPositiveGrid: false,
        "Wariant służy do filtrowania bazy. Sterownik protokołu uTracerNXT nie jest jeszcze aktywny, dlatego rzeczywisty pomiar pozostaje zablokowany.");

    public static HardwareCapabilities UTracer6 { get; } = new(
        "UTRACER6",
        HardwareGeneration.UTracer6,
        "uTracer6 — konfiguracja standardowa",
        1000,
        1000,
        -100,
        1000,
        12,
        SupportsFastCapture: true,
        SupportsOnChipCalibration: true,
        SupportsLowCurrentRange: true,
        SupportsFirmwareUpdate: true,
        RequiresHardwareModification: false,
        SupportsCurrentProtocol: false,
        SupportsPositiveGrid: true,
        "Wariant służy do filtrowania bazy. Sterownik protokołu uTracer6 nie jest jeszcze aktywny, dlatego rzeczywisty pomiar pozostaje zablokowany.");

    public static IReadOnlyList<HardwareCapabilities> All { get; } =
    [
        StockSafe,
        Stock600Ma,
        UMax307,
        UTracerNxt,
        UTracer6
    ];
}
