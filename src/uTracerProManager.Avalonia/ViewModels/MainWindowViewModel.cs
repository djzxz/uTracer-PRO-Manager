using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using uTracerProManager.Core.Models;
using uTracerProManager.Core.Safety;
using uTracerProManager.Core.Services;
using uTracerProManager.Services;

namespace uTracerProManager.AvaloniaApp.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ApplicationDataPaths _paths;
    private readonly TubeMeasurementCatalogService _catalog;
    private readonly TubeFavoritesService _favorites = new();
    private readonly UserTubeProfileService _userProfiles = new();
    private readonly CalibrationFileService _calibrationFiles = new();
    private readonly OriginalUTracerSetupFileService _originalSetupFiles = new();
    private readonly FullTestDatabaseService _history;
    private readonly PortCatalogService _ports = new();
    private readonly AutoPortDiscoveryService _autoPort = new();
    private readonly AppLogService _log;
    private readonly List<TubeProfile> _manualProfiles = new();
    private ITracerTransport? _transport;
    private CalibrationProfile? _calibration;
    private OriginalUTracerSetupDocument? _originalSetup;
    private CancellationTokenSource? _testCancellation;
    private CancellationTokenSource? _referenceCancellation;
    private CancellationTokenSource? _searchDebounce;
    private CancellationTokenSource? _specimenSearchDebounce;

    private string _searchQuery = string.Empty;
    private string? _selectedManufacturer;
    private string? _selectedModel;
    private TubeProfile? _selectedProfile;
    private TubeProfile? _highlightedProfile;
    private FrankCatalogEntry? _selectedDatasheet;
    private string? _selectedPort;
    private bool _emulatorRequested;
    private bool _isConnected;
    private bool _isBusy;
    private bool _modifiedHardwareConfirmed;
    private bool _externalHeaterConfirmed;
    private HardwareCapabilities _selectedHardware = HardwareCapabilities.StockSafe;
    private string _statusMessage = "Uruchamianie…";
    private string _connectionStatus = "ROZŁĄCZONY";
    private string _catalogSummary = "Ładowanie bazy…";
    private string _calibrationSummary = "Brak kalibracji";
    private string _calibrationTopSummary = "KALIBRACJA: BRAK";
    private string _originalSetupSummary = "Nie zaimportowano ustawień .uts — eksport użyje zgodnego szablonu V3.12.6.";
    private string _testStatus = "Nie uruchomiono pomiaru.";
    private double _testProgress;
    private string _lastResult = "Brak wyniku.";
    private string _inventoryNumber = string.Empty;
    private string _manufacturerForTest = string.Empty;
    private string _testNotes = string.Empty;
    private string _selectedTestMode = "Pełna diagnostyka";
    private string _specimenSearchQuery = string.Empty;
    private StoredTestSummary? _selectedStoredMeasurement;
    private int _selectedTab;
    private string _pinoutDescription = "Wybierz profil, aby wyświetlić opis pinów.";
    private string _pinoutLegend = "Brak aktywnego profilu.";
    private string _pinoutSocketLabel = "BRAK PROFILU";

    public event EventHandler<FullTestResult>? TestCompleted;
    public event EventHandler<ReferenceMeasurementResult>? ReferenceMeasurementCompleted;

    public MainWindowViewModel(ApplicationDataPaths paths)
    {
        _paths = paths;
        _catalog = new TubeMeasurementCatalogService(paths.ActiveCatalogPath);
        _history = new FullTestDatabaseService(paths.HistoryPath);
        _log = new AppLogService(paths.LogPath);

        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        FindDeviceCommand = new AsyncRelayCommand(FindDeviceAsync, () => !IsBusy && !IsConnected);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy && !IsConnected);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => !IsBusy && IsConnected);
        PingCommand = new AsyncRelayCommand(PingAsync, () => !IsBusy && IsConnected && !IsEmulatorActive);
        SendEscapeCommand = new AsyncRelayCommand(SendEscapeAsync, () => !IsBusy && IsConnected && !IsEmulatorActive);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        LoadHighlightedProfileCommand = new AsyncRelayCommand(LoadHighlightedProfileAsync, () => CanLoadProfile(HighlightedProfile));
        LoadDatasheetProfileCommand = new AsyncRelayCommand(LoadProfilesForDatasheetAsync, () => CanLoadSelectedDatasheet);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, () => SelectedProfile is not null);
        RunTestCommand = new AsyncRelayCommand(RunTestAsync, () => !IsBusy && IsConnected && SelectedProfileReady);
        CancelTestCommand = new RelayCommand(CancelTest, () => _testCancellation is not null);
        RunReferenceMeasurementCommand = new AsyncRelayCommand(RunReferenceMeasurementAsync,
            () => !IsBusy && IsConnected && SelectedProfileReady);
        CancelReferenceMeasurementCommand = new RelayCommand(CancelReferenceMeasurement,
            () => _referenceCancellation is not null);
        RefreshHistoryCommand = new AsyncRelayCommand(RefreshHistoryAsync, () => !IsBusy);
        SearchStoredMeasurementsCommand = new AsyncRelayCommand(SearchStoredMeasurementsAsync, () => !IsBusy);
        NewSpecimenCommand = new RelayCommand(NewSpecimen, () => !IsBusy);
        SaveManualProfileCommand = new AsyncRelayCommand(SaveManualProfileAsync, () => !IsBusy);

        HardwareOptions = HardwareCapabilities.All;
        TestModes = new[] { "Szybki test", "Test normalny A/B", "Pełna diagnostyka" };
        UpdatePinoutDiagram(null);
    }

    public ObservableCollection<TubeProfile> Profiles { get; } = new();
    public ObservableCollection<FrankCatalogEntry> Datasheets { get; } = new();
    public ObservableCollection<string> Manufacturers { get; } = new();
    public ObservableCollection<string> Models { get; } = new();
    public ObservableCollection<string> Ports { get; } = new();
    public ObservableCollection<StoredTestSummary> History { get; } = new();
    public ObservableCollection<StoredTestSummary> StoredMeasurementMatches { get; } = new();
    public ObservableCollection<string> CommunicationLog { get; } = new();
    public ObservableCollection<PinoutPinViewModel> PinoutPins { get; } = new();
    public ManualProfileEditorViewModel ManualProfile { get; } = new();
    public ReferenceMeasurementViewModel ReferenceMeasurement { get; } = new();
    public IReadOnlyList<HardwareCapabilities> HardwareOptions { get; }
    public IReadOnlyList<string> TestModes { get; }

    public RelayCommand RefreshPortsCommand { get; }
    public AsyncRelayCommand FindDeviceCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand PingCommand { get; }
    public AsyncRelayCommand SendEscapeCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand LoadHighlightedProfileCommand { get; }
    public AsyncRelayCommand LoadDatasheetProfileCommand { get; }
    public AsyncRelayCommand ToggleFavoriteCommand { get; }
    public AsyncRelayCommand RunTestCommand { get; }
    public RelayCommand CancelTestCommand { get; }
    public AsyncRelayCommand RunReferenceMeasurementCommand { get; }
    public RelayCommand CancelReferenceMeasurementCommand { get; }
    public AsyncRelayCommand RefreshHistoryCommand { get; }
    public AsyncRelayCommand SearchStoredMeasurementsCommand { get; }
    public RelayCommand NewSpecimenCommand { get; }
    public AsyncRelayCommand SaveManualProfileCommand { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
                ScheduleSearch();
        }
    }

    public string? SelectedManufacturer
    {
        get => _selectedManufacturer;
        set
        {
            if (SetProperty(ref _selectedManufacturer, value))
            {
                _ = LoadModelsAsync();
                ScheduleSearch();
            }
        }
    }

    public string? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (SetProperty(ref _selectedModel, value))
                ScheduleSearch();
        }
    }

    public TubeProfile? HighlightedProfile
    {
        get => _highlightedProfile;
        set
        {
            if (!SetProperty(ref _highlightedProfile, value))
                return;
            OnPropertyChanged(nameof(HighlightedProfileSummary));
            OnPropertyChanged(nameof(CanLoadHighlightedProfile));
            LoadHighlightedProfileCommand.RaiseCanExecuteChanged();
        }
    }

    public TubeProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value))
                return;

            ExternalHeaterConfirmed = false;
            OnPropertyChanged(nameof(SelectedProfileSummary));
            OnPropertyChanged(nameof(SelectedProfileReady));
            OnPropertyChanged(nameof(RequiresExternalHeaterConfirmation));
            OnPropertyChanged(nameof(SelectedProfileStatus));
            OnPropertyChanged(nameof(ActiveTubeSummary));
            OnPropertyChanged(nameof(HeaterValue));
            OnPropertyChanged(nameof(VaVsValue));
            OnPropertyChanged(nameof(VgValue));
            OnPropertyChanged(nameof(IaIsValue));
            OnPropertyChanged(nameof(GmValue));
            OnPropertyChanged(nameof(MuRpValue));
            OnPropertyChanged(nameof(LimitsValue));
            OnPropertyChanged(nameof(CompatibilitySummary));
            UpdatePinoutDiagram(value);
            RunTestCommand.RaiseCanExecuteChanged();
            RunReferenceMeasurementCommand.RaiseCanExecuteChanged();
            ToggleFavoriteCommand.RaiseCanExecuteChanged();
            if (value is not null)
                ReferenceMeasurement.ApplyProfile(value);
        }
    }

    public FrankCatalogEntry? SelectedDatasheet
    {
        get => _selectedDatasheet;
        set
        {
            if (!SetProperty(ref _selectedDatasheet, value))
                return;
            OnPropertyChanged(nameof(CanLoadSelectedDatasheet));
            LoadDatasheetProfileCommand.RaiseCanExecuteChanged();
        }
    }

    public string? SelectedPort { get => _selectedPort; set => SetProperty(ref _selectedPort, value); }
    public bool EmulatorRequested { get => _emulatorRequested; set => SetProperty(ref _emulatorRequested, value); }
    public bool IsConnected { get => _isConnected; private set { if (SetProperty(ref _isConnected, value)) RaiseCommandStates(); } }
    public bool IsEmulatorActive => _transport?.IsEmulator == true && IsConnected;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                RaiseCommandStates();
        }
    }

    public HardwareCapabilities SelectedHardware
    {
        get => _selectedHardware;
        set
        {
            if (SetProperty(ref _selectedHardware, value))
            {
                SelectedProfile = null;
                HighlightedProfile = null;
                ModifiedHardwareConfirmed = false;
                OnPropertyChanged(nameof(HardwareSummary));
                OnPropertyChanged(nameof(HardwareTopSummary));
                OnPropertyChanged(nameof(AdvancedFirmwareEnabled));
                OnPropertyChanged(nameof(HardwareReadyForMeasurement));
                ScheduleSearch(immediate: true);
            }
        }
    }

    public bool ModifiedHardwareConfirmed
    {
        get => _modifiedHardwareConfirmed;
        set
        {
            if (SetProperty(ref _modifiedHardwareConfirmed, value))
            {
                OnPropertyChanged(nameof(AdvancedFirmwareEnabled));
                OnPropertyChanged(nameof(HardwareReadyForMeasurement));
                OnPropertyChanged(nameof(HardwareSummary));
                OnPropertyChanged(nameof(HardwareTopSummary));
                OnPropertyChanged(nameof(CanLoadHighlightedProfile));
                RaiseCommandStates();
            }
        }
    }

    public bool ExternalHeaterConfirmed
    {
        get => _externalHeaterConfirmed;
        set
        {
            if (SetProperty(ref _externalHeaterConfirmed, value))
            {
                OnPropertyChanged(nameof(SelectedProfileReady));
                RaiseCommandStates();
            }
        }
    }

    public bool RequiresExternalHeaterConfirmation =>
        SelectedProfile?.RequiresExternalHeater == true;

    public bool AdvancedFirmwareEnabled => SelectedHardware.RequiresHardwareModification && ModifiedHardwareConfirmed;

    public bool HardwareReadyForMeasurement =>
        SelectedHardware.SupportsCurrentProtocol &&
        (!SelectedHardware.RequiresHardwareModification || ModifiedHardwareConfirmed);

    public string HardwareSummary =>
        $"{SelectedHardware.DisplayName} • Vg {SelectedHardware.GridResolutionBits}-bit • " +
        (!SelectedHardware.SupportsCurrentProtocol
            ? "tylko filtrowanie bazy"
            : SelectedHardware.RequiresHardwareModification && !ModifiedHardwareConfirmed
                ? "wymaga potwierdzenia modyfikacji"
                : AdvancedFirmwareEnabled ? "modyfikacja potwierdzona" : "tryb bezpieczny");

    public string HardwareTopSummary => SelectedHardware.DatabaseId switch
    {
        "UTRACER3_PLUS_600MA_MOD" => "uTracer 3+ • 600 mA",
        "UTRACER3_PLUS_UTMAX" => "uTracer 3+ • uTmax",
        "UTRACER_NXT" => "uTracerNXT",
        "UTRACER6" => "uTracer6",
        _ => "uTracer 3+"
    };

    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string ConnectionStatus { get => _connectionStatus; private set => SetProperty(ref _connectionStatus, value); }
    public string CatalogSummary { get => _catalogSummary; private set => SetProperty(ref _catalogSummary, value); }
    public string CalibrationSummary { get => _calibrationSummary; private set => SetProperty(ref _calibrationSummary, value); }
    public string CalibrationTopSummary { get => _calibrationTopSummary; private set => SetProperty(ref _calibrationTopSummary, value); }
    public string OriginalSetupSummary { get => _originalSetupSummary; private set => SetProperty(ref _originalSetupSummary, value); }
    public string TestStatus { get => _testStatus; private set => SetProperty(ref _testStatus, value); }
    public double TestProgress { get => _testProgress; private set => SetProperty(ref _testProgress, value); }
    public string LastResult { get => _lastResult; private set => SetProperty(ref _lastResult, value); }
    public string InventoryNumber { get => _inventoryNumber; set => SetProperty(ref _inventoryNumber, value); }
    public string ManufacturerForTest { get => _manufacturerForTest; set => SetProperty(ref _manufacturerForTest, value); }
    public string TestNotes { get => _testNotes; set => SetProperty(ref _testNotes, value); }
    public string SelectedTestMode { get => _selectedTestMode; set => SetProperty(ref _selectedTestMode, value); }
    public string SpecimenSearchQuery
    {
        get => _specimenSearchQuery;
        set
        {
            if (SetProperty(ref _specimenSearchQuery, value))
                ScheduleSpecimenSearch();
        }
    }

    public StoredTestSummary? SelectedStoredMeasurement
    {
        get => _selectedStoredMeasurement;
        set
        {
            if (!SetProperty(ref _selectedStoredMeasurement, value) || value is null)
                return;
            _ = LoadStoredMeasurementAsync(value);
        }
    }
    public int SelectedTab { get => _selectedTab; set => SetProperty(ref _selectedTab, value); }
    public string ActiveCatalogPath => _paths.ActiveCatalogPath;
    public string CalibrationPath => _paths.CalibrationPath;
    public string LogPath => _paths.LogPath;
    public CalibrationProfile CalibrationForWizard => _calibration ?? new CalibrationProfile();
    public FullTestResult? LastCompletedTest { get; private set; }
    public ReferenceMeasurementResult? LastReferenceMeasurement { get; private set; }

    public string CalibrationValuesSummary => _calibration is null
        ? "Brak wartości kalibracyjnych."
        : $"Va {_calibration.VaFactor:F4}  •  Vs {_calibration.VsFactor:F4}  •  " +
          $"Ia {_calibration.IaFactor:F4}  •  Is {_calibration.IsFactor:F4}  •  " +
          $"Vsu {_calibration.VsuFactor:F4}  •  Vn {_calibration.VnFactor:F4}\n" +
          $"Vg low {_calibration.Vg1Factor:F4}  •  Vg 4 V {_calibration.Vg4Factor:F4}  •  " +
          $"Vg 40 V {_calibration.Vg40Factor:F4}  •  offset {_calibration.GridOffsetV:+0.0000;-0.0000;0.0000} V  •  " +
          $"slope {_calibration.GridSlope:F4}";

    public bool SelectedProfileReady =>
        CanLoadProfile(SelectedProfile) &&
        (!RequiresExternalHeaterConfirmation || ExternalHeaterConfirmed);

    public bool CanLoadHighlightedProfile => CanLoadProfile(HighlightedProfile);

    public bool CanLoadSelectedDatasheet =>
        SelectedDatasheet?.CanLoadProfile == true && HardwareReadyForMeasurement;

    public string SelectedProfileStatus => SelectedProfile is null
        ? "BRAK AKTYWNEGO PROFILU"
        : SelectedProfile.ApprovalLabel;

    public string ActiveTubeSummary => SelectedProfile is null
        ? "LAMPA: NIE WYBRANO"
        : $"{PrimaryTubeModel(SelectedProfile)} • {PrimaryManufacturer(SelectedProfile)}";

    public string HeaterValue => SelectedProfile is { } profile
        ? $"{profile.HeaterVoltage:0.##} V / {profile.HeaterCurrentAmp:0.##} A" +
          (profile.RequiresExternalHeater ? " • ZEWNĘTRZNE DC" : string.Empty)
        : "—";

    public string VaVsValue => SelectedProfile is { } profile
        ? $"{profile.AnodeVoltage:0} / {profile.ScreenVoltage:0} V"
        : "—";

    public string VgValue => SelectedProfile is { } profile
        ? $"{profile.GridVoltage:0.##} V"
        : "—";

    public string IaIsValue => SelectedProfile is { } profile
        ? $"{profile.NominalAnodeCurrentMa:0.##} / {profile.NominalScreenCurrentMa:0.##} mA"
        : "—";

    public string GmValue => SelectedProfile is { } profile
        ? $"{profile.NominalGmMaV:0.##} mA/V"
        : "—";

    public string MuRpValue => SelectedProfile is { } profile
        ? $"{profile.NominalMu:0.##} / {profile.NominalRpKohm:0.##} kΩ"
        : "—";

    public string LimitsValue => SelectedProfile is { } profile
        ? $"{profile.MaxAnodePowerW:0.##} W / {profile.AnodeComplianceMa:0} mA"
        : "—";

    public string CompatibilitySummary
    {
        get
        {
            if (SelectedProfile is null)
                return "PASUJE DO: —";
            const string marker = "PASUJE DO:";
            var source = SelectedProfile.ManufacturerScope + " • " + SelectedProfile.DisplayName;
            var index = source.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return index >= 0
                ? "PASUJE DO: " + source[(index + marker.Length)..].Split('•')[0].Trim()
                : $"PASUJE DO: profil bezpośredni {PrimaryTubeModel(SelectedProfile)}";
        }
    }

    public string PinoutDescription
    {
        get => _pinoutDescription;
        private set => SetProperty(ref _pinoutDescription, value);
    }

    public string PinoutLegend
    {
        get => _pinoutLegend;
        private set => SetProperty(ref _pinoutLegend, value);
    }

    public string PinoutSocketLabel
    {
        get => _pinoutSocketLabel;
        private set => SetProperty(ref _pinoutSocketLabel, value);
    }

    public string HighlightedProfileSummary => HighlightedProfile is null
        ? "Zaznacz profil. Czerwony wpis jest tylko dokumentacją i nie może zostać załadowany."
        : $"{HighlightedProfile.DisplayName}\n{HighlightedProfile.ManufacturerScope}\n" +
          $"{HighlightedProfile.ApprovalLabel}: {HighlightedProfile.HardwareCompatibilityReason}";

    public string SelectedProfileSummary => SelectedProfile is null
        ? "Nie wybrano profilu."
        : $"{SelectedProfile.DisplayName} • {SelectedProfile.ManufacturerScope}\n" +
          $"Żarzenie {SelectedProfile.HeaterVoltage:F1} V / {SelectedProfile.HeaterCurrentAmp:F2} A • " +
          $"Va {SelectedProfile.AnodeVoltage:F0} V • Vs {SelectedProfile.ScreenVoltage:F0} V • Vg {SelectedProfile.GridVoltage:F1} V\n" +
          $"Ia {SelectedProfile.NominalAnodeCurrentMa:F2} mA • gm {SelectedProfile.NominalGmMaV:F2} mA/V • " +
          $"{SelectedProfile.ApprovalLabel}\nPinout: {SelectedProfile.Pinout}";

    public async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;
            var info = await _catalog.EnsureReadyAsync();
            CatalogSummary = $"SQLite {info.CatalogVersion}: {info.ProfileCount} profili, " +
                             $"{info.ReadyProfileCount} gotowych, {info.ModelCount} modeli, " +
                             $"{info.ManufacturerCount} producentów";

            _manualProfiles.Clear();
            _manualProfiles.AddRange(await _userProfiles.LoadAsync());

            Manufacturers.Clear();
            foreach (var manufacturer in await _catalog.LoadManufacturersAsync())
                Manufacturers.Add(manufacturer);

            await SearchAsync();
            await _history.InitializeAsync();
            await RefreshHistoryAsync();
            _calibration = await _calibrationFiles.LoadJsonAsync(_paths.CalibrationPath);
            UpdateCalibrationSummary();
            RefreshPorts();
            StatusMessage = "Gotowy. Domyślnie aktywny jest bezpieczny tryb fabrycznego uTracera 3+.";
            await LogAsync("Aplikacja zainicjalizowana. " + CatalogSummary);
        }
        catch (Exception ex)
        {
            StatusMessage = "Błąd startu: " + ex.Message;
            await LogAsync(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportCalibrationAsync(string path)
    {
        try
        {
            IsBusy = true;
            _calibration = await _calibrationFiles.ImportAsync(path);
            await _calibrationFiles.SaveJsonAsync(_calibration, _paths.CalibrationPath);
            if (!string.IsNullOrWhiteSpace(_calibration.PortName))
            {
                if (!Ports.Contains(_calibration.PortName))
                    Ports.Add(_calibration.PortName);
                SelectedPort = _calibration.PortName;
            }
            UpdateCalibrationSummary();
            StatusMessage = _calibration.CalibrationVersion == "1.0"
                ? "Zaimportowano oryginalny plik .cal. Wartości są widoczne; przed pomiarem dokończ kreator Vn i kalibrację v2."
                : "Zaimportowano kalibrację. Przed pomiarem sprawdź stan w ustawieniach.";
            await LogAsync($"Zaimportowano kalibrację z {path}.");
        }
        catch (Exception ex)
        {
            StatusMessage = "Błąd importu kalibracji: " + ex.Message;
            await LogAsync(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportOriginalCalibrationAsync(string path)
    {
        try
        {
            IsBusy = true;
            var calibration = _calibration ?? throw new InvalidOperationException("Brak kalibracji do eksportu.");
            await _calibrationFiles.ExportOriginalGuiAsync(calibration, path, SelectedPort);
            StatusMessage = $"Zapisano kalibrację zgodną z oryginalnym GUI V3.11/V3.12.6: {path}";
            await LogAsync(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = "Błąd eksportu kalibracji .cal: " + ex.Message;
            await LogAsync(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportOriginalSetupAsync(string path)
    {
        try
        {
            IsBusy = true;
            _originalSetup = await _originalSetupFiles.ImportAsync(path);
            ManualProfile.ApplyOriginalSetup(_originalSetup);
            ReferenceMeasurement.ApplyOriginalSetup(_originalSetup);
            OriginalSetupSummary = _originalSetup.Summary + " • pozostałe pola zachowane do eksportu 1:1";
            SelectedTab = 1;
            StatusMessage = "Zaimportowano ustawienia .uts. Pinning, osie, zakresy i krzywe zostaną zachowane przy eksporcie.";
            await LogAsync($"Zaimportowano ustawienia {_originalSetup.Variant} z {path}.");
        }
        catch (Exception ex)
        {
            StatusMessage = "Błąd importu ustawień .uts: " + ex.Message;
            await LogAsync(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportOriginalSetupAsync(string path)
    {
        try
        {
            IsBusy = true;
            var settings = ManualProfile.BuildOriginalSettings(_originalSetup?.QuickTest);
            await _originalSetupFiles.ExportAsync(_originalSetup, settings, path);
            var exported = await _originalSetupFiles.ImportAsync(path);
            OriginalSetupSummary = exported.Summary + " • plik sprawdzony po zapisie";
            StatusMessage = $"Zapisano ustawienia .uts zgodne z oryginalnym GUI: {path}";
            await LogAsync(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = "Błąd eksportu ustawień .uts: " + ex.Message;
            await LogAsync(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveCalibrationFromWizardAsync(CalibrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _calibration = profile;
        await _calibrationFiles.SaveJsonAsync(profile, _paths.CalibrationPath);
        UpdateCalibrationSummary();
        StatusMessage = profile.IsCompleteForTubeTesting
            ? "Zapisano kompletną kalibrację v2. Przed lampą wykonaj test kontrolny bez lampy."
            : "Zapisano kalibrację roboczą. Pomiar lampy pozostaje zablokowany do ukończenia wszystkich kroków.";
        await LogAsync(StatusMessage);
    }

    public async Task<string> ReadCalibrationIdleAsync(CalibrationProfile draft)
    {
        var hardware = RequireCalibrationHardware();
        var result = await hardware.ReadIdleAsync(draft);
        var value = result.Engineering ?? throw new InvalidOperationException("Nie udało się przeliczyć odczytu ADC.");
        return $"Vsu {value.SupplyVoltage:F3} V • Vn {value.NegativeSupplyVoltage:F3} V • " +
               $"Va {value.EstimatedAnodeVoltage:F3} V • Vs {value.MeasuredScreenVoltage:F3} V • " +
               $"Ia {value.AnodeCurrentMa:F4} mA • Is {value.ScreenCurrentMa:F4} mA";
    }

    public async Task HoldCalibrationGridAsync(CalibrationProfile draft, double gridVoltage)
    {
        var hardware = RequireCalibrationHardware();
        await hardware.HoldGridPointAsync(draft, gridVoltage);
        StatusMessage = $"Utrzymywany punkt Vg={gridVoltage:F1} V, Va/Vs=4 V, żarzenie=0. Zmierz napięcie multimetrem.";
    }

    public async Task HoldCalibrationBoostAsync(CalibrationProfile draft, bool anode, double targetVoltage)
    {
        if (targetVoltage is < 10 or > 250)
            throw new ArgumentOutOfRangeException(nameof(targetVoltage), "Punkt kreatora musi wynosić 10–250 V.");
        var hardware = RequireCalibrationHardware();
        await hardware.HoldBoostPointAsync(draft, anode, targetVoltage);
        StatusMessage = $"Utrzymywany punkt {(anode ? "Va" : "Vs")}={targetVoltage:F0} V, żarzenie=0. Zachowaj odstęp od wysokiego napięcia.";
    }

    public async Task StopCalibrationOutputAsync()
    {
        var hardware = RequireCalibrationHardware();
        await hardware.SafeStopAsync();
        StatusMessage = "Wyjścia kalibracyjne wyłączone: HEATER 0 + END.";
    }

    private CalibrationHardwareService RequireCalibrationHardware()
    {
        if (!IsConnected || _transport is not SerialTracerTransport serial)
            throw new InvalidOperationException("Kreator sprzętowy wymaga rzeczywistego połączenia z uTracerem. Emulator jest niedozwolony.");
        return new CalibrationHardwareService(serial);
    }

    public async Task ImportDatabaseAsync(string path)
    {
        try
        {
            IsBusy = true;
            var info = await _catalog.ImportDatabaseAsync(path);
            CatalogSummary = $"SQLite {info.CatalogVersion}: {info.ProfileCount} profili, " +
                             $"{info.ReadyProfileCount} gotowych, {info.ModelCount} modeli, " +
                             $"{info.ManufacturerCount} producentów";
            await SearchAsync();
            StatusMessage = "Baza została sprawdzona, podmieniona atomowo i poprzednia wersja trafiła do backupu.";
            await LogAsync($"Zaimportowano bazę {path}; {CatalogSummary}");
        }
        catch (Exception ex)
        {
            StatusMessage = "Błąd importu bazy: " + ex.Message;
            await LogAsync(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SearchAsync()
    {
        _searchDebounce?.Cancel();
        await SearchCoreAsync(CancellationToken.None);
    }

    private async Task SearchCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var profiles = (await _catalog.SearchAsync(
                SearchQuery, SelectedHardware.DatabaseId, cancellationToken)).ToList();
            profiles.AddRange(_manualProfiles.Where(MatchesSearch));

            Profiles.Clear();
            foreach (var profile in profiles
                         .DistinctBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
                         .OrderByDescending(profile => profile.ApprovedForHardware)
                         .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase))
                Profiles.Add(profile);

            var datasheets = await _catalog.SearchDatasheetsForHardwareAsync(
                SearchQuery,
                SelectedManufacturer ?? string.Empty,
                SelectedModel ?? string.Empty,
                500,
                SelectedHardware.DatabaseId,
                cancellationToken);
            Datasheets.Clear();
            foreach (var datasheet in datasheets)
                Datasheets.Add(datasheet);

            StatusMessage = $"Znaleziono {Profiles.Count} profili i {Datasheets.Count} kart producentów.";
        }
        catch (OperationCanceledException)
        {
            // Kolejne znaki wyszukiwania zastąpiły starsze zapytanie.
        }
        catch (Exception ex)
        {
            StatusMessage = "Błąd wyszukiwania: " + ex.Message;
            await LogAsync(StatusMessage);
        }
    }

    private void ScheduleSearch(bool immediate = false)
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = new CancellationTokenSource();
        _ = DebouncedSearchAsync(_searchDebounce.Token, immediate);
    }

    private async Task DebouncedSearchAsync(CancellationToken cancellationToken, bool immediate)
    {
        try
        {
            if (!immediate)
                await Task.Delay(280, cancellationToken);
            await SearchCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = "Nie udało się odświeżyć wyszukiwania profili: " + ex.Message;
        }
    }

    private bool MatchesSearch(TubeProfile profile)
    {
        var q = SearchQuery.Trim();
        return q.Length == 0 ||
               profile.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               profile.TubeTypes.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               profile.ManufacturerScope.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdatePinoutDiagram(TubeProfile? profile)
    {
        PinoutPins.Clear();
        if (profile is null)
        {
            PinoutSocketLabel = "BRAK PROFILU";
            PinoutLegend = "Brak aktywnego profilu.";
            PinoutDescription = "Wybierz gotowy profil, aby wyświetlić numerację podstawki i opis każdego pinu.";
            return;
        }

        var parsed = ParsePinout(profile.Pinout);
        PinoutDescription = "Opis zapisany w profilu: " + profile.Pinout;
        if (parsed.Count == 0)
        {
            PinoutSocketLabel = "TYLKO OPIS TEKSTOWY";
            PinoutLegend = "Nie narysowano pinów: opis nie ma jednoznacznego formatu. Program nie zgaduje numeracji.";
            return;
        }

        var pinCount = NormalizeSocketPinCount(parsed.Keys.Max());
        const double center = 105;
        const double radius = 82;
        const double pinRadius = 17;
        for (var pin = 1; pin <= pinCount; pin++)
        {
            var angle = (130d + (pin - 1) * 360d / pinCount) * Math.PI / 180d;
            var x = center + Math.Cos(angle) * radius - pinRadius;
            var y = center + Math.Sin(angle) * radius - pinRadius;
            var function = parsed.TryGetValue(pin, out var labels)
                ? string.Join(" / ", labels.Distinct(StringComparer.OrdinalIgnoreCase))
                : string.Empty;
            PinoutPins.Add(new PinoutPinViewModel(pin, function, x, y, PinColor(function)));
        }

        PinoutSocketLabel = $"{pinCount} PIN • OD SPODU";
        PinoutLegend = string.Join("  •  ", parsed
            .OrderBy(item => item.Key)
            .Select(item => $"{item.Key} — {string.Join(" / ", item.Value.Distinct(StringComparer.OrdinalIgnoreCase))}"));
    }

    private static SortedDictionary<int, List<string>> ParsePinout(string text)
    {
        var result = new SortedDictionary<int, List<string>>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        foreach (var rawSegment in Regex.Split(text, @"[;\r\n]+"))
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0 || segment.StartsWith("PROCEDURA", StringComparison.OrdinalIgnoreCase))
                continue;
            var procedureIndex = segment.IndexOf(". PROCEDURA", StringComparison.OrdinalIgnoreCase);
            if (procedureIndex >= 0)
                segment = segment[..procedureIndex];

            var context = string.Empty;
            var content = segment;
            var colon = segment.IndexOf(':');
            if (colon >= 0)
            {
                var prefix = segment[..colon].Trim();
                content = segment[(colon + 1)..].Trim();
                if (IsHeaterLabel(prefix))
                    content = "żarzenie=" + content;
                else if (prefix.Contains("połów", StringComparison.OrdinalIgnoreCase) ||
                         prefix.Contains("sekcj", StringComparison.OrdinalIgnoreCase))
                    context = prefix;
            }

            foreach (var rawClause in content.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                ParsePinoutClause(rawClause, context, result);
        }
        return result;
    }

    private static void ParsePinoutClause(
        string rawClause,
        string context,
        SortedDictionary<int, List<string>> result)
    {
        var clause = rawClause.Trim().TrimEnd('.');
        if (clause.Length == 0)
            return;

        var equals = clause.IndexOf('=');
        if (equals >= 0)
        {
            var left = clause[..equals].Trim();
            var right = clause[(equals + 1)..].Split('.')[0].Trim();
            if (IsPinExpression(left))
                AddPinLabels(result, ExtractPins(left), BuildPinLabel(right, context));
            else
                AddPinLabels(result, ExtractPins(right), BuildPinLabel(left, context));
            return;
        }

        const string rolePattern =
            @"(?<role>anoda|anody|katoda|siatka|g[123]|żarzenie|włókno(?:/katoda)?|ekran(?:\s+wewnętrzny)?|NC|połączenie\s+wewnętrzne)" +
            @"\s*(?:na\s+pinie|pin)?\s*(?<pins>(?:1[0-2]|[1-9])(?:\s*(?:\+|/|-|–|i|oraz)\s*(?:1[0-2]|[1-9]))*)";
        var matches = Regex.Matches(clause, rolePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (Match match in matches)
            AddPinLabels(
                result,
                ExtractPins(match.Groups["pins"].Value),
                BuildPinLabel(match.Groups["role"].Value, context));

        if (matches.Count == 0 && !string.IsNullOrWhiteSpace(context))
            AddPinLabels(result, ExtractPins(clause), context);
    }

    private static bool IsPinExpression(string value) =>
        Regex.IsMatch(
            value,
            @"^\s*(?:1[0-2]|[1-9])(?:\s*(?:\+|/|-|–|,|i|oraz)\s*(?:1[0-2]|[1-9]))*\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IReadOnlyList<int> ExtractPins(string value) =>
        Regex.Matches(value, @"(?<!\d)(?:1[0-2]|[1-9])(?!\d)")
            .Select(match => int.Parse(match.Value))
            .Distinct()
            .ToArray();

    private static void AddPinLabels(
        SortedDictionary<int, List<string>> target,
        IEnumerable<int> pins,
        string label)
    {
        label = label.Trim(' ', '.', ':');
        if (label.Length == 0)
            return;
        foreach (var pin in pins.Where(pin => pin is >= 1 and <= 12))
        {
            if (!target.TryGetValue(pin, out var labels))
            {
                labels = new List<string>();
                target.Add(pin, labels);
            }
            if (!labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                labels.Add(label);
        }
    }

    private static string BuildPinLabel(string function, string context)
    {
        function = Regex.Replace(function.Trim(), @"\s+", " ");
        if (string.IsNullOrWhiteSpace(context))
            return function;
        return $"{context}: {function}";
    }

    private static bool IsHeaterLabel(string value) =>
        value.Contains("żarzen", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("heater", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("włók", StringComparison.OrdinalIgnoreCase);

    private static int NormalizeSocketPinCount(int highestPin) => highestPin switch
    {
        <= 4 => 4,
        5 => 5,
        <= 7 => 7,
        8 => 8,
        9 => 9,
        10 => 10,
        _ => 12
    };

    private static string PinColor(string function)
    {
        if (string.IsNullOrWhiteSpace(function)) return "#73869A";
        if (IsHeaterLabel(function)) return "#D97706";
        if (function.Contains("anod", StringComparison.OrdinalIgnoreCase)) return "#C62828";
        if (function.Contains("g1", StringComparison.OrdinalIgnoreCase) ||
            function.Contains("siat", StringComparison.OrdinalIgnoreCase)) return "#2E7D32";
        if (function.Contains("g2", StringComparison.OrdinalIgnoreCase)) return "#1565C0";
        if (function.Contains("katod", StringComparison.OrdinalIgnoreCase)) return "#A56A00";
        if (function.Contains("g3", StringComparison.OrdinalIgnoreCase) ||
            function.Contains("ekran", StringComparison.OrdinalIgnoreCase)) return "#7B1FA2";
        if (function.Contains("NC", StringComparison.OrdinalIgnoreCase) ||
            function.Contains("pomini", StringComparison.OrdinalIgnoreCase)) return "#66717C";
        return "#496E91";
    }

    private static string PrimaryTubeModel(TubeProfile profile)
    {
        var model = (profile.TubeTypes ?? string.Empty)
            .Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(model))
            return model;
        return profile.DisplayName.Split('—', StringSplitOptions.TrimEntries)[0];
    }

    private static string PrimaryManufacturer(TubeProfile profile)
    {
        var manufacturer = (profile.ManufacturerScope ?? string.Empty)
            .Split('•', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(manufacturer) ? "producent nieokreślony" : manufacturer;
    }

    private async Task LoadModelsAsync()
    {
        try
        {
            Models.Clear();
            foreach (var model in await _catalog.LoadModelsAsync(SelectedManufacturer ?? string.Empty, SearchQuery))
                Models.Add(model);
        }
        catch (Exception ex)
        {
            StatusMessage = "Nie udało się wczytać modeli: " + ex.Message;
        }
    }

    private async Task LoadProfilesForDatasheetAsync()
    {
        if (SelectedDatasheet is null)
            return;

        if (!CanLoadSelectedDatasheet)
        {
            StatusMessage = "Ten wpis jest nieobsługiwany lub niezweryfikowany dla wybranego sprzętu. Profilu nie załadowano.";
            return;
        }

        try
        {
            IsBusy = true;
            var matches = await _catalog.FindMatchingProfilesForHardwareAsync(
                SelectedDatasheet.DataSheetUrl,
                SelectedDatasheet.TubeType,
                SelectedDatasheet.Manufacturer,
                SelectedHardware.DatabaseId);
            Profiles.Clear();
            foreach (var profile in matches)
                Profiles.Add(profile);
            HighlightedProfile = matches.FirstOrDefault(CanLoadProfile);
            if (HighlightedProfile is not null)
                await LoadHighlightedProfileAsync();
            ManufacturerForTest = SelectedDatasheet.Manufacturer;
            StatusMessage = matches.Count == 0
                ? "Karta pozostaje widoczna, ale nie ma jeszcze zweryfikowanego profilu pomiarowego."
                : $"Załadowano {matches.Count} profil(e) dokładnie powiązane z kartą producenta.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Nie udało się załadować profilu: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task LoadHighlightedProfileAsync()
    {
        if (!CanLoadProfile(HighlightedProfile))
        {
            StatusMessage = "Profil jest czerwony i zablokowany dla wybranego wariantu sprzętu. Nie został załadowany.";
            return Task.CompletedTask;
        }

        var profile = HighlightedProfile!;
        SelectedProfile = profile;
        ManufacturerForTest = PrimaryManufacturer(profile);
        StatusMessage = $"Załadowano profil: {profile.DisplayName}.";
        SelectedTab = 0;
        return Task.CompletedTask;
    }

    private async Task ToggleFavoriteAsync()
    {
        if (SelectedProfile is null)
            return;
        var nowFavorite = await _favorites.ToggleProfileAsync(SelectedProfile.Id);
        StatusMessage = nowFavorite ? "Dodano profil do ulubionych." : "Usunięto profil z ulubionych.";
    }

    private void RefreshPorts()
    {
        try
        {
            var previous = SelectedPort;
            Ports.Clear();
            foreach (var port in _ports.GetPortNames())
                Ports.Add(port);
            SelectedPort = previous is not null && Ports.Contains(previous) ? previous : Ports.FirstOrDefault();
            StatusMessage = Ports.Count == 0 ? "Nie wykryto portów COM." : $"Dostępne porty: {string.Join(", ", Ports)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Nie udało się odczytać portów: " + ex.Message;
        }
    }

    private async Task FindDeviceAsync()
    {
        try
        {
            IsBusy = true;
            await ReleaseTransportAsync();
            var progress = new Progress<string>(message => StatusMessage = message);
            var result = await _autoPort.FindAndConnectAsync(progress);
            if (result is null)
            {
                StatusMessage = "Nie znaleziono uTracera. Wybierz port ręcznie i sprawdź zasilanie oraz kabel USB.";
                return;
            }

            _transport = result.Transport;
            _transport.LogMessage += TransportOnLogMessage;
            RefreshPorts();
            SelectedPort = result.Probe.PortName;
            IsConnected = true;
            OnPropertyChanged(nameof(IsEmulatorActive));
            ConnectionStatus = $"POŁĄCZONY — {_transport.ConnectionName}";
            StatusMessage = $"{result.Probe.Message} Port pozostaje otwarty i jest gotowy do następnych poleceń.";
            await LogAsync(StatusMessage);
        }
        catch (Exception ex)
        {
            await ReleaseTransportAsync();
            IsConnected = false;
            ConnectionStatus = "ROZŁĄCZONY";
            StatusMessage = "Błąd wykrywania: " + ex.Message;
            await LogAsync(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConnectAsync()
    {
        try
        {
            IsBusy = true;
            await ReleaseTransportAsync();

            if (EmulatorRequested)
            {
                _transport = new EmulatorTracerTransport();
                await _transport.ConnectAsync("EMULATOR");
                ConnectionStatus = "EMULATOR — BRAK NAPIĘĆ";
            }
            else
            {
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("Transport sprzętowy Win32 jest obecnie przeznaczony dla Windows x64.");
                if (string.IsNullOrWhiteSpace(SelectedPort))
                    throw new InvalidOperationException("Wybierz port COM.");
                _transport = new SerialTracerTransport();
                _transport.LogMessage += TransportOnLogMessage;
                await _transport.ConnectAsync(SelectedPort);
                await _transport.PingAsync();
                ConnectionStatus = $"POŁĄCZONY — {_transport.ConnectionName}";
            }

            IsConnected = true;
            OnPropertyChanged(nameof(IsEmulatorActive));
            StatusMessage = EmulatorRequested
                ? "Emulator aktywny. Wyniki są syntetyczne i zostaną jednoznacznie oznaczone."
                : "Połączenie sprzętowe potwierdzone przez echo protokołu.";
            await LogAsync(StatusMessage);
        }
        catch (Exception ex)
        {
            await ReleaseTransportAsync();
            IsConnected = false;
            ConnectionStatus = "ROZŁĄCZONY";
            StatusMessage = "Błąd połączenia: " + ex.Message;
            await LogAsync(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DisconnectAsync()
    {
        try
        {
            IsBusy = true;
            await ReleaseTransportAsync();
            IsConnected = false;
            OnPropertyChanged(nameof(IsEmulatorActive));
            ConnectionStatus = "ROZŁĄCZONY";
            StatusMessage = "Połączenie zamknięte i wysłano bezpieczne zakończenie, jeżeli port odpowiadał.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PingAsync()
    {
        if (_transport is null)
            return;
        try
        {
            IsBusy = true;
            StatusMessage = await _transport.PingAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "PING nieudany: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SendEscapeAsync()
    {
        if (_transport is not SerialTracerTransport serial)
            return;
        try
        {
            IsBusy = true;
            await serial.SendEscapeAsync();
            StatusMessage = "Wysłano ESC. Niedokończona komenda została przerwana, a port pozostał otwarty.";
            await LogAsync(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = "Nie udało się wysłać ESC: " + ex.Message;
            await LogAsync(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunTestAsync()
    {
        if (_transport is null || SelectedProfile is null)
            return;

        try
        {
            IsBusy = true;
            _testCancellation = new CancellationTokenSource();
            CancelTestCommand.RaiseCanExecuteChanged();
            TestProgress = 0;
            LastResult = "Pomiar w toku…";

            if (!SelectedHardware.SupportsCurrentProtocol)
                throw new NotSupportedException($"Sterownik protokołu {SelectedHardware.DisplayName} nie jest jeszcze aktywny.");
            if (SelectedHardware.RequiresHardwareModification && !ModifiedHardwareConfirmed)
                throw new InvalidOperationException("Wybrany wariant wymaga potwierdzenia wykonanej modyfikacji sprzętowej.");
            if (SelectedProfile.RequiresExternalHeater && !ExternalHeaterConfirmed)
                throw new InvalidOperationException("Najpierw potwierdź podłączenie zewnętrznego zasilania żarzenia DC.");

            HardwareCapabilityGuard.EnsureProfileFits(SelectedProfile, SelectedHardware);
            var calibration = _transport.IsEmulator
                ? CreateEmulatorCalibration()
                : _calibration ?? throw new InvalidOperationException("Brak zapisanej kalibracji sprzętu.");

            if (!_transport.IsEmulator)
                await _transport.PingAsync(_testCancellation.Token);

            var mode = SelectedTestMode switch
            {
                "Szybki test" => TubeTestMode.Quick,
                "Test normalny A/B" => TubeTestMode.NormalDual,
                _ => TubeTestMode.FullDiagnostic
            };
            var options = FullTestOptions.ForMode(mode, _transport.IsEmulator);
            var controller = new FullTestController(new SafetyValidator(), new FullTestStatisticsService());
            var progress = new Progress<FullTestProgress>(item =>
            {
                TestStatus = item.Message;
                TestProgress = item.Percent;
            });
            var result = await controller.RunAsync(
                InventoryNumber,
                string.IsNullOrWhiteSpace(ManufacturerForTest)
                    ? SelectedProfile.ManufacturerScope
                    : ManufacturerForTest,
                TestNotes,
                SelectedProfile,
                _transport,
                calibration,
                options,
                progress,
                _testCancellation.Token);

            await _history.SaveAsync(result, _testCancellation.Token);
            await RefreshHistoryAsync();
            LastCompletedTest = result;
            TestCompleted?.Invoke(this, result);
            LastResult = $"{result.Profile.DisplayName} • {result.Statistics.Grade} • " +
                         $"kondycja {result.Statistics.OverallConditionPercent:F1}%\n" +
                         $"Ia {result.Statistics.MeanIaMa:F3} mA • gm {result.Statistics.MeanGmMaV:F3} mA/V • " +
                         $"Rp {result.Statistics.MeanRpKohm:F2} kΩ • μ {result.Statistics.MeanMu:F2}\n" +
                         $"{result.Statistics.Reliability}" + (result.Emulator ? " • WYNIK EMULATORA" : " • POMIAR SPRZĘTOWY");
            StatusMessage = result.Emulator
                ? "Zakończono symulację. Wynik nie jest pomiarem fizycznej lampy."
                : "Zakończono rzeczywisty pomiar i zapisano historię.";
            await LogAsync(StatusMessage + " " + LastResult.Replace('\n', ' '));
        }
        catch (OperationCanceledException)
        {
            TestStatus = "Pomiar przerwany. Trwało bezpieczne wyłączenie.";
            StatusMessage = TestStatus;
        }
        catch (Exception ex)
        {
            TestStatus = "Błąd pomiaru: " + ex.Message;
            StatusMessage = TestStatus;
            await LogAsync(TestStatus);
        }
        finally
        {
            _testCancellation?.Dispose();
            _testCancellation = null;
            CancelTestCommand.RaiseCanExecuteChanged();
            IsBusy = false;
        }
    }

    private void CancelTest() => _testCancellation?.Cancel();

    private async Task RunReferenceMeasurementAsync()
    {
        if (_transport is null || SelectedProfile is null)
            return;

        try
        {
            IsBusy = true;
            _referenceCancellation = new CancellationTokenSource();
            CancelReferenceMeasurementCommand.RaiseCanExecuteChanged();
            ReferenceMeasurement.Progress = 0;
            ReferenceMeasurement.Status = "Przygotowanie skanu…";

            if (SelectedHardware.RequiresHardwareModification && !ModifiedHardwareConfirmed)
                throw new InvalidOperationException("Najpierw potwierdź fizyczną modyfikację wybranego wariantu sprzętu.");
            if (SelectedProfile.RequiresExternalHeater && !ExternalHeaterConfirmed)
                throw new InvalidOperationException("Najpierw potwierdź podłączenie zewnętrznego zasilania żarzenia DC.");
            ReferenceMeasurement.ExternalHeater = SelectedProfile.RequiresExternalHeater;
            var calibration = _transport.IsEmulator
                ? CreateEmulatorCalibration()
                : _calibration ?? throw new InvalidOperationException("Brak zapisanej kalibracji sprzętu.");
            if (!_transport.IsEmulator)
                await _transport.PingAsync(_referenceCancellation.Token);

            var request = ReferenceMeasurement.BuildRequest();
            var progress = new Progress<ReferenceMeasurementProgress>(item =>
            {
                ReferenceMeasurement.Status = item.Message;
                ReferenceMeasurement.Progress = item.Percent;
            });
            var controller = new ReferenceMeasurementController();
            var result = await controller.RunAsync(
                SelectedProfile,
                _transport,
                calibration,
                SelectedHardware,
                request,
                progress,
                _referenceCancellation.Token);
            LastReferenceMeasurement = result;
            ReferenceMeasurementCompleted?.Invoke(this, result);
            ReferenceMeasurement.Status =
                $"Zakończono {result.Points.Count} punktów" +
                (result.Emulator ? " — EMULATOR, DANE SYNTETYCZNE." : " — POMIAR SPRZĘTOWY.");
            StatusMessage = ReferenceMeasurement.Status;
            await LogAsync(StatusMessage);
        }
        catch (OperationCanceledException)
        {
            ReferenceMeasurement.Status = "Skan przerwany; wyjścia wyłączone i rozładowane.";
            StatusMessage = ReferenceMeasurement.Status;
        }
        catch (Exception ex)
        {
            ReferenceMeasurement.Status = "Błąd skanu: " + ex.Message;
            StatusMessage = ReferenceMeasurement.Status;
            await LogAsync(StatusMessage);
        }
        finally
        {
            _referenceCancellation?.Dispose();
            _referenceCancellation = null;
            CancelReferenceMeasurementCommand.RaiseCanExecuteChanged();
            IsBusy = false;
        }
    }

    private void CancelReferenceMeasurement() => _referenceCancellation?.Cancel();

    public async Task ExportLastResultAsync(string directory)
    {
        if (LastCompletedTest is null)
        {
            StatusMessage = "Brak zakończonego pomiaru do eksportu.";
            return;
        }

        try
        {
            IsBusy = true;
            var exporter = new FullTestExportService(new FullTestChartService());
            var bundle = await exporter.ExportAllAsync(LastCompletedTest, directory);
            StatusMessage = $"Zapisano PDF, XLSX, CSV, wykresy i raport zgodny z oryginalnym Quick Test .txt w: {bundle.Directory}";
            await LogAsync(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = "Eksport nieudany: " + ex.Message;
            await LogAsync(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshHistoryAsync()
    {
        try
        {
            var rows = await _history.GetRecentAsync(150);
            History.Clear();
            foreach (var row in rows)
                History.Add(row);
            await SearchStoredMeasurementsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "Nie udało się wczytać historii: " + ex.Message;
        }
    }

    private async Task SearchStoredMeasurementsAsync()
    {
        try
        {
            _specimenSearchDebounce?.Cancel();
            var rows = await _history.SearchAsync(SpecimenSearchQuery, 120);
            StoredMeasurementMatches.Clear();
            foreach (var row in rows)
                StoredMeasurementMatches.Add(row);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = "Nie udało się wyszukać zapisanych pomiarów: " + ex.Message;
        }
    }

    private void ScheduleSpecimenSearch()
    {
        _specimenSearchDebounce?.Cancel();
        _specimenSearchDebounce?.Dispose();
        _specimenSearchDebounce = new CancellationTokenSource();
        _ = DebouncedSpecimenSearchAsync(_specimenSearchDebounce.Token);
    }

    private async Task DebouncedSpecimenSearchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            var rows = await _history.SearchAsync(SpecimenSearchQuery, 120, cancellationToken);
            StoredMeasurementMatches.Clear();
            foreach (var row in rows)
                StoredMeasurementMatches.Add(row);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = "Nie udało się odświeżyć listy zapisanych pomiarów: " + ex.Message;
        }
    }

    private async Task LoadStoredMeasurementAsync(StoredTestSummary summary)
    {
        try
        {
            IsBusy = true;
            var result = await _history.LoadAsync(summary.TestId);
            if (result is null)
                throw new InvalidOperationException("Nie znaleziono pełnego rekordu wybranego pomiaru.");

            InventoryNumber = result.TubeInventoryNumber;
            ManufacturerForTest = result.Manufacturer;
            TestNotes = result.Notes;
            SelectedTestMode = result.TestMode.DisplayName();
            LastCompletedTest = result;
            LastResult = $"Zapisany pomiar {result.CompletedAt:yyyy-MM-dd HH:mm} • " +
                         $"{result.Statistics.Grade} • Ia {result.Statistics.MeanIaMa:F3} mA • " +
                         $"gm {result.Statistics.MeanGmMaV:F3} mA/V" +
                         (result.Emulator ? " • EMULATOR" : " • POMIAR SPRZĘTOWY");
            TestStatus = "Wczytano zapisany pomiar. Kolejny test utworzy nowy, niezmienny rekord.";
            TestCompleted?.Invoke(this, result);

            var currentProfiles = await _catalog.SearchAsync(
                result.Profile.DisplayName,
                SelectedHardware.DatabaseId);
            var currentProfile = currentProfiles.FirstOrDefault(profile =>
                                     profile.Id.Equals(result.Profile.Id, StringComparison.OrdinalIgnoreCase))
                                 ?? _manualProfiles.FirstOrDefault(profile =>
                                     profile.Id.Equals(result.Profile.Id, StringComparison.OrdinalIgnoreCase));
            SelectedProfile = CanLoadProfile(currentProfile) ? currentProfile : null;
            StatusMessage = SelectedProfile is null
                ? "Wczytano wynik i dane egzemplarza, ale jego dawny profil nie jest obecnie dopuszczony dla wybranego sprzętu."
                : "Wczytano zapisany wynik, egzemplarz i aktualny bezpieczny profil z bazy.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Nie udało się wczytać zapisanego pomiaru: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NewSpecimen()
    {
        SelectedStoredMeasurement = null;
        SpecimenSearchQuery = string.Empty;
        InventoryNumber = string.Empty;
        ManufacturerForTest = SelectedProfile is null ? string.Empty : PrimaryManufacturer(SelectedProfile);
        TestNotes = string.Empty;
        TestStatus = "Nowy egzemplarz — wpisz numer i producenta, a pomiar zostanie zapisany jako nowy rekord.";
        LastResult = "Brak wyniku dla nowego egzemplarza.";
        StatusMessage = "Przygotowano nowy egzemplarz. Aktywny model i profil pomiarowy pozostają bez zmian.";
    }

    private async Task SaveManualProfileAsync()
    {
        try
        {
            IsBusy = true;
            var profile = ManualProfile.Build();
            await _userProfiles.SaveAsync(profile);
            _manualProfiles.RemoveAll(item => item.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
            _manualProfiles.Add(profile);
            Profiles.Insert(0, profile);
            HighlightedProfile = profile;
            SelectedProfile = CanLoadProfile(profile) ? profile : null;
            StatusMessage = profile.ApprovedForHardware
                ? "Zapisano ręczny profil. Nadal przejdzie kontrolę limitów przed każdym pomiarem."
                : "Zapisano szkic profilu. Pomiar jest zablokowany do potwierdzenia wszystkich wartości.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Nie udało się zapisać profilu ręcznego: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateCalibrationSummary()
    {
        CalibrationSummary = _calibration switch
        {
            null => "Brak kalibracji — rzeczywisty pomiar zablokowany",
            { IsCompleteForTubeTesting: true } profile =>
                $"Kalibracja v{profile.CalibrationVersion} kompletna • {profile.CalibrationCompletedAt:yyyy-MM-dd HH:mm}",
            { IsValidForHardwareDiagnostics: true } profile =>
                $"Kalibracja v{profile.CalibrationVersion} niepełna — dokończ kreator",
            _ => "Kalibracja nieprawidłowa — pomiar zablokowany"
        };
        CalibrationTopSummary = _calibration switch
        {
            null => "KALIBRACJA: BRAK",
            { IsCompleteForTubeTesting: true } profile when profile.CalibrationCompletedAt is { } completed =>
                $"KALIBRACJA: {completed:yyyy-MM-dd}",
            { IsCompleteForTubeTesting: true } => "KALIBRACJA: KOMPLETNA",
            _ => "KALIBRACJA: NIEKOMPLETNA"
        };
        OnPropertyChanged(nameof(CalibrationValuesSummary));
    }

    private static CalibrationProfile CreateEmulatorCalibration() => new()
    {
        DeviceName = "EMULATOR",
        ImportedFromFile = true,
        CalibrationVersion = "2.0",
        CalibrationCompletedAt = DateTimeOffset.Now,
        MaxAnodeVoltage = 400,
        SupplyCalibrationVerified = true,
        NegativeSupplyCalibrationVerified = true,
        GridCalibrationVerified = true,
        GridOffsetSlopeVerified = true,
        VoltageCalibrationVerified = true,
        CurrentCalibrationVerified = true
    };

    private async Task ReleaseTransportAsync()
    {
        if (_transport is null)
            return;
        _transport.LogMessage -= TransportOnLogMessage;
        try
        {
            await _transport.DisposeAsync();
        }
        finally
        {
            _transport = null;
        }
    }

    private void TransportOnLogMessage(object? sender, string message)
    {
        CommunicationLog.Add($"{DateTime.Now:HH:mm:ss} {message}");
        while (CommunicationLog.Count > 500)
            CommunicationLog.RemoveAt(0);
        _ = LogAsync("COM " + message);
    }

    private async Task LogAsync(string message)
    {
        try { await _log.WriteAsync(message); }
        catch { /* Log nie może zatrzymać pomiaru. */ }
    }

    private void RaiseCommandStates()
    {
        FindDeviceCommand.RaiseCanExecuteChanged();
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        PingCommand.RaiseCanExecuteChanged();
        SendEscapeCommand.RaiseCanExecuteChanged();
        SearchCommand.RaiseCanExecuteChanged();
        LoadHighlightedProfileCommand.RaiseCanExecuteChanged();
        LoadDatasheetProfileCommand.RaiseCanExecuteChanged();
        RunTestCommand.RaiseCanExecuteChanged();
        RunReferenceMeasurementCommand.RaiseCanExecuteChanged();
        RefreshHistoryCommand.RaiseCanExecuteChanged();
        SearchStoredMeasurementsCommand.RaiseCanExecuteChanged();
        NewSpecimenCommand.RaiseCanExecuteChanged();
        SaveManualProfileCommand.RaiseCanExecuteChanged();
    }

    public async ValueTask DisposeAsync()
    {
        _testCancellation?.Cancel();
        _referenceCancellation?.Cancel();
        _searchDebounce?.Cancel();
        _specimenSearchDebounce?.Cancel();
        await ReleaseTransportAsync();
    }

    private bool CanLoadProfile(TubeProfile? profile)
    {
        if (profile is null || !HardwareReadyForMeasurement || profile.IsBlockedForSelectedHardware)
            return false;
        if (profile.RequiresHardwareModification && !ModifiedHardwareConfirmed)
            return false;
        try
        {
            HardwareCapabilityGuard.EnsureProfileFits(profile, SelectedHardware);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
