# Watchdog TM V4.3.10

V4.3.10 fixes saving alarm limits. Database, configuration, password, license and history are kept. No values are written to the PLC.

Earlier releases remain available unchanged on GitHub (V4.3.9, V4.3.8, V4.3.7 and older). See [Rollback](Rollback.md).

## Fixed

- **Save alarm limits did nothing.** In V4.3.5 to V4.3.9 the **Save alarm limits** button in the High/Low alarm window was not marked as the form's submit button, and the save script stopped before sending the limits. The window stayed open, no message appeared and the limits were not saved. The button is fixed and the script no longer depends on it.
- The release check now confirms the alarm window's Save button before publishing.

Everything from V4.3.9 is included: PLC history sized for any number of sensors per PLC, and confirmed record-format changes.

## Upgrade

1. Download a database backup from **Backup and restore**.
2. Run `Watchdog-TM-Server-4.3.10-Setup.exe` as administrator.
3. Reload the browser page (Ctrl+F5) so it uses the new page files.
4. Open a sensor's **High Alarm**, enter the limits and select **Save alarm limits**: the window closes and the card shows the new limits.
