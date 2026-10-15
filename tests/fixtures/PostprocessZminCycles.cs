// API doubles follow NXOpen .NET signatures (including UF out parameters).
// The production reader, traversal and geometry code are appended by the test.
using System;
using System.Collections.Generic;
using NXOpen;
using NXOpen.UF;
using NXOpen.CAM;

namespace NXOpen
{
    public enum Tag { Null = 0, Operation = 1, Tool = 2, Other = 3 }
    public struct Point3d
    {
        public double X, Y, Z;
        public Point3d(double[] p) { X = p[0]; Y = p[1]; Z = p[2]; }
    }
    public struct Vector3d
    {
        public double X, Y, Z;
        public Vector3d(double[] p) { X = p[0]; Y = p[1]; Z = p[2]; }
    }
    public class Part
    {
        public CAMSetup CAMSetup = new CAMSetup();
        public List<CAMFeature> CAMFeatures = new List<CAMFeature>();
    }
}

namespace NXOpen.UF
{
    public class UFSession
    {
        public static UFSession Current;
        public UFParam Param = new UFParam();
        public static UFSession GetUFSession() { return Current; }
    }
    public class UFParam
    {
        public struct IndexAttribute { public string name; }
        public Dictionary<int, string> Names = new Dictionary<int, string>();
        public Dictionary<string, object> Values = new Dictionary<string, object>();
        public void Add(Tag tag, int index, string name, object value)
        { Names[index] = name; Values[tag + ":" + index] = value; }
        public void AskParamAttributes(int index, out IndexAttribute attributes)
        { attributes = new IndexAttribute { name = Names[index] }; }
        public void AskStrValue(Tag tag, int index, out string value)
        { value = (string)Values[tag + ":" + index]; }
        public void AskDoubleValue(Tag tag, int index, out double value)
        { value = (double)Values[tag + ":" + index]; }
    }
}

namespace NXOpen.CAM.FBM
{
    public class Feature
    {
        public Dictionary<string, object> Values = new Dictionary<string, object>();
        public HashSet<string> Overridden = new HashSet<string>();
        public CAMAttribute GetAttribute(string name) { return new CAMAttribute(Values[name]); }
        public bool IsAttributeOverridden(string name) { return Overridden.Contains(name); }
    }
    public class FeatureSet
    {
        public bool CustomPartOffset, CustomStock;
        public Feature LocalFeature;
        public Feature GetFeature() { return LocalFeature; }
    }
    public class FeatureSetList
    {
        public List<FeatureSet> Items = new List<FeatureSet>();
        public int Length { get { return Items.Count; } }
    }
    public class MachiningFeatureGeometry
    {
        public bool UseModelDepth;
        public FeatureSetList GeometryList = new FeatureSetList();
        public FeatureSet GetFeatureSet(int i) { return GeometryList.Items[i]; }
    }
}

