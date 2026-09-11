# V4.2.1

Watchdog TM now runs as a local-network ASP.NET Core Windows Service. The release includes automatic continuation of live monitoring after configuration edits and restart, dashboard software alarm thresholds, and interactive temperature trends with selectable periods and recording/chart intervals.

The ZIP contains the same Windows x64 installer distributed separately, plus the server guide. Existing configuration and historical readings are retained by server upgrades. Customer data and private signing keys are not included.

Validation: regression checks cover core behavior, Modbus TCP loopback reads, automatic live-mode persistence, trend grouping, extrema, invalid readings, bounded chart density, and period validation. Browser and authenticated API checks cover Trend controls and recording-interval saves. Real hardware verification is specific to each installation.

Limitations: Excel exports cover up to 31 days per file. The chart supports periods up to one year at a time. Software alarm emails are not enabled. No public internet hosting is configured.

## Documentation update
Added the bundled nine-page PDF customer Help guide, a Help centre with open/download actions and chapter links, and an About page with version, publisher, platform, operating mode and installation license information.
