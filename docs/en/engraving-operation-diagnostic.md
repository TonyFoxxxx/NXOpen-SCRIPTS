# Engraving operation diagnostic

[Русский](../engraving-operation-diagnostic.md) | **English**

## Latest release

**V1.01 · 2026-11-23** — JSON now includes predrill and cut-region start points, clearance and lower-limit planes, boundary inheritance and UDE order. If a read fails, the journal records the reason and continues collecting available data. Sorting UF parameter indices now compiles with the NX journal compiler's restricted assembly references, fixing `CS1069`.

Select **«Диагностика операции гравировки»** in the updater. The permanent file is [NX_Engraving_Operation_Diagnostic.cs](../../scripts/NX_Engraving_Operation_Diagnostic.cs).

V1.01 source SHA-256 is `27edbd5b40c68ec73b3525f443966e2afa83bfc42ef5d0331cf6c51a031f03b8`.

## Obtain a report

1. Open the work CAM part in **Manufacturing**. Accept the reference **PLANAR_MILL** or **PLANAR_PROFILE** settings with **OK** and close the operation editor.
2. Select only that operation in the **Operation Navigator**. Selecting multiple nodes produces a selection error.
3. Run the diagnostic through **Tools → Journal → Play** or a journal button.
4. Click **«Сохранить JSON» (Save JSON)** and choose a file with the `.json` extension. **«Копировать всё» (Copy all)** copies the entire text report, including lines outside the visible area.

## Captured data

- Operation name and class, work part units, NXOpen version, Template Type and Template Subtype.
- Program Order, Machine Tool, Geometry and Machine Method parent chains, including the tool, MCS and Workpiece.
- UF parameters of the operation and its parent groups. The journal reads the required and inherited lists and checks public indices from the installed NXOpen.UF for the operation. Each applicable entry records its index, name, type, Default / Inherited / Overridden status, source and value. Length types carry the owning part's units in `units`. Other types have `units: null`. Consult the corresponding builder settings for feed and spindle speed units.
- Available editor settings, including feeds and speeds, cut pattern, levels, stock, tolerances, tool axis, engage, retract, transfers and compensation.
- Predrill and cut-region start points, their absolute part coordinates and available depth fields. The journal reads plane status and usage independently. It requests the origin and normal of defined planes, including inactive ones.
- Selected objects and selection lists, geometry providers, local and inherited boundaries. The report includes boundary counts and members, available Open / Closed types, tool position, stock and tolerances.
- Start and end UDEs in their original order, including events with no parameters. Parameter records contain the type, state and available value.

## Reading the JSON

The schema is `nx-engraving-operation-diagnostic`, version `1`. The `records` array contains `context`, `parent`, `parameter`, `setting`, `list`, `alias`, `geometry_provider`, `geometry_item`, `boundaries`, `boundary_member`, `ude_set`, `ude`, `ude_parameter`, `verification` and `error` entries. The `report` field contains the full text displayed in the window.

Numbers use round-trip precision. The snapshot preserves `0`, `false`, empty strings, empty lists and `null`. For UF parameters, `value_available` and `source_available` are independent. If `partial` is `true`, `problems` gives the path and reason for each missing read. Error records include the exception type, message, stack and NX error code when available. Successful reads remain in the same file. Object tags belong to the source part.

## Files and limits

The journal reads the operation. The journal never calls `Commit`, generates a toolpath or saves the PRT. The operation builder is destroyed in `finally`. The `verification` entry records IsModified before and after the read and compares operation membership, direct parents and the Part Boundary data read. A `null` comparison result means at least one check was unavailable. Verification covers the listed model data.

The report window is resizable. Only **Save JSON** writes a file, in UTF-8 without a BOM. Cancelling the picker returns to the report. Windows asks before overwriting a file. The journal keeps the previous bytes in memory and attempts to restore them on a write error. If restoration also fails, the window displays both errors. It creates no temporary files, INI files or archives. If the custom window fails, NX Information displays the report text.

Traversal excludes internal pointers, previews and the toolpath editor. It reports unavailable data and limits of 16 levels or 20,000 nodes. Automated tests use synthetic operations to check typed values, inheritance, geometry, error handling and file writes. The full V1.01 source was compiled with NXOpen 2412 assemblies against .NET Framework 4.8 and the restricted set of .NET 10 Windows assemblies used by the journal compiler check. The reference operation and interactive file dialog require validation inside Designcenter 2606.
