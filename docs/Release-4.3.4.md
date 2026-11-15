# Watchdog TM V4.3.4

V4.3.4 adds a safe, reviewed correction path for PLC-history UTC offsets after history has already been imported.

## Changes

- Adds **Correct stored UTC offset (minutes)** to each PLC's verified history layout.
- Creates a database backup before changing imported PLC-history timestamps.
- Updates the saved PLC layout and shifts imported snapshot and sensor-history timestamps by the exact offset difference.
- Preserves PLC sequence numbers, recorded temperature values, quality states and raw-record hashes.
- Keeps ordinary PLC-history layout protection in place: buffer, record, channel and address changes still require a reviewed migration.

## Installation

Back up the Watchdog database, then run `Watchdog-TM-Server-4.3.4-Setup.exe` as administrator on Windows x64. The installer retains the existing database, administrator password, signed license and retained history. The ZIP is `Watchdog-TM-4.3.4.zip`.

After upgrade, open **PLC history and combined trends**, expand **Verified history layout** for the intended PLC, enter the required UTC offset in minutes, then choose **Correct stored timestamps**. Review the confirmation carefully. The action creates a named backup in the Watchdog data directory before it updates any existing timestamps. It does not write to a PLC.
