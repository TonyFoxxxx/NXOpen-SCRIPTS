# Mirrored CAM project — trial

[Русский](../mirror-project.md) | **English**

Script: [`NX_Mirror_Project.py`](../../scripts/NX_Mirror_Project.py). Version: **V1.26**.

<a id="replacement-probe-v126"></a>
## V1.26: one-body replacement prototype

**This experiment always rolls back; it is not a completed mirrored CAM project.** If ordinary body history has retained consumers other than protected Tooling Boxes, the journal automatically selects one body with the largest number of these direct dependencies. An isolated replacement experiment substitutes for the common transformation of original bodies. Without such dependencies, the previous full workflow and its guards remain in place.

1. Before changing the model, read feature references through `AskReferencesOfFeatures`, single-body history ownership, external expression consumers, sketch geometry/expression membership and selected CAM objects. The report links each SKETCH feature tag to its Sketch object and curve tags. Intermediate-history references absent from the final body's faces/edges stop the experiment; another coplanar object is never substituted.
2. `ExtractFaceBuilder` creates a separate non-associative body copy. After independence checks, remove parameters **only from this new body**. Recheck original objects inside the copied PRT after every preparation change.
3. Before reflection, build a complete face/edge bijection using position, underlying surfaces/curves, trimmed topology and face-edge incidence. Missing or ambiguous matches stop processing. Only the new independent body is passed to the raw reflection call.
4. `ReplaceFeatureBuilder` uses the checked explicit map, disabled automatic matching and `MapOnlyObjectsWithModelingDependencies`. After Commit, check original body/face/edge tags, geometric references, retained features and expressions, sketch membership, dependent-curve positions, new warnings, CAM selections, stock settings and MCS reference selections. The API documentation **does not guarantee tag preservation**; changed tags cause rejection.
5. Success and failure both enter the existing copy-only rollback path, restore the source and verify its integrity. This branch does not transfer MCS/stock or delete toolpaths. Even a successful prototype **is not left for saving or CAM regeneration**. If Undo fails, retain the existing attempt to close only this invocation's copy without saving and report the actual outcome.

Curves upstream of the replaced body may remain at their original positions in this limited experiment. Complete sketch constraints, all associative definitions and whole-project transfer remain unproven. The next NX step is to run V1.26 on the saved source without components and share the complete report; do not save the experimental branch's result. The disk file remains the initial PRT copy.

### Part Boundary: groove centerlines

For `R1_CHERN_Z-0.97`, the user confirmed selecting sketch curves drawn along groove centerlines. The Part Boundaries screenshot shows `Select Curve (1)`, `Boundary Type = Closed`, `Tool Side = Outside`, `Plane = Specify`, and a separate `Tool Position = On` for `Member 1`. Substituting model edges would change machining. The successful body-edge selection in `FASKA_1 / 3 Axis Deburring / Specify Edges` on a test part uses a different definition and does not establish preservation of this Planar Mill boundary on the working PRT.

V1.26 reads application data for the boundary and **each member**, using `UF_CAMBND_ask_boundary_app_data` / `UF_CAMBND_ask_item_app_data`. Compare flags and active stock, tolerance, feed, blank-distance and tool-position values separately; `On` is never inferred from `material_side`. Inactive storage becomes `None` only after reading an explicit zero flag. API errors, unknown codes and unwritten flags stop the trial. Existing order/duplicate-reference, six boundary-field and plane checks remain. This is still not a complete boundary, selection-rule or UDE snapshot; the earlier report's unknown `boundary_type=2` is not guessed to mean Closed.

<a id="replacement-probe-sdk-2606"></a>
### Items requiring SDK and NX 2606 verification

| Methods / definitions | Outstanding verification |
| --- | --- |
| `UF.Modl.AskReferencesOfFeatures([tag], 1)` | Python signature and `(parents, names, count)` result, complete intermediate-history references and part ownership. |
| `SketchFeature.Sketch`, `Feature.GetEntities`, `Sketch.GetAllGeometry/GetAllExpressions` | Actual classes/results, associative geometry and complete dependencies; this snapshot does not establish every sketch constraint. |
| `CreateExtractFaceBuilder(None)`, `CreateRuleBodyDumb`, `ExtractBodyCollector.ReplaceRules`, `CommitFeature` | Non-associative extraction without changing originals, independent history, solid/sheet support. |
| `UF.Modl.DeleteObjectParms([tag])` | Removing parameters of the new independent body to obtain one BREP; application to original sketches/stock remains prohibited. |
| `UF.Modl.Compare`, `CompareTopology`, `UFConstants.COMPARE_*` | Signatures, return values, units and accuracy; final face/edge correspondence must be unique. |
| `CreateReplaceFeatureBuilder()`, `SelectFeature/ReplacementFeature.GetArray`, `UpdateMap`, `FeatureReferences.GetMatchedReferences`, `SetMatchingForParent`, `MatchedReferenceBuilder.ReverseDirection`, `Validate/Commit/Destroy` | Complete mapping, original geometry tags/selections, sketch constraints/dependencies and MCS references, behavior of `CopyReplacementFeature=True`. |
| `UF_CAMBND_app_data_t`, both `ask_*_app_data`, `UF_CAM_tool_position_t` | Windows 2606 ABI, flags and applicability to operation families/channels. The older contract has On=1 and Tanto=2; correspondence to this UI requires an actual read. |

