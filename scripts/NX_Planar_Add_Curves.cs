// NX_Planar_Add_Curves.cs - separate curve boundaries for PLANAR_MILL / PLANAR_PROFILE.
// SCRIPT_VERSION: V1
// Select one planar operation, choose ON / TANTO, then select existing work-part curves.
// Each new curve becomes one Part Boundary; geometrically closed curves remain Closed.
// Existing assignments are skipped. No chaining, new curves, files or automatic toolpath generation.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using NXOpen.Utilities;
using CamOperation = NXOpen.CAM.Operation;

public class NXPlanarAddCurves
{
    private const string SCRIPT_VERSION = "V1";
    private static readonly string Title = "Кривые в PLANAR — " + SCRIPT_VERSION + " — "
        + Encoding.UTF8.GetString(Convert.FromBase64String("YnkgQFRvbnlfRm94eHggKFRHKQ=="));

    private sealed class CurveData
    {
        internal Curve Curve;
        internal Point3d Start;
        internal bool Closed;
    }

    public static void Main(string[] args)
    {
        Session session = Session.GetSession();
        UFSession uf = UFSession.GetUFSession();
        Part part = session.Parts.Work;
        PlanarMilling operation = null;
        Session.UndoMarkId mark = default(Session.UndoMarkId);
        bool pendingUndo = false, success = false;
        string stage = "Проверка выбранной операции", headline = "Не удалось добавить кривые";
        List<string> original = null;
        StringBuilder report = new StringBuilder(Title + Environment.NewLine);
        try
        {
            if (part == null || part.CAMSetup == null)
                throw new InvalidOperationException("Откройте CAM-проект в Manufacturing.");
            operation = SelectedOperation(uf, part);
            report.AppendLine("Операция: " + operation.Name + " (Tag " + operation.Tag + ")");
            report.AppendLine("Класс NXOpen: " + operation.GetType().FullName);
            EnsureLocalBoundaries(uf, operation);

            PlanarCurveOptions options = PlanarCurveWindows.AskOptions(Title, operation.Name);
            if (options == null) return;
            report.AppendLine("Режим: " + options.ModeLabel);
            if (options.Tanto) report.AppendLine("Сторона: " + options.SideLabel);

            stage = "Выбор кривых";
            List<Curve> selected = SelectCurves();
            if (selected == null) return;
            report.AppendLine("Выбрано уникальных кривых: " + selected.Count);
            if (session.Parts.Work == null || session.Parts.Work.Tag != part.Tag)
                throw new InvalidOperationException("Рабочая деталь изменилась. Запустите скрипт заново.");
            EnsureLocalBoundaries(uf, operation);

            stage = "Чтение существующих границ";
            HashSet<Tag> assigned = new HashSet<Tag>();
            original = ReadState(part, operation, assigned);
            List<CurveData> pending = new List<CurveData>();
            List<string> skipped = new List<string>();
            stage = "Проверка выбранных кривых";
            foreach (Curve curve in selected)
            {
                string label = CurveLabel(curve);
                if (assigned.Contains(curve.Tag)) { skipped.Add(label); continue; }
                if (curve.IsOccurrence || curve.OwningPart == null || curve.OwningPart.Tag != part.Tag)
                    throw new InvalidOperationException(label + ": выберите кривую рабочей CAM-детали. Кривые компонентов в V1 не поддерживаются.");
                if (!(curve is Line) && !(curve is Arc) && !(curve is Spline) && !(curve is Ellipse))
                    throw new InvalidOperationException(label + ": поддерживаются линии, дуги, окружности, эллипсы и сплайны.");
                pending.Add(InspectCurve(uf, curve));
            }
            report.AppendLine("Существующих границ: " + original.Count);
            report.AppendLine("Уже назначенных кривых пропущено: " + skipped.Count);
            if (skipped.Count > 0)
            {
                report.AppendLine("Их границы и положение инструмента не изменялись:");
                foreach (string item in skipped) report.AppendLine("  " + item);
            }
            if (pending.Count == 0)
            {
                report.AppendLine("Добавлять нечего: все выбранные кривые уже есть в Part Boundaries.");
                PlanarCurveWindows.ShowReport(Title, "Кривые уже назначены", report.ToString(), 1);
                return;
            }

            Matrix3x3 viewMatrix = part.ModelingViews.WorkView.Matrix;
            mark = session.SetUndoMark(Session.MarkVisibility.Visible, "Кривые в PLANAR: " + options.ModeLabel);
            pendingUndo = true;
            PlanarMillingBuilder builder = part.CAMSetup.CAMOperationCollection.CreatePlanarMillingBuilder(operation);
            Exception buildFailure = null;
            try
            {
                if (!SameState(original, SeparatePlanarBoundary.Snapshot(builder.PartBoundary, null)))
                    throw new InvalidOperationException("Границы операции изменились во время выбора. Повторите запуск.");
                int number = 0;
                foreach (CurveData data in pending)
                {
                    stage = "Добавление кривой " + (++number) + "/" + pending.Count + ": " + CurveLabel(data.Curve);
                    uf.Ui.SetStatus(stage);
                    SeparatePlanarBoundary.Append(builder.PartBoundary, data.Curve, data.Start,
                        viewMatrix, data.Closed, options.Tanto, options.Outside);
                }
                stage = "Применение границ к операции";
                builder.Commit();
            }
            catch (Exception ex) { buildFailure = ex; throw; }
            finally
            {
                try { builder.Destroy(); }
                catch (Exception cleanup)
                {
                    report.AppendLine("Ошибка закрытия редактора: " + cleanup);
                    if (buildFailure == null) throw;
                }
            }

            stage = "Проверка границ после Commit";
            VerifyResult(part, operation, original, pending, options);
            pendingUndo = false;
            success = true;
            int closed = pending.FindAll(delegate(CurveData c) { return c.Closed; }).Count;
            headline = "Добавлено границ: " + pending.Count;
            report.AppendLine();
            report.AppendLine("Результат: успешно.");
            report.AppendLine("Добавлено отдельных границ: " + pending.Count);
            report.AppendLine("Open: " + (pending.Count - closed) + "; Closed: " + closed + ".");
            report.AppendLine("Проверено после Commit: одна кривая в каждой новой границе; тип Open/Closed; " + options.ModeLabel
                + (options.Tanto ? "; " + options.SideLabel : "") + ".");
            report.AppendLine("Проверены состав и считанные параметры прежних границ.");
            report.AppendLine("Припуск и допуск новых границ наследуются из операции.");
            report.AppendLine("Траектория не рассчитывалась. Проверьте её средствами NX.");
            report.AppendLine("Проект не сохранялся. Отмена этого запуска: Ctrl+Z.");
            try { uf.UiOnt.Refresh(); }
            catch (Exception refresh) { report.AppendLine("Навигатор не обновлён: " + refresh.Message); }
        }
        catch (Exception ex)
        {
            report.AppendLine();
            report.AppendLine("Результат: ошибка.");
            report.AppendLine("Этап: " + stage);
            NXException nxError = ex as NXException;
            if (nxError != null) report.AppendLine("Код NX: " + nxError.ErrorCode);
            report.AppendLine(ex.ToString());
            if (pendingUndo)
            {
                try
                {
                    session.UndoToMark(mark, null);
                    report.AppendLine("UndoToMark выполнен.");
                    if (original == null || !SameState(original, ReadState(part, operation, null)))
                        throw new InvalidOperationException("Считанные границы после отката не совпали с исходными.");
                    report.AppendLine("Откат проверен: исходный состав и считанные параметры границ восстановлены.");
                    session.DeleteUndoMark(mark, null);
                }
                catch (Exception rollback)
                {
                    report.AppendLine("Не удалось подтвердить полный откат. Проверьте операцию перед сохранением.");
                    report.AppendLine(rollback.ToString());
                }
            }
            else report.AppendLine(success ? "Границы уже добавлены и проверены. Отмена: Ctrl+Z." : "Изменение границ не начиналось.");
            if (success) { success = false; headline = "Границы добавлены; ошибка завершения"; }
        }
        finally { try { uf.Ui.SetStatus(""); } catch { } }
        try { PlanarCurveWindows.ShowReport(Title, headline, report.ToString(), success ? 0 : 2); }
        catch (Exception dialogError)
        {
            session.ListingWindow.Open();
            session.ListingWindow.WriteLine(report.ToString());
            session.ListingWindow.WriteLine("Ошибка окна отчёта: " + dialogError);
        }
    }

