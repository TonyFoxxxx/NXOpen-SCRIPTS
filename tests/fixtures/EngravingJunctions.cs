using System;using System.Collections.Generic;
using P=EngravingCenterlines.P;using R=EngravingCenterlines.Region;
public class JunctionChecks
{
 static int scenarios;
 static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
 static List<P> Poly(params double[] xy){var p=new List<P>();for(int i=0;i<xy.Length;i+=2)p.Add(new P(xy[i],xy[i+1]));return p;}
 static P Intersection(P a,P b,P c,P d){P u=b-a,v=d-c;return a+u*(P.Cross(c-a,v)/P.Cross(u,v));}
 static List<P> Bevel(double angle,double taper)
 {
  double r=.2;P a=new P(-2.8,0),b=new P(2.8*Math.Cos(angle),2.8*Math.Sin(angle)),n0=new P(0,1),n1=new P(-Math.Sin(angle),Math.Cos(angle));
  P inner=Intersection(a+n0*r,n0*r*1.2,b+n1*r*taper,n1*r*1.2);
  P outer=Intersection(a-n0*r,n0*(-r*1.2),b-n1*r*taper,n1*(-r*1.2));
  P cutA=outer+(a-n0*r-outer).Unit()*.12,cutB=outer+(b-n1*r*taper-outer).Unit()*.12;
  return new List<P>{a+n0*r,inner,b+n1*r*taper,b-n1*r*taper,cutB,cutA,a-n0*r};
 }
 static List<P> Band(int count)
 {
  var p=new List<P>();double start=.4,end=5.4;for(int i=0;i<=count;i++){double a=start+(end-start)*i/count;p.Add(new P(2.2*Math.Cos(a),2.2*Math.Sin(a)));}
  for(int i=count;i>=0;i--){double a=start+(end-start)*i/count;p.Add(new P(1.8*Math.Cos(a),1.8*Math.Sin(a)));}return p;
 }
 static List<P> LineArc(int count)
 {
  var p=new List<P>{new P(-4,.2)};double a=Math.PI-Math.Asin(.2/2.2),end=-.6;
  for(int i=0;i<=count;i++){double t=a+(end-a)*i/count;p.Add(new P(2.2*Math.Cos(t),2.2*Math.Sin(t)));}
  a=Math.PI+Math.Asin(.2/1.8);for(int i=0;i<=count;i++){double t=end+(a-end)*i/count;p.Add(new P(1.8*Math.Cos(t),1.8*Math.Sin(t)));}
  p.Add(new P(-4,-.2));return p;
 }
 static List<P> ShortT(double h,bool round)
 {
  var p=Poly(-2,-.2,2,-.2,2,.2,.2,.2,.2,h);
  if(round)for(int i=1;i<=48;i++){double a=i*Math.PI/48;p.Add(new P(.2*Math.Cos(a),h+.2*Math.Sin(a)));}
  else p.Add(new P(-.2,h));p.Add(new P(-.2,.2));p.Add(new P(-2,.2));return p;
 }
 static List<P> ApproximateLineArc()
 {
  var p=Poly(80,640,309,73,430,120,213,575,585,822,567,674,595,527,662,412,794,318,943,255,1068,254,1210,282,1330,333,1444,418,1540,541,1592,628,1638,750,1642,895,1618,1032,1543,1150,1403,1249,1283,1287,1138,1298,1008,1264,1064,1083,1168,1102,1249,1098,1352,1056,1420,995,1473,869,1473,757,1443,640,1390,551,1290,489,1151,462,1029,450,953,458,850,503,776,579,738,672,721,724,721,818,748,894,786,951,705,1099);
  for(int i=0;i<p.Count;i++)p[i]=new P((p[i].X-800)*.003,(700-p[i].Y)*.003);return p;
 }
 static List<P> Transform(List<P> loop,double angle,double scale,bool mirror,int start)
 {
  var p=new List<P>();for(int j=0;j<loop.Count;j++){P v=loop[(j+start)%loop.Count];double x=mirror?-v.X:v.X;p.Add(new P(17+scale*(x*Math.Cos(angle)-v.Y*Math.Sin(angle)),-9+scale*(x*Math.Sin(angle)+v.Y*Math.Cos(angle))));}
  if(mirror){p.Reverse();var dense=new List<P>();for(int i=0;i<p.Count;i++)for(int k=0;k<3;k++)dense.Add(p[i]+(p[(i+1)%p.Count]-p[i])*(k/3.0));p=dense;}return p;
 }
 static void Check(string label,List<P> outline,int branches,int routes,double angle,double scale,bool mirror,int start,bool sharp=false)
 {
  string name=label+" angle="+angle+" scale="+scale+" mirror="+mirror+" start="+start;
  try{
   var region=new R(new List<List<P>>{Transform(outline,angle,scale,mirror,start)});var raw=EngravingCenterlines.Build(region,.002*scale);
   Require(raw.Curves.Count==branches,name+" raw branches "+raw.Curves.Count+" expected "+branches);Require(raw.Circles.Count==0,name+" misclassified as dot");
   if(sharp){Require(raw.Curves[0].Count==2,name+" bend is not two straight strokes");foreach(var b in raw.Curves[0])Require(b.Line,name+" bend became spline");Require((raw.Curves[0][0].D-new P(17,-9)).Length<.003*scale,name+" corner displaced");}
   var result=EngravingRoutes.Build(raw,region,.02*scale,.002*scale);Require(result.Curves.Count==routes,name+" routed paths "+result.Curves.Count+" expected "+routes);
   EngravingRoutes.Validate(result,region,.002*scale,.0002*scale);
   foreach(var path in result.Curves)foreach(var b in path)for(int i=0;i<=32;i++)Require(region.Inside(b.At(i/32.0)),name+" curve outside face");
   if(!sharp)foreach(var path in result.Curves)
   {
    var points=new List<P>();foreach(var b in path)for(int i=0;i<=32;i++){P p=b.At(i/32.0);if(points.Count==0||(p-points[points.Count-1]).Length>.00002*scale)points.Add(p);}
    for(int i=1;i+1<points.Count;i++)Require(P.Dot((points[i]-points[i-1]).Unit(),(points[i+1]-points[i]).Unit())>-.25,name+" local reversing hook");
   }
   scenarios++;
  }catch(Exception ex){throw new Exception(name,ex);}
 }
 static void Run()
 {
  foreach(double angle in new double[]{0,.41,1.27})foreach(double scale in new double[]{1,1/25.4})foreach(bool mirror in new bool[]{false,true})foreach(int start in new int[]{0,3})
  {
   foreach(double bend in new double[]{.75,1.5,2.25})foreach(double taper in new double[]{.6,1.5})Check("bevelled tapered bend",Bevel(bend,taper),1,1,angle,scale,mirror,start,true);
   foreach(int n in new int[]{24,60}){Check("polygonal band",Band(n),1,1,angle,scale,mirror,start);Check("line to arc",LineArc(n),1,1,angle,scale,mirror,start);}
   Check("uneven line/arc outline",ApproximateLineArc(),1,1,angle,scale,mirror,start);
   foreach(bool round in new bool[]{false,true})foreach(double h in new double[]{.55,.85})Check("real short arm round="+round,ShortT(h,round),3,2,angle,scale,mirror,start);
  }
  Console.WriteLine(scenarios+" engraving-junction scenarios passed");
 }
 public static void Main(){System.Threading.Thread.CurrentThread.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;Console.OutputEncoding=new System.Text.UTF8Encoding(false);try{Run();}catch(Exception e){Console.WriteLine(e);Environment.Exit(1);}}
}
