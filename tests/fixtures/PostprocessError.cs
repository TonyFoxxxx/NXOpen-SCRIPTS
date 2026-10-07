// Exercise the production dialog with real modal Forms and the Windows clipboard.
internal static class PostprocessErrorChecks
{
    private static int checks;
    private static void Assert(bool ok, string message)
    { checks++; if (!ok) throw new Exception(message); }
    internal static void Run(bool themed)
    {
        string caption = ScriptInfo.WindowTitle("\u041E\u0448\u0438\u0431\u043A\u0430");
        string text = "WorkOffsetException: O5601, \u0441\u0442\u0440\u043E\u043A\u0430 12\n\n" +
            "\u041F\u0440\u043E\u0444\u0438\u043B\u044C: Auto\n> 12: G43 Z20 H1 X1\n  13: G01 Z-1 F100\n\n" +
            "\u041F\u0430\u043A\u0435\u0442 \u043E\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D \u0434\u043E \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u044F \u0423\u041F.";
        foreach (float scale in new float[] { 1F, 1.5F, 2F })
        using (PostprocessErrorDialog dialog = new PostprocessErrorDialog(text, caption))
        {
            Form form = (Form)typeof(PostprocessErrorDialog).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dialog);
            form.Scale(new SizeF(scale, scale));
            Exception failure = null;
            form.Shown += delegate
            {
                try
                {
                    TextBox details = null; Button copy = null, close = null;
                    foreach (Control item in form.Controls)
                    {
                        if (item is TextBox) details = (TextBox)item;
                        if (item is Panel)
                            foreach (Control child in item.Controls)
                            {
                                if (child.Dock == DockStyle.Left) copy = child as Button;
                                if (child.Dock == DockStyle.Right) close = child as Button;
                            }
                    }
                    Assert(form.Text == caption && caption.Contains(ScriptInfo.SCRIPT_VERSION), "versioned diagnostic title");
                    Assert(details != null && details.ReadOnly && details.Multiline && !details.WordWrap, "selectable read-only NC context");
                    Assert(details.ScrollBars == ScrollBars.Both, "long NC lines remain accessible");
                    Assert(details.Text == text.Replace("\n", "\r\n"), "full error text and line endings");
                    Assert(details.Height > 150, "details remain visible at " + scale);
                    Assert(copy != null && close != null && copy.Right <= close.Left, "exactly two separate actions");
                    Assert(copy.Parent.Controls.Count == 2, "no extra actions");
                    Assert(form.CancelButton == close, "Escape closes diagnostic");
                    Clipboard.SetText("before");
                    copy.PerformClick();
                    Assert(Clipboard.GetText() == caption + "\r\n\r\n" + text, "copy includes version and original diagnostics");
                    Assert(form.Visible, "copy keeps diagnostic open");
                    if (scale == 1F && themed)
                    {
                        using (Bitmap bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                        using (MemoryStream bytes = new MemoryStream())
                        {
                            form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(bytes, ImageFormat.Png);
                            Console.WriteLine("POST_ERROR_PNG:" + Convert.ToBase64String(bytes.ToArray()));
                        }
                    }
                    close.PerformClick();
                }
                catch (Exception ex) { failure = ex; form.Close(); }
            };
            string result = dialog.ShowDialog(null);
            if (failure != null) throw failure;
            Assert(result == "Cancel", "Close returns from modal dialog");
        }
        Console.WriteLine("PASS real Windows post error: " + checks + " layout/clipboard/modal checks.");
    }
}
