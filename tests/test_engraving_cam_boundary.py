"""Exercise the production CAM adapter against a UF API contract double."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class EngravingBoundaryTests(unittest.TestCase):
    def test_open_profile_tolerances_stock_and_tool_position(self):
        source = (ROOT / 'scripts/NX_PlanarMill_Select_Curves.cs').read_text(encoding='utf-8-sig')
        core = source.split('// BEGIN ENGRAVING CAM BOUNDARY ADAPTER\n', 1)[1].split('// END ENGRAVING CAM BOUNDARY ADAPTER', 1)[0]
        with tempfile.TemporaryDirectory(prefix='nx-engraving-centerline-') as folder:
            path = Path(folder)
            (path / 'Core.cs').write_text('using System;\nusing NXOpen;\nusing NXOpen.UF;\n' + core, encoding='utf-8')
            shutil.copyfile(ROOT / 'tests/fixtures/EngravingCamBoundary.cs', path / 'Checks.cs')
            if os.name == 'nt':
                compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
                exe = path / 'Checks.exe'
                command = [str(compiler), '/nologo', '/codepage:65001', '/out:' + str(exe), str(path / 'Core.cs'), str(path / 'Checks.cs')]
                run = [str(exe)]
            else:
                dotnet = shutil.which('dotnet')
                if not dotnet:
                    self.skipTest('.NET SDK required for centerline scenarios')
                project = path / 'Checks.csproj'
                project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                                   '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
                                   '<LangVersion>7.3</LangVersion></PropertyGroup></Project>', encoding='ascii')
                command = [dotnet, 'build', str(project), '--nologo', '-v:q']
                run = [dotnet, str(path / 'bin/Debug/net8.0/Checks.dll')]
            result = subprocess.run(command, capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            result = subprocess.run(run, capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('engraving-boundary contract checks passed', result.stdout)


if __name__ == '__main__':
    unittest.main()
