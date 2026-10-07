using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

// Synthetic, public ISO examples. No customer geometry or proprietary NC files.
internal static class WorkOffsetChecks
{
    private static int count;
    private const string Header = "%\nO1 (header G54)\nG17 G21 G94\nG91 G28 Z0\nT1 M06\n";
    private const string End = "M30\nN99 (footer)\n%\n";
    private static string Body(int tool)
    {
        return "(operation, G54 M06 M30 are comments)\nG40 G90 G00 G54 X1 Y2 S1000 M03\nG43 Z20 H" + tool +
            " M08\nG01 Z-1 F100\nG02 X2 Y3 I1 J1\nG00 Z20\nG00 X3 Y4\nG01 Z-2 F80\nG00 Z20\nM09\nM05\nG91 G28 Z0\n";
    }
    private static string Example(string body) { return Header + body + End; }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); count++; }
    private static byte[] Bytes(string text) { return Encoding.UTF8.GetBytes(text); }
    private static string Run(string nc, params int[] offsets)
    { return Run(nc, new WorkOffsetRules(), offsets); }
    private static string Run(string nc, WorkOffsetRules rules, params int[] offsets)
    { return Encoding.UTF8.GetString(WorkOffsetPrograms.Rewrite(Bytes(nc), "O1", offsets, rules)); }
    private static WorkOffsetException Bad(string nc, string message)
    { return Bad(nc, new WorkOffsetRules(), message); }
    private static WorkOffsetException Bad(string nc, WorkOffsetRules rules, string message)
    {
        try { Run(nc, rules, 1, 2); }
        catch (WorkOffsetException ex)
        {
            Check(ex.Message.Contains("Auto") || ex.Message.Contains("ISO_"), "profile in error");
            Check(ex.SourceLine == 0 || ex.Message.Contains("> " + ex.SourceLine + ": "), "source context in error");
            count++; return ex;
        }
        throw new Exception("Expected rejection: " + message);
    }
    private static string At(string body, int offset)
    { return body.Replace("G00 G54", "G00 G" + (53 + offset)); }

    private static void FormatsAndOrder()
    {
        byte[] ignored = Bytes("not NC");
        Check(Object.ReferenceEquals(ignored, WorkOffsetPrograms.Rewrite(ignored, "O1", null)), "disabled no-op");
        WorkOffsetPrograms.Apply("DOES_NOT_EXIST", "O1", null);
        string a = Body(1), b = Body(2), nc = Header + a + "T2 M06\n" + b + End;
        foreach (int[] offsets in new int[][] { new int[] { 1 }, new int[] { 2 }, new int[] { 1, 2, 3, 6 }, new int[] { 6, 3, 1, 2 }, new int[] { 6, 5, 4, 3, 2, 1 } })
        {
            string expected = Header;
            foreach (int offset in offsets) expected += At(a, offset);
            expected += "T2 M06\n";
            foreach (int offset in offsets) expected += At(b, offset);
            expected += End;
            Check(Run(nc, offsets) == expected, "exact tool-major order and original NC preservation");
        }
        foreach (string eol in new string[] { "\n", "\r\n", "\r" })
            Check(Run(nc.Replace("\n", eol), 1) == nc.Replace("\n", eol), "one offset preserves bytes/EOL");
        Check(Run("\uFEFF" + nc.Replace("(header G54)", "(\u041F\u0440\u0438\u043C\u0435\u0440 G54)"), 1).StartsWith("\uFEFF%"), "UTF8 BOM once");
        byte[] ansi = Encoding.GetEncoding(28591).GetBytes(nc.Replace("(header G54)", "(\u00C0\u00C1 G54)"));
        Check(Convert.ToBase64String(WorkOffsetPrograms.Rewrite(ansi, "O1", new int[] { 1 })) == Convert.ToBase64String(ansi), "ANSI bytes unchanged");
        for (int i = 0; i < 16; i++)
        {
            string variant = nc;
            if ((i & 1) != 0) variant = variant.ToLowerInvariant();
            if ((i & 2) != 0) variant = Regex.Replace(variant, @"(?i)\bG0([0-9])", "G$1");
            if ((i & 4) != 0) variant = Regex.Replace(variant, @"(?m)^(?=[gGTM])", "N001 ");
            if ((i & 8) != 0) variant = variant.Replace("M06", "M6").Replace(" ", "\t");
            string output = Run(variant, 1, 2, 3, 6);
            Check(Regex.Matches(output, @"(?i)G59\b").Count == 2, "semantic parsing, variant " + i);
            Check(Regex.Matches(output, @"(?im)^(?:N001[ \t]+)?T[12][ \t]+M0?6$").Count == 2, "tool changes retained, variant " + i);
        }
        Check(Run(nc.Replace("O1 (", "N1 O1 ("), 1, 2).Contains("N1 O1"), "numbered O header");
        Check(Run(nc.Replace("T1 M06", "T1\nM06"), 1, 2).Contains("T1\nM06"), "split T/M06");
        Check(Run(nc.Replace("G54", "G56"), 1, 6).Contains("G00 G59"), "source work offset need not be G54");
        foreach (int[] invalid in new int[][] { new int[0], new int[] { 0 }, new int[] { 7 }, new int[] { 1, 1 } })
        {
            bool rejected = false; try { WorkOffsetPrograms.Validate(invalid); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid offset selection");
        }
    }

    private static void ModalRestoration()
    {
        string a = Body(1);
        string headerOnly = Example(a.Replace("G40 G90 G00 G54", "G00 G54")).Replace("G17 G21 G94", "G17 G21 G94 G40").Replace("T1 M06", "G90\nT1 M06");
        string output = Run(headerOnly, 1, 2, 6);
        Check(output.Contains("G91 G28 Z0\nG90\n(operation"), "restore inherited G90 after retract");
        Check(Regex.Matches(output, @"(?m)^G90$").Count == 3, "original header G90 plus two repeats");
        Check(Regex.Matches(output, "G40").Count == 1, "G40 in header sufficient; no pointless duplication");
        string offsetInHeader = headerOnly.Replace("G17 G21 G94 G40", "G17 G21 G94 G40 G54").Replace("G00 G54 X", "G00 X");
        output = Run(offsetInHeader, 6, 3, 1, 2);
        Check(output.Contains("T1 M06\nG59\n"), "first requested offset inserted when declared only in header");
        Check(output.Contains("G90\nG56\n") && output.Contains("G90\nG54\n") && output.Contains("G90\nG55\n"), "header-only offset explicitly selected each pass");
        string split = Example(a.Replace("G40 G90 G00 G54 X1 Y2", "G40\nG90\nG54\nG00 X1\nY2").Replace("G43 Z20 H1", "G43\nH1\nZ20"));
        Check(Regex.Matches(Run(split, 1, 2), @"(?m)^Y2 S1000 M03$").Count == 2, "split modal words, XY approach and G43/H");
        foreach (string tail in new string[] { "G18\n", "G20\n", "G95\n" })
        {
            output = Run(Example(a + tail), 1, 2);
            string expected = tail == "G18\n" ? "G17" : tail == "G20\n" ? "G21" : "G94";
            Check(output.Contains(tail + expected + "\n(operation"), "restore a known header mode: " + expected);
        }
        string unknown = Example(a + "G21\n").Replace("G17 G21 G94", "G17 G94");
        Bad(unknown, "unknown units must not be invented");
        Bad(Example(a).Replace("G40 ", ""), "G40 unknown in entire NC");
        Bad(Example(a.Replace("M03", "")).Replace("T1 M06", "M03\nT1 M06"), "do not synthesize spindle commands");
        Bad(Example(a.Replace(" M08", "")).Replace("T1 M06", "M08\nT1 M06"), "do not synthesize coolant commands");
        Bad(headerOnly.Replace("G94 G40", "G94\n/ G40"), "optional-only header invalid when deleted");
        string optional = Example(a.Replace("M09\nM05\nG91 G28 Z0", "/ M09\n/ M05\n/ G91 G28 Z0\n/ G91 G28 Y0\nM09\nM05\nG91 G28 Z0"));
        Check(Run(optional, 1, 2).Contains("/ G91 G28 Z0"), "optional blocks preserved with unconditional exit");
    }

    private static void CyclesAndFailures()
    {
        string body = "G40 G90 G00 G54 X1 Y2 S1000 M03\nG43 Z20 H1 M08\nG98 G81 X1 Y2 Z-5 R2 F100\nX3\nG80 G00 Z20\nM05\nM09\nG91 G28 Z0\n";
        foreach (string cancel in new string[] { "G80 G00", "G00 G80", "G80\nG00" })
            Check(Regex.Matches(Run(Example(body.Replace("G80 G00", cancel)), 1, 2, 3, 6), "G81").Count == 4, "cycle cancellation and G00 order");
        string lateFeed = Example(body.Replace("G80 G00 Z20", "G80 G00 Z20\nG94")).Replace("G17 G21 G94", "G17 G21");
        Check(Run(lateFeed, 1, 2).Contains("T1 M06\nG94\n"), "legacy delayed G94 initialization");
        string a = Body(1);
        foreach (string bad in new string[] {
            a.Replace("G91 G28 Z0", "/ G91 G28 Z0"), a.Replace("G91 G28 Z0", "G00 Z20"),
            a.Replace("G91 G28 Z0", "G90 G28 Z0"), a.Replace("G91 G28 Z0", "G91 G28 X0 Z0"),
            a.Replace("G91 G28 Z0", "G91 G28 Z0\nG90 G00 Z5"), a.Replace("X1 Y2", "X1"),
            a.Replace("G40 G90", "G40 G91"), a.Replace("G40 G90", "/ G40 G90"),
            a.Replace("G43 Z20 H1", "Z20"), a.Replace("G43 Z20 H1", "G43 Z20 H1 X1"),
            a.Replace("F100", ""), a.Replace("S1000", ""), a.Replace("M03", "M05"), a.Replace("M03", "M19"),
            a.Replace("M09\nM05", "G41 D1\nM09\nM05"), a.Replace("G00 X3 Y4", "G00 G55 X3 Y4"),
            body.Replace("R2", ""), body.Replace("G80 G00", "G80 G00 G01"), body.Replace("G81 X", "G81 G83 X") })
            Bad(Example(bad), "invalid entry/exit/mode/cycle");
        foreach (string code in new string[] { "G54.1 P1", "G52 X0", "G68 X0 Y0 R30", "G10 L2 P1 X5", "M98 P20", "G65 P1", "#100=2", "IF[#1EQ1]GOTO20", "G00 A20", "M60" })
            Bad(Example(a.Replace("G00 X3 Y4", code)), "unverified dialect must not be ignored");
        Bad(Example(a).Replace("T1 M06", "/ T1 M06"), "optional tool change");
        Bad(Example(a).Replace("T1 M06", "T1 M06 G00 X0"), "mixed tool change/motion");
        Bad(Example(a).Replace("T1 M06", "M06"), "unknown T");
        Bad(Example(a).Replace("M30\nN99", "/ M30\nN99"), "optional termination");
        Bad(Example(a) + "O2\nM30\n", "multiple O programs");
        Bad(Example(a).Replace("(header G54)", "(open"), "broken comment");
        WorkOffsetException error = Bad(Example(a.Replace("G43 Z20 H1", "G43 Z20 H1 X1")), "precise source diagnostics");
        Check(error.Message.Contains("G43 Z20 H1 X1") && error.Message.Contains("G01 Z-1 F100"), "problem and neighboring NC lines");
    }

    private static void ProfilesAndFiles(string directory)
    {
        string a = Body(1);
        string g53 = Example(a).Replace("G91 G28 Z0", "G49 G40 G90 G00 G53 Z-5");
        WorkOffsetRules rules = new WorkOffsetRules { Profile = "ISO_G53", G53Z = -5 };
        Check(Run(g53, rules, 1, 2, 6).Contains("G00 G59"), "configured original G53 retract");
        Bad(g53, "never infer machine Z clearance");
        Bad(g53, new WorkOffsetRules { Profile = "ISO_G28", G53Z = -5 }, "profile mismatch");
        Bad(g53, new WorkOffsetRules { Profile = "ISO_G53", G53Z = 0 }, "wrong G53 height");
        Bad(g53.Replace("G49 ", ""), rules, "G53 with unverified length compensation");
        Bad(g53.Replace("G53 Z-5", "G53 X0 Z-5"), rules, "combined G53 XY/Z");
        Bad(g53.Replace("G90 G00 G53", "G91 G00 G53"), rules, "G53 must be absolute");
        Bad(g53.Replace("G00 G53", "G01 G53"), rules, "G53 retract must be rapid");
        Bad(g53.Replace("G21 ", ""), rules, "G53 coordinate units unknown");
        Bad(g53.Replace("M09\nM05", "G20\nM09\nM05"), rules, "mixed units make one configured G53 value ambiguous");
        Bad(g53.Replace("M09\nM05\nG49", "M09\nM05\n/ G49"), rules, "optional G53 exit");
        string iniPath = Path.Combine(directory, "NX_Postprocess_To_Machine.ini");
        string tcl = Path.Combine(directory, "post.tcl"), def = Path.Combine(directory, "post.def");
        File.WriteAllText(tcl, "# test"); File.WriteAllText(def, "# test");
        string ini = "; retained\r\n[Settings]\r\nPostList=\r\n[Post Test]\r\nTcl=" + tcl + "\r\nDef=" + def +
            "\r\nWorkOffsetProfile=ISO_G53\r\nWorkOffsetG53Z=-5\r\n";
        RouterConfig config = RouterConfig.Parse(ini, iniPath, delegate(string key) { return ""; });
        Check(config.Posts[0].WorkOffsetProfile == "ISO_G53" && config.Posts[0].WorkOffsetG53Z == -5, "INI profile parsed");
        // .NET Framework expands Windows 8.3 TEMP paths when resolving the INI.
        Check(WorkOffsetRules.FromPost(config.Posts[0]).Describe().Contains(Path.GetFullPath(tcl)), "post identity in diagnostics");
        foreach (string bad in new string[] { ini.Replace("ISO_G53", "Unknown"), ini.Replace("WorkOffsetG53Z=-5\r\n", ""), ini.Replace("=-5", "=-5,2"), ini.Replace("ISO_G53", "ISO_G28") })
        {
            bool failed = false; try { RouterConfig.Parse(bad, iniPath, delegate(string key) { return ""; }); } catch (FormatException) { failed = true; }
            Check(failed, "invalid profile configuration rejected");
        }
        File.WriteAllText(iniPath, ini, new UTF8Encoding(false));
        PostIniDocument document = new PostIniDocument(iniPath);
        document.Posts[0].Name = "Renamed";
        string edited = document.BuildText(delegate(string key) { return ""; });
        Check(edited == ini.Replace("[Post Test]", "[Post Renamed]"), "path editor retains profile and all other settings exactly");
        string file = Path.Combine(directory, "O1.nc");
        File.WriteAllText(file, g53, new UTF8Encoding(false));
        WorkOffsetPrograms.Apply(file, "O1", new int[] { 1, 2, 6 }, config.Posts[0]);
        string output = File.ReadAllText(file);
        Check(output == Run(g53, rules, 1, 2, 6), "assigned post reaches Apply");
        string called = Encoding.UTF8.GetString(ProgramCallChain.Rewrite(Bytes(output), "O1", new string[] { "2", "3" }));
        Check(called.Contains("M98 P2\nM98 P3\nM30"), "main sequential calls after offset processing");
        string sub = Encoding.UTF8.GetString(ProgramCallChain.Rewrite(Bytes(output), "O1", new string[0]));
        Check(sub.Contains("M99\nN99"), "subprogram M99 preserved");
        string invalid = Example(a.Replace("G91 G28 Z0", "G00 Z20"));
        File.WriteAllText(file, invalid, new UTF8Encoding(false));
        string[] before = Directory.GetFiles(directory);
        bool stopped = false;
        try { WorkOffsetPrograms.Apply(file, "O1", new int[] { 1, 2 }); } catch (WorkOffsetException) { stopped = true; }
        Check(stopped && File.ReadAllText(file) == invalid, "rejection leaves staged NC untouched");
        Check(Directory.GetFiles(directory).Length == before.Length, "no report, backup or sidecar on error");
    }

    private static int Main(string[] args)
    {
        try
        {
            FormatsAndOrder(); ModalRestoration(); CyclesAndFailures(); ProfilesAndFiles(args[0]);
            Console.WriteLine("PASS work offsets: " + count + " assertions (formats, modal state, profiles, errors, preservation).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
