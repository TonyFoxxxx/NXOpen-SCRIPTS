using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

// Exercise the actual native dialog on a desktop which is never shown to the user.
internal static class PlanarSideSelection
{
    private const int Continue = 1, Cancel = 2, On = 110, Tanto = 111;
    private const int Inside = 121, Outside = 122, Left = 124, Right = 125;
    private const uint BmGetCheck = 0xf0, BmClick = 0xf5;
    private static readonly IntPtr Zero = IntPtr.Zero;
    private static IntPtr desktop;

    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool SetThreadDesktop(IntPtr handle);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string kind, string caption);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll", EntryPoint="SendMessageW")] private static extern IntPtr Send(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", EntryPoint="PostMessageW")] private static extern bool Post(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int X, Y, Right, Bottom; }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Click(IntPtr w, int id) { Send(GetDlgItem(w, id), BmClick, Zero, Zero); }
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
        PlanarCurveOptions options = PlanarCurveWindows.AskOptions(Zero, title, "TEST");
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
            PlanarCurveOptions options = Open("TANTO "+outside+" "+right, delegate(IntPtr w) {
                CheckEnabled(w,false);
                CheckPair(w,Inside,Outside,false); CheckPair(w,Left,Right,false);
                Check(Bounds(w,Inside).Right <= Bounds(w,Left).X && Bounds(w,Inside).Bottom <= Bounds(w,Outside).Y, "separate control columns and rows");
                Click(w,Tanto); CheckEnabled(w,true);
                int closedId = outside ? Outside : Inside;
                Click(w,closedId); Click(w,closedId);
                CheckPair(w,Inside,Outside,outside); CheckPair(w,Left,Right,false);
                int openId = right ? Right : Left;
                Click(w,openId); Click(w,openId);
                CheckPair(w,Inside,Outside,outside); CheckPair(w,Left,Right,right);
                Click(w,On); CheckEnabled(w,false); Click(w,Tanto);
                CheckEnabled(w,true); CheckPair(w,Inside,Outside,outside); CheckPair(w,Left,Right,right);
                Click(w,Continue);
            });
            Check(options!=null && options.Tanto && options.ClosedOutside==outside && options.OpenRight==right, "returned combination");
        }
        PlanarCurveOptions on = Open("ON ignores sides",delegate(IntPtr w) {
            Click(w,Tanto); Click(w,Outside); Click(w,Right); Click(w,On); Click(w,Continue);
        });
        Check(on!=null && !on.Tanto && !on.UsesOutsideOrRight(false) && !on.UsesOutsideOrRight(true), "ON ignores side selections");
        Check(Open("Cancel",delegate(IntPtr w){Click(w,Tanto); Click(w,Right); Click(w,Cancel);})==null, "cancel");
        return 0;
    }
    public static int Main()
    {
        int status=1;
        Thread gui = new Thread((ThreadStart)delegate {
            try { status=Run(); } catch(Exception ex) { Console.Error.WriteLine(ex); }
        });
        gui.SetApartmentState(ApartmentState.MTA);
        gui.IsBackground=true;
        gui.Start();
        return gui.Join(20000) ? status : 1;
    }
}
