$ErrorActionPreference='Stop'
$path='src/uTracerProManager.Infrastructure/Services/TubeMeasurementCatalogService.cs'
$text=Get-Content -Raw -LiteralPath $path
$old=@'
unsafeProfileCheck.CommandText = "SELECT COUNT(*)
FROM measurement_profiles
WHERE heater_voltage <= 0
   OR heater_current_amp <= 0
   OR anode_compliance_ma <= 0
   OR anode_compliance_ma > 200
   OR screen_compliance_ma > 200
   OR anode_voltage > max_anode_voltage
   OR screen_voltage > max_screen_voltage
   OR source_title = ''
   OR source_url = ''
   OR source_page = ''
   OR (counts_for_condition_percent = 1
       AND (nominal_anode_current_ma <= 0
            OR nominal_gm_ma_v <= 0));";
'@
$new=@'
unsafeProfileCheck.CommandText = "SELECT COUNT(*)
FROM measurement_profiles
WHERE approved_for_hardware = 1
  AND (heater_voltage <= 0
   OR heater_current_amp <= 0
   OR anode_compliance_ma <= 0
   OR anode_compliance_ma > 200
   OR screen_compliance_ma > 200
   OR anode_voltage > max_anode_voltage
   OR screen_voltage > max_screen_voltage
   OR source_title = ''
   OR source_url = ''
   OR source_page = ''
   OR (counts_for_condition_percent = 1
       AND (nominal_anode_current_ma <= 0
            OR nominal_gm_ma_v <= 0)));";
'@
if(-not $text.Contains($old)){throw 'v1.3.1 blocked profile policy: expected unsafeProfileCheck SQL not found'}
$text=$text.Replace($old,$new)
Set-Content -LiteralPath $path -Value $text -Encoding utf8NoBOM
Write-Host 'v1.3.1 blocked profiles may retain unknown fields; READY profiles remain strictly validated.'