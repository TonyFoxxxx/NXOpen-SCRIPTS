"""Portable tests for data preservation; no NX session or internet required."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('updater', ROOT / 'scripts/NX_Update_Scripts.py')
u = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(u)


def source(version='V1.10', extension='.py'):
    if extension == '.cs':
        return ('// SCRIPT_VERSION: ' + version + '\nclass Journal {\n'
                'private const string SCRIPT_VERSION = "' + version + '";\n}\n').encode()
    return ('# SCRIPT_VERSION: ' + version + '\nSCRIPT_VERSION = "' + version + '"\n').encode()


class UpdaterTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.data = self.root / 'numbering'
        self.addCleanup(patch.stopall)
        patch.object(u, 'NUMBERING_DATA_FOLDER', str(self.data)).start()
        self.settings = u.load_settings(str(self.root / 'NX_Update_Scripts.py'))
        self.settings['working_folder'] = str(self.root)
        self.payloads = {}
        self.calls = []
        self.manifest = {'schema_version': 1, 'min_updater_version': 'V1.09', 'scripts': []}

    def add_script(self, name='Example.py', version='V1.10', config=False, numbering=False):
        raw = source(version, Path(name).suffix)
        record = dict(id=Path(name).stem, name='Example', description='Test journal',
                      file=name, path='scripts/' + name, version=version, sha256=u.digest(raw))
        self.payloads[record['path']] = raw
        if config:
            filename = 'NX_Numbering_Settings_v1.0.ini' if numbering else Path(name).stem + '.ini'
            cfg = dict(file=filename, path='config/' + filename[:-4] + '.example.ini',
                       location='numbering_data' if numbering else 'script')
            data = (b'[Numbering]\nPrefix=O\nStartNumber=1659\nEndNumber=5000\n'
                    b'RegistryFile=NX_Numbering_Register_v1.0.xlsx\n') if numbering else b'[Example]\npath=\n'
            cfg['sha256'] = u.digest(data)
            self.payloads[cfg['path']] = data
            record['config'] = cfg
            if numbering:
                template = dict(file='NX_Numbering_Register_v1.0.xlsx',
                                path='templates/NX_Numbering_Register_v1.0.xlsx', location='numbering_data')
                data = (ROOT / template['path']).read_bytes()
                template['sha256'] = u.digest(data)
                self.payloads[template['path']] = data
                record['install_files'] = [template]
        self.manifest['scripts'].append(record)
        return record

    def fetch(self, url, limit, cancelled=None):
        self.calls.append(url)
        if url == u.DEFAULT_MANIFEST_URL:
            return json.dumps(self.manifest).encode()
        return self.payloads[url.removeprefix(u.RAW_ROOT)]

    def scan(self, **kwargs):
        return u.scan_github(self.settings, threading.Event(), fetcher=self.fetch, **kwargs)

    def assert_no_stages(self):
        self.assertEqual(list(self.root.rglob('*.pending')), [])

    def test_manifest_only_scan_and_new_install(self):
        self.add_script(config=True)
        rows = self.scan()
        self.assertEqual(self.calls, [u.DEFAULT_MANIFEST_URL])
        self.assertFalse(rows[0]['checked'])
        self.assertEqual(rows[0]['operation'], 'install')
        u.apply_updates(rows, self.fetch)
        self.assertEqual(u.unpack_script((self.root / 'Example.py').read_bytes())[0], source())
        self.assertTrue((self.root / 'Example.ini').is_file())
        self.assert_no_stages()
        self.assertFalse(self.scan()[0]['eligible'])

    def test_numeric_version_order_and_never_downgrade(self):
        self.add_script()
        target = self.root / 'Example.py'
        target.write_bytes(u.pack_script(source('V1.09'), target.name))
        self.assertTrue(self.scan()[0]['checked'])
        target.write_bytes(source('V1.11'))
        self.assertFalse(self.scan()[0]['eligible'])
        self.assertLess(u.parse_version('V1')[0], u.parse_version('V1.01')[0])

    def test_local_edits_and_unknown_provenance_require_selection(self):
        self.add_script()
        target = self.root / 'Example.py'
        for raw in (source('V1.09'), u.pack_script(source('V1.09'), target.name).replace(b'V1.09\n', b'V1.09  \n')):
            with self.subTest(raw=raw[:30]):
                target.write_bytes(raw)
                row = self.scan()[0]
                self.assertTrue(row['eligible'])
                self.assertFalse(row['checked'])

    def test_same_version_different_bytes_requires_selection(self):
        self.add_script()
        (self.root / 'Example.py').write_bytes(source() + b'# local customization\n')
        row = self.scan()[0]
        self.assertTrue(row['eligible'])
        self.assertFalse(row['checked'])

    def test_duplicate_local_family_is_blocked(self):
        self.add_script()
        (self.root / 'Example.py').write_bytes(source())
        (self.root / 'Example_V1.09.py').write_bytes(source('V1.09'))
        self.assertFalse(self.scan()[0]['eligible'])

    def test_missing_or_empty_local_version_is_manual(self):
        self.add_script()
        (self.root / 'Example.py').write_bytes(b'# no version\n')
        self.assertFalse(self.scan()[0]['checked'])
        (self.root / 'Example.py').write_bytes(b'')
        self.assertFalse(self.scan()[0]['eligible'])

    def test_manifest_rejects_unsafe_names_paths_duplicates(self):
        record = self.add_script()
        bad = [('path', 'scripts/../Example.py'), ('path', 'scripts/%2e%2e.py'),
               ('file', 'CON.py'), ('file', 'Example_V1.10.py'), ('sha256', 'bad'),
               ('version', 'V1.1'), ('id', 'Other')]
        for key, value in bad:
            with self.subTest(key=key, value=value):
                candidate = copy.deepcopy(self.manifest)
                candidate['scripts'][0][key] = value
                with self.assertRaises(ValueError):
                    u.validate_manifest(json.dumps(candidate).encode())
        duplicate = copy.deepcopy(self.manifest)
        duplicate['scripts'].append(record)
        with self.assertRaises(ValueError):
            u.validate_manifest(json.dumps(duplicate).encode())
        with self.assertRaises(ValueError):
            u.validate_manifest(b'{"schema_version":1,"schema_version":1}')

    def test_foreign_url_and_redirect_rejected(self):
        for url in ('http://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/manifest.json',
                    'https://raw.githubusercontent.com/Other/Repo/main/manifest.json',
                    u.DEFAULT_MANIFEST_URL + '?token=secret', u.RAW_ROOT + '../other.py'):
            with self.subTest(url=url), self.assertRaises(ValueError):
                u.validate_raw_url(url, manifest=True)
        handler = u._RepositoryRedirect()
        with self.assertRaises(ValueError):
            handler.redirect_request(None, None, 302, '', {}, 'https://example.com/payload')

    def test_hash_failure_leaves_entire_selection_untouched(self):
        first = self.add_script('First.py')
        second = self.add_script('Second.py')
        target = self.root / 'First.py'
        before = source('V1.09')
        target.write_bytes(before)
        rows = self.scan()
        self.payloads[second['path']] += b'corrupt'
        with self.assertRaises(ValueError):
            u.apply_updates(rows, self.fetch)
        self.assertEqual(target.read_bytes(), before)
        self.assertFalse((self.root / 'Second.py').exists())
        self.assert_no_stages()

    def test_version_mismatch_and_python_syntax_error_block_write(self):
        record = self.add_script()
        for raw in (source('V1.09'), source() + b'broken = (\n'):
            with self.subTest(raw=raw):
                self.payloads[record['path']] = raw
                record['sha256'] = u.digest(raw)
                with self.assertRaises((ValueError, SyntaxError)):
                    u.apply_updates(self.scan(), self.fetch)
                self.assertFalse((self.root / record['file']).exists())

    def test_existing_ini_is_preserved_on_install_and_update(self):
        self.add_script(config=True)
        ini = self.root / 'Example.ini'
        custom = b'[Example]\npath=D:\\MyData\n; custom comment\n'
        ini.write_bytes(custom)
        u.apply_updates(self.scan(), self.fetch)
        self.assertEqual(ini.read_bytes(), custom)
        (self.root / 'Example.py').write_bytes(source('V1.09'))
        u.apply_updates(self.scan(), self.fetch)
        self.assertEqual(ini.read_bytes(), custom)
        self.assertNotIn(u.RAW_ROOT + 'config/Example.example.ini', self.calls)

    def test_update_does_not_recreate_missing_config(self):
        self.add_script(config=True)
        (self.root / 'Example.py').write_bytes(source('V1.09'))
        u.apply_updates(self.scan(), self.fetch)
        self.assertFalse((self.root / 'Example.ini').exists())

    def test_new_numbering_installs_empty_template_only_once(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        u.apply_updates(self.scan(), self.fetch)
        register = self.root / 'NX_Numbering_Register_v1.0.xlsx'
        self.assertEqual(register.read_bytes(), self.payloads[record['install_files'][0]['path']])
        self.assertTrue((self.root / 'NX_Numbering_Settings_v1.0.ini').is_file())
        self.assertFalse(self.data.exists())
        register.write_bytes(b'precious numbering history')
        (self.root / record['file']).write_bytes(source('V1.09', '.cs'))
        u.apply_updates(self.scan(), self.fetch)
        self.assertEqual(register.read_bytes(), b'precious numbering history')

    def test_existing_numbering_ini_never_recreates_lost_registry(self):
        self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        self.data.mkdir()
        ini = self.root / 'NX_Numbering_Settings_v1.0.ini'
        ini.write_bytes(b'[Numbering]\nRegistryFile=custom_history.xlsx\n')
        with self.assertRaisesRegex(ValueError, 'Excel'):
            u.apply_updates(self.scan(), self.fetch)
        self.assertEqual([p.name for p in self.root.iterdir() if p.is_file()], [ini.name])
        self.assertEqual(len(self.calls), 2)  # manifest + selected C# source

    def legacy_numbering(self, record, filename='NX_Numbering_Register_v1.0.xlsx', absolute=False):
        self.data.mkdir(exist_ok=True)
        book = self.data / filename
        book.write_bytes(b'issued numbers 1659,1660,1661; keep every byte')
        ini = self.data / record['config']['file']
        raw = ('; Settings and comments must survive\r\n[Numbering]\r\nPrefix=O\r\n'
               'StartNumber=1659\r\nEndNumber=5000\r\nRegistryFile=' +
               (str(book) if absolute else filename) + '\r\n').encode('utf-8-sig')
        ini.write_bytes(raw)
        return ini, book

    def test_numbering_initial_files_follow_selected_folder_with_spaces(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        target = self.root / '2. NX_Scripts'
        target.mkdir()
        self.settings['working_folder'] = str(target)
        u.apply_updates(self.scan(), self.fetch)
        self.assertEqual({p.name for p in target.iterdir()}, {
            record['file'], record['config']['file'], record['install_files'][0]['file']})
        self.assertFalse(self.data.exists())

    def test_numbering_update_migrates_old_history_and_ini_without_downloading_templates(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        ini, book = self.legacy_numbering(record)
        (self.root / record['file']).write_bytes(source('V1.03', '.cs'))
        u.apply_updates(self.scan(), self.fetch)
        self.assertEqual((self.root / ini.name).read_bytes(), ini.read_bytes())
        self.assertEqual((self.root / book.name).read_bytes(), book.read_bytes())
        self.assertEqual(len(self.calls), 2)
        self.assert_no_stages()

    def test_numbering_migration_relocates_absolute_custom_registry_preserving_range(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        ini, book = self.legacy_numbering(record, filename='My history.xlsx', absolute=True)
        before = ini.read_bytes()
        u.apply_updates(self.scan(), self.fetch)
        self.assertEqual((self.root / book.name).read_bytes(), book.read_bytes())
        self.assertEqual((self.root / ini.name).read_bytes(), before.replace(str(book).encode(), book.name.encode()))
        self.assertEqual(ini.read_bytes(), before)

    def test_numbering_can_complete_ini_only_move_from_legacy_folder(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        ini, book = self.legacy_numbering(record)
        (self.root / ini.name).write_bytes(ini.read_bytes())
        u.apply_updates(self.scan(), self.fetch)
        self.assertEqual((self.root / book.name).read_bytes(), book.read_bytes())
        self.assertEqual((self.root / ini.name).read_bytes(), ini.read_bytes())

    def test_numbering_existing_custom_history_takes_precedence_over_legacy_folder(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        ini, book = self.legacy_numbering(record)
        current = self.root / 'other.xlsx'
        current.write_bytes(b'current history')
        config = b'[Numbering]\nPrefix=P\nStartNumber=6000\nEndNumber=7000\nRegistryFile=other.xlsx\n'
        (self.root / ini.name).write_bytes(config)
        u.apply_updates(self.scan(), self.fetch)
        self.assertEqual(current.read_bytes(), b'current history')
        self.assertEqual((self.root / ini.name).read_bytes(), config)
        self.assertFalse((self.root / book.name).exists())
        self.assertEqual(len(self.calls), 2)

    def test_numbering_lost_legacy_registry_blocks_installation_without_blank_history(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        ini, book = self.legacy_numbering(record)
        book.unlink()
        with self.assertRaisesRegex(ValueError, 'прежний Excel'):
            u.apply_updates(self.scan(), self.fetch)
        self.assertFalse((self.root / record['file']).exists())
        self.assertFalse((self.root / ini.name).exists())

    def test_numbering_existing_script_without_settings_does_not_reset_history(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        script = self.root / record['file']
        before = source('V1.03', '.cs')
        script.write_bytes(before)
        with self.assertRaisesRegex(ValueError, 'прежний INI'):
            u.apply_updates(self.scan(), self.fetch)
        self.assertEqual(script.read_bytes(), before)
        self.assertFalse((self.root / record['install_files'][0]['file']).exists())

    def test_numbering_orphaned_legacy_backup_prevents_blank_registry(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        self.data.mkdir()
        backup = self.data / (record['install_files'][0]['file'] + '.bak')
        backup.write_bytes(b'old history, lost ini')
        with self.assertRaisesRegex(ValueError, 'прежний INI'):
            u.apply_updates(self.scan(), self.fetch)
        self.assertFalse((self.root / record['file']).exists())
        self.assertEqual(backup.read_bytes(), b'old history, lost ini')

    def test_numbering_conflicting_destination_registry_is_not_overwritten(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        ini, book = self.legacy_numbering(record)
        target = self.root / book.name
        target.write_bytes(b'different history')
        with self.assertRaisesRegex(ValueError, 'разные Excel'):
            u.apply_updates(self.scan(), self.fetch)
        self.assertEqual(target.read_bytes(), b'different history')
        self.assertFalse((self.root / ini.name).exists())

    def test_numbering_changed_source_after_preparation_aborts_before_writes(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        ini, book = self.legacy_numbering(record)
        script = self.root / record['file']
        before = source('V1.03', '.cs')
        script.write_bytes(before)
        jobs = u.prepare_jobs(self.scan(), self.fetch)
        book.write_bytes(b'another number was just issued')
        with self.assertRaisesRegex(ValueError, 'изменились'):
            u.commit_jobs(jobs)
        self.assertEqual(script.read_bytes(), before)
        self.assertFalse((self.root / ini.name).exists())
        self.assertFalse((self.root / book.name).exists())
        self.assert_no_stages()

    def test_numbering_active_legacy_lock_aborts_before_writes(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        ini, book = self.legacy_numbering(record)
        lock = Path(str(book) + '.lock')
        lock.write_bytes(b'')
        original = u.open_replace_guard

        def busy(path):
            if path == str(lock):
                raise PermissionError('legacy numbering is running')
            return original(path)

        with patch.object(u, 'open_replace_guard', side_effect=busy), self.assertRaises(PermissionError):
            u.apply_updates(self.scan(), self.fetch)
        self.assertFalse((self.root / record['file']).exists())
        self.assertFalse((self.root / ini.name).exists())
        self.assertFalse((self.root / book.name).exists())

    def test_numbering_moved_ini_with_different_range_does_not_import_old_history(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        ini, book = self.legacy_numbering(record)
        changed = ini.read_bytes().replace(b'StartNumber=1659', b'StartNumber=2000')
        (self.root / ini.name).write_bytes(changed)
        with self.assertRaisesRegex(ValueError, 'диапазон'):
            u.apply_updates(self.scan(), self.fetch)
        self.assertEqual((self.root / ini.name).read_bytes(), changed)
        self.assertFalse((self.root / book.name).exists())

    def test_numbering_migration_failure_rolls_back_script_and_copied_registry(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        ini, book = self.legacy_numbering(record)
        script = self.root / record['file']
        before = source('V1.03', '.cs')
        script.write_bytes(before)
        install = u._install_staged

        def fail_ini(stage, target):
            if target.endswith('.ini'):
                raise OSError('simulated disk error')
            return install(stage, target)

        with patch.object(u, '_install_staged', side_effect=fail_ini), self.assertRaises(RuntimeError):
            u.apply_updates(self.scan(), self.fetch)
        self.assertEqual(script.read_bytes(), before)
        self.assertFalse((self.root / ini.name).exists())
        self.assertFalse((self.root / book.name).exists())
        self.assertTrue(book.is_file())
        self.assert_no_stages()

    def test_numbering_same_version_missing_files_can_be_restored_manually(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        ini, book = self.legacy_numbering(record)
        (self.root / record['file']).write_bytes(u.pack_script(self.payloads[record['path']], record['file']))
        row = self.scan()[0]
        self.assertTrue(row['eligible'])
        self.assertFalse(row['checked'])
        u.apply_updates([row], self.fetch)
        self.assertEqual((self.root / book.name).read_bytes(), book.read_bytes())
        self.assertFalse(self.scan()[0]['eligible'])

    def test_numbering_template_checksum_failure_leaves_no_installed_files(self):
        record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        self.payloads[record['install_files'][0]['path']] += b'broken download'
        with self.assertRaisesRegex(ValueError, 'SHA-256'):
            u.apply_updates(self.scan(), self.fetch)
        self.assertEqual(list(self.root.iterdir()), [])

    def test_concurrent_local_edit_aborts_before_any_write(self):
        self.add_script()
        target = self.root / 'Example.py'
        target.write_bytes(source('V1.09'))
        rows = self.scan()
        changed = source('V1.09') + b'# edited after scan\n'
        target.write_bytes(changed)
        with self.assertRaises(ValueError):
            u.apply_updates(rows, self.fetch)
        self.assertEqual(target.read_bytes(), changed)
        self.assert_no_stages()

    def test_failed_second_replace_rolls_back_first_and_removes_new(self):
        originals = {}
        for name in ('First.py', 'Second.py'):
            self.add_script(name)
            path = self.root / name
            originals[path] = source('V1.09')
            path.write_bytes(originals[path])
        self.add_script('New.py', config=True)
        count = [0]

        def fail_second(stage, target):
            count[0] += 1
            if count[0] == 2:
                raise OSError('simulated disk error')
            os.replace(stage, target)

        with self.assertRaises(RuntimeError):
            u.apply_updates(self.scan(), self.fetch, replace=fail_second)
        self.assertEqual(count[0], 2, 'The first replacement must succeed before the simulated failure')
        for path, before in originals.items():
            self.assertEqual(path.read_bytes(), before)
        self.assertFalse((self.root / 'New.py').exists())
        self.assertFalse((self.root / 'New.ini').exists())
        self.assert_no_stages()

    def test_creation_race_preserves_other_program_file(self):
        self.add_script()
        rows = self.scan()
        target = self.root / 'Example.py'
        target.write_bytes(b'created by another program')
        with self.assertRaises(FileExistsError):
            u.apply_updates(rows, self.fetch)
        self.assertEqual(target.read_bytes(), b'created by another program')

    def test_self_update_is_deferred_until_explicit_final_commit(self):
        self.add_script('NX_Update_Scripts.py', 'V1.10')
        target = self.root / 'NX_Update_Scripts.py'
        before = source('V1.09')
        target.write_bytes(before)
        legacy = self.root / 'NX_Update_Script_Buttons.py'
        legacy_before = source('V1.12')
        legacy.write_bytes(legacy_before)
        rows = self.scan(self_path=str(target))
        self.assertEqual(len(rows), 1)
        self.assertTrue(rows[0]['is_self'])
        self.assertFalse(rows[0]['checked'])
        with self.assertRaises(ValueError):
            u.apply_updates(rows, self.fetch)
        pending = u.apply_updates(rows, self.fetch, defer_self=True)
        self.assertEqual(target.read_bytes(), before)
        self.assert_no_stages()
        u.commit_jobs([pending])
        self.assertEqual(u.internal_info(str(target), target.read_bytes())['label'], 'V1.10')
        self.assertEqual(legacy.read_bytes(), legacy_before)

    def test_new_minimum_allows_only_updater(self):
        self.add_script()
        self.add_script('NX_Update_Scripts.py', 'V1.11')
        self.manifest['min_updater_version'] = 'V1.11'
        # Model an older client explicitly so this remains a minimum-version
        # regression test when the real updater advances to V1.11 and beyond.
        with patch.object(u, 'SCRIPT_VERSION', 'V1.10'):
            rows = {r['remote']['id']: r for r in self.scan()}
        self.assertFalse(rows['Example']['eligible'])
        self.assertTrue(rows['NX_Update_Scripts']['eligible'])

    def test_legacy_ini_preserves_paths_unknown_options_and_sections(self):
        ini = Path(self.settings['config_path'])
        ini.write_text('[Paths]\nworking_folder=' + str(self.root) + '\nupdate_folder=' + str(self.root / 'offline') +
                       '\n[Options]\nsearch_working_subfolders=no\ncustom_option=keep\n[Personal]\nvalue=preserve\n')
        cfg = u.load_settings(str(self.root / 'NX_Update_Scripts.py'))
        self.assertEqual(cfg['source_type'], 'github')
        saved = u.save_settings(cfg)
        parsed = u.config_parser(saved['_config_bytes'])
        self.assertEqual(parsed['Personal']['value'], 'preserve')
        self.assertEqual(parsed['Options']['custom_option'], 'keep')
        self.assertEqual(parsed['Paths']['update_folder'], str(self.root / 'offline'))

    def test_renamed_updater_preserves_legacy_ini_without_a_second_config(self):
        legacy = self.root / 'NX_Update_Script_Buttons.ini'
        current = self.root / 'NX_Update_Scripts.ini'
        legacy.write_text('[Paths]\nworking_folder=' + str(self.root / 'custom') +
                          '\nupdate_folder=\n[Options]\ncustom_option=keep\n'
                          'exclude_files=NX_Update_Script_Buttons*.py\n'
                          '[Personal]\nvalue=preserve\n')
        cfg = u.load_settings(str(self.root / 'NX_Update_Scripts.py'))
        self.assertEqual(cfg['config_path'], str(legacy))
        self.assertEqual(cfg['working_folder'], str(self.root / 'custom'))
        saved = u.save_settings(cfg)
        parsed = u.config_parser(saved['_config_bytes'])
        self.assertEqual(parsed['Personal']['value'], 'preserve')
        self.assertEqual(parsed['Options']['custom_option'], 'keep')
        self.assertEqual(set(self.root.iterdir()), {legacy})
        legacy.rename(current)
        cfg = u.load_settings(str(self.root / 'NX_Update_Scripts.py'))
        self.assertEqual(cfg['config_path'], str(current))
        self.assertEqual(cfg['working_folder'], str(self.root / 'custom'))
        self.assertEqual(u.save_settings(cfg)['_config_bytes'], current.read_bytes())
        self.assertEqual(set(self.root.iterdir()), {current})
        legacy.write_text('[Paths]\nworking_folder=' + str(self.root / 'obsolete') + '\n')
        self.assertEqual(u.load_settings(str(self.root / 'NX_Update_Scripts.py'))['config_path'], str(current))

    def test_folder_source_excludes_both_updater_names(self):
        remote = self.root / 'offline'
        remote.mkdir()
        originals = {}
        for name in ('NX_Update_Scripts.py', 'NX_Update_Script_Buttons.py'):
            target = self.root / name
            originals[target] = source('V1.12')
            target.write_bytes(originals[target])
            (remote / name).write_bytes(source('V1.14'))
        (remote / 'Example_V1.10.py').write_bytes(source())
        cfg = dict(self.settings, source_type='folder', update_folder=str(remote), exclude_files=())
        rows = u.scan_settings(cfg, threading.Event())
        self.assertEqual([Path(row['path']).name for row in rows], ['Example.py'])
        u.apply_updates(rows)
        self.assertTrue((self.root / 'Example.py').is_file())
        for path, before in originals.items():
            self.assertEqual(path.read_bytes(), before)

    def test_folder_source_still_updates_stable_name(self):
        remote = self.root / 'offline'
        remote.mkdir()
        (remote / 'Example_V1.10.py').write_bytes(source())
        (self.root / 'Example.py').write_bytes(source('V1.09'))
        cfg = dict(self.settings, source_type='folder', update_folder=str(remote))
        rows = u.scan_settings(cfg, threading.Event())
        u.apply_updates(rows)
        self.assertEqual(u.unpack_script((self.root / 'Example.py').read_bytes())[0], source())

    def test_cleanup_keeps_active_stages_and_unrelated_files(self):
        stale = self.root / ('.nxupdater-Example.py.999999999.' + 'a' * 32 + '.pending')
        active = self.root / ('.nxupdater-Example.py.' + str(os.getpid()) + '.' + 'b' * 32 + '.pending')
        unrelated = self.root / 'user.pending'
        for p in (stale, active, unrelated):
            p.write_bytes(b'pending')
        with patch.object(u, '_process_running', side_effect=lambda pid: pid == os.getpid()):
            u.cleanup_stages(self.settings)
        self.assertFalse(stale.exists())
        self.assertTrue(active.exists())
        self.assertTrue(unrelated.exists())


if __name__ == '__main__':
    unittest.main()
