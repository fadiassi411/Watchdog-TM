# Delete controllers and registers — V4.2.2 update

Open **Controllers and sensors**. Choose **Delete register** beside a sensor, or **Delete controller** on a controller card. Confirm the name and, for a controller, the number of attached sensor/register entries.

Deleting a controller also removes its attached sensors from active configuration. Monitoring stops for removed entries, commissioned IDs and PLC-history channel mappings are removed, and pending notifications are cancelled. A controller with no remaining history channels has history synchronization disabled. Other controllers remain configured.

Saved readings are not erased. Open **PLC history and combined trends**, leave the PLC and sensor filters at **All**, and use **Show trends**, **Export CSV**, or **Export Excel**. Deleted entries retain their sensor and PLC names in these results. Deleted entries no longer appear in active sensor/controller selectors.

Before deletion the server creates a `before-delete-<timestamp>-<unique-id>.db` backup in its data directory (normally `C:\ProgramData\Watchdog TM`). Backups contain the complete database and must be kept private. Restoring one uses the existing full-database restore workflow and also restores the configuration/history as of that backup; it is not a selective undo.

Deletion requires an authenticated session, antiforgery validation, and a current configuration revision. A stale page must reload before it can delete. Deletion never writes to the PLC and does not erase the PLC's own buffer.
