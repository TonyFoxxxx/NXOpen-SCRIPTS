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
    private const int Gear=150,PaletteClose=151,Dark=152,Light=153,Square=154,Hue=155,Hex=156,Shared=157;
    private const uint BmGetCheck = 0xf0, BmClick = 0xf5;
    private static readonly IntPtr Zero = IntPtr.Zero;
    private static IntPtr desktop;
    [DllImport("user32.dll", ExactSpelling=true)] private static extern int GetWindowLongW(IntPtr window, int index);
    [DllImport("gdi32.dll")] private static extern bool PatBlt(IntPtr dc,int x,int y,int w,int h,uint operation);
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

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
        Check(IsWindowEnabled(GetDlgItem(w, PlaneChoice)) && Send(GetDlgItem(w, PlaneChoice), BmGetCheck, Zero, Zero).ToInt32() == (enabled ? 1 : 0), "independent plane checkbox");
        var caption = new System.Text.StringBuilder(80);
        GetWindowText(GetDlgItem(w, Continue), caption, caption.Capacity);
        Check(caption.ToString().Contains(enabled ? "плоскость" : "кривые"), "continue indicates next selection");
        GetWindowText(GetDlgItem(w, PlaneHint), caption, caption.Capacity);
        string hint = caption.ToString();
        Check(enabled ? hint == "Выберите плоскость в окне Plane" : hint.Contains("автоматически"), "visible instruction matches the next action");
    }
    private static void CheckPair(IntPtr w, int first, int second, bool useSecond)
    {
        Check(Send(GetDlgItem(w, first), BmGetCheck, Zero, Zero).ToInt32() == (useSecond ? 0 : 1), "first checkbox " + first);
        Check(Send(GetDlgItem(w, second), BmGetCheck, Zero, Zero).ToInt32() == (useSecond ? 1 : 0), "second checkbox " + second);
    }
    private static void CheckEnabled(IntPtr w, bool enabled)
    {
        foreach (int id in new[]
        {
            Inside,
            Outside,
            Left,
            Right
        }

        )
            Check(IsWindowEnabled(GetDlgItem(w, id)) == enabled && IsWindowVisible(GetDlgItem(w, id)) == enabled, "side " + id + " expected " + enabled + "; enabled=" + IsWindowEnabled(GetDlgItem(w, id)) + ", visible=" + IsWindowVisible(GetDlgItem(w, id)) + ", window=" + IsWindowVisible(w));
    }
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    private static extern bool SetWindowTextW(IntPtr hwnd,string text);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process,uint flag);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
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

    private static PlanarCurveOptions Open(string title, Action<IntPtr> action, Action showReport = null)
    {
        Exception problem = null;
        Thread monitor = new Thread((ThreadStart)delegate
        {
            IntPtr w = Zero;
            try
            {
                Check(SetThreadDesktop(desktop), "attach monitor to hidden desktop");
                for (int i = 0; i < 200; i++)
                {
                    w = FindWindow("#32770", title);
                    if (w != Zero && IsWindowVisible(w) && GetDlgItem(w, Continue) != Zero)
                        break;
                    Thread.Sleep(15);
                }

                Check(w != Zero && IsWindowVisible(w) && GetDlgItem(w, Continue) != Zero, "find visible options dialog");
                Send(w, 0, Zero, Zero); // Wait for WM_INITDIALOG to finish.
                action(w);
            }
            catch (Exception ex)
            {
                problem = ex;
            }
            finally
            {
                IntPtr popup = FindWindow("#32770", "Оформление — " + title);
                if (popup != Zero)
                    Send(popup, 0x10, Zero, Zero);
                if (w != Zero)
                    Post(w, 0x10, Zero, Zero);
            }
        });
        monitor.IsBackground = true;
        monitor.Start();
        PlanarCurveOptions options = null;
        if (showReport == null)
            options = PlanarCurveWindows.AskOptions(Zero, title, "PLSK_CHIST_Z0", prefs);
        else
            showReport();
        Check(monitor.Join(5000), "options dialog deadline");
        if (problem != null)
            throw new InvalidOperationException("Native dialog " + title, problem);
        return options;
    }

    private static string TextOf(IntPtr hwnd)
    {
        var value = new System.Text.StringBuilder(80);
        GetWindowText(hwnd, value, value.Capacity);
        return value.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawItem
    {
        public uint Type, Id, ItemId, Action, State;
        public IntPtr Window, Dc;
        public Rect Bounds;
        public UIntPtr Data;
    }
    private static void CheckPressedSurface(IntPtr parent, int id)
    {
        IntPtr button = GetDlgItem(parent, id);
        Check((GetWindowLongW(button, -16) & 15) == 11, "single owner for the button surface " + id);
        Rect bounds = Bounds(parent, id);
        int w = bounds.Right - bounds.X, h = bounds.Bottom - bounds.Y;
        IntPtr display = GetDC(button), memory = CreateCompatibleDC(display), bitmap = CreateCompatibleBitmap(display, w, h);
        IntPtr previous = SelectObject(memory, bitmap), record = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DrawItem)));
        try
        {
            var draw = new DrawItem { Type=4, Id=(uint)id, Window=button, Dc=memory, Bounds=new Rect {Right=w, Bottom=h} };
            foreach (uint action in new uint[] { 2, 4 }) // Windows requests immediate paints for press and focus.
            {
                Check(PatBlt(memory,0,0,w,h,0x00ff0062),"clear the test surface to white");
                draw.Action = action;
                draw.State = action == 2 ? 1u : 16u;
                Marshal.StructureToPtr(draw, record, false);
                Send(parent, 0x2b, new IntPtr(id), record);
                int centre = id==Dark || id==Light ? h*4/5 : h/2;
                int sample = GetPixel(memory, 8, centre);
                Check(sample > 0 && sample != 0xffffff, "button painted behind the native radio glyph position");
                for (int x = 5; x <= 9; x++)
                    for (int y = centre - 3; y <= centre + 3; y++)
                        Check(GetPixel(memory,x,y)==sample,"no extra ring or dot at left of button " + id);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(record);
            SelectObject(memory,previous);
            DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(button,display);
        }
    }
    private static void CheckText(IntPtr window, bool light)
    {
        int width,height;
        byte[] pixels = Render(window,out width,out height);
        double scale = width / 692.0;
        int ink = 0, edges = 0;
        for (int y = (int)(12*scale); y < (int)(42*scale); y++)
        {
            for (int x = (int)(80*scale); x < (int)(325*scale); x++)
            {
                int c = Pixel(pixels,width,x,y);
                int r=c&255, g=(c>>8)&255, b=(c>>16)&255;
                Check(Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b)) < 24, "neutral text edges in the heading");
                if (light ? r<100 : r>180) ink++;
                if (r>100 && r<180) edges++;
            }
        }
        Check(ink>110 && edges>40,"legible, antialiased heading in both themes");
    }

    private static void CheckDisplayAndInput()
    {
        IntPtr old = SetThreadDpiAwarenessContext(new IntPtr(-1));
        try
        {
            Open("PLANAR display and input", delegate(IntPtr w) {
                Check(AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(w),new IntPtr(-4)), "journal window is per-monitor aware in a legacy DPI host");
                IntPtr tanto = GetDlgItem(w,Tanto);
                Send(tanto,0x201,new IntPtr(1),PointAt(tanto,.6,.5));
                Check((Send(tanto,0xf2,Zero,Zero).ToInt32() & 4)!=0,"physical mouse button is down");
                CheckPressedSurface(w,Tanto);
                Send(tanto,0x202,Zero,PointAt(tanto,.6,.5));
                Check(Send(tanto,BmGetCheck,Zero,Zero).ToInt32()==1,"mouse selection is committed on release");
                CheckPressedSurface(w,Outside);
                Send(GetDlgItem(w,Inside),0x100,new IntPtr(39),Zero);
                CheckPair(w,Inside,Outside,true);
                Check((GetWindowLongW(GetDlgItem(w,Outside),-16)&0x10000)!=0,"Tab uses the selected radio");
                Send(GetDlgItem(w,Outside),0x100,new IntPtr(37),Zero);
                CheckPair(w,Inside,Outside,false);
                IntPtr blank = GetDlgItem(w,Blank);
                Send(blank,0x100,new IntPtr(32),Zero); Send(blank,0x101,new IntPtr(32),Zero);
                Check(Send(blank,BmGetCheck,Zero,Zero).ToInt32()==1,"Space toggles the independent checkbox");
                Rect bounds; Check(GetWindowRect(w,out bounds),"window bounds at 96 DPI");
                IntPtr rect=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Rect)));
                try
                {
                    Marshal.StructureToPtr(bounds,rect,false);
                    foreach (int dpi in new int[]{120,144,96})
                    {
                        Send(w,0x2e0,new IntPtr(dpi | (dpi<<16)),rect);
                        Rect changed; Check(GetWindowRect(w,out changed),"DPI bounds");
                        int width=changed.Right-changed.X;
                        Check(dpi==96 ? width==692 : width>692 && width<=(int)Math.Round(692*dpi/96.0),"DPI layout "+dpi+" : "+width);
                        CheckText(w,false);
                    }
                }
                finally { Marshal.FreeHGlobal(rect); }
                Click(w,Cancel);
            });
            Check(AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(),new IntPtr(-1)), "caller DPI restored");
        }
        finally { if(old!=Zero) SetThreadDpiAwarenessContext(old); }
    }
    private static IntPtr OpenPalette(IntPtr window, string title)
    {
        Click(window, Gear);
        IntPtr popup = FindWindow("#32770", "Оформление — " + title);
        Check(popup != Zero && IsWindowVisible(popup) && GetDlgItem(popup, Square) != Zero, "modeless palette is open");
        return popup;
    }
    private static IntPtr PointAt(IntPtr window, double x, double y)
    {
        Rect b;
        Check(GetWindowRect(window, out b), "picker area");
        int xx = (int)Math.Round(x * (b.Right - b.X)), yy = (int)Math.Round(y * (b.Bottom - b.Y));
        return new IntPtr((xx & 65535) | ((yy & 65535) << 16));
    }
    private static int NativeColour(string hex)
    {
        int c = Convert.ToInt32(hex.TrimStart('#'), 16);
        return (c >> 16) | (c & 65280) | ((c & 255) << 16);
    }
    private static byte[] Render(IntPtr hwnd, out int width, out int height)
    {
        Rect rect;
        Check(GetWindowRect(hwnd, out rect), "render bounds");
        width = rect.Right - rect.X;
        height = rect.Bottom - rect.Y;
        IntPtr display = GetDC(hwnd), memory = CreateCompatibleDC(display), bmp = CreateCompatibleBitmap(display, width, height);
        IntPtr previous = SelectObject(memory, bmp);
        try
        {
            Send(hwnd, 0x317, memory, new IntPtr(0x1e));
            SelectObject(memory, previous);
            var header = new BitmapHeader
            {
                Size = 40,
                Width = width,
                Height = -height,
                Planes = 1,
                Bits = 32
            };
            byte[] bgra = new byte[width * height * 4];
            Check(GetDIBits(display, bmp, 0, (uint)height, bgra, ref header, 0) == height, "read rendered controls");
            return bgra;
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bmp);
            DeleteDC(memory);
            ReleaseDC(hwnd, display);
        }
    }
    private static int Pixel(byte[] pixels, int w, int x, int y)
    {
        int i = 4 * (y * w + x);
        return pixels[i + 2] | (pixels[i + 1] << 8) | (pixels[i] << 16);
    }
    private static int PrimaryColour(IntPtr w)
    {
        int width, height;
        byte[] pixels = Render(GetDlgItem(w, Continue), out width, out height);
        return Pixel(pixels, width, width / 2, Math.Max(2, height / 6));
    }
    private static void Snapshot(IntPtr window, string name)
    {
        string dir = Environment.GetEnvironmentVariable("NX_PLANAR_APPEARANCE_PREVIEW");
        if (String.IsNullOrEmpty(dir))
            return;
        int w, h;
        byte[] pixels = Render(window, out w, out h);
        using (var stream = new System.IO.BinaryWriter(System.IO.File.Create(System.IO.Path.Combine(dir, name + ".bmp"))))
        {
            stream.Write((ushort)0x4d42);
            stream.Write(54 + pixels.Length);
            stream.Write(0);
            stream.Write(54);
            stream.Write(40);
            stream.Write(w);
            stream.Write(-h);
            stream.Write((ushort)1);
            stream.Write((ushort)32);
            stream.Write(0);
            stream.Write(pixels.Length);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(pixels);
        }
    }
    private static void CheckBlueMark(IntPtr window)
    {
        int w, h, runs = 0;
        byte[] pixels = Render(window, out w, out h);
        double scale = w / 692.0;
        int x = (int)Math.Round(47.5 * scale);
        bool was = false;
        for (int y = (int)(26 * scale); y < (int)(45 * scale); y++)
        {
            int c = Pixel(pixels, w, x, y);
            int red = c & 255, green = (c >> 8) & 255, blueChannel = c >> 16;
            bool blue = blueChannel > red + 30 && green > red + 25 && blueChannel > green + 10;
            if (blue && !was)
                runs++;
            was = blue;
        }

        Check(runs == 3, "three independent cyan passes, found " + runs);
    }
    private static void CheckGradient(IntPtr popup)
    {
        int width, height;
        byte[] pixels = Render(popup, out width, out height);
        Rect parent;
        Check(GetWindowRect(popup, out parent), "palette bounds");
        Rect square = Bounds(popup, Square), hue = Bounds(popup, Hue);
        int sx = (square.X + square.Right) / 2 - parent.X;
        int hx = (hue.X + hue.Right) / 2 - parent.X;
        int nearWhite = Pixel(pixels, width, square.X - parent.X + (square.Right - square.X) / 10, square.Y - parent.Y + (square.Bottom - square.Y) / 10);
        int middle = Pixel(pixels, width, sx, (square.Y + square.Bottom) / 2 - parent.Y);
        int bottom = Pixel(pixels, width, sx, square.Bottom - parent.Y - (square.Bottom - square.Y) / 10);
        Check((nearWhite & 255) > 207 && (nearWhite >> 16) > 180, "bright area across the square");
        Check((middle & 255) > 110 && (middle & 255) < 145 && (middle >> 16) < 95, "centre of the orange gradient");
        Check((bottom & 255) < 40 && (bottom >> 16) < 40, "dark end of the square");
        int cyan = Pixel(pixels, width, hx, (hue.Y + hue.Bottom) / 2 - parent.Y);
        Check((cyan & 255) < 25 && ((cyan >> 8) & 255) > 220 && (cyan >> 16) > 220, "rainbow strip is visible inside the popup");
    }
    private static void CheckLiveStyle()
    {
        CheckDisplayAndInput();
        string local = System.IO.Path.ChangeExtension(prefs, "appearance");
        string shared = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(prefs), "Appearance.settings");
        const string title = "PLANAR appearance";
        Open(title, delegate (IntPtr w)
        {
            Snapshot(w, "compact-on");
            Click(w, Tanto);
            Click(w, Outside);
            Click(w, Right);
            IntPtr p = OpenPalette(w, title), field = GetDlgItem(p, Hex), square = GetDlgItem(p, Square), hue = GetDlgItem(p, Hue);
            Check(TextOf(field) == "#00CD86", "default accent");
            Check(!System.IO.File.Exists(local) && !System.IO.File.Exists(shared), "opening palette is read-only");
            SetWindowTextW(field, "#FF790F");
            Check(PrimaryColour(w) == NativeColour("#FF790F"), "HEX updates the main action before confirmation");
            Snapshot(w, "dark-main");
            Snapshot(p, "dark-menu");
            CheckText(w, false);
            CheckPressedSurface(p, Dark);
            CheckBlueMark(w);
            CheckGradient(p);
            string before = System.IO.File.ReadAllText(local);
            Send(square, 0x201, new IntPtr(1), PointAt(square, .23, .75));
            string first = TextOf(field);
            Check(PrimaryColour(w) == NativeColour(first) && first != "#FF790F", "live square while pressed");
            Send(square, 0x200, new IntPtr(1), PointAt(square, .72, .26));
            string second = TextOf(field);
            Check(second != first && PrimaryColour(w) == NativeColour(second), "live move before mouseup");
            Check(System.IO.File.ReadAllText(local) == before, "no disk writes during drag");
            Send(square, 0x202, Zero, PointAt(square, .72, .26));
            Check(System.IO.File.ReadAllText(local).Contains(second), "commit on release");
            Send(hue, 0x201, new IntPtr(1), PointAt(hue, .5, .19));
            string third = TextOf(field);
            Send(hue, 0x200, new IntPtr(1), PointAt(hue, .5, .69));
            Check(third != TextOf(field) && PrimaryColour(w) == NativeColour(TextOf(field)), "live hue before release");
            Send(hue, 0x202, Zero, PointAt(hue, .5, .69));
            CheckPair(w, Inside, Outside, true);
            CheckPair(w, Left, Right, true);
            Click(p, Light);
            SetWindowTextW(field, "#FF790F");
            Snapshot(w, "light-main");
            Snapshot(p, "light-menu");
            CheckText(w, true);
            CheckPressedSurface(p, Light);
            CheckBlueMark(w);
            CheckGradient(p);
            Click(p, Shared);
            Check(System.IO.File.ReadAllText(shared).Contains("light|#FF790F"), "shared profile is explicit");
            Send(square, 0x201, new IntPtr(1), PointAt(square, .55, .55));
            Send(square, 0x202, Zero, PointAt(square, -.2, -.2));
            Check(TextOf(field) == "#FFFFFF", "white square corner");
            Send(square, 0x201, new IntPtr(1), PointAt(square, .7, .45));
            Send(square, 0x202, Zero, PointAt(square, 1.1, 1.1));
            Check(TextOf(field) == "#000000", "black square corner");
            Send(hue, 0x201, new IntPtr(1), PointAt(hue, .5, .3));
            Send(hue, 0x202, Zero, PointAt(hue, .5, .3));
            Check(TextOf(field) == "#000000", "black retains independent hue");
            SetWindowTextW(field, "#ace");
            Send(field, 0x100, new IntPtr(13), Zero);
            Check(TextOf(field) == "#AACCEE", "three-digit HEX");
            SetWindowTextW(field, "#ZZZZZZ");
            Send(field, 0x100, new IntPtr(13), Zero);
            Check(TextOf(field) == "#AACCEE", "invalid input restores previous colour");
            SetWindowTextW(field, "#1020CD");
            Send(square, 0x100, new IntPtr(38), Zero);
            Check(TextOf(field) != "#1020CD", "square arrow changes brightness");
            SetWindowTextW(field, "#314159");
            Send(field, 0x100, new IntPtr(27), Zero);
            Check(FindWindow("#32770", "Оформление — " + title) == Zero, "Escape closes only the palette");
            uint start = GetGuiResources(GetCurrentProcess(), 0);
            for (int i = 0; i < 6; i++)
            {
                p = OpenPalette(w, title);
                Click(p, PaletteClose);
            }

            Check(GetGuiResources(GetCurrentProcess(), 0) <= start + 5, "palette resources released");
            Click(w, Cancel);
        });
        const string reopened = "Saved appearance";
        Open(reopened, delegate (IntPtr w)
        {
            IntPtr p = OpenPalette(w, reopened);
            Check(TextOf(GetDlgItem(p, Hex)) == "#314159" && Send(GetDlgItem(p, Light), BmGetCheck, Zero, Zero).ToInt32() == 1, "local preferences survive cancellation");
            Check(!IsWindowVisible(GetDlgItem(w, Outside)), "cancel did not apply CAM changes");
            Click(p, PaletteClose);
            Click(w, Cancel);
        });
        System.IO.File.Delete(local);
        Open(reopened, delegate (IntPtr w)
        {
            IntPtr p = OpenPalette(w, reopened);
            Check(TextOf(GetDlgItem(p, Hex)) == "#FF790F", "shared fallback with no local override");
            Click(p, PaletteClose);
            Click(w, Cancel);
        });
        const string reportTitle = "PLANAR report";
        const string reportText = "Part Boundaries 36\r\nBlank Boundaries 5";
        Open(reportTitle, delegate (IntPtr w)
        {
            Check(TextOf(GetDlgItem(w, 130)) == reportText, "complete diagnostic in the report");
            Check(TextOf(GetDlgItem(w, 131)) == "Копировать всё", "copy report action available");
            Check(PrimaryColour(w) == NativeColour("#FF790F"), "report uses the saved appearance");
            Click(w, Continue);
        }, delegate
        {
            PlanarCurveWindows.ShowReport(Zero, reportTitle, "Границы добавлены", reportText, 0, prefs);
        });
    }

    private static int Run(bool appearanceOnly)
    {
        desktop = CreateDesktop("PlanarSideTest" + Guid.NewGuid().ToString("N"), Zero, Zero, 0, 0xc3, Zero);
        if (desktop == Zero || !SetThreadDesktop(desktop))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 5 || error == 170)
                return 77;
            throw new Win32Exception(error);
        }

        if (appearanceOnly)
        {
            CheckLiveStyle();
            return 0;
        }

        foreach (bool outside in new[]
        {
            false,
            true
        }

        )
            foreach (bool right in new[]
            {
                false,
                true
            }

            )
            {
                System.IO.File.Delete(prefs);
                PlanarCurveOptions options = Open("TANTO " + outside + " " + right, delegate (IntPtr w)
                {
                    CheckEnabled(w, false);
                    CheckPlane(w, true);
                    int compactFooter = Bounds(w, Continue).Y;
                    Check(Bounds(w, PlaneChoice).Bottom <= Bounds(w, On).Y && Bounds(w, On).Bottom <= compactFooter, "ON fits the compact dialog");
                    Click(w, PlaneChoice);
                    CheckPlane(w, false);
                    CheckPair(w, Inside, Outside, false);
                    CheckPair(w, Left, Right, false);
                    Check(Bounds(w, Inside).Right <= Bounds(w, Outside).X && Bounds(w, Outside).Right <= Bounds(w, Left).X && Bounds(w, Left).Right <= Bounds(w, Right).X, "four rounded sides in two groups");
                    Click(w, Tanto);
                    CheckEnabled(w, true);
                    Check(Bounds(w, Outside).Bottom <= Bounds(w, Continue).Y && Bounds(w, Continue).Y > compactFooter, "TANTO opens space for the sides");
                    int closedId = outside ? Outside : Inside;
                    Click(w, closedId);
                    Click(w, closedId);
                    CheckPair(w, Inside, Outside, outside);
                    CheckPair(w, Left, Right, false);
                    int openId = right ? Right : Left;
                    Click(w, openId);
                    Click(w, openId);
                    CheckPair(w, Inside, Outside, outside);
                    CheckPair(w, Left, Right, right);
                    Click(w, On);
                    CheckEnabled(w, false);
                    CheckPlane(w, false);
                    Click(w, Tanto);
                    if (outside)
                        Click(w, PlaneChoice);
                    CheckPlane(w, outside);
                    CheckEnabled(w, true);
                    CheckPair(w, Inside, Outside, outside);
                    CheckPair(w, Left, Right, right);
                    Click(w, Continue);
                });
                Check(options != null && options.Tanto && options.ClosedOutside == outside && options.OpenRight == right && options.SelectPlane == outside, "returned combination");
            }

        System.IO.File.Delete(prefs);
        PlanarCurveOptions on = Open("ON ignores sides", delegate (IntPtr w)
        {
            Click(w, Tanto);
            Click(w, Outside);
            Click(w, Right);
            Click(w, On);
            Click(w, Continue);
        });
        Check(on != null && on.SelectPlane && !on.Tanto && !on.UsesOutsideOrRight(false) && !on.UsesOutsideOrRight(true), "ON ignores side selections");
        Check(Open("Cancel", delegate (IntPtr w)
        {
            Click(w, Tanto);
            Click(w, Right);
            Click(w, Cancel);
        }) == null, "cancel");
        System.IO.File.Delete(prefs);
        Open("Defaults", delegate (IntPtr w)
        {
            CheckPair(w, Part, Blank, false);
            Click(w, Part);
            Check(!IsWindowEnabled(GetDlgItem(w, Continue)), "at least one set required");
            Send(w, 0x111, new IntPtr(Continue), Zero);
            Click(w, Blank);
            CheckPair(w, Part, Blank, true);
            Click(w, Part);
            Click(w, Tanto);
            Click(w, Outside);
            Check(Send(GetDlgItem(w, Part), BmGetCheck, Zero, Zero).ToInt32() == 1 && Send(GetDlgItem(w, Blank), BmGetCheck, Zero, Zero).ToInt32() == 1, "both independent");
            VerifyAppearance(w);
            Click(w, Continue);
        });
        Open("Cancel edited options", delegate (IntPtr w)
        {
            CheckEnabled(w, true);
            CheckPair(w, Inside, Outside, true);
            CheckPlane(w, true);
            Check(Send(GetDlgItem(w, Blank), BmGetCheck, Zero, Zero).ToInt32() == 1, "blank restored");
            Click(w, Blank);
            Click(w, PlaneChoice);
            Click(w, On);
            Click(w, Cancel);
        });
        var restored = Open("Reopen", delegate (IntPtr w)
        {
            Click(w, Continue);
        });
        Check(restored.Part && restored.Blank && restored.SelectPlane && restored.Tanto && restored.ClosedOutside && !restored.OpenRight, "cancel preserves last confirmed choices");
        restored.LastMethod = "Distance";
        restored.OffsetMm = -25.4;
        restored.Save(prefs);
        var previous = PlanarCurveOptions.Load(prefs);
        Check(previous.LastMethod == "Distance" && previous.OffsetMm == restored.OffsetMm, "signed plane preset survives a new journal instance");
        System.IO.File.WriteAllText(prefs, "damaged");
        var recovered = Open("Recover defaults", delegate (IntPtr w)
        {
            Click(w, Continue);
        });
        Check(recovered.Part && !recovered.Blank && !recovered.Tanto && recovered.SelectPlane, "malformed settings use defaults");
        return 0;
    }
    public static int Main(string[] args)
    {
        return ManagedTestHost.Run(delegate
        {
            return RunOnDesktop(args.Length != 0 && args[0] == "appearance");
        });
    }
    private static int RunOnDesktop(bool appearanceOnly)
    {
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "planar-ui-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(folder);
        prefs = System.IO.Path.Combine(folder, "PlanarAddCurves.settings");
        int status = 1;
        Thread gui = new Thread((ThreadStart)delegate
        {
            try
            {
                status = Run(appearanceOnly);
            }
            catch (Exception ex)
            {
                status = ManagedTestHost.Record(ex);
            }
        });
        gui.SetApartmentState(ApartmentState.MTA);
        gui.IsBackground = true;
        gui.Start();
        if (!gui.Join(35000))
            throw new TimeoutException("Private desktop test exceeded 35 seconds");
        foreach (string file in new[]
        {
            prefs,
            System.IO.Path.ChangeExtension(prefs, "appearance"),
            System.IO.Path.Combine(folder, "Appearance.settings")
        }

        )
            System.IO.File.Delete(file);
        System.IO.Directory.Delete(folder);
        return status;
    }
}
