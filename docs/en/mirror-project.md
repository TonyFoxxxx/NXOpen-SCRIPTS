# Mirrored CAM project — trial

[Русский](../mirror-project.md) | **English**

Script: [`NX_Mirror_Project.py`](../../scripts/NX_Mirror_Project.py). Version: **V1.09**.

## Latest release

**V1.09 · 2026-11-14** — add transfer of a CSYS backed by one numerical Xform with no parametric parents. Such a CSYS may be associative while its Xform is defined by numbers. With unit scale and matching initial origin/axes, update the existing Xform's origin and directions. Retain the CSYS, Xform and CAM references; do not reflect shared or already transferred Xforms twice. A parameterized Xform with an empty parent list is not treated as numerical.

The report identifies MCS/RCS owners, parameter status/provider and, for unrecognized definitions, object types and parents. V1.08 recognized two stock features and two Three Planes definitions but stopped at a third CSYS before their transfer. The old report did not contain that CSYS's exact type; a new run is needed to establish whether it uses a numerical Xform.

Tooling Box solids are now excluded from the general transformation. When needed, edit the existing parametric stock feature and verify its type, offsets and references. Compare model history before and after to detect lost features and parameters. Before and after reflection, create API editors for Workpiece, MCS and supported operations, reload available geometry and run Validate. An error fails the trial and triggers rollback.

MCS transfer retains the existing CSYS. Recognized Three Planes definitions retain the same three support faces/planes and their original associative definitions. If directions need correction, linked coincident planes rebuild the Three Planes construction inside the existing CSYS; X/Z are reflected and Y completes a right-handed frame. Unknown associative definitions are not replaced by Dynamic. Verify position, axes, inheritance and the stored dependency graph separately. Implicit NX defaults still receive explicit bindings in the copy.

**This is a trial version. V1.09 has not been tested on a real PRT in NX / Designcenter 2606. Workpiece editability and Three Planes transfer after reflection remain unconfirmed. Retest from a healthy original project whose Workpiece opens. Other numerical directions and coordinates inside operation settings remain unchanged.**

## Workflow

1. Open a simple test part without components. It must be fully loaded, be both work and displayed part, and have a normal `.prt` file on disk.
2. Save it with the normal NX command. The journal stops if the source has unsaved changes; it does not save the source itself.
3. Use **Tools → Journal → Play** to run the script. Updater entry: **«Зеркальная копия CAM-проекта — тест» V1.09**.
4. In the geometry picker, click one **planar face** or **datum plane**, then click **OK**. No name or coordinate entry is required. Oblique planes are supported. The surface defines the plane, not the cursor position; reversing its normal does not change the reflection. Curved surfaces and objects from another component are rejected. Cancelling selection exits without creating a copy.
5. After selecting the plane, the first unused sibling filename such as `name_MIRROR_TEST_001.prt` is created. Existing files and names of parts still open in NX are not reused. To retest the original model, activate it first: the journal copies the work part at launch, even if that is a previous test copy. Collapsing the suffix changes only the output name, not the source data.
6. Read and share the report using **«Копировать всё» (Copy all)**. If checks pass, inspect bodies, MCS frames and the geometry of every operation. Then save the open test copy using the normal NX command and regenerate operations.

**Until manually saved, the reflected result exists only in the open NX session. The file on disk remains the initial copy of the source PRT.**

## Behavior

