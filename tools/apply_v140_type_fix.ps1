$ErrorActionPreference = 'Stop'
$path='src/uTracerProManager.Infrastructure/Services/ProfileWorkQueueService.cs'
$text=Get-Content -Raw -LiteralPath $path
$old=$text
$text=$text.Replace('curveStop.GetValueOrDefault()', 'curveStop')
$text=$text.Replace('curveStep.GetValueOrDefault()', 'curveStep')
if($text -eq $old) { throw 'v1.4.0 type fix: expected curve target expressions were not found' }
Set-Content -LiteralPath $path -Value $text -Encoding utf8NoBOM
Write-Host 'v1.4.0 queue curve numeric type fix applied.'
