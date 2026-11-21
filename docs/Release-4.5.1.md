# Watchdog TM V4.5.1

V4.5.1 makes the Administrator sign-in unmistakable. Database, configuration, users, levels, passwords, license and history are kept. No values are written to the PLC.

Earlier releases remain available unchanged on GitHub (V4.5.0, V4.4.1, V4.3.10 and older). See [Rollback](Rollback.md).

## Why the Administrator could not sign in

In V4.5.0 the Administrator had to leave the **User name** box empty. After a user (for example Jawad) signed in on the same PC, the browser filled that user's name into the box again, so the Administrator password was checked against the user's account and refused. Several tries then triggered the one-minute sign-in lock.

## Fixed

- **Sign in as Administrator** box on the sign-in page. When ticked, the user name box is hidden and ignored, whatever the browser filled into it. The page remembers whether you last signed in as Administrator or as a user.
- `admin` is accepted as well as `Administrator` (or a blank name). Neither can be used as a user name.
- Typing the Administrator password with a user name now shows: *That is the Administrator password. Tick "Sign in as Administrator" and sign in again.*
- Too many attempts now shows *Too many sign-in attempts. Wait one minute and try again.*

## Upgrade

1. Download a database backup from **Backup and restore**.
2. Run `Watchdog-TM-Server-4.5.1-Setup.exe` as administrator, then press Ctrl+F5 in the browser.
3. Tick **Sign in as Administrator**, enter your Administrator password, and sign in.
