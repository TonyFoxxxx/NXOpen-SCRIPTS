using System;
using System.Collections.Generic;
using P = EngravingCenterlines.P;
using B = EngravingCenterlines.Bezier;
using Region = EngravingCenterlines.Region;

// Analytic outlines reconstructed from the marked 1 and 4. Check the routed CAM
// chains in the outline's own frame, including the real stroke ends and crossings.
internal class ClippedCornerChecks
{
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static List<P> Polygon(params double[] xy)
    {
        var points = new List<P>();
        for (int i = 0; i < xy.Length; i += 2) points.Add(new P(xy[i], xy[i + 1]));
        return points;
    }
    private static P Cubic(P a, P b, P c, P d, double t)
    {
        double s = 1 - t;
        return a * (s*s*s) + b * (3*s*s*t) + c * (3*s*t*t) + d * (t*t*t);
    }
    private static double Fraction(int i, int n) { return Math.Pow(i / (double)n, n == 24 ? 1 : 1.4); }
    private static List<List<P>> Serif(double handle, int n)
    {
        var p = Polygon(.44,.88, .72,.88, .72,.24, 2.86,.24, 2.86,.88, 3.18,.88,
                        3.18,-.8, 2.86,-.8, 2.86,-.16, 0,-.16, 0,.16);
        P a = p[p.Count - 1], b = new P(handle,.16), c = new P(.44,.4), d = p[0];
        for (int i = 1; i < n; i++) p.Add(Cubic(a, b, c, d, Fraction(i, n)));
        return new List<List<P>> { p };
    }
    private static List<List<P>> Triangle(double slope, int n, bool curvedTop)
    {
        double intercept = .19 + .18*slope, inner = .21 - .34*slope;
        var outer = Polygon(-.18,-.196, -.18,.19);
        P crown = new P(2.08, intercept + 1.85*slope);
        if (curvedTop)
        {
            P a = new P(1.03, intercept + 1.03*slope), b = a + new P(.42,.42*slope);
            P c = new P(1.82, crown.Y + .065);
            for (int i = 0; i < n; i++) outer.Add(Cubic(a, b, c, crown, Fraction(i, n)));
        }
        else outer.Add(new P(1.85, crown.Y));
        outer.Add(crown);
        outer.AddRange(Polygon(2.08,.21, 2.97,.21, 2.97,-.196, 2.08,-.196, 2.08,-.68, 1.72,-.68, 1.72,-.196));
        return new List<List<P>> { outer, Polygon(.34,.21, 1.72,.21, 1.72,1.72*slope+inner) };
    }
    private sealed class Frame
    {
        internal P X, Y;
        internal double Scale;
        private readonly P origin = new P(4,-7);
        internal P Map(P p) { return origin + (X*p.X + Y*p.Y)*Scale; }
        internal P Local(P p) { return new P(P.Dot(p-origin,X)/Scale, P.Dot(p-origin,Y)/Scale); }
        internal Region Face(List<List<P>> loops, bool split)
        {
            var contours = new List<List<P>>();
            foreach (List<P> loop in loops)
            {
                var points = new List<P>();
                for (int i = 0; i < loop.Count; i++)
                {
                    int j = (i + (split ? 3 : 0)) % loop.Count;
                    points.Add(Map(loop[j]));
                    if (split) points.Add(Map((loop[j] + loop[(j + 1) % loop.Count])*.5));
                }
                if (split) points.Reverse();
                contours.Add(points);
            }
            return new Region(contours);
        }
    }
    private static P SerifOffset(double t, double handle)
    {
        P a = new P(0,.16), b = new P(handle,.16), c = new P(.44,.4), d = new P(.44,.88);
        double s = 1 - t;
        P tangent = ((b-a)*(s*s) + (c-b)*(2*s*t) + (d-c)*(t*t)).Unit();
        return Cubic(a,b,c,d,t) + new P(tangent.Y,-tangent.X)*.14;
    }
    private static P SerifVertex(double handle)
    {
        double low = 0, high = 1;
        for (int i = 0; i < 50; i++)
        {
            double t = (low + high)*.5;
            if (SerifOffset(t, handle).Y < .04) low = t; else high = t;
        }
        return SerifOffset((low + high)*.5, handle);
    }
    private static double PathDistance(P point, List<B> path, Frame frame)
    {
        double best = double.MaxValue;
        foreach (B curve in path)
        {
            P first = frame.Local(curve.A); int steps = curve.Line ? 1 : 48;
            for (int i = 1; i <= steps; i++)
            {
                P last = frame.Local(curve.At((double)i/steps));
                P foot = EngravingCenterlines.Closest(new EngravingCenterlines.Segment(first,last),point);
                best = Math.Min(best,(point-foot).Length); first = last;
            }
        }
        return best;
    }
    private static void Check(Frame frame, List<List<P>> loops, bool split, bool serif, double shape, bool curvedTop)
    {
        Region region = frame.Face(loops,split);
        var raw = EngravingCenterlines.Build(region,.002*frame.Scale);
        Require(raw.WallReconstruction == (!serif && !curvedTop), "unexpected reconstruction engine");
        var routed = EngravingRoutes.Build(raw,region,.02*frame.Scale,.002*frame.Scale);
        EngravingRoutes.Validate(routed,region,.002*frame.Scale,.0002*frame.Scale);
        Require(routed.Curves.Count == 2 && !routed.Strokes[0].Closed && !routed.Strokes[1].Closed, "expected two open routes");
        double barY = serif ? .04 : .007;
        double radius = (.19 + .18*shape - (.21 - .34*shape)) / (2*Math.Sqrt(1 + shape*shape));
        P target = serif ? SerifVertex(shape) : new P(-.18 + Math.Min(.203,radius),barY);
        foreach (List<B> path in routed.Curves) for (int i = 0; i < path.Count; i++)
        {
            B bar = path[i]; P a = frame.Local(bar.A), d = frame.Local(bar.D);
            if (!bar.Line || Math.Abs(d.X-a.X) < 2 || Math.Abs(a.Y-barY) > .002 || Math.Abs(d.Y-barY) > .002) continue;
            bool forward = a.X < d.X; P corner = forward ? a : d;
            Require((corner-target).Length < .004, "bar turns at " + corner.X + ", " + corner.Y + ", expected " + target.X + ", " + target.Y);
            int adjacent = forward ? i-1 : i+1;
            Require(adjacent >= 0 && adjacent < path.Count, "corner treated as a terminal");
            B side = path[adjacent];
            P away = forward ? (side.Line ? side.A : side.C)-side.D : (side.Line ? side.D : side.B)-side.A;
            Require(P.Dot(away.Unit(),frame.X) > .45, "return lost its sharp vertex");
            if (serif)
            {
                for (int j = 4; j <= 18; j++)
                    Require(PathDistance(SerifOffset(j/22.0,shape),path,frame) < .006, "axis leaves the curved flank");
                P tip = frame.Local(forward ? path[0].A : path[path.Count-1].D);
                Require(Math.Abs(tip.Y-.74) < .005 && Math.Abs(tip.X-.58) < .008, "serif terminal lost its half-width setback");
                var opposite = routed.Curves[1 - routed.Curves.IndexOf(path)];
                Require(opposite.Count == 1 && opposite[0].Line && PathDistance(new P(3.02,.04),opposite,frame) < .002,
                    "base stroke interrupted at the crossing");
                P baseA = frame.Local(opposite[0].A), baseB = frame.Local(opposite[0].D);
                Require(Math.Abs(Math.Min(baseA.Y,baseB.Y)+.64)<.002 && Math.Abs(Math.Max(baseA.Y,baseB.Y)-.72)<.002,"base terminal setback");
            }
            else
            {
                double intercept = (.19 + .18*shape + .21 - .34*shape)*.5;
                Require(side.Line && PathDistance(new P(.75,intercept + .75*shape),path,frame) < .002, "diagonal moved off its paired walls");
                Require(Math.Abs(Math.Max(a.X,d.X)-2.767)<.002, "crossbar setback");
                Require(PathDistance(new P(1.9,barY),path,frame)<.002, "crossbar interrupted");
                var stub = routed.Curves[1 - routed.Curves.IndexOf(path)];
                Require(stub.Count == 1 && stub[0].Line && PathDistance(new P(1.9,-.50),stub,frame)<.002, "vertical stroke or free end lost");
            }
            return;
        }
        throw new Exception("missing straight bar");
    }
    private static int Main()
    {
        return ManagedTestHost.Run(delegate {
            int count = 0;
            foreach (double angle in new double[] {0,.48,1.62}) foreach (double scale in new double[] {1,1/25.4})
            foreach (bool mirror in new bool[] {false,true}) foreach (int n in new int[] {24,52})
            {
                var x = new P(Math.Cos(angle),Math.Sin(angle));
                var frame = new Frame { X=x, Y=new P(-x.Y,x.X)*(mirror ? -1 : 1), Scale=scale };
                foreach (double handle in new double[] {.30,.33})
                {
                    try { Check(frame,Serif(handle,n),n==52,true,handle,false); count++; }
                    catch (Exception e) { throw new Exception("serif angle="+angle+" scale="+scale+" mirror="+mirror+" samples="+n+" handle="+handle,e); }
                }
                foreach (double slope in new double[] {.80,.85}) foreach (bool curved in new bool[] {false,true})
                {
                    try { Check(frame,Triangle(slope,n,curved),n==52,false,slope,curved); count++; }
                    catch (Exception e) { throw new Exception("bevel angle="+angle+" scale="+scale+" mirror="+mirror+" samples="+n+" slope="+slope+" curvedTop="+curved,e); }
                }
            }
            Console.WriteLine(count+" clipped-corner scenarios passed"); return 0;
        });
    }
}
