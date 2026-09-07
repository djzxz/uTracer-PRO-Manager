$ErrorActionPreference = 'Stop'

function Write-Utf8NoBom([string]$path, [string]$content) {
    Set-Content -LiteralPath $path -Value $content -Encoding utf8NoBOM
}

$path = 'src/uTracerProManager.Avalonia/Views/MainWindow.axaml.cs'
$text = Get-Content -Raw -LiteralPath $path

function Replace-Required([string]$old, [string]$new, [string]$label) {
    if (-not $script:text.Contains($old)) {
        throw "CP78 UI patch: missing expected source fragment: $label"
    }
    $script:text = $script:text.Replace($old, $new)
}

if ($text.Contains('_viewModel.ReferenceMeasurementCompleted += OnReferenceMeasurementCompleted;')) {
    Replace-Required '_viewModel.ReferenceMeasurementCompleted += OnReferenceMeasurementCompleted;' '_viewModel.ReferenceMeasurementCompleted += OnReferenceMeasurementCompletedV2;' 'reference completed subscribe'
}

if (-not $text.Contains('InitializeReferenceCurveUiHooks();')) {
    Replace-Required '        ConfigurePlot();' "        ConfigurePlot();`n        InitializeReferenceCurveUiHooks();" 'configure plot hook'
}

if (-not $text.Contains('await InitializeReferenceCurveDatabaseAsync();')) {
    Replace-Required '        await _viewModel.InitializeAsync();' "        await _viewModel.InitializeAsync();`n        await InitializeReferenceCurveDatabaseAsync();" 'opened database hook'
}

if ($text.Contains('_viewModel.ReferenceMeasurementCompleted -= OnReferenceMeasurementCompleted;')) {
    Replace-Required '_viewModel.ReferenceMeasurementCompleted -= OnReferenceMeasurementCompleted;' "_viewModel.ReferenceMeasurementCompleted -= OnReferenceMeasurementCompletedV2;`n        DisposeReferenceCurveUiHooks();" 'reference completed unsubscribe'
}

if (-not $text.Contains('_viewModel.ReferenceMeasurementCompleted += OnReferenceMeasurementCompletedV2;')) {
    throw 'CP78 UI patch verification failed: V2 completion handler is not subscribed.'
}
if (-not $text.Contains('DisposeReferenceCurveUiHooks();')) {
    throw 'CP78 UI patch verification failed: curve hooks are not disposed.'
}
Write-Utf8NoBom $path $text

# Microsoft.Data.Sqlite 8 exposes BeginTransactionAsync as DbTransaction in this target.
# Use the provider-specific synchronous BeginTransaction so command.Transaction remains
# strongly typed as SqliteTransaction. Database I/O inside each transaction stays async.
$transactionFiles = @(
    'src/uTracerProManager.Infrastructure/Services/ReferenceCurveRepository.cs',
    'src/uTracerProManager.Infrastructure/Services/LegacyReferenceCurveMigrationService.cs',
    'src/uTracerProManager.Infrastructure/Services/ReferenceCurveTargetRepository.cs'
)
foreach ($transactionPath in $transactionFiles) {
    $source = Get-Content -Raw -LiteralPath $transactionPath
    $source = $source.Replace(
        'await using var transaction = await connection.BeginTransactionAsync(cancellationToken);',
        'using var transaction = connection.BeginTransaction();')
    $source = $source.Replace(
        'await transaction.CommitAsync(cancellationToken);',
        'transaction.Commit();')
    Write-Utf8NoBom $transactionPath $source
}

Write-Host 'CP78 UI/database compatibility patch applied successfully.'
