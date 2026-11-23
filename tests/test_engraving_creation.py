"""Exercise the journal's selection, undo boundary, four CAM parents and preset outside NX."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
JOURNAL = ROOT / 'scripts/NX_PlanarMill_Select_Curves.cs'


def section(source, first, following):
    start = source.index(first)
    return source[start:source.index(following, start)]


class EngravingCreationTests(unittest.TestCase):
    def test_selection_undo_parents_and_preset(self):
        source = JOURNAL.read_text(encoding='utf-8-sig')
        workflow = section(source, '    public static void Main(', '    private static string AssignAxes(')
        selection = section(source, '    private static PlanarMilling SelectedOperation(', '    private static CurveInfo InspectCurve(')
        parents = section(source, 'internal sealed class EngravingParents', '// A standard modal Win32 dialog')
        preset = section(source, 'internal sealed class EngravingPreset', '    internal static void Apply(')
        preset += section(source, '    private void Feeds(', '    private void Moves(')
        preset += section(source, '    private void EngageOrRetract(', '    // Public UF operation parameters')
        preset += section(source, '    internal static void SetUfParameters(', '// BEGIN PORTABLE ENGRAVING ROUTING CORE')
        fixture = (ROOT / 'tests/fixtures/EngravingCreation.cs').read_text(encoding='utf-8')
        fixture = fixture.replace('// JOURNAL WORKFLOW', workflow + selection)
        fixture += '\n' + parents + preset.replace('sealed class EngravingPreset', 'sealed partial class EngravingPreset')
        with tempfile.TemporaryDirectory(prefix='nx-engraving-create-') as folder:
            tmp = Path(folder)
            (tmp / 'Checks.cs').write_text(fixture, encoding='utf-8')
            host = 'ManagedTestHost.cs'
            shutil.copyfile(ROOT / 'tests/fixtures' / host, tmp / host)
            hidden = getattr(subprocess, 'CREATE_NO_WINDOW', 0)
            csc = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
            if os.name == 'nt' and csc.exists():
                build = [str(csc), '/nologo', '/codepage:65001', '/main:Checks', '/out:Checks.exe', 'Checks.cs', host]
                run = [str(tmp / 'Checks.exe')]
            elif shutil.which('dotnet'):
                (tmp / 'Checks.csproj').write_text(
                    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework>'
                    '<OutputType>Exe</OutputType><StartupObject>Checks</StartupObject>'
                    '<LangVersion>5</LangVersion></PropertyGroup></Project>', encoding='ascii')
                build = ['dotnet', 'build', 'Checks.csproj', '-o', 'out', '--nologo']
                run = ['dotnet', str(tmp / 'out/Checks.dll')]
            else:
                self.skipTest('C# compiler not installed')
            for command in (build, run):
                result = subprocess.run(command, cwd=tmp, capture_output=True, encoding='utf-8', errors='replace',
                                        timeout=120, creationflags=hidden)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('engraving creation checks passed', result.stdout)

    def test_only_new_operations_receive_preset_and_saved_results_are_checked(self):
        source = JOURNAL.read_text(encoding='utf-8-sig')
        write = section(source, '    private static string AssignAxes(', '    private static Face[] SelectFaces(')
        self.assertIn('if (parents != null) EngravingPreset.SetUfParameters(uf, operation, unit, false)', write)
        self.assertRegex(write, r'if \(parents != null\)\s*\{\s*EngravingPreset.Apply\(builder, unit, parents.ReferenceTool, false\)')
        self.assertLess(write.index('builder.Commit()'), write.index('VerifyNewBoundaries('))
        self.assertLess(write.index('VerifyNewBoundaries('), write.index('EngravingPreset.SetUfParameters(uf, operation, unit, true)'))
        self.assertIn('finally { builder.Destroy(); }', write)
        verify = section(source, '    private static void VerifyNewBoundaries(', '    private static bool SameMembers(')
        self.assertIn('if (parents != null) EngravingPreset.Apply(reader, unit, parents.ReferenceTool, true)', verify)
        self.assertIn('finally { reader.Destroy(); }', verify)
        self.assertIn('GeometryRevision = "13"', source)
        for call in ('.GenerateToolPath(', '.Save(', '.SaveAs(', '.SetClearPlaneData(', '.SetClearPlaneTag(', '.CreateTool(', '.SetTagValue('):
            self.assertNotIn(call, source)


if __name__ == '__main__':
    unittest.main()
