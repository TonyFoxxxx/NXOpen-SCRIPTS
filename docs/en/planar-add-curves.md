# Curves for PLANAR_MILL / PLANAR_PROFILE

[Русский](../planar-add-curves.md) | **English**

## Latest release

**V1.02 · 2026-11-23** — Each selected curve is appended to the operation as its own Part Boundary. The journal counts saved CAM boundaries. An empty entry row in the editor no longer adds one to the expected total. Selecting 36 curves in an empty operation gives an expected total of 36.

File: [NX_Planar_Add_Curves.cs](../../scripts/NX_Planar_Add_Curves.cs). Updater name: **«Кривые в PLANAR_MILL / PROFILE»**. This is a separate script for existing curves; [engraving centerline construction](planar-mill-curves.md) remains a separate tool.

## Workflow

1. Prepare an existing **PLANAR_MILL** or **PLANAR_PROFILE** in Manufacturing, including the tool, orientation, levels, stock, tolerance, approaches and retracts.
2. Close the operation editor and select one operation in **Operation Navigator**. Its name can be arbitrary.
3. Run `NX_Planar_Add_Curves.cs` using **Tools → Journal → Play** or a custom button.
4. Choose **ON** or **TANTO** in the opening dialog. ON is the default. For TANTO, choose the side.
5. Click **«Выбрать кривые →»**, select work-part curves and accept the NX selection dialog.
6. Review the result. Each previously unassigned curve receives its own new **Part Boundary**. Inspect the geometry and generate the toolpath using NX commands.

| Mode | Behavior |
| --- | --- |
| ON | Position the tool on the boundary. The side selector is disabled. |
| TANTO + Inside / Left | Contact inside a closed boundary or to the left of an open curve's direction. |
| TANTO + Outside / Right | Contact outside a closed boundary or to the right of an open curve's direction. |

The exact cutting geometry also depends on the tool and operation settings. The source curve direction determines left and right for open curves. The NXOpen field for ON/TANTO is **Tool Position**; the TANTO side is **Tool Side**.

## Added geometry

- Supports lines, arcs, circles, ellipses and splines in the work part, including sketch curves that can be selected as curves.
- Every curve is added separately. Shared endpoints do not cause chaining. Four lines forming a rectangle produce four Open boundaries; one closed curve or circle produces one Closed boundary.
- Closure is read from NX. Open curves are never closed by inserting an artificial segment.
- NX determines the boundary plane in Automatic mode. The script does not rotate the MCS or set a new operation orientation.
- New boundaries inherit stock and tolerance from the operation; their individual overrides are disabled.
- Curves already present in Part Boundaries are skipped and listed in the report. Their grouping, ON/TANTO and other settings remain unchanged. This script does not change modes on existing assignments.

## Preservation and report

The journal appends one source curve at a time directly to the operation. It reads saved Part Boundaries before and after through the CAM API, checks every new curve tag, Open/Closed, ON/TANTO, the TANTO side and inherited stock and tolerance. The operation editor applies a settings change only when required. A final read checks the saved result. Existing geometry is compared by its members and settings, including actual saved boundaries without source tags.

The report lists editor rows and saved boundaries separately and shows expected and actual counts. A missing, duplicated or grouped curve is identified by tag. If a change fails, the journal rolls back the run and checks the original boundaries and inheritance. A successful run is one **Ctrl+Z** step.

The result window is resizable. **«Копировать всё»** copies the entire report, including text outside the viewport. Error reports include the stage, exception type, NX error code when available and stack trace. Reports remain in memory and are never sent automatically.

## Limits and verification

- Supports classic PLANAR_MILL and PLANAR_PROFILE operations represented by the NXOpen `PlanarMilling` class. Other operation classes are rejected.
- The operation must belong to the CAM work part. Component curves, body edges, faces and bodies are not selected.
- If the operation inherits nonempty Part Boundaries from parent geometry, first define local boundaries in NX. The script does not change the parent geometry.
- No new curves, toolpath generation or automatic part saving. No files, folders, logs, temporary files or disk settings are created.
- V1.02 compiled against the installed Designcenter 2606 NXOpen libraries on .NET 10. A fixture exercised the journal entry point with 36 curves, a placeholder row, existing boundaries, both TANTO sides, repeat selections and rollback on lost or merged curves. A live run on a CAM part is still unverified.

[Download script](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/scripts/NX_Planar_Add_Curves.cs) · [Catalog](../../README.en.md) · [Changelog](../../CHANGELOG.en.md)
