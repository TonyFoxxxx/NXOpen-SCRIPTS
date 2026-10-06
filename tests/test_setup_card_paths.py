"""Long Windows paths, short NX capture names and unchanged result location."""
import ast
import hashlib
import importlib.util
import os
from pathlib import Path, PureWindowsPath
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Setup_Prototype.py'
SPEC = importlib.util.spec_from_file_location('setup_card_paths_test', SOURCE)
CARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CARD)


class PathTests(unittest.TestCase):
    def test_drive_unc_and_existing_prefixes(self):
        for source, expected in (
                ('C:/Проект/a/../Карта.html', '\\\\?\\C:\\Проект\\Карта.html'),
                ('\\\\server\\share\\Папка\\Карта.html', '\\\\?\\UNC\\server\\share\\Папка\\Карта.html'),
                ('\\\\?\\C:\\Проект\\Карта.html', '\\\\?\\C:\\Проект\\Карта.html'),
                ('\\\\?\\UNC\\server\\share\\Карта.html', '\\\\?\\UNC\\server\\share\\Карта.html')):
            with self.subTest(source=source):
                self.assertEqual(CARD.windows_io_name(source), expected)
                self.assertNotIn('?', CARD.display_file_name(expected))
        for invalid in ('relative', 'C:relative', '\\relative', '\\\\server',
                        '\\\\.\\C:\\file', '\\\\?\\GLOBALROOT\\Device\\file'):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                CARD.windows_io_name(invalid)

    def test_length_measured_in_utf16_not_python_characters(self):
        self.assertTrue(CARD.windows_io_name('C:\\' + 'а' * 1000).startswith('\\\\?\\'))
        with self.assertRaises(ValueError):
            CARD.windows_io_name('C:\\' + '\U0001f600' * 16400)

    def test_nx_uses_existing_short_alias_or_stops_before_capture(self):
        long = PureWindowsPath('C:/') / ('Папка' * 45) / ('Деталь' * 10) / '1/01_top_no_ipw.png'
        with patch.object(CARD, 'io_path', side_effect=lambda p: p), patch.object(CARD.os, 'name', 'nt'):
            with patch.object(CARD, 'windows_short_name', return_value='C:\\SHORT\\1'):
                self.assertEqual(CARD.nx_image_file_name(long), 'C:\\SHORT\\1\\01_top_no_ipw.png')
            for result in (str(long.parent), OSError('8.3 unavailable')):
                with patch.object(CARD, 'windows_short_name', **(
                        {'side_effect': result} if isinstance(result, OSError) else {'return_value': result})):
                    with self.assertRaisesRegex(RuntimeError, 'Сохраните проект'):
                        CARD.nx_image_file_name(long)
            short = PureWindowsPath('C:/NX/1/01_top.png')
            with patch.object(CARD, 'windows_short_name', side_effect=AssertionError('not needed')):
                self.assertEqual(CARD.nx_image_file_name(short), str(short))

    def test_flat_staging_preflight_publish_and_cleanup(self):
        with tempfile.TemporaryDirectory() as directory:
            root = CARD.io_path(directory)
            (root / 'untouched.txt').write_text('keep')
            target = root / 'Карты Наладки' / 'Деталь'
            staging = CARD.make_output_folder(root, 'Деталь')
            self.assertEqual(staging.parent, root)
            self.assertTrue(staging.name.startswith('.nx_'))
            jobs = [{'report': {'views': []}} for _ in range(2)]
            with patch.object(CARD, 'nx_image_file_name', wraps=CARD.nx_image_file_name) as check:
                CARD.prepare_capture_folders(staging, jobs)
            self.assertEqual(check.call_count, 12)
            self.assertEqual([j['output'].name for j in jobs], ['1', '2'])
            for job in jobs:
                (job['output'] / '01_top.png').write_bytes(b'image')
                job['report']['views'] = [{'file': '01_top.png'}]
            CARD.collect_document_assets(staging, jobs)
            self.assertFalse((staging / '1').exists())
            self.assertEqual(jobs[1]['report']['views'][0]['file'], 's02_01_top.png')
            previous = target / 'Деталь.html'
            previous.write_bytes(b'old card')
            rendered = staging / previous.name
            rendered.write_bytes(b'new card')
            report = {'status': 'ok', 'views': [1], 'document_stem': 'Деталь',
                      '_existing_card': {'digest': hashlib.sha256(b'old card').hexdigest()}}
            with patch.object(CARD, 'write_preview', return_value=rendered), \
                    patch.object(CARD, 'export_is_complete', return_value=True):
                self.assertEqual(CARD.finalize_output(staging, report, target), previous)
            self.assertEqual(CARD.cleanup_work_files(staging, [target.parent, target]), [])
            self.assertEqual(previous.read_bytes(), b'new card')
            self.assertEqual((root / 'untouched.txt').read_text(), 'keep')
            self.assertFalse(staging.exists())

    def test_different_volume_keeps_atomic_replace(self):
        with tempfile.TemporaryDirectory() as directory:
            root = CARD.io_path(directory)
            target = root / 'Карты Наладки' / 'Деталь'
            target.mkdir(parents=True)
            actual_stat = type(root).stat
            def stat(path, *args, **kwargs):
                if path == root or path == target:
                    return SimpleNamespace(st_dev=1 if path == root else 2)
                return actual_stat(path, *args, **kwargs)
            with patch.object(type(root), 'stat', stat), patch.object(type(root), 'mkdir'):
                staging = CARD.make_output_folder(root, 'Деталь')
            self.assertEqual(staging.parent, target)
            CARD.cleanup_work_files(staging, [])

    def test_preflight_before_mesh_and_view_changes(self):
        tree = ast.parse(SOURCE.read_text(encoding='utf-8'))
        main = next(node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name == 'main')
        calls = {n.func.id: n.lineno for n in ast.walk(main)
                 if isinstance(n, ast.Call) and isinstance(n.func, ast.Name)}
        self.assertLess(calls['prepare_capture_folders'], calls['collect_project_model'])
        self.assertLess(calls['prepare_capture_folders'], calls['activate_first_mcs'])

    @unittest.skipUnless(os.name == 'nt', 'Real Windows filesystem and 8.3 API')
    def test_real_long_unicode_path_read_publish_ini_and_cleanup(self):
        with tempfile.TemporaryDirectory() as directory:
            root = CARD.io_path(directory)
            deep = root / ('Папка_' * 12) / ('Проект_' * 12) / ('Деталь_' * 12)
            deep.mkdir(parents=True)
            self.assertGreater(len(CARD.display_file_name(deep)), 260)
            target = deep / 'карта.html'
            target.write_bytes(b'old')
            stage = deep / 'stage.html'
            stage.write_bytes('Новая карта'.encode('utf-8'))
            CARD.publish_output(stage, target, hashlib.sha256(b'old').hexdigest())
            self.assertEqual(target.read_text(encoding='utf-8'), 'Новая карта')
            ini = deep / 'NX_Setup_Prototype.ini'
            ini.write_text('[SetupCard]\nprogrammer = Тест\n', encoding='utf-8')
            self.assertEqual(CARD.read_card_settings(ini)['author'], 'Тест')
            self.assertFalse(CARD.read_card_settings(ini)['author_settings']['file'].startswith('\\\\?\\'))
            short = CARD.windows_short_name(deep)
            self.assertTrue(CARD.io_path(short).samefile(deep))
            self.assertEqual(CARD.cleanup_work_files(root / ('Папка_' * 12), []), [])
            self.assertEqual(list(root.iterdir()), [])


if __name__ == '__main__':
    unittest.main()
