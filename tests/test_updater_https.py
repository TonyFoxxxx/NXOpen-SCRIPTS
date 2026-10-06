"""Regression coverage for the embedded-Python certificate failure; no network/NX."""
import ctypes
import importlib.util
import io
import os
from pathlib import Path
import ssl
import threading
import unittest
import urllib.error
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('updater_https', ROOT / 'scripts/NX_Update_Scripts.py')
u = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(u)
D = ctypes.c_uint32


class FakeWinHttp:
    """Model documented WinHTTP buffers, errors and opaque 64-bit handles."""
    def __init__(self, payload=b'{"ok":true}', declared=None, status=200):
        self.stream = io.BytesIO(payload)
        self.declared = str(len(payload)) if declared is None else declared
        self.status = status
        self.error = 0
        self.closed = []
        self.options = []
        self.WinHttpOpen = Mock(return_value=0x100000001)
        self.WinHttpConnect = Mock(return_value=0x100000002)
        self.WinHttpOpenRequest = Mock(return_value=0x100000003)
        self.WinHttpSetTimeouts = Mock(return_value=1)
        self.WinHttpSetOption = Mock(side_effect=self.option)
        self.WinHttpSendRequest = Mock(return_value=1)
        self.WinHttpReceiveResponse = Mock(return_value=1)
        self.WinHttpQueryHeaders = Mock(side_effect=self.query)
        self.WinHttpReadData = Mock(side_effect=self.read)
        self.WinHttpCloseHandle = Mock(side_effect=lambda handle: self.closed.append(handle) or 1)

    def option(self, handle, option, value, size):
        if size != 4:
            raise AssertionError('WinHTTP options require a 32-bit DWORD')
        # Windows SDK contract, independent of the updater's constants:
        # SECURE_PROTOCOLS=84 applies to sessions; SECURITY_FLAGS=31 to requests.
        # https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/Networking/WinHttp/constant.WINHTTP_OPTION_SECURE_PROTOCOLS.html
        valid_handles = {84: (0x100000001,), 31: (0x100000003,),
                         88: (0x100000001, 0x100000003)}
        if option not in valid_handles:
            self.error = 12009  # ERROR_WINHTTP_INVALID_OPTION
            return 0
        if handle not in valid_handles[option]:
            self.error = 12018  # ERROR_WINHTTP_INCORRECT_HANDLE_TYPE
            return 0
        if option == 31:
            raise AssertionError('The updater must not override certificate security flags')
        self.options.append((handle, option, ctypes.cast(value, ctypes.POINTER(D))[0]))
        return 1

    def query(self, handle, info, name, buffer, length, index):
        if info == 19 | 0x20000000:  # WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER
            ctypes.cast(buffer, ctypes.POINTER(D))[0] = self.status
            return 1
        if info != 5:
            raise AssertionError('Unexpected query')
        if self.declared is False:
            self.error = 12150  # ERROR_WINHTTP_HEADER_NOT_FOUND
            return 0
        if (len(self.declared) + 1) * ctypes.sizeof(ctypes.c_wchar) > ctypes.cast(length, ctypes.POINTER(D))[0]:
            self.error = 122  # ERROR_INSUFFICIENT_BUFFER
            return 0
        buffer.value = self.declared
        return 1

    def read(self, handle, buffer, count, received):
        chunk = self.stream.read(count)
        ctypes.memmove(buffer, chunk, len(chunk))
        ctypes.cast(received, ctypes.POINTER(D))[0] = len(chunk)
        return 1


