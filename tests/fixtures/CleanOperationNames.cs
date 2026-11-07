using System;
using System.Collections.Generic;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Operation = NXOpen.CAM.Operation;

public static class Checks
{
    static int cases;
    static Part part;
    static CAMSetup setup;
    public static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Equal(IList<string> actual, params string[] expected)
    {
        Assert(actual.Count == expected.Length, "Wrong number of names");
        for (int i = 0; i < expected.Length; i++)
            Assert(actual[i] == expected[i], "Expected " + expected[i] + ", got " + actual[i]);
    }
    static void Rule(string[] source, string[] expected, params string[] reserved)
    {
        List<string> actual = CleanOperationNameRules.BuildNames(source, reserved);
        Equal(actual, expected);
        Equal(CleanOperationNameRules.BuildNames(actual, reserved), expected);
        Assert(new HashSet<string>(actual, StringComparer.OrdinalIgnoreCase).Count == actual.Count, "Duplicate name");
        cases++;
    }
    static void Reset()
    {
        Session.Current = new Session(); UI.Current = new UI(); UFSession.Current = new UFSession();
        part = new Part(); setup = new CAMSetup(); part.CAMSetup = setup;
        Session.Current.Parts.Work = part;
        setup.Root = new NCGroup { Name = "NC_PROGRAM", OwningPart = part };
        setup.CAMGroupCollection.Add(setup.Root);
    }
    static Operation Op(string name, NCGroup parent)
    {
        Operation op = new Operation { Name = name, OwningPart = part };
        setup.CAMOperationCollection.Add(op);
        if (parent != null) parent.Members.Add(op);
        return op;
    }
    static NCGroup Group(string name, NCGroup parent)
    {
        NCGroup group = new NCGroup { Name = name, OwningPart = part };
        setup.CAMGroupCollection.Add(group);
        if (parent != null) parent.Members.Add(group);
        return group;
    }
    static void Run() { NXCleanOperationNames.Main(new string[0]); }
    static void Case(Action body) { Reset(); body(); cases++; }
    static void Restored(Operation op, string old)
    {
        Assert(op.Name == old, "Name not restored");
        Assert(Session.Current.UndoCalls == 1, "Rollback missing");
        Assert(UI.Current.LastType == NXMessageBox.DialogType.Error, "Error missing");
        Assert(UI.Current.LastMessage.Contains("Изменения этого запуска отменены."), "Rollback confirmation missing");
    }
    public static void Main()
    {
        Rule(new[] { "PROFILE_3D", "PROFILE_3D_INSTANCE", "PROFILE_3D_INSTANCE_1",
            "PROFILE_3D_INSTANCE_INSTANCE", "PROFILE_3D_COPY", "PROFILE_3D_COPY_INSTANCE",
            "PROFILE_3D_COPY_INSTANCE_1", "PROFILE_3D_COPY_INSTANCE_INSTANCE" },
            new[] { "PROFILE_3D", "PROFILE_3D_1", "PROFILE_3D_2", "PROFILE_3D_3",
                "PROFILE_3D_4", "PROFILE_3D_5", "PROFILE_3D_6", "PROFILE_3D_7" });
        Rule(new[] { "KARMAN_CHERN_DORAB_2", "KARMAN_CHERN_DORAB_2_INSTANCE",
            "KARMAN_CHERN_DORAB_2_INSTANCE_1", "KARMAN_CHERN_DORAB_2_INSTANCE_INSTANCE" },
            new[] { "KARMAN_CHERN_DORAB", "KARMAN_CHERN_DORAB_1", "KARMAN_CHERN_DORAB_2", "KARMAN_CHERN_DORAB_3" });
        Rule(new[] { "KARMAN_CHERN_1_COPY_COPY_COPY", "KARMAN_CHERN_1_COPY_COPY_COPY_INSTANCE" },
            new[] { "KARMAN_CHERN", "KARMAN_CHERN_1" });
        Rule(new[] { "PLSK_CHIST_Z0", "PLSK_CHIST_Z0_COPY", "PLSK_CHIST_8_Z0_COPY_INSTANCE_1" },
            new[] { "PLSK_CHIST_Z0", "PLSK_CHIST_1_Z0", "PLSK_CHIST_2_Z0" });
        Rule(new[] { "PREDV_D11_Z-33.8046", "PREDV_D11_1_Z-33.8046", "PREDV_D11_Z-10.15_COPY" },
            new[] { "PREDV_D11_Z-33.8046", "PREDV_D11_1_Z-33.8046", "PREDV_D11_Z-10.15" });
        Rule(new[] { "PERIMETR_ROUGH_NEW_Z-20.15", "14H7_CHERN_Z-29.9999", "R1_KANAVKA_INSTANCE",
            "ZLEVEL_TRANSFER_METHOD_1_INSTANCE_1", "CEKOVKA_Z-8.25" },
            new[] { "PERIMETR_ROUGH_NEW_Z-20.15", "14H7_CHERN_Z-29.9999", "R1_KANAVKA",
                "ZLEVEL_TRANSFER_METHOD", "CEKOVKA_Z-8.25" });
        foreach (string marker in new[] { "COPY", "INSTANCE", "INSTANT", "DUPLICATE", "КОПИЯ", "ЭКЗЕМПЛЯР" })
            Rule(new[] { "MILL_" + marker, "MILL_" + marker.ToLowerInvariant() + "17_2" }, new[] { "MILL", "MILL_1" });
        foreach (string z in new[] { "Z0", "Z-0.000", "Z+2.5000", "Z-.5", "Z-1,2500", "z-33.8046", "Z−5.0", "Z-1.2e-5" })
            Rule(new[] { "OP_8_COPY_" + z + "_INSTANCE_1", "OP_" + z + "_COPY" },
                new[] { "OP_" + z, "OP_1_" + z });
        Rule(new[] { "COPY_MILL", "MILL_COPY_FINISH", "R1_KANAVKA", "PROFILE_3D", "STEP_2_FINISH", "POCKET_NEW", "POCKET_MIRROR" },
            new[] { "COPY_MILL", "MILL_COPY_FINISH", "R1_KANAVKA", "PROFILE_3D", "STEP_2_FINISH", "POCKET_NEW", "POCKET_MIRROR" });
        Rule(new[] { "OP_99_COPY_8", "OP_7", "OP_3_4_1", "OP" }, new[] { "OP", "OP_1", "OP_2", "OP_3" });
        Rule(new[] { "OP_COPY", "op_INSTANCE", "OP" }, new[] { "OP_2", "op_4", "OP_5" }, "op", "OP_1", "op_3");
        Rule(new[] { "OP_Z-5_COPY", "OP_Z-5_INSTANCE" }, new[] { "OP_1_Z-5", "OP_3_Z-5" }, "OP_Z-5", "OP_2_Z-5");
        Rule(new[] { "OP__COPY__1", "OP" }, new[] { "OP", "OP_1" });
        Rule(new[] { "123_2_COPY", "COPY_COPY", "INSTANCE_1" }, new[] { "123", "COPY", "INSTANCE" });
        Rule(new[] { "OP_Z-1_Z-2_COPY", "OP_Z-1_7_Z-2" }, new[] { "OP_Z-1_Z-2", "OP_Z-1_1_Z-2" });
        Rule(new[] { "OP_Z-UNKNOWN_COPY", "OP_Z_COPY" }, new[] { "OP_Z-UNKNOWN", "OP_Z" });
        Rule(new[] { "ФРЕЗЕРОВАНИЕ_COPY_1", "ФРЕЗЕРОВАНИЕ_INSTANCE" }, new[] { "ФРЕЗЕРОВАНИЕ", "ФРЕЗЕРОВАНИЕ_1" });

        // Stress stable allocation and exact preservation against large mixed families.
        Random random = new Random(7001);
        List<string> many = new List<string>();
        for (int i = 0; i < 5000; i++)
            many.Add("MILL_D" + random.Next(1, 30) + "_" + random.Next(1, 99) + "_COPY_Z-" + random.Next(0, 4) + ".500_INSTANCE_1");
        List<string> clean = CleanOperationNameRules.BuildNames(many, new[] { "MILL_D1_Z-0.500" });
        Equal(CleanOperationNameRules.BuildNames(clean, new[] { "MILL_D1_Z-0.500" }), clean.ToArray());
        Assert(new HashSet<string>(clean, StringComparer.OrdinalIgnoreCase).Count == many.Count, "Stress collision");
        foreach (string name in clean) Assert(name.EndsWith(".500") && !name.Contains("COPY") && !name.Contains("INSTANCE"), "Stress suffix lost");
        cases++;

        Case(delegate {
            Operation a = Op("POCKET_COPY", setup.Root), b = Op("POCKET", setup.Root);
            Run(); Assert(a.Name == "POCKET" && b.Name == "POCKET_1", "Swap failed");
            Assert(Session.Current.MarkCalls == 1 && Session.Current.UndoCalls == 0, "Expected one undo mark");
            int calls = Session.Current.SetCalls; Run();
            Assert(Session.Current.SetCalls == calls && Session.Current.MarkCalls == 1, "Second run changes names");
            Session.Current.UndoToMark(Session.UndoMarkId.One, null);
            Assert(a.Name == "POCKET_COPY" && b.Name == "POCKET", "Single undo did not restore names");
        });
        Case(delegate {
            NCGroup folder = Group("PROGRAM", setup.Root);
            Operation late = Op("MILL_COPY", null), first = Op("MILL_INSTANCE", folder);
            setup.Root.Members.Add(late); Run();
            Assert(first.Name == "MILL" && late.Name == "MILL_1", "Collection order overrides program order");
            Assert(folder.Name == "PROGRAM", "Group renamed");
        });
        Case(delegate {
            Operation orphan = Op("MILL_COPY", null), normal = Op("MILL_INSTANCE", setup.Root);
            Run(); Assert(normal.Name == "MILL" && orphan.Name == "MILL_1", "Unassigned operation omitted");
        });
        Case(delegate {
            Group("MILL", null); Group("MILL_1", null);
            Operation op = Op("MILL_COPY", setup.Root); Run(); Assert(op.Name == "MILL_2", "CAM group collision");
        });
        Case(delegate { Run(); Assert(Session.Current.MarkCalls == 0, "Empty project changed history"); });
        Case(delegate { Op("STENKI_DORAB", setup.Root); Run(); Assert(Session.Current.MarkCalls == 0, "Clean name changed history"); });
        Case(delegate { Session.Current.Parts.Work = null; Run(); Assert(Session.Current.MarkCalls == 0, "No-part mutation"); });
        Case(delegate { part.CAMSetup = null; Run(); Assert(Session.Current.MarkCalls == 0, "No-CAM mutation"); });
        Case(delegate { Op("MILL_COPY", setup.Root).OwningPart = new Part(); Run(); Assert(Session.Current.MarkCalls == 0, "Foreign operation renamed"); });
        Case(delegate { Op("", setup.Root); Run(); Assert(Session.Current.MarkCalls == 0, "Empty name renamed"); });
        Case(delegate { Op("MILL_COPY", setup.Root); setup.Root = null; Run(); Assert(Session.Current.MarkCalls == 0, "Missing tree renamed"); });
        Case(delegate { Op("MILL_COPY", setup.Root); setup.Root.Members.Add(setup.Root); Run(); Assert(Session.Current.MarkCalls == 0, "Cycle ignored"); });
        Case(delegate { Op("MILL_COPY", setup.Root); setup.Root.NullMembers = true; Run(); Assert(Session.Current.MarkCalls == 0, "Unreadable group ignored"); });
        Case(delegate { Op("MILL_COPY", setup.Root); Session.Current.FailMark = true; Run(); Assert(Session.Current.SetCalls == 0, "Renamed without mark"); });
        // Fail on each temporary/final SetName and on a silently altered NX result.
        for (int failure = 1; failure <= 4; failure++)
        {
            int at = failure;
            Case(delegate {
                Operation a = Op("MILL_COPY", setup.Root), b = Op("MILL_INSTANCE", setup.Root);
                Session.Current.FailSet = at; Run(); Restored(a, "MILL_COPY"); Assert(b.Name == "MILL_INSTANCE", "Partial rename left behind");
            });
        }
        Case(delegate {
            Operation a = Op("MILL_COPY", setup.Root); Session.Current.TruncateAt = 2; Run(); Restored(a, "MILL_COPY");
        });
        Case(delegate {
            Operation a = Op("MILL_COPY", setup.Root), unchanged = Op("KEEP", setup.Root);
            Session.Current.AfterSet = delegate { if (a.Name == "MILL") unchanged.Name = "UNEXPECTED"; };
            Run(); Restored(a, "MILL_COPY"); Assert(unchanged.Name == "KEEP", "Linked name was not restored");
        });
        Case(delegate {
            Operation a = Op("MILL_COPY", setup.Root); UFSession.Current.UiOnt.Fail = true; Run();
            Assert(a.Name == "MILL" && Session.Current.UndoCalls == 0, "Refresh failure undoes renaming");
            Assert(UI.Current.LastMessage.Contains("вручную"), "Refresh warning missing");
        });
        Case(delegate {
            Op("MILL_COPY", setup.Root); Session.Current.FailSet = 2; Session.Current.FailUndo = true; Run();
            Assert(UI.Current.LastMessage.Contains("Не удалось полностью отменить"), "Undo failure hidden");
            Assert(!UI.Current.LastMessage.Contains("Изменения этого запуска отменены."), "False rollback success");
        });
        Case(delegate {
            Op("MILL_COPY", setup.Root); Session.Current.FailSet = 2; Session.Current.SkipUndo = true; Run();
            Assert(UI.Current.LastMessage.Contains("Не удалось полностью отменить"), "Incomplete undo hidden");
        });
        string signature = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String("YnkgQFRvbnlfRm94eHggKFRHKQ=="));
        Assert(UI.Current.LastTitle.EndsWith(signature), "Incorrect window title");
        Console.WriteLine(cases + " clean-name scenarios passed");
    }
}

