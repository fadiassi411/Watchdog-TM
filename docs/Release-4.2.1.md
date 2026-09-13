# V4.2.1

Watchdog TM now runs as a local-network ASP.NET Core Windows Service. The release includes automatic continuation of live monitoring after configuration edits and restart, dashboard software alarm thresholds, and interactive temperature trends with selectable periods and recording/chart intervals.

The ZIP contains the same Windows x64 installer distributed separately, plus the server guide. Existing configuration and historical readings are retained by server upgrades. Customer data and private signing keys are not included.

Validation: regression checks cover core behavior, Modbus TCP loopback reads, automatic live-mode persistence, trend grouping, extrema, invalid readings, bounded chart density, and period validation. Browser and authenticated API checks cover Trend controls and recording-interval saves. Real hardware verification is specific to each installation.

Limitations: Excel exports cover up to 31 days per file. The chart supports periods up to one year at a time. Automatic alarm emails are available and disabled by default until SMTP and sensor recipients are configured. No public internet hosting is configured.

## Documentation update
Added the bundled PDF customer Help guide, a Help centre with open/download actions and chapter links, and an About page with version, publisher, platform, operating mode and installation license information.

## SMTP update
Added SMTP settings, encrypted password storage, explicit save/test, per-sensor high/low/recovery/reminder notification rules and persistent delivery history. Automatic sends are off by default and suppressed in demonstration mode. Help now contains ten pages including SMTP instructions.


## Screen lock and session recovery
Administrator sessions now persist for 30 days with sliding renewal. The browser reconnects on unlock, focus and network recovery, refreshes session tokens, and preserves open settings forms. Sign out and back in once after upgrading to replace an older 30-minute session. Windows sleep still pauses hardware monitoring; keep the server awake.
Validation: six browser recovery checks and a successful Release build. Physical Windows lock/unlock remains a field check.
