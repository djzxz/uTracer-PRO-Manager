$ErrorActionPreference = 'Stop'

function Write-Utf8NoBom([string]$path, [string]$content) { Set-Content -LiteralPath $path -Value $content -Encoding utf8NoBOM }
function Replace-Or-Throw([string]$text,[string]$old,[string]$new,[string]$label) {
    if (-not $text.Contains($old)) { throw "v1.3.1 patch: missing source fragment: $label" }
    return $text.Replace($old,$new)
}

# Version 1.3.1 everywhere.
$props='Directory.Build.props'
$p=Get-Content -Raw $props
$p=$p.Replace('<Version>1.3.0</Version>','<Version>1.3.1</Version>').Replace('<AssemblyVersion>1.3.0.0</AssemblyVersion>','<AssemblyVersion>1.3.1.0</AssemblyVersion>').Replace('<FileVersion>1.3.0.0</FileVersion>','<FileVersion>1.3.1.0</FileVersion>').Replace('<InformationalVersion>1.3.0-cp78</InformationalVersion>','<InformationalVersion>1.3.1-cp78-final</InformationalVersion>')
Write-Utf8NoBom $props $p
foreach($file in @('src/uTracerProManager.Avalonia/uTracerProManager.Avalonia.csproj','src/uTracerProManager.Core/uTracerProManager.Core.csproj','src/uTracerProManager.Infrastructure/uTracerProManager.Infrastructure.csproj')) {
    $x=Get-Content -Raw $file
    $x=[regex]::Replace($x,'<Version>[^<]+</Version>','<Version>1.3.1</Version>')
    $x=[regex]::Replace($x,'<FileVersion>[^<]+</FileVersion>','<FileVersion>1.3.1.0</FileVersion>')
    $x=[regex]::Replace($x,'<AssemblyVersion>[^<]+</AssemblyVersion>','<AssemblyVersion>1.3.1.0</AssemblyVersion>')
    Write-Utf8NoBom $file $x
}
$program='tests/uTracerProManager.SelfTest/Program.cs'
$t=Get-Content -Raw $program
$t=$t.Replace('uTracer PRO Manager Avalonia v1.3.0 — self-test','uTracer PRO Manager Avalonia v1.3.1 — self-test')
Write-Utf8NoBom $program $t

