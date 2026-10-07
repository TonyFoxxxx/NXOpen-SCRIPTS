# Installation

[Русский](../installation.md) | **English**

[All scripts](../../README.en.md#scripts) · [Configuration](configuration.md) · [Updater](updater.md)

## Requirements

Windows with a supported NX / Designcenter installation, a writable script folder and permission to run NX journals. The targets are NX 2506.8100 and Designcenter 2606 Build 4002; other builds are unconfirmed. Python scripts use NX's own Python and NXOpen environment; a separate Python installation is not required. C# journals require journal playback support in NX.

## Install through the updater

1. Create `C:\ProgramData\NX_SCRIPTS`, or another writable working folder.
2. [Download the raw updater](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/scripts/NX_Update_Scripts.py) and save it there as `NX_Update_Scripts.py`. Check that the browser has not added `.txt` or saved a GitHub HTML page.
3. In NX, choose **Tools → Journal → Play**. Alt+F8 may open this command if assigned in your configuration.
4. Select the updater, then run it.
5. Set **«Рабочая папка» (Working folder)** and choose GitHub as the source. The catalog address is:

```text
https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/manifest.json
```

6. Click **«Проверить» (Check)**. This saves the updater settings and reads the catalog; it does not replace working scripts.
7. Select the scripts to install, click **«Применить выбранное» (Apply selected)**, review the confirmation and wait for completion.
8. Configure scripts that need your paths or settings before running them.

The updater creates its own INI on Check if missing. Initial installation can create missing example INI files for new scripts. Existing settings are retained.

## Test the updater in NX

Use the small **Open project folder** script for a first installation check:

1. Start the updater from its working folder and confirm the source and path.
2. Click Check, then **«Снять все» (Clear all)**.
3. Select `NX_Open_Project_Folder.cs` if it is not installed and apply the selection.
4. Run Check again. A successfully installed file should show **«Установлена эта версия» (This version is installed)**.
5. Run the installed journal with a saved PRT open and check that Explorer opens its folder.

An already matching file does not need reinstalling. For failures, report the NX and updater versions, reproduction steps, full message and a screenshot in [Issues](https://github.com/TonyFoxxxx/NXOpen-SCRIPTS/issues). Remove confidential paths and project data first.

## Buttons

A button is configured **once for each script in the relevant NX role**. It points to a stable local filename; the updater replaces the contents without changing the button.

1. Switch to the role in which you want to use the script, for example Manufacturing.
2. Right-click a blank ribbon area and choose **Customize**. Ctrl+1 may work if assigned.
3. Open **Commands → New Item** (or **New Button**, depending on version).
4. Drag **New User Command** onto the desired toolbar or ribbon location.
5. Leave Customize open, right-click the new button and choose **Edit Action**.
6. Select **Execute an Action** or **Journal File**, depending on your NX version.
7. Click **Browse** and select the script. If it is not listed, change the file filter to **All Files**.
8. Set the button name and, optionally, its tooltip and icon. Confirm and close Customize.
9. Test the button and save the customized role if you need to restore or reuse it.

| Button | Example action path |
| --- | --- |
| Script updater | `C:\ProgramData\NX_SCRIPTS\NX_Update_Scripts.py` |
| Setup sheet | `C:\ProgramData\NX_SCRIPTS\NX_Setup_Prototype.py` |

Names, tooltips and icons are separate from the action path. A button created in one role may not appear in another. Moving or renaming the working file requires changing its action path; ordinary updates do not.

Background references used by the Russian guide: [NX Open User Guide](https://manualzz.com/doc/43946990/siemens-plm-software-nx-open-user-guide) and [NX custom macro/journaling tutorial](https://www.jiveengineering.com/nxblog/siemens-nx-custom-macro-journaling-tutorial). Menu wording varies with NX version and language.

## Manual installation

Download the raw script files or the repository ZIP and put the required files from `scripts/` into your working folder. Keep their filenames unchanged.

Copy the relevant examples from `config/`, remove `.example` from each name and enter your settings. Do not overwrite existing working INI files. The numbering script uses the fixed configuration path `C:\ProgramData\3_NX_DATA\NX_Numbering_Settings_v1.0.ini`; its empty XLSX template is for the initial setup only, not for replacing numbering history.

You do not need to copy `docs/`, `tools/`, `tests/` or `manifest.json` to the working folder. Run journals inside NX.

## Migrating from the old updater name

Since V1.13, the stable name is `NX_Update_Scripts.py` and its main configuration file is `NX_Update_Scripts.ini`. An updater named `NX_Update_Script_Buttons.py` at V1.12 or earlier needs a one-time manual migration; the old name cannot complete this rename through self-update. The catalog requires at least V1.13.

1. Close the old updater.
2. Download the current `NX_Update_Scripts.py` into your existing working folder.
3. Rename `NX_Update_Script_Buttons.ini` to `NX_Update_Scripts.ini`, retaining its contents. If both already exist, keep the valid configuration rather than replacing it with an example.
4. Run the new updater. Verify the saved working path and source settings, then click Check. Legacy `[Paths]` and `[Options]` are supported; if `[Source]` is absent, GitHub is the default.
5. Change **only the updater button's action path** to the new filename and test it.
6. Remove the old updater script after the new one works.

If the new INI is absent but the old INI exists, the updater reads and saves the old file without creating a second copy. If both exist, the new name takes precedence. Files whose origin cannot be verified may require manual selection after checking their contents.

Subsequent updates use the new stable name without another button change.
