// Synthetic NX contract doubles. The test driver inserts production methods from the standalone journal.
using System;
using System.Collections.Generic;
using System.Globalization;
using CamOperation = Operation;

internal enum Tag { Null }
internal static class UFConstants
{
    public const int UF_STD_ERR_USER_ABORT = 1570003;
    public const int USER_ABORT = 875127;
}
internal class NXException : Exception
{
    internal int ErrorCode { get; private set; }
    internal NXException(int code, string message) : base(message) { ErrorCode = code; }
}
internal class NXObject
{
    static int sequence;
    public Tag Tag = (Tag)(++sequence);
    public Part OwningPart;
    public string Name = "";
    public bool IsOccurrence;
}
internal static class NXObjectManager
{
    public static Dictionary<Tag, NXObject> Items = new Dictionary<Tag, NXObject>();
    public static NXObject Get(Tag tag) { return Items[tag]; }
}
internal class CAMObject : NXObject { }
internal class NCGroup : CAMObject
{
    internal List<CAMObject> Members = new List<CAMObject>();
    public CAMObject[] GetMembers() { return Members.ToArray(); }
}
internal class Method : NCGroup { }
internal class Tool : NCGroup { }
internal class OrientGeometry : NCGroup { }
internal class Operation : CAMObject { }
internal class PlanarMilling : Operation
{
    internal NCGroup[] Parents;
    internal readonly List<BoundarySetPlanarMill> Saved = new List<BoundarySetPlanarMill>();
}
internal class OperationCollection
{
    public enum UseDefaultName { False }
    public List<Operation> Items = new List<Operation>();
    public Operation[] ToArray() { return Items.ToArray(); }
    internal int Builders, Commits, Destroyed, ChecksBeforeCommit, ChecksAfterCommit;
    internal PlanarMillingBuilder CreatePlanarMillingBuilder(PlanarMilling operation)
    { Builders++; return new PlanarMillingBuilder(this, operation); }
    public Operation Create(NCGroup p, NCGroup m, NCGroup t, NCGroup g, string type, string subtype, UseDefaultName policy, string name)
    {
        Checks.Expect(type == "mill_planar" && subtype == "PLANAR_MILL" && policy == UseDefaultName.False, "wrong template");
        var op = new PlanarMilling { Name = name, Parents = new NCGroup[] { p, m, t, g }, OwningPart = Checks.Part };
        Items.Add(op);
        if (Checks.Mode == "create-failure") throw new Exception("synthetic partial creation");
        if (Checks.Mode == "create-abort" || Checks.Mode == "undo-failure" || Checks.Mode == "delete-mark-failure")
            throw new NXException(UFConstants.USER_ABORT, "Отмена");
        NXObjectManager.Items[op.Tag] = op;
        return op;
    }
}
internal class NCGroupCollection
{
    internal List<NCGroup> Items = new List<NCGroup>();
    public NCGroup[] ToArray() { return Items.ToArray(); }
}
internal class CAMSetup
{
    public enum View { ProgramOrder, MachineMethod, MachineTool, Geometry }
    public NCGroup[] Roots;
    public NCGroup GetRoot(View view) { return Roots[(int)view]; }
    public OperationCollection CAMOperationCollection = new OperationCollection();
    public NCGroupCollection CAMGroupCollection = new NCGroupCollection();
}
internal class BasePart { public enum Units { Millimeters, Inches } }
internal class Part : NXObject { internal CAMSetup CAMSetup = new CAMSetup(); internal BasePart.Units PartUnits = BasePart.Units.Millimeters; }
internal class NXMessageBox { internal enum DialogType { Information, Error } }
internal class Face { public void Highlight() { } public void Unhighlight() { } }
internal class PlanarCurveChains
{
    internal class Frame { }
    internal class Chain { internal List<int> Items = new List<int>(); internal bool Closed; }
}
internal class V { public double X, Y, Z; }
internal class Point3d { internal Point3d(double x, double y, double z) { } }
internal class Matrix3x3 { }
internal class Plane { }
internal class BoundarySet { }
internal class BoundarySetPlanarMill : BoundarySet
{
    internal BoundarySetPlanarMill BoundaryMemberList { get { return this; } }
    internal NXObject[] Members = new NXObject[0];
    internal NXObject[] GetContents() { return Members; }
    internal bool Closed;
}
internal class Boundary
{
    internal readonly List<BoundarySet> Items = new List<BoundarySet>();
    internal Boundary BoundaryList { get { return this; } }
    internal BoundarySet[] GetContents() { return Items.ToArray(); }
    internal void Erase(BoundarySet item) { Items.Remove(item); }
}
internal class PlanarMillingBuilder
{
    readonly OperationCollection owner;
    readonly PlanarMilling operation;
    internal readonly Boundary PartBoundary = new Boundary();
    internal PlanarMillingBuilder(OperationCollection owner, PlanarMilling operation)
    {
        this.owner = owner; this.operation = operation;
        PartBoundary.Items.AddRange(operation.Saved);
        if (operation.Saved.Count == 0) PartBoundary.Items.Add(new BoundarySetPlanarMill());
    }
    internal void Commit()
    {
        owner.Commits++; Checks.Edits.Add("axes");
        if (Checks.Mode == "after-axes") throw new InvalidOperationException("synthetic native Commit failure");
        if (Checks.Mode == "commit-abort") throw new NXException(UFConstants.UF_STD_ERR_USER_ABORT, "Отмена");
        operation.Saved.Clear();
        foreach (BoundarySetPlanarMill item in PartBoundary.Items) operation.Saved.Add(item);
        if (Checks.Mode == "normalized")
        {
            UFSession.Current.Param.Values.Remove(37); // NX canonicalizes a different override at Commit.
            UFSession.Current.Param.ForbidAccess = true;
        }
    }
    internal void Destroy() { owner.Destroyed++; }
}
internal static class EngravingCamBoundary
{
    internal static BoundarySetPlanarMill Append(Boundary boundary, NXObject[] items, Point3d start, Matrix3x3 matrix, Plane plane, bool closed)
    {
        var set = new BoundarySetPlanarMill { Members = items, Closed = closed };
        boundary.Items.Add(set); return set;
    }
    internal static void Verify(BoundarySetPlanarMill set, bool closed)
    { Checks.Part.CAMSetup.CAMOperationCollection.ChecksBeforeCommit++; Checks.Expect(set.Closed == closed, "boundary type changed"); }
}
internal class Session
{
    internal enum UndoMarkId { Edit }
    internal enum MarkVisibility { Visible }
    internal class PartsList
    {
        internal Part Work
        {
            get
            {
                if (Checks.Mode == "managed-work-abort") throw new NXException(999, "User abort");
                return Checks.Part;
            }
        }
    }
    internal PartsList Parts = new PartsList();
    internal class Log
    {
        internal string Text = "";
        public void WriteLine(string message) { Text += message + "\n"; }
    }
    internal Log LogFile = new Log();
    public static Session Current;
    public static Session GetSession()
    {
        if (Checks.Mode == "session-abort") throw new NXException(UFConstants.UF_STD_ERR_USER_ABORT, "Отмена");
        return Current;
    }
    internal int Marks, Undos, RemovedMarks;
    private Operation[] before;
    internal UndoMarkId SetUndoMark(MarkVisibility visible, string name)
    { Marks++; before = Checks.Part.CAMSetup.CAMOperationCollection.ToArray(); return UndoMarkId.Edit; }
    internal void UndoToMark(UndoMarkId mark, string name)
    {
        Undos++;
        if (Checks.Mode == "undo-failure") throw new NXException(605, "synthetic undo failure");
        var list = Checks.Part.CAMSetup.CAMOperationCollection.Items;
        list.Clear(); list.AddRange(before); Checks.Edits.Clear();
    }
    internal void DeleteUndoMark(UndoMarkId mark, string name)
    {
        if (Checks.Mode == "delete-mark-failure") throw new NXException(606, "synthetic delete mark failure");
        RemovedMarks++;
    }
}
internal class UFSession
{
    public static UFSession Current;
    public static UFSession GetUFSession()
    {
        if (Checks.Mode == "uf-abort") throw new NXException(UFConstants.USER_ABORT, "Отмена");
        return Current;
    }
    internal class Assembly
    {
        internal int Calls;
        public Tag AskWorkPart()
        {
            Calls++;
            if (Checks.Mode == "work-abort") throw new NXException(999, "User abort");
            if (Checks.Mode == "work-error" || Checks.Mode == "error-dialog-abort")
                throw new NXException(314, "synthetic work part failure");
            if (Checks.Mode == "null-work" || (Calls == 2 && Checks.Mode == "closed-work")) return Tag.Null;
            if (Calls == 2 && Checks.Mode == "changed-work") return (Tag)909;
            if (Calls == 2 && Checks.Mode == "recheck-abort") throw new NXException(UFConstants.UF_STD_ERR_USER_ABORT, "Отмена");
            return Checks.Part.Tag;
        }
    }
    internal readonly Assembly Assem = new Assembly();
    internal class AbortApi
    {
        int reads;
        public void AskFlagStatus(out bool interrupted)
        {
            reads++;
            interrupted = Checks.Mode == "pending-abort" || (reads == 2 && Checks.Mode == "pending-at-write");
        }
    }
    internal readonly AbortApi Abort = new AbortApi();
    internal class Navigator
    {
        public int Count;
        public Tag[] Tags;
        public void AskSelectedNodes(out int count, out Tag[] tags) { count = Count; tags = Tags; }
        public void Refresh() { }
    }
    internal class UiApi
    {
        public string Status = "";
        public IntPtr GetDefaultParent() { return (IntPtr)1; }
        public void SetStatus(string text) { Status = text; }
    }
    internal readonly Navigator UiOnt = new Navigator();
    internal readonly UiApi Ui = new UiApi();
    internal readonly UFParam Param = new UFParam();
    internal ClearanceApi Cam;
}
internal class EngravingParentDialog
{
    internal static int Calls;
    internal static int[] Defaults;
    internal static string[][] Captions;
    public static bool Choose(IntPtr owner, string title, string[] labels, string[][] names, int[] selected)
    {
        Calls++; Defaults = (int[])selected.Clone(); Captions = names;
        if (Checks.Mode == "parent-cancel") return false;
        for (int i = 0; i < selected.Length; i++) if (selected[i] < 0) selected[i] = names[i].Length - 1;
        return true;
    }
}
internal class Workflow
{
    const string Title = "Fixture";
    class FaceGroup { }
    class FaceGeometryException : Exception { public List<Face> Faces = new List<Face>(); }
    private static Face[] SelectFaces() { return Checks.Mode == "face-cancel" ? null : new Face[] { new Face() }; }
    private static List<FaceGroup> Prepare(UFSession uf, Part part, Face[] faces, HashSet<Tag> assigned, double tolerance,
        double join, out PlanarCurveChains.Frame frame, out int skipped)
    {
        skipped = 0; frame = new PlanarCurveChains.Frame();
        if (Checks.Mode == "bad-geometry") throw new Exception("synthetic invalid face");
        if (Checks.Mode == "wrapped-abort") throw new InvalidOperationException("synthetic group", new NXException(999, "User abort"));
        if (Checks.Mode == "similar-error") throw new NXException(315, "User abort recovery failed");
        return Checks.Mode == "already-assigned" ? new List<FaceGroup>() : new List<FaceGroup> { new FaceGroup() };
    }
    static void EnsureNoInheritedBoundaries(UFSession uf, PlanarMilling op) { }
    class CurveInfo { internal V Start = new V(); internal NXObject Curve = new NXObject(); }
    static HashSet<Tag> AssignedCurves(UFSession uf, PlanarMilling op)
    {
        var tags = new HashSet<Tag>();
        foreach (var set in op.Saved) foreach (var item in set.Members) tags.Add(item.Tag);
        return tags;
    }
    static IntPtr[] Boundaries(UFSession uf, Tag op) { return new IntPtr[((PlanarMilling)NXObjectManager.Get(op)).Saved.Count]; }
    static void VerifyNewBoundaries(Part part, PlanarMilling op, List<Tag[]> previous, List<CurveInfo> curves, List<PlanarCurveChains.Chain> chains)
    {
        part.CAMSetup.CAMOperationCollection.ChecksAfterCommit++;
        if (Checks.Mode == "changed-existing-boundary") throw new InvalidOperationException("synthetic saved-boundary mismatch");
        Checks.Expect(op.Saved.Count == previous.Count + chains.Count, "wrong saved boundary count");
    }
    static string AssignAxes(UFSession uf, Part part, PlanarMilling op, EngravingParents parents, List<FaceGroup> groups,
        PlanarCurveChains.Frame frame, HashSet<Tag> assigned, int skipped, double unit, double join)
    {
        var previous = new List<Tag[]>();
        foreach (var set in op.Saved) previous.Add(Array.ConvertAll(set.Members, item => item.Tag));
        var curves = new List<CurveInfo> { new CurveInfo(), new CurveInfo() };
        var chains = new List<PlanarCurveChains.Chain> {
            new PlanarCurveChains.Chain { Items = { 0 } }, new PlanarCurveChains.Chain { Items = { 1 }, Closed = true } };
        CommitBoundaries(uf, part, op, parents, unit, new Plane(), new Matrix3x3(), curves, chains, previous, assigned);
        return op.Name;
    }
    static void Show(NXMessageBox.DialogType type, string message)
    {
        Checks.Dialogs++;
        if (Checks.Mode == "success-dialog-abort" || Checks.Mode == "error-dialog-abort") throw new NXException(999, "User abort");
        Checks.LastMessage = message; Checks.LastType = type;
    }
    // JOURNAL WORKFLOW
}

