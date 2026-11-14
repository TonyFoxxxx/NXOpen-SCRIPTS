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
    static List<List<P>> AcuteFour(double width,double height,double radius)
    {
        double slope=height/width,offset=radius*Math.Sqrt(1+slope*slope);
        return new List<List<P>>{
            Poly(radius,height+slope*radius+offset,(-radius-height-offset)/slope,-radius,-radius,-radius,
                -radius,-height,radius,-height,radius,-radius,1,-radius,1,radius,radius,radius),
            Poly(-radius,height-slope*radius-offset,(radius-height+offset)/slope,radius,-radius,radius)};
    }
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
        P u=b-a,v=d-c;double eps=1e-12*Math.Max(P.Dot(u,u),P.Dot(v,v));
        double ca=P.Cross(u,c-a),da=P.Cross(u,d-a),ac=P.Cross(v,a-c),bc=P.Cross(v,b-c);
        if(((ca>eps&&da< -eps)||(ca< -eps&&da>eps))&&((ac>eps&&bc< -eps)||(ac< -eps&&bc>eps)))return 0;
        return Math.Min(Math.Min(PointSegment(a,c,d),PointSegment(b,c,d)),Math.Min(PointSegment(c,a,b),PointSegment(d,a,b)));
    }
    static EngravingCenterlines.Result Check(string name,List<List<P>> loops,int paths,int closed=0,double unit=1,bool adaptive=false,int maxPaths=0)
    {
        try {
            var r=new R(loops);var source=EngravingCenterlines.Build(r,.002*unit);var result=EngravingRoutes.Build(source,r,.02*unit,.002*unit);
            return CheckResult(name,r,result,paths,closed,unit,adaptive,maxPaths);
        } catch(Exception ex) { throw new Exception(name,ex); }
    }
    static EngravingCenterlines.Result CheckResult(string name,R r,EngravingCenterlines.Result result,int paths,int closed,double unit,bool adaptive,int maxPaths=0)
    {
        if(maxPaths==0)maxPaths=paths;
        Require(result.Curves.Count>=paths&&result.Curves.Count<=maxPaths,name+" paths expected "+paths+".."+maxPaths+" got "+result.Curves.Count);
        if(result.SeparatedJunctions>0)Require(result.JunctionGap>=.004*unit-1e-12&&result.JunctionGap<=.02*unit+1e-12,"gap outside permitted range");
        int loopsCount=0;var sampled=new List<List<P>>();
        for(int i=0;i<result.Curves.Count;i++)
        {
            var path=result.Curves[i];var points=new List<P>{path[0].A};if(result.Strokes[i].Closed)loopsCount++;
            for(int j=0;j<path.Count;j++)
            {
                Require((path[j].D-path[j].A).Length>1e-10,"zero segment after joining through-stroke");
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
            Require(min>=(adaptive?result.JunctionGap*.95:.019*unit),name+" insufficient separation: "+min);
        }
        scenarios++;return result;
    }
    static List<List<P>> Shear(List<List<P>> loops,double scale,double shear)
    {var output=new List<List<P>>();foreach(var loop in loops){var p=new List<P>();foreach(P v in loop)p.Add(new P(scale*(v.X+v.Y*shear),scale*v.Y));output.Add(p);}return output;}
    static void AddLine(EngravingCenterlines.Result source,P a,P b)
    {source.Strokes.Add(new EngravingCenterlines.Stroke());source.Curves.Add(new List<B>{new B{A=a,D=b,Line=true}});}
    static P Rot(double x,double y,double angle,double unit)
    {return new P(unit*(x*Math.Cos(angle)-y*Math.Sin(angle)),unit*(x*Math.Sin(angle)+y*Math.Cos(angle)));}
    static void RetainedEnds(EngravingCenterlines.Result source,EngravingCenterlines.Result result)
    {
        var ends=new List<P>();foreach(var path in source.Curves){ends.Add(path[0].A);ends.Add(path[path.Count-1].D);}
        foreach(P p in ends)
        {
            int count=0;foreach(P q in ends)if((p-q).Length<1e-10)count++;
            if(count!=1)continue;bool found=false;
            foreach(var path in result.Curves)if((p-path[0].A).Length<1e-10||(p-path[path.Count-1].D).Length<1e-10)found=true;
            Require(found,"genuine terminal endpoint lost");
        }
    }
    static void ShortBranches()
    {
        foreach(double unit in new double[]{1,1/25.4})for(int rotation=0;rotation<4;rotation++)
        {
            double angle=rotation*Math.PI/7;var region=new R(Shear(new List<List<P>>{Poly(-2,-2,2,-2,2,2,-2,2)},unit,0));
            var close=new EngravingCenterlines.Result();P a=Rot(0,0,angle,unit),b=Rot(0,.01,angle,unit);
            AddLine(close,Rot(0,-1,angle,unit),a);AddLine(close,a,b);AddLine(close,b,Rot(0,1,angle,unit));
            AddLine(close,a,Rot(-1,0,angle,unit));AddLine(close,b,Rot(1,.01,angle,unit));
            var result=EngravingRoutes.Build(close,region,.02*unit,.002*unit);
            CheckResult("nearby T junctions",region,result,3,0,unit,true);RetainedEnds(close,result);
            double distance=double.MaxValue;P midpoint=Rot(0,.005,angle,unit);
            foreach(var path in result.Curves)foreach(B c in path)if(c.Line)distance=Math.Min(distance,PointSegment(midpoint,c.A,c.D));
            Require(distance<1e-10,"short through-connector was removed");
            foreach(double length in new double[]{.025,.009,.007,.005,.0045})
            {
                var source=new EngravingCenterlines.Result();AddLine(source,Rot(0,-1,angle,unit),a);AddLine(source,a,Rot(0,1,angle,unit));AddLine(source,a,Rot(length,0,angle,unit));
                result=EngravingRoutes.Build(source,region,.02*unit,.002*unit);
                CheckResult("short terminal",region,result,2,0,unit,true);RetainedEnds(source,result);
                if(length<.02)Require(result.JunctionGap<.02*unit,"gap was not adapted to short branch");
                Require(source.Curves[2][0].A.Equals(a)&&(source.Curves[2][0].D-Rot(length,0,angle,unit)).Length<1e-12,"retry modified source");
            }
        }
        var blocked=new EngravingCenterlines.Result();AddLine(blocked,new P(0,-1),new P(0,0));AddLine(blocked,new P(0,0),new P(0,1));AddLine(blocked,new P(0,0),new P(.003,0));
        Reject(delegate{EngravingRoutes.Build(blocked,new R(new List<List<P>>{Poly(-2,-2,2,-2,2,2,-2,2)}),.02,.002);},"unresolvable short branch silently dropped");
    }
    static void AcuteTriangles()
    {
        // V1.04 creates a third path to the outside miter tip and rounds the triangular loop.
        // Check geometry, not just successful execution: exactly three straight triangle sides,
        // two genuine stem extensions, known corner locations and no extra branch after routing.
        foreach(double width in new double[]{4,5,6})foreach(double scale in new double[]{.25,1.0})
        foreach(double shear in new double[]{0,.268,.55})foreach(double unit in new double[]{1,1/25.4})
        {
            double angle=.73;bool mirror=width==5;
            var loops=Transform(Shear(AcuteFour(width,2,.2),scale,shear),angle,unit,mirror);
            var region=new R(loops);var raw=EngravingCenterlines.Build(region,.002*unit);
            string name="acute 4 width="+width+" scale="+scale+" shear="+shear+" unit="+unit;
            Require(raw.Curves.Count==3,name+" extra medial branch");
            Require(raw.WallReconstruction,name+" did not reconstruct the paired walls");
            int segments=0,triangles=0;
            foreach(var path in raw.Curves)
            {
                foreach(B c in path){Require(c.Line,name+" rounded a straight triangle");segments++;}
                if(path.Count==3&&(path[0].A-path[path.Count-1].D).Length<1e-9*unit)triangles++;
            }
            Require(segments==5&&triangles==1,name+" not a triangle plus two stems");
            var corners=Transform(Shear(new List<List<P>>{Poly(-width,0,0,2,0,0)},scale,shear),angle,unit,mirror)[0];
            foreach(P corner in corners)
            {
                double error=double.MaxValue;
                foreach(var path in raw.Curves)foreach(B c in path)error=Math.Min(error,Math.Min((corner-c.A).Length,(corner-c.D).Length));
                Require(error<1e-8*unit,name+" displaced triangle corner "+error);
            }
            var result=EngravingRoutes.Build(raw,region,.02*unit,.002*unit);
            CheckResult(name,region,result,2,0,unit,true);RetainedEnds(raw,result);
            int routedSegments=0;foreach(var path in result.Curves)foreach(B c in path){Require(c.Line,name+" routing rounded a straight side");routedSegments++;}
            Require(routedSegments==4,name+" expected three sharp sides with one through-extension and one separate stub");
        }
        // A genuine pointed arm has no pair of opposite constant-width walls.
        // Its long terminal branch must not be mistaken for a triangular miter spur.
        var pointed=new R(new List<List<P>>{Poly(-.2,-2,.2,-2,.2,2,-.2,2,-.2,.2,-6,0,-.2,-.2)});
        var pointedRaw=EngravingCenterlines.Build(pointed,.002);bool retained=false;
        foreach(var path in pointedRaw.Curves)if(path[0].A.X< -5.5||path[path.Count-1].D.X< -5.5)retained=true;
        Require(retained,"genuine long tapered arm was pruned");
        var pointedResult=EngravingRoutes.Build(pointedRaw,pointed,.02,.002);
        CheckResult("pointed arm",pointed,pointedResult,2,0,1,true);RetainedEnds(pointedRaw,pointedResult);
    }
    static void Reject(Action action,string label) { bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,label);scenarios++; }
    static List<List<P>> Quantize(List<List<P>> loops,double angle,double grid)
    {
        var result=new List<List<P>>();foreach(var loop in loops){var p=new List<P>();foreach(P v in loop)
        {P q=Rot(v.X,v.Y,angle,1);if(grid>0){q.X=Math.Round(q.X/grid)*grid;q.Y=Math.Round(q.Y/grid)*grid;}p.Add(q);}result.Add(p);}return result;
    }
    static void ImportedStraightStrokes()
    {
        // A font's integer outline coordinates produce slightly nonparallel walls after CAD
        // import. V1.05 leaves rounded K elbows/three routes and an outer-tip branch on 4.
        foreach(double angle in new double[]{.31,.73,1.23})foreach(double grid in new double[]{0,.002,.005,.01})
        foreach(double shear in new double[]{0,.268})foreach(bool mirror in new bool[]{false,true})
        foreach(double unit in new double[]{1,1/25.4})
        {
            double scale=mirror?.5:1;
            foreach(string letter in new string[]{"K","4"})
            {
                var original=letter=="K"?new List<List<P>>{K()}:AcuteFour(4,2,.2);
                // Short external upright, as in the user's reference: the long crossbar must
                // continue through; the upright and the triangular return end on either side.
                if(letter=="4"){original[0][3]=new P(-.2,-.75);original[0][4]=new P(.2,-.75);}
                var loops=Transform(Shear(Quantize(original,angle,grid),scale,shear),.42,unit,mirror);
                string name="imported "+letter+" angle="+angle+" grid="+grid+" shear="+shear+" mirror="+mirror+" unit="+unit;
                var region=new R(loops);EngravingCenterlines.Result raw,result;
                try {raw=EngravingCenterlines.Build(region,.002*unit);result=EngravingRoutes.Build(raw,region,.02*unit,.002*unit);}
                catch(Exception ex){throw new Exception(name,ex);}
                Require(raw.WallReconstruction,name+" fell back instead of restoring the wall network");
                CheckResult(name,region,result,2,0,unit,true);RetainedEnds(raw,result);
                var a=result.Curves[0];var b=result.Curves[1];if(a.Count>b.Count){var t=a;a=b;b=t;}
                Require(a.Count==1&&b.Count==(letter=="K"?2:3),name+" incorrect straight stroke layout: "+a.Count+"/"+b.Count);
                foreach(var path in result.Curves)foreach(B c in path)Require(c.Line,name+" rounded a straight stroke");
                if(letter=="K")
                {
                    var stem=Transform(Shear(Quantize(new List<List<P>>{Poly(0,-2,0,2)},angle,0),scale,shear),.42,unit,mirror)[0];
                    Require((a[0].D-a[0].A).Length>.95*(stem[1]-stem[0]).Length,name+" stem lost length");
                    Require(Math.Abs(P.Cross((a[0].D-a[0].A).Unit(),(stem[1]-stem[0]).Unit()))<.01,name+" incorrect stem direction");
                    Require((b[0].D-b[1].A).Length<1e-10*unit,name+" disconnected checkmark");
                }
                else
                {
                    var reference=Transform(Shear(Quantize(new List<List<P>>{Poly(-4,0,0,2,0,0)},angle,0),scale,shear),.42,unit,mirror)[0];
                    for(int i=0;i<2;i++)
                    {
                        double distance=double.MaxValue;foreach(B c in b)distance=Math.Min(distance,Math.Min((c.A-reference[i]).Length,(c.D-reference[i]).Length));
                        Require(distance<.035*scale*unit,name+" displaced triangle corner");
                    }
                    double throughDistance=double.MaxValue;
                    foreach(B c in b)throughDistance=Math.Min(throughDistance,PointSegment(reference[2],c.A,c.D));
                    Require(throughDistance<.012*scale*unit,name+" long crossbar is not continuous");
                }
            }
        }
    }
    static void WallOutlines()
    {
        // Independent stroke layouts, with no character identity or font templates.
        var shapes=new List<List<List<P>>>{
            new List<List<P>>{Poly(-.2,-.2,3,-.2,3,.2,.2,.2,.2,3,-.2,3)},
            new List<List<P>>{Poly(-.2,-.2,3.2,-.2,3.2,3,2.8,3,2.8,.2,.2,.2,.2,3,-.2,3)},
            new List<List<P>>{Poly(-.2,-2,.2,-2,.2,1.8,2,1.8,2,2.2,-2,2.2,-2,1.8,-.2,1.8)},
            new List<List<P>>{Poly(-1.2,-2,-.8,-2,-.8,-.2,.8,-.2,.8,-2,1.2,-2,1.2,2,.8,2,.8,.2,-.8,.2,-.8,2,-1.2,2)},
            new List<List<P>>{Poly(-2.2,-1.2,2.2,-1.2,2.2,1.2,-2.2,1.2),Poly(-1.8,-.8,1.8,-.8,1.8,.8,-1.8,.8)}
        };
        int[] expected={1,1,2,3,1};
        foreach(double angle in new double[]{.0,.417,1.236})foreach(double shear in new double[]{0,.268,.51})
        foreach(double unit in new double[]{1,1/25.4})for(int i=0;i<shapes.Count;i++)
        {
            var region=new R(Transform(Shear(shapes[i],.7,shear),angle,unit,angle>1));
            var raw=EngravingCenterlines.Build(region,.002*unit);
            string name="paired walls layout="+i+" angle="+angle+" shear="+shear+" unit="+unit;
            Require(raw.WallReconstruction,name+" unexpectedly fell back to medial axis");
            foreach(var path in raw.Curves)foreach(B b in path)Require(b.Line,name+" bent a straight stroke");
            CheckResult(name,region,EngravingRoutes.Build(raw,region,.02*unit,.002*unit),expected[i],i==4?1:0,unit,true);
        }
        // An imported punctuation dot may be slightly oval; no tiny branches or filled area.
        foreach(double unit in new double[]{1,1/25.4})foreach(double ratio in new double[]{1.04,1.10,1.14})
        {
            var oval=new List<P>();for(int i=0;i<240;i++){double a=2*Math.PI*i/240;oval.Add(new P(.15*unit*ratio*Math.Cos(a),.15*unit*Math.Sin(a)));}
            var region=new R(new List<List<P>>{oval});var raw=EngravingCenterlines.Build(region,.002*unit);
            Require(raw.Curves.Count==0&&raw.Circles.Count==1,"oval punctuation produced a branched skeleton");
            Require(Math.Abs(raw.Circles[0].Radius-.075*unit*Math.Sqrt(ratio))<.00001*unit,"oval D/2 equivalent diameter");scenarios++;
        }
        var elongated=new List<P>();for(int i=0;i<240;i++){double a=2*Math.PI*i/240;elongated.Add(new P(.4*Math.Cos(a),.15*Math.Sin(a)));}
        Require(EngravingCenterlines.Build(new R(new List<List<P>>{elongated}),.002).Circles.Count==0,"elongated mark mistaken for a dot");scenarios++;
    }
    static void ResampledDots()
    {
        // Adding collinear boundary samples changes neither the polygon nor its area.
        // Punctuation detection and its D/2 circle must not depend on sample density.
        foreach(double ratio in new double[]{1.10,1.14,1.16})foreach(int density in new int[]{1,5,25})
        foreach(double angle in new double[]{0,.71,2.1})foreach(bool reverse in new bool[]{false,true})
        foreach(double unit in new double[]{1,1/25.4})
        {
            var polygon=new List<P>();for(int i=0;i<96;i++){double a=2*Math.PI*i/96;polygon.Add(new P(.15*ratio*Math.Cos(a),.15*Math.Sin(a)));}
            var sampled=new List<P>();for(int i=0;i<polygon.Count;i++)
            {
                P a=polygon[i],b=polygon[(i+1)%polygon.Count];int count=Math.Abs(a.X)>.12?density:1;
                for(int j=0;j<count;j++)sampled.Add(a+(b-a)*(j/(double)count));
            }
            if(reverse)sampled.Reverse();
            var region=new R(Transform(new List<List<P>>{sampled},angle,unit,reverse));
            var original=new R(Transform(new List<List<P>>{polygon},angle,unit,reverse));
            Require(Math.Abs(region.Area-original.Area)<1e-12*unit*unit,"resampling changed the face");
            var result=EngravingCenterlines.Build(region,.002*unit);
            string name="resampled dot ratio="+ratio+" density="+density+" angle="+angle+" reverse="+reverse+" unit="+unit;
            Require(result.Circles.Count==1&&result.Curves.Count==0,name+" became a branched skeleton");
            Require((result.Circles[0].Center-new P(17,-9)).Length<1e-10*unit,name+" displaced center");
            Require(Math.Abs(result.Circles[0].Radius-Math.Sqrt(original.Area/Math.PI)/2)<1e-10*unit,name+" diameter depends on sample density");
            var routed=EngravingRoutes.Build(result,region,.02*unit,.002*unit);
            Require(routed.Circles.Count==1&&routed.Curves.Count==0,name+" routing modified punctuation");scenarios++;
        }
    }
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
        // These slanted outlines reproduced V1.03's short-stroke and degenerate-curve failures.
        foreach(double scale in new double[]{.1,.2,.5,1.0})foreach(double shear in new double[]{0.0,.268,.55})
        {
            string suffix=" scale="+scale+" shear="+shear;
            Check("slanted plus"+suffix,Shear(new List<List<P>>{Plus()},scale,shear),2,0,1,true);
            // On a small K, distinct medial junctions may need a third separated path.
            Check("slanted K"+suffix,Shear(new List<List<P>>{K()},scale,shear),2,0,1,true,3);
            Check("slanted P"+suffix,Shear(LetterP(),scale,shear),1,0,1,true);
            Check("slanted 4"+suffix,Shear(Four(),scale,shear),2,0,1,true);
            Check("slanted Phi"+suffix,Shear(Phi(),scale,shear),2,0,1,true,3);
        }
        ShortBranches();
        AcuteTriangles();
        ImportedStraightStrokes();
        WallOutlines();
        ResampledDots();
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
