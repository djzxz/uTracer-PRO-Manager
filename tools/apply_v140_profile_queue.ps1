$ErrorActionPreference = 'Stop'

function Write-Utf8NoBom([string]$path, [string]$content) {
    Set-Content -LiteralPath $path -Value $content -Encoding utf8NoBOM
}

function Replace-Required([string]$text, [string]$old, [string]$new, [string]$label) {
    if (-not $text.Contains($old)) {
        throw "v1.4.0 profile queue patch: missing source fragment: $label"
    }
    return $text.Replace($old, $new)
}

# Version 1.4.0.
$propsPath = 'Directory.Build.props'
$props = Get-Content -Raw -LiteralPath $propsPath
$props = $props.Replace('<Version>1.3.1</Version>', '<Version>1.4.0</Version>')
$props = $props.Replace('<AssemblyVersion>1.3.1.0</AssemblyVersion>', '<AssemblyVersion>1.4.0.0</AssemblyVersion>')
$props = $props.Replace('<FileVersion>1.3.1.0</FileVersion>', '<FileVersion>1.4.0.0</FileVersion>')
$props = $props.Replace('<InformationalVersion>1.3.1-cp78-final</InformationalVersion>', '<InformationalVersion>1.4.0-cp78-profile-queue</InformationalVersion>')
Write-Utf8NoBom $propsPath $props

foreach ($file in @(
    'src/uTracerProManager.Avalonia/uTracerProManager.Avalonia.csproj',
    'src/uTracerProManager.Core/uTracerProManager.Core.csproj',
    'src/uTracerProManager.Infrastructure/uTracerProManager.Infrastructure.csproj')) {
    $content = Get-Content -Raw -LiteralPath $file
    $content = $content.Replace('<Version>1.3.1</Version>', '<Version>1.4.0</Version>')
    $content = $content.Replace('<FileVersion>1.3.1.0</FileVersion>', '<FileVersion>1.4.0.0</FileVersion>')
    $content = $content.Replace('<AssemblyVersion>1.3.1.0</AssemblyVersion>', '<AssemblyVersion>1.4.0.0</AssemblyVersion>')
    Write-Utf8NoBom $file $content
}

$programTestPath = 'tests/uTracerProManager.SelfTest/Program.cs'
$programTest = Get-Content -Raw -LiteralPath $programTestPath
$programTest = $programTest.Replace('uTracer PRO Manager Avalonia v1.3.1 — self-test', 'uTracer PRO Manager Avalonia v1.4.0 — self-test')
Write-Utf8NoBom $programTestPath $programTest

