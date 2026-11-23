using System;
using System.Collections.Generic;
using NXOpen;
using NXOpen.UF;

// Synthetic CAM data for exercising the journal's geometry and event readers outside NX.
internal sealed partial class GeometryFixture
{
    private readonly FakeSession uf = new FakeSession();
    private readonly EngravingReport report;
    private readonly EngravingTree Tree;
    private readonly Dictionary<int, UFParam.IndexAttribute> attributes = new Dictionary<int, UFParam.IndexAttribute>();

    private GeometryFixture(EngravingReport report)
    {
        this.report = report;
        Tree = new EngravingTree(report, delegate(object item) { return item is Tag ? TagReference((Tag)item) : null; });
    }
    private string TagReference(Tag tag) { return "Tag=" + (uint)tag; }
    private string Reference(CAMObject obj) { return obj.Name; }
    internal static void Read(EngravingReport report, bool faults)
    {
        var fixture = new GeometryFixture(report);
        fixture.uf.Cam.Faults = fixture.uf.Cambnd.Faults = fixture.uf.Camgeom.Faults = fixture.uf.Ude.Faults = faults;
        var op = new CamOperation();
        fixture.uf.Param.Faults = faults;
        fixture.Parameters(op, true);
        fixture.Geometry(op);
        fixture.Events(Tag.Sample);
    }
    private sealed class FakeSession
    {
        internal readonly UFCamgeom Camgeom = new UFCamgeom();
        internal readonly UFCam Cam = new UFCam();
        internal readonly UFCambnd Cambnd = new UFCambnd();
        internal readonly FakeParam Param = new FakeParam();
        internal readonly UFUde Ude = new UFUde();
    }
}

internal class CAMObject
{
    public Tag Tag { get { return Tag.Sample; } }
    public string Name { get { return "fixture"; } }
    public FakePart OwningPart = new FakePart();
}
internal class FakePart { public string PartUnits = "Inches"; }
internal class CamOperation : CAMObject { }

namespace NXOpen.UF
{
    internal enum CamGeomType { CamPart, CamBlank, CamCheck, CamTrim }
    internal enum ParamClrplaneStatus { ParamClrplaneUndefined, ParamClrplaneDefineAndInactive, ParamClrplaneDefineAndActive }
    internal enum ParamLwplaneStatus { ParamLwplaneStatusUndefined, ParamLwplaneStatusDefinedAndActive, ParamLwplaneStatusDefinedAndInactive }
    internal enum ParamClrplaneUsage { ParamClrplaneUsageAtOperationStartAndEnd }
    internal enum ParamLwplaneUsage { ParamOutputWarningOnly }
    internal enum UdeSetType { UdeStartSet, UdeEndSet }

    internal class FixtureException : Exception
    {
        internal FixtureException(string message) : base(message) { }
        public int ErrorCode { get { return 12345; } }
    }

