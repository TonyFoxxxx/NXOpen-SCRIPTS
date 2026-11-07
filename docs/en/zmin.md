# Minimum toolpath Z in operation names

[Русский](../zmin.md) | **English**

Script: [NX_Operation_Zmin.py](../../scripts/NX_Operation_Zmin.py), **V1.09**. [All scripts](../../README.en.md#scripts).

## Latest release

**V1.09 · 2026-11-07**

- Z in operation names is rounded to three decimal places: `CHIST_1_Z-2.5375` with a calculated Z of −2.5375 becomes `CHIST_1_Z-2.538`.
- Trailing zeros are omitted and negative zero is displayed as `Z0`. Names that collide after rounding receive a counter before Z under the existing rules.
- Only the standalone journal's Z formatting is changed. Depth calculation, Setup sheets and Postprocessing are unchanged.

## Purpose

Adds or replaces a terminal numeric Z suffix, for example `MILL_Z-1` or `MILL_Z1`. Values use CAM-part units, are rounded to three decimal places and omit unnecessary trailing zeros. For example, −2.5375 becomes `Z-2.538`, −2.5 becomes `Z-2.5`, and −0.0004 becomes `Z0`. Rounding is applied only when forming the name, after calculating the minimum at full precision.

## Scope and use

1. Open the saved CAM project with the same work and displayed part.
2. Select operations to process only those operations, or leave selection completely empty to process all operations.
3. Run the journal and review changed, unchanged and skipped counts and reasons.
4. Check the values on a known project and save the PRT if required.

Selecting a folder does not mean recursively selecting its operations. If selection cannot be read reliably, the script stops instead of falling back to the whole project.

Only existing, current toolpaths are used. Missing, out-of-date, invalid or unsupported paths are skipped with explanations. Name collisions receive a counter before Z as described below. Changes can be undone in NX.

## COPY and duplicate names

Names are cleaned of leading `COPY_` / `COPY_COPY_` prefixes and trailing `_COPY` / numeric `_Z` chains in any order; `COPY1` and `COPY_1` markers are also supported. For example, `MILL_R0.5_Z-6_COPY_COPY_Z7.75` with a calculated Z of 7.75 becomes `MILL_R0.5_Z7.75`. Fragments inside the core name, such as `MILL_COPY_TOOL` or `MILL_Z1_part`, are retained.

If the final name is occupied, the first available number is inserted **before Z**: `MILL_1_Z-6`, `MILL_2_Z-6`, and so on. All project operations and groups are considered, including unselected objects. The same core name with different final Z values needs no counter. Natural targets of other planned renames are reserved before generated counters. Already correct names are retained; rerunning with the same depths adds no counters. Existing numeric parts of the core name are preserved.

## Calculation

For a CL point `P`, MCS origin `O` and the actual motion's tool-axis vector:

```text
Z = dot(P - O, unit(ToolAxis))
```

The actual ToolAxis is read for both Three and Five formats. The result is measured along the tool axis, not necessarily along a physical machine axis.

For ordinary three-axis machining, including a tilted MCS, the familiar result is retained when the tool vector aligns with MCS Z. For rotary machining, **the MCS origin must lie on the part's rotation axis**. Rotation preserves the dot product under that condition. An unavailable or zero-length tool-axis vector causes a skip; the script does not substitute a fixed MCS axis.

Linear motion is evaluated at saved CL points. Internal minima of arcs and helices with a constant tool axis are calculated analytically. A varying-axis arc without interpolation data and compound paths are unsupported. It does not model interpolation of a changing axis between linear samples, postprocessor transformations, controller kinematics or smoothing.

For a circular arc whose plane is perpendicular to the current `ToolAxis`, Z is constant: a small difference between endpoint radii in that plane does not affect depth. Both the possible Z variation over the entire radial plane and the endpoint-level difference are checked against a numerical limit of `1e-10` part units. Equal endpoint Z alone is insufficient: a tilted arc can dip lower between them. Other arcs and helices retain radius validation and analytic calculation.

## Spot drilling and drilling

A saved machine-cycle point may lie at the hole entrance. A minimum based only on those CL points therefore misses drilling depth. The script reads operation-local FBM data, matches each hole by entrance coordinates and axis, and calculates:

`Z_tip = dot(P_entry - O, unit(ToolAxis)) - depth_to_tip`.

The following standard `HoleDrilling` profiles are supported:

| Operation | Depth to tool tip |
| --- | --- |
| Spot drilling: `SPOT_DRILLING` / `NXHOLECENTER`, `Drill`, `Drill,Csink` or `Drill,Deep` cycle | Local `DEPTH`; the conical point length is not added again. |
| Through-hole drilling: `DRILLING` / `NXHOLE`, `Drill` or `Drill,Deep` cycle | Full local `DEPTH` + bottom offset + tool point length. A linked `STEP1HOLE` or `STEP2HOLE` feature confirms the through-hole type. |

Depth comes from the feature **inside the operation**, rather than the master model feature or operation name. For `STEP2HOLE`, the full operation-local depth is used; the master feature's `DEPTH_1` does not replace it. Geometry order and path order may differ. Matching allows small FBM coordinate roundoff with a base tolerance of `0.00001` model units; the entrance-point and axis match must be unique. Approach and retract heights are not added to drilling depth. Ordinary CL movements still contribute to the overall minimum.

Through-hole confirmation requires exactly one model feature linked to the operation, of type `STEP1HOLE` or `STEP2HOLE`, on the same axis with the same direction. Local FBM and model entrances may lie at different axial levels; their difference need not equal `TopOffset`. The transverse distance to both axes uses the existing tolerance. Multiple linked coaxial features, including unsupported types, remain ambiguous even if one entrance matches exactly. This check only classifies the hole: each `Cycle` point must still match the actual local entrance in all three coordinates. Local entry, axis and `DEPTH` are neither moved nor replaced with model values; top offset is not added again.

This requires a current path, `MachineCycle` output and a verified standard conical tip. Spot drilling uses `SYS_CL_TIP` with `IgnoreToolTip` disabled. Through-hole drilling supports two setting pairs:

| Control point | `IgnoreToolTip` | Depth to the tip |
| --- | --- | --- |
| `SYS_CL_TIP` | Disabled (`False`) | Local `DEPTH` + bottom offset + point length. |
| `SYS_CL_SHOULDER` | Enabled (`True`) | The same formula: point length is added even when `IgnoreToolTip` is enabled. |

Other control-point / `IgnoreToolTip` combinations are unsupported. Depth must come from the local feature, with `Use Model Depth` and `Predefined Depth` disabled. Part/bottom stock must be zero and custom feature stocks/offsets disabled. For drilling, the local hole diameter must match the drill diameter and bottom offset must be a nonnegative distance. For spot drilling, bottom offset must be zero. The local feature tip angle must match the tool; custom cycle tracking points are unsupported.

Blind holes and other unconfirmed hole types, other cycles, pilot/compound tips, unsupported control points and depth policies are skipped with explanations. If even one Cycle point cannot be resolved, the entire operation is skipped; no partial minimum or substituted Z0 is written. A skipped operation retains its old name.

[Setup sheets V2.67](setup-card.md) and [Postprocessing V1.50](postprocess.md#zmin-in-operation-names) still use the Zmin V1.07 calculation. The V1.08 circular-arc fix and V1.09 three-decimal name formatting are published only in the standalone journal; integration into those two scripts is deferred. Machine-cycle calculations and ordinary/rotary CL projection remain shared.

## Validation and file effects

V1.09 was checked outside NX for rounding positive and negative values, replacing old suffixes, omitting trailing zeros, handling name collisions after rounding and rerunning the journal. Toolpath calculation is retained from V1.08, previously checked against a complete diagnostic export with 8425 events and reference NC output. The journal itself neither reads nor creates NC files. Execution of this revision in the target NX environment still requires a control run. See the [changelog](../../CHANGELOG.en.md) for release history.

The script shows its version in the window. It does not edit or recalculate toolpaths, save the PRT automatically, or create an INI, log, temporary file or other output file.
