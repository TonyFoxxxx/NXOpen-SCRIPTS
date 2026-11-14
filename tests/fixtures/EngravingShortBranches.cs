using System;
using System.Collections.Generic;
using P=EngravingCenterlines.P;
using R=EngravingCenterlines.Region;
public class ShortBranchChecks
{
    static int scenarios;
    static void Require(bool ok,string message) { if(!ok)throw new Exception(message); }
    static void Arc(List<P> p,double x,double y,double r,double a,double b,int n)
    { for(int i=0;i<=n;i++){double t=a+(b-a)*i/n;p.Add(new P(x+r*Math.Cos(t),y+r*Math.Sin(t)));} }
    static List<List<P>> Bowls(double h,double ratio,double fillet)
    {
        // Analytic two-island band, independent of any font or customer's model.
        // Outer fillets and sharp inner corners used to leave terminal spurs or
        // collapsed loops at the two ordinary corners of the upright stroke.
        double width=h*ratio,r=(h+width)/4,x=h*.32,f=width*fillet;
        double angle=Math.Asin((h/2-r)/r);var outer=new List<P>();
        Arc(outer,f,h-f,f,Math.PI,Math.PI/2,16);
        Arc(outer,x,h-r,r,Math.PI/2,-angle,64);
        Arc(outer,x,r,r,angle,-Math.PI/2,64);
        Arc(outer,f,f,f,-Math.PI/2,-Math.PI,16);
        var loops=new List<List<P>>{outer};
        foreach(double y in new double[]{r,h-r})
        {
            var hole=new List<P>{new P(width,y+r-width)};
            Arc(hole,x,y,r-width,Math.PI/2,-Math.PI/2,64);
            hole.Add(new P(width,y-r+width));loops.Add(hole);
        }
        return loops;
    }
    static P Transform(P p,double angle,double unit,bool mirror)
    { double x=mirror?-p.X:p.X;return new P(17+unit*(x*Math.Cos(angle)-p.Y*Math.Sin(angle)),-9+unit*(x*Math.Sin(angle)+p.Y*Math.Cos(angle))); }
    static List<List<P>> Transform(List<List<P>> loops,double angle,double unit,bool mirror,int phase)
    {
        var result=new List<List<P>>();
        foreach(var loop in loops)
        {
            var p=new List<P>();for(int i=0;i<loop.Count;i++)p.Add(Transform(loop[(i+phase)%loop.Count],angle,unit,mirror));
            if(mirror)p.Reverse();result.Add(p);
        }
        return result;
    }
    static double PointSegment(P p,P a,P b)
    { P v=b-a;double n=P.Dot(v,v);return (p-a-v*(n>0?Math.Max(0,Math.Min(1,P.Dot(p-a,v)/n)):0)).Length; }
    sealed class SampledSegment
    {
        internal P A,B;
        internal double MinX,MinY,MaxX,MaxY;
    }
    static List<SampledSegment> Sample(EngravingCenterlines.Result raw)
    {
        var result=new List<SampledSegment>();
        foreach(var path in raw.Curves)foreach(var b in path)
        {
            P a=b.A;int count=b.Line?1:64;
            for(int i=1;i<=count;i++)
            {
                P q=b.At(i/(double)count);result.Add(new SampledSegment{A=a,B=q,
                    MinX=Math.Min(a.X,q.X),MinY=Math.Min(a.Y,q.Y),MaxX=Math.Max(a.X,q.X),MaxY=Math.Max(a.Y,q.Y)});a=q;
            }
        }
        return result;
    }
    static double Distance(List<SampledSegment> path,P p)
    {
        double best=double.MaxValue;
        foreach(var s in path)
        {
            // Sample each curve once. A box lower bound skips segments that cannot
            // improve the answer; neither the sample density nor assertions change.
            double dx=Math.Max(0,Math.Max(s.MinX-p.X,p.X-s.MaxX));
            double dy=Math.Max(0,Math.Max(s.MinY-p.Y,p.Y-s.MaxY));
            if(dx*dx+dy*dy<=best*best)best=Math.Min(best,PointSegment(p,s.A,s.B));
        }
        return best;
    }
    static void CheckBowls(double h,double ratio,double fillet,double angle,double unit,bool mirror,int phase)
    {
        string label="two islands h="+h+" width="+ratio+" fillet="+fillet+" angle="+angle+" unit="+unit+" mirror="+mirror+" phase="+phase;
        try
        {
            var region=new R(Transform(Bowls(h,ratio,fillet),angle,unit,mirror,phase));
            var raw=EngravingCenterlines.Build(region,.002*unit);
            var rawSamples=Sample(raw);
            Require(raw.Curves.Count==3,"false corner branch remained: "+raw.Curves.Count);
            Require(raw.Circles.Count==0,"two islands mistaken for a dot");
            var ends=new List<P>();
            foreach(var path in raw.Curves){ends.Add(path[0].A);ends.Add(path[path.Count-1].D);}
            // Exactly two degree-three nodes: three routes between them retain BOTH
            // islands. No artificial fourth node/terminal may be invented at a corner.
            var nodes=new List<P>();foreach(P p in ends)
            { bool exists=false;foreach(P q in nodes)if((p-q).Length<1e-9*unit)exists=true;if(!exists)nodes.Add(p); }
            Require(nodes.Count==2,"corner incorrectly treated as a junction");
            foreach(P node in nodes){int count=0;foreach(P end in ends)if((node-end).Length<1e-9*unit)count++;Require(count==3,"incorrect node degree");}
            foreach(double y in new double[]{h*ratio/2,h-h*ratio/2})
            {
                P corner=Transform(new P(h*ratio/2,y),angle,unit,mirror);
                Require(Distance(rawSamples,corner)<.002*unit,"ordinary corner was displaced");
            }
            var routed=EngravingRoutes.Build(raw,region,.02*unit,.002*unit);
            var routeSamples=Sample(routed);
            Require(routed.Curves.Count==1&&!routed.Strokes[0].Closed,"expected one open path through both bowls");
            EngravingRoutes.Validate(routed,region,.002*unit,.00004*unit);
            // Routing may open the two junctions, but must retain the two curved lobes
            // and the ordinary upright corners. Check original interior sample coverage.
            foreach(var path in raw.Curves)foreach(var b in path)for(int k=0;k<=8;k++)
            {
                P p=b.At(k/8.0);bool nearNode=false;foreach(P q in nodes)if((q-p).Length<.12*unit)nearNode=true;
                if(!nearNode)Require(Distance(routeSamples,p)<.00001*unit,"lost part of a real stroke");
            }
            scenarios++;
        }
        catch(Exception ex){throw new Exception(label,ex);}
    }
    static void RealLoop(double radius,double unit)
    {
        var outer=new List<P>();var hole=new List<P>();
        Arc(outer,0,0,radius+.15,0,2*Math.PI,128);outer.RemoveAt(outer.Count-1);
        Arc(hole,0,0,radius,0,2*Math.PI,128);hole.RemoveAt(hole.Count-1);
        var region=new R(Transform(new List<List<P>>{outer,hole},.37,unit,true,19));
        var raw=EngravingCenterlines.Build(region,.002*unit);
        Require(raw.Curves.Count==1&&raw.Strokes[0].Closed,"a genuine island was contracted away");
        var routed=EngravingRoutes.Build(raw,region,.02*unit,.002*unit);
        Require(routed.Curves.Count==1&&routed.Strokes[0].Closed,"a genuine loop was opened");
        EngravingRoutes.Validate(routed,region,.002*unit,.00004*unit);scenarios++;
    }
    static void Run()
    {
        foreach(double h in new double[]{1.25,2.5,5})foreach(double width in new double[]{.06,.1})
        foreach(double fillet in new double[]{.25,.5,.75})foreach(double angle in new double[]{0,.63})
        foreach(double unit in new double[]{1,1/25.4})foreach(bool mirror in new bool[]{false,true})
        foreach(int phase in new int[]{0,7,31,81})CheckBowls(h,width,fillet,angle,unit,mirror,phase);
        foreach(double radius in new double[]{.035,.2,1.0})foreach(double unit in new double[]{1,1/25.4})RealLoop(radius,unit);
        Console.WriteLine(scenarios+" engraving short-branch and island scenarios passed");
    }
    public static void Main(){System.Threading.Thread.CurrentThread.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;Console.OutputEncoding=new System.Text.UTF8Encoding(false);try{Run();}catch(Exception ex){Console.WriteLine(ex);Environment.Exit(1);}}
}
