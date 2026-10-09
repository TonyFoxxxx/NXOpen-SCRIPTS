# Configuration

[Русский](../configuration.md) | **English**

[All scripts](../../README.en.md#scripts) · [Installation](installation.md)

Examples are in [config/](../../config). For manual installation, remove `.example` from the filename and save the settings as UTF-8. Keep existing working INI files; add new keys deliberately instead of replacing personal settings with an example.

## Setup sheets

`NX_Setup_Prototype.ini` belongs beside the script:

```ini
[SetupCard]
programmer=
```

`programmer` supplies the default programmer name. An empty value leaves the field blank. See the [setup-sheet guide](setup-card.md).

## Zmin

`NX_Operation_Zmin.py` has no INI. It reads the open CAM project, its toolpaths and coordinate-system relationships. See [calculation requirements](zmin.md).

## Postprocessing

Use `NX_Postprocess_To_Machine.ini` beside the script. Example: [NX_Postprocess_To_Machine.example.ini](../../config/NX_Postprocess_To_Machine.example.ini). Supply your own postprocessors and machine paths; production posts are not included.

### Postprocessor paths

In the post-selection window, open **«Пути к постпроцессорам» (Postprocessor paths)**. Add a named entry and select its TCL. A matching DEF is filled in automatically if present; otherwise select it yourself. **«Сохранить» (Save)** writes the INI and reloads the list.

Cancel writes nothing. Removing an entry does not delete post files. Other sections, comments, encoding and line endings are preserved. If no INI exists, the first successful save creates it with `PostList=auto`.

Each explicit post section can also define `WorkOffsetProfile=Auto`, `ISO_G28` or `ISO_G53` and, when required, `WorkOffsetG53Z`. This value must match a verified retract already output by your post. It does not instruct the script to generate a new safe move. See [multiple work offsets](postprocess.md#multiple-work-offsets).

### Machine folders

In the output-destination window, open **«Папки со станками…» (Machine folders)**. Add local or network root folders, browse for them or paste paths. Multiple roots and descriptive labels are supported.

The script finds **immediate subfolders whose names consist of digits**. Other machine names can be listed explicitly in `[Machines]`. The root dialog saves `[MachineRoots]` only when you click Save; the machine buttons refresh and you must select the destination again. Cancel keeps the previous choice. The INI and destination must be writable, and Windows must be able to access network paths.

Any `C:\CNC_Example` path in an example is fictional. Replace it with your own.

### Other settings

`[Settings] PostList=auto` includes the standard NX post list. `DefaultExtension` controls the default output extension. Relative paths are resolved from the INI location.

The script keeps its last choices in `%LOCALAPPDATA%\NX_Postprocess_To_Machine\LastChoice.ini`. This is separate from the updater's settings.

## ESKD formatting

Place `NX_ESKD_Settings.ini` beside the script. [Example](../../config/NX_ESKD_Settings.example.ini).

- `TitleBlock`: organization and signature fields.
- `Defaults`: default material.
- `[TechnicalRequirements]` and `[SurfaceRoughness]`: technical-requirement and roughness settings. Verify the example requirements against the actual part.
- Successful choices are saved in `LastUsed`; the updater preserves this INI. The dialog can reload the base INI values.

### External font

The required family is `NX_ESKD_GOST_A_Italic_v110`, supplied by `NX_ESKD_GOST_A_Italic_v110.ttf`. Its file is **not included** in the repository; supply a compatible copy you are lawfully entitled to use.

The script searches beside its `.cs` file, in `UGII_STANDARD_FONT_DIR`, NX/user font locations and `%WINDIR%\Fonts`. Numbered filename variants `_1` through `_99` are supported. User locations include:

- `%LOCALAPPDATA%\Microsoft\Windows\Fonts`
- `%LOCALAPPDATA%\Siemens\NX_ESKD\Fonts`

The expected edition is checked by size (57,764 bytes), CRC32 (`582A8BB4`) and actual font tables. These checks do not grant permission to redistribute the font. A corporate NX font directory may be used. NX may need restarting after the font becomes available.

Without a compatible font, formatting stops instead of substituting a similar one. See [third-party notices](../../THIRD_PARTY_NOTICES.en.md) and the [ESKD guide](eskd.md).

## Program-folder numbering

Since V1.04, the INI is beside `NX_Number_Program_Folders.cs` in the selected working folder. For example:

```text
C:\ProgramData\2. NX_Scripts\NX_Numbering_Settings_v1.0.ini
```

[Example INI](../../config/NX_Numbering_Settings_v1.0.example.ini). Configure your own `Prefix`, `StartNumber` and `EndNumber`. The example range 1–1000 is only an example; choose a range that does not overlap your other numbering systems.

`RegistryFile` points to the XLSX history. It can be absolute or relative to the INI. Microsoft Excel is not required. The empty [template](../../templates) contains headers only and is for a first installation.

Updater V1.18 downloads the INI and empty XLSX into the installed numbering script's folder only for a first installation with no previous settings or history.

Existing files beside the script take precedence. Otherwise, the updater migrates the working pair from the former `C:\ProgramData\3_NX_DATA` folder, preserving the range, comments and allocation history. It places the migrated workbook beside the script and changes only `RegistryFile` in the copied INI if needed. The old files remain untouched. Close Excel and other numbering runs before migration.

Lost settings or history are never replaced with an empty register. Conflicting workbooks are not merged or overwritten. Restore the required INI and XLSX. If the current script version is installed but its data files are absent, manually select the missing-numbering-files row to retry migration of the old pair.

`v1.0` is part of the stable INI/register filenames; keep it. See [numbering behavior and file changes](number-program-folders.md).

## Updater

Place `NX_Update_Scripts.ini` beside `NX_Update_Scripts.py`:

```ini
[Paths]
working_folder=C:\ProgramData\NX_SCRIPTS
update_folder=

[Source]
type=github
manifest_url=https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/manifest.json

[Options]
search_update_subfolders=yes
search_working_subfolders=no
exclude_files=NX_Update_Scripts*.py
```

GitHub is the default. `search_update_subfolders` applies to the folder source. Working-folder recursion is controlled separately.

Clicking **«Проверить» (Check)** saves the updater's own settings. Unknown keys are retained except the legacy dynamic `[Scripts]` section; the INI parser may rewrite formatting and comments. Other scripts' INI files are not changed by this action.

If the new INI is absent, the old `NX_Update_Script_Buttons.ini` is reused for both reading and saving. If both exist, the new one wins. See [migration](installation.md#migrating-from-the-old-updater-name).

For an offline folder source, set `type=folder` and `update_folder`, or choose the folder with Browse. Folder mode is an explicit local/network alternative. It copies scripts only, without initial INI examples or self-update. A GitHub failure does not switch sources automatically. See the [updater guide](updater.md).
