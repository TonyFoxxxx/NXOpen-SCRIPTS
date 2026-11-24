# NXOpen-SCRIPTS

[Русский](README.md) | **English**

Scripts for Siemens NX / Designcenter: CAM preparation, setup sheets, postprocessing, drawing formatting and export. Run them inside NX as journals or through custom ribbon buttons.

Author: **by @Tony_Foxxx**. Source code is available under the [MIT License](LICENSE). See [third-party notices](THIRD_PARTY_NOTICES.en.md) for separately licensed resources.

## Compatibility

Target versions: **NX 2506.8100** and **Designcenter 2606 Build 4002** on Windows. Other builds have not been confirmed compatible. Each guide describes its requirements and limitations. This is English documentation; labels shown in Russian in the scripts are retained below to help you find the controls.

## Scripts

| File | Version | Purpose | Guide |
| --- | --- | --- | --- |
| [NX_Setup_Prototype.py](scripts/NX_Setup_Prototype.py) | V2.72 | Editable setup sheets with views, tools and operations. | [All features](docs/en/setup-card.md) |
| [NX_Postprocess_To_Machine.cs](scripts/NX_Postprocess_To_Machine.cs) | V1.50 | NC programs and FANUC BIN output to machine folders or removable media. | [All features](docs/en/postprocess.md) |
| [NX_Operation_Zmin.py](scripts/NX_Operation_Zmin.py) | V1.09 | Add minimum Z to operation names, including supported spot-drilling and through-hole drilling cycles. | [All features](docs/en/zmin.md) |
| [NX_Rename_Operations_In_Selected_Folder.cs](scripts/NX_Rename_Operations_In_Selected_Folder.cs) | V1.04 | Rename operations in CAM tree order. | [All features](docs/en/rename-operations.md) |
| [NX_Clean_Operation_Names.cs](scripts/NX_Clean_Operation_Names.cs) | V1 | Clean copy/instance suffixes and counters in all operation names while preserving Z. | [All features](docs/en/clean-operation-names.md) |
| [PlanarCHIK.cs](scripts/PlanarCHIK.cs) | V1.01 | Set Tool Position On for selected PLANAR_MILL boundaries and optionally apply a shared Stock value. | [All features](docs/en/planarchik.md) |
| [NX_Planar_Add_Curves.cs](scripts/NX_Planar_Add_Curves.cs) | V1.09 | One boundary per curve in Part and/or Blank, separate selections, a shared plane with offsets, ON/TANTO and saved preferences. | [All features](docs/en/planar-add-curves.md) |
| [NX_PlanarMill_Select_Curves.cs](scripts/NX_PlanarMill_Select_Curves.cs) | V1.18 | Engraving centerlines and D/2 dots for an existing or new PLANAR_MILL. | [Full guide](docs/en/planar-mill-curves.md) |
| [NX_Engraving_Operation_Diagnostic.cs](scripts/NX_Engraving_Operation_Diagnostic.cs) | V1.01 | Engraving-operation settings snapshot with a text report and JSON export. | [All features](docs/en/engraving-operation-diagnostic.md) |
| [NX_GOST_Smooth_Text.cs](scripts/NX_GOST_Smooth_Text.cs) | V1.01 | Create single-stroke GOST A text from lines and smooth splines using an external FON. | [All features](docs/en/gost-smooth-text.md) |
| [NX_Number_Program_Folders.cs](scripts/NX_Number_Program_Folders.cs) | V1.05 | Number program folders in tree order using an XLSX register. | [All features](docs/en/number-program-folders.md) |
| [NX_Tool_D_To_Description.cs](scripts/NX_Tool_D_To_Description.cs) | V1.06 | Fill tool descriptions with diameter and T, H, D numbers. | [All features](docs/en/tool-description.md) |
| [NX_Delete_Empty_Tools.cs](scripts/NX_Delete_Empty_Tools.cs) | V1 | Delete empty CAM tools without operations, with a preview and one undo step. | [All features](docs/en/delete-empty-tools.md) |
| [NX_ESKD_Format_GOST_A.cs](scripts/NX_ESKD_Format_GOST_A.cs) | V1.41 | ESKD drawing borders, title blocks and annotation formatting. | [All features](docs/en/eskd.md) |
| [NX_Export_Drawing_To_PDF.cs](scripts/NX_Export_Drawing_To_PDF.cs) | V1.13 | Export the current sheet to PDF with adjustable line weights. | [All features](docs/en/export-pdf.md) |
| [NX_Export_Current_View_To_DXF.cs](scripts/NX_Export_Current_View_To_DXF.cs) | V1.08 | Export edges, the outer outline or selected curves to DXF. | [All features](docs/en/export-dxf.md) |
| [NX_Rename_Assemblies.cs](scripts/NX_Rename_Assemblies.cs) | V1.05 | Rename part and assembly files and update component references. | [All features](docs/en/rename-assemblies.md) |
| [NX_Save_Project_And_Archive.py](scripts/NX_Save_Project_And_Archive.py) | V1 | Save the work-part PRT and replace one sibling ZIP without temporary files. | [All features](docs/en/save-project-archive.md) |
| [NX_Open_Project_Folder.cs](scripts/NX_Open_Project_Folder.cs) | V1.01 | Open the current PRT folder in Windows Explorer. | [All features](docs/en/open-project-folder.md) |
| [NX_Open_Setup_Cards_Folder.py](scripts/NX_Open_Setup_Cards_Folder.py) | V1.01 | Open the current project's setup-sheet folder. | [All features](docs/en/open-setup-cards-folder.md) |
| [NX_Mirror_Project.py](scripts/NX_Mirror_Project.py) | V1.30.10 | CAM-copy trial; historical-support access and one-body experiments with mandatory rollback. No components or automatic save. | [All features](docs/en/mirror-project.md) |
| [NX_Update_Scripts.py](scripts/NX_Update_Scripts.py) | V1.20 | Install and update selected scripts from GitHub or a folder. | [All features](docs/en/updater.md) |

