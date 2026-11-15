# Watchdog TM V4.3.3

V4.3.3 makes PLC-history recovery resilient to interrupted circular buffers. A stale, erased or incomplete PLC slot no longer prevents the later valid snapshots in the same download from being imported.

## Changes

- Imports every valid exact-sequence record that remains in the verified PLC buffer.
- Skips only malformed or stale slots and reports their count in the PLC-history status.
- Records every skipped sequence range as a precise detected PLC-history gap, instead of hiding later records behind the first bad slot.
- Supports the compact one-sensor history record layout (11 words) as well as the existing multi-sensor layouts.
- Updates all web assets to V4.3.3 so an upgraded browser does not reuse an older PLC-history script.

## Installation

Back up the Watchdog database, then run `Watchdog-TM-Server-4.3.3-Setup.exe` as administrator on Windows x64. The installer retains the existing database, administrator password, signed license and retained history. The ZIP is `Watchdog-TM-4.3.3.zip`.

After upgrade, open **PLC history and combined trends**, choose each enabled PLC, then select **Download PLC History** once. A successful import reports the number of imported records. A skipped-slot message is a preserved evidence of a PLC-side interruption; valid later records are still retained and charted.

For new PLC installations, make the PLC history sequence, record count and write position retentive across ordinary PLC restarts. Watchdog never writes to the PLC.
