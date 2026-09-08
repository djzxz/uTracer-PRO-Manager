using System.Text.Json;
using Microsoft.Data.Sqlite;
using uTracerProManager.Core.Models;
using uTracerProManager.Core.Services;

namespace uTracerProManager.Services;

public sealed class ProfileWorkQueueService
{
    private readonly string _databasePath;

    public ProfileWorkQueueService(string databasePath) => _databasePath = Path.GetFullPath(databasePath);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await new DatabaseProfileEditorService(_databasePath).InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
CREATE TABLE IF NOT EXISTS profile_work_queue_v140 (
    profile_id TEXT NOT NULL,
    hardware_id TEXT NOT NULL,
    primary_category TEXT NOT NULL,
    priority INTEGER NOT NULL,
    missing_flags TEXT NOT NULL,
    completeness_percent INTEGER NOT NULL,
    ready_candidate INTEGER NOT NULL,
    power_blocked INTEGER NOT NULL,
    hardware_status TEXT NOT NULL,
    review_status TEXT NOT NULL,
    queue_class TEXT NOT NULL,
    reason TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY(profile_id, hardware_id)
);
CREATE INDEX IF NOT EXISTS ix_profile_work_queue_v140_category
    ON profile_work_queue_v140(hardware_id, primary_category, priority DESC);
""";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ProfileWorkQueueSummary> RefreshAsync(
        HardwareCapabilities hardware,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        await InitializeAsync(cancellationToken);
        var items = (await LoadClassifiedAsync(hardware, null, cancellationToken)).ToArray();

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM profile_work_queue_v140 WHERE hardware_id=$hardware";
            delete.Parameters.AddWithValue("$hardware", hardware.DatabaseId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertItemsAsync(connection, transaction, hardware.DatabaseId, items, cancellationToken);
        transaction.Commit();
        return await GetSummaryAsync(hardware.DatabaseId, cancellationToken);
    }

    public async Task<ProfileWorkQueueSummary> RefreshProfileAsync(
        string profileId,
        HardwareCapabilities hardware,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(hardware);
        await InitializeAsync(cancellationToken);
        var items = (await LoadClassifiedAsync(hardware, profileId, cancellationToken)).ToArray();

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM profile_work_queue_v140 WHERE hardware_id=$hardware AND profile_id=$profile";
            delete.Parameters.AddWithValue("$hardware", hardware.DatabaseId);
            delete.Parameters.AddWithValue("$profile", profileId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertItemsAsync(connection, transaction, hardware.DatabaseId, items, cancellationToken);
        transaction.Commit();
        return await GetSummaryAsync(hardware.DatabaseId, cancellationToken);
    }

    public async Task<ProfileWorkQueueSummary> GetSummaryAsync(
        string hardwareId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        int[] counts;
        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
SELECT COUNT(*),
 SUM(CASE WHEN ready_candidate=1 THEN 1 ELSE 0 END),
 SUM(CASE WHEN missing_flags LIKE '%PDF/ŹRÓDŁO%' THEN 1 ELSE 0 END),
 SUM(CASE WHEN missing_flags LIKE '%PINOUT%' THEN 1 ELSE 0 END),
 SUM(CASE WHEN missing_flags LIKE '%ŻARZENIE%' THEN 1 ELSE 0 END),
 SUM(CASE WHEN missing_flags LIKE '%PUNKT PRACY%' THEN 1 ELSE 0 END),
 SUM(CASE WHEN missing_flags LIKE '%LIMITY%' THEN 1 ELSE 0 END),
 SUM(CASE WHEN missing_flags LIKE '%KRZYWA%' THEN 1 ELSE 0 END),
 SUM(CASE WHEN missing_flags LIKE '%SPRZĘT%' THEN 1 ELSE 0 END),
 SUM(CASE WHEN power_blocked=1 THEN 1 ELSE 0 END)
FROM profile_work_queue_v140 WHERE hardware_id=$hardware;
""";
            command.Parameters.AddWithValue("$hardware", hardwareId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            counts = Enumerable.Range(0, 10)
                .Select(i => reader.IsDBNull(i) ? 0 : Convert.ToInt32(reader.GetValue(i)))
                .ToArray();
        }
        var pending = await TableExistsAsync("reference_curve_targets_v2", cancellationToken)
            ? await CountPendingTargetsAsync(cancellationToken)
            : 0;
        return new ProfileWorkQueueSummary(counts[0],counts[1],counts[2],counts[3],counts[4],counts[5],counts[6],counts[7],counts[8],counts[9],pending);
    }

    private async Task<IReadOnlyList<ProfileWorkQueueItem>> LoadClassifiedAsync(
        HardwareCapabilities hardware,
        string? profileId,
        CancellationToken cancellationToken)
    {
        var hasLegacyQueue = await TableExistsAsync("profile_verification_queue", cancellationToken);
        var hasSourceVerification = await TableExistsAsync("profile_source_verification", cancellationToken);
        var rows = new List<QueueSourceRow>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = BuildSourceQuery(hasLegacyQueue, hasSourceVerification, profileId is not null);
        command.Parameters.AddWithValue("$hardware", hardware.DatabaseId);
        if (profileId is not null) command.Parameters.AddWithValue("$profile", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadSourceRow(reader));
        return rows.Select(row => Classify(row, hardware)).ToArray();
    }

    private static async Task InsertItemsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string hardwareId,
        IReadOnlyCollection<ProfileWorkQueueItem> items,
        CancellationToken cancellationToken)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