Each guide covers features, use, settings, output files and limitations. See the [changelog](CHANGELOG.en.md) for release history.

### Latest update — Curves for PLANAR V1.09 · 2026-11-24

The rounded start dialog remembers confirmed settings. Select **Part Boundaries**, **Blank Boundaries**, or both. With both checked, pick the part curves first and the blank curves second. One Undo step covers both sets. [Guide](docs/en/planar-add-curves.md#latest-release).

V1.09 source SHA-256 is `34bfd81c0f8065032c0a6440fed4d2b8804e959f69b7e65472c3530ad89dbb8a`.

### Also released — Engraving centerlines for PLANAR_MILL V1.18 · 2026-11-24

An NX interrupt during startup now opens a Windows **Retry / Cancel** dialog. Retry acknowledges the interrupt and reads the work part once more. Cancel ends the journal before any CAM edit. [Guide](docs/en/planar-mill-curves.md#latest-release).

V1.18 source SHA-256 is `05355bc3f2fa5d50799292b41571b4a645be5e72a9c782f258d3d82fc41a809d`.

### Also released — Engraving centerlines for PLANAR_MILL V1.17 · 2026-11-24

The journal now reads the work part by its UF tag. If NX reports **User abort** at startup, the journal exits normally. An interruption during editing rolls back the curves, boundaries and any new operation from that run. [Guide](docs/en/planar-mill-curves.md#latest-release).

V1.17 source SHA-256 is `9470e594d7f6a105d2031025c533f29c2e61224ce62295a6e6af13bb37a3a5aa`.

### Also released — Engraving centerlines for PLANAR_MILL V1.16 · 2026-11-24

The diagonal meets the crossbar of a bevelled ‘4’ at a sharp corner. The curved serif of a ‘1’ reaches the nose before turning onto the stem. [Guide](docs/en/planar-mill-curves.md#latest-release).

V1.16 source SHA-256 is `c470268a5e5d1f111604d247e9514c98cc4d5ab06f4436954d19e76700358a03`.

### Also released — Curves for PLANAR V1.08 · 2026-11-24

The NX Plane constructor now sets the common boundary plane. Choose **At Distance** for an offset, or use a construction method such as **At Angle** or **Bisector**. [Guide](docs/en/planar-add-curves.md#latest-release).

### Also released — Curves for PLANAR V1.07 · 2026-11-24

The NX selection dialogs now start with “Выберите плоскость в модели” (Select a plane in the model) and “Выберите кривые в модели” (Select curves in the model). The action stays at the front of a narrow window. [Guide](docs/en/planar-add-curves.md#latest-release).

### Also released — Engraving operation diagnostic V1.01 · 2026-11-23

Select one operation and click **Save JSON** to capture the settings. [Guide](docs/en/engraving-operation-diagnostic.md).

V1.01 source SHA-256 is `27edbd5b40c68ec73b3525f443966e2afa83bfc42ef5d0331cf6c51a031f03b8`.

### Also released — Curves for PLANAR V1.05 · 2026-11-23

Fixed the journal compilation error on line 425 in Designcenter 2606. The journal now counts and sorts the boundary type codes using the collections available to the NX journal compiler. [Guide](docs/en/planar-add-curves.md#latest-release).

### Also released — Setup sheets V2.72 · 2026-11-19

The × in the upper-right corner removes one sheet after confirmation. Ctrl+Z restores it. Re-export replaces each selected setup at its saved position. The script resolves short paths from older cards against Program Order. Selecting a subfolder of a saved setup shows which folder to select to update it.

V2.72 preserves the original line endings in the markup and notes of unselected setups during partial updates on Windows. Update **«Карта наладки»** to **V2.72** in the updater and regenerate the HTML. [Sheet deletion and partial updates](docs/en/setup-card.md#latest-release).

### Also released — Mirrored CAM project trial V1.30.10 · 2026-11-16

V1.30.09 passed historical contexts without 821049 and rolled back. The edge was traced to Blend selection; Part Box and two historical inputs were absent from the separate map. V1.30.10 tests a smaller original-history selection in an independent editor without assignment, Validate or Commit. Primary blockers remain. [Result and limits](docs/en/mirror-project.md#map-selection-v13010).

All **855 focused checks outside NX** pass, including **147 using C fixtures**. Next run: **V1.30.10** from the saved source PRT with the same plane. Return parent-path, reduced-view and rollback results; do not save the experimental copy. Complete CAM transfer remains unconfirmed.

## Installation

1. Create a writable script folder. The default is `C:\ProgramData\NX_SCRIPTS`.
2. [Download the updater](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/scripts/NX_Update_Scripts.py) into that folder as `NX_Update_Scripts.py`.
3. In NX, open **Tools → Journal → Play**, select the updater and run it.
4. Set the working folder. GitHub is the default source. Click **«Проверить» (Check)**.
5. Select the scripts you need and click **«Применить выбранное» (Apply selected)**.
6. Configure them using their guides. For postprocessing, select TCL/DEF files through **«Пути к постпроцессорам» (Postprocessor paths)** and machine roots through **«Папки со станками…» (Machine folders)** in the destination window. Set a numbering range and register for the numbering script; supply a compatible external font for ESKD formatting.

Check creates the updater's own INI if missing. Updates preserve existing settings for other scripts. Installed scripts run through the same Journal menu or ribbon buttons.

## Create a launch button once

**Create each script's button only once.** It points to a local file at a fixed path. The updater replaces that file's contents, so the button keeps working.

1. Right-click an empty area of the NX ribbon and choose **Customize**.
2. On **Commands**, select **New Item** (called **New Button** in some versions), then drag **New User Command** onto the required toolbar.
3. Keep Customize open, right-click the new button and select **Edit Action**.
4. Choose **Execute an Action** or **Journal File**, depending on NX version. Browse to `C:\ProgramData\NX_SCRIPTS\NX_Update_Scripts.py`. For another script, select its `.py` or `.cs` file.
5. Set a name and optional icon, confirm, close Customize and test the button.

[Detailed button and role instructions](docs/en/installation.md#buttons). Moving the scripts to a different folder requires updating button paths.

The old updater filename, `NX_Update_Script_Buttons.py`, requires a [one-time migration](docs/en/installation.md#migrating-from-the-old-updater-name). Subsequent updates use `NX_Update_Scripts.py`.

## INI settings

Files in `config/` ending in `.example.ini` are examples. On a new GitHub installation, the updater creates a working INI if it is absent.

| Script | Example | Working settings |
| --- | --- | --- |
| Setup sheets | [Download](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/config/NX_Setup_Prototype.example.ini) | `NX_Setup_Prototype.ini` beside the script; `[SetupCard] programmer`. |
| Postprocessing | [Download](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/config/NX_Postprocess_To_Machine.example.ini) | `NX_Postprocess_To_Machine.ini` beside the script; your posts and machine folders. |
| ESKD | [Download](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/config/NX_ESKD_Settings.example.ini) | `NX_ESKD_Settings.ini` beside the script; title-block text and formatting. |
| Numbering | [Download](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/config/NX_Numbering_Settings_v1.0.example.ini) | `NX_Numbering_Settings_v1.0.ini` beside the script and the register specified by `RegistryFile`; the updater migrates the old pair when updating. |
| Updater | [Download](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/config/NX_Update_Scripts.example.ini) | `NX_Update_Scripts.ini` beside the script; source and working folder. |

For manual installation, remove `.example` from the filename and fill in your settings. Do not replace existing INI files or numbering history with examples. See [configuration](docs/en/configuration.md).

## Updates

Run the updater and click **«Проверить» (Check)** to compare installed and available versions. Select the required rows and click **«Применить выбранное» (Apply selected)**.

Stable filenames keep NX buttons working. New scripts and locally modified files require manual selection. Downgrades are blocked. The updater replaces itself after its window closes; its new version runs next time.

[Updater features, file states and troubleshooting](docs/en/updater.md).

## Repository layout

| Path | Contents |
| --- | --- |
| `scripts/` | Scripts with stable filenames. |
| `config/` | Example INI files. |
| `templates/` | Empty register for the first numbering installation. |
| `manifest.json` | Updater catalog. |
| `docs/` | Russian script, installation and configuration guides. |
| `docs/en/` | English versions of the guides. |
| `tools/`, `tests/`, `.github/workflows/` | Validation tools and automated tests. |

## Development

See [publishing instructions](docs/en/publishing.md) for adding scripts, versioning and release checks. Keep the Russian and English documentation in sync when changing user-visible behavior.

[GitHub Actions](https://github.com/TonyFoxxxx/NXOpen-SCRIPTS/actions) checks the catalog and updater logic on Windows and Linux. Windows checks also exercise postprocessor drawing in a real TreeView. These tests do not run NXOpen; execution inside NX must be tested separately.

