# Watchdog TM V4.3.2

V4.3.2 improves PLC-history diagnostics and removes browser Content Security Policy warnings from the combined-history page.

## PLC-history diagnostics

- **Test PLC History Header:** PLC history and combined trends now includes a read-only test that reads the configured header words. It never writes to a PLC.
- **Actionable download failure:** a failed history download now displays the underlying Modbus or validation error rather than a generic connection-and-mapping message.
- **No PLC mapping changes:** existing verified history layouts, sensor mapping, stored history, live values and alarms are preserved.

## Browser security-policy correction

- The combined-history page no longer generates inline style attributes. Trend legend colours are supplied by the application stylesheet, preserving the existing restrictive Content Security Policy.

## Upgrade

Back up the Watchdog database, then run `Watchdog-TM-Server-4.3.2-Setup.exe` as administrator on Windows x64. The installer retains the existing database, administrator password, signed license and history. The ZIP is `Watchdog-TM-4.3.2.zip`.

After upgrading, open **PLC history and combined trends**, select the controller, and click **Test PLC History Header**. For the DVP history configuration used in commissioning, a successful test reads holding-register offset `8996` and returns the five header words corresponding to `D4900:D4904`.

## Validation and release requirements

The Windows release workflow runs the core and web regression suites, builds the self-contained Windows x64 server and Inno Setup installer, verifies the packaged server, and publishes installer, ZIP, and SHA-256 checksums only after those checks pass. Physical PLC commissioning remains installation-specific acceptance work.
