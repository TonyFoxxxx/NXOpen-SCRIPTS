# Number program folders

[Русский](../number-program-folders.md) | **English**

Script: [NX_Number_Program_Folders.cs](../../scripts/NX_Number_Program_Folders.cs). [All scripts](../../README.en.md#scripts).

Numbers program folders using an XLSX register to retain allocation history.

## Latest release

**V1.05 · 2026-10-21.** Deleted folders' numbers can now restore tree order within the same project. After deleting `O1689`, `O1690` becomes `O1689`; the unused tail remains reserved for that project. Updater V1.19 updates the code even without INI/XLSX and offers data setup afterwards.

## Features

- Processes the whole Program Order tree, including nested folders whose names begin with uppercase `O`. Selection does not restrict the scope.
- Uses the configured prefix and number range in tree order.
- A repeat run on an unchanged project retains its numbering. Inserting, deleting or reordering folders redistributes this project's available numbers in tree order.
- Checks CAM name conflicts and the available number pool.
- Uses NX attributes together with the project path to identify allocations. Copied attributes alone do not allow another project to reuse those IDs.
- Records number, project, path, date, old name, status and ID in XLSX.
- Reserves numbers before changing NX. Confirmed allocations of deleted folders can be reused only by the same project; an unused tail is marked as a project reserve in the note. Other projects' numbers and cancelled/unconfirmed allocations remain occupied.
- Protects the register against concurrent use and detects external changes. Microsoft Excel is not required.

## Use

1. Save the PRT.
2. Configure your range and register in `NX_Numbering_Settings_v1.0.ini` beside the script. See [configuration](configuration.md#program-folder-numbering).
3. Run the script; no selection is needed.
4. Review the result and save the PRT.

A failure rolls back NX changes and updates the register's status where possible. Reserved numbers are not silently released. **NX Undo does not roll back the XLSX history.**

## Files and limitations

Since V1.04, the script locates its INI beside the running `.cs` journal, for example in `C:\ProgramData\2. NX_Scripts`. Updater V1.19 commits the code first, then migrates a compatible old pair from `C:\ProgramData\3_NX_DATA` or offers existing-register connection, new numbering or deferred setup. A new INI/XLSX is placed beside the script; a selected existing workbook is used at its current path. Existing settings and history are retained.

`Prefix`, `StartNumber` and `EndNumber` define the numbering format and range. `RegistryFile` in the INI points to the register. The current implementation creates a `.lock` lock file and a `.bak` previous version beside the register. Writing uses a `.tmp` file, which is removed after replacement. The script also writes CAM attributes; it does not save the PRT automatically.

A missing or incompatible existing register blocks numbering. Do not substitute an empty register for a used history: restore the original. The empty repository template is only for the first setup. Keep the stable `v1.0` filenames.

## Deletion and project reserve

Each run compares the full tree with the register. Only confirmed records with the same PRT path and current prefix qualify for reuse, provided their ID no longer occurs in the tree and their name is not occupied by another CAM object. A marked folder manually renamed without `O` is not treated as deleted.

| Before deletion | After deleting O1689 and running the journal |
| --- | --- |
| O1686, O1687, O1688, O1689, O1690 | O1686, O1687, O1688, O1689 |

The `O1690` row stays allocated to this project and can number its next new folder. The record ID moves with the number. The row retains the `Присвоен` status; its note identifies the reserve, so the workbook format stays compatible. Other projects' records are unchanged. Gaps are closed using the project's available slots; numbers occupied by other projects/objects or cancelled allocations are not reused.

<a id="recovery"></a>

## Recovery from NX

The updater's «Восстановление из NX…» button opens the Russian version of these instructions. The updater itself neither reconstructs history nor executes downloaded code.

1. Prefer a surviving filled or older workbook. Save the [V1 recovery journal](../../tools/NX_Recover_Numbering_Register.py) beside the numbering INI as `NX_Recover_Numbering_Register.py`.
2. If the workbook is completely lost, download the [empty workbook](../../templates/NX_Numbering_Register_v1.0.xlsx) beside the script **as a recovery scaffold only**, without replacing an existing file. Connect it as an existing register in the updater. If the INI is missing, enter the previously allocated range. Do not run ordinary numbering yet.
3. Open the saved original CAM project containing numbering attributes, close Excel and other numbering runs, and run the recovery journal through Alt+F8. Review and confirm the missing rows.
4. Repeat for every project with missing history before allocating new numbers. The recovery journal adds missing records, preserves existing rows, leaves NX unchanged and stops on conflicts.

Only saved attributes in the open PRT can be recovered. Original allocation dates, deleted folders and their reserve, and cancelled/interrupted allocations in a lost workbook cannot be reconstructed from the current tree. If complete history cannot be established, assign a known unused range for new allocations. New rows contain the recovery date.
