# SCRIPT_VERSION: V1
"""Check a release, or refresh versions and hashes without executing NX journals."""
SCRIPT_VERSION = "V1"

import argparse
import importlib.util
import json
from pathlib import Path
import re
import sys
import zipfile
import xml.etree.ElementTree as ET

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('nx_updater', ROOT / 'scripts/NX_Update_Script_Buttons.py')
updater = importlib.util.module_from_spec(spec)
spec.loader.exec_module(updater)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument('--write', action='store_true', help='refresh version and SHA-256 fields')
    modes.add_argument('--check', action='store_true', help='verify only (default)')
    args = parser.parse_args()
    path = ROOT / 'manifest.json'
    original = path.read_bytes()
    manifest = updater.validate_manifest(original)
    managed = set()
    changed = []
    for entry in manifest['scripts']:
        script = ROOT / entry['path']
        raw = script.read_bytes()
        text = raw.decode('utf-8')
        if '\r' in text:
            raise ValueError('Use LF as declared in .gitattributes: ' + entry['path'])
        info = updater.internal_info(script.name, raw)
        if info['meta'] is not None:
            raise ValueError('Do not publish an installed file with updater metadata: ' + script.name)
        if not re.search(r'(?m)^\s*(?:#|//|\x27) SCRIPT_VERSION:\s*' + re.escape(info['label']) + r'\s*$',
                         '\n'.join(text.splitlines()[:10])):
            raise ValueError('Version header must be in the first 10 lines: ' + script.name)
        declarations = updater.version_declarations(text, script.suffix)
        if not any(key.casefold() == 'script_version' for key, _ in declarations):
            raise ValueError('Missing SCRIPT_VERSION string constant: ' + script.name)
        updater.pack_script(raw, script.name)  # syntax/version check, no execution
        if info['label'] != entry['version']:
            changed.append(entry['path'] + ': version')
            entry['version'] = info['label']
        records = [entry] + ([entry['config']] if entry.get('config') else []) + entry.get('install_files', [])
        for record in records:
            asset = ROOT / record['path']
            data = asset.read_bytes()
            managed.add(record['path'])
            checksum = updater.digest(data)
            if record['sha256'] != checksum:
                changed.append(record['path'] + ': sha256')
                record['sha256'] = checksum
            if asset.suffix == '.ini':
                if b'\r' in data or data.startswith(b'\xef\xbb\xbf'):
                    raise ValueError('Public examples must use UTF-8 without BOM and LF: ' + asset.name)
                config = updater.config_parser(data)
                if any(section.casefold() == 'lastused' for section in config.sections()):
                    raise ValueError('Remove personal LastUsed values from public examples')
        if 'EmbeddedFontChunks' in text:
            raise ValueError('Do not distribute the embedded ASCON font')
    actual = {p.relative_to(ROOT).as_posix() for folder in ('scripts', 'config', 'templates')
              for p in (ROOT / folder).rglob('*') if p.is_file() and '__pycache__' not in p.parts}
    if actual != managed:
        raise ValueError('Unlisted/missing managed files: ' + ', '.join(sorted(actual ^ managed)))
    for name in actual:
        if Path(name).suffix in ('.ttf', '.otf', '.log', '.pending', '.tmp', '.prt'):
            raise ValueError('Local data is not part of a release: ' + name)
    with zipfile.ZipFile(ROOT / 'templates/NX_Numbering_Register_v1.0.xlsx') as archive:
        ns = {'m': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
        for name in archive.namelist():
            if name.startswith('xl/worksheets/sheet') and name.endswith('.xml'):
                root = ET.fromstring(archive.read(name))
                for row in root.findall('.//m:row', ns):
                    if int(row.get('r', '0')) > 1 and any((x.text or '').strip() for x in row.iter()):
                        raise ValueError('Template registry contains user records')
    updated = (json.dumps(manifest, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
    updater.validate_manifest(updated)
    updater_entry = next((item for item in manifest['scripts'] if item['id'].casefold() == updater.UPDATER_ID), None)
    if updater_entry is None or updater.parse_version(updater_entry['version'])[0] < updater.parse_version(manifest['min_updater_version'])[0]:
        raise ValueError('The catalog must offer an updater that satisfies min_updater_version')
    if changed and not args.write:
        raise ValueError('Manifest is stale; run python tools/manifest.py --write:\n' + '\n'.join(changed))
    if args.write and updated != original:
        path.write_bytes(updated)
    print('OK: {} scripts, {} managed files; versions, hashes, syntax and empty registry checked.'.format(
        len(manifest['scripts']), len(managed)))


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, SyntaxError, zipfile.BadZipFile) as exc:
        print(str(exc), file=sys.stderr)
        sys.exit(1)
