using System;
using System.Collections.Generic;
using System.Globalization;
using NXOpen;
using NXOpen.CAM;

namespace NXOpen
{
    public enum Tag { Null = 0 }
    public class NXObject { public Tag Tag; public NXObject(int tag) { Tag = (Tag)tag; } }
    public struct Point3d { }
    public struct Matrix3x3 { }
}
namespace NXOpen.CAM
{
    public class BoundaryMemberSet { public NXObject Geometry; }
    public class BoundaryMemberSetPlanarMill : BoundaryMemberSet
    {
        public int ToolPosition = 8;
        public bool CustomStock = true, CustomTolerance = true;
        public double Stock = 0.25;
    }
    public class BoundaryMemberSetList : List<BoundaryMemberSet>
    { public BoundaryMemberSet[] GetContents() { return ToArray(); } }
    public class BoundarySetList : List<BoundarySet>
    {
        public BoundarySet[] GetContents() { return ToArray(); }
        public void Append(BoundarySet set) { Add(set); }
    }
    public class BoundarySet
    {
        public enum BoundaryTypes { Closed, Open }
        public enum ToolPositionTypes { On, Contact }
        public enum ToolSideTypes { InsideOrLeft, OutsideOrRight }
        public enum PlaneTypes { Automatic, UserDefined }
        public BoundaryTypes BoundaryType;
        public ToolPositionTypes ToolPosition;
        public ToolSideTypes ToolSide;
        public PlaneTypes PlaneType;
        public bool CustomOffset;
        public double Offset;
        public readonly BoundaryMemberSetList BoundaryMemberList = new BoundaryMemberSetList();
    }
    public class BoundarySetPlanarMill : BoundarySet
    {
        public bool CustomStock = true, CustomTolerance = true;
        public double Stock = 0.5;
        internal BoundaryPlanarMill Owner;
        public void AppendCurves(NXObject[] curves, Point3d point, Matrix3x3 matrix)
        {
            Owner.Calls.Add(curves);
            Owner.SeenInitial.Add(Owner.InitialToolPosition);
            if (Owner.FailAppend) throw new InvalidOperationException("Injected NX append failure");
            // Simulate NX inference overwriting options when geometry is appended.
            BoundaryType = BoundaryTypes.Closed;
            ToolPosition = ToolPositionTypes.Contact;
            ToolSide = ToolSideTypes.OutsideOrRight;
            foreach (NXObject curve in curves)
                BoundaryMemberList.Add(new BoundaryMemberSetPlanarMill { Geometry = Owner.WrongGeometry ? new NXObject(999) : curve });
            if (Owner.ExtraMember) BoundaryMemberList.Add(new BoundaryMemberSetPlanarMill { Geometry = curves[0] });
        }
    }
    public class BoundaryPlanarMill
    {
        public enum ToolPositionTypes { On, TangentTo }
        public ToolPositionTypes InitialToolPosition = ToolPositionTypes.TangentTo;
        public bool FailAppend, ExtraMember, WrongGeometry;
        public readonly BoundarySetList BoundaryList = new BoundarySetList();
        public readonly List<NXObject[]> Calls = new List<NXObject[]>();
        public readonly List<ToolPositionTypes> SeenInitial = new List<ToolPositionTypes>();
        public BoundarySetPlanarMill CreateBoundarySetPlanarMill() { return new BoundarySetPlanarMill { Owner = this }; }
    }
}

