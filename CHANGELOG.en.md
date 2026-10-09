# Changelog

[Русский](CHANGELOG.md) | **English**

## 2026-10-09 — ESKD V1.37

- Previous current-sheet borders, title/auxiliary tables and their labels are deleted before the new format is built, including hidden Siemens elements and objects from earlier runs.
- Cleanup respects sheet membership, cascading table/child deletion and the prohibition on cleanup after new formatting starts. A deletion failure rolls back this run.
- Material text now has an explicit break before GOST/OST/TU, followed by wrapping of long entries and font reduction if the block is too tall. Final checks verify that line breaks survived the sheet refresh.
- Retained table sizes, INI handling, the external font and section geometry. Updated the catalog and Russian/English documentation.
- Added executable C# algorithm checks for text placement, border areas, deletion order and failure handling. Checks without NX do not replace object/rendering verification in the target application.

## 2026-10-09 — Setup sheets V2.49

- Added an optional first-sheet faceted 3D model. With it off, NX fits the current orientation and captures a screenshot; camera and display settings are restored afterwards.
- Enabling it reveals an accuracy coefficient (0 < k ≤ 1) and combined triangle limit (default 500,000). Lower coefficients reduce detail; higher triangle limits are supported by both export and the browser.
- Exceeding the limit shows a concise instruction to reduce accuracy or increase the limit. The previous document is retained.
- Re-export switches between screenshot and mesh, including partial setup updates. Updated the updater catalog and Russian/English documentation.
- 30 portable checks and the browser mesh → screenshot → mesh scenario passed. Four native Windows checks were skipped on Linux; target NX / Designcenter execution still requires separate validation.

## 2026-10-09 — Updater V1.18 and numbering V1.04

- Numbering INI/XLSX files are installed beside the script in the selected working folder. Numbering locates its INI using the running NX journal path instead of the former fixed data directory.
- The updater migrates the old settings/history pair, preserving ranges, comments and records. Existing destination files are retained; lost history is never replaced with an empty template.
- Migration sources are checked again and guarded against writes while copying. Ordinary write failures roll back changes; old source files remain untouched.
- The catalog requires updater V1.18: update and restart it before installing the new numbering script. Missing companion files can be restored manually even when the script version is current.
- Added scenarios for installation into a folder with spaces, history migration, custom paths, repeat runs, missing/conflicting history, source changes and write rollback. Execution inside NX / Designcenter remains to be checked in the target environment.

## 2026-10-09 — ESKD V1.36

- Restored the horizontal 70 × 14 mm upper box for all supported formats and both sheet orientations; duplicate-designation text remains rotated by 180°.
- Long title-block text wraps before font reduction. Added wrapping for codes without spaces and full line-height checks while retaining the 185 × 55 mm title block.
- Cells use native Wrap followed by AutoSizeText. After the sheet refresh, the script checks fitting settings and evaluated overflow hashes. It warns if measured text cannot fit even at 2.5 mm and needs further fitting.
- Retained the public external-font workflow, INI handling, menus and section geometry. Updated the catalog and Russian/English documentation.
- Checked C# syntax, box dimensions and catalog consistency. Local full-suite run: 111 tests passed and 20 were skipped; one postprocessor test stopped because the validation environment lacks the .NET SDK. Repository checks do not replace NX execution; rendering in the target NX / Designcenter builds remains unverified.

## 2026-10-07 — English documentation

- Added an English repository overview, installation/configuration instructions and guides for all 13 scripts.
- Added English publishing instructions, release history and third-party notices, with language links to the Russian originals.
- Script source, versions, settings and updater catalog are unchanged.

## 2026-10-07 — Postprocessing V1.45

