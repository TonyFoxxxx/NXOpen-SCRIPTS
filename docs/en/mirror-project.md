# Mirrored CAM project — trial

[Русский](../mirror-project.md) | **English**

Script: [`NX_Mirror_Project.py`](../../scripts/NX_Mirror_Project.py). Version: **V1.04**.

## Latest release

**V1.04 · 2026-11-14** — transfer boundaries with user-defined planes. In the copy, reflect the plane origin and X/Y axes, then reconstruct Z to keep a right-handed frame. This preserves in-plane contour coordinates and the meaning of material side. Plane position, boundary type, material side, flags and geometry references are checked after transfer. A boundary shared by several operations is written once.

Replace single-object selection with its name entry field by the standard geometry picker: **click one planar face or datum plane → click OK**. Empty or multiple selection explains that exactly one object is required. No copy is created before confirmation.

For plane reads and writes, only the copy's WCS is temporarily aligned with ABS, then restored even on exceptions. Boundary handles are reacquired after model update. Transfer uses the original data, so an associative plane that has already moved is not reflected twice.

**This remains a trial. A complete successful run in NX / Designcenter 2606 is not yet confirmed. Other numerical directions and coordinates inside operation settings are not transformed yet.**

## Workflow

1. Open a simple test part without components. It must be fully loaded, be both work and displayed part, and have a normal `.prt` file on disk.
2. Save it with the normal NX command. The journal stops if the source has unsaved changes; it does not save the source itself.
3. Use **Tools → Journal → Play** to run the script. Updater entry: **«Зеркальная копия CAM-проекта — тест» V1.04**.
4. In the geometry picker, click one **planar face** or **datum plane**, then click **OK**. No name or coordinate entry is required. Oblique planes are supported. The surface defines the plane, not the cursor position; reversing its normal does not change the reflection. Curved surfaces and objects from another component are rejected. Cancelling selection exits without creating a copy.
5. After selecting the plane, the first unused sibling filename such as `name_MIRROR_TEST_001.prt` is created. Existing files are never replaced.
6. Read and share the report using **«Копировать всё» (Copy all)**. If checks pass, inspect bodies, MCS frames and the geometry of every operation. Then save the open test copy using the normal NX command and regenerate operations.

**Until manually saved, the reflected result exists only in the open NX session. The file on disk remains the initial copy of the source PRT.**

## Behavior

- Copies the saved PRT byte for byte, verifies checksums and opens a separate part. Source CAM data is not processed.
- Reads operations, MCS/RCS and Part, Blank, Check, Trim, Cut Area, Wall and Drive geometry references in the copy, including collector members and supported boundaries.
- Reflects bodies and supported referenced geometry through a model-parent transformation. Body, face and edge identifiers must remain stable; otherwise the trial fails.
- Creates transferred MCS/RCS frames. X and Z are reflected; Y is reconstructed to form a right-handed coordinate system. Local definitions are replaced; the geometry root receives its own frame when necessary. Inheritance is checked.
- Transfers user-defined boundary planes without recreating contours. Plane X/Y axes are reflected and Z is reconstructed as the negative of that plane's reflected original Z. MCS Z is transferred separately. The geometric plane and material side should follow the reflection. Parameters and references are checked after update. The copy's WCS is restored to its original position.
- Removes calculated toolpaths in the copy. Does not create mirrored CAM operations, generate paths or postprocess.
- Checks exact oriented object bounds in the selected-plane frame, geometry identifiers, available selected CAM references, operation membership and geometry parents, missing toolpaths and transferred MCS/RCS frames. Finally checks the source file checksum and modification flag.
- On an exception after the Undo mark is created, attempts to undo changes in the copy and reports the stage, error and rollback outcome. Close the copy without saving if rollback fails.

## First-trial limits

- Only operation classes `VolumeBased25DMillingOperation`, `SurfaceContour`, `PlanarMilling` and `TpMultiAxisDeburring` are accepted. Existing mirrored operations are rejected.
- Supported simple local feature history: DATUM_CSYS, DATUM_PLANE, DATUM_AXIS, SKETCH, EXTRUDE, BLEND, Tooling Box, FSET, BREP, BLOCK, CYLINDER, CONE, SPHERE, IMPORTED_BODY, IMPORT. Other types and detected external parents stop the trial before transformation.
- Components, non-associative boundaries without geometry references and detected custom start/predrill points are not supported yet.
- Other numerical spatial operation parameters are not reflected. The report lists detected local nonzero 3D parameters for inspection; this is not a complete inventory of spatial settings. The journal does not edit feeds, allowances or strategies.
- Preserving references and bounds does not prove machining correctness. Selected regions, directions, engage/retract moves and regenerated paths must be checked before use on production projects.
- Requires Windows and the installed NX UF libraries. Geometry channels unavailable with code 1345005 are listed as unverified. Their contents are not confirmed; successful checks on available references do not establish full coverage of all regions. Other API errors or a failed transformation stop the trial.

## Files and validation

Only the requested PRT copy is created. The journal writes no reports, INI files, logs, archives, temporary files or `.dlx` resources. It does not call Save, SaveAs or SaveAll. The report is held in memory and includes names, coordinates and the copy path. Nothing is sent automatically. A newly created PRT copy may remain on disk after a failed trial.

Forty-seven checks passed outside NX, including ten with a separately compiled C library. Coverage includes plane reads/writes, boundary settings and references, one write per inherited boundary, avoiding double reflection, detecting failed writes, restoring a translated/rotated WCS, exactly-one-object selection and cancellation. Mathematics checks preserve in-plane contour coordinates and material side under oblique reflection. A user confirmed V1.03 reading of a user-defined boundary plane and rollback. A complete V1.04 run and the new dialog appearance still require validation in NX.