# Main ViewModel becomes partial and hooks the v1.4 queue module.
$vmPath = 'src/uTracerProManager.Avalonia/ViewModels/MainWindowViewModel.cs'
$vm = Get-Content -Raw -LiteralPath $vmPath
$vm = Replace-Required $vm 'public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable' 'public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable' 'partial MainWindowViewModel'
$vm = Replace-Required $vm '        _log = new AppLogService(paths.LogPath);' "        _log = new AppLogService(paths.LogPath);`n        InitializeProfileWorkQueueV140(paths);" 'queue constructor hook'
$vm = Replace-Required $vm '                ScheduleSearch(immediate: true);' "                ScheduleSearch(immediate: true);`n                OnHardwareChangedV140();" 'hardware queue refresh hook'
$vm = Replace-Required $vm '            await _history.InitializeAsync();' "            await InitializeProfileWorkQueueAsyncV140();`n            await _history.InitializeAsync();" 'queue startup hook'
$oldImport = '            await SearchAsync();' + [Environment]::NewLine + '            StatusMessage = "Baza została sprawdzona, podmieniona atomowo i poprzednia wersja trafiła do backupu.";'
$newImport = '            await SearchAsync();' + [Environment]::NewLine + '            await _profileWorkQueue.InitializeAsync();' + [Environment]::NewLine + '            _profileWorkQueueInitializedV140 = true;' + [Environment]::NewLine + '            await RefreshProfileWorkQueueV140Async(rebuildAll: true);' + [Environment]::NewLine + '            StatusMessage = "Baza została sprawdzona, podmieniona atomowo i poprzednia wersja trafiła do backupu.";'
$vm = Replace-Required $vm $oldImport $newImport 'queue database import hook'
$vm = Replace-Required $vm '            SelectedTab = 4;' '            SelectedTab = 5;' 'verification tab index'
$oldDraftStatus = '            StatusMessage = "Zapisano szkic edycji bez zmiany statusu aktywnego profilu.";'
$newDraftStatus = $oldDraftStatus + [Environment]::NewLine + '            await RefreshProfileWorkQueueAfterEditV140Async(draft.ProfileId);'
$vm = Replace-Required $vm $oldDraftStatus $newDraftStatus 'draft queue refresh'
$oldApprovalStatus = '            StatusMessage = $"Profil {draft.ProfileId} zatwierdzono: DANE READY / SPRZĘT {validation.HardwareStatus}.";'
$newApprovalStatus = $oldApprovalStatus + [Environment]::NewLine + '            await RefreshProfileWorkQueueAfterEditV140Async(draft.ProfileId);'
$vm = Replace-Required $vm $oldApprovalStatus $newApprovalStatus 'approval queue refresh'
$vm = Replace-Required $vm "        PromoteDatabaseProfileReadyCommand.RaiseCanExecuteChanged();`n    }" "        PromoteDatabaseProfileReadyCommand.RaiseCanExecuteChanged();`n        RaiseProfileWorkQueueCommandStatesV140();`n    }" 'queue command states'
Write-Utf8NoBom $vmPath $vm

# Database editor writes the new catalog/program compatibility version after manual approval.
$editorPath = 'src/uTracerProManager.Infrastructure/Services/DatabaseProfileEditorService.cs'
$editor = Get-Content -Raw -LiteralPath $editorPath
$editor = $editor.Replace("('catalog_version','3.00.1'),('database_version','3.00.1'),('program_min_version','1.3.1')", "('catalog_version','3.01.0'),('database_version','3.01.0'),('program_min_version','1.4.0')")
Write-Utf8NoBom $editorPath $editor

