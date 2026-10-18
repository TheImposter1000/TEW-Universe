# TEW Universe Studio

Independent Windows desktop editor for **existing** Total Extreme Wrestling IX scenario records.

## Desktop application

The published Windows app now uses **WPF and .NET 8**. Its source is in `src/TEWUniverse.Desktop`, sharing the existing Access data layer and settings from `src/TEWUniverse.Studio`.

The worker portrait feature reads each worker's `tblWorker.Picture` filename from the MDB and displays it from a configurable local People picture folder. Missing portraits use the silhouette placeholder; image files are never written into the MDB.

The redesigned Workers screen includes a sidebar, searchable worker list, real MDB fields, biography editor and status bar. Other module navigation links are placeholders, not working editors yet.

**Save Changes** writes to the current working MDB. **Create Backup** makes an independent snapshot. **Save Copy As** creates a new working MDB at a location you choose.

## Distribution

Get the Windows installer from [GitHub Releases](https://github.com/TheImposter1000/TEW-Universe/releases). GitHub Actions builds WPF, the installer and update agent. Microsoft ACE OLE DB for 64-bit Windows is required for Access MDB connections.

Original scenario databases are never edited by Import. Credentials are not in source code; optional password remembering uses local Windows-user DPAPI encryption.

See [development notes](docs/DEVELOPMENT.md) for build instructions and validation guidance.

Not affiliated with or endorsed by the makers of Total Extreme Wrestling.
