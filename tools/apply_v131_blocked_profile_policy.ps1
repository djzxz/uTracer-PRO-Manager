$ErrorActionPreference='Stop'
$path='src/uTracerProManager.Infrastructure/Services/TubeMeasurementCatalogService.cs'
$text=Get-Content -Raw -LiteralPath $path
$old="unsafeProfileCheck.CommandText = \"SELECT COUNT(*)\nFROM measurement_profiles\nWHERE heater_voltage <= 0\n   OR heater_current_amp <= 0\n   OR anode_compliance_ma <= 0\n   OR anode_compliance_ma > 200\n   OR screen_compliance_ma > 200\n   OR anode_voltage > max_anode_voltage\n   OR screen_voltage > max_screen_voltage\n   OR source_title = ''\n   OR source_url = ''\n   OR source_page = ''\n   OR (counts_for_condition_percent = 1\n       AND (nominal_anode_current_ma <= 0\n            OR nominal_gm_ma_v <= 0));\";"
$new="unsafeProfileCheck.CommandText = \"SELECT COUNT(*)\nFROM measurement_profiles\nWHERE approved_for_hardware = 1\n  AND (heater_voltage <= 0\n   OR heater_current_amp <= 0\n   OR anode_compliance_ma <= 0\n   OR anode_compliance_ma > 200\n   OR screen_compliance_ma > 200\n   OR anode_voltage > max_anode_voltage\n   OR screen_voltage > max_screen_voltage\n   OR source_title = ''\n   OR source_url = ''\n   OR source_page = ''\n   OR (counts_for_condition_percent = 1\n       AND (nominal_anode_current_ma <= 0\n            OR nominal_gm_ma_v <= 0)));\";"
if(-not $text.Contains($old)){throw 'v1.3.1 blocked profile policy: expected unsafeProfileCheck SQL not found'}
$text=$text.Replace($old,$new)
Set-Content -LiteralPath $path -Value $text -Encoding utf8NoBOM
Write-Host 'v1.3.1 blocked profiles may retain unknown fields; READY profiles remain strictly validated.'