    private static PlanarMilling SelectedOperation(UFSession uf, Part part)
    {
        int count; Tag[] tags;
        uf.UiOnt.AskSelectedNodes(out count, out tags);
        List<CamOperation> operations = new List<CamOperation>();
        HashSet<Tag> seen = new HashSet<Tag>();
        if (tags != null) foreach (Tag tag in tags)
        {
            CamOperation item = NXObjectManager.Get(tag) as CamOperation;
            if (item != null && seen.Add(tag)) operations.Add(item);
        }
        // Classic PLANAR_MILL and PLANAR_PROFILE both use the PlanarMilling NXOpen class.
        // Deliberately do not rely on the operation name: users routinely rename operations.
        if (operations.Count != 1 || !(operations[0] is PlanarMilling))
            throw new InvalidOperationException("Закройте окно операции. Выделите одну PLANAR_MILL или PLANAR_PROFILE в Operation Navigator и запустите скрипт.");
        if (operations[0].OwningPart == null || operations[0].OwningPart.Tag != part.Tag)
            throw new InvalidOperationException("Выбранная операция должна принадлежать рабочей детали.");
        return (PlanarMilling)operations[0];
    }

    private static List<Curve> SelectCurves()
    {
        TaggedObject[] objects;
        Selection.MaskTriple[] mask = new Selection.MaskTriple[] {
            new Selection.MaskTriple(UFConstants.UF_line_type, 0, 0),
            new Selection.MaskTriple(UFConstants.UF_circle_type, 0, 0),
            new Selection.MaskTriple(UFConstants.UF_conic_type, 0, 0),
            new Selection.MaskTriple(UFConstants.UF_spline_type, 0, 0) };
        Selection.Response answer = UI.GetUI().SelectionManager.SelectTaggedObjects(
            "Выберите кривые: каждая будет добавлена отдельной границей", Title,
            Selection.SelectionScope.WorkPart, Selection.SelectionAction.ClearAndEnableSpecific,
            false, false, mask, out objects);
        if (answer == Selection.Response.Cancel || answer == Selection.Response.Back || objects == null || objects.Length == 0)
            return null;
        List<Curve> result = new List<Curve>(); HashSet<Tag> unique = new HashSet<Tag>();
        foreach (TaggedObject item in objects)
        {
            Curve curve = item as Curve;
            if (curve == null) throw new InvalidOperationException("Выберите кривые, а не грани, тела или рёбра.");
            if (unique.Add(curve.Tag)) result.Add(curve);
        }
        return result;
    }

