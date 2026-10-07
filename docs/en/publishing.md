# Publishing scripts and updates

[Русский](../publishing.md) | **English**

[All scripts](../../README.en.md#scripts)

## Stable names and versions

Keep working filenames constant: do not append version numbers or copy suffixes. Keep the correct `.py` or `.cs` extension and UTF-8 text.

The first ten source lines must contain `# SCRIPT_VERSION: V1.01` for Python or `// SCRIPT_VERSION: V1.01` for C#, with the actual release version. The string constant `SCRIPT_VERSION` must have the same value. Version components compare numerically: `V1`, `V1.01`, …, `V1.10`. Increase the version for code changes; a major-version change is a maintainer decision, not an automatic action.

Preserve author attribution and stable script IDs, manifest names/descriptions and paths. The updater uses permanent paths on `main`.

## Documentation

Descriptions should state what the script does, without phrases such as “based on V…”. Clearly describe known limitations.

Each script guide must cover all functions, use, settings, created/changed files and limitations. Link it from the README's **All features** column.

Every release needs a latest-release section in its guide with version, date, features/fixes—even for a bug-fix-only release—and actual validation/remaining limitations. Add a short README summary linked to that section, and retain older history in CHANGELOG. Publish source, manifest and documentation together.

Keep instructions understandable to a new user, without references to private conversations. **Update Russian and English documentation together when behavior changes**, including versions, links and limitations. The English entry point is `README.en.md`; translated guides are in `docs/en/`. Retain language links.

Documentation-only changes do **not** require a script version increase.

## Adding a script

1. Add clean source under its permanent name in `scripts/`, with matching header and `SCRIPT_VERSION`.
2. If required, add an example INI containing neutral values, not company settings.
3. Add the manifest entry: stable `id`, `name`, `file`, `version`, `path`, `sha256` and `description`. Add supported configuration/location metadata where applicable.
4. Add both guide languages and entries in both READMEs; record the release.
5. Recalculate the manifest and run the checks below.

A temporary all-zero hash is only a drafting placeholder; never publish it. The updater discovers a new catalog entry without changing updater code, provided it uses the supported schema and dependency types. It does not create NX buttons.

## Release checks

From the repository root:

```bash
python tools/manifest.py --write
python tools/manifest.py --check
python -m unittest discover -s tests -v
```

`--write` synchronizes versions and hashes from source; it does not choose a new version or edit the scripts. `--check` is read-only and checks the catalog, version headers/constants, Python syntax, example INI and the empty XLSX template. The register template must contain no allocations.

For an existing script, update both source version markers consistently, then its manifest, guide, README and changelog. Commit the matching files together so checking and downloading see consistent hashes.

## Settings and compatibility

Working user INI files are not overwritten with new examples during ordinary updates. New keys need safe defaults or a deliberate migration that retains user data.

Raise `min_updater_version` only for a necessary incompatibility. An incompatible schema can require a manual update; document the procedure.

## Review before committing

Inspect the staged diff. Do not include real company INI files, network paths, credentials, local models, populated numbering registers, restricted fonts, logs or temporary outputs. Ignoring a file does not erase it from Git history.

Do not publish an installed working copy containing the `NX_UPDATER_INSTALLED_V1` marker. Use the clean repository source.

CI checks catalog/updater behavior on Windows and Linux and includes real Windows TreeView drawing checks. These do not run NXOpen. Test the journal inside the target NX / Designcenter separately and do not claim wider compatibility without evidence.
