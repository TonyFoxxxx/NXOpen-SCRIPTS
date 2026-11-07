# Clean operation names

[Русский](../clean-operation-names.md) | **English**

File: [`NX_Clean_Operation_Names.cs`](../../scripts/NX_Clean_Operation_Names.cs). Version: **V1**.

## Latest release

**V1 · 2026-11-08**

- New standalone journal removes trailing copy and instance markers from every CAM operation in the work part.
- Repeated names receive `_1`, `_2`, etc. in Program Order; the first available name has no counter. Counters precede Z.
- Existing Z suffixes retain their exact sign, precision and trailing zeros. Depth is not calculated.
- Includes collision checks against all CAM groups, one undo step and rollback on a renaming error.

## Purpose

The journal removes trailing chains of `_COPY`, `_INSTANCE`, `_INSTANT`, `_DUPLICATE`, `_КОПИЯ` and `_ЭКЗЕМПЛЯР`, including attached or separate counters such as `_COPY2` and `_INSTANCE_1`. Marker matching ignores case. Only the trailing service chain is removed; meaningful names such as `COPY_MILL` and `MILL_COPY_FINISH` remain intact.

Trailing underscore-separated integers are treated as old duplicate counters and reassigned. A single `KARMAN_CHERN_DORAB_2` therefore becomes `KARMAN_CHERN_DORAB`; a family becomes `KARMAN_CHERN_DORAB`, `KARMAN_CHERN_DORAB_1`, `KARMAN_CHERN_DORAB_2`, etc. This also applies to manually entered trailing integers: their origin cannot be inferred. Numbers in `D11`, `3D`, `14H7`, `R1` and inside `STEP_2_FINISH` stay intact. Words such as `NEW`, `MIRROR` and arbitrary name components are retained.

Z can occur before or after copy markers. It moves to the end without any text changes. A duplicate counter is inserted before Z; different Z suffixes form different names. Numeric Z tokens include `Z-33.8046`, `Z0`, `Z+2.500` and `Z-1,25`. Text that does not match a numeric Z remains part of the name.

Examples process each family from top to bottom:

| Before | After |
| --- | --- |
| `PROFILE_3D` | `PROFILE_3D` |
| `PROFILE_3D_INSTANCE` | `PROFILE_3D_1` |
| `PROFILE_3D_INSTANCE_1` | `PROFILE_3D_2` |
| `PROFILE_3D_INSTANCE_INSTANCE` | `PROFILE_3D_3` |
| `PROFILE_3D_COPY` | `PROFILE_3D_4` |
| `PLSK_CHIST_Z0` | `PLSK_CHIST_Z0` |
| `PLSK_CHIST_Z0_COPY` | `PLSK_CHIST_1_Z0` |
| `PREDV_D11_Z-33.8046` | `PREDV_D11_Z-33.8046` |
| `PREDV_D11_1_Z-33.8046_COPY` | `PREDV_D11_1_Z-33.8046` |

## Run

1. In the updater, click **«Проверить» (Check)**, select **«Очистка названий операций» (Clean operation names)** and click **«Применить выбранное» (Apply selected)**.
2. Open the intended work part with a CAM setup.
3. Run `NX_Clean_Operation_Names.cs` through **Tools → Journal → Play** or a custom button.
4. Renaming starts immediately for the entire work part. No selection is required; selection does not limit the scope.
5. The result shows the number of renamed operations and the first ten changes. **Ctrl+Z** undoes the complete run.

Numbering follows the **Program Order** tree, including nested groups, regardless of the active navigator view. Operations absent from that tree are processed afterwards in CAM collection order. Generated and ungenerated operations and instances are all included.

## Collisions and repeat runs

Uniqueness is case-insensitive across the entire work part. Names occupied by folders, tools, geometry or methods are skipped, so numbering may have gaps. For example, if a tool is named `MILL`, the first operation receives `MILL_1`.

If operations, tree order and CAM group names are unchanged, repeating the journal does not rename clean operations or create another undo mark. Deleting or rearranging operations can change their counters to reflect the new order.

## Project changes and limitations

- Only operation names are changed. Operations are not recreated; tools, groups, geometry, methods and toolpaths are not deleted or recalculated.
- No files, logs, INI settings or attributes are created. The journal does not save the PRT automatically; save normally to keep the result.
- Swaps use temporary object names in NX memory. Only final names remain after success. No temporary files are involved.
- A rejected or truncated name triggers rollback of the entire run and verification of the original names. If NX cannot complete undo, the result explicitly reports that failure.
- A navigator refresh failure does not undo successful renaming; refresh the tree manually.
- This is a standalone journal. Zmin, Setup sheets, Postprocessing and the selected-operation renaming journal are unchanged.

## Validation

Executable NX API model checks cover example names, exact Z preservation, traversal order, CAM group collisions, repeat runs, one-step undo, injected failures and incomplete rollback. A further 5000 mixed suffix-chain names verify allocation and stability. The source was compiled against the installed **Designcenter 2606** libraries; the catalog, updater installation and regression suite were checked separately. This version has not yet been run inside NX on a production part.

Across the local suite and the ESKD recheck with the appropriate encoding, 287 of 288 tests passed. The existing program-folder numbering test exited with code 5 without diagnostics; its journal, fixture and XLSX template are unchanged. The new journal and updater checks passed.
