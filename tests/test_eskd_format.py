"""Exercise production ESKD layout helpers without an NX installation."""
from pathlib import Path
import os
import re
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class EskdFormatTests(unittest.TestCase):
    def test_cleanup_precedes_new_format(self):
        source = (ROOT / "scripts/NX_ESKD_Format_GOST_A.cs").read_text(encoding="utf-8")
        main = source[source.index("    public static void Main("):source.index("    private static bool OnSheet(")]
        calls = list(re.finditer(r"DeleteCurrentSheetFormat\(\);", main))
        self.assertEqual(len(calls), 3)
        self.assertTrue(all(call.start() < main.index("BuildFrame();") for call in calls))
        self.assertLess(main.index("DraftingSetupDialog.Show("), calls[0].start())
        self.assertLess(main.index("Session.MarkVisibility.Visible"), calls[0].start())

    def test_material_wrapping_and_ordered_deletion(self):
        source = (ROOT / "scripts/NX_ESKD_Format_GOST_A.cs").read_text(encoding="utf-8")
        masked = re.sub(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*[\s\S]*?\*/',
                        lambda match: " " * len(match.group()), source)
        methods = []
        for name in ("NormalizeTitleText", "MaterialCellText", "WrapTableText", "TableWordBreak",
                     "FitTableText", "TableLinePitch", "InTitleArea", "InAuxiliaryArea",
                     "InFormatArea", "IsBorderLine", "IsLiveTag", "DeleteFormatObjects",
                     "RequireTemplateCleanupPhase"):
            match = re.search(r"^    private static [^\n]*\b" + name + r"\(", source, re.MULTILINE)
            self.assertIsNotNone(match, name)
            start = masked.index("{", match.start())
            depth = 1
            end = start + 1
            while depth:
                depth += (masked[end] == "{") - (masked[end] == "}")
                end += 1
            # The test shim represents UF tags as integers; NX's null tag is 0.
            methods.append(source[match.start():end].replace("Tag.Null", "0"))
        fixture = (ROOT / "tests/fixtures/EskdFormat.cs").read_text(encoding="utf-8")
        fixture = fixture.replace("// PRODUCTION_METHODS", "\n\n".join(methods))
        with tempfile.TemporaryDirectory(prefix="nx-eskd-test-") as directory:
            path = Path(directory)
            code = path / "Checks.cs"
            code.write_text(fixture, encoding="utf-8")
            if os.name == "nt":
                compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
                exe = path / "Checks.exe"
                command = [str(compiler), "/nologo", "/out:" + str(exe), str(code)]
                run = [str(exe)]
            else:
                dotnet = shutil.which("dotnet")
                if not dotnet:
                    self.skipTest(".NET SDK required for ESKD helper execution")
                project = path / "Checks.csproj"
                project.write_text(
                    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                    '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
                    '<LangVersion>7.3</LangVersion></PropertyGroup></Project>', encoding="ascii")
                command = [dotnet, "build", str(project), "--nologo", "-v:q"]
                run = [dotnet, str(path / "bin/Debug/net8.0/Checks.dll")]
            result = subprocess.run(command, capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            result = subprocess.run(run, capture_output=True, text=True, timeout=60)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