- Multiple-offset parsing inherits modal state from headers and previous blocks. Known G modes are restored only when needed; coordinates and tool/spindle/coolant commands are not invented. Moves and final modes are checked with optional-block skipping on and off.
- Added separate X and Y blocks before the first separate Z approach with G43/H, numbered O headers, and G80 with G00/G01/G02/G03 in either order. A header-only offset is inserted before every repeated section.
- Explicit post entries support `Auto`, `ISO_G28` and `ISO_G53` in the existing INI. G53 requires `WorkOffsetG53Z` matching the original retract, G49, G90/G00/G40 and one explicit G20/G21 unit mode. The script neither chooses a machine-safe height nor adds retract moves.
- Errors show post, profile, offsets, the failed condition and neighboring original NC lines. Copy error details includes the version; Close/Escape dismisses the window. A native error fallback remains if Windows Forms fails. No report/backup files were added.
- Existing paths, INI editors, selection UI, Description, Zmin, numbering, subprogram calls and BIN behavior were retained. Added numeric ISO and Windows error-window checks. Original O1261/O5555 cases were checked privately. Target NX/controller validation remains necessary.

## 2026-10-07 — Setup sheets V2.48

- Added startup checkboxes matching postprocessor V1.44: operation numbering, tool Description and Zmin naming. All reset to off. Inline Description formats are mutually exclusive and retain the choice when hidden/shown during a run.
- Numbering restarts in each selected Program Order folder, replaces old leading numbers and uses two/three digits at the 100-operation threshold. Description updates both fields for supported tools throughout the part without changing diameter or T/H/D; missing D is not invented.
- Zmin uses selection captured before the menu. Empty selection covers the project; unreliable selection never expands scope. It uses the standalone algorithm, replaces numeric suffixes and reports unsupported paths/conflicts in NX. Calculated values are reused in the sheet.
- Preparation begins after setup/component confirmation. Cancellation or failure before HTML publication restores names/descriptions. Successful export leaves one NX Undo mark; no automatic PRT save.
- Added option-combination, repeat-run, 99/100 boundary, conflict, Description, missing/invalid D, partial-write restoration and HTML-write failure checks. Real Windows dialogs test mouse/keyboard input, double-click, inline formats, repeated show/hide alignment and cancellation.
- Existing selection drawing, HTML editor, paths and INI were retained without extra service files. Target NX validation is still required.

## 2026-10-07 — Setup sheets V2.47

- Checkboxes and text in setup/component dialogs share the actual Windows row center. Tree connectors/expand icons are aligned; state sizes come from the native image list.
- Preserved hit areas, selection, scrolling, double-click, keyboard behavior, parent/descendant exclusion, initially collapsed NONE, disabled suppressed components and independent per-setup component choices.
- Added real Windows-dialog pixel/input checks with normal/enlarged fonts, themes, scrolling, Space and component previews when switching setups.
- A green Sheet saved message appears for three seconds after a confirmed manual save, overlaying the UI without shifting it. It is neither embedded in saved HTML nor printed. Autosave, cancellation, errors and unconfirmed downloads do not show it.
- Calculations, view generation, paths and INI were unchanged; no extra runtime files. Target NX validation remains necessary.

## 2026-10-07 — Postprocessing V1.44

- Replaced the separate Description-format dialog with inline radio buttons below its checkbox: diameter only by default, or diameter plus T/H/D.
- Disabling the option hides its formats and restores the Zmin/tree positions without accumulating offsets. The current format survives show/hide; a new run defaults to diameter. INI is unchanged.
- Retained V1.43 tree drawing and preparation/postprocessing algorithms. Model changes still occur only before output; no extra runtime files.
- Added real Windows Forms checks for selection, absence of a second window, repeated show/hide and scaling. The full journal was compiled as C# 7.3 against NX API models; target NX execution requires separate checking.

## 2026-10-07 — Postprocessing V1.43

- Full tree rows now draw checkboxes, labels, connectors and expansion icons around one vertical center, fixing the native-checkbox offset left by V1.42's text-only adjustment.
- Native TreeView nodes, selection handlers, hit areas, mouse/keyboard behavior, scrolling, collapsed NONE and the operations tree without checkboxes were retained. State sizes use the actual Windows image list; row bounds come from Win32.
- Added a real Windows TreeView regression test covering pixels, normal/enlarged fonts, two themes, scrolling, mouse/Space selection, BeforeCheck cancellation and expansion. It does not launch NX or change desktop DPI.
- Full C# 7.3 compilation against NX API models, Windows/Linux repository checks and 815 real-tree checks per theme passed. Postprocessing, Description, Zmin, numbering, paths and INI were unchanged; no additional runtime files.

