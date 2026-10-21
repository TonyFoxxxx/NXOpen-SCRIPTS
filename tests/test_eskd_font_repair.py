"""Check font metadata migration without redistributing the external font."""
import base64
import hashlib
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import tempfile
import unittest
import zlib

ROOT = Path(__file__).resolve().parents[1]


def source_and_recipe():
    source = (ROOT / "scripts/NX_ESKD_Format_GOST_A.cs").read_text(encoding="utf-8")
    declaration = re.search(r'private const string FontMetadataRepair\s*=\s*((?:"[A-Za-z0-9+/=]+"\s*\+?\s*)+);', source)
    if declaration is None:
        raise AssertionError("Missing metadata repair recipe")
    recipe = base64.b64decode("".join(re.findall(r'"([A-Za-z0-9+/=]+)"', declaration.group(1))), validate=True)
    return source, declaration.group(0), recipe


def metadata_changes(recipe, size):
    count = struct.unpack_from(">H", recipe)[0]
    cursor, previous_end = 2, 0
    changes = []
    for _ in range(count):
        offset, length = struct.unpack_from(">HH", recipe, cursor)
        cursor += 4
        if not (length > 0 and previous_end <= offset and offset + length <= size and cursor + length <= len(recipe)):
            raise AssertionError("Invalid metadata record")
        changes.append((offset, recipe[cursor:cursor + length]))
        cursor += length
        previous_end = offset + length
    if cursor != len(recipe):
        raise AssertionError("Trailing recipe bytes")
    return changes


def production_method(source, name):
    masked = re.sub(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*[\s\S]*?\*/',
                    lambda match: " " * len(match.group()), source)
    match = re.search(r"^        private static [^\n]*\b" + name + r"\(", source, re.MULTILINE)
    if match is None:
        raise AssertionError("Missing production method " + name)
    start = masked.index("{", match.start())
    depth, end = 1, start + 1
    while depth:
        depth += (masked[end] == "{") - (masked[end] == "}")
        end += 1
    return source[match.start():end]


class EskdFontRepairTests(unittest.TestCase):
    def test_recipe_is_bounded_metadata_and_public_source_has_no_font(self):
        source, _, recipe = source_and_recipe()
        self.assertLess(len(recipe), 4096)
        self.assertNotIn("EmbeddedFontChunks", source)
        changes = metadata_changes(recipe, 57764)
        self.assertGreater(len(changes), 0)
        self.assertLess(sum(len(data) for _, data in changes), 2048)
        for suffix in (".ttf", ".otf"):
            self.assertEqual(list((ROOT / "scripts").rglob("*" + suffix)), [])

    def test_production_migration_and_rejection(self):
        source, declaration, recipe = source_and_recipe()
        # Deterministic non-font bytes exercise the actual C# control flow.
        # Only the expected identities are replaced; no real font is in the fixture.
        original = bytes((i * 73 + 19) & 255 for i in range(57764))
        target = bytearray(original)
        for offset, data in metadata_changes(recipe, len(original)):
            target[offset:offset + len(data)] = data
        methods = "\n".join(production_method(source, name) for name in (
            "ReadFontUInt16", "Crc32", "FontDigestMatches", "TryNormalizeExternalFont", "ApplyFontMetadataRepair"))
        constants = "\n".join([
            'private const int FontByteCount = 57764;',
            'private const uint FontCrc32 = 0x%08Xu;' % zlib.crc32(target),
            'private const uint LegacyFontCrc32 = 0x%08Xu;' % zlib.crc32(original),
            'private const string FontSha256 = "%s";' % hashlib.sha256(target).hexdigest(),
            'private const string LegacyFontSha256 = "%s";' % hashlib.sha256(original).hexdigest(),
        ])
        fixture = r'''using System;
public class Checks
{
// CONSTANTS
// RECIPE
// METHODS
static void Require(bool condition, string message)
{ if (!condition) throw new Exception(message); }
static void BadRecipe(byte[] original, byte[] recipe)
{
    try { ApplyFontMetadataRepair(original, recipe); }
    catch (InvalidOperationException) { return; }
    throw new Exception("Malformed recipe was accepted");
}
public static void Main()
{
    byte[] original = new byte[FontByteCount];
    for (int i = 0; i < original.Length; i++) original[i] = (byte)((i * 73 + 19) & 255);
    byte[] snapshot = (byte[])original.Clone();
    byte[] fixedBytes = TryNormalizeExternalFont(original);
    Require(fixedBytes != null && !Object.ReferenceEquals(original, fixedBytes), "Legacy migration failed");
    Require(FontDigestMatches(fixedBytes, FontSha256), "Wrong target bytes");
    Require(Object.ReferenceEquals(fixedBytes, TryNormalizeExternalFont(fixedBytes)), "Corrected font was not reused");
    for (int i = 0; i < original.Length; i++) Require(original[i] == snapshot[i], "Original bytes changed");
    Require(TryNormalizeExternalFont(null) == null, "Null accepted");
    Require(TryNormalizeExternalFont(new byte[FontByteCount - 1]) == null, "Wrong length accepted");
    Require(TryNormalizeExternalFont(new byte[FontByteCount]) == null, "Unknown bytes accepted");
    original[FontByteCount - 1] ^= 1;
    Require(TryNormalizeExternalFont(original) == null, "Damaged source accepted");
    fixedBytes[FontByteCount - 1] ^= 1;
    Require(TryNormalizeExternalFont(fixedBytes) == null, "Damaged target accepted");
    Require(!FontDigestMatches(snapshot, new string('0', 64)), "Wrong SHA accepted");
    BadRecipe(snapshot, null);
    BadRecipe(snapshot, new byte[0]);
    BadRecipe(snapshot, new byte[] { 0, 1 });
    BadRecipe(snapshot, new byte[] { 0, 1, 0, 0, 0, 0 });
    BadRecipe(snapshot, new byte[] { 0, 1, 255, 255, 0, 2, 1, 2 });
    BadRecipe(snapshot, new byte[] { 0, 2, 0, 1, 0, 1, 170, 0, 1, 0, 1, 187 });
    BadRecipe(snapshot, new byte[] { 0, 0, 7 });
    BadRecipe(snapshot, new byte[] { 0, 1, 0, 0, 0, 1 });
    Console.WriteLine("Font metadata migration, reuse, source preservation and rejection checks passed.");
}
}'''
        fixture = fixture.replace("// CONSTANTS", constants).replace("// RECIPE", declaration).replace("// METHODS", methods)
        with tempfile.TemporaryDirectory(prefix="nx-font-repair-test-") as directory:
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
                    self.skipTest(".NET SDK required for C# font repair execution")
                project = path / "Checks.csproj"
                project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                                   '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                                   '</PropertyGroup></Project>', encoding="ascii")
                command = [dotnet, "build", str(project), "--nologo", "-v:q"]
                run = [dotnet, str(path / "bin/Debug/net8.0/Checks.dll")]
            compiled = subprocess.run(command, capture_output=True, text=True, timeout=120)
            self.assertEqual(compiled.returncode, 0, compiled.stdout + compiled.stderr)
            result = subprocess.run(run, capture_output=True, text=True, timeout=60)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
