$ErrorActionPreference='Stop'
$path='src/uTracerProManager.Infrastructure/Services/ProfileWorkQueueService.cs'
$text=Get-Content -Raw -LiteralPath $path
$old='        var readyCandidate = validation.IsReady;'
$new='        var readyCandidate = validation.IsReady && !powerBlocked && sourceExact && !sourceMismatch && !sourceMetadataMissing;'
if(-not $text.Contains($old)){ throw 'v1.4 queue safety fix: readyCandidate anchor not found' }
$text=$text.Replace($old,$new)
Set-Content -LiteralPath $path -Value $text -Encoding utf8NoBOM
Write-Host 'v1.4.0 queue READY candidate now requires no unresolved source or 95% power flag.'
