using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using uTracerProManager.Core.Models;
using uTracerProManager.Services;

internal static class ProfileWorkQueueRegressionSelfTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "utracer-queue-v140-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            CreateCatalog(path);
            var queue = new ProfileWorkQueueService(path);
            queue.InitializeAsync().GetAwaiter().GetResult();
            SeedWorkspace(path);

            var summary = queue.RefreshAsync(HardwareCapabilities.StockSafe).GetAwaiter().GetResult();
            Assert(summary.TotalBlocked == 3, "all blocked profiles must be represented in the work queue");
            Assert(summary.ReadyToApprove == 1, "fully verified blocked draft must be surfaced as ready-to-approve, not auto-approved");
            Assert(summary.PowerBlocked == 1, "95% power violation must have its own queue flag");
            Assert(summary.SourcePdf >= 1 && summary.Pinout >= 1 && summary.Heater >= 1,
                "missing profile must expose source, pinout and heater gaps independently");

            var ready = queue.SearchAsync(HardwareCapabilities.StockSafe.DatabaseId, ProfileWorkQueueCategories.ReadyToApprove, "", 100)
                .GetAwaiter().GetResult();
            Assert(ready.Count == 1 && ready[0].ProfileId == "Q-READY", "ready-to-approve filter must return the verified candidate only");

            var missing = queue.SearchAsync(HardwareCapabilities.StockSafe.DatabaseId, ProfileWorkQueueCategories.All, "Q-MISS", 100)
                .GetAwaiter().GetResult();
            Assert(missing.Count == 1 && missing[0].MissingFlags.Contains("PINOUT", StringComparison.Ordinal),
                "text search must keep the incomplete profile visible with explicit missing flags");

            var editor = new DatabaseProfileEditorService(path);
            var draft = editor.LoadAsync("Q-MISS").GetAwaiter().GetResult() with
            {
                Pinout = "1=a,2=g1,3=k,4=f,5=f",
                HeaterVoltage = 6.3,
                HeaterCurrentAmp = 0.3,
                AnodeVoltage = 100,
                ScreenVoltage = 0,
                GridVoltage = -2,
                NominalAnodeCurrentMa = 1.2,
                MaxAnodeVoltage = 300,
                MaxAnodePowerW = 1.2,
                AnodeComplianceMa = 12,
                ScreenComplianceMa = 12,
                WarmupSeconds = 60,
                CurveVaStartV = 20,
                CurveVaStopV = 120,
                CurveVaStepV = 10,
                CurveGridVoltages = "-1;-2;-3",
                SourceTitle = "Selftest source",
                SourceUrl = "https://example.invalid/q-miss.pdf",
                SourcePage = "1",
                SourceIdentityConfirmed = true,
                PinoutConfirmed = true,
                HeaterConfirmed = true,
                OperatingPointConfirmed = true,
                LimitsConfirmed = true,
                PowerGuardConfirmed = true,
                HardwareConfirmed = true,
                Reviewer = "SELFTEST",
                ReviewNote = "Manual queue verification completed."
            };
            editor.SaveDraftAsync(draft).GetAwaiter().GetResult();
            var afterDraft = queue.RefreshProfileAsync("Q-MISS", HardwareCapabilities.StockSafe).GetAwaiter().GetResult();
            Assert(afterDraft.TotalBlocked == 3 && afterDraft.ReadyToApprove == 2,
                "saved draft must update queue readiness while leaving measurement_profiles BLOCKED");

            using (var verifyBlocked = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                verifyBlocked.Open();
                using var cmd = verifyBlocked.CreateCommand();
                cmd.CommandText = "SELECT approved_for_hardware FROM measurement_profiles WHERE id='Q-MISS'";
                Assert(Convert.ToInt32(cmd.ExecuteScalar()) == 0, "queue refresh must never auto-promote a draft");
            }

            var promoted = editor.PromoteReadyAsync(draft, HardwareCapabilities.StockSafe).GetAwaiter().GetResult();
            Assert(promoted.IsReady, "explicit editor approval must still be the only promotion path");
            var afterPromotion = queue.RefreshProfileAsync("Q-MISS", HardwareCapabilities.StockSafe).GetAwaiter().GetResult();
            Assert(afterPromotion.TotalBlocked == 2, "approved profile must disappear from blocked work queue");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    private static void CreateCatalog(string path)
    {
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
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
INSERT INTO catalog_info VALUES('catalog_version','3.00.1'),('database_version','3.00.1'),('ready_profile_count','0'),('blocked_profile_count','3');
INSERT INTO measurement_profiles VALUES
('Q-READY','Queue ready','Trioda','[]','12AX7','SELFTEST','legacy','',6.3,0.3,100,0,-2,1.2,0,1.6,100,62.5,300,0,1.2,0,12,12,60,'Selftest','Source','https://example.invalid/ready.pdf','1','PENDING',0,0,20,120,10,'-1;-2;-3','', 'INTERNAL_OK',''),
('Q-MISS','Queue missing','Trioda','[]','12AX7','SELFTEST','', '',0,0,0,0,0,0,0,0,0,0,300,0,0,0,0,0,60,'Selftest','Source','https://example.invalid/miss.pdf','1','PENDING',0,0,0,0,0,'','', 'INTERNAL_OK',''),
('Q-POWER','Queue power','Trioda','[]','12AX7','SELFTEST','legacy','',6.3,0.3,100,0,-2,20,0,1.6,100,62.5,300,0,1.5,0,25,25,60,'Selftest','Source','https://example.invalid/power.pdf','1','PENDING',0,0,20,120,10,'-1;-2;-3','', 'INTERNAL_OK','');
INSERT INTO profile_hardware_compatibility VALUES
('Q-READY','UTRACER3_PLUS_STOCK','BLOCKED','BRAK WERYFIKACJI','Awaiting explicit approval.',0,0,1),
('Q-MISS','UTRACER3_PLUS_STOCK','BLOCKED','BRAK WERYFIKACJI','Missing data.',0,0,1),
('Q-POWER','UTRACER3_PLUS_STOCK','BLOCKED','BLOKADA MOCY','95% power guard violation.',0,0,1);
""";
        cmd.ExecuteNonQuery();
    }

    private static void SeedWorkspace(string path)
    {
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
INSERT INTO profile_manual_workspace_v268(
 profile_id,work_order,queue_class,model,manufacturer,source_url,previous_extraction_status,pinout,heater_voltage,heater_current_amp,
 anode_voltage,screen_voltage,grid_voltage,nominal_anode_current_ma,nominal_screen_current_ma,nominal_gm_ma_v,nominal_mu,nominal_rp_kohm,
 max_anode_voltage,max_screen_voltage,max_anode_power_w,max_screen_power_w,source_pages,source_identity_ok,pinout_ok,heater_ok,operating_point_ok,
 limits_ok,power_guard_ok,hardware_ok,reviewer,review_note,review_status)
VALUES
('Q-READY',1,'A_EXACT_SOURCE_MANUAL_EXTRACTION','12AX7','SELFTEST','https://example.invalid/ready.pdf','PENDING','1=a,2=g1,3=k,4=f,5=f',6.3,0.3,100,0,-2,1.2,0,1.6,100,62.5,300,0,1.2,0,'1',1,1,1,1,1,1,1,'SELFTEST','Ready candidate verified.','DO_UZUPELNIENIA'),
('Q-POWER',2,'A_EXACT_SOURCE_MANUAL_EXTRACTION','12AX7','SELFTEST','https://example.invalid/power.pdf','PENDING','1=a,2=g1,3=k,4=f,5=f',6.3,0.3,100,0,-2,20,0,1.6,100,62.5,300,0,1.5,0,'1',1,1,1,1,1,1,1,'SELFTEST','Power guard regression.','DO_UZUPELNIENIA');
""";
        cmd.ExecuteNonQuery();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("PROFILE QUEUE SELFTEST FAILED: " + message);
    }
}
