// NX_GOST_Smooth_Text.cs - smooth single-stroke engraving text.
// SCRIPT_VERSION: V1
// Reads an external vector GOST_A.FON; no font data is embedded or installed.
// Creates lines and cubic splines in the current WCS XY plane; one undo step.

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using NXOpen;
using NXOpen.UF;
using NXOpen.Utilities;

public class NXGostSmoothText
{
    private const string SCRIPT_VERSION = "V1";
    private static readonly string Title = "Гладкий текст ГОСТ А — " + SCRIPT_VERSION + " — "
        + Encoding.UTF8.GetString(Convert.FromBase64String("YnkgQFRvbnlfRm94eHggKFRHKQ=="));

    public static void Main(string[] args)
    {
        Session session = Session.GetSession();
        UFSession uf = UFSession.GetUFSession();
        Part part = session.Parts.Work;
        Session.UndoMarkId mark = default(Session.UndoMarkId);
        bool editing = false;
        try
        {
            if (part == null) throw new InvalidOperationException("Откройте рабочую деталь.");
            string fontPath, inscription; double height, angle;
            using (InputForm form = new InputForm())
            {
                if (form.ShowDialog() != DialogResult.OK) return;
                fontPath = form.FontPath.Text; inscription = form.Inscription.Text;
                height = (double)form.HeightValue.Value;
                angle = (double)form.AngleValue.Value * Math.PI / 180.0;
            }
            byte[] bytes = File.ReadAllBytes(fontPath);
            GostStrokeGeometry.FontData font = GostStrokeGeometry.ReadFont(bytes);
            List<GostStrokeGeometry.Stroke> geometry = GostStrokeGeometry.Text(font, inscription);
            if (geometry.Count == 0) throw new InvalidOperationException("В тексте нет штрихов.");
            if (geometry.Count > 20000) throw new InvalidOperationException("Слишком длинная надпись. Разделите её на несколько строк.");
            Matrix3x3 orientation = part.WCS.CoordinateSystem.Orientation.Element;
            double[] origin = new double[3]; Tag view; int response;
            uf.Ui.LockUgAccess(UFConstants.UF_UI_FROM_CUSTOM);
            try { uf.Ui.SpecifyScreenPosition("Укажите начало надписи на плоскости XY текущей WCS", null, IntPtr.Zero, origin, out view, out response); }
            finally { uf.Ui.UnlockUgAccess(UFConstants.UF_UI_FROM_CUSTOM); }
            if (response != UFConstants.UF_UI_PICK_RESPONSE) return;
            if (session.Parts.Work == null || session.Parts.Work.Tag != part.Tag)
                throw new InvalidOperationException("Рабочая деталь изменилась. Запустите журнал заново.");
            double scale = part.PartUnits == BasePart.Units.Inches ? height / 25.4 : height;
            mark = session.SetUndoMark(Session.MarkVisibility.Visible, "Гладкий текст ГОСТ А");
            editing = true;
            int lines = 0, splines = 0;
            foreach (GostStrokeGeometry.Stroke stroke in geometry)
            {
                Point3d[] points = new Point3d[stroke.P.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    double px = scale * (stroke.P[i].X * Math.Cos(angle) - stroke.P[i].Y * Math.Sin(angle));
                    double py = scale * (stroke.P[i].X * Math.Sin(angle) + stroke.P[i].Y * Math.Cos(angle));
                    points[i] = new Point3d(origin[0] + px * orientation.Xx + py * orientation.Yx,
                        origin[1] + px * orientation.Xy + py * orientation.Yy,
                        origin[2] + px * orientation.Xz + py * orientation.Yz);
                }
                Curve created;
                if (points.Length == 2) { created = part.Curves.CreateLine(points[0], points[1]); lines++; }
                else
                {
                    double[,] poles = new double[4, 4];
                    for (int i = 0; i < 4; i++) { poles[i, 0] = points[i].X; poles[i, 1] = points[i].Y; poles[i, 2] = points[i].Z; poles[i, 3] = 1; }
                    UFCurve.Spline data = new UFCurve.Spline { num_poles = 4, order = 4, is_rational = 0,
                        knots = new double[] { 0, 0, 0, 0, 1, 1, 1, 1 }, poles = poles, start_param = 0, end_param = 1 };
                    Tag tag; int stateCount; UFCurve.State[] states;
                    uf.Curve.CreateSpline(ref data, out tag, out stateCount, out states);
                    if (tag == Tag.Null || stateCount != 0) throw new InvalidOperationException("NX сообщил о проблеме при создании сплайна.");
                    created = NXObjectManager.Get(tag) as Curve;
                    if (created == null) throw new InvalidOperationException("NX не создал кривую.");
                    splines++;
                }
                created.SetName("GOST_TEXT");
                created.SetUserAttribute("GOST_TEXT_SOURCE", -1, inscription, Update.Option.Now);
            }
            editing = false;
            UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Information,
                "Надпись создана.\nЛиний: " + lines + "\nГладких сплайнов: " + splines
                + "\n\nВыберите кривые рамкой в скрипте «Кривые для PLANAR_MILL»."
                + "\nОтмена: Ctrl+Z. Исходный файл шрифта не изменён.");
        }
        catch (Exception ex)
        {
            string result = "";
            if (editing)
            {
                try { session.UndoToMark(mark, null); session.DeleteUndoMark(mark, null); result = "\nСозданные этим запуском кривые удалены."; }
                catch (Exception undo) { result = "\nПроверьте частично созданные кривые: " + undo.Message; }
            }
            UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Error, ex.Message + result);
        }
    }

    private sealed class InputForm : Form
    {
        internal readonly TextBox FontPath = new TextBox();
        internal readonly TextBox Inscription = new TextBox();
        internal readonly NumericUpDown HeightValue = new NumericUpDown();
        internal readonly NumericUpDown AngleValue = new NumericUpDown();
        internal InputForm()
        {
            Text = Title; StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            Font = new System.Drawing.Font("Segoe UI", 10); ClientSize = new Size(730, 310);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 3, RowCount = 6 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            FontPath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "GOST_A.FON");
            FontPath.Dock = DockStyle.Fill;
            Button browse = new Button { Text = "Обзор…", Dock = DockStyle.Fill };
            browse.Click += delegate { using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Векторный GOST A (*.fon)|*.fon", CheckFileExists = true })
                { if (dialog.ShowDialog(this) == DialogResult.OK) FontPath.Text = dialog.FileName; } };
            Inscription.Text = "25_12_АБВ"; Inscription.Dock = DockStyle.Fill; Inscription.MaxLength = 250;
            HeightValue.DecimalPlaces = 3; HeightValue.Minimum = .1m; HeightValue.Maximum = 1000m; HeightValue.Value = 5;
            AngleValue.DecimalPlaces = 2; AngleValue.Minimum = -360; AngleValue.Maximum = 360;
            table.Controls.Add(new Label { Text = "Шрифт FON", AutoSize = true }, 0, 0); table.Controls.Add(FontPath, 1, 0); table.Controls.Add(browse, 2, 0);
            table.Controls.Add(new Label { Text = "Надпись", AutoSize = true }, 0, 1); table.Controls.Add(Inscription, 1, 1); table.SetColumnSpan(Inscription, 2);
            table.Controls.Add(new Label { Text = "Высота, мм", AutoSize = true }, 0, 2); table.Controls.Add(HeightValue, 1, 2);
            table.Controls.Add(new Label { Text = "Поворот строки, °", AutoSize = true }, 0, 3); table.Controls.Add(AngleValue, 1, 3);
            Label hint = new Label { Text = "Текст размещается на XY текущей WCS. После нажатия «Создать» укажите начало надписи. Наклон букв 75° уже учтён. Нужен векторный GOST_A.FON, а не TTF.", Dock = DockStyle.Fill };
            table.Controls.Add(hint, 0, 4); table.SetColumnSpan(hint, 3);
            FlowLayoutPanel buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            Button cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };
            Button ok = new Button { Text = "Создать", DialogResult = DialogResult.OK, AutoSize = true };
            buttons.Controls.Add(cancel); buttons.Controls.Add(ok); table.Controls.Add(buttons, 0, 5); table.SetColumnSpan(buttons, 3);
            Controls.Add(table); AcceptButton = ok; CancelButton = cancel;
        }
    }
    public static int GetUnloadOption(string unused) { return (int)Session.LibraryUnloadOption.Immediately; }
}

