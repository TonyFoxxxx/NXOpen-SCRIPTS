import ctypes
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

RUNNER = Path(__file__).resolve().parents[1] / 'tools/run_tests.py'


def invoke(log, command, timeout=20):
    return subprocess.run([sys.executable, str(RUNNER), '--timeout', str(timeout), '--log', str(log),
                           '--', sys.executable, *command], capture_output=True, timeout=timeout + 15,
                          creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))


class BackgroundTests(unittest.TestCase):
    def test_failure_is_logged_once(self):
        with tempfile.TemporaryDirectory() as tmp:
            log = Path(tmp) / 'test.log'
            result = invoke(log, ['-c', 'import sys; print("one attempt"); sys.exit(7)'])
            self.assertEqual(result.returncode, 7, result.stderr)
            lines = log.read_text(encoding='utf-8').splitlines()
            self.assertEqual(lines.count('one attempt'), 1)
            self.assertEqual(lines[-1], 'Exit code 7')

    @unittest.skipUnless(os.name == 'nt', 'Windows desktop isolation')
    def test_child_and_grandchild_share_the_private_desktop(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            log = folder / 'test.log'
            probe = folder / 'desktop.py'
            probe.write_text(
                'import ctypes as c, sys, subprocess\n'
                'u=c.windll.user32; u.GetThreadDesktop.restype=c.c_void_p\n'
                'desk=c.c_void_p(u.GetThreadDesktop(c.windll.kernel32.GetCurrentThreadId()))\n'
                'name=c.create_unicode_buffer(256); needed=c.c_ulong()\n'
                'assert u.GetUserObjectInformationW(desk, 2, name, c.sizeof(name), c.byref(needed))\n'
                'print("Observed " + name.value, flush=True)\n'
                'if len(sys.argv)==1: subprocess.run([sys.executable, __file__, "child"], check=True)\n',
                encoding='utf-8')
            result = invoke(log, [str(probe)])
            self.assertEqual(result.returncode, 0, result.stderr)
            lines = log.read_text(encoding='utf-8').splitlines()
            expected, = (line.split(' ', 1)[1] for line in lines if line.startswith('Desktop NXOpenTests_'))
            self.assertEqual(lines.count('Observed ' + expected), 2)

    @unittest.skipUnless(os.name == 'nt', 'Windows job object cleanup')
    def test_timeout_terminates_the_child_tree(self):
        from ctypes import wintypes as w
        with tempfile.TemporaryDirectory() as tmp:
            log = Path(tmp) / 'test.log'
            pid_file = Path(tmp) / 'child.pid'
            code = ('import subprocess, sys, time; from pathlib import Path; '
                    'child=subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"]); '
                    'Path(sys.argv[1]).write_text(str(child.pid)); time.sleep(30)')
            result = invoke(log, ['-c', code, str(pid_file)], timeout=3)
            self.assertEqual(result.returncode, 124, result.stderr)
            self.assertIn('Terminated the test process tree', log.read_text(encoding='utf-8'))
            kernel = ctypes.WinDLL('kernel32', use_last_error=True)
            kernel.OpenProcess.restype = w.HANDLE
            handle = kernel.OpenProcess(0x00100000, False, int(pid_file.read_text()))
            if handle:
                try:
                    self.assertEqual(kernel.WaitForSingleObject(w.HANDLE(handle), 5000), 0)
                finally:
                    kernel.CloseHandle(w.HANDLE(handle))
            else:
                self.assertEqual(ctypes.get_last_error(), 87)  # ERROR_INVALID_PARAMETER: PID is gone
