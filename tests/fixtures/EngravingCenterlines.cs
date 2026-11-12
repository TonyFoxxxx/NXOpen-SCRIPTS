using System;
using System.Collections.Generic;
using System.Globalization;
using P = EngravingCenterlines.P;
using R = EngravingCenterlines.Region;
public class CenterlineChecks
{
    static int scenarios;
    static void Require(bool value,string message) { if(!value)throw new Exception(message); }
    static List<P> Poly(params double[] xy) { var p=new List<P>();for(int i=0;i<xy.Length;i+=2)p.Add(new P(xy[i],xy[i+1]));return p; }
    static List<P> Circle(double r,int n=360) { var p=new List<P>();for(int i=0;i<n;i++){double a=2*Math.PI*i/n;p.Add(new P(r*Math.Cos(a),r*Math.Sin(a)));}return p; }
    static List<P> ArcBand(double r,double w,double angle)
    {
        var p=new List<P>();for(int i=0;i<=300;i++){double a=angle+(2*Math.PI-2*angle)*i/300;p.Add(new P((r+w)*Math.Cos(a),(r+w)*Math.Sin(a)));}
        for(int i=300;i>=0;i--){double a=angle+(2*Math.PI-2*angle)*i/300;p.Add(new P((r-w)*Math.Cos(a),(r-w)*Math.Sin(a)));}return p;
    }
    static List<P> Plus() { return Poly(-2,-.2,-.2,-.2,-.2,-2,.2,-2,.2,-.2,2,-.2,2,.2,.2,.2,.2,2,-.2,2,-.2,.2,-2,.2); }
    static void AddArc(List<P> p,double r,double start,double end) { for(int i=0;i<=240;i++){double a=start+(end-start)*i/240;p.Add(new P(r*Math.Cos(a),r*Math.Sin(a)));} }
    static List<List<P>> Phi()
    {
        double a=Math.Acos(.2/2.2),b=Math.Acos(.2/1.8);
        var outer=new List<P>{new P(-.2,2.7),new P(.2,2.7)};
        AddArc(outer,2.2,a,-a);outer.Add(new P(.2,-2.7));outer.Add(new P(-.2,-2.7));AddArc(outer,2.2,-Math.PI+a,-Math.PI-a);
        var right=new List<P>();AddArc(right,1.8,b,-b);var left=new List<P>();foreach(var p in right)left.Add(new P(-p.X,p.Y));
        return new List<List<P>>{outer,right,left};
    }
    static List<P> Transform(List<P> p,double angle,double scale,double x,double y,bool mirror=false)
    {
        var q=new List<P>();double c=Math.Cos(angle),s=Math.Sin(angle);foreach(var a in p){double xx=mirror?-a.X:a.X;q.Add(new P(x+scale*(c*xx-s*a.Y),y+scale*(s*xx+c*a.Y)));}return q;
    }
    static EngravingCenterlines.Result Check(List<List<P>> loops,int strokes,int closed,int circles,double tolerance=.002)
    {
        var region=new R(loops);var result=EngravingCenterlines.Build(region,tolerance);
        Require(result.Strokes.Count==strokes,"stroke count: expected "+strokes+", got "+result.Strokes.Count);
        Require(result.Curves.Count==strokes,"unfitted strokes");Require(result.Circles.Count==circles,"circle count");
        int nc=0;var seen=new HashSet<string>();
        for(int i=0;i<result.Strokes.Count;i++)
        {
            var path=result.Curves[i];if(result.Strokes[i].Closed)nc++;
            Require(path.Count>0,"empty path");
            for(int j=0;j<path.Count;j++)
            {
                var curve=path[j];Require((curve.D-curve.A).Length>tolerance*.01,"zero length");
                if(j>0)Require((path[j-1].D-curve.A).Length<1e-10,"path gap");
                string forward="",reverse="";
                for(int k=0;k<=64;k++)
                {
                    P p=curve.At(k/64.0);Require(!double.IsNaN(p.X)&&!double.IsInfinity(p.Y),"non-finite curve");
                    Require(region.Inside(p),"fitted curve outside source face");
                    if(k%8==0){string t=p.X.ToString("F7",CultureInfo.InvariantCulture)+","+p.Y.ToString("F7",CultureInfo.InvariantCulture)+";";forward+=t;reverse=t+reverse;}
                }
                Require(seen.Add(StringComparer.Ordinal.Compare(forward,reverse)<0?forward:reverse),"duplicate/reversed segment");
            }
            if(result.Strokes[i].Closed)Require((path[0].A-path[path.Count-1].D).Length<1e-10,"false closure");
        }
        Require(nc==closed,"closed path count");
        foreach(var circle in result.Circles)for(int i=0;i<32;i++){double a=i*Math.PI/16;Require(region.Inside(circle.Center+new P(Math.Cos(a),Math.Sin(a))*circle.Radius),"circle outside face");}
        scenarios++;return result;
    }
    static void Reject(Action action,string label) { bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,label);scenarios++; }
    public static void Main()
    {
        System.Threading.Thread.CurrentThread.CurrentCulture=CultureInfo.InvariantCulture;
        var rectangle=Check(new List<List<P>>{Poly(-2,-.2,2,-.2,2,.2,-2,.2)},1,0,0);
        Require(rectangle.Curves[0].Count==1&&rectangle.Curves[0][0].Line,"straight stroke not recognized");
        foreach(var p in rectangle.Strokes[0].Points)Require(Math.Abs(p.Y)<.004,"rectangle off center");
        var ring=Check(new List<List<P>>{Circle(2.2),Circle(1.8)},1,1,0);
        foreach(var b in ring.Curves[0])for(int k=0;k<=64;k++)Require(Math.Abs(b.At(k/64.0).Length-2)<.004,"ring radial accuracy");
        Check(new List<List<P>>{ArcBand(2,.2,Math.PI*.2)},1,0,0);
        Check(new List<List<P>>{Poly(-2,-2,2,-2,2,-1.6,-1.6,-1.6,-1.6,2,-2,2)},1,0,0);
        Check(new List<List<P>>{Poly(-1.2,-2,1.2,-2,1.2,2,-1.2,2),Poly(-.8,-1.6,.8,-1.6,.8,-.2,-.8,-.2),Poly(-.8,.2,.8,.2,.8,1.6,-.8,1.6)},3,0,0);
        var phi=Check(Phi(),5,0,0);
        double ymin=100,ymax=-100;foreach(var path in phi.Strokes)foreach(var p in path.Points){ymin=Math.Min(ymin,p.Y);ymax=Math.Max(ymax,p.Y);}
        Require(ymin< -2.45&&ymax>2.45,"short Phi stems were pruned");
        var shortPlus=Plus();for(int i=0;i<shortPlus.Count;i++){P p=shortPlus[i];shortPlus[i]=new P(Math.Abs(p.X)>1?Math.Sign(p.X)*.6:p.X,Math.Abs(p.Y)>1?Math.Sign(p.Y)*.6:p.Y);}
        Check(new List<List<P>>{shortPlus},4,0,0);
        for(int i=0;i<12;i++)
        {
            Check(new List<List<P>>{Transform(Plus(),i*Math.PI/6,1,15,-7)},4,0,0);
            Check(new List<List<P>>{Transform(Poly(-2,-.2,2,-.2,2,.2,-2,.2),i*Math.PI/6,1,15,-7)},1,0,0);
        }
        Check(new List<List<P>>{Transform(Plus(),.7,1,0,0,true)},4,0,0);
        Check(new List<List<P>>{Transform(Plus(),.7,1/25.4,0,0)},4,0,0,.002/25.4);
        Check(new List<List<P>>{Transform(Plus(),.7,1,10000,-20000)},4,0,0);
        var dot=Check(new List<List<P>>{Circle(.3,120)},0,0,1);
        Require(dot.Circles[0].Center.Length<1e-12&&Math.Abs(dot.Circles[0].Radius-.15)<1e-12,"D/2 dot convention");
        var inch=Check(new List<List<P>>{Transform(Circle(.3,120),.4,1/25.4,2,-3)},0,0,1,.002/25.4);
        Require((inch.Circles[0].Center-new P(2,-3)).Length<1e-10&&Math.Abs(inch.Circles[0].Radius-.15/25.4)<1e-10,"inch dot");
        var reverse=Circle(2.2);reverse.Reverse();Check(new List<List<P>>{reverse,Circle(1.8)},1,1,0);
        var regions=EngravingCenterlines.Regions(new List<List<P>>{Circle(1.8),Circle(2.2),Circle(.3)});
        Require(regions.Count==2&&regions[0].Loops.Count==2&&regions[1].Loops.Count==1,"nested islands/holes");scenarios++;
        var separate=EngravingCenterlines.Regions(new List<List<P>>{Circle(.3),Transform(Circle(.3),0,1,3,0)});
        Require(separate.Count==2,"disjoint letters joined");scenarios++;
        Reject(delegate{EngravingCenterlines.Build(new R(new List<List<P>>{Poly(0,0,5,0,5,.001,0,.001)}),.002);},"under-resolved narrow face not rejected");
        Reject(delegate{new R(new List<List<P>>{Poly(0,0,1,0,2,0)});},"zero-area face not rejected");
        var hole=new R(new List<List<P>>{Circle(2.2),Circle(1.8)});
        Require(!hole.SegmentInside(new P(-2,0),new P(2,0),.01),"bridge crosses a hole");scenarios++;
        // An ellipse is not treated as a circular punctuation mark.
        var ellipse=Circle(.5);for(int i=0;i<ellipse.Count;i++)ellipse[i]=new P(ellipse[i].X*3,ellipse[i].Y);
        var er=EngravingCenterlines.Build(new R(new List<List<P>>{ellipse}),.002);Require(er.Circles.Count==0,"ellipse misclassified as dot");scenarios++;
        Console.WriteLine(scenarios+" engraving-centerline scenarios passed");
    }
}