// BEGIN PORTABLE GOST GEOMETRY
public static class GostStrokeGeometry
{
    public struct P
    {
        public double X, Y;
        public P(double x, double y) { X = x; Y = y; }
        public static P operator +(P a, P b) { return new P(a.X + b.X, a.Y + b.Y); }
        public static P operator -(P a, P b) { return new P(a.X - b.X, a.Y - b.Y); }
        public static P operator *(P a, double b) { return new P(a.X * b, a.Y * b); }
        public double Length { get { return Math.Sqrt(X * X + Y * Y); } }
        public P Unit() { double n = Length; return n == 0 ? new P() : this * (1 / n); }
        public static double Dot(P a, P b) { return a.X * b.X + a.Y * b.Y; }
    }
    public sealed class Stroke { public P[] P; public Stroke(params P[] p) { P = p; } }
    public sealed class Glyph { public double Width; public List<List<P>> Paths = new List<List<P>>(); }
    public sealed class FontData { public Dictionary<char, Glyph> Glyphs = new Dictionary<char, Glyph>(); public double CapHeight, Baseline, SourceShear; }
    private static bool Same(P a, P b) { return (a - b).Length < 1e-9; }
    private static bool Straight(P a, P b, double cap)
    { P d = b - a; return Math.Abs(d.X) < 1e-9 || Math.Abs(d.Y) < 1e-9 || d.Length >= cap * .35; }

