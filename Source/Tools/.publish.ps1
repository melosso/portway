# Publish all tool subprojects
$toolsRoot = $PSScriptRoot

Get-ChildItem -Path $toolsRoot -Directory | ForEach-Object {
    $publishScript = Join-Path $_.FullName ".publish.ps1"
    if (Test-Path $publishScript) {
        Write-Host "\n=== Publishing $($_.Name) ===" -ForegroundColor Cyan
        & $publishScript
    }
}
Write-Host "\nAll tool publish scripts completed." -ForegroundColor Green
