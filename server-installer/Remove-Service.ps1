$ErrorActionPreference='Stop'
$service=Get-Service WatchdogTM -ErrorAction SilentlyContinue
if($service){if($service.Status -ne 'Stopped'){Stop-Service WatchdogTM;$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(45))};& sc.exe delete WatchdogTM | Out-Null}
Get-NetFirewallRule -DisplayName 'Watchdog TM local network (TCP 5081)' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
# Customer database, history and backups are deliberately retained in ProgramData.
