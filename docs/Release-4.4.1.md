# Watchdog TM V4.4.1

V4.4.1 makes temperature alarm email easy to verify. Database, configuration, password, license and history are kept. No values are written to the PLC.

Earlier releases remain available unchanged on GitHub (V4.3.10, V4.3.9, V4.3.8 and older). See [Rollback](Rollback.md).

## Why alarm emails could be missing

The SMTP **test email** only proves that the mail server accepts Watchdog's messages. An automatic alarm email additionally needs all of these:

1. A saved **High Alarm** (or Low Alarm) limit on the sensor. In V4.3.5 to V4.3.9 the alarm window could not save limits, so no alarm, and no alarm email, could occur. Fixed in V4.3.10.
2. **Enable temperature alarm email** and **Automatically email temperature alarms** both switched on.
3. For each sensor: **Enable email for this sensor**, recipients, **High temperature** (and/or Low) ticked, and **Save sensor notifications**.
4. Live monitoring (not demonstration mode), with the sensor's controller enabled.

An end-to-end test of the service wiring (simulated PLC → polling → alarm → mail queue → SMTP) confirms that a high-alarm email and a return-to-normal email are sent when these conditions are met.

## New

- **Alarm email readiness** on **Email and SMTP**: shows whether automatic alarm email is on, and under every sensor either **Ready** (for example "Emails alarm@site.com when the temperature is above 21 °C or below 18 °C for 10 s.") or **Not ready** with the exact reason: email off, no recipients, no High/Low limit, automatic alerts off, demonstration mode, controller disabled, incomplete SMTP settings.
- **Send test alarm email** under every sensor: sends one sample alarm email, in the real alarm format, to that sensor's own recipients. Nothing is queued or saved; the audit log records it without the addresses.
- The release check now also confirms the Email page's readiness and test controls.

## Upgrade

1. Download a database backup from **Backup and restore**.
2. Run `Watchdog-TM-Server-4.4.1-Setup.exe` as administrator, then press Ctrl+F5 in the browser.
3. Open **Email and SMTP**, fix anything listed under **Alarm email readiness**, and use **Send test alarm email** on one sensor.
