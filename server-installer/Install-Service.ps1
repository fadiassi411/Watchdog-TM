param([Parameter(Mandatory=$true)][string]$InstallDirectory,[string]$LegacyDatabase)
$ErrorActionPreference='Stop'
$serviceName='WatchdogTM'
$exe=Join-Path $InstallDirectory 'Watchdog.TM.Web.exe'
$data=Join-Path $env:ProgramData 'Watchdog TM'
if(Get-Process -Name 'Watchdog TM' -ErrorAction SilentlyContinue){throw 'Close the old Watchdog TM desktop app before installing the web server.'}
$existing=Get-Service $serviceName -ErrorAction SilentlyContinue
if($existing -and $existing.Status -ne 'Stopped'){Stop-Service $serviceName;$existing.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(45))}
New-Item -ItemType Directory -Force $data | Out-Null
# The database includes configuration and password hashes. Only the service and admins need disk access.
& icacls.exe $data /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if($LASTEXITCODE -ne 0){throw 'Could not secure the server data directory.'}
$db=Join-Path $data 'watchdog.db'
if(-not(Test-Path -LiteralPath $db) -and $LegacyDatabase -and (Test-Path -LiteralPath $LegacyDatabase)){
    & $exe --migrate-db $LegacyDatabase $db
    if($LASTEXITCODE -ne 0){throw 'Desktop data migration failed. Original desktop data is unchanged.'}
    Copy-Item -LiteralPath $db -Destination (Join-Path $data ('before-web-migration-'+(Get-Date -Format yyyyMMddHHmmss)+'.db'))
}
$binary='"'+$exe+'" --urls http://0.0.0.0:5081'
if($existing){& sc.exe config $serviceName binPath= $binary start= delayed-auto DisplayName= 'Watchdog Temperature Monitoring Server' | Out-Null;if($LASTEXITCODE -ne 0){throw 'Service configuration failed.'}}
else{New-Service -Name $serviceName -BinaryPathName $binary -DisplayName 'Watchdog Temperature Monitoring Server' -Description 'Watchdog TM web dashboard and read-only temperature polling.' -StartupType Automatic | Out-Null;& sc.exe config $serviceName start= delayed-auto | Out-Null}
& sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
if($LASTEXITCODE -ne 0){throw 'Service recovery setup failed.'}
& sc.exe failureflag $serviceName 1 | Out-Null
$rule='Watchdog TM local network (TCP 5081)'
if(Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue){Remove-NetFirewallRule -DisplayName $rule}
New-NetFirewallRule -DisplayName $rule -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5081 -RemoteAddress LocalSubnet -Profile Any -Program $exe | Out-Null
# Replace the old per-user startup with the unattended Windows Service.
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name WatchdogTM -ErrorAction SilentlyContinue
Start-Service $serviceName
$ready=$false
for($attempt=0;$attempt -lt 30;$attempt++){try{$response=Invoke-RestMethod 'http://127.0.0.1:5081/health' -TimeoutSec 2;if($response.status -eq 'running'){$ready=$true;break}}catch{};Start-Sleep -Seconds 1}
if(-not $ready){throw 'Service did not become ready. Check ProgramData\Watchdog TM\Logs.'}
Write-Output 'Watchdog TM service is running on port 5081. Desktop configuration and history have been retained.'
