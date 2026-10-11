# Web temperature alarm management

Open either alarm-limit button on a dashboard sensor card to configure:

- **High / low limit (°C):** leave blank to disable that limit. New alarms require a reading strictly beyond the limit.
- **Alarm delay (seconds):** 0–86400, shared by high and low activation. An excursion must remain beyond the limit across valid samples for this period before activating an alarm or sending its initial email. Returning within the limit cancels the pending alarm. Unavailable samples, a stale-sized sample gap, or restarting the server interrupt a pending delay. Activation occurs on a new sample, so polling frequency affects timing.
- **Hysteresis (°C):** a non-negative reset margin. A high alarm clears at or below high minus hysteresis; a low alarm clears at or above low plus hysteresis. For example, high 8°C and hysteresis 1°C means activation above 8°C and recovery at 7°C or below. With both limits enabled, hysteresis must be smaller than their difference. Recovery has no additional delay.

Zero delay and zero hysteresis preserve the previous immediate alarm behavior. Existing configurations receive these defaults automatically. Changes are local software settings and never write to a PLC. These controls apply to the web server; the legacy WPF client retains its existing alarm behavior.

Use **Acknowledge alarm** on an active card to stop blinking and email reminders. The card retains its high/low color and shows the acknowledgement time until the condition clears. Initial alert messages and recovery emails remain enabled according to SMTP settings; acknowledgement does not retract email already submitted. Pending reminders are cancelled, and future reminders are suppressed for that episode. A new episode requires a new acknowledgement. All authenticated administrators share acknowledgements, which are audited and survive server restart in SQLite.

Unavailable readings never count as recovery or show a current temperature alarm. An existing active episode is retained through an outage; fresh readings determine its eventual recovery. Changes to alarm settings, sensor mapping/enabled status, or operating mode reset the affected episode. Pending state is sample-driven and independent of browser refreshes or whether SMTP is enabled.

## Validation

Run the portable alarm regression checks with .NET SDK 10:

```sh
dotnet build tests/Watchdog.TM.Web.Tests -c Release -m:2 -nr:false
dotnet tests/Watchdog.TM.Web.Tests/bin/Release/net10.0-windows/Watchdog.TM.Web.Tests.dll --alarms-only
```

The full web test executable also runs these checks before its existing suites. Full SMTP tests use Windows DPAPI and require Windows.

For the browser/API regression, publish the web project, start it with a fresh isolated data directory on port 5184 and a unique `Monitoring:MutexName`, then run `node tests/alarm-ui.cjs` with Playwright and Chromium installed. The test creates its own demonstration controller and sensor, verifies settings and acknowledgement, and must not target a customer database.