# New queue tab and navigation button in verification tab.
$xamlPath = 'src/uTracerProManager.Avalonia/Views/MainWindow.axaml'
$xaml = Get-Content -Raw -LiteralPath $xamlPath
if (-not $xaml.Contains('Header="☑  KOLEJKA PROFILI"')) {
$queueTab = @'
      <TabItem Header="☑  KOLEJKA PROFILI">
        <Grid Margin="12" RowDefinitions="Auto,Auto,*,Auto" RowSpacing="10">
          <Border Classes="card">
            <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
              <StackPanel Spacing="3">
                <TextBlock Classes="h1" Text="Kolejka profili BLOCKED / PENDING"/>
                <TextBlock Text="{Binding ProfileQueueSummary}" FontWeight="Bold"/>
                <TextBlock Text="{Binding ProfileQueueDetail}" TextWrapping="Wrap"/>
                <TextBlock Text="READY nie jest nadawane automatycznie. Kolejka tylko porządkuje braki i kieruje do ręcznej weryfikacji." TextWrapping="Wrap"/>
              </StackPanel>
              <Button Grid.Column="1" Content="PRZELICZ KOLEJKĘ" Command="{Binding RefreshProfileWorkQueueCommand}" VerticalAlignment="Center"/>
            </Grid>
          </Border>

          <Border Grid.Row="1" Classes="card">
            <Grid ColumnDefinitions="2*,*,Auto" ColumnSpacing="9">
              <TextBox Text="{Binding ProfileQueueSearch, UpdateSourceTrigger=PropertyChanged}" Watermark="Szukaj ID, modelu, producenta albo rodzaju braku"/>
              <ComboBox Grid.Column="1" ItemsSource="{Binding ProfileQueueCategories}" SelectedItem="{Binding ProfileQueueCategory}"/>
              <TextBlock Grid.Column="2" Text="{Binding ProfileQueueDisplayed}" VerticalAlignment="Center" FontWeight="SemiBold"/>
            </Grid>
          </Border>

          <Border Grid.Row="2" Classes="card">
            <DataGrid ItemsSource="{Binding ProfileWorkQueue}" SelectedItem="{Binding SelectedProfileQueueItem}"
                      IsReadOnly="True" AutoGenerateColumns="False" GridLinesVisibility="Horizontal">
              <DataGrid.Columns>
                <DataGridTextColumn Header="Priorytet" Binding="{Binding Priority}" Width="75"/>
                <DataGridTextColumn Header="%" Binding="{Binding CompletenessPercent}" Width="55"/>
                <DataGridTemplateColumn Header="Profil" Width="1.7*"><DataGridTemplateColumn.CellTemplate><DataTemplate><TextBlock Text="{Binding DisplayName}" Foreground="{Binding ListForeground}" FontWeight="SemiBold" TextWrapping="Wrap"/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
                <DataGridTextColumn Header="Typ" Binding="{Binding TubeTypes}" Width="*"/>
                <DataGridTextColumn Header="Producent" Binding="{Binding ManufacturerScope}" Width="1.4*"/>
                <DataGridTemplateColumn Header="Następny krok" Width="1.5*"><DataGridTemplateColumn.CellTemplate><DataTemplate><TextBlock Text="{Binding PrimaryCategory}" Foreground="{Binding ListForeground}" FontWeight="Bold" TextWrapping="Wrap"/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
                <DataGridTextColumn Header="Wszystkie braki" Binding="{Binding MissingFlags}" Width="2.3*"/>
                <DataGridTextColumn Header="Sprzęt" Binding="{Binding HardwareStatus}" Width="1.1*"/>
              </DataGrid.Columns>
            </DataGrid>
          </Border>

          <Border Grid.Row="3" Classes="card">
            <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
              <TextBlock Text="{Binding SelectedProfileQueueReason}" TextWrapping="Wrap"/>
              <WrapPanel Grid.Column="1" VerticalAlignment="Center">
                <Button Classes="primary" Content="OTWÓRZ W EDYTORZE" Command="{Binding OpenProfileWorkQueueItemCommand}" Margin="0,0,8,0"/>
                <Button Content="NASTĘPNY PROFIL" Command="{Binding OpenNextProfileWorkQueueItemCommand}"/>
              </WrapPanel>
            </Grid>
          </Border>
        </Grid>
      </TabItem>

'@
    $xaml = Replace-Required $xaml '      <TabItem Header="✎  PROFIL RĘCZNY">' ($queueTab + '      <TabItem Header="✎  PROFIL RĘCZNY">') 'queue tab insertion'
}

$oldButtons = '<WrapPanel Grid.Row="2" HorizontalAlignment="Right"><Button Content="ZAPISZ SZKIC — BEZ ZMIANY STATUSU" Command="{Binding SaveDatabaseProfileDraftCommand}" Margin="0,0,8,0"/><Button Classes="primary" Content="ZWALIDUJ I ZATWIERDŹ READY" Command="{Binding PromoteDatabaseProfileReadyCommand}"/></WrapPanel>'
$newButtons = '<WrapPanel Grid.Row="2" HorizontalAlignment="Right"><Button Content="ZAPISZ SZKIC — BEZ ZMIANY STATUSU" Command="{Binding SaveDatabaseProfileDraftCommand}" Margin="0,0,8,0"/><Button Classes="primary" Content="ZWALIDUJ I ZATWIERDŹ READY" Command="{Binding PromoteDatabaseProfileReadyCommand}" Margin="0,0,8,0"/><Button Content="NASTĘPNY Z KOLEJKI" Command="{Binding OpenNextProfileWorkQueueItemCommand}"/></WrapPanel>'
$xaml = Replace-Required $xaml $oldButtons $newButtons 'verification next queue button'
Write-Utf8NoBom $xamlPath $xaml

Write-Host 'v1.4.0 profile work queue patch applied successfully.'
