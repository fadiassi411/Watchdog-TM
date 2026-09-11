# Watchdog TM V4.2.1

Windows x64 temperature monitoring server from MicroBrain. The self-contained ASP.NET Core application runs as the WatchdogTM Windows Service and is accessed through a browser on the local network.

## Install

Download `Watchdog-TM-Server-4.2.1-Setup.exe` from the GitHub release, or extract it from `Watchdog-TM-4.2.1.zip`. Run the installer as administrator, then open http://localhost:5081. Other devices on the local network use http://SERVER-IP:5081. A fresh installation prompts for an administrator password locally. Existing server installations retain their configuration, password, license and history.

Keep the server powered and awake. Closing the browser does not stop monitoring. Configure verified RTU/TCP settings and temperature registers; live operation requires a signed TM license. The runtime contains public verification material only, never the supplier private key.

## Features

- Read-only Modbus RTU/TCP temperature acquisition (FC03/FC04); no PLC writes.
- Persistent live mode; controller and sensor edits apply automatically.
- Per-sensor software high/low thresholds with red/yellow blinking cards.
- Trends with quick/custom periods up to one year, zoom, hover details, min/average/max and selectable chart intervals.
- Configurable recording intervals from one second to one day. Recording faster does not increase controller polling speed.
- SQLite history retained without automatic sample deletion; Excel export up to 31 days per export.
- Local password authentication, backup/restore, and signed product-specific capacity licensing.

Software alarm emails are not enabled in this web release. Backup restore intentionally returns to demonstration mode. The server is intended for a trusted local network; no public internet endpoint is configured. Physical commissioning remains specific to the customer's controller, wiring and register map.

## Build and test

Use Windows x64, .NET SDK 10 and Inno Setup 6. Initial restore requires NuGet access. Customers do not need the SDK.

```powershell
./build-server.ps1
dotnet build tests/Watchdog.TM.Tests.csproj -c Release -m:1 -nr:false
dotnet tests/bin/Release/net10.0-windows/Watchdog.TM.Tests.dll
dotnet run --project tests/Watchdog.TM.Web.Tests -c Release -m:1
```

The installer is written to `artifacts/`. `src/Watchdog.TM.Web` shares core models, storage, transport and license verification with `src/Watchdog.TM`. The WPF desktop source and its documentation are retained for legacy reference; the server installer is the current release. Supplier tool source is separate from the customer runtime. Tests create temporary signing keys in memory.

See [server setup](docs/Server-setup.md) and [release notes](docs/Release-4.2.1.md).
