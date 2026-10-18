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

The imported source is never edited. Settings persists three local paths in `%LOCALAPPDATA%\TEW Universe Studio\settings.json`: **Default Database** (source, read only), **Current Copy** (editable MDB opened on the next launch), and **Backup Folder** (pre-save snapshots). The user may select working copies outside the app Projects folder. The editor refuses to open the configured Default Database as a writable working copy, and importing never overwrites an existing file. Be careful when selecting a Current Copy: it must not be an original game database. Every Save closes its database connection, creates a copy in the selected backup folder (or the copy's Backups folder by default), reopens, and performs parameterized updates within a transaction. The database password is requested on each session **unless** Remember database password is enabled. If enabled, Windows DPAPI encrypts the credential with the CurrentUser key in a local credential file. It is never stored in settings.json or the public repository. Keep additional independent backups and never test on a live game save.

No database password is hardcoded in this repository. Opt-in credential storage writes **encrypted** Windows DPAPI bytes locally at %LOCALAPPDATA%\TEW Universe Studio\credential.dpapi. Settings only stores the opt-in preference, and turning it off deletes the encrypted file. Do **not** commit .mdb, .accdb, saved games, passwords, or developer/game assets to this public repository. The .gitignore excludes common database files, but review commits before pushing.

## Settings workflow

Open **Settings** from the toolbar. Set the original TEW IX MDB path, an editable current-copy MDB path, and a backup folder. The file paths persist through application updates. If Current Copy already exists, the program reopens it on startup. With **Remember database password on this Windows account** enabled, a successful connection saves an encrypted credential and later launches try it automatically; otherwise the app asks for the password. If the credential is unavailable or rejected, it asks again. **Import TEW9.mdb** now prompts the user to choose an exact new working-copy file path instead of silently creating a copy in the Projects folder. **Save copy as...** copies the active working database to a user-chosen, new MDB filename and switches the active project. Neither action overwrites an existing file. Settings warns when using an existing editable MDB; never select the installed scenario as the working copy.

Users updating from early v0.1 releases can choose their existing MDB from the original app's Projects directory as Current Copy. They do not need to import again.

## Runtime requirements

Microsoft Access Database Engine/ACE OLE DB compatible with win-x64 is required. The app tries ACE 16 and ACE 12, with several connection string variants. It cannot install an Access driver on its own. If a particular database fails to connect, confirm the database file and provider's architecture and configuration.

## Validation caveats

A write succeeding does not establish in-game compatibility. Always open the resulting working copy as a TEW IX scenario in a separate test environment, run TEW's Data Check, and create a **new game** when testing scenario edits. Existing saves do not incorporate edits from scenario MDBs. Application validation currently checks database value types and update row counts, not the many TEW-specific rules, enumerations and foreign keys. It is easy to create data TEW rejects.

GitHub releases are currently **unsigned**. The installer updater compares SHA-256 checksums with the matching GitHub Release metadata and then runs Inno Setup in quiet mode after the editor closes. A future production release should use Windows code signing.

## Product roadmap

First, validate existing-worker updates on a sacrificial TEW IX scenario. Then introduce human-friendly dropdown mapping, limits and relationship checks. Later milestones can add company editing, title histories and controlled batch updates. Never claim new record creation is supported until proved with real TEW IX validation.
