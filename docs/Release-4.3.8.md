# Watchdog TM V4.3.8

V4.3.8 adds a PLC history layout that survives PLC power cuts on the Delta DVP12SE. Database, configuration, password, license and history are kept. No values are written to the PLC.

Earlier releases remain available unchanged on GitHub (V4.3.7, V4.3.6, V4.3.5, V4.3.4 and older). See [Rollback](Rollback.md).

## Why

The DVP12SE retains only D408–D599 and D2000–D3919 through a power cut or STOP→RUN, and this range cannot be changed. The 144-record buffer at D5000 is therefore cleared whenever the PLC restarts. 144 × 17 words (2,448) do not fit in the retained area, so the retained buffer holds **111 records (about 18.5 hours)** at **D2000–D3886**.

## New

- PLC history accepts **111-record** layouts as well as 144 and 300.
- **Load standard MicroBrain layout** asks for the PLC program version:
  - **Retained buffer D2000 · 111 records**: survives power cuts (requires the PLC change below).
  - **Buffer D5000 · 144 records**: the V4.3.7 layout, cleared by power cuts.
- A controller that already has imported history can move its buffer (capacity, buffer address, segments) without losing that history. Record fields, channels and clock settings still cannot change, so stored samples keep their identity.

## PLC program change (History_Record)

| Network | Before | After |
|---|---|---|
| 38 | `D5000@E0` … `D5009@E0` | `D2000@E0` … `D2009@E0` |
| 39 | `D5010@E0` … `D5016@E0` | `D2010@E0` … `D2016@E0` |
| 40 | `D4903 < 144` | `D4903 < 111` |
| 42 | `D4904 >= 144` | `D4904 >= 111` |
| 3 (new rung at the end) | — | if `D4903 > 111` or `D4904 >= 111`: set D4903, D4904, D3903, D3904 to 0 |

The header stays at D4900–D4904 and is restored at power-up from D3900–D3904. The buffer ends at D3886, before D3900.

**Once, right after downloading the changed program:** in the monitor table set D3903, D3904, D4903 and D4904 to 0, because they still point into the old D5000 buffer. Do **not** change D3900 (the sequence must never go back).

**Test:** after a few records, note the newest record's sequence at D2000 + (D3904 − 1) × 17 (for example D3904 = 3 → D2034). Do PLC → Stop, PLC → Run, and switch the PLC off and on. The value must still be there.

## Watchdog setup

1. Install `Watchdog-TM-Server-4.3.8-Setup.exe` as administrator (download a database backup first).
2. Open **PLC history and combined trends**, expand the controller's **Verified history layout**.
3. Choose **Retained buffer D2000 · 111 records** and **4-digit year**, select **Load standard MicroBrain layout**.
4. Use **Test PLC History Header**, then **Save history layout**.

Records written to D5000 before the change and not yet downloaded are not read again. The first downloads after the change report skipped slots until the new buffer has filled.
