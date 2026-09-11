# Watchdog TM V4.2.1 — Temperature-only monitoring

This update reads only each sensor's configured temperature value. It does not read PLC alarm bits, fault registers or limit registers, and cannot write setpoints to the PLC. Existing temperature settings and history are preserved. Legacy alarm mappings are ignored.

## Start live readings
1. Open Controllers and Sensors. Enable your RTU or TCP controller and verify its connection settings.
2. Edit the sensor. Enter the zero-based temperature register, FC03 or FC04, value format and multiplier. Advanced settings contain byte/word order and correction.
3. Open Settings and Backup and select Start live monitoring. A valid existing TM license is required. Accept the read-only monitoring confirmation.
4. Check Dashboard for readings and Controllers and Sensors for the last successful communication and any errors.

Read every (seconds) in controller setup determines the live polling interval. Record every (seconds) in sensor setup determines the historical sampling interval. Trends and Export shows recorded samples.

A 32-bit value reads two consecutive 16-bit registers starting at the configured address. The value format and scaling must match the controller data. You can test communication with an existing kWh register without changing the DDC program, but the temperature screen still labels the result in degrees C; this does not convert kWh into temperature.

## Alarms
Software-generated temperature alarms will be added in a later update. This build does not generate live high/low alarms or send automatic alarm emails. The Alarms and History page retains previously recorded events. SMTP settings remain available for future use.

## Other functions
Test connection performs one real read. Switch to simulation stops live hardware polling and uses demonstration values. Back up database saves configuration and history; Restore backup replaces them from a selected backup. Exit stops monitoring. The computer and app must remain running for live polling and recording.
