# Curves for PLANAR_MILL / PLANAR_PROFILE

[Русский](../planar-add-curves.md) | **English**

## Latest release

**V1.08 · 2026-11-24** — The NX Plane constructor now sets the common boundary plane. Choose **At Distance** for an offset, or use a construction method such as **At Angle** or **Bisector**.

File: [NX_Planar_Add_Curves.cs](../../scripts/NX_Planar_Add_Curves.cs). Updater name: **«Кривые в PLANAR_MILL / PROFILE»**. This is a separate script for existing curves; [engraving centerline construction](planar-mill-curves.md) remains a separate tool.

## Workflow

1. Prepare an existing **PLANAR_MILL** or **PLANAR_PROFILE** in Manufacturing, including the tool, orientation, levels, stock, tolerance, approaches and retracts.
2. Close the operation editor and select one operation in **Operation Navigator**. Its name can be arbitrary.
3. Run `NX_Planar_Add_Curves.cs` using **Tools → Journal → Play** or a custom button.
4. Choose **ON** (default) or **TANTO**. For TANTO, check one side in each group. When the selection contains only one curve type, only that group applies. Switching back to ON disables both groups.
5. Leave **«Выбрать плоскость границ»** checked and click **«Выбрать плоскость →»**. Use the native plane control in **«Выберите плоскость в модели»**. Open **Plane Dialog** for the full method list. For an offset, choose **At Distance**, pick the reference face or plane, enter the distance and check the direction in the preview. Confirm the plane with **OK**. For automatic planes, clear the checkbox in the starting dialog.
6. When the title reads **«Выберите кривые в модели»**, select work-part curves and click **OK**. Each previously unassigned curve gets its own **Part Boundary**. Review the report and generate the toolpath in NX.

| Position | Closed curves | Open curves |
| --- | --- | --- |
| ON | On the boundary | On the boundary |
| TANTO | Inside | Left |
| TANTO | Inside | Right |
| TANTO | Outside | Left |
| TANTO | Outside | Right |

The exact cutting geometry also depends on the tool and operation settings. The source curve direction determines left and right for open curves. The NXOpen field for ON/TANTO is **Tool Position**; the TANTO side is **Tool Side**.

## Added geometry

- Supports lines, arcs, circles, ellipses and splines in the work part, including sketch curves that can be selected as curves.
- Every curve is added separately. Shared endpoints do not cause chaining. Four lines forming a rectangle produce four Open boundaries; one closed curve or circle produces one Closed boundary.
- Closure is read from NX. Open curves are never closed by inserting an artificial segment.
- The native **Plane** constructor provides Inferred, At Distance, At Angle, Bisector and other NX plane methods. The journal takes the evaluated position and normal in work-part coordinates. For example, an offset of 5 from an inclined reference moves the boundary plane 5 part units along the selected normal. The source curves stay in place.
- New boundaries inherit stock and tolerance from the operation; their individual overrides are disabled.
- Curves already present in Part Boundaries are skipped and listed in the report. Their grouping, ON/TANTO and other settings remain unchanged. This script does not change modes on existing assignments.

## Preservation and report

The journal appends one source curve at a time directly to the operation. It reads saved Part Boundaries before and after through the CAM API, checks every new curve tag, Open/Closed, ON/TANTO, the TANTO side and inherited stock and tolerance. The operation editor applies a settings change only when required. The reopened editor verifies the saved plane position and normal for every new boundary when a plane was selected. Existing geometry is compared by its members and settings, including actual saved boundaries without source tags. The Open/Closed totals use the values read back from the reopened editor. A missing curve, an unconfirmed shape or a conflict between named CAM and editor types triggers a verified rollback.

The report lists editor rows and saved boundaries separately and shows expected and actual counts. A missing, duplicated or grouped curve is identified by tag. If a change fails, the journal rolls back the run and checks the original boundaries and inheritance. A successful run is one **Ctrl+Z** step.

The result window is resizable. **«Копировать всё»** copies the entire report, including text outside the viewport. Error reports include the stage, exception type, NX error code when available and stack trace. Reports remain in memory and are never sent automatically.

## Limits and verification

- Supports classic PLANAR_MILL and PLANAR_PROFILE operations represented by the NXOpen `PlanarMilling` class. Other operation classes are rejected.
- The CAM operation and selected curves must belong to the work part. A planar face or Datum Plane can also be selected on a positioned assembly component.
- If the operation inherits nonempty Part Boundaries from parent geometry, first define local boundaries in NX. The script does not change the parent geometry.
- No new curves, toolpath generation or automatic part saving. No files, folders, logs, temporary files or disk settings are created.
- V1.08 compiled against Designcenter 2606 NXOpen. Offline tests cover a +5 offset applied to 36 mixed curves, negative and zero offsets, reversing the offset direction, a bisector, cancellation, all four TANTO side combinations, ON and rollback. The native constructor has yet to be tried on a live CAM part.

[Download script](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/scripts/NX_Planar_Add_Curves.cs) · [Catalog](../../README.en.md) · [Changelog](../../CHANGELOG.en.md)
