# Watchdog TM V4.3.1

V4.3.1 improves unattended temperature-history continuity on Windows without changing any PLC register or configuration.

## Sleep protection and recovered trends

- **Computer sleep protection:** Settings > Monitoring and license now offers an opt-in setting to request that Windows keep the server awake during active live monitoring. The request is cleared when Watchdog stops or enters demonstration mode, and the page reports its current state.
- This protects against ordinary Windows idle sleep only. It cannot prevent a shutdown, power loss, forced sleep, depleted battery, or a laptop-lid policy.
- **Recovered PLC history in sensor trends:** per-sensor trend charts now include validated PLC-history samples imported after a Watchdog interruption, such as a PC sleep period. The combined-history view and exports retain their existing behavior.

PLC-side timestamped history is still required to recover measurements collected while the computer is asleep. This release does not write to a PLC, create a PLC history layout, or certify a controller memory map.

## Upgrade

Back up the Watchdog database, then run `Watchdog-TM-Server-4.3.1-Setup.exe` as administrator on Windows x64. The installer retains the existing database, administrator password, signed license and history. The ZIP is `Watchdog-TM-4.3.1.zip`.

After upgrading, open **Settings > Monitoring and license**, enable **Keep this computer awake while live monitoring**, then save. Confirm the displayed status says that Watchdog is keeping the computer awake. Keep PLC history enabled only after its layout has been verified against the PLC program.

## Validation and release requirements

The Windows release workflow runs the core and web regression suites, builds the self-contained Windows x64 server and Inno Setup installer, verifies the packaged server, and publishes installer, ZIP, and SHA-256 checksums only after those checks pass. Physical PLC commissioning, a real Windows sleep/resume cycle, and SMTP delivery remain installation-specific acceptance checks.
