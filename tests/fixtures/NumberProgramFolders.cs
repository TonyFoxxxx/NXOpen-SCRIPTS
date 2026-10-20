using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NXOpen;
using NXOpen.CAM;

namespace NXOpen
{
    public enum Tag { Null = 0 }
    public class NXObject
    {
        static int serial;
        public Tag Tag = (Tag)(++serial);
        public enum AttributeType { String }
        public Dictionary<string, string> Attributes = new Dictionary<string, string>();
        public bool HasUserAttribute(string title, AttributeType type, int index) { return Attributes.ContainsKey(title); }
        public string GetStringUserAttribute(string title, int index) { return Attributes[title]; }
        public void SetUserAttribute(string title, int index, string value, Update.Option option) { Attributes[title] = value; }
    }
    public class Update { public enum Option { Now } }
    public class Part { public string FullPath; public CAMSetup CAMSetup = new CAMSetup(); }
    public class PartCollection { public Part Work; }
    public class Session
    {
        public enum MarkVisibility { Visible }
        public enum LibraryUnloadOption { Immediately }
        public struct UndoMarkId { }
        public PartCollection Parts = new PartCollection();
        public string ExecutingJournal;
        private Action undo;
        public static Session GetSession() { return new Session(); }
        public UndoMarkId SetUndoMark(MarkVisibility visibility, string title)
        {
            List<Action> changes = new List<Action>();
            foreach (NCGroup g in Parts.Work.CAMSetup.CAMGroupCollection)
            {
                NCGroup group = g;
                string name = g.Name;
                Dictionary<string, string> attributes = new Dictionary<string, string>(g.Attributes);
                changes.Add(delegate { group.Name = name; group.Attributes = attributes; });
            }
            undo = delegate { foreach (Action change in changes) change(); };
            return new UndoMarkId();
        }
        public void UndoToMark(UndoMarkId mark, string title) { undo(); }
    }
    public class UI
    {
        public NXMessageBox NXMessageBox = new NXMessageBox();
        public static UI GetUI() { return new UI(); }
    }
    public class NXMessageBox
    {
        public enum DialogType { Information, Error }
        public void Show(string title, DialogType type, string message) { }
    }
}
namespace NXOpen.CAM
{
    public class CAMObject : NXObject { public string Name; }
    public class Operation : CAMObject { }
    public class NCGroup : CAMObject
    {
        public List<CAMObject> Members = new List<CAMObject>();
        public string FailName;
        public CAMObject[] GetMembers() { return Members.ToArray(); }
        public void SetName(string name)
        {
            if (name == FailName) throw new Exception("Injected NX rename failure");
            Name = name;
        }
    }
    public class CAMSetup
    {
        public enum View { ProgramOrder }
        public NCGroup Root = new NCGroup { Name = "NC_PROGRAM" };
        public List<NCGroup> CAMGroupCollection = new List<NCGroup>();
        public List<Operation> CAMOperationCollection = new List<Operation>();
        public NCGroup GetRoot(View view) { return Root; }
    }
}

public class Checks
{
    static string template, root;
    const BindingFlags Fields = BindingFlags.Public | BindingFlags.Instance;
    static Type registryType = typeof(NX_Number_Program_Folders).GetNestedType("Registry", BindingFlags.NonPublic);
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static string Field(object record, string name) { return Convert.ToString(record.GetType().GetField(name, Fields).GetValue(record)); }
    static object Registry(string file) { return registryType.GetMethod("Load").Invoke(null, new object[] { file }); }
    static object Record(string file, int number)
    {
        foreach (object record in (IEnumerable)registryType.GetField("Records").GetValue(Registry(file)))
            if (Field(record, "Number") == number.ToString()) return record;
        throw new Exception("Missing record " + number);
    }
    static int Count(string file) { return ((ICollection)registryType.GetField("Records").GetValue(Registry(file))).Count; }
    static void State(string file, int number, string state)
    {
        object registry = Registry(file);
        foreach (object record in (IEnumerable)registryType.GetField("Records").GetValue(registry))
            if (Field(record, "Number") == number.ToString())
                registryType.GetMethod("SetState").Invoke(registry, new object[] { record, state, "test interrupted issue" });
        registryType.GetMethod("SaveAtomic").Invoke(registry, null);
    }
    sealed class Case
    {
        public string Folder, Ini, Book;
        public Part Part;
        public Session Session;
        public List<NCGroup> Groups = new List<NCGroup>();
        public Case(string name, int count)
        {
            Folder = Path.Combine(root, name); Directory.CreateDirectory(Folder);
            Ini = Path.Combine(Folder, "settings.ini"); Book = Path.Combine(Folder, "register.xlsx");
            File.Copy(template, Book);
            File.WriteAllText(Ini, "[Numbering]\nPrefix=O\nStartNumber=1686\nEndNumber=1700\nRegistryFile=register.xlsx\n");
            Part = new Part { FullPath = Path.Combine(Folder, "Part.prt") }; File.WriteAllText(Part.FullPath, "fixture");
            Part.CAMSetup.CAMGroupCollection.Add(Part.CAMSetup.Root);
            Session = new Session(); Session.Parts.Work = Part;
            for (int i = 0; i < count; i++) Add("O_new_" + i);
            Run();
        }
        public NCGroup Add(string name)
        {
            NCGroup group = new NCGroup { Name = name };
            Groups.Add(group); Part.CAMSetup.Root.Members.Add(group); Part.CAMSetup.CAMGroupCollection.Add(group);
            return group;
        }
        public void Delete(int index)
        {
            NCGroup group = Groups[index]; Groups.RemoveAt(index);
            Part.CAMSetup.Root.Members.Remove(group); Part.CAMSetup.CAMGroupCollection.Remove(group);
        }
        public string Run() { return NX_Number_Program_Folders.Execute(Session, Part, Ini); }
        public void Names(params string[] names)
        {
            Assert(names.Length == Groups.Count, "Folder count");
            for (int i = 0; i < names.Length; i++) Assert(Groups[i].Name == names[i], "Expected " + names[i] + ", got " + Groups[i].Name);
        }
    }
    public static void Main(string[] args)
    {
        template = args[0]; root = args[1];
        Case c = new Case("deleted_middle", 5);
        string freedId = Field(Record(c.Book, 1690), "Id");
        c.Delete(3); c.Run(); c.Names("O1686", "O1687", "O1688", "O1689");
        Assert(Count(c.Book) == 5, "Deleted number must remain occupied in register");
        Assert(Field(Record(c.Book, 1690), "Note").StartsWith("Резерв этого проекта. "), "Tail marked as project reserve");
        byte[] before = File.ReadAllBytes(c.Book); c.Run();
        Assert(Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(c.Book)), "Repeat must not rewrite registry");
        c.Add("O_new"); c.Run(); c.Names("O1686", "O1687", "O1688", "O1689", "O1690");
        Assert(Count(c.Book) == 5 && c.Groups[4].Attributes["NX_NUM_V1_ID"] == freedId, "Reuse the same project slot and ID");

