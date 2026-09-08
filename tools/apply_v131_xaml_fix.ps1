$ErrorActionPreference='Stop'
$path='src/uTracerProManager.Avalonia/Views/MainWindow.axaml'
$text=Get-Content -Raw -LiteralPath $path
$bad='<Button Content="★ ULUBIONE" Command="{Binding ToggleFavoriteCommand}" Margin="0,0,8,0"/>`n                  <Button Content="EDYTUJ BLOCKED / PENDING" Command="{Binding LoadDatabaseProfileEditorCommand}" />'
$good='<Button Content="★ ULUBIONE" Command="{Binding ToggleFavoriteCommand}" Margin="0,0,8,0"/>' + [Environment]::NewLine + '                  <Button Content="EDYTUJ BLOCKED / PENDING" Command="{Binding LoadDatabaseProfileEditorCommand}" />'
if($text.Contains($bad)) {
    $text=$text.Replace($bad,$good)
} elseif(-not $text.Contains('EDYTUJ BLOCKED / PENDING')) {
    throw 'v1.3.1 xaml fix: database editor button not found'
}
Set-Content -LiteralPath $path -Value $text -Encoding utf8NoBOM
Write-Host 'v1.3.1 XAML button newline normalized.'