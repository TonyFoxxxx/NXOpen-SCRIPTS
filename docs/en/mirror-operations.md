# CAM operation coverage audit — V1.26.01

[Русский](../mirror-operations.md) | **English** · [Guide](mirror-project.md)

Audit date: **2026-11-15**. Baseline: published V1.18, commit `b7c881ce6850206977b249cb199f58ba01e23966`. There was no newer main-branch revision when work started. This environment has neither the DC 2606 SDK nor a running NX / Designcenter 2606. **DC 2606 compatibility and complete operation coverage remain unconfirmed.**

V1.26.01 fixes a confirmed API-selection error: Python Modl has no `AskReferencesOfFeatures` on the user installation. The prototype now reads references and calls its other UF operations through C exports, with preflight and unchanged guards. The V1.26 run established initial On values for all four R1 boundaries but stopped before body changes. No additional operation family is approved. [Evidence and C contracts](mirror-project.md#uf-api-v12601).

V1.26 adds an isolated one-body replacement prototype with mandatory rollback and boundary/member application-data checks, including Tool Position On. The 21 dispatch routes and 31 recognized builders are unchanged. It does not establish full CAM transfer. [Prototype and additional SDK methods](mirror-project.md#replacement-probe-v126).

V1.25.02 queries modeling parents of curves/points when selecting direct reflection targets. Operation coverage is unchanged. The user V1.25.02 report confirms the same failure after the common transform of three bodies and 23 points; target-selection changes did not preserve dependent history. [Report analysis](mirror-project.md#v125-result-in-nx).

Initial builder reads in this report succeeded for 35 operations: SurfaceContour — 4, VolumeBased25DMillingOperation — 4, PlanarMilling — 13, CavityMilling — 2, HoleDrilling — 1, CylinderMilling — 5, ZLevelMilling — 2, TpMultiAxisDeburring — 4. Both CavityMilling operations opened through `CreateCavityMillingBuilder` as `CavityMillingBuilder`. These are observed API classes in this PRT, not a UI-template inventory or confirmation of correctly mirrored settings. The run did not reach final reference checks or operation regeneration.

## What the catalog means

The template name in Create Operation, the `NXOpen.CAM.Operation` object class and its Builder class are different things. A template name cannot determine a factory name, and one operation class does not mean one template. V1.25.02 reports the object name, actual Python class, selected factory and actual Builder class. A user-assigned object name is not presented as its original template name.

| Level | Actual status |
| --- | --- |
| Common project transformation | Transfers checked geometry, supported MCS/RCS, parametric stock and specified boundary planes. References, identities and reflected positions have separate checks. |
| Accessible operation data | Calls `Geometry.InitializeData(True)` and `Validate()` on admitted editors. Reads accessible Part, Blank, Check, Trim, Cut Area, Wall and Drive channels, collectors and boundaries through UF. This is shared processing, without complete family-specific adapters. |
| Editor recognition | Retains 21 operation-class dispatch routes and 31 recognized Builders. Falls back to checked `CreateBuilder` when the specific method is absent. Another 10 editors are recognized by their actual returned class; a specialized factory may return one of them too. |
| Complete reflection of a family's settings | **Confirmed for no family.** In particular, obtaining `CavityMillingBuilder` does not establish that all Cavity Milling levels, directions and moves were transferred. |
| Unknown/excluded families | An unknown Builder stops the trial. The catalog does not admit turning, WEDM, additive, inspection or user-defined motion. |

Reading a Builder does not establish selection preservation: the common UF reference snapshot checks that separately. An absent property, `None` and an unhandled field type are not empty selections; the report lists them as unverified through the Builder. A read error on an existing property or an `InitializeData` failure stops the trial. Losing a previously available field or changing Builder class also stops it. An available UF channel is checked independently of an unavailable Builder field.

## Implemented dispatch routes

This table describes the code, **not signatures verified against SDK 2606**. Method spelling is intentional. Every returned Builder is checked against the catalog, including specialized factories. Factory exceptions are not hidden by retrying with another editor.

| NXOpen object class | OperationCollection method |
| --- | --- |
| `CavityMilling` | `CreateCavityMillingBuilder` |
| `FaceMilling` | `CreateFaceMillingBuilder` |
| `PlanarMilling` | `CreatePlanarMillingBuilder` |
| `Engraving` | `CreateEngravingBuilder` |
| `ZLevelMilling` | `CreateZlevelMillingBuilder` |
| `VariableZLevelMilling` | `CreateVazlMillingBuilder` |
| `FeatureMilling` | `CreateFeatureMillingBuilder` |
| `PlungeMilling` | `CreatePlungeMillingBuilder` |
| `HoleMaking` | `CreateHoleMakingBuilder` |
| `HoleDrilling` | `CreateHoleDrillingBuilder` |
| `ThreadMilling` | `CreateThreadMillingBuilder` |
| `CylinderMilling` | `CreateCylinderMillingBuilder` |
| `VolumeBased25DMillingOperation` | `CreateVolumeBased25dMillingOperationBuilder` |
| `GrooveMilling` | `CreateGrooveMillingBuilder` |
| `ChamferMilling` | `CreateChamferMillingBuilder` |
| `RadialGrooveMilling` | `CreateRadialGrooveMillingBuilder` |
| `GenericFeatureOperation` | `CreateGenericFeatureOperationBuilder` |
| `SurfaceContour` | `CreateSurfaceContourBuilder` |
| `TpMultiAxisDeburring` | `CreateMultiAxisDeburringBuilder` |
| `TpMultiAxisWallFinishing` | `CreateMultiAxisWallFinishingBuilder` |
| `MwBLDeburringOperation` | `CreateMwBldeburringOperationBuilder` |

The catalog additionally recognizes `EdgeChamferBuilder`, `FacingBuilder`, `FacingZigZagBuilder`, `MultiAxisRoughingBuilder`, `PlanarRoughingBuilder`, `PointToPointBuilder`, `RotaryRoughingBuilder`, `SwarfBuilder`, `WallProfilingBuilder` and `ZlevelUndercutBuilder`. In the user's V1.20 report, specialized `CreateSurfaceContourBuilder` returned `PlanarRoughingBuilder` for a `SurfaceContour` object; reading and Validate passed. Complete mappings to actual object classes and DC 2606 templates still need verification. Admitted editors undergo the same common checks, not just a name check.

## Data still needing transformation or verification

| Family | Existing common processing | Unconfirmed data |
| --- | --- | --- |
| Cavity / Planar / Face / Z-level / Plunge and related | Editor opening, accessible geometry, boundaries, MCS/RCS; both custom-point channels for newly admitted families | Manual cut levels/planes, numerical directions and points, engage/retract and transfer moves; meaning of settings after reflection |
| Surface Contour, multi-axis, Swarf, Rotary, Deburring | The same common processing and accessible Drive/Wall/Check references | Complete tool-axis definitions, orientation/tilt, drive geometry outside the read channels, local directions and moves |
| HoleMaking / HoleDrilling / Thread / Cylinder / FeatureMilling / GenericFeature | Common references and editors; full hole/FBM definitions explicitly remain unverified, including generic dispatch without `GetFeatureGeometry` | Feature/hole membership and order, axes and depths, local CSYS, control points, attributes and machining-feature data |
| All 31 Builders | Read accessible custom points; transfer is limited to four classes and cases described in the [guide](mirror-project.md#custom-cam-points). Code 1345005 still marks an unverified channel | Nonzero active depths and other unverified point definitions remain unsupported; unused fields are excluded from validation. Other numerical spatial settings are not reflected; the local 3D-parameter inventory is incomplete |

Existing custom-point query exceptions remain: `SurfaceContour` queries channel 1, `TpMultiAxisDeburring` makes no such UF query, and other operations query channels 0 and 1. These are coverage limits, not proof that those two classes contain no other points; channel applicability requires the 2606 SDK.

V1.25 corrects sketch-curve target selection and adds retained-history/warning guards after the V1.24 report lost sketch-defined Part boundaries in four PlanarMilling operations. Model-edge selections retain body-topology and CAM checks. [Behavior and limits](mirror-project.md#sketch-curves-and-nx-warnings). No additional operation families or complete settings adapters are introduced.

## Items to verify against SDK DC 2606

1. All 21 factories above and `OperationCollection.CreateBuilder(CAMObject)`: Python availability, arguments, actual return classes and editor selection for different templates sharing an operation class. Presence in an older C++ or .NET API does not prove presence in Python 2606.
2. All 31 classes in the [catalog](mirror-project.md#milling-and-hole-making-editor-catalog), inheritance and additional 2606 editors. Map installed templates separately; discovering a name does not authorize adding it to the allowlist.
3. `Geometry.InitializeData(True)`, `Builder.Validate()`, `Builder.Destroy()` and property/descriptor availability, including `dir()` behavior. Verify availability/types of the seven `*Geometry` fields, empty/inactive fields, and `GeometryGroup` modes/offsets. For example, `TrimBoundary` is a separate definition, not a synonym for an absent `TrimGeometry`.
4. `CavityMillingBuilder.CutLevel`, associated levels/planes and moves; current tool-axis and drive-geometry definitions for surface/multi-axis editors. The older `SurfaceContourBuilder.ToolAxis` appears in the deprecated API list; no guessed transfer was implemented.
5. `GetFeatureGeometry()` and `NXOpen.CAM.FBM.FeatureGeometry`, hole/thread/machining-feature definitions: enumeration, references, points/axes, coordinate frames and units. Method availability currently only flags additional unverified data; its contents are not read.
6. Declarations/applicability of `UF_CAMGEOM_ask_items`, `UF_CAMGEOM_ask_geom_provider`, `UF_CAMGEOM_ask_collector_items`, `UF_CAMGEOM_ask_custom_points`; `UF_CAMBND_ask_boundaries`, `UF_CAMBND_ask_boundary_data`, `UF_CAMBND_ask_boundary_items`, `UF_CAMBND_set_boundary_plane`; structures, sizes, ownership and code 1345005 in 2606 headers. Check custom-point channels for SurfaceContour and TpMultiAxisDeburring separately.
7. `UF_PARAM_ask_required_params` / Python `AskRequiredParams`, `AskParamAttributes`, 3D/3DLength/tag/VLA-tag types, `UF_PARAM_ask_param_status`, `UF_PARAM_ask_param_definer`, MCS/RCS indices 59/60 and link index 41. Each transformed parameter needs verified meaning and frame, not just a type.
8. For V1.20's general model-history admission: Python `NXOpen.Features.BodyFeature`, concrete classes from `Body.GetFeatures()`, `BodyFeature.GetBodies()`, `Feature.GetParents()`, `GetExpressions()`, `GetFeatureErrorMessages()`, `Expression.GetUsingFeatures()` and `GetReferencingExpressions()`. Verify completeness for boolean, multi-body, suppressed and user-defined features and history preservation/replacement by `UF_MODL_transform_entities`. [Rules and limits](mirror-project.md#model-feature-handling).
9. For V1.21 points: `UF_CAMGEOM_custom_point_s`, pointer-array layout and individual-record cleanup, `UF_CAMGEOM_ask_custom_points`, `UF_CAMGEOM_delete_custom_points`, `UF_CAMGEOM_append_custom_points`; units/reference frames and zero/nonzero meanings of `upper_depth`, `lower_depth`, `depth`, retained associativity and order after replacement. In particular, verify Planar/Cavity cases where reflection changes ABS Z.

10. For V1.23 CSYS: `XformCollection.CreateXformByPlaneXDirPoint` and `CreateXformByPlaneYDirPoint` — Python signatures, acceptance of a `Plane` object, point-projection and X/Y/Z flag semantics, direct parents (Plane/Direction/Point and numerical Scalar). `Direction.Vector`, `Point.Coordinates`, `Scalar.Value`, `Xform.Scale/Origin/Orientation`, `Plane.Method/GetGeometry()`, `CoordinateSystems.CreateCoordinateSystem`, `SmartObject.Evaluate/ReplaceParameters`, `UF_SO_ask_parents`: evaluation order, retained selected faces/edges and installed CSYS, old Xform lifetime. Verify after update, manual save and reopen. This is an older published contract, not a verified SDK 2606 contract. In the user’s V1.23 report, the X variant with a Plane object passed the implemented checks for one CSYS after update; this does not confirm the Y variant, other definitions or the full SDK contract.

11. For V1.25: `UF_SKET_ask_sketch_of_geom(tag_t, tag_t*)`, its NULL_TAG/error semantics and coverage of sketch curves/points, including extracted/referenced geometry; `Feature.GetFeatureWarningMessages()` Python return type, timing and completeness of NX update warnings. Verify `UF_MODL_transform_entities` plus model update with sketch members excluded from the input array, retained curve/edge tags, sketch dependencies/constraints and CAM boundaries. `UF_SO_is_so` is not proof of no sketch owner. Check the reported `UF_CAM_boundary_type_t` value 2 and boundary application/member data before implementing any restoration; six integer fields do not constitute a complete boundary definition.

12. For the replacement mechanism (V1.26 implements only an isolated prototype): `UF_MODL_replace_features`, `UF_MODL_replace_features_t`, `return_map`, `use_copy_of_replacement`, returned-map ownership and cleanup; `FeatureCollection.CreateReplaceFeatureBuilder`, `UpdateMap`, `SetMatchingForParent`, `MappingMethod`, `DeleteOriginalFeature` and `CopyReplacementFeature`. Verify coverage of intermediate-history references, actual tags before/after, the distinction between modeling dependencies and CAM references, and complete sketch constraints/associative curves. Check the single-face sheet-body restriction on `UF_MODL_replace_body_data` separately; do not use it as a general solid-body replacement. [Alternative analysis](mirror-project.md#investigated-transformation-alternatives).

V1.25 runtime evidence: `UF_SKET_ask_sketch_of_geom` recognized both selected Arc members of one sketch; `GetFeatureWarningMessages()` returned baseline/current data and the new warning messages. This confirms those calls for the observed case, not every sketch/point form or the complete SDK 2606 contract. The general model transform still damaged retained history. A new read of CAM boundaries, their transfer and final geometry/MCS checks were not reached. [NX report result](mirror-project.md#v125-result-in-nx).

V1.22 distinguishes active, unused and unverified fields. The [applicability table](mirror-project.md#custom-cam-points) follows the older UF header and still needs SDK 2606 verification. An active-data read error is not an absent value; active nonzero depths are not rounded.

SDK verification cannot replace an NX run. Each new family needs a test PRT with nonempty family-specific settings, selection/axis inspection, manual save and reopen of the copy, then normal toolpath regeneration and inspection. The source must remain unchanged; the journal neither saves automatically nor creates extra files.

13. V1.26 additionally requires verification of extraction, topology matching, builder map/selection readback and both boundary/member application-data reads. The [separate SDK 2606 table](mirror-project.md#replacement-probe-sdk-2606) lists the exact methods and definitions. No new operation family is automatically approved.

## Sources and verification limits

- [Older Siemens uf_sket.h contract](https://www.ugapi.com/doc/UFun/uf_sket/global.html) — sketch membership; [Feature warning messages](https://docs.tech-plm.ru/documentation/external/PL20230425447184281/en-US/custom_api/nx_open_c_plus_plus_ref/a56513.html) — message order is unspecified; [UF transform contract](https://www.ugapi.com/doc/ufun/uf_trns/global.html) — transforms inputs and their parents. Available search excerpts were reviewed; these are not SDK 2606 sources.

- [Siemens NXOpen XformCollection, NX 2022.1 reference mirror](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.XformCollection.html) — Plane/X(Y)Dir/Point factories; [Direction.Vector](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.Direction.html). V1.23 API sources, not 2606 documentation.

- [Older Siemens uf_camgeom.h reproduced by ugapi](https://www.ugapi.com/doc/UFun/uf_cam/uf_camgeom.html) describes custom-point records, ABS coordinates and list read/replacement functions. This is V1.21's contract source, not SDK DC 2606.

- [Siemens NXOpen OperationCollection, 2023 reference reproduced by Tech PLM](https://docs.tech-plm.ru/documentation/external/PL20230425447184281/en-US/custom_api/nx_open_c_plus_plus_ref/a50101.html) — V1.18's catalog source, not 2606 documentation.
- [CavityMillingBuilder](https://docs.tech-plm.ru/documentation/external/PL20230425447184281/en-US/custom_api/nx_open_c_plus_plus_ref/a48265.html) and [HoleDrillingBuilder](https://docs.tech-plm.ru/documentation/external/PL20230425447184281/en-US/custom_api/nx_open_c_plus_plus_ref/a49073.html) from that reference expose separate CutLevel and FBM definitions. Search excerpts were available during this audit; full Tech PLM pages returned 502.
- [Siemens SurfaceContourBuilder, older reference reproduced by ugapi](https://www.ugapi.com/doc/nxopen/a02024.html) and [deprecated API list](https://www.ugapi.com/doc/NXOpen/a10908.html) provide further reason not to transfer old signatures mechanically to 2606.
- The [official Siemens NX documentation portal](https://docs.sw.siemens.com/en-US/product/209349590) could not be read with the available lookup tool. No SDK 2606 sources were available for exact comparison.

V1.25: **368 regression checks outside NX**, including 53 using test C libraries; 40 new scenarios. Coverage remains 21 dispatch routes / 31 Builders. The user identified the V1.24 boundary members as sketch curves. This release does not certify repaired sketch dependencies, a complete mirrored PRT or SDK 2606 compatibility. The test libraries are not Siemens libraries.

Additional report regressions: **371 focused checks**, including 53 with test C libraries. Three synthetic scenarios preserve rejection of the observed V1.25 failure. V1.25.01 changes only the journal version and corresponding catalog version/SHA-256; the algorithm is unchanged.

V1.25.02: **394 focused checks**, including 61 using C fixtures. The 23 added scenarios cover target selection, modeling-parent reads and dependent-geometry position checks; they provide no new evidence of CAM-family compatibility with actual NX.

After the V1.25.02 report: **395 checks**, with an additional protective-stop regression using three bodies and 23 points whose parent queries return empty. Journal version and catalog are unchanged. Final CAM-boundary retention and operation regeneration remain unconfirmed.
