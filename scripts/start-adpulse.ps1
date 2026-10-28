<#
.SYNOPSIS
  Starts the AdPulse API (Kestrel on http://127.0.0.1:8085) and the ngrok tunnel that publishes it.
  Installed on the hosting PC at C:\Users\Rahul\apps\adpulse\start-adpulse.ps1 and run by the
  Windows Scheduled Task "AdPulse API" at logon (see scripts/install-adpulse-task.ps1).
  Idempotent: re-running it replaces any instance started from the same folder.

  Layout on the PC:
    C:\Users\Rahul\apps\adpulse\app\            dotnet publish output (framework-dependent, win-x64)
    C:\Users\Rahul\apps\adpulse\app\appsettings.Production.json   provider=Postgres + Supabase connection string (NOT in git)
    C:\Users\Rahul\apps\adpulse\logs\           adpulse-YYYYMMDD.log (Serilog rolling), api-stdout.log, ngrok.log
#>
[CmdletBinding()]
param(
    [string]$Root = 'C:\Users\Rahul\apps\adpulse',
    [string]$Url = 'http://127.0.0.1:8085',
    [string]$NgrokDomain = 'reassign-tableful-breeding.ngrok-free.dev',
    [switch]$NoNgrok
)
$ErrorActionPreference = 'Stop'
$app  = Join-Path $Root 'app'
$logs = Join-Path $Root 'logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$stamp = { (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') }
$runlog = Join-Path $logs 'start-adpulse.log'
function Log($m) { "$(& $stamp) $m" | Tee-Object -FilePath $runlog -Append }

# ---- 1. stop previous instances launched from this folder ----
Get-CimInstance Win32_Process | Where-Object {
    ($_.Name -eq 'AdPulse.Api.exe' -and $_.ExecutablePath -like "$app*") -or
    ($_.Name -eq 'ngrok.exe' -and $_.CommandLine -like "*$NgrokDomain*")
} | ForEach-Object { Log "stopping pid $($_.ProcessId) ($($_.Name))"; Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 1

# ---- 2. API ----
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = $Url
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$api = Start-Process -FilePath (Join-Path $app 'AdPulse.Api.exe') -WorkingDirectory $app -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $logs 'api-stdout.log') -RedirectStandardError (Join-Path $logs 'api-stderr.log')
Log "api started pid $($api.Id) on $Url"

$healthy = $false
for ($i = 0; $i -lt 60 -and -not $healthy; $i++) {
    Start-Sleep -Seconds 1
    try { $healthy = (Invoke-WebRequest -UseBasicParsing "$Url/health" -TimeoutSec 3).Content -eq 'Healthy' } catch { }
    if ($api.HasExited) { Log "api exited early with code $($api.ExitCode) - see api-stderr.log"; exit 1 }
}
Log "api health: $healthy"
if (-not $healthy) { exit 1 }

# ---- 3. ngrok (free static domain; the browser interstitial is skipped when clients send ngrok-skip-browser-warning) ----
if (-not $NoNgrok) {
    $ngrok = Start-Process -FilePath 'ngrok' -ArgumentList @('http', ($Url -replace '^https?://127\.0\.0\.1:', ''), "--url=$NgrokDomain", '--log=stdout', '--log-format=json') `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logs 'ngrok.log') -RedirectStandardError (Join-Path $logs 'ngrok-stderr.log')
    Log "ngrok started pid $($ngrok.Id) -> https://$NgrokDomain"
}
Log 'done'
