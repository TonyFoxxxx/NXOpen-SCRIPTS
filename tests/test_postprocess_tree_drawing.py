"""Render the production tree with real Windows Forms, rather than layout doubles."""
from pathlib import Path
import os
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]


@unittest.skipUnless(os.name == "nt", "requires a real Windows TreeView")
class NativeTreeDrawingTests(unittest.TestCase):
    def test_pixels_and_native_interaction(self):
        source = (ROOT / "scripts/NX_Postprocess_To_Machine.cs").read_text(encoding="ascii")
        renderer = source.split("internal static class TreeTextLayout\n", 1)[1]
        renderer = "internal static class TreeTextLayout\n" + renderer.split(
            "\ninternal static class ProgramSelection\n", 1
        )[0]
        framework = Path(os.environ["WINDIR"]) / "Microsoft.NET" / "Framework64" / "v4.0.30319"
        compiler = framework / "csc.exe"
        self.assertTrue(compiler.is_file(), "Windows .NET Framework compiler is required")
        fixture = (ROOT / "tests/fixtures/PostprocessTreeDrawing.cs").read_text(encoding="ascii")
        with tempfile.TemporaryDirectory(prefix="nx-tree-test-") as directory:
            path = Path(directory)
            code = path / "TreeDrawing.cs"
            code.write_text(fixture + "\n" + renderer, encoding="ascii")
            exe = path / "TreeDrawing.exe"
            result = subprocess.run(
                [str(compiler), "/nologo", "/target:exe", "/out:" + str(exe),
                 "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll", str(code)],
                capture_output=True, text=True, timeout=60,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            for theme in ("classic", "themed"):
                with self.subTest(theme=theme):
                    result = subprocess.run([str(exe), theme], capture_output=True, text=True, timeout=60)
                    print(result.stdout, flush=True)
                    self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