    public static List<Stroke> Smooth(List<P> input, double cap)
    {
        List<P> p = new List<P>(); foreach (P a in input) if (p.Count == 0 || !Same(a, p[p.Count - 1])) p.Add(a);
        List<Stroke> result = new List<Stroke>(); if (p.Count < 2) return result;
        bool closed = Same(p[0], p[p.Count - 1]); if (closed) p.RemoveAt(p.Count - 1);
        int n = p.Count; if (n < 2) return result;
        P[] tangents = new P[n]; bool[] corners = new bool[n]; bool[] straight = new bool[n];
        int edges = closed ? n : n - 1;
        for (int i = 0; i < edges; i++) straight[i] = Straight(p[i], p[(i + 1) % n], cap);
        for (int i = 0; i < n; i++)
        {
            if (!closed && i == 0) { tangents[i] = (p[1] - p[0]).Unit(); continue; }
            if (!closed && i == n - 1) { tangents[i] = (p[i] - p[i - 1]).Unit(); continue; }
            int previous = (i + n - 1) % n;
            P incoming = (p[i] - p[previous]).Unit(), outgoing = (p[(i + 1) % n] - p[i]).Unit();
            corners[i] = P.Dot(incoming, outgoing) < Math.Cos(64 * Math.PI / 180);
            if (straight[previous] && straight[i] && P.Dot(incoming, outgoing) < 1 - 1e-9) corners[i] = true;
            tangents[i] = straight[previous] ? incoming : straight[i] ? outgoing : (incoming + outgoing).Unit();
        }
        for (int i = 0; i < edges; i++)
        {
            int j = (i + 1) % n; P d = p[j] - p[i], u = d.Unit();
            P a = corners[i] ? u : tangents[i], b = corners[j] ? u : tangents[j];
            if (straight[i] || (corners[i] && corners[j]) || (P.Dot(a, u) > .999999999 && P.Dot(b, u) > .999999999))
                result.Add(new Stroke(p[i], p[j]));
            else result.Add(new Stroke(p[i], p[i] + a * (d.Length / 3), p[j] - b * (d.Length / 3), p[j]));
        }
        return result;
    }

