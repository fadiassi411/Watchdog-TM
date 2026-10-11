$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
& dotnet publish src/Watchdog.TM.Web/Watchdog.TM.Web.csproj -c Release -r win-x64 --self-contained true -o server -m:1 -nr:false
if($LASTEXITCODE -ne 0){throw 'Server build failed'}
Copy-Item docs/Server-setup.md server/Server-setup.md
Copy-Item docs/Alarm-management.md,docs/Release-4.3.0.md server
New-Item -ItemType Directory -Force server/notices | Out-Null
Copy-Item docs/Third-party-licenses/* server/notices
$iscc=Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'
if(-not(Test-Path $iscc)){$iscc='C:/Program Files (x86)/Inno Setup 6/ISCC.exe'}
& $iscc server-installer/WatchdogTM-Web.iss
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
