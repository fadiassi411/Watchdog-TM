# Third-party components

The self-contained application bundles the Microsoft .NET Windows runtime and these NuGet dependencies (plus their transitive components). Source, notices and licensing information are available at the authoritative package pages:

- Microsoft.Data.Sqlite 10.0.3 — https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.3
- SQLitePCLRaw.bundle_e_sqlite3 2.1.13 — https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.13
- NModbus 3.0.83 — https://www.nuget.org/packages/NModbus/3.0.83
- System.IO.Ports 10.0.3 — https://www.nuget.org/packages/System.IO.Ports/10.0.3
- MailKit and MimeKit 4.17.0 — https://www.nuget.org/packages/MailKit/4.17.0
- ClosedXML 0.105.0 — https://www.nuget.org/packages/ClosedXML/0.105.0
- OxyPlot.Wpf 2.2.0 — https://www.nuget.org/packages/OxyPlot.Wpf/2.2.0

The older SQLitePCLRaw 2.1.11 dependency was replaced with 2.1.13 after NuGet reported advisory GHSA-2m69-gcr7-jv3q. The final restore/build reported no package vulnerability warnings. Dependency availability and advisories can change; repeat package auditing before future releases.

The .NET runtime's LICENSE.txt and ThirdPartyNotices.txt are included in the published app directory. NuGet project assets in a local build list all transitive packages; the project files pin direct package versions.
