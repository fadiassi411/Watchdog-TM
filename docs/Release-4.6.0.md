# Watchdog TM V4.6.0

V4.6.0 lets you arrange the temperature blocks on the dashboard. Database, configuration, users, levels, passwords, license and history are kept. No values are written to the PLC.

Earlier releases remain available unchanged on GitHub (V4.5.1, V4.5.0, V4.4.1 and older). See [Rollback](Rollback.md).

## Arrange the dashboard

1. On the **Dashboard**, select **Arrange blocks**.
2. Drag a block to its new place with the mouse, or use the **◀ ▶** arrows on each block (touch screens and keyboard).
3. Select **Save layout**. **Cancel** discards the changes; **Reset to default order** returns to the order in which the sensors were added.

- The layout is saved on the server, so every screen and every user sees the same order.
- Live updates pause while you arrange and resume when you save or cancel.
- New sensors appear at the end; deleted sensors simply disappear from the layout.
- The order is also used in sensor lists on the other pages.

## Who can arrange

The Administrator always can. For other users, tick **Arrange the dashboard** in their user level (**Settings → Users and levels**). A user who sees only some sensors rearranges only those; the other blocks keep their places.

## Upgrade

1. Download a database backup from **Backup and restore**.
2. Run `Watchdog-TM-Server-4.6.0-Setup.exe` as administrator, then press Ctrl+F5 in the browser.
