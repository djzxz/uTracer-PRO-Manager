$ErrorActionPreference = 'Stop'
function Save([string]$p,[string]$s){ Set-Content -LiteralPath $p -Value $s -Encoding utf8NoBOM }
function Need([string]$s,[string]$old,[string]$new,[string]$label){ if(-not $s.Contains($old)){throw "v1.4.0 patch: missing $label"}; $s.Replace($old,$new) }

# Version.
$p='Directory.Build.props'; $s=Get-Content -Raw $p
$s=$s.Replace('<Version>1.3.1</Version>','<Version>1.4.0</Version>').Replace('<AssemblyVersion>1.3.1.0</AssemblyVersion>','<AssemblyVersion>1.4.0.0</AssemblyVersion>').Replace('<FileVersion>1.3.1.0</FileVersion>','<FileVersion>1.4.0.0</FileVersion>').Replace('<InformationalVersion>1.3.1-cp78-final</InformationalVersion>','<InformationalVersion>1.4.0-cp78-profile-queue</InformationalVersion>'); Save $p $s
foreach($p in @('src/uTracerProManager.Avalonia/uTracerProManager.Avalonia.csproj','src/uTracerProManager.Core/uTracerProManager.Core.csproj','src/uTracerProManager.Infrastructure/uTracerProManager.Infrastructure.csproj')){ $s=Get-Content -Raw $p; $s=$s.Replace('<Version>1.3.1</Version>','<Version>1.4.0</Version>').Replace('<FileVersion>1.3.1.0</FileVersion>','<FileVersion>1.4.0.0</FileVersion>').Replace('<AssemblyVersion>1.3.1.0</AssemblyVersion>','<AssemblyVersion>1.4.0.0</AssemblyVersion>'); Save $p $s }
$p='tests/uTracerProManager.SelfTest/Program.cs'; $s=Get-Content -Raw $p; $s=$s.Replace('uTracer PRO Manager Avalonia v1.3.1 — self-test','uTracer PRO Manager Avalonia v1.4.0 — self-test'); Save $p $s

# Main VM integration. Each anchor is unique in the v1.3.1 post-patch source.
$p='src/uTracerProManager.Avalonia/ViewModels/MainWindowViewModel.cs'; $s=Get-Content -Raw $p
$s=Need $s 'public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable' 'public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable' 'partial class'
$s=Need $s '        _log = new AppLogService(paths.LogPath);' ('        _log = new AppLogService(paths.LogPath);'+[Environment]::NewLine+'        InitializeProfileWorkQueueV140(paths);') 'constructor hook'
$s=Need $s '                ScheduleSearch(immediate: true);' ('                ScheduleSearch(immediate: true);'+[Environment]::NewLine+'                OnHardwareChangedV140();') 'hardware hook'
$s=Need $s '            await _history.InitializeAsync();' ('            await InitializeProfileWorkQueueAsyncV140();'+[Environment]::NewLine+'            await _history.InitializeAsync();') 'startup hook'
$s=Need $s '            StatusMessage = "Baza została sprawdzona, podmieniona atomowo i poprzednia wersja trafiła do backupu.";' ('            await _profileWorkQueue.InitializeAsync();'+[Environment]::NewLine+'            _profileWorkQueueInitializedV140 = true;'+[Environment]::NewLine+'            await RefreshProfileWorkQueueV140Async(rebuildAll: true);'+[Environment]::NewLine+'            StatusMessage = "Baza została sprawdzona, podmieniona atomowo i poprzednia wersja trafiła do backupu.";') 'database import hook'
$s=Need $s '            SelectedTab = 4;' '            SelectedTab = 5;' 'verification tab index'
$s=Need $s '            StatusMessage = "Zapisano szkic edycji bez zmiany statusu aktywnego profilu.";' ('            StatusMessage = "Zapisano szkic edycji bez zmiany statusu aktywnego profilu.";'+[Environment]::NewLine+'            await RefreshProfileWorkQueueAfterEditV140Async(draft.ProfileId);') 'draft refresh'
$s=Need $s '            StatusMessage = $"Profil {draft.ProfileId} zatwierdzono: DANE READY / SPRZĘT {validation.HardwareStatus}.";' ('            StatusMessage = $"Profil {draft.ProfileId} zatwierdzono: DANE READY / SPRZĘT {validation.HardwareStatus}.";'+[Environment]::NewLine+'            await RefreshProfileWorkQueueAfterEditV140Async(draft.ProfileId);') 'approval refresh'
$s=Need $s '        PromoteDatabaseProfileReadyCommand.RaiseCanExecuteChanged();' ('        PromoteDatabaseProfileReadyCommand.RaiseCanExecuteChanged();'+[Environment]::NewLine+'        RaiseProfileWorkQueueCommandStatesV140();') 'command states'
Save $p $s

# DB editor release metadata.
$p='src/uTracerProManager.Infrastructure/Services/DatabaseProfileEditorService.cs'; $s=Get-Content -Raw $p
$s=$s.Replace("('catalog_version','3.00.1'),('database_version','3.00.1'),('program_min_version','1.3.1')","('catalog_version','3.01.0'),('database_version','3.01.0'),('program_min_version','1.4.0')"); Save $p $s

