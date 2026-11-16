# Returning to an earlier Watchdog TM version

Every earlier release stays available, unchanged, on the GitHub Releases page: V4.3.6, V4.3.5, V4.3.4, V4.3.3 and older. Each release keeps its own installer, ZIP and SHA256SUMS.txt.

The server database (C:\ProgramData\Watchdog TM\watchdog.db) is kept by every installer and uninstaller, so changing version does not remove configuration, password, license or history.

## Before upgrading

1. In **Backup and restore**, download a database backup.
2. Keep the installer you are currently using (for example `Watchdog-TM-Server-4.3.4-Setup.exe`) next to the backup.

## Roll back from V4.3.7 or V4.3.6

1. Download the installer for the version you want from GitHub Releases, for example **Watchdog TM V4.3.4** → `Watchdog-TM-Server-4.3.4-Setup.exe`. Optionally check it against that release's SHA256SUMS.txt.
2. Run it as administrator. It installs over the newer version and restarts the Watchdog TM service with the same database.
3. Open http://localhost:5081 and confirm the footer shows the expected version.

If you want the database exactly as it was before upgrading, sign in after the rollback and use **Restore database** with the backup you downloaded before upgrading.

## Notes

- Earlier versions ignore the V4.3.6 `timeZoneId` setting and use the fixed `utcOffsetMinutes` again. Records already imported keep their corrected timestamps. Records downloaded after the rollback use the fixed offset.
- V4.3.4 contains the earlier timestamp-correction defect fixed in V4.3.5. After rolling back to V4.3.4, do not use **Correct stored timestamps**.
- Restore in V4.3.5 and earlier is limited to backups of about 28 MB. Restore a larger backup with V4.3.6.