## 2026-10-07 — Setup sheets V2.46

- Added direct percentage input for every slider. Click the number; Enter/blur applies it and Esc cancels. Integer steps and existing ranges are retained; the logarithmic model slider accepts an exact percentage without rounding it to a slider position.
- Incomplete input stays in the UI rather than autosaved HTML. Label double-click, independent controls, exact saved values and Undo are retained.
- Original view restores the current sheet's initial layout, deleted original images, sizes/scales/settings and removes added cells. The first setup sheet also restores operation placement/limit; the project sheet restores the initial model view.
- Initial state is embedded in HTML, retaining deleted original images without duplicating remaining ones. Restoration survives save/reopen. Old HTML without the lost original data needs regeneration to restore those elements.
- The reset is one Undo step. Notes, locating information, global column settings and other sheets remain. No extra files, notifications or INI changes.
- Chromium checks covered sheet/setup variations, all percentage inputs and limits, exact model scale, Undo, repeated save, deleted-cell restoration, real autosave and printing. Windows/Linux repository checks passed; target NX execution remains outstanding.

## 2026-10-07 — Postprocessing V1.42

- Tree label alignment now uses the complete row returned by Win32 TVM_GETITEMRECT instead of text-top and configured ItemHeight. Actual scaled/scrolled coordinates are respected; native checkboxes and selection are retained.
- Renamed the Description option to “Add tool parameters to description”. Enabling it opened a choice of diameter only or diameter with T/H/D. Cancel/close/dialog failure unchecked the option while keeping the first window open.
- Passed the chosen format directly into preparation, removing the repeated prompt immediately before output. Re-enabling allowed another choice. Model changes remained deferred until output; algorithms, INI, paths and shared rollback were retained.
- Checked full C# 7.3 compilation against NX API models, format-selection flows, output without another prompt, rollback and alignment at 100/150/200%. Actual NX-window visual validation remained necessary.

## 2026-10-07 — Postprocessing V1.41

- Added initially disabled Description and Zmin-name options. Description format was selected each time the option was enabled: `⌀*_T*_H*_D*` or `⌀*`, filling both description fields from actual tool parameters.
- Retained standalone-script scopes: Description for tools in the work part; Zmin for preselected operations or, only with empty selection, all project operations. Unreliable selection blocks Zmin rather than widening scope.
- Zmin uses actual Three/Five ToolAxis and analytical arc/helix minima. Only terminal numeric Z suffixes are replaced; repeated runs do not duplicate them. Conflicts/unsupported paths are included in the result.
- Preparation runs alongside numbering before output. Cancellation/failure before the first saved file restores descriptions/names; partial publication retains changes.
- Adjusted vertical tree-label alignment while retaining native interaction. Paths, INI and postprocessing code were unchanged; no extra files.
- Checked full C# 7.3 compilation against NX API models, Description, combined numbering, cancellation/errors, 160 arc/helix comparisons and 72 suffix/rounding cases against standalone Zmin. Drawing geometry was checked at 100/150/200%; live NX/window checks remained necessary.

## 2026-10-07 — Updater V1.17

- Available-version cells for newer versions of installed scripts now have green RGB 0, 205, 134 (`#00CD86`) backgrounds and black text, independent of checkbox/row selection.
- Numeric internal versions determine highlighting. Equal/older/unknown versions and new installations are not highlighted. Successful update followed by Check removes the highlight.
- Correct cells remain highlighted through sorting, scrolling and resizing. Native checkboxes, grid, paths, INI, validation and installation order were retained without new service files.
- Added version-comparison/drawing-handler checks and pixel checks on a real Windows list.

## 2026-10-07 — Setup sheets V2.45

- Original view was added to every physical sheet's toolbar: project tool catalog, setup start and operation continuation. It replaces the old model-only button without duplication.
- Reset restores that sheet's table scale to 100%, operation wrapping and manual note height. The first setup sheet also restores tool scale, MCS arrows/outline and image scale/center; the project sheet restores the model/static image view and scale.
- Text, locating information, IPW choices, cell contents/layout, global column settings and other sheets are retained. Operation pagination is recalculated.
- Reset is one Undo step. No additional files/notifications; paths and INI retained.
- Chromium checks covered multiple sheets/setups, input, reset/Undo, data preservation, save/reopen, migration from the old button, static images and printing. Windows/Linux checks passed; target NX execution remained outstanding.

