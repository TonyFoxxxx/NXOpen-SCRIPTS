// NX_Planar_Add_Curves.cs - separate curve boundaries for PLANAR_MILL / PLANAR_PROFILE.
// SCRIPT_VERSION: V1.01
// Select one planar operation, choose ON / TANTO, then select existing work-part curves.
// Each new curve becomes one Part Boundary; geometrically closed curves remain Closed.
// Existing assignments are skipped. No chaining, new curves, files or automatic toolpath generation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using System.Runtime.InteropServices;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using NXOpen.Utilities;
using CamOperation = NXOpen.CAM.Operation;

public class NXPlanarAddCurves
{
    private const string SCRIPT_VERSION = "V1.01";
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

            PlanarCurveOptions options = PlanarCurveWindows.AskOptions(uf.Ui.GetDefaultParent(), Title, operation.Name);
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
                PlanarCurveWindows.ShowReport(uf.Ui.GetDefaultParent(), Title, "Кривые уже назначены", report.ToString(), 1);
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
        try { PlanarCurveWindows.ShowReport(uf.Ui.GetDefaultParent(), Title, headline, report.ToString(), success ? 0 : 2); }
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

// These dialogs use the same in-memory Win32 dialog mechanism as the project's PDF
// exporter. The NX journal compiler only needs the base runtime and NXOpen.
public static class PlanarCurveWindows
{
    public static PlanarCurveOptions AskOptions(IntPtr owner, string title, string operation)
    {
        Dialog dialog = new Dialog(title, "Кривые в геометрию детали", operation, null, 0);
        return dialog.Show(owner) == OK ? dialog.Options : null;
    }

    public static void ShowReport(IntPtr owner, string title, string headline, string report, int severity)
    {
        new Dialog(title, headline, null, report, severity).Show(owner);
    }

    private const int OK = 1, Cancel = 2;
    private const int Heading = 100, Summary = 101, On = 110, Tanto = 111;
    private const int OnRule = 112, TantoRule = 113, OnHint = 114, TantoHint = 115;
    private const int SideLabel = 120, Side = 121, SideHint = 122, Note = 123;
    private const int ReportEdit = 130, Copy = 131;
    private const uint TabStop = 0x00010000, Group = 0x00020000;
    private const uint ButtonAutoRadio = 9, ButtonPushLike = 0x1000;
    private const uint StaticNoPrefix = 0x80, StaticNotify = 0x100;
    private const uint WmSetFont = 0x30, CbAddString = 0x143, CbGetCurSel = 0x147, CbSetCurSel = 0x14e;

    private static int Rgb(int r, int g, int b) { return r | (g << 8) | (b << 16); }
    private static readonly int Canvas = Rgb(245, 247, 248), Ink = Rgb(31, 43, 49);
    private static readonly int Accent = Rgb(24, 116, 89), Muted = Rgb(93, 109, 115);
    private static readonly int Neutral = Rgb(212, 220, 223);

    private sealed class Dialog
    {
        internal readonly PlanarCurveOptions Options = new PlanarCurveOptions();
        private readonly string title, headline, operation, report;
        private readonly int severity;
        private readonly List<IntPtr> fonts = new List<IntPtr>();
        private IntPtr window;
        private bool ready;
        private Exception callbackError;
        private bool IsReport { get { return report != null; } }

        internal Dialog(string title, string headline, string operation, string report, int severity)
        {
            this.title = title;
            this.headline = headline;
            this.operation = operation;
            this.report = report;
            this.severity = severity;
        }

        internal int Show(IntPtr owner)
        {
            byte[] template = Template(title, IsReport ? 398 : 374, IsReport ? 250 : 211);
            IntPtr memory = Marshal.AllocHGlobal(template.Length);
            Native.DialogProc procedure = HandleMessage;
            try
            {
                Marshal.Copy(template, 0, memory, template.Length);
                IntPtr result = Native.DialogBoxIndirectParamW(Native.GetModuleHandleW(null), memory, owner, procedure, IntPtr.Zero);
                int error = Marshal.GetLastWin32Error();
                if (callbackError != null) throw new InvalidOperationException("Ошибка окна журнала.", callbackError);
                if (result.ToInt64() == -1) throw new System.ComponentModel.Win32Exception(error, "Не удалось открыть окно журнала.");
                return result.ToInt32();
            }
            finally
            {
                GC.KeepAlive(procedure);
                Marshal.FreeHGlobal(memory);
                foreach (IntPtr font in fonts) Native.DeleteObject(font);
            }
        }

