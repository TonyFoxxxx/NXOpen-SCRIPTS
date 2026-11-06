# Delete empty tools

[Русский](../delete-empty-tools.md) | **English**

[Script catalog](../../README.en.md) · [NX_Delete_Empty_Tools.cs source](../../scripts/NX_Delete_Empty_Tools.cs)

## Latest release

**V1 · 2026-11-06.** First release. Delete empty CAM tools with a preview, confirmation, one undo step and result verification. Available through the updater under the stable filename `NX_Delete_Empty_Tools.cs`.

Compiled against the Designcenter 2606 libraries and checked 31 automated scenarios using an NX API test model. Execution on a real CAM project inside NX has not yet been checked.

## Which tools are deleted

The script checks all tools in the **current work part**. No Operation Navigator selection is required; selection does not limit the scope.

A tool is deleted only when it has no child objects and is not the parent of any CAM operation, directly or through nested groups. The script checks the Tool object type rather than names or T numbers.

- Tools with operations are preserved, including ungenerated, out-of-date and suppressed operations.
- Tools containing child nodes are preserved. If deleting an empty child tool leaves its parent tool empty, the parent remains until a later run.
- Folders, the machine root, Unused Items and other objects that are not tools are not deleted.
- Tools owned by another part are not deleted. Failure to read the CAM structure stops the run before deletion.

## Installation and use

1. In the [updater](updater.md), click **«Проверить» (Check)**.
2. Select **«Удаление пустых инструментов» (Delete empty tools)** and click **«Применить выбранное» (Apply selected)**.
3. Open the CAM project in Manufacturing. In an assembly, ensure the intended part is the work part.
4. Run `NX_Delete_Empty_Tools.cs` through **Tools → Journal → Play** or your NX button.
5. Review the tool names and count. **«Удалить» (Delete)** performs deletion; **«Отмена» (Cancel)** or closing the dialog exits without changes.
6. Inspect the tree after the result message. Use **Ctrl+Z** to undo the entire run in one step if needed.

For repeat use, [configure a button once](installation.md#buttons) pointing to the stable filename. New scripts are not installed automatically: manually select the new updater row.

## Checks and errors

After confirmation, the script rechecks the work part, tools and operation associations. A changed structure requires a fresh run. If another NX command already has a deletion queue, the script leaves it intact and asks you to finish that command.

Deletion uses one undo mark. The queue check prevents collateral deletion of other CAM groups and operations. After updating, the remaining groups, operations and machine-tree parents are compared with the original state. On failure, the script attempts to clear its deletion queue and restore the state before the run; a rollback failure is reported explicitly.

If deletion succeeds but Operation Navigator refresh fails, the result asks you to refresh the tree manually. Repeating deletion is not necessary.

## Settings and files

No INI or additional settings are required. The script creates no files, logs, reports or attributes, does not generate toolpaths and does not save the PRT automatically. Save changes using the normal NX save command.

Project targets are NX 2506 and Designcenter 2606 on Windows. Automated tests and compilation do not replace behavior checks on a particular CAM model.
