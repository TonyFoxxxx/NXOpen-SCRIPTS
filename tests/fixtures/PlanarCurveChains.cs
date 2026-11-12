using System;
using System.Collections.Generic;
using V = PlanarCurveChains.V;
using E = PlanarCurveChains.Ends;
public class Checks
{
    static int scenarios;
    static V P(double x,double y,double z=0) { return new V(x,y,z); }
    static E Edge(double x,double y,double xx,double yy) { return new E(P(x,y),P(xx,yy)); }
    static void Require(bool result,string text) { if(!result) throw new Exception(text); }
    static List<PlanarCurveChains.Chain> Check(List<E> input,int count,int loops,double tolerance=1e-6)
    {
        var chains=PlanarCurveChains.Build(input,tolerance);
        Require(chains.Count==count,"Chain count");int closed=0;var seen=new HashSet<int>();
        foreach(var chain in chains)
        {
            Require(chain.Items.Count>0,"Empty chain");V first=new V(),last=new V();
            for(int i=0;i<chain.Items.Count;i++)
            {
                int id=chain.Items[i];Require(seen.Add(id),"Curve visited twice");
                E edge=input[id];V start=chain.Reversed[i]?edge.B:edge.A;V end=chain.Reversed[i]?edge.A:edge.B;
                if(i==0)first=start;else Require((last-start).Length<=tolerance+1e-14,"Artificial bridge across a gap");
                last=end;
            }
            if(chain.Closed){closed++;Require((last-first).Length<=tolerance+1e-14,"False closing segment");}
        }
        Require(closed==loops,"Loop count");Require(seen.Count==input.Count,"Lost curve");scenarios++;return chains;
    }
    public static void Main()
    {
        Check(new List<E>(),0,0);
        Check(new List<E>{Edge(0,0,0,10)},1,0);
        Check(new List<E>{Edge(1,0,2,0),Edge(1,0,0,0),Edge(3,0,2,0)},1,0);
        // H: the crossbar touches the middles of complete strokes. No arbitrary joining.
        Check(new List<E>{Edge(0,0,0,10),Edge(0,5,5,5),Edge(5,0,5,10)},3,0);
        Check(new List<E>{Edge(0,0,1,0),Edge(1,0,2,0),Edge(1,0,1,1)},3,0);
        Check(new List<E>{Edge(-1,0,0,0),Edge(0,0,1,0),Edge(0,-1,0,0),Edge(0,0,0,1)},4,0);
        Check(new List<E>{Edge(0,0,1,0),Edge(1,1,1,0),Edge(0,1,1,1),Edge(0,1,0,0)},1,1);
        // A full circle or closed spline can be represented by one curve.
        Check(new List<E>{Edge(0,0,0,0)},1,1);
        Check(new List<E>{Edge(0,0,0,0),Edge(0,0,2,0)},2,1);
        Check(new List<E>{Edge(0,0,0,0),Edge(0,0,0,0)},2,2);
        Check(new List<E>{Edge(0,0,1,0),Edge(1.00001,0,2,0)},2,0);
        Check(new List<E>{Edge(0,0,1,0),Edge(1.0000004,0,2,0)},1,0);
        Check(new List<E>{Edge(-2,0,-0.0000004,0),Edge(0.0000004,0,2,0)},1,0);
        // A representative must not merge endpoints that are two tolerances apart.
        Check(new List<E>{Edge(-10,0,0,0),Edge(0.9e-6,0,10,0),Edge(-0.9e-6,0,0,10)},2,0);
        Check(new List<E>{new E(P(0,0,0),P(1,0,0)),new E(P(1,0,0.01),P(2,0,0.01))},2,0);
        var input=new List<E>();for(int i=0;i<1000;i++)input.Add(Edge(i,0,i+1,0));
        var random=new Random(1234);
        for(int i=input.Count-1;i>0;i--){int j=random.Next(i+1);E tmp=input[i];input[i]=input[j];input[j]=tmp;}
        Check(input,1,0);
        var frame=PlanarCurveChains.FindPlane(new List<V>{P(0,0,5),P(10,0,5),P(10,8,5)},P(0,0,1),1e-6);
        Require(Math.Abs(V.Dot(frame.Z,P(0,0,1))-1)<1e-12,"XY normal");scenarios++;
        frame=PlanarCurveChains.FindPlane(new List<V>{P(3,0,0),P(3,10,0),P(3,10,8)},P(1,0,0),1e-6);
        Require(V.Dot(frame.Z,P(1,0,0))>0.999999,"YZ normal");scenarios++;
        frame=PlanarCurveChains.FindPlane(new List<V>{P(0,0),P(10,10)},P(0,0,1),1e-6);
        Require(Math.Abs(V.Dot(frame.X,frame.Z))<1e-12,"Collinear basis");scenarios++;
        frame=PlanarCurveChains.FindPlane(new List<V>{P(0,0),P(0,0,10)},P(0,0,1),1e-6);
        Require(Math.Abs(V.Dot(frame.X,frame.Z))<1e-12,"Parallel fallback basis");scenarios++;
        bool rejected=false;
        try{PlanarCurveChains.FindPlane(new List<V>{P(0,0),P(10,0),P(0,10),P(5,5,0.01)},P(0,0,1),1e-6);}catch(InvalidOperationException){rejected=true;}
        Require(rejected,"Nonplanar geometry must be rejected");scenarios++;
        Console.WriteLine(scenarios+" planar-curve scenarios passed");
    }
}
