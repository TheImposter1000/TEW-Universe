# TEW Universe Studio

Independent Windows desktop editor for **existing** Total Extreme Wrestling IX scenario records.

## Desktop application

The published Windows app now uses **WPF and .NET 8**. Its source is in `src/TEWUniverse.Desktop`, sharing the existing Access data layer and settings from `src/TEWUniverse.Studio`.

The worker portrait feature looks for a **worker-name PNG first**, then WebP/JPEG/GIF/BMP files in that order, in the configured local pictures folder or its `People` subfolder. If no usable worker-name image exists, it falls back to the stem of the worker's `tblWorker.Picture` filename, with PNG still preferred. Missing images show the silhouette; loaded images hide the silhouette and its background, preserving PNG transparency. Portrait loading never edits the MDB or image files. WebP requires a compatible Windows image codec.

Worker fields that store numeric TEW IX enumeration values, including Gender, Nationality, Based In and Wrestling Style, are displayed consistently as their original codes. The profile editor also accepts their raw stored codes rather than mixing in translated dropdowns. Boolean flags such as Active remain Yes/No controls.

The redesigned Workers screen includes a sidebar, searchable worker list, real MDB fields, biography editor and status bar. Companies, Titles (championships), Events, and Stables now have existing-record editors in the same dark workspace. These sections browse existing MDB entries and allow updates to a restricted set of fields. Relationships, History and Tools remain nonfunctional placeholders until relationship and title-lineage operations are validated.

In Titles, open **Title Lineage** to view existing pre-game reigns from `tblBeltPreHistory`, filtered by the selected championship and sorted by Order Number where the MDB schema identifies it. A Generate Title Reigns panel lets you select a pool of existing workers, reign count, years and singles/tag-team mode, then generate a dated preview and export a draft CSV. You can replace holders and dates ONLY when the selected championship already has exactly the same number of prehistory rows; this updates only that belt's existing history UIDs in one transaction. Nothing is inserted or deleted. Always back up before replacing lineage; the exported draft CSV is not a verified TEW IX import file. If the belt-reference column cannot be recognized unambiguously, the app shows diagnostic columns rather than mixing reigns across belts.

Titles now also includes an **Advanced Generator** tab: select existing workers by UID (optionally using historical ring names) or add historical name-only champions with `Holder1=-1`. Specify their eligible years in the company and the exact number of required reigns. The planner reserves these reigns within their eligibility windows, then can fill the remaining slots using eligible background holders loaded from the selected title's existing MDB history. A preview and CSV export work for any proposed number of reigns; applying the draft is restricted to the existing rows of that championship, without inserting records. This advanced generator currently handles singles histories and requires a backup before replacing records.

**Direct database editing:** Select an existing scenario MDB as the **Active Database**. Save Changes writes to that same file; Refresh Database reloads records added or changed externally. There is no required working copy. **Create Backup** is a separate manual snapshot. **Export Copy As** creates an independent MDB without changing the active selection. Legacy Default Database and Current Copy paths are not automatically activated after upgrading: explicitly choose the MDB you intend to edit. If the file changes externally after opening, saving is blocked until you refresh. Close TEW IX\'s database editor while using this application; do not edit the same MDB simultaneously. **No entity insert or delete operations are performed.** Existing IDs and linked references are protected.

## Distribution

Get the Windows installer from [GitHub Releases](https://github.com/TheImposter1000/TEW-Universe/releases). GitHub Actions builds WPF, the installer and update agent. Microsoft ACE OLE DB for 64-bit Windows is required for Access MDB connections.

The explicitly selected scenario database is edited directly. Credentials are not in source code; optional password remembering uses local Windows-user DPAPI encryption.

See [development notes](docs/DEVELOPMENT.md) for build instructions and validation guidance.

Not affiliated with or endorsed by the makers of Total Extreme Wrestling.