public class SeparatePlanarBoundaryChecks
{
    static int checks;
    static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, message);
    }
    public static void Main()
    {
        for (int c = 0; c < 2; c++) for (int t = 0; t < 2; t++) for (int s = 0; s < 2; s++)
        {
            bool closed = c != 0, tanto = t != 0, outside = s != 0;
            BoundaryPlanarMill target = new BoundaryPlanarMill();
            // An unrelated old boundary has deliberately different settings.
            BoundarySetPlanarMill old = target.CreateBoundarySetPlanarMill();
            old.BoundaryMemberList.Add(new BoundaryMemberSetPlanarMill { Geometry = new NXObject(91) });
            target.BoundaryList.Add(old);
            string oldState = SeparatePlanarBoundary.State(old, null);
            NXObject a = new NXObject(10), b = new NXObject(11);
            BoundarySetPlanarMill addedA = SeparatePlanarBoundary.Append(target, a, new Point3d(), new Matrix3x3(), closed, tanto, outside);
            BoundarySetPlanarMill addedB = SeparatePlanarBoundary.Append(target, b, new Point3d(), new Matrix3x3(), closed, tanto, outside);
            Check(target.BoundaryList.Count == 3 && !Object.ReferenceEquals(addedA, addedB), "one separate set per input curve");
            Check(target.Calls.Count == 2 && target.Calls[0].Length == 1 && target.Calls[1].Length == 1, "no combined-chain append");
            Check(SeparatePlanarBoundary.State(old, null) == oldState, "old boundary settings untouched");
            Check(target.InitialToolPosition == BoundaryPlanarMill.ToolPositionTypes.TangentTo, "initial preference restored");
            Check(target.SeenInitial[0] == (tanto ? BoundaryPlanarMill.ToolPositionTypes.TangentTo : BoundaryPlanarMill.ToolPositionTypes.On), "member creation uses selected position");
            Check(addedA.BoundaryType == (closed ? BoundarySet.BoundaryTypes.Closed : BoundarySet.BoundaryTypes.Open), "closed circle remains closed; open curve remains open");
            SeparatePlanarBoundary.Verify(addedA, a.Tag, closed, tanto, outside);
            BoundaryMemberSetPlanarMill member = (BoundaryMemberSetPlanarMill)addedA.BoundaryMemberList[0];
            Check(member.ToolPosition == (tanto ? 1 : 0), "correct member enum independent of UF");
            HashSet<Tag> assigned = new HashSet<Tag>();
            List<string> snapshot = SeparatePlanarBoundary.Snapshot(target, assigned);
            Check(snapshot.Count == 3 && assigned.SetEquals(new Tag[] { (Tag)91, a.Tag, b.Tag }), "snapshot includes existing and new source identities");
            member.ToolPosition = tanto ? 0 : 1;
            Reject(delegate { SeparatePlanarBoundary.Verify(addedA, a.Tag, closed, tanto, outside); }, "reject a saved member with wrong position");
            member.ToolPosition = tanto ? 1 : 0;
            addedA.BoundaryType = closed ? BoundarySet.BoundaryTypes.Open : BoundarySet.BoundaryTypes.Closed;
            Reject(delegate { SeparatePlanarBoundary.Verify(addedA, a.Tag, closed, tanto, outside); }, "reject a saved wrong closure flag");
            addedA.BoundaryType = closed ? BoundarySet.BoundaryTypes.Closed : BoundarySet.BoundaryTypes.Open;
            member.Geometry = b;
            Reject(delegate { SeparatePlanarBoundary.Verify(addedA, a.Tag, closed, tanto, outside); }, "reject a different saved curve");
            member.Geometry = a;
            member.CustomStock = true;
            Reject(delegate { SeparatePlanarBoundary.Verify(addedA, a.Tag, closed, tanto, outside); }, "reject unintended stock override");
            member.CustomStock = false;
            if (tanto)
            {
                addedA.ToolSide = outside ? BoundarySet.ToolSideTypes.InsideOrLeft : BoundarySet.ToolSideTypes.OutsideOrRight;
                Reject(delegate { SeparatePlanarBoundary.Verify(addedA, a.Tag, closed, tanto, outside); }, "reject wrong TANTO side");
            }
        }
        BoundaryPlanarMill failure = new BoundaryPlanarMill { FailAppend = true };
        Reject(delegate { SeparatePlanarBoundary.Append(failure, new NXObject(1), new Point3d(), new Matrix3x3(), false, false, false); }, "append failure propagates to transaction rollback");
        Check(failure.InitialToolPosition == BoundaryPlanarMill.ToolPositionTypes.TangentTo, "initial preference restored after append failure");
        Check(failure.BoundaryList.Count == 0, "failed append not published");
        failure = new BoundaryPlanarMill { ExtraMember = true };
        Reject(delegate { SeparatePlanarBoundary.Append(failure, new NXObject(1), new Point3d(), new Matrix3x3(), false, false, false); }, "split or extra member rejected");
        Check(failure.BoundaryList.Count == 0, "split curve not published");
        failure = new BoundaryPlanarMill { WrongGeometry = true };
        Reject(delegate { SeparatePlanarBoundary.Append(failure, new NXObject(1), new Point3d(), new Matrix3x3(), false, false, false); }, "wrong entity rejected before Commit");
        Console.WriteLine("Separate planar boundary checks passed: " + checks);
    }
}
