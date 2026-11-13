using System;
using System.Collections.Generic;
using P=EngravingCenterlines.P;
using R=EngravingCenterlines.Region;
public class CurvedCornerChecks
{
    static int scenarios;
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static double Fraction(int i,int n){return Math.Pow(i/(double)n,n==24?1:1.6);}
    static P Transform(P p,double angle,double scale,bool mirror)
    {double x=mirror?-p.X:p.X;return new P(17+scale*(x*Math.Cos(angle)-p.Y*Math.Sin(angle)),-9+scale*(x*Math.Sin(angle)+p.Y*Math.Cos(angle)));}
    static P Inverse(P p,double angle,double scale,bool mirror)
    {p=(p-new P(17,-9))*(1/scale);double x=p.X*Math.Cos(angle)+p.Y*Math.Sin(angle);return new P(mirror?-x:x,-p.X*Math.Sin(angle)+p.Y*Math.Cos(angle));}
    static List<P> Circular(double radius,double angle,double length,int n)
    {
        double w=.2,cx=radius*Math.Sin(angle),cy=-radius*Math.Cos(angle);
        double lo=cy+Math.Sqrt((radius-w)*(radius-w)-(-w-cx)*(-w-cx));
        double hi=cy+Math.Sqrt((radius+w)*(radius+w)-(w-cx)*(w-cx));
        double a=Math.Atan2(hi-cy,w-cx),b=Math.Atan2(lo-cy,-w-cx),end=Math.PI/2+angle-length/radius;
        var p=new List<P>{new P(-w,4),new P(w,4),new P(w,hi)};
        for(int i=1;i<=n;i++){double t=a+(end-a)*Fraction(i,n);p.Add(new P(cx+(radius+w)*Math.Cos(t),cy+(radius+w)*Math.Sin(t)));}
        for(int i=0;i<=n;i++){double t=end+(b-end)*Fraction(i,n);p.Add(new P(cx+(radius-w)*Math.Cos(t),cy+(radius-w)*Math.Sin(t)));}
        return p;
    }
    static P Offset(double x,double k,double sign)
    {double tangent=.6+2*k*x;return new P(x,.6*x+k*x*x)+new P(-tangent,1).Unit()*(.2*sign);}
    static double WallRoot(double k,double sign)
    {double low=-1,high=1;for(int i=0;i<60;i++){double x=(low+high)/2;if(Offset(x,k,sign).X>.2*sign)high=x;else low=x;}return (low+high)/2;}
    static List<P> Parabolic(double k,int n)
    {
        double lo=WallRoot(k,-1),hi=WallRoot(k,1);var p=new List<P>{new P(-.2,4),new P(.2,4),Offset(hi,k,1)};
        for(int i=1;i<=n;i++)p.Add(Offset(hi+(1.2-hi)*Fraction(i,n),k,1));
        for(int i=0;i<=n;i++)p.Add(Offset(1.2+(lo-1.2)*Fraction(i,n),k,-1));return p;
    }
    static List<P> Fillet(int n)
    {
        var p=new List<P>{new P(-3,-.2),new P(0,-.2)};
        for(int i=1;i<=n;i++){double a=-Math.PI/2+Math.PI/2*Fraction(i,n);p.Add(new P(1.2*Math.Cos(a),1+1.2*Math.Sin(a)));}
        p.Add(new P(1.2,4));p.Add(new P(.8,4));p.Add(new P(.8,1));
        for(int i=1;i<=n;i++){double a=-Math.PI/2*Fraction(i,n);p.Add(new P(.8*Math.Cos(a),1+.8*Math.Sin(a)));}
        p.Add(new P(-3,.2));return p;
    }
    static void Quadratic(List<P> p,P a,P b,P c,int n)
    {for(int i=1;i<=n;i++){double t=Fraction(i,n);p.Add(a*((1-t)*(1-t))+b*(2*t*(1-t))+c*(t*t));}}
    static List<P> Asymmetric(int n)
    {
        var p=new List<P>{new P(226,154),new P(320,165),new P(255,769)};
        Quadratic(p,new P(255,769),new P(347,704),new P(457,675),n);p.Add(new P(447,768));
        Quadratic(p,new P(447,768),new P(294,820),new P(203,937),n);p.Add(new P(142,931));
        for(int i=0;i<p.Count;i++)p[i]=new P((p[i].X-200)*.004,(850-p[i].Y)*.004);return p;
    }
    static void Check(string label,List<P> outline,double angle,double scale,bool mirror,int n,int kind,double radius,double parameter)
    {
        string name=label+" rotation="+angle+" scale="+scale+" mirror="+mirror+" samples="+n;
        try{
            var p=new List<P>();int start=n==24?0:3;
            for(int i=0;i<outline.Count;i++)p.Add(Transform(outline[(i+start)%outline.Count],angle,scale,mirror));
            if(n!=24){p.Reverse();var split=new List<P>();for(int i=0;i<p.Count;i++){split.Add(p[i]);split.Add((p[i]+p[(i+1)%p.Count])*.5);}p=split;}
            var region=new R(new List<List<P>>{p});var raw=EngravingCenterlines.Build(region,.002*scale);
            Require(raw.Curves.Count==1&&raw.Circles.Count==0,name+" disconnected or dot");
            var path=raw.Curves[0];int stem=-1;
            for(int i=0;i<path.Count;i++)if(path[i].Line&&(path[i].D-path[i].A).Length>2.5*scale)stem=i;
            Require(stem>=0,name+" long stem is not straight");
            if(kind<2)
            {
                P origin=Transform(new P(),angle,scale,mirror);
                double error=Math.Min((path[stem].A-origin).Length,(path[stem].D-origin).Length)/scale;
                Require(error<.003,name+" reconstructed corner error "+error);
                if(kind==0)
                {
                    P center=new P(radius*Math.Sin(parameter),-radius*Math.Cos(parameter));
                    double end=Math.PI/2+parameter-1.5/radius+Math.Atan(.2/radius);
                    P tip=Transform(center+new P(Math.Cos(end),Math.Sin(end))*radius,angle,scale,mirror);
                    double endError=Math.Min((path[0].A-tip).Length,(path[path.Count-1].D-tip).Length)/scale;
                    Require(endError<.003,name+" curved terminal half-width setback "+endError);
                }
                foreach(var b in path)if(b!=path[stem])for(int j=0;j<=32;j++)
                {
                    P q=Inverse(b.At(j/32.0),angle,scale,mirror);double d;
                    if(kind==0)d=Math.Abs((q-new P(radius*Math.Sin(parameter),-radius*Math.Cos(parameter))).Length-radius);
                    else d=Math.Abs(q.Y-.6*q.X-parameter*q.X*q.X)/Math.Sqrt(1+Math.Pow(.6+2*parameter*q.X,2));
                    Require(d<.006,name+" curved axis deviation "+d);
                }
            }
            int sharp=0;
            for(int i=1;i<path.Count;i++)
            {
                var a=path[i-1];var b=path[i];P before=(a.Line?a.D-a.A:a.D-a.C).Unit(),after=(b.Line?b.D-b.A:b.B-b.A).Unit();
                if(P.Dot(before,after)<.9)sharp++;
            }
            Require(kind==2?sharp==0:sharp==1,name+" sharp joins "+sharp);
            var routed=EngravingRoutes.Build(raw,region,.02*scale,.002*scale);Require(routed.Curves.Count==1,name+" routing split");
            EngravingRoutes.Validate(routed,region,.002*scale,.0002*scale);
            foreach(var b in routed.Curves[0])for(int j=0;j<=32;j++)Require(region.Inside(b.At(j/32.0)),name+" outside face");
            scenarios++;
        }catch(Exception ex){throw new Exception(name,ex);}
    }
    static void Run()
    {
        foreach(double angle in new double[]{0,.41,1.27})foreach(double scale in new double[]{1,1/25.4})foreach(bool mirror in new bool[]{false,true})foreach(int n in new int[]{24,72})
        {
            Check("circular sharp joint",Circular(3,.65,1.5,n),angle,scale,mirror,n,0,3,.65);
            Check("acute circular joint",Circular(4,.8,1.5,n),angle,scale,mirror,n,0,4,.8);
            Check("shallow curved joint",Circular(6,.65,1.5,n),angle,scale,mirror,n,0,6,.65);
            foreach(double k in new double[]{.12,-.09})Check("noncircular joint",Parabolic(k,n),angle,scale,mirror,n,1,0,k);
            Check("tangential fillet",Fillet(n),angle,scale,mirror,n,2,0,0);
            Check("asymmetric curved walls",Asymmetric(n),angle,scale,mirror,n,3,0,0);
        }
        Console.WriteLine(scenarios+" engraving-curved-corner scenarios passed");
    }
    public static void Main(){System.Threading.Thread.CurrentThread.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;Console.OutputEncoding=new System.Text.UTF8Encoding(false);try{Run();}catch(Exception ex){Console.WriteLine(ex);Environment.Exit(1);}}
}
