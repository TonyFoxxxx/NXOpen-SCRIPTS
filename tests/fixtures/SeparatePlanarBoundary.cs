using System;
using System.Collections.Generic;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;

// Models the public API contract only. The native store and detached editor rows are separate.
namespace NXOpen
{
    public enum Tag { Null = 0 }
    public class TaggedObject { public Tag Tag; }
    public class NXObject : TaggedObject { public string Name; public Part OwningPart; public bool IsOccurrence; }
    public class Curve : NXObject { public bool Closed; public double GetLength() { return 10.0; } }
    public class Line : Curve {} public class Arc : Curve {} public class Spline : Curve {} public class Ellipse : Curve {}
    public class NXException : Exception { public int ErrorCode; }
    public class Part : NXObject { public CAMSetup CAMSetup = new CAMSetup(); }
    public class PartList { public Part Work; }
    public class ListingWindow { public void Open() {} public void WriteLine(string line) { throw new Exception(line); } }
    public class Session
    {
        public enum LibraryUnloadOption { Immediately } public enum MarkVisibility { Visible } public struct UndoMarkId {}
        public PartList Parts = new PartList(); public ListingWindow ListingWindow = new ListingWindow();
        public static Session GetSession() { return Model.Session; }
        public UndoMarkId SetUndoMark(MarkVisibility visibility, string name)
        { Model.Marks++; Model.SaveUndo(); return new UndoMarkId(); }
        public void UndoToMark(UndoMarkId mark,string name) { Model.Undos++; Model.RestoreUndo(); }
        public void DeleteUndoMark(UndoMarkId mark,string name) {}
    }
    public class Selection
    {
        public struct MaskTriple { public MaskTriple(int a,int b,int c) {} }
        public enum Response { OK, Cancel, Back } public enum SelectionScope { WorkPart }
        public enum SelectionAction { ClearAndEnableSpecific }
        public Response SelectTaggedObjects(string prompt,string title,SelectionScope scope,SelectionAction action,
            bool features,bool highlight,MaskTriple[] mask,out TaggedObject[] objects)
        { objects=Model.Selection.ToArray(); return Response.OK; }
    }
    public class UI { public Selection SelectionManager=new Selection(); public static UI GetUI() { return new UI(); } }
}
namespace NXOpen.Utilities
{
    public static class NXObjectManager
    {
        public static NXObject Get(Tag tag) { return tag==Model.Op.Tag ? (NXObject)Model.Op : Model.Curves[tag]; }
    }
}
namespace NXOpen.CAM
{
    public class CAMSetup { public enum View { Geometry } public Collection CAMOperationCollection=new Collection(); }
    public class NCGroup : NXObject { public NCGroup GetParent() { return null; } }
    public class Operation : NXObject { public NCGroup GetParent(CAMSetup.View view) { return null; } }
    public class PlanarMilling : Operation {}
    public class Collection { public PlanarMillingBuilder CreatePlanarMillingBuilder(PlanarMilling op) { return new PlanarMillingBuilder(); } }
    public class BoundaryMemberSet { public NXObject Geometry; }
    public class BoundaryMemberSetPlanarMill : BoundaryMemberSet { public int ToolPosition; public bool CustomStock, CustomTolerance; }
    public class MemberList : List<BoundaryMemberSet> { public BoundaryMemberSet[] GetContents() { return ToArray(); } }
    public class BoundarySet
    {
        public enum BoundaryTypes { Closed, Open } public enum ToolPositionTypes { On, Contact }
        public enum ToolSideTypes { InsideOrLeft, OutsideOrRight } public enum PlaneTypes { Automatic, UserDefined }
        public BoundaryTypes BoundaryType; public ToolPositionTypes ToolPosition; public ToolSideTypes ToolSide;
        public PlaneTypes PlaneType; public bool CustomOffset; public double Offset; public MemberList BoundaryMemberList=new MemberList();
    }
    public class BoundarySetPlanarMill : BoundarySet { public bool CustomStock, CustomTolerance; internal Record Saved; }
    public class RowList : List<BoundarySet> { public BoundarySet[] GetContents() { return ToArray(); } }
    public class BoundaryPlanarMill { public RowList BoundaryList=new RowList(); }
    public class PlanarMillingBuilder
    {
        public BoundaryPlanarMill PartBoundary=new BoundaryPlanarMill();
        public PlanarMillingBuilder()
        {
            foreach(Record record in Model.Records) PartBoundary.BoundaryList.Add(record.Editor());
            if(Model.Records.Count==0 || Model.KeepPlaceholder)
            {
                var blank=new BoundarySetPlanarMill();
                if(Model.NullPlaceholderMember) blank.BoundaryMemberList.Add(new BoundaryMemberSetPlanarMill());
                PartBoundary.BoundaryList.Add(blank);
            }
        }
        public void Commit()
        {
            Model.Commits++;
            foreach(BoundarySetPlanarMill row in PartBoundary.BoundaryList)
                if(row.Saved!=null) row.Saved.Apply(row);
            if(Model.DropOnCommit) Model.Records.RemoveAt(Model.Records.Count-1);
        }
        public void Destroy() {}
    }
}
namespace NXOpen.UF
{
    public enum CamGeomType { CamPart }
    public enum CamBoundaryType { CamBoundaryTypeClosed, CamBoundaryTypeOpen }
    public enum CamMaterialSide { CamMaterialSideUndefined, CamMaterialSideInLeft, CamMaterialSideOutRight }
    public enum CamToolPosition { CamToolPositionUndefined, CamToolPositionOn, CamToolPositionTanto }
    public class UFConstants { public const int UF_line_type=3, UF_circle_type=5, UF_conic_type=6, UF_spline_type=9; }
    public class UFUi { public IntPtr GetDefaultParent(){ return IntPtr.Zero; } public void SetStatus(string s){} }
    public class UFUiOnt
    {
        public void AskSelectedNodes(out int count,out Tag[] result) { count=1; result=new[]{Model.Op.Tag}; }
        public void Refresh() {}
    }
    public class UFModl { public int AskCurveClosed(Tag t) { return Model.Curves[t].Closed ? 1 : 0; } }
    public class UFSession
    {
        public UFUi Ui=new UFUi(); public UFUiOnt UiOnt=new UFUiOnt(); public UFModl Modl=new UFModl(); public UFCambnd Cambnd=new UFCambnd();
        public static UFSession GetUFSession(){return Model.Uf;}
    }
    public class UFCambnd
    {
        public struct AppData { public int has_stock,has_tolerances,has_feedrate,has_blank_distance,has_tool_position,feedrate_unit;
            public double stock,feedrate_value,blank_distance; public double[] tolerances; public CamToolPosition tool_position; }
        public struct BoundaryData { public CamBoundaryType boundary_type; public int plane_type,ignore_holes,ignore_islands,ignore_chamfers;
            public double[] origin,matrix; public CamMaterialSide material_side; public AppData[] app_data; }
        public void IsInherited(Tag operation,CamGeomType type,out bool inherited) { inherited=Model.Inherited; }
        public void AskBoundaries(Tag operation,CamGeomType type,out int count,out IntPtr[] boundaries)
        {
            count=Model.Records.Count; boundaries=count==0 && !Model.KeepPlaceholder ? null : new IntPtr[Math.Max(count,1)];
            for(int i=0;i<count;i++) boundaries[i]=new IntPtr(i+1);
        }
        public void AskBoundaryData(IntPtr b,out BoundaryData data) { data=Model.Records[b.ToInt32()-1].Definition; }
        public void AskBoundaryAppData(IntPtr b,out AppData data) { data=Model.Records[b.ToInt32()-1].Settings; }
        public void AskBoundaryItems(IntPtr b,out int count,out IntPtr[] items)
        {
            count=Model.Records[b.ToInt32()-1].Tags.Length; items=new IntPtr[count];
            for(int i=0;i<count;i++) items[i]=new IntPtr(b.ToInt32()*1000+i);
        }
        public void AskItemEntity(IntPtr item,out Tag tag)
        { int k=item.ToInt32(); tag=Model.Records[k/1000-1].Tags[k%1000]; }
        public void AskItemAppData(IntPtr item,out AppData data)
        { int k=item.ToInt32(); data=Model.Records[k/1000-1].Members[k%1000]; }
        public void AppendBndFromCurve(Tag operation,CamGeomType type,int count,Tag[] curves,ref BoundaryData definition,AppData[] members)
        {
            if(operation!=Model.Op.Tag || type!=CamGeomType.CamPart || count!=1 || curves.Length!=1 || members.Length!=1 ||
               definition.app_data!=null || definition.plane_type!=1 || definition.origin.Length!=3 || definition.matrix.Length!=9 ||
               members[0].tolerances.Length!=2 || members[0].has_tool_position!=1) throw new Exception("Invalid UF append contract");
            Model.Calls++; Model.Inherited=false;
            if(Model.FailAt==Model.Calls) throw new InvalidOperationException("Injected append failure");
            if(Model.DropTag==curves[0]) return;
            var r=new Record {Tags=(Tag[])curves.Clone(), Definition=definition, Members=(AppData[])members.Clone(),
                Position=members[0].tool_position==CamToolPosition.CamToolPositionTanto ? BoundarySet.ToolPositionTypes.Contact : BoundarySet.ToolPositionTypes.On,
                Side=definition.material_side==CamMaterialSide.CamMaterialSideOutRight ? BoundarySet.ToolSideTypes.OutsideOrRight : BoundarySet.ToolSideTypes.InsideOrLeft};
            if(Model.WrongEditorDefaults) { r.Side=BoundarySet.ToolSideTypes.InsideOrLeft; r.Position=BoundarySet.ToolPositionTypes.Contact; }
            Model.Records.Add(r);
            if(Model.SubstituteTag==curves[0]) r.Tags[0]=Model.Records[0].Tags[0];
            if(Model.CorruptOld && Model.Calls==1) Model.Records[0].Settings.stock+=1;
            if(Model.MergeAt==Model.Calls) {
                Record earlier=Model.Records[Model.Records.Count-2];
                earlier.Tags=new[]{earlier.Tags[0],curves[0]}; earlier.Members=new[]{earlier.Members[0],members[0]};
                Model.Records.RemoveAt(Model.Records.Count-1);
            }
            if(Model.DuplicateTag==curves[0]) Model.Records.Add(r.Copy());
            if(Model.WrongClosure) r.Definition.boundary_type=CamBoundaryType.CamBoundaryTypeClosed;
        }
    }
}
public static class PlanarCurveWindows
{
    public static PlanarCurveOptions AskOptions(IntPtr owner,string title,string operation) { return Model.Options; }
    public static void ShowReport(IntPtr owner,string title,string headline,string report,int severity)
    { Model.Report=report; Model.Severity=severity; }
}
public class Record
{
    public Tag[] Tags; public UFCambnd.BoundaryData Definition; public UFCambnd.AppData Settings;
    public UFCambnd.AppData[] Members; public BoundarySet.ToolPositionTypes Position; public BoundarySet.ToolSideTypes Side;
    public bool CustomOffset; public double Offset;
    public Record Copy() {var copy=(Record)MemberwiseClone(); copy.Tags=(Tag[])Tags.Clone(); copy.Members=(UFCambnd.AppData[])Members.Clone(); return copy;}
    public BoundarySetPlanarMill Editor()
    {
        var r=new BoundarySetPlanarMill {Saved=this, ToolPosition=Position, ToolSide=Side, CustomOffset=CustomOffset, Offset=Offset,
            BoundaryType=Definition.boundary_type==CamBoundaryType.CamBoundaryTypeClosed ? BoundarySet.BoundaryTypes.Closed : BoundarySet.BoundaryTypes.Open,
            CustomStock=Settings.has_stock!=0,CustomTolerance=Settings.has_tolerances!=0};
        for(int i=0;i<Tags.Length;i++) r.BoundaryMemberList.Add(new BoundaryMemberSetPlanarMill {
            Geometry=Tags[i]==Tag.Null ? null : Model.Curves[Tags[i]],
            ToolPosition=Members[i].tool_position==CamToolPosition.CamToolPositionTanto ? 1 : 0,
            CustomStock=Members[i].has_stock!=0,CustomTolerance=Members[i].has_tolerances!=0});
        return r;
    }
    public void Apply(BoundarySetPlanarMill row)
    {
        Position=row.ToolPosition; Side=row.ToolSide; CustomOffset=row.CustomOffset; Offset=row.Offset;
        Settings.has_stock=row.CustomStock?1:0; Settings.has_tolerances=row.CustomTolerance?1:0;
        for(int i=0;i<Members.Length;i++) {
            var item=(BoundaryMemberSetPlanarMill)row.BoundaryMemberList[i];
            Members[i].tool_position=item.ToolPosition==0 ? CamToolPosition.CamToolPositionOn : CamToolPosition.CamToolPositionTanto;
            Members[i].has_stock=item.CustomStock?1:0; Members[i].has_tolerances=item.CustomTolerance?1:0;
        }
    }
}
public static class Model
{
    public static Session Session; public static UFSession Uf; public static PlanarMilling Op;
    public static Dictionary<Tag,Curve> Curves; public static List<TaggedObject> Selection;
    public static List<Record> Records; public static PlanarCurveOptions Options;
    public static string Report; public static int Severity,Marks,Undos,Commits,Calls,FailAt,MergeAt;
    public static bool Inherited,KeepPlaceholder,NullPlaceholderMember,WrongEditorDefaults,DropOnCommit,CorruptOld,WrongClosure;
    public static Tag DropTag,DuplicateTag,SubstituteTag; static List<Record> undo; static bool undoInherited;
    public static void SaveUndo(){ undo=Records.ConvertAll(r=>r.Copy()); undoInherited=Inherited; }
    public static void RestoreUndo(){ Records=undo.ConvertAll(r=>r.Copy()); Inherited=undoInherited; }
    public static void Reset()
    {
        Session=new Session(); Uf=new UFSession(); Session.Parts.Work=new Part {Tag=(Tag)10};
        Op=new PlanarMilling {Tag=(Tag)9000, OwningPart=Session.Parts.Work, Name="PLANAR_TEST"};
        Curves=new Dictionary<Tag,Curve>(); Selection=new List<TaggedObject>(); Records=new List<Record>();
        Options=new PlanarCurveOptions(); Report=""; Severity=-1; Marks=Undos=Commits=Calls=FailAt=MergeAt=0;
        Inherited=KeepPlaceholder=NullPlaceholderMember=WrongEditorDefaults=DropOnCommit=CorruptOld=WrongClosure=false;
        DuplicateTag=DropTag=SubstituteTag=Tag.Null;
    }
    public static Curve AddCurve(int n,bool closed)
    {
        Curve curve=closed?(Curve)new Arc():new Line(); curve.Tag=(Tag)n; curve.OwningPart=Session.Parts.Work; curve.Closed=closed;
        Curves.Add(curve.Tag,curve); Selection.Add(curve); return curve;
    }
    public static void Run(){NXPlanarAddCurves.Main(new string[0]);}
}
public static class PlanarChecks
{
    static int checks;
    static void Check(bool answer,string why) {checks++; if(!answer)throw new Exception(why+"\n"+Model.Report);}
    static void Select36(){for(int i=1;i<=36;i++) Model.AddCurve(100+i,i%4==0);}
    static void Success(){Check(Model.Severity==0 && Model.Report.Contains("Результат: успешно."),"expected success");}
    static void RolledBack()
    {
        Check(Model.Severity==2 && Model.Undos==1 && Model.Report.Contains("Откат проверен"),"expected verified rollback");
        Check(!Model.Report.Contains("Результат: успешно."),"no false success");
    }
    static void EmptyTo36(bool keepPlaceholder,bool inherited)
    {
        Model.Reset(); Model.KeepPlaceholder=keepPlaceholder; Model.NullPlaceholderMember=keepPlaceholder; Model.Inherited=inherited;
        Select36(); Model.Selection.Add(Model.Selection[0]); Model.Run(); Success();
        Check(Model.Report.Contains("до запуска: 0") && Model.Report.Contains("до добавления: 1"),"separate editor and saved counts");
        Check(Model.Records.Count==36 && Model.Calls==36 && Model.Marks==1 && Model.Commits==0,"one native append per curve, one undo, no redundant commit");
        for(int i=0;i<36;i++)
        {
            Record r=Model.Records[i];
            Check(r.Tags.Length==1 && r.Tags[0]==(Tag)(101+i),"one original tag per boundary");
            Check(r.Members[0].tool_position==CamToolPosition.CamToolPositionOn && r.Position==BoundarySet.ToolPositionTypes.On,"ON");
            Check((r.Definition.boundary_type==CamBoundaryType.CamBoundaryTypeClosed)==((i+1)%4==0),"Open/Closed");
        }
        Check(Model.Report.Contains("Open: 27; Closed: 9"),"correct breakdown");
        Model.Run(); Check(Model.Severity==1 && Model.Calls==36 && Model.Marks==1,"second run skips assigned curves");
        Model.Session.UndoToMark(new Session.UndoMarkId(),null); Check(Model.Records.Count==0 && Model.Inherited==inherited,"one-step undo");
    }
    static void PreserveAndTanto(bool outside)
    {
        Model.Reset(); Model.AddCurve(500,true); Model.Run(); Success();
        Record old=Model.Records[0]; old.CustomOffset=true; old.Offset=1.25; old.Settings.has_stock=1; old.Settings.stock=4.5;
        Model.Records.Add(new Record {Tags=new Tag[0],Members=new UFCambnd.AppData[0],Definition=old.Definition});
        Model.Records.Add(new Record {Tags=new[]{Tag.Null},Members=new UFCambnd.AppData[1],Definition=old.Definition});
        Model.Options.Tanto=true; Model.Options.Outside=outside; Model.WrongEditorDefaults=outside;
        Model.AddCurve(501,false); Model.AddCurve(502,true); Model.Run(); Success();
        Check(Model.Records.Count==5 && Model.Records[1].Tags.Length==0 && Model.Records[2].Tags[0]==Tag.Null,"preserve real empty/permanent boundaries");
        Check(Model.Records[0].Settings.stock==4.5 && Model.Records[0].Position==BoundarySet.ToolPositionTypes.On && Model.Records[0].Offset==1.25,"existing options");
        Check(Model.Records[3].Side==(outside?BoundarySet.ToolSideTypes.OutsideOrRight:BoundarySet.ToolSideTypes.InsideOrLeft)
            && Model.Records[4].Members[0].tool_position==CamToolPosition.CamToolPositionTanto,"TANTO applied to each new boundary");
        Check(Model.Commits==(outside?1:0),"commit only corrected settings"); Check(Model.Report.Contains("пропущено: 1"),"existing curve skipped");
    }
    public static void Main()
    {
        EmptyTo36(false,true); EmptyTo36(true,false);
        PreserveAndTanto(false); PreserveAndTanto(true);
        Model.Reset(); Select36(); Model.Inherited=true; Model.DropTag=(Tag)136; Model.Run(); RolledBack();
        Check(Model.Records.Count==0 && Model.Inherited && Model.Report.Contains("ожидалось 36, фактически 35") && Model.Report.Contains("136"),"missing curve diagnosed and original restored");
        Model.Reset(); Select36(); Model.MergeAt=2; Model.Run(); RolledBack(); Check(Model.Report.Contains("101, 102"),"merged rows identified");
        Model.Reset(); Select36(); Model.DuplicateTag=(Tag)104; Model.Run(); RolledBack(); Check(Model.Report.Contains("104] назначена повторно"),"duplicate rejected");
        Model.Reset(); Select36(); Model.SubstituteTag=(Tag)136; Model.Run(); RolledBack();
        Check(Model.Report.Contains("ожидалось 36, фактически 36") && Model.Report.Contains("136"),"equal count with wrong source is rejected");
        Model.Reset(); Select36(); Model.FailAt=3; Model.Run(); RolledBack(); Check(Model.Calls==3 && Model.Records.Count==0 && Model.Report.Contains("3/36"),"partway failure");
        Model.Reset(); Select36(); Model.WrongClosure=true; Model.Run(); RolledBack(); Check(Model.Report.Contains("ожидалось Open"),"wrong closure rejected");
        Model.Reset(); Select36(); Model.WrongEditorDefaults=true; Model.DropOnCommit=true; Model.Run(); RolledBack();
        Check(Model.Commits==1 && Model.Report.Contains("Итоговое число сохранённых границ: 35"),"verify after optional edit commit");
        Model.Reset(); Model.AddCurve(501,true); Model.Run(); Success(); Model.Records[0].Settings.has_stock=1; Model.Records[0].Settings.stock=9;
        Model.AddCurve(502,false); Model.CorruptOld=true; Model.Calls=0; Model.Run(); RolledBack();
        Check(Model.Records.Count==1 && Model.Records[0].Settings.stock==9,"original active overrides verified on rollback");
        Model.Reset(); Model.AddCurve(77,true); Model.Options=null; Model.Run();
        Check(Model.Marks==0 && Model.Calls==0 && Model.Severity==-1,"cancel before mutations");
        Console.WriteLine("Planar transaction checks passed: "+checks);
    }
}
