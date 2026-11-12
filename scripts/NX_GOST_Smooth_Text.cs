// NX_GOST_Smooth_Text.cs - smooth single-stroke engraving text.
// SCRIPT_VERSION: V1.01
// Reads an external vector GOST_A.FON; no font data is embedded or installed.
// Creates lines and cubic splines in the current WCS XY plane; one undo step.

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using NXOpen;
using NXOpen.UF;
using NXOpen.Utilities;

public class NXGostSmoothText
{
    private const string SCRIPT_VERSION = "V1.01";
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
            using (GostTextInputForm form = new GostTextInputForm(Title))
            {
                if (!form.ShowDialog()) return;
                fontPath = form.FontPath; inscription = form.Inscription;
                height = form.HeightValue;
                angle = form.AngleValue * Math.PI / 180.0;
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
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            string result = "";
            if (editing)
            {
                try { session.UndoToMark(mark, null); session.DeleteUndoMark(mark, null); result = "\nСозданные этим запуском кривые удалены."; }
                catch (Exception undo) { result = "\nПроверьте частично созданные кривые: " + undo.Message; }
            }
            UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Error, ex.Message + result);
        }
    }

    public static int GetUnloadOption(string unused) { return (int)Session.LibraryUnloadOption.Immediately; }
}

// BEGIN GOST INPUT DIALOG
// Only object/reflection boundaries cross into Forms and Drawing. NX's journal
// compiler need not reference their .NET 10 implementation assemblies.
internal sealed class GostTextInputForm : IDisposable
{
    private object form, ownedFont;
    private object fontPath, inscription, heightValue, angleValue;
    internal string FontPath { get { return (string)GostRuntimeForms.Get(fontPath, "Text"); } }
    internal string Inscription { get { return (string)GostRuntimeForms.Get(inscription, "Text"); } }
    internal double HeightValue { get { return Convert.ToDouble(GostRuntimeForms.Get(heightValue, "Value")); } }
    internal double AngleValue { get { return Convert.ToDouble(GostRuntimeForms.Get(angleValue, "Value")); } }

