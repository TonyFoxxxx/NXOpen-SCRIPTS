# Mirrored CAM project diagnostic

[Русский](../mirror-project-diagnostic.md) | **English**

Script: [`NX_Mirror_Project_Diagnostic.py`](../../scripts/NX_Mirror_Project_Diagnostic.py). Version: **V1**.

## Latest release

**V1 · 2026-11-13** — first diagnostic release to support development of a separate mirrored assembly with independent CAM operations and relocated MCS frames.

The script reads the open project's structure, modeling feature types, operations and available geometry references. Results appear in a separate window with a **«Копировать всё» / Copy all** button.

**This is a diagnostic. Mirroring, component cloning, MCS relocation and operation-reference transfer are not implemented yet. A report does not certify that a project can be mirrored automatically.**

## Workflow

1. Open the top-level CAM assembly and make it the **Display Part**. Fully load its components using standard NX controls beforehand if their data is needed.
2. Run **Tools → Journal → Play** and select the script. The updater entry is **«Диагностика зеркального CAM-проекта»**.
3. Wait for the result window, click **«Копировать всё» / Copy all**, and send the text to the developer for reference-transfer analysis.
4. Close the window. The script does not save or close the project.

## Collected data

- Already loaded component prototypes, repeated occurrences and positions returned by `Component.GetPosition` in NX's native context. Each prototype is inspected once; occurrences remain separate.
- Load completeness, modified flag, body count and modeling feature type counts.
- Operations and geometry-tree groups: class, name, toolpath presence, mirrored-operation flag and geometry parent.
- Required UF parameter catalog and types; spatial/reference values and selected numeric parameters identified by geometric names. Values are **not transformed**. Coordinate systems found through references include origins and axis matrices.
- Part, Blank, Check, Cut Area, Trim, Wall and Drive geometry; available boundaries, element references, custom start points and predrill points.
- Read failures and size limits. Unknown parameter types are not guessed. A mismatch between a UF count and its returned array is reported as an error.

## Files and changes

Results are held in memory. The script does not create PRT, INI, TXT, ZIP, logs, temporary files or directories; it does not load more parts, save, SaveAs, update, generate paths, undo, or create/commit builders. Clipboard contents change only when **Copy all** is clicked.

The text includes part/operation names and coordinates. The script does not request full file paths, although NX error messages may contain them. Review the text before sharing if needed. Nothing is sent automatically.

## Limitations and validation

- Intended for Windows inside NX / Designcenter. The result window uses Win32 without external libraries or `.dlx` resources.
- Partially loaded parts are marked and skipped; no automatic loading occurs.
- UF exposes only part of CAM's internal representation. This is not a complete export of all settings, WAVE links, expressions, assembly constraints or FBM data. An unavailable call can indicate an unsupported object type rather than a damaged project.
- Limits: 500 parts, 5000 occurrences, 3000 CAM objects per part, 10000 parameter indices per object, and 200 items per nested list. Truncation is stated in `limits` or an `omitted` field.
- Portable checks cover partial parts, repeated components, read errors and incomplete arrays. **Execution in NX 2606 and the Windows result window have not been tested yet.**
