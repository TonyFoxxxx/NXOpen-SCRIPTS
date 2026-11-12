using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using G = GostStrokeGeometry;
public static class Checks
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static bool Near(G.P a, G.P b) { return (a - b).Length < 1e-8; }
    static void W16(byte[] b,int p,int n) { b[p]=(byte)n;b[p+1]=(byte)(n>>8); }
    static void W32(byte[] b,int p,int n) { for(int i=0;i<4;i++) b[p+i]=(byte)(n>>(8*i)); }
    static byte[] FontBytes()
    {
        byte[] b=new byte[4096];b[0]=77;b[1]=90;W32(b,60,64);W16(b,64,0x454e);W16(b,100,64);
        W16(b,128,0);W16(b,130,0x8008);W16(b,132,1);W16(b,138,256);W16(b,140,3000);
        int f=256;W16(b,f,0x200);W32(b,f+2,3000);W16(b,f+66,1);W16(b,f+88,90);W16(b,f+93,70);
        b[f+95]=32;b[f+96]=255;W32(b,f+105,1020);W32(b,f+113,1050);
        byte[] name=Encoding.ASCII.GetBytes("GOST test\0");Array.Copy(name,0,b,f+1020,name.Length);
        byte[] h={128,6,70,0,204,128,0,24,24,0,128,0,232,0,52};
        for(int c=32;c<=256;c++){W16(b,f+117+4*(c-32),c<=72?0:h.Length);W16(b,f+119+4*(c-32),36);}
        Array.Copy(h,0,b,f+1050,h.Length);return b;
    }
    public static int Main()
    {
        G.FontData f=G.ReadFont(FontBytes());Require(f.CapHeight==52 && f.Baseline==70,"metrics");
        List<G.Stroke> h=G.Text(f,"H H");Require(h.Count==6,"space/three strokes");
        G.P stem=h[0].P[1]-h[0].P[0];Require(Math.Abs(Math.Atan2(Math.Abs(stem.X),Math.Abs(stem.Y))*180/Math.PI-15)<1e-9,"exact slant");
        Require(Math.Abs(Math.Abs(stem.Y)-1)<1e-9,"capital height");
        bool rejected=false;try{G.Text(f,"?");}catch(InvalidOperationException){rejected=true;}Require(rejected,"missing glyph");
        byte[] invalid=FontBytes();W32(invalid,60,int.MaxValue);rejected=false;try{G.ReadFont(invalid);}catch(Exception){rejected=true;}Require(rejected,"bad offset");
        List<G.P> corner=new List<G.P>{new G.P(0,0),new G.P(0,52),new G.P(30,52)};
        var angular=G.Smooth(corner,52);Require(angular.Count==2 && angular[0].P.Length==2 && angular[1].P.Length==2,"sharp corners");
        List<G.P> rounded=new List<G.P>{new G.P(6,60),new G.P(6,28),new G.P(7,23),new G.P(11,19),new G.P(16,18),new G.P(20,18)};
        var smooth=G.Smooth(rounded,52);int curves=0;
        for(int i=0;i<smooth.Count;i++)
        {
            G.P[] p=smooth[i].P;Require(Near(p[0],rounded[i]) && Near(p[p.Length-1],rounded[i+1]),"endpoint preservation");
            if(p.Length==4)curves++;
            if(i>0){G.P[] prev=smooth[i-1].P;G.P a=(prev[prev.Length-1]-prev[prev.Length-2]).Unit();G.P c=(p[1]-p[0]).Unit();Require(G.P.Dot(a,c)>1-1e-9,"tangent continuity");}
        }
        Require(curves==3,"rounded corners are cubic");
        var loop=G.Smooth(new List<G.P>{new G.P(0,0),new G.P(5,0),new G.P(5,5),new G.P(0,5),new G.P(0,0)},52);
        Require(loop.Count==4 && Near(loop[3].P[1],loop[0].P[0]),"closed loop");
        var repeated=G.Smooth(new List<G.P>{new G.P(0,0),new G.P(0,0),new G.P(5,0)},52);Require(repeated.Count==1,"zero segment removal");
        G.Glyph retrace=new G.Glyph{Width=40};retrace.Paths.Add(new List<G.P>{new G.P(0,0),new G.P(5,0),new G.P(0,0),new G.P(0,8)});f.Glyphs['R']=retrace;
        Require(G.Text(f,"R").Count==2,"no repeated engraving segment");
        Console.WriteLine("10 smooth-font scenarios passed");return 0;
    }
}
