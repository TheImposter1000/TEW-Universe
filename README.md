# TEW Universe Studio

Independent Windows desktop editor for **existing** Total Extreme Wrestling IX scenario records.

## Desktop application

The published Windows app now uses **WPF and .NET 8**. Its source is in `src/TEWUniverse.Desktop`, sharing the existing Access data layer and settings from `src/TEWUniverse.Studio`.

The worker portrait feature looks for a **worker-name PNG first**, then WebP/JPEG/GIF/BMP files in that order, in the configured local pictures folder or its `People` subfolder. If no usable worker-name image exists, it falls back to the stem of the worker's `tblWorker.Picture` filename, with PNG still preferred. Missing images show the silhouette; loaded images hide the silhouette and its background, preserving PNG transparency. Portrait loading never edits the MDB or image files. WebP requires a compatible Windows image codec.

The redesigned Workers screen includes a sidebar, searchable worker list, real MDB fields, biography editor and status bar. Companies, Titles (championships), Events, and Stables now have existing-record editors in the same dark workspace. These sections browse existing MDB entries and allow updates to a restricted set of fields. Relationships, History and Tools remain nonfunctional placeholders until relationship and title-lineage operations are validated.

In Titles, open **Title Lineage** to view existing pre-game reigns from `tblBeltPreHistory`, filtered by the selected championship and sorted by Order Number where the MDB schema identifies it. A Generate Title Reigns panel lets you select a pool of existing workers, reign count, years and singles/tag-team mode, then generate a dated preview and export a draft CSV. You can replace holders and dates ONLY when the selected championship already has exactly the same number of prehistory rows; this updates only that belt's existing history UIDs in one transaction. Nothing is inserted or deleted. Always back up before replacing lineage; the exported draft CSV is not a verified TEW IX import file. If the belt-reference column cannot be recognized unambiguously, the app shows diagnostic columns rather than mixing reigns across belts.

**No entity insert or delete operations are performed.** Existing IDs and linked references are protected. **Save Changes** writes to the current working MDB. **Create Backup** makes an independent snapshot. **Save Copy As** creates a new working MDB at a location you choose.

## Distribution

Get the Windows installer from [GitHub Releases](https://github.com/TheImposter1000/TEW-Universe/releases). GitHub Actions builds WPF, the installer and update agent. Microsoft ACE OLE DB for 64-bit Windows is required for Access MDB connections.

Original scenario databases are never edited by Import. Credentials are not in source code; optional password remembering uses local Windows-user DPAPI encryption.

See [development notes](docs/DEVELOPMENT.md) for build instructions and validation guidance.

Not affiliated with or endorsed by the makers of Total Extreme Wrestling.
