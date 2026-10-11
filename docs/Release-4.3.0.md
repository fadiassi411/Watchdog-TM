# Watchdog TM V4.3.0

V4.3.0 adds alarm management to the Windows web server. V4.2.2 remains a separate release with its original tag and assets.

## Alarm management

- Per-sensor activation delay (0–86400 seconds) filters brief excursions and applies to both dashboard alarms and initial email alerts.
- Per-sensor hysteresis sets a recovery margin to reduce repeated alerts near the threshold.
- Acknowledge an active alarm to stop blinking and suppress email reminders while retaining its active color and acknowledgement time.
- Acknowledgements persist in SQLite across server restarts and are audited. New episodes require new acknowledgement.
- Unavailable readings do not count as recovery; pending delays are interrupted by unavailable samples, long sample gaps or restart.
- Alarm settings validate ranges, and acknowledgement rejects stale episodes. Restore recreates the alarm table for older backups and clears previous acknowledgements.

Existing configurations use zero delay and zero hysteresis automatically, preserving immediate alarms. Initial and recovery emails retain their existing configuration. No alarm settings are written to the PLC. Legacy WPF alarm behavior is unchanged.

See [Alarm management](Alarm-management.md) for settings and examples. The web About/footer, installer metadata and bundled PDF identify V4.3.0.

## Upgrade

Back up your database, then run `Watchdog-TM-Server-4.3.0-Setup.exe` as administrator on Windows x64. The installer retains the existing database, administrator password, signed license and history. The ZIP is `Watchdog-TM-4.3.0.zip`. V4.2.2 is preserved and is not overwritten by the release workflow.

## Validation and release requirements

The Linux development build passed with zero warnings/errors; 41 portable alarm checks, Chromium browser/API checks and an actual server stop/restart acknowledgement check passed. Full Windows DPAPI/WPF suites and installer validation cannot run on the Linux development machine.

The V4.3.0 GitHub Actions workflow runs the full core and web suites on Windows, builds the self-contained Windows x64 server and Inno Setup installer, then packages assets and SHA-256 checksums. Publication occurs only after successful checks. Physical PLC commissioning and real SMTP delivery remain installation-specific and are not claimed as release verification.
