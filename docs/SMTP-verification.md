# SMTP verification

Verified with the source and Windows x64 build in this checkout:
- 89 core regression checks.
- 11 server/trend regression checks.
- 30 SMTP/notification checks, including password encryption, header injection, validation, high/low transitions, restart deduplication, reminders/recovery, disabled/simulation behavior, bounded retries, local SMTP submission, Bcc and required STARTTLS refusal.
- 13 authenticated HTTP checks for SMTP access, CSRF, stale revisions, successful and failed tests, credential redaction/clearing, changed-host credential protection and recipient rules.
- Browser layout inspected and Save and send test email successfully exercised against a local SMTP fixture.
- PDF SMTP page rendered and inspected. No customer/provider mail was sent; real provider TLS/authentication and inbox delivery require the customer's configuration and explicit test.

The automatic email switches default to off. Existing application configuration is retained on upgrade. After the user installed the update, the running Windows service and its served HTML were verified to include the Email and SMTP menu and script.

