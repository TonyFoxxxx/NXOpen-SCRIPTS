# Mirrored CAM project — trial

[Русский](../mirror-project.md) | **English**

Script: [`NX_Mirror_Project.py`](../../scripts/NX_Mirror_Project.py). Version: **V1.16**.

## Latest release

**V1.16 · 2026-11-14** — fix verification of stale Plane objects after replacing an MCS definition. In the V1.15 report, post-update verification accessed an original support that no longer existed and failed with `Invalid self parameter passed., Object no longer exists`.

After installing a new definition, verify the planes linked to the active CSYS. Reacquire objects by their identifiers after checking that the dependency graph is unchanged; verify their types, ownership and membership in the current part. Compare construction methods, direct selections and geometric placement with snapshots taken before reflection. Do not read unused Plane objects from the retired definition.

Losing an active plane, selected face or intentionally selected intermediate support remains an error. An original Plane still used by another MCS is checked through that active definition. Post-update orientation correction uses current planes and must survive a control update. The report adds the original and currently checked support identifiers.

An existing Plane now uses `CopyPlane()` to copy its definition with the same construction method and directly selected geometry. No additional new-plane-to-old-plane link is introduced. When the original input is directly a Face or DatumPlane, one coincident plane references that object directly. Intentionally selected intermediate planes are not flattened to their underlying faces.

Freeze the method and ordered direct selections before reflection. Check them during creation and orientation, after installing the definition, after model update and at final verification. Missing selections, a different face on the same geometric plane, or a changed construction method fail the trial even when MCS coordinates match. The report lists direct X, Y and Z selections separately. A copied definition must be a separate object in the same part; the original Plane is not used as a mutable copy.

Stock/MCS dependency ordering remains: evaluate original linked supports from parents to children and verify their placement before creating the CSYS. Dependency cycles stop before reflection.

If Undo fails, attempt to restore the original project and close without saving only the copy created by this invocation. Verify its path, separate identity, absence of components and actual closure. Use CloseRetainUndoMarks to retain other undo marks. If safe closure is unavailable or unconfirmed, report that the copy must be closed manually. Do not report failed Undo as successful rollback; no files are deleted.

Geometry and CAM mode is retained: a model body's history in the copy may become one non-parametric BREP. This requires verified ownership by one body and unchanged body, face and edge identifiers. Parameters with users outside the replaced history may not be removed. A protected Tooling Box may only remap parents to the same body's BREP while retaining its type and expression membership. Other history changes remain failures.

Tooling Box solids are excluded from the general transformation. When needed, edit the existing parametric stock feature and verify its type, offsets and references. Before and after reflection, create API editors for Workpiece, MCS and supported operations, reload available geometry and run Validate. Compare CAM modes and flags exactly; lengths use an absolute tolerance of 1e-8 part units. An error fails the trial and triggers rollback.

MCS transfer retains the existing CSYS. Recognized Three Planes definitions retain the original plane methods and direct selections. Direction correction uses copies of existing Plane definitions; direct Face/DatumPlane inputs use coincident planes. X/Z are reflected and Y completes a right-handed frame. Unknown associative definitions are not replaced by Dynamic. Verify position, axes, inheritance, the dependency graph and direct selections separately. Implicit NX defaults still receive explicit bindings in the copy.

**This is a trial release. V1.16 has been checked outside NX; a complete run, actual Three Planes face selections, saving/reopening and toolpath generation require an NX / Designcenter 2606 test. Run V1.16 from a healthy original project. The copy's body history may become BREP; the script does not save the source part. Other numerical directions and coordinates inside operation settings remain unchanged.**

## Workflow