    private static CurveData InspectCurve(UFSession uf, Curve curve)
    {
        double length = curve.GetLength();
        if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0)
            throw new InvalidOperationException(CurveLabel(curve) + ": некорректная длина.");
        int closed = uf.Modl.AskCurveClosed(curve.Tag);
        if (closed != 0 && closed != 1) throw new InvalidOperationException("NX не определил замкнутость " + CurveLabel(curve));
        IntPtr evaluator = IntPtr.Zero;
        try
        {
            uf.Eval.Initialize2(curve.Tag, out evaluator);
            double[] limits = new double[2], point = new double[3];
            uf.Eval.AskLimits(evaluator, limits);
            uf.Eval.Evaluate(evaluator, 0, limits[0], point, new double[3]);
            foreach (double value in point) if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException(CurveLabel(curve) + ": некорректные координаты.");
            return new CurveData { Curve = curve, Closed = closed == 1, Start = new Point3d(point[0], point[1], point[2]) };
        }
        finally { if (evaluator != IntPtr.Zero) uf.Eval.Free(evaluator); }
    }

    private static string CurveLabel(Curve curve)
    {
        return (string.IsNullOrEmpty(curve.Name) ? curve.GetType().Name : curve.Name) + " [" + curve.Tag + "]";
    }

    private static void EnsureLocalBoundaries(UFSession uf, PlanarMilling operation)
    {
        bool inherited; uf.Cambnd.IsInherited(operation.Tag, CamGeomType.CamPart, out inherited);
        if (!inherited) return;
        if (BoundaryCount(uf, operation.Tag) > 0)
            throw new InvalidOperationException("Операция наследует Part Boundaries. Сначала задайте для неё локальные границы в NX.");
        NCGroup parent = operation.GetParent(CAMSetup.View.Geometry);
        HashSet<Tag> seen = new HashSet<Tag>();
        while (parent != null && seen.Add(parent.Tag))
        {
            if (BoundaryCount(uf, parent.Tag) > 0)
                throw new InvalidOperationException("У родительской геометрии есть наследуемые Part Boundaries. Сначала задайте для операции локальные границы.");
            parent = parent.GetParent();
        }
    }

    private static int BoundaryCount(UFSession uf, Tag owner)
    {
        int count; IntPtr[] items;
        uf.Cambnd.AskBoundaries(owner, CamGeomType.CamPart, out count, out items);
        if (count < 0 || (count > 0 && (items == null || items.Length != count)))
            throw new InvalidOperationException("Не удалось прочитать количество границ NX.");
        return count;
    }

    private static List<string> ReadState(Part part, PlanarMilling operation, HashSet<Tag> assigned)
    {
        PlanarMillingBuilder reader = part.CAMSetup.CAMOperationCollection.CreatePlanarMillingBuilder(operation);
        try { return SeparatePlanarBoundary.Snapshot(reader.PartBoundary, assigned); }
        finally { reader.Destroy(); }
    }

    private static bool SameState(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        List<string> left = new List<string>(a), right = new List<string>(b);
        left.Sort(StringComparer.Ordinal); right.Sort(StringComparer.Ordinal);
        for (int i = 0; i < left.Count; i++) if (left[i] != right[i]) return false;
        return true;
    }

    private static void VerifyResult(Part part, PlanarMilling operation, List<string> original,
        List<CurveData> pending, PlanarCurveOptions options)
    {
        PlanarMillingBuilder reader = part.CAMSetup.CAMOperationCollection.CreatePlanarMillingBuilder(operation);
        try
        {
            BoundarySet[] sets = reader.PartBoundary.BoundaryList.GetContents();
            if (sets.Length != original.Count + pending.Count)
                throw new InvalidOperationException("NX изменил ожидаемое количество границ.");
            List<string> remaining = new List<string>(original);
            Dictionary<Tag, CurveData> added = new Dictionary<Tag, CurveData>();
            foreach (CurveData data in pending) added.Add(data.Curve.Tag, data);
            foreach (BoundarySet item in sets)
            {
                string state = SeparatePlanarBoundary.State(item, null);
                int oldIndex = remaining.IndexOf(state);
                if (oldIndex >= 0) { remaining.RemoveAt(oldIndex); continue; }
                BoundarySetPlanarMill set = item as BoundarySetPlanarMill;
                if (set == null) throw new InvalidOperationException("NX вернул неизвестный тип границы.");
                BoundaryMemberSet[] members = set.BoundaryMemberList.GetContents();
                CurveData data;
                if (members.Length != 1 || members[0].Geometry == null || !added.TryGetValue(members[0].Geometry.Tag, out data))
                    throw new InvalidOperationException("В новой границе должна быть ровно одна выбранная кривая.");
                SeparatePlanarBoundary.Verify(set, data.Curve.Tag, data.Closed, options.Tanto, options.Outside);
                added.Remove(data.Curve.Tag);
            }
            if (remaining.Count > 0 || added.Count > 0)
                throw new InvalidOperationException("NX изменил прежние границы или не сохранил новые.");
        }
        finally { reader.Destroy(); }
    }

    public static int GetUnloadOption(string unused) { return (int)Session.LibraryUnloadOption.Immediately; }
}

