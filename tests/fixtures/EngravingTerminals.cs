using System;
using System.Collections.Generic;
using P=EngravingCenterlines.P;
using R=EngravingCenterlines.Region;
public class TerminalChecks
{
    static int scenarios;
    static void Require(bool ok,string message) { if(!ok)throw new Exception(message); }
    static List<P> Poly(params double[] xy) { var p=new List<P>();for(int i=0;i<xy.Length;i+=2)p.Add(new P(xy[i],xy[i+1]));return p; }
    static P Transform(P p,double angle,double scale,bool mirror)
    { double x=mirror?-p.X:p.X;return new P(17+scale*(x*Math.Cos(angle)-p.Y*Math.Sin(angle)),-9+scale*(x*Math.Sin(angle)+p.Y*Math.Cos(angle))); }
    static List<P> Transform(List<P> p,double angle,double scale,bool mirror)
    { var q=new List<P>();foreach(P v in p)q.Add(Transform(v,angle,scale,mirror));if(mirror)q.Reverse();return q; }
    static List<P> Ends(EngravingCenterlines.Result result)
    { var p=new List<P>();foreach(var path in result.Curves){p.Add(path[0].A);p.Add(path[path.Count-1].D);}return p; }
    static void Match(List<P> actual,List<P> expected,double error,string label)
    {
        Require(actual.Count==expected.Count,label+" endpoint count");var used=new HashSet<int>();
        foreach(P p in expected){int best=-1;double d=double.MaxValue;for(int i=0;i<actual.Count;i++)if(!used.Contains(i)&&(actual[i]-p).Length<d){d=(actual[i]-p).Length;best=i;}
            Require(d<=error,label+" endpoint error "+d+" expected "+p.X+","+p.Y);used.Add(best);}
    }
    static void EndCase(string name,List<P> outline,List<P> expected,double angle,double scale,bool mirror,double error)
    {
        var region=new R(new List<List<P>>{Transform(outline,angle,scale,mirror)});
        var raw=EngravingCenterlines.Build(region,.002*scale);
        Match(Ends(raw),Transform(expected,angle,scale,mirror),error*scale,name);
        var result=EngravingRoutes.Build(raw,region,.02*scale,.002*scale);
        Match(Ends(result),Ends(raw),1e-10*scale,name+" route changed terminal");
        foreach(var path in result.Curves)foreach(var b in path)for(int k=0;k<=32;k++)Require(region.Inside(b.At(k/32.0)),name+" outside face");
        scenarios++;
    }
    static List<P> Capsule()
    {
        var p=new List<P>();for(int i=0;i<=180;i++){double a=-Math.PI/2+i*Math.PI/180;p.Add(new P(2+.2*Math.Cos(a),.2*Math.Sin(a)));}
        for(int i=0;i<=180;i++){double a=Math.PI/2+i*Math.PI/180;p.Add(new P(-2+.2*Math.Cos(a),.2*Math.Sin(a)));}return p;
    }
    static List<P> ArcBand()
    {
        var p=new List<P>();for(int i=0;i<=360;i++){double a=i*Math.PI/360;p.Add(new P(2.2*Math.Cos(a),2.2*Math.Sin(a)));}
        for(int i=360;i>=0;i--){double a=i*Math.PI/360;p.Add(new P(1.8*Math.Cos(a),1.8*Math.Sin(a)));}return p;
    }
    static void Run()
    {
        foreach(double angle in new double[]{0,.37,1.8})foreach(double scale in new double[]{1,1/25.4})foreach(bool mirror in new bool[]{false,true})
        {
            foreach(double r in new double[]{.035,.2,.5})foreach(double skew in new double[]{0,.8,-1.3})
                EndCase("flat/slanted cap",Poly(-2-skew*r,-r,2-skew*r,-r,2+skew*r,r,-2+skew*r,r),Poly(-2+r,0,2-r,0),angle,scale,mirror,1e-7);
            // Two different stroke widths in one face: both ends need their own half-width.
            EndCase("different local widths",Poly(-2,-.1,0,-.1,0,-.3,2,-.3,2,.3,0,.3,0,.1,-2,.1),Poly(-1.9,0,1.7,0),angle,scale,mirror,.003);
            EndCase("tapered local width",Poly(-2,-.18,2,-.22,2,.22,-2,.18),Poly(-1.8/.99,0,1.8/1.01,0),angle,scale,mirror,1e-7);
            EndCase("short straight stroke",Poly(-.3,-.2,.3,-.2,.3,.2,-.3,.2),Poly(-.1,0,.1,0),angle,scale,mirror,1e-7);
            EndCase("short arm at sharp bend",Poly(-2,-.2,.6,-.2,.6,2,.2,2,.2,.2,-2,.2),Poly(-1.8,0,.4,1.8),angle,scale,mirror,1e-7);
            EndCase("rounded caps already inset",Capsule(),Poly(-2,0,2,0),angle,scale,mirror,.003);
            double a=Math.Atan(.2/2);
            EndCase("curved terminals",ArcBand(),Poly(2*Math.Cos(a),2*Math.Sin(a),-2*Math.Cos(a),2*Math.Sin(a)),angle,scale,mirror,.003);
            foreach(double shear in new double[]{0,.268})foreach(double ratio in new double[]{1,1.1})foreach(int samples in new int[]{1,17})
            {
                var polygon=Poly(-.3*ratio-.3*shear,-.3,.3*ratio-.3*shear,-.3,.3*ratio+.3*shear,.3,-.3*ratio+.3*shear,.3);
                var p=new List<P>();for(int i=0;i<4;i++)for(int j=0;j<samples;j++)p.Add(polygon[i]+(polygon[(i+1)%4]-polygon[i])*(j/(double)samples));
                var region=new R(new List<List<P>>{Transform(p,angle,scale,mirror)});var result=EngravingCenterlines.Build(region,.002*scale);
                Require(result.Circles.Count==1&&result.Curves.Count==0,"square dot skeleton");
                Require((result.Circles[0].Center-new P(17,-9)).Length<1e-10,"square dot center");
                double radius=.15*Math.Min(1,ratio/Math.Sqrt(1+shear*shear))*scale;
                Require(Math.Abs(result.Circles[0].Radius-radius)<1e-10,"square dot half-width diameter");
                var routed=EngravingRoutes.Build(result,region,.02*scale,.002*scale);
                Require(routed.Circles.Count==1&&routed.Curves.Count==0,"square dot routing");scenarios++;
            }
        }
        var squareCounter=new R(new List<List<P>>{Poly(-.6,-.6,.6,-.6,.6,.6,-.6,.6),Poly(-.4,-.4,.4,-.4,.4,.4,-.4,.4)});
        var counter=EngravingCenterlines.Build(squareCounter,.002);
        Require(counter.Circles.Count==0&&counter.Strokes.Count==1&&counter.Strokes[0].Closed,"square counter mistaken for punctuation");scenarios++;
        Console.WriteLine(scenarios+" engraving-terminal scenarios passed");
    }
    public static void Main()
    { System.Threading.Thread.CurrentThread.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;Console.OutputEncoding=new System.Text.UTF8Encoding(false);try{Run();}catch(Exception ex){Console.WriteLine(ex);Environment.Exit(1);} }
}
