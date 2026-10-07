using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// The renderer is extracted verbatim from the journal by the Python test.
// This adapter resolves the same APIs against the real loaded Forms assembly.
internal static class RuntimeForms
{
    internal static Type FormType(string name) { return typeof(Form).Assembly.GetType("System.Windows.Forms." + name, true); }
    internal static object Get(object target, string name) { return target.GetType().GetProperty(name).GetValue(target, null); }
    internal static void Set(object target, string name, object value) { target.GetType().GetProperty(name).SetValue(target, value, null); }
    internal static void SetEnum(object target, string name, string value)
    {
        PropertyInfo property = target.GetType().GetProperty(name);
        property.SetValue(target, Enum.Parse(property.PropertyType, value), null);
    }
    internal static object Call(object target, string name, params object[] arguments)
    { return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance, null, target, arguments); }
    internal static void Dispose(object target) { IDisposable disposable = target as IDisposable; if (disposable != null) disposable.Dispose(); }
}

internal static class TreeDrawingChecks
{
    private static int checks;
    private static void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            bool themed = args.Length != 0 && args[0] == "themed";
            if (themed) Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            float[] sizes = { 8.25F, 12F, 16.5F };
            int[] heights = { 26, 39, 52 };
            for (int i = 0; i < sizes.Length; i++)
                Run(sizes[i], heights[i], true, i == 0, themed);
            Run(8.25F, 26, false, false, themed);
            Console.WriteLine("PASS real Windows tree: " + checks + " pixel/interaction checks; " + args[0]);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Run(float fontSize, int rowHeight, bool checkBoxes, bool emitImage, bool themed)
    {
        using (Form form = new Form())
        using (Font font = new Font("Microsoft Sans Serif", fontSize))
        using (TreeView tree = new TreeView())
        {
            form.ClientSize = new Size(780, 580);
            tree.Dock = DockStyle.Fill; tree.Font = font; tree.ItemHeight = rowHeight;
            tree.CheckBoxes = checkBoxes; tree.Indent = 24; tree.HideSelection = false;
            tree.ForeColor = Color.FromArgb(0, 0, 160); tree.BackColor = Color.White;
            tree.ShowLines = true; tree.ShowRootLines = true; tree.ShowPlusMinus = true;
            TreeTextLayout.Attach(tree);
            TreeNode root = tree.Nodes.Add("NC_PROGRAM");
            TreeNode none = root.Nodes.Add("NONE"); none.Nodes.Add("O99");
            TreeNode first = root.Nodes.Add("PODGOTOVKA_1"); first.Nodes.Add("O1");
            TreeNode second = root.Nodes.Add("PODGOTOVKA_2"); second.Nodes.Add("O2");
            TreeNode main = root.Nodes.Add("MAIN"); TreeNode setup = main.Nodes.Add("1_UST");
            for (int i = 1; i <= 4; i++) setup.Nodes.Add("O3" + i);
            root.ExpandAll(); none.Collapse();
            form.Controls.Add(tree); form.Show(); Application.DoEvents();
            tree.SelectedNode = null; tree.Refresh(); Application.DoEvents();
            using (Bitmap bitmap = Capture(tree))
            {
                if (emitImage) Emit(bitmap, themed ? "themed" : "classic");
                CheckPixels(tree, bitmap, checkBoxes);
            }
            Assert(!none.IsExpanded, "NONE must remain collapsed");
            Assert(tree.DrawMode == TreeViewDrawMode.OwnerDrawAll, "every glyph must use the same row geometry");
            if (checkBoxes)
            {
                int before = 0, after = 0;
                tree.BeforeCheck += delegate(object sender, TreeViewCancelEventArgs e) { before++; if (e.Node == none) e.Cancel = true; };
                tree.AfterCheck += delegate { after++; };
                Point firstCheck = StatePoint(tree, first);
                Assert(tree.HitTest(firstCheck).Location == TreeViewHitTestLocations.StateImage, "painted checkbox must be a native state target");
                Click(tree, firstCheck);
                Assert(first.Checked && before == 1 && after == 1, "mouse check must retain native events");
                Click(tree, StatePoint(tree, none));
                Assert(!none.Checked && before == 2 && after == 1, "BeforeCheck cancellation must survive");
                tree.SelectedNode = first; tree.Focus();
                SendMessageW(tree.Handle, 0x0100, new IntPtr(0x20), IntPtr.Zero);
                SendMessageW(tree.Handle, 0x0101, new IntPtr(0x20), IntPtr.Zero);
                Application.DoEvents();
                Assert(!first.Checked && after == 2, "Space must toggle the native node");
                first.Checked = true; tree.SelectedNode = null;
                using (Bitmap bitmap = Capture(tree)) CheckPixels(tree, bitmap, true);
            }
            Point expand = BranchPoint(tree, main);
            Assert(tree.HitTest(expand).Location == TreeViewHitTestLocations.PlusMinus, "painted expander must be a native button target");
            Click(tree, expand); Assert(!main.IsExpanded, "mouse must collapse branch");
            Click(tree, BranchPoint(tree, main)); Assert(main.IsExpanded, "mouse must expand branch");
            // Exercise a nonzero vertical scroll origin and a long horizontal label.
            setup.Nodes[3].Text = "O34_" + new string('W', 160);
            form.ClientSize = new Size(350, 210); setup.Nodes[3].EnsureVisible();
            SendMessageW(tree.Handle, 0x0114, new IntPtr(6), IntPtr.Zero); // SB_LEFT after EnsureVisible
            tree.SelectedNode = null; tree.Refresh(); Application.DoEvents();
            using (Bitmap bitmap = Capture(tree)) CheckPixels(tree, bitmap, checkBoxes);
            SendMessageW(tree.Handle, 0x0114, new IntPtr(1), IntPtr.Zero); // SB_LINERIGHT
            Application.DoEvents();
            using (Bitmap bitmap = Capture(tree)) CheckPixels(tree, bitmap, checkBoxes);
            form.Close();
        }
    }