// BEGIN SEPARATE PLANAR BOUNDARY ADAPTER
public static class SeparatePlanarBoundary
{
    public static BoundarySetPlanarMill Append(BoundaryPlanarMill target, NXObject curve, Point3d start,
        Matrix3x3 view, bool closed, bool tanto, bool outside)
    {
        BoundarySetPlanarMill set = target.CreateBoundarySetPlanarMill();
        set.PlaneType = BoundarySet.PlaneTypes.Automatic;
        set.BoundaryType = closed ? BoundarySet.BoundaryTypes.Closed : BoundarySet.BoundaryTypes.Open;
        set.ToolPosition = tanto ? BoundarySet.ToolPositionTypes.Contact : BoundarySet.ToolPositionTypes.On;
        set.ToolSide = outside ? BoundarySet.ToolSideTypes.OutsideOrRight : BoundarySet.ToolSideTypes.InsideOrLeft;
        BoundaryPlanarMill.ToolPositionTypes initial = target.InitialToolPosition;
        try
        {
            target.InitialToolPosition = tanto ? BoundaryPlanarMill.ToolPositionTypes.TangentTo : BoundaryPlanarMill.ToolPositionTypes.On;
            set.AppendCurves(new NXObject[] { curve }, start, view);
        }
        finally { target.InitialToolPosition = initial; }
        // AppendCurves may infer new defaults; apply settings again to the populated set.
        set.BoundaryType = closed ? BoundarySet.BoundaryTypes.Closed : BoundarySet.BoundaryTypes.Open;
        set.ToolPosition = tanto ? BoundarySet.ToolPositionTypes.Contact : BoundarySet.ToolPositionTypes.On;
        set.ToolSide = outside ? BoundarySet.ToolSideTypes.OutsideOrRight : BoundarySet.ToolSideTypes.InsideOrLeft;
        set.CustomStock = false;
        set.CustomTolerance = false;
        BoundaryMemberSet[] members = set.BoundaryMemberList.GetContents();
        if (members.Length != 1) throw new InvalidOperationException("NX объединил или разделил выбранную кривую. Ожидался один элемент границы.");
        BoundaryMemberSetPlanarMill member = members[0] as BoundaryMemberSetPlanarMill;
        if (member == null) throw new InvalidOperationException("Неизвестный тип элемента границы NX.");
        // NXOpen member convention: 0 = ON, 1 = TANTO (not the UF boundary enum).
        member.ToolPosition = tanto ? 1 : 0;
        member.CustomStock = false;
        member.CustomTolerance = false;
        target.BoundaryList.Append(set);
        Verify(set, curve.Tag, closed, tanto, outside);
        return set;
    }

