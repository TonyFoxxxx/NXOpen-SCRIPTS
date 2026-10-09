# Number program folders

[Русский](../number-program-folders.md) | **English**

Script: [NX_Number_Program_Folders.cs](../../scripts/NX_Number_Program_Folders.cs). [All scripts](../../README.en.md#scripts).

Numbers program folders using an XLSX register to retain allocation history.

## Features

- Processes the whole Program Order tree, including nested folders whose names begin with uppercase `O`. Selection does not restrict the scope.
- Uses the configured prefix and number range in tree order.
- A repeat run on an unchanged project retains its numbering. Inserting or reordering folders can redistribute this project's numbers to preserve tree order.
- Checks CAM name conflicts and the available number pool.
- Uses NX attributes together with the project path to identify allocations. Copied attributes alone do not allow another project to reuse those IDs.
- Records number, project, path, date, old name, status and ID in XLSX.
- Reserves numbers before changing NX. Numbers belonging to other projects, deleted items or cancelled allocations remain used.
- Protects the register against concurrent use and detects external changes. Microsoft Excel is not required.

## Use

1. Save the PRT.
2. Configure your range and register in `NX_Numbering_Settings_v1.0.ini` beside the script. See [configuration](configuration.md#program-folder-numbering).
3. Run the script; no selection is needed.
4. Review the result and save the PRT.

A failure rolls back NX changes and updates the register's status where possible. Reserved numbers are not silently released. **NX Undo does not roll back the XLSX history.**

## Files and limitations

Since V1.04, the script locates its INI beside the running `.cs` journal, for example in `C:\ProgramData\2. NX_Scripts`. Updater V1.18 installs the initial INI/XLSX there and migrates the old pair from `C:\ProgramData\3_NX_DATA` when necessary. Existing settings and history are retained.

`Prefix`, `StartNumber` and `EndNumber` define the numbering format and range. `RegistryFile` in the INI points to the register. The current implementation creates a `.lock` lock file and a `.bak` previous version beside the register. Writing uses a `.tmp` file, which is removed after replacement. The script also writes CAM attributes; it does not save the PRT automatically.

A missing or incompatible existing register blocks numbering. Do not substitute an empty register for a used history: restore the original. The empty repository template is only for the first setup. Keep the stable `v1.0` filenames.