    private static int U16(byte[] b, int i) { Check(b, i, 2); return b[i] | b[i + 1] << 8; }
    private static int U32(byte[] b, int i)
    { Check(b, i, 4); uint n = (uint)(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24); if (n > int.MaxValue) throw BadFont(); return (int)n; }
    private static void Check(byte[] b, int i, int count) { if (i < 0 || count < 0 || i > b.Length - count) throw BadFont(); }
    private static Exception BadFont() { return new InvalidOperationException("Не удалось прочитать векторный GOST_A.FON. Выберите исходный GOST type A (plotter) или GOST A SingleLine 75 V1."); }
    private static string CString(byte[] b, int i)
    { Check(b, i, 1); int end = i; while (end < b.Length && b[end] != 0 && end - i < 128) end++; return Encoding.ASCII.GetString(b, i, end - i); }
    public static FontData ReadFont(byte[] b)
    {
        if (b == null || b.Length < 128 || b.Length > 1048576 || b[0] != 77 || b[1] != 90) throw BadFont();
        int ne = U32(b, 60); if (U16(b, ne) != 0x454e) throw BadFont();
        int resource = checked(ne + U16(b, ne + 36)); int shift = U16(b, resource); if (shift > 15) throw BadFont(); resource += 2;
        int fontOffset = -1, fontLength = 0;
        while (U16(b, resource) != 0)
        {
            int type = U16(b, resource), count = U16(b, resource + 2); resource += 8; Check(b, resource, checked(count * 12));
            if (type == 0x8008 && count > 0) { fontOffset = U16(b, resource) << shift; fontLength = U16(b, resource + 2) << shift; break; }
            resource += count * 12;
        }
        Check(b, fontOffset, fontLength); byte[] f = new byte[fontLength]; Array.Copy(b, fontOffset, f, 0, f.Length);
        if (U16(f, 0) != 0x200 || U16(f, 66) != 1 || U16(f, 88) > 128 || U16(f, 93) > 128 || U16(f, 86) != 0) throw BadFont();
        string face = CString(f, U32(f, 105)); if (face.IndexOf("GOST", StringComparison.OrdinalIgnoreCase) < 0) throw BadFont();
        int first = f[95], last = f[96], bits = U32(f, 113); if (first > 32 || last < 255) throw BadFont();
        FontData font = new FontData(); Encoding cp = Encoding.GetEncoding(1251);
        for (int code = first; code <= last; code++)
        {
            int at = 117 + 4 * (code - first), start = checked(bits + U16(f, at)), end = checked(bits + U16(f, at + 4));
            Check(f, start, end - start); Glyph glyph = new Glyph { Width = U16(f, at + 2) }; int x = 0, y = 0; List<P> path = null;
            while (start < end)
            {
                bool move = f[start] == 128; if (move) start++;
                if (start > end - 2) throw BadFont();
                x += unchecked((sbyte)f[start++]); y += unchecked((sbyte)f[start++]);
                if (move || path == null) { path = new List<P>(); glyph.Paths.Add(path); } path.Add(new P(x, y));
            }
            char ch = cp.GetString(new byte[] { (byte)code })[0]; font.Glyphs[ch] = glyph;
        }
        Glyph h; if (!font.Glyphs.TryGetValue('H', out h) || h.Paths.Count != 3 || h.Paths[0].Count != 2) throw BadFont();
        P h0 = h.Paths[0][0], h1 = h.Paths[0][1]; font.CapHeight = Math.Abs(h0.Y - h1.Y); font.Baseline = Math.Max(h0.Y, h1.Y);
        if (font.CapHeight < 10 || font.CapHeight > 128) throw BadFont();
        font.SourceShear = (h1.X - h0.X) / (h0.Y - h1.Y);
        if (Math.Abs(font.SourceShear) > .5) throw BadFont();
        return font;
    }

    private static string Key(P a, P b)
    {
        string x = a.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + a.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string y = b.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + b.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return string.CompareOrdinal(x, y) < 0 ? x + ";" + y : y + ";" + x;
    }
    public static List<Stroke> Text(FontData font, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Введите надпись.");
        List<Stroke> result = new List<Stroke>(); double cursor = 0; double shear = Math.Tan(Math.PI / 12);
        foreach (char ch in text)
        {
            Glyph glyph; if (!font.Glyphs.TryGetValue(ch, out glyph) || (glyph.Paths.Count == 0 && ch != ' ' && ch != '\u00a0'))
                throw new InvalidOperationException("В выбранном шрифте нет символа: " + ch);
            HashSet<string> seen = new HashSet<string>(); List<List<P>> paths = new List<List<P>>();
            foreach (List<P> source in glyph.Paths)
            {
                List<P> path = new List<P>();
                for (int i = 0; i < source.Count; i++)
                {
                    P p = source[i]; P normalized = new P(p.X - (font.Baseline - p.Y) * font.SourceShear, p.Y);
                    if (path.Count == 0) { path.Add(normalized); continue; }
                    P previous = source[i - 1];
                    if (Same(previous, p)) continue;
                    if (!seen.Add(Key(previous, p))) { if (path.Count > 1) paths.Add(path); path = new List<P> { normalized }; }
                    else path.Add(normalized);
                }
                if (path.Count > 1) paths.Add(path);
            }
            double maxX = 0;
            foreach (List<P> path in paths) foreach (Stroke stroke in Smooth(path,
                ch == '(' || ch == ')' ? font.CapHeight * 1000 : font.CapHeight))
            {
                P[] control = new P[stroke.P.Length];
                for (int i = 0; i < control.Length; i++)
                {
                    double y = (font.Baseline - stroke.P[i].Y) / font.CapHeight;
                    double x = stroke.P[i].X / font.CapHeight + y * shear;
                    control[i] = new P(cursor + x, y); maxX = Math.Max(maxX, x);
                }
                result.Add(new Stroke(control));
            }
            cursor += Math.Max(glyph.Width / font.CapHeight, maxX + .08);
        }
        return result;
    }
}
// END PORTABLE GOST GEOMETRY