        private IntPtr HandleMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                switch (message)
                {
                    case 0x110: // WM_INITDIALOG
                        window = hwnd;
                        BuildControls();
                        Native.SendMessageW(hwnd, 0x401, new IntPtr(OK), IntPtr.Zero); // DM_SETDEFID
                        ready = true;
                        Layout();
                        Native.SetFocus(Item(IsReport ? OK : On));
                        return IntPtr.Zero; // Keep the explicitly assigned keyboard focus.
                    case 0x111: // WM_COMMAND
                        if (Command((int)(wParam.ToInt64() & 0xffff), (int)((wParam.ToInt64() >> 16) & 0xffff)))
                            return new IntPtr(1);
                        break;
                    case 0x10: // WM_CLOSE (including the title-bar cross)
                        Native.EndDialog(hwnd, new IntPtr(Cancel));
                        return new IntPtr(1);
                    case 0x05: // WM_SIZE
                        if (ready) Layout();
                        break;
                    case 0x24: // WM_GETMINMAXINFO
                        if (ready) { SetMinimumSize(lParam); return new IntPtr(1); }
                        break;
                    case 0x136: // WM_CTLCOLORDLG
                        return Brush(wParam, Canvas, Ink);
                    case 0x138: // WM_CTLCOLORSTATIC (also used by read-only EDITs)
                        return ControlBrush(wParam, Native.GetDlgCtrlID(lParam));
                }
            }
            catch (Exception error)
            {
                if (callbackError == null) callbackError = error;
                Native.EndDialog(hwnd, new IntPtr(Cancel));
                return new IntPtr(1);
            }
            return IntPtr.Zero;
        }

        private IntPtr Item(int id) { return Native.GetDlgItem(window, id); }

        private void BuildControls()
        {
            IntPtr normal = Native.SendMessageW(window, 0x31, IntPtr.Zero, IntPtr.Zero); // WM_GETFONT
            IntPtr headingFont = CreateFont(18, 700, "Segoe UI", normal);
            Add("STATIC", Heading, headline, StaticNoPrefix, headingFont);
            if (IsReport)
            {
                Add("STATIC", Summary, "Результат и проверка границ", StaticNoPrefix, normal);
                // Multiline, read-only, scrollable edit; copy always takes the original full report.
                Add("EDIT", ReportEdit, "", TabStop | 0x00b008c4u, CreateFont(10, 400, "Consolas", normal));
                Native.SendMessageW(Item(ReportEdit), 0xc5, new IntPtr(Int32.MaxValue - 2), IntPtr.Zero); // EM_SETLIMITTEXT
                if (!Native.SetWindowTextW(Item(ReportEdit), report))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Не удалось показать текст отчёта.");
                Add("BUTTON", Copy, "Копировать всё", TabStop, normal);
                Add("BUTTON", OK, "Закрыть", TabStop | 1u, normal); // BS_DEFPUSHBUTTON
                return;
            }
            Add("STATIC", Summary, "PLANAR_MILL / PLANAR_PROFILE\r\nОперация: " + operation, StaticNoPrefix | 0x4000u, normal);
            IntPtr modeFont = CreateFont(15, 700, "Segoe UI", normal);
            // The two real radio controls form one keyboard group. The next control starts a new group.
            Add("BUTTON", On, "ON", Group | TabStop | ButtonAutoRadio | ButtonPushLike, modeFont);
            Add("BUTTON", Tanto, "TANTO", ButtonAutoRadio | ButtonPushLike, modeFont);
            Add("STATIC", OnRule, "", Group | StaticNotify, normal);
            Add("STATIC", TantoRule, "", StaticNotify, normal);
            Add("STATIC", OnHint, "Инструмент расположен на выбранной границе.", StaticNoPrefix | StaticNotify, normal);
            Add("STATIC", TantoHint, "Инструмент касается границы с выбранной стороны.", StaticNoPrefix | StaticNotify, normal);
            Add("STATIC", SideLabel, "Сторона инструмента · TANTO", StaticNoPrefix, normal);
            Add("COMBOBOX", Side, "", TabStop | 0x00200003u, normal); // CBS_DROPDOWNLIST | WS_VSCROLL
            AddSide(0, "Inside / Left — внутри замкнутой границы / слева от открытой");
            AddSide(1, "Outside / Right — снаружи замкнутой границы / справа от открытой");
            if (Native.SendMessageW(Item(Side), CbSetCurSel, IntPtr.Zero, IntPtr.Zero).ToInt64() != 0)
                throw new InvalidOperationException("Не удалось выбрать исходную сторону TANTO.");
            Add("STATIC", SideHint, "Слева и справа определяются по направлению открытой кривой.", StaticNoPrefix, normal);
            Add("STATIC", Note, "Одна выбранная кривая образует одну границу.", StaticNoPrefix, normal);
            Add("BUTTON", Cancel, "Отмена", TabStop, normal);
            Add("BUTTON", OK, "Выбрать кривые  →", TabStop | 1u, normal);
            SetMode(false);
        }

