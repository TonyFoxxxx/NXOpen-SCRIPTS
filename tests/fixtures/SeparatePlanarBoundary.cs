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
    public struct Point3d
    {
        public double X,Y,Z;
        public Point3d(double x,double y,double z) { X=x; Y=y; Z=z; }
    }
    public struct Vector3d
    {
        public double X,Y,Z;
        public Vector3d(double x,double y,double z) { X=x; Y=y; Z=z; }
    }
    public class PlaneTypes { public enum MethodType { Fixed, Distance, Angle, Bisector } }
    public class Expression
    {
        public enum UnitsOption { Base }
        public string RightHandSide;
        public double GetValueUsingUnits(UnitsOption units) { return Double.Parse(RightHandSide,System.Globalization.CultureInfo.InvariantCulture); }
    }
    public class SmartObject { public enum UpdateOption { AfterModeling } }
    public class Plane : NXObject
    {
        public Point3d Origin; public Vector3d Normal; public bool Temporary;
        public PlaneTypes.MethodType Method; public Expression Expression; public bool OffsetFlip;
        private NXObject[] references;
        public void SetMethod(PlaneTypes.MethodType method) {Method=method;Temporary=true;}
        public NXObject[] GetGeometry() {return references??new NXObject[]{new NXObject()};}
        public void SetGeometry(NXObject[] objects) {references=objects;}
        public void SetExpression(string value) {Expression=new Expression{RightHandSide=value};}
        public void Evaluate()
        {
            if(!Temporary) return;
            if(!Model.DialogOpen || !Model.PreviewActive) throw new Exception("Plane evaluated after its native dialog closed");
            Model.PlaneEvaluations++;
            Origin=Model.ReferencePoint; Normal=Model.ReferenceNormal;
            double length=Math.Sqrt(Normal.X*Normal.X+Normal.Y*Normal.Y+Normal.Z*Normal.Z);
            if(Method==PlaneTypes.MethodType.Distance && length>0)
            {
                double shift=Expression.GetValueUsingUnits(Expression.UnitsOption.Base)*(OffsetFlip?-1:1)/length;
                Origin=new Point3d(Origin.X+Normal.X*shift,Origin.Y+Normal.Y*shift,Origin.Z+Normal.Z*shift);
            }
        }
        public void DestroyPlane() { if(Temporary) throw new Exception("Dialog owns the constructed plane"); }
    }
    public class PlaneCollection
    {
        public Plane CreatePlane(Point3d origin,Vector3d normal,SmartObject.UpdateOption update)
        {
            Model.Replacements++;
            return new Plane {Origin=origin,Normal=normal};
        }
    }
    public class Curve : NXObject { public bool Closed; public double GetLength() { return 10.0; } }
    public class Line : Curve {} public class Arc : Curve {} public class Spline : Curve {} public class Ellipse : Curve {}
    public class NXException : Exception { public int ErrorCode; }
    public class BasePart : NXObject { public enum Units { Millimeters, Inches }; public Units PartUnits; }
    public class Part : BasePart { public CAMSetup CAMSetup = new CAMSetup(); public PlaneCollection Planes=new PlaneCollection(); }
    public class PartList { public Part Work; }
    public class ListingWindow { public void Open() {} public void WriteLine(string line) { throw new Exception(line); } }
    public class Session
    {
        public enum LibraryUnloadOption { Immediately } public enum MarkVisibility { Visible, Invisible }
        public struct UndoMarkId { internal bool Preview; }
        public PartList Parts = new PartList(); public ListingWindow ListingWindow = new ListingWindow();
        public static Session GetSession() { return Model.Session; }
        public UndoMarkId SetUndoMark(MarkVisibility visibility, string name)
        {
            if(Model.PreviewActive) throw new Exception("Previous plane preview is still active");
            if(visibility==MarkVisibility.Invisible) { Model.PreviewActive=true; return new UndoMarkId {Preview=true}; }
            Model.Marks++; Model.SaveUndo(); return new UndoMarkId();
        }
        public void UndoToMark(UndoMarkId mark,string name)
        {
            if(mark.Preview)
            {
                if(Model.DialogOpen || !Model.PreviewActive) throw new Exception("Incorrect native cleanup order");
                if(Model.PlaneBuilt) { Model.PlaneDisposals++; Model.PlaneBuilt=false; }
                return;
            }
            Model.Undos++; Model.RestoreUndo();
        }
        public void DeleteUndoMark(UndoMarkId mark,string name)
        {
            if(mark.Preview) { if(Model.PlaneBuilt) throw new Exception("Plane preview data left in model"); Model.PreviewActive=false; }
        }
    }
    public class Selection
    {
        public struct MaskTriple
        {
            public int Type, Subtype, SolidBodySubtype;
            public MaskTriple(int a,int b,int c) { Type=a; Subtype=b; SolidBodySubtype=c; }
        }
        public enum Response { OK, ObjectSelected, Cancel, Back } public enum SelectionScope { WorkPart, AnyInAssembly }
        public enum SelectionAction { ClearAndEnableSpecific }
        public Response SelectTaggedObjects(string prompt,string title,SelectionScope scope,SelectionAction action,
            bool features,bool highlight,MaskTriple[] mask,out TaggedObject[] objects)
        {
            bool blank=title.StartsWith("Выберите кривые заготовки",StringComparison.Ordinal);
            if(Model.PreviewActive || (!blank && !title.StartsWith("Выберите кривые детали",StringComparison.Ordinal))
                || !prompt.Contains(blank?"Blank Boundaries":"Part Boundaries")) throw new Exception("Missing destination in curve picker");
            Model.Picks+=blank?"B":"C";
            objects=(blank?Model.BlankSelection:Model.Selection).ToArray();
            return blank?Model.BlankResponse:Model.CurveResponse;
        }
    }
    public class UI
    {
        public Selection SelectionManager=new Selection(); public static UI GetUI() { return new UI(); }
        public BlockStyler.SnapBlockDialog CreateSnapDialog(string title) { return new BlockStyler.SnapBlockDialog(title); }
    }
}
namespace NXOpen.BlockStyler
{
    public class UIBlock { public string Label; public bool Enable; }
    public class SpecifyPlane : UIBlock
    {
        public string LabelString, StepStatusAsString, BalloonTooltipText; public bool ShowShortcuts;
        private TaggedObject[] selection;
        public void SetSelectedObjects(TaggedObject[] objs) {selection=objs;}
        public TaggedObject[] GetSelectedObjects()
        {
            if(!Model.DialogOpen) throw new Exception("Read plane outside the dialog");
            if(Model.MissingPlane) return new TaggedObject[0];
            if(selection!=null) return selection;
            return new TaggedObject[] {new Plane { Temporary=true, Method=Model.Method, OffsetFlip=Model.OffsetFlip,
                Expression=Model.Method==PlaneTypes.MethodType.Distance ? new Expression {RightHandSide=Model.Distance.ToString(System.Globalization.CultureInfo.InvariantCulture)} : null }};
        }
    }
    public class CompositeBlock : UIBlock
    {
        internal readonly Dictionary<string,UIBlock> Blocks=new Dictionary<string,UIBlock>();
        public UIBlock FindBlock(string name){return Blocks[name];}
    }
    public class BlockDialog
    {
        public enum DialogResponse { Ok, Cancel, Back } public enum DialogMode { Create, Edit }
    }
    public class SnapBlockDialog : BlockDialog, IDisposable
    {
        public CompositeBlock TopBlock=new CompositeBlock();
        private Action init; private Func<int> ok; private Func<UIBlock,int> update; private string title;
        public SnapBlockDialog(string title) { this.title=title; }
        public void AddItem(string type,string id)
        {
            if(type!="Specify Plane" && type!="Label" && type!="Button") throw new Exception("Expected native NX block type");
            TopBlock.Blocks.Add(id,type=="Specify Plane"?(UIBlock)new SpecifyPlane():new UIBlock());
        }
        public void AddInitializeHandler(Action callback) {init=callback;}
        public void AddOkHandler(Func<int> callback) {ok=callback;}
        public void AddUpdateHandler(Func<UIBlock,int> callback) {update=callback;}
        public DialogResponse LaunchInDialogMode(DialogMode mode)
        {
            if(mode!=DialogMode.Edit || !Model.PreviewActive) throw new Exception("Unscoped constructor or repeating Apply mode");
            Model.Picks+="P"; Model.DialogOpen=true; init();
            var plane=(SpecifyPlane)TopBlock.FindBlock("boundaryPlane");
            if(TopBlock.Label!=title || !title.StartsWith("Выберите плоскость в модели",StringComparison.Ordinal) ||
                !plane.ShowShortcuts || plane.StepStatusAsString!="Required" || !plane.LabelString.Contains("плоскость") ||
                !TopBlock.FindBlock("offsetHint").Label.Contains("At Distance")) throw new Exception("Missing native plane instructions");
            Model.PlaneBuilds++; Model.PlaneBuilt=true;
            if(Model.PlaneResponse==Selection.Response.Back) return DialogResponse.Back;
            if(Model.PlaneResponse==Selection.Response.Cancel) return DialogResponse.Cancel;
            if(Model.RepeatClicks>0)
            {
                UIBlock repeat=TopBlock.FindBlock("repeatOffset");
                update(plane);
                if(!repeat.Enable) throw new Exception("Repeat requires an explicit new reference");
                for(int n=0;n<Model.RepeatClicks;n++) update(repeat);
            }
            if(ok()!=0) throw new Exception("OK callback failed");
            return DialogResponse.Ok;
        }
        public void Dispose() {Model.DialogOpen=false;}
    }
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
    public class Operation : NXObject { public NCGroup GetParent(CAMSetup.View view) { return Model.Parent; } }
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
        internal Plane StoredPlane; internal bool PlaneAssigned;
        public Plane Plane { get { return StoredPlane; } set { StoredPlane=value; PlaneAssigned=true; } }
    }
    public class BoundarySetPlanarMill : BoundarySet { public bool CustomStock, CustomTolerance; internal Record Saved; }
    public class RowList : List<BoundarySet> { public BoundarySet[] GetContents() { return ToArray(); } }
    public class BoundaryPlanarMill { public RowList BoundaryList=new RowList(); }
    public class PlanarMillingBuilder
    {
        public BoundaryPlanarMill PartBoundary=new BoundaryPlanarMill(), BlankBoundary=new BoundaryPlanarMill();
        public PlanarMillingBuilder()
        {
            Fill(PartBoundary,Model.Records);
            if (Model.NoBlankEditor) BlankBoundary=null;
            else Fill(BlankBoundary,Model.BlankRecords);
        }
        private static void Fill(BoundaryPlanarMill collection,List<Record> records)
        {
            foreach(Record record in records)
                if(Model.OmitEditorTag==Tag.Null || Array.IndexOf(record.Tags,Model.OmitEditorTag)<0)
                    collection.BoundaryList.Add(record.Editor());
            if(records.Count==0 || Model.KeepPlaceholder)
            {
                var blank=new BoundarySetPlanarMill();
                if(Model.NullPlaceholderMember) blank.BoundaryMemberList.Add(new BoundaryMemberSetPlanarMill());
                collection.BoundaryList.Add(blank);
            }
        }
        public void Commit()
        {
            Model.Commits++;
            foreach(BoundaryPlanarMill collection in new[]{PartBoundary,BlankBoundary})
                if(collection!=null)
                    foreach(BoundarySetPlanarMill row in collection.BoundaryList)
                        if(row.Saved!=null) row.Saved.Apply(row);
            if(Model.TouchOtherOnCommit && Model.BlankRecords.Count>0) Model.BlankRecords[0].Offset+=1;
            if(Model.DropOnCommit) Model.Records.RemoveAt(Model.Records.Count-1);
        }
        public void Destroy() {}
    }
}
namespace NXOpen.UF
{
    public enum CamGeomType { CamPart, CamBlank }
    public enum CamBoundaryType { CamBoundaryTypeClosed, CamBoundaryTypeOpen }
    public enum CamMaterialSide { CamMaterialSideUndefined, CamMaterialSideInLeft, CamMaterialSideOutRight }
    public enum CamToolPosition { CamToolPositionUndefined, CamToolPositionOn, CamToolPositionTanto }
    public class UFConstants
    {
        public const int UF_line_type=3, UF_circle_type=5, UF_conic_type=6, UF_spline_type=9;
        public const int UF_solid_type=70, UF_datum_plane_type=197, UF_UI_SEL_FEATURE_PLANAR_FACE=19;
    }
    public class UFMtx3
    {
        public void InitializeZ(double[] z,double[] matrix)
        {
            double x0=0, x1=z[2], x2=-z[1];
            if(Math.Abs(z[0])>0.9) { x0=-z[2]; x1=0; x2=z[0]; }
            double length=Math.Sqrt(x0*x0+x1*x1+x2*x2);
            matrix[0]=x0/length; matrix[1]=x1/length; matrix[2]=x2/length;
            matrix[3]=z[1]*matrix[2]-z[2]*matrix[1];
            matrix[4]=z[2]*matrix[0]-z[0]*matrix[2];
            matrix[5]=z[0]*matrix[1]-z[1]*matrix[0];
            Array.Copy(z,0,matrix,6,3);
        }
    }
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
        public UFMtx3 Mtx3=new UFMtx3();
        public static UFSession GetUFSession(){return Model.Uf;}
    }
    public class UFCambnd
    {
        public struct AppData { public int has_stock,has_tolerances,has_feedrate,has_blank_distance,has_tool_position,feedrate_unit;
            public double stock,feedrate_value,blank_distance; public double[] tolerances; public CamToolPosition tool_position; }
        public struct BoundaryData { public CamBoundaryType boundary_type; public int plane_type,ignore_holes,ignore_islands,ignore_chamfers;
            public double[] origin,matrix; public CamMaterialSide material_side; public AppData[] app_data; }
        private Record Boundary(int handle)
        { return (handle<0?Model.BlankRecords:Model.Records)[Math.Abs(handle)-1]; }
        private Record Member(int handle)
        { return Boundary(handle/1000); }
        public void IsInherited(Tag operation,CamGeomType type,out bool inherited)
        { inherited=type==CamGeomType.CamBlank?Model.BlankInherited:Model.Inherited; }
        public void AskBoundaries(Tag operation,CamGeomType type,out int count,out IntPtr[] boundaries)
        {
            bool blank=type==CamGeomType.CamBlank;
            count=operation==Model.Op.Tag?(blank?Model.BlankRecords:Model.Records).Count:(blank?Model.ParentBlankCount:Model.ParentPartCount);
            boundaries=count==0 && !Model.KeepPlaceholder?null:new IntPtr[Math.Max(count,1)];
            for(int i=0;i<count;i++) boundaries[i]=new IntPtr((blank?-1:1)*(i+1));
        }
        public void AskBoundaryData(IntPtr b,out BoundaryData data)
        {
            data=Boundary(b.ToInt32()).Definition;
            if(Model.UfReadType.HasValue) data.boundary_type=(CamBoundaryType)Model.UfReadType.Value;
        }
        public void AskBoundaryAppData(IntPtr b,out AppData data) { data=Boundary(b.ToInt32()).Settings; }
        public void AskBoundaryItems(IntPtr b,out int count,out IntPtr[] items)
        {
            int handle=b.ToInt32();count=Boundary(handle).Tags.Length; items=new IntPtr[count];
            for(int i=0;i<count;i++) items[i]=new IntPtr(handle*1000+Math.Sign(handle)*i);
        }
        public void AskItemEntity(IntPtr item,out Tag tag)
        { int k=item.ToInt32(); tag=Member(k).Tags[Math.Abs(k)%1000]; }
        public void AskItemAppData(IntPtr item,out AppData data)
        { int k=item.ToInt32(); data=Member(k).Members[Math.Abs(k)%1000]; }
        public void AppendBndFromCurve(Tag operation,CamGeomType type,int count,Tag[] curves,ref BoundaryData definition,AppData[] members)
        {
            bool blank=type==CamGeomType.CamBlank;
            List<Record> records=blank?Model.BlankRecords:Model.Records;
            if(operation!=Model.Op.Tag || count!=1 || curves.Length!=1 || members.Length!=1 ||
               definition.app_data!=null || definition.plane_type!=(Model.Options.SelectPlane?0:1) || definition.origin.Length!=3 || definition.matrix.Length!=9 ||
               members[0].tolerances.Length!=2 || members[0].has_tool_position!=1) throw new Exception("Invalid UF append contract");
            Model.Calls++; if(blank) Model.BlankInherited=false; else Model.Inherited=false;
            if(Model.FailAt==Model.Calls) throw new InvalidOperationException("Injected append failure");
            if(Model.DropTag==curves[0]) return;
            var r=new Record {Tags=(Tag[])curves.Clone(), Definition=definition, Members=(AppData[])members.Clone(),
                Position=members[0].tool_position==CamToolPosition.CamToolPositionTanto ? BoundarySet.ToolPositionTypes.Contact : BoundarySet.ToolPositionTypes.On,
                Side=definition.material_side==CamMaterialSide.CamMaterialSideOutRight ? BoundarySet.ToolSideTypes.OutsideOrRight : BoundarySet.ToolSideTypes.InsideOrLeft};
            if(Model.WrongEditorDefaults) { r.Side=BoundarySet.ToolSideTypes.InsideOrLeft; r.Position=BoundarySet.ToolPositionTypes.Contact; }
            records.Add(r);
            if(Model.SubstituteTag==curves[0]) r.Tags[0]=records[0].Tags[0];
            if(Model.CorruptOld && Model.Calls==1) records[0].Settings.stock+=1;
            if(Model.MergeAt==Model.Calls) {
                Record earlier=records[records.Count-2];
                earlier.Tags=new[]{earlier.Tags[0],curves[0]}; earlier.Members=new[]{earlier.Members[0],members[0]};
                records.RemoveAt(records.Count-1);
            }
            if(Model.DuplicateTag==curves[0]) records.Add(r.Copy());
            if(Model.WrongClosure) r.Definition.boundary_type=CamBoundaryType.CamBoundaryTypeClosed;
            if(definition.plane_type==0 && Model.AdjustInitialPlane!=null) Model.AdjustInitialPlane(r);
        }
    }
}
#if !COMPILE_FULL_PLANAR_JOURNAL
public static class PlanarCurveWindows
{
    public static PlanarCurveOptions AskOptions(IntPtr owner,string title,string operation) { return Model.Options; }
    public static void ShowReport(IntPtr owner,string title,string headline,string report,int severity)
    { Model.Report=report; Model.Severity=severity; }
}
#endif
public class Record
{
    public Tag[] Tags; public UFCambnd.BoundaryData Definition; public UFCambnd.AppData Settings;
    public UFCambnd.AppData[] Members; public BoundarySet.ToolPositionTypes Position; public BoundarySet.ToolSideTypes Side;
    public bool CustomOffset; public double Offset;
    public Record Copy()
    {
        var copy=(Record)MemberwiseClone();
        copy.Tags=(Tag[])Tags.Clone(); copy.Members=(UFCambnd.AppData[])Members.Clone();
        if(Definition.origin!=null) copy.Definition.origin=(double[])Definition.origin.Clone();
        if(Definition.matrix!=null) copy.Definition.matrix=(double[])Definition.matrix.Clone();
        return copy;
    }
    public BoundarySetPlanarMill Editor()
    {
        var r=new BoundarySetPlanarMill {Saved=this, ToolPosition=Position, ToolSide=Side, CustomOffset=CustomOffset, Offset=Offset,
            BoundaryType=Definition.boundary_type==CamBoundaryType.CamBoundaryTypeClosed ? BoundarySet.BoundaryTypes.Closed : BoundarySet.BoundaryTypes.Open,
            CustomStock=Settings.has_stock!=0,CustomTolerance=Settings.has_tolerances!=0};
        if(Definition.plane_type==0)
        {
            r.PlaneType=BoundarySet.PlaneTypes.UserDefined;
            var o=Definition.origin; var m=Definition.matrix;
            r.StoredPlane=new Plane {Origin=new Point3d(o[0],o[1],o[2]),Normal=new Vector3d(m[6],m[7],m[8])};
        }
        if(Model.EditorReadType.HasValue) r.BoundaryType=(BoundarySet.BoundaryTypes)Model.EditorReadType.Value;
        for(int i=0;i<Tags.Length;i++) r.BoundaryMemberList.Add(new BoundaryMemberSetPlanarMill {
            Geometry=Tags[i]==Tag.Null ? null : Model.Curves[Tags[i]],
            ToolPosition=Members[i].tool_position==CamToolPosition.CamToolPositionTanto ? 1 : 0,
            CustomStock=Members[i].has_stock!=0,CustomTolerance=Members[i].has_tolerances!=0});
        return r;
    }
    public void Apply(BoundarySetPlanarMill row)
    {
        if(row.PlaneAssigned && !Model.IgnorePlaneEdits)
        {
            var o=row.Plane.Origin; var n=row.Plane.Normal;
            Definition.plane_type=row.PlaneType==BoundarySet.PlaneTypes.UserDefined?0:1;
            Definition.origin=new[]{o.X,o.Y,o.Z}; Definition.matrix=new double[9];
            Model.Uf.Mtx3.InitializeZ(new[]{n.X,n.Y,n.Z}, Definition.matrix);
        }
        Position=row.ToolPosition; CustomOffset=row.CustomOffset; Offset=row.Offset;
        if(!Model.IgnoreShapeEdits)
            Definition.boundary_type=row.BoundaryType==BoundarySet.BoundaryTypes.Closed ? CamBoundaryType.CamBoundaryTypeClosed : CamBoundaryType.CamBoundaryTypeOpen;
        if (!Model.IgnoreOpenSide || row.BoundaryType!=BoundarySet.BoundaryTypes.Open) Side=row.ToolSide;
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
    public static bool Inherited,KeepPlaceholder,NullPlaceholderMember,WrongEditorDefaults,DropOnCommit,CorruptOld,WrongClosure,IgnoreOpenSide;
    public static bool IgnoreShapeEdits;
    public static Point3d ReferencePoint; public static Vector3d ReferenceNormal;
    public static PlaneTypes.MethodType Method; public static double Distance;
    public static bool OffsetFlip,MissingPlane,DialogOpen,PreviewActive,PlaneBuilt;
    public static Selection.Response PlaneResponse,CurveResponse;
    public static string Picks; public static int PlaneBuilds,PlaneEvaluations,PlaneDisposals,Replacements;
    public static Action<Record> AdjustInitialPlane; public static bool IgnorePlaneEdits;
    public static int? UfReadType, EditorReadType;
    public static Tag OmitEditorTag;
    public static Tag DropTag,DuplicateTag,SubstituteTag; static List<Record> undo,undoBlank; static bool undoInherited,undoBlankInherited;
    public static List<Record> BlankRecords; public static List<TaggedObject> BlankSelection;
    public static bool BlankInherited,TouchOtherOnCommit,NoBlankEditor; public static Selection.Response BlankResponse;
    public static int RepeatClicks;
    public static NCGroup Parent; public static int ParentPartCount,ParentBlankCount;
    public static void SaveUndo(){ undo=Records.ConvertAll(r=>r.Copy()); undoInherited=Inherited; undoBlank=BlankRecords.ConvertAll(r=>r.Copy()); undoBlankInherited=BlankInherited; }
    public static void RestoreUndo(){ Records=undo.ConvertAll(r=>r.Copy()); Inherited=undoInherited; BlankRecords=undoBlank.ConvertAll(r=>r.Copy()); BlankInherited=undoBlankInherited; }
    public static void Reset()
    {
        Session=new Session(); Uf=new UFSession(); Session.Parts.Work=new Part {Tag=(Tag)10};
        Op=new PlanarMilling {Tag=(Tag)9000, OwningPart=Session.Parts.Work, Name="PLANAR_TEST"};
        Curves=new Dictionary<Tag,Curve>(); Selection=new List<TaggedObject>(); Records=new List<Record>();
        BlankSelection=new List<TaggedObject>();BlankRecords=new List<Record>(); Parent=null;ParentPartCount=ParentBlankCount=0;
        BlankInherited=TouchOtherOnCommit=NoBlankEditor=false;BlankResponse=NXOpen.Selection.Response.OK;RepeatClicks=0;
        Options=new PlanarCurveOptions { SelectPlane=false }; Report=""; Severity=-1; Marks=Undos=Commits=Calls=FailAt=MergeAt=0;
        Method=PlaneTypes.MethodType.Fixed; Distance=0;
        OffsetFlip=MissingPlane=DialogOpen=PreviewActive=PlaneBuilt=false;
        ReferencePoint=new Point3d(11,-4,29); ReferenceNormal=new Vector3d(0,3,4);
        Picks=""; PlaneBuilds=PlaneEvaluations=PlaneDisposals=Replacements=0;
        PlaneResponse=NXOpen.Selection.Response.ObjectSelected; CurveResponse=NXOpen.Selection.Response.OK;
        AdjustInitialPlane=null; IgnorePlaneEdits=false;
        Inherited=KeepPlaceholder=NullPlaceholderMember=WrongEditorDefaults=DropOnCommit=CorruptOld=WrongClosure=IgnoreOpenSide=false;
        IgnoreShapeEdits=false; UfReadType=EditorReadType=null; OmitEditorTag=Tag.Null;
        DuplicateTag=DropTag=SubstituteTag=Tag.Null;
    }
    public static Curve AddCurve(int n,bool closed)
    {
        Curve curve=closed?(Curve)new Arc():new Line(); curve.Tag=(Tag)n; curve.OwningPart=Session.Parts.Work; curve.Closed=closed;
        Curves.Add(curve.Tag,curve); Selection.Add(curve); return curve;
    }
    public static void Run(){NXPlanarAddCurves.Main(new string[0]);}
}
#if !COMPILE_FULL_PLANAR_JOURNAL
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
        Model.Options.ClosedOutside=true; Model.Options.OpenRight=true; // Dormant choices after returning to ON.
        Select36(); Model.Selection.Add(Model.Selection[0]); Model.Run(); Success();
        Check(Model.Report.Contains("до запуска: 0") && Model.Report.Contains("до добавления: 1"),"separate editor and saved counts");
        Check(Model.Records.Count==36 && Model.Calls==36 && Model.Marks==1 && Model.Commits==0,"one native append per curve, one undo, no redundant commit");
        for(int i=0;i<36;i++)
        {
            Record r=Model.Records[i];
            Check(r.Tags.Length==1 && r.Tags[0]==(Tag)(101+i),"one original tag per boundary");
            Check(r.Members[0].tool_position==CamToolPosition.CamToolPositionOn && r.Position==BoundarySet.ToolPositionTypes.On,"ON");
            Check(r.Definition.material_side==CamMaterialSide.CamMaterialSideInLeft,"ON ignores dormant TANTO settings");
            Check((r.Definition.boundary_type==CamBoundaryType.CamBoundaryTypeClosed)==((i+1)%4==0),"Open/Closed");
        }
        Check(Model.Report.Contains("Open: 27; Closed: 9"),"correct breakdown");
        Model.Run(); Check(Model.Severity==1 && Model.Calls==36 && Model.Marks==1,"second run skips assigned curves");
        Model.Session.UndoToMark(new Session.UndoMarkId(),null); Check(Model.Records.Count==0 && Model.Inherited==inherited,"one-step undo");
    }
    static void PreserveAndTanto(bool outside, bool right)
    {
        Model.Reset(); Model.AddCurve(500,true); Model.Run(); Success();
        Record old=Model.Records[0]; old.CustomOffset=true; old.Offset=1.25; old.Settings.has_stock=1; old.Settings.stock=4.5;
        Model.Records.Add(new Record {Tags=new Tag[0],Members=new UFCambnd.AppData[0],Definition=old.Definition});
        Model.Records.Add(new Record {Tags=new[]{Tag.Null},Members=new UFCambnd.AppData[1],Definition=old.Definition});
        Model.Options.Tanto=true; Model.Options.ClosedOutside=outside; Model.Options.OpenRight=right; Model.WrongEditorDefaults=outside || right;
        Model.AddCurve(501,false); Model.AddCurve(502,true); Model.Run(); Success();
        Check(Model.Records.Count==5 && Model.Records[1].Tags.Length==0 && Model.Records[2].Tags[0]==Tag.Null,"preserve real empty/permanent boundaries");
        Check(Model.Records[0].Settings.stock==4.5 && Model.Records[0].Position==BoundarySet.ToolPositionTypes.On && Model.Records[0].Offset==1.25,"existing options");
        Check(Model.Records[3].Side==(right?BoundarySet.ToolSideTypes.OutsideOrRight:BoundarySet.ToolSideTypes.InsideOrLeft),"open tool side");
        Check(Model.Records[4].Side==(outside?BoundarySet.ToolSideTypes.OutsideOrRight:BoundarySet.ToolSideTypes.InsideOrLeft),"closed tool side");
        Check(Model.Records[3].Members[0].tool_position==CamToolPosition.CamToolPositionTanto && Model.Records[4].Members[0].tool_position==CamToolPosition.CamToolPositionTanto,"TANTO on both members");
        Check(Model.Commits==((outside||right)?1:0),"commit only corrected settings"); Check(Model.Report.Contains("пропущено: 1"),"existing curve skipped");
    }
    static void Mixed36(bool outside,bool right)
    {
        Model.Reset(); Select36(); Model.Options.Tanto=true;
        Model.Options.ClosedOutside=outside; Model.Options.OpenRight=right;
        Model.Run(); Success();
        Check(Model.Records.Count==36 && Model.Calls==36 && Model.Marks==1,"36 separate TANTO boundaries");
        foreach(Record record in Model.Records)
        {
            bool closed=Model.Curves[record.Tags[0]].Closed;
            bool alternate=closed ? outside : right;
            Check(record.Tags.Length==1 && record.Members[0].tool_position==CamToolPosition.CamToolPositionTanto,"one TANTO member");
            Check(record.Definition.material_side==(alternate?CamMaterialSide.CamMaterialSideOutRight:CamMaterialSide.CamMaterialSideInLeft),"native side chosen per curve");
            Check(record.Side==(alternate?BoundarySet.ToolSideTypes.OutsideOrRight:BoundarySet.ToolSideTypes.InsideOrLeft),"verified editor side per curve");
        }
        Check(Model.Report.Contains("Замкнутых границ со стороной "+(outside?"Outside — снаружи":"Inside — внутри")+": 9"),"closed report");
        Check(Model.Report.Contains("Открытых границ со стороной "+(right?"Right — справа":"Left — слева")+": 27"),"open report");
    }
    static void RememberedDistance()
    {
        Model.Reset(); Model.Options.SelectPlane=true; Model.Options.Blank=true;
        Model.Distance=25.4; Model.OffsetFlip=true;Model.Method=PlaneTypes.MethodType.Distance;
        Model.BlankSelection.Add(Model.AddCurve(701,true));Model.Run();Success();
        Check(Model.Options.LastMethod=="Distance" && Model.Options.OffsetMm.HasValue && Math.Abs(Model.Options.OffsetMm.Value+25.4)<1e-9,
            "remember evaluated signed distance after plane confirmation");
        Model.Session.Parts.Work.PartUnits=BasePart.Units.Inches;
        Model.ReferencePoint=new Point3d(10,20,30);Model.ReferenceNormal=new Vector3d(0,0,1);
        Model.OffsetFlip=false; Model.Distance=9; Model.RepeatClicks=2;
        Model.BlankSelection.Add(Model.AddCurve(702,false));Model.Run();Success();
        Check(Math.Abs(Model.Records[1].Definition.origin[2]-29)<1e-9 && Math.Abs(Model.BlankRecords[1].Definition.origin[2]-29)<1e-9,
            "repeat on new support in inches, replace current distance, never double the offset");
        Model.Method=PlaneTypes.MethodType.Bisector;Model.RepeatClicks=0;Model.BlankSelection.Add(Model.AddCurve(703,true));Model.Run();Success();
        Check(Model.Options.LastMethod=="Bisector" && !Model.Options.OffsetMm.HasValue && Model.Records[2].Definition.origin[2]==30,
            "a different native construction wins and clears the distance preset");
    }

    static void BothTargets()
    {
        Model.Reset(); Model.Options.Part=false; Model.Options.Blank=true; Model.Options.SelectPlane=true;
        Model.BlankSelection.Add(Model.AddCurve(201,true)); Model.BlankSelection.Add(Model.BlankSelection[0]);
        Model.Parent=new NCGroup {Tag=(Tag)800}; Model.ParentPartCount=1; Model.Inherited=true;
        Model.Run(); Success();
        Check(Model.Picks=="PB" && Model.Marks==1 && Model.Calls==1 && Model.Records.Count==0 && Model.Inherited
            && Model.BlankRecords.Count==1,"blank-only, independent of inherited part");
        Model.Parent=null;Model.Inherited=false;Model.Options.Part=true;Model.Options.Tanto=true;Model.Options.OpenRight=true;
        Model.BlankSelection.Add(Model.AddCurve(202,false)); Model.Run(); Success();
        Check(Model.Picks=="PBPCB" && Model.Records.Count==2 && Model.BlankRecords.Count==2 && Model.Calls==4 && Model.Marks==2,
            "one curve may belong to both; duplicates are per target; common plane requested once per run");
        Check(Model.BlankRecords[0].Position==BoundarySet.ToolPositionTypes.On && Model.Records[0].Position==BoundarySet.ToolPositionTypes.Contact
            && Model.BlankRecords[1].Side==BoundarySet.ToolSideTypes.OutsideOrRight,"separate settings and shared TANTO mode");
        Model.Run();Check(Model.Severity==1 && Model.Calls==4 && Model.Marks==2,"both selections already assigned");
        Model.Session.UndoToMark(new Session.UndoMarkId(),null);
        Check(Model.Records.Count==0 && Model.BlankRecords.Count==1,"undo restores both stores at once");

        foreach(Selection.Response cancel in new[]{Selection.Response.Cancel,Selection.Response.Back})
        {
            Model.Reset();Model.Options.Blank=true;Model.Options.SelectPlane=true;Model.AddCurve(301,true);Model.BlankResponse=cancel;Model.Run();
            Check(Model.Picks=="PCB" && Model.Marks==0 && Model.Calls==0 && Model.PlaneBuilds==Model.PlaneDisposals,
                "cancelling blank leaves part and plane untouched");
        }
        Model.Reset();Model.Options.Blank=true;Model.BlankSelection.Add(Model.AddCurve(351,true));
        Model.FailAt=2;Model.Inherited=true;Model.BlankInherited=true;Model.Run();RolledBack();
        Check(Model.Records.Count==0 && Model.BlankRecords.Count==0 && Model.Inherited && Model.BlankInherited && Model.Report.Contains("Blank Boundaries"),
            "second target failure restores both stores and inheritance");
        Model.Reset();Model.Options.Part=false;Model.Options.Blank=true;Model.BlankSelection.Add(Model.AddCurve(450,false));Model.Run();Success();
        Model.BlankRecords[0].CustomOffset=true; Model.BlankRecords[0].Offset=3;
        Model.Options.Blank=false;Model.Options.Part=true;Model.TouchOtherOnCommit=true;Model.WrongEditorDefaults=true;
        Model.Run();RolledBack();Check(Model.BlankRecords[0].Offset==3 && Model.Records.Count==0,"detect collateral editor change in unselected store");
        Model.Reset(); Model.Parent=new NCGroup {Tag=(Tag)810};Model.ParentBlankCount=2;Model.BlankInherited=true;
        Model.Options.Blank=true;Model.BlankSelection.Add(Model.AddCurve(223,true));Model.Run();
        Check(Model.Severity==2 && Model.Marks==0 && Model.Calls==0 && Model.Report.Contains("Blank Boundaries"),"protect populated inherited blank");
        Model.Reset(); Model.NoBlankEditor=true;Model.AddCurve(225,true);Model.Run();Success();
        Check(Model.Calls==1 && Model.Records.Count==1,"part-only operation with no blank editor");
        Model.Options.Part=false;Model.Options.Blank=true;Model.BlankSelection.Add(Model.AddCurve(226,false));Model.Run();
        Check(Model.Severity==2 && Model.Marks==1 && Model.Calls==1 && Model.Report.Contains("не предоставляет Blank Boundaries"),"missing requested editor identified before CAM mutation");
    }

    static void PlaneSelection()
    {
        Model.Reset(); Select36(); Model.Options.SelectPlane=true;
        Model.Method=PlaneTypes.MethodType.Distance; Model.Distance=5;
        Model.UfReadType=2; Model.Options.Tanto=true; Model.Options.ClosedOutside=true;
        Model.Run(); Success();
        Check(Model.Picks=="PC" && Model.PlaneBuilds==1 && Model.PlaneEvaluations==1 && Model.PlaneDisposals==1,
            "select a plane before curves and evaluate its placement once");
        Check(Model.Commits==0 && Model.Replacements==0 && Model.Records.Count==36,"keep correctly saved native planes");
        Check(Model.Report.Contains("At Distance") && Model.Report.Contains("Выражение смещения в NX: 5") &&
            Model.Report.Contains("плоскость подтверждена для всех новых границ: 36"),"offset plane in result");
        foreach(Record rec in Model.Records)
        {
            var o=rec.Definition.origin; var m=rec.Definition.matrix;
            Check(rec.Definition.plane_type==0 && o[0]==11 && o[1]==-1 && o[2]==33 && m[6]==0 && m[7]==0.6 && m[8]==0.8,
                "all 36 boundaries use the +5 offset along the inclined normal exactly once");
            Check(rec.Tags.Length==1 && rec.Members[0].tool_position==CamToolPosition.CamToolPositionTanto &&
                rec.Side==(Model.Curves[rec.Tags[0]].Closed?BoundarySet.ToolSideTypes.OutsideOrRight:BoundarySet.ToolSideTypes.InsideOrLeft),
                "mixed closed/outside and open/left on one common plane");
        }
        Model.Method=PlaneTypes.MethodType.Bisector; Model.Options.Tanto=false;
        Model.ReferencePoint=new Point3d(-13,0,0); Model.ReferenceNormal=new Vector3d(-4,0,0);
        Model.Run();
        Check(Model.Severity==1 && Model.Marks==1 && Model.PlaneDisposals==Model.PlaneBuilds && Model.Calls==36,"duplicate-only selection leaves no plane or visible undo step");
        Model.AddCurve(999,false);
        Model.AdjustInitialPlane=delegate(Record r) { r.Definition.plane_type=1; };
        Model.Run(); Success();
        var added=Model.Records[36];
        Check(added.Position==BoundarySet.ToolPositionTypes.On && added.Definition.plane_type==0 &&
            added.Definition.origin[0]==-13 && added.Definition.matrix[6]==-1,"bisector with negative normal in ON");
        Check(Model.Replacements==1 && Model.Commits==1 && Model.Records[0].Definition.origin[2]==33 &&
            Model.Report.Contains("Bisector"),"correct new row without changing the earlier offset planes");

        foreach(double distance in new[]{-5.0,0,5.0}) foreach(bool reverse in new[]{false,true})
        {
            Model.Reset(); Model.Options.SelectPlane=true; Model.Method=PlaneTypes.MethodType.Distance;
            Model.Distance=distance; Model.OffsetFlip=reverse; Model.AddCurve(335,false); Model.Run(); Success();
            var data=Model.Records[0].Definition;
            double effective=reverse?-distance:distance;
            Check(Math.Abs(data.origin[1]-(-4+0.6*effective))<1e-9 && Math.Abs(data.origin[2]-(29+0.8*effective))<1e-9 &&
                data.matrix[7]==0.6 && data.matrix[8]==0.8, "signed and reversed offset uses NX result and keeps its normal");
        }

        Model.Reset(); Model.Options.SelectPlane=true; Model.AddCurve(333,false);
        Model.AdjustInitialPlane=delegate(Record r) { r.Definition.origin[0]+=100; };
        Model.Run(); Success(); Check(Model.Replacements==0 && Model.Commits==0,"equivalent in-plane origin");
        foreach(bool flip in new[]{false,true})
        {
            Model.Reset(); Model.AddCurve(334,true); Model.Run(); Success();
            Model.AddCurve(333,false); Model.Options.SelectPlane=true; Model.IgnorePlaneEdits=true;
            Model.AdjustInitialPlane=delegate(Record r) {
                if(flip) { r.Definition.matrix[7]*=-1; r.Definition.matrix[8]*=-1; }
                else r.Definition.origin[2]+=10;
            };
            Model.Run(); RolledBack();
            Check(Model.Replacements==1 && Model.Commits==1 && Model.Records.Count==1 &&
                Model.Records[0].Definition.plane_type==1 && Model.Report.Contains("[333]"),"unpersisted plane offset or flipped normal rolls back");
        }
        foreach(Selection.Response cancel in new[]{Selection.Response.Cancel,Selection.Response.Back})
        {
            Model.Reset(); Model.AddCurve(101,false); Model.Options.SelectPlane=true; Model.PlaneResponse=cancel; Model.Run();
            Check(Model.Picks=="P" && Model.Marks==0 && Model.PlaneDisposals==1 && !Model.PreviewActive && Model.Severity==-1,"cancel removes preview and skips curves");
            Model.Reset(); Model.AddCurve(101,false); Model.Options.SelectPlane=true; Model.CurveResponse=cancel; Model.Run();
            Check(Model.Picks=="PC" && Model.Marks==0 && Model.PlaneDisposals==1 && !Model.PreviewActive && Model.Severity==-1,"cancel subsequent curve selection");
        }
        Model.Reset(); Model.AddCurve(101,false); Model.Options.SelectPlane=true;
        Model.MissingPlane=true; Model.Run();
        Check(Model.Severity==2 && Model.Picks=="P" && Model.Marks==0 && Model.Calls==0 && Model.PlaneDisposals==1,
            "empty constructor result rejected before CAM changes");
        Model.Reset(); Model.AddCurve(101,false); Model.Options.SelectPlane=true; Model.ReferenceNormal=new Vector3d(); Model.Run();
        Check(Model.Severity==2 && Model.PlaneDisposals==1 && !Model.PreviewActive && Model.Calls==0 && Model.Marks==0,
            "invalid solved plane releases the preview before curve selection");
        Model.Reset(); Model.AddCurve(101,false); Model.Run(); Success();
        Check(Model.Picks=="C" && Model.PlaneBuilds==0 && Model.Replacements==0 && Model.Records[0].Definition.plane_type==1,
            "automatic path stays independent of reference geometry");
    }
    public static int Main() { return ManagedTestHost.Run(RunChecks); }
    private static int RunChecks()
    {
        PlaneSelection();
        Model.Reset(); Model.UfReadType=2; Model.Options.Tanto=true; Model.Options.ClosedOutside=true;
        for(int i=0;i<36;i++) Model.AddCurve(1000+i,true);
        Model.Run(); Success();
        Check(Model.Records.Count==36 && Model.Calls==36 && Model.Marks==1 && Model.Undos==0,"36 closed contours with raw UF code 2");
        foreach(Record b in Model.Records) Check(b.Tags.Length==1 && b.Definition.boundary_type==CamBoundaryType.CamBoundaryTypeClosed
            && b.Side==BoundarySet.ToolSideTypes.OutsideOrRight && b.Members[0].tool_position==CamToolPosition.CamToolPositionTanto,"each boundary is Closed/TANTO/Outside");
        Check(Model.Report.Contains("Open: 0; Closed: 36"),"verified closed count");
        Check(Model.Report.Contains("код типа 2 для границ: 36"),"grouped native code diagnostic");
        var saved = SeparatePlanarBoundary.Read(Model.Uf.Cambnd, Model.Op.Tag, CamGeomType.CamPart);
        saved.Boundaries[0].Definition.boundary_type = (CamBoundaryType)17;
        var expected = new Dictionary<Tag, bool>();
        foreach (Tag tag in saved.Sources) expected.Add(tag, true);
        var counts = new System.Text.StringBuilder();
        SeparatePlanarBoundary.VerifySaved(new SeparatePlanarBoundary.Saved(), saved, expected, true, null, counts, "Part Boundaries");
        string summary = counts.ToString();
        int twos = summary.IndexOf("код типа 2 для границ: 35.", StringComparison.Ordinal);
        int seventeen = summary.IndexOf("код типа 17 для границ: 1.", StringComparison.Ordinal);
        Check(twos >= 0 && seventeen > twos, "unknown codes are counted and listed in numeric order");
        Model.Reset(); Select36(); Model.UfReadType=2; Model.WrongClosure=true;
        Model.Options.Tanto=true; Model.Options.ClosedOutside=true; Model.Run(); Success();
        Check(Model.Commits==1 && Model.Report.Contains("Open: 27; Closed: 9"),"correct and reopen mixed boundaries with the same UF code");
        foreach(Record b in Model.Records)
        {
            bool closed=Model.Curves[b.Tags[0]].Closed;
            Check((b.Definition.boundary_type==CamBoundaryType.CamBoundaryTypeClosed)==closed,"persisted mixed shape");
            Check(b.Side==(closed?BoundarySet.ToolSideTypes.OutsideOrRight:BoundarySet.ToolSideTypes.InsideOrLeft),"mixed Outside/Left");
        }
        Model.Reset(); Model.UfReadType=17; Model.AddCurve(401,false); Model.Run(); Success();
        Check(Model.Report.Contains("Open: 1; Closed: 0"),"unmapped code with independently confirmed open geometry in ON");
        foreach(int editorType in new[]{(int)BoundarySet.BoundaryTypes.Open,2})
        {
            Model.Reset(); Model.UfReadType=2; Model.EditorReadType=editorType;
            Model.AddCurve(401,true); Model.Run(); RolledBack();
            Check(Model.Records.Count==0 && Model.Report.Contains("[401]") && Model.Commits==1,"wrong or unknown editor shape must roll back");
        }
        Model.Reset(); Model.UfReadType=2; Select36(); Model.OmitEditorTag=(Tag)136; Model.Run(); RolledBack();
        Check(Model.Records.Count==0 && Model.Report.Contains("Нет тегов [136]"),"missing editor evidence with equal UF count");
        Model.Reset(); Model.UfReadType=(int)CamBoundaryType.CamBoundaryTypeOpen; Model.AddCurve(401,true); Model.Run(); RolledBack();
        Check(Model.Report.Contains("редактор NX подтвердил Closed, CAM вернул CamBoundaryTypeOpen"),"named native/editor conflict");
        RememberedDistance();
        BothTargets();
        EmptyTo36(false,true); EmptyTo36(true,false);
        foreach(bool outside in new[]{false,true}) foreach(bool right in new[]{false,true})
        {
            PreserveAndTanto(outside,right); Mixed36(outside,right);
        }
        Model.Reset(); Select36(); Model.Options.Tanto=true; Model.Options.OpenRight=true;
        Model.WrongEditorDefaults=true; Model.IgnoreOpenSide=true; Model.Run(); RolledBack();
        Check(Model.Records.Count==0 && Model.Report.Contains("OutsideOrRight"),"an unpersisted side on open curves triggers rollback");
        Model.Reset(); Select36(); Model.Inherited=true; Model.DropTag=(Tag)136; Model.Run(); RolledBack();
        Check(Model.Records.Count==0 && Model.Inherited && Model.Report.Contains("ожидалось 36, фактически 35") && Model.Report.Contains("136"),"missing curve diagnosed and original restored");
        Model.Reset(); Select36(); Model.MergeAt=2; Model.Run(); RolledBack(); Check(Model.Report.Contains("101, 102"),"merged rows identified");
        Model.Reset(); Select36(); Model.DuplicateTag=(Tag)104; Model.Run(); RolledBack(); Check(Model.Report.Contains("104] назначена повторно"),"duplicate rejected");
        Model.Reset(); Select36(); Model.SubstituteTag=(Tag)136; Model.Run(); RolledBack();
        Check(Model.Report.Contains("ожидалось 36, фактически 36") && Model.Report.Contains("136"),"equal count with wrong source is rejected");
        Model.Reset(); Select36(); Model.FailAt=3; Model.Run(); RolledBack(); Check(Model.Calls==3 && Model.Records.Count==0 && Model.Report.Contains("3/36"),"partway failure");
        Model.Reset(); Select36(); Model.WrongClosure=true; Model.IgnoreShapeEdits=true; Model.Run(); RolledBack();
        Check(Model.Report.Contains("Open") && Model.Report.Contains("Closed"),"persistently wrong known closure rejected");
        Model.Reset(); Select36(); Model.WrongEditorDefaults=true; Model.DropOnCommit=true; Model.Run(); RolledBack();
        Check(Model.Commits==1 && Model.Report.Contains("Итоговое число сохранённых границ: 35"),"verify after optional edit commit");
        Model.Reset(); Model.AddCurve(501,true); Model.Run(); Success(); Model.Records[0].Settings.has_stock=1; Model.Records[0].Settings.stock=9;
        Model.AddCurve(502,false); Model.CorruptOld=true; Model.Calls=0; Model.Run(); RolledBack();
        Check(Model.Records.Count==1 && Model.Records[0].Settings.stock==9,"original active overrides verified on rollback");
        Model.Reset(); Model.AddCurve(77,true); Model.Options=null; Model.Run();
        Check(Model.Marks==0 && Model.Calls==0 && Model.Severity==-1,"cancel before mutations");
        Console.WriteLine("Planar transaction checks passed: "+checks);
        return 0;
    }
}
#endif
