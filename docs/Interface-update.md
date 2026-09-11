# Watchdog TM — EM-style interface update

The desktop interface now follows the existing Watchdog EM design: its navy/teal palette, logo, white rounded setup panels, numbered sections and simple action buttons.

- Dashboard demonstration controls are hidden until requested.
- Controller setup shows only RTU or TCP connection fields, according to the selected protocol. Polling resilience is in Advanced settings.
- Sensor setup begins with refrigerator details, the temperature register and historical recording. PLC alarm, fault, low-alarm and email sections are collapsed initially.
- The address field is labelled Temperature register address, with the exact zero-based protocol address shown underneath. No address conversion or live map is assumed.
- The Retired checkbox has been removed from the sensor editor. Retirement/reactivation remains an explicit controller-screen action. Existing retired state is preserved and explained.
- Test connection now offers an explicit read-only temperature test for a real RTU/TCP controller, even while the application remains in demonstration mode. It requires only an enabled, non-retired sensor with a verified temperature address. It does not write any PLC register or commission live monitoring.
- Existing configuration, sensor IDs, history, licensing and passwords remain in the same database; the update installer does not replace the database.

Verification: 61 regression checks passed, including an actual TCP loopback read using a temperature-only mapping. The five pages and both setup forms were rendered and visually checked. No real PLC reads or writes were performed during development.

## Window sizing correction — 11 September 2026

Startup bounds now fit the active monitor's available work area, accounting for display scaling and the taskbar. Setup dialogs use the same bounds handling. The main window can be resized smaller, retains the normal Windows title bar, and provides Fit window and Exit buttons. The close confirmation is owned by the main window. Missing/malformed licenses now show customer instructions instead of JSON parsing details. Regression suite: 68 checks passed, including scaled/small desktop bounds and license-message cases.
