# Mirrored CAM project — trial

[Русский](../mirror-project.md) | **English**

Script: [`NX_Mirror_Project.py`](../../scripts/NX_Mirror_Project.py). Version: **V1.01**.

## Latest release

**V1.01 · 2026-11-14** — pick a planar face or datum plane directly in NX, including oblique planes. The manual XY/XZ/YZ and coordinate dialog has been removed. Bounds are checked in a fixed frame aligned with the selected plane.

Fix the stop at `UF_CAMGEOM_ask_items: 1345005`. When a node cannot supply a geometry channel, the geometry provider is queried. A still-unavailable channel is explicitly reported instead of being treated as empty geometry. Other errors stop the trial with the node name, class and geometry kind. Transformation does not start if no CAM geometry reference can be read.

**This remains a trial. A complete successful run in NX / Designcenter 2606 is not yet confirmed. Numerical directions and coordinates inside operation settings are not transformed yet.**

## Workflow

1. Open a simple test part without components. It must be fully loaded, be both work and displayed part, and have a normal `.prt` file on disk.
2. Save it with the normal NX command. The journal stops if the source has unsaved changes; it does not save the source itself.
3. Use **Tools → Journal → Play** to run the script. Updater entry: **«Зеркальная копия CAM-проекта — тест» V1.01**.
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

Twenty-six portable checks passed, including error 1345005 on a container, geometry-provider routing, distinguishing empty from unavailable channels, oblique planes, face/datum selection and cancellation. A user reproduced the V1 pre-reflection failure and successful rollback. Plane selection and a complete V1.01 run in NX still need runtime validation.
