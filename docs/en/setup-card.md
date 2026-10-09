# Setup sheets

[Русский](../setup-card.md) | **English**

Script: [NX_Setup_Prototype.py](../../scripts/NX_Setup_Prototype.py). [All scripts](../../README.en.md#scripts).

Creates one standalone HTML document containing the selected setups, CAM tables and embedded images. Edit, save and print it in a browser.

## Latest release

**V2.49 · 2026-10-09**

- The startup menu has an initially unchecked “Create a faceted 3D model on the first sheet” option. When it is off, NX fits visible geometry to the window without changing the current orientation and captures an image for the first sheet.
- Enabling it reveals the accuracy coefficient (default 1) and maximum triangle count (default 500,000). Accuracy must be greater than 0 and no greater than 1; decimal commas and points are accepted. The triangle limit is a positive integer and may be increased.
- A smaller coefficient reduces the detail of newly generated meshes and normally their data size. Part dimensions are unchanged. At 1 the tolerances are 0.01 mm and 3°; at 0.5 they are 0.02 mm and 6°. Tolerances are divided by the coefficient, with the angular tolerance capped at 180°.
- The selected limit applies to the entire model and is also used by the browser. Exceeding it produces a short instruction to reduce accuracy or increase the triangle limit. The previous sheet is retained.
- Re-export replaces the previous mesh with the new screenshot or the previous screenshot with the new mesh, including partial setup updates. The tool catalog remains.

30 portable checks passed, covering coefficient propagation, millimetres/inches, configurable limits, concise limit errors, fitting before capture and camera restoration after failures. Chromium checks covered mesh → screenshot → mesh through browser saving and partial re-export. Four native Windows dialog checks were skipped on Linux; execution in NX / Designcenter has not been verified. See the [changelog](../../CHANGELOG.en.md).

## Building the sheet in NX

- Select one or more setups in the script's Program Order tree. A selected folder includes all its operations and nested folders. A parent and its descendant cannot both be selected. Preselection in the NX navigator does not select setups; only the Zmin naming option uses it.
- The setup name, view orientation and IPW source come from the first operation's MCS in the selected folder.
- Choose visible part and fixture components separately for each setup. NX immediately previews the selection. Original visibility is restored after selection and capture.
- Top, side and isometric views are created relative to the MCS, each with matching-camera IPW and NO IPW variants.
- X/Y/Z arrows are anchored at the MCS origin. Geometry is fitted with margins.
- The document includes tools for each setup and a project-wide tool catalog. Duplicate T numbers trigger a warning and are marked.
- The first sheet contains the project tool catalog. Enabling the startup option adds a rotatable 3D model of the initially visible project geometry in the original NX view. Images and model geometry are embedded in the HTML.
- Progress shows the current stage and percentage. If generation is incomplete, the previously completed HTML is retained.

### First-sheet model

“Create a faceted 3D model on the first sheet” resets to off on every launch. In this mode NX fits the current view without changing its orientation, captures it and embeds the image in the first sheet. The camera and display settings are restored after capture, including on failure. The PNG uses the existing sheet staging folder and is deleted immediately after embedding.

Enabling the option reveals the coefficient on its right and the triangle limit below. Smaller coefficients produce coarser meshes and normally smaller HTML files. Triangle counts depend on the geometry and are not specified by multiplying the limit by the coefficient. The coefficient does not change part dimensions, IPW or setup views.

The default is 500,000 triangles for the entire model, including repeated components. Enter any integer from 1 to 2,147,483,647, with or without spaces. Actual capacity depends on NX and browser resources. The coefficient controls solid/sheet body tessellation; existing NX facet bodies retain their existing mesh. Settings are not persisted on disk.

### Preparation options

All three checkboxes reset to off on every launch. They change the current CAM project, so the generated sheet uses the resulting names and descriptions.

| Option | Effect |
| --- | --- |
| «Нумеровать операции» (Number operations) | Renumbers all operations in each selected folder, including nested folders, in Program Order. Up to 99 operations: `01_`, `02_`…; from 100: `001_`, `002_`… Existing leading numbers are replaced. With the option off, numbering is left unchanged. |
| «Добавить параметры инструмента в описание (description)» (Add tool parameters to description) | Writes General Description and Cutter Description for supported tools throughout the current part, including unused tools. Format: `⌀6` or `⌀6_T2_H3_D4`. Missing D is omitted; a readable zero remains `D0`. Geometry and T/H/D parameters themselves do not change. |
| Add Zmin to operation names | Uses selection captured before opening the menu. Selected operations restrict the scope; a completely empty selection means all project operations. Replaces terminal numeric `_Z…` suffixes, for example `MILL_Z-3.125`. Folder-only selection or a selection-reading error does not authorize renaming the entire project. |

Use the mouse or Space to toggle checkboxes. Double-clicking a label toggles it once. Description formats appear in the same window without another dialog.

Supported tool families are Mill, Drill, Barrel, Tcutter and MillForm with an accessible positive diameter. Other descriptions remain unchanged. A per-tool error rolls back that tool's partial write while allowing other tools to be processed. Unavailable/out-of-date toolpaths and conflicting Zmin names are skipped with explanations in the NX result. Numbering conflicts or a general preparation error stop generation and roll back preparation. These diagnostics are not inserted into the HTML.

## Operation and tool data

The operation table retains program-folder hierarchy.

| Column | Data |
| --- | --- |
| Operation | Name and toolpath state: calculated, missing, needs recalculation, suppressed or unavailable. |
| MCS | Operation machining coordinate system. |
| Tool change | Marks a transition to a different tool. |
| Tool / T | Tool name and number. |
| Time | Toolpath time; groups show the total for active toolpaths. |
| Feed / spindle speed | CAM machining parameters. |
| Wall / floor stock | Stock values where supported by the operation. |
| Zmin | Minimum calculated toolpath position along the actual tool axis, relative to the operation's MCS origin, in CAM-part units. |

Available tool information includes overhang H, cutting length L and relieved length HL; the sheet explains these labels. Toolpath time does not include every machine overhead.

Zmin uses the [standalone Zmin algorithm](zmin.md): `dot(P - O, unit(ToolAxis))` at saved CL points. Both Three and Five motions supply their actual tool vectors. Names change only when the startup Zmin option is enabled. For rotary machining, the MCS origin must lie on the part's rotation axis. Constant-axis arcs/helices include internal extrema; varying-axis arcs without interpolation data and compound paths are unsupported. This is not a simulation of postprocessed machine movement.

## Editing in the browser

- Fill in programmer, part/setup names, date, stock and notes.
- Enter X/Y/Z locating information manually or choose supplied wording.
- Notes grow without shrinking the font. Drag the lower boundary to set a height, which is saved.
- Jump to a setup using the top-bar list.
- Hide operation columns, wrap long names, scale tables and adjust column widths; return to **«Автоширина» (Auto width)** when needed.
- Auto width shows full tool names and other values, allocating the remaining space to Operation. Operation names use ellipses or wrap when enabled for that sheet. If necessary, the whole table fits to the page without internal horizontal scrolling. Manual widths are saved.
- Move selected operations onto the setup's first sheet or back using buttons or drag-and-drop. The optional limit is 25 operations on the first sheet; available space also limits placement.
- Operations paginate automatically, with repeated group headings and continuous page numbers.
- Click an image to switch IPW / NO IPW; pan, zoom and reset to 100% or center.
- Adjust MCS-arrow size, outline color and outline thickness independently of table scale.
- Double-click any slider label to reset only that parameter to 100%, without selecting text. Hover for its tooltip. Click the percentage to type an integer within the control's range; Enter or leaving the field applies it, Esc cancels.
- Add image cells up to a 6 × 6 grid where space permits. Paste your own images with Ctrl+V; delete, move or swap cells. Multiple cells can be selected. Add/delete/move operations participate in Undo.
- Resize cell boundaries and image-block height, or align cells.
- Rotate the overall 3D model with the mouse, pan with Shift and zoom with the wheel. **«Вписать» (Fit)** and **«Исходный вид» (Original view)** are available. Model scale and placement are saved and appear in print.
- **Original view** above a sheet restores that sheet's original elements, sizes, layout and settings. Deleted original images return and subsequently added cells are removed. The project sheet restores its initial model view; a setup sheet also restores MCS arrows, images and operation placement. Notes, locating information, global column settings and other sheets are retained. The original state is embedded in HTML, and the reset can be undone in one step.
- Use **«Отменить» (Undo)** in the top bar, **«Отменить действие» (Undo action)** by a setup, or Ctrl+Z outside a text field. Up to 100 actions, including image deletion, can be undone in sequence. Saving retains history; closing or reloading the HTML clears it. Inside a text field, Ctrl+Z undoes typing.
- When opened by NX, the sheet saves back to its source HTML automatically about a second after editing pauses. No initial file selection is needed. **«Сохранить карту» (Save sheet)** or Ctrl+S saves immediately.
- When HTML is opened directly from a folder, Save sheet asks you to select the original file if the browser supports file access; otherwise it downloads HTML. **«Скачать копию» (Download copy)** is available on a save error.
- **«Печать» (Print)** opens browser printing with A4 pages. A system PDF printer can produce PDF.

### Autosave conditions

Edit the tab opened by the script at `http://127.0.0.1:<port>/card/…/`. A helper starts automatically as a separate process and stays active while the launching NX process remains open. It performs file operations only, without NXOpen, so browser service does not depend on Python threads continuing inside NX after the journal finishes. Repeated runs reuse the helper.

It first tries the existing `python.exe` from NX's environment. If unavailable or unable to start, it identifies the DLL of NX's already loaded Python and uses standard Windows PowerShell to launch it. The launcher works in memory and uses NX's existing library locations. Nothing is installed; Python is not searched for on PATH. No separate helper file, new INI, log, backup or other service file is created, and Windows/PowerShell policies are not changed.

A real HTTP response is checked before opening the browser. If both launch methods fail, policy blocks startup or the helper does not respond, the completed HTML opens directly with normal browser saving. In that mode, autosave without first selecting a file is unavailable. NX shows the explanation; the HTML does not add a warning.

The helper keeps the output path in memory; a page cannot supply an arbitrary write path. Its session key is not saved into HTML. When upgrading from V2.38, use the new tab opened by V2.39 or later; the old in-NX helper is stopped.

Requests are accepted only over loopback, from the same origin and with the page's key. Identity and revision checks prevent an old tab from overwriting another tab or a fresh NX export. On conflict, automatic retries stop: download a copy before reloading, then merge your edits into the current sheet manually.

After a confirmed manual Save sheet or Ctrl+S, a green **«Карта сохранена» (Sheet saved)** message appears for three seconds beside the button without moving the layout. Cancellation, failure or merely handing a download to the browser does not confirm a saved file. Autosave does not show this message.

During writing, the button shows **«Сохранение…» (Saving)**. On failure, it offers **«Повторить сохранение» (Retry save)** and Download copy, without pop-ups or yellow warnings. A draft remains in browser storage where possible, but it does not replace the saved HTML. Closing NX stops autosave; launching the journal again opens a sheet through the helper. Saved HTML remains standalone and suitable for sharing and printing.

Before closing the tab/NX or exporting again, click Save sheet and wait for the write to finish. Closing immediately before the delay expires may lose the last edit. Autosave writes the existing file without a temporary copy; ordinary write failures restore previous bytes from memory, but power-loss recovery is not guaranteed. One saved HTML is limited to 256 MiB; larger sheets can still be downloaded as a copy.

## Regenerating

Running the script again updates selected setups in the existing HTML. Unselected setups retain their saved formatting. Selecting a parent folder merges its previous separate subfolder entries into that parent.

Regenerated setups retain saved notes, their headings and heights, and X/Y/Z locating information. The rest of the selected setup's layout is rebuilt; not every manual layout change is preserved.

## Use

1. Open a saved CAM project as both work and displayed part. Prepare toolpaths and the required IPW geometry.
2. Run the journal, choose setup folders, review warnings and enable preparation options only if needed.
3. Select visible components for each setup and wait for generation.
4. The sheet and output folder open; the folder path is copied to the clipboard.
5. Edit the opened tab, wait for saving and print. Keep NX open while editing.

## Settings and output

- `NX_Setup_Prototype.ini` beside the script: `[SetupCard] programmer` sets the default programmer name; blank means an empty field.
- Output: `<PRT folder>\Карты Наладки\<project name>\<project name>.html`. The Russian folder name is literal; invalid Windows filename characters are replaced.
- Capture and working files are removed after generation. The finished document embeds its images.
- A single staging folder with a short `.nx_…` name is used beside the PRT instead of the old nested `.capture/setup-…` layout. If output is redirected to another volume, staging stays on the output volume to permit atomic HTML replacement.
- Python file operations, regeneration, INI and autosave support extended Windows paths without registry changes or renaming final files. NX receives a normal image path or an existing 8.3 short path. All capture paths are checked before model building and capture. If even the PRT folder path is too long and no short names exist, generation stops and asks you to save the project through NX into a shallower folder; the previous HTML is retained. Explorer and other applications have their own path limits.
- The PRT is not saved and toolpaths are not recalculated. After capture, visibility is restored, the triad is enabled and the NX view is fitted.
- Missing, unavailable, out-of-date or unsupported paths leave Zmin blank. An unavailable ToolAxis is never replaced with fixed MCS Z. Check values on a known control project; live execution in the target NX / Designcenter requires separate validation.

[Configuration](configuration.md#setup-sheets).