    private static Bitmap Capture(TreeView tree)
    {
        tree.Refresh(); Application.DoEvents();
        Bitmap bitmap = new Bitmap(tree.Width, tree.Height);
        tree.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        return bitmap;
    }
    private static void Emit(Bitmap bitmap, string theme)
    {
        using (MemoryStream stream = new MemoryStream())
        {
            bitmap.Save(stream, ImageFormat.Png);
            Console.WriteLine("TREE_PNG_" + theme + ":" + Convert.ToBase64String(stream.ToArray()));
        }
    }
    private static void CheckPixels(TreeView tree, Bitmap bitmap, bool checkBoxes)
    {
        int seen = 0;
        for (TreeNode node = tree.TopNode; node != null; node = node.NextVisibleNode)
        {
            NativeRect row = Row(tree, node);
            if (row.Top >= tree.ClientSize.Height) break;
            if (row.Top < 0 || row.Bottom > tree.ClientSize.Height) continue;
            // DrawToBitmap includes the border; native coordinates are client coordinates.
            Point border = tree.PointToScreen(Point.Empty);
            Rectangle window; GetWindowRect(tree.Handle, out window);
            int ox = border.X - window.X, oy = border.Y - window.Y;
            Rectangle label = node.Bounds;
            if (label.Right <= 0 || label.Left >= tree.ClientSize.Width) continue;
            Rectangle textInk = Ink(bitmap, new Rectangle(label.X + ox, row.Top + oy,
                Math.Min(label.Width, tree.ClientSize.Width - label.X), row.Bottom - row.Top), true);
            if (textInk.IsEmpty) Emit(bitmap, "missing");
            Assert(!textInk.IsEmpty, "missing text: " + node.Text + " label=" + label + " row=" + row.Top + ":" + row.Bottom);
            double textCenter = (textInk.Top + textInk.Bottom - 1) / 2.0;
            if (checkBoxes)
            {
                Point point = StatePoint(tree, node);
                int left = point.X, right = point.X;
                while (left > 0 && tree.HitTest(left - 1, point.Y).Location == TreeViewHitTestLocations.StateImage) left--;
                while (right + 1 < label.Left && tree.HitTest(right + 1, point.Y).Location == TreeViewHitTestLocations.StateImage) right++;
                Rectangle boxInk = Ink(bitmap, new Rectangle(left + ox, row.Top + oy, right - left + 1, row.Bottom - row.Top), false);
                Assert(!boxInk.IsEmpty, "missing checkbox: " + node.Text);
                double boxCenter = (boxInk.Top + boxInk.Bottom - 1) / 2.0;
                double delta = Math.Abs(boxCenter - textCenter);
                Console.WriteLine("ROW " + node.Text.Substring(0, Math.Min(node.Text.Length, 20)) + " h=" + tree.ItemHeight +
                    " text=" + textCenter + " box=" + boxCenter + " delta=" + delta);
                Assert(delta <= 2.0, "checkbox/text mismatch: " + node.Text + ", " + delta + " px");
                Assert(Math.Abs(boxCenter - (row.Top + (row.Bottom - row.Top) / 2 + oy)) <= 1,
                    "checkbox must be centered in the actual native row");
            }
            seen++;
        }
        Assert(seen > 0, "no visible rows were verified");
    }
    private static Rectangle Ink(Bitmap bitmap, Rectangle area, bool blue)
    {
        area.Intersect(new Rectangle(Point.Empty, bitmap.Size));
        int left = area.Right, top = area.Bottom, right = -1, bottom = -1;
        for (int y = area.Top; y < area.Bottom; y++) for (int x = area.Left; x < area.Right; x++)
        {
            Color c = bitmap.GetPixel(x, y);
            bool ink = blue ? c.B > c.R + 40 && c.B > c.G + 40 : c.R < 200 && c.G < 200 && c.B < 200;
            if (!ink) continue;
            left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }
    private static Point StatePoint(TreeView tree, TreeNode node)
    {
        NativeRect row = Row(tree, node); int y = row.Top + (row.Bottom - row.Top) / 2;
        int left = -1, right = -1;
        for (int x = Math.Max(0, node.Bounds.Left - 40); x < node.Bounds.Left; x++)
            if (tree.HitTest(x, y).Location == TreeViewHitTestLocations.StateImage)
            { if (left < 0) left = x; right = x; }
        Assert(left >= 0, "native state-image target missing");
        return new Point((left + right) / 2, y);
    }
    private static Point BranchPoint(TreeView tree, TreeNode node)
    {
        NativeRect row = Row(tree, node); int y = row.Top + (row.Bottom - row.Top) / 2;
        int left = -1, right = -1;
        for (int x = 0; x < node.Bounds.Left; x++)
            if (tree.HitTest(x, y).Location == TreeViewHitTestLocations.PlusMinus)
            { if (left < 0) left = x; right = x; }
        Assert(left >= 0, "native expand target missing");
        return new Point((left + right) / 2, y);
    }
    private static void Click(TreeView tree, Point point)
    {
        IntPtr position = new IntPtr((point.Y << 16) | (point.X & 0xffff));
        SendMessageW(tree.Handle, 0x0201, new IntPtr(1), position);
        SendMessageW(tree.Handle, 0x0202, IntPtr.Zero, position); Application.DoEvents();
    }
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct NativeRect
    {
        [FieldOffset(0)] internal IntPtr Item;
        [FieldOffset(0)] internal int Left;
        [FieldOffset(4)] internal int Top;
        [FieldOffset(8)] internal int Right;
        [FieldOffset(12)] internal int Bottom;
    }
    private static NativeRect Row(TreeView tree, TreeNode node)
    {
        NativeRect row = new NativeRect(); row.Item = node.Handle;
        Assert(GetItemRect(tree.Handle, 0x1104U, IntPtr.Zero, ref row) != IntPtr.Zero, "native row unavailable");
        return row;
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr GetItemRect(IntPtr hwnd, uint message, IntPtr wParam, ref NativeRect rect);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    // Only X/Y (left/top) are used; right/bottom are intentionally not interpreted as Width/Height.
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rectangle rect);
}
