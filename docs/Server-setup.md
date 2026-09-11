# Watchdog TM Server — local network installation

Watchdog TM uses the same server arrangement as Watchdog EM: a self-contained ASP.NET Core application, an automatic Windows Service, SQLite in ProgramData, and a browser shortcut. TM uses TCP 5081 so it can coexist with EM on TCP 5080.

## Installation and migration

1. Save edits and close the old Watchdog TM desktop application.
2. Run Watchdog-TM-Server-4.2.1-Setup.exe and approve Windows administrator elevation.
3. The installer copies the existing user's desktop database into C:\ProgramData\Watchdog TM\watchdog.db only if a server database does not already exist. It creates a migration backup. Existing server upgrades retain their database.
4. The Watchdog Temperature Monitoring Server service starts automatically with delayed startup and restart-on-failure recovery.
5. Open the Watchdog TM desktop shortcut or http://localhost:5081. Sign in using the existing TM administrator password. No default password is created. New installations create their password locally on the server PC.

If installation uses a different Windows administrator account, supply the original desktop database explicitly to Install-Service.ps1 -InstallDirectory "C:\Program Files\Watchdog TM Server" -LegacyDatabase "C:\Users\ORIGINAL-USER\AppData\Local\WatchdogTM\watchdog.db" before first server startup. Do not overwrite an existing server database; use authenticated backup/restore instead.

## Access from the local network

On a phone or PC connected to the same network, open http://SERVER-IP:5081. Find the server PC's IPv4 address with ipconfig. Prefer a DHCP reservation so its address stays fixed. The installer allows TCP 5081 from the local subnet only. Routed/VLAN clients need a site-specific firewall rule approved by the network administrator.

This follows EM's local HTTP deployment. Use a trusted LAN; no public internet access or router port forwarding is configured. HTTPS/reverse-proxy or VPN deployment is a separate site setup.

## Monitoring

Connect the USB/RS485 adapter to the server computer, not to the browsing device. Configure RTU/TCP under Settings > Controllers and sensors. Enter only the temperature register, read function, data type, word order, multiplier and correction. A 32-bit value reads two consecutive words. The server never writes to the PLC or reads alarm/limit registers. Keep the existing DDC program unchanged.

If in demonstration mode, select Settings > Monitoring and license > Switch to live monitoring once. The existing signed TM license remains valid on this PC. Saved controller and sensor changes apply automatically while live mode is enabled. Polling continues when the browser closes or Windows is locked/signed out. Keep the computer powered, awake and the adapter connected. Sleep or shutdown interrupts collection.

The historical recording interval is separate from the controller polling interval. Trends support periods up to one year; Excel downloads support up to 31 days per export. Software high/low temperature alarms are evaluated internally; automatic alarm email delivery remains off. Legacy historical database records and SMTP configuration are retained, but old PLC-alarm control fields are not exposed in the web UI.

## Operations

- Service: WatchdogTM / Watchdog Temperature Monitoring Server.
- Application: C:\Program Files\Watchdog TM Server.
- Database, backups, protected cookie keys and logs: C:\ProgramData\Watchdog TM.
- Daily service logs are retained for 30 days.
- Backups contain private configuration and password hashes; store them securely.
- Restore keeps the current administrator password and license, saves a recovery backup, and returns to demonstration mode until explicitly restarted.
- The installer replaces the desktop shortcut with a browser launcher and disables the old desktop auto-start. Do not run both TM versions simultaneously. Original desktop files/data remain for rollback.
- Uninstalling the server removes its service and firewall rule but retains the customer database and backups.

## Build from source

Run dotnet publish src/Watchdog.TM.Web/Watchdog.TM.Web.csproj -c Release -r win-x64 --self-contained true -o server, then compile server-installer/WatchdogTM-Web.iss with Inno Setup 6. The web project links the tested TM temperature transport, history store and license verifier; the Watchdog EM project is unchanged.

## Dashboard trends and software alarm limits
Each reading card has a Trend button that opens its recorded history, and High Alarm / Low Alarm buttons for setting local thresholds. Values above the high limit blink red; values below the low limit blink yellow. Equal-to-limit readings are normal. Leave a limit blank to disable it. Limits persist in configuration and backups. Stale, offline, disabled or missing readings do not raise a current temperature alarm. Browsers refresh alarm status every five seconds. Reduced-motion preferences show a steady alarm color. This update does not enable alarm emails or write anything to the PLC.

Once live monitoring is enabled, controller and sensor edits apply automatically. The Windows service resumes configured live readings after a restart. Disabled or retired sensors remain stopped; demonstration mode is an explicit option.

Trends: choose a quick period or custom range up to one year. Chart interval selects the averaging period with min-max range and gaps. Very dense ranges automatically increase the chart interval. Recording interval changes future saved history from 1 second to 24 hours, without changing PLC polling. Excel exports original readings for ranges up to 31 days. The sample table is removed; stored history is retained.