    internal GostTextInputForm(string title)
    {
        try
        {
            form = GostRuntimeForms.New("Form");
            GostRuntimeForms.Set(form, "Text", title);
            GostRuntimeForms.SetEnum(form, "StartPosition", "CenterScreen");
            GostRuntimeForms.SetEnum(form, "AutoScaleMode", "Dpi");
            ownedFont = GostRuntimeForms.SetValue(form, "Font", "Segoe UI", 10f);
            GostRuntimeForms.SetValue(form, "ClientSize", 730, 310);
            GostRuntimeForms.SetEnum(form, "FormBorderStyle", "FixedDialog");
            GostRuntimeForms.Set(form, "MaximizeBox", false); GostRuntimeForms.Set(form, "MinimizeBox", false);
            object table = GostRuntimeForms.New("TableLayoutPanel");
            GostRuntimeForms.SetEnum(table, "Dock", "Fill"); GostRuntimeForms.SetValue(table, "Padding", 14);
            GostRuntimeForms.Set(table, "ColumnCount", 3); GostRuntimeForms.Set(table, "RowCount", 6);
            AddStyle(table, "ColumnStyles", "ColumnStyle", "Absolute", 130);
            AddStyle(table, "ColumnStyles", "ColumnStyle", "Percent", 100);
            AddStyle(table, "ColumnStyles", "ColumnStyle", "Absolute", 85);
            for (int i = 0; i < 4; i++) AddStyle(table, "RowStyles", "RowStyle", "Absolute", 42);
            AddStyle(table, "RowStyles", "RowStyle", "Percent", 100);
            AddStyle(table, "RowStyles", "RowStyle", "Absolute", 36);

            fontPath = GostRuntimeForms.New("TextBox");
            GostRuntimeForms.Set(fontPath, "Name", "fontPath");
            GostRuntimeForms.Set(fontPath, "Text", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "GOST_A.FON"));
            GostRuntimeForms.SetEnum(fontPath, "Dock", "Fill");
            object browse = Button("Обзор…", "browse"); GostRuntimeForms.SetEnum(browse, "Dock", "Fill");
            GostRuntimeForms.On(browse, "Click", delegate
            {
                object dialog = GostRuntimeForms.New("OpenFileDialog");
                try
                {
                    GostRuntimeForms.Set(dialog, "Filter", "Векторный GOST A (*.fon)|*.fon");
                    GostRuntimeForms.Set(dialog, "CheckFileExists", true);
                    if (GostRuntimeForms.Call(dialog, "ShowDialog", form).ToString() == "OK")
                        GostRuntimeForms.Set(fontPath, "Text", GostRuntimeForms.Get(dialog, "FileName"));
                }
                finally { GostRuntimeForms.Dispose(dialog); }
            });
            inscription = GostRuntimeForms.New("TextBox");
            GostRuntimeForms.Set(inscription, "Name", "inscription");
            GostRuntimeForms.Set(inscription, "Text", "25_12_АБВ");
            GostRuntimeForms.Set(inscription, "MaxLength", 250); GostRuntimeForms.SetEnum(inscription, "Dock", "Fill");
            heightValue = GostRuntimeForms.New("NumericUpDown");
            GostRuntimeForms.Set(heightValue, "Name", "heightValue");
            GostRuntimeForms.Set(heightValue, "DecimalPlaces", 3); GostRuntimeForms.Set(heightValue, "Minimum", .1m);
            GostRuntimeForms.Set(heightValue, "Maximum", 1000m); GostRuntimeForms.Set(heightValue, "Value", 5m);
            angleValue = GostRuntimeForms.New("NumericUpDown");
            GostRuntimeForms.Set(angleValue, "Name", "angleValue");
            GostRuntimeForms.Set(angleValue, "DecimalPlaces", 2); GostRuntimeForms.Set(angleValue, "Minimum", -360m);
            GostRuntimeForms.Set(angleValue, "Maximum", 360m);
            Add(table, Label("Шрифт FON"), 0, 0); Add(table, fontPath, 1, 0); Add(table, browse, 2, 0);
            Add(table, Label("Надпись"), 0, 1); Add(table, inscription, 1, 1); GostRuntimeForms.Call(table, "SetColumnSpan", inscription, 2);
            Add(table, Label("Высота, мм"), 0, 2); Add(table, heightValue, 1, 2);
            Add(table, Label("Поворот строки, °"), 0, 3); Add(table, angleValue, 1, 3);
            object hint = Label("Текст размещается на XY текущей WCS. После нажатия «Создать» укажите начало надписи. Наклон букв 75° уже учтён. Нужен векторный GOST_A.FON, а не TTF.");
            GostRuntimeForms.Set(hint, "AutoSize", false); GostRuntimeForms.SetEnum(hint, "Dock", "Fill");
            Add(table, hint, 0, 4); GostRuntimeForms.Call(table, "SetColumnSpan", hint, 3);
            object buttons = GostRuntimeForms.New("FlowLayoutPanel");
            GostRuntimeForms.SetEnum(buttons, "FlowDirection", "RightToLeft"); GostRuntimeForms.SetEnum(buttons, "Dock", "Fill");
            object cancel = Button("Отмена", "cancel"); GostRuntimeForms.SetEnum(cancel, "DialogResult", "Cancel");
            object ok = Button("Создать", "create"); GostRuntimeForms.SetEnum(ok, "DialogResult", "OK");
            GostRuntimeForms.Call(GostRuntimeForms.Get(buttons, "Controls"), "Add", cancel);
            GostRuntimeForms.Call(GostRuntimeForms.Get(buttons, "Controls"), "Add", ok);
            Add(table, buttons, 0, 5); GostRuntimeForms.Call(table, "SetColumnSpan", buttons, 3);
            GostRuntimeForms.Call(GostRuntimeForms.Get(form, "Controls"), "Add", table);
            GostRuntimeForms.Set(form, "AcceptButton", ok); GostRuntimeForms.Set(form, "CancelButton", cancel);
        }
        catch { Dispose(); throw; }
    }
    private static object Label(string text)
    {
        object label = GostRuntimeForms.New("Label"); GostRuntimeForms.Set(label, "Text", text);
        GostRuntimeForms.Set(label, "AutoSize", true); return label;
    }
    private static object Button(string text, string name)
    {
        object button = GostRuntimeForms.New("Button"); GostRuntimeForms.Set(button, "Text", text);
        GostRuntimeForms.Set(button, "Name", name); GostRuntimeForms.Set(button, "AutoSize", true); return button;
    }
    private static void Add(object table, object control, int column, int row)
    { GostRuntimeForms.Call(GostRuntimeForms.Get(table, "Controls"), "Add", control, column, row); }
    private static void AddStyle(object table, string collection, string type, string sizeType, float value)
    {
        object style = GostRuntimeForms.New(type, Enum.Parse(GostRuntimeForms.FormType("SizeType"), sizeType), value);
        GostRuntimeForms.Call(GostRuntimeForms.Get(table, collection), "Add", style);
    }
    internal bool ShowDialog() { return GostRuntimeForms.Call(form, "ShowDialog").ToString() == "OK"; }
    public void Dispose()
    {
        GostRuntimeForms.Dispose(form); form = null;
        GostRuntimeForms.Dispose(ownedFont); ownedFont = null;
    }
}