    public static void Verify(BoundarySetPlanarMill set, Tag curve, bool closed, bool tanto, bool outside)
    {
        if (set.BoundaryType != (closed ? BoundarySet.BoundaryTypes.Closed : BoundarySet.BoundaryTypes.Open))
            throw new InvalidOperationException("NX не сохранил тип границы Open/Closed.");
        if (set.ToolPosition != (tanto ? BoundarySet.ToolPositionTypes.Contact : BoundarySet.ToolPositionTypes.On))
            throw new InvalidOperationException("NX не сохранил выбранное положение инструмента у границы.");
        if (tanto && set.ToolSide != (outside ? BoundarySet.ToolSideTypes.OutsideOrRight : BoundarySet.ToolSideTypes.InsideOrLeft))
            throw new InvalidOperationException("NX не сохранил выбранную сторону TANTO.");
        BoundaryMemberSet[] members = set.BoundaryMemberList.GetContents();
        if (members.Length != 1 || members[0].Geometry == null || members[0].Geometry.Tag != curve)
            throw new InvalidOperationException("Состав границы не совпал с выбранной кривой.");
        BoundaryMemberSetPlanarMill member = members[0] as BoundaryMemberSetPlanarMill;
        if (member == null || member.ToolPosition != (tanto ? 1 : 0))
            throw new InvalidOperationException("NX не сохранил ON/TANTO у элемента границы.");
        if (set.CustomStock || member.CustomStock || set.CustomTolerance || member.CustomTolerance)
            throw new InvalidOperationException("NX не сохранил наследование припуска или допуска из операции.");
    }

    public static List<string> Snapshot(BoundaryPlanarMill boundary, HashSet<Tag> assigned)
    {
        List<string> result = new List<string>();
        foreach (BoundarySet set in boundary.BoundaryList.GetContents()) result.Add(State(set, assigned));
        return result;
    }

