"""Compile the production cycle reader and path traversal with .NET-shaped NX doubles."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]


class PostprocessCycleTests(unittest.TestCase):
    def test_production_cycle_depths_and_rotary_paths(self):
        source = (ROOT / "scripts/NX_Postprocess_To_Machine.cs").read_text(encoding="ascii")
        traversal = source.split("    private static double[] Xyz(Point3d p)", 1)[1].split(
            "    private void RemoveConflicts()", 1)[0]
        traversal = "internal sealed class OperationZmin\n{\n    private static double[] Xyz(Point3d p)" + traversal + "}\n"
        cycle_and_geometry = "internal static class ZminCycleDepth" + source.split(
            "internal static class ZminCycleDepth", 1)[1].split("internal static class ToolDescriptionUpdate", 1)[0]
        fixture = (ROOT / "tests/fixtures/PostprocessZminCycles.cs").read_text(encoding="ascii")
        code = fixture + "\n" + traversal + cycle_and_geometry
        with tempfile.TemporaryDirectory(prefix="nx-zmin-cycle-test-") as directory:
            path = Path(directory)
            (path / "Checks.cs").write_text(code, encoding="ascii")
            if os.name == "nt":
                compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
                exe = path / "Checks.exe"
                command = [str(compiler), "/nologo", "/out:" + str(exe), str(path / "Checks.cs")]
                run = [str(exe)]
            else:
                dotnet = shutil.which("dotnet")
                self.assertIsNotNone(dotnet, ".NET SDK required for production cycle tests")
                (path / "Checks.csproj").write_text(
                    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                    '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
                    '<LangVersion>7.3</LangVersion>'
                    '</PropertyGroup></Project>', encoding="ascii")
                command = [dotnet, "build", str(path / "Checks.csproj"), "--nologo", "-v:q"]
                run = [dotnet, str(path / "bin/Debug/net8.0/Checks.dll")]
            result = subprocess.run(command, capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            result = subprocess.run(run, capture_output=True, text=True, timeout=60)
            print(result.stdout, flush=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
