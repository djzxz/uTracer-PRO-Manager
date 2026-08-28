using uTracerProManager.Core.Models;
using uTracerProManager.Core.Protocol;
using uTracerProManager.Core.Safety;
using uTracerProManager.Core.Services;
using uTracerProManager.Services;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException($"SELFTEST FAILED: {message}");
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"SELFTEST FAILED: {message}");
}

static CalibrationProfile ValidCalibration() => new()
{
    DeviceName = "SelfTest",
    SourcePath = "SELFTEST",
    ImportedAt = DateTimeOffset.Now,
    ImportedFromFile = true,
    PortName = "EMULATOR",
    CalibrationVersion = "2.0",
    CalibrationCompletedAt = DateTimeOffset.Now,
    VaFactor = 1,
    VsFactor = 1,
    IaFactor = 1,
    IsFactor = 1,
    VsuFactor = 1,
    Vg1Factor = 1,
    Vg4Factor = 1,
    Vg40Factor = 1,
    GridOffsetV = 0,
    GridSlope = 1,
    GridCalibrationModel = "offset-slope",
    VnFactor = 1,
    AnodeDividerOhm = 6800,
    AnodeSenseOhm = 18,
    ScreenSenseOhm = 18,
    MaxAnodeVoltage = 400,
    MaxAnodeCurrentMa = 200,
    MaxScreenCurrentMa = 200,
    MaxGridMagnitudeV = 50,
    SupplyCalibrationVerified = true,
    NegativeSupplyCalibrationVerified = true,
    GridCalibrationVerified = true,
    GridOffsetSlopeVerified = true,
    VoltageCalibrationVerified = true,
    CurrentCalibrationVerified = true
};

static TubeProfile TestProfile(bool approved = true, double anodeVoltage = 250) => new()
{
    Id = approved ? "SELFTEST_ECC83" : "SELFTEST_BLOCKED",
    DisplayName = approved ? "ECC83 self-test" : "Zablokowany profil",
    Family = "Podwójna trioda",
    TubeTypes = "ECC83, 12AX7",
    ManufacturerScope = "Profil testowy",
    Pinout = "Połówka A: 1=a, 2=g, 3=k; Połówka B: 6=a, 7=g, 8=k; 4-5-9=f",
    HeaterVoltage = 6.3,
    HeaterCurrentAmp = 0.3,
    AnodeVoltage = anodeVoltage,
    ScreenVoltage = 0,
    GridVoltage = -2,
    NominalAnodeCurrentMa = 1.2,
    NominalGmMaV = 1.6,
    NominalMu = 100,
    NominalRpKohm = 62.5,
    MaxAnodeVoltage = 300,
    MaxAnodePowerW = 1,
    AnodeComplianceMa = 7,
    WarmupSeconds = 60,
    ApprovedForHardware = approved
};

Console.WriteLine("uTracer PRO Manager Avalonia v1.2.7 — self-test");

Assert(TracerProtocol.BuildStartMeasurement(0x8F, 0x40, 0x08, 0x08) == "00000000008F400808", "START frame");
Assert(TracerProtocol.BuildGetMeasurement(0x0123, 0x0456, 0x0789, 0x00AB) == "1001230456078900AB", "GET frame");
Assert(TracerProtocol.BuildHoldMeasurement(0x0123, 0x0456, 0x0789, 0x00AB) == "2001230456078900AB", "HOLD frame");
Assert(TracerProtocol.BuildFilament(0x03FF) == "4000000000000003FF", "HEATER frame");
Assert(TracerProtocol.BuildEndMeasurement() == TracerProtocol.EndOrPingCommand, "END/PING frame");
Assert(TracerProtocol.BuildReadAdc() == TracerProtocol.ReadAdcCommand, "ADC frame");

