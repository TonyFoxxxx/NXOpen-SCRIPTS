# Rename part and assembly files

[Русский](../rename-assemblies.md) | **English**

Script: [NX_Rename_Assemblies.cs](../../scripts/NX_Rename_Assemblies.cs). [All scripts](../../README.en.md#scripts).

Renames native PRT files and updates component references within the supported project scope.

## Features

- Uses the displayed root PRT's folder, loads directly contained PRT files and checks their references.
- Shows assemblies and parts as a tree; choose files and enter new names.
- A file used by several component occurrences has one common new filename.
- Saves the selected parts and the assemblies containing them, **including their existing unsaved changes**.
- Deletes old PRT files only after saving and checking references.
- Checks existing names, write access, assembly structure and references from open assemblies.
- Provides a draggable splitter and horizontal scrolling. Column widths are retained only for the current window.

## Use

1. Open the saved native project and review its unsaved changes.
2. Run the script, select the files and enter their new names.
3. Review the rename operation and let the script save the affected parts and assemblies.
4. Check the resulting files and assembly references.

Errors before disk writes roll back component-instance names. Once writing starts, retrying in the same window is blocked. If a failure leaves work unfinished, use **«Завершить после сбоя» (Finish after failure)**: select the new file and enter the old name manually. The function first saves references, then closes and deletes the old PRT only if the checked assemblies no longer reference it.

## Scope and file effects

This is for native files, not Teamcenter. Subfolders are not recursively scanned. External components are displayed but not renamed; an affected open assembly outside the project blocks the operation. References in closed assemblies outside the project, WAVE links and expression links are not repaired. Renames differing only by letter case are blocked.

NX Undo cannot undo changes already written to disk. A failure may leave the original file or both filenames until the references are repaired. The script creates no INI, archive, log, report or backup; NX may still create its own files or store dialog preferences.
