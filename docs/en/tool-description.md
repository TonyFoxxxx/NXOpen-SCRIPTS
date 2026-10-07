# Fill tool descriptions

[Русский](../tool-description.md) | **English**

Script: [NX_Tool_D_To_Description.cs](../../scripts/NX_Tool_D_To_Description.cs). [All scripts](../../README.en.md#scripts).

Fills both **General Description** and **Cutter Description** from actual CAM tool parameters.

## Formats and scope

Choose either diameter only, such as `⌀4`, or the full format, such as `⌀4_T4_H4_D4`.

- Diameter comes from the tool's parameters, not its name; unnecessary trailing decimal zeros are removed.
- `T` is ToolNumber, `H` is AdjustRegister and `D` is CutcomRegister.
- A missing D parameter is omitted. A readable zero remains `D0`.
- All supported tools in the work part are processed, regardless of selection.
- Supported families: Mill, Drill, Barrel, Tcutter and MillForm. Unsupported tools or tools without an accessible positive diameter remain unchanged. Descriptions that already match are not rewritten.

## Use

1. Open the CAM work part and run the journal.
2. Choose the description format. Cancel exits without changes.
3. Check the tool descriptions and save the PRT when required.

The script verifies the written description fields and checks that geometry and T/H/D parameters remain unchanged. A per-tool error rolls back that tool's partial changes; a general NX update failure attempts to roll back all changes. Successful changes share an NX Undo mark.

There is no success dialog; errors are reported. The script does not rename tools, change their geometry or numbers, recalculate toolpaths, create an INI or other files, or automatically save the PRT.
