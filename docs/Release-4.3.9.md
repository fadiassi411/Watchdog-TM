# Watchdog TM V4.3.9

V4.3.9 makes PLC history flexible: each PLC can record any number of sensors, and a PLC program can change after history was already imported. Database, configuration, password, license and history are kept. No values are written to the PLC.

Earlier releases remain available unchanged on GitHub (V4.3.8, V4.3.7, V4.3.6 and older). See [Rollback](Rollback.md).

## New

### Any number of sensors per PLC
**Load standard MicroBrain layout → Retained buffer D2000 · sized for this PLC's sensors** counts the sensors set to D100, D101, D102 … on that controller and sizes the layout to fit the DVP12SE retained area (D2000–D3899):

| Sensors | Record words | Records | About | Buffer |
|---|---|---|---|---|
| 1 | 11 | 144 | 24 h | D2000–D3583 |
| 2 | 12 | 144 | 24 h | D2000–D3727 |
| 3 | 13 | 144 | 24 h | D2000–D3871 |
| 7 | 17 | 111 | 18.5 h | D2000–D3886 |
| 8 | 18 | 105 | 17.5 h | D2000–D3889 |
| 12 | 22 | 86 | 14.3 h | D2000–D3891 |
| 18 | 28 | 67 | 11.2 h | D2000–D3875 |

The result also lists the **PLC program values** for ISPSoft: the MUL factor (record words), the record limit used in the four checks, the `@E0` destinations for every temperature, the status contacts (M20 …) and the buffer range.

- A PLC history layout now accepts **6 to 300 records** (previously only 144 or 300) and up to 115 sensors.
- A sensor register that is not used (for example D102 when D100, D101 and D103 are used) keeps its word in the record, so later sensors keep their positions.
- Adding controllers and sensors is limited only by the TM license's sensor count.

### Record-format change after history was imported
Changing a PLC program (for example from 2 sensors to 1) changes the record format. **Save history layout** now asks for confirmation instead of refusing. After you confirm:
- every sample already imported stays exactly as it is, including those of removed sensors;
- the server stores the highest imported sequence (`importAfterSequence`, set by the server only) and imports only newer records, in the new format;
- the audit log records the change (old and new record size and channel count).

Clock settings (`utcOffsetMinutes`, `timeZoneId`, `yearBase`) still cannot change on this screen; use **Correct stored timestamps**. Change the layout only after the PLC program was changed and its count and slot (D3903, D3904, D4903, D4904) were reset to 0.

## Upgrade

1. Download a database backup from **Backup and restore**.
2. Run `Watchdog-TM-Server-4.3.9-Setup.exe` as administrator.
3. For each PLC: choose **Retained buffer D2000 · sized for this PLC's sensors** and **4-digit year**, select **Load standard MicroBrain layout**, compare the listed PLC program values with the ISPSoft program, use **Test PLC History Header**, then **Save history layout**. Confirm the record-format change when asked.

Existing layouts (for example F2A with 7 sensors, 111 × 17) are unchanged and match what the new option produces.