internal static class GostRuntimeForms
{
    private static Assembly forms;
    internal static Type FormType(string name)
    {
        if (forms == null) forms = GostSharedFormsAssembly.Load();
        return forms.GetType("System.Windows.Forms." + name, true);
    }
    internal static object New(string name, params object[] values)
    { return Activator.CreateInstance(FormType(name), values); }
    private static PropertyInfo Property(object target, string name)
    {
        // TableLayoutPanel hides Controls with a more specific collection type.
        // Resolve the nearest declaration rather than an ambiguous inherited match.
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property != null) return property;
        }
        throw new MissingMemberException(target.GetType().FullName, name);
    }
    internal static object Get(object target, string name)
    { return Property(target, name).GetValue(target, null); }
    internal static void Set(object target, string name, object value)
    { Property(target, name).SetValue(target, value, null); }
    internal static void SetEnum(object target, string name, string value)
    {
        PropertyInfo property = Property(target, name);
        property.SetValue(target, Enum.Parse(property.PropertyType, value), null);
    }
    internal static object SetValue(object target, string name, params object[] values)
    {
        PropertyInfo property = Property(target, name);
        object value = Activator.CreateInstance(property.PropertyType, values);
        property.SetValue(target, value, null); return value;
    }
    internal static object Call(object target, string name, params object[] values)
    {
        return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
            null, target, values);
    }
    internal static void On(object target, string name, EventHandler handler)
    { target.GetType().GetEvent(name).AddEventHandler(target, handler); }
    internal static void Dispose(object target)
    { IDisposable disposable = target as IDisposable; if (disposable != null) disposable.Dispose(); }
}

