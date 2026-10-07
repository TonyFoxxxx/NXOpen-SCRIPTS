# Minimum toolpath Z in operation names

[Русский](../zmin.md) | **English**

Script: [NX_Operation_Zmin.py](../../scripts/NX_Operation_Zmin.py), **V1.02**. [All scripts](../../README.en.md#scripts).

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

The [setup-sheet generator](setup-card.md), from V2.38, uses the same algorithm for its Zmin column. Unsupported values remain blank; names change only when its separate renaming option is enabled.

## Validation and file effects

V1.02 was checked outside NX against exported toolpaths and algorithm tests. Execution of this revision in the target NX environment still requires a control run.

The script shows its version in the window. It does not edit or recalculate toolpaths, save the PRT automatically, or create an INI, log, temporary file or other output file.
