# Development and compatibility

## Annual tournament calendar dates and lineage import

For an **Annual Tournament / Cup** title, the Advanced Generator no longer asks for a fixed winning day number. Choose a month (1–12), week of month (First, Second, Third, Fourth, or Last), and weekday (Sunday through Saturday). Every historical year resolves its own valid date, allowing events that always occur on a given weekday. For example, First Sunday of March produces **1994-03-06** and **1997-03-02**. The first four occurrences always exist, and Last selects the last matching weekday of the month. Changing the calendar controls invalidates the current preview.

**Import Preview to Title Lineage** uses the existing transactional `RebuildTitleLineage` path to replace `tblBeltPreHistory` rows only for the selected title. It still requires a manually created backup, a schema check, unchanged-file checks, and explicit destructive confirmation. After importing, the app switches to Title Lineage and reloads the saved entries. Annual tournaments retain `BeltLost = 1666-01-01` and `Defences = -1`. A CSV export is still only a draft, **not** an import format; the import button writes the current in-memory preview to the selected scenario MDB. Test a disposable scenario in TEW IX Data Check before using production data.

## Windows build

Install the .NET 8 SDK on Windows 10/11. Run:

    dotnet run --project src/TEWUniverse.Desktop/TEWUniverse.Desktop.csproj

To publish a standalone executable:

    dotnet publish src/TEWUniverse.Desktop/TEWUniverse.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/app

Builds on GitHub Actions also publish a separate .NET updater agent and create an Inno Setup per-user installer. A successful build creates a versioned GitHub Release, which the installed editor can check for and install on restart. Do not claim a usable installer until that Windows build succeeds.

## New WPF interface

The Windows release now builds `src/TEWUniverse.Desktop` using WPF. Its MainWindow provides the redesigned sidebar, searchable worker list, profile, skills, popularity, physical and biography panels, plus settings and the existing update workflow. It shares the proven database service and local preferences from `src/TEWUniverse.Studio` through linked C# files; the old WinForms UI is not shipped. Other sidebar sections are labeled unavailable until implemented.

## Uniform display of TEW IX codes

The worker editor uses the stored numerical codes for Gender, Nationality, Based In, Wrestling Style and other unverified enumerations. The header and Quick Facts display `Code N`; the editable fields show their original numeric values and no longer translate Gender 1/2 into Male/Female dropdown labels. This avoids partially translated field values and does not alter the MDB schema or values by itself. True/False flags are Boolean fields and continue to use Yes/No controls.

### Advanced Generator: worker tenures and exact reigns

The third tab in **Titles** is **Advanced Generator**, separate from the original generic Title Lineage preview. It uses existing workers from `tblWorker`, including their numeric UIDs, and also accepts name-only historical holders without any worker record (`Holder1=-1`). The history display name can differ from the worker's current MDB name, so historical gimmicks and aliases are preserved. **Load Recorded Holders** builds a background candidate pool from that championship's own `tblBeltPreHistory` entries, respecting each recorded holder's UID where one exists.

Each row in the candidate list has a history name, an optional linked worker UID, a first and last eligible year at the company, an **exact number of required reigns**, and a **Fill gaps** flag. The constrained generator places required reigns only in eligible years, attempts to spread multiple reigns throughout a worker's tenure, and optionally fills all unassigned reign positions using eligible background holders. Example: Dragonheart (worker #4975) could be guaranteed **three reigns in 2016–2021**, while other historical holders fill the remaining title chronology. The work-period dates are entered by the user, not inferred as facts from worker records. Preview and CSV export never write to the MDB.

As with the original generator, the apply action updates the selected belt's existing `tblBeltPreHistory` rows **only when the draft reign count exactly matches existing slots**. History UIDs, BeltUID and BeltOrder remain unchanged; no new records are inserted or borrowed. Real worker UIDs are verified against `tblWorker`, while name-only champions intentionally use the TEW convention `-1` and a populated holder-name field. An existing worker can use a historical alias in that field. The current constrained generator handles singles; it does not overwrite existing tag-team reigns. The button requires a confirmation and the user should create a separate backup first. In-game compatibility of generated dates and history requires testing against a sacrificial scenario before production use.

