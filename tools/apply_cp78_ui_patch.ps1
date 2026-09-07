$ErrorActionPreference = 'Stop'
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

Set-Content -LiteralPath $path -Value $text -Encoding utf8NoBOM
Write-Host 'CP78 UI hook patch applied successfully.'