## 2026-10-07 — Postprocessing V1.40

- Added leading zeros: `01_`–`99_` for output programs with at most 99 numbered operations; `001_`, `002_`, … from the first operation when there are 100 or more.
- Width is based on the full numbered set for each output program independently. Selected-operation mode counts only that set. Renumbering replaces old prefixes and recalculates width across 99/100.
- Updated the checkbox tooltip. With numbering disabled, current names remain unchanged. Order, cancellation, restoration, paths, INI and other postprocessing functions were retained.
- Checked C# 7.3 compilation against NX API models; counts 1/9/10/99/100/101/999; mixed-size programs; selected sets; repeated runs; cancelled transition to three digits; earlier error/output scenarios. This revision had not run in target NX. Manifest, README and guide updated.

## 2026-10-06 — Postprocessing V1.39

- Restyled diameter errors with a white background, red Segoe UI text, even spacing and two equal buttons. Added Continue? and removed old labels/icon.
- Green No stops; bright-red Yes continues while bypassing this check's errors for the current run. Original messages/full mismatch list remain. No is initially selected; Escape stops.
- Only this dialog class and version changed. Tool/MCS checks, NC output, paths, INI and other windows were retained without extra files.
- Checked C# 7.3 compilation with NX API models, controls/layout, choices, default state, resource disposal and previous postprocessing/INI-preservation cases. Native appearance/scaling still required NX validation. Manifest, README and guide updated.

## 2026-10-06 — Postprocessing V1.38

- Added Machine folders to destination selection: multiple local/network roots, labels and removal; supports Browse and pasted paths including Windows Copy as path quotes.
- Save updates only `[MachineRoots]` in the existing INI, retaining other sections, post paths, comments, encoding and line endings. Cancel writes nothing; removing an entry does not delete directories/programs.
- Saving refreshes/resizes machine buttons immediately and requires selecting the destination again. BIN, numbering, names, offsets and manual-folder behavior remain. Discovery still uses immediate all-digit subfolders and explicit `[Machines]` entries.
- Invalid/inaccessible new paths, no numbered subfolders, invalid labels or an INI write conflict block saving. Unchanged unavailable roots can remain with warnings. No extra service files.
- Checked C# 7.3 compilation with NX API models, UTF-8/UTF-16 settings, cancellation, multiple roots, refresh/reselection, access errors and write conflicts. Target NX/network validation remained necessary. Manifest, README and guides updated.

## 2026-10-06 — Setup sheets V2.44

- Added shared Undo history of up to 100 actions, top-bar Undo, Ctrl+Z outside text fields and a setup-level Undo action button. Text fields keep normal typing Undo.
- Undo covers text/locating data, arrow/scale parameters, column widths/visibility, operation moves, cells/images and model view. A drag or slider movement becomes one step.
- Deleted original views and pasted images are restored with grid, dimensions, scale, pan and IPW variant. History stays in memory without extra files or per-step copies of model geometry.
- Save/autosave retain open-sheet history; an undone state is saved as a new revision, including after an in-flight save. Reopening clears history.
- Chromium checks covered editor actions, image deletion/restoration, text Ctrl+Z, model, save/reopen and printing; real autosave and Windows/Linux checks passed. Target NX validation remained necessary.

## 2026-10-06 — Postprocessing V1.37

