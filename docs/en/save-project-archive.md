# Save project and archive

[Русский](../save-project-archive.md) | **English**

Script: [`NX_Save_Project_And_Archive.py`](../../scripts/NX_Save_Project_And_Archive.py). Version: **V1**.

## Latest release

**V1 · 2026-11-11** — initial release.

- Native work-part save with NX error checks, without changing the path or closing the project.
- One ZIP next to the project; another run replaces its contents with the latest saved `.prt`.
- The ZIP is built and verified in memory. Disk readback checks the extracted `.prt` CRC, size and SHA-256.
- On a catchable write failure, the journal attempts to restore the previous ZIP from memory and verifies restoration. Project-save and archive results are reported separately.

## Use

1. Save the project once with the normal NX command to an existing `.prt` file.
2. Make the intended project the **Work Part**. If an assembly component is the work part, that component is the save/archive target.
3. Play the journal or launch it through a custom button. The updater entry is **«Сохранение проекта и архива» (Save project and archive)**.
4. NX saves the work part and its modified components through `BasePart.Save(SaveComponents.TrueValue, CloseAfterSave.FalseValue)`.
5. If NX reports no unsaved parts/objects and the work part is no longer marked modified, the journal reads the saved main `.prt` and updates the ZIP.
6. A result dialog appears. After dismissing it, continue working and saving normally.

For example, `Part.prt` receives a sibling `Part.zip` containing exactly one entry, `Part.prt`. No dates or revision numbers are added to names. A smaller project also truncates the old ZIP; previous entries do not accumulate.

**External assembly component files are not included in the ZIP.** Modified components are saved by the native NX command, but the archive contains only the work-part `.prt`. It is not a complete assembly package or a folder archive: separate drawings, setup sheets, NC programs, images and other files are not added.

## Subsequent normal saves

The journal does not call `SaveAs`, rename or replace the original `.prt` through filesystem operations, switch Work/Display Part, create undo marks or register save handlers. It opens the original file read-only after the native save completes. All file handles and the `PartSaveStatus` object are released before displaying the result.

A normal save updates the original `.prt`. Only running this journal updates the ZIP.

## Files and settings

- The journal's only additional file is `<project name>.zip` next to the `.prt`.
- No INI, sidecar, log, backup ZIP, subfolder or temporary file is created, including in Temp. Archive ownership is recorded in the ZIP comment itself.
- NX settings, global paths, backup preferences and autosave policies are not changed. NX's own supporting files depend on NX configuration; the journal does not create additional copies or delete them.
- An unrelated same-named ZIP, a ZIP with added unrelated entries, or an unrecognizable ZIP is not overwritten. The journal reports a conflict.
- Reparse points, symbolic links and multiply hard-linked archives are rejected for writing.

## Failures and limitations

- If the NX save is not confirmed, the ZIP is unchanged. The journal cannot repair failures in NX saving, network storage or third-party save handlers.
- If NX saved the project but archiving failed, the result explicitly reports **«Проект сохранён штатной командой NX» (The project was saved by the native NX command)** and the ZIP error separately. The project is not rolled back.
- The complete new ZIP is held in RAM. During replacement, the previous ZIP is also held in RAM. Memory exhaustion before writing leaves the previous archive intact.
- ZIP access is exclusive while writing. Handles are released on completion or a handled error. The journal does not coordinate separate NX sessions editing the same `.prt`.
- **Without a staging file, ZIP replacement is not atomic.** Power loss, forced process termination or loss of disk access during overwrite can damage the archive. In-memory restoration is possible only for failures the running process can handle. Archiving itself never overwrites the original `.prt`.
- Unsaved projects, Teamcenter paths and read-only projects require normal user action before this journal can run. The target is ordinary Windows filesystem `.prt` files; network paths depend on server availability and permissions.

## Validation

File scenarios cover ZIP creation/replacement, absence of extra files, handle release, normal `.prt` writes between two runs, NX failures, memory exhaustion, partial writes, restoration and existing-archive conflicts. Used properties and methods were checked against the installed Designcenter 2606 NX Open Python stubs and headers.

Native execution in the installed **Designcenter 2606** passed using an isolated test `.prt`: journal → model edit and native Save → model edit and journal again. The original path, Work Part and Display Part were preserved. The ZIP contains exactly one `.prt`, byte-for-byte equal to the latest saved original. Only `.prt` and `.zip` remained in the project folder. All 22 dedicated automated tests passed. Production assemblies and network-server operation have not been tested.

Repository-wide suite: 313 tests; the first run had one existing numbering-test failure and one setup-sheet test socket error. All 10 setup-sheet save tests passed on an isolated rerun. The existing numbering test again exited with code 5 and no message; neither its source nor its test was changed in this release. Catalog, version and SHA-256 validation passed.