### Championship reign variety

The Advanced Generator now takes a different path for **Championship** titles than for **Annual Tournament / Cup** labels. For a normal championship, each real superstar's pinned winning **year** is preserved, but the precise capture date within that year is distributed across the calendar. The remaining holder transitions are generated from weighted duration tiers (roughly 2–6 weeks, 1–3 months, 3–8 months, 8–15 months, and occasional 15–25 month reigns), rather than rigid January-to-December cycles. Generated NPCs can recur after intervening reigns but do not receive new `tblWorker` records. When the same linked worker has several pinned victories, an intervening different champion is required so they become separate reigns. The timeline remains chronological and nonoverlapping, with `BeltLost` one day before the next capture and the final holder's history extending through the chosen end year. Defence counts use variable duration-sensitive ranges, not a universal fixed rate. The generator still honors its 500-record safety cap.

In the **legacy fixed-count** generator, randomly weighted reign durations are normalized so exactly the requested number of reigns covers the user-selected dates without equal division. Previews show each reign's **Days held**; the advanced preview also summarizes short, medium, and longer-than-one-year reigns. Rerunning Generate can produce a different plausible history without writing to the MDB. These are fictional history heuristics, not recorded TEW IX results.

**Annual Tournament / Cup** is intentionally excluded from this algorithm. Tournament history still uses one winner per calendar year on an event date calculated using its selected month, ordinal weekday occurrence (first through fourth, or last), and weekday, with `BeltLost = 1666-01-01` and `Defences = -1`. Rebuild actions remain destructive for the selected title's existing history and require the manual backup and confirmation safeguards. Verify on a disposable MDB before applying.

### Editing additional championship fields

All additional supported scalar `tblBelt` fields are editable in **Championship Details → Additional TEW IX title settings**. Fields are detected from the selected MDB's schema, and each field retains its stored .NET/OLE DB type. Boolean flags get Yes/No controls, numerical TEW IX codes remain raw editable values, and text/date fields retain their original values until the user changes them. Edits are included in the ordinary dirty-check, Save Changes, and transaction flow; no direct writes occur from typing into a control.

The `UID` primary key, internal `Recordname`, unsupported/binary or calculated fields, and unknown `*UID` / `*_ID` references remain in a smaller read-only group. The two recognized references, `OriginalFed` and `CardUID`, are editable but must reference existing IDs in `tblFed` or `tblCard`, or an explicit unassigned sentinel (0 / -1). The app confirms if a linked ID is being changed; it does not create missing companies/events. This title setting editor is different from the **Title Classification** selector, which is app-only and never changes a TEW IX type column.

This functionality writes to the selected active MDB. Back up before using it, and validate changes through TEW IX's Data Check where applicable.

## Championship details and presentation classification

The Titles editor now exposes all additional `tblBelt` columns through an expandable **Additional TEW IX fields** inspector, with protected IDs/references read-only. Basic text/description columns from a strict allowlist are editable when present; the app does not enable arbitrary linked-field editing.

The **Title Classification** control offers **Championship** or **Annual Tournament / Cup**. It is *app-only*, keyed by database filename/path and `tblBelt.UID` in `settings.json`; there is no new type column and no change to existing TEW IX numeric enums. The label persists across launches and survives other settings changes. Championship previews generate ongoing reign records. Annual tournaments generate winners rather than ongoing reigns: one winner per calendar year (unless a championship title is selected), a recurring event schedule (month, week-of-month occurrence, weekday) that computes a distinct date for each year, `BeltLost = 1666-01-01`, and `Defences = -1`, matching the sentinel convention observed in the provided TEW IX screenshot. The tournament viewer omits these no-reign fields, relabeling winning dates and holders. This is not a guarantee that every scenario uses the same sentinel semantics; test in TEW IX Data Check using a disposable MDB before rebuilding any production title history.

