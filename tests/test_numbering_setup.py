"""Explicit setup, preservation of real XLSX records, and native dialog smoke test."""
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch

import test_updater as baseline

ROOT, u = baseline.ROOT, baseline.u

SPEC = importlib.util.spec_from_file_location('numbering_recovery', ROOT / 'tools/NX_Recover_Numbering_Register.py')
recovery = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(recovery)


class NumberingSetupTests(unittest.TestCase):
    setUp = baseline.UpdaterTests.setUp
    add_script = baseline.UpdaterTests.add_script
    fetch = baseline.UpdaterTests.fetch
    scan = baseline.UpdaterTests.scan

    def number_row(self):
        self.record = self.add_script('NX_Number_Program_Folders.cs', config=True, numbering=True)
        return self.scan()[0]

    def filled_book(self):
        import datetime
        book = recovery.Register((ROOT / 'templates/NX_Numbering_Register_v1.0.xlsx').read_bytes())
        book.append(dict(name='O1689', number=1689, project='Part', path=r'C:\Projects\Part.prt',
                         id='a' * 32, state='Присвоен'), datetime.datetime(2026, 10, 20))
        path = self.root / 'История с пробелами.xlsx'
        path.write_bytes(book.encoded())
        return path

    def test_connect_existing_keeps_all_bytes_and_creates_only_ini(self):
        row, book = self.number_row(), self.filled_book()
        before = book.read_bytes()
        u.apply_updates([row], self.fetch)
        jobs = u.prepare_numbering_setup(row, 'existing', str(book), 'O', '1659', '5000', self.fetch)
        self.assertEqual(len(jobs), 1)
        u.commit_jobs(jobs)
        self.assertEqual(book.read_bytes(), before)
        ini = self.root / self.record['config']['file']
        values = u._numbering_values(ini.read_bytes())
        self.assertEqual(values['startnumber'], '1659')
        self.assertEqual(values['endnumber'], '5000')
        self.assertEqual(values['registryfile'], book.name)
        self.assertEqual(len(recovery.Register(book.read_bytes()).records), 1)
        self.assertNotIn(u.RAW_ROOT + self.record['install_files'][0]['path'], self.calls)

    def test_connect_retains_existing_range_encoding_and_comments(self):
        row, book = self.number_row(), self.filled_book()
        ini = self.root / self.record['config']['file']
        before = ('; Keep this comment\r\n[Numbering]\r\nPrefix=P\r\nStartNumber=2000\r\n'
                  'EndNumber=6000\r\nRegistryFile=lost.xlsx\r\n').encode('utf-8-sig')
        ini.write_bytes(before)
        u.commit_jobs(u.prepare_numbering_setup(row, 'existing', str(book), 'O', '1', '1000', self.fetch))
        self.assertEqual(ini.read_bytes(), before.replace(b'lost.xlsx', book.name.encode('utf-8')))

    def test_missing_and_invalid_data_never_disable_same_version_script(self):
        row = self.number_row()
        (self.root / self.record['file']).write_bytes(u.pack_script(self.payloads[self.record['path']], self.record['file']))
        ini = self.root / self.record['config']['file']
        for raw in (None, b'broken INI', b'[Numbering]\nRegistryFile=lost.xlsx\n', b'\xff\xff'):
            with self.subTest(raw=raw):
                if raw is not None:
                    ini.write_bytes(raw)
                row = self.scan()[0]
                self.assertTrue(row['eligible'])
                self.assertFalse(row['checked'])
                u.apply_updates([row], self.fetch)
                self.assertIn('numbering_notice', row)
                if raw is not None:
                    self.assertEqual(ini.read_bytes(), raw)

    def test_range_is_required_no_example_defaults_or_overflow(self):
        row = self.number_row()
        for start, end in (('', ''), ('0', '100'), ('200', '100'), ('1', '2147483648'), ('1e2', '5000')):
            with self.subTest(start=start, end=end), self.assertRaises(ValueError):
                u.prepare_numbering_setup(row, 'new', prefix='O', start=start, end=end, fetcher=self.fetch)
        self.assertEqual(list(self.root.iterdir()), [])

    def test_existing_invalid_workbook_is_not_connected_or_modified(self):
        row = self.number_row()
        book = self.root / 'other.xlsx'
        book.write_bytes(b'not an xlsx')
        with self.assertRaises(ValueError):
            u.prepare_numbering_setup(row, 'existing', str(book), 'O', '1659', '5000', self.fetch)
        self.assertEqual(book.read_bytes(), b'not an xlsx')
        self.assertFalse((self.root / self.record['config']['file']).exists())

    def test_changed_existing_book_before_connect_keeps_ini_absent(self):
        row, book = self.number_row(), self.filled_book()
        jobs = u.prepare_numbering_setup(row, 'existing', str(book), 'O', '1659', '5000', self.fetch)
        book.write_bytes(book.read_bytes() + b'changed')
        with self.assertRaisesRegex(ValueError, 'изменились'):
            u.commit_jobs(jobs)
        self.assertFalse((self.root / self.record['config']['file']).exists())

    def test_setup_write_failure_does_not_undo_installed_script(self):
        row = self.number_row()
        u.apply_updates([row], self.fetch)
        jobs = u.prepare_numbering_setup(row, 'new', prefix='O', start='1659', end='5000', fetcher=self.fetch)
        install = u._install_staged

        def fail(stage, path):
            if path.endswith('.ini'):
                raise OSError('disk write failed')
            install(stage, path)

        with patch.object(u, '_install_staged', side_effect=fail), self.assertRaises(RuntimeError):
            u.commit_jobs(jobs)
        self.assertEqual({p.name for p in self.root.iterdir()}, {self.record['file']})

    def test_new_never_overwrites_existing_book_or_backup(self):
        row = self.number_row()
        for suffix in ('', '.bak', '.lock'):
            book = self.root / (self.record['install_files'][0]['file'] + suffix)
            book.write_bytes(b'precious history')
            with self.assertRaises(ValueError):
                u.prepare_numbering_setup(row, 'new', prefix='O', start='1659', end='5000', fetcher=self.fetch)
            self.assertEqual(book.read_bytes(), b'precious history')
            book.unlink()

    @unittest.skipUnless(os.name == 'nt', 'Native Windows dialog')
    def test_native_dialog_cancel_keeps_files_untouched(self):
        # A child process bounds a malformed template or modal-loop regression.
        code = r'''
import ctypes, importlib.util, os, tempfile, threading, time
from ctypes import wintypes as W
spec = importlib.util.spec_from_file_location('u', r"SOURCE")
u = importlib.util.module_from_spec(spec); spec.loader.exec_module(u)
user = ctypes.WinDLL('user32', use_last_error=True)
user.FindWindowW.argtypes = [W.LPCWSTR, W.LPCWSTR]; user.FindWindowW.restype = W.HWND
user.PostMessageW.argtypes = [W.HWND, W.UINT, ctypes.c_size_t, ctypes.c_ssize_t]
user.GetDlgItem.argtypes = [W.HWND, ctypes.c_int]; user.GetDlgItem.restype = W.HWND
found = []
def cancel():
    for _ in range(200):
        hwnd = user.FindWindowW(None, u.window_title('Настройка нумерации'))
        if hwnd:
            found.extend(bool(user.GetDlgItem(hwnd, n)) for n in (102,103,105,106,107,110,111,112,2))
            user.PostMessageW(hwnd, 0x111, 2, 0)
            return
        time.sleep(.025)
with tempfile.TemporaryDirectory() as folder:
    row = {'path': os.path.join(folder, 'NX_Number_Program_Folders.cs'),
           'remote': {'config': {'file': 'NX_Numbering_Settings_v1.0.ini'}}}
    worker = threading.Thread(target=cancel, daemon=True); worker.start()
    assert u.run_numbering_setup(None, row) is False
    worker.join(1)
    assert len(found) == 9 and all(found), found
    assert os.listdir(folder) == []
'''.replace('SOURCE', str(ROOT / 'scripts/NX_Update_Scripts.py'))
        result = subprocess.run([sys.executable, '-c', code], capture_output=True, text=True, timeout=15)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == '__main__':
    unittest.main()