    public static string State(BoundarySet set, HashSet<Tag> assigned)
    {
        StringBuilder state = new StringBuilder();
        state.Append(set.BoundaryType).Append('|').Append(set.ToolPosition).Append('|').Append(set.ToolSide)
            .Append('|').Append(set.PlaneType).Append('|').Append(set.CustomOffset).Append('|')
            .Append(set.Offset.ToString("R", CultureInfo.InvariantCulture));
        BoundarySetPlanarMill planar = set as BoundarySetPlanarMill;
        if (planar == null) throw new InvalidOperationException("Неподдерживаемый тип существующей границы.");
        state.Append('|').Append(planar.CustomStock).Append('|').Append(planar.Stock.ToString("R", CultureInfo.InvariantCulture))
            .Append('|').Append(planar.CustomTolerance);
        foreach (BoundaryMemberSet item in set.BoundaryMemberList.GetContents())
        {
            if (item == null) throw new InvalidOperationException("NX вернул пустой элемент существующей границы.");
            Tag tag = item.Geometry == null ? Tag.Null : item.Geometry.Tag;
            if (assigned != null && tag != Tag.Null) assigned.Add(tag);
            BoundaryMemberSetPlanarMill member = item as BoundaryMemberSetPlanarMill;
            if (member == null) throw new InvalidOperationException("Неподдерживаемый элемент существующей границы.");
            state.Append(';').Append(tag).Append('|').Append(member.ToolPosition).Append('|').Append(member.CustomStock)
                .Append('|').Append(member.Stock.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(member.CustomTolerance);
        }
        return state.ToString();
    }
}
// END SEPARATE PLANAR BOUNDARY ADAPTER

// BEGIN PLANAR CURVE WINDOWS
public sealed class PlanarCurveOptions
{
    public bool Tanto, Outside;
    public string ModeLabel { get { return Tanto ? "TANTO" : "ON"; } }
    public string SideLabel { get { return Outside ? "Outside / Right — снаружи / справа" : "Inside / Left — внутри / слева"; } }
}

public static class PlanarCurveWindows
{
    private static readonly Color Ink = Color.FromArgb(31, 43, 49);
    private static readonly Color Muted = Color.FromArgb(100, 113, 119);
    private static readonly Color Accent = Color.FromArgb(24, 116, 89);
    private static readonly Color Canvas = Color.FromArgb(245, 247, 248);

    private sealed class SmoothForm : Form
    {
        public SmoothForm()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Segoe UI", 10F);
            BackColor = Canvas; ForeColor = Ink;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
        }
    }