- Added an initially unchecked Number operations option. Without it, original names/prefixes remain and no extra numbering Undo mark is created. It applies to full and selected-operation output and is not stored in INI.
- When enabled, NX operation names received `1_`, `2_`, … in tree order, restarting for each output program. Old leading numbers were replaced; selected-operation mode renamed only selected operations.
- Cancellation/failure before the first saved NC file restores names. Successful/partial publication retains names and an NX Undo mark. NC transformations, output paths, offsets and BIN were unchanged.
- Added Postprocessor paths for common/per-program post selection: named entries, add/remove and TCL/DEF selection with automatic matching DEF.
- Save writes post settings to the main INI and reloads the list, retaining other settings/comments/encoding/line endings. Cancel writes nothing; removal does not delete post files. First save can create a missing INI.
- Checked C# 7.3 compilation with NX API models, both numbering states, repeat/rollback, post settings/INI and different-post output. Target NX validation remained necessary. Manifest/guides updated; stable filename retained.

## 2026-10-06 — Setup sheets V2.43

- Fixed a one-way coupling between global sliders: MCS-arrow size and outline thickness no longer change operation row/text scale. The scale handler accepts only its intended controls.
- Double-clicking a label resets only that parameter to 100%. Independent sheet/setup settings, tool scale and first-sheet image scale are retained.
- Chromium checks covered keyboard adjustment, double-click without text selection, arrow/outline geometry, saved HTML reopened without a draft and printing. Windows/Linux checks passed; target NX validation remained necessary.
- Auto-width allocation to Operation, paths, INI and autosave remain, without extra files.

## 2026-10-06 — Setup sheets V2.42

- Changed operation-table auto-width priority: Tool and other columns get their full content width; Operation receives the remaining space.
- Operation names may use ellipses or wrap when the sheet option is enabled; pagination accounts for wrapped row height.
- The priority also applies to saved tables in automatic mode. Separate tool lists, manual widths, paths, INI, notes, locating data and autosave were retained.
- Chromium checks covered full tool names/numeric values, wrapping, 60–300% scale, column hiding, absence of internal scrolling, save/reopen and printing. Target NX validation remained necessary.

## 2026-10-06 — Setup sheets V2.41

- Changed auto-width to reserve full values first, leaving remaining width for tool names with ellipses. Measurement uses the actual font, units, indentation and repeated group headings.
- Tables fit to the sheet on screen and in print when space is insufficient. Removed internal horizontal scrolling; navigation between sheets remains. Fitting repeats when scale or column visibility changes.
- Numeric values are no longer artificially shortened because of measurement error. Pagination accounts for fitting and rounded row boundaries at screen scale.
- Manual widths, restoring auto width, notes, locating data, paths, INI and autosave retained; no extra files.
- Chromium checks covered 60–300% scales, column switching, long values, save/reopen and printing. Target NX validation remained necessary.

## 2026-10-06 — Setup sheets V2.40

- Added autosave-helper fallback when NX's embedded Python has no working standalone `python.exe`. It uses the already loaded interpreter DLL/libraries in a separate standard Windows PowerShell process.
- The launcher uses in-memory Reflection.Emit, without extra PS1/EXE/DLL/log/INI/temp files, Python installation or changes to PATH, registry or execution policy.
- HTTP service, address/origin/key checks, revision protection and coordination with regeneration remain. If startup is unavailable, completed HTML still opens directly.
- Startup errors now include available helper diagnostics from memory. Added real Windows DLL-launch checks, blocked-primary-interpreter cases, long paths, repeated journal runs and save conflicts.
- Stable filename, output paths, INI, notes, locating data and editor retained. Target NX validation remained necessary.

## 2026-10-06 — Setup sheets V2.39

- Reduced staging-path nesting that caused WinError 206 for long project names. One short staging folder sits beside the PRT while remaining on the required volume for atomic replacement. Final HTML path, PRT path and INI format are unchanged.
- Python file operations, INI, regeneration and autosave use extended Unicode Windows paths. Normal/existing short capture paths are checked before NX model building/capture; excessive paths without 8.3 support stop early. No registry, drive or project-name changes.
- Moved the local helper out of NX Python threads into a separate process of already available Python. An HTTP check precedes browser opening; failure automatically opens HTML directly. No helper installation/files.
- Reading/replacing an old sheet is coordinated with autosave through memory/process channels rather than lock files. Revision checks, notes, locating data, UI and Zmin are retained.
- Added Windows long-path, compact-staging, repeated-run, automatic-opening, concurrent export/autosave and blocked-main-interpreter response checks. A control run in target NX remained necessary.

