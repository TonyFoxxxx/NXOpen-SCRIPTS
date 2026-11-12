"""Execute the production chain and plane algorithms without an NX installation."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class PlanarMillCurveTests(unittest.TestCase):
    def test_chain_topology_gaps_branching_and_plane(self):
        source = (ROOT / 'scripts/NX_PlanarMill_Select_Curves.cs').read_text(encoding='utf-8-sig')
        core = source.split('// BEGIN PORTABLE CURVE CHAIN CORE\n', 1)[1].split('// END PORTABLE CURVE CHAIN CORE', 1)[0]
        with tempfile.TemporaryDirectory(prefix='nx-planar-curve-check-') as folder:
            path = Path(folder)
            (path / 'Core.cs').write_text('using System;\nusing System.Collections.Generic;\n' + core, encoding='utf-8')
            shutil.copyfile(ROOT / 'tests/fixtures/PlanarCurveChains.cs', path / 'Checks.cs')
            if os.name == 'nt':
                compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
                exe = path / 'Checks.exe'
                command = [str(compiler), '/nologo', '/codepage:65001', '/out:' + str(exe), str(path / 'Core.cs'), str(path / 'Checks.cs')]
                run = [str(exe)]
            else:
                dotnet = shutil.which('dotnet')
                if not dotnet:
                    self.skipTest('.NET SDK required for curve chain scenarios')
                project = path / 'Checks.csproj'
                project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                                   '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
                                   '<LangVersion>7.3</LangVersion></PropertyGroup></Project>', encoding='ascii')
                command = [dotnet, 'build', str(project), '--nologo', '-v:q']
                run = [dotnet, str(path / 'bin/Debug/net8.0/Checks.dll')]
            result = subprocess.run(command, capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            result = subprocess.run(run, capture_output=True, text=True, timeout=60)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('21 planar-curve scenarios passed', result.stdout)


if __name__ == '__main__':
    unittest.main()