        private void Add(string kind, int id, string text, uint style, IntPtr font)
        {
            // WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS
            IntPtr child = Native.CreateWindowExW(0, kind, text, 0x54000000u | style, 0, 0, 1, 1,
                window, new IntPtr(id), Native.GetModuleHandleW(null), IntPtr.Zero);
            if (child == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Не удалось создать элемент " + kind + ".");
            Native.SendMessageW(child, WmSetFont, font, IntPtr.Zero);
        }

        private IntPtr CreateFont(int points, int weight, string family, IntPtr fallback)
        {
            int dpi = (int)Native.GetDpiForWindow(window);
            int height = -(int)Math.Round(points * (dpi > 0 ? dpi : 96) / 72.0);
            IntPtr font = Native.CreateFontW(height, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, family);
            if (font == IntPtr.Zero) return fallback;
            fonts.Add(font);
            return font;
        }

        private void AddSide(int expected, string text)
        {
            if (Native.SendMessageTextW(Item(Side), CbAddString, IntPtr.Zero, text).ToInt64() != expected)
                throw new InvalidOperationException("Не удалось заполнить список сторон TANTO.");
        }

        private void SetMode(bool tanto)
        {
            Options.Tanto = tanto;
            Native.CheckRadioButton(window, On, Tanto, tanto ? Tanto : On);
            Native.EnableWindow(Item(Side), tanto);
            Native.EnableWindow(Item(SideLabel), tanto);
            Native.EnableWindow(Item(SideHint), tanto);
            Native.InvalidateRect(Item(OnRule), IntPtr.Zero, true);
            Native.InvalidateRect(Item(TantoRule), IntPtr.Zero, true);
        }

        private bool Command(int id, int notification)
        {
            if (notification != 0) return false; // BN_CLICKED / STN_CLICKED or a dialog key
            switch (id)
            {
                case On: case OnRule: case OnHint: SetMode(false); return true;
                case Tanto: case TantoRule: case TantoHint: SetMode(true); return true;
                case Cancel: Native.EndDialog(window, new IntPtr(Cancel)); return true;
                case OK:
                    if (!IsReport && Options.Tanto)
                    {
                        int index = Native.SendMessageW(Item(Side), CbGetCurSel, IntPtr.Zero, IntPtr.Zero).ToInt32();
                        if (index != 0 && index != 1) throw new InvalidOperationException("Выберите сторону TANTO.");
                        Options.Outside = index == 1;
                    }
                    Native.EndDialog(window, new IntPtr(OK));
                    return true;
                case Copy:
                    if (!IsReport) return false;
                    if (CopyText(window, report))
                    {
                        Native.SetWindowTextW(Item(Copy), "Скопировано");
                        Native.SetWindowTextW(Item(Summary), "Отчёт скопирован целиком.");
                    }
                    else Native.SetWindowTextW(Item(Summary), "Буфер обмена недоступен. Повторите копирование.");
                    return true;
            }
            return false;
        }

        private static IntPtr Brush(IntPtr dc, int background, int text)
        {
            Native.SetTextColor(dc, text);
            Native.SetBkColor(dc, background);
            Native.SetDCBrushColor(dc, background);
            return Native.GetStockObject(18); // DC_BRUSH belongs to Windows.
        }

        private IntPtr ControlBrush(IntPtr dc, int id)
        {
            if (id == ReportEdit) return Brush(dc, 0xffffff, Ink);
            if (id == OnRule || id == TantoRule)
                return Brush(dc, ((id == TantoRule) == Options.Tanto) ? Accent : Neutral, Ink);
            int color = Muted;
            if (id == Heading) color = !IsReport ? Ink : severity == 2 ? Rgb(162, 54, 49) : severity == 1 ? Rgb(136, 96, 28) : Accent;
            if (!IsReport && Options.Tanto && id == SideLabel) color = Ink;
            return Brush(dc, Canvas, color);
        }

        private void Layout()
        {
            Native.Rect client;
            if (!Native.GetClientRect(window, out client) || client.Right < 1 || client.Bottom < 1) return;
            // Four horizontal and eight vertical dialog units equal the template font's base units.
            Native.Rect units = Units(4, 8);
            int width = client.Right * 4 / Math.Max(1, units.Right);
            int height = client.Bottom * 8 / Math.Max(1, units.Bottom);
            const int pad = 12, gap = 7, buttonHeight = 19;
            int usable = width - 2 * pad;
            int buttonsY = height - pad - buttonHeight;
            Place(Heading, pad, 8, usable, 21);
            if (IsReport)
            {
                Place(Summary, pad, 32, usable, 13);
                Place(ReportEdit, pad, 49, usable, buttonsY - 56);
                Place(OK, width - pad - 66, buttonsY, 66, buttonHeight);
                Place(Copy, width - pad - 66 - gap - 88, buttonsY, 88, buttonHeight);
            }
            else
            {
                int first = (usable - gap) / 2, second = usable - gap - first;
                int right = pad + first + gap;
                Place(Summary, pad, 32, usable, 22);
                Place(On, pad, 60, first, 27); Place(Tanto, right, 60, second, 27);
                Place(OnRule, pad, 87, first, 2); Place(TantoRule, right, 87, second, 2);
                Place(OnHint, pad + 2, 94, first - 3, 22); Place(TantoHint, right + 2, 94, second - 3, 22);
                Place(SideLabel, pad, 120, usable, 12);
                Place(Side, pad, 134, usable, 64); // Includes the height of the opened dropdown.
                Place(SideHint, pad, 151, usable, 13);
                Place(Note, pad, 167, usable, 13);
                Place(Cancel, width - pad - 101 - gap - 61, buttonsY, 61, buttonHeight);
                Place(OK, width - pad - 101, buttonsY, 101, buttonHeight);
            }
        }

        private Native.Rect Units(int x, int y)
        {
            Native.Rect rect = new Native.Rect { Right = x, Bottom = y };
            if (!Native.MapDialogRect(window, ref rect)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return rect;
        }
        private void Place(int id, int x, int y, int width, int height)
        {
            Native.Rect rect = new Native.Rect { Left = x, Top = y, Right = x + width, Bottom = y + height };
            if (!Native.MapDialogRect(window, ref rect)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Native.SetWindowPos(Item(id), IntPtr.Zero, rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left),
                Math.Max(1, rect.Bottom - rect.Top), 0x14u); // SWP_NOZORDER | SWP_NOACTIVATE
        }
        private void SetMinimumSize(IntPtr data)
        {
            Native.Rect client, outer;
            if (!Native.GetClientRect(window, out client) || !Native.GetWindowRect(window, out outer)) return;
            Native.Rect min = Units(IsReport ? 275 : 330, IsReport ? 136 : 211);
            Native.MinMaxInfo limits = (Native.MinMaxInfo)Marshal.PtrToStructure(data, typeof(Native.MinMaxInfo));
            limits.MinTrack.X = Math.Max(limits.MinTrack.X, min.Right + outer.Right - outer.Left - client.Right);
            limits.MinTrack.Y = Math.Max(limits.MinTrack.Y, min.Bottom + outer.Bottom - outer.Top - client.Bottom);
            Marshal.StructureToPtr(limits, data, false);
        }
    }

    // DLGTEMPLATE with the default dialog class, a Unicode caption and a DPI-scaled dialog font.
    private static byte[] Template(string title, int width, int height)
    {
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode))
        {
            // WS_POPUP | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MAXIMIZEBOX | WS_CLIPCHILDREN
            // DS_MODALFRAME | DS_SETFONT | DS_CENTER; WS_EX_COMPOSITED buffers child updates during resize.
            writer.Write(0x82cd08c0u); writer.Write(0x02000000u); writer.Write((ushort)0);
            writer.Write((short)0); writer.Write((short)0); writer.Write((short)width); writer.Write((short)height);
            writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write(Encoding.Unicode.GetBytes(title + "\0"));
            writer.Write((ushort)10);
            writer.Write(Encoding.Unicode.GetBytes("Segoe UI\0"));
            return stream.ToArray();
        }
    }

    private static bool CopyText(IntPtr owner, string text)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
        IntPtr memory = Native.GlobalAlloc(2, new UIntPtr((uint)bytes.Length)); // GMEM_MOVEABLE
        if (memory == IntPtr.Zero) return false;
        try
        {
            IntPtr pointer = Native.GlobalLock(memory);
            if (pointer == IntPtr.Zero) return false;
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { Native.GlobalUnlock(memory); }

            if (!Native.OpenClipboard(owner)) return false;
            try
            {
                if (!Native.EmptyClipboard() || Native.SetClipboardData(13, memory) == IntPtr.Zero) return false; // CF_UNICODETEXT
                memory = IntPtr.Zero; // Ownership transfers to Windows only on success.
                return true;
            }
            finally { Native.CloseClipboard(); }
        }
        finally { if (memory != IntPtr.Zero) Native.GlobalFree(memory); }
    }

    private static class Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate IntPtr DialogProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct MinMaxInfo { internal Point Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)] internal static extern IntPtr DialogBoxIndirectParamW(IntPtr instance, IntPtr template, IntPtr owner, DialogProc proc, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] internal static extern IntPtr CreateWindowExW(uint exStyle, string type, string text, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr id, IntPtr instance, IntPtr param);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern IntPtr GetModuleHandleW(string module);
        [DllImport("user32.dll")] internal static extern bool EndDialog(IntPtr hwnd, IntPtr result);
        [DllImport("user32.dll")] internal static extern IntPtr GetDlgItem(IntPtr hwnd, int id);
        [DllImport("user32.dll")] internal static extern int GetDlgCtrlID(IntPtr control);
        [DllImport("user32.dll")] internal static extern bool EnableWindow(IntPtr hwnd, bool enable);
        [DllImport("user32.dll")] internal static extern bool CheckRadioButton(IntPtr hwnd, int first, int last, int selected);
        [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] internal static extern bool SetWindowTextW(IntPtr hwnd, string text);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern IntPtr SendMessageTextW(IntPtr hwnd, uint msg, IntPtr wParam, string text);
        [DllImport("user32.dll")] internal static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool MapDialogRect(IntPtr hwnd, ref Rect rect);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern IntPtr CreateFontW(int height, int width, int angle, int orientation, int weight, uint italic, uint underline, uint strikeout, uint charset, uint precision, uint clip, uint quality, uint pitch, string face);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] internal static extern IntPtr GetStockObject(int index);
        [DllImport("gdi32.dll")] internal static extern int SetBkColor(IntPtr dc, int color);
        [DllImport("gdi32.dll")] internal static extern int SetTextColor(IntPtr dc, int color);
        [DllImport("gdi32.dll")] internal static extern int SetDCBrushColor(IntPtr dc, int color);
        [DllImport("user32.dll")] internal static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] internal static extern bool CloseClipboard();
        [DllImport("user32.dll")] internal static extern bool EmptyClipboard();
        [DllImport("user32.dll")] internal static extern IntPtr SetClipboardData(uint format, IntPtr memory);
        [DllImport("kernel32.dll")] internal static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] internal static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] internal static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] internal static extern IntPtr GlobalFree(IntPtr memory);
    }
}
// END PLANAR CURVE WINDOWS
