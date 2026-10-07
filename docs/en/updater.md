# Updater V1.17

[Русский](../updater.md) | **English**

Script: [NX_Update_Scripts.py](../../scripts/NX_Update_Scripts.py). [All scripts](../../README.en.md#scripts).

Installs new journals and updates selected working files while retaining their stable paths.

The default working folder is `C:\ProgramData\NX_SCRIPTS`. An existing INI path takes precedence. To relocate an installation, move scripts together with their INI files, change the updater's working folder and update NX button paths.

## Features

- Two sources: the public GitHub catalog or a user-selected local/network folder.
- Finds working `.py`, `.cs` and `.vb` journals; working-folder and source-folder recursion are separate options.
- Lists script name, working file, installed/available versions and state. Selecting a row shows paths, description and any restriction.
- The available-version cell turns green, `#00CD86` (RGB 0, 205, 134), when an installed script has a newer source version. The color persists through selection/sorting and disappears after updating and checking again. New installations, unknown and equal versions are not highlighted.
- Click any column heading to sort; click again to reverse. An arrow shows direction. Versions compare numerically. Checkboxes and the selected row are retained.
- Manual checkboxes plus **«Выбрать обновления» (Select updates)**, **«Выбрать новые скрипты» (Select new scripts)** and **«Снять все» (Clear all)**.
- Double-click a row, including its version/state cells, to toggle its checkbox. Clicking the checkbox itself also works; unavailable rows remain protected.
- Checks internal versions and hashes, detects local edits and ambiguous copies, and blocks downgrades.
- Installs new files with stable names and updates existing files at their working paths.
- Installs initial INI examples from GitHub without replacing existing settings.
- Defers self-update until its window closes. Folder mode skips the running updater.
- Check saves settings; **«Перечитать INI» (Reload INI)** rereads them.
- Closing the window cancels checking. A second updater window in the current Windows session is blocked. Ordinary write failures trigger rollback.

## Use

1. Run the journal from the working folder through **Tools → Journal → Play** in NX.
2. Confirm **«Рабочая папка» (Working folder)** and **«Источник» (Source)**. For GitHub, use the raw manifest below. For folder mode, choose a source with Browse.
3. Click **«Проверить» (Check)**. The updater saves its own INI; working scripts are not replaced at this stage.
4. Sort if useful, read states/details, then select scripts by checkbox or double-click.
5. Click **«Применить выбранное» (Apply selected)**, review the confirmation and wait for the result.
6. Check again. Successfully installed files should show **«Установлена эта версия» (This version is installed)**.

| State shown in the UI | Meaning |
| --- | --- |
| Новый скрипт — установка | New script; select it to install. |
| Новая версия | New version. A verified working copy without local edits may be selected automatically. |
| Происхождение не подтверждено / местные правки / нет версии | Unverified origin, local edits or no version: inspect the working file and select individually. |
| Та же версия, другой код | Same version, different code; manual selection only. |
| Установлена эта версия | Contents match the catalog; no reinstall needed. |
| На ПК более новая версия | Installed version is newer; downgrade blocked. |
| Несколько рабочих файлов одного скрипта | Multiple working files in one script family; retain one working path. The updater does not choose a copy for you. |
| Нет в каталоге GitHub | Local file is not in the catalog; it is left in place. |

Use the [first NX test](installation.md#test-the-updater-in-nx) if installing for the first time. For `NX_Update_Script_Buttons.py` V1.12 or earlier, complete the [one-time filename migration](installation.md#migrating-from-the-old-updater-name) first.

Create an [NX button](installation.md#buttons) once, pointing to the permanent `NX_Update_Scripts.py` path. Later updates replace its contents without changing the action.

## GitHub source

Catalog: [raw manifest](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/manifest.json).

Base file URL: `https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/`. Example: [setup-sheet source](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/scripts/NX_Setup_Prototype.py). Normal `github.com/.../blob/...` links return HTML and are not raw source URLs for the updater.

## Checking

1. Read INI and validate the working folder/source.
2. Download one manifest over HTTPS. No Python/C# source files are downloaded yet.
3. Validate schema 1, required fields, unique entries, versions and allowed paths.
4. Read local internal versions and validate any installation marker.
5. Show new scripts and updates in the selection window.

Versions compare by numeric components: `V1.09 < V1.10`. The major version is not automatically changed.

New scripts, unverified files, local edits and different code with an equal version are not selected automatically. A newer local version cannot be downgraded. Multiple working copies of one script family block an ambiguous update.

## Folder source

Install absent journals or update existing ones from a local or network folder. Candidates are matched by filename family and internal version, respecting INI exclusions and recursion settings.

This mode copies scripts only: no initial INI examples or other companion files, and no self-update of the running updater. A GitHub failure never switches to folder mode automatically.

## Installing and updating

After selection, only the required source files and initial examples are downloaded. Before modifying working files, every download is checked for SHA-256, size, internal version, header version comment and `SCRIPT_VERSION`. Python syntax is checked without executing the script.

A new script uses its stable filename in the working folder. A missing INI is created from its example only when installing an absent script; existing INI files are retained. The empty numbering register is installed only when its INI and register are absent. An existing INI does not authorize the updater to recreate missing numbering history.

An ordinary update replaces only the selected script at its existing path. Other scripts' INI files, models, registers, logs and NX buttons are outside that replacement. Downloaded code is not executed during installation.

An `NX_UPDATER_INSTALLED_V1` comment at the end of an installed file records the source hash and helps detect local modifications.

## Writing and failures

Local hashes are checked again before writing. On Windows, existing files are protected against outside writes and renames during preparation. The current file's protective handle is closed immediately before atomic replacement; other files stay protected until their turn. This fixes the V1.09 `WinError 5` issue.

Detected changes cancel the operation. Do not edit working files during installation: a short interval remains between closing the protective handle and replacing that file.

The new version is written to a verified neighboring `.nxupdater-….pending` file, then replaces the destination as a whole. The temporary name has no journal extension and is not run. Ordinary write errors roll back from memory. If rollback cannot finish, the message lists affected paths. No persistent backups or logs are created.

Atomic replacement applies to **one file, not the whole selection**. A crash or power loss may leave some files updated and others unchanged. Run Check again after restarting. Leftover pending files from completed processes are removed on the next launch. This does not replace backups of important user data.

## Self-update

If the running updater is selected, its download is checked and held in memory. Other selected files update first. The window then closes, worker threads stop, and only then does the main thread replace its `.py` file. The new code runs on the next launch. If the old process exits before replacement, the old file remains.

Already running Python code stays in memory; new code is not imported over it. A second updater window is blocked in the same Windows session. From V1.13, self-update uses `NX_Update_Scripts.py`; the old filename requires [manual migration](installation.md#migrating-from-the-old-updater-name).

## Manifest format

```json
{
  "schema_version": 1,
  "min_updater_version": "V1.13",
  "scripts": [
    {
      "id": "NX_Setup_Prototype",
      "name": "Карта наладки",
      "file": "NX_Setup_Prototype.py",
      "version": "V2.37",
      "path": "scripts/NX_Setup_Prototype.py",
      "sha256": "<64 lowercase hexadecimal characters>",
      "description": "Карта наладки с видами, инструментами и Zmin.",
      "config": {
        "path": "config/NX_Setup_Prototype.example.ini",
        "file": "NX_Setup_Prototype.ini",
        "location": "script",
        "sha256": "<64 lowercase hexadecimal characters>"
      }
    }
  ]
}
```

This demonstrates fields, not a ready-to-publish manifest or current release. Real hashes are calculated by the publishing tool. `id` is the stable filename without its extension. Omit `config` for a script without an INI. `location=script` means the installed journal's folder. `numbering_data` is reserved for the numbering script's fixed `C:\ProgramData\3_NX_DATA` location and one `install_files` entry for the empty XLSX.

New journals with ordinary INI settings can be added without changing the updater. New dependency types or installation directories require format/updater support. Unknown schemas are blocked. If `min_updater_version` increases, an old client may update itself first, while other entries remain blocked.

## Trust and limitations

Only this repository's raw HTTPS URLs on `main` are accepted, including redirect validation. The manifest cannot contain external URLs, directory traversal, absolute paths or arbitrary installation commands. TLS certificate verification remains enabled. No token or password is needed for the public catalog.

In V1.15, if NX Python reports `CERTIFICATE_VERIFY_FAILED`, the request is retried through WinHTTP. Windows validates the certificate and supplies proxy settings; TLS 1.2 is used. Redirects are forbidden in the fallback, while size/completeness limits and SHA-256 checks remain. Downloads stay in memory. The V1.14 `WinHttpSetOption(TLS): 12018` error is fixed.

If Windows also rejects the certificate, the error includes its code and explanation. Check the PC's date and whether the manifest opens in its browser; ask your administrator to check trusted certificates, corporate proxy or antivirus HTTPS inspection. The updater does not install certificates or change system settings. If an old updater cannot download the catalog, replace `NX_Update_Scripts.py` manually, retaining its INI, then try again.

SHA-256 detects corruption or disagreement with the catalog; it is not an author's digital signature. Repository access remains the trust source. If `main` changes between checking and downloading, hashes can mismatch; the operation stops and you should check again. Source and manifest are therefore published in one commit.

GitHub mode requires internet access, trusted certificates and write permission. Do not disable TLS verification to bypass a corporate proxy. The updater does not compile C# or execute NXOpen; journals need testing in the target NX environment.
