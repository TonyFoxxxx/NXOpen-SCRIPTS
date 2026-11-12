"""Compile the actual UI without Forms/Drawing references and exercise its modal paths."""
from pathlib import Path
import os
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


@unittest.skipUnless(os.name == 'nt', 'Windows Forms requires Windows')
class GostInputDialogTests(unittest.TestCase):
    def test_reflection_dialog_values_accept_cancel_and_reopen(self):
        source = (ROOT / 'scripts/NX_GOST_Smooth_Text.cs').read_text(encoding='utf-8')
        dialog = source.split('// BEGIN GOST INPUT DIALOG\n', 1)[1].split('// END GOST INPUT DIALOG', 1)[0]
        preamble = 'using System;\nusing System.IO;\nusing System.Collections.Generic;\nusing System.Reflection;\nusing System.Runtime.InteropServices;\n'
        with tempfile.TemporaryDirectory(prefix='gost-input-') as folder:
            path = Path(folder)
            (path / 'Input.cs').write_text(preamble + dialog, encoding='utf-8')
            compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
            exe = path / 'Checks.exe'
            build = [str(compiler), '/nologo', '/codepage:65001', '/out:' + str(exe),
                     str(path / 'Input.cs'), str(ROOT / 'tests/fixtures/GostInputDialog.cs')]
            result = subprocess.run(build, capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            result = subprocess.run([str(exe)], capture_output=True, text=True, timeout=45)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('4 repeated dialogs passed', result.stdout)
