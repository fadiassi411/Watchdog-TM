$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
function Run-Dotnet([string[]]$Arguments) { & dotnet @Arguments; if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $Arguments" } }
Run-Dotnet @('build','tests/Watchdog.TM.Tests.csproj','-c','Release','-m:1','-nr:false')
Run-Dotnet @('tests/bin/Release/net10.0-windows/Watchdog.TM.Tests.dll')
Run-Dotnet @('publish','src/Watchdog.TM/Watchdog.TM.csproj','-c','Release','-r','win-x64','--self-contained','true','-o','app','-m:1','-nr:false')
Run-Dotnet @('publish','tools/WatchdogLicenseManager/WatchdogLicenseManager.csproj','-c','Release','-r','win-x64','--self-contained','true','-o','supplier/Manager','-m:1','-nr:false')
Run-Dotnet @('publish','tools/WatchdogLicenseGenerator/WatchdogLicenseGenerator.csproj','-c','Release','-r','win-x64','--self-contained','true','-o','supplier/Generator','-m:1','-nr:false')
New-Item -ItemType Directory -Force app/docs | Out-Null
Copy-Item docs/Customer-guide.html,docs/Customer-guide.md,docs/PLC-integration-template.md,docs/Commissioning-checklist.md app/docs
New-Item -ItemType Directory -Force app/notices | Out-Null
Copy-Item docs/Third-party-licenses/* app/notices
$iscc = Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'
if (-not (Test-Path $iscc)) { $iscc = 'C:/Program Files (x86)/Inno Setup 6/ISCC.exe' }
& $iscc installer.iss
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }

