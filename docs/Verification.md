# Verification — Watchdog TM V4.2.1

Date: 10 September 2026. Platform: Windows x64, .NET SDK 10.0.301. This records software verification; no real PLC commissioning or medical regulatory compliance is claimed.

## Automated regression results

**60 checks passed.** See `test-results.txt` for individual assertions and the executable `tests/` project for reproducibility.

Covered: signed negative and float byte/word decoding; 18-sensor demo; persistent configuration and renamed sensor history; single alarm episode per transition; acknowledgement without clearing; reminder/recovery queue behaviour; simulated-send suppression; typed Excel values and blank invalid cells; locked/cancelled export handling; consistent database backup/restore; salted password hashes and Windows credential protection; valid/wrong-product/wrong-installation/tampered/malformed/expired license checks; unchanged EM format compatibility; no customer signing API; capacity with disabled/retired/reactivated sensors; capacity upgrade preservation; expansion restrictions after expiry; injected SMTP authentication failures, retry exhaustion, delayed initial messages after recovery; reminder generation during outages without falsifying last observation time; serial request gate non-overlap and configuration conflict checks; independent controller polling; alarms ahead of the 600-second history interval; missing samples during outages; restart preserving last known active alarm without a healthy state; simulated successful/failed/unverified writes.

A local TCP test server exercised actual NModbus **FC03, FC04, FC01 and FC06** request/response traffic, including high-limit write/readback. It did not emulate Delta firmware or validate actual register addresses. RTU serialization was tested at the shared request gate; no physical serial port or analyser was used.

Email queue success/failure testing used an injected sender, including MailKit authentication exceptions. No external message was sent. Actual SMTP account authentication, TLS negotiation, recipient delivery and site firewall behaviour remain commissioning checks.

## UI and packaging

The five actual WPF screens were instantiated and rendered. The dashboard was visually inspected with all 18 demo cards visible at the tested window size and large temperature values. Screenshots are in `screenshots/`. Rendering is not an exhaustive manual click-through of every dialog.

The customer application and separate supplier GUI/CLI build as self-contained Windows x64 applications. The customer references a verification-only assembly. The copied legacy EM license source has the same SHA-256 as the original. No private key file, supplier executable or signing library is included in the customer app directory. The installer is an unsigned Inno Setup executable; the runtime is bundled for offline operation.

## Practical boundaries

- Real probe identity ("PTC100" versus Pt100), Delta module compatibility, all PLC offsets, PLC logic, writable limits and hardware calibration remain unverified.
- Limit registers currently use signed 16-bit holding registers and FC06. Temperature values support the configured 16/32-bit formats. The optional low alarm has a separate mapping; the customer write workflow edits the high limit.
- The local role arrangement is Operator and one Administrator account. Audit entries use those role names.
- Large Excel exports load selected records in memory and can require substantial RAM; drawing downsampling never alters the raw database.
- The history screen shows the latest 2,000 alarm/audit events and latest 1,000 email entries; older rows remain in SQLite. Temperature range queries/export access all selected stored samples.
- SMTP acceptance is not recipient-delivery proof. A process/network failure after acceptance but before saving the result can produce a duplicate on retry. Restoring an older database can also replay its older queue state.
- The application cannot observe events while stopped. Recordings and exact alarm transitions during downtime are unavailable. Use the operational procedures in the commissioning checklist.

Use `Commissioning-checklist.md` for real-equipment acceptance and a supervised soak test before operational deployment.

## Installer smoke test

The final functional build installed successfully (exit 0) into an isolated work folder. The installed executable hash matched the published executable. The installed app launched with the exact title **Watchdog TM V4.2.1**, remained responsive, survived an intentional process termination/restart using its test database, and prevented a second instance from creating a second database. The generated Start-menu shortcut pointed to the installed executable. These checks used an isolated demonstration database, not customer data. The build is supplied as an installer; the isolated test installation is not the customer's commissioned installation.
