# CAM operation coverage audit — V1.19

[Русский](../mirror-operations.md) | **English** · [Guide](mirror-project.md)

Audit date: **2026-11-15**. Baseline: published V1.18, commit `b7c881ce6850206977b249cb199f58ba01e23966`. There was no newer main-branch revision when work started. This environment has neither the DC 2606 SDK nor a running NX / Designcenter 2606. **DC 2606 compatibility and complete operation coverage remain unconfirmed.**

## What the catalog means

The template name in Create Operation, the `NXOpen.CAM.Operation` object class and its Builder class are different things. A template name cannot determine a factory name, and one operation class does not mean one template. V1.19 reports the object name, actual Python class, selected factory and actual Builder class. A user-assigned object name is not presented as its original template name.

| Level | Actual status |
| --- | --- |
| Common project transformation | Transfers checked geometry, supported MCS/RCS, parametric stock and specified boundary planes. References, identities and reflected positions have separate checks. |
| Accessible operation data | Calls `Geometry.InitializeData(True)` and `Validate()` on admitted editors. Reads accessible Part, Blank, Check, Trim, Cut Area, Wall and Drive channels, collectors and boundaries through UF. This is shared processing, without complete family-specific adapters. |
| Editor recognition | Retains 21 operation-class dispatch routes and 31 recognized Builders. Falls back to checked `CreateBuilder` when the specific method is absent. The other 10 editors are recognized only from the generic call's result. |
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

The generic route additionally recognizes `EdgeChamferBuilder`, `FacingBuilder`, `FacingZigZagBuilder`, `MultiAxisRoughingBuilder`, `PlanarRoughingBuilder`, `PointToPointBuilder`, `RotaryRoughingBuilder`, `SwarfBuilder`, `WallProfilingBuilder` and `ZlevelUndercutBuilder`. Their mapping to actual object classes and DC 2606 templates still needs investigation. Admitted editors undergo the same common checks, not just a name check.

## Data still needing transformation or verification

| Family | Existing common processing | Unconfirmed data |
| --- | --- | --- |
| Cavity / Planar / Face / Z-level / Plunge and related | Editor opening, accessible geometry, boundaries, MCS/RCS; both custom-point channels for newly admitted families | Manual cut levels/planes, numerical directions and points, engage/retract and transfer moves; meaning of settings after reflection |
| Surface Contour, multi-axis, Swarf, Rotary, Deburring | The same common processing and accessible Drive/Wall/Check references | Complete tool-axis definitions, orientation/tilt, drive geometry outside the read channels, local directions and moves |
| HoleMaking / HoleDrilling / Thread / Cylinder / FeatureMilling / GenericFeature | Common references and editors; full hole/FBM definitions explicitly remain unverified, including generic dispatch without `GetFeatureGeometry` | Feature/hole membership and order, axes and depths, local CSYS, control points, attributes and machining-feature data |
| All 31 Builders | Detected custom start/predrill points stop the trial; unavailable channels returning 1345005 are reported separately | Other numerical spatial settings are not reflected. The local nonzero 3D parameter list does not cover every coordinate, inherited value or family-specific setting |

Existing custom-point query exceptions remain: `SurfaceContour` queries channel 1, `TpMultiAxisDeburring` makes no such UF query, and other operations query channels 0 and 1. These are coverage limits, not proof that those two classes contain no other points; channel applicability requires the 2606 SDK.

## Items to verify against SDK DC 2606

1. All 21 factories above and `OperationCollection.CreateBuilder(CAMObject)`: Python availability, arguments, actual return classes and editor selection for different templates sharing an operation class. Presence in an older C++ or .NET API does not prove presence in Python 2606.
2. All 31 classes in the [catalog](mirror-project.md#milling-and-hole-making-editor-catalog), inheritance and additional 2606 editors. Map installed templates separately; discovering a name does not authorize adding it to the allowlist.
3. `Geometry.InitializeData(True)`, `Builder.Validate()`, `Builder.Destroy()` and property/descriptor availability, including `dir()` behavior. Verify availability/types of the seven `*Geometry` fields, empty/inactive fields, and `GeometryGroup` modes/offsets. For example, `TrimBoundary` is a separate definition, not a synonym for an absent `TrimGeometry`.
4. `CavityMillingBuilder.CutLevel`, associated levels/planes and moves; current tool-axis and drive-geometry definitions for surface/multi-axis editors. The older `SurfaceContourBuilder.ToolAxis` appears in the deprecated API list; no guessed transfer was implemented.
5. `GetFeatureGeometry()` and `NXOpen.CAM.FBM.FeatureGeometry`, hole/thread/machining-feature definitions: enumeration, references, points/axes, coordinate frames and units. Method availability currently only flags additional unverified data; its contents are not read.
6. Declarations/applicability of `UF_CAMGEOM_ask_items`, `UF_CAMGEOM_ask_geom_provider`, `UF_CAMGEOM_ask_collector_items`, `UF_CAMGEOM_ask_custom_points`; `UF_CAMBND_ask_boundaries`, `UF_CAMBND_ask_boundary_data`, `UF_CAMBND_ask_boundary_items`, `UF_CAMBND_set_boundary_plane`; structures, sizes, ownership and code 1345005 in 2606 headers. Check custom-point channels for SurfaceContour and TpMultiAxisDeburring separately.
7. `UF_PARAM_ask_required_params` / Python `AskRequiredParams`, `AskParamAttributes`, 3D/3DLength/tag/VLA-tag types, `UF_PARAM_ask_param_status`, `UF_PARAM_ask_param_definer`, MCS/RCS indices 59/60 and link index 41. Each transformed parameter needs verified meaning and frame, not just a type.

SDK verification cannot replace an NX run. Each new family needs a test PRT with nonempty family-specific settings, selection/axis inspection, manual save and reopen of the copy, then normal toolpath regeneration and inspection. The source must remain unchanged; the journal neither saves automatically nor creates extra files.

## Sources and verification limits

- [Siemens NXOpen OperationCollection, 2023 reference reproduced by Tech PLM](https://docs.tech-plm.ru/documentation/external/PL20230425447184281/en-US/custom_api/nx_open_c_plus_plus_ref/a50101.html) — V1.18's catalog source, not 2606 documentation.
- [CavityMillingBuilder](https://docs.tech-plm.ru/documentation/external/PL20230425447184281/en-US/custom_api/nx_open_c_plus_plus_ref/a48265.html) and [HoleDrillingBuilder](https://docs.tech-plm.ru/documentation/external/PL20230425447184281/en-US/custom_api/nx_open_c_plus_plus_ref/a49073.html) from that reference expose separate CutLevel and FBM definitions. Search excerpts were available during this audit; full Tech PLM pages returned 502.
- [Siemens SurfaceContourBuilder, older reference reproduced by ugapi](https://www.ugapi.com/doc/nxopen/a02024.html) and [deprecated API list](https://www.ugapi.com/doc/NXOpen/a10908.html) provide further reason not to transfer old signatures mechanically to 2606.
- The [official Siemens NX documentation portal](https://docs.sw.siemens.com/en-US/product/209349590) could not be read with the available lookup tool. No SDK 2606 sources were available for exact comparison.

V1.19: **228 regression checks outside NX**, including 32 using a separately compiled C library. New checks reproduce specialized-factory allowlist bypass, changed Builder type, lost fields, getter failures, incomplete hole/FBM definitions, operations missing from the Geometry branch and lost failure diagnostics. The C library is a test implementation of the contract, not a Siemens library. These results do not establish NX compatibility or machining correctness.
