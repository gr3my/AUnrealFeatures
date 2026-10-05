$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failed = $false

$sensitiveFiles = Get-ChildItem -Path $root -Recurse -File | Where-Object {
    $_.FullName -notmatch '\\(node_modules|bin|obj|\.next)\\' -and (
        $_.Name -match '\.(db|sqlite|sqlite3|log|pem|pfx|p12|key|crt|cer|bak|rar|7z)$' -or
        $_.Name -in @('sessions.json','lobbies.json','secrets.json')
    )
}
if ($sensitiveFiles) {
    $failed = $true
    Write-Host 'Sensitive or runtime files found:' -ForegroundColor Red
    $sensitiveFiles | ForEach-Object { Write-Host "  $($_.FullName)" }
}

$privateConfig = Get-ChildItem -Path $root -Recurse -File | Where-Object {
    $_.Name -eq '.env' -or ($_.Name -like '.env.*' -and $_.Name -ne '.env.example')
}
if ($privateConfig) {
    $failed = $true
    Write-Host 'Private environment files found:' -ForegroundColor Red
    $privateConfig | ForEach-Object { Write-Host "  $($_.FullName)" }
}

$generatedDirs = Get-ChildItem -Path $root -Recurse -Directory | Where-Object {
    $_.Name -in @('bin','obj','node_modules','.next','publish')
}
if ($generatedDirs) {
    $failed = $true
    Write-Host 'Generated directories should not be included in a source release:' -ForegroundColor Red
    $generatedDirs | ForEach-Object { Write-Host "  $($_.FullName)" }
}

if ($failed) { exit 1 }
Write-Host 'Public-release scan passed.' -ForegroundColor Green