## 2026-10-06 — Setup sheets V2.38

- Aligned the Zmin column with `NX_Operation_Zmin.py` V1.02, using actual ToolAxis for both Three and Five motions. Operation names were unchanged.
- Added a local helper: browser access through `127.0.0.1` with autosave to the original HTML without first selecting a file. In this release the helper ran in NX process memory, without installation/logs/service files.
- Autosave waits for editing/dragging to pause. Identity/revision checks prevent overwriting another tab or fresh export; retry after a lost response does not create copies.
- The helper accepts writes only from its page to a registered file. Draft/download-copy remain if unavailable; direct HTML opening retains browser saving. No new HTML notifications.
- Checked portable code, Zmin trajectory models, local HTTP protocol and Chromium editor. Target NX execution still required checking.

## 2026-10-06 — Updater V1.16

- All five column headers sort rows; repeated clicks reverse direction and show an arrow. Versions sort numerically.
- Sorting retains checkboxes/selected row; another Check in the same window retains sort order.
- Double-clicking any row cell toggles selection; double-click directly on the checkbox counts once.
- Unavailable-row restrictions and selection lock during installation remain. Added sorting, selection-retention, mouse-event and real Windows-list checks.
- Manifest version/hash updated. V1.15 WinHTTP fix retained; minimum updater remains V1.13.

## 2026-10-06 — Updater V1.15

- Fixed fallback-download error `WinHttpSetOption(TLS): 12018` from V1.14 by passing `WINHTTP_OPTION_SECURE_PROTOCOLS = 84` for the session instead of unrelated option `31`.
- The WinHTTP test model now checks option validity for the handle type: old code reproduces error 12018 and the fix passes.
- Added a real Windows WinHTTP test creating native handles and configuring TLS while intercepting send before network exchange. Skipped on Linux.
- TLS 1.2, certificate/hash checks and existing INI are retained without system changes. Manifest version/hash updated; minimum remains V1.13.

## 2026-10-06 — Updater V1.14

- NX Python `CERTIFICATE_VERIFY_FAILED` triggers a Windows WinHTTP retry using system certificate-chain validation, proxy settings and TLS 1.2. Certificate verification is not disabled.
- Fallback accepts only the original repository raw HTTPS address, rejects redirects and retains size/completeness limits, cancellation and SHA-256 checks. Downloads remain in memory without extra files.
- A Windows certificate failure explains that an administrator should check trust, date and antivirus/proxy HTTPS inspection.
- Added 12 download/handle-release regression tests; 43 tests passed outside NX. The affected PC still required validation.
- Manifest version/hash updated; minimum remains V1.13. Script name and INI format retained.

## 2026-10-05 — Updater V1.13

- Renamed the updater to `NX_Update_Scripts.py` and main settings to `NX_Update_Scripts.ini`. Updated example INI, catalog, links, validators and tests.
- If the new INI is absent, the old `NX_Update_Script_Buttons.ini` is read/written without creating a second copy.
- Raised catalog minimum to V1.13. Old clients need a one-time manual filename migration; installation instructions cover INI and button-path changes.
- Added step-by-step NX button instructions. Create a button once at a stable path; updates replace contents.
- Both updater names are excluded from ordinary folder updates. GitHub self-update of the new name remains deferred until the window closes.

## 2026-10-05 — Updater V1.12

- Changed the default working folder to `C:\ProgramData\NX_SCRIPTS` and updated examples/guides.
- Existing INI paths are retained. Relocation and button-path changes remain manual.
- Updated updater/example-INI hashes and version in the manifest. Minimum remained V1.10.

## 2026-10-05 — User documentation

- README became the catalog/installation entry point, with detailed separate script guides.
- Removed internal bundle notes and private-conversation links; instructions are written for new users.
- Clarified installation, configuration, error reporting and Zmin requirements.
- Script source, versions and settings were unchanged.

## 2026-10-05 — Updater V1.11

