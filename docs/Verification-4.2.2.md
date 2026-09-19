# V4.2.2 validation

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
