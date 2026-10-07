# Rename operations

[Русский](../rename-operations.md) | **English**

Script: [NX_Rename_Operations_In_Selected_Folder.cs](../../scripts/NX_Rename_Operations_In_Selected_Folder.cs). [All scripts](../../README.en.md#scripts).

Applies a common base name and numbered suffixes, for example `ROUGH_1`, `ROUGH_2`.

## Features

- Select one program folder to rename its **direct operations only**, without descending into subfolders.
- Alternatively, select individual operations with Ctrl/Shift. Mixing a folder with individual operations is rejected.
- Numbering follows CAM tree order, not the order in which you clicked.
- Names already used anywhere in CAM—including groups, tools and geometry—are skipped when assigning suffixes.
- All names are planned before changes begin. A rename error rolls back the changes; successful changes have an NX Undo mark. The result lists the count and assigned names.

## Use

1. Open the CAM work part and select one folder or the required operations.
2. Run the script and enter the common base name.
3. Review the resulting names. Save the PRT if you want to retain them.

The script needs to identify every selected operation in the work part's tree; otherwise it stops. It does not recalculate toolpaths, use an INI, create separate files or save the PRT automatically.
