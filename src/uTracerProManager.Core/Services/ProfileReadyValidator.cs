using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

public static class ProfileReadyValidator
{
    private const double Guard = 0.95;

    public static ProfileEditValidationResult Validate(ProfileEditDraft draft, HardwareCapabilities hardware)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(hardware);
        var errors = new List<string>();
        var warnings = new List<string>();

        RequiredText(draft.DisplayName, "nazwa profilu", errors);
        RequiredText(draft.Pinout, "pinout", errors);
        Positive(draft.HeaterVoltage, "napięcie żarzenia", errors);
        Positive(draft.HeaterCurrentAmp, "prąd żarzenia", errors);
        Positive(draft.AnodeVoltage, "Va punktu pracy", errors);
        if (!draft.GridVoltage.HasValue) errors.Add("Brak Vg punktu pracy.");
        Positive(draft.NominalAnodeCurrentMa, "Ia nominalne", errors);
        Positive(draft.MaxAnodeVoltage, "Va max", errors);
        Positive(draft.MaxAnodePowerW, "Pa max", errors);
        Positive(draft.AnodeComplianceMa, "limit Ia/compliance", errors);
        if (!draft.WarmupSeconds.HasValue || draft.WarmupSeconds is < 60 or > 1800)
            errors.Add("Czas nagrzewania musi wynosić 60–1800 s.");

        if (draft.AnodeVoltage.HasValue && draft.MaxAnodeVoltage.HasValue && draft.AnodeVoltage > draft.MaxAnodeVoltage)
            errors.Add("Va punktu pracy przekracza Va max.");
        if (draft.ScreenVoltage.GetValueOrDefault() > 0)
        {
            Positive(draft.MaxScreenVoltage, "Vs max", errors);
            Positive(draft.ScreenComplianceMa, "limit Is/compliance", errors);
            if (draft.MaxScreenVoltage.HasValue && draft.ScreenVoltage > draft.MaxScreenVoltage)
                errors.Add("Vs punktu pracy przekracza Vs max.");
        }

        if (draft.AnodeVoltage.HasValue && draft.NominalAnodeCurrentMa.HasValue && draft.MaxAnodePowerW.HasValue)
        {
            var pa = draft.AnodeVoltage.Value * draft.NominalAnodeCurrentMa.Value / 1000.0;
            if (pa > draft.MaxAnodePowerW.Value * Guard)
                errors.Add($"Punkt nominalny anody ma {pa:F2} W i przekracza 95% Pa max.");
        }
        if (draft.ScreenVoltage.GetValueOrDefault() > 0 && draft.NominalScreenCurrentMa.GetValueOrDefault() > 0 && draft.MaxScreenPowerW.GetValueOrDefault() > 0)
        {
            var ps = draft.ScreenVoltage!.Value * draft.NominalScreenCurrentMa!.Value / 1000.0;
            if (ps > draft.MaxScreenPowerW!.Value * Guard)
                errors.Add($"Punkt nominalny ekranu ma {ps:F2} W i przekracza 95% Ps max.");
        }

        RequiredText(draft.SourceTitle, "tytuł źródła", errors);
        RequiredText(draft.SourceUrl, "URL źródła", errors);
        RequiredText(draft.SourcePage, "strona źródła", errors);
        if (!draft.SourceIdentityConfirmed) errors.Add("Nie potwierdzono zgodności źródła z typem lampy.");
        if (!draft.PinoutConfirmed) errors.Add("Nie potwierdzono pinoutu.");
        if (!draft.HeaterConfirmed) errors.Add("Nie potwierdzono żarzenia.");
        if (!draft.OperatingPointConfirmed) errors.Add("Nie potwierdzono punktu pracy.");
        if (!draft.LimitsConfirmed) errors.Add("Nie potwierdzono limitów katalogowych.");
        if (!draft.PowerGuardConfirmed) errors.Add("Nie potwierdzono kontroli mocy 95%.");
        if (!draft.HardwareConfirmed) errors.Add("Nie potwierdzono zgodności z wybranym sprzętem.");
        RequiredText(draft.Reviewer, "osoba zatwierdzająca", errors);
        RequiredText(draft.ReviewNote, "notatka z weryfikacji", errors);

        string hwStatus;
        string hwReason;
        if (!hardware.SupportsCurrentProtocol)
        {
            hwStatus = "BLOCKED";
            hwReason = $"{hardware.DisplayName}: brak fizycznie zweryfikowanego adaptera protokołu.";
            errors.Add(hwReason);
        }
        else if (draft.AnodeVoltage.GetValueOrDefault() > hardware.MaxAnodeVoltage ||
                 draft.ScreenVoltage.GetValueOrDefault() > hardware.MaxScreenVoltage ||
                 draft.GridVoltage.GetValueOrDefault() < hardware.MinGridVoltage)
        {
            hwStatus = "BLOCKED";
            hwReason = "Nominalny punkt pracy przekracza zakres wybranego wariantu sprzętu.";
            errors.Add(hwReason);
        }
        else
        {
            var external = string.Equals(draft.HeaterSupplyMode, "EXTERNAL_DC_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                           draft.HeaterVoltage.GetValueOrDefault() > 24;
            hwStatus = external ? "READY_EXTERNAL_HEATER" : "READY";
            hwReason = external
                ? "Profil danych zweryfikowany; przed START wymagane jawne potwierdzenie zewnętrznego, izolowanego zasilacza żarzenia."
                : "Profil danych i nominalny punkt pracy zweryfikowane dla wybranego wariantu.";
        }

        if (!draft.NominalGmMaV.HasValue) warnings.Add("gm pozostaje nieznane — nie będzie używane jako referencja kondycji.");
        if (!draft.NominalMu.HasValue) warnings.Add("μ pozostaje nieznane.");
        if (!draft.NominalRpKohm.HasValue) warnings.Add("Rp pozostaje nieznane.");

        return new ProfileEditValidationResult(errors.Count == 0, errors, warnings, hwStatus, hwReason);
    }

    private static void Positive(double? value, string label, List<string> errors)
    {
        if (!value.HasValue || !double.IsFinite(value.Value) || value <= 0)
            errors.Add($"Brak prawidłowej wartości: {label}.");
    }

    private static void RequiredText(string? value, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add($"Brak: {label}.");
    }
}