1. Open a simple test part without components. It must be fully loaded, be both work and displayed part, and have a normal `.prt` file on disk.
2. Save it with the normal NX command. The journal stops if the source has unsaved changes; it does not save the source itself.
3. Use **Tools → Journal → Play** to run the script. Updater entry: **«Зеркальная копия CAM-проекта — тест» V1.16**.
4. In the geometry picker, click one **planar face** or **datum plane**, then click **OK**. No name or coordinate entry is required. Oblique planes are supported. The surface defines the plane, not the cursor position; reversing its normal does not change the reflection. Curved surfaces and objects from another component are rejected. Cancelling selection exits without creating a copy.
5. After selecting the plane, the first unused sibling filename such as `name_MIRROR_TEST_001.prt` is created. Existing files and names of parts still open in NX are not reused. To retest the original model, activate it first: the journal copies the work part at launch, even if that is a previous test copy. Collapsing the suffix changes only the output name, not the source data.
6. Read and share the report using **«Копировать всё» (Copy all)**. If checks pass, inspect bodies, MCS frames and the geometry of every operation. In Three Planes, check the original face choices for each X/Y/Z field, including opening the corresponding plane definition; the Three Planes type and correct coordinates alone are insufficient. Then save the open test copy using the normal NX command and regenerate operations.

**Until manually saved, the reflected result exists only in the open NX session. The file on disk remains the initial copy of the source PRT.**

## Behavior

- Copies the saved PRT byte for byte, verifies checksums and opens a separate part. Source CAM data is not processed.
- Reads operations, MCS/RCS and Part, Blank, Check, Trim, Cut Area, Wall and Drive geometry references in the copy, including collector members and supported boundaries.
- Reflects ordinary bodies and supported referenced geometry through a model-parent transformation. Parametric stock is updated through its existing Tooling Box. Body identifiers and all selected references must remain stable. All faces/edges are checked on ordinary bodies; the body and explicitly selected faces/edges are checked on regenerated stock. Unselected internal stock topology may be regenerated.
- Transfers existing MCS/RCS while retaining supported Three Planes and numerical-Xform CSYS definitions and verifying associativity. X and Z are reflected; Y completes a right-handed frame. Only implicit NX defaults receive new numerical bindings. Resolve inheritance providers through UF; do not overwrite inherited nodes. Verify coordinates, parameter status/provider and the available RCS-to-MCS link flag.
- Transfers user-defined boundary planes without recreating contours. Plane X/Y axes are reflected and Z is reconstructed as the negative of that plane's reflected original Z. MCS Z is transferred separately. The geometric plane and material side should follow the reflection. Parameters and references are checked after update. The copy's WCS is restored to its original position.
- Removes calculated toolpaths in the copy. Does not create mirrored CAM operations, generate paths or postprocess.
- Checks exact oriented object bounds in the selected-plane frame, geometry identifiers, available selected CAM references, operation membership and geometry parents, missing toolpaths and transferred MCS/RCS frames. Finally checks the source file checksum and modification flag.
- On an exception after the Undo mark is created, attempts to undo changes in the copy and reports the stage, error and rollback outcome. After successful rollback, restore the source as work and displayed part. If Undo fails, attempt to restore the original and close only this invocation's copy without saving while retaining other undo marks. If closure is unavailable or unconfirmed, ask the user to close the copy manually. Restoring the source does not delete files or close other parts.

## First-trial limits

- A model body's history in the copy may be replaced with one BREP. Only verified conversions of EXTRUDE, BLEND, BLOCK, CYLINDER, CONE, SPHERE, BREP, IMPORTED_BODY and IMPORT are permitted. Shared ownership by multiple bodies, partial replacement or external users of a removed parameter stop the trial. Stock, sketch and MCS definitions are not covered by this permission.
- Remaining history validation checks feature identities/types, parents and expression identifiers. Formulas are diagnostic; changing a coordinate formula under the same identifier is not treated as parameter loss. This is not complete validation of all CAD values and dependencies. Replacing expression identifiers outside the permitted body history is rejected even when formulas match.
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

All 201 checks outside NX passed, including 31 with a separately compiled C library. Add 12 object-lifetime cases: old Plane removal during ReplaceParameters and update, orientation correction after removal, a stale Python wrapper, nested/shared supports, and loss of an active plane, selection, method or placement. Axis, stock, BREP, CAM and rollback checks remain. These emulate API behavior; they do not validate the NX kernel, actual dialog highlighting or toolpath generation on the user's PRT.

## Later stages — not implemented yet

- Select two planes and automatically construct the mirror plane between them. Parallel planes use their midplane; behavior for nonparallel planes needs a separate decision.
- Component checkboxes: for example, mirror fixtures while leaving vises unmirrored. Component copying, placement and dependencies need separate validation.
- Reuse the Setup Card component-selection window behavior. These features are deferred until the trial without components works correctly.
