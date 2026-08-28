using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

public static class HardwareCapabilityGuard
{
    public static void EnsureProfileFits(TubeProfile profile, HardwareCapabilities hardware)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(hardware);

        if (profile.AnodeVoltage > hardware.MaxAnodeVoltage)
        {
            throw new InvalidOperationException(
                $"Profil wymaga napięcia anodowego większego niż limit {hardware.MaxAnodeVoltage:F0} V dla {hardware.DisplayName}.");
        }

        if (profile.ScreenVoltage > hardware.MaxScreenVoltage)
        {
            throw new InvalidOperationException(
                $"Profil wymaga napięcia siatki drugiej większego niż limit {hardware.MaxScreenVoltage:F0} V dla {hardware.DisplayName}.");
        }

        if (profile.GridVoltage < hardware.MinGridVoltage)
        {
            throw new InvalidOperationException(
                $"Profil wymaga Vg={profile.GridVoltage:F1} V, poniżej limitu {hardware.MinGridVoltage:F1} V.");
        }

        if (profile.IsBlockedForSelectedHardware)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(profile.HardwareCompatibilityReason)
                    ? "Profil jest widoczny w katalogu, ale pozostaje zablokowany do pomiaru."
                    : profile.HardwareCompatibilityReason);
        }

        var requestedCurveStop = profile.CurveVaStopV > 0 ? profile.CurveVaStopV : profile.AnodeVoltage;
        if (profile.UsableCurveStopV > 0 && requestedCurveStop > profile.UsableCurveStopV + 0.001)
        {
            throw new InvalidOperationException(
                $"Skan profilu kończy się przy {requestedCurveStop:F0} V, a wybrany wariant dopuszcza {profile.UsableCurveStopV:F0} V.");
        }

        if (profile.UsableCurrentMa > 0 &&
            Math.Max(profile.AnodeComplianceMa, profile.ScreenComplianceMa) > profile.UsableCurrentMa + 0.001)
        {
            throw new InvalidOperationException(
                $"Limit profilu przekracza {profile.UsableCurrentMa:F0} mA dopuszczone dla wybranego wariantu.");
        }
    }

    public static void EnsureFeature(bool supported, string feature, HardwareCapabilities hardware)
    {
        if (!supported)
        {
            throw new NotSupportedException(
                $"Funkcja „{feature}” nie jest dostępna dla trybu {hardware.DisplayName}.");
        }
    }
}