# Catalog cache invalidation after an in-place DB profile approval.
$catalogPath='src/uTracerProManager.Infrastructure/Services/TubeMeasurementCatalogService.cs'
$c=Get-Content -Raw $catalogPath
if(-not $c.Contains('public void InvalidateCaches()')) {
    $needle="`tpublic async Task<TubeCatalogInfo> EnsureReadyAsync(CancellationToken cancellationToken = default(CancellationToken))"
    $insert=@"
    public void InvalidateCaches()
    {
        _cachedInfo = null;
        lock (_profileAvailabilityByHardware)
            _profileAvailabilityByHardware.Clear();
    }

`tpublic async Task<TubeCatalogInfo> EnsureReadyAsync(CancellationToken cancellationToken = default(CancellationToken))
"@
    $c=Replace-Or-Throw $c $needle $insert 'catalog invalidation insertion'
    Write-Utf8NoBom $catalogPath $c
}

# Main VM: database editor service + commands + operations.
$vmPath='src/uTracerProManager.Avalonia/ViewModels/MainWindowViewModel.cs'
$v=Get-Content -Raw $vmPath
if(-not $v.Contains('DatabaseProfileEditorService _databaseProfileEditor')) {
    $v=Replace-Or-Throw $v '    private readonly FullTestDatabaseService _history;' "    private readonly FullTestDatabaseService _history;`n    private readonly DatabaseProfileEditorService _databaseProfileEditor;" 'editor service field'
    $v=Replace-Or-Throw $v '        _history = new FullTestDatabaseService(paths.HistoryPath);' "        _history = new FullTestDatabaseService(paths.HistoryPath);`n        _databaseProfileEditor = new DatabaseProfileEditorService(paths.ActiveCatalogPath);" 'editor service ctor'
    $v=Replace-Or-Throw $v '        SaveManualProfileCommand = new AsyncRelayCommand(SaveManualProfileAsync, () => !IsBusy);' "        SaveManualProfileCommand = new AsyncRelayCommand(SaveManualProfileAsync, () => !IsBusy);`n        LoadDatabaseProfileEditorCommand = new AsyncRelayCommand(LoadDatabaseProfileEditorAsync, () => !IsBusy && HighlightedProfile is not null && !HighlightedProfile.IsUserDefined);`n        SaveDatabaseProfileDraftCommand = new AsyncRelayCommand(SaveDatabaseProfileDraftAsync, () => !IsBusy && DatabaseProfileEditor.HasProfile);`n        PromoteDatabaseProfileReadyCommand = new AsyncRelayCommand(PromoteDatabaseProfileReadyAsync, () => !IsBusy && DatabaseProfileEditor.HasProfile);" 'editor commands ctor'
    $v=Replace-Or-Throw $v '    public ManualProfileEditorViewModel ManualProfile { get; } = new();' "    public ManualProfileEditorViewModel ManualProfile { get; } = new();`n    public DatabaseProfileEditorViewModel DatabaseProfileEditor { get; } = new();" 'editor VM property'
    $v=Replace-Or-Throw $v '    public AsyncRelayCommand SaveManualProfileCommand { get; }' "    public AsyncRelayCommand SaveManualProfileCommand { get; }`n    public AsyncRelayCommand LoadDatabaseProfileEditorCommand { get; }`n    public AsyncRelayCommand SaveDatabaseProfileDraftCommand { get; }`n    public AsyncRelayCommand PromoteDatabaseProfileReadyCommand { get; }" 'editor command properties'
    $v=$v.Replace('            LoadHighlightedProfileCommand.RaiseCanExecuteChanged();',"            LoadHighlightedProfileCommand.RaiseCanExecuteChanged();`n            LoadDatabaseProfileEditorCommand.RaiseCanExecuteChanged();")
    $v=Replace-Or-Throw $v '            var info = await _catalog.EnsureReadyAsync();' "            await _databaseProfileEditor.InitializeAsync();`n            var info = await _catalog.EnsureReadyAsync();" 'editor init'

    $methods=@'
    private async Task LoadDatabaseProfileEditorAsync()
    {
        if (HighlightedProfile is null || HighlightedProfile.IsUserDefined) return;
        try
        {
            IsBusy = true;
            var draft = await _databaseProfileEditor.LoadAsync(HighlightedProfile.Id);
            DatabaseProfileEditor.Load(draft);
            SelectedTab = 4;
            DatabaseProfileEditor.Status = $"Edycja {HighlightedProfile.DisplayName}. Puste wartości są celowo niezapisane/niepotwierdzone.";
            StatusMessage = "Wczytano profil bazy do edytora weryfikacji.";
        }
        catch (Exception ex) { StatusMessage = "Nie udało się otworzyć profilu do edycji: " + ex.Message; }
        finally { IsBusy = false; RaiseCommandStates(); }
    }

    private async Task SaveDatabaseProfileDraftAsync()
    {
        try
        {
            IsBusy = true;
            var draft = DatabaseProfileEditor.Build();
            await _databaseProfileEditor.SaveDraftAsync(draft);
            var validation = ProfileReadyValidator.Validate(draft, SelectedHardware);
            DatabaseProfileEditor.Status = "Szkic zapisany. " + validation.Summary +
                (validation.Errors.Count == 0 ? string.Empty : "\n• " + string.Join("\n• ", validation.Errors));
            StatusMessage = "Zapisano szkic edycji bez zmiany statusu aktywnego profilu.";
        }
        catch (Exception ex) { StatusMessage = "Nie udało się zapisać szkicu: " + ex.Message; }
        finally { IsBusy = false; RaiseCommandStates(); }
    }

    private async Task PromoteDatabaseProfileReadyAsync()
    {
        try
        {
            IsBusy = true;
            var draft = DatabaseProfileEditor.Build();
            await _databaseProfileEditor.SaveDraftAsync(draft);
            var validation = await _databaseProfileEditor.PromoteReadyAsync(draft, SelectedHardware);
            DatabaseProfileEditor.Status = validation.IsReady
                ? "ZATWIERDZONO READY. " + validation.Summary + "\n" + validation.HardwareReason
                : "NIE ZATWIERDZONO.\n• " + string.Join("\n• ", validation.Errors);
            if (!validation.IsReady)
            {
                StatusMessage = "Profil nadal BLOCKED — popraw wskazane pola i potwierdzenia.";
                return;
            }
            _catalog.InvalidateCaches();
            var info = await _catalog.EnsureReadyAsync();
            CatalogSummary = $"SQLite {info.CatalogVersion}: {info.ProfileCount} profili, {info.ReadyProfileCount} gotowych, {info.ModelCount} modeli, {info.ManufacturerCount} producentów";
            await SearchAsync();
            HighlightedProfile = Profiles.FirstOrDefault(x => x.Id.Equals(draft.ProfileId, StringComparison.OrdinalIgnoreCase));
            SelectedProfile = CanLoadProfile(HighlightedProfile) ? HighlightedProfile : null;
            StatusMessage = $"Profil {draft.ProfileId} zatwierdzono: DANE READY / SPRZĘT {validation.HardwareStatus}.";
        }
        catch (Exception ex) { StatusMessage = "Nie udało się zatwierdzić READY: " + ex.Message; }
        finally { IsBusy = false; RaiseCommandStates(); }
    }

'@
    $v=Replace-Or-Throw $v '    private void UpdateCalibrationSummary()' ($methods+'    private void UpdateCalibrationSummary()') 'editor methods'
    $v=$v.Replace('        SaveManualProfileCommand.RaiseCanExecuteChanged();',"        SaveManualProfileCommand.RaiseCanExecuteChanged();`n        LoadDatabaseProfileEditorCommand.RaiseCanExecuteChanged();`n        SaveDatabaseProfileDraftCommand.RaiseCanExecuteChanged();`n        PromoteDatabaseProfileReadyCommand.RaiseCanExecuteChanged();")
    Write-Utf8NoBom $vmPath $v
}

