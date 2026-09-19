# V4.2.2 — 144-snapshot PLC history and controller/register deletion

## September 20 update: one-day PLC buffers

New PLC-history layouts now default to **144 snapshots at 10-minute intervals**, approximately **24 hours** of recovery. The per-PLC history page displays each controller's actual configured capacity and recovery hours. Segment validation uses that capacity, rather than requiring 300 records.

Existing 300-snapshot configurations remain supported and are not silently resized. The PLC program and Watchdog mapping must agree: configure capacity 144 only after the PLC's count, wraparound and memory layout have been updated and verified. Layouts with imported history retain their structural-change protection and require a reviewed migration. This update does not program any PLC or change the installed laptop service.

Saved Watchdog data remains available beyond the one-day PLC window. Live readings, software alarms, deletion buttons, backups and exports remain available. New synthetic coverage uses four independent floors with 15, 3, 5 and 1 sensors, covering one-day recovery, ring wraparound, duplicate imports, exact gaps, invalid headers, segmented buffers and isolated outages with no replayed alarms.

The EXE and ZIP on this same V4.2.2 release have been rebuilt. See PLC-history.md for setup and the commissioning boundary.

## September 19 update: Delete controllers and registers

Controllers and sensors now includes **Delete controller** and **Delete register** buttons with explicit confirmation. Controller deletion removes its attached sensor entries; individual register deletion leaves other sensors configured. Changes apply immediately without a separate Start Live Monitoring step.

Every deletion creates a database backup, checks the browser configuration revision, removes obsolete commissioned IDs/history-channel mappings, and cancels pending sensor notifications. Saved readings remain available in **PLC history and combined trends** and CSV/Excel exports with their original sensor/controller names. Select All PLCs / All sensors to include deleted entries. No PLC writes or PLC-buffer erasure occur.

Authenticated, antiforgery-protected endpoints and stale-revision checks are covered by isolated packaged-server tests. The web regression suite passes 82 checks, including 12 new deletion checks; JavaScript tests verify confirmation cancellation, correct delete targets and session resume. See Delete-entries.md for backup/restore behavior.

The rebuilt installer and ZIP replace the assets on the existing V4.2.2 release. The laptop installation is not automatically updated.

## Existing PLC history features

Adds separate history synchronization per PLC, with configurable verified layouts, stable controller/sensor identities, a 300-snapshot buffer and 600-second PLC sampling. Synthetic test coverage uses 2, 3 and 13 sensors with independent 13-, 14- and 24-word records. No field station or buffer addresses are assumed.

Downloads run at startup, on live communication recovery, every ten minutes by default, and through Download PLC History. Each PLC has an independent worker, progress and retry schedule. Shared serial buses necessarily serialize individual Modbus requests. Historical imports never update live values or call alarm/email observers.

The database deduplicates by controller plus sequence, retains imported values permanently, records overwritten sequence ranges and imports only coherent complete downloads. Per-sensor trends include recovered data. PLC history and combined trends adds timestamp-ordered previews, PLC/sensor/date filters, and unrestricted-date CSV/Excel exports. Preview is bounded to 2,000 rows; exports include all matching stored records. Excel splits sheets at its row limit.

## Required commissioning
The three ISPSoft projects have NOT been delivered. History remains disabled with unset addresses. No PLC was programmed or field-tested. Configure only from verified projects and confirm the contract in PLC-history.md. This release reads history; it does not write sampling settings to the PLC. Each PLC program must independently record at 600 seconds.

Supported contract: zero-based FC03 header and contiguous circular buffer; position indicates next write slot; persistent increasing 32-bit snapshot sequence; signed 16-bit temperatures with configured multipliers; six binary RTC fields and explicit fixed UTC offset; status healthy value configurable. PLC header status must remain non-healthy throughout record/header changes. Sequence resets/rollover or conflicting duplicate content are refused pending reviewed migration. Prefer UTC PLC timestamps; automatic daylight-saving inference is not supported.

300 records at ten minutes offer about 50 hours, not unlimited recovery. Older overwritten PLC data cannot be recovered. Existing Watchdog data and V4.2.1 release assets are preserved. The bundled V4.2.1 PDF covers the previous features; PLC-history.md is the V4.2.2 supplement.
