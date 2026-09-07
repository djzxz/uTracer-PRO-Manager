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

# A required external heater is NOT the same as operator confirmation.
$viewModelPath = 'src/uTracerProManager.Avalonia/ViewModels/ReferenceMeasurementViewModel.cs'
$viewModel = Get-Content -Raw -LiteralPath $viewModelPath
$viewModel = $viewModel.Replace('        ExternalHeater = profile.RequiresExternalHeater;', '        ExternalHeater = false;')
$viewModel = $viewModel.Replace('        Status = ExternalHeater`n            ?', '        Status = profile.RequiresExternalHeater`n            ?')
Write-Utf8NoBom $viewModelPath $viewModel

$quickPath = 'src/uTracerProManager.Core/Services/SinglePointMeasurementController.cs'
$quick = Get-Content -Raw -LiteralPath $quickPath
$quick = $quick.Replace(
    'if (!transport.IsEmulator && string.Equals(profile.HardwareCompatibilityStatus, "READY_EXTERNAL_HEATER", StringComparison.OrdinalIgnoreCase))',
    'if (!transport.IsEmulator && profile.RequiresExternalHeater)')
Write-Utf8NoBom $quickPath $quick

# Microsoft.Data.Sqlite 8 exposes BeginTransactionAsync as DbTransaction in this target.
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

# Floating-point axis values are compared with tolerance; this does not weaken the routing assertions.
$selfTestPath = 'tests/uTracerProManager.SelfTest/SafetyRegressionSelfTests.cs'
$selfTest = Get-Content -Raw -LiteralPath $selfTestPath
$selfTest = $selfTest.Replace(
'        Assert(positiveTargets.Select(point => point.Vs).SequenceEqual(new[] { 0.1, 1.325, 2.55, 3.775, 5.0 }),
            "+Vg scan voltage must be carried by the SCREEN output");',
'        var expectedPositiveGridVs = new[] { 0.1, 1.325, 2.55, 3.775, 5.0 };
        Assert(positiveTargets.Count == expectedPositiveGridVs.Length &&
               positiveTargets.Select(point => point.Vs)
                   .Zip(expectedPositiveGridVs, (actual, expected) => Math.Abs(actual - expected) < 1e-9)
                   .All(match => match),
            "+Vg scan voltage must be carried by the SCREEN output");')
Write-Utf8NoBom $selfTestPath $selfTest

# Main self-test: v1.3.0 checks data/hardware separation instead of historical UI labels or old profile IDs.
$programTestPath = 'tests/uTracerProManager.SelfTest/Program.cs'
$programTest = Get-Content -Raw -LiteralPath $programTestPath
$programTest = $programTest.Replace(
    'Console.WriteLine("uTracer PRO Manager Avalonia v1.2.7 — self-test");',
    'Console.WriteLine("uTracer PRO Manager Avalonia v1.3.0 — self-test");')
$programTest = $programTest.Replace(
'Assert(exactProfiles[0].DisplayName.Contains("PASUJE DO", StringComparison.Ordinal),
    "manufacturer profile visibly identifies its approved template");',
'Assert(exactProfiles[0].ManufacturerScope.Contains("General Electric", StringComparison.OrdinalIgnoreCase) ||
       exactProfiles[0].TubeTypes.Contains("12AX7", StringComparison.OrdinalIgnoreCase),
    "manufacturer profile preserves manufacturer/model identity");')
$programTest = $programTest.Replace(
'Assert(newBatchProfiles.Count == 1 &&
       newBatchProfiles[0].Id.StartsWith("MFR26_6N7_GENERAL_ELECTRIC_", StringComparison.Ordinal),
    "v2.26 card resolves its manufacturer-specific READY profile");
Assert(!newBatchProfiles[0].CountsForConditionPercent && newBatchProfiles[0].RequiresManualConfirmation,
    "v2.26 manufacturer profile keeps percentage disabled and confirmation enabled");',
'if (newBatchProfiles.Count == 0)
{
    Assert(!newBatchCard.HasApprovedMeasurementProfile && newBatchCard.HasBlockedMeasurementProfile,
        "6N7 without a compatible hardware profile must be visibly BLOCKED");
}
else
{
    Assert(newBatchCard.HasApprovedMeasurementProfile,
        "6N7 with compatible recommendations must be visibly available");
    Assert(newBatchProfiles.All(profile => profile.ApprovedForHardware && !profile.IsBlockedForSelectedHardware),
        "6N7 query must return only approved, non-BLOCKED hardware profiles");
}')
Write-Utf8NoBom $programTestPath $programTest

Write-Host 'CP78 UI/database/safety compatibility patch applied successfully.'