INSERT INTO profile_work_queue_v140(
 profile_id,hardware_id,primary_category,priority,missing_flags,completeness_percent,ready_candidate,power_blocked,
 hardware_status,review_status,queue_class,reason,updated_utc)
VALUES($profile,$hardware,$category,$priority,$missing,$complete,$ready,$power,$hwstatus,$review,$class,$reason,$utc);
""";
        foreach (var name in new[] { "$profile","$hardware","$category","$priority","$missing","$complete","$ready","$power","$hwstatus","$review","$class","$reason","$utc" })
            insert.Parameters.Add(new SqliteParameter(name, DBNull.Value));
        insert.Parameters["$hardware"].Value = hardwareId;
        foreach (var item in items)
        {
            insert.Parameters["$profile"].Value = item.ProfileId;
            insert.Parameters["$category"].Value = item.PrimaryCategory;
            insert.Parameters["$priority"].Value = item.Priority;
            insert.Parameters["$missing"].Value = item.MissingFlags;
            insert.Parameters["$complete"].Value = item.CompletenessPercent;
            insert.Parameters["$ready"].Value = item.ReadyCandidate ? 1 : 0;
            insert.Parameters["$power"].Value = item.PowerBlocked ? 1 : 0;
            insert.Parameters["$hwstatus"].Value = item.HardwareStatus;
            insert.Parameters["$review"].Value = item.ReviewStatus;
            insert.Parameters["$class"].Value = item.QueueClass;
            insert.Parameters["$reason"].Value = item.Reason;
            insert.Parameters["$utc"].Value = DateTimeOffset.UtcNow.ToString("O");
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProfileWorkQueueItem>> SearchAsync(
        string hardwareId,
        string? category,
        string? search,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        limit = Math.Clamp(limit, 1, 5000);
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? ProfileWorkQueueCategories.All : category.Trim();
        var normalizedSearch = search?.Trim() ?? string.Empty;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT q.profile_id,p.display_name,p.tube_types,p.manufacturer_scope,q.primary_category,q.missing_flags,q.priority,
       q.completeness_percent,q.hardware_status,q.review_status,q.queue_class,p.source_title,p.source_url,p.source_page,
       q.reason,q.ready_candidate,q.power_blocked
FROM profile_work_queue_v140 q
JOIN measurement_profiles p ON p.id=q.profile_id
WHERE q.hardware_id=$hardware
  AND ($category='WSZYSTKIE' OR q.primary_category=$category)
  AND ($search='' OR p.id LIKE '%'||$search||'%' COLLATE NOCASE
       OR p.display_name LIKE '%'||$search||'%' COLLATE NOCASE
       OR p.tube_types LIKE '%'||$search||'%' COLLATE NOCASE
       OR p.manufacturer_scope LIKE '%'||$search||'%' COLLATE NOCASE
       OR q.missing_flags LIKE '%'||$search||'%' COLLATE NOCASE)
ORDER BY q.ready_candidate DESC,q.priority DESC,q.completeness_percent DESC,p.display_name COLLATE NOCASE
LIMIT $limit;
""";
        command.Parameters.AddWithValue("$hardware", hardwareId);
        command.Parameters.AddWithValue("$category", normalizedCategory);
        command.Parameters.AddWithValue("$search", normalizedSearch);
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<ProfileWorkQueueItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProfileWorkQueueItem(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetString(8), reader.GetString(9),
                reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetString(13), reader.GetString(14),
                reader.GetInt32(15) != 0, reader.GetInt32(16) != 0));
        }
        return result;
    }

    private static ProfileWorkQueueItem Classify(QueueSourceRow row, HardwareCapabilities hardware)
    {
        ProfileEditDraft? savedDraft = null;
        if (!string.IsNullOrWhiteSpace(row.DraftJson))
        {
            try { savedDraft = JsonSerializer.Deserialize<ProfileEditDraft>(row.DraftJson); }
            catch { /* Uszkodzony szkic nie może podnieść gotowości. */ }
        }

        var hasWorkspace = row.WorkspaceExists;
        var pinout = savedDraft?.Pinout ?? EffectiveText(row.WorkspacePinout, hasWorkspace ? null : row.ProfilePinout);
        var heaterV = savedDraft?.HeaterVoltage ?? EffectiveNumber(row.WorkspaceHeaterVoltage, hasWorkspace ? null : row.ProfileHeaterVoltage);
        var heaterI = savedDraft?.HeaterCurrentAmp ?? EffectiveNumber(row.WorkspaceHeaterCurrent, hasWorkspace ? null : row.ProfileHeaterCurrent);
        var va = savedDraft?.AnodeVoltage ?? EffectiveNumber(row.WorkspaceAnodeVoltage, hasWorkspace ? null : row.ProfileAnodeVoltage);
        var vs = savedDraft?.ScreenVoltage ?? EffectiveNumberAllowZero(row.WorkspaceScreenVoltage, hasWorkspace ? null : row.ProfileScreenVoltage);
        var vg = savedDraft?.GridVoltage ?? EffectiveNumberAllowZero(row.WorkspaceGridVoltage, hasWorkspace ? null : row.ProfileGridVoltage);
        var ia = savedDraft?.NominalAnodeCurrentMa ?? EffectiveNumber(row.WorkspaceIa, hasWorkspace ? null : row.ProfileIa);
        var isCurrent = savedDraft?.NominalScreenCurrentMa ?? EffectiveNumberAllowZero(row.WorkspaceIs, hasWorkspace ? null : row.ProfileIs);
        var gm = savedDraft?.NominalGmMaV ?? EffectiveNumber(row.WorkspaceGm, hasWorkspace ? null : row.ProfileGm);
        var mu = savedDraft?.NominalMu ?? EffectiveNumber(row.WorkspaceMu, hasWorkspace ? null : row.ProfileMu);
        var rp = savedDraft?.NominalRpKohm ?? EffectiveNumber(row.WorkspaceRp, hasWorkspace ? null : row.ProfileRp);
        var vaMax = savedDraft?.MaxAnodeVoltage ?? EffectiveNumber(row.WorkspaceMaxAnodeVoltage, hasWorkspace ? null : row.ProfileMaxAnodeVoltage);
        var vsMax = savedDraft?.MaxScreenVoltage ?? EffectiveNumberAllowZero(row.WorkspaceMaxScreenVoltage, hasWorkspace ? null : row.ProfileMaxScreenVoltage);
        var paMax = savedDraft?.MaxAnodePowerW ?? EffectiveNumber(row.WorkspaceMaxAnodePower, hasWorkspace ? null : row.ProfileMaxAnodePower);
        var psMax = savedDraft?.MaxScreenPowerW ?? EffectiveNumberAllowZero(row.WorkspaceMaxScreenPower, hasWorkspace ? null : row.ProfileMaxScreenPower);
        var anodeCompliance = savedDraft?.AnodeComplianceMa ?? (row.ProfileAnodeCompliance > 0 ? row.ProfileAnodeCompliance : null);
        var screenCompliance = savedDraft?.ScreenComplianceMa ?? (row.ProfileScreenCompliance >= 0 ? row.ProfileScreenCompliance : null);
        var curveStart = savedDraft?.CurveVaStartV ?? row.ProfileCurveStart;
        var curveStop = savedDraft?.CurveVaStopV ?? row.ProfileCurveStop;
        var curveStep = savedDraft?.CurveVaStepV ?? row.ProfileCurveStep;
        var curveGrids = savedDraft?.CurveGridVoltages ?? row.ProfileCurveGridVoltages;
        var sourceTitle = savedDraft?.SourceTitle ?? row.SourceTitle;
        var sourceUrl = savedDraft?.SourceUrl ?? row.SourceUrl;
        var sourcePage = savedDraft?.SourcePage ?? row.SourcePage;
        var sourceIdentityConfirmed = savedDraft?.SourceIdentityConfirmed ?? row.SourceIdentityConfirmed;
        var pinoutConfirmed = savedDraft?.PinoutConfirmed ?? row.PinoutConfirmed;
        var heaterConfirmed = savedDraft?.HeaterConfirmed ?? row.HeaterConfirmed;
        var operatingPointConfirmed = savedDraft?.OperatingPointConfirmed ?? row.OperatingPointConfirmed;
        var limitsConfirmed = savedDraft?.LimitsConfirmed ?? row.LimitsConfirmed;
        var powerGuardConfirmed = savedDraft?.PowerGuardConfirmed ?? row.PowerGuardConfirmed;
        var hardwareConfirmed = savedDraft?.HardwareConfirmed ?? row.HardwareConfirmed;
        var reviewer = savedDraft?.Reviewer ?? row.Reviewer;
        var reviewNote = savedDraft?.ReviewNote ?? row.ReviewNote;

        var missing = new List<string>();
        var sourceExact = sourceIdentityConfirmed || IsExactSourceStatus(row.SourceIdentityStatus, row.VerificationStatus);
        var sourceMismatch = Contains(row.SourceIdentityStatus, "MISMATCH") || Contains(row.ReviewStatus, "MISMATCH");
        var sourceMetadataMissing = string.IsNullOrWhiteSpace(sourceTitle) || string.IsNullOrWhiteSpace(sourceUrl) || string.IsNullOrWhiteSpace(sourcePage);
        if (!sourceExact || sourceMismatch || sourceMetadataMissing) missing.Add("PDF/ŹRÓDŁO");
        if (string.IsNullOrWhiteSpace(pinout)) missing.Add("PINOUT");
        if (!Positive(heaterV) || !Positive(heaterI)) missing.Add("ŻARZENIE");
        if (!Positive(va) || !vg.HasValue || !Positive(ia)) missing.Add("PUNKT PRACY");
        if (!Positive(vaMax) || !Positive(paMax) || !Positive(anodeCompliance) ||
            (vs.GetValueOrDefault() > 0 && (!Positive(vsMax) || !Positive(screenCompliance))))
            missing.Add("LIMITY");
        if (curveStop.GetValueOrDefault() <= 0 || curveStep.GetValueOrDefault() <= 0 || string.IsNullOrWhiteSpace(curveGrids))
            missing.Add("KRZYWA");
        if (!hardwareConfirmed || string.Equals(row.HardwareStatus, "BLOCKED", StringComparison.OrdinalIgnoreCase))
            missing.Add("SPRZĘT");

        var powerBlocked = Contains(row.LegacyNextAction, "95%") || Contains(row.LegacyNextAction, "power reserve") ||
                           Contains(row.LegacyNextAction, "zapas mocy") || Contains(row.CriticalWarning, "95%");
        if (Positive(va) && Positive(ia) && Positive(paMax) && va!.Value * ia!.Value / 1000.0 > paMax!.Value * 0.95)
            powerBlocked = true;
        if (vs.GetValueOrDefault() > 0 && isCurrent.GetValueOrDefault() > 0 && psMax.GetValueOrDefault() > 0 &&
            vs!.Value * isCurrent!.Value / 1000.0 > psMax!.Value * 0.95)
            powerBlocked = true;
        if (powerBlocked) missing.Add("MOC 95%");

        var draft = savedDraft ?? new ProfileEditDraft
        {
            ProfileId = row.ProfileId,
            DisplayName = row.DisplayName,
            Family = row.Family,
            TubeTypes = row.TubeTypes,
            ManufacturerScope = row.ManufacturerScope,
            Pinout = pinout,
            CriticalWarning = row.CriticalWarning,
            HeaterVoltage = heaterV,
            HeaterCurrentAmp = heaterI,
            AnodeVoltage = va,
            ScreenVoltage = vs,
            GridVoltage = vg,
            NominalAnodeCurrentMa = ia,
            NominalScreenCurrentMa = isCurrent,
            NominalGmMaV = gm,
            NominalMu = mu,
            NominalRpKohm = rp,
            MaxAnodeVoltage = vaMax,
            MaxScreenVoltage = vsMax,
            MaxAnodePowerW = paMax,
            MaxScreenPowerW = psMax,
            AnodeComplianceMa = anodeCompliance,
            ScreenComplianceMa = screenCompliance,
            WarmupSeconds = row.ProfileWarmupSeconds,
            CurveVaStartV = curveStart,
            CurveVaStopV = curveStop,
            CurveVaStepV = curveStep,
            CurveGridVoltages = curveGrids,
            MeasurementPurpose = row.MeasurementPurpose,
            SourceTitle = sourceTitle,
            SourceUrl = sourceUrl,
            SourcePage = sourcePage,
            Notes = row.Notes,
            HeaterSupplyMode = row.HeaterSupplyMode,
            HeaterSupplyNote = row.HeaterSupplyNote,
            SourceIdentityConfirmed = sourceIdentityConfirmed,
            PinoutConfirmed = pinoutConfirmed,
            HeaterConfirmed = heaterConfirmed,
            OperatingPointConfirmed = operatingPointConfirmed,
            LimitsConfirmed = limitsConfirmed,
            PowerGuardConfirmed = powerGuardConfirmed,
            HardwareConfirmed = hardwareConfirmed,
            Reviewer = reviewer,
            ReviewNote = reviewNote
        };
        var validation = ProfileReadyValidator.Validate(draft, hardware);
        var readyCandidate = validation.IsReady;

        var primary = readyCandidate ? ProfileWorkQueueCategories.ReadyToApprove :
            powerBlocked ? ProfileWorkQueueCategories.PowerBlocked :
            (!sourceExact || sourceMismatch || sourceMetadataMissing) ? ProfileWorkQueueCategories.SourcePdf :
            string.IsNullOrWhiteSpace(pinout) ? ProfileWorkQueueCategories.Pinout :
            (!Positive(heaterV) || !Positive(heaterI)) ? ProfileWorkQueueCategories.Heater :
            (!Positive(va) || !vg.HasValue || !Positive(ia)) ? ProfileWorkQueueCategories.OperatingPoint :
            (!Positive(vaMax) || !Positive(paMax) || !Positive(anodeCompliance) ||
             (vs.GetValueOrDefault() > 0 && (!Positive(vsMax) || !Positive(screenCompliance)))) ? ProfileWorkQueueCategories.Limits :
            (curveStop.GetValueOrDefault() <= 0 || curveStep.GetValueOrDefault() <= 0 || string.IsNullOrWhiteSpace(curveGrids)) ? ProfileWorkQueueCategories.Curve :
            ProfileWorkQueueCategories.Hardware;

        var completed = 0;
        const int totalChecks = 8;
        if (sourceExact && !sourceMismatch && !sourceMetadataMissing) completed++;
        if (!string.IsNullOrWhiteSpace(pinout)) completed++;
        if (Positive(heaterV) && Positive(heaterI)) completed++;
        if (Positive(va) && vg.HasValue && Positive(ia)) completed++;
        if (Positive(vaMax) && Positive(paMax) && Positive(anodeCompliance)) completed++;
        if (curveStop.GetValueOrDefault() > 0 && curveStep.GetValueOrDefault() > 0 && !string.IsNullOrWhiteSpace(curveGrids)) completed++;
        if (!powerBlocked && powerGuardConfirmed) completed++;
        if (hardwareConfirmed && !string.Equals(row.HardwareStatus, "BLOCKED", StringComparison.OrdinalIgnoreCase)) completed++;
        var completeness = (int)Math.Round(completed * 100.0 / totalChecks);

        var priority = QueueClassPriority(row.QueueClass) + Math.Clamp(row.LegacyPriority, 0, 999);
        priority += readyCandidate ? 5000 : powerBlocked ? 4000 : primary == ProfileWorkQueueCategories.SourcePdf ? 1000 : 2000;
        var reasonParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.LegacyNextAction)) reasonParts.Add(row.LegacyNextAction);
        if (!string.IsNullOrWhiteSpace(row.HardwareReason)) reasonParts.Add(row.HardwareReason);
        if (validation.Errors.Count > 0) reasonParts.Add(string.Join("; ", validation.Errors.Take(3)));
        var reason = string.Join(" • ", reasonParts.Distinct()).Trim();
        if (string.IsNullOrWhiteSpace(reason)) reason = "Profil wymaga ręcznej weryfikacji przed READY.";

        return new ProfileWorkQueueItem(
            row.ProfileId, row.DisplayName, row.TubeTypes, row.ManufacturerScope, primary,
            missing.Count == 0 ? "BRAK — komplet do walidacji" : string.Join(", ", missing.Distinct()),
            priority, completeness, row.HardwareStatus, row.ReviewStatus, row.QueueClass,
            sourceTitle, sourceUrl, sourcePage, reason, readyCandidate, powerBlocked);
    }

    private async Task<int> CountPendingTargetsAsync(CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM reference_curve_targets_v2 WHERE status='PENDING'";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<bool> TableExistsAsync(string table, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static string BuildSourceQuery(bool hasLegacyQueue, bool hasSourceVerification, bool singleProfile)
    {
        var legacyJoin = hasLegacyQueue
            ? "LEFT JOIN profile_verification_queue q ON q.profile_id=p.id"
            : string.Empty;
        var sourceJoin = hasSourceVerification
            ? """
LEFT JOIN (
  SELECT v.* FROM profile_source_verification v
  JOIN (SELECT profile_id,MAX(id) AS max_id FROM profile_source_verification GROUP BY profile_id) latest
    ON latest.profile_id=v.profile_id AND latest.max_id=v.id
) sv ON sv.profile_id=p.id
"""
            : string.Empty;
        var legacyColumns = hasLegacyQueue
            ? "COALESCE(q.priority_score,0),COALESCE(q.next_action,'')"
            : "0,''";
        var sourceColumns = hasSourceVerification
            ? "COALESCE(sv.source_identity_status,''),COALESCE(sv.verification_status,'')"
            : "'',''";
        var profilePredicate = singleProfile ? " AND p.id=$profile" : string.Empty;

        return $"""
SELECT p.id,p.display_name,p.family,p.tube_types,p.manufacturer_scope,p.pinout,p.critical_warning,
 p.heater_voltage,p.heater_current_amp,p.anode_voltage,p.screen_voltage,p.grid_voltage,p.nominal_anode_current_ma,p.nominal_screen_current_ma,
 p.nominal_gm_ma_v,p.nominal_mu,p.nominal_rp_kohm,p.max_anode_voltage,p.max_screen_voltage,p.max_anode_power_w,p.max_screen_power_w,
 p.anode_compliance_ma,p.screen_compliance_ma,p.warmup_seconds,p.curve_va_start_v,p.curve_va_stop_v,p.curve_va_step_v,p.curve_grid_voltages,
 p.measurement_purpose,p.source_title,p.source_url,p.source_page,p.notes,p.heater_supply_mode,p.heater_supply_note,
 w.profile_id,w.pinout,w.heater_voltage,w.heater_current_amp,w.anode_voltage,w.screen_voltage,w.grid_voltage,w.nominal_anode_current_ma,w.nominal_screen_current_ma,
 w.nominal_gm_ma_v,w.nominal_mu,w.nominal_rp_kohm,w.max_anode_voltage,w.max_screen_voltage,w.max_anode_power_w,w.max_screen_power_w,
 COALESCE(w.source_identity_ok,0),COALESCE(w.pinout_ok,0),COALESCE(w.heater_ok,0),COALESCE(w.operating_point_ok,0),COALESCE(w.limits_ok,0),
 COALESCE(w.power_guard_ok,0),COALESCE(w.hardware_ok,0),COALESCE(w.reviewer,''),COALESCE(w.review_note,''),COALESCE(w.review_status,'BRAK_WORKSPACE'),COALESCE(w.queue_class,'BRAK_WORKSPACE'),
 COALESCE(c.status,'BLOCKED'),COALESCE(c.reason,'Brak wpisu zgodności sprzętowej.'),{legacyColumns},{sourceColumns},COALESCE(d.payload_json,'')
FROM measurement_profiles p
LEFT JOIN profile_manual_workspace_v268 w ON w.profile_id=p.id
LEFT JOIN profile_hardware_compatibility c ON c.profile_id=p.id AND c.hardware_id=$hardware
LEFT JOIN profile_edit_draft_v131 d ON d.profile_id=p.id
{legacyJoin}
{sourceJoin}
WHERE p.approved_for_hardware=0{profilePredicate};
""";
    }

    private static QueueSourceRow ReadSourceRow(SqliteDataReader r) => new(
        r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),
        r.GetDouble(7),r.GetDouble(8),r.GetDouble(9),r.GetDouble(10),r.GetDouble(11),r.GetDouble(12),r.GetDouble(13),r.GetDouble(14),r.GetDouble(15),r.GetDouble(16),
        r.GetDouble(17),r.GetDouble(18),r.GetDouble(19),r.GetDouble(20),r.GetDouble(21),r.GetDouble(22),r.GetInt32(23),r.GetDouble(24),r.GetDouble(25),r.GetDouble(26),r.GetString(27),
        r.GetString(28),r.GetString(29),r.GetString(30),r.GetString(31),r.GetString(32),r.GetString(33),r.GetString(34),
        !r.IsDBNull(35),S(r,36),N(r,37),N(r,38),N(r,39),N0(r,40),N0(r,41),N(r,42),N0(r,43),N(r,44),N(r,45),N(r,46),N(r,47),N0(r,48),N(r,49),N0(r,50),
        r.GetInt32(51)!=0,r.GetInt32(52)!=0,r.GetInt32(53)!=0,r.GetInt32(54)!=0,r.GetInt32(55)!=0,r.GetInt32(56)!=0,r.GetInt32(57)!=0,
        r.GetString(58),r.GetString(59),r.GetString(60),r.GetString(61),r.GetString(62),r.GetString(63),r.GetInt32(64),r.GetString(65),r.GetString(66),r.GetString(67),r.GetString(68));

    private static string? S(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static double? N(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
    private static double? N0(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
    private static double? EffectiveNumber(double? workspace, double? fallback) => Positive(workspace) ? workspace : Positive(fallback) ? fallback : null;
    private static double? EffectiveNumberAllowZero(double? workspace, double? fallback) => workspace.HasValue ? workspace : fallback;
    private static string? EffectiveText(string? workspace, string? fallback) => !Placeholder(workspace) ? workspace : !Placeholder(fallback) ? fallback : null;
    private static bool Positive(double? value) => value.HasValue && double.IsFinite(value.Value) && value.Value > 0;
    private static bool Placeholder(string? value) => string.IsNullOrWhiteSpace(value) || value.Trim().Equals("BRAK",StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("UNKNOWN",StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("N/A",StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("TBD",StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string? value,string term) => !string.IsNullOrWhiteSpace(value) && value.Contains(term,StringComparison.OrdinalIgnoreCase);
    private static bool IsExactSourceStatus(string identity,string verification) =>
        Contains(identity,"CONFIRMED_EXACT") || Contains(identity,"REVERIFIED_EXACT") || Contains(identity,"EXACT_TYPE_FILENAME_AND_APPROVED_TEMPLATE") ||
        Contains(identity,"OFFICIAL_MANUFACTURER_PDF_VISUALLY_CONFIRMED") || Contains(verification,"FULL_PROFILE_VERIFIED_STRICT_EXACT") || Contains(verification,"PRIMARY_PDF_LOCAL_SHA256_COMPLETE_STRICT");
    private static int QueueClassPriority(string value) => value switch
    {
        var x when Contains(x,"A_EXACT_SOURCE") => 900,
        var x when Contains(x,"B_EXACT_SOURCE") => 800,
        var x when Contains(x,"D_EXISTING") => 650,
        var x when Contains(x,"C_EXACT_SOURCE") => 550,
        var x when Contains(x,"E_CATALOG_ONLY") => 100,
        _ => 300
    };

    private SqliteConnection CreateConnection() => new(new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Cache = SqliteCacheMode.Shared
    }.ToString());

    private sealed record QueueSourceRow(
        string ProfileId,string DisplayName,string Family,string TubeTypes,string ManufacturerScope,string ProfilePinout,string CriticalWarning,
        double ProfileHeaterVoltage,double ProfileHeaterCurrent,double ProfileAnodeVoltage,double ProfileScreenVoltage,double ProfileGridVoltage,double ProfileIa,double ProfileIs,double ProfileGm,double ProfileMu,double ProfileRp,
        double ProfileMaxAnodeVoltage,double ProfileMaxScreenVoltage,double ProfileMaxAnodePower,double ProfileMaxScreenPower,double ProfileAnodeCompliance,double ProfileScreenCompliance,int ProfileWarmupSeconds,
        double ProfileCurveStart,double ProfileCurveStop,double ProfileCurveStep,string ProfileCurveGridVoltages,string MeasurementPurpose,string SourceTitle,string SourceUrl,string SourcePage,string Notes,string HeaterSupplyMode,string HeaterSupplyNote,
        bool WorkspaceExists,string? WorkspacePinout,double? WorkspaceHeaterVoltage,double? WorkspaceHeaterCurrent,double? WorkspaceAnodeVoltage,double? WorkspaceScreenVoltage,double? WorkspaceGridVoltage,double? WorkspaceIa,double? WorkspaceIs,double? WorkspaceGm,double? WorkspaceMu,double? WorkspaceRp,double? WorkspaceMaxAnodeVoltage,double? WorkspaceMaxScreenVoltage,double? WorkspaceMaxAnodePower,double? WorkspaceMaxScreenPower,
        bool SourceIdentityConfirmed,bool PinoutConfirmed,bool HeaterConfirmed,bool OperatingPointConfirmed,bool LimitsConfirmed,bool PowerGuardConfirmed,bool HardwareConfirmed,
        string Reviewer,string ReviewNote,string ReviewStatus,string QueueClass,string HardwareStatus,string HardwareReason,int LegacyPriority,string LegacyNextAction,string SourceIdentityStatus,string VerificationStatus,string DraftJson);
}