namespace NXOpen.CAM
{
    public class CAMAttribute
    {
        private readonly object value;
        public CAMAttribute(object v) { value = v; }
        public double GetDoubleValue() { return (double)value; }
        public int GetIntegerValue() { return (int)value; }
    }
    public class CAMFeature : FBM.Feature
    {
        public string Type = "STEP1HOLE";
        public Operation[] Operations;
        public Operation[] GetOperations() { return Operations; }
    }
    public class NCGroup { public Tag Tag = Tag.Tool; }
    public class CAMObject
    {
        public enum Status { Complete, Approved, Repost, Regen }
        public Tag Tag = Tag.Operation;
    }
    public class Operation : CAMObject
    {
        public Part OwningPart;
        public Path SavedPath;
        public Status CurrentStatus = Status.Complete;
        public NCGroup Tool = new NCGroup();
        public Path GetPath() { return SavedPath; }
        public Status GetStatus() { return CurrentStatus; }
        public NCGroup GetParent(CAMSetup.View view)
        {
            if (view != CAMSetup.View.MachineTool) throw new Exception("wrong parent view");
            return Tool;
        }
    }
    public class HoleDrilling : Operation { }
    public class CAMSetup
    {
        public enum View { MachineTool }
        public OperationCollection CAMOperationCollection = new OperationCollection();
    }
    public class OperationCollection
    {
        public HoleDrillingBuilder Builder;
        public int Creates;
        public HoleDrillingBuilder CreateHoleDrillingBuilder(CAMObject operation)
        { Creates++; return Builder; }
    }
    public class InheritableDouble { public double Value; public bool Status; }
    public class VerticalPosition
    {
        public enum OffsetTypes { Distance, ToolDiameter }
        public OffsetTypes OffsetType = OffsetTypes.Distance;
        public double Distance;
    }
    public class HoleDrillingCutParameters
    {
        public InheritableDouble BottomStock = new InheritableDouble(), PartStock = new InheritableDouble();
        public VerticalPosition BottomOffset = new VerticalPosition();
    }
    public class Cycle
    {
        public enum MotionOutputTypes { MachineCycle, SingleMoves }
        public MotionOutputTypes MotionOutput = MotionOutputTypes.MachineCycle;
        public string CycleType, CycleTrackingPoint = "None";
        public double CsinkDiameter;
    }
    public class OperationBuilder
    {
        public enum HoleDepthTypes { Point, Shoulder }
    }
    public class HoleDrillingBuilder
    {
        public enum ControlPointOffsetType { None, NotSet, Manual }
        public ControlPointOffsetType ControlPointOffset = ControlPointOffsetType.None;
        public OperationBuilder.HoleDepthTypes HoleDepthType = OperationBuilder.HoleDepthTypes.Point;
        public InheritableDouble PredefinedDepth = new InheritableDouble();
        public bool IgnoreToolTip;
        public string DrivePoint = "SYS_CL_TIP";
        public Cycle CycleTable = new Cycle();
        public HoleDrillingCutParameters CutParameters = new HoleDrillingCutParameters();
        public FBM.MachiningFeatureGeometry Geometry = new FBM.MachiningFeatureGeometry();
        public int Destroys;
        public FBM.MachiningFeatureGeometry GetFeatureGeometry() { return Geometry; }
        public string GetToolDrivePoint() { return DrivePoint; }
        public void Destroy() { if (++Destroys != 1) throw new Exception("builder destroyed twice"); }
        public void Commit() { throw new Exception("read-only cycle reader committed a builder"); }
    }
    public enum CamPathMotionType { Rapid, Feed, Cycle }
    public enum CamPathMotionShapeType { Linear, Circular, Helical, Invalid }
    public enum CamPathToolAxisType { Three, Five, Unknown }
    public enum CamPathDir { Clockwise, Counterclockwise, Unknown }
    public class TrackedBuffer : IDisposable
    {
        public bool Disposed;
        public void Dispose() { if (Disposed) throw new Exception("buffer disposed twice"); Disposed = true; }
    }
    public class PathEvent : TrackedBuffer
    {
        public Path Owner;
        public int Index;
        public PathEvent GetNext() { return Owner.Event(Index + 1); }
    }
    public class PathLinearMotion : TrackedBuffer
    {
        public Point3d EndPoint;
        public Vector3d ToolAxis;
    }
    public class PathCircularMotion : PathLinearMotion
    {
        public Point3d ArcCenter;
        public Vector3d ArcAxis;
        public CamPathDir Direction;
    }
    public class PathHelixMotion : PathCircularMotion { public double NumberOfRevolutions; }
    public class PathRecord
    {
        public bool IsMotion = true;
        public CamPathMotionType Type = CamPathMotionType.Feed;
        public CamPathMotionShapeType Shape = CamPathMotionShapeType.Linear;
        public double[] Point, Axis;
        public double[] Center, ArcAxis;
        public CamPathDir Direction;
        public double Revolutions;
    }
    public class Path
    {
        public CamPathToolAxisType ToolAxisType = CamPathToolAxisType.Five;
        public List<PathRecord> Records = new List<PathRecord>();
        public List<TrackedBuffer> Buffers = new List<TrackedBuffer>();
        public bool SubPath;
        public bool HasSubPath() { return SubPath; }
        public int NumberOfToolpathEvents { get { return Records.Count; } }
        public PathEvent Event(int index)
        {
            PathEvent item = new PathEvent { Owner = this, Index = index };
            Buffers.Add(item); return item;
        }
        public PathEvent GetFirstEvent() { return Event(0); }
        public bool IsToolpathEventAMotion(PathEvent e, out CamPathMotionType type, out CamPathMotionShapeType shape)
        { var p = Records[e.Index]; type = p.Type; shape = p.Shape; return p.IsMotion; }
        private T Motion<T>(PathEvent e) where T : PathLinearMotion, new()
        {
            PathRecord p = Records[e.Index];
            T m = new T { EndPoint = new Point3d(p.Point), ToolAxis = new Vector3d(p.Axis) };
            PathCircularMotion arc = m as PathCircularMotion;
            if (arc != null) { arc.ArcCenter = new Point3d(p.Center); arc.ArcAxis = new Vector3d(p.ArcAxis); arc.Direction = p.Direction; }
            PathHelixMotion helix = m as PathHelixMotion;
            if (helix != null) helix.NumberOfRevolutions = p.Revolutions;
            Buffers.Add(m); return m;
        }
        public PathLinearMotion GetLinearMotion(PathEvent e) { return Motion<PathLinearMotion>(e); }
        public PathCircularMotion GetCircularMotion(PathEvent e) { return Motion<PathCircularMotion>(e); }
        public PathHelixMotion GetHelixMotion(PathEvent e) { return Motion<PathHelixMotion>(e); }
    }
}