# Queue UI.
$p='src/uTracerProManager.Avalonia/Views/MainWindow.axaml'; $s=Get-Content -Raw $p
if(-not $s.Contains('Header="☑  KOLEJKA PROFILI"')){
$tab=@'
      <TabItem Header="☑  KOLEJKA PROFILI">
        <Grid Margin="12" RowDefinitions="Auto,Auto,*,Auto" RowSpacing="10">
          <Border Classes="card"><Grid ColumnDefinitions="*,Auto" ColumnSpacing="12"><StackPanel Spacing="3"><TextBlock Classes="h1" Text="Kolejka profili BLOCKED / PENDING"/><TextBlock Text="{Binding ProfileQueueSummary}" FontWeight="Bold"/><TextBlock Text="{Binding ProfileQueueDetail}" TextWrapping="Wrap"/><TextBlock Text="READY nie jest nadawane automatycznie. Kolejka porządkuje braki i kieruje do ręcznej weryfikacji." TextWrapping="Wrap"/></StackPanel><Button Grid.Column="1" Content="PRZELICZ KOLEJKĘ" Command="{Binding RefreshProfileWorkQueueCommand}" VerticalAlignment="Center"/></Grid></Border>
          <Border Grid.Row="1" Classes="card"><Grid ColumnDefinitions="2*,*,Auto" ColumnSpacing="9"><TextBox Text="{Binding ProfileQueueSearch, UpdateSourceTrigger=PropertyChanged}" Watermark="Szukaj ID, modelu, producenta albo rodzaju braku"/><ComboBox Grid.Column="1" ItemsSource="{Binding ProfileQueueCategories}" SelectedItem="{Binding ProfileQueueCategory}"/><TextBlock Grid.Column="2" Text="{Binding ProfileQueueDisplayed}" VerticalAlignment="Center" FontWeight="SemiBold"/></Grid></Border>
          <Border Grid.Row="2" Classes="card"><DataGrid ItemsSource="{Binding ProfileWorkQueue}" SelectedItem="{Binding SelectedProfileQueueItem}" IsReadOnly="True" AutoGenerateColumns="False" GridLinesVisibility="Horizontal"><DataGrid.Columns><DataGridTextColumn Header="Priorytet" Binding="{Binding Priority}" Width="75"/><DataGridTextColumn Header="%" Binding="{Binding CompletenessPercent}" Width="55"/><DataGridTemplateColumn Header="Profil" Width="1.7*"><DataGridTemplateColumn.CellTemplate><DataTemplate><TextBlock Text="{Binding DisplayName}" Foreground="{Binding ListForeground}" FontWeight="SemiBold" TextWrapping="Wrap"/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn><DataGridTextColumn Header="Typ" Binding="{Binding TubeTypes}" Width="*"/><DataGridTextColumn Header="Producent" Binding="{Binding ManufacturerScope}" Width="1.4*"/><DataGridTemplateColumn Header="Następny krok" Width="1.5*"><DataGridTemplateColumn.CellTemplate><DataTemplate><TextBlock Text="{Binding PrimaryCategory}" Foreground="{Binding ListForeground}" FontWeight="Bold" TextWrapping="Wrap"/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn><DataGridTextColumn Header="Wszystkie braki" Binding="{Binding MissingFlags}" Width="2.3*"/><DataGridTextColumn Header="Sprzęt" Binding="{Binding HardwareStatus}" Width="1.1*"/></DataGrid.Columns></DataGrid></Border>
          <Border Grid.Row="3" Classes="card"><Grid ColumnDefinitions="*,Auto" ColumnSpacing="12"><TextBlock Text="{Binding SelectedProfileQueueReason}" TextWrapping="Wrap"/><WrapPanel Grid.Column="1" VerticalAlignment="Center"><Button Classes="primary" Content="OTWÓRZ W EDYTORZE" Command="{Binding OpenProfileWorkQueueItemCommand}" Margin="0,0,8,0"/><Button Content="NASTĘPNY PROFIL" Command="{Binding OpenNextProfileWorkQueueItemCommand}"/></WrapPanel></Grid></Border>
        </Grid>
      </TabItem>

'@
$s=Need $s '      <TabItem Header="✎  PROFIL RĘCZNY">' ($tab+'      <TabItem Header="✎  PROFIL RĘCZNY">') 'queue tab'
}
$old='<WrapPanel Grid.Row="2" HorizontalAlignment="Right"><Button Content="ZAPISZ SZKIC — BEZ ZMIANY STATUSU" Command="{Binding SaveDatabaseProfileDraftCommand}" Margin="0,0,8,0"/><Button Classes="primary" Content="ZWALIDUJ I ZATWIERDŹ READY" Command="{Binding PromoteDatabaseProfileReadyCommand}"/></WrapPanel>'
$new='<WrapPanel Grid.Row="2" HorizontalAlignment="Right"><Button Content="ZAPISZ SZKIC — BEZ ZMIANY STATUSU" Command="{Binding SaveDatabaseProfileDraftCommand}" Margin="0,0,8,0"/><Button Classes="primary" Content="ZWALIDUJ I ZATWIERDŹ READY" Command="{Binding PromoteDatabaseProfileReadyCommand}" Margin="0,0,8,0"/><Button Content="NASTĘPNY Z KOLEJKI" Command="{Binding OpenNextProfileWorkQueueItemCommand}"/></WrapPanel>'
$s=Need $s $old $new 'verification queue button'; Save $p $s
Write-Host 'v1.4.0 profile work queue patch applied successfully.'
