using System;
using NXOpen;
using NXOpen.UF;
// Contract double for the installed UF boundary API. Native NX integration is a separate check.
namespace NXOpen { public enum Tag { Null, Operation, Line, Arc, Spline } }
namespace NXOpen.UF
{
    public enum CamBoundaryType { CamBoundaryTypeClosed, CamBoundaryTypeOpen }
    public enum CamGeomType { CamPart }
    public enum CamToolPosition { CamToolPositionTanto, CamToolPositionOn }
    public enum CamMaterialSide { CamMaterialSideInLeft }
    public class UFSession { public readonly UFCambnd Cambnd = new UFCambnd(); }
    public class UFCambnd
    {
        public struct AppData
        { public int has_stock,has_tolerances,has_tool_position;public double stock;public double[] tolerances;public CamToolPosition tool_position; }
        public struct BoundaryData
        { public CamBoundaryType boundary_type;public int plane_type;public double[] origin,matrix;public CamMaterialSide material_side;public AppData[] app_data; }
        public BoundaryData Saved;
        public AppData[] Members;
        public Tag[] Tags;
        public void AppendBndFromCurve(Tag operation,CamGeomType type,int count,Tag[] tags,ref BoundaryData boundary,AppData[] members)
        {
            if(operation!=Tag.Operation||count!=tags.Length||count!=members.Length)throw new Exception("invalid API input");
            Saved=boundary;Members=members;Tags=(Tag[])tags.Clone();
        }
        public void AskBoundaryData(IntPtr boundary,out BoundaryData data) { data=Saved; }
        public void AskBoundaryAppData(IntPtr boundary,out AppData data) { data=Saved.app_data[0]; }
        public void AskBoundaryItems(IntPtr boundary,out int count,out IntPtr[] members)
        { count=Members.Length;members=new IntPtr[count];for(int i=0;i<count;i++)members[i]=new IntPtr(i+1); }
        public void AskItemAppData(IntPtr member,out AppData data) { data=Members[member.ToInt32()-1]; }
    }
}
public class EngravingBoundaryChecks
{
    static int checks;
    static void Require(bool value,string message) { if(!value)throw new Exception(message);checks++; }
    static void Reject(Action action,string message)
    { bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,message); }
    public static void Main()
    {
        foreach(double unit in new double[]{1,1/25.4})
        foreach(Tag[] tags in new Tag[][] {new Tag[]{Tag.Line},new Tag[]{Tag.Arc},new Tag[]{Tag.Line,Tag.Spline,Tag.Arc}})
        {
            var uf=new UFSession();double tolerance=.001*unit;
            EngravingCamBoundary.Append(uf,Tag.Operation,tags,new double[]{4,-2,8},new double[]{0,1,0,0,0,1,1,0,0},tolerance);
            Require(uf.Cambnd.Saved.boundary_type==CamBoundaryType.CamBoundaryTypeOpen,"profile mode must be OPEN even for a complete circle");
            Require(uf.Cambnd.Tags.Length==tags.Length,"curve was added/removed to close or split a boundary");
            for(int i=0;i<tags.Length;i++)Require(uf.Cambnd.Tags[i]==tags[i],"curve chain order changed");
            Require(uf.Cambnd.Saved.plane_type==0&&uf.Cambnd.Saved.origin[2]==8&&uf.Cambnd.Saved.matrix[6]==1,"rotated boundary plane changed");
            EngravingCamBoundary.Verify(uf,new IntPtr(100),tolerance);
            var original=uf.Cambnd.Members[0];
            var broken=original;broken.has_tool_position=0;uf.Cambnd.Members[0]=broken;
            Reject(delegate {EngravingCamBoundary.Verify(uf,new IntPtr(100),tolerance);},"inherited tool position accepted");
            broken=original;broken.tool_position=CamToolPosition.CamToolPositionTanto;uf.Cambnd.Members[0]=broken;
            Reject(delegate {EngravingCamBoundary.Verify(uf,new IntPtr(100),tolerance);},"radius offset accepted");
            broken=original;broken.has_tolerances=0;uf.Cambnd.Members[0]=broken;
            Reject(delegate {EngravingCamBoundary.Verify(uf,new IntPtr(100),tolerance);},"inherited tolerance accepted");
            broken=original;broken.tolerances=new double[]{.1*unit,tolerance};uf.Cambnd.Members[0]=broken;
            Reject(delegate {EngravingCamBoundary.Verify(uf,new IntPtr(100),tolerance);},"commit increased tolerance undetected");
            broken=original;broken.stock=double.NaN;uf.Cambnd.Members[0]=broken;
            Reject(delegate {EngravingCamBoundary.Verify(uf,new IntPtr(100),tolerance);},"non-finite stock accepted");
            uf.Cambnd.Members[0]=original;uf.Cambnd.Saved.boundary_type=CamBoundaryType.CamBoundaryTypeClosed;
            Reject(delegate {EngravingCamBoundary.Verify(uf,new IntPtr(100),tolerance);},"area boundary accepted");
            uf.Cambnd.Saved.boundary_type=CamBoundaryType.CamBoundaryTypeOpen;
            var group=uf.Cambnd.Saved.app_data[0];group.has_tolerances=0;uf.Cambnd.Saved.app_data[0]=group;
            Reject(delegate {EngravingCamBoundary.Verify(uf,new IntPtr(100),tolerance);},"inherited boundary tolerance accepted");
        }
        Console.WriteLine(checks+" engraving-boundary contract checks passed");
    }
}
