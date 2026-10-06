"""Real independent helper: readiness, NX replay, exclusion and safe fallback."""
from concurrent.futures import ThreadPoolExecutor
import ctypes
import hashlib
import http.client
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest.mock import patch
from urllib.parse import urlsplit

SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Setup_Prototype.py'
SPEC = importlib.util.spec_from_file_location('setup_card_remote_test', SOURCE)
CARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CARD)


def document(revision='a' * 32):
    return ('<!doctype html><html><head><meta name="nx-card-id" content="sample-card">'
            '<meta name="nx-card-revision" content="%s">'
            '<meta name="nx-card-format" content="setup-sections-v1">'
            '</head><body>Примечание и привязки</body></html>' % revision).encode('utf-8')


class RemoteTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name) / 'Карта с пробелами.html'
        self.path.write_bytes(document())
        self.helper = CARD.RemoteCardHelper(sys.executable)
        self.addCleanup(self.stop)
        self.runtime = {'lock': CARD.threading.RLock(), 'helper': self.helper}
        runtime_patch = patch.object(CARD, 'local_card_runtime', return_value=self.runtime)
        runtime_patch.start()
        self.addCleanup(runtime_patch.stop)
        self.url = CARD.local_card_url(self.path)
        self.address = urlsplit(self.url)
        status, body = self.request()
        self.assertEqual(status, 200)
        self.session = json.loads(re.search(rb'id="nx-local-session"[^>]*>(.*?)</script>', body)[1])

    def stop(self):
        if self.helper.process.poll() is None:
            self.helper.process.stdin.close()
            try:
                self.helper.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.helper.process.kill()  # Only this test's process and temporary fixture.
                self.helper.process.wait(timeout=5)
        self.helper.reader.join(timeout=3)
        self.helper.process.stdout.close()

    def request(self, method='GET', revision='b' * 32, previous='a' * 32):
        connection = http.client.HTTPConnection(self.address.netloc, timeout=5)
        try:
            if method == 'POST':
                headers = {'Origin': 'http://' + self.address.netloc, 'X-NX-Card-Key': self.session['key'],
                           'X-NX-Card-Revision': previous, 'X-NX-Card-Next': revision,
                           'Content-Type': 'text/html; charset=utf-8'}
                connection.request(method, self.address.path + 'save', document(revision), headers)
            else:
                connection.request(method, self.address.path)
            response = connection.getresponse()
            return response.status, response.read()
        finally:
            connection.close()

    def test_independent_process_save_and_eof_shutdown_without_sidecars(self):
        self.assertNotEqual(self.helper.process.pid, os.getpid())
        self.assertEqual(self.request('POST')[0], 200)
        self.assertEqual(self.path.read_bytes(), document('b' * 32))
        self.assertEqual(list(self.path.parent.iterdir()), [self.path])
        self.stop()
        self.assertEqual(self.helper.process.returncode, 0)
        with self.assertRaises(OSError):
            self.request()

    def test_responds_while_parent_interpreter_is_blocked(self):
        # Simulate an embedded host holding the GIL after Journal / Play.
        code = ('import http.client,sys,time,urllib.parse;time.sleep(.1);'
                'u=urllib.parse.urlsplit(sys.argv[1]);s=time.monotonic();'
                'c=http.client.HTTPConnection(u.netloc,timeout=.8);c.request("GET",u.path);'
                'r=c.getresponse();r.read();assert r.status==200;'
                'assert time.monotonic()-s<.8;c.close()')
        client = subprocess.Popen([sys.executable, '-I', '-S', '-B', '-c', code, self.url],
                                  stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if os.name == 'nt':
            sleep = ctypes.PyDLL('kernel32').Sleep
            sleep.argtypes, sleep.restype = [ctypes.c_ulong], None
            sleep(1800)
        else:
            sleep = ctypes.PyDLL(None).usleep
            sleep.argtypes, sleep.restype = [ctypes.c_uint], ctypes.c_int
            sleep(1800000)
        out, err = client.communicate(timeout=5)
        self.assertEqual(client.returncode, 0, (out, err))

    def test_journal_replay_does_not_restart_helper(self):
        # Different class, same stable protocol (as with a new journal namespace).
        proxy = SimpleNamespace(external_protocol=1, process=self.helper.process,
                                register=self.helper.register, request=self.helper.request)
        self.runtime['helper'] = proxy
        with patch.object(CARD, 'RemoteCardHelper', side_effect=AssertionError('must reuse')):
            self.assertEqual(CARD.local_card_url(self.path), self.url)
            with CARD.local_card_guard():
                pass

    def test_export_blocks_save_and_stale_tab_cannot_overwrite_export(self):
        stage = self.path.with_name('stage.html')
        stage.write_bytes(document('c' * 32))
        with ThreadPoolExecutor(max_workers=1) as pool:
            with CARD.local_card_guard():
                future = pool.submit(self.request, 'POST')
                time.sleep(.15)
                self.assertFalse(future.done())
                CARD.publish_output(stage, self.path, hashlib.sha256(document()).hexdigest())
            self.assertEqual(future.result(timeout=5)[0], 409)
        self.assertEqual(self.path.read_bytes(), document('c' * 32))

    def test_autosave_during_export_preserved_by_digest_check(self):
        stage = self.path.with_name('stage.html')
        stage.write_bytes(document('c' * 32))
        self.assertEqual(self.request('POST')[0], 200)
        with self.assertRaisesRegex(RuntimeError, 'изменилась'):
            CARD.publish_output(stage, self.path, hashlib.sha256(document()).hexdigest())
        self.assertEqual(self.path.read_bytes(), document('b' * 32))

    def test_guard_releases_after_exception_and_retires_dead_helper(self):
        with self.assertRaisesRegex(ValueError, 'test'):
            with CARD.local_card_guard():
                raise ValueError('test')
        self.assertEqual(self.request('POST')[0], 200)
        self.stop()
        with CARD.local_card_guard():
            self.assertIsNone(self.runtime['helper'])

    def test_readiness_failure_never_returns_url(self):
        with patch.object(CARD.http.client, 'HTTPConnection', side_effect=TimeoutError('no response')):
            with self.assertRaises(TimeoutError):
                self.helper.register(self.path)

    def test_legacy_in_process_helper_retires_before_opening_new_card(self):
        legacy = CARD.LocalCardHelper(self.runtime['lock'])
        old_url = legacy.register(self.path)
        old_entry = legacy.entries[urlsplit(old_url).path]
        self.runtime['helper'] = legacy
        with patch.object(CARD, 'helper_python_candidates', return_value=iter((sys.executable,))), \
                patch.object(CARD, 'RemoteCardHelper', return_value=self.helper):
            self.assertEqual(CARD.local_card_url(self.path), self.url)
        self.assertNotEqual(old_entry['id'], 'sample-card')
        legacy.thread.join(timeout=3)
        self.assertFalse(legacy.thread.is_alive())
        self.assertEqual(self.request('POST')[0], 200)

    @unittest.skipUnless(os.name == 'nt', 'Real long Windows path in the independent worker')
    def test_remote_autosave_with_long_unicode_path(self):
        deep = CARD.io_path(self.path.parent) / ('Папка_' * 12) / ('Проект_' * 12) / ('Деталь_' * 12)
        deep.mkdir(parents=True)
        self.path = deep / 'Карта.html'
        self.path.write_bytes(document())
        try:
            self.url = CARD.local_card_url(self.path)
            self.address = urlsplit(self.url)
            self.session = json.loads(re.search(rb'id="nx-local-session"[^>]*>(.*?)</script>', self.request()[1])[1])
            self.assertEqual(self.request('POST')[0], 200)
            self.assertEqual(self.path.read_bytes(), document('b' * 32))
        finally:
            CARD.cleanup_work_files(deep.parent.parent, [])


class FallbackTests(unittest.TestCase):
    def test_unavailable_helper_opens_file_automatically_not_dead_url(self):
        card = Path('/card/карта.html')
        with patch.object(CARD.os, 'name', 'nt'), \
                patch.object(CARD.os, 'startfile', create=True) as opened, \
                patch.object(CARD, 'export_is_complete', return_value=True), \
                patch.object(CARD, 'local_card_url', side_effect=RuntimeError('unavailable')):
            errors = CARD.open_output_folder(card, {}, open_folder=False)
        opened.assert_called_once_with(str(card))
        self.assertEqual(len(errors), 1)
        self.assertIn('обычным сохранением', errors[0])

    def test_no_runtime_never_attempts_to_launch_nx(self):
        runtime = {'lock': CARD.threading.RLock(), 'helper': None}
        with patch.object(CARD, 'local_card_runtime', return_value=runtime), \
                patch.object(CARD, 'helper_python_candidates', return_value=iter(())), \
                patch.object(CARD, 'RemoteCardHelper') as launch:
            with self.assertRaisesRegex(RuntimeError, 'отдельный Python'):
                CARD.local_card_url(Path('card.html'))
        launch.assert_not_called()


if __name__ == '__main__':
    unittest.main()
