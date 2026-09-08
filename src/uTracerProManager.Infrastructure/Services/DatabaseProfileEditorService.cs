using System.Text.Json;
using Microsoft.Data.Sqlite;
using uTracerProManager.Core.Models;
using uTracerProManager.Core.Services;

namespace uTracerProManager.Services;

public sealed class DatabaseProfileEditorService
{
    private readonly string _databasePath;
    private static readonly string[] HardwareIds = HardwareCapabilities.All.Select(x => x.DatabaseId).ToArray();

    public DatabaseProfileEditorService(string databasePath) => _databasePath = Path.GetFullPath(databasePath);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var con = CreateConnection();
        await con.OpenAsync(cancellationToken);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
CREATE TABLE IF NOT EXISTS profile_manual_workspace_v268 (
    profile_id TEXT PRIMARY KEY, work_order INTEGER NOT NULL DEFAULT 0, queue_class TEXT NOT NULL DEFAULT 'USER_EDIT_V131',
    model TEXT NOT NULL DEFAULT '', manufacturer TEXT NOT NULL DEFAULT '', source_url TEXT NOT NULL DEFAULT '', source_sha256 TEXT NULL,
    source_page_count INTEGER NULL, source_signal_count INTEGER NOT NULL DEFAULT 0, source_signals_json TEXT NOT NULL DEFAULT '{}',
    previous_extraction_status TEXT NOT NULL DEFAULT '', suggested_values_json TEXT NOT NULL DEFAULT '{}', suggested_values_warning TEXT NOT NULL DEFAULT '',
    pinout TEXT NULL, heater_voltage REAL NULL, heater_current_amp REAL NULL, anode_voltage REAL NULL, screen_voltage REAL NULL, grid_voltage REAL NULL,
    nominal_anode_current_ma REAL NULL, nominal_screen_current_ma REAL NULL, nominal_gm_ma_v REAL NULL, nominal_mu REAL NULL, nominal_rp_kohm REAL NULL,
    max_anode_voltage REAL NULL, max_screen_voltage REAL NULL, max_anode_power_w REAL NULL, max_screen_power_w REAL NULL, source_pages TEXT NULL,
    source_identity_ok INTEGER NOT NULL DEFAULT 0, pinout_ok INTEGER NOT NULL DEFAULT 0, heater_ok INTEGER NOT NULL DEFAULT 0,
    operating_point_ok INTEGER NOT NULL DEFAULT 0, limits_ok INTEGER NOT NULL DEFAULT 0, power_guard_ok INTEGER NOT NULL DEFAULT 0,
    hardware_ok INTEGER NOT NULL DEFAULT 0, reviewer TEXT NULL, review_note TEXT NULL, review_status TEXT NOT NULL DEFAULT 'DO_UZUPELNIENIA', activated_utc TEXT NULL
);
CREATE TABLE IF NOT EXISTS profile_edit_draft_v131 (
    profile_id TEXT PRIMARY KEY, payload_json TEXT NOT NULL, reviewer TEXT NOT NULL DEFAULT '', review_note TEXT NOT NULL DEFAULT '', updated_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS profile_edit_audit_v131 (
    id INTEGER PRIMARY KEY AUTOINCREMENT, profile_id TEXT NOT NULL, hardware_id TEXT NOT NULL, action TEXT NOT NULL,
    before_json TEXT NOT NULL, after_json TEXT NOT NULL, actor TEXT NOT NULL, note TEXT NOT NULL, created_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_profile_edit_audit_v131_profile ON profile_edit_audit_v131(profile_id, created_utc);
CREATE TABLE IF NOT EXISTS profile_approval_history_v2 (
    id INTEGER PRIMARY KEY AUTOINCREMENT, profile_id TEXT NOT NULL, hardware_id TEXT NOT NULL, data_status TEXT NOT NULL,
    hardware_status TEXT NOT NULL, decision TEXT NOT NULL, reason TEXT NOT NULL, source TEXT NOT NULL, actor TEXT NOT NULL, created_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_profile_approval_history_v2_profile ON profile_approval_history_v2(profile_id, hardware_id, created_utc);
""";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ProfileEditDraft> LoadAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var con = CreateConnection();
        await con.OpenAsync(cancellationToken);
        await using (var draftCmd = con.CreateCommand())
        {
            draftCmd.CommandText = "SELECT payload_json FROM profile_edit_draft_v131 WHERE profile_id=$id";
            draftCmd.Parameters.AddWithValue("$id", profileId);
            var json = await draftCmd.ExecuteScalarAsync(cancellationToken) as string;
            if (!string.IsNullOrWhiteSpace(json))
            {
                var stored = JsonSerializer.Deserialize<ProfileEditDraft>(json);
                if (stored is not null) return stored;
            }
        }

        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
SELECT p.id,p.display_name,p.family,p.tube_types,p.manufacturer_scope,p.pinout,p.critical_warning,
 p.heater_voltage,p.heater_current_amp,p.anode_voltage,p.screen_voltage,p.grid_voltage,p.nominal_anode_current_ma,p.nominal_screen_current_ma,
 p.nominal_gm_ma_v,p.nominal_mu,p.nominal_rp_kohm,p.max_anode_voltage,p.max_screen_voltage,p.max_anode_power_w,p.max_screen_power_w,
 p.anode_compliance_ma,p.screen_compliance_ma,p.warmup_seconds,p.curve_va_start_v,p.curve_va_stop_v,p.curve_va_step_v,p.curve_grid_voltages,
 p.measurement_purpose,p.source_title,p.source_url,p.source_page,p.notes,p.heater_supply_mode,p.heater_supply_note,
 w.pinout,w.heater_voltage,w.heater_current_amp,w.anode_voltage,w.screen_voltage,w.grid_voltage,w.nominal_anode_current_ma,w.nominal_screen_current_ma,
 w.nominal_gm_ma_v,w.nominal_mu,w.nominal_rp_kohm,w.max_anode_voltage,w.max_screen_voltage,w.max_anode_power_w,w.max_screen_power_w,
 w.reviewer,w.review_note,w.source_identity_ok,w.pinout_ok,w.heater_ok,w.operating_point_ok,w.limits_ok,w.power_guard_ok,w.hardware_ok
FROM measurement_profiles p LEFT JOIN profile_manual_workspace_v268 w ON w.profile_id=p.id WHERE p.id=$id;
""";
        cmd.Parameters.AddWithValue("$id", profileId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) throw new KeyNotFoundException($"Nie znaleziono profilu {profileId}.");
        double? N(int i, bool zeroIsMissing=true) { if (r.IsDBNull(i)) return null; var v=r.GetDouble(i); return zeroIsMissing && Math.Abs(v)<1e-12 ? null:v; }
        string? S(int i)=>r.IsDBNull(i)?null:r.GetString(i);
        double? Prefer(int wi,int bi,bool zeroIsMissing=true)=>!r.IsDBNull(wi)?N(wi,zeroIsMissing):N(bi,zeroIsMissing);
        return new ProfileEditDraft {
            ProfileId=r.GetString(0),DisplayName=r.GetString(1),Family=r.GetString(2),TubeTypes=r.GetString(3),ManufacturerScope=r.GetString(4),
            Pinout=!string.IsNullOrWhiteSpace(S(35))?S(35):S(5),CriticalWarning=r.GetString(6),HeaterVoltage=Prefer(36,7),HeaterCurrentAmp=Prefer(37,8),
            AnodeVoltage=Prefer(38,9),ScreenVoltage=Prefer(39,10,false),GridVoltage=Prefer(40,11,false),NominalAnodeCurrentMa=Prefer(41,12),
            NominalScreenCurrentMa=Prefer(42,13,false),NominalGmMaV=Prefer(43,14),NominalMu=Prefer(44,15),NominalRpKohm=Prefer(45,16),
            MaxAnodeVoltage=Prefer(46,17),MaxScreenVoltage=Prefer(47,18,false),MaxAnodePowerW=Prefer(48,19),MaxScreenPowerW=Prefer(49,20,false),
            AnodeComplianceMa=N(21),ScreenComplianceMa=N(22,false),WarmupSeconds=r.GetInt32(23),CurveVaStartV=N(24,false),CurveVaStopV=N(25,false),
            CurveVaStepV=N(26,false),CurveGridVoltages=S(27),MeasurementPurpose=r.GetString(28),SourceTitle=r.GetString(29),SourceUrl=r.GetString(30),
            SourcePage=r.GetString(31),Notes=r.GetString(32),HeaterSupplyMode=r.GetString(33),HeaterSupplyNote=r.GetString(34),Reviewer=S(50)??string.Empty,
            ReviewNote=S(51)??string.Empty,SourceIdentityConfirmed=!r.IsDBNull(52)&&r.GetInt64(52)!=0,PinoutConfirmed=!r.IsDBNull(53)&&r.GetInt64(53)!=0,
            HeaterConfirmed=!r.IsDBNull(54)&&r.GetInt64(54)!=0,OperatingPointConfirmed=!r.IsDBNull(55)&&r.GetInt64(55)!=0,LimitsConfirmed=!r.IsDBNull(56)&&r.GetInt64(56)!=0,
            PowerGuardConfirmed=!r.IsDBNull(57)&&r.GetInt64(57)!=0,HardwareConfirmed=!r.IsDBNull(58)&&r.GetInt64(58)!=0 };
    }

    public async Task SaveDraftAsync(ProfileEditDraft draft, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var json=JsonSerializer.Serialize(draft);
        await using var con=CreateConnection(); await con.OpenAsync(cancellationToken);
        await using (var cmd=con.CreateCommand())
        {
            cmd.CommandText="""
INSERT INTO profile_edit_draft_v131(profile_id,payload_json,reviewer,review_note,updated_utc)
VALUES($id,$json,$reviewer,$note,$utc)
ON CONFLICT(profile_id) DO UPDATE SET payload_json=excluded.payload_json,reviewer=excluded.reviewer,review_note=excluded.review_note,updated_utc=excluded.updated_utc;
""";
            cmd.Parameters.AddWithValue("$id",draft.ProfileId);cmd.Parameters.AddWithValue("$json",json);cmd.Parameters.AddWithValue("$reviewer",draft.Reviewer??"");cmd.Parameters.AddWithValue("$note",draft.ReviewNote??"");cmd.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var workspace=con.CreateCommand();
        workspace.CommandText="""
INSERT INTO profile_manual_workspace_v268(profile_id,work_order,queue_class,model,manufacturer,source_url,previous_extraction_status,pinout,heater_voltage,heater_current_amp,anode_voltage,screen_voltage,grid_voltage,nominal_anode_current_ma,nominal_screen_current_ma,nominal_gm_ma_v,nominal_mu,nominal_rp_kohm,max_anode_voltage,max_screen_voltage,max_anode_power_w,max_screen_power_w,source_pages,source_identity_ok,pinout_ok,heater_ok,operating_point_ok,limits_ok,power_guard_ok,hardware_ok,reviewer,review_note,review_status)
VALUES($id,(SELECT COALESCE(MAX(work_order),0)+1 FROM profile_manual_workspace_v268),'USER_EDIT_V131',$model,$mfr,$url,'USER_EDIT_V131',$pin,$hv,$hi,$va,$vs,$vg,$ia,$is,$gm,$mu,$rp,$vamax,$vsmax,$pa,$ps,$pages,$sourceok,$pinok,$heaterok,$pointok,$limitsok,$powerok,$hwok,$reviewer,$note,'DO_UZUPELNIENIA')
ON CONFLICT(profile_id) DO UPDATE SET queue_class='USER_EDIT_V131',model=excluded.model,manufacturer=excluded.manufacturer,source_url=excluded.source_url,pinout=excluded.pinout,heater_voltage=excluded.heater_voltage,heater_current_amp=excluded.heater_current_amp,anode_voltage=excluded.anode_voltage,screen_voltage=excluded.screen_voltage,grid_voltage=excluded.grid_voltage,nominal_anode_current_ma=excluded.nominal_anode_current_ma,nominal_screen_current_ma=excluded.nominal_screen_current_ma,nominal_gm_ma_v=excluded.nominal_gm_ma_v,nominal_mu=excluded.nominal_mu,nominal_rp_kohm=excluded.nominal_rp_kohm,max_anode_voltage=excluded.max_anode_voltage,max_screen_voltage=excluded.max_screen_voltage,max_anode_power_w=excluded.max_anode_power_w,max_screen_power_w=excluded.max_screen_power_w,source_pages=excluded.source_pages,source_identity_ok=excluded.source_identity_ok,pinout_ok=excluded.pinout_ok,heater_ok=excluded.heater_ok,operating_point_ok=excluded.operating_point_ok,limits_ok=excluded.limits_ok,power_guard_ok=excluded.power_guard_ok,hardware_ok=excluded.hardware_ok,reviewer=excluded.reviewer,review_note=excluded.review_note,review_status='DO_UZUPELNIENIA';
""";
        void W(string n,object? v)=>workspace.Parameters.AddWithValue(n,v??DBNull.Value);
        W("$id",draft.ProfileId);W("$model",draft.TubeTypes);W("$mfr",draft.ManufacturerScope);W("$url",draft.SourceUrl);W("$pin",draft.Pinout);W("$hv",draft.HeaterVoltage);W("$hi",draft.HeaterCurrentAmp);W("$va",draft.AnodeVoltage);W("$vs",draft.ScreenVoltage);W("$vg",draft.GridVoltage);W("$ia",draft.NominalAnodeCurrentMa);W("$is",draft.NominalScreenCurrentMa);W("$gm",draft.NominalGmMaV);W("$mu",draft.NominalMu);W("$rp",draft.NominalRpKohm);W("$vamax",draft.MaxAnodeVoltage);W("$vsmax",draft.MaxScreenVoltage);W("$pa",draft.MaxAnodePowerW);W("$ps",draft.MaxScreenPowerW);W("$pages",draft.SourcePage);W("$sourceok",draft.SourceIdentityConfirmed?1:0);W("$pinok",draft.PinoutConfirmed?1:0);W("$heaterok",draft.HeaterConfirmed?1:0);W("$pointok",draft.OperatingPointConfirmed?1:0);W("$limitsok",draft.LimitsConfirmed?1:0);W("$powerok",draft.PowerGuardConfirmed?1:0);W("$hwok",draft.HardwareConfirmed?1:0);W("$reviewer",draft.Reviewer);W("$note",draft.ReviewNote);
        await workspace.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ProfileEditValidationResult> PromoteReadyAsync(ProfileEditDraft draft, HardwareCapabilities hardware, CancellationToken cancellationToken=default)
    {
        var validation=ProfileReadyValidator.Validate(draft,hardware);
        if(!validation.IsReady) return validation;
        await InitializeAsync(cancellationToken);
        await using var con=CreateConnection(); await con.OpenAsync(cancellationToken); using var tx=con.BeginTransaction();
        string before;
        await using(var read=con.CreateCommand()) { read.Transaction=tx;read.CommandText="SELECT json_object('id',id,'display_name',display_name,'approved',approved_for_hardware,'extraction_status',extraction_status,'notes',notes) FROM measurement_profiles WHERE id=$id";read.Parameters.AddWithValue("$id",draft.ProfileId);before=(string?)await read.ExecuteScalarAsync(cancellationToken)??"{}"; }
        await using(var cmd=con.CreateCommand())
        {
            cmd.Transaction=tx;cmd.CommandText="""
UPDATE measurement_profiles SET display_name=$name,family=$family,tube_types=$types,manufacturer_scope=$mfr,pinout=$pinout,critical_warning=$warn,
heater_voltage=$hv,heater_current_amp=$hi,anode_voltage=$va,screen_voltage=$vs,grid_voltage=$vg,nominal_anode_current_ma=$ia,nominal_screen_current_ma=$is,
nominal_gm_ma_v=$gm,nominal_mu=$mu,nominal_rp_kohm=$rp,max_anode_voltage=$vamax,max_screen_voltage=$vsmax,max_anode_power_w=$paw,max_screen_power_w=$psw,
anode_compliance_ma=$iac,screen_compliance_ma=$isc,warmup_seconds=$warm,curve_va_start_v=$cstart,curve_va_stop_v=$cstop,curve_va_step_v=$cstep,curve_grid_voltages=$cgrid,
measurement_purpose=$purpose,source_title=$stitle,source_url=$surl,source_page=$spage,notes=$notes,heater_supply_mode=$hmode,heater_supply_note=$hnote,
extraction_status='USER_CONFIRMED_V131',approved_for_hardware=1,counts_for_condition_percent=CASE WHEN $gm>0 THEN 1 ELSE 0 END WHERE id=$id;
""";
            void P(string n,object? v)=>cmd.Parameters.AddWithValue(n,v??DBNull.Value);
            P("$id",draft.ProfileId);P("$name",draft.DisplayName);P("$family",draft.Family);P("$types",draft.TubeTypes);P("$mfr",draft.ManufacturerScope);P("$pinout",draft.Pinout);P("$warn",draft.CriticalWarning);P("$hv",draft.HeaterVoltage);P("$hi",draft.HeaterCurrentAmp);P("$va",draft.AnodeVoltage);P("$vs",draft.ScreenVoltage??0);P("$vg",draft.GridVoltage??0);P("$ia",draft.NominalAnodeCurrentMa);P("$is",draft.NominalScreenCurrentMa??0);P("$gm",draft.NominalGmMaV??0);P("$mu",draft.NominalMu??0);P("$rp",draft.NominalRpKohm??0);P("$vamax",draft.MaxAnodeVoltage);P("$vsmax",draft.MaxScreenVoltage??0);P("$paw",draft.MaxAnodePowerW);P("$psw",draft.MaxScreenPowerW??0);P("$iac",draft.AnodeComplianceMa);P("$isc",draft.ScreenComplianceMa??draft.AnodeComplianceMa);P("$warm",draft.WarmupSeconds);P("$cstart",draft.CurveVaStartV??0);P("$cstop",draft.CurveVaStopV??draft.AnodeVoltage);P("$cstep",draft.CurveVaStepV??Math.Max(1,(draft.AnodeVoltage??10)/10));P("$cgrid",draft.CurveGridVoltages??(draft.GridVoltage?.ToString(System.Globalization.CultureInfo.InvariantCulture)??""));P("$purpose",draft.MeasurementPurpose);P("$stitle",draft.SourceTitle);P("$surl",draft.SourceUrl);P("$spage",draft.SourcePage);P("$notes",draft.Notes);P("$hmode",draft.HeaterSupplyMode);P("$hnote",draft.HeaterSupplyNote);
            if(await cmd.ExecuteNonQueryAsync(cancellationToken)!=1) throw new InvalidOperationException("Nie zaktualizowano profilu.");
        }
        foreach(var id in HardwareIds)
        {
            await using var ensure=con.CreateCommand();ensure.Transaction=tx;ensure.CommandText="INSERT OR IGNORE INTO profile_hardware_compatibility(profile_id,hardware_id,status,short_label,reason,usable_curve_stop_v,usable_current_ma,requires_manual_confirmation) VALUES($p,$h,'BLOCKED','BRAK WERYFIKACJI','Brak zatwierdzonej zgodności sprzętowej — v1.3.1.',0,0,1);";ensure.Parameters.AddWithValue("$p",draft.ProfileId);ensure.Parameters.AddWithValue("$h",id);await ensure.ExecuteNonQueryAsync(cancellationToken);
        }
        await using(var hw=con.CreateCommand())
        {
            hw.Transaction=tx;hw.CommandText="UPDATE profile_hardware_compatibility SET status=$status,short_label=$label,reason=$reason,usable_curve_stop_v=$curve,usable_current_ma=$current,requires_manual_confirmation=$confirm WHERE profile_id=$p AND hardware_id=$h;";
            hw.Parameters.AddWithValue("$status",validation.HardwareStatus);hw.Parameters.AddWithValue("$label",validation.HardwareStatus=="READY"?"GOTOWY":validation.HardwareStatus=="READY_EXTERNAL_HEATER"?"ZEWNĘTRZNE ŻARZENIE":"ZABLOKOWANY");hw.Parameters.AddWithValue("$reason",validation.HardwareReason);hw.Parameters.AddWithValue("$curve",Math.Min(draft.CurveVaStopV??draft.AnodeVoltage??0,hardware.MaxAnodeVoltage));hw.Parameters.AddWithValue("$current",Math.Min(draft.AnodeComplianceMa??0,hardware.MaxPulseCurrentMa));hw.Parameters.AddWithValue("$confirm",validation.HardwareStatus=="READY"?0:1);hw.Parameters.AddWithValue("$p",draft.ProfileId);hw.Parameters.AddWithValue("$h",hardware.DatabaseId);await hw.ExecuteNonQueryAsync(cancellationToken);
        }
        await using(var work=con.CreateCommand()) { work.Transaction=tx;work.CommandText="UPDATE profile_manual_workspace_v268 SET review_status='READY_USER_CONFIRMED',activated_utc=$utc,reviewer=$actor,review_note=$note,source_identity_ok=1,pinout_ok=1,heater_ok=1,operating_point_ok=1,limits_ok=1,power_guard_ok=1,hardware_ok=1 WHERE profile_id=$p";work.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O"));work.Parameters.AddWithValue("$actor",draft.Reviewer);work.Parameters.AddWithValue("$note",draft.ReviewNote);work.Parameters.AddWithValue("$p",draft.ProfileId);await work.ExecuteNonQueryAsync(cancellationToken); }
        await using(var audit=con.CreateCommand()) { audit.Transaction=tx;audit.CommandText="INSERT INTO profile_edit_audit_v131(profile_id,hardware_id,action,before_json,after_json,actor,note,created_utc) VALUES($p,$h,'PROMOTE_READY',$b,$a,$actor,$note,$utc);";audit.Parameters.AddWithValue("$p",draft.ProfileId);audit.Parameters.AddWithValue("$h",hardware.DatabaseId);audit.Parameters.AddWithValue("$b",before);audit.Parameters.AddWithValue("$a",JsonSerializer.Serialize(draft));audit.Parameters.AddWithValue("$actor",draft.Reviewer);audit.Parameters.AddWithValue("$note",draft.ReviewNote);audit.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O"));await audit.ExecuteNonQueryAsync(cancellationToken); }
        await using(var hist=con.CreateCommand()) { hist.Transaction=tx;hist.CommandText="INSERT INTO profile_approval_history_v2(profile_id,hardware_id,data_status,hardware_status,decision,reason,source,actor,created_utc) VALUES($p,$h,'READY',$hs,'USER_APPROVED',$reason,'DATABASE_EDITOR_V131',$actor,$utc);";hist.Parameters.AddWithValue("$p",draft.ProfileId);hist.Parameters.AddWithValue("$h",hardware.DatabaseId);hist.Parameters.AddWithValue("$hs",validation.HardwareStatus);hist.Parameters.AddWithValue("$reason",validation.HardwareReason+" "+draft.ReviewNote);hist.Parameters.AddWithValue("$actor",draft.Reviewer);hist.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O"));await hist.ExecuteNonQueryAsync(cancellationToken); }
        await using(var meta=con.CreateCommand()) { meta.Transaction=tx;meta.CommandText="INSERT OR REPLACE INTO catalog_info(key,value) VALUES('catalog_version','3.00.1'),('database_version','3.00.1'),('program_min_version','1.3.1'); UPDATE catalog_info SET value=(SELECT COUNT(*) FROM measurement_profiles WHERE approved_for_hardware=1) WHERE key='ready_profile_count'; UPDATE catalog_info SET value=(SELECT COUNT(*) FROM measurement_profiles WHERE approved_for_hardware=0) WHERE key='blocked_profile_count';";await meta.ExecuteNonQueryAsync(cancellationToken); }
        await using(var clear=con.CreateCommand()) { clear.Transaction=tx;clear.CommandText="DELETE FROM profile_edit_draft_v131 WHERE profile_id=$p";clear.Parameters.AddWithValue("$p",draft.ProfileId);await clear.ExecuteNonQueryAsync(cancellationToken); }
        tx.Commit();return validation;
    }

    private SqliteConnection CreateConnection()=>new(new SqliteConnectionStringBuilder{DataSource=_databasePath,Mode=SqliteOpenMode.ReadWrite,Cache=SqliteCacheMode.Shared}.ToString());
}
