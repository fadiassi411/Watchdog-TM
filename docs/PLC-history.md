# V4.2.2 — Independent PLC-backed temperature history

New layouts default to 144 snapshots at 600-second PLC sampling: approximately 24 hours of recovery. Each PLC has a separate verified layout and stable controller/sensor identities. Existing explicitly configured 300-snapshot layouts remain supported and are not automatically changed. No field station or buffer addresses are assumed.

Downloads run at startup, on live communication recovery, every ten minutes by default, and through Download PLC History. Each PLC has an independent worker, progress and retry schedule. Shared serial buses necessarily serialize individual Modbus requests. Historical imports never update live values or call alarm/email observers.

The database deduplicates by controller plus sequence, retains imported values permanently, records overwritten sequence ranges and imports only coherent complete downloads. Per-sensor trends include recovered data. PLC history and combined trends adds timestamp-ordered previews, PLC/sensor/date filters, and unrestricted-date CSV/Excel exports. Preview is bounded to 2,000 rows; exports include all matching stored records. Excel splits sheets at its row limit.

## Required commissioning
The three ISPSoft projects have NOT been delivered. History remains disabled with unset addresses. No PLC was programmed or field-tested. Configure only from verified projects and confirm the contract in PLC-history.md. This release reads history; it does not write sampling settings to the PLC. Each PLC program must independently record at 600 seconds.

Supported contract: zero-based FC03 header and contiguous circular buffer; position indicates next write slot; persistent increasing 32-bit snapshot sequence; signed 16-bit temperatures with configured multipliers; six binary RTC fields and explicit fixed UTC offset; status healthy value configurable. PLC header status must remain non-healthy throughout record/header changes. Sequence resets/rollover or conflicting duplicate content are refused pending reviewed migration. Prefer UTC PLC timestamps; automatic daylight-saving inference is not supported.

144 records at ten minutes offer approximately 24 hours of sampling capacity; oldest-to-newest timestamps span 23 hours 50 minutes. Existing 300-record layouts offer about 50 hours. Older overwritten PLC data cannot be recovered. Downloaded Watchdog data is retained independently of this limit. The bundled V4.2.1 PDF covers the previous features; this document is the updated V4.2.2 supplement.

## Segmented buffers
For discontinuous verified Modbus areas, specify Segments with FirstRecord, RecordCount and Address. Segments must cover exactly the configured Capacity (144 or legacy 300) without overlap; each record stays within one segment. Otherwise use BufferAddress for one contiguous area. No D-register-to-Modbus conversion is performed automatically.

## Set up the one-day buffer

Update and verify the PLC program to store 144 records, cap its count at 144 and wrap its next-write position from 143 to zero. Keep its interval at 600 seconds. Then open PLC history and combined trends > Verified history layout, set capacity to 144 and syncSeconds to 600, and enter the verified header, buffer and channel mappings. Segments must total 144. No PLC addresses are supplied by this update.

The four-floor synthetic tests cover 15, 3, 5 and 1 sensors with independent 26-, 14-, 16- and 12-word records. At 144 records these example buffers occupy 3,744, 2,016, 2,304 and 1,728 words, excluding metadata and working registers.

Existing 300-record PLC programs must keep capacity 300 until PLC and software mappings are migrated together. Layouts with imported history remain protected against structural changes; those require a reviewed migration. Do not delete/recreate a controller merely to bypass this check.

If buffer data is discarded at PLC power loss, reset count and position coherently while preserving non-reused sequence identities. This is a PLC-program responsibility; the software does not certify its memory allocation or retention behavior. Live readings and alarms remain independent of the history buffer.
