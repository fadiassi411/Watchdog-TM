# V4.2.2 validation
- 89 core regression checks passed, including Modbus TCP loopback live reads.
- 11 server/trend checks and 30 SMTP checks passed.
- 29 PLC history checks passed: independent 2/3/13-channel layouts, 13/14/24-word records, 300-slot buffers, 48-hour recovery, wraparound, duplicate downloads and database reopen, exact overwrite gaps, inconsistent snapshots, sequence reset rejection, independent worker outage/recovery, cross-controller mapping refusal, segmented buffers, and no live/alarm/email observer changes.
- 8 isolated HTTP checks passed: authentication, login, controller layouts, combined history, all-time CSV and Excel, version/navigation and unconfigured download refusal.
- Browser login and PLC history page controls inspected on isolated localhost server.
- JavaScript syntax checks and Release build passed.

Test addresses and data are synthetic. No ISPSoft projects or physical PLC logging mappings were delivered. No live PLC history connection, two-day physical soak, or hardware memory-retention test is claimed. Existing laptop service/configuration was not changed.
