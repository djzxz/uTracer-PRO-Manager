$ErrorActionPreference='Stop'
$path='Directory.Build.props'
$text=Get-Content -Raw -LiteralPath $path
$old='<InformationalVersion>1.4.0-cp78-profile-queue</InformationalVersion>'
$new='<InformationalVersion>1.4.0-cp79-profile-queue</InformationalVersion>'
if(-not $text.Contains($old)){ throw 'v1.4.0 CP79 version fix: old informational version not found' }
$text=$text.Replace($old,$new)
Set-Content -LiteralPath $path -Value $text -Encoding utf8NoBOM
Write-Host 'v1.4.0 informational version aligned to CP79.'