# Database list button and verification tab.
$xamlPath='src/uTracerProManager.Avalonia/Views/MainWindow.axaml'
$x=Get-Content -Raw $xamlPath
if(-not $x.Contains('EDYTUJ BLOCKED / PENDING')) {
    $x=$x.Replace('<Button Content="★ ULUBIONE" Command="{Binding ToggleFavoriteCommand}" />','<Button Content="★ ULUBIONE" Command="{Binding ToggleFavoriteCommand}" Margin="0,0,8,0"/>`n                  <Button Content="EDYTUJ BLOCKED / PENDING" Command="{Binding LoadDatabaseProfileEditorCommand}" />')
}
if(-not $x.Contains('Header="✓  WERYFIKACJA PROFILU"')) {
$tab=@'
      <TabItem Header="✓  WERYFIKACJA PROFILU">
        <Grid Margin="12" RowDefinitions="Auto,*,Auto" RowSpacing="10">
          <Border Classes="card"><StackPanel Spacing="4"><TextBlock Classes="h1" Text="Edycja istniejącego profilu BLOCKED / PENDING"/><TextBlock Text="Wczytaj profil z BAZA LAMP. Puste wartości pozostają puste. Zapis szkicu nie zmienia katalogu; READY wymaga pełnej walidacji i historii." TextWrapping="Wrap"/><TextBlock Text="{Binding DatabaseProfileEditor.Status}" FontWeight="SemiBold" TextWrapping="Wrap"/></StackPanel></Border>
          <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto"><StackPanel Spacing="10">
            <Border Classes="card"><Grid ColumnDefinitions="*,*,*,*" RowDefinitions="Auto,Auto,Auto,Auto,Auto,Auto,Auto" ColumnSpacing="10" RowSpacing="8">
              <StackPanel><TextBlock Classes="caption" Text="ID"/><TextBox Text="{Binding DatabaseProfileEditor.ProfileId}" IsReadOnly="True"/></StackPanel><StackPanel Grid.Column="1"><TextBlock Classes="caption" Text="Nazwa"/><TextBox Text="{Binding DatabaseProfileEditor.DisplayName}"/></StackPanel><StackPanel Grid.Column="2"><TextBlock Classes="caption" Text="Typy"/><TextBox Text="{Binding DatabaseProfileEditor.TubeTypes}"/></StackPanel><StackPanel Grid.Column="3"><TextBlock Classes="caption" Text="Producent"/><TextBox Text="{Binding DatabaseProfileEditor.Manufacturer}"/></StackPanel>
              <StackPanel Grid.Row="1" Grid.ColumnSpan="3"><TextBlock Classes="caption" Text="Pinout — pusty jeśli niepotwierdzony"/><TextBox Text="{Binding DatabaseProfileEditor.Pinout}"/></StackPanel><StackPanel Grid.Row="1" Grid.Column="3"><TextBlock Classes="caption" Text="Rodzina"/><TextBox Text="{Binding DatabaseProfileEditor.Family}"/></StackPanel>
              <StackPanel Grid.Row="2"><TextBlock Classes="caption" Text="Uf [V]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.HeaterVoltage}" Increment="0.1"/></StackPanel><StackPanel Grid.Row="2" Grid.Column="1"><TextBlock Classes="caption" Text="If [A]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.HeaterCurrentAmp}" Increment="0.05"/></StackPanel><StackPanel Grid.Row="2" Grid.Column="2"><TextBlock Classes="caption" Text="Va [V]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.AnodeVoltage}" Increment="1"/></StackPanel><StackPanel Grid.Row="2" Grid.Column="3"><TextBlock Classes="caption" Text="Vs [V]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.ScreenVoltage}" Increment="1"/></StackPanel>
              <StackPanel Grid.Row="3"><TextBlock Classes="caption" Text="Vg [V]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.GridVoltage}" Increment="0.1"/></StackPanel><StackPanel Grid.Row="3" Grid.Column="1"><TextBlock Classes="caption" Text="Ia [mA]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.NominalIa}" Increment="0.1"/></StackPanel><StackPanel Grid.Row="3" Grid.Column="2"><TextBlock Classes="caption" Text="Is [mA]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.NominalIs}" Increment="0.1"/></StackPanel><StackPanel Grid.Row="3" Grid.Column="3"><TextBlock Classes="caption" Text="gm [mA/V]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.NominalGm}" Increment="0.1"/></StackPanel>
              <StackPanel Grid.Row="4"><TextBlock Classes="caption" Text="μ"/><NumericUpDown Value="{Binding DatabaseProfileEditor.NominalMu}" Increment="0.1"/></StackPanel><StackPanel Grid.Row="4" Grid.Column="1"><TextBlock Classes="caption" Text="Rp [kΩ]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.NominalRp}" Increment="0.1"/></StackPanel><StackPanel Grid.Row="4" Grid.Column="2"><TextBlock Classes="caption" Text="Va max [V]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.MaxAnodeVoltage}" Increment="1"/></StackPanel><StackPanel Grid.Row="4" Grid.Column="3"><TextBlock Classes="caption" Text="Vs max [V]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.MaxScreenVoltage}" Increment="1"/></StackPanel>
              <StackPanel Grid.Row="5"><TextBlock Classes="caption" Text="Pa max [W]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.MaxAnodePower}" Increment="0.1"/></StackPanel><StackPanel Grid.Row="5" Grid.Column="1"><TextBlock Classes="caption" Text="Ps max [W]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.MaxScreenPower}" Increment="0.1"/></StackPanel><StackPanel Grid.Row="5" Grid.Column="2"><TextBlock Classes="caption" Text="Ia compliance [mA]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.AnodeCompliance}" Increment="1"/></StackPanel><StackPanel Grid.Row="5" Grid.Column="3"><TextBlock Classes="caption" Text="Is compliance [mA]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.ScreenCompliance}" Increment="1"/></StackPanel>
              <StackPanel Grid.Row="6"><TextBlock Classes="caption" Text="Warmup [s]"/><NumericUpDown Value="{Binding DatabaseProfileEditor.WarmupSeconds}" Increment="10"/></StackPanel><StackPanel Grid.Row="6" Grid.Column="1"><TextBlock Classes="caption" Text="Tryb żarzenia"/><TextBox Text="{Binding DatabaseProfileEditor.HeaterSupplyMode}"/></StackPanel><StackPanel Grid.Row="6" Grid.Column="2"><TextBlock Classes="caption" Text="Strona źródła"/><TextBox Text="{Binding DatabaseProfileEditor.SourcePage}"/></StackPanel><StackPanel Grid.Row="6" Grid.Column="3"><TextBlock Classes="caption" Text="Źródło"/><TextBox Text="{Binding DatabaseProfileEditor.SourceTitle}"/></StackPanel>
            </Grid></Border>
            <Border Classes="card"><Grid ColumnDefinitions="*,*" ColumnSpacing="12"><StackPanel Spacing="6"><TextBlock Classes="h2" Text="Potwierdzenia wymagane do READY"/><CheckBox IsChecked="{Binding DatabaseProfileEditor.SourceIdentityConfirmed}" Content="Źródło odpowiada dokładnie typowi/producentowi"/><CheckBox IsChecked="{Binding DatabaseProfileEditor.PinoutConfirmed}" Content="Pinout sprawdzony"/><CheckBox IsChecked="{Binding DatabaseProfileEditor.HeaterConfirmed}" Content="Żarzenie sprawdzone"/><CheckBox IsChecked="{Binding DatabaseProfileEditor.OperatingPointConfirmed}" Content="Punkt pracy sprawdzony"/><CheckBox IsChecked="{Binding DatabaseProfileEditor.LimitsConfirmed}" Content="Limity katalogowe sprawdzone"/><CheckBox IsChecked="{Binding DatabaseProfileEditor.PowerGuardConfirmed}" Content="Kontrola mocy 95% sprawdzona"/><CheckBox IsChecked="{Binding DatabaseProfileEditor.HardwareConfirmed}" Content="Zgodność z aktualnym sprzętem potwierdzona"/></StackPanel><StackPanel Grid.Column="1" Spacing="6"><TextBlock Classes="h2" Text="Audyt"/><TextBlock Classes="caption" Text="Osoba zatwierdzająca"/><TextBox Text="{Binding DatabaseProfileEditor.Reviewer}"/><TextBlock Classes="caption" Text="Notatka — obowiązkowa"/><TextBox Text="{Binding DatabaseProfileEditor.ReviewNote}" AcceptsReturn="True" MinHeight="70"/><TextBlock Classes="caption" Text="URL źródła"/><TextBox Text="{Binding DatabaseProfileEditor.SourceUrl}"/><TextBlock Classes="caption" Text="Notatki profilu"/><TextBox Text="{Binding DatabaseProfileEditor.Notes}" AcceptsReturn="True"/></StackPanel></Grid></Border>
          </StackPanel></ScrollViewer>
          <WrapPanel Grid.Row="2" HorizontalAlignment="Right"><Button Content="ZAPISZ SZKIC — BEZ ZMIANY STATUSU" Command="{Binding SaveDatabaseProfileDraftCommand}" Margin="0,0,8,0"/><Button Classes="primary" Content="ZWALIDUJ I ZATWIERDŹ READY" Command="{Binding PromoteDatabaseProfileReadyCommand}"/></WrapPanel>
        </Grid>
      </TabItem>

'@
    $x=Replace-Or-Throw $x '      <TabItem Header="◷  HISTORIA">' ($tab+'      <TabItem Header="◷  HISTORIA">') 'verification tab'
}
Write-Utf8NoBom $xamlPath $x

Write-Host 'v1.3.1 final database editor/UI/version patch applied successfully.'
