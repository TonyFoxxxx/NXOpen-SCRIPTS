"""Compile the complete journal; exercise real XLSX writes against a small NX shim."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class NumberProgramFoldersTests(unittest.TestCase):
    def test_deletion_reserve_collisions_and_rollback(self):
        with tempfile.TemporaryDirectory(prefix='nx-numbering-test-') as directory:
            path = Path(directory)
            shutil.copyfile(ROOT / 'scripts/NX_Number_Program_Folders.cs', path / 'Journal.cs')
            shutil.copyfile(ROOT / 'tests/fixtures/NumberProgramFolders.cs', path / 'Checks.cs')
            if os.name == 'nt':
                compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
                exe = path / 'Checks.exe'
                command = [str(compiler), '/nologo', '/main:Checks', '/out:' + str(exe),
                           str(path / 'Checks.cs'), str(path / 'Journal.cs')]
                run = [str(exe)]
            else:
                dotnet = shutil.which('dotnet')
                if not dotnet:
                    self.skipTest('.NET SDK required to execute the numbering journal')
                project = path / 'Checks.csproj'
                project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                    '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
                    '<StartupObject>Checks</StartupObject><LangVersion>7.3</LangVersion>'
                    '</PropertyGroup></Project>', encoding='ascii')
                command = [dotnet, 'build', str(project), '--nologo', '-v:q']
                run = [dotnet, str(path / 'bin/Debug/net8.0/Checks.dll')]
            result = subprocess.run(command, capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            run += [str(ROOT / 'templates/NX_Numbering_Register_v1.0.xlsx'), str(path / 'cases')]
            result = subprocess.run(run, capture_output=True, text=True, timeout=60)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('12 numbering scenarios passed', result.stdout)


if __name__ == '__main__':
    unittest.main()
