using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using NXOpen;
using NXOpen.UF;

namespace NXOpen
{
    public enum Tag : uint { Null=0, Sample=0xf0000001 }
}
namespace NXOpen.UF
{
    public class UFParam
    {
        public enum Type { TypeLogical,TypeChar,TypeShort,TypeInt,TypePointer,TypeFloat,TypeDouble,TypeByte,TypeDate,TypeTag,TypeString,Type2d,Type3d,TypeVlaReal,TypeVlaInt,TypeVlaTag,TypeDoubleLength,Type2dLength,Type3dLength,TypeVlaLength,TypeVlaString,TypeObject,TypeVlaLengthComposite,TypeVlaFloat,TypeLast }
        public int Calls, Mode;
        private void Read(Tag tag,int index) { if(tag!=Tag.Sample||index!=105)throw new Exception("wrong target");Calls++;if(Mode==3)throw new InvalidOperationException("API read failed"); }
        public void AskLogicalValue(Tag t,int i,out bool v){Read(t,i);v=true;}
        public void AskIntValue(Tag t,int i,out int v){Read(t,i);v=-123;}
        public void AskDoubleValue(Tag t,int i,out double v){Read(t,i);v=0.00123456789012345;}
        public void AskTagValue(Tag t,int i,out Tag v){Read(t,i);v=Tag.Sample;}
        public void AskStrValue(Tag t,int i,out string v){Read(t,i);v="гравировка";}
        public void Ask2dValue(Tag t,int i,double[] v){Read(t,i);v[0]=1.2;v[1]=2.3;}
        public void Ask3dValue(Tag t,int i,double[] v){Read(t,i);v[0]=1.2;v[1]=2.3;v[2]=3.4;}
        public void AskDoubleVla(Tag t,int i,out int n,out double[] v){Read(t,i);n=Mode==1?3:Mode==2?0:2;v=Mode==2?null:new double[]{0.1,0.2};}
        public void AskIntVla(Tag t,int i,out int n,out int[] v){Read(t,i);n=Mode==1?3:Mode==2?0:2;v=Mode==2?null:new int[]{4,5};}
        public void AskTagVla(Tag t,int i,out int n,out Tag[] v){Read(t,i);n=Mode==1?3:Mode==2?0:2;v=Mode==2?null:new Tag[]{Tag.Sample,Tag.Null};}
    }
}
namespace NXOpen.CAM
{
    public enum Choice { Open=4,Closed=7 }
    public class Child
    {
        public int Value {get;set;}
        public object Parent {get;set;}
        public override bool Equals(object o){return true;}
        public override int GetHashCode(){return 0;}
    }
    public class MockList
    {
        public Child[] GetContents(){return new Child[]{new Child{Value=3},new Child{Value=5}};}
        public void Clear(){throw new Exception("mutation");}
    }
    public class Root
    {
        public int Writes,UnexpectedCalls;
        public double Feed {get{return 12.3456789012345;}set{Writes++;}}
        public Choice Mode {get{return Choice.Closed;}}
        public bool Enabled {get{return false;}}
        public string Empty {get{return null;}}
        public string Escaped {get{return "К\"\\\r\n\t\u0000\ud83d\ude00";}}
        public Child First {get;set;}
        public Child Second {get;set;}
        public Child Alias {get;set;}
        public MockList Items {get{return new MockList();}}
        public string Failed {get{throw new InvalidOperationException("unavailable setting");}}
        public object PreviewBuilder {get{UnexpectedCalls++;throw new Exception("preview called");}}
        public object ToolPathEditor {get{UnexpectedCalls++;throw new Exception("editor called");}}
        public string this[int i] {get{UnexpectedCalls++;throw new Exception("indexer called");}}
        public object GetInputIpw(){UnexpectedCalls++;throw new Exception("expensive method called");}
        public void Commit(){Writes++;}
    }
}
public static class DiagnosticChecks
{
    private static int checks;
    private static void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
    private static void Reject(Action action){bool failed=false;try{action();}catch{failed=true;}Check(failed,"expected rejection");}
    public static int Main() { return ManagedTestHost.Run(RunChecks); }
    private static int RunChecks()
    {
        Console.OutputEncoding=new UTF8Encoding(false);
        CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("ru-RU");
        UFParam api=new UFParam();
        Check((bool)EngravingParamReader.Read(api,Tag.Sample,105,UFParam.Type.TypeLogical),"bool");
        Check((int)EngravingParamReader.Read(api,Tag.Sample,105,UFParam.Type.TypeInt)==-123,"integer");
        foreach(UFParam.Type t in new[]{UFParam.Type.TypeDouble,UFParam.Type.TypeDoubleLength})Check((double)EngravingParamReader.Read(api,Tag.Sample,105,t)==0.00123456789012345,"double precision");
        Check((Tag)EngravingParamReader.Read(api,Tag.Sample,105,UFParam.Type.TypeTag)==Tag.Sample,"unsigned tag");
        Check((string)EngravingParamReader.Read(api,Tag.Sample,105,UFParam.Type.TypeString)=="гравировка","unicode");
        foreach(UFParam.Type t in new[]{UFParam.Type.Type2d,UFParam.Type.Type2dLength})Check(((double[])EngravingParamReader.Read(api,Tag.Sample,105,t))[1]==2.3,"2d");
        foreach(UFParam.Type t in new[]{UFParam.Type.Type3d,UFParam.Type.Type3dLength})Check(((double[])EngravingParamReader.Read(api,Tag.Sample,105,t))[2]==3.4,"3d");
        foreach(UFParam.Type t in new[]{UFParam.Type.TypeVlaReal,UFParam.Type.TypeVlaLength,UFParam.Type.TypeVlaInt,UFParam.Type.TypeVlaTag})
        {
            api.Mode=0;Check(((Array)EngravingParamReader.Read(api,Tag.Sample,105,t)).Length==2,"array");
            api.Mode=1;Reject(delegate{EngravingParamReader.Read(api,Tag.Sample,105,t);});
            api.Mode=2;Check(((Array)EngravingParamReader.Read(api,Tag.Sample,105,t)).Length==0,"empty array");
        }
        int calls=api.Calls;
        foreach(UFParam.Type t in new[]{UFParam.Type.TypePointer,UFParam.Type.TypeObject,UFParam.Type.TypeVlaString,UFParam.Type.TypeVlaLengthComposite,UFParam.Type.TypeVlaFloat,UFParam.Type.TypeChar,UFParam.Type.TypeShort,UFParam.Type.TypeByte,UFParam.Type.TypeDate,UFParam.Type.TypeFloat})
            Reject(delegate{EngravingParamReader.Read(api,Tag.Sample,105,t);});
        Check(api.Calls==calls,"unsupported type invoked unsafe reader");
        api.Mode=3;Reject(delegate{EngravingParamReader.Read(api,Tag.Sample,105,UFParam.Type.TypeDouble);});
        Reject(delegate{EngravingParamReader.Count(-1,null);});Reject(delegate{EngravingParamReader.Count(1,null);});
        Reject(delegate{EngravingParamReader.Count(0,new int[]{1});});
        EngravingParamReader.Count(0,null);checks++;

        var root=new NXOpen.CAM.Root();root.First=new NXOpen.CAM.Child{Value=11,Parent=root};root.Second=new NXOpen.CAM.Child{Value=12};root.Alias=root.First;
        var report=new EngravingReport("Diagnostic test V1");
        var tree=new EngravingTree(report,delegate(object o){return o is Tag?"Tag="+((uint)(Tag)o).ToString(CultureInfo.InvariantCulture):null;});
        tree.Read("Operation",root);
        Check(root.Writes==0&&root.UnexpectedCalls==0,"unsafe getter, setter or method invoked");
        Check(report.Incomplete==1,"missing field not explicitly reported");
        Check(report.ToString().Contains("= 11")&&report.ToString().Contains("= 12"),"reference identity collapsed distinct equal objects");
        Check(report.ToString().Contains("Operation.Items.Items[1].Value = 5"),"list data not collected");
        Check(report.ToString().Contains(" -> Operation"),"cycle/alias not represented");
        Check(tree.Value(0.0123)=="0.0123","locale-dependent number");
        Check(tree.Value(new double[]{1,2}).Contains("1, 2"),"array formatting");
        var chain=new NXOpen.CAM.Child();var cursor=chain;for(int i=0;i<30;i++){cursor.Parent=new NXOpen.CAM.Child();cursor=(NXOpen.CAM.Child)cursor.Parent;}
        var limited=new EngravingReport("limits");new EngravingTree(limited,delegate(object o){return null;}).Read("deep",chain);Check(limited.Incomplete>0,"depth silently truncated");
        int destroyed=0;
        EngravingTree.WithBuilder(delegate{return root;},delegate(object o){Check(o==root,"wrong builder");},delegate(object o){destroyed++;});Check(destroyed==1,"success cleanup");
        Reject(delegate{EngravingTree.WithBuilder(delegate{return root;},delegate(object o){throw new Exception("read failed");},delegate(object o){destroyed++;});});Check(destroyed==2,"failure cleanup");
        Reject(delegate{EngravingTree.WithBuilder(delegate{throw new Exception("create failed");},delegate(object o){},delegate(object o){destroyed++;});});Check(destroyed==2,"cleanup called without builder");
        Reject(delegate{EngravingTree.WithBuilder(delegate{return null;},delegate(object o){},delegate(object o){destroyed++;});});Check(destroyed==2,"null cleanup");
        bool both=false;try{EngravingTree.WithBuilder(delegate{return root;},delegate(object o){throw new Exception("read failed");},delegate(object o){throw new Exception("destroy failed");});}catch(AggregateException e){both=e.InnerExceptions.Count==2;}Check(both,"lost one of read/cleanup exceptions");
        Type dialog=typeof(EngravingReportWindow).GetNestedType("Native",BindingFlags.NonPublic).GetNestedType("OpenFileName",BindingFlags.NonPublic);
        int dialogSize=Marshal.SizeOf(dialog);Check(dialogSize==(IntPtr.Size==8?152:88),"OPENFILENAME ABI");
        Check(Marshal.OffsetOf(dialog,"File").ToInt32()==(IntPtr.Size==8?48:28),"OPENFILENAME file buffer offset");
        object dialogData=Activator.CreateInstance(dialog);IntPtr memory=Marshal.AllocHGlobal(dialogSize);
        try{Marshal.StructureToPtr(dialogData,memory,false);Marshal.DestroyStructure(memory,dialog);checks++;}finally{Marshal.FreeHGlobal(memory);}
        report.Record("sample","number",0.00123456789012345,"large_tag",(uint)Tag.Sample,"nan",Double.NaN,"infinity",Double.PositiveInfinity);
        report.Record("parameter","index",105,"value",new string('Ж',120000));
        report.Finish();
        Console.Error.WriteLine(checks+" diagnostic checks passed");Console.Write(report.ToJson());
        return 0;
    }
}
