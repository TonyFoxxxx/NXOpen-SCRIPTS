// Production controls, setup and event methods are inserted verbatim.
// The enclosing window replaces NX data; all controls and layout are real Forms.
internal sealed class DescriptionInlineFixture : IDisposable
{
    internal readonly Form Window = new Form();
    internal readonly Panel Header = new Panel();
    internal readonly TreeView Tree = new TreeView();
    internal readonly ToolTip Tips = new ToolTip();
    /* PRODUCTION_FIELDS */
    internal CheckBox Toggle { get { return (CheckBox)updateDescriptions; } }
    internal Panel Choices { get { return (Panel)descriptionFormats; } }
    internal RadioButton Diameter { get { return (RadioButton)diameterOnly; } }
    internal RadioButton Full { get { return (RadioButton)diameterAndNumbers; } }
    internal CheckBox Zmin { get { return (CheckBox)addZmin; } }
    internal DescriptionInlineFixture()
    {
        Window.AutoScaleMode = AutoScaleMode.None;
        Window.ClientSize = new Size(820, 654); Window.Padding = new Padding(18);
        Header.Dock = DockStyle.Top; Header.Size = new Size(784, 258);
        Tree.Dock = DockStyle.Fill;
        Panel footer = new Panel(); footer.Dock = DockStyle.Bottom; footer.Height = 52;
        Window.Controls.Add(Tree); Window.Controls.Add(Header); Window.Controls.Add(footer);
        /* PRODUCTION_SETUP */
    }
    private static void Position(object control, int x, int y, int width, int height)
    {
        RuntimeForms.Set(control, "Left", x); RuntimeForms.Set(control, "Top", y);
        RuntimeForms.Set(control, "Width", width); RuntimeForms.Set(control, "Height", height);
    }
    public void Dispose() { Window.Dispose(); Tips.Dispose(); }
}

internal static class DescriptionInlineChecks
{
    private static int checks;
    private static void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    internal static void Run(bool themed)
    {
        foreach (float scale in new float[] { 1F, 1.5F, 2F })
        using (DescriptionInlineFixture ui = new DescriptionInlineFixture())
        {
            // Explicit control scaling tests geometry; this is not a desktop DPI test.
            ui.Window.Scale(new SizeF(scale, scale));
            // Match the production minimum size. Otherwise the small CI desktop
            // clamps the scaled test form and consumes its entire tree area.
            ui.Window.MinimumSize = new Size((int)(800 * scale), (int)(544 * scale));
            ui.Window.Show(); Application.DoEvents();
            // The runner has a 1024x768 desktop: Forms clamps even MinimumSize
            // to its maximum tracking size. Use a native offscreen window size
            // to exercise 200% geometry as on a suitably sized production screen.
            if (scale > 1F)
            {
                Assert(SetWindowPos(ui.Window.Handle, IntPtr.Zero, 0, 0,
                    (int)(840 * scale), (int)(700 * scale), 0x0416), "resize scaled test viewport");
                Application.DoEvents();
            }
            int header = ui.Header.Height, top = ui.Zmin.Top, treeHeight = ui.Tree.Height;
            Assert(!ui.UpdateDescriptions && !ui.IncludeToolNumbers && !ui.Choices.Visible, "initially disabled and hidden");
            Assert(ui.Diameter.Checked && !ui.Full.Checked, "diameter is default for each launch");
            Assert(ui.Diameter.Parent == ui.Full.Parent && ui.Choices.Controls.Count == 2, "one native radio group");
            for (int pass = 0; pass < 4; pass++)
            {
                ui.Toggle.Checked = true; Application.DoEvents();
                Assert(Application.OpenForms.Count == 1, "enabling Description must not open a modal window");
                Assert(ui.Choices.Visible && ui.UpdateDescriptions, "choices appear inline");
                Assert(ui.Choices.Top >= ui.Toggle.Bottom && ui.Zmin.Top > ui.Choices.Bottom, "inline rows must not overlap");
                Assert(ui.Header.Height > ui.Zmin.Bottom && ui.Tree.Top >= ui.Header.Bottom, "header and tree must not overlap");
                Assert(ui.Tree.Height > 0, "tree remains available at " + scale + "; client=" + ui.Window.ClientSize + "; header=" + ui.Header.Height);
                ui.Full.PerformClick();
                Assert(ui.Full.Checked && !ui.Diameter.Checked && ui.IncludeToolNumbers, "full format excludes diameter-only");
                ui.Diameter.PerformClick();
                Assert(ui.Diameter.Checked && !ui.Full.Checked && !ui.IncludeToolNumbers, "diameter-only excludes full format");
                ui.Full.PerformClick();
                ui.Toggle.Checked = false; Application.DoEvents();
                Assert(!ui.Choices.Visible && !ui.IncludeToolNumbers, "disabling hides and disables description format");
                Assert(ui.Header.Height == header && ui.Zmin.Top == top && ui.Tree.Height == treeHeight, "repeated toggles must restore layout exactly");
                ui.Toggle.Checked = true;
                Assert(ui.Full.Checked && ui.IncludeToolNumbers, "current launch retains chosen format after hiding");
                ui.Toggle.Checked = false;
            }
            if (scale == 1F && themed)
            {
                ui.Toggle.Checked = true; ui.Diameter.PerformClick(); Application.DoEvents();
                using (Bitmap bitmap = new Bitmap(ui.Header.Width, ui.Header.Height))
                using (MemoryStream stream = new MemoryStream())
                {
                    ui.Header.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(stream, ImageFormat.Png);
                    Console.WriteLine("INLINE_DESCRIPTION_PNG:" + Convert.ToBase64String(stream.ToArray()));
                }
            }
            ui.Window.Close();
        }
        Console.WriteLine("PASS real Windows inline Description: " + checks + " checks; " + (themed ? "themed" : "classic"));
    }
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