internal static class GostNativeFormsOwner
{
    private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    internal static Assembly Find()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
        return Find(AppDomain.CurrentDomain.GetAssemblies(), RegisteredProcedure);
    }

    internal static Assembly Find(Assembly[] assemblies, Func<string, IntPtr> registeredProcedure)
    {
        Assembly owner = null;
        foreach (Assembly assembly in assemblies)
        {
            if (!String.Equals(assembly.GetName().Name, "System.Windows.Forms", StringComparison.Ordinal)) continue;
            if (!OwnsRegisteredClasses(assembly, registeredProcedure)) continue;
            if (owner != null && !Object.ReferenceEquals(owner, assembly))
                throw new InvalidOperationException("\u0412 \u044D\u0442\u043E\u043C \u0441\u0435\u0430\u043D\u0441\u0435 NX \u043E\u043A\u043E\u043D\u043D\u044B\u0435 \u043A\u043B\u0430\u0441\u0441\u044B \u043F\u0440\u0438\u043D\u0430\u0434\u043B\u0435\u0436\u0430\u0442 \u0440\u0430\u0437\u043D\u044B\u043C \u043A\u043E\u043F\u0438\u044F\u043C Windows Forms. \u041F\u043E\u043B\u043D\u043E\u0441\u0442\u044C\u044E \u0437\u0430\u043A\u0440\u043E\u0439\u0442\u0435 NX, \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u0435\u0433\u043E \u0437\u0430\u043D\u043E\u0432\u043E \u0438 \u0441\u043D\u0430\u0447\u0430\u043B\u0430 \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u044D\u0442\u043E\u0442 \u0441\u043A\u0440\u0438\u043F\u0442.");
            owner = assembly;
        }
        return owner;
    }

    private static FieldInfo Field(Type type, params string[] names)
    {
        foreach (string name in names)
        {
            FieldInfo field = type.GetField(name, Fields);
            if (field != null) return field;
        }
        return null;
    }

    private static bool OwnsRegisteredClasses(Assembly assembly, Func<string, IntPtr> registeredProcedure)
    {
        Type native = assembly.GetType("System.Windows.Forms.NativeWindow", false);
        Type wc = native == null ? null : native.GetNestedType("WindowClass", BindingFlags.NonPublic);
        if (wc == null) return false;
        // .NET 6/8+ and the older .NET Framework use different field spellings.
        FieldInfo cache = Field(wc, "s_cache", "cache");
        FieldInfo next = Field(wc, "_next", "next");
        FieldInfo name = Field(wc, "_windowClassName", "windowClassName");
        FieldInfo procedure = Field(wc, "_windProc", "windProc", "windowProc");
        if (cache == null || next == null || name == null || procedure == null) return false;
        object entry = cache.GetValue(null);
        HashSet<object> seen = new HashSet<object>();
        while (entry != null && seen.Add(entry))
        {
            string className = name.GetValue(entry) as string;
            Delegate callback = procedure.GetValue(entry) as Delegate;
            if (!String.IsNullOrEmpty(className) && callback != null)
            {
                IntPtr actual = registeredProcedure(className);
                // A cache entry alone is not evidence: the native registration
                // must still point to this exact assembly's managed callback.
                if (actual != IntPtr.Zero && actual == Marshal.GetFunctionPointerForDelegate(callback)) return true;
            }
            entry = next.GetValue(entry);
        }
        return false;
    }

    private static IntPtr RegisteredProcedure(string className)
    {
        WindowClassInfo info;
        return GetClassInfoW(GetModuleHandleW(null), className, out info) ? info.Procedure : IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClassInfo
    {
        internal uint Style;
        internal IntPtr Procedure;
        internal int ClassExtra, WindowExtra;
        internal IntPtr Instance, Icon, Cursor, Background, MenuName, ClassName;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClassInfoW(IntPtr instance, string className, out WindowClassInfo info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr GetModuleHandleW(string moduleName);
}


internal static class GostSharedFormsAssembly
{
    internal static Assembly Load()
    {
        Assembly registeredOwner = GostNativeFormsOwner.Find();
        if (registeredOwner != null)
        {
            // Keep the callback-owning assembly alive for the NX process. A
            // journal may unload, but its native class callbacks must not vanish.
            // Only an Assembly reference is stored; no journal UI or files.
            AppDomain.CurrentDomain.SetData("NX_GOST_Smooth_Text.FormsOwner", registeredOwner);
            return registeredOwner;
        }
        AssemblyName name = new AssemblyName("System.Windows.Forms");
        Type contextType = typeof(object).Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext", false);
        if (contextType == null)
        {
            // .NET Framework: one Forms instance per current AppDomain, as before.
            foreach (Assembly existing in AppDomain.CurrentDomain.GetAssemblies())
                if (String.Equals(existing.GetName().Name, name.Name, StringComparison.Ordinal)) return existing;
            return Assembly.Load("System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
        }

        object sharedContext = contextType.GetProperty("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
        MethodInfo getContext = contextType.GetMethod("GetLoadContext", BindingFlags.Public | BindingFlags.Static,
            null, new Type[] { typeof(Assembly) }, null);
        foreach (Assembly existing in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!String.Equals(existing.GetName().Name, name.Name, StringComparison.Ordinal)) continue;
            if (Object.ReferenceEquals(getContext.Invoke(null, new object[] { existing }), sharedContext)) return existing;
        }

        // Never use Assembly.Load here: from an NX journal it may bind in that
        // journal's private context. Never fall back to an arbitrary loaded copy.
        MethodInfo load = contextType.GetMethod("LoadFromAssemblyName", BindingFlags.Public | BindingFlags.Instance,
            null, new Type[] { typeof(AssemblyName) }, null);
        Assembly result;
        try { result = (Assembly)load.Invoke(sharedContext, new object[] { name }); }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0437\u0430\u0433\u0440\u0443\u0437\u0438\u0442\u044C \u043E\u0431\u0449\u0443\u044E \u043E\u043A\u043E\u043D\u043D\u0443\u044E \u0431\u0438\u0431\u043B\u0438\u043E\u0442\u0435\u043A\u0443 NX. \u041F\u043E\u043B\u043D\u043E\u0441\u0442\u044C\u044E \u0437\u0430\u043A\u0440\u043E\u0439\u0442\u0435 NX \u0438 \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u0435\u0433\u043E \u0437\u0430\u043D\u043E\u0432\u043E.", ex.InnerException ?? ex);
        }
        if (!Object.ReferenceEquals(getContext.Invoke(null, new object[] { result }), sharedContext))
            throw new InvalidOperationException("NX \u0432\u0435\u0440\u043D\u0443\u043B \u043E\u0442\u0434\u0435\u043B\u044C\u043D\u0443\u044E \u043A\u043E\u043F\u0438\u044E \u043E\u043A\u043E\u043D\u043D\u043E\u0439 \u0431\u0438\u0431\u043B\u0438\u043E\u0442\u0435\u043A\u0438 \u0432\u043C\u0435\u0441\u0442\u043E \u043E\u0431\u0449\u0435\u0439. \u041F\u043E\u043B\u043D\u043E\u0441\u0442\u044C\u044E \u0437\u0430\u043A\u0440\u043E\u0439\u0442\u0435 NX \u0438 \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u0435\u0433\u043E \u0437\u0430\u043D\u043E\u0432\u043E.");
        return result;
    }
}


// END GOST INPUT DIALOG

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
    private static Encoding CyrillicEncoding()
    {
        try { return Encoding.GetEncoding(1251); }
        catch (NotSupportedException)
        {
            // Modern .NET keeps legacy encodings in its runtime provider.
            // Ask it directly without changing NX's process-wide registration.
            Type type = Type.GetType("System.Text.CodePagesEncodingProvider, System.Text.Encoding.CodePages", true);
            object provider = type.GetProperty("Instance").GetValue(null, null);
            return (Encoding)type.GetMethod("GetEncoding", new Type[] { typeof(int) }).Invoke(provider, new object[] { 1251 });
        }
    }
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
        FontData font = new FontData(); Encoding cp = CyrillicEncoding();
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
