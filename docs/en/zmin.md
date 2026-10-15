# Minimum toolpath Z in operation names

[Русский](../zmin.md) | **English**

Script: [NX_Operation_Zmin.py](../../scripts/NX_Operation_Zmin.py), **V1.03**. [All scripts](../../README.en.md#scripts).

## Latest release

**V1.03 · 2026-10-14**

- Fixed false Z0 results for supported spot-drilling and through-hole drilling cycles: a Cycle entrance point is no longer treated as the final depth.
- Spot drilling uses local tip depth; through-hole drilling includes local depth, bottom offset and drill point length.
- Each hole is matched to the path position and axis, including rotary orientations. Ambiguous and unsupported cycles are skipped with reasons.
- Checked outside NX against diagnostic data and final NC Z values. Regressions cover rotations, different hole depths, unsupported cases and unchanged ordinary CL results. V1.03 still needs a control run inside the target NX / Designcenter environment.

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

A saved machine-cycle point may lie at the hole entrance. A minimum based only on those CL points therefore misses drilling depth. V1.03 reads operation-local FBM data, matches each hole by entrance coordinates and axis, and calculates:

`Z_tip = dot(P_entry - O, unit(ToolAxis)) - depth_to_tip`.

The following standard `HoleDrilling` profiles are supported:

| Operation | Depth to tool tip |
| --- | --- |
| Spot drilling: `SPOT_DRILLING` / `NXHOLECENTER`, `Drill,Csink` cycle | Local `DEPTH`; the conical point length is not added again. |
| Through-hole drilling: `DRILLING` / `NXHOLE`, `Drill` or `Drill,Deep` cycle | Local `DEPTH` + bottom offset + tool point length. A linked `STEP1HOLE` feature confirms the through-hole type. |

Depth comes from the feature **inside the operation**, rather than the master model feature or operation name. Geometry order and path order may differ. Approach and retract heights are not added to drilling depth. Ordinary CL movements still contribute to the overall minimum.

This requires a current path, `MachineCycle` output, a standard conical tip and the `SYS_CL_TIP` control point. Depth must come from the local feature, with `Use Model Depth` and `Predefined Depth` disabled. Part/bottom stock must be zero and custom feature stocks/offsets disabled. For drilling, the local hole diameter must match the drill diameter and bottom offset must be a nonnegative distance. For spot drilling, bottom offset must be zero. The local feature tip angle must match the tool.

Blind or stepped holes, other cycles, pilot/compound tips, other control points and unsupported depth policies are skipped with explanations. If even one Cycle point cannot be resolved, the entire operation is skipped; no partial minimum or substituted Z0 is written. A skipped operation retains its old name.

Ordinary CL projection matches the [setup-sheet generator](setup-card.md) algorithm introduced in V2.38. The standalone V1.03 cycle-depth correction is included in setup-sheet V2.51, for both its Zmin column and optional renaming. [Postprocessing V1.46](postprocess.md#zmin-in-operation-names) uses the same calculation when Zmin naming is enabled before NC output. All three scripts share the same machine-cycle limitations.

## Validation and file effects

V1.03 was checked outside NX against exported toolpaths, hole parameters, reference NC output and regression tests. A four-decimal suffix can differ from three-decimal NC output solely because of rounding. The journal itself neither reads nor creates NC files. Execution of this revision in the target NX environment still requires a control run. See the [changelog](../../CHANGELOG.en.md) for release history.

The script shows its version in the window. It does not edit or recalculate toolpaths, save the PRT automatically, or create an INI, log, temporary file or other output file.
