// Synthetic NX contract doubles. The test driver inserts production methods from the standalone journal.
using System;
using System.Collections.Generic;
using System.Globalization;
using CamOperation = Operation;

internal enum Tag { Null }
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
    internal NCGroup GetParent(CAMSetup.View view) { return Parents[(int)view]; }
}
internal class OperationCollection
{
    public enum UseDefaultName { False }
    public List<Operation> Items = new List<Operation>();
    public Operation[] ToArray() { return Items.ToArray(); }
    public Operation Create(NCGroup p, NCGroup m, NCGroup t, NCGroup g, string type, string subtype, UseDefaultName policy, string name)
    {
        Checks.Expect(type == "mill_planar" && subtype == "PLANAR_MILL" && policy == UseDefaultName.False, "wrong template");
        var op = new PlanarMilling { Name = name, Parents = new NCGroup[] { p, m, t, g }, OwningPart = Checks.Part };
        Items.Add(op);
        if (Checks.Mode == "create-failure") throw new Exception("synthetic partial creation");
        if (Checks.Mode == "wrong-parent") op.Parents[0] = g;
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
internal class PlanarCurveChains { internal class Frame { } }
internal class Session
{
    internal enum UndoMarkId { Edit }
    internal enum MarkVisibility { Visible }
    internal class PartsList { internal Part Work; }
    internal PartsList Parts = new PartsList();
    public static Session Current;
    public static Session GetSession() { return Current; }
    internal int Marks, Undos, RemovedMarks;
    private Operation[] before;
    internal UndoMarkId SetUndoMark(MarkVisibility visible, string name)
    { Marks++; before = Parts.Work.CAMSetup.CAMOperationCollection.ToArray(); return UndoMarkId.Edit; }
    internal void UndoToMark(UndoMarkId mark, string name)
    {
        Undos++; var list = Parts.Work.CAMSetup.CAMOperationCollection.Items;
        list.Clear(); list.AddRange(before); Checks.Edits.Clear();
    }
    internal void DeleteUndoMark(UndoMarkId mark, string name) { RemovedMarks++; }
}
internal class UFSession
{
    public static UFSession Current;
    public static UFSession GetUFSession() { return Current; }
    internal class Navigator
    {
        public int Count;
        public Tag[] Tags;
        public void AskSelectedNodes(out int count, out Tag[] tags) { count = Count; tags = Tags; }
        public void Refresh() { }
    }
    internal class UiApi { public IntPtr GetDefaultParent() { return (IntPtr)1; } public void SetStatus(string text) { } }
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
        return Checks.Mode == "already-assigned" ? new List<FaceGroup>() : new List<FaceGroup> { new FaceGroup() };
    }
    static void EnsureNoInheritedBoundaries(UFSession uf, PlanarMilling op) { }
    static HashSet<Tag> AssignedCurves(UFSession uf, PlanarMilling op) { return new HashSet<Tag>(); }
    static string AssignAxes(UFSession uf, Part part, PlanarMilling op, EngravingParents parents, List<FaceGroup> groups,
        PlanarCurveChains.Frame frame, HashSet<Tag> assigned, int skipped, double unit, double join)
    {
        if (parents != null) Checks.Edits.Add("preset");
        Checks.Edits.Add("axes");
        if (Checks.Mode == "after-axes") throw new Exception("synthetic saved-boundary mismatch");
        return op.Name;
    }
    static void Show(NXMessageBox.DialogType type, string message) { Checks.LastMessage = message; Checks.LastType = type; }
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
    public int Writes;
    public int[] Events = { 2, 1 };
    public void AskUdes(Tag tag, UdeSetType set, out int count, out IntPtr[] events)
    { count = Events[(int)set]; events = count == 0 && set == UdeSetType.UdeStartSet ? null : new IntPtr[count + 1]; }
    public void DeleteAllUdes(Tag tag, UdeSetType set) { Events[(int)set] = 0; Writes++; }
    public void AskParamStatus(Tag tag, int i, out Status status) { status = Values.ContainsKey(i) ? Status.Overridden : Status.Inherited; }
    public void InheritValue(Tag tag, int i) { Values.Remove(i); Writes++; }
    public void Set2dValue(Tag tag, int i, double[] value) { Values[i] = value; Writes++; }
    public void Ask2dValue(Tag tag, int i, double[] value) { ((double[])Values[i]).CopyTo(value, 0); }
    public void SetIntValue(Tag tag, int i, int value) { Values[i] = value; Writes++; }
    public void AskIntValue(Tag tag, int i, out int value) { value = (int)Values[i]; }
    public void SetDoubleValue(Tag tag, int i, double value) { Values[i] = value; Writes++; }
    public void AskDoubleValue(Tag tag, int i, out double value) { value = (double)Values[i]; }
    public void SetLogicalValue(Tag tag, int i, bool value) { Values[i] = value; Writes++; }
    public void AskLogicalValue(Tag tag, int i, out bool value) { value = (bool)Values[i]; }
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
    internal static void CheckEngage(NcmPlanarEngRetBuilder move, double unit, bool initial, bool verify)
    {
        new EngravingPreset(unit, verify).EngageOrRetract(move, initial ? NcmPlanarEngRetBuilder.EngRetTypes.SameAsDefault :
            NcmPlanarEngRetBuilder.EngRetTypes.PlungeLift, false, false, initial);
    }
    internal static void CheckFeeds(FeedsBuilder feeds, double unit, bool reference, bool verify)
    { new EngravingPreset(unit, verify).Feeds(feeds, reference); }
    internal static void CheckDistance(InheritableToolDepBuilder field, double unit, bool percent, bool verify)
    { new EngravingPreset(unit, verify).Distance("fixture", field, 50, percent); }
}
internal class Checks
{
    internal static Part Part;
    internal static string Mode, LastMessage;
    internal static NXMessageBox.DialogType LastType;
    internal static List<string> Edits = new List<string>();
    static int count;
    public static void Expect(bool result, string reason)
    { if (!result) throw new Exception(reason); count++; }
    static void Reject(Action action, string field)
    {
        try { action(); }
        catch (InvalidOperationException ex) { Expect(ex.Message.Contains(field), "Missing field: " + ex.Message); return; }
        throw new Exception("Accepted mismatched " + field);
    }
    static T Group<T>(string name, NCGroup parent) where T : NCGroup, new()
    {
        T group = new T { Name = name, OwningPart = Part };
        if (parent != null) parent.Members.Add(group);
        Part.CAMSetup.CAMGroupCollection.Items.Add(group); NXObjectManager.Items[group.Tag] = group;
        return group;
    }
    static void Reset(string mode)
    {
        Mode = mode; Edits.Clear(); LastMessage = ""; LastType = NXMessageBox.DialogType.Information;
        EngravingParentDialog.Calls = 0; EngravingParentDialog.Defaults = null; NXObjectManager.Items.Clear();
        Session.Current = new Session(); Part = Session.Current.Parts.Work = new Part();
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
    static void WorkflowCases()
    {
        foreach (string mode in new string[] { "parent-cancel", "face-cancel", "bad-geometry", "already-assigned" })
        {
            Reset(mode); Run(); Expect(Ops == 0 && Edits.Count == 0 && Session.Current.Marks == 0, "early " + mode);
            Expect(LastType == (mode == "bad-geometry" ? NXMessageBox.DialogType.Error : NXMessageBox.DialogType.Information), mode);
        }
        foreach (string mode in new string[] { "create-failure", "wrong-parent", "after-axes" })
        {
            Reset(mode); Run();
            Expect(Ops == 0 && Edits.Count == 0 && Session.Current.Marks == 1 && Session.Current.Undos == 1 && Session.Current.RemovedMarks == 1, "leaked " + mode);
            Expect(LastType == NXMessageBox.DialogType.Error && LastMessage.Contains("отменены"), "incorrect rollback report");
        }
        Reset("ok"); Run();
        Expect(EngravingParentDialog.Defaults[0] == -1 && EngravingParentDialog.Defaults[1] == 0 &&
            EngravingParentDialog.Defaults[2] == -1 && EngravingParentDialog.Defaults[3] == 0, "guessed parent or tool");
        Expect(Ops == 1 && Edits.Contains("preset") && Edits.Contains("axes") && Session.Current.Undos == 0, "creation failed");
        PlanarMilling first = (PlanarMilling)Part.CAMSetup.CAMOperationCollection.Items[0];
        Expect(first.Name == "GRAVIROVKA" && first.Parents[0].Name == "Pocket" && first.Parents[2].Name == "Ball A", "parent routing");
        Edits.Clear(); Select(first); Run();
        Expect(Ops == 1 && EngravingParentDialog.Calls == 1 && Edits.Count == 1 && Edits[0] == "axes", "existing operation reset");
        Mode = "after-axes"; Run(); Expect(Ops == 1 && Session.Current.Undos == 1, "existing operation lost");
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
            EngravingPreset.SetUfParameters(uf, op, unit, false);
            Expect(!uf.Param.Values.ContainsKey(220) && !uf.Param.Values.ContainsKey(1000) && (string)uf.Param.Values[3007] == "manual floor", "wrong inherited MCS, tool or Floor");
            Expect((double)uf.Param.Values[399] == 200 && (double)uf.Param.Values[42] == 3 * unit && !(bool)uf.Param.Values[116], "unit or zero/false mismatch");
            var tol = (double[])uf.Param.Values[37]; Expect(tol[0] == 0.03 * unit && tol[1] == tol[0], "boundary tolerance pair");
            Expect(uf.Param.Events[0] == 0 && uf.Param.Events[1] == 0, "start/end UDEs not cleared");
            int writes = uf.Param.Writes; EngravingPreset.SetUfParameters(uf, op, unit, true); Expect(writes == uf.Param.Writes, "readback wrote state");
            uf.Param.Events[0] = 1; Reject(delegate { EngravingPreset.SetUfParameters(uf, op, unit, true); }, "UdeStartSet"); uf.Param.Events[0] = 0;
            tol[1] = double.PositiveInfinity; Reject(delegate { EngravingPreset.SetUfParameters(uf, op, unit, true); }, "37");
            tol[1] = tol[0]; uf.Param.Values.Remove(37); Reject(delegate { EngravingPreset.SetUfParameters(uf, op, unit, true); }, "37");
            var f = new InheritableToolDepBuilder();
            EngravingPreset.CheckDistance(f, unit, true, false); Expect(f.Value == 50 && f.Intent == ParamValueIntent.ToolDep && f.ValueIntent == ValueIntent.Function && !f.InheritanceStatus, "percent scaled");
            EngravingPreset.CheckDistance(f, unit, false, false); Expect(f.Value == 50 * unit && f.Intent == ParamValueIntent.PartUnits && f.ValueIntent == ValueIntent.PartUnits, "length not scaled");
            EngravingPreset.CheckDistance(f, unit, false, true);
            foreach (bool initial in new bool[] { false, true })
            {
                var move = new NcmPlanarEngRetBuilder();
                move.DiameterBuilder.Value = 72; move.MinRampLengthBuilder.Value = 27; move.HelicalRampAngleBuilder.Value = 12;
                EngravingPreset.CheckEngage(move, unit, initial, false); EngravingPreset.CheckEngage(move, unit, initial, true);
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
                EngravingPreset.CheckFeeds(feeds, unit, reference, false); EngravingPreset.CheckFeeds(feeds, unit, reference, true);
                Expect(feeds.SpindleRpmBuilder.Value == 7000 && feeds.FeedCutBuilder.Value == 350 * unit &&
                    feeds.FeedCutBuilder.Unit == FeedRateUnit.PerMinute && !feeds.FeedCutBuilder.InheritanceStatus, "feed/RPM");
                if (reference) Expect(feeds.Recalculations == 0 && feeds.FeedPerToothBuilder.Value == 0.025 * unit &&
                    feeds.SurfaceSpeedBuilder.Value == (unit == 1 ? 21.991148575128552 : 21.991148575128552 / 0.3048), "reference feed parameters");
                else Expect(feeds.Recalculations == 2 && feeds.SurfaceSpeedBuilder.Value == 84 &&
                    feeds.FeedPerToothBuilder.Value == 350 * unit / 7000 / 4, "replacement tool inherited reference geometry");
                feeds.SpindleRpmBuilder.Value = 6000;
                Reject(delegate { EngravingPreset.CheckFeeds(feeds, unit, reference, true); }, "Spindle RPM");
            }
        }
    }
    public static int Main()
    {
        return ManagedTestHost.Run(delegate { WorkflowCases(); PresetCases(); Console.WriteLine(count + " engraving creation checks passed"); return 0; });
    }
}
