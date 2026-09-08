using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using uTracerProManager.Core.Models;
using uTracerProManager.Core.Services;
using uTracerProManager.Services;

internal static class DatabaseEditorRegressionSelfTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "utracer-editor-v131-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = """
CREATE TABLE measurement_profiles (
 id TEXT PRIMARY KEY, display_name TEXT NOT NULL, family TEXT NOT NULL, aliases_json TEXT NOT NULL, tube_types TEXT NOT NULL, manufacturer_scope TEXT NOT NULL,
 pinout TEXT NOT NULL, critical_warning TEXT NOT NULL, heater_voltage REAL NOT NULL, heater_current_amp REAL NOT NULL, anode_voltage REAL NOT NULL,
 screen_voltage REAL NOT NULL, grid_voltage REAL NOT NULL, nominal_anode_current_ma REAL NOT NULL, nominal_screen_current_ma REAL NOT NULL,
 nominal_gm_ma_v REAL NOT NULL, nominal_mu REAL NOT NULL, nominal_rp_kohm REAL NOT NULL, max_anode_voltage REAL NOT NULL, max_screen_voltage REAL NOT NULL,
 max_anode_power_w REAL NOT NULL, max_screen_power_w REAL NOT NULL, anode_compliance_ma REAL NOT NULL, screen_compliance_ma REAL NOT NULL,
 warmup_seconds INTEGER NOT NULL, measurement_purpose TEXT NOT NULL, source_title TEXT NOT NULL, source_url TEXT NOT NULL, source_page TEXT NOT NULL,
 extraction_status TEXT NOT NULL, approved_for_hardware INTEGER NOT NULL, counts_for_condition_percent INTEGER NOT NULL, curve_va_start_v REAL NOT NULL,
 curve_va_stop_v REAL NOT NULL, curve_va_step_v REAL NOT NULL, curve_grid_voltages TEXT NOT NULL, notes TEXT NOT NULL,
 heater_supply_mode TEXT NOT NULL DEFAULT 'INTERNAL_OK', heater_supply_note TEXT NOT NULL DEFAULT ''
);
CREATE TABLE profile_hardware_compatibility(profile_id TEXT NOT NULL,hardware_id TEXT NOT NULL,status TEXT NOT NULL,short_label TEXT NOT NULL,reason TEXT NOT NULL,usable_curve_stop_v REAL NOT NULL,usable_current_ma REAL NOT NULL,requires_manual_confirmation INTEGER NOT NULL DEFAULT 0,PRIMARY KEY(profile_id,hardware_id));
CREATE TABLE catalog_info(key TEXT PRIMARY KEY,value TEXT NOT NULL);
INSERT INTO catalog_info VALUES('catalog_version','3.00.0'),('database_version','3.00.0'),('ready_profile_count','0'),('blocked_profile_count','1');
INSERT INTO measurement_profiles VALUES('EDIT-1','Editor selftest','Trioda','[]','12AX7','SELFTEST','', '',0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,60,'','','','', 'PENDING',0,0,0,0,0,'','', 'INTERNAL_OK','');
""";
                cmd.ExecuteNonQuery();
            }

            var service = new DatabaseProfileEditorService(path);
            var draft = service.LoadAsync("EDIT-1").GetAwaiter().GetResult();
            Assert(!draft.HeaterVoltage.HasValue && string.IsNullOrWhiteSpace(draft.Pinout), "blocked profile must expose unknown values as empty draft fields");
            service.SaveDraftAsync(draft).GetAwaiter().GetResult();
            var invalid = ProfileReadyValidator.Validate(draft, HardwareCapabilities.StockSafe);
            Assert(!invalid.IsReady, "incomplete draft must never become READY");

            var complete = draft with
            {
                Pinout = "1=a,2=g1,3=k,4=f,5=f",
                HeaterVoltage = 6.3, HeaterCurrentAmp = 0.3, AnodeVoltage = 100, ScreenVoltage = 0, GridVoltage = -2,
                NominalAnodeCurrentMa = 1.2, NominalGmMaV = 1.6, MaxAnodeVoltage = 300, MaxAnodePowerW = 1.2,
                AnodeComplianceMa = 12, ScreenComplianceMa = 12, WarmupSeconds = 60,
                CurveVaStartV = 20, CurveVaStopV = 120, CurveVaStepV = 10, CurveGridVoltages = "-1;-2;-3",
                SourceTitle = "Selftest datasheet", SourceUrl = "https://example.invalid/selftest.pdf", SourcePage = "1",
                SourceIdentityConfirmed = true, PinoutConfirmed = true, HeaterConfirmed = true, OperatingPointConfirmed = true,
                LimitsConfirmed = true, PowerGuardConfirmed = true, HardwareConfirmed = true,
                Reviewer = "SELFTEST", ReviewNote = "Values verified for regression test."
            };
            var promoted = service.PromoteReadyAsync(complete, HardwareCapabilities.StockSafe).GetAwaiter().GetResult();
            Assert(promoted.IsReady && promoted.HardwareStatus == "READY", "complete verified draft must promote to READY for stock 3+");

            using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            verify.Open();
            using var check = verify.CreateCommand();
            check.CommandText = "SELECT approved_for_hardware FROM measurement_profiles WHERE id='EDIT-1'";
            Assert(Convert.ToInt32(check.ExecuteScalar()) == 1, "promotion must update data readiness");
            check.CommandText = "SELECT COUNT(*) FROM profile_hardware_compatibility WHERE profile_id='EDIT-1'";
            Assert(Convert.ToInt32(check.ExecuteScalar()) == HardwareCapabilities.All.Count, "promotion must fill complete hardware matrix");
            check.CommandText = "SELECT COUNT(*) FROM profile_edit_audit_v131 WHERE profile_id='EDIT-1' AND action='PROMOTE_READY'";
            Assert(Convert.ToInt32(check.ExecuteScalar()) == 1, "promotion must write audit entry");
            check.CommandText = "SELECT COUNT(*) FROM profile_approval_history_v2 WHERE profile_id='EDIT-1' AND decision='USER_APPROVED'";
            Assert(Convert.ToInt32(check.ExecuteScalar()) == 1, "promotion must write approval history");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("DATABASE EDITOR SELFTEST FAILED: " + message);
    }
}