The legacy reign-based generator remains for championship titles only. Rebuild operations still delete/reinsert only the selected `tblBeltPreHistory` rows transactionally, and require a manual backup and explicit confirmation.

### Superstar and NPC history sections

The Advanced Generator interface is deliberately separated into two areas:

- **Superstars (top):** Linked `tblWorker` entries. Add a worker by name or UID, optionally specify a historical alias, then enter the worker's eligible years, exact reign count, and one calendar year for every title-winning reign. Reign counts and fixed winning years are enforced for linked superstars. A single-year eligible window starts with one win prefilled.
- **NPC History (bottom):** Only the historical first and last year, plus optional exact race/gender codes. All usable names come from `tblGenerateWorker`; there is no manual NPC roster or NPC reign-count field. The generator fills each otherwise-unassigned calendar year using a recurring set of generated historical names. Those NPCs remain `Holder1=-1` in `tblBeltPreHistory` and never appear in `tblWorker`. Generating an entirely name-only history is permitted.

Unlike the previous advanced editor, the total history-row count is derived from the chosen years and pinned human wins; users do not need to count NPC reigns. For example, 1994–2020 with one real championship-winning entry in 2020 produces 27 historical entries, 26 from the NPC name pool. Where multiple real wins are pinned in the same year, each remains a separate recorded entry. Generated dates use calendar-year boundaries as defaults; this does not assert the actual date of tournament matches or that a previous winner literally lost a match. The preview labels previous and next **winners** rather than claiming match results.

Rebuilding remains destructive **for the selected championship's existing prehistory only**, is guarded by a manually created backup and an explicit confirmation, and needs TEW IX compatibility testing on a disposable MDB. These changes do not affect the separate original generic generator in the Title Lineage tab.

### Pinned winning years, generated NPCs, and creating new title history

The **Advanced Generator** provides a `Fixed win years` cell for linked workers. Comma-separated years (for example `2016, 2017, 2019`) are non-negotiable anchor events. The generator plans title changes before, between, and after those specific victories. It uses `tblGenerateWorker` for an NPC naming pool (with optional exact race and gender code filters), not `tblWorker`; NPCs exist **only in title history**, as `Holder1=-1` with the display name. A linked worker such as Kaito Moriyama #4975 can use the historical ring name Dragonheart.

The planner fills other historical reigns with a recurring cast of generated NPCs and shows implied title changes in a preview. These are implied by consecutive holders, not explicit match results in the MDB. The existing generic generator is retained for non-anchored workflows. Generated names are loaded from the actual MDB schema. A missing or unrecognized source-name field produces an error with available columns; field names or numeric code legends are never invented.

Applying an anchored draft **replaces the selected championship's complete title prehistory**, even if the new number of reigns differs from the old number. It deletes `tblBeltPreHistory` rows only where `BeltUID` matches the selected title, then inserts new rows with unique `Recordname`, `BeltOrder`, holder names and IDs, win/loss dates, and defences in **one database transaction**. It does not insert or modify worker records, change other titles, or create companies. The application checks the schema and file modification stamp, requires an on-demand **Create Backup** operation in the active session, and shows a separate irreversible replacement confirmation before committing. On a failed insert, it rolls back the transaction. All regeneration should first be tested against a backed-up TEW IX scenario; MDB structure and game-side behavior still require on-device validation.

## Title lineage and bounded generator

