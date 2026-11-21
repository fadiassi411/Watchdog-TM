# Returning to an earlier Watchdog TM version

Every earlier release stays available, unchanged, on the GitHub Releases page: V4.4.1, V4.3.10, V4.3.9, V4.3.8, V4.3.7, V4.3.6, V4.3.5, V4.3.4, V4.3.3 and older. Each release keeps its own installer, ZIP and SHA256SUMS.txt.

The server database (C:\ProgramData\Watchdog TM\watchdog.db) is kept by every installer and uninstaller, so changing version does not remove configuration, password, license or history.

## Before upgrading

1. In **Backup and restore**, download a database backup.
2. Keep the installer you are currently using (for example `Watchdog-TM-Server-4.3.4-Setup.exe`) next to the backup.

## Roll back from V4.5.0, V4.4.1, V4.3.10, V4.3.9, V4.3.8, V4.3.7 or V4.3.6

1. Download the installer for the version you want from GitHub Releases, for example **Watchdog TM V4.3.4** → `Watchdog-TM-Server-4.3.4-Setup.exe`. Optionally check it against that release's SHA256SUMS.txt.
2. Run it as administrator. It installs over the newer version and restarts the Watchdog TM service with the same database.
3. Open http://localhost:5081 and confirm the footer shows the expected version.

If you want the database exactly as it was before upgrading, sign in after the rollback and use **Restore database** with the backup you downloaded before upgrading.

## Notes

- Versions before V4.5.0 ignore user levels and user accounts: only the Administrator password works. The accounts are kept in the database and come back when V4.5.0 is reinstalled.
- Earlier versions ignore the V4.3.6 `timeZoneId` setting and use the fixed `utcOffsetMinutes` again. Records already imported keep their corrected timestamps. Records downloaded after the rollback use the fixed offset.
- V4.3.5 to V4.3.9 cannot save alarm limits from the alarm window (fixed in V4.3.10). After rolling back to one of them, change alarm limits only after upgrading again.
- V4.3.8 and earlier ignore the V4.3.9 `importAfterSequence` value. After a record-format change, keep the PLC count reset so that old records are not read again in the new format.
- Versions before V4.3.8 only accept 144- or 300-record PLC history layouts. If a controller uses the 111-record retained buffer, its history downloads stop after a rollback until V4.3.8 or later is reinstalled; already imported history is kept.
- V4.3.4 contains the earlier timestamp-correction defect fixed in V4.3.5. After rolling back to V4.3.4, do not use **Correct stored timestamps**.
- Restore in V4.3.5 and earlier is limited to backups of about 28 MB. Restore a larger backup with V4.3.6.