Sources are older published Siemens contracts: [ExtractFaceBuilder, NX 2022.1](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.Features.ExtractFaceBuilder.html), [ReplaceFeatureBuilder](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.Features.ReplaceFeatureBuilder.html), [UFModl](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.UF.UFModl.html), [uf_cambnd.h](https://www.ugapi.com/doc/UFun/uf_cam/uf_cambnd.html), [uf_cam.h](https://www.ugapi.com/doc/UFun/uf_cam/uf_cam.html). These are not SDK 2606. **427 focused checks outside NX**, including **67 using separately compiled C fixtures**, exercise the code and rejection scenarios. Real NX prototype execution, full-copy save/reopen and toolpath regeneration remain unconfirmed.

## V1.25 result in NX

The **V1.25.02** report dated 2026-11-15 (`MIRROR_TEST_011.prt`) confirms that target-selection changes **did not fix the failure**. Exactly **three bodies and 23 points** were passed to `UF_MODL_transform_entities`. Every point returned no modeling parents; both selected sketch arcs were excluded from direct input. The common call still damaged the same eight retained features. The new parent query successfully exercised its no-parents branch for these points; returning and freeing a nonempty parent array in actual NX was not tested by this report.

The preceding **V1.25.01** report (`MIRROR_TEST_010.prt`) showed the same failure. Release .01 corrected update detection and .02 addressed a target-selection gap; neither made this PRT work. This is not rejection of an unknown operation or modeling-command type. The individual effects of the 26 inputs have not been isolated: the common call does not prove that one body alone caused the failure or that all points are harmless.

An added regression uses these target categories/counts with the existing history-damage scenario: **395 focused checks outside NX**, including the previous 61 using C fixtures. It verifies rejection before MCS/stock transfer, toolpath deletion and subsequent CAM writes. The scenario uses synthetic objects and does not reproduce the NX kernel. Journal and catalog were unchanged for that analysis. V1.26 now adds the isolated prototype described above. Dependency transfer with exact geometric references still needs implementation; another command allowlist extension or permission to lose dependencies would not solve it.

**The user PRT still fails.** In the 15 November 2026 report, both selected `Arc` objects were recognized as members of one sketch and excluded from separate transformation. The common `UF_MODL_transform_entities` call nevertheless changed eight retained features: four Datum CSYS parent references, three sketch definitions and one CPROJ definition. Two sketches lost expressions; other parent dependencies were removed. New warnings include lost point parents, under-constrained sketches and failed associative curves. One sketch acquired a warning without a structural snapshot change.

The early history guard stopped execution before MCS/stock transfer and toolpath deletion. The report records successful Undo, return to the source, an unchanged source-file checksum and a clear source modification flag. The final CAM-boundary comparison was not reached, so this run does not establish whether the four boundaries lost in V1.24 were retained or lost again. It confirms damage during the common model transformation; the responsible individual target within that call has not been isolated.

Merely accepting the four Datum CSYS parent remaps would leave three damaged sketches and a damaged projection. These changes are outside the authorized body-history-to-BREP conversion. The current transfer mechanism remains unsuitable for this dependency graph. A replacement must preserve geometry identifiers, selected objects and sketch dependencies; full successful mirroring has not been established.

Three additional regressions reproduce this failure pattern with synthetic objects, including both excluded arcs and the warning-only sketch. All **371 focused checks outside NX** pass, including 53 using test C libraries. These are failure-guard checks, not reproduction or repair of the NX kernel. The initial analysis was published without changing the journal. **V1.25.01** subsequently updates the version and checksum for updater detection; the algorithm and reported failure remain unchanged.

## Investigated transformation alternatives

This investigation uses available older NXOpen/UF contracts, **not the 2606 SDK**. The analysis below predates V1.26; only the isolated builder experiment above has now been added:

| API / approach | Established behavior | Why it is not a ready fix |
| --- | --- | --- |
| `UF_MODL_transform_entities` | Transforms input objects and their parents. In the V1.25 report, retained history is damaged even with both arcs excluded. | A different dependency-transfer mechanism is needed; weakening checks does not repair it. |
| `UF_MODL_replace_body_data` | Replaces an unparameterized feature's body with a copy of another body. Both must be single-face sheet bodies. | It is not a general BREP editor for a solid part. |
| `UF_MODL_replace_features` | Provides an edge/face correspondence map and reparents child features. With `return_map=true`, it returns downstream references needing mapping without performing replacement. | This map does not promise CAM coverage. The description also does not guarantee preservation of body/face/edge tags. |
| `ReplaceFeatureBuilder` | Provides explicit reference matching. The older reference marks `MapAllObjects` as unsupported. | Modeling dependencies cannot be treated as a complete map of selected CAM geometry. Verification against 2606 is required. |
| `MirrorBodyBuilder`, `UF_BREP_make_body` | Create a mirrored/new body. | Creation alone does not preserve existing identifiers or sketch references. |

The V1.26 prototype follows **explicit dependency capture and matching before changing the body**. First, obtain every used reference, including intermediate-history entities, and match each to its exact reflected counterpart. A reference absent from the final body cannot be assumed unnecessary; another coplanar face is not an equivalent selection, and ambiguous matches must not be chosen silently. Then verify preservation of identifiers and sketch/projection definitions through history replacement. Only after those checks may the existing MCS, stock and CAM stages proceed. Reparenting one reference to a BREP without checking the rest of the sketch definition is insufficient.

Open requirements before full-project use include the actual DC 2606 API behavior, identifiers after replacement, access to intermediate references, and complete transfer of sketch constraints/associative geometry. The experimental call is guarded by explicit mapping, post-change checks and mandatory rollback; it does not establish a production transfer. There is no ready fix established by the available offline checks; further development does not require the user to provide a manual mirroring method.

Sources: [older Siemens `uf_modl.h` header](https://github.com/feifeiwang/NXOPEN/blob/main/%E5%AE%98%E6%96%B9%E6%96%87%E6%A1%A3/%E7%A8%8B%E5%BA%8F%E4%B8%8E%E5%AE%98%E6%96%B9%E7%A4%BA%E4%BE%8B/uf_modl.h), [ReplaceFeatureBuilder](https://www.ugapi.com/doc/nxopen/a03024.html), [MirrorBodyBuilder, NX 2022.1](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.Features.MirrorBodyBuilder.html). These are published copies of contracts, not proof of kernel behavior or 2606 compatibility.

## Latest release

Installation requires updater **V1.20**. If yours is older, update and restart it; then check the catalog and install **V1.26**. [Updater upgrade](updater.md#upgrading-to-v120).

**V1.26 · 2026-11-15** — Add an isolated one-body history replacement prototype with an explicit reference map and mandatory rollback. Independently check boundary/member application data, including Tool Position On. This is an experimental replacement of the failing stage, not a confirmed fix for the working PRT. [Prototype workflow and limits](#replacement-probe-v126).

**V1.25.02 · 2026-11-15** — Address curve/point selection outside sketches and UF Smart Objects: query modeling parents through `UF_MODL_ask_entity_parents` before separate transformation. Exclude dependent geometry from direct input while retaining position, parent and CAM-reference checks. Selected Smart curves/points now also require reflected-position verification. Report the exact direct transformation targets. All **394 focused checks outside NX** pass, including 61 using test C libraries; 23 scenarios were added. This is a limited target-selection correction, not a confirmed fix for the history damage reported in V1.25.01.

### V1.25.01

**V1.25.01 · 2026-11-15** — Release V1.25.01 so the updater recognizes an update from V1.25. Align the journal header and version constant, catalog and SHA-256. The mirroring algorithm is unchanged; the known sketch/projection dependency damage reported for V1.25 is not fixed. Small publications within the same main revision use .01, .02 and subsequent suffixes; the working filename remains NX_Mirror_Project.py.

### V1.25

**V1.25 · 2026-11-15** — Fix reflection target selection: query curve/point sketch membership independently of UF Smart Object status. Exclude sketch geometry from separate transformation, then verify its reflected bounds, original sketch membership and CAM references. Edges move with their bodies. Add an early stop for damaged retained history and checks for new NX warnings. All 368 focused checks outside NX passed, including 53 using test C libraries and 40 new scenarios. The fix remains unverified on the user PRT; safe transfer of every dependent sketch is not claimed. [Details](#sketch-curves-and-nx-warnings).

### V1.24

**V1.24 · 2026-11-15** — Diagnostic revision after V1.23 stopped while comparing CAM references. Replace the generic error and last-read operation label with the actual changed node, channel, field and before/after values. Keep the existing exact snapshot comparisons; add no tolerance, normalization or skipped differences. All 328 focused checks outside NX passed, including 50 using test C libraries and 21 new diagnostic scenarios. The cause of the user PRT’s CAM-data change remains unknown; a V1.24 report is needed. [Diagnostic details](#cam-difference-diagnostics).

### V1.23

**V1.23 · 2026-11-15** — Add transfer for a recognized associative plane–X/Y direction–point CSYS after V1.22 stopped at MCS “2”. Retain the existing CSYS, Plane/Direction/Point objects and their selections; set orientation flags on a new Xform. Verify the candidate before replacement and the installed definition after replacement, model update and CAM reads. Keep Y′ = R(Y), Z′ = R(Z), X′ = Y′ × Z′. All 307 focused checks outside NX passed, including 50 using test C libraries; 22 new scenarios exercise the definition and protection failures. SDK 2606 compatibility and a complete NX result remain unconfirmed. [Definition and limits](#plane-direction-point-mcs).

### V1.22

**V1.22 · 2026-11-15** — Fix V1.21's false stop on an unused custom-point depth field. For Planar/Cavity, check upper_depth/lower_depth for Cut Region Start and depth for Predrill Engage; Face and valid Surface Contour points use no depth fields. Unused values are neither loaded as numbers nor compared as settings. Active nonzero depths, however small, still stop the trial. All 285 checks outside NX passed, including 50 using test C libraries. [Field applicability table](#custom-cam-points).

### V1.21

**V1.21 · 2026-11-15** — Add limited custom CAM point transfer after V1.20 stopped on a PlanarMilling Cut Region Start point. Point references retain their selection; numerical points reflect in ABS while preserving order. Check coordinates after model updates and final CAM editor reads. Nonzero depths and unverified cases stop the trial with recorded data. All 275 checks outside NX passed, including 43 using test C libraries. [Supported cases and limits](#custom-cam-points).

### V1.20

**V1.20 · 2026-11-15** — Remove both model-feature name catalogs after V1.19 stopped on `DELETE FACE`. All history features undergo common validation. Body history may become BREP based on `NXOpen.Features.BodyFeature` membership, single-body ownership, retained topology and expression dependencies. Modeling command names no longer determine admission. Retain Tooling Box, MCS, sketch and CAM protection. All 244 checks outside NX passed, including 16 new tests. This does not establish that every modeling command works in DC 2606; [methods and definitions needing SDK verification](mirror-operations.md) are listed separately.

### V1.19

**V1.19 · 2026-11-15** — Check Builder classes for specialized factories as well, including CavityMilling. Changed editors, lost fields and read errors stop the trial. Include operations missing from the Geometry traversal. Distinguish editor recognition, accessible geometry reads and unverified definitions in the report. Flag complete hole/FBM data regardless of dispatch route. Preserve NX/API codes, tracebacks and source-integrity diagnostics on failure. All 228 checks outside NX passed; retain the 31-Builder catalog. [Family coverage, sources and separate SDK 2606 verification list](mirror-operations.md).

### V1.18

**V1.18 · 15.11.2026** — Expand NXOpen milling and hole-making families. Use specialized editors for known classes, otherwise OperationCollection.CreateBuilder with an explicit concrete-editor catalog. Do not automatically accept turning, WEDM, additive, inspection or user-defined motion. Check available geometry, Validate and custom points; report complete hole/FBM definitions separately as unverified. Retain the V1.17 axis convention. All 210 checks outside NX passed; actual projects still require verification and regenerated toolpaths.

### V1.17

**V1.17 · 2026-11-14** — change MCS/RCS orientation after the NX test: V1.16 retained selected faces but introduced an additional Y reversal. Y and Z now follow the geometric reflection, and X completes a right-handed frame: `Y′ = R(Y)`, `Z′ = R(Z)`, `X′ = Y′ × Z′ = −R(X)`.

For reflection across X=0 and setups aligned with ABS, the original X/Y/Z directions remain unchanged, including a lower setup with negative Y and Z. Reflect the origin: for example, `(60, −25, 16)` becomes `(−60, −25, 16)`. For an oblique plane, Y and Z change according to that plane's reflection. Relative to V1.16, both transverse axes X/Y reverse while Z keeps its direction.

Apply the same rule to Three Planes, numerical CSYS/Xform, the root MCS and final verification. Retain Three Planes faces and construction methods, including V1.16's checks of active objects after old Plane cleanup. The report shows the transfer rule and original/verified final ABS axes. Boundary planes keep their separate material-side transformation rule.

An existing Plane now uses `CopyPlane()` to copy its definition with the same construction method and directly selected geometry. No additional new-plane-to-old-plane link is introduced. When the original input is directly a Face or DatumPlane, one coincident plane references that object directly. Intentionally selected intermediate planes are not flattened to their underlying faces.

Freeze the method and ordered direct selections before reflection. Check them during creation and orientation, after installing the definition, after model update and at final verification. Missing selections, a different face on the same geometric plane, or a changed construction method fail the trial even when MCS coordinates match. The report lists direct X, Y and Z selections separately. A copied definition must be a separate object in the same part; the original Plane is not used as a mutable copy.

Stock/MCS dependency ordering remains: evaluate original linked supports from parents to children and verify their placement before creating the CSYS. Dependency cycles stop before reflection.

If Undo fails, attempt to restore the original project and close without saving only the copy created by this invocation. Verify its path, separate identity, absence of components and actual closure. Use CloseRetainUndoMarks to retain other undo marks. If safe closure is unavailable or unconfirmed, report that the copy must be closed manually. Do not report failed Undo as successful rollback; no files are deleted.

Geometry and CAM mode is retained: a model body's history in the copy may become one non-parametric BREP. This requires verified ownership by one body and unchanged body, face and edge identifiers. Parameters with users outside the replaced history may not be removed. A protected Tooling Box may only remap parents to the same body's BREP while retaining its type and expression membership. Other history changes remain failures.

Tooling Box solids are excluded from the general transformation. When needed, edit the existing parametric stock feature and verify its type, offsets and references. Before and after reflection, create API editors for Workpiece, MCS and supported operations, reload available geometry and run Validate. Compare CAM modes and flags exactly; lengths use an absolute tolerance of 1e-8 part units. An error fails the trial and triggers rollback.

MCS transfer retains the existing CSYS. Recognized Three Planes definitions retain the original plane methods and direct selections. Direction correction uses copies of existing Plane definitions; direct Face/DatumPlane inputs use coincident planes. Y/Z are reflected and X completes a right-handed frame. Unknown associative definitions are not replaced by Dynamic. Verify position, axes, inheritance, the dependency graph and direct selections separately. Implicit NX defaults still receive explicit bindings in the copy.

**This is a trial release. V1.25 has been checked outside NX. The user confirmed V1.16's face selections but reported an unwanted Y reversal. The new orientation, saving/reopening and toolpath generation require an NX / Designcenter 2606 test. Run V1.25 from a healthy original project. The copy's body history may become BREP; the script does not save the source part. Other numerical directions and coordinates inside operation settings remain unchanged.**

## Workflow

1. Open a simple test part without components. It must be fully loaded, be both work and displayed part, and have a normal `.prt` file on disk.
2. Save it with the normal NX command. The journal stops if the source has unsaved changes; it does not save the source itself.
3. Use **Tools → Journal → Play** to run the script. Updater entry: **«Зеркальная копия CAM-проекта — тест» V1.26**.
4. In the geometry picker, click one **planar face** or **datum plane**, then click **OK**. No name or coordinate entry is required. Oblique planes are supported. The surface defines the plane, not the cursor position; reversing its normal does not change the reflection. Curved surfaces and objects from another component are rejected. Cancelling selection exits without creating a copy.
5. After selecting the plane, the first unused sibling filename such as `name_MIRROR_TEST_001.prt` is created. Existing files and names of parts still open in NX are not reused. To retest the original model, activate it first: the journal copies the work part at launch, even if that is a previous test copy. Collapsing the suffix changes only the output name, not the source data.
6. **If the one-body prototype ran, it must roll back; share the report and do not save an experimental result.** The following inspection/save steps apply only to a successful full-project branch. Read and share the report using **«Копировать всё» (Copy all)**. If checks pass, inspect bodies, MCS frames and the geometry of every operation. In Three Planes, check the original face choices for each X/Y/Z field, including opening the corresponding plane definition; the Three Planes type and correct coordinates alone are insufficient. Check Y and Z directions in both setups against the model and the report's ABS-axis lines. Then save the open test copy using the normal NX command and regenerate operations.

**Until manually saved, the reflected result exists only in the open NX session. The file on disk remains the initial copy of the source PRT.**

## Behavior

The full-project branch is described below. The dependency-driven V1.26 prototype stops earlier and always rolls back; see [its separate workflow](#replacement-probe-v126).

- Copies the saved PRT byte for byte, verifies checksums and opens a separate part. Source CAM data is not processed.
- Reads operations, MCS/RCS and Part, Blank, Check, Trim, Cut Area, Wall and Drive geometry references in the copy, including collector members and supported boundaries.
- Reflects ordinary bodies and supported referenced geometry through a model-parent transformation. Parametric stock is updated through its existing Tooling Box. Body identifiers and all selected references must remain stable. All faces/edges are checked on ordinary bodies; the body and explicitly selected faces/edges are checked on regenerated stock. Unselected internal stock topology may be regenerated.
- Transfers existing MCS/RCS while retaining supported Three Planes and numerical-Xform CSYS definitions and verifying associativity. Y and Z are reflected; X completes a right-handed frame. Only implicit NX defaults receive new numerical bindings. Resolve inheritance providers through UF; do not overwrite inherited nodes. Verify coordinates, parameter status/provider and the available RCS-to-MCS link flag.
- Transfers user-defined boundary planes without recreating contours. Plane X/Y axes are reflected and Z is reconstructed as the negative of that plane's reflected original Z. MCS Z is transferred separately. The geometric plane and material side should follow the reflection. Parameters and references are checked after update. The copy's WCS is restored to its original position.
- Removes calculated toolpaths in the copy. Does not create mirrored CAM operations, generate paths or postprocess.
- Checks exact oriented object bounds in the selected-plane frame, geometry identifiers, available selected CAM references, operation membership and geometry parents, missing toolpaths and transferred MCS/RCS frames. Finally checks the source file checksum and modification flag.
- On an exception after the Undo mark is created, attempts to undo changes in the copy and reports the stage, error and rollback outcome. After successful rollback, restore the source as work and displayed part. If Undo fails, attempt to restore the original and close only this invocation's copy without saving while retaining other undo marks. If closure is unavailable or unconfirmed, ask the user to close the copy manually. Restoring the source does not delete files or close other parts.

## Plane-direction-point MCS

V1.23 recognizes the direct `CSYS → Xform → Plane/Face/DatumPlane + Direction + Point + Scalar` structure. Require exactly one numerical Scalar equal to 1 and unit Xform scale. The original projected direction must unambiguously match the original CSYS X or Y axis, the plane normal must match Z up to sign, and the projected point must match the origin. Also check the original Xform origin and axes. This adapter does not authorize ambiguous, additional or parameterized inputs.

Update supports in dependency order, including stock, before rebuilding. Check the reflected plane, full direction vector and point coordinates. Build a candidate CSYS through `CreateXformByPlaneXDirPoint` or `CreateXformByPlaneYDirPoint` using **the same** Plane/Direction/Point. Do not reverse shared directions or planes; change only the new Xform's axis flags. Retain the Plane method and ordered direct selection, Direction/Point graphs and selected faces/edges. A different coplanar selected face is rejected.

The candidate must confirm the expected origin and all three axes before `ReplaceParameters`. Retain the existing CSYS tag and CAM inheritance after replacement; check its installed Xform, direct supports, ownership by the copy and numerical scale. Repeat checks after model update and final CAM reads. Late drift, missing supports or changed selections stop the trial. Old Xform/Scalar objects may be retired normally; Plane/Direction/Point must remain live. There is no Dynamic fallback.

This is a limited adapter for a recognized structure, not every CSYS definition. A numerical or other direction that does not follow reflection cannot silently count as transferred. An available factory and passing API-model tests do not establish its Python DC 2606 behavior. [Methods requiring SDK verification](mirror-operations.md#items-to-verify-against-sdk-dc-2606).

## Custom CAM points

For `PlanarMilling`, `CavityMilling`, `FaceMilling` and `SurfaceContour`, read point references, ABS coordinates and applicable depth fields. Retain a selected `Point` and include it in checked geometry; transform a shared point once. Associative points must follow their defining geometry. Reflect numerical points without references in ABS by replacing the same channel's list, preserving order and other entries' references. Do not reflect coordinates twice if NX already moved them; unexpected motion is an error.

Determine applicable fields from the operation class and channel using the available older `uf_camgeom.h`, rather than the numerical magnitude:

| Operation class | Channel | Used depth fields |
| --- | --- | --- |
| PlanarMilling / CavityMilling | 0 — Predrill Engage | `depth` |
| PlanarMilling / CavityMilling | 1 — Cut Region Start | `upper_depth`, `lower_depth` |
| FaceMilling | 0 and 1 | None |
| SurfaceContour | 1 — Cut Region Start | None |

Do not interpret other fields in these records as numbers or include them in the settings snapshot; the report marks them `<не используется>` (unused). Unknown applicability is marked `<не проверено>` (unverified) and does not authorize transfer of a detected nonempty list. Unused storage may contain nonzero values or NaN; this is no reason to round active parameters. For writing, initialize a new C structure and assign only applicable fields; do not zero meaningful depths. Unknown families and unverified combinations are not automatically admitted.

Active Planar/Cavity fields still require exact zero; even a nonzero value around `1e-310` stops the trial. Missing or nonfinite active values also fail. The plane must additionally be parallel to ABS Z: zero is not assumed to disable a depth constraint. Face and valid Surface Contour points also support oblique planes. The meaning/reference frame of nonzero depths in SDK 2606 remains unverified.

After transfer, verify record count/order, original references/active depths and each point's expected coordinates. Repeat verification after final CAM editor reads; do not silently repair late motion. Write failures, including append failure after list deletion, propagate to the existing copy rollback. Channels unavailable with code 1345005 remain unverified rather than empty.

## Model-feature handling

The `Part.Features` traversal covers every type, including `DELETE FACE`, without an allowlist of `FeatureType` names. Read each feature's errors, parents and expressions before reflection; detected external references stop the trial. Compare the entire history after transformation, including features outside body histories.

For a permitted BREP conversion, original features must be `NXOpen.Features.BodyFeature` instances, occur in `Body.GetFeatures()`, and belong exclusively to the checked body according to `GetBodies()`. Individual command names are not used for admission. Freeze expression inventories before reflection; ownership or parameter-consumer read errors are not empty data. The existing NX transformation must retain the same body, faces and edges and leave one new BREP without parents or expressions. The journal does not invoke face-deletion editors or separately remove history.

Other features remain monitored and must retain their checked definitions. Sketches, datum/MCS features, groups and protected Tooling Box features receive no permission to disappear as body history. Partial replacement, shared ownership across bodies and parameter consumers outside the replaced history still stop the trial. Tooling Box retains only the existing permission to remap its parents exactly to that same body's BREP.

This is a general validation mechanism, not confirmed support for every NX command. SDK 2606 must still verify the Python `BodyFeature` class, concrete classes returned by `Body.GetFeatures()`, and completeness of `GetBodies()`, `GetParents()`, `GetExpressions()`, `GetUsingFeatures()` and `GetReferencingExpressions()`, including boolean, multi-body, suppressed and user-defined features. Error reporting through `GetFeatureErrorMessages()` and history replacement by `UF_MODL_transform_entities` also need verification in real NX.

## First-trial limits

- A model body's history in the copy may be replaced with one BREP based on common BodyFeature class, ownership and dependency checks, without a catalog of command names. Shared ownership by multiple bodies, partial replacement or external users of a removed parameter stop the trial. Stock, sketch and MCS definitions are not covered by this permission.
- Remaining history validation checks feature identities/types, parents and expression identifiers. Formulas are diagnostic; changing a coordinate formula under the same identifier is not treated as parameter loss. This is not complete validation of all CAD values and dependencies. Replacing expression identifiers outside the permitted body history is rejected even when formulas match.
- Three Planes rebuilding supports recognized mutually perpendicular support planes with unit scale. A CSYS backed by one numerical Xform is also supported with unit scale and matching initial origin/axes. An unconfirmed definition or damaged support stops the trial. Other associative CSYS definitions are accepted only if native updates retain their definition and produce the correct reflected frame. A limited [Plane / Direction / Point adapter](#plane-direction-point-mcs) is also provided.
- Some parametric stock cannot reach the reflected position without changing its settings. If original orientation, asymmetric offsets or dependencies prevent exact reflection, verification stops the trial. The journal does not switch stock to From Geometry or zero its offsets.
- All model features undergo validation without a name filter. This does not guarantee that every feature reflects successfully: an unknown class receives no permission to lose its history; unavailable data and detected external parents stop the trial.
- Components and non-associative boundaries without geometry references remain unsupported. Custom points have [separate limits](#custom-cam-points); other numerical spatial operation parameters still remain unreflected.
- Other numerical spatial operation parameters are not reflected. The report lists detected local nonzero 3D parameters for inspection; this is not a complete inventory of spatial settings. The journal does not edit feeds, allowances or strategies.
- Preserving references and bounds does not prove machining correctness. Selected regions, directions, engage/retract moves and regenerated paths must be checked before use on production projects.
- Requires Windows and the installed NX UF libraries. Geometry channels unavailable with code 1345005 are listed as unverified. Their contents are not confirmed; successful checks on available references do not establish full coverage of all regions. Other API errors or a failed transformation stop the trial.

## Sketch curves and NX warnings

### Modeling parents of curves and points — V1.25.02

Absence of sketch membership and UF Smart Object status is insufficient to authorize a separate transformation. Such curves/points now use `UF_MODL_ask_entity_parents`. Objects with returned parents are expected to follow model updates and are excluded from direct input to `UF_MODL_transform_entities`. An unavailable function, API error or invalid result stops the trial; it never means no parents. Modeling-command names do not control this decision.

Every selected curve/point, including Smart Objects, remains in reflected-bounds verification. For objects read by the new query, parent tags are checked again after final CAM/MCS reads; repetitions matter, while array order is not treated as an axis selection. Lost/replaced parents and newly acquired parents on previously independent objects fail. Parents are not automatically remapped to a new BREP. Existing exact CAM-object and ordered MCS-selection checks remain.

Immediately before the common transformation, the report lists the exact input tags and NXOpen classes. These are call inputs, not a complete list of affected modeling parents. Without them, the previous report cannot isolate the object responsible for the history damage. Excluding a dependent curve does not establish that transforming its body is safe: the early stop for the eight changes reported in V1.25/V1.25.01 remains. Geometry that fails to move through the model also fails verification.

**Items requiring the SDK and actual DC 2606:**

| Method / definition | Required verification |
| --- | --- |
| `UF_MODL_ask_entity_parents` in `uf_trns.h` | Export, 32-bit `tag_t`, one-byte `logical`, `int` count and `tag_t*` array. Meaning and completeness of parents for projected curves, points and intermediate-feature geometry. |
| `found_parent` / `UF_free` | The available contract only defines the count/array and transfers ownership when `found_parent=true`. On successful false, neither is read or freed. The C fixture tests this contract implementation, not the installed NX ABI. |
| `UF_SO_is_so`, `UF_SKET_ask_sketch_of_geom` | Combined coverage of this PRT's geometry; absence of one dependency kind does not exclude another. |
| `UF_MODL_transform_entities`, dependency update | Transformation of remaining direct targets while retaining history, warnings, CAM boundaries and reflected positions of every excluded curve/point. The new query does not reconstruct sketch definitions. |

New-query source: [copy of Siemens `uf_trns.h`](https://github.com/feifeiwang/NXOPEN/blob/main/%E5%AE%98%E6%96%B9%E6%96%87%E6%A1%A3/%E7%A8%8B%E5%BA%8F%E4%B8%8E%E5%AE%98%E6%96%B9%E7%A4%BA%E4%BE%8B/uf_trns.h). This is an older contract; the development environment lacks SDK 2606. No `RemoveParametersBuilder`, sketch-associativity break or replacement of selected curves with new ones is introduced.

### Retained V1.25 checks

The V1.24 report showed lost Part boundaries in four PlanarMilling operations. The user confirmed sketch-curve selections; model-edge selections remain an intended case too. History snapshots immediately after reflection showed lost sketch parents and expressions, and the NX update report warned about lost point parents and an under-constrained sketch. These are unacceptable retained-history changes. V1.25 confirmed exclusion of both selected arcs, but this was insufficient: the common transformation still damaged dependencies. The individual input object responsible for that damage has not been isolated.

`UF_SKET_ask_sketch_of_geom` is queried for selected curves and points before the general transform. A successful `NULL_TAG` means no sketch; an unavailable function or API error stops the trial. `UF_SO_is_so` alone does not establish independence. A sketch curve/point is expected to follow model updates, retains its tag and remains in reflected-bounds verification. Its original sketch membership is checked after final CAM/MCS reads. Unmoved geometry, lost membership or transfer to another sketch fails. Edges are excluded from separate targets; existing body/face/edge and exact CAM-selection checks remain.

Immediately after reflection, check original features outside the one-body history-conversion candidates. A lost feature or changed type, parents or expression identifiers stops work before MCS/stock transfer and toolpath deletion. This uses ownership and class, without modeling-command name lists. Protected stock completes its existing update and validation stages. The early check does not authorize BREP conversion: full ownership, topology and external expression-consumer checks remain mandatory.

Read `Feature.GetFeatureWarningMessages()` at history checkpoints. Report baseline warnings separately; record added/removed messages with their stage and feature. Message order is insignificant; multiplicity is retained. Unavailable/unread data never means an empty list. Warnings new relative to the baseline stop the trial before reflection, after model update and during final history verification. A transient warning that clears before validation stays in the report without being declared a persistent failure. These queries introduce no additional model update.

For each original CAM boundary, report every selected object's NXOpen class and tag in the original order, including duplicates, along with the numerical boundary fields. Do not invent a meaning for the user report's `boundary_type=2`; it requires SDK 2606 verification. Lost boundaries are not recreated from an incomplete snapshot.

This is a limited target-selection correction and stronger validation, not a universal sketch adapter. No sketch dependency/constraint reconstruction, parameter removal or replacement with new curves is added. If body reflection itself damages sketches, the trial stops through the existing rollback path. These checks do not certify the complete sketch constraint definition.

## CAM difference diagnostics

V1.24 retains exact comparisons of the initial and repeated snapshots. On mismatch, report the name/class and tag of the actual changed CAM node, the Part/Blank/Check/Trim/Cut Area/Wall/Drive or Predrill Engage/Cut Region Start channel, and the boundary number where relevant. The stage identifies the comparison and first changed node, rather than the last completed geometry query.

Differences include read state (`read`, `provider`, `unavailable`), provider tag, counts and changed reference positions, ordered boundary members, and named `plane_type`, `boundary_type`, `material_side`, `ignore_holes`, `ignore_islands`, `ignore_chamfers` parameters. For custom points, show the record number, `Point tag`, `upper_depth`, `lower_depth`, `depth`. Missing records, unavailable channels and readable empty lists stay distinct. Tiny nonzero numbers are not rounded; depth-snapshot `None` remains a separate unused/unverified-field value.

Compare at the same four checkpoints: before and after custom-point transfer, before boundary-plane transfer, and final verification after CAM reads. Never replace the initial snapshot with the current one. Reflected point coordinates retain their separate check; expected coordinate motion is not a reference change. A read failure keeps its original NX/API code, traceback and read stage.

Build diagnostics in memory from captured snapshots and names, without querying NX objects again while describing differences. For readability, show the first 80 changed records and first 16 changed positions per sequence; explicitly report the full counts when abbreviated. Only text is limited: every record and position still participates in verification. Do not sort selections or remove duplicate references. A mismatch follows the existing failure/rollback path; failed preliminary checks prevent point or boundary writes.

The V1.23 report passed the previous MCS “2” stop and both Three Planes rebuild checks, then found a CAM-snapshot mismatch. It contains no actual differences, so the affected operation cannot be established and the last deburring-operation Drive read cannot be blamed. V1.24 repairs this diagnostic gap; **it does not establish that the CAM-data change itself is fixed**. Retain geometry/MCS transfer, stock/history protection, rollback rules and the absence of automatic saving.

## Files and validation

V1.26: **427 focused checks outside NX**, including **67 C-fixture checks**. The 32 additions exercise explicit mapping, history/selection/position guards, mandatory rollback and boundary-member application data. API models and the independent C library do not establish NX or SDK 2606 compatibility. The next real-NX check is the isolated prototype, not full-copy saving or toolpath generation.

Only the requested PRT copy is created. The journal writes no reports, INI files, logs, archives, temporary files or `.dlx` resources. It does not call Save, SaveAs or SaveAll. The report is held in memory and includes names, coordinates and the copy path. Nothing is sent automatically. A newly created PRT copy may remain on disk after a failed trial.

The V1.25 release passed 368 focused checks outside NX, including 53 using separately compiled test C libraries. Its 40 new scenarios cover sketch target selection, independent position/membership guards, retained-history loss, baseline/new/transient warnings, unreadable API results and boundary-member diagnostics. Three later report regressions bring the count to 371. The user V1.25 report confirms that the current transfer still fails on retained history; successful mirroring, manual save/reopen and toolpath regeneration remain unconfirmed. API-model tests and C fixtures do not establish NX compatibility.

## Later stages — not implemented yet

- Select two planes and automatically construct the mirror plane between them. Parallel planes use their midplane; behavior for nonparallel planes needs a separate decision.
- Component checkboxes: for example, mirror fixtures while leaving vises unmirrored. Component copying, placement and dependencies need separate validation.
- Reuse the Setup Card component-selection window behavior. These features are deferred until the trial without components works correctly.

## Milling and hole-making editor catalog

These are API classes, not operation-template names in the NX menu. This catalog does not establish complete coverage of new Designcenter 2606 templates. Unknown editors stop the trial. Numerical spatial operation settings still remain unchanged.

`CavityMillingBuilder`, `ChamferMillingBuilder`, `CylinderMillingBuilder`, `EdgeChamferBuilder`, `EngravingBuilder`, `FaceMillingBuilder`, `FacingBuilder`, `FacingZigZagBuilder`, `FeatureMillingBuilder`, `GenericFeatureOperationBuilder`, `GrooveMillingBuilder`, `HoleDrillingBuilder`, `HoleMakingBuilder`, `MultiAxisDeburringBuilder`, `MultiAxisRoughingBuilder`, `MultiAxisWallFinishingBuilder`, `MwBLDeburringOperationBuilder`, `PlanarMillingBuilder`, `PlanarRoughingBuilder`, `PlungeMillingBuilder`, `PointToPointBuilder`, `RadialGrooveMillingBuilder`, `RotaryRoughingBuilder`, `SurfaceContourBuilder`, `SwarfBuilder`, `ThreadMillingBuilder`, `VazlMillingBuilder`, `VolumeBased25DMillingOperationBuilder`, `WallProfilingBuilder`, `ZLevelMillingBuilder`, `ZlevelUndercutBuilder`.

[NXOpen OperationCollection reference (Siemens reference reproduced by Tech PLM)](https://docs.tech-plm.ru/documentation/external/PL20230425447184281/en-US/custom_api/nx_open_c_plus_plus_ref/a50101.html).

Detailed [coverage table and methods requiring SDK 2606](mirror-operations.md). No family is claimed to have all its settings fully transferred.