The Titles workspace includes **Championship Details** and **Title Lineage** tabs. Lineage reads `tblBeltPreHistory` directly. The data reader inspects the available columns, identifies an explicit numeric reference to `tblBelt.UID`, then fetches only the selected belt's rows using a parameterized query. Order Number is applied when a recognizable numeric order field is present. The read-only grid exposes the actual MDB columns rather than inventing holder names or dates. If the relation cannot be established, it explains which columns were found and shows no rows; other championships' histories must never leak into the selected one. The supplied TEW IX database schema confirms `BeltOrder`, `Holder_Name1`/`Holder_Name2`/`Holder_Name3`, `BeltCaptured`, `BeltLost`, and `Defences`. A generator now produces a preview from a user-selected pool of existing workers, a date range, and single/tag mode. Applying a preview is an explicitly confirmed, transactional update of **every already-existing history row of the selected championship only**. Row UIDs, BeltUIDs, BeltOrders and Recordnames remain intact; there are no INSERT or DELETE operations. A generated count different from the number of the title's pre-existing slots can be exported as a draft CSV but cannot be applied to the MDB. CSV drafts are for inspection and are not confirmed to be directly importable by TEW IX. Existing original history is overwritten when a user applies a proposal; back up and run TEW Data Check.

## Existing-record module expansion

The integrated WPF workspace now supports repurposing **existing** company, title, event and stable entries without creating new TEW IX records. Modules use `tblFed`, `tblBelt`, `tblCard`, and `tblStable` respectively, each keyed by existing `UID`. The left browser searches names and IDs, and the right pane edits an allowlist of basic fields. **UID, company links, title holders, stable members, relationship IDs, and other unverified coded fields cannot be changed in these modules.**

Permitted fields are intentionally narrow: Companies can edit Name, Initials, URL, Logo, Backdrop, Banner, Prestige, Influence, Money and Momentum. Titles can edit Name, Picture, Profile, Prestige and Active. Events can edit Name, Logo and Dormant. Stables can edit Name, Logo, Backdrop, Profile, Active and Important. All modifications still go through the existing parameterized, transactional `Save Changes` path. Backups are created only through `Create Backup`.

This is not evidence that TEW IX will accept every modified value. Test with a separate copied database and TEW IX Data Check. No automatic reign insertion or creation of missing linked records is enabled.

## Scope of current MVP

The editor updates **existing** workers, companies, championship titles, events and stables. It displays raw data fields where TEW-specific value mappings are not yet established. It never inserts worker records. It searches tblWorker, and reads tblWorkerSkill, tblWorkerOver, tblWorkerPhysical and tblWorkerBio via existing WorkerUID/UID keys. Linked rows that do not exist are left untouched. Brand-new record creation, title lineage generation and advanced game-specific validation are not supported.

## Active database workflow (direct edit)

TEW Universe Studio now works directly with **one explicitly selected scenario MDB**. Choose **Select Database** to open it (or choose it through **Settings → Active Database**). **Save Changes** modifies that file in place, using parameterized transactions. **Refresh Database** closes and reopens the data session to load records added or edited outside the application. Avoid concurrent editing: close TEW IX's own database editor before using TEW Universe Studio, and close TEW Universe Studio before editing the file in TEW IX.

**Migration is explicit:** settings from older versions still load, but the previous **Default Database** and **Current Copy** paths are not silently promoted to the Active Database. Select the actual MDB intentionally the first time you use this release. The app confirms direct file editing before opening it and again on startup. This mode does **not** edit `TEW9Save.mdb` or update existing save games.

**External modification guard:** the application captures the active MDB's size and last-write timestamp on opening and rejects Save Changes or generated title-reign replacement if those file attributes change before a write. Refresh Database discards pending unsaved edits after confirmation, reconnects, and rereads workers and other entities. Metadata checks reduce accidental stale edits but cannot guarantee conflict prevention against simultaneous writers; do not have both editors open for editing.

**Create Backup** remains separate and manual. It closes the connection momentarily, copies the saved active MDB to the configured Backup Folder (or a sibling `Backups` folder), then reopens the connection. It does not include unsaved fields. **Export Copy As** similarly creates a new file without switching the active database and without overwriting existing files. Use a backup before testing unverified TEW data modifications, especially title lineage generation.

The password is requested for each session unless encrypted password remembering is enabled. When enabled, the password is encrypted with Windows-user DPAPI and stored outside settings.json. No credentials, database files, or game assets should be committed to the repository.

## Runtime requirements