        c = new Case("multiple_holes_and_reorder", 5);
        c.Delete(3); c.Delete(0);
        c.Groups.Reverse(); c.Part.CAMSetup.Root.Members.Reverse(); c.Run();
        c.Names("O1686", "O1687", "O1688");

        c = new Case("manual_non_o", 4);
        c.Groups[1].Name = "Manual"; c.Delete(2); c.Run();
        c.Names("O1686", "Manual", "O1688");
        Assert(!Field(Record(c.Book, 1687), "Note").StartsWith("Резерв этого проекта. "), "Manual folder is not deleted");

        c = new Case("foreign_project", 4);
        Part other = new Part { FullPath = Path.Combine(c.Folder, "Other.prt") }; File.WriteAllText(other.FullPath, "other");
        NCGroup otherGroup = new NCGroup { Name = "O_new" };
        other.CAMSetup.Root.Members.Add(otherGroup); other.CAMSetup.CAMGroupCollection.Add(otherGroup);
        Session otherSession = new Session(); otherSession.Parts.Work = other;
        NX_Number_Program_Folders.Execute(otherSession, other, c.Ini);
        string otherRecord = Field(Record(c.Book, 1690), "Id");
        c.Delete(1); c.Run(); c.Names("O1686", "O1687", "O1688");
        Assert(otherGroup.Name == "O1690" && Field(Record(c.Book, 1690), "Id") == otherRecord, "Other project's number preserved");
        c.Add("O_new"); c.Run(); Assert(c.Groups[3].Name == "O1689", "Reuse own reserve before global max");

        c = new Case("occupied_name", 4);
        c.Delete(1); c.Part.CAMSetup.CAMOperationCollection.Add(new Operation { Name = "O1687" }); c.Run();
        c.Names("O1686", "O1688", "O1689");

        foreach (string status in new string[] { "Отменён", "Зарезервирован" })
        {
            c = new Case(status == "Отменён" ? "cancelled" : "interrupted", 4);
            State(c.Book, 1687, status); c.Delete(1); c.Run();
            c.Names("O1686", "O1688", "O1689");
            Assert(Field(Record(c.Book, 1687), "State") == status, "Unconfirmed record not reused");
        }

        c = new Case("range_exhausted_with_reserve", 5);
        File.WriteAllText(c.Ini, File.ReadAllText(c.Ini).Replace("EndNumber=1700", "EndNumber=1690"));
        c.Delete(2); c.Run(); c.Add("O_new"); c.Run(); c.Names("O1686", "O1687", "O1688", "O1689", "O1690");

        c = new Case("rollback", 4); c.Delete(1);
        string note = Field(Record(c.Book, 1689), "Note"), oldId = c.Groups[1].Attributes["NX_NUM_V1_ID"];
        c.Groups[1].FailName = "O1687";
        bool failed = false; try { c.Run(); } catch (Exception) { failed = true; }
        Assert(failed, "Expected injected failure"); c.Names("O1686", "O1688", "O1689");
        Assert(c.Groups[1].Attributes["NX_NUM_V1_ID"] == oldId && Field(Record(c.Book, 1689), "Note") == note, "Rollback preserves marks and registry");

        c = new Case("all_removed_then_new", 3);
        while (c.Groups.Count > 0) c.Delete(0);
        c.Run(); c.Add("O_new"); c.Run(); c.Names("O1686"); Assert(Count(c.Book) == 3, "All deleted slots stay project-owned");

        c = new Case("missing_record", 3);
        File.Copy(template, c.Book, true);
        failed = false; try { c.Run(); } catch (Exception) { failed = true; }
        Assert(failed, "Lost registry must not reset live marked folders"); c.Names("O1686", "O1687", "O1688");
        Console.WriteLine("12 numbering scenarios passed");
    }
}
