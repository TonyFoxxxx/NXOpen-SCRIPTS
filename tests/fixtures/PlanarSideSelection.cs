using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

// Exercise the actual native dialog on a desktop which is never shown to the user.
internal static class PlanarSideSelection
{
    private const int Continue = 1, Cancel = 2, On = 110, Tanto = 111, Part = 140, Blank = 141;
    private static string prefs;
    private const int Inside = 121, Outside = 122, Left = 124, Right = 125, PlaneHint = 127, PlaneChoice = 128;
    private const uint BmGetCheck = 0xf0, BmClick = 0xf5;
    private static readonly IntPtr Zero = IntPtr.Zero;
    private static IntPtr desktop;

    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool SetThreadDesktop(IntPtr handle);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string kind, string caption);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int capacity);
    [DllImport("user32.dll", EntryPoint="SendMessageW")] private static extern IntPtr Send(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", EntryPoint="PostMessageW")] private static extern bool Post(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int X, Y, Right, Bottom; }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Click(IntPtr w, int id) { Send(GetDlgItem(w, id), BmClick, Zero, Zero); }
    private static void CheckPlane(IntPtr w, bool enabled)
    {
        Check(IsWindowEnabled(GetDlgItem(w,PlaneChoice)) && Send(GetDlgItem(w,PlaneChoice),BmGetCheck,Zero,Zero).ToInt32()==(enabled?1:0),
            "independent plane checkbox");
        var caption=new System.Text.StringBuilder(80);
        GetWindowText(GetDlgItem(w,Continue),caption,caption.Capacity);
        Check(caption.ToString().Contains(enabled?"плоскость":"кривые"),"continue indicates next selection");
        GetWindowText(GetDlgItem(w,PlaneHint),caption,caption.Capacity);
        string hint=caption.ToString();
        Check(enabled ? hint.Contains("At Distance") && hint.Contains("расстояние")
            : hint.Contains("автоматически"), "visible instruction matches the next action");
    }
    private static void CheckPair(IntPtr w, int first, int second, bool useSecond)
    {
        Check(Send(GetDlgItem(w, first), BmGetCheck, Zero, Zero).ToInt32() == (useSecond ? 0 : 1), "first checkbox " + first);
        Check(Send(GetDlgItem(w, second), BmGetCheck, Zero, Zero).ToInt32() == (useSecond ? 1 : 0), "second checkbox " + second);
    }
    private static void CheckEnabled(IntPtr w, bool enabled)
    {
        foreach (int id in new[] { Inside, Outside, Left, Right })
            Check(IsWindowEnabled(GetDlgItem(w, id)) == enabled, "side control availability " + id);
    }
    private static Rect Bounds(IntPtr w, int id)
    {
        Rect r; Check(GetWindowRect(GetDlgItem(w,id), out r), "control bounds"); return r;
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr hwnd,IntPtr region);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region,int x,int y);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int w,int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetPixel(IntPtr dc,int x,int y);
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader
    { public int Size,Width,Height;public ushort Planes,Bits;public uint Compression,ImageSize;public int X,Y,Colors,Important; }
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc,IntPtr bitmap,uint first,uint lines,[Out]byte[] pixels,ref BitmapHeader header,uint usage);
    private static void VerifyAppearance(IntPtr window)
    {
        Rect frame; Check(GetWindowRect(window,out frame),"window bounds");
        int width=frame.Right-frame.X,height=frame.Bottom-frame.Y;
        IntPtr region=CreateRectRgn(0,0,0,0);
        try{Check(GetWindowRgn(window,region)>0 && !PtInRegion(region,0,0) && PtInRegion(region,width/2,height/2),"rounded window region");}
        finally{DeleteObject(region);}
        IntPtr display=GetDC(window),memory=CreateCompatibleDC(display),bitmap=CreateCompatibleBitmap(display,width,height),previous=SelectObject(memory,bitmap);
        try
        {
            Send(window,0x317,memory,new IntPtr(0x1e));  // WM_PRINT, background/client/children/non-client
            foreach(int id in new[]{Part,Blank,Tanto,Outside,Left,Continue})
            {
                Rect b=Bounds(window,id);int x=b.X-frame.X,y=b.Y-frame.Y;
                int delta=Math.Max(4,(int)Math.Round(7.0*width/692));
                Check(GetPixel(memory,x,y)!=GetPixel(memory,x+delta,y+delta),"rounded button "+id);
            }
            string capture=Environment.GetEnvironmentVariable("NX_PLANAR_TEST_PREVIEW");
            if (!String.IsNullOrEmpty(capture))
            {
                SelectObject(memory,previous);
                var header=new BitmapHeader{Size=40,Width=width,Height=height,Planes=1,Bits=32};
                var bytes=new byte[width*height*4];
                Check(GetDIBits(display,bitmap,0,(uint)height,bytes,ref header,0)==height,"read native preview");
                using(var stream=new System.IO.BinaryWriter(System.IO.File.Create(capture)))
                {
                    stream.Write((ushort)0x4d42);stream.Write(54+bytes.Length);stream.Write(0);stream.Write(54);stream.Write(40);
                    stream.Write(width);stream.Write(height);stream.Write((ushort)1);stream.Write((ushort)32);
                    stream.Write(0);stream.Write(bytes.Length);stream.Write(0);stream.Write(0);stream.Write(0);stream.Write(0);stream.Write(bytes);
                }
            }
        }
        finally{SelectObject(memory,previous);DeleteObject(bitmap);DeleteDC(memory);ReleaseDC(window,display);}
    }

    private static PlanarCurveOptions Open(string title, Action<IntPtr> action)
    {
        Exception problem = null;
        Thread monitor = new Thread((ThreadStart)delegate {
            IntPtr w = Zero;
            try
            {
                Check(SetThreadDesktop(desktop), "attach monitor to hidden desktop");
                for (int i=0; i<200; i++)
                {
                    w = FindWindow("#32770", title);
                    if (w != Zero && GetDlgItem(w, Continue) != Zero) break;
                    Thread.Sleep(15);
                }
                Check(w != Zero && GetDlgItem(w, Continue) != Zero, "find options dialog");
                Send(w, 0, Zero, Zero); // Wait for WM_INITDIALOG to finish.
                action(w);
            }
            catch(Exception ex) { problem = ex; }
            finally { if (w!=Zero) Post(w, 0x10, Zero, Zero); }
        });
        monitor.IsBackground = true;
        monitor.Start();
        PlanarCurveOptions options = PlanarCurveWindows.AskOptions(Zero, title, "PLSK_CHIST_Z0", prefs);
        Check(monitor.Join(5000), "options dialog deadline");
        if (problem!=null) throw problem;
        return options;
    }

    private static int Run()
    {
        desktop = CreateDesktop("PlanarSideTest"+Guid.NewGuid().ToString("N"), Zero, Zero, 0, 0xc3, Zero);
        if (desktop==Zero || !SetThreadDesktop(desktop))
        {
            int error=Marshal.GetLastWin32Error();
            if (error==5 || error==170) return 77;
            throw new Win32Exception(error);
        }
        foreach (bool outside in new[]{false,true}) foreach (bool right in new[]{false,true})
        {
            System.IO.File.Delete(prefs);
            PlanarCurveOptions options = Open("TANTO "+outside+" "+right, delegate(IntPtr w) {
                CheckEnabled(w,false);
                CheckPlane(w,true);
                Check(Bounds(w,PlaneChoice).Bottom<=Bounds(w,On).Y && Bounds(w,Outside).Bottom<=Bounds(w,Continue).Y,"options fit the dialog");
                Click(w,PlaneChoice); CheckPlane(w,false);
                CheckPair(w,Inside,Outside,false); CheckPair(w,Left,Right,false);
                Check(Bounds(w,Inside).Right <= Bounds(w,Outside).X && Bounds(w,Outside).Right <= Bounds(w,Left).X &&
                    Bounds(w,Left).Right <= Bounds(w,Right).X, "four rounded sides in two groups");
                Click(w,Tanto); CheckEnabled(w,true);
                int closedId = outside ? Outside : Inside;
                Click(w,closedId); Click(w,closedId);
                CheckPair(w,Inside,Outside,outside); CheckPair(w,Left,Right,false);
                int openId = right ? Right : Left;
                Click(w,openId); Click(w,openId);
                CheckPair(w,Inside,Outside,outside); CheckPair(w,Left,Right,right);
                Click(w,On); CheckEnabled(w,false); CheckPlane(w,false); Click(w,Tanto);
                if (outside) Click(w,PlaneChoice);
                CheckPlane(w,outside);
                CheckEnabled(w,true); CheckPair(w,Inside,Outside,outside); CheckPair(w,Left,Right,right);
                Click(w,Continue);
            });
            Check(options!=null && options.Tanto && options.ClosedOutside==outside && options.OpenRight==right && options.SelectPlane==outside, "returned combination");
        }
        System.IO.File.Delete(prefs);
        PlanarCurveOptions on = Open("ON ignores sides",delegate(IntPtr w) {
            Click(w,Tanto); Click(w,Outside); Click(w,Right); Click(w,On); Click(w,Continue);
        });
        Check(on!=null && on.SelectPlane && !on.Tanto && !on.UsesOutsideOrRight(false) && !on.UsesOutsideOrRight(true), "ON ignores side selections");
        Check(Open("Cancel",delegate(IntPtr w){Click(w,Tanto); Click(w,Right); Click(w,Cancel);})==null, "cancel");
        System.IO.File.Delete(prefs);
        Open("Defaults",delegate(IntPtr w){
            CheckPair(w,Part,Blank,false);Click(w,Part);
            Check(!IsWindowEnabled(GetDlgItem(w,Continue)),"at least one set required");
            Send(w,0x111,new IntPtr(Continue),Zero);
            Click(w,Blank);CheckPair(w,Part,Blank,true);Click(w,Part);Click(w,Tanto);Click(w,Outside);
            Check(Send(GetDlgItem(w,Part),BmGetCheck,Zero,Zero).ToInt32()==1 && Send(GetDlgItem(w,Blank),BmGetCheck,Zero,Zero).ToInt32()==1,"both independent");
            VerifyAppearance(w);
            Click(w,Continue);
        });
        Open("Cancel edited options",delegate(IntPtr w){
            CheckEnabled(w,true);CheckPair(w,Inside,Outside,true);CheckPlane(w,true);
            Check(Send(GetDlgItem(w,Blank),BmGetCheck,Zero,Zero).ToInt32()==1,"blank restored");
            Click(w,Blank);Click(w,PlaneChoice);Click(w,On);Click(w,Cancel);
        });
        var restored=Open("Reopen",delegate(IntPtr w){Click(w,Continue);});
        Check(restored.Part && restored.Blank && restored.SelectPlane && restored.Tanto && restored.ClosedOutside && !restored.OpenRight,"cancel preserves last confirmed choices");
        restored.LastMethod="Distance";restored.OffsetMm=-25.4;restored.Save(prefs);
        var previous=PlanarCurveOptions.Load(prefs);
        Check(previous.LastMethod=="Distance" && previous.OffsetMm==restored.OffsetMm,"signed plane preset survives a new journal instance");
        System.IO.File.WriteAllText(prefs,"damaged");
        var recovered=Open("Recover defaults",delegate(IntPtr w){Click(w,Continue);});
        Check(recovered.Part && !recovered.Blank && !recovered.Tanto && recovered.SelectPlane,"malformed settings use defaults");
        return 0;
    }
    public static int Main()
    {
        return ManagedTestHost.Run(RunOnDesktop);
    }
    private static int RunOnDesktop()
    {
        prefs=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"planar-ui-"+Guid.NewGuid().ToString("N")+".settings");
        int status=1;
        Thread gui = new Thread((ThreadStart)delegate {
            try { status=Run(); } catch(Exception ex) { status=ManagedTestHost.Record(ex); }
        });
        gui.SetApartmentState(ApartmentState.MTA);
        gui.IsBackground=true;
        gui.Start();
        if (!gui.Join(20000)) throw new TimeoutException("Private desktop test exceeded 20 seconds");
        System.IO.File.Delete(prefs);
        return status;
    }
}
