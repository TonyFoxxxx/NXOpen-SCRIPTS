# Curves for PLANAR_MILL / PLANAR_PROFILE

[Русский](../planar-add-curves.md) | **English**

## Latest release

**V1.10 · 2026-11-24** — The header gear opens light and dark themes, a continuous colour picker and HEX input. Dragging the marker immediately updates the buttons and checkboxes. The compact ON dialog expands the two side selectors in TANTO. The milling icon has three fixed blue toolpaths. The shared style button saves a default for scripts that support it.

File: [NX_Planar_Add_Curves.cs](../../scripts/NX_Planar_Add_Curves.cs). Updater name: **«Кривые в PLANAR_MILL / PROFILE»**. This is a separate script for existing curves; [engraving centerline construction](planar-mill-curves.md) remains a separate tool.

## Workflow

1. Prepare an existing **PLANAR_MILL** or **PLANAR_PROFILE** in Manufacturing, including the tool, orientation, levels, stock, tolerance, approaches and retracts.
2. Close the operation editor and select one operation in **Operation Navigator**. Its name can be arbitrary.
3. Run `NX_Planar_Add_Curves.cs` using **Tools → Journal → Play** or a custom button.
4. Check **Part Boundaries** for the part, **Blank Boundaries** for the stock, or both. Choose **ON** or **TANTO**. In TANTO select one side for closed curves and one for open curves. The first launch defaults to Part, ON and manual plane selection.
5. Leave **«Выбрать плоскость границ»** checked and click **«Выбрать плоскость →»**. Use the native plane control in **«Выберите плоскость в модели»**. Open **Plane Dialog** for the full method list. For an offset, choose **At Distance**, pick the reference face or plane, enter the distance and check the direction in the preview. Confirm the plane with **OK**. For automatic planes, clear the checkbox in the starting dialog.
6. Select the curves requested by each NX prompt. If both sets are enabled, confirm the part curves first, then select the blank curves separately. Each new curve gets one boundary in its own set. Cancelling either picker leaves both sets as they were.

Confirming the start dialog remembers the two boundary checkboxes, the plane checkbox, ON/TANTO and both side selections for this Windows user. Cancel keeps the previous preferences. The Plane dialog shows the last confirmed construction method. After **At Distance**, choose a fresh reference and click **«Повторить последнее смещение»** to preview the saved signed distance, converted into the current part units. To enter a new distance, use the native **At Distance** control. No geometry identifiers are stored.

## Appearance

The gear beside the close button opens **«Оформление»**. Choose **«Тёмная»** or **«Светлая»**. Drag the right-hand strip to select a hue and the marker in the square to set saturation horizontally and brightness vertically. The window previews every move while the mouse button is held. You can also enter a six-digit HEX colour such as **#FF790F**. Arrow keys adjust the focused picker, Shift makes larger steps, and Esc closes the appearance panel.

The journal remembers the theme and colour for the next launch, including when you cancel the start dialog. **«Для всех скриптов»** writes a shared default for compatible scripts. PLANAR uses this default when it has no local appearance preference. The header icon uses the accent for the cutter and stepped part. Its three passes keep their original blue **#00A9D0**. The results window also uses the chosen theme.

With **ON**, the tool stays on the boundary. Switching to **TANTO** shows Inside / Outside and Left / Right and restores the last selected sides. The plane checkbox shows **«Выберите плоскость в окне Plane»** when enabled.

## Tool position and sides

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
- Existing assignments are skipped separately in each set. The same source curve can appear once in Part and once in Blank. Previous assignments retain their settings.

## Preservation and report

The journal appends one source curve at a time directly to the operation. It reads the saved Part and Blank Boundaries before and after through the CAM API, checks every new curve tag, Open/Closed, ON/TANTO, the TANTO side and inherited stock and tolerance. The operation editor applies a settings change only when required. The reopened editor verifies the saved plane position and normal for every new boundary when a plane was selected. Existing geometry is compared by its members and settings, including actual saved boundaries without source tags. The Open/Closed totals use the values read back from the reopened editor. A missing curve, an unconfirmed shape or a conflict between named CAM and editor types triggers a verified rollback.

The report lists editor rows and saved boundaries separately and shows expected and actual counts. A missing, duplicated or grouped curve is identified by tag. If a change fails, the journal rolls back the run and checks the original boundaries and inheritance. A successful run is one **Ctrl+Z** step.

The result window is resizable. **«Копировать всё»** copies the entire report, including text outside the viewport. Error reports include the stage, exception type, NX error code when available and stack trace. Reports remain in memory and are never sent automatically.

## Limits and verification

- Supports classic PLANAR_MILL and PLANAR_PROFILE operations represented by the NXOpen `PlanarMilling` class. Other operation classes are rejected.
- The CAM operation and selected curves must belong to the work part. A planar face or Datum Plane can also be selected on a positioned assembly component.
- When a selected set inherits populated boundaries from a geometry parent, define local boundaries in NX before adding curves to that set.
- User preferences in `%LOCALAPPDATA%\NXOpen-Scripts` are stored in `PlanarAddCurves.settings`, `PlanarAddCurves.appearance`, `Appearance.settings` for CAM options, local appearance and the shared style respectively. The operator generates the toolpath and saves the part as usual.
- V1.10 was compiled against the Designcenter 2606 journal libraries. Background tests exercise the native dialogs, live colour updates before release, saved local and shared settings, sides, plane offsets, separate Part/Blank selections and rollback. CAM transactions were tested with an API fixture. A live machining operation requires verification in NX.

[Download script](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/scripts/NX_Planar_Add_Curves.cs) · [Catalog](../../README.en.md) · [Changelog](../../CHANGELOG.en.md)