- Fixed redraw during rapid resizing: controls move in one batch without copying stale pixels, then background, child controls and borders repaint. Drag end also triggers full redraw.
- Added sibling clipping, minimized-window handling and fallback control movement if batch layout fails.
- Added seven resize-handler checks. Minimum-version testing now models an old client independently of current source version. Total: 29 tests; NX pixel drawing remained a separate check.
- Manifest version/hash updated; minimum remained V1.10 so self-update was available.

## 2026-10-05 — Zmin V1.02

- Fixed rotated-part Z calculations: Three-format paths can carry a tilted ToolAxis, which V1.01 replaced with MCS Z. Both Three and Five now use the actual motion vector.
- The MCS origin must lie on the part's rotation axis. Ordinary three-axis results remain when tool axis aligns with MCS Z. Unavailable axes and varying-axis arcs/helices are skipped with explanations.
- Passed 25 tests outside NX; checked 8,346 saved CL points from 35 operations and replayed 17 full paths including arcs/helices. A control run of the new version inside NX remained necessary.
- Updated catalog version/hash, README and guide; stable filename retained.

## 2026-10-05 — Script guides

- Short descriptions in README/manifest now describe functionality without revision history.
- Added detailed guides for all 13 journals: features, use, settings, output and limitations.
- Clarified postprocessing modes, sheet editing, eight PDF line groups, three DXF modes, numbering file operations and assembly renaming.
- Added an NX walkthrough for updater V1.10; Windows/Linux automated checks passed.
- Source, script versions, INI and register template were unchanged.

## 2026-10-05 — Updater V1.10

- Fixed Windows CI `WinError 5` when replacing an existing script or updater INI: close the updater's own protective handle immediately before atomic replacement of that file.
- Retained pre-write hash checks, protection of other files and ordinary-error rollback. The rollback test now verifies that the first replacement really happened before simulating failure of the second.
- Raised minimum to V1.10. V1.08/V1.09 users needed a one-time manual updater replacement while keeping their INI.

## 2026-10-05 — First public GitHub bundle

Published 13 journals for CAM, setup sheets, drawings, export and file management. Working filenames are stable; versions are inside each source file.

### Updater V1.09

- GitHub uses one manifest for Check, then downloads selected sources for installation.
- Validates schema, paths, SHA-256 and internal versions; blocks downgrades.
- Retains selection UI and working paths. Folder mode is manual, without automatic fallback on errors.
- User INI and numbering history are not updated alongside scripts. Examples are created only for an absent script with absent settings.
- Writes through a checked adjacent file with atomic replacement and in-memory rollback for ordinary errors. Pending files are removed afterwards; leftovers from ended processes are removed next launch.
- Self-update follows window closure/thread completion. V1.08 required one-time manual replacement.
- Blocks a second window in the same Windows session. Does not create NX buttons/New User Commands.
- Added data-preservation tests and Linux/Windows CI; these do not replace NX/Win32-window validation.

### Setup sheets V2.37

- Notes grow vertically without shrinking the font and can be resized from the bottom; height is saved.
- `NX_Setup_Prototype.ini` supplies the programmer name; blank leaves the field empty.
- Clarified INI-read errors.
- Included toolpath Zmin relative to operation MCS.

### Minimum Z in operation names V1.01

- Incorrect Z for some rotated-part paths was discovered in V1.01 and corrected in V1.02.
- Added renaming for selected operations or all project operations with empty selection.
- Adds/replaces a terminal numeric `_Z` suffix. Four-axis calculation uses the current tool direction; the MCS origin must lie on the part's rotation axis.
- Retained path/name-conflict checks and NX Undo without separate settings/service files.

### ESKD V1.35

- Removed embedded ASCON font bytes because redistribution permission was not established.
- Loads a compatible external copy the user is permitted to use, from the script folder or supported NX/Windows locations.
- Retained size, CRC and actual-family checks. No automatic substitution of a similar font; formatting does not run without a compatible file.

### Other journals

Included postprocessing V1.35, operation renaming V1.04, numbering V1.03, tool descriptions V1.06, PDF V1.11, DXF V1.08, assembly renaming V1.05, project folder V1.01 and setup-sheet folder V1.01. Filenames were normalized and text saved as UTF-8 with LF.

Added schema-1 manifest, example INI files, an empty register, guides, third-party notices and publishing checks.
