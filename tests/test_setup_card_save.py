"""The loopback helper never needs NX, a browser permission or extra files."""
from concurrent.futures import ThreadPoolExecutor
import hashlib
import http.client
import importlib.util
import json
from pathlib import Path
import re
import tempfile
import threading
import unittest
from unittest.mock import patch
from urllib.parse import urlsplit


SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Setup_Prototype.py'
SPEC = importlib.util.spec_from_file_location('setup_card_save_test', SOURCE)
CARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CARD)


def document(revision='a' * 32, ident='sample-card', text='Примечание'):
    return ('<!doctype html><html><head><meta charset="utf-8">'
            '<meta name="nx-card-id" content="%s">'
            '<meta name="nx-card-revision" content="%s">'
            '<meta name="nx-card-format" content="setup-sections-v1">'
            '</head><body>%s</body></html>' % (ident, revision, text)).encode('utf-8')


class LocalSaveTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.helper = CARD.LocalCardHelper(CARD.local_card_runtime()['lock'])

    @classmethod
    def tearDownClass(cls):
        cls.helper.server.shutdown()
        cls.helper.server.server_close()
        cls.helper.thread.join(timeout=3)

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name) / 'Деталь & карта.html'
        self.original = document()
        self.path.write_bytes(self.original)
        self.url = self.helper.register(self.path)
        self.route = urlsplit(self.url).path
        self.entry = self.helper.entries[self.route]

    def request(self, method='GET', route=None, body=None, headers=None):
        connection = http.client.HTTPConnection(self.helper.authority, timeout=5)
        try:
            connection.request(method, route or self.route, body=body, headers=headers or {})
            response = connection.getresponse()
            return response.status, dict(response.getheaders()), response.read()
        finally:
            connection.close()

    def save(self, next_revision='b' * 32, previous='a' * 32, payload=None, **headers):
        values = {'Origin': self.helper.origin, 'X-NX-Card-Key': self.entry['key'],
                  'X-NX-Card-Revision': previous, 'X-NX-Card-Next': next_revision,
                  'Content-Type': 'text/html; charset=utf-8'}
        values.update(headers)
        return self.request('POST', self.route + 'save',
                            document(next_revision, text='X=0; примечание изменено') if payload is None else payload,
                            values)

    def test_binds_only_loopback_and_serves_in_memory_session(self):
        self.assertEqual(self.helper.server.server_address[0], '127.0.0.1')
        self.assertTrue(self.helper.thread.daemon)
        status, headers, body = self.request()
        self.assertEqual(status, 200)
        session = json.loads(re.search(rb'id="nx-local-session"[^>]*>(.*?)</script>', body)[1])
        self.assertEqual(session, {'protocol': 1, 'id': 'sample-card', 'key': self.entry['key'],
                                   'save': self.route + 'save'})
        self.assertEqual(headers['Cache-Control'], 'no-store')
        self.assertEqual(headers['Referrer-Policy'], 'no-referrer')
        self.assertEqual(headers['X-Frame-Options'], 'DENY')
        self.assertNotIn('Access-Control-Allow-Origin', headers)
        self.assertEqual(self.path.read_bytes(), self.original)
        self.assertEqual(list(self.path.parent.iterdir()), [self.path])
        self.assertNotIn(str(self.path).encode('utf-8'), body)
        self.assertEqual(self.request('HEAD')[2], b'')

    def test_save_unicode_in_same_file_without_sidecars(self):
        payload = document('b' * 32, text='X=0; Y=центр; Z=верх\nПримечание')
        status, _, response = self.save(payload=payload)
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(response)['revision'], 'b' * 32)
        self.assertEqual(self.path.read_bytes(), payload)
        self.assertEqual(list(self.path.parent.iterdir()), [self.path])
        self.assertEqual(self.helper.register(self.path), self.url)

    def test_lost_response_retry_is_idempotent(self):
        self.assertEqual(self.save()[0], 200)
        after = self.path.read_bytes()
        self.assertEqual(self.save()[0], 200)
        self.assertEqual(self.path.read_bytes(), after)
        self.assertEqual(self.save(next_revision='c' * 32)[0], 409)
        self.assertEqual(self.path.read_bytes(), after)

    def test_two_tabs_never_overwrite_each_other(self):
        barrier = threading.Barrier(2)
        def write(revision):
            barrier.wait(timeout=3)
            return revision, self.save(next_revision=revision)[0]
        with ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(write, ('b' * 32, 'c' * 32)))
        self.assertEqual(sorted(status for _, status in results), [200, 409])
        winner = next(revision for revision, status in results if status == 200)
        self.assertEqual(CARD.local_card_identity(self.path.read_text(encoding='utf-8'))[1], winner)

    def test_host_origin_token_and_cross_site_requests_rejected(self):
        for headers in ({'Origin': 'https://example.com'}, {'Host': 'example.com'},
                        {'X-NX-Card-Key': 'wrong'}, {'Sec-Fetch-Site': 'cross-site'}):
            with self.subTest(headers=headers):
                self.assertEqual(self.save(**headers)[0], 403)
                self.assertEqual(self.path.read_bytes(), self.original)
        self.assertEqual(self.request(headers={'Host': 'localhost:' + self.helper.authority.split(':')[1]})[0], 403)
        self.assertEqual(self.request('OPTIONS', self.route + 'save')[0], 501)

    def test_no_arbitrary_path_directory_listing_or_query(self):
        for path in ('/', '/favicon.ico', '/card/', self.route + '../',
                     self.route + '?file=other.html', '/etc/passwd'):
            with self.subTest(path=path):
                self.assertEqual(self.request(route=path)[0], 404)
        status, _, _ = self.save(**{'X-NX-File-Path': 'not-a-destination.html'})
        self.assertEqual(status, 200)
        self.assertEqual(list(self.path.parent.iterdir()), [self.path])

    def test_bad_payloads_do_not_change_disk(self):
        for payload in (b'broken', document('b' * 32, ident='other'), document('c' * 32),
                        document('b' * 32)[:-7], b'\xff',
                        document('b' * 32, text='<script id="nx-local-session">{}</script>')):
            with self.subTest(payload=payload[:30]):
                self.assertEqual(self.save(payload=payload)[0], 422)
                self.assertEqual(self.path.read_bytes(), self.original)
        for headers, expected in (({'Content-Type': 'text/plain'}, 400),
                                  ({'Content-Length': str(256 * 1024 * 1024 + 1)}, 413),
                                  ({'X-NX-Card-Revision': 'not-revision'}, 400)):
            self.assertEqual(self.save(**headers)[0], expected)
            self.assertEqual(self.path.read_bytes(), self.original)

    def test_replaced_or_missing_document_never_written(self):
        self.path.write_bytes(document(ident='different-card'))
        self.assertEqual(self.save()[0], 409)
        self.assertEqual(self.request()[0], 409)
        other = self.helper.register(self.path)
        self.assertNotEqual(other, self.url)
        self.assertEqual(self.save()[0], 404)
        self.path.unlink()
        self.assertEqual(self.request(route=urlsplit(other).path)[0], 404)
        self.assertFalse(self.path.exists())

    def test_io_error_restores_previous_bytes_in_memory(self):
        with patch.object(CARD.os, 'fsync', side_effect=[OSError('write failed'), None]):
            self.assertEqual(self.save()[0], 500)
        self.assertEqual(self.path.read_bytes(), self.original)
        self.assertEqual(list(self.path.parent.iterdir()), [self.path])
        self.assertEqual(self.save()[0], 200)

    def test_export_conflicts_with_autosave_in_either_direction(self):
        digest = hashlib.sha256(self.original).hexdigest()
        staged = self.path.parent / 'staged.html'
        staged.write_bytes(document('c' * 32))
        self.assertEqual(self.save()[0], 200)
        after_save = self.path.read_bytes()
        with self.assertRaisesRegex(RuntimeError, 'изменилась'):
            CARD.publish_output(staged, self.path, digest)
        self.assertEqual(self.path.read_bytes(), after_save)
        CARD.publish_output(staged, self.path, hashlib.sha256(after_save).hexdigest())
        self.assertEqual(self.save(next_revision='d' * 32, previous='b' * 32)[0], 409)
        self.assertEqual(self.path.read_bytes(), document('c' * 32))


if __name__ == '__main__':
    unittest.main()
