# NXOpen-SCRIPTS

[Русский](README.md) | **English**

Scripts for Siemens NX / Designcenter: CAM preparation, setup sheets, postprocessing, drawing formatting and export. Run them inside NX as journals or through custom ribbon buttons.

Author: **by @Tony_Foxxx**. Source code is available under the [MIT License](LICENSE). See [third-party notices](THIRD_PARTY_NOTICES.en.md) for separately licensed resources.

## Compatibility

Target versions: **NX 2506.8100** and **Designcenter 2606 Build 4002** on Windows. Other builds have not been confirmed compatible. Each guide describes its requirements and limitations. This is English documentation; labels shown in Russian in the scripts are retained below to help you find the controls.

## Scripts

| File | Version | Purpose | Guide |
| --- | --- | --- | --- |
| [NX_Setup_Prototype.py](scripts/NX_Setup_Prototype.py) | V2.48 | Editable setup sheets with views, tools and operations. | [All features](docs/en/setup-card.md) |
| [NX_Postprocess_To_Machine.cs](scripts/NX_Postprocess_To_Machine.cs) | V1.45 | NC programs and FANUC BIN output to machine folders or removable media. | [All features](docs/en/postprocess.md) |
| [NX_Operation_Zmin.py](scripts/NX_Operation_Zmin.py) | V1.02 | Add minimum toolpath Z to operation names. | [All features](docs/en/zmin.md) |
| [NX_Rename_Operations_In_Selected_Folder.cs](scripts/NX_Rename_Operations_In_Selected_Folder.cs) | V1.04 | Rename operations in CAM tree order. | [All features](docs/en/rename-operations.md) |
| [NX_Number_Program_Folders.cs](scripts/NX_Number_Program_Folders.cs) | V1.03 | Number program folders in tree order using an XLSX register. | [All features](docs/en/number-program-folders.md) |
| [NX_Tool_D_To_Description.cs](scripts/NX_Tool_D_To_Description.cs) | V1.06 | Fill tool descriptions with diameter and T, H, D numbers. | [All features](docs/en/tool-description.md) |
| [NX_ESKD_Format_GOST_A.cs](scripts/NX_ESKD_Format_GOST_A.cs) | V1.36 | ESKD drawing borders, title blocks and annotation formatting. | [All features](docs/en/eskd.md) |
| [NX_Export_Drawing_To_PDF.cs](scripts/NX_Export_Drawing_To_PDF.cs) | V1.11 | Export the current sheet to PDF with adjustable line weights. | [All features](docs/en/export-pdf.md) |
| [NX_Export_Current_View_To_DXF.cs](scripts/NX_Export_Current_View_To_DXF.cs) | V1.08 | Export edges, the outer outline or selected curves to DXF. | [All features](docs/en/export-dxf.md) |
| [NX_Rename_Assemblies.cs](scripts/NX_Rename_Assemblies.cs) | V1.05 | Rename part and assembly files and update component references. | [All features](docs/en/rename-assemblies.md) |
| [NX_Open_Project_Folder.cs](scripts/NX_Open_Project_Folder.cs) | V1.01 | Open the current PRT folder in Windows Explorer. | [All features](docs/en/open-project-folder.md) |
| [NX_Open_Setup_Cards_Folder.py](scripts/NX_Open_Setup_Cards_Folder.py) | V1.01 | Open the current project's setup-sheet folder. | [All features](docs/en/open-setup-cards-folder.md) |
| [NX_Update_Scripts.py](scripts/NX_Update_Scripts.py) | V1.17 | Install and update selected scripts from GitHub or a folder. | [All features](docs/en/updater.md) |

Each guide covers features, use, settings, output files and limitations. See the [changelog](CHANGELOG.en.md) for release history.

### Latest update — ESKD V1.36 · 2026-10-09

- The upper duplicate-designation box is horizontal on all supported sheets.
- Long title-block text wraps first, then uses a smaller font if needed, keeping the table dimensions fixed.
- Added a final check for cell fitting settings and overflow hashes after the sheet refresh.

[Changes in V1.36, checks and limits](docs/en/eskd.md#latest-release).

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
| Numbering | [Download](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/config/NX_Numbering_Settings_v1.0.example.ini) | `C:\ProgramData\3_NX_DATA\NX_Numbering_Settings_v1.0.ini` and the register specified by `RegistryFile`. |
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
