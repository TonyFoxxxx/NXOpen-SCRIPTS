"""Focused boundary adapter checks; these do not launch or simulate a real NX session."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'scripts/NX_Planar_Add_Curves.cs'
CSC = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'


class SeparatePlanarBoundaries(unittest.TestCase):
    @unittest.skipUnless(CSC.is_file() or (shutil.which('mcs') and shutil.which('mono')), 'C# compiler unavailable')
    def test_individual_boundaries_and_rejected_nx_results(self):
        source = SOURCE.read_text(encoding='utf-8')
        adapter = source.split('// BEGIN SEPARATE PLANAR BOUNDARY ADAPTER\n', 1)[1].split('// END SEPARATE PLANAR BOUNDARY ADAPTER', 1)[0]
        fixture = ROOT / 'tests/fixtures/SeparatePlanarBoundary.cs'
        with tempfile.TemporaryDirectory(prefix='nx-planar-check-') as folder:
            folder = Path(folder)
            core = folder / 'Adapter.cs'
            core.write_text('using System;\nusing System.Collections.Generic;\nusing System.Globalization;\nusing System.Text;\nusing NXOpen;\nusing NXOpen.CAM;\n' + adapter, encoding='utf-8')
            executable = folder / 'Checks.exe'
            if CSC.is_file():
                compile_command = [str(CSC), '/nologo', '/target:exe', '/out:' + str(executable), str(core), str(fixture)]
                run_command = [str(executable)]
            else:
                compile_command = ['mcs', '-out:' + str(executable), str(core), str(fixture)]
                run_command = ['mono', str(executable)]
            compiled = subprocess.run(compile_command, text=True, capture_output=True)
            self.assertEqual(compiled.returncode, 0, compiled.stdout + compiled.stderr)
            checked = subprocess.run(run_command, text=True, capture_output=True)
            self.assertEqual(checked.returncode, 0, checked.stdout + checked.stderr)
            self.assertIn('checks passed: 106', checked.stdout)


if __name__ == '__main__':
    unittest.main()
