import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'scripts/NX_Engraving_Operation_Diagnostic.cs'


class EngravingDiagnosticTests(unittest.TestCase):
    def test_readers_readonly_walk_cleanup_and_json_roundtrip(self):
        source = SOURCE.read_text(encoding='utf-8')
        names = ('ENGRAVING DIAGNOSTIC PARAM READER', 'ENGRAVING DIAGNOSTIC TREE', 'ENGRAVING DIAGNOSTIC JSON')
        core = '\n'.join(source.split('// BEGIN ' + name + '\n', 1)[1].split('// END ' + name, 1)[0] for name in names)
        core += '\npublic static class EngravingReportWindow' + source.split('public static class EngravingReportWindow', 1)[1]
        harness = (ROOT / 'tests/fixtures/EngravingDiagnostic.cs').read_text(encoding='utf-8')
        with tempfile.TemporaryDirectory(prefix='nx-engraving-diagnostic-test-') as folder:
            tmp = Path(folder)
            (tmp / 'Program.cs').write_text(harness + '\n' + core, encoding='utf-8')
            shutil.copyfile(ROOT / 'tests/fixtures/ManagedTestHost.cs', tmp / 'ManagedTestHost.cs')
            hidden = getattr(subprocess, 'CREATE_NO_WINDOW', 0)
            csc = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
            if os.name == 'nt' and csc.exists():
                build = [str(csc), '/nologo', '/utf8output', '/codepage:65001', '/optimize+', '/out:' + str(tmp / 'check.exe'),
                         str(tmp / 'Program.cs'), str(tmp / 'ManagedTestHost.cs')]
                run = [str(tmp / 'check.exe')]
            elif shutil.which('dotnet'):
                (tmp / 'Check.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>')
                build = ['dotnet', 'build', str(tmp / 'Check.csproj'), '-c', 'Release', '-o', str(tmp / 'out'), '--nologo']
                run = ['dotnet', str(tmp / 'out/Check.dll')]
            else:
                self.skipTest('C# compiler not installed')
            result = subprocess.run(build, capture_output=True, encoding='utf-8', errors='replace', timeout=120, creationflags=hidden)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            result = subprocess.run(run, capture_output=True, encoding='utf-8', errors='replace', timeout=60, creationflags=hidden)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('diagnostic checks passed', result.stderr)
            data = json.loads(result.stdout.lstrip('\ufeff'))
            self.assertEqual(data['schema_version'], 1)
            self.assertTrue(data['partial'])
            self.assertEqual(data['unavailable_count'], 1)
            settings = {row['path']: row['value'] for row in data['records'] if row['kind'] == 'setting'}
            self.assertIs(settings['Operation.Enabled'], False)
            self.assertIsNone(settings['Operation.Empty'])
            self.assertNotIn('Operation.Failed', settings)
            self.assertEqual(settings['Operation.Escaped'], 'К"\\\r\n\t\0😀')
            self.assertEqual(settings['Operation.Mode']['value'], 7)
            self.assertEqual(settings['Operation.Feed'], 12.3456789012345)
            sample = next(row for row in data['records'] if row['kind'] == 'sample')
            self.assertEqual(sample['large_tag'], 0xf0000001)
            self.assertEqual(sample['number'], 0.00123456789012345)
            self.assertEqual(sample['nan'], {'nonfinite': 'NaN'})
            self.assertEqual(sample['infinity'], {'nonfinite': 'Infinity'})
            self.assertEqual(next(row['value'] for row in data['records'] if row['kind'] == 'parameter'), 'Ж' * 120000)
            self.assertIn('unavailable setting', data['report'])

    def test_journal_never_commits_or_writes_model(self):
        source = SOURCE.read_text(encoding='utf-8')
        for call in ('.Commit(', '.Save(', '.SaveAs(', '.GenerateToolPath(', '.SetUndoMark(', '.UndoToMark(', '.SetAttribute(', '.InitializeData('):
            self.assertNotIn(call, source)
        self.assertNotIn('System.Windows.Forms', source)
        self.assertNotIn('System.Drawing', source)
        self.assertEqual(source.count('File.WriteAllText('), 1)
        self.assertIn('if (path != null)', source)
        self.assertIn('dialog.Flags = 0x0008080au', source)


if __name__ == '__main__':
    unittest.main()
