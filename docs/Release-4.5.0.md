# Watchdog TM V4.5.0

V4.5.0 adds user levels. The Administrator creates levels, ticks what each level can monitor and control, and gives each person their own user name and password. Database, configuration, Administrator password, license and history are kept. No values are written to the PLC.

Earlier releases remain available unchanged on GitHub (V4.4.1, V4.3.10, V4.3.9 and older). See [Rollback](Rollback.md).

## User levels

Open **Settings → Users and levels** (Administrator only).

1. **Add user level**, for example "Operator", "Pharmacy" or "Viewer".
2. Tick what the level can do:
   - **Monitor:** view trends and history; export to Excel / CSV; view audit history. Every level can always view the dashboard.
   - **Control:** acknowledge alarms; change alarm limits; test read sensors; add/edit/delete sensors; add/edit/delete controllers; PLC history setup; start/stop live monitoring; Email and SMTP settings.
3. Choose **which sensors the level can see**: **All sensors** (including sensors added later), or tick individual sensors. A user limited to some sensors sees only those sensors on the dashboard, trends, exports, stored PLC history and email rules, and cannot act on any other sensor.
4. **Add user**: user name, level, password (at least 10 characters) and Enabled.

These are always Administrator-only: users and levels, license import, backup download and restore.

## Signing in

- Users enter their **user name** and password.
- The Administrator leaves the user name **blank** (or types `Administrator`) and uses the existing administrator password. Nothing changes for the Administrator.
- Users can change their own password under **Monitoring and license**.
- Changing a user's level applies immediately. Disabling or deleting a user, or resetting their password, signs them out at once.
- The audit history now records who made each change, and each sign-in.

The server checks every permission itself; hiding a button in the browser is only a convenience. Any API route without a permission rule is Administrator-only.

## Upgrade

1. Download a database backup from **Backup and restore**.
2. Run `Watchdog-TM-Server-4.5.0-Setup.exe` as administrator, then press Ctrl+F5 in the browser.
3. Sign in as Administrator (user name blank), open **Settings → Users and levels**, create a level, then the users.

Restoring a backup keeps the current users and levels, like the Administrator password and license.
