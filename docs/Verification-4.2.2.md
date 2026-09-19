# V4.2.2 validation

## September 20: 144 snapshots
- 51 new synthetic history checks passed for 15/3/5/1 sensors across four independent controllers: defaults and legacy 300 persistence, empty/partial/full rings, 24-hour recovery, wraparound, deduplication across database reopen, exact overwrite gaps, signed values/timestamps, segmented buffers, bad count/position/torn reads, and independent worker outage/recovery.
- Historical imports and recovery leave live readings and alarm/email observation untouched.
- The previous 300-snapshot tests remain active with explicit capacity 300. Deletion, SMTP and server/trend regression checks also passed: 133 checks total.
- Compiler: zero warnings/errors. JavaScript syntax, deletion UI and session-resume checks passed.
- Packaged-server HTTP checks passed: 144 default and mapping save, segmented total rejection, legacy 300 compatibility, switching an unimported layout back to 144, authentication/antiforgery, deletion, and updated browser assets.
- Physical PLC memory, program changes and live commissioning remain unverified. No laptop service or PLC configuration is modified by this release process.

## September 19 deletion update
- Release build completed with zero compiler warnings/errors; Windows x64 self-contained package retains runtime 10.0.9, matching the previous package.
- 82 web regression checks passed, including 12 deletion checks: stale revision, missing ID, scoped sensor removal, channel cleanup, last-channel disable, backup contents, retained history labels/ownership, controller cascade and restart persistence.
- Packaged-server HTTP test passed authentication, antiforgery rejection, stale-revision rejection, sensor deletion, controller cascade and missing-ID behavior, using an isolated database and mutex.
- JavaScript deletion tests passed button rendering, escaped names, cancellation and correct API targets/revisions. Six session-resume checks passed.
- Installer compiled successfully. No production database, laptop service installation, PLC or SMTP destination was changed by testing.

## Original history-release verification
- 89 core regression checks passed, including Modbus TCP loopback live reads.
- 11 server/trend checks and 30 SMTP checks passed.
- 29 PLC history checks passed: independent 2/3/13-channel layouts, 13/14/24-word records, 300-slot buffers, 48-hour recovery, wraparound, duplicate downloads and database reopen, exact overwrite gaps, inconsistent snapshots, sequence reset rejection, independent worker outage/recovery, cross-controller mapping refusal, segmented buffers, and no live/alarm/email observer changes.
- 8 isolated HTTP checks passed: authentication, login, controller layouts, combined history, all-time CSV and Excel, version/navigation and unconfigured download refusal.
- Browser login and PLC history page controls inspected on isolated localhost server.
- JavaScript syntax checks and Release build passed.

Test addresses and data are synthetic. No ISPSoft projects or physical PLC logging mappings were delivered. No live PLC history connection, two-day physical soak, or hardware memory-retention test is claimed. Existing laptop service/configuration was not changed.
