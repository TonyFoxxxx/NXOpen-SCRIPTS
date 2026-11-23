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
        snapshot = source.split('// BEGIN ENGRAVING DIAGNOSTIC CAM SNAPSHOT\n', 1)[1].split('// END ENGRAVING DIAGNOSTIC CAM SNAPSHOT', 1)[0]
        core += '\ninternal sealed partial class GeometryFixture {\n' + snapshot + '\n}\n'
        core += '\npublic static class EngravingReportWindow' + source.split('public static class EngravingReportWindow', 1)[1]
        harness = (ROOT / 'tests/fixtures/EngravingDiagnostic.cs').read_text(encoding='utf-8')
        with tempfile.TemporaryDirectory(prefix='nx-engraving-diagnostic-test-') as folder:
            tmp = Path(folder)
            (tmp / 'Program.cs').write_text(harness + '\n' + core, encoding='utf-8')
            shutil.copyfile(ROOT / 'tests/fixtures/ManagedTestHost.cs', tmp / 'ManagedTestHost.cs')
            shutil.copyfile(ROOT / 'tests/fixtures/EngravingDiagnosticGeometry.cs', tmp / 'Geometry.cs')
            hidden = getattr(subprocess, 'CREATE_NO_WINDOW', 0)
            csc = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
            if os.name == 'nt' and csc.exists():
                build = [str(csc), '/nologo', '/utf8output', '/codepage:65001', '/optimize+', '/out:' + str(tmp / 'check.exe'),
                         str(tmp / 'Program.cs'), str(tmp / 'ManagedTestHost.cs'), str(tmp / 'Geometry.cs')]
                run = [str(tmp / 'check.exe')]
            elif shutil.which('dotnet'):
                (tmp / 'Check.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>')
                build = ['dotnet', 'build', str(tmp / 'Check.csproj'), '-c', 'Release', '-o', str(tmp / 'out'), '--nologo']
                run = ['dotnet', str(tmp / 'out/Check.dll')]
            else:
                self.skipTest('C# compiler not installed')
            result = subprocess.run(build, capture_output=True, encoding='utf-8', errors='replace', timeout=120, creationflags=hidden)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            result = subprocess.run(run + [str(tmp)], capture_output=True, encoding='utf-8', errors='replace', timeout=60, creationflags=hidden)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('diagnostic checks passed', result.stderr)
            data = json.loads(result.stdout.lstrip('\ufeff'))
            self.assertEqual(data['schema_version'], 1)
            self.assertTrue(data['partial'])
            self.assertEqual(data['unavailable_count'], 1)
            settings = {row['path']: row['value'] for row in data['records'] if row['kind'] == 'setting'}
            self.assertIs(settings['Operation.Enabled'], False)
            self.assertIsNone(settings['Operation.Empty'])
            self.assertEqual(settings['Operation.Selected.Value'], '')
            self.assertEqual(settings['Operation.SelectedList.Items[0]']['id'], 0)
            self.assertEqual(settings['Operation.SelectedList.Items[1]']['id'], 0xf0000001)
            self.assertNotIn('Operation.Failed', settings)
            self.assertEqual(settings['Operation.Escaped'], 'К"\\\r\n\t\0😀')
            self.assertEqual(settings['Operation.Mode']['value'], 7)
            self.assertEqual(settings['Operation.Feed'], 12.3456789012345)
            sample = next(row for row in data['records'] if row['kind'] == 'sample')
            self.assertEqual(sample['large_tag'], 0xf0000001)
            self.assertEqual(sample['number'], 0.00123456789012345)
            self.assertEqual(sample['adjacent_to_one'].hex(), '0x1.0000000000001p+0')
            self.assertEqual(sample['nan'], {'nonfinite': 'NaN'})
            self.assertEqual(sample['infinity'], {'nonfinite': 'Infinity'})
            self.assertEqual(next(row['value'] for row in data['records'] if row['kind'] == 'parameter'), 'Ж' * 120000)
            self.assertIn('unavailable setting', data['report'])
            self.check_geometry(data, settings)
            self.check_partial_geometry(json.loads((tmp / 'geometry-faults.json').read_text(encoding='utf-8')))
            exports = tmp / 'exports'
            self.assertEqual(sorted(p.name for p in exports.iterdir()), ['template.prt', 'Гравировка.JSON'])
            saved = (exports / 'Гравировка.JSON').read_bytes()
            self.assertFalse(saved.startswith(b'\xef\xbb\xbf'))
            self.assertEqual(json.loads(saved.decode('utf-8')), data)
            self.assertEqual((exports / 'template.prt').read_bytes(), b'keep')

    def check_geometry(self, data, settings):
        records = data['records']
        params = [r for r in records if r['kind'] == 'parameter' and 'owner_tag' in r]
        self.assertEqual([r['index'] for r in params], [103, 104, 105, 106])
        self.assertEqual([(r['status'], r['value'], r['units']) for r in params],
                         [('Default', 0.0, 'Inches'), ('Inherited', False, None),
                          ('Overridden', 'гравировка', None), ('Default', '', None)])
        self.assertIs(params[1]['value'], False)
        self.assertEqual(params[1]['source_tag'], 7)
        point = settings['Operation.CustomPoints.PredrillEngageType[0]']
        self.assertEqual(point['point_acs'], [0, 1.2345678901234567, -2.5])
        self.assertEqual(point['depth'], 0)
        self.assertEqual(point['point_tag']['id'], 0)
        self.assertIn({'kind': 'list', 'path': 'Operation.CustomPoints.CutRegionStartType', 'count': 0}, records)
        self.assertEqual(settings['Operation.ClearancePlane.Origin[1]'], -8.75)
        self.assertEqual(settings['Operation.ClearancePlane.Normal[2]'], 1)
        self.assertEqual(settings['Operation.LowerLimitPlane.Status']['name'], 'ParamLwplaneStatusUndefined')
        self.assertFalse(any('LowerLimitPlane.Origin' in key or 'LowerLimitPlane.Normal' in key for key in settings))
        self.assertIs(settings['Operation.Boundaries.CamPart.Inherited'], True)
        self.assertIs(settings['Operation.Boundaries.CamBlank.Inherited'], False)
        counts = {(r['owner_tag'], r['geometry_type']): r['count'] for r in records if r['kind'] == 'boundaries'}
        self.assertEqual(counts[(0xf0000001, 'CamPart')], 0)
        self.assertEqual(counts[(7, 'CamPart')], 1)
        self.assertNotIn((7, 'CamCheck'), counts)
        self.assertEqual([(r['order'], r['name'], r['parameter_count']) for r in records if r['kind'] == 'ude'],
                         [(0, 'same name', 0), (1, 'same name', 2)])
        self.assertTrue(any(r['kind'] == 'ude_set' and r['set'] == 'UdeEndSet' and r['count'] == 0 for r in records))
        fields = {r['parameter']: r for r in records if r['kind'] == 'ude_parameter'}
        self.assertIs(fields['enabled']['value'], False)
        self.assertEqual(fields['enabled']['state'], 'ParamInactive')
        self.assertEqual(fields['label']['value'], '')

    def check_partial_geometry(self, data):
        self.assertTrue(data['partial'])
        errors = {r['path']: r for r in data['records'] if r['kind'] == 'error'}
        self.assertIn('array count', errors['Operation.CustomPoints.PredrillEngageType']['message'])
        self.assertEqual(errors['Operation.ClearancePlane.Data']['error_code'], 12345)
        self.assertIn('provider unavailable', errors['Геометрия CamBlank']['message'])
        inherited = next(r for r in data['records'] if r.get('status') == 'Inherited')
        self.assertIs(inherited['value'], False)
        self.assertIs(inherited['value_available'], True)
        self.assertIs(inherited['source_available'], False)
        settings = {r['path']: r['value'] for r in data['records'] if r['kind'] == 'setting'}
        self.assertIn('Operation.ClearancePlane.Usage', settings)
        self.assertNotIn('Operation.ClearancePlane.Origin[0]', settings)
        self.assertEqual(settings['Operation.LowerLimitPlane.Origin[2]'], -0.125)
        self.assertEqual([r['tag'] for r in data['records'] if r['kind'] == 'boundary_member' and 'CamBlank' in r['path']], [110, 120])
        fields = {r['parameter']: r for r in data['records'] if r['kind'] == 'ude_parameter'}
        self.assertIs(fields['enabled']['value_available'], False)
        self.assertNotIn('value', fields['enabled'])
        self.assertEqual(fields['enabled']['state'], 'ParamInactive')
        self.assertEqual(fields['label']['value'], '')

    def test_journal_never_commits_or_writes_model(self):
        source = SOURCE.read_text(encoding='utf-8')
        for call in ('.Commit(', '.Save(', '.SaveAs(', '.GenerateToolPath(', '.SetUndoMark(', '.UndoToMark(', '.SetAttribute(', '.InitializeData('):
            self.assertNotIn(call, source)
        self.assertNotIn('System.Windows.Forms', source)
        self.assertNotIn('System.Drawing', source)
        # DC 2606 journal references do not resolve these forwarded collection types.
        self.assertNotIn('SortedSet<', source)
        self.assertNotIn('SortedDictionary<', source)
        self.assertEqual(source.count('EngravingJson.WriteFile('), 1)
        self.assertIn('if (path != null)', source)
        self.assertIn('dialog.Flags = 0x0008080au', source)


if __name__ == '__main__':
    unittest.main()
