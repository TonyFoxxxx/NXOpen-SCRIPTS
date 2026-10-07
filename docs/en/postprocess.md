# Postprocessing

[Русский](../postprocess.md) | **English**

Script: [NX_Postprocess_To_Machine.cs](../../scripts/NX_Postprocess_To_Machine.cs). [All scripts](../../README.en.md#scripts).

Outputs NC programs to machine folders, removable media or a manually selected folder. Supports post assignment, optional CAM preparation, sequential subprogram calls, repeated machining at multiple work offsets and FANUC BIN output.

## Latest release

**V1.45 · 2026-10-07**

- Multiple-offset parsing inherits modal state from the header and previous blocks. Known G modes are restored only when necessary; coordinates, tool, spindle and coolant commands are never invented. Moves and final states are checked with optional-block skipping both on and off.
- Supports separate X and Y blocks before a separate Z approach with G43/H, O headers with N numbers, and G80 together with G00/G01/G02/G03 in either order. An offset specified only in the header is inserted before each repeated section.
- Explicit post entries can use `Auto`, `ISO_G28` or `ISO_G53` profiles. G53 requires a confirmed `WorkOffsetG53Z` matching the original retract, G49, G90/G00/G40 and fixed explicit G20/G21 units. The script does not choose a safe machine height or create retract moves.
- Detailed errors show post, profile, offsets, the failed condition and nearby original NC lines. **«Скопировать данные ошибки» (Copy error details)** includes the version; Close and Escape dismiss the window. A native fallback is retained if Windows Forms cannot open. No report or backup files are added.
- Existing paths, INI editors, program selection, Description, Zmin, numbering, subprogram calls and BIN behavior are retained.

Numeric ISO conversion and the Windows error window have automated checks. Original O1261/O5555 examples were checked separately without publishing their NC source. Testing inside the target NX / Designcenter and verification for the actual controller remain necessary. See the [changelog](../../CHANGELOG.en.md).

## Program selection and posts

- The program tree recognizes folders beginning with uppercase `O`. Selection cascades through nested folders, while nested programs remain separate output jobs without duplicates.
- Operations selected in NX before launch can be output as a selected set. The window shows their own names.
- Choose a common post or assign posts separately to output programs. The list supports searching by names and paths, opening the INI and refreshing the catalog/INI.
- **«Пути к постпроцессорам» (Postprocessor paths)** is available from post selection. Add a named post and choose TCL; a matching DEF is filled automatically if available, otherwise select it manually.
- Save writes only the relevant settings and refreshes the list, preserving unrelated parameters, comments, encoding and line endings. Cancel writes nothing; removing a list entry does not delete post files. If the INI is absent, a successful first save creates it when the location is writable.

## Machine folders and destinations

Choose a machine folder, removable medium or manual destination. A separate button searches for removable media. To configure machine buttons, open **«Папки со станками…» (Machine folders)** in the destination window.

1. Add a local or network root folder using Browse or by pasting its path. Quoted Windows **Copy as path** values are supported.
2. Give the entry a label. You can add multiple roots or remove entries.
3. Click **«Сохранить» (Save)**. The script validates the settings, writes `[MachineRoots]` in the existing INI and refreshes/resizes the machine-button list.
4. Select the output destination again after saving.

Only **immediate subfolders named entirely with digits** become machine buttons through a root. Other names can be added explicitly in `[Machines]`. Removing a root entry does not delete folders or NC programs. Cancel does not save edits and keeps the existing destination.

Windows must be able to access the network share; the INI and output location must be writable. Existing unchanged roots that are temporarily unavailable can be retained with a warning. Invalid new roots, missing numbered subfolders or concurrent INI changes can block saving.

## Optional CAM preparation

The first window offers three options. They start unchecked on every launch and are not stored as persistent choices.

### Operation numbering

**«Нумеровать операции» (Number operations)** adds leading numbers in Program Order, restarting for each output program.

- Up to 99 operations: `01_`, `02_`, …, `99_`.
- From 100 operations: `001_`, `002_`, …, starting with the first operation.
- The width is calculated separately for each output program.
- In selected-operation mode, only selected operations are counted and renamed.
- Repeated numbering replaces old leading numbers and recalculates the width when crossing 99/100.

When unchecked, this option preserves existing names and prefixes; the separate Zmin option may still change the end of a name.

### Tool descriptions

**«Добавить параметры инструмента в описание (description)» (Add tool parameters to description)** reveals two inline radio buttons:

- Diameter only, the default on each launch: `⌀6.5`.
- Diameter with T/H/D: `⌀6.5_T2_H3_D4`.

Hiding and showing the options retains the format for this window. Selecting a format does not immediately change the model and opens no additional dialog.

Both General Description and Cutter Description are filled from actual parameters for all supported tools in the work part, including unused tools. Supported families are Mill, Drill, Barrel, Tcutter and MillForm with an accessible positive diameter. Missing CutcomRegister omits D; a readable zero remains `D0`. Geometry, tool numbers and registers are checked and left unchanged.

An individual tool error rolls back its partial write and is reported in the output result. A general NX update failure stops output and rolls back preparation. There is no separate success dialog.

### Zmin in operation names

This option uses NX selection captured **before the first window opens**:

- If operations were selected, it processes those operations.
- A completely empty selection means all CAM operations in the project, not just programs selected for NC output.
- Folder-only selection or a selection-reading error does not expand the scope to the whole project.
- Work and displayed part must be the same.

Existing, current toolpaths are evaluated using actual Three/Five ToolAxis vectors and `dot(P - O, unit(ToolAxis))`. Values are rounded to four decimal places without unnecessary zeros. For rotary machining the MCS origin must lie on the part's rotation axis. Constant-axis arcs/helices include analytical internal minima; unsupported varying-axis or compound paths, missing data and out-of-date paths are skipped with reasons. This is a CL calculation, not controller simulation.

Repeated terminal numeric Z suffixes are replaced by one: `01_Milling_Z-5_Z-8` becomes `01_Milling_Z-12`. A Z fragment in the middle of a name and other prefixes/suffixes are retained. Already correct names and conflicts are skipped; extra counters are not added.

### Order and rollback

Preparation runs just before output, in this order: **Description → numbering of output operations → Zmin in its own selection scope**.

Cancellation or an error before the first NC program is published restores prepared descriptions and names. After successful or partial publication, the NX changes remain with an Undo mark. The result includes changed/current/skipped counts and reasons. Save the PRT manually to retain the changes. Undo in NX does not modify already exported NC files.

## Pre-output checks and program names

The script compares diameters in tool names with actual parameters for all CAM tools, including unused tools. Mismatches appear in a white dialog with red text and a **Continue?** question. Green **«Нет» (No)** is the default and stops; Escape also stops. Red **«Да» (Yes)** skips this check's errors for the current run only.

Mixed or unavailable MCS data also requires an explicit choice to continue. These checks do not repair tool data or coordinate systems.

Post paths, output names, destinations and media are checked. Output is staged before publication; unexpected extra files produced by a post block publication.

You can enter an O number of 1–8 digits with a nonzero value; the O prefix is added automatically. This changes the output filename and NC header, not the NX program-folder name. The extension follows post/configuration settings. An optional second copy of both NC programs and BIN can be written beside the saved project. Click **«Вывести» (Output)** to begin; replacing existing results requires confirmation.

## Sequential subprogram calls

The first program becomes the main program, calls the remaining programs with M98 and ends with M30. Subprograms end with M99. Use O numbers from 1 to 9999. Change the call order with drag-and-drop or the ordering buttons.

**This function was tailored to the author's postprocessor and is not guaranteed to work with another post or controller.** Check the resulting code for your system. If an error occurs, send the message/screenshot and a suitable source NC example so the format can be investigated.

## Multiple work offsets

Choose one to six unique offsets from **G54–G59**, adding/removing them and setting their order. The selection applies to each output program. Machining is repeated at the chosen offsets **tool by tool**; NX MCS objects are not changed.

This transformation has been checked on specific numeric ISO examples and the author's post output. It is not a general converter for every postprocessor or controller.

### Supported structure

The parser expects three-axis numeric ISO with an O header, M06 tool changes and one original G54–G59 selection. It reads modal commands rather than depending on fixed line numbers, whitespace, case or comments.

Each original tool section retains one M06, and the program retains one final M30/M02. Original coordinates, feeds, spindle speeds and compensation values are preserved.

Known modes can be inherited from the header, including G40, and restored where necessary: for example G90 after G91 G28, units, plane, feed and motion modes, and return to G00 after G80. Unknown state is not invented. Tool, H, spindle and coolant commands are not synthesized. A header-only work-offset selection is inserted for each repeat. The legacy initial G94 handling is accepted only where later explicit state establishes it and there is no G95.

The approach must establish **explicit X and Y**, together or in separate blocks, before a **separate absolute rapid Z approach with explicit G43/H in the section**. A combined first XYZ approach, unknown axes, macros, rotations, extra coordinate transformations or unverified G/M commands are rejected.

Each section must have a confirmed Z retract, with canned cycles and cutter compensation cancelled. A retract present only in a `/` optional block is insufficient: both block-skip states are checked. Modal validation is not collision simulation.

### Per-post profiles

Set the following keys in the explicit post's `[Post name]` section of the existing INI. Standard NX catalog posts use Auto; add an explicit post entry to customize its profile. Older INI files default to Auto.

| `WorkOffsetProfile` | Accepted retract |
| --- | --- |
| `Auto` | G91 G28 Z0; G53 only when `WorkOffsetG53Z` is configured. |
| `ISO_G28` | G91 G28 Z0 only; G53 is rejected. |
| `ISO_G53` | G53 only, with required `WorkOffsetG53Z`; G28 is rejected. |

`WorkOffsetG53Z` must be the **verified machine-coordinate Z of a separate retract already produced by the post**, in the NC program's units, using a decimal point. It must match the original output.

Do not guess this value or assume zero is safe. It is not a work-coordinate Z height and does not ask the script to create or change a retract. G53 requires effective G90/G00/G40/G49, explicit G20 or G21 and no unit change. A separate Z retract must precede the XY return.

Controller semantics must be checked against the actual controller documentation. The source guide references the [Haas mill programming workbook](https://www.haascnc.com/content/dam/haascnc/en/service/reference/programming-workbooks/mill---programming-workbook.pdf); that does not establish compatibility with every machine.

### Errors and reports

Unrecognized or ambiguous structure blocks the whole package before NC/BIN publication. The error window shows post, profile, offset list, failed condition and the relevant original NC line with up to two neighboring lines on either side. It shows the original code, not the expanded result.

**«Скопировать данные ошибки» (Copy error details)** copies the diagnostic and script version only when you click it. Text can also be selected/copied, with horizontal scrolling for long lines. Close or Escape dismisses the window. No logs or backups are created and nothing is sent externally.

For investigation, provide the copied error, controller name and **original NC output generated without multiple offsets and without sequential subprogram calls**. A screenshot is useful, but alone may not show enough program structure to fix the issue.

## FANUC BIN

The container filename is `FANUCPRG.BIN`. Choose no BIN output, **«Добавить / обновить» (Add / update)** or **«Новый BIN» (New BIN)**:

- Merge retains other programs and replaces matching O numbers.
- New contains only the selected programs.
- Choose 2, 4, 8 MB or a custom capacity. The full resulting file size is checked; capacity is not increased automatically.
- If several destinations are used, each existing BIN is handled independently.

## Workflow

1. Open and prepare the CAM project. Select individual operations before launching if that mode or the Zmin selection scope is needed.
2. Run the script, select output programs/operations and optionally enable preparation.
3. Review diameter/MCS checks and choose a common post or assign posts individually. Configure paths if needed.
4. Choose the machine, medium or manual folder; add machine roots if required and reselect the destination after saving them.
5. Set output names and any project copy, subprogram calls, offsets or BIN options.
6. Confirm output. Preparation runs, then postprocessing and publication.
7. Review output files and the result, including skipped operations or partial-output errors. Save the PRT separately if required.

## Settings and files

Main settings: `NX_Postprocess_To_Machine.ini` beside the script. Use [configuration](configuration.md#postprocessing) for examples. Post paths and machine roots can be edited in the UI; other settings and explicit machine entries are edited in the INI. Relative paths are resolved from its location. Posts are not bundled.

The last manual folder and project-copy choice are stored in `%LOCALAPPDATA%\NX_Postprocess_To_Machine\LastChoice.ini`. Temporary working folders are cleaned up. The INI editors do not add report or backup files. There are no persistent NC/BIN backups.

A write failure can occur after some outputs have already been published; the error lists saved files. A project copy means an additional output copy, not an automatic PRT save.
