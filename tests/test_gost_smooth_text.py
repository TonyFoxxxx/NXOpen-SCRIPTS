"""Exercise the actual font reader and curve reconstruction without NX or a font installation."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class GostSmoothTests(unittest.TestCase):
    def test_font_parser_geometry_tangency_and_retraces(self):
        source = (ROOT / 'scripts/NX_GOST_Smooth_Text.cs').read_text(encoding='utf-8')
        core = source.split('// BEGIN PORTABLE GOST GEOMETRY\n', 1)[1].split('// END PORTABLE GOST GEOMETRY', 1)[0]
        with tempfile.TemporaryDirectory(prefix='gost-smooth-') as folder:
            path = Path(folder)
            (path / 'Core.cs').write_text('using System;\nusing System.IO;\nusing System.Text;\nusing System.Collections.Generic;\n' + core, encoding='utf-8')
            shutil.copyfile(ROOT / 'tests/fixtures/GostSmoothGeometry.cs', path / 'Checks.cs')
            if os.name == 'nt':
                compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
                exe = path / 'Checks.exe'
                build = [str(compiler), '/nologo', '/codepage:65001', '/out:' + str(exe), str(path / 'Core.cs'), str(path / 'Checks.cs')]
                run = [str(exe)]
            else:
                dotnet = shutil.which('dotnet')
                if not dotnet:
                    self.skipTest('.NET SDK required')
                # Legacy Cyrillic encodings require registration on .NET Core.
                original = (path / 'Checks.cs').read_text()
                original = original.replace('G.FontData f=G.ReadFont', 'Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); G.FontData f=G.ReadFont')
                (path / 'Checks.cs').write_text(original)
                project = path / 'Checks.csproj'
                project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup></Project>')
                build = [dotnet, 'build', str(project), '--nologo', '-v:q']
                run = [dotnet, str(path / 'bin/Debug/net8.0/Checks.dll')]
            result = subprocess.run(build, capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            result = subprocess.run(run, capture_output=True, text=True, timeout=60)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('10 smooth-font scenarios passed', result.stdout)
