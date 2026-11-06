using System;
using System.Collections.Generic;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using CamOperation = NXOpen.CAM.Operation;

public static class Checks
{
    static int cases;
    static Part part;
    static CAMSetup setup;
    static Tool empty;
    static Tool used;
    static CamOperation operation;
    public static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Reset()
    {
        Session.Current = new Session(); UI.Current = new UI(); UFSession.Current = new UFSession();
        part = new Part(); setup = new CAMSetup(); part.CAMSetup = setup;
        Session.Current.Parts.Work = part;
        setup.Root = new NCGroup { Name = "GENERIC_MACHINE", OwningPart = part };
        setup.CAMGroupCollection.Add(setup.Root);
        empty = AddTool("EMPTY"); used = AddTool("USED");
        operation = new CamOperation { Name = "UNGENERATED", Parent = used };
        setup.CAMOperationCollection.Add(operation); used.Members.Add(operation);
    }
    static Tool AddTool(string name)
    {
        Tool tool = new Tool { Name = name, OwningPart = part, Parent = setup.Root };
        setup.CAMGroupCollection.Add(tool); return tool;
    }
    static void Run() { NXDeleteEmptyTools.Main(new string[0]); }
    static void Kept() { Assert(setup.CAMGroupCollection.Contains(empty), "empty tool was unexpectedly removed"); }
    static void RolledBack()
    {
        Kept(); Assert(Session.Current.UndoCalls == 1, "expected rollback");
        Assert(Session.Current.UpdateManager.Queue.Count == 0, "delete queue leaked");
        Assert(UI.Current.LastType == NXMessageBox.DialogType.Error, "error message missing");
    }
    static void Case(Action body) { Reset(); body(); cases++; }
    public static void Main()
    {
        Case(delegate {
            Run(); Assert(!setup.CAMGroupCollection.Contains(empty), "empty tool remains");
            Assert(setup.CAMGroupCollection.Contains(used), "used tool deleted");
            Assert(setup.CAMOperationCollection.Contains(operation) && operation.Parent == used, "operation changed");
            Assert(Session.Current.MarkCalls == 1 && Session.Current.UndoCalls == 0, "one visible undo step required");
            Assert(UFSession.Current.Ui.Lines.Contains("EMPTY") && !UFSession.Current.Ui.Lines.Contains("USED"), "preview contains wrong tools");
            Session.Current.UndoToMark(Session.UndoMarkId.One, null); Kept();
        });
        Case(delegate { Tool duplicate = AddTool("EMPTY"); Run(); Assert(!setup.CAMGroupCollection.Contains(duplicate), "duplicate name skipped"); });
        foreach (int response in new int[] { 12, 0, 2, 3 }) {
            int answer = response;
            Case(delegate { UFSession.Current.Ui.Response = answer; Run(); Kept(); Assert(Session.Current.MarkCalls == 0, "cancel changed history"); });
        }
        Case(delegate { Session.Current.Parts.Work = null; Run(); Assert(Session.Current.MarkCalls == 0, "no-part mutation"); });
        Case(delegate { part.CAMSetup = null; Run(); Assert(Session.Current.MarkCalls == 0, "no-CAM mutation"); });
        Case(delegate { setup.CAMGroupCollection.Remove(empty); Run(); Assert(UFSession.Current.Ui.Calls == 0, "nothing to delete still prompts"); });
        Case(delegate {
            NCGroup folder = new NCGroup { Name = "EMPTY_FOLDER", OwningPart = part, Parent = setup.Root };
            setup.CAMGroupCollection.Add(folder); Run(); Assert(setup.CAMGroupCollection.Contains(folder), "folder deleted");
        });
        Case(delegate {
            Tool container = AddTool("CONTAINER"); empty.Parent = container; container.Members.Add(empty);
            Run(); Assert(setup.CAMGroupCollection.Contains(container), "tool container deleted");
        });
        Case(delegate {
            NCGroup nested = new NCGroup { Name = "NESTED", Parent = empty, OwningPart = part };
            setup.CAMGroupCollection.Add(nested); operation.Parent = nested;
            // Empty members intentionally contradict operation ancestry: ancestry must protect it.
            Run(); Kept(); Assert(operation.Parent == nested, "nested operation reassigned");
        });
        Case(delegate { empty.OwningPart = new Part(); Run(); Kept(); });
        Case(delegate { empty.OwningPart = null; Run(); Kept(); });
        Case(delegate { empty.MembersFail = true; Run(); Kept(); Assert(Session.Current.MarkCalls == 0, "unreadable members deleted"); });
        Case(delegate { empty.NullMembers = true; Run(); Kept(); Assert(Session.Current.MarkCalls == 0, "null members deleted"); });
        Case(delegate { operation.Parent = null; Run(); Kept(); Assert(Session.Current.MarkCalls == 0, "unknown association ignored"); });
        Case(delegate { used.Parent = used; Run(); Kept(); Assert(Session.Current.MarkCalls == 0, "cycle not detected"); });
        Case(delegate {
            TaggedObject foreign = new TaggedObject(); Session.Current.UpdateManager.Queue.Add(foreign);
            Run(); Kept(); Assert(Session.Current.UpdateManager.Queue.Contains(foreign), "existing deletion was erased");
            Assert(Session.Current.MarkCalls == 0, "started with pending deletion");
        });
        Case(delegate {
            UFSession.Current.Ui.OnConfirm = delegate { operation.Parent = empty; empty.Members.Add(operation); };
            Run(); Kept(); Assert(Session.Current.MarkCalls == 0, "stale candidates deleted");
        });
        Case(delegate {
            UFSession.Current.Ui.OnConfirm = delegate { Session.Current.Parts.Work = new Part(); };
            Run(); Kept(); Assert(Session.Current.MarkCalls == 0, "work-part switch ignored");
        });
        Case(delegate { Session.Current.UpdateManager.AddErrors = 1; Run(); RolledBack(); });
        Case(delegate { Session.Current.UpdateManager.ExtraQueued = operation; Run(); RolledBack(); Assert(Session.Current.UpdateManager.UpdateCalls == 0, "cascading delete applied"); });
        Case(delegate { Session.Current.UpdateManager.ExtraQueued = used; Run(); RolledBack(); });
        Case(delegate { Session.Current.UpdateManager.UpdateErrors = 1; Run(); RolledBack(); });
        Case(delegate { Session.Current.UpdateManager.ThrowAfterDelete = true; Run(); RolledBack(); });
        Case(delegate { Session.Current.UpdateManager.SkipDelete = true; Run(); RolledBack(); });
        Case(delegate {
            Session.Current.UpdateManager.AfterUpdate = delegate { setup.CAMGroupCollection.Remove(used); };
            Run(); RolledBack(); Assert(setup.CAMGroupCollection.Contains(used), "collateral deletion not restored");
        });
        Case(delegate {
            Session.Current.UpdateManager.AfterUpdate = delegate { operation.Parent = setup.Root; };
            Run(); RolledBack(); Assert(operation.Parent == used, "operation assignment not restored");
        });
        Case(delegate {
            UFSession.Current.UiOnt.Fail = true; Run(); Assert(!setup.CAMGroupCollection.Contains(empty), "refresh error undid deletion");
            Assert(UI.Current.LastType == NXMessageBox.DialogType.Information && UI.Current.LastMessage.Contains("вручную"), "refresh warning missing");
        });
        Case(delegate {
            Session.Current.UpdateManager.ThrowAfterDelete = true; Session.Current.FailUndo = true;
            Run(); Assert(UI.Current.LastMessage.Contains("Не удалось полностью отменить"), "rollback failure hidden");
            Assert(!UI.Current.LastMessage.Contains("Изменения этого запуска отменены."), "false rollback success");
        });
        Assert(UI.Current.LastTitle.EndsWith("by @Tony_Foxxx (TG)"), "window caption incorrect");
        Console.WriteLine(cases + " empty-tool scenarios passed");
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
        public Parts Parts = new Parts(); public Update UpdateManager = new Update();
        public int MarkCalls, UndoCalls; public bool FailUndo;
        List<NCGroup> groups; List<CamOperation> ops; Dictionary<CamOperation, NCGroup> parents;
        Dictionary<NCGroup, List<CAMObject>> members;
        public UndoMarkId SetUndoMark(MarkVisibility visibility, string name)
        {
            MarkCalls++; CAMSetup setup = Parts.Work.CAMSetup;
            groups = new List<NCGroup>(setup.CAMGroupCollection); ops = new List<CamOperation>(setup.CAMOperationCollection);
            parents = new Dictionary<CamOperation, NCGroup>(); members = new Dictionary<NCGroup, List<CAMObject>>();
            foreach (CamOperation op in ops) parents[op] = op.Parent;
            foreach (NCGroup group in groups) members[group] = new List<CAMObject>(group.Members);
            return UndoMarkId.One;
        }
        public void UndoToMark(UndoMarkId mark, string name)
        {
            UndoCalls++; if (FailUndo) throw new Exception("undo failed");
            CAMSetup setup = Parts.Work.CAMSetup;
            setup.CAMGroupCollection.Clear(); setup.CAMGroupCollection.AddRange(groups);
            setup.CAMOperationCollection.Clear(); setup.CAMOperationCollection.AddRange(ops);
            foreach (CamOperation op in ops) op.Parent = parents[op];
            foreach (NCGroup group in groups) { group.Members.Clear(); group.Members.AddRange(members[group]); }
        }
        public void DeleteUndoMark(UndoMarkId mark, string name) { }
    }
    public class Update
    {
        public List<TaggedObject> Queue = new List<TaggedObject>();
        public int AddErrors, UpdateErrors, UpdateCalls;
        public TaggedObject ExtraQueued; public bool ThrowAfterDelete, SkipDelete; public Action AfterUpdate;
        public TaggedObject[] GetObjectsOnDeleteList() { return Queue.ToArray(); }
        public int AddObjectsToDeleteList(TaggedObject[] objects)
        { Queue.AddRange(objects); if (ExtraQueued != null) Queue.Add(ExtraQueued); return AddErrors; }
        public void RemoveObjectsFromDeleteList(TaggedObject[] objects) { foreach (TaggedObject item in objects) Queue.Remove(item); }
        public int DoUpdate(Session.UndoMarkId mark)
        {
            UpdateCalls++; CAMSetup setup = Session.Current.Parts.Work.CAMSetup;
            if (!SkipDelete) foreach (TaggedObject item in Queue) {
                NCGroup group = item as NCGroup;
                if (group != null) { setup.CAMGroupCollection.Remove(group); if (group.Parent != null) group.Parent.Members.Remove(group); }
            }
            Queue.Clear(); if (AfterUpdate != null) AfterUpdate();
            if (ThrowAfterDelete) throw new Exception("update failed after deletion");
            return UpdateErrors;
        }
    }
    public class UI
    {
        public static UI Current; public static UI GetUI() { return Current; }
        public NXMessageBox NXMessageBox = new NXMessageBox();
        public NXMessageBox.DialogType LastType; public string LastMessage = "", LastTitle = "";
    }
    public class NXMessageBox
    {
        public enum DialogType { Information, Error }
        public int Show(string title, DialogType type, string message)
        { UI.Current.LastTitle = title; UI.Current.LastType = type; UI.Current.LastMessage = message; return 0; }
    }
}
namespace NXOpen.CAM
{
    public class CAMObject : TaggedObject { public string Name; public Part OwningPart; }
    public class NCGroup : CAMObject
    {
        public NCGroup Parent; public List<CAMObject> Members = new List<CAMObject>(); public bool MembersFail, NullMembers;
        public NCGroup GetParent() { return Parent; }
        public CAMObject[] GetMembers() { if (MembersFail) throw new Exception("members unavailable"); return NullMembers ? null : Members.ToArray(); }
    }
    public class Tool : NCGroup { }
    public class Operation : CAMObject { public NCGroup Parent; public NCGroup GetParent(CAMSetup.View view) { return Parent; } }
    public class CAMSetup
    {
        public enum View { MachineTool }
        public NCGroup Root; public NCGroup GetRoot(View view) { return Root; }
        public List<NCGroup> CAMGroupCollection = new List<NCGroup>();
        public List<Operation> CAMOperationCollection = new List<Operation>();
    }
}
namespace NXOpen.UF
{
    public enum UiMessageDialogType { UiMessageQuestion }
    public class UFSession
    {
        public static UFSession Current; public static UFSession GetUFSession() { return Current; }
        public UFUi Ui = new UFUi(); public UIOnt UiOnt = new UIOnt();
    }
    public class UIOnt { public bool Fail; public void Refresh() { if (Fail) throw new Exception("refresh failed"); } }
    public class UFUi
    {
        public struct MessageButtons { public bool button1, button2; public string label1, label2; public int response1, response2; }
        public int Response = 11, Calls; public Action OnConfirm; public List<string> Lines;
        public void MessageDialog(string title, UiMessageDialogType type, string[] lines, int count, bool translate, ref MessageButtons buttons, out int response)
        { Calls++; Lines = new List<string>(lines); if (OnConfirm != null) OnConfirm(); response = Response; }
    }
}