class HttpsTests(unittest.TestCase):
    def setUp(self):
        self.addCleanup(patch.stopall)

    def native(self, fake, limit=1000000, cancelled=None, url=None):
        with patch.object(u.ctypes, 'WinDLL', return_value=fake, create=True) as dll, \
                patch.object(u.ctypes, 'get_last_error', side_effect=lambda: fake.error, create=True):
            result = u._download_bytes_winhttp(url or u.DEFAULT_MANIFEST_URL, limit, cancelled)
        dll.assert_called_once_with('winhttp', use_last_error=True)
        return result

    def python_failure(self, error, platform='nt'):
        patch.object(u.os, 'name', platform).start()
        opener = Mock()
        opener.open.side_effect = error
        patch.object(u.urllib.request, 'build_opener', return_value=opener).start()
        patch.object(u.ssl, 'create_default_context', return_value=object()).start()
        return patch.object(u, '_download_bytes_winhttp', return_value=b'valid data').start()

    def test_certificate_error_retries_windows_once(self):
        for wrapped in (True, False):
            with self.subTest(wrapped=wrapped):
                error = ssl.SSLCertVerificationError(1, 'unable to get issuer certificate')
                # A real direct SSLError also has a string-valued .reason.
                error.reason = 'CERTIFICATE_VERIFY_FAILED'
                fallback = self.python_failure(urllib.error.URLError(error) if wrapped else error)
                cancelled = threading.Event()
                self.assertEqual(u.download_bytes(u.DEFAULT_MANIFEST_URL, 100, cancelled), b'valid data')
                fallback.assert_called_once_with(u.DEFAULT_MANIFEST_URL, 100, cancelled)
                patch.stopall()

    def test_other_failures_and_non_windows_do_not_retry(self):
        for error, platform in ((urllib.error.URLError('timed out'), 'nt'),
                                (ssl.SSLError('TLS connection failed'), 'nt'),
                                (ValueError('wrong response URL'), 'nt'),
                                (urllib.error.URLError(ssl.SSLCertVerificationError(1, 'untrusted')), 'posix')):
            with self.subTest(error=error, platform=platform):
                fallback = self.python_failure(error, platform)
                with self.assertRaises(OSError):
                    u.download_bytes(u.DEFAULT_MANIFEST_URL, 100)
                fallback.assert_not_called()
                patch.stopall()

    def test_both_tls_stacks_reject_error_is_reported(self):
        fallback = self.python_failure(urllib.error.URLError(ssl.SSLCertVerificationError(1, 'issuer')))
        fallback.side_effect = OSError('Windows: certificate rejected')
        with self.assertRaisesRegex(OSError, 'Windows: certificate rejected') as error:
            u.download_bytes(u.DEFAULT_MANIFEST_URL, 100)
        self.assertIn('Python NX:', str(error.exception))
        fallback.assert_called_once()

    def test_successful_python_download_does_not_use_fallback(self):
        response = Mock()
        response.geturl.return_value = u.DEFAULT_MANIFEST_URL
        response.status, response.headers = 200, {'Content-Length': '2'}
        response.read = io.BytesIO(b'{}').read
        context = Mock()
        context.__enter__ = Mock(return_value=response)
        context.__exit__ = Mock(return_value=False)
        opener = Mock()
        opener.open.return_value = context
        with patch.object(u.urllib.request, 'build_opener', return_value=opener), \
                patch.object(u.ssl, 'create_default_context', return_value=object()), \
                patch.object(u, '_download_bytes_winhttp') as fallback:
            self.assertEqual(u.download_bytes(u.DEFAULT_MANIFEST_URL, 10), b'{}')
        fallback.assert_not_called()

    def test_native_uses_https_proxy_tls_and_closes_64bit_handles(self):
        payload = bytes(range(256)) * 700
        fake = FakeWinHttp(payload)
        self.assertEqual(self.native(fake), payload)
        self.assertEqual(fake.closed, [0x100000003, 0x100000002, 0x100000001])
        self.assertEqual(fake.WinHttpOpen.call_args.args[1:], (4, None, None, 0))
        self.assertEqual(fake.WinHttpConnect.call_args.args[1:], ('raw.githubusercontent.com', 443, 0))
        self.assertEqual(fake.WinHttpOpenRequest.call_args.args[1:],
                         ('GET', '/TonyFoxxxx/NXOpen-SCRIPTS/main/manifest.json', None, None, None, 0x800000))
        self.assertEqual(fake.options, [(0x100000001, 84, 0x800), (0x100000003, 88, 0)])
        self.assertIs(fake.WinHttpOpen.restype, ctypes.c_void_p)
        self.assertIs(fake.WinHttpSendRequest.argtypes[-1], ctypes.c_size_t)
        fake.WinHttpSetTimeouts.assert_called_once_with(0x100000001, 20000, 20000, 20000, 20000)

    def test_native_unknown_length_and_exact_limit(self):
        for declared in ('3', False):
            with self.subTest(declared=declared):
                self.assertEqual(self.native(FakeWinHttp(b'abc', declared), limit=3), b'abc')

    @unittest.skipUnless(os.name == 'nt', 'Requires the real Windows WinHTTP API')
    def test_real_windows_configures_tls_before_any_network_request(self):
        """Exercise real handles/options; intercept SendRequest before any network I/O."""
        class BeforeNetwork(Exception):
            pass

        api = ctypes.WinDLL('winhttp', use_last_error=True)
        with patch.object(api, 'WinHttpSendRequest', side_effect=BeforeNetwork) as send, \
                patch.object(u.ctypes, 'WinDLL', return_value=api):
            with self.assertRaises(BeforeNetwork):
                u._download_bytes_winhttp(u.DEFAULT_MANIFEST_URL, 1024)
        send.assert_called_once()

    def test_native_size_empty_and_incomplete_responses_rejected(self):
        for payload, declared, limit in ((b'abcd', False, 3), (b'a', '4', 3),
                                          (b'a', '3', 3), (b'', '0', 3),
                                          (b'abc', '-1', 3), (b'a', '9' * 100, 3)):
            with self.subTest(payload=payload, declared=declared):
                fake = FakeWinHttp(payload, declared)
                with self.assertRaises((ValueError, OSError)):
                    self.native(fake, limit)
                self.assertEqual(fake.closed, [0x100000003, 0x100000002, 0x100000001])

    def test_native_rejects_redirects_and_http_errors_without_reading(self):
        for status in (301, 302, 307, 308, 401, 404, 500):
            with self.subTest(status=status):
                fake = FakeWinHttp(status=status)
                with self.assertRaisesRegex(ValueError, 'HTTP ' + str(status)):
                    self.native(fake)
                fake.WinHttpReadData.assert_not_called()
                self.assertIn((0x100000003, 88, 0), fake.options)
                self.assertEqual(len(fake.closed), 3)

    def test_native_errors_stop_and_close_every_open_handle(self):
        for name, closed in (('WinHttpOpen', []), ('WinHttpSetTimeouts', [0x100000001]),
                             ('WinHttpSetOption', [0x100000001]),
                             ('WinHttpConnect', [0x100000001]),
                             ('WinHttpOpenRequest', [0x100000002, 0x100000001]),
                             ('WinHttpSendRequest', [0x100000003, 0x100000002, 0x100000001]),
                             ('WinHttpReceiveResponse', [0x100000003, 0x100000002, 0x100000001]),
                             ('WinHttpReadData', [0x100000003, 0x100000002, 0x100000001])):
            with self.subTest(name=name):
                fake = FakeWinHttp()
                fake.error = 12175
                setattr(fake, name, Mock(return_value=0))
                with self.assertRaisesRegex(OSError, 'Windows не подтвердила сертификат'):
                    self.native(fake)
                self.assertEqual(fake.closed, closed)

    def test_cancelled_retry_creates_no_session_and_closes_active_session(self):
        cancelled = threading.Event()
        cancelled.set()
        fake = FakeWinHttp()
        with self.assertRaisesRegex(RuntimeError, 'отменена'):
            self.native(fake, cancelled=cancelled)
        fake.WinHttpOpen.assert_not_called()
        cancelled.clear()
        fake.WinHttpReceiveResponse.side_effect = lambda *args: cancelled.set() or 1
        with self.assertRaisesRegex(RuntimeError, 'отменена'):
            self.native(fake, cancelled=cancelled)
        fake.WinHttpReadData.assert_not_called()
        self.assertEqual(len(fake.closed), 3)

    def test_foreign_native_url_rejected_before_any_request(self):
        fake = FakeWinHttp()
        for url in ('http://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/manifest.json',
                    'https://example.com/manifest.json', u.DEFAULT_MANIFEST_URL + '?x=1'):
            with self.subTest(url=url), self.assertRaises(ValueError):
                self.native(fake, url=url)
        fake.WinHttpOpen.assert_not_called()

    def test_sha256_still_checked_after_windows_retry(self):
        fallback = self.python_failure(urllib.error.URLError(ssl.SSLCertVerificationError(1, 'issuer')))
        fallback.return_value = b'changed on server'
        with self.assertRaisesRegex(ValueError, 'SHA-256'):
            u._verified_download({'path': 'scripts/Example.py', 'sha256': u.digest(b'expected')},
                                 1000, u.download_bytes)


if __name__ == '__main__':
    unittest.main()
