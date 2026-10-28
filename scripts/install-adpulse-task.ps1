<#
.SYNOPSIS
  Registers the Windows Scheduled Task "AdPulse API" that runs start-adpulse.ps1 hidden at every logon of the
  current user (and restarts it up to 3 times if it fails), so the API + ngrok tunnel survive reboots.
  Run once on the hosting PC:  powershell -ExecutionPolicy Bypass -File install-adpulse-task.ps1
#>
param([string]$Root = 'C:\Users\Rahul\apps\adpulse', [string]$TaskName = 'AdPulse API')
$ErrorActionPreference = 'Stop'
$script = Join-Path $Root 'start-adpulse.ps1'
if (-not (Test-Path $script)) { throw "missing $script" }

$me = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name   # DOMAIN\user form, required for MS-account users
$action   = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`"" -WorkingDirectory $Root
$trigger  = New-ScheduledTaskTrigger -AtLogOn -User $me
$trigger.Delay = 'PT20S'   # let the network come up first
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
            -ExecutionTimeLimit (New-TimeSpan -Seconds 0) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
$principal = New-ScheduledTaskPrincipal -UserId $me -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
Get-ScheduledTask -TaskName $TaskName | Select-Object TaskName, State