var serialConnectionType = typeof(PortCatalogService).Assembly.GetType("uTracerProManager.Services.Win32SerialConnection");
var dcbLayoutMethod = serialConnectionType?.GetMethod(
    "GetDcbLayoutSizeForSelfTest",
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
Assert(dcbLayoutMethod?.Invoke(null, null) is int dcbSize && dcbSize == 28, "Win32 DCB layout must be 28 bytes");

var adcText = "10" + "0001" + "0002" + "0003" + "0004" + "0005" + "0006" + "0007" + "0008" + "06" + "07";
var adc = TracerProtocol.ParseAdcResponse(adcText);
Assert(adcText.Length == TracerProtocol.AdcResponseLength, "ADC length");
Assert(adc.Ia == 1 && adc.IaRaw == 2 && adc.Is == 3 && adc.IsRaw == 4, "ADC layout");

var calibration = ValidCalibration();
Assert(calibration.IsValidForHardwareDiagnostics, "calibration diagnostics validity");
Assert(calibration.IsCompleteForTubeTesting, "calibration v2 completeness");
Assert(CommandCodeConverter.GridCode(0, calibration) == 0, "grid zero code");
Assert(CommandCodeConverter.GridCode(-40, calibration) > CommandCodeConverter.GridCode(-1, calibration), "grid monotonicity");

var compatibilityDirectory = Path.Combine(Path.GetTempPath(), "utracer-compatibility-selftest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(compatibilityDirectory);
try
{
    var originalCalibrationPath = Path.Combine(compatibilityDirectory, "uTracer_3p11.cal");
    await File.WriteAllTextAsync(originalCalibrationPath, string.Join("\r\n", new[]
    {
        " 1005         'Va Gain",
        " 1004         'Vs Gain",
        " 982          'Ia Gain",
        " 987          'Is Gain",
        " 1000         'Vsupp",
        " 1007         'Vgrid Gain (40V)",
        " 1000         'Vsat",
        " 1013         'Vgrid Gain (4V)",
        " 960          'spare",
        " 1000         'spare",
        "-3            'COM port number",
        " 2            'version number",
        "-1            '-1 is end of file"
    }) + "\r\n");

    var calibrationFiles = new CalibrationFileService();
    var importedOriginalCalibration = await calibrationFiles.ImportAsync(originalCalibrationPath);
    Assert(importedOriginalCalibration.PortName == "COM3", "original .cal COM port");
    Assert(Math.Abs(importedOriginalCalibration.VaFactor - 1.005) < 0.000001, "original .cal Va factor");
    Assert(Math.Abs(importedOriginalCalibration.Vg1Factor - 0.960) < 0.000001, "original .cal low-grid factor");
    Assert(!importedOriginalCalibration.IsCompleteForTubeTesting, "original .cal remains safety-incomplete");

    var roundTripCalibrationPath = Path.Combine(compatibilityDirectory, "roundtrip.cal");
    await calibrationFiles.ExportOriginalGuiAsync(importedOriginalCalibration, roundTripCalibrationPath);
    var roundTripCalibration = await calibrationFiles.ImportAsync(roundTripCalibrationPath);
    Assert(Math.Abs(roundTripCalibration.Vg4Factor - 1.013) < 0.000001, "original .cal round-trip");

    var setupFiles = new OriginalUTracerSetupFileService();
    var defaultSetupPath = Path.Combine(compatibilityDirectory, "uTracer_3p12p6.uts");
    var setupSettings = new OriginalUTracerQuickTestSettings(
        "ECC83 SELFTEST", true, 6.3, 19.5, false,
        250, 10, 250, 10, -2, 10,
        1.2, 1.2, 62.5, 1.6, 100);
    await setupFiles.ExportAsync(null, setupSettings, defaultSetupPath);
    var importedSetup = await setupFiles.ImportAsync(defaultSetupPath);
    Assert(importedSetup.Lines.Count == 147, "original .uts line count");
    Assert(importedSetup.Variant == "Oryginalne GUI V3.12.6", "original .uts variant");
    Assert(Math.Abs(importedSetup.QuickTest.GridVoltage + 2) < 0.000001, "original .uts signed grid voltage");

    foreach (var suppliedSetupPath in args.Where(path => Path.GetExtension(path).Equals(".uts", StringComparison.OrdinalIgnoreCase)))
    {
        var suppliedSetup = await setupFiles.ImportAsync(suppliedSetupPath);
        Assert(suppliedSetup.Lines.Count >= 147, $"supplied .uts parsed: {Path.GetFileName(suppliedSetupPath)}");
        var exportedPath = Path.Combine(compatibilityDirectory, "roundtrip_" + Path.GetFileName(suppliedSetupPath));
        await setupFiles.ExportAsync(suppliedSetup, suppliedSetup.QuickTest, exportedPath);
        var reparsed = await setupFiles.ImportAsync(exportedPath);
        Assert(reparsed.Variant == suppliedSetup.Variant, $"supplied .uts round-trip: {Path.GetFileName(suppliedSetupPath)}");
    }

    foreach (var suppliedReportPath in args.Where(path => Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)))
    {
        var report = await File.ReadAllTextAsync(suppliedReportPath);
        Assert(report.Contains("Triode Quick Test", StringComparison.Ordinal), $"supplied Quick Test report: {Path.GetFileName(suppliedReportPath)}");
        Assert(report.Contains("SECTION 1", StringComparison.Ordinal) && report.Contains("SECTION 2", StringComparison.Ordinal),
            $"supplied Quick Test sections: {Path.GetFileName(suppliedReportPath)}");
    }
}
finally
{
    Directory.Delete(compatibilityDirectory, recursive: true);
}

HardwareCapabilityGuard.EnsureProfileFits(TestProfile(), HardwareCapabilities.StockSafe);
AssertThrows<InvalidOperationException>(
    () => HardwareCapabilityGuard.EnsureProfileFits(TestProfile(approved: false), HardwareCapabilities.StockSafe),
    "blocked profile must never reach hardware");
AssertThrows<InvalidOperationException>(
    () => HardwareCapabilityGuard.EnsureProfileFits(TestProfile(anodeVoltage: 450), HardwareCapabilities.StockSafe),
    "operating point above hardware voltage limit must be rejected");
AssertThrows<NotSupportedException>(
    () => HardwareCapabilityGuard.EnsureFeature(HardwareCapabilities.StockSafe.SupportsFastCapture, "fast capture", HardwareCapabilities.StockSafe),
    "stock firmware must not expose uTmax-only feature");
Assert(HardwareCapabilities.All.Count == 5, "five hardware variants available");
Assert(HardwareCapabilities.All.Select(item => item.DatabaseId).Distinct().Count() == 5,
    "hardware variants map to five distinct database IDs");
Assert(!HardwareCapabilities.UTracerNxt.SupportsCurrentProtocol,
    "NXT remains catalog-only until its physical protocol is implemented");

Assert(ReferenceMeasurementDefinition.All.Count == 13,
    "all 13 documented V3.12.6 measurement modes are present");
Assert(ReferenceMeasurementDefinition.All.Select(item => item.OriginalGuiIndex).SequenceEqual(Enumerable.Range(1, 13)),
    "reference measurement modes keep original GUI order");
Assert(ReferenceMeasurementDefinition.All.All(item => !string.IsNullOrWhiteSpace(item.Usage)),
    "every reference measurement explains when it should be used");
var specialReferenceRequest = new ReferenceMeasurementRequest(
    ReferenceMeasurementKind.TiedAnodeScreenSweepSteppedGrid,
    0, 250, 10, new[] { -2.0 },
    250, 250, -2, 6.3, 43, 20,
    2, 25, 0, 60, false, false, false);
AssertThrows<InvalidOperationException>(specialReferenceRequest.Validate,
    "special wiring mode requires explicit confirmation");
var confirmedReferenceRequest = specialReferenceRequest with { SpecialWiringConfirmed = true };
confirmedReferenceRequest.Validate();

var catalogPath = Path.Combine(AppContext.BaseDirectory, "Data", "tube_measurements.db");
var catalog = new TubeMeasurementCatalogService(catalogPath);
var info = await catalog.EnsureReadyAsync();
Assert(info.SchemaVersion == "7", "catalog schema v7");
Assert(info.CatalogVersion == "2.44.0", "catalog version 2.44.0");
Assert(info.ProfileCount == 21_505, "catalog profile count");
Assert(info.ReadyProfileCount == 879, "catalog ready profile count");
Assert(info.DatasheetCount == 22_156, "datasheet catalog count");
Assert(info.ModelCount == 15_885, "model catalog count");
Assert(info.ManufacturerCount == 217, "manufacturer catalog count");
Assert(info.ReadyDatasheetCount == 640, "ready manufacturer/model cards");
Assert(info.ReadyModelCount == 227, "ready model designations");
Assert((await catalog.SearchAsync(string.Empty)).Count == info.ReadyProfileCount,
    "empty profile search loads only READY profiles");
Assert((await catalog.SearchAsync("ECC83")).Count > 0, "ECC83 database search");
Assert((await catalog.LoadManufacturersAsync()).Count >= 200, "manufacturer index");

var readyCards = await catalog.SearchDatasheetsAsync("12AX7", "General Electric", "12AX7");
var readyCard = readyCards.Single(card =>
    card.DataSheetUrl == "https://tube-data.com/sheets/093/1/12AX7.pdf");
Assert(readyCard.HasApprovedMeasurementProfile, "exact manufacturer/model card marked READY");
var exactProfiles = await catalog.FindMatchingProfilesAsync(
    readyCard.DataSheetUrl,
    readyCard.TubeType,
    readyCard.Manufacturer);
Assert(exactProfiles.Count == 1, "exact manufacturer/model resolves one recommendation");
Assert(exactProfiles[0].Id.StartsWith("MFR25_12AX7_GENERAL_ELECTRIC_", StringComparison.Ordinal),
    "exact manufacturer/model resolves its v2.25 manufacturer profile");
Assert(exactProfiles[0].DisplayName.Contains("PASUJE DO", StringComparison.Ordinal),
    "manufacturer profile visibly identifies its approved template");
Assert(!exactProfiles[0].CountsForConditionPercent && exactProfiles[0].RequiresManualConfirmation,
    "manufacturer alias disables percentage and requires confirmation");

var newBatchCards = await catalog.SearchDatasheetsAsync("6N7", "General Electric", "6N7");
var newBatchCard = newBatchCards.Single(card =>
    card.DataSheetUrl == "https://tube-data.com/sheets/093/6/6N7.pdf");
var newBatchProfiles = await catalog.FindMatchingProfilesAsync(
    newBatchCard.DataSheetUrl,
    newBatchCard.TubeType,
    newBatchCard.Manufacturer);
Assert(newBatchProfiles.Count == 1 &&
       newBatchProfiles[0].Id.StartsWith("MFR26_6N7_GENERAL_ELECTRIC_", StringComparison.Ordinal),
    "v2.26 card resolves its manufacturer-specific READY profile");
Assert(!newBatchProfiles[0].CountsForConditionPercent && newBatchProfiles[0].RequiresManualConfirmation,
    "v2.26 manufacturer profile keeps percentage disabled and confirmation enabled");

var v227Cards = await catalog.SearchDatasheetsAsync("GL6201", "General Electric", "GL6201");
var v227Card = v227Cards.Single(card =>
    card.DataSheetUrl == "https://tube-data.com/sheets/093/6/6201.pdf");
var v227Profiles = await catalog.FindMatchingProfilesAsync(
    v227Card.DataSheetUrl,
    v227Card.TubeType,
    v227Card.Manufacturer);
Assert(v227Profiles.Count == 0 && !v227Card.HasApprovedMeasurementProfile &&
       v227Card.HasBlockedMeasurementProfile,
    "v2.44 strict power-reserve audit blocks GL6201 at the documented maximum point");

var blockedCards = await catalog.SearchDatasheetsAsync("6080", "AEG", "6080");
var blockedCard = blockedCards.Single();
Assert(!blockedCard.HasApprovedMeasurementProfile && blockedCard.HasBlockedMeasurementProfile,
    "unverified card remains visibly BLOCKED");
Assert((await catalog.FindMatchingProfilesAsync(
    blockedCard.DataSheetUrl,
    blockedCard.TubeType,
    blockedCard.Manufacturer)).Count == 0,
    "BLOCKED card cannot load a measurement profile");

var temporaryDirectory = Path.Combine(Path.GetTempPath(), "utracer-avalonia-selftest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryDirectory);
try
{
    var logPath = Path.Combine(temporaryDirectory, "application.log");
    var log = new AppLogService(logPath);
    await Task.WhenAll(Enumerable.Range(0, 30).Select(index => log.WriteAsync("parallel-" + index)));
    Assert((await File.ReadAllLinesAsync(logPath)).Length == 30, "concurrent non-locking log writes");
}
finally
{
    Directory.Delete(temporaryDirectory, recursive: true);
}

await using var emulator = new EmulatorTracerTransport();
await emulator.ConnectAsync("EMULATOR");
var referenceRequest = new ReferenceMeasurementRequest(
    ReferenceMeasurementKind.AnodeSweepSteppedGrid,
    50, 100, 2, new[] { -2.0 },
    250, 0, -2, 6.3, 43, 1,
    2, 7, 0, 60, false, false, false);
var referenceResult = await new ReferenceMeasurementController().RunAsync(
    TestProfile(), emulator, calibration, HardwareCapabilities.StockSafe, referenceRequest);
Assert(referenceResult.Emulator && referenceResult.Points.Count == 3,
    "reference curve controller completes a three-point synthetic scan");
Assert(referenceResult.Points.All(point => point.Status.Contains("EMULATOR", StringComparison.Ordinal)),
    "every synthetic reference point is visibly marked");
var options = FullTestOptions.ForMode(TubeTestMode.Quick, emulator: true) with
{
    EmulatorSpeedMultiplier = 500,
    IntervalSeconds = 0
};
var controller = new FullTestController(new SafetyValidator(), new FullTestStatisticsService());
var result = await controller.RunAsync(
    "SELFTEST-001",
    "SelfTest",
    "Automated emulator smoke test",
    TestProfile(),
    emulator,
    calibration,
    options);
Assert(result.Emulator, "emulator result is marked");
Assert(result.Samples.Count > 0, "emulator produced samples");
Assert(result.TestMode == TubeTestMode.Quick, "quick mode stored");

var historyDirectory = Path.Combine(Path.GetTempPath(), "utracer-history-search-selftest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(historyDirectory);
try
{
    var history = new FullTestDatabaseService(Path.Combine(historyDirectory, "history.db"));
    await history.SaveAsync(result);
    var storedMatches = await history.SearchAsync("SELFTEST-001");
    Assert(storedMatches.Count == 1 && storedMatches[0].SearchLabel.Contains("SELFTEST-001", StringComparison.Ordinal),
        "stored specimen search returns a readable saved-measurement label");
    Assert((await history.LoadAsync(storedMatches[0].TestId))?.Profile.Id == result.Profile.Id,
        "saved measurement restores the exact embedded profile");
}
finally
{
    Directory.Delete(historyDirectory, recursive: true);
}

var reportDirectory = Path.Combine(Path.GetTempPath(), "utracer-original-report-selftest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(reportDirectory);
try
{
    var originalReportPath = Path.Combine(reportDirectory, "uTracer_Quick_Test.txt");
    await new OriginalUTracerQuickTestReportService().ExportAsync(result, originalReportPath);
    var originalReport = await File.ReadAllTextAsync(originalReportPath);
    Assert(originalReport.Contains("GUI  V3.11  Triode Quick Test", StringComparison.Ordinal), "original Quick Test report header");
    Assert(originalReport.Contains("Vg  : -2", StringComparison.Ordinal), "original Quick Test signed grid value");
}
finally
{
    Directory.Delete(reportDirectory, recursive: true);
}

Console.WriteLine($"DATABASE: {info.CatalogVersion}; {info.ProfileCount} profiles; {info.ReadyProfileCount} ready");
Console.WriteLine("SELFTEST PASSED");
