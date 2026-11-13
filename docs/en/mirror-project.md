# Mirrored CAM project — trial

[Русский](../mirror-project.md) | **English**

Script: [`NX_Mirror_Project.py`](../../scripts/NX_Mirror_Project.py). Version: **V1.03**.

## Latest release

**V1.03 · 2026-11-14** — replace the Python `Cambnd.AskBoundaryData` call where V1.02 reported a memory access violation while reading a Blank boundary. Boundary parameters now use `UF_CAMBND_ask_boundary_data` with a full-width pointer and explicitly allocated C structure. Boundary handles no longer cross between the C API and the Python wrapper.

Returned arrays are freed separately from NX-owned handles; `app_data` is not dereferenced. The automatic-boundary-plane check is retained. Read errors are not ignored. The report identifies the boundary number and whether parameters or members were being read. Plane picking is retained.

**This remains a trial. A complete successful run in NX / Designcenter 2606 is not yet confirmed. Numerical directions and coordinates inside operation settings are not transformed yet.**

## Workflow

1. Open a simple test part without components. It must be fully loaded, be both work and displayed part, and have a normal `.prt` file on disk.
2. Save it with the normal NX command. The journal stops if the source has unsaved changes; it does not save the source itself.
3. Use **Tools → Journal → Play** to run the script. Updater entry: **«Зеркальная копия CAM-проекта — тест» V1.03**.
4. Click a **planar face** or **datum plane** in NX. Oblique planes are supported. The surface defines the plane, not the cursor position; reversing its normal does not change the reflection. Curved surfaces and objects from another component are rejected. Cancelling selection exits without creating a copy.
5. After selecting the plane, the first unused sibling filename such as `name_MIRROR_TEST_001.prt` is created. Existing files are never replaced.
6. Read and share the report using **«Копировать всё» (Copy all)**. If checks pass, inspect bodies, MCS frames and the geometry of every operation. Then save the open test copy using the normal NX command and regenerate operations.

**Until manually saved, the reflected result exists only in the open NX session. The file on disk remains the initial copy of the source PRT.**

## Behavior

- Copies the saved PRT byte for byte, verifies checksums and opens a separate part. Source CAM data is not processed.
- Reads operations, MCS/RCS and Part, Blank, Check, Trim, Cut Area, Wall and Drive geometry references in the copy, including collector members and supported boundaries.
- Reflects bodies and supported referenced geometry through a model-parent transformation. Body, face and edge identifiers must remain stable; otherwise the trial fails.
- Creates transferred MCS/RCS frames. X and Z are reflected; Y is reconstructed to form a right-handed coordinate system. Local definitions are replaced; the geometry root receives its own frame when necessary. Inheritance is checked.
- Removes calculated toolpaths in the copy. Does not create mirrored CAM operations, generate paths or postprocess.
- Checks exact oriented object bounds in the selected-plane frame, geometry identifiers, available selected CAM references, operation membership and geometry parents, missing toolpaths and transferred MCS/RCS frames. Finally checks the source file checksum and modification flag.
- On an exception after the Undo mark is created, attempts to undo changes in the copy and reports the stage, error and rollback outcome. Close the copy without saving if rollback fails.

## First-trial limits

- Only operation classes `VolumeBased25DMillingOperation`, `SurfaceContour`, `PlanarMilling` and `TpMultiAxisDeburring` are accepted. Existing mirrored operations are rejected.
- Supported simple local feature history: DATUM_CSYS, DATUM_PLANE, DATUM_AXIS, SKETCH, EXTRUDE, BLEND, Tooling Box, FSET, BREP, BLOCK, CYLINDER, CONE, SPHERE, IMPORTED_BODY, IMPORT. Other types and detected external parents stop the trial before transformation.
- Components, manually defined boundary planes, non-associative boundaries without geometry references and detected custom start/predrill points are not supported yet.
- Numerical spatial operation parameters are not reflected. The report lists detected local nonzero 3D parameters for inspection; this is not a complete inventory of spatial settings. The journal does not edit feeds, allowances or strategies.
- Preserving references and bounds does not prove machining correctness. Selected regions, directions, engage/retract moves and regenerated paths must be checked before use on production projects.
- Requires Windows and the installed NX UF libraries. Geometry channels unavailable with code 1345005 are listed as unverified. Their contents are not confirmed; successful checks on available references do not establish full coverage of all regions. Other API errors or a failed transformation stop the trial.

## Files and validation

Only the requested PRT copy is created. The journal writes no reports, INI files, logs, archives, temporary files or `.dlx` resources. It does not call Save, SaveAs or SaveAll. The report is held in memory and includes names, coordinates and the copy path. Nothing is sent automatically. A newly created PRT copy may remain on disk after a failed trial.

Thirty-nine checks passed outside NX. Six use a separately compiled C library to check structure size and field offsets, 64-bit handles, freeing only returned arrays, Blank boundary and geometry-reference reads, and stopping on errors or unsupported planes. Plane-selection, copying and rollback checks are retained; rollback after an access-violation exception is covered. These tests do not execute the NX kernel. A user confirmed V1.02 plane selection and rollback after a boundary-read failure; a complete V1.03 run in NX remains unverified.