internal enum UdeSetType { UdeStartSet, UdeEndSet }
internal enum ParamClrplaneUsage { Other, ParamClrplaneUsageAtOperationStartAndEnd }
internal class ClearanceApi
{
    internal UFParam Param;
    private ParamClrplaneUsage usage;
    public void AskClearPlaneUsage(Tag tag, out ParamClrplaneUsage value) { value = usage; }
    public void SetClearPlaneUsage(Tag tag, ParamClrplaneUsage value) { usage = value; Param.SetIntValue(tag, 221, (int)value); }
}
internal class UFParam
{
    public enum Status { Default, Inherited, Overridden, InvalidIndex }
    public Dictionary<int, object> Values = new Dictionary<int, object>();
    internal bool ForbidAccess;
    private int Index(int index)
    {
        if (index == 9703) throw new InvalidOperationException("Coolant Type is outside the engraving preset.");
        if (ForbidAccess) throw new InvalidOperationException("Unexpected UF access after Commit: " + index);
        return index;
    }
    public int Writes;
    public int[] Events = { 2, 1 };
    public void AskUdes(Tag tag, UdeSetType set, out int count, out IntPtr[] events)
    { count = Events[(int)set]; events = count == 0 && set == UdeSetType.UdeStartSet ? null : new IntPtr[count + 1]; }
    public void DeleteAllUdes(Tag tag, UdeSetType set) { Events[(int)set] = 0; Writes++; }
    public void AskParamStatus(Tag tag, int i, out Status status) { status = Values.ContainsKey(Index(i)) ? Status.Overridden : Status.Inherited; }
    public void InheritValue(Tag tag, int i) { Values.Remove(Index(i)); Writes++; }
    public void Set2dValue(Tag tag, int i, double[] value) { Values[Index(i)] = value; Writes++; }
    public void Ask2dValue(Tag tag, int i, double[] value) { ((double[])Values[Index(i)]).CopyTo(value, 0); }
    public void SetIntValue(Tag tag, int i, int value) { Values[Index(i)] = value; Writes++; }
    public void AskIntValue(Tag tag, int i, out int value) { value = (int)Values[Index(i)]; }
    public void SetDoubleValue(Tag tag, int i, double value) { Values[Index(i)] = value; Writes++; }
    public void AskDoubleValue(Tag tag, int i, out double value) { value = (double)Values[Index(i)]; }
    public void SetLogicalValue(Tag tag, int i, bool value) { Values[Index(i)] = value; Writes++; }
    public void AskLogicalValue(Tag tag, int i, out bool value) { value = (bool)Values[Index(i)]; }
}
internal enum ValueIntent { Function, PartUnits }
internal enum ParamValueIntent { PartUnits, ToolDep }
internal enum FeedRateUnit { Other, PerMinute }
internal class InheritableBuilder { public bool InheritanceStatus = true; }
internal class InheritableDoubleBuilder : InheritableBuilder { public double Value; public ValueIntent ValueIntent; }
internal class InheritableToolDepBuilder : InheritableDoubleBuilder { public ParamValueIntent Intent; }
internal class InheritableFeedBuilder : InheritableBuilder { public double Value; public FeedRateUnit Unit; }
internal class FeedsBuilder
{
    public enum RecalculateBasedOn { SpindleSpeed, CutFeedRate }
    public bool OptimizeFeedRateWhenGenerating = true;
    public int SpindleRpmToggle, Recalculations;
    public InheritableDoubleBuilder SurfaceSpeedBuilder = new InheritableDoubleBuilder(), FeedPerToothBuilder = new InheritableDoubleBuilder(),
        SpindleRpmBuilder = new InheritableDoubleBuilder();
    public InheritableFeedBuilder FeedCutBuilder = new InheritableFeedBuilder();
    public InheritableBuilder FeedRapidOutput = new InheritableBuilder();
    public void RecalculateData(RecalculateBasedOn basis)
    {
        Recalculations++;
        if (basis == RecalculateBasedOn.SpindleSpeed) { SurfaceSpeedBuilder.Value = 84; FeedCutBuilder.Value = -1; }
        else FeedPerToothBuilder.Value = FeedCutBuilder.Value / 7000 / 4;
    }
}
internal class NcmPlanarEngRetBuilder
{
    internal enum EngRetTypes { SameAsDefault, PlungeLift }
    internal enum MeasureHeightFrom { CurrentLevel, PreviousLevel }
    internal enum MinClearanceTypes { ExtendAndTrim, ExtendOnly, SameAsRetract, SameAsOpenEngage }
    internal enum IfEngageDoesNotFitTypes { Plunge, Skip }
    internal EngRetTypes EngRetType;
    internal MeasureHeightFrom HeightFrom;
    internal MinClearanceTypes MinimumClearance;
    internal IfEngageDoesNotFitTypes IfEngageDoesNotFit;
    internal bool IgnoreBlankOnTrimSide, UseEffectDist, UseMaxWidth, StartAtArcCenter;
    internal double ArcAngle, SwingAngle, RampAngle;
    internal InheritableToolDepBuilder HeightBuilder = new InheritableToolDepBuilder(), LengthBuilder = new InheritableToolDepBuilder(),
        RadiusBuilder = new InheritableToolDepBuilder(), DiameterBuilder = new InheritableToolDepBuilder(), EffectDistBuilder = new InheritableToolDepBuilder(),
        MaxWidthBuilder = new InheritableToolDepBuilder(), MinClearanceBuilder = new InheritableToolDepBuilder(), MinRampLengthBuilder = new InheritableToolDepBuilder(),
        ExtendBeforeArc = new InheritableToolDepBuilder(), ExtendAfterArc = new InheritableToolDepBuilder();
    internal InheritableDoubleBuilder HelicalRampAngleBuilder = new InheritableDoubleBuilder();
}
internal sealed partial class EngravingPreset
{
    internal static void Apply(PlanarMillingBuilder builder, double unit, bool reference)
    { Checks.Edits.Add("preset"); }
    internal static void CheckEngage(NcmPlanarEngRetBuilder move, double unit, bool initial)
    {
        new EngravingPreset(unit).EngageOrRetract(move, initial ? NcmPlanarEngRetBuilder.EngRetTypes.SameAsDefault :
            NcmPlanarEngRetBuilder.EngRetTypes.PlungeLift, false, false, initial);
    }
    internal static void CheckFeeds(FeedsBuilder feeds, double unit, bool reference)
    { new EngravingPreset(unit).Feeds(feeds, reference); }
    internal static void CheckDistance(InheritableToolDepBuilder field, double unit, bool percent)
    { new EngravingPreset(unit).Distance("fixture", field, 50, percent); }
}
internal class Checks
{
    internal static Part Part;
    internal static string Mode, LastMessage;
    internal static NXMessageBox.DialogType LastType;
    internal static List<string> Edits = new List<string>();
    internal static int Dialogs;
    static int count;
    public static void Expect(bool result, string reason)
    { if (!result) throw new Exception(reason); count++; }
    static T Group<T>(string name, NCGroup parent) where T : NCGroup, new()
    {
        T group = new T { Name = name, OwningPart = Part };
        if (parent != null) parent.Members.Add(group);
        Part.CAMSetup.CAMGroupCollection.Items.Add(group); NXObjectManager.Items[group.Tag] = group;
        return group;
    }
    static void Reset(string mode)
    {
        Mode = mode; Edits.Clear(); Dialogs = 0; LastMessage = ""; LastType = NXMessageBox.DialogType.Information;
        EngravingParentDialog.Calls = 0; EngravingParentDialog.Defaults = null; NXObjectManager.Items.Clear();
        Session.Current = new Session(); Part = new Part(); NXObjectManager.Items[Part.Tag] = Part;
        UFSession.Current = new UFSession(); UFSession.Current.Cam = new ClearanceApi { Param = UFSession.Current.Param };
        var prog = Group<NCGroup>("Programs", null); Group<NCGroup>("Pocket", prog);
        var method = Group<Method>("Method", null); Group<Method>("Finish", method);
        var machine = Group<NCGroup>("Machine", null); Group<Tool>("Ball A", machine);
        var geometry = Group<NCGroup>("Geometry", null); var workpiece = Group<NCGroup>("Workpiece", geometry);
        Group<OrientGeometry>("MCS A", workpiece);
        Part.CAMSetup.Roots = new NCGroup[] { prog, method, machine, geometry };
    }
    static void Select(params NXObject[] objects)
    {
        List<Tag> tags = new List<Tag>();
        foreach (NXObject o in objects) { NXObjectManager.Items[o.Tag] = o; tags.Add(o.Tag); }
        UFSession.Current.UiOnt.Count = tags.Count; UFSession.Current.UiOnt.Tags = tags.ToArray();
    }
    static void Run() { Workflow.Main(new string[0]); }
    static int Ops { get { return Part.CAMSetup.CAMOperationCollection.Items.Count; } }
    static void InterruptCases()
    {
        // The live stack stopped in PartCollection.get_Work. The UF work-part tag can
        // identify a valid CAM part directly, including when a different part is displayed.
        Reset("managed-work-abort"); Run();
        Expect(Ops == 1 && UFSession.Current.Assem.Calls == 2 && Session.Current.Undos == 0, "work-part entry and pre-edit identity");
        foreach (string mode in new[] { "session-abort", "uf-abort", "work-abort", "recheck-abort", "wrapped-abort", "pending-abort", "pending-at-write" })
        {
            Reset(mode); Run();
            Expect(Ops == 0 && Edits.Count == 0 && Session.Current.Marks == 0 && Dialogs == 0, mode);
            if (mode == "work-abort")
                Expect(UFSession.Current.Assem.Calls == 1 && EngravingParentDialog.Calls == 0 && Session.Current.LogFile.Text.Contains("999"), "retried or lost cancellation code");
            if (mode == "pending-abort" || mode == "pending-at-write")
                Expect(UFSession.Current.Assem.Calls == (mode == "pending-abort" ? 0 : 1), "ignored pending NX interrupt");
        }
        foreach (string mode in new[] { "null-work", "closed-work", "changed-work", "work-error", "similar-error" })
        {
            Reset(mode); Run();
            Expect(Ops == 0 && Session.Current.Marks == 0 && LastType == NXMessageBox.DialogType.Error && Dialogs == 1, mode);
            if (mode == "work-error") Expect(LastMessage.Contains("314") && LastMessage.Contains("synthetic work part failure"), "lost work-part diagnostic");
        }
        foreach (string mode in new[] { "create-abort", "commit-abort" })
        {
            Reset(mode); Run();
            Expect(Ops == 0 && Edits.Count == 0 && Session.Current.Undos == 1 && Session.Current.RemovedMarks == 1 && Dialogs == 0, mode);
            Expect(UFSession.Current.Ui.Status.Contains("отменены"), "lost rollback status");
        }
        Reset("undo-failure"); Run();
        Expect(Ops == 1 && Session.Current.Undos == 1 && Session.Current.RemovedMarks == 0 && Dialogs == 1
            && LastType == NXMessageBox.DialogType.Error && LastMessage.Contains("synthetic undo failure") && LastMessage.Contains("605"), "hidden rollback failure");
        Reset("delete-mark-failure"); Run();
        Expect(Ops == 0 && Session.Current.Undos == 1 && LastMessage.Contains("отменены")
            && LastMessage.Contains("synthetic delete mark failure"), "mark cleanup misreported as a failed undo");
        Reset("success-dialog-abort"); Run();
        Expect(Ops == 1 && Session.Current.Undos == 0 && Dialogs == 1 && Session.Current.LogFile.Text.Contains("GRAVIROVKA"), "lost committed result");
        Reset("error-dialog-abort"); Run();
        Expect(Ops == 0 && Dialogs == 1 && Session.Current.LogFile.Text.Contains("314")
            && Session.Current.LogFile.Text.Contains("synthetic work part failure"), "secondary dialog failure masked the original error");
    }
    static void WorkflowCases()
    {
        foreach (string mode in new string[] { "parent-cancel", "face-cancel", "bad-geometry", "already-assigned" })
        {
            Reset(mode); Run(); Expect(Ops == 0 && Edits.Count == 0 && Session.Current.Marks == 0, "early " + mode);
            Expect(LastType == (mode == "bad-geometry" ? NXMessageBox.DialogType.Error : NXMessageBox.DialogType.Information), mode);
        }
        foreach (string mode in new string[] { "create-failure", "after-axes" })
        {
            Reset(mode); Run();
            Expect(Ops == 0 && Edits.Count == 0 && Session.Current.Marks == 1 && Session.Current.Undos == 1 && Session.Current.RemovedMarks == 1, "leaked " + mode);
            Expect(LastType == NXMessageBox.DialogType.Error && LastMessage.Contains("отменены"), "incorrect rollback report");
        }
        Reset("normalized"); UFSession.Current.Param.Values[9703] = 7; Run();
        var operations = Part.CAMSetup.CAMOperationCollection;
        Expect(operations.Commits == 1 && operations.Builders == 1 && operations.Destroyed == 1, "new operation reopened or committed twice");
        Expect(operations.ChecksBeforeCommit == 0 && operations.ChecksAfterCommit == 0 && LastType == NXMessageBox.DialogType.Information,
            "new operation was checked");
        Expect((int)UFSession.Current.Param.Values[9703] == 7 && !UFSession.Current.Param.Values.ContainsKey(37), "normalized settings changed");
        Mode = "ok"; UFSession.Current.Param.ForbidAccess = false;
        Expect(EngravingParentDialog.Defaults[0] == -1 && EngravingParentDialog.Defaults[1] == 0 &&
            EngravingParentDialog.Defaults[2] == -1 && EngravingParentDialog.Defaults[3] == 0, "guessed parent or tool");
        Expect(Ops == 1 && Edits.Contains("preset") && Edits.Contains("axes") && Session.Current.Undos == 0, "creation failed");
        PlanarMilling first = (PlanarMilling)Part.CAMSetup.CAMOperationCollection.Items[0];
        Expect(first.Name == "GRAVIROVKA" && first.Parents[0].Name == "Pocket" && first.Parents[2].Name == "Ball A", "parent routing");
        Expect(first.Saved.Count == 2 && !first.Saved[0].Closed && first.Saved[1].Closed, "open and closed boundaries on creation");
        int presetWrites = UFSession.Current.Param.Writes;
        Edits.Clear(); Select(first); Run();
        Expect(UFSession.Current.Param.Writes == presetWrites, "existing operation received UF settings");
        Expect(operations.ChecksBeforeCommit == 2 && operations.ChecksAfterCommit == 1, "existing append checks skipped");
        Expect(Ops == 1 && EngravingParentDialog.Calls == 1 && Edits.Count == 1 && Edits[0] == "axes", "existing operation reset");
        Mode = "changed-existing-boundary"; Run(); Expect(Ops == 1 && Session.Current.Undos == 1, "existing boundary rollback");
        Reset("ok"); NCGroup toolGroup = Part.CAMSetup.Roots[2];
        Group<Tool>(EngravingParents.ToolName, toolGroup); Run();
        Expect(EngravingParentDialog.Defaults[2] == 1 && ((PlanarMilling)Part.CAMSetup.CAMOperationCollection.Items[0]).Parents[2].Name == EngravingParents.ToolName, "exact tool");
        Group<Tool>(EngravingParents.ToolName, toolGroup); Run();
        Expect(EngravingParentDialog.Defaults[2] == -1 && Part.CAMSetup.CAMOperationCollection.Items[1].Name == "GRAVIROVKA_1", "duplicate tool or operation name");
        Expect(EngravingParentDialog.Captions[2][1] != EngravingParentDialog.Captions[2][2], "ambiguous tool captions");
        Reset("ok"); Select(Part.CAMSetup.Roots[0]); Run();
        Expect(Ops == 1 && EngravingParentDialog.Defaults[0] == 0, "explicit group ignored");
        foreach (string mode in new string[] { "two", "mixed", "unsupported", "occurrence", "foreign", "missing-tag", "stale-count", "ineligible-group" })
        {
            Reset(mode); var op = new PlanarMilling { OwningPart = Part };
            switch (mode)
            {
                case "two": Select(op, op); break;
                case "mixed": Select(op, Part.CAMSetup.Roots[0]); break;
                case "unsupported": Select(new Operation { OwningPart = Part }); break;
                case "occurrence": op.IsOccurrence = true; Select(op); break;
                case "foreign": op.OwningPart = new Part(); Select(op); break;
                case "missing-tag": UFSession.Current.UiOnt.Count = 1; break;
                case "stale-count": Select(op); UFSession.Current.UiOnt.Count = 0; break;
                case "ineligible-group": Select(Part.CAMSetup.Roots[2]); break;
            }
            Run();
            Expect(Ops == 0 && Session.Current.Marks == 0 && LastType == NXMessageBox.DialogType.Error && EngravingParentDialog.Calls == 0, "guessed " + mode);
        }
    }
    static void PresetCases()
    {
        foreach (double unit in new double[] { 1.0, 1 / 25.4 })
        {
            Reset("ok"); var uf = UFSession.Current; var op = new PlanarMilling();
            uf.Param.Values[220] = 2; uf.Param.Values[1000] = 99.0; uf.Param.Values[3007] = "manual floor";
            EngravingPreset.SetUfParameters(uf, op, unit);
            Expect(!uf.Param.Values.ContainsKey(220) && !uf.Param.Values.ContainsKey(1000) && (string)uf.Param.Values[3007] == "manual floor", "wrong inherited MCS, tool or Floor");
            Expect((double)uf.Param.Values[399] == 200 && (double)uf.Param.Values[42] == 3 * unit && !(bool)uf.Param.Values[116], "unit or zero/false mismatch");
            var tol = (double[])uf.Param.Values[37]; Expect(tol[0] == 0.03 * unit && tol[1] == tol[0], "boundary tolerance pair");
            Expect(uf.Param.Events[0] == 0 && uf.Param.Events[1] == 0, "start/end UDEs not cleared");
            Expect(!uf.Param.Values.ContainsKey(9703), "inherited coolant became local");
            var f = new InheritableToolDepBuilder();
            EngravingPreset.CheckDistance(f, unit, true); Expect(f.Value == 50 && f.Intent == ParamValueIntent.ToolDep && f.ValueIntent == ValueIntent.Function && !f.InheritanceStatus, "percent scaled");
            EngravingPreset.CheckDistance(f, unit, false); Expect(f.Value == 50 * unit && f.Intent == ParamValueIntent.PartUnits && f.ValueIntent == ValueIntent.PartUnits, "length not scaled");
            foreach (bool initial in new bool[] { false, true })
            {
                var move = new NcmPlanarEngRetBuilder();
                move.DiameterBuilder.Value = 72; move.MinRampLengthBuilder.Value = 27; move.HelicalRampAngleBuilder.Value = 12;
                EngravingPreset.CheckEngage(move, unit, initial);
                Expect(move.DiameterBuilder.InheritanceStatus == initial && move.MinRampLengthBuilder.InheritanceStatus == initial &&
                    move.HelicalRampAngleBuilder.InheritanceStatus == initial, "engage override/inheritance");
                Expect(move.HeightBuilder.Value == 3 * unit && move.LengthBuilder.Value == 90 && move.LengthBuilder.ValueIntent == ValueIntent.Function,
                    "closed engage height / tool-relative length");
                Expect(move.DiameterBuilder.Value == (initial ? 72 : 90) && move.MinRampLengthBuilder.Value == (initial ? 27 : 10) &&
                    move.HelicalRampAngleBuilder.Value == (initial ? 12 : 3), "inherited start moves overwritten");
            }
            foreach (bool reference in new bool[] { false, true })
            {
                var feeds = new FeedsBuilder();
                EngravingPreset.CheckFeeds(feeds, unit, reference);
                Expect(feeds.SpindleRpmBuilder.Value == 7000 && feeds.FeedCutBuilder.Value == 350 * unit &&
                    feeds.FeedCutBuilder.Unit == FeedRateUnit.PerMinute && !feeds.FeedCutBuilder.InheritanceStatus, "feed/RPM");
                if (reference) Expect(feeds.Recalculations == 0 && feeds.FeedPerToothBuilder.Value == 0.025 * unit &&
                    feeds.SurfaceSpeedBuilder.Value == (unit == 1 ? 21.991148575128552 : 21.991148575128552 / 0.3048), "reference feed parameters");
                else Expect(feeds.Recalculations == 2 && feeds.SurfaceSpeedBuilder.Value == 84 &&
                    feeds.FeedPerToothBuilder.Value == 350 * unit / 7000 / 4, "replacement tool inherited reference geometry");
            }
        }
    }
    public static int Main()
    {
        return ManagedTestHost.Run(delegate { InterruptCases(); WorkflowCases(); PresetCases(); Console.WriteLine(count + " engraving creation checks passed"); return 0; });
    }
}
