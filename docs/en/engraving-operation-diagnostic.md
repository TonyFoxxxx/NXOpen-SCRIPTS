# Engraving operation diagnostic

[Русский](../engraving-operation-diagnostic.md) | **English**

## Latest release

**V1 · 2026-11-23** — separate journal that reads an existing PLANAR_MILL / PLANAR_PROFILE and displays a report with **Save JSON** and **Copy all** buttons. The snapshot supports development of automatic engraving-operation creation when no operation is selected. This version does not create operations.

Working file: [NX_Engraving_Operation_Diagnostic.cs](../../scripts/NX_Engraving_Operation_Diagnostic.cs). Updater entry: **«Диагностика операции гравировки»**.

## Obtain a report

1. Configure one engraving operation as the future default. Accept its settings with OK and close the editor.
2. Select that operation in **Operation Navigator**. It must belong to the work CAM part. A generated toolpath is not required.
3. Run the diagnostic through **Tools → Journal → Play** or a journal button.
4. Click **«Сохранить JSON» (Save JSON)**, choose a folder and attach the `.json` file to the conversation. Alternatively, **«Копировать всё» (Copy all)** copies the full text, including content outside the visible area.

The report window is resizable. Saving happens only on a button click; cancelling the file picker writes nothing. Windows asks before replacing an existing file. Nothing is sent automatically.

## Captured data

- Operation name and class, part units, NXOpen version, Template Type and Template Subtype.
- Program Order, Machine Tool, Geometry and Machine Method parent chains, including the tool, MCS and Workpiece.
- Required and inherited UF parameters of the operation and parent groups. The operation is additionally checked against public indices in the installed NXOpen.UF; inapplicable indices are skipped. Each applicable entry records its index, name, data type, Default / Inherited / Overridden status, source and value.
- Available editor properties: feeds, speeds, cut pattern, levels, stock, tolerances, tool axis, engage/retract, transfers and compensation. Numerical values are not rounded for display.
- Geometry providers, assigned objects, saved boundaries and members, Open / Closed type, stock, tolerances and tool position.
- Start/end UDE parameters, types and optional-parameter states.

JSON uses schema `nx-engraving-operation-diagnostic`, `schema_version: 1`. `records` holds `context`, `parent`, `parameter`, `setting`, `list`, `alias`, `geometry_provider`, `geometry_item`, `boundary_member`, `ude_parameter` and `error` entries. `problems` lists unavailable reads; `report` contains the full window text. Numbers and booleans retain their types. Tags belong to the source model and cannot serve as portable references in another part.

## Limits and validation

This is a diagnostic snapshot of available API data, **not an importable template** or a guarantee of every internal NX setting. Unsupported types, read failures and traversal limits are explicitly marked `UNAVAILABLE` / `UNSUPPORTED`; `partial` becomes `true`. Send that file in full: partial snapshots remain useful. Internal pointers are not dereferenced. Preview and toolpath editor objects are excluded; arbitrary object methods are never called. Traversal limits of 16 levels and 20,000 nodes are reported when reached.

The journal never commits, assigns parameters, creates operations/curves, generates a toolpath or saves the PRT. The reading builder is destroyed in `finally`. It compares operation membership, direct parents, saved Part Boundary membership/read data and IsModified before and after; this is not a byte-for-byte model comparison. Missing checks are reported; no automatic rollback occurs.

The only output file is the user-selected JSON. No temporary files, INI files, logs or archives are created. Reports include model object names and settings; share them intentionally. If the custom window fails, NX Information is used; the journal does not control that built-in window's title.

The full source compiles against Designcenter 2606 for .NET Framework and .NET 10. Tests outside NX cover typed readers, numeric precision, Unicode and long JSON values, unsupported types and API errors, cycles, avoiding mutating methods, and builder cleanup on failure. The Windows save-dialog structure is checked. A real NX operation and interactive dialog still need user validation; NX was not launched during development.
