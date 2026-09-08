$ErrorActionPreference='Stop'
$path='src/uTracerProManager.Infrastructure/Services/TubeMeasurementCatalogService.cs'
$text=Get-Content -Raw -LiteralPath $path

# Source stores the SQL inside a C# string, therefore \n below are literal backslash+n characters.
$fromStart='WHERE heater_voltage <= 0\n'
$toStart='WHERE approved_for_hardware = 1\n  AND (heater_voltage <= 0\n'
$fromEnd='OR nominal_gm_ma_v <= 0));";'
$toEnd='OR nominal_gm_ma_v <= 0)));";'

if(-not $text.Contains($fromStart)){throw 'v1.3.1 blocked profile policy: unsafeProfileCheck WHERE fragment not found'}
if(-not $text.Contains($fromEnd)){throw 'v1.3.1 blocked profile policy: unsafeProfileCheck closing fragment not found'}
$text=$text.Replace($fromStart,$toStart)
$text=$text.Replace($fromEnd,$toEnd)
Set-Content -LiteralPath $path -Value $text -Encoding utf8NoBOM
Write-Host 'v1.3.1 blocked profiles may retain unknown fields; READY profiles remain strictly validated.'