    private sealed class ModeCard : Panel
    {
        public readonly RadioButton Radio;
        public readonly Label Description;
        private bool active;
        public ModeCard(string name, string description)
        {
            DoubleBuffered = true; Dock = DockStyle.Fill;
            Padding = new Padding(18, 16, 18, 14);
            Cursor = Cursors.Hand;
            Radio = new RadioButton { Text = name, Dock = DockStyle.Top, Height = 34,
                Font = new Font("Segoe UI", 17F, FontStyle.Bold), AutoSize = false };
            Description = new Label { Text = description, Dock = DockStyle.Fill, ForeColor = Muted,
                Padding = new Padding(1, 10, 0, 0) };
            Controls.Add(Description); Controls.Add(Radio);
            Click += delegate { Radio.Checked = true; };
            Description.Click += delegate { Radio.Checked = true; };
            Resize += delegate { Invalidate(); };
        }
        public void SelectCard(bool selected)
        {
            active = selected;
            BackColor = selected ? Color.FromArgb(231, 244, 238) : Color.White;
            Radio.ForeColor = selected ? Accent : Ink;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(active ? Accent : Color.FromArgb(218, 224, 227), active ? 2 : 1))
                e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        }
    }

    private static Label TextLabel(string text, float size, Color color, bool bold)
    {
        return new Label { Text = text, Dock = DockStyle.Fill, AutoSize = false,
            ForeColor = color, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0) };
    }

    private static Button ActionButton(string text, bool primary, int width)
    {
        Button button = new Button { Text = text, Width = width, Height = 40,
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Color.White,
            ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand,
            Margin = new Padding(10, 0, 0, 0), UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(211, 219, 223);
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    public static Form BuildOptions(string title, string operation, PlanarCurveOptions options)
    {
        SmoothForm form = new SmoothForm { Text = title, ClientSize = new Size(750, 500), MinimumSize = new Size(710, 520) };
        TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 21, 26, 20),
            ColumnCount = 1, RowCount = 7, BackColor = Canvas };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float height in new float[] { 38, 50, 152, 92, 38, 0, 43 })
            layout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        layout.Controls.Add(TextLabel("Кривые в геометрию детали", 19, Ink, true), 0, 0);
        Label operationLabel = TextLabel("PLANAR_MILL / PLANAR_PROFILE\nОперация: " + operation, 10, Muted, false);
        layout.Controls.Add(operationLabel, 0, 1);

        TableLayoutPanel cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 5, 0, 11) };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        ModeCard on = new ModeCard("ON", "Инструмент расположен\nна выбранной границе.");
        ModeCard tanto = new ModeCard("TANTO", "Инструмент касается границы\nс выбранной стороны.");
        on.Margin = new Padding(0, 0, 7, 0); tanto.Margin = new Padding(7, 0, 0, 0);
        cards.Controls.Add(on, 0, 0); cards.Controls.Add(tanto, 1, 0); layout.Controls.Add(cards, 0, 2);

        TableLayoutPanel side = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 5, 0, 0) };
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); side.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        side.Controls.Add(TextLabel("Сторона инструмента · TANTO", 10, Ink, true), 0, 0);
        ComboBox sideChoice = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(0), Name = "SideChoice" };
        sideChoice.Items.Add("Inside / Left — внутри замкнутой границы / слева от открытой");
        sideChoice.Items.Add("Outside / Right — снаружи замкнутой границы / справа от открытой");
        sideChoice.SelectedIndex = options.Outside ? 1 : 0;
        sideChoice.SelectedIndexChanged += delegate { options.Outside = sideChoice.SelectedIndex == 1; };
        side.Controls.Add(sideChoice, 0, 1);
        side.Controls.Add(TextLabel("Для открытой кривой левая и правая стороны зависят от её направления.", 9, Muted, false), 0, 2);
        layout.Controls.Add(side, 0, 3);
        layout.Controls.Add(TextLabel("Одна кривая — одна граница. Соседние кривые не объединяются.", 10, Muted, false), 0, 4);

        FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0) };
        Button next = ActionButton("Выбрать кривые  →", true, 216); next.DialogResult = DialogResult.OK;
        Button cancel = ActionButton("Отмена", false, 108); cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(next); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 6);
        form.Controls.Add(layout); form.AcceptButton = next; form.CancelButton = cancel;
        bool changing = false;
        EventHandler update = delegate(object sender, EventArgs e)
        {
            if (changing) return;
            RadioButton clicked = (RadioButton)sender;
            if (!clicked.Checked) return;
            changing = true;
            options.Tanto = clicked == tanto.Radio;
            on.Radio.Checked = !options.Tanto; tanto.Radio.Checked = options.Tanto;
            on.SelectCard(!options.Tanto); tanto.SelectCard(options.Tanto);
            side.Enabled = options.Tanto;
            changing = false;
        };
        on.Radio.CheckedChanged += update; tanto.Radio.CheckedChanged += update;
        if (options.Tanto) tanto.Radio.Checked = true; else on.Radio.Checked = true;
        return form;
    }

    public static PlanarCurveOptions AskOptions(string title, string operation)
    {
        PlanarCurveOptions result = new PlanarCurveOptions();
        using (Form form = BuildOptions(title, operation, result))
            return form.ShowDialog() == DialogResult.OK ? result : null;
    }

    public static Form BuildReport(string title, string headline, string report, int severity)
    {
        SmoothForm form = new SmoothForm { Text = title, ClientSize = new Size(810, 555), MinimumSize = new Size(680, 430) };
        TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(25, 18, 25, 18), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        Color statusColor = severity == 2 ? Color.FromArgb(171, 57, 57) : severity == 1 ? Color.FromArgb(151, 108, 32) : Accent;
        layout.Controls.Add(TextLabel(headline, 19, statusColor, true), 0, 0);
        layout.Controls.Add(TextLabel("Результат и проверка границ", 10, Muted, false), 0, 1);
        TextBox details = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Text = report,
            ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Color.White, ForeColor = Ink,
            BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10), Margin = new Padding(0, 3, 0, 12) };
        layout.Controls.Add(details, 0, 2);
        FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, 7, 0, 0), Margin = new Padding(0) };
        Button close = ActionButton("Закрыть", true, 125); close.DialogResult = DialogResult.OK;
        Button copy = ActionButton("Копировать всё", false, 180);
        copy.Click += delegate
        {
            try { Clipboard.SetText(report); copy.Text = "Скопировано"; }
            catch (Exception error) { MessageBox.Show(form, "Не удалось скопировать текст.\n" + error.Message,
                title, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        actions.Controls.Add(close); actions.Controls.Add(copy); layout.Controls.Add(actions, 0, 3);
        form.Controls.Add(layout); form.AcceptButton = close; form.CancelButton = close;
        form.Shown += delegate { details.Select(0, 0); close.Focus(); };
        return form;
    }

    public static void ShowReport(string title, string headline, string report, int severity)
    {
        using (Form form = BuildReport(title, headline, report, severity)) form.ShowDialog();
    }
}
// END PLANAR CURVE WINDOWS