- Copies the saved PRT byte for byte, verifies checksums and opens a separate part. Source CAM data is not processed.
- Reads operations, MCS/RCS and Part, Blank, Check, Trim, Cut Area, Wall and Drive geometry references in the copy, including collector members and supported boundaries.
- Reflects ordinary bodies and supported referenced geometry through a model-parent transformation. Parametric stock is updated through its existing Tooling Box. Body identifiers and all selected references must remain stable. All faces/edges are checked on ordinary bodies; the body and explicitly selected faces/edges are checked on regenerated stock. Unselected internal stock topology may be regenerated.
- Transfers existing MCS/RCS while retaining supported Three Planes definitions and verifying associativity. X and Z are reflected; Y completes a right-handed frame. Only implicit NX defaults receive new numerical bindings. Resolve inheritance providers through UF; do not overwrite inherited nodes. Verify coordinates, parameter status/provider and the available RCS-to-MCS link flag.
- Transfers user-defined boundary planes without recreating contours. Plane X/Y axes are reflected and Z is reconstructed as the negative of that plane's reflected original Z. MCS Z is transferred separately. The geometric plane and material side should follow the reflection. Parameters and references are checked after update. The copy's WCS is restored to its original position.
- Removes calculated toolpaths in the copy. Does not create mirrored CAM operations, generate paths or postprocess.
- Checks exact oriented object bounds in the selected-plane frame, geometry identifiers, available selected CAM references, operation membership and geometry parents, missing toolpaths and transferred MCS/RCS frames. Finally checks the source file checksum and modification flag.
- On an exception after the Undo mark is created, attempts to undo changes in the copy and reports the stage, error and rollback outcome. After successful rollback, restore the source as work and displayed part. If rollback fails, leave the copy active and ask the user to close it without saving. Restoring the source does not delete files or close other parts.

## First-trial limits

- Three Planes rebuilding supports recognized mutually perpendicular support planes with unit scale. A CSYS backed by one numerical Xform is also supported with unit scale and matching initial origin/axes. An unconfirmed definition or damaged support stops the trial. Other associative CSYS definitions are accepted only if native updates retain their definition and produce the correct reflected frame.
- Some parametric stock cannot reach the reflected position without changing its settings. If original orientation, asymmetric offsets or dependencies prevent exact reflection, verification stops the trial. The journal does not switch stock to From Geometry or zero its offsets.
- Only operation classes `VolumeBased25DMillingOperation`, `SurfaceContour`, `PlanarMilling` and `TpMultiAxisDeburring` are accepted. Existing mirrored operations are rejected.
- Supported simple local feature history: DATUM_CSYS, DATUM_PLANE, DATUM_AXIS, SKETCH, EXTRUDE, BLEND, Tooling Box, FSET, BREP, BLOCK, CYLINDER, CONE, SPHERE, IMPORTED_BODY, IMPORT. Other types and detected external parents stop the trial before transformation.
- Components, non-associative boundaries without geometry references and detected custom start/predrill points are not supported yet.
- Other numerical spatial operation parameters are not reflected. The report lists detected local nonzero 3D parameters for inspection; this is not a complete inventory of spatial settings. The journal does not edit feeds, allowances or strategies.
- Preserving references and bounds does not prove machining correctness. Selected regions, directions, engage/retract moves and regenerated paths must be checked before use on production projects.
- Requires Windows and the installed NX UF libraries. Geometry channels unavailable with code 1345005 are listed as unverified. Their contents are not confirmed; successful checks on available references do not establish full coverage of all regions. Other API errors or a failed transformation stop the trial.

## Files and validation

Only the requested PRT copy is created. The journal writes no reports, INI files, logs, archives, temporary files or `.dlx` resources. It does not call Save, SaveAs or SaveAll. The report is held in memory and includes names, coordinates and the copy path. Nothing is sent automatically. A newly created PRT copy may remain on disk after a failed trial.

All 98 checks outside NX passed, including 31 with a separately compiled C library. Coverage includes Three Planes and numerical-Xform CSYS definitions, transferring both together, shared and inherited references, avoiding double reflection, Xform write errors, the one-byte UF logical and parent arrays. Checks for bounding-geometry errors, excluding stock from direct transformation, and selected stock faces and offsets remain in place. These tests do not validate the NX kernel, actual dialogs or toolpath generation on the user's PRT.

## Later stages — not implemented yet

- Select two planes and automatically construct the mirror plane between them. Parallel planes use their midplane; behavior for nonparallel planes needs a separate decision.
- Component checkboxes: for example, mirror fixtures while leaving vises unmirrored. Component copying, placement and dependencies need separate validation.
- Reuse the Setup Card component-selection window behavior. These features are deferred until the trial without components works correctly.
