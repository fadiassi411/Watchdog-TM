# Supplier licensing — Watchdog EM and Watchdog TM

The supplier applications are separate from the customer installer. This source delivery adapts copies of the existing Watchdog EM tools. The original EM project and private-key folders were not modified. No existing private key was read, copied or packaged.

The customer app contains the established public verification key and the TM signature verifier only. Signing methods are compiled exclusively into the supplier licensing library. Private keys must remain under supplier control.

## Generate an 18-sensor TM license

1. Open the separately delivered **Watchdog License Manager**.
2. Select **Watchdog TM** in Product. Capacity defaults to **18**, in single-sensor increments.
3. Enter the customer and the Installation ID copied from the customer's Settings and Backup screen.
4. Select edition and optional expiry, then select the existing supplier private PEM through the file picker. It must match the public verification key already used by Watchdog EM.
5. Choose the license output folder and generate. Send only the resulting `.wdlicense` file to the customer.
6. The customer imports it with Import / Upgrade license. Retain the supplier issuance record.

CLI alternative:

```powershell
& '.\WatchdogLicenseGenerator.exe' issue --product TM --sensors 18 --private 'path-to-existing-supplier-key.pem' --customer 'Customer name' --installation 'WD-XXXXX-XXXXX-XXXXX-XXXXX-XXXXX-XXXXX' --out 'customer-tm.wdlicense'
```

Optional `--expires yyyy-MM-dd`, `--edition Commercial` and `--license-id GUID` are supported. Issue time is UTC at generation. Omit expiry for a perpetual license.

Select Watchdog EM (or omit `--product TM` in the CLI) for the original meter-block licensing. Existing EM payload format 1 and ECDSA SHA-256/DER verification remain unchanged. TM uses format 2 with signed `product=WatchdogTM` and `maximumTemperatureSensors`. The unchanged EM verifier rejects TM format 2; TM rejects EM format 1.

Normal restart/upgrade retains the existing machine-derived Watchdog Installation ID approach. Machine replacement requires a new license. Customer monitoring already commissioned is not abruptly stopped by expiry; a valid renewal is required for expansion.

Use `verify --public matching-public.pem --license customer-tm.wdlicense` to check the signature. Supplier signature verification is distinct from the customer's installation/date/capacity checks. Never put the private key in the customer app directory or customer installer.
