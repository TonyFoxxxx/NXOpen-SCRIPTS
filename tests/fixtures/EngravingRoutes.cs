using System;
using System.Collections.Generic;
using P=EngravingCenterlines.P;
using B=EngravingCenterlines.Bezier;
using R=EngravingCenterlines.Region;
public class RoutingChecks
{
    static int scenarios;
    static List<P> Poly(params double[] v) { var p=new List<P>();for(int i=0;i<v.Length;i+=2)p.Add(new P(v[i],v[i+1]));return p; }
    static void Require(bool ok,string message) { if(!ok)throw new Exception(message); }
    static List<P> Plus() { return Poly(-2,-.2,-.2,-.2,-.2,-2,.2,-2,.2,-.2,2,-.2,2,.2,.2,.2,.2,2,-.2,2,-.2,.2,-2,.2); }
    static List<P> K() { double t=.2*Math.Sqrt(2),c=.2/Math.Sqrt(2);return Poly(-.2,-2.2,.2,-2.2,.2,-.2-t,2-c,-2-c,2+c,-2+c,t,0,2+c,2-c,2-c,2+c,.2,.2+t,.2,2.2,-.2,2.2); }
    static List<List<P>> LetterP() { return new List<List<P>>{Poly(-.2,-2.2,.2,-2.2,.2,-.2,2.2,-.2,2.2,2.2,-.2,2.2),Poly(.2,.2,1.8,.2,1.8,1.8,.2,1.8)}; }
    static List<List<P>> Four()
    { double m=.2*(1+Math.Sqrt(2));return new List<List<P>>{Poly(.2,2+m,-2-m,-.2,-.2,-.2,-.2,-2.2,.2,-2.2,.2,-.2,1.2,-.2,1.2,.2,.2,.2),Poly(-.2,2-m,-2+m,.2,-.2,.2)}; }
    static List<P> Circle(double radius) { var p=new List<P>();for(int i=0;i<360;i++){double a=2*Math.PI*i/360;p.Add(new P(radius*Math.Cos(a),radius*Math.Sin(a)));}return p; }
    static void Arc(List<P> p,double r,double start,double end) { for(int i=0;i<=240;i++){double a=start+(end-start)*i/240;p.Add(new P(r*Math.Cos(a),r*Math.Sin(a)));} }
    static List<List<P>> Phi()
    {
        double a=Math.Acos(.2/2.2),b=Math.Acos(.2/1.8);var outer=Poly(-.2,2.7,.2,2.7);
        Arc(outer,2.2,a,-a);outer.Add(new P(.2,-2.7));outer.Add(new P(-.2,-2.7));Arc(outer,2.2,-Math.PI+a,-Math.PI-a);
        var right=new List<P>();Arc(right,1.8,b,-b);var left=new List<P>();foreach(var p in right)left.Add(new P(-p.X,p.Y));return new List<List<P>>{outer,right,left};
    }
    static List<List<P>> Transform(List<List<P>> loops,double angle,double scale,bool mirror)
    {
        var result=new List<List<P>>();foreach(var loop in loops){var p=new List<P>();foreach(var v in loop){double x=mirror?-v.X:v.X;p.Add(new P(17+scale*(x*Math.Cos(angle)-v.Y*Math.Sin(angle)),-9+scale*(x*Math.Sin(angle)+v.Y*Math.Cos(angle))));}result.Add(p);}return result;
    }
    static double PointSegment(P p,P a,P b)
    { P v=b-a;double t=P.Dot(p-a,v)/P.Dot(v,v);t=Math.Max(0,Math.Min(1,t));return (p-a-v*t).Length; }
    static double Segments(P a,P b,P c,P d)
    {
        P u=b-a,v=d-c;double den=P.Cross(u,v);
        if(Math.Abs(den)>1e-25){double s=P.Cross(c-a,v)/den,t=P.Cross(c-a,u)/den;if(s>=0&&s<=1&&t>=0&&t<=1)return 0;}
        return Math.Min(Math.Min(PointSegment(a,c,d),PointSegment(b,c,d)),Math.Min(PointSegment(c,a,b),PointSegment(d,a,b)));
    }
    static EngravingCenterlines.Result Check(string name,List<List<P>> loops,int paths,int closed=0,double unit=1)
    {
        var r=new R(loops);var source=EngravingCenterlines.Build(r,.002*unit);var result=EngravingRoutes.Build(source,r,.02*unit,.002*unit);
        Require(result.Curves.Count==paths,name+" paths expected "+paths+" got "+result.Curves.Count);
        int loopsCount=0;var sampled=new List<List<P>>();
        for(int i=0;i<result.Curves.Count;i++)
        {
            var path=result.Curves[i];var points=new List<P>{path[0].A};if(result.Strokes[i].Closed)loopsCount++;
            for(int j=0;j<path.Count;j++)
            {
                if(j>0)Require((path[j-1].D-path[j].A).Length<1e-10,"gap inside chain");
                int n=path[j].Line?1:64;for(int k=1;k<=n;k++){P p=path[j].At(k/(double)n);Require(r.Inside(p),name+" outside face");points.Add(p);}
            }
            if(result.Strokes[i].Closed)Require((points[0]-points[points.Count-1]).Length<1e-10,"false closure");
            sampled.Add(points);
        }
        Require(loopsCount==closed,name+" closed count");
        for(int i=0;i<sampled.Count;i++)for(int j=i+1;j<sampled.Count;j++)
        {
            double min=double.MaxValue;var a=sampled[i];var b=sampled[j];
            for(int m=1;m<a.Count;m++)for(int n=1;n<b.Count;n++)min=Math.Min(min,Segments(a[m-1],a[m],b[n-1],b[n]));
            Require(min>=.019*unit,name+" insufficient separation: "+min);
        }
        scenarios++;return result;
    }
    static void Reject(Action action,string label) { bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,label);scenarios++; }
    public static void Main()
    {
        Console.OutputEncoding=new System.Text.UTF8Encoding(false);
        try { Run(); } catch(Exception e) {Console.WriteLine(e);Environment.Exit(1);}
    }
    static void Run()
    {
        for(int i=0;i<12;i++)
        {
            double angle=i*Math.PI/6;
            Check("plus "+i,Transform(new List<List<P>>{Plus()},angle,1,false),2);
            Check("K "+i,Transform(new List<List<P>>{K()},angle,1,false),2);
            Check("P "+i,Transform(LetterP(),angle,1,false),1);
            Check("4 "+i,Transform(Four(),angle,1,false),2);
        }
        Check("Phi",Phi(),3);
        Check("H: distinct junctions",new List<List<P>>{Poly(-1.2,-2,-.8,-2,-.8,-.2,.8,-.2,.8,-2,1.2,-2,1.2,2,.8,2,.8,.2,-.8,.2,-.8,2,-1.2,2)},3);
        Check("E: distinct junctions",new List<List<P>>{Poly(-.2,-2.2,2,-2.2,2,-1.8,.2,-1.8,.2,-.2,1.5,-.2,1.5,.2,.2,.2,.2,1.8,2,1.8,2,2.2,-.2,2.2)},2);
        Check("T",new List<List<P>>{Poly(-.2,-2,.2,-2,.2,1.8,2,1.8,2,2.2,-2,2.2,-2,1.8,-.2,1.8)},2);
        Check("mirrored K",Transform(new List<List<P>>{K()},.42,1,true),2);
        Check("inch plus",Transform(new List<List<P>>{Plus()},.42,1/25.4,false),2,0,1/25.4);
        Check("ring",new List<List<P>>{Circle(2.2),Circle(1.8)},1,1);
        var dot=Check("dot",new List<List<P>>{Circle(.3)},0);Require(dot.Circles.Count==1&&Math.Abs(dot.Circles[0].Radius-.15)<1e-10,"D/2 changed");
        var region=new R(new List<List<P>>{Poly(-3,-3,3,-3,3,3,-3,3)});
        var bad=new EngravingCenterlines.Result();
        bad.Curves.Add(new List<B>{new B{A=new P(-1,0),D=new P(1,0),Line=true}});bad.Strokes.Add(new EngravingCenterlines.Stroke());
        bad.Curves.Add(new List<B>{new B{A=new P(0,-1),D=new P(0,1),Line=true}});bad.Strokes.Add(new EngravingCenterlines.Stroke());
        Reject(delegate{EngravingRoutes.Validate(bad,region,.002,.0002);},"interior crossing accepted");
        var self=new EngravingCenterlines.Result();self.Strokes.Add(new EngravingCenterlines.Stroke());
        self.Curves.Add(new List<B>{new B{A=new P(-1,-1),D=new P(1,1),Line=true},new B{A=new P(1,1),D=new P(-1,1),Line=true},new B{A=new P(-1,1),D=new P(1,-1),Line=true}});
        Reject(delegate{EngravingRoutes.Validate(self,region,.002,.0002);},"self crossing accepted");
        var tiny=new R(Transform(new List<List<P>>{Plus()},0,.02,false));
        Reject(delegate{EngravingRoutes.Build(EngravingCenterlines.Build(tiny,.00002),tiny,.02,.00002);},"gap consuming thin stroke accepted");
        Console.WriteLine(scenarios+" nonintersecting engraving routing scenarios passed");
    }
}