namespace NXOpen
{
    public enum Tag { Null = 0 }
    public class TaggedObject { static int next; public Tag Tag = (Tag)(++next); }
    public class Part : TaggedObject { public CAMSetup CAMSetup; }
    public class Parts { public Part Work; }
    public class Session
    {
        public enum MarkVisibility { Visible }
        public enum UndoMarkId { One }
        public enum LibraryUnloadOption { Immediately }
        public static Session Current;
        public static Session GetSession() { return Current; }
        public Parts Parts = new Parts();
        public int MarkCalls, UndoCalls, SetCalls, FailSet, TruncateAt;
        public bool FailMark, FailUndo, SkipUndo;
        public Action AfterSet;
        Dictionary<Operation, string> names;
        public UndoMarkId SetUndoMark(MarkVisibility visibility, string name)
        {
            if (FailMark) throw new Exception("mark failed");
            MarkCalls++; names = new Dictionary<Operation, string>();
            foreach (Operation op in Parts.Work.CAMSetup.CAMOperationCollection) names.Add(op, op.Name);
            return UndoMarkId.One;
        }
        public void UndoToMark(UndoMarkId mark, string name)
        {
            UndoCalls++; if (FailUndo) throw new Exception("undo failed");
            if (!SkipUndo) foreach (KeyValuePair<Operation, string> item in names) item.Key.Name = item.Value;
        }
        public void DeleteUndoMark(UndoMarkId mark, string name) { }
    }
    public class UI
    {
        public static UI Current;
        public static UI GetUI() { return Current; }
        public NXMessageBox NXMessageBox = new NXMessageBox();
        public string LastMessage, LastTitle;
        public NXMessageBox.DialogType LastType;
    }
    public class NXMessageBox
    {
        public enum DialogType { Information, Error }
        public int Show(string title, DialogType type, string message)
        {
            UI.Current.LastTitle = title; UI.Current.LastType = type; UI.Current.LastMessage = message; return 0;
        }
    }
}
namespace NXOpen.CAM
{
    public class CAMObject : TaggedObject { public string Name; public Part OwningPart; }
    public class Operation : CAMObject
    {
        public void SetName(string value)
        {
            Session s = Session.Current; s.SetCalls++;
            if (s.SetCalls == s.FailSet) throw new Exception("rename rejected");
            CAMSetup setup = s.Parts.Work.CAMSetup;
            foreach (Operation op in setup.CAMOperationCollection)
                if (op != this && String.Equals(op.Name, value, StringComparison.OrdinalIgnoreCase)) throw new Exception("operation collision");
            foreach (NCGroup g in setup.CAMGroupCollection)
                if (String.Equals(g.Name, value, StringComparison.OrdinalIgnoreCase)) throw new Exception("group collision");
            Name = s.SetCalls == s.TruncateAt ? value.Substring(0, 1) : value;
            if (s.AfterSet != null) s.AfterSet();
        }
    }
    public class NCGroup : CAMObject
    {
        public List<CAMObject> Members = new List<CAMObject>();
        public bool NullMembers;
        public CAMObject[] GetMembers() { return NullMembers ? null : Members.ToArray(); }
    }
    public class CAMSetup
    {
        public enum View { ProgramOrder }
        public List<Operation> CAMOperationCollection = new List<Operation>();
        public List<NCGroup> CAMGroupCollection = new List<NCGroup>();
        public NCGroup Root;
        public NCGroup GetRoot(View view) { return Root; }
    }
}
namespace NXOpen.UF
{
    public class UFSession
    {
        public static UFSession Current;
        public static UFSession GetUFSession() { return Current; }
        public UFUiOnt UiOnt = new UFUiOnt();
    }
    public class UFUiOnt
    {
        public bool Fail;
        public void Refresh() { if (Fail) throw new Exception("refresh failed"); }
    }
}
