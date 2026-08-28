using System.Globalization;
using uTracerProManager.Core.Models;

namespace uTracerProManager.AvaloniaApp.ViewModels;

public sealed class ManualProfileEditorViewModel : ObservableObject
{
    private string _displayName = string.Empty;
    private string _tubeTypes = string.Empty;
    private string _manufacturer = string.Empty;
    private string _family = "Profil ręczny";
    private string _pinout = string.Empty;
    private string _warning = string.Empty;
    private double _heaterVoltage = 6.3;
    private double _heaterCurrentAmp = 0.3;
    private double _anodeVoltage = 100;
    private double _screenVoltage;
    private double _gridVoltage = -2;
    private double _nominalIa = 1;
    private double _nominalIs;
    private double _nominalGm = 1;
    private double _nominalMu;
    private double _nominalRp;
    private double _maxAnodeVoltage = 250;
    private double _maxScreenVoltage = 250;
    private double _maxAnodePower = 1;
    private double _maxScreenPower;
    private double _anodeCompliance = 20;
    private double _screenCompliance = 10;
    private int _warmupSeconds = 60;
    private string _notes = string.Empty;
    private bool _valuesConfirmed;

    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string TubeTypes { get => _tubeTypes; set => SetProperty(ref _tubeTypes, value); }
    public string Manufacturer { get => _manufacturer; set => SetProperty(ref _manufacturer, value); }
    public string Family { get => _family; set => SetProperty(ref _family, value); }
    public string Pinout { get => _pinout; set => SetProperty(ref _pinout, value); }
    public string Warning { get => _warning; set => SetProperty(ref _warning, value); }
    public double HeaterVoltage { get => _heaterVoltage; set => SetProperty(ref _heaterVoltage, value); }
    public double HeaterCurrentAmp { get => _heaterCurrentAmp; set => SetProperty(ref _heaterCurrentAmp, value); }
    public double AnodeVoltage { get => _anodeVoltage; set => SetProperty(ref _anodeVoltage, value); }
    public double ScreenVoltage { get => _screenVoltage; set => SetProperty(ref _screenVoltage, value); }
    public double GridVoltage { get => _gridVoltage; set => SetProperty(ref _gridVoltage, value); }
    public double NominalIa { get => _nominalIa; set => SetProperty(ref _nominalIa, value); }
    public double NominalIs { get => _nominalIs; set => SetProperty(ref _nominalIs, value); }
    public double NominalGm { get => _nominalGm; set => SetProperty(ref _nominalGm, value); }
    public double NominalMu { get => _nominalMu; set => SetProperty(ref _nominalMu, value); }
    public double NominalRp { get => _nominalRp; set => SetProperty(ref _nominalRp, value); }
    public double MaxAnodeVoltage { get => _maxAnodeVoltage; set => SetProperty(ref _maxAnodeVoltage, value); }
    public double MaxScreenVoltage { get => _maxScreenVoltage; set => SetProperty(ref _maxScreenVoltage, value); }
    public double MaxAnodePower { get => _maxAnodePower; set => SetProperty(ref _maxAnodePower, value); }
    public double MaxScreenPower { get => _maxScreenPower; set => SetProperty(ref _maxScreenPower, value); }
    public double AnodeCompliance { get => _anodeCompliance; set => SetProperty(ref _anodeCompliance, value); }
    public double ScreenCompliance { get => _screenCompliance; set => SetProperty(ref _screenCompliance, value); }
    public int WarmupSeconds { get => _warmupSeconds; set => SetProperty(ref _warmupSeconds, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public bool ValuesConfirmed { get => _valuesConfirmed; set => SetProperty(ref _valuesConfirmed, value); }

    public void ApplyOriginalSetup(OriginalUTracerSetupDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var settings = document.QuickTest;
        DisplayName = settings.Title;
        TubeTypes = settings.Title;
        Manufacturer = "Import z oryginalnego GUI";
        Family = settings.IsTriode ? "Trioda / ustawienia uTracer" : "Pentoda / ustawienia uTracer";
        HeaterVoltage = settings.HeaterVoltage;
        AnodeVoltage = settings.AnodeVoltage;
        ScreenVoltage = settings.ScreenVoltage;
        GridVoltage = settings.GridVoltage;
        NominalIa = settings.NominalAnodeCurrentMa;
        NominalIs = settings.NominalSecondCurrentMa;
        NominalRp = settings.NominalRpKohm;
        NominalGm = settings.NominalGmMaV;
        NominalMu = settings.NominalMu;
        MaxAnodeVoltage = Math.Max(MaxAnodeVoltage, settings.AnodeVoltage);
        MaxScreenVoltage = Math.Max(MaxScreenVoltage, settings.ScreenVoltage);
        ValuesConfirmed = false;
        Notes = $"{document.Variant}; import .uts. Pinout i limity trzeba potwierdzić przed pomiarem.";
    }

    public OriginalUTracerQuickTestSettings BuildOriginalSettings(OriginalUTracerQuickTestSettings? baseline = null)
    {
        var isTriode = Family.Contains("triod", StringComparison.OrdinalIgnoreCase) ||
                       (!Family.Contains("pentod", StringComparison.OrdinalIgnoreCase) && ScreenVoltage <= 0);
        return new OriginalUTracerQuickTestSettings(
            string.IsNullOrWhiteSpace(DisplayName) ? "uTracer PRO Manager" : DisplayName.Trim(),
            isTriode,
            Math.Max(0, HeaterVoltage),
            baseline?.ExternalHeaterVoltage ?? 19.5,
            baseline?.ExternalHeaterEnabled ?? false,
            Math.Max(0, AnodeVoltage),
            baseline?.AnodeSwingPercent ?? 10,
            Math.Max(0, ScreenVoltage),
            baseline?.ScreenSwingPercent ?? 10,
            Math.Min(0, GridVoltage),
            baseline?.GridSwingPercent ?? 10,
            Math.Max(0, NominalIa),
            Math.Max(0, NominalIs),
            Math.Max(0, NominalRp),
            Math.Max(0, NominalGm),
            Math.Max(0, NominalMu));
    }

    public TubeProfile Build()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
            throw new InvalidOperationException("Podaj nazwę profilu ręcznego.");
        if (string.IsNullOrWhiteSpace(Pinout))
            throw new InvalidOperationException("Podaj pełny pinout. Profil bez pinoutu nie może być zapisany.");
        if (HeaterVoltage <= 0 || HeaterCurrentAmp <= 0)
            throw new InvalidOperationException("Podaj prawidłowe żarzenie.");

        var normalized = new string(DisplayName
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .Take(24)
            .ToArray());
        if (normalized.Length == 0)
            normalized = "profile";

        return new TubeProfile
        {
            Id = $"user-{normalized}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            DisplayName = DisplayName.Trim(),
            Family = string.IsNullOrWhiteSpace(Family) ? "Profil ręczny" : Family.Trim(),
            TubeTypes = string.IsNullOrWhiteSpace(TubeTypes) ? DisplayName.Trim() : TubeTypes.Trim(),
            ManufacturerScope = string.IsNullOrWhiteSpace(Manufacturer) ? "Użytkownik" : Manufacturer.Trim(),
            Pinout = Pinout.Trim(),
            CriticalWarning = Warning.Trim(),
            HeaterVoltage = HeaterVoltage,
            HeaterCurrentAmp = HeaterCurrentAmp,
            AnodeVoltage = AnodeVoltage,
            ScreenVoltage = ScreenVoltage,
            GridVoltage = GridVoltage,
            NominalAnodeCurrentMa = NominalIa,
            NominalScreenCurrentMa = NominalIs,
            NominalGmMaV = NominalGm,
            NominalMu = NominalMu,
            NominalRpKohm = NominalRp,
            MaxAnodeVoltage = MaxAnodeVoltage,
            MaxScreenVoltage = MaxScreenVoltage,
            MaxAnodePowerW = MaxAnodePower,
            MaxScreenPowerW = MaxScreenPower,
            AnodeComplianceMa = AnodeCompliance,
            ScreenComplianceMa = ScreenCompliance,
            WarmupSeconds = Math.Clamp(WarmupSeconds, 60, 1800),
            MeasurementPurpose = "Parametry ręczne użytkownika",
            SourceTitle = "Profil wprowadzony ręcznie",
            SourceUrl = string.Empty,
            SourcePage = string.Empty,
            ExtractionStatus = ValuesConfirmed ? "USER_CONFIRMED" : "USER_DRAFT",
            ApprovedForHardware = ValuesConfirmed,
            CountsForConditionPercent = NominalIa > 0 && NominalGm > 0,
            IsUserDefined = true,
            CurveVaStartV = Math.Max(0, AnodeVoltage * 0.2),
            CurveVaStopV = Math.Min(MaxAnodeVoltage, Math.Max(AnodeVoltage, AnodeVoltage * 1.2)),
            CurveVaStepV = Math.Max(5, AnodeVoltage * 0.1),
            CurveGridVoltages = GridVoltage.ToString(CultureInfo.InvariantCulture),
            Notes = Notes.Trim()
        };
    }
}
