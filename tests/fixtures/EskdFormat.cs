using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Tag = System.Int32;

class TaggedObject { }
struct Point3d
{
    public double X, Y, Z;
    public Point3d(double x, double y, double z) { X = x; Y = y; Z = z; }
}
namespace NXOpen
{
    class Line : TaggedObject
    {
        public Point3d StartPoint, EndPoint;
        public Line(double x1, double y1, double x2, double y2)
        { StartPoint = new Point3d(x1, y1, 0); EndPoint = new Point3d(x2, y2, 0); }
    }
}
class NXException : Exception { }
static class UFConstants { public const int UF_OBJ_ALIVE = 1; }
class FakeSheet { public double Length, Height; }
class FakeObjects
{
    public readonly HashSet<int> Alive = new HashSet<int>();
    public readonly List<int> Deleted = new List<int>();
    public readonly Dictionary<int, int> Child = new Dictionary<int, int>();
    public bool FailDelete;
    public int AskStatus(int tag) { return Alive.Contains(tag) ? 1 : 0; }
    public void DeleteObject(int tag)
    {
        if (FailDelete) throw new NXException();
        if (!Alive.Remove(tag)) throw new Exception("Deleted a stale child tag");
        Deleted.Add(tag);
        int child;
        if (Child.TryGetValue(tag, out child)) Alive.Remove(child);
    }
}
class FakeUf { public readonly FakeObjects Obj = new FakeObjects(); }
class Checks
{
    static FakeSheet Sheet = new FakeSheet();
    static FakeUf U = new FakeUf();
    static readonly List<string> Warnings = new List<string>();
    static readonly HashSet<int> CreatedFormatObjects = new HashSet<int>();
    static int TableTextProbe, TableTextMark;
    static double CharacterSpacingFactor = 1.0;
    static double LineFactorForHeight(double height) { return 1.0; }
    // Deterministic metrics exercise the actual wrapping algorithm. These
    // metrics deliberately do not claim to emulate NX font rendering.
    static double TableTextWidth(string text, double height, double factor)
    { return StringInfo.ParseCombiningCharacters(text).Length * height * 0.65; }
    static double[] MeasureCalibrationText(int tag, string[] lines, double height,
        double character, double factor, int mark)
    {
        double width = 0;
        foreach (string line in lines) width = Math.Max(width, TableTextWidth(line, height, factor));
        return new double[] { width, height + (lines.Length - 1) * TableLinePitch(height) };
    }

    // PRODUCTION_METHODS

    static void Assert(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    static string Compact(string text)
    { return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", ""); }
    static void Material(string input, int minimumLines)
    {
        double height;
        string text = FitTableText(MaterialCellText(input), 3.5, 68, 13, out height);
        string[] lines = text.Split('\n');
        Assert(lines.Length >= minimumLines, "Material must wrap before shrinking");
        Assert(Compact(text) == Compact(NormalizeTitleText(input)), "Material characters changed");
        foreach (string line in lines)
            Assert(TableTextWidth(line, height, 1) <= 68.01, "Unsplit overlong material token");
    }
    static void Main()
    {
        Assert(MaterialCellText("Сталь 40Х ГОСТ 4543-2016") == "Сталь 40Х\nГОСТ 4543-2016", "Grade/standard break");
        Assert(MaterialCellText("Сталь 40Х\r\nГОСТ 4543-2016") == "Сталь 40Х\nГОСТ 4543-2016", "Existing newline");
        Assert(MaterialCellText("Сталь 40Х\u00a0ГОСТ 4543-2016").Contains("\nГОСТ"), "NBSP separator");
        Assert(MaterialCellText("Сталь 40Х") == "Сталь 40Х", "Short material changed");
        Material("Сталь 40Х ГОСТ 4543-2016", 2);
        Material("Сталь 40Х ГОСТ 4543-2016" + new string('0', 45), 3);
        Material(new string('0', 100), 3);
        Material("Сталь 40Х ГОСТ 4543-2016" + new string('0', 300), 4);
        Assert(Warnings.Count > 0, "Extreme text must report reduced readability");
        string unicode = "и\u0306" + Char.ConvertFromUtf32(0x1D7CE) + "е\u0308";
        string[] wrapped = WrapTableText(unicode, 3, 3.5, 1);
        Assert(String.Join("", wrapped) == unicode, "Unicode content changed");
        foreach (string line in wrapped)
            Assert(StringInfo.ParseCombiningCharacters(line).Length == 1, "Split Unicode grapheme");
        foreach (double[] size in new double[][] { new double[] {210,297}, new double[] {297,210},
            new double[] {420,297}, new double[] {297,420}, new double[] {594,420} })
        {
            Sheet.Length = size[0]; Sheet.Height = size[1];
            Assert(IsBorderLine(new NXOpen.Line(20, 5, 20, size[1] - 5)), "Left border missed");
            Assert(IsBorderLine(new NXOpen.Line(20, 5, size[0] - 5, 5)), "Bottom border missed");
            Assert(!IsBorderLine(new NXOpen.Line(100, 100, 120, 100)), "Detail line selected");
            Assert(!IsBorderLine(new NXOpen.Line(20, 5, 80, 100)), "Diagonal selected");
            Assert(InFormatArea(new Point3d(size[0] - 185, 40, 0)), "Title block missed");
            Assert(InAuxiliaryArea(new Point3d(30, size[1] - 10, 0)), "Upper label missed");
            Assert(!InFormatArea(new Point3d(size[0] / 2, size[1] / 2, 0)), "Sheet centre selected");
        }
        U.Obj.Alive.UnionWith(new int[] {1, 2, 3}); U.Obj.Child[1] = 2;
        DeleteFormatObjects(new int[] {1, 2, 3});
        Assert(U.Obj.Alive.Count == 0 && U.Obj.Deleted.Count == 2, "Cascading deletion not respected");
        U.Obj.Alive.Add(4); U.Obj.FailDelete = true;
        bool failed = false;
        try { DeleteFormatObjects(new int[] {4}); } catch (InvalidOperationException) { failed = true; }
        Assert(failed && U.Obj.Alive.Contains(4), "Deletion failure must abort formatting");
        CreatedFormatObjects.Add(5); failed = false;
        try { RequireTemplateCleanupPhase(); } catch (InvalidOperationException) { failed = true; }
        Assert(failed, "Cleanup permitted after creating new format");
        Console.WriteLine("ESKD helper checks passed");
    }
}