Microsoft Access Database Engine/ACE OLE DB compatible with win-x64 is required. The app tries ACE 16 and ACE 12, with several connection string variants. It cannot install an Access driver on its own. If a particular database fails to connect, confirm the database file and provider's architecture and configuration.

## Validation caveats

A write succeeding does not establish in-game compatibility. Always open the resulting working copy as a TEW IX scenario in a separate test environment, run TEW's Data Check, and create a **new game** when testing scenario edits. Existing saves do not incorporate edits from scenario MDBs. Application validation currently checks database value types and update row counts, not the many TEW-specific rules, enumerations and foreign keys. It is easy to create data TEW rejects.

GitHub releases are currently **unsigned**. The installer updater compares SHA-256 checksums with the matching GitHub Release metadata and then runs Inno Setup in quiet mode after the editor closes. A future production release should use Windows code signing.

## Product roadmap

First, validate existing-worker updates on a sacrificial TEW IX scenario. Then introduce human-friendly dropdown mapping, limits and relationship checks. Later milestones can add company editing, title histories and controlled batch updates. Never claim new record creation is supported until proved with real TEW IX validation.


## Phase 1 desktop shell

The Windows Forms application now uses the TEW Universe dark navy and cyan workspace layout selected for Phase 1. It has module navigation, a searchable worker browser, top actions, five editor tabs, contextual summary cards, and a project footer. The browser and editor use actual TEW IX worker data, without fabricated profile values or unverified field mappings.

Workers and four existing-record modules are functional. The legacy WinForms notes below are retained for historical context. The Companies, Titles, Events, Stables, Storylines, Databases and Tools navigation items explicitly identify themselves as future modules. The existing database workflows are preserved: import and open copy, direct save (without automatic backups), manual Create Backup, Save copy as, settings, optional encrypted password remembering, and updater. Phase 2 will handle field-friendly controls, TEW-specific value mappings and smarter data presentation. The layout does not import or display invented wrestler images from conceptual mockups.


## Phase 2: Existing worker data-entry improvements

The Worker Profile tab now renders the actual MDB columns as clearly labeled form fields using their database types. Boolean columns use three-state checkboxes so a database NULL is not silently converted to False. Existing date columns use date pickers when representable, and unsupported date values remain text fields. Text and numeric fields preserve their real stored values; typed validation runs before Save Changes. Labels and tooltips retain each true TEW IX column name to aid investigation.

The Skills, Popularity and Physical tabs retain their dense editable grids, but names are humanized and the source column is exposed in tooltips. Unverified enumerations such as Gender, Pronouns and Sexuality explicitly say "(code)" and stay numeric until mappings have been tested against the game; no speculative code conversion is performed. The search library now filters by the database's actual Active boolean with All, Active and Inactive filters. The profile side panel shows real worker status and a skills snapshot drawn only from recorded numeric skill fields.

**Do not confuse friendly field names with game-rule validation.** TEW IX internal relationships and numeric enum meanings still need testing, and the application only edits existing worker records. Save Changes writes to Current Copy without automatic backup. Create Backup remains a separate operation.


## Phase 2, Step 2: profile form

The worker Profile tab now groups **Identity**, **Status & Dates**, **Game Codes** and **Additional Database Fields** into scrollable two-column form sections built from the actual loaded \`tblWorker\` columns. Every editing control is bound to its source MDB field, and only changed values are sent through the existing validated transactional save pipeline. Names use text inputs, Boolean values use three-state checkboxes with null support, date columns use date pickers where representable, integer values use bounded numeric inputs when non-null, and unknown or nullable numeric values use validated text fields.

TEW IX's numeric enumeration meanings have not been confirmed. They are explicitly labeled as codes and remain editable as raw stored values without speculative dropdown labels. No worker creation, relationship editing, or implicit creation of missing linked rows is included. Changes still save only to the selected working copy, while **Create Backup** remains a separate manual action.

Windows compilation and installer packaging are verified through GitHub Actions. Actual read/write compatibility and layout behavior still require a test with a local expendable TEW IX working-copy MDB.
