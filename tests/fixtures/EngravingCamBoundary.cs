using System;
using System.Collections.Generic;
using NXOpen;
using NXOpen.CAM;
// Contract double for the NXOpen boundary builders; not a native NX runtime test.
namespace NXOpen
{
    public enum Tag { Null,Line,Arc,Spline,Old }
    public class TaggedObject { public Tag Tag; }
    public class NXObject:TaggedObject { }
    public class Plane { }
    public struct Point3d { public double X,Y,Z; }
    public struct Matrix3x3 { public double Xx,Yy,Zz; }
}
namespace NXOpen.CAM
{
    public class BoundaryMemberSet { public TaggedObject Geometry; }
    public class BoundaryMemberSetPlanarMill:BoundaryMemberSet
    {
        public bool CustomStock,CustomTolerance=true;public double Stock;public int ToolPosition;
        public double InTolerance { get{throw new Exception("member tolerance read");} set{throw new Exception("member tolerance write");} }
        public double OutTolerance { get{throw new Exception("member tolerance read");} set{throw new Exception("member tolerance write");} }
    }
    public class BoundaryMemberSetList
    { public List<BoundaryMemberSet> Items=new List<BoundaryMemberSet>();public BoundaryMemberSet[] GetContents(){return Items.ToArray();} }
    public class BoundarySet
    {
        public enum BoundaryTypes { Closed,Open }
        public enum PlaneTypes { Automatic,UserDefined }
        public enum ToolPositionTypes { On,Contact }
        public enum ToolSideTypes { InsideOrLeft,OutsideOrRight }
        public BoundaryTypes BoundaryType;public PlaneTypes PlaneType;public ToolPositionTypes ToolPosition;public ToolSideTypes ToolSide;
        public Plane Plane;public BoundaryMemberSetList BoundaryMemberList=new BoundaryMemberSetList();
    }
    public class BoundarySetPlanarMill:BoundarySet
    {
        public bool CustomStock,CustomTolerance=true,ThrowAppend,InferClosed;public double Stock;
        public BoundaryTypes TypeBeforeAppend;
        public double InTolerance { get{throw new Exception("boundary tolerance read");} set{throw new Exception("boundary tolerance write");} }
        public double OutTolerance { get{throw new Exception("boundary tolerance read");} set{throw new Exception("boundary tolerance write");} }
        public void AppendCurves(NXObject[] curves,Point3d point,Matrix3x3 matrix)
        {
            if(ThrowAppend)throw new InvalidOperationException("synthetic append failure");
            TypeBeforeAppend=BoundaryType;
            BoundaryType=InferClosed?BoundaryTypes.Closed:BoundaryTypes.Open;ToolPosition=ToolPositionTypes.Contact;
            foreach(NXObject c in curves)BoundaryMemberList.Items.Add(new BoundaryMemberSetPlanarMill{Geometry=c,ToolPosition=17,Stock=4});
        }
    }
    public class BoundarySetList
    { public List<BoundarySet> Items=new List<BoundarySet>();public void Append(BoundarySet set){Items.Add(set);} }
    public class BoundaryPlanarMill
    {
        public enum ToolPositionTypes { On,TangentTo }
        public ToolPositionTypes InitialToolPosition=ToolPositionTypes.TangentTo;
        public BoundarySetList BoundaryList=new BoundarySetList();public bool ThrowAppend,InferClosed;
        public BoundarySetPlanarMill CreateBoundarySetPlanarMill(){return new BoundarySetPlanarMill{ThrowAppend=ThrowAppend,InferClosed=InferClosed};}
    }
}
public class EngravingBoundaryChecks
{
    static int checks;
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
    static void Reject(Action action,string message){bool no=false;try{action();}catch(InvalidOperationException){no=true;}Require(no,message);}
    public static void Main()
    {
        foreach(double unit in new double[]{1,1/25.4})
        foreach(bool closed in new bool[]{false,true})foreach(bool inferredClosed in new bool[]{false,true})
        foreach(Tag[] tags in new Tag[][]{new Tag[]{Tag.Line},new Tag[]{Tag.Arc},new Tag[]{Tag.Line,Tag.Spline,Tag.Arc}})
        {
            var target=new BoundaryPlanarMill{InferClosed=inferredClosed};var old=new BoundarySetPlanarMill{Stock=5,BoundaryType=BoundarySet.BoundaryTypes.Closed};target.BoundaryList.Append(old);
            var objects=new NXObject[tags.Length];for(int i=0;i<tags.Length;i++)objects[i]=new NXObject{Tag=tags[i]};
            var plane=new Plane();
            var set=EngravingCamBoundary.Append(target,objects,new Point3d{Z=8*unit},new Matrix3x3{Xx=1,Yy=1,Zz=1},plane,closed);
            var expected=closed?BoundarySet.BoundaryTypes.Closed:BoundarySet.BoundaryTypes.Open;
            Require(target.BoundaryList.Items.Count==2&&target.BoundaryList.Items[0]==old&&old.Stock==5&&old.CustomTolerance,"existing boundary changed");
            Require(target.InitialToolPosition==BoundaryPlanarMill.ToolPositionTypes.TangentTo,"initial defaults not restored");
            Require(set.PlaneType==BoundarySet.PlaneTypes.UserDefined&&set.Plane==plane,"plane lost");
            Require(set.TypeBeforeAppend==expected,"source closure was not set before appending curves");
            Require(set.BoundaryType==expected,"append inference overwrote the source closure type");
            var read=EngravingCamBoundary.Tags(set);Require(read.Length==tags.Length,"member count changed");
            for(int i=0;i<read.Length;i++)Require(read[i]==tags[i],"members reordered or closing member inserted");
            Require(!set.CustomTolerance,"boundary does not inherit operation tolerance");
            foreach(BoundaryMemberSetPlanarMill m in set.BoundaryMemberList.Items)Require(!m.CustomTolerance,"member does not inherit operation tolerance");
            EngravingCamBoundary.Verify(set,closed);checks++;
            // Simulate either saved-state mismatch: a closed ring downgraded to Open,
            // or an open engraving stroke silently closed by NX.
            set.BoundaryType=closed?BoundarySet.BoundaryTypes.Open:BoundarySet.BoundaryTypes.Closed;
            Reject(delegate{EngravingCamBoundary.Verify(set,closed);},"saved boundary closure differs from source but was accepted");
            set.BoundaryType=expected;EngravingCamBoundary.Verify(set,closed);checks++;
            var member=(BoundaryMemberSetPlanarMill)set.BoundaryMemberList.Items[0];
            Require(member.ToolPosition==0,"NXOpen member ON is not zero");
            member.ToolPosition=1;Reject(delegate{EngravingCamBoundary.Verify(set,closed);},"UF integer convention silently accepted");member.ToolPosition=0;
            set.Stock=.1*unit;Reject(delegate{EngravingCamBoundary.Verify(set,closed);},"boundary stock accepted");set.Stock=0;
            member.Stock=.1*unit;Reject(delegate{EngravingCamBoundary.Verify(set,closed);},"member stock accepted");member.Stock=0;
            member.Stock=double.NaN;Reject(delegate{EngravingCamBoundary.Verify(set,closed);},"non-finite stock accepted");member.Stock=0;
            member.Geometry=null;Reject(delegate{EngravingCamBoundary.Tags(set);},"implicit member without source geometry accepted");
            Require(EngravingCamBoundary.Tags(set,true)[0]==Tag.Null,"existing virtual member cannot be compared");
        }
        foreach(bool closed in new bool[]{false,true})
        {
            var fail=new BoundaryPlanarMill{ThrowAppend=true};
            Reject(delegate{EngravingCamBoundary.Append(fail,new NXObject[]{new NXObject()},new Point3d(),new Matrix3x3(),new Plane(),closed);},"append exception swallowed");
            Require(fail.InitialToolPosition==BoundaryPlanarMill.ToolPositionTypes.TangentTo&&fail.BoundaryList.Items.Count==0,"append failure changed defaults or list");
        }
        Console.WriteLine(checks+" engraving-boundary contract checks passed");
    }
}
