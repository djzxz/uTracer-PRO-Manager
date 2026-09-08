using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using uTracerProManager.Core.Models;
using uTracerProManager.Core.Protocol;
using uTracerProManager.Core.Services;
using uTracerProManager.Services;

internal static class SafetyRegressionSelfTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        Assert(CurrentLimitCodes.FloorMilliAmps(100) == 100, "100 mA must remain 100 mA");
        Assert(CurrentLimitCodes.FloorMilliAmps(99) == 50, "99 mA must floor to 50 mA, never round up");
        Assert(CurrentLimitCodes.FloorMilliAmps(24) == 12, "24 mA must floor to 12 mA");
        AssertThrows<InvalidOperationException>(() => CurrentLimitCodes.FloorMilliAmps(6.99),
            "limits below the minimum hardware threshold must be rejected");

        var calibration = ValidCalibration();
        var profile = PowerTubeProfile();
        var request = new ReferenceMeasurementRequest(
            ReferenceMeasurementKind.AnodeSweepSteppedGrid,
            20, 300, 14, new[] { -2.0, -3.0 },
            250, 250, -2, 6.3, 43, 1,
            0, 100, 0, 60, false, false, false)
        {
            AnodeComplianceMa = 100,
            ScreenComplianceMa = 25,
            AnodeRangeIndex = 0,
            ScreenRangeIndex = 0
        };

        var plan = ReferenceMeasurementPlanValidator.BuildAndValidate(
            profile, HardwareCapabilities.StockSafe, calibration, request);
        Assert(plan.IsValidated, "reference scan plan must be explicitly validated");
        Assert(plan.AnodeSoftwareLimitMa == 100, "anode software limit must remain separate");
        Assert(plan.ScreenSoftwareLimitMa == 25, "screen software limit must remain separate");
        Assert(plan.HardwareComplianceMa <= 25,
            "shared hardware threshold must not exceed the safer electrode limit");
        Assert(plan.Targets.Count == 30, "2 curves x 15 axis points must be expanded before START");

        var positiveGrid = request with
        {
            Kind = ReferenceMeasurementKind.PositiveGridSweepSteppedAnode,
            XStart = 0.1,
            XStop = 5,
            Intervals = 4,
            SteppingValues = new[] { 150.0 },
            SpecialWiringConfirmed = true
        };
        var positiveTargets = ReferenceMeasurementPlanValidator.BuildTargets(positiveGrid);
        Assert(positiveTargets.All(point => point.PositiveGridViaScreen), "+Vg targets must be marked as SCREEN-routed");
        Assert(positiveTargets.All(point => point.Vg == 0), "+Vg must not create a positive normal GRID code");
        Assert(positiveTargets.Select(point => point.Vs).SequenceEqual(new[] { 0.1, 1.325, 2.55, 3.775, 5.0 }),
            "+Vg scan voltage must be carried by the SCREEN output");

        var externalProfile = PowerTubeProfile().withCompatibility("READY_EXTERNAL_HEATER");
        AssertThrows<InvalidOperationException>(() =>
            ReferenceMeasurementPlanValidator.BuildAndValidate(
                externalProfile, HardwareCapabilities.StockSafe, calibration, request),
            "external-heater profile must be blocked until separate heater is explicitly selected");

        var externalRequest = request with
        {
            ExternalHeater = true,
            ExternalHeaterSupplyConfirmed = true
        };
        var externalPlan = ReferenceMeasurementPlanValidator.BuildAndValidate(
            externalProfile, HardwareCapabilities.StockSafe, calibration, externalRequest);
        Assert(externalPlan.IsValidated, "confirmed external heater must allow plan validation");

        var stockAdapter = TracerProtocolAdapterFactory.For(HardwareCapabilities.StockSafe);
        Assert(stockAdapter.MeasurementVerified, "stock 3+ adapter must be measurement-capable");
        Assert(stockAdapter.SupportsPositiveGridViaScreen, "3+ adapter must expose +Vg through SCREEN wiring");
        Assert(stockAdapter.RangeCode(0) == 0x08, "PGA range Auto must map to 0x08");
        Assert(stockAdapter.AveragingCode(0) == 0x40, "auto averaging must map to 0x40");

        var nxtAdapter = TracerProtocolAdapterFactory.For(HardwareCapabilities.UTracerNxt);
        Assert(!nxtAdapter.MeasurementVerified, "NXT must stay catalog-only until physical protocol verification");
        AssertThrows<NotSupportedException>(nxtAdapter.EnsureMeasurementAllowed,
            "NXT measurement must remain blocked");

        VerifyAppendOnlyCurveMigration();
    }

    private static void VerifyAppendOnlyCurveMigration()
    {
        var path = Path.Combine(Path.GetTempPath(), "utracer-curves-v2-selftest-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE manual_history(id INTEGER PRIMARY KEY, note TEXT NOT NULL); INSERT INTO manual_history(note) VALUES('KEEP-ME');";
                command.ExecuteNonQuery();
            }

            var repository = new ReferenceCurveRepository(path);
            repository.InitializeAsync().GetAwaiter().GetResult();

            var series = new ReferenceCurveSeries(
                "SELFTEST-SERIES", "SELFTEST", "A", "CATALOG", "Selftest source",
                "https://example.invalid/selftest.pdf", "1", "ANODE_SWEEP",
                DateTimeOffset.UtcNow, "REVIEW", 0.8, "selftest-1", false,
                new[]
                {
                    new ReferenceCurveDataPoint(1, "Vg=-2", 100, 100, 250, 250, -2, 6.3, 10, 1, null, null, "OK", false),
                    new ReferenceCurveDataPoint(2, "Vg=-2", 200, 200, 250, 250, -2, 6.3, 20, 2, null, null, "OK", false)
                });
            repository.SaveAsync(series).GetAwaiter().GetResult();

            var matches = repository.FindMatchingAsync(new ReferenceCurveMatchRequest(
                "SELFTEST", "A", null, 250, -2, 6.3, 0.01)).GetAwaiter().GetResult();
            Assert(matches.Count == 1 && matches[0].Points.Count == 2,
                "curve v2 repository must save and reload a matching series");

            using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            verify.Open();
            using var check = verify.CreateCommand();
            check.CommandText = "SELECT note FROM manual_history WHERE id=1";
            Assert((string?)check.ExecuteScalar() == "KEEP-ME",
                "curve migration must not overwrite existing manual history");
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static CalibrationProfile ValidCalibration() => new()
    {
        DeviceName = "Safety regression",
        SourcePath = "SELFTEST",
        ImportedAt = DateTimeOffset.UtcNow,
        ImportedFromFile = true,
        PortName = "EMULATOR",
        CalibrationVersion = "2.0",
        CalibrationCompletedAt = DateTimeOffset.UtcNow,
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

    private static TubeProfile PowerTubeProfile() => new()
    {
        Id = "SELFTEST-EL34",
        DisplayName = "EL34 safety selftest",
        Family = "Pentoda mocy",
        TubeTypes = "EL34",
        ManufacturerScope = "SELFTEST",
        Pinout = "1=g3, 2=f, 3=a, 4=g2, 5=g1, 6=NC, 7=f, 8=k",
        HeaterVoltage = 6.3,
        HeaterCurrentAmp = 1.5,
        AnodeVoltage = 250,
        ScreenVoltage = 250,
        GridVoltage = -13.5,
        NominalAnodeCurrentMa = 70,
        NominalScreenCurrentMa = 10,
        NominalGmMaV = 11,
        MaxAnodeVoltage = 800,
        MaxScreenVoltage = 500,
        MaxAnodePowerW = 25,
        MaxScreenPowerW = 8,
        AnodeComplianceMa = 100,
        ScreenComplianceMa = 25,
        WarmupSeconds = 60,
        ApprovedForHardware = true,
        HardwareCompatibilityStatus = "READY",
        HardwareCompatibilityLabel = "GOTOWY"
    };

    private static TubeProfile withCompatibility(this TubeProfile source, string status) => new()
    {
        Id = source.Id + "-" + status,
        DisplayName = source.DisplayName,
        Family = source.Family,
        Aliases = source.Aliases,
        TubeTypes = source.TubeTypes,
        ManufacturerScope = source.ManufacturerScope,
        Pinout = source.Pinout,
        CriticalWarning = source.CriticalWarning,
        HeaterVoltage = source.HeaterVoltage,
        HeaterCurrentAmp = source.HeaterCurrentAmp,
        AnodeVoltage = source.AnodeVoltage,
        ScreenVoltage = source.ScreenVoltage,
        GridVoltage = source.GridVoltage,
        NominalAnodeCurrentMa = source.NominalAnodeCurrentMa,
        NominalScreenCurrentMa = source.NominalScreenCurrentMa,
        NominalGmMaV = source.NominalGmMaV,
        NominalMu = source.NominalMu,
        NominalRpKohm = source.NominalRpKohm,
        MaxAnodeVoltage = source.MaxAnodeVoltage,
        MaxScreenVoltage = source.MaxScreenVoltage,
        MaxAnodePowerW = source.MaxAnodePowerW,
        MaxScreenPowerW = source.MaxScreenPowerW,
        AnodeComplianceMa = source.AnodeComplianceMa,
        ScreenComplianceMa = source.ScreenComplianceMa,
        WarmupSeconds = source.WarmupSeconds,
        MeasurementPurpose = source.MeasurementPurpose,
        SourceTitle = source.SourceTitle,
        SourceUrl = source.SourceUrl,
        SourcePage = source.SourcePage,
        ExtractionStatus = source.ExtractionStatus,
        ApprovedForHardware = source.ApprovedForHardware,
        CountsForConditionPercent = source.CountsForConditionPercent,
        CurveVaStartV = source.CurveVaStartV,
        CurveVaStopV = source.CurveVaStopV,
        CurveVaStepV = source.CurveVaStepV,
        CurveGridVoltages = source.CurveGridVoltages,
        Notes = source.Notes,
        CatalogCompatibilityNote = source.CatalogCompatibilityNote,
        HardwareCompatibilityStatus = status,
        HardwareCompatibilityLabel = status,
        HardwareCompatibilityReason = source.HardwareCompatibilityReason,
        UsableCurveStopV = source.UsableCurveStopV,
        UsableCurrentMa = source.UsableCurrentMa,
        RequiresManualConfirmation = source.RequiresManualConfirmation
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("SAFETY REGRESSION SELFTEST FAILED: " + message);
    }

    private static void AssertThrows<TException>(Action action, string message)
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
        throw new InvalidOperationException("SAFETY REGRESSION SELFTEST FAILED: " + message);
    }
}
