param(
    [switch]$SkipBuild,
    [int]$StartupTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipBuild) {
    Write-Host '== Building AUnrealFeatures.sln =='
    dotnet build .\AUnrealFeatures.sln
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
}

$stdout = Join-Path $env:TEMP 'aunrealfeatures-smoke.out.log'
$stderr = Join-Path $env:TEMP 'aunrealfeatures-smoke.err.log'
Remove-Item $stdout,$stderr -ErrorAction SilentlyContinue

Write-Host '== Starting backend =='
$proc = Start-Process dotnet -ArgumentList @('run','--no-build','--project','AUnrealFeatures.Ares') `
    -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr

try {
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 500
        if ($proc.HasExited) {
            $err = (Get-Content $stderr -Raw -ErrorAction SilentlyContinue)
            $out = (Get-Content $stdout -Raw -ErrorAction SilentlyContinue)
            throw "Backend exited during startup.`nSTDOUT:`n$out`nSTDERR:`n$err"
        }
        try {
            Invoke-RestMethod -Method Get -Uri 'http://127.0.0.1:8080/api/sessions' -TimeoutSec 2 | Out-Null
            $ready = $true
        } catch { $ready = $false }
    } until ($ready -or (Get-Date) -gt $deadline)

    if (-not $ready) { throw "Backend did not become ready within $StartupTimeoutSeconds seconds" }

    $deploymentId = 'smoke-' + [guid]::NewGuid().ToString('N')
    $registerUri = 'http://127.0.0.1:78/v1/server/register_server'

    $first = @{
        deployment_id = $deploymentId
        server_name    = 'AUnrealFeatures Smoke'
        ip             = '127.0.0.1'
        port           = '7777'
        region         = 'TEST'
        max_players    = 8
        version        = '1'
        build_id       = 1
    } | ConvertTo-Json

    Write-Host '== Registering server =='
    $r1 = Invoke-RestMethod -Method Post -Uri $registerUri -ContentType 'application/json' -Body $first
    if (-not $r1.success) { throw 'First register_server call failed' }

    # Re-register the same deployment with changed endpoint. This verifies the LiteDB update path and
    # EOS deployment de-duplication instead of just testing a first-time insert.
    $second = @{
        deployment_id = $deploymentId
        server_name    = 'AUnrealFeatures Smoke'
        ip             = '127.0.0.1'
        port           = '7788'
        region         = 'TEST'
        max_players    = 8
        version        = '1'
        build_id       = 1
    } | ConvertTo-Json

    Write-Host '== Re-registering same deployment =='
    $r2 = Invoke-RestMethod -Method Post -Uri $registerUri -ContentType 'application/json' -Body $second
    if (-not $r2.success) { throw 'Second register_server call failed' }

    $sessions = @(Invoke-RestMethod -Method Get -Uri 'http://127.0.0.1:8080/api/sessions')
    $ours = @($sessions | Where-Object { $_.deployment_id -eq $deploymentId })
    if ($ours.Count -ne 1) { throw "Expected exactly one EOS session for $deploymentId; got $($ours.Count)" }
    if ($ours[0].port -ne '7788') { throw "Re-registration did not update EOS port; got $($ours[0].port)" }

    $filterBody = @{
        criteria = @(
            @{ key='attributes.SERVERNAME_s'; op='EQUAL'; value='AUnrealFeatures Smoke' }
        )
        maxResults = 20
    } | ConvertTo-Json -Depth 6
    $eosDeployment = if ($env:EOS_DEPLOYMENT_ID) { $env:EOS_DEPLOYMENT_ID } else { 'YOUR_EOS_DEPLOYMENT_ID' }
    $filterUri = "http://127.0.0.1:50/matchmaking/v1/$eosDeployment/filter"
    $filter = Invoke-RestMethod -Method Post -Uri $filterUri -ContentType 'application/json' -Body $filterBody
    $found = @($filter.sessions | Where-Object { $_.attributes.PUBLICPORT_s -eq '7788' })
    if ($found.Count -lt 1) { throw 'EOS matchmaking did not return the re-registered server endpoint' }

    Write-Host '== Deleting deployment and checking EOS cleanup =='
    Invoke-RestMethod -Method Delete -Uri ("http://127.0.0.1:78/deployments/{0}" -f $deploymentId) | Out-Null
    $sessionsAfter = @(Invoke-RestMethod -Method Get -Uri 'http://127.0.0.1:8080/api/sessions')
    if (@($sessionsAfter | Where-Object { $_.deployment_id -eq $deploymentId }).Count -ne 0) {
        throw 'Deleting the deployment left a stale EOS session behind'
    }

    Write-Host 'PASS: build/startup, idempotent registration, EOS discovery, and deployment cleanup passed.'
}
finally {
    if ($proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
    Write-Host "Backend logs: $stdout / $stderr"
}
