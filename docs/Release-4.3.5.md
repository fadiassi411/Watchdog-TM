# Watchdog TM V4.3.5

V4.3.5 fixes PLC-history UTC offset correction.

## Fixed

- Correcting a PLC history layout from UTC offset `0` to `+120` now moves stored timestamps two hours earlier in UTC, which is the correct instant for a PLC that records local Lebanon time.
- Trend charts include corrected PLC-history readings in the current time window instead of treating them as future readings.
- The correction creates a database backup before changing timestamps and preserves sequence numbers, values, quality states and record hashes.

## Upgrade and correct an affected PLC

1. Back up the database.
2. Install `Watchdog-TM-Server-4.3.5-Setup.exe` as administrator.
3. Open **PLC history and combined trends**.
4. Expand the affected controller's **Verified history layout**.
5. Enter `120` in **Correct stored UTC offset (minutes)** and select **Correct stored timestamps** once.
6. Open **Trends and export**, choose the sensor, and select **Last 24 hours**.

The correction does not write to the PLC.
