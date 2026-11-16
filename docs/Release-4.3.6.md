# Watchdog TM V4.3.6

V4.3.6 is a reliability and maintenance release. It keeps the existing database, configuration, password, license and history. No values are written to the PLC.

Earlier releases remain available unchanged on GitHub (V4.3.5, V4.3.4 and older). See [Rollback](Rollback.md) to return to an earlier version.

## Fixed

- **Daylight saving for PLC history.** A PLC history layout can now name the PLC clock's time zone (`timeZoneId`, for example `Asia/Beirut`). Summer (UTC+3) and winter (UTC+2) timestamps are then converted correctly, including the repeated hour when clocks go back. Leave it blank to keep the fixed `utcOffsetMinutes` behaviour, which is correct for PLC clocks that never change for summer time.
- **Correct stored timestamps** accepts the time zone too, and recalculates every imported snapshot from the PLC's original local clock. A database backup is still created first.
- **Large backup restore.** Restore previously failed for backups larger than about 28 MB even though the screen allowed 100 MB. Backups up to 8 GB are now accepted.
- **Large downloads.** Database backups, single-sensor Excel exports and the stored-history Excel export now stream from a temporary file instead of being built in memory, so multi-year databases no longer risk exhausting server memory. Temporary files are deleted when the download ends.
- **Dashboard freezes.** Sending an alarm email (up to 15 seconds when the mail server is slow or unreachable) and **Test connection** no longer hold the server's configuration lock, so the dashboard, settings and PLC history import stay responsive.
- **Login lockout.** The limit of 5 login attempts per minute now applies per client address, so one device on the network can no longer lock everyone else out.
- **Error messages.** Unexpected internal errors return a generic message (details go to the server log) with HTTP 500. Validation messages are unchanged. Unknown sensor IDs return "not found".
- **Fault banner.** A successful daily backup no longer clears an active recording fault, and the recording fault clears itself once sampling recovers.
- **PLC history after restore.** A controller that disappears and later returns (for example through backup restore) resumes automatic history downloads without restarting the service.
- **Safety backups.** Automatic `before-restore-`, `before-delete-` and `before-plc-history-offset-` copies are limited to the newest 10 of each kind. Daily backups in your backup folder and the installer's migration backup are not affected.

## Release housekeeping

- Version shown in the dashboard, installer and executable is 4.3.6.
- The server package includes these notes and the rollback guide.
- A dedicated `release-4.3.6.yml` workflow builds, tests, smoke-tests and publishes only V4.3.6.

## Upgrade

1. Open **Backup and restore** and download a database backup. Keep it with the V4.3.5 or V4.3.4 installer.
2. Run `Watchdog-TM-Server-4.3.6-Setup.exe` as administrator.
3. Open http://localhost:5081 and confirm the footer shows **V4.3.6 Web**.

### Optional: PLC clocks that follow summer time

If the PLC clock moves forward in summer, open **PLC history and combined trends**, expand the controller's **Verified history layout**, enter `Asia/Beirut` in **PLC time zone** and select **Correct stored timestamps** once. If the PLC clock stays on winter time all year, keep the fixed offset `120` and leave the time zone blank.
