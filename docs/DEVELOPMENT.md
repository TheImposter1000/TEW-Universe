# Development and compatibility

## Windows build

Install the .NET 8 SDK on Windows 10/11. Run:

    dotnet run --project src/TEWUniverse.Studio/TEWUniverse.Studio.csproj

To publish a standalone executable:

    dotnet publish src/TEWUniverse.Studio/TEWUniverse.Studio.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/app

Builds on GitHub Actions also publish a separate .NET updater agent and create an Inno Setup per-user installer. A successful build creates a versioned GitHub Release, which the installed editor can check for and install on restart. Do not claim a usable installer until that Windows build succeeds.

## Scope of current MVP

The editor only updates **existing** workers. It displays raw data fields where TEW-specific value mappings are not yet established. It never inserts worker records. It searches tblWorker, and reads tblWorkerSkill, tblWorkerOver, tblWorkerPhysical and tblWorkerBio via existing WorkerUID/UID keys. Linked rows that do not exist are left untouched. Company creation, title lineage generation and advanced validation are future work.

## File safety

Import makes a fresh working copy of an .mdb file in:

    %LOCALAPPDATA%\TEW Universe Studio\Projects\

The imported source is never edited. Settings persists three local paths in `%LOCALAPPDATA%\TEW Universe Studio\settings.json`: **Default Database** (source, read only), **Current Copy** (editable MDB opened on the next launch), and **Backup Folder** (snapshots created on request). The user may select working copies outside the app Projects folder. The editor refuses to open the configured Default Database as a writable working copy, and importing never overwrites an existing file. Be careful when selecting a Current Copy: it must not be an original game database. **Save Changes** updates the selected Current Copy directly with parameterized SQL inside a transaction; it does not create a backup. **Create Backup** is a separate command that copies the current saved database to the selected Backup Folder (or the copy's Backups folder by default), without saving unsaved edits. If edits are pending, the user is warned they will not be included. Backups never overwrite an existing file. The database password is requested on each session **unless** Remember database password is enabled. If enabled, Windows DPAPI encrypts the credential with the CurrentUser key in a local credential file. It is never stored in settings.json or the public repository. Keep additional independent backups and never test on a live game save.

No database password is hardcoded in this repository. Opt-in credential storage writes **encrypted** Windows DPAPI bytes locally at %LOCALAPPDATA%\TEW Universe Studio\credential.dpapi. Settings only stores the opt-in preference, and turning it off deletes the encrypted file. Do **not** commit .mdb, .accdb, saved games, passwords, or developer/game assets to this public repository. The .gitignore excludes common database files, but review commits before pushing.

## Settings workflow

Open **Settings** from the toolbar. Set the original TEW IX MDB path, an editable current-copy MDB path, and a backup folder. The file paths persist through application updates. If Current Copy already exists, the program reopens it on startup. With **Remember database password on this Windows account** enabled, a successful connection saves an encrypted credential and later launches try it automatically; otherwise the app asks for the password. If the credential is unavailable or rejected, it asks again. **Import TEW9.mdb** now prompts the user to choose an exact new working-copy file path instead of silently creating a copy in the Projects folder. **Save copy as...** copies the active working database to a user-chosen, new MDB filename and switches the active project. Neither action overwrites an existing file. Settings warns when using an existing editable MDB; never select the installed scenario as the working copy.

Users updating from early v0.1 releases can choose their existing MDB from the original app's Projects directory as Current Copy. They do not need to import again.

## Saving and manual backups

**Save Changes** writes edits straight to the working .mdb that is currently open. It never creates a snapshot. **Create Backup** copies the latest saved state of that working .mdb to the folder selected in Settings; it does not commit unsaved form edits. These operations are intentionally independent. **Save copy as...** creates a separate editable working database and switches to it. The application continues to keep the configured Default Database separate from the editable Current Copy.

## Runtime requirements

Microsoft Access Database Engine/ACE OLE DB compatible with win-x64 is required. The app tries ACE 16 and ACE 12, with several connection string variants. It cannot install an Access driver on its own. If a particular database fails to connect, confirm the database file and provider's architecture and configuration.

## Validation caveats

A write succeeding does not establish in-game compatibility. Always open the resulting working copy as a TEW IX scenario in a separate test environment, run TEW's Data Check, and create a **new game** when testing scenario edits. Existing saves do not incorporate edits from scenario MDBs. Application validation currently checks database value types and update row counts, not the many TEW-specific rules, enumerations and foreign keys. It is easy to create data TEW rejects.

GitHub releases are currently **unsigned**. The installer updater compares SHA-256 checksums with the matching GitHub Release metadata and then runs Inno Setup in quiet mode after the editor closes. A future production release should use Windows code signing.

## Product roadmap

First, validate existing-worker updates on a sacrificial TEW IX scenario. Then introduce human-friendly dropdown mapping, limits and relationship checks. Later milestones can add company editing, title histories and controlled batch updates. Never claim new record creation is supported until proved with real TEW IX validation.


## Phase 1 desktop shell

The Windows Forms application now uses the TEW Universe dark navy and cyan workspace layout selected for Phase 1. It has module navigation, a searchable worker browser, top actions, five editor tabs, contextual summary cards, and a project footer. The browser and editor use actual TEW IX worker data, without fabricated profile values or unverified field mappings.

Workers remains the only functional editor module. The Companies, Titles, Events, Stables, Storylines, Databases and Tools navigation items explicitly identify themselves as future modules. The existing database workflows are preserved: import and open copy, direct save (without automatic backups), manual Create Backup, Save copy as, settings, optional encrypted password remembering, and updater. Phase 2 will handle field-friendly controls, TEW-specific value mappings and smarter data presentation. The layout does not import or display invented wrestler images from conceptual mockups.


## Phase 2: Existing worker data-entry improvements

The Worker Profile tab now renders the actual MDB columns as clearly labeled form fields using their database types. Boolean columns use three-state checkboxes so a database NULL is not silently converted to False. Existing date columns use date pickers when representable, and unsupported date values remain text fields. Text and numeric fields preserve their real stored values; typed validation runs before Save Changes. Labels and tooltips retain each true TEW IX column name to aid investigation.

The Skills, Popularity and Physical tabs retain their dense editable grids, but names are humanized and the source column is exposed in tooltips. Unverified enumerations such as Gender, Pronouns and Sexuality explicitly say "(code)" and stay numeric until mappings have been tested against the game; no speculative code conversion is performed. The search library now filters by the database's actual Active boolean with All, Active and Inactive filters. The profile side panel shows real worker status and a skills snapshot drawn only from recorded numeric skill fields.

**Do not confuse friendly field names with game-rule validation.** TEW IX internal relationships and numeric enum meanings still need testing, and the application only edits existing worker records. Save Changes writes to Current Copy without automatic backup. Create Backup remains a separate operation.
