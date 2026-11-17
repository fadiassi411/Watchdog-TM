# Watchdog TM V4.3.7

V4.3.7 adds a ready-made PLC history layout for the standard MicroBrain Delta DVP program. Database, configuration, password, license and history are kept. No values are written to the PLC.

Earlier releases remain available unchanged on GitHub (V4.3.6, V4.3.5, V4.3.4 and older). See [Rollback](Rollback.md).

## New: Load standard MicroBrain layout

Open **PLC history and combined trends**, expand a controller's **Verified history layout**, choose the **PLC year format** and select **Load standard MicroBrain layout**. The layout editor is filled for the standard `History_Record` program:

| Item | PLC register | Modbus address |
|---|---|---|
| Header: status, sequence (2 words), count, next slot | D4900–D4904 | 37668–37672 |
| 144 records × 17 words | D5000–D7447 | 37768–40215 |
| Record: sequence, status, interval 600, year…second, 7 temperatures | +0…+16 | |
| Temperatures recorded | D100–D106 | 4196–4202 |

- Sensors are matched automatically by their temperature register (D100–D106). Each sensor's live multiplier is reused for its history channel.
- Sensors on other registers, or not in signed 16-bit format, are listed as "not mapped" with the reason.
- A new layout uses the fixed UTC offset +120 (DVP clocks do not change for summer time). An existing layout keeps its offset or time zone.
- **Year format:** the standard program sets M1016, which makes the PLC clock report a 4-digit year. Check D4930 in ISPSoft's monitor table: 2026 → 4-digit; 26 → 2-digit.
- Nothing is saved until you review the layout, use **Test PLC History Header** and select **Save history layout**. Controllers that already have imported history still refuse layout changes, as before.

## Upgrade

1. Download a database backup from **Backup and restore**.
2. Run `Watchdog-TM-Server-4.3.7-Setup.exe` as administrator.
3. Confirm the footer shows **V4.3.7 Web**.