    internal class UFCamgeom
    {
        internal bool Faults;
        internal enum CustomPointType { PredrillEngageType, CutRegionStartType }
        internal struct CustomPoint { public Tag point_tag; public double[] point_acs; public double depth; }
        internal struct AppData { public double stock; }
        internal void AskCustomPoints(Tag op, CustomPointType type, out int count, out CustomPoint[] points)
        {
            if (op != Tag.Sample) throw new Exception("wrong custom point owner");
            points = type == CustomPointType.CutRegionStartType ? null : new[] {
                new CustomPoint { point_tag = Tag.Null, point_acs = new[] { 0.0, 1.2345678901234567, -2.5 }, depth = 0.0 } };
            count = points == null ? 0 : points.Length;
            if (Faults && type == CustomPointType.PredrillEngageType) count++;
        }
        internal void AskGeomProvider(Tag op, CamGeomType kind, out Tag provider)
        {
            if (Faults && kind == CamGeomType.CamBlank) throw new FixtureException("provider unavailable");
            provider = kind == CamGeomType.CamTrim ? Tag.Null : kind == CamGeomType.CamBlank ? op : (Tag)7;
        }
        internal void AskItems(Tag owner, CamGeomType kind, out int count, out IntPtr[] items)
        { count = 0; items = null; }
        internal void AskItemEntity(IntPtr item, out Tag entity) { throw new Exception("unexpected item"); }
        internal void AskItemAppData(IntPtr item, out AppData data) { throw new Exception("unexpected item"); }
    }
    internal class UFCam
    {
        internal bool Faults;
        internal void AskClearPlaneStatus(Tag op, out ParamClrplaneStatus status) { status = ParamClrplaneStatus.ParamClrplaneDefineAndInactive; }
        internal void AskClearPlaneUsage(Tag op, out ParamClrplaneUsage usage) { usage = ParamClrplaneUsage.ParamClrplaneUsageAtOperationStartAndEnd; }
        internal void AskClearPlaneData(Tag op, double[] origin, double[] normal)
        {
            if (Faults) throw new FixtureException("clearance data unavailable");
            origin[1] = -8.75; normal[2] = 1;
        }
        internal void AskLowerLimitPlaneStatus(Tag op, out ParamLwplaneStatus status)
        { status = Faults ? ParamLwplaneStatus.ParamLwplaneStatusDefinedAndActive : ParamLwplaneStatus.ParamLwplaneStatusUndefined; }
        internal void AskLowerLimitPlaneUsage(Tag op, out ParamLwplaneUsage usage) { usage = ParamLwplaneUsage.ParamOutputWarningOnly; }
        internal void AskLowerLimitPlaneData(Tag op, double[] origin, double[] normal)
        {
            if (!Faults) throw new Exception("undefined plane data requested");
            origin[2] = -0.125; normal[0] = 1;
        }
    }
    internal class UFCambnd
    {
        internal bool Faults;
        internal struct BoundaryData { public int boundary_type; }
        internal struct AppData { public int has_stock; public double stock; }
        internal void IsInherited(Tag op, CamGeomType kind, out bool inherited) { inherited = kind == CamGeomType.CamPart; }
        internal void AskBoundaries(Tag owner, CamGeomType kind, out int count, out IntPtr[] curves)
        {
            curves = null;
            if (kind == CamGeomType.CamPart && owner == (Tag)7) curves = new[] { new IntPtr(7) };
            if (kind == CamGeomType.CamBlank && owner == Tag.Sample) curves = new[] { new IntPtr(11), new IntPtr(12) };
            count = curves == null ? 0 : curves.Length;
        }
        internal void AskBoundaryData(IntPtr curve, out BoundaryData data)
        {
            if (Faults && curve.ToInt32() == 11) throw new FixtureException("boundary definition unavailable");
            data = new BoundaryData { boundary_type = 2 };
        }
        internal void AskBoundaryAppData(IntPtr curve, out AppData data) { data = new AppData { has_stock = 1, stock = 0.0 }; }
        internal void AskBoundaryItems(IntPtr curve, out int count, out IntPtr[] items)
        { count = 1; items = new[] { new IntPtr(curve.ToInt32() * 10) }; }
        internal void AskItemEntity(IntPtr member, out Tag tag) { tag = (Tag)member.ToInt32(); }
        internal void AskItemAppData(IntPtr member, out AppData data)
        {
            if (Faults && member.ToInt32() == 110) throw new FixtureException("member settings unavailable");
            data = new AppData();
        }
    }
    internal class UFConstants { public const int UF_PARAM_SAMPLE = 106; }
    internal class FakeParam : UFParam
    {
        internal bool Faults;
        internal void AskRequiredParams(Tag op, out int count, out int[] ids) { ids = new[] { 105, 103, 105 }; count = ids.Length; }
        internal void AskInheritedParams(Tag op, out int count, int[] ids) { ids[0] = 104; count = 1; }
        internal void AskParamAttributes(int id, out IndexAttribute data)
        {
            data = new IndexAttribute { name = "UF " + id,
                type = id == 103 ? Type.TypeDoubleLength : id == 104 ? Type.TypeLogical : Type.TypeString };
        }
        internal void AskParamStatus(Tag op, int id, out Status status)
        { status = id == 104 ? Status.Inherited : id == 105 ? Status.Overridden : Status.Default; }
        internal void AskParamDefiner(Tag op, int id, out Tag tag)
        {
            if (Faults && id == 104) throw new FixtureException("definer unavailable");
            tag = id == 104 ? (Tag)7 : op;
        }
        internal void AskUdes(Tag op, UdeSetType kind, out int count, out IntPtr[] events)
        {
            events = kind == UdeSetType.UdeStartSet ? new[] { new IntPtr(1), new IntPtr(2) } : null;
            count = events == null ? 0 : events.Length;
        }
    }
    internal class UFUde
    {
        internal bool Faults;
        internal enum ParamType { ParamTypeInt, ParamTypeDouble, ParamTypeString, ParamTypeBoolean, ParamTypeOption, ParamTypePoint, ParamTypeVector }
        internal enum ParamToggle { ParamInactive }
        internal void AskName(IntPtr item, out string name) { name = "same name"; }
        internal void AskParams(IntPtr item, out int count, out string[] names)
        {
            if (Faults && item.ToInt32() == 1) throw new FixtureException("UDE list unavailable");
            names = item.ToInt32() == 2 ? new[] { "enabled", "label" } : null;
            count = names == null ? 0 : names.Length;
        }
        internal void AskParamType(IntPtr item, string key, out ParamType type) { type = key == "enabled" ? ParamType.ParamTypeBoolean : ParamType.ParamTypeString; }
        internal void IsParamOptional(IntPtr item, string key, ref bool optional) { optional = key == "enabled"; }
        internal void AskParamToggle(IntPtr item, string key, out ParamToggle toggle) { toggle = ParamToggle.ParamInactive; }
        internal void AskBoolean(IntPtr item, string key, out bool value)
        {
            if (Faults) throw new FixtureException("toggle value unavailable");
            value = false;
        }
        internal void AskString(IntPtr item, string key, out string value) { value = ""; }
        internal void AskInteger(IntPtr item, string key, out int value) { throw new Exception("unexpected integer"); }
        internal void AskDouble(IntPtr item, string key, out double value) { throw new Exception("unexpected double"); }
        internal void AskPoint(IntPtr item, string key, out Tag value) { throw new Exception("unexpected point"); }
        internal void AskVector(IntPtr item, string key, out Tag value) { throw new Exception("unexpected vector"); }
    }
}