internal static class CycleChecks
{
    private sealed class Case
    {
        internal HoleDrilling Operation;
        internal HoleDrillingBuilder Builder;
        internal UFParam Parameters;
        internal double[] Origin = new double[] { 0, 0, 0 };
        internal void Released()
        {
            foreach (TrackedBuffer buffer in Operation.SavedPath.Buffers) Check(buffer.Disposed, "path buffer leaked");
            int creates = Operation.OwningPart.CAMSetup.CAMOperationCollection.Creates;
            Check(Builder.Destroys == creates, "builder leaked");
        }
    }
    private static int count;
    private static void Check(bool condition, string label) { count++; if (!condition) throw new Exception(label); }
    private static void Near(double actual, double expected, string label) { Check(Math.Abs(actual - expected) < 1e-8, label + ": " + actual + " != " + expected); }
    private static void Reject(Case c, string fragment)
    {
        bool rejected = false;
        try { OperationZmin.ToolpathMinimum(c.Operation, c.Origin); }
        catch (Exception ex) { rejected = ex.Message.Contains(fragment); if (!rejected) throw new Exception("wrong rejection: " + ex.Message); }
        Check(rejected, "unsupported cycle accepted: " + fragment); c.Released();
    }
    private static void RejectMissingField(Case c)
    {
        bool rejected = false;
        try { OperationZmin.ToolpathMinimum(c.Operation, c.Origin); }
        // .NET Framework omits the key from this exception's message.
        catch (KeyNotFoundException) { rejected = true; }
        Check(rejected, "missing local depth accepted"); c.Released();
    }
    private static void Location(NXOpen.CAM.FBM.Feature feature, double[] point, double[] axis)
    {
        for (int i = 0; i < 3; i++)
        {
            string c = "XYZ"[i].ToString();
            feature.Values[c + "_POSITION"] = point[i]; feature.Values[c + "_ORIENTATION_D"] = -axis[i];
        }
    }
    private static Case Make(bool spot)
    {
        Case c = new Case();
        c.Operation = new HoleDrilling { OwningPart = new Part(), SavedPath = new Path() };
        c.Builder = new HoleDrillingBuilder();
        c.Operation.OwningPart.CAMSetup.CAMOperationCollection.Builder = c.Builder;
        UFSession.Current = new UFSession(); c.Parameters = UFSession.Current.Param;
        double diameter = spot ? 6.0 : 5.0, angle = spot ? Math.PI / 2 : Math.PI * 2 / 3;
        c.Parameters.Add(Tag.Operation, 154, "System Template Subtype", spot ? "SPOT_DRILLING" : "DRILLING");
        c.Parameters.Add(Tag.Operation, 8101, "Feature Type Name", spot ? "NXHOLECENTER" : "NXHOLE");
        c.Parameters.Add(Tag.Tool, 1000, "Tool Diameter", diameter);
        c.Parameters.Add(Tag.Tool, 1016, "Tool Point Angle", angle);
        c.Parameters.Add(Tag.Tool, 1053, "Tool Point Length", diameter / (2 * Math.Tan(angle / 2)));
        int[] extra = new int[] { 1176, 1177, 1181, 1182 };
        string[] names = new string[] { "Tool Tip Diameter", "Tool Tip Length", "Tool Pilot Diameter", "Tool Pilot Length" };
        for (int i = 0; i < extra.Length; i++) c.Parameters.Add(Tag.Tool, extra[i], names[i], 0.0);
        c.Builder.CycleTable.CycleType = spot ? "Drill,Csink" : "Drill,Deep";
        c.Builder.CutParameters.BottomOffset.Distance = spot ? 0.0 : 2.0;
        double[] axis = new double[] { -1, 0, 0 };
        c.Operation.SavedPath.Records.Add(new PathRecord { Type = CamPathMotionType.Rapid, Point = new double[] { -37.5, 20, 10 }, Axis = axis });
        for (int i = 0; i < 2; i++)
        {
            double[] point = new double[] { 0, 20 + i * 100, 10 };
            var feature = new NXOpen.CAM.FBM.Feature();
            Location(feature, point, axis);
            feature.Values["DEPTH"] = spot ? 1.25 : 8.26330541722281;
            feature.Values["TIP_ANGLE"] = angle * 180 / Math.PI;
            feature.Values["DIAMETER_1"] = diameter; feature.Values["SUBTYPE"] = 0;
            c.Builder.Geometry.GeometryList.Items.Add(new NXOpen.CAM.FBM.FeatureSet { LocalFeature = feature });
            CAMFeature master = new CAMFeature { Operations = new Operation[] { c.Operation } };
            Location(master, point, axis); master.Values["DEPTH"] = 900.0; // Deliberately wrong master depth.
            c.Operation.OwningPart.CAMFeatures.Add(master);
            c.Operation.SavedPath.Records.Add(new PathRecord { IsMotion = false });
            c.Operation.SavedPath.Records.Add(new PathRecord { Type = CamPathMotionType.Cycle, Point = point, Axis = axis });
        }
        return c;
    }
    private static double[] Rotate(double[] p, double angle)
    { return new double[] { p[0] * Math.Cos(angle) - p[1] * Math.Sin(angle), p[0] * Math.Sin(angle) + p[1] * Math.Cos(angle), p[2] }; }
    private static double[] Shift(double[] p, double[] offset)
    { return new double[] { p[0] + offset[0], p[1] + offset[1], p[2] + offset[2] }; }
    private static void TransformFeature(NXOpen.CAM.FBM.Feature f, double angle, double[] offset)
    {
        double[] p = new double[3], a = new double[3];
        for (int i = 0; i < 3; i++) { string c = "XYZ"[i].ToString(); p[i] = (double)f.Values[c + "_POSITION"]; a[i] = -(double)f.Values[c + "_ORIENTATION_D"]; }
        Location(f, Shift(Rotate(p, angle), offset), Rotate(a, angle));
    }
    private static void Profiles()
    {
        Case spot = Make(true);
        Near(OperationZmin.ToolpathMinimum(spot.Operation, spot.Origin).Value, -1.25, "spot NC depth"); spot.Released();
        Check(spot.Operation.OwningPart.CAMSetup.CAMOperationCollection.Creates == 1, "cycle reader should be cached per path");
        Case drill = Make(false);
        Near(OperationZmin.ToolpathMinimum(drill.Operation, drill.Origin).Value, -11.706681090196877, "drill NC depth"); drill.Released();
        for (int degrees = -150; degrees <= 150; degrees += 30)
        {
            Case rotated = Make(false); double angle = degrees * Math.PI / 180;
            rotated.Origin = new double[] { 30, -70, 14 };
            foreach (var f in rotated.Builder.Geometry.GeometryList.Items) TransformFeature(f.LocalFeature, angle, rotated.Origin);
            foreach (var f in rotated.Operation.OwningPart.CAMFeatures) TransformFeature(f, angle, rotated.Origin);
            foreach (PathRecord record in rotated.Operation.SavedPath.Records)
                if (record.IsMotion) { record.Point = Shift(Rotate(record.Point, angle), rotated.Origin); record.Axis = Rotate(record.Axis, angle); }
            if (degrees < 0) rotated.Operation.SavedPath.ToolAxisType = CamPathToolAxisType.Three;
            Near(OperationZmin.ToolpathMinimum(rotated.Operation, rotated.Origin).Value, -11.706681090196877, "rotated drill depth"); rotated.Released();
        }
        Case varying = Make(false);
        varying.Builder.Geometry.GeometryList.Items[0].LocalFeature.Values["DEPTH"] = 4.0;
        varying.Builder.Geometry.GeometryList.Items[1].LocalFeature.Values["DEPTH"] = 15.0;
        varying.Builder.Geometry.GeometryList.Items.Reverse(); varying.Operation.OwningPart.CAMFeatures.Reverse();
        Near(OperationZmin.ToolpathMinimum(varying.Operation, varying.Origin).Value, -18.443375672974064, "match each local hole by position"); varying.Released();
        Case noneAlias = Make(true); noneAlias.Builder.ControlPointOffset = HoleDrillingBuilder.ControlPointOffsetType.NotSet;
        noneAlias.Operation.CurrentStatus = CAMObject.Status.Repost;
        Near(OperationZmin.ToolpathMinimum(noneAlias.Operation, noneAlias.Origin).Value, -1.25, "NX enum alias and repost status"); noneAlias.Released();
    }
    private static void Unsupported()
    {
        Case c = Make(false); c.Operation.CurrentStatus = CAMObject.Status.Regen; Reject(c, "\u0430\u043a\u0442\u0443\u0430\u043b\u044c\u043d\u0430\u044f");
        c = Make(false); c.Parameters.Names[154] = "Wrong parameter"; Reject(c, "\u043e\u043f\u0438\u0441\u0430\u043d\u0438\u0435");
        c = Make(false); c.Parameters.Values[Tag.Operation + ":154"] = "TAPPING"; Reject(c, "TAPPING");
        c = Make(false); c.Builder.Geometry.UseModelDepth = true; Reject(c, "Predefined");
        c = Make(false); c.Builder.PredefinedDepth.Status = true; Reject(c, "Predefined");
        c = Make(false); c.Builder.IgnoreToolTip = true; Reject(c, "\u0442\u043e\u0447\u043a\u0443 \u0438\u043d\u0441\u0442\u0440\u0443\u043c\u0435\u043d\u0442\u0430");
        c = Make(false); c.Builder.DrivePoint = "SYS_CL_SHOULDER"; Reject(c, "\u0442\u043e\u0447\u043a\u0443 \u0438\u043d\u0441\u0442\u0440\u0443\u043c\u0435\u043d\u0442\u0430");
        c = Make(false); c.Builder.ControlPointOffset = HoleDrillingBuilder.ControlPointOffsetType.Manual; Reject(c, "\u043a\u043e\u043d\u0442\u0440\u043e\u043b\u044c\u043d\u043e\u0439");
        c = Make(false); c.Builder.HoleDepthType = OperationBuilder.HoleDepthTypes.Shoulder; Reject(c, "\u0441\u043f\u043e\u0441\u043e\u0431");
        c = Make(false); c.Builder.CycleTable.MotionOutput = Cycle.MotionOutputTypes.SingleMoves; Reject(c, "\u0432\u044b\u0432\u043e\u0434\u0430");
        c = Make(false); c.Builder.CycleTable.CycleType = "Drill,Break"; Reject(c, "Drill,Break");
        c = Make(false); c.Builder.CycleTable.CycleTrackingPoint = "Shoulder"; Reject(c, "\u043e\u0442\u0441\u043b\u0435\u0436\u0438\u0432\u0430\u043d\u0438\u044f");
        c = Make(false); c.Builder.CycleTable.CsinkDiameter = 3; Reject(c, "\u0437\u0435\u043d\u043a\u043e\u0432\u043a\u0438");
        c = Make(false); c.Builder.CutParameters.BottomStock.Value = 1; Reject(c, "\u043f\u0440\u0438\u043f\u0443\u0441\u043a\u043e\u043c");
        c = Make(false); c.Builder.CutParameters.PartStock.Value = 1; Reject(c, "\u043f\u0440\u0438\u043f\u0443\u0441\u043a\u043e\u043c");
        c = Make(false); c.Builder.CutParameters.BottomOffset.OffsetType = VerticalPosition.OffsetTypes.ToolDiameter; Reject(c, "\u0440\u0430\u0441\u0441\u0442\u043e\u044f\u043d\u0438\u0435\u043c");
        c = Make(false); c.Builder.CutParameters.BottomOffset.Distance = -1; Reject(c, "\u0441\u043c\u0435\u0449\u0435\u043d\u0438\u0435");
        c = Make(true); c.Builder.CutParameters.BottomOffset.Distance = 1; Reject(c, "\u0441\u043c\u0435\u0449\u0435\u043d\u0438\u0435");
        c = Make(false); c.Operation.Tool = null; Reject(c, "\u0438\u043d\u0441\u0442\u0440\u0443\u043c\u0435\u043d\u0442");
        c = Make(false); c.Parameters.Values[Tag.Tool + ":1000"] = 0.0; Reject(c, "\u0440\u0430\u0437\u043c\u0435\u0440\u044b");
        c = Make(false); c.Parameters.Values[Tag.Tool + ":1053"] = 0.0; Reject(c, "\u043a\u043e\u043d\u0438\u0447\u0435\u0441\u043a\u043e\u043c\u0443");
        c = Make(false); c.Parameters.Values[Tag.Tool + ":1181"] = 1.0; Reject(c, "\u043f\u0438\u043b\u043e\u0442\u043d\u0430\u044f");
        c = Make(false); c.Builder.Geometry.GeometryList.Items.Clear(); Reject(c, "FBM");
        c = Make(false); c.Builder.Geometry.GeometryList.Items[0].CustomPartOffset = true; Reject(c, "\u0418\u043d\u0434\u0438\u0432\u0438\u0434\u0443\u0430\u043b\u044c\u043d\u043e\u0435");
        c = Make(false); c.Builder.Geometry.GeometryList.Items[0].CustomStock = true; Reject(c, "\u0418\u043d\u0434\u0438\u0432\u0438\u0434\u0443\u0430\u043b\u044c\u043d\u043e\u0435");
        c = Make(false); c.Builder.Geometry.GeometryList.Items[0].LocalFeature.Values["DEPTH"] = 0.0; Reject(c, "\u043f\u043e\u043b\u043e\u0436\u0438\u0442\u0435\u043b\u044c\u043d\u044b\u043c");
        c = Make(false); c.Builder.Geometry.GeometryList.Items[0].LocalFeature.Values["DEPTH"] = Double.NaN; Reject(c, "\u043d\u0435\u0447\u0438\u0441\u043b\u043e\u0432\u043e\u0435");
        c = Make(false); c.Builder.Geometry.GeometryList.Items[0].LocalFeature.Values.Remove("DEPTH"); RejectMissingField(c);
        c = Make(false); c.Builder.Geometry.GeometryList.Items[0].LocalFeature.Values["TIP_ANGLE"] = 90.0; Reject(c, "\u0423\u0433\u043e\u043b");
        c = Make(false); c.Operation.OwningPart.CAMFeatures[0].Type = "STEP1POCKET"; Reject(c, "STEP1HOLE");
        c = Make(false); c.Operation.OwningPart.CAMFeatures[0].Operations = new Operation[0]; Reject(c, "STEP1HOLE");
        c = Make(false); c.Operation.OwningPart.CAMFeatures.Add(c.Operation.OwningPart.CAMFeatures[0]); Reject(c, "STEP1HOLE");
        c = Make(false); c.Builder.Geometry.GeometryList.Items[0].LocalFeature.Overridden.Add("SUBTYPE"); Reject(c, "\u0442\u0438\u043f \u043b\u043e\u043a\u0430\u043b\u044c\u043d\u043e\u0433\u043e");
        c = Make(false); c.Builder.Geometry.GeometryList.Items[0].LocalFeature.Values["SUBTYPE"] = 1; Reject(c, "\u0442\u0438\u043f \u043b\u043e\u043a\u0430\u043b\u044c\u043d\u043e\u0433\u043e");
        c = Make(false); c.Builder.Geometry.GeometryList.Items[0].LocalFeature.Values["DIAMETER_1"] = 4.0; Reject(c, "\u0414\u0438\u0430\u043c\u0435\u0442\u0440");
        c = Make(true); c.Builder.Geometry.GeometryList.Items.Add(c.Builder.Geometry.GeometryList.Items[0]); Reject(c, "\u0441\u043e\u043f\u043e\u0441\u0442\u0430\u0432\u0438\u0442\u044c");
        c = Make(true); c.Operation.SavedPath.Records[2].Point = new double[] { 0.1, 20, 10 }; Reject(c, "\u0441\u043e\u043f\u043e\u0441\u0442\u0430\u0432\u0438\u0442\u044c");
        c = Make(true); c.Operation.SavedPath.Records[2].Axis = new double[] { 1, 0, 0 }; Reject(c, "\u0441\u043e\u043f\u043e\u0441\u0442\u0430\u0432\u0438\u0442\u044c");
        c = Make(true); c.Operation.SavedPath.Records[2].Axis = new double[] { 0, 0, 0 }; Reject(c, "\u043d\u0430\u043f\u0440\u0430\u0432\u043b\u0435\u043d\u0438\u0435");
        c = Make(true); c.Operation.SavedPath.Records[2].Shape = CamPathMotionShapeType.Circular;
        c.Operation.SavedPath.Records[2].Center = new double[] { 0, 0, 0 }; c.Operation.SavedPath.Records[2].ArcAxis = new double[] { 0, 0, 1 };
        Reject(c, "\u0444\u043e\u0440\u043c\u0430 \u0434\u0432\u0438\u0436\u0435\u043d\u0438\u044f \u0446\u0438\u043a\u043b\u0430");
        c = Make(true); c.Operation.SavedPath.Records[4].Point = new double[] { 0, 999, 10 }; Reject(c, "\u0441\u043e\u043f\u043e\u0441\u0442\u0430\u0432\u0438\u0442\u044c"); // Earlier correct cycle must not hide failure.
    }
    private static void OrdinaryPaths()
    {
        Case c = Make(false); c.Operation.CurrentStatus = CAMObject.Status.Regen;
        c.Operation.SavedPath.Records.Clear();
        for (int degrees = -150; degrees <= 150; degrees += 30)
        {
            double angle = degrees * Math.PI / 180;
            c.Operation.SavedPath.Records.Add(new PathRecord { Point = Rotate(new double[] { 9, 4, 0 }, angle), Axis = Rotate(new double[] { -1, 0, 0 }, angle) });
        }
        Near(OperationZmin.ToolpathMinimum(c.Operation, c.Origin).Value, -9, "ordinary rotary projection"); c.Released();
        Check(c.Builder.Destroys == 0, "ordinary path must not read cycle geometry");
        c = Make(false); c.Operation.SavedPath.Records.Clear(); c.Operation.SavedPath.ToolAxisType = CamPathToolAxisType.Three;
        c.Operation.SavedPath.Records.Add(new PathRecord { Point = new double[] { 0, 1, 0 }, Axis = new double[] { 1, 0, 0 } });
        c.Operation.SavedPath.Records.Add(new PathRecord { Shape = CamPathMotionShapeType.Circular, Point = new double[] { 0, -1, 0 }, Axis = new double[] { 1, 0, 0 },
            Center = new double[] { 0, 0, 0 }, ArcAxis = new double[] { 0, 0, 1 }, Direction = CamPathDir.Counterclockwise });
        Near(OperationZmin.ToolpathMinimum(c.Operation, c.Origin).Value, -1, "ordinary arc interior minimum"); c.Released();
        c = Make(false); c.Operation.SavedPath.Records.Clear();
        c.Operation.SavedPath.Records.Add(new PathRecord { Point = new double[] { 1, 0, 0 }, Axis = new double[] { 0, 0, 1 } });
        c.Operation.SavedPath.Records.Add(new PathRecord { Shape = CamPathMotionShapeType.Helical, Point = new double[] { 1, 0, -2 }, Axis = new double[] { 0, 0, 1 },
            Center = new double[] { 0, 0, 0 }, ArcAxis = new double[] { 0, 0, 1 }, Direction = CamPathDir.Counterclockwise, Revolutions = 1 });
        Near(OperationZmin.ToolpathMinimum(c.Operation, c.Origin).Value, -2, "ordinary helix"); c.Released();
    }
    public static int Main()
    {
        try { Profiles(); Unsupported(); OrdinaryPaths(); Console.WriteLine("Production C# Zmin cycle checks: " + count + " passed."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
