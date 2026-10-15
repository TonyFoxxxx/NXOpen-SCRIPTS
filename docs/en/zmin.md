# Minimum toolpath Z in operation names

[Русский](../zmin.md) | **English**

Script: [NX_Operation_Zmin.py](../../scripts/NX_Operation_Zmin.py), **V1.04**. [All scripts](../../README.en.md#scripts).

## Latest release

**V1.04 · 2026-10-15**

- Spot drilling now supports `Drill` as well as `Drill,Csink`; depth to the tip comes from operation-local FBM `DEPTH`.
- Through-hole drilling now supports `SYS_CL_SHOULDER` with `IgnoreToolTip` enabled. The existing `SYS_CL_TIP` with `IgnoreToolTip` disabled remains supported. Both use local `DEPTH` + bottom offset + standard conical point length; enabling `IgnoreToolTip` does not remove the point-length term.
- Linked through-hole features `STEP1HOLE` and `STEP2HOLE` are supported. The full operation-local depth is used, not the master feature's `DEPTH_1`.
- Hole matching allows small FBM coordinate roundoff, with a base tolerance of `0.00001` model units. Entrance-point and axis correspondence must remain unique.
- New diagnostic data and reference NC output confirm the calculation outside NX. Ordinary CL projection and rotary handling are retained; unsupported settings still produce a skip reason. V1.04 requires a control run inside the target NX / Designcenter environment.

## Purpose

Adds or replaces a terminal numeric Z suffix, for example `MILL_Z-1` or `MILL_Z1`. Values use CAM-part units, are rounded to four decimal places and omit unnecessary trailing zeros.

## Scope and use

1. Open the saved CAM project with the same work and displayed part.
2. Select operations to process only those operations, or leave selection completely empty to process all operations.
3. Run the journal and review changed, unchanged and skipped counts and reasons.
4. Check the values on a known project and save the PRT if required.

Selecting a folder does not mean recursively selecting its operations. If selection cannot be read reliably, the script stops instead of falling back to the whole project.

Only existing, current toolpaths are used. Missing, out-of-date, invalid or unsupported paths are skipped with explanations. Name conflicts do not cause extra suffixes to be invented. Changes can be undone in NX.

## Calculation

For a CL point `P`, MCS origin `O` and the actual motion's tool-axis vector:

```text
Z = dot(P - O, unit(ToolAxis))
```

The actual ToolAxis is read for both Three and Five formats. The result is measured along the tool axis, not necessarily along a physical machine axis.

For ordinary three-axis machining, including a tilted MCS, the familiar result is retained when the tool vector aligns with MCS Z. For rotary machining, **the MCS origin must lie on the part's rotation axis**. Rotation preserves the dot product under that condition. An unavailable or zero-length tool-axis vector causes a skip; the script does not substitute a fixed MCS axis.

Linear motion is evaluated at saved CL points. Internal minima of arcs and helices with a constant tool axis are calculated analytically. A varying-axis arc without interpolation data and compound paths are unsupported. It does not model interpolation of a changing axis between linear samples, postprocessor transformations, controller kinematics or smoothing.

## Spot drilling and drilling

A saved machine-cycle point may lie at the hole entrance. A minimum based only on those CL points therefore misses drilling depth. V1.04 reads operation-local FBM data, matches each hole by entrance coordinates and axis, and calculates:

`Z_tip = dot(P_entry - O, unit(ToolAxis)) - depth_to_tip`.

The following standard `HoleDrilling` profiles are supported:

| Operation | Depth to tool tip |
| --- | --- |
| Spot drilling: `SPOT_DRILLING` / `NXHOLECENTER`, `Drill` or `Drill,Csink` cycle | Local `DEPTH`; the conical point length is not added again. |
| Through-hole drilling: `DRILLING` / `NXHOLE`, `Drill` or `Drill,Deep` cycle | Full local `DEPTH` + bottom offset + tool point length. A linked `STEP1HOLE` or `STEP2HOLE` feature confirms the through-hole type. |

Depth comes from the feature **inside the operation**, rather than the master model feature or operation name. For `STEP2HOLE`, the full operation-local depth is used; the master feature's `DEPTH_1` does not replace it. Geometry order and path order may differ. Matching allows small FBM coordinate roundoff with a base tolerance of `0.00001` model units; the entrance-point and axis match must be unique. Approach and retract heights are not added to drilling depth. Ordinary CL movements still contribute to the overall minimum.

This requires a current path, `MachineCycle` output and a verified standard conical tip. Spot drilling uses `SYS_CL_TIP` with `IgnoreToolTip` disabled. Through-hole drilling supports two setting pairs:

| Control point | `IgnoreToolTip` | Depth to the tip |
| --- | --- | --- |
| `SYS_CL_TIP` | Disabled (`False`) | Local `DEPTH` + bottom offset + point length. |
| `SYS_CL_SHOULDER` | Enabled (`True`) | The same formula: point length is added even when `IgnoreToolTip` is enabled. |

Other control-point / `IgnoreToolTip` combinations are unsupported. Depth must come from the local feature, with `Use Model Depth` and `Predefined Depth` disabled. Part/bottom stock must be zero and custom feature stocks/offsets disabled. For drilling, the local hole diameter must match the drill diameter and bottom offset must be a nonnegative distance. For spot drilling, bottom offset must be zero. The local feature tip angle must match the tool; custom cycle tracking points are unsupported.

Blind holes and other unconfirmed hole types, other cycles, pilot/compound tips, unsupported control points and depth policies are skipped with explanations. If even one Cycle point cannot be resolved, the entire operation is skipped; no partial minimum or substituted Z0 is written. A skipped operation retains its old name.

Ordinary CL projection matches the [setup-sheet generator](setup-card.md) algorithm introduced in V2.38. The standalone V1.04 cycle-depth correction is included in setup-sheet V2.52, for both its Zmin column and optional renaming. [Postprocessing V1.47](postprocess.md#zmin-in-operation-names) uses the same calculation when Zmin naming is enabled before NC output. All three scripts share the same machine-cycle limitations.

## Validation and file effects

V1.04 was checked outside NX against exported toolpaths, hole parameters, reference NC output and regression tests. A four-decimal suffix can differ from three-decimal NC output solely because of rounding. The journal itself neither reads nor creates NC files. Execution of this revision in the target NX environment still requires a control run. See the [changelog](../../CHANGELOG.en.md) for release history.

The script shows its version in the window. It does not edit or recalculate toolpaths, save the PRT automatically, or create an INI, log, temporary file or other output file.
