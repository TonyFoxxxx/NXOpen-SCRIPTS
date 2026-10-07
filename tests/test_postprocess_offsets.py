"""Compile the production NC transformer; exercise it without an NX installation."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class WorkOffsetTests(unittest.TestCase):
    def test_nc_transformation_and_profiles(self):
        with tempfile.TemporaryDirectory(prefix="nx-offset-test-") as directory:
            path = Path(directory)
            source = ROOT / "scripts/NX_Postprocess_To_Machine.cs"
            fixture = ROOT / "tests/fixtures/WorkOffsets.cs"
            if os.name == "nt":
                compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
                exe = path / "WorkOffsets.exe"
                command = [str(compiler), "/nologo", "/define:NX_POST_CORE_ONLY", "/out:" + str(exe), str(source), str(fixture)]
                run = [str(exe), directory]
            else:
                dotnet = shutil.which("dotnet")
                self.assertIsNotNone(dotnet, ".NET SDK required for production NC tests")
                shutil.copyfile(source, path / "Core.cs")
                shutil.copyfile(fixture, path / "WorkOffsets.cs")
                (path / "Checks.csproj").write_text(
                    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                    '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
                    '<DefineConstants>NX_POST_CORE_ONLY</DefineConstants><LangVersion>7.3</LangVersion>'
                    '<EnableDefaultCompileItems>true</EnableDefaultCompileItems>'
                    '</PropertyGroup></Project>', encoding="ascii")
                command = [dotnet, "build", str(path / "Checks.csproj"), "--nologo", "-v:q"]
                run = [dotnet, str(path / "bin/Debug/net8.0/Checks.dll"), directory]
            result = subprocess.run(command, capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            result = subprocess.run(run, capture_output=True, text=True, timeout=60)
            print(result.stdout, flush=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
