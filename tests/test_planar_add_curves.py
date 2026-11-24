"""Exercise the journal entry point with a fixture that separates saved CAM records from editor rows."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'scripts/NX_Planar_Add_Curves.cs'
CSC = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
HOST = ROOT / 'tests/fixtures/ManagedTestHost.cs'


def invoke(command, *, timeout=25, env=None):
    return subprocess.run(command, capture_output=True, encoding='utf-8', errors='replace', timeout=timeout,
                          creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0), env=env)


class SeparatePlanarBoundaries(unittest.TestCase):
    @unittest.skipUnless(shutil.which('pwsh'), 'PowerShell Core with Roslyn unavailable')
    def test_full_journal_compiles_with_restricted_runtime_references(self):
        fixtures = ROOT / 'tests/fixtures'
        result = invoke([shutil.which('pwsh'), '-NoProfile', '-NonInteractive', '-File',
                         str(fixtures / 'CompilePlanarJournal.ps1'), str(SOURCE), str(fixtures / 'SeparatePlanarBoundary.cs')])
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    @unittest.skipUnless(CSC.is_file(), 'Windows .NET Framework compiler unavailable')
    def test_native_side_checkboxes_with_base_runtime_only(self):
        self.check_native_dialog('sides')

    @unittest.skipUnless(CSC.is_file(), 'Windows .NET Framework compiler unavailable')
    def test_native_live_appearance(self):
        self.check_native_dialog('appearance')

    def check_native_dialog(self, scenario):
        source = SOURCE.read_text(encoding='utf-8')
        imports = [line for line in source.splitlines() if line.startswith('using ') and 'NXOpen' not in line]
        windows = source.split('// BEGIN PLANAR CURVE WINDOWS\n', 1)[1].split('// END PLANAR CURVE WINDOWS', 1)[0]
        with tempfile.TemporaryDirectory(prefix='nx-planar-ui-') as folder:
            unit = Path(folder) / 'Dialogs.cs'
            unit.write_text('\n'.join(imports) + '\n' + windows, encoding='utf-8-sig')
            executable = unit.with_suffix('.exe')
            fixture = ROOT / 'tests/fixtures/PlanarSideSelection.cs'
            command = [str(CSC), '/nologo', '/noconfig', '/nostdlib+', '/target:exe',
                       '/reference:' + str(CSC.parent / 'mscorlib.dll'), '/reference:' + str(CSC.parent / 'System.dll'),
                       '/out:' + str(executable), str(unit), str(fixture), str(HOST)]
            result = invoke(command)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            checked = invoke([str(executable), scenario], timeout=45)
            if checked.returncode == 77:
                self.skipTest('A private Windows desktop is unavailable')
            self.assertEqual(checked.returncode, 0, checked.stdout + checked.stderr)

    @unittest.skipUnless(CSC.is_file() or (shutil.which('mcs') and shutil.which('mono')), 'C# compiler unavailable')
    def test_journal_boundary_transactions(self):
        source = SOURCE.read_text(encoding='utf-8')
        journal = source.split('public static class PlanarCurveWindows', 1)[0]
        fixture = ROOT / 'tests/fixtures/SeparatePlanarBoundary.cs'
        with tempfile.TemporaryDirectory(prefix='nx-planar-check-') as folder:
            folder = Path(folder)
            core = folder / 'Journal.cs'
            core.write_text(journal, encoding='utf-8-sig')
            executable = folder / 'Checks.exe'
            if CSC.is_file():
                compile_command = [str(CSC), '/nologo', '/target:exe', '/main:PlanarChecks', '/out:' + str(executable), str(core), str(fixture), str(HOST)]
                run_command = [str(executable)]
            else:
                compile_command = ['mcs', '-main:PlanarChecks', '-out:' + str(executable), str(core), str(fixture), str(HOST)]
                run_command = ['mono', str(executable)]
            compiled = invoke(compile_command)
            self.assertEqual(compiled.returncode, 0, compiled.stdout + compiled.stderr)
            checked = invoke(run_command)
            self.assertEqual(checked.returncode, 0, checked.stdout + checked.stderr)
            self.assertIn('Planar transaction checks passed', checked.stdout)

    @unittest.skipUnless(CSC.is_file(), 'Windows .NET Framework compiler unavailable')
    def test_managed_failure_returns_once_and_logs_its_stack(self):
        with tempfile.TemporaryDirectory(prefix='nx-planar-log-') as directory:
            folder = Path(directory)
            src = folder / 'Probe.cs'
            src.write_text('using System; class Probe { static int Main() { return ManagedTestHost.Run(Fail); } '
                           'static int Fail() { throw new InvalidOperationException("controlled failure"); } }', encoding='utf-8')
            exe = folder / 'Probe.exe'
            build = invoke([str(CSC), '/nologo', '/out:' + str(exe), str(src), str(HOST)])
            self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
            probe = invoke([str(exe)], env=dict(os.environ, NXOPEN_TEST_LOG_DIR=str(folder)))
            self.assertEqual(probe.returncode, 1, probe.stderr)
            log, = folder.glob('managed-*.log')
            self.assertIn('System.InvalidOperationException: controlled failure', log.read_text(encoding='utf-8'))
            self.assertIn('Probe.Fail()', log.read_text(encoding='utf-8'))
            self.assertIn(str(log), probe.stderr)


if __name__ == '__main__':
    unittest.main()
