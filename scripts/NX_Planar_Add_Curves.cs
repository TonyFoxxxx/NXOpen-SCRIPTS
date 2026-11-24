// NX_Planar_Add_Curves.cs - separate curve boundaries for PLANAR_MILL / PLANAR_PROFILE.
// SCRIPT_VERSION: V1.09
// Select one planar operation, choose ON / TANTO and a boundary plane, then pick existing curves.
// One saved boundary per curve in each selected Part / Blank destination.
// Remembers confirmed dialog settings per user. No automatic toolpath calculation or part save.

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
using BlockUI = NXOpen.BlockStyler;
using CamOperation = NXOpen.CAM.Operation;

public class NXPlanarAddCurves
{
    private const string SCRIPT_VERSION = "V1.09";
    private static readonly string Title = "Кривые в PLANAR — " + SCRIPT_VERSION + " — "
        + Encoding.UTF8.GetString(Convert.FromBase64String("YnkgQFRvbnlfRm94eHggKFRHKQ=="));

    private sealed class BoundaryTarget
    {
        internal readonly CamGeomType Kind;
        internal readonly bool Enabled;
        internal string Name { get { return Kind == CamGeomType.CamPart ? "Part Boundaries" : "Blank Boundaries"; } }
        internal List<Curve> Selection;
        internal readonly List<Curve> Pending = new List<Curve>();
        internal readonly Dictionary<Tag, bool> Requested = new Dictionary<Tag, bool>();
        internal readonly Dictionary<Tag, BoundarySet.BoundaryTypes> Confirmed = new Dictionary<Tag, BoundarySet.BoundaryTypes>();
        internal SeparatePlanarBoundary.Saved Original;
        internal List<string> Editor;
        internal BoundaryTarget(CamGeomType kind, bool enabled) { Kind = kind; Enabled = enabled; }
        internal SeparatePlanarBoundary.Saved Read(UFSession uf, Tag operation)
        { return SeparatePlanarBoundary.Read(uf.Cambnd, operation, Kind); }

        internal void Prepare(UFSession uf, Part part, StringBuilder report)
        {
            if (!Enabled) return;
            report.AppendLine(Name + " — выбрано уникальных кривых: " + Selection.Count + ". Сохранённых границ до запуска: " + Original.Boundaries.Count);
            List<string> skipped = new List<string>();
            foreach (Curve curve in Selection)
            {
                string label = CurveLabel(curve);
                if (Original.Sources.Contains(curve.Tag)) { skipped.Add(label); continue; }
                if (curve.IsOccurrence || curve.OwningPart == null || curve.OwningPart.Tag != part.Tag)
                    throw new InvalidOperationException(label + ": выберите кривую рабочей CAM-детали.");
                if (!(curve is Line) && !(curve is Arc) && !(curve is Spline) && !(curve is Ellipse))
                    throw new InvalidOperationException(label + ": поддерживаются линии, дуги, окружности, эллипсы и сплайны.");
                Requested.Add(curve.Tag, IsClosed(uf, curve));
                Pending.Add(curve);
            }
            report.AppendLine("Уже назначенных кривых пропущено: " + skipped.Count + ". Новых: " + Pending.Count + ".");
            foreach (string item in skipped) report.AppendLine("  " + item);
        }

        internal void Verify(UFSession uf, Tag operation, bool tanto, bool final, StringBuilder report)
        {
            SeparatePlanarBoundary.Saved actual = Read(uf, operation);
            if (Pending.Count == 0)
            {
                if (!Original.SameAs(actual)) throw new InvalidOperationException(Name + ": изменился состав прежних границ или наследование.");
                return;
            }
            if (report != null) report.AppendLine(Name + " после добавления: " + actual.Boundaries.Count + " (ожидалось " + (Original.Boundaries.Count + Requested.Count) + ").");
            SeparatePlanarBoundary.VerifySaved(Original, actual, Requested, tanto, final ? Confirmed : null, report, Name);
        }
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
        BoundaryTarget[] targets = null;
        StringBuilder report = new StringBuilder(Title + Environment.NewLine);
        try
        {
            if (part == null || part.CAMSetup == null) throw new InvalidOperationException("Откройте CAM-проект в Manufacturing.");
            operation = SelectedOperation(uf, part);
            report.AppendLine("Операция: " + operation.Name + " (Tag " + operation.Tag + ")");
            report.AppendLine("Класс NXOpen: " + operation.GetType().FullName);
            stage = "Выбор границ и положения инструмента";
            PlanarCurveOptions options = PlanarCurveWindows.AskOptions(uf.Ui.GetDefaultParent(), Title, operation.Name);
            if (options == null) return;
            if (!String.IsNullOrEmpty(options.PreferenceNotice)) report.AppendLine(options.PreferenceNotice);
            targets = new BoundaryTarget[] { new BoundaryTarget(CamGeomType.CamPart, options.Part),
                new BoundaryTarget(CamGeomType.CamBlank, options.Blank) };
            if (!options.Part && !options.Blank) throw new InvalidOperationException("Отметьте хотя бы один набор границ.");
            report.AppendLine("Режим: " + options.ModeLabel);
            if (options.Tanto)
            {
                report.AppendLine("Замкнутые кривые: " + options.ClosedSideLabel);
                report.AppendLine("Открытые кривые: " + options.OpenSideLabel);
            }
            foreach (BoundaryTarget target in targets)
                if (target.Enabled) EnsureLocalBoundaries(uf, operation, target);
            PlanarBoundaryPlane plane = null;
            if (options.SelectPlane)
            {
                stage = "Построение плоскости границ в окне Plane";
                plane = PlanarBoundaryPlane.Choose(session, part, uf, "Выберите плоскость в модели — " + Title, report, options);
                if (plane == null) return;
                report.AppendLine("Плоскость в ABS рабочей детали: " + plane.Description + ".");
            }
            else report.AppendLine("Плоскость границ: автоматически для каждой кривой.");

            foreach (BoundaryTarget target in targets)
            {
                if (!target.Enabled) continue;
                stage = "Выбор кривых для " + target.Name;
                target.Selection = SelectCurves(target);
                if (target.Selection == null) return;
            }
            if (session.Parts.Work == null || session.Parts.Work.Tag != part.Tag)
                throw new InvalidOperationException("Рабочая деталь изменилась. Запустите скрипт заново.");
            int total = 0;
            foreach (BoundaryTarget target in targets)
            {
                stage = "Проверка исходной геометрии " + target.Name;
                if (target.Enabled) EnsureLocalBoundaries(uf, operation, target);
                target.Original = target.Read(uf, operation.Tag);
                target.Prepare(uf, part, report);
                total += target.Pending.Count;
            }
            if (total == 0)
            {
                report.AppendLine("Все выбранные кривые уже назначены в соответствующих наборах.");
                PlanarCurveWindows.ShowReport(uf.Ui.GetDefaultParent(), Title, "Кривые уже назначены", report.ToString(), 1);
                return;
            }
            stage = "Чтение исходных настроек редактора NX";
            InspectEditor(part, operation, targets, options, null, EditorPass.Snapshot, report);
            foreach (BoundaryTarget target in targets)
                if (!target.Original.SameAs(target.Read(uf, operation.Tag)))
                    throw new InvalidOperationException(target.Name + ": границы изменились во время выбора. Повторите запуск.");

            mark = session.SetUndoMark(Session.MarkVisibility.Visible, "Кривые в PLANAR: " + options.ModeLabel);
            pendingUndo = true;
            foreach (BoundaryTarget target in targets)
            {
                for (int i = 0; i < target.Pending.Count; i++)
                {
                    Curve curve = target.Pending[i];
                    stage = target.Name + " — добавление кривой " + (i + 1) + "/" + target.Pending.Count + ": " + CurveLabel(curve);
                    uf.Ui.SetStatus(stage);
                    SeparatePlanarBoundary.Append(uf.Cambnd, operation.Tag, target.Kind, curve.Tag, target.Requested[curve.Tag], options, plane);
                }
            }
            foreach (BoundaryTarget target in targets)
            {
                stage = "Чтение сохранённых " + target.Name + " после добавления";
                target.Verify(uf, operation.Tag, options.Tanto, false, report);
            }
            stage = "Настройка новых границ в редакторе NX";
            InspectEditor(part, operation, targets, options, plane, EditorPass.Configure, report);
            foreach (BoundaryTarget target in targets)
                if (target.Enabled) report.AppendLine(target.Name + ". Итоговое число сохранённых границ: " + BoundaryCount(uf, operation.Tag, target.Kind)
                    + " (ожидалось " + (target.Original.Boundaries.Count + target.Pending.Count) + ").");
            stage = "Подтверждение границ в повторно открытом редакторе NX";
            InspectEditor(part, operation, targets, options, plane, EditorPass.Confirm, null);
            foreach (BoundaryTarget target in targets)
            {
                stage = "Итоговая проверка " + target.Name;
                target.Verify(uf, operation.Tag, options.Tanto, true, null);
            }
            success = true;
            pendingUndo = false;
            headline = "Добавлено границ: " + total;
            report.AppendLine(); report.AppendLine("Результат: успешно.");
            foreach (BoundaryTarget target in targets)
            {
                if (!target.Enabled) continue;
                int closed = 0;
                foreach (BoundarySet.BoundaryTypes type in target.Confirmed.Values) if (type == BoundarySet.BoundaryTypes.Closed) closed++;
                int open = target.Confirmed.Count - closed;
                report.AppendLine(target.Name + ": добавлено " + target.Confirmed.Count + ". Open: " + open + "; Closed: " + closed + ".");
                if (options.Tanto && closed > 0) report.AppendLine("Замкнутых границ со стороной " + options.ClosedSideLabel + ": " + closed + ".");
                if (options.Tanto && open > 0) report.AppendLine("Открытых границ со стороной " + options.OpenSideLabel + ": " + open + ".");
            }
            if (plane != null) report.AppendLine("Выбранная плоскость подтверждена для всех новых границ: " + total + ".");
            report.AppendLine("Проверены состав и считанные параметры прежних границ. Припуск и допуск новых границ наследуются из операции.");
            report.AppendLine("Траектория не рассчитывалась. Проект не сохранялся. Отмена этого запуска: Ctrl+Z.");
            try { uf.UiOnt.Refresh(); } catch (Exception refresh) { report.AppendLine("Навигатор не обновлён: " + refresh.Message); }
        }
        catch (Exception ex)
        {
            report.AppendLine(); report.AppendLine("Результат: ошибка."); report.AppendLine("Этап: " + stage);
            NXException nxError = ex as NXException;
            if (nxError != null) report.AppendLine("Код NX: " + nxError.ErrorCode);
            report.AppendLine(ex.ToString());
            if (pendingUndo)
            {
                try
                {
                    session.UndoToMark(mark, null);
                    report.AppendLine("UndoToMark выполнен.");
                    foreach (BoundaryTarget target in targets)
                    {
                        SeparatePlanarBoundary.Saved restored = target.Read(uf, operation.Tag);
                        report.AppendLine(target.Name + " после отката: " + restored.Boundaries.Count + " (исходно " + target.Original.Boundaries.Count + ").");
                        if (!target.Original.SameAs(restored)) throw new InvalidOperationException(target.Name + ": исходные границы или наследование не восстановлены.");
                    }
                    InspectEditor(part, operation, targets, null, null, EditorPass.Restored, null);
                    report.AppendLine("Откат проверен: исходный состав и считанные параметры границ восстановлены.");
                    session.DeleteUndoMark(mark, null);
                }
                catch (Exception rollback) { report.AppendLine("Не удалось подтвердить полный откат. Проверьте операцию перед сохранением."); report.AppendLine(rollback.ToString()); }
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

    private static List<Curve> SelectCurves(BoundaryTarget target)
    {
        string which = target.Kind == CamGeomType.CamPart ? "детали" : "заготовки";
        TaggedObject[] objects;
        Selection.MaskTriple[] mask = new Selection.MaskTriple[] {
            new Selection.MaskTriple(UFConstants.UF_line_type, 0, 0),
            new Selection.MaskTriple(UFConstants.UF_circle_type, 0, 0),
            new Selection.MaskTriple(UFConstants.UF_conic_type, 0, 0),
            new Selection.MaskTriple(UFConstants.UF_spline_type, 0, 0) };
        Selection.Response answer = UI.GetUI().SelectionManager.SelectTaggedObjects(
            "Выберите кривые для " + target.Name + " и нажмите OK.", "Выберите кривые " + which + " — " + target.Name + " — " + Title,
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

    private static bool IsClosed(UFSession uf, Curve curve)
    {
        double length = curve.GetLength();
        if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0)
            throw new InvalidOperationException(CurveLabel(curve) + ": некорректная длина.");
        int closed = uf.Modl.AskCurveClosed(curve.Tag);
        if (closed != 0 && closed != 1) throw new InvalidOperationException("NX не определил замкнутость " + CurveLabel(curve));
        return closed == 1;
    }

    private static string CurveLabel(Curve curve)
    {
        return (string.IsNullOrEmpty(curve.Name) ? curve.GetType().Name : curve.Name) + " [" + curve.Tag + "]";
    }

    private static void EnsureLocalBoundaries(UFSession uf, PlanarMilling operation, BoundaryTarget target)
    {
        bool inherited; uf.Cambnd.IsInherited(operation.Tag, target.Kind, out inherited);
        if (!inherited) return;
        if (BoundaryCount(uf, operation.Tag, target.Kind) > 0)
            throw new InvalidOperationException("Операция наследует " + target.Name + ". Сначала задайте для неё локальные границы в NX.");
        NCGroup parent = operation.GetParent(CAMSetup.View.Geometry);
        HashSet<Tag> seen = new HashSet<Tag>();
        while (parent != null && seen.Add(parent.Tag))
        {
            if (BoundaryCount(uf, parent.Tag, target.Kind) > 0)
                throw new InvalidOperationException("У родительской геометрии есть наследуемые " + target.Name + ". Сначала задайте для операции локальные границы.");
            parent = parent.GetParent();
        }
    }

    private static int BoundaryCount(UFSession uf, Tag owner, CamGeomType kind)
    {
        int count; IntPtr[] items;
        uf.Cambnd.AskBoundaries(owner, kind, out count, out items);
        if (count < 0 || (count > 0 && (items == null || items.Length != count)))
            throw new InvalidOperationException("Не удалось прочитать количество границ NX.");
        return count;
    }

    private enum EditorPass { Snapshot, Configure, Confirm, Restored }

    private static void InspectEditor(Part part, PlanarMilling operation, BoundaryTarget[] targets,
        PlanarCurveOptions options, PlanarBoundaryPlane plane, EditorPass pass, StringBuilder report)
    {
        PlanarMillingBuilder builder = part.CAMSetup.CAMOperationCollection.CreatePlanarMillingBuilder(operation);
        try
        {
            bool commit = false;
            foreach (BoundaryTarget target in targets)
            {
                BoundaryPlanarMill collection = target.Kind == CamGeomType.CamPart ? builder.PartBoundary : builder.BlankBoundary;
                if (collection == null)
                {
                    if (target.Enabled || target.Editor != null)
                        throw new InvalidOperationException("Редактор NX не предоставляет " + target.Name + " для операции " + operation.Name + ".");
                    // Some templates omit an unselected editor. Its saved UF records still have to match.
                    continue;
                }
                BoundarySet[] rows = collection.BoundaryList.GetContents();
                if (report != null && target.Enabled && pass == EditorPass.Snapshot)
                    report.AppendLine(target.Name + ": строк в редакторе NX до добавления: " + rows.Length + ".");
                bool changed;
                bool newRows = pass == EditorPass.Configure || pass == EditorPass.Confirm;
                List<string> originalRows = SeparatePlanarBoundary.CheckEditor(rows, target.Original.Sources,
                    newRows ? target.Requested : null, options, pass == EditorPass.Configure, plane,
                    pass == EditorPass.Confirm ? target.Confirmed : null, out changed);
                if (pass == EditorPass.Snapshot) target.Editor = originalRows;
                else if (target.Editor == null || !SeparatePlanarBoundary.SameKeys(target.Editor, originalRows))
                    throw new InvalidOperationException(target.Name + ": состав или параметры прежних заполненных границ изменились.");
                commit |= changed;
            }
            if (commit)
            {
                builder.Commit();
                if (report != null) report.AppendLine("Параметры новых границ применены в редакторе NX.");
            }
        }
        finally { builder.Destroy(); }
    }

    public static int GetUnloadOption(string unused) { return (int)Session.LibraryUnloadOption.Immediately; }
}

// BEGIN SEPARATE PLANAR BOUNDARY ADAPTER
public sealed class PlanarBoundaryPlane
{
    private readonly Part part;
    private readonly Point3d origin;
    private readonly Vector3d normal;
    private readonly double[] rotation = new double[9];

    private PlanarBoundaryPlane(Part part, UFSession uf, Point3d point, Vector3d direction)
    {
        this.part = part;
        origin = point;
        if (!Finite(point.X) || !Finite(point.Y) || !Finite(point.Z) || !TryUnit(direction, out normal))
            throw new InvalidOperationException("NX не вернул корректную точку и нормаль выбранной плоскости.");
        uf.Mtx3.InitializeZ(new double[] { normal.X, normal.Y, normal.Z }, rotation);
    }

    public static PlanarBoundaryPlane Choose(Session session, Part part, UFSession uf, string title, StringBuilder report, PlanarCurveOptions options)
    {
        // The constructor can create temporary NX planes and expressions. Keep only the solved ABS geometry.
        Session.UndoMarkId mark = session.SetUndoMark(Session.MarkVisibility.Invisible, "Построение плоскости границ");
        try
        {
            using (BlockUI.SnapBlockDialog dialog = UI.GetUI().CreateSnapDialog(title))
            {
                dialog.AddItem("Specify Plane", "boundaryPlane");
                dialog.AddItem("Label", "offsetHint");
                double mmPerUnit = part.PartUnits == BasePart.Units.Millimeters ? 1.0 : part.PartUnits == BasePart.Units.Inches ? 25.4 : 0.0;
                bool canRepeat = options.LastMethod == "Distance" && options.OffsetMm.HasValue && mmPerUnit > 0;
                double previous = canRepeat ? options.OffsetMm.Value / mmPerUnit : 0.0;
                string previousText = previous.ToString("G15", CultureInfo.CurrentCulture) + (mmPerUnit == 25.4 ? " in" : " мм");
                if (canRepeat) dialog.AddItem("Button", "repeatOffset");
                BlockUI.SpecifyPlane block = null;
                BlockUI.UIBlock repeat = null, hint = null;
                bool updating = false;
                Exception repeatError = null;
                string confirmedMethod = null;
                double? confirmedMm = null;
                PlanarBoundaryPlane selected = null;
                Exception callbackError = null;
                dialog.AddInitializeHandler(delegate {
                    try
                    {
                        dialog.TopBlock.Label = title;
                        block = (BlockUI.SpecifyPlane)dialog.TopBlock.FindBlock("boundaryPlane");
                        block.LabelString = "Задайте плоскость границ";
                        block.ShowShortcuts = true;
                        block.StepStatusAsString = "Required";
                        block.BalloonTooltipText = PlanarCurveOptions.PlanePickPrompt;
                        hint = dialog.TopBlock.FindBlock("offsetHint");
                        hint.Label = PlanarCurveOptions.PlanePickPrompt + (String.IsNullOrEmpty(options.LastMethod) ? ""
                            : " Предыдущий способ: " + (options.LastMethod == "Distance" ? "At Distance" : options.LastMethod) + ".");
                        if (canRepeat)
                        {
                            repeat = dialog.TopBlock.FindBlock("repeatOffset");
                            repeat.Label = "Повторить последнее смещение " + previousText;
                            repeat.Enable = false;
                        }
                    }
                    catch (Exception ex) { callbackError = ex; }
                });
                if (canRepeat) dialog.AddUpdateHandler(delegate(BlockUI.UIBlock changed) {
                    if (updating || repeat == null || block == null) return 0;
                    try
                    {
                        updating = true;
                        TaggedObject[] picked = block.GetSelectedObjects();
                        Plane basis = picked != null && picked.Length == 1 ? picked[0] as Plane : null;
                        repeat.Enable = basis != null;
                        if (changed == block) repeatError = null;
                        if (changed != repeat || basis == null) return 0;
                        basis.Evaluate();
                        // Re-edit an existing distance from its support, so pressing Repeat twice never stacks offsets.
                        NXObject[] references = basis.Method == PlaneTypes.MethodType.Distance ? basis.GetGeometry() : new NXObject[] { basis };
                        if (references == null || references.Length == 0)
                            throw new InvalidOperationException("Выберите опору для смещения в модели.");
                        Plane preview = part.Planes.CreatePlane(basis.Origin, basis.Normal, SmartObject.UpdateOption.AfterModeling);
                        preview.SetMethod(PlaneTypes.MethodType.Distance);
                        preview.SetGeometry(references);
                        preview.SetExpression(previous.ToString("R", CultureInfo.InvariantCulture));
                        preview.Evaluate();
                        block.SetSelectedObjects(new TaggedObject[] { preview });
                        repeatError = null;
                        hint.Label = "At Distance: " + previousText + ". Проверьте плоскость в модели и нажмите OK.";
                    }
                    catch (Exception ex)
                    {
                        repeatError = ex;
                        hint.Label = "Не удалось повторить смещение. Выберите опору заново: " + ex.Message;
                    }
                    finally { updating = false; }
                    return 0;
                });
                dialog.AddOkHandler(delegate {
                    try
                    {
                        if (callbackError != null) return 0;
                        if (repeatError != null) throw new InvalidOperationException("Повтор смещения не подтверждён.", repeatError);
                        TaggedObject[] objects = block.GetSelectedObjects();
                        Plane result = objects != null && objects.Length == 1 ? objects[0] as Plane : null;
                        if (result == null) throw new InvalidOperationException("Конструктор Plane не вернул построенную плоскость.");
                        result.Evaluate();
                        selected = new PlanarBoundaryPlane(part, uf, result.Origin, result.Normal);
                        PlaneTypes.MethodType method = result.Method;
                        confirmedMethod = method.ToString();
                        report.AppendLine("Способ построения плоскости NX: " + (method == PlaneTypes.MethodType.Distance ? "At Distance" : method.ToString()) + ".");
                        if (method == PlaneTypes.MethodType.Distance)
                        {
                            try
                            {
                                Expression distance = result.Expression;
                                if (distance != null)
                                {
                                    report.AppendLine("Выражение смещения в NX: " + distance.RightHandSide
                                        + ". Обратное направление смещения: " + (result.OffsetFlip ? "да." : "нет."));
                                    double signed = distance.GetValueUsingUnits(Expression.UnitsOption.Base) * (result.OffsetFlip ? -1.0 : 1.0) * mmPerUnit;
                                    if (mmPerUnit > 0 && Finite(signed)) confirmedMm = signed;
                                }
                            }
                            catch (NXException ex) { report.AppendLine("Не удалось прочитать выражение смещения: " + ex.Message); }
                        }
                    }
                    catch (Exception ex) { callbackError = ex; }
                    return 0;
                });
                BlockUI.BlockDialog.DialogResponse answer = dialog.LaunchInDialogMode(BlockUI.BlockDialog.DialogMode.Edit);
                if (callbackError != null) throw new InvalidOperationException("Не удалось прочитать плоскость из конструктора NX.", callbackError);
                if (answer == BlockUI.BlockDialog.DialogResponse.Cancel || answer == BlockUI.BlockDialog.DialogResponse.Back) return null;
                if (answer != BlockUI.BlockDialog.DialogResponse.Ok || selected == null)
                    throw new InvalidOperationException("NX не подтвердил построение плоскости: " + answer + ".");
                options.LastMethod = confirmedMethod;
                options.OffsetMm = confirmedMm;
                if (options.PreferencePath != null)
                {
                    options.Save(options.PreferencePath);
                    if (!String.IsNullOrEmpty(options.PreferenceNotice)) report.AppendLine(options.PreferenceNotice);
                }
                return selected;
            }
        }
        finally
        {
            session.UndoToMark(mark, null);
            session.DeleteUndoMark(mark, null);
        }
    }

    public void SetDefinition(ref UFCambnd.BoundaryData data)
    {
        data.plane_type = 0; // UF_CAMBND user defined
        data.origin = new double[] { origin.X, origin.Y, origin.Z };
        data.matrix = (double[])rotation.Clone();
    }

    private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }

    private static bool TryUnit(Vector3d direction, out Vector3d unit)
    {
        double length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
        unit = new Vector3d();
        if (!Finite(length) || length <= 0) return false;
        unit = new Vector3d(direction.X / length, direction.Y / length, direction.Z / length);
        return true;
    }

    private bool Matches(Point3d point, Vector3d direction)
    {
        Vector3d unit;
        if (!TryUnit(direction, out unit)) return false;
        // Multiple origins and in-plane X axes describe the same plane. Its normal sense matters for TANTO.
        double distance = (point.X - origin.X) * normal.X + (point.Y - origin.Y) * normal.Y + (point.Z - origin.Z) * normal.Z;
        double dx = unit.X - normal.X, dy = unit.Y - normal.Y, dz = unit.Z - normal.Z;
        return Math.Abs(distance) <= 1e-6 && dx * dx + dy * dy + dz * dz <= 1e-12;
    }

    private static string Describe(Point3d point, Vector3d direction)
    {
        return String.Format(CultureInfo.InvariantCulture, "точка ({0:G12}, {1:G12}, {2:G12}), нормаль ({3:G12}, {4:G12}, {5:G12})",
            new object[] { point.X, point.Y, point.Z, direction.X, direction.Y, direction.Z });
    }
    public string Description { get { return Describe(origin, normal); } }

    public bool ApplyOrVerify(BoundarySet row, Tag source, bool configure)
    {
        BoundarySet.PlaneTypes mode = row.PlaneType;
        Plane actual = mode == BoundarySet.PlaneTypes.UserDefined ? row.Plane : null;
        if (actual != null && Matches(actual.Origin, actual.Normal)) return false;
        if (!configure)
            throw new InvalidOperationException("Кривая [" + source + "]: ожидалась плоскость " + Description
                + ". Редактор NX вернул " + mode + (actual == null ? "" : ", " + Describe(actual.Origin, actual.Normal)) + ".");
        row.PlaneType = BoundarySet.PlaneTypes.UserDefined;
        Plane replacement = part.Planes.CreatePlane(origin, normal, SmartObject.UpdateOption.AfterModeling);
        try { row.Plane = replacement; }
        catch { replacement.DestroyPlane(); throw; }
        return true;
    }
}

public static class SeparatePlanarBoundary
{
    public sealed class Entry
    {
        internal UFCambnd.BoundaryData Definition;
        internal UFCambnd.AppData Settings;
        internal Tag[] Tags;
        internal UFCambnd.AppData[] Members;
        internal string Key;
    }

    public sealed class Saved
    {
        internal bool Inherited;
        internal readonly List<Entry> Boundaries = new List<Entry>();
        internal readonly HashSet<Tag> Sources = new HashSet<Tag>();
        internal List<string> Keys()
        {
            List<string> keys = new List<string>();
            foreach (Entry entry in Boundaries) keys.Add(entry.Key);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
        internal bool SameAs(Saved other)
        { return Inherited == other.Inherited && SameKeys(Keys(), other.Keys()); }
    }

    internal static bool SameKeys(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // UF creates one permanent CAM boundary per call on the selected operation.
    // Boundary-level app_data is optional. Planar member tool position uses the UF enum.
    public static void Append(UFCambnd api, Tag operation, CamGeomType kind, Tag curve, bool closed, PlanarCurveOptions options, PlanarBoundaryPlane plane)
    {
        UFCambnd.BoundaryData definition = new UFCambnd.BoundaryData {
            boundary_type = closed ? CamBoundaryType.CamBoundaryTypeClosed : CamBoundaryType.CamBoundaryTypeOpen,
            plane_type = 1, origin = new double[3], matrix = new double[] {1,0,0, 0,1,0, 0,0,1},
            material_side = options.UsesOutsideOrRight(closed) ? CamMaterialSide.CamMaterialSideOutRight : CamMaterialSide.CamMaterialSideInLeft
        };
        if (plane != null) plane.SetDefinition(ref definition);
        UFCambnd.AppData member = new UFCambnd.AppData {
            tolerances = new double[2], has_tool_position = 1,
            tool_position = options.Tanto ? CamToolPosition.CamToolPositionTanto : CamToolPosition.CamToolPositionOn
        };
        api.AppendBndFromCurve(operation, kind, 1, new Tag[] { curve }, ref definition, new UFCambnd.AppData[] { member });
    }

    public static Saved Read(UFCambnd api, Tag operation, CamGeomType kind)
    {
        Saved result = new Saved();
        api.IsInherited(operation, kind, out result.Inherited);
        int count; IntPtr[] boundaries;
        api.AskBoundaries(operation, kind, out count, out boundaries);
        CheckHandles(count, boundaries, "границ");
        for (int i = 0; i < count; i++)
        {
            Entry entry = new Entry();
            api.AskBoundaryData(boundaries[i], out entry.Definition);
            api.AskBoundaryAppData(boundaries[i], out entry.Settings);
            int size; IntPtr[] members;
            api.AskBoundaryItems(boundaries[i], out size, out members);
            CheckHandles(size, members, "элементов границы");
            entry.Tags = new Tag[size]; entry.Members = new UFCambnd.AppData[size];
            StringBuilder key = new StringBuilder();
            key.Append(entry.Definition.boundary_type).Append('|').Append(entry.Definition.material_side)
                .Append('|').Append(entry.Definition.plane_type);
            if (entry.Definition.plane_type == 0)
            {
                AppendNumbers(key, entry.Definition.origin, 3);
                AppendNumbers(key, entry.Definition.matrix, 9);
            }
            key.Append('|').Append(entry.Definition.ignore_holes).Append('|').Append(entry.Definition.ignore_islands)
                .Append('|').Append(entry.Definition.ignore_chamfers);
            AppendSettings(key, entry.Settings);
            for (int j = 0; j < size; j++)
            {
                api.AskItemEntity(members[j], out entry.Tags[j]);
                api.AskItemAppData(members[j], out entry.Members[j]);
                if (entry.Tags[j] != Tag.Null) result.Sources.Add(entry.Tags[j]);
                key.Append(';').Append(entry.Tags[j]);
                AppendSettings(key, entry.Members[j]);
            }
            entry.Key = key.ToString();
            result.Boundaries.Add(entry);
        }
        return result;
    }

    private static void CheckHandles(int count, IntPtr[] values, string what)
    {
        if (count == 0) return;
        if (count < 0 || values == null || values.Length != count)
            throw new InvalidOperationException("NX вернул неполный список " + what + " (заявлено " + count + ").");
        for (int i = 0; i < count; i++) if (values[i] == IntPtr.Zero)
            throw new InvalidOperationException("Пустая ссылка в списке " + what + " на позиции " + (i + 1) + ".");
    }

    private static void AppendNumbers(StringBuilder key, double[] values, int size)
    {
        if (values == null || values.Length != size) throw new InvalidOperationException("NX вернул неполные параметры границы.");
        foreach (double value in values) key.Append('|').Append(value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void AppendSettings(StringBuilder key, UFCambnd.AppData data)
    {
        // Inactive fields are not overrides. Their numeric defaults may be recomputed by NX.
        key.Append('|').Append(data.has_stock).Append('|').Append(data.has_tolerances)
            .Append('|').Append(data.has_feedrate).Append('|').Append(data.has_blank_distance).Append('|').Append(data.has_tool_position);
        if (data.has_stock != 0) key.Append('|').Append(data.stock.ToString("R", CultureInfo.InvariantCulture));
        if (data.has_tolerances != 0) AppendNumbers(key, data.tolerances, 2);
        if (data.has_feedrate != 0) key.Append('|').Append(data.feedrate_unit).Append('|').Append(data.feedrate_value.ToString("R", CultureInfo.InvariantCulture));
        if (data.has_blank_distance != 0) key.Append('|').Append(data.blank_distance.ToString("R", CultureInfo.InvariantCulture));
        if (data.has_tool_position != 0) key.Append('|').Append(data.tool_position);
    }

    public static void VerifySaved(Saved original, Saved actual, Dictionary<Tag, bool> requested, bool tanto,
        Dictionary<Tag, BoundarySet.BoundaryTypes> confirmedTypes, StringBuilder report, string name)
    {
        Dictionary<string, int> old = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Entry entry in original.Boundaries)
        {
            int count; old.TryGetValue(entry.Key, out count); old[entry.Key] = count + 1;
        }
        HashSet<Tag> found = new HashSet<Tag>();
        List<string> problems = new List<string>();
        Dictionary<int, int> unknownCodes = new Dictionary<int, int>();
        for (int i = 0; i < actual.Boundaries.Count; i++)
        {
            Entry b = actual.Boundaries[i];
            int copies;
            if (old.TryGetValue(b.Key, out copies))
            {
                if (copies == 1) old.Remove(b.Key); else old[b.Key] = copies - 1;
                continue;
            }
            bool closed;
            if (b.Tags.Length != 1 || !requested.TryGetValue(b.Tags[0], out closed))
            {
                problems.Add("Граница №" + (i + 1) + ": элементов " + b.Tags.Length + ", теги [" + string.Join(", ", b.Tags) + "].");
                continue;
            }
            Tag tag = b.Tags[0];
            if (!found.Add(tag)) problems.Add("Кривая [" + tag + "] назначена повторно.");
            CamBoundaryType raw = b.Definition.boundary_type;
            bool named = raw == CamBoundaryType.CamBoundaryTypeClosed || raw == CamBoundaryType.CamBoundaryTypeOpen;
            if (!named && report != null)
            {
                int count;
                unknownCodes.TryGetValue((int)raw, out count);
                unknownCodes[(int)raw] = count + 1;
            }
            if (confirmedTypes != null)
            {
                BoundarySet.BoundaryTypes expectedType = closed ? BoundarySet.BoundaryTypes.Closed : BoundarySet.BoundaryTypes.Open;
                BoundarySet.BoundaryTypes editorType;
                if (!confirmedTypes.TryGetValue(tag, out editorType) || editorType != expectedType)
                    problems.Add("Кривая [" + tag + "]: редактор NX не подтвердил " + expectedType + ".");
                else if (named && (raw == CamBoundaryType.CamBoundaryTypeClosed) != closed)
                    problems.Add("Кривая [" + tag + "]: редактор NX подтвердил " + editorType + ", CAM вернул " + raw + ".");
                UFCambnd.AppData member = b.Members[0];
                if (member.has_tool_position != 0 && member.tool_position != (tanto ? CamToolPosition.CamToolPositionTanto : CamToolPosition.CamToolPositionOn))
                    problems.Add("Кривая [" + tag + "]: в CAM сохранено положение " + member.tool_position + ".");
                if (b.Settings.has_stock != 0 || b.Settings.has_tolerances != 0 || member.has_stock != 0 || member.has_tolerances != 0)
                    problems.Add("Кривая [" + tag + "]: в CAM есть индивидуальный припуск или допуск.");
            }
        }
        if (report != null && unknownCodes.Count > 0)
        {
            List<int> codes = new List<int>(unknownCodes.Keys);
            codes.Sort();
            foreach (int code in codes)
                report.AppendLine("CAM вернул код типа " + code + " для границ: " + unknownCodes[code] + ". Тип Open/Closed проверяется в редакторе NX.");
        }
        int lost = 0; foreach (int count in old.Values) lost += count;
        if (lost > 0) problems.Add("Не найдены прежние границы с исходными параметрами: " + lost + ".");
        List<Tag> missing = new List<Tag>();
        foreach (Tag tag in requested.Keys) if (!found.Contains(tag)) missing.Add(tag);
        if (missing.Count > 0) problems.Add("Нет отдельных границ для тегов [" + string.Join(", ", missing) + "].");
        if (actual.Inherited) problems.Add("Операция по-прежнему наследует " + name + ".");
        int expected = original.Boundaries.Count + requested.Count;
        if (problems.Count > 0 || actual.Boundaries.Count != expected)
            throw new InvalidOperationException(name + ": исходно " + original.Boundaries.Count + ", новых " + requested.Count
                + ", ожидалось " + expected + ", фактически " + actual.Boundaries.Count + "." + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    // The editor may include an empty input row. All permanent entries, even genuine empty ones,
    // are protected separately by the UF snapshot. Here match the geometry-backed rows by source tag.
    public static List<string> CheckEditor(BoundarySet[] rows, HashSet<Tag> oldSources, Dictionary<Tag, bool> requested,
        PlanarCurveOptions options, bool configure, PlanarBoundaryPlane plane, Dictionary<Tag, BoundarySet.BoundaryTypes> confirmedTypes, out bool changed)
    {
        changed = false;
        List<string> previous = new List<string>();
        HashSet<Tag> found = new HashSet<Tag>();
        foreach (BoundarySet row in rows)
        {
            BoundaryMemberSet[] members = row.BoundaryMemberList.GetContents();
            Tag selected = Tag.Null; bool existing = false;
            foreach (BoundaryMemberSet member in members)
            {
                if (member == null || member.Geometry == null) continue;
                Tag tag = member.Geometry.Tag;
                if (oldSources.Contains(tag)) existing = true;
                if (requested != null && requested.ContainsKey(tag)) selected = tag;
            }
            if (selected == Tag.Null)
            {
                if (existing) previous.Add(EditorKey(row, members));
                continue;
            }
            if (members.Length != 1 || !found.Add(selected))
                throw new InvalidOperationException("Редактор NX вернул группу или повтор для кривой [" + selected + "], элементов " + members.Length + ".");
            BoundarySetPlanarMill set = row as BoundarySetPlanarMill;
            BoundaryMemberSetPlanarMill item = members[0] as BoundaryMemberSetPlanarMill;
            if (set == null || item == null) throw new InvalidOperationException("Неизвестный тип границы для кривой [" + selected + "].");
            if (plane != null && plane.ApplyOrVerify(set, selected, configure))
            {
                changed = true;
                members = set.BoundaryMemberList.GetContents();
                item = members.Length == 1 ? members[0] as BoundaryMemberSetPlanarMill : null;
                if (item == null || item.Geometry == null || item.Geometry.Tag != selected)
                    throw new InvalidOperationException("Редактор NX изменил состав границы при выборе плоскости для кривой [" + selected + "].");
            }
            bool closed = requested[selected], tanto = options.Tanto;
            BoundarySet.BoundaryTypes shape = closed ? BoundarySet.BoundaryTypes.Closed : BoundarySet.BoundaryTypes.Open;
            BoundarySet.ToolPositionTypes position = tanto ? BoundarySet.ToolPositionTypes.Contact : BoundarySet.ToolPositionTypes.On;
            BoundarySet.ToolSideTypes side = options.UsesOutsideOrRight(closed) ? BoundarySet.ToolSideTypes.OutsideOrRight : BoundarySet.ToolSideTypes.InsideOrLeft;
            int memberPosition = tanto ? 1 : 0; // NXOpen editor values differ from CamToolPosition in UF.
            BoundarySet.BoundaryTypes actualType = set.BoundaryType;
            if (actualType == shape && set.ToolPosition == position && (!tanto || set.ToolSide == side)
                && item.ToolPosition == memberPosition && !set.CustomStock && !set.CustomTolerance && !item.CustomStock && !item.CustomTolerance)
            {
                if (!configure && confirmedTypes != null) confirmedTypes.Add(selected, actualType);
                continue;
            }
            if (!configure)
                throw new InvalidOperationException("Кривая [" + selected + "]: ожидались " + shape + ", " + position
                    + (tanto ? ", " + side : "") + ", припуск и допуск из операции. В NX: " + actualType + ", " + set.ToolPosition
                    + ", " + set.ToolSide + ", положение элемента " + item.ToolPosition + ", свой припуск " + set.CustomStock + "/" + item.CustomStock
                    + ", свой допуск " + set.CustomTolerance + "/" + item.CustomTolerance + ".");
            if (actualType != shape) set.BoundaryType = shape;
            if (set.ToolPosition != position) set.ToolPosition = position;
            if (tanto && set.ToolSide != side) set.ToolSide = side;
            if (set.CustomStock) set.CustomStock = false;
            if (set.CustomTolerance) set.CustomTolerance = false;
            members = set.BoundaryMemberList.GetContents();
            item = members.Length == 1 ? members[0] as BoundaryMemberSetPlanarMill : null;
            if (item == null || item.Geometry == null || item.Geometry.Tag != selected)
                throw new InvalidOperationException("Редактор NX изменил состав границы при настройке кривой [" + selected + "].");
            if (item.ToolPosition != memberPosition) item.ToolPosition = memberPosition;
            if (item.CustomStock) item.CustomStock = false;
            if (item.CustomTolerance) item.CustomTolerance = false;
            changed = true;
        }
        if (requested != null && found.Count != requested.Count)
        {
            List<Tag> missing = new List<Tag>();
            foreach (Tag tag in requested.Keys) if (!found.Contains(tag)) missing.Add(tag);
            throw new InvalidOperationException("Редактор NX: строк " + rows.Length + ", отдельных новых границ найдено " + found.Count
                + " из " + requested.Count + ". Нет тегов [" + string.Join(", ", missing) + "].");
        }
        previous.Sort(StringComparer.Ordinal);
        return previous;
    }

    // Additional editor-only settings of pre-existing geometric rows, including custom offsets.
    private static string EditorKey(BoundarySet row, BoundaryMemberSet[] members)
    {
        StringBuilder key = new StringBuilder();
        key.Append(row.BoundaryType).Append('|').Append(row.ToolPosition).Append('|').Append(row.ToolSide)
            .Append('|').Append(row.PlaneType).Append('|').Append(row.CustomOffset);
        if (row.CustomOffset) key.Append('|').Append(row.Offset.ToString("R", CultureInfo.InvariantCulture));
        BoundarySetPlanarMill planar = row as BoundarySetPlanarMill;
        if (planar != null) key.Append('|').Append(planar.CustomStock).Append('|').Append(planar.CustomTolerance);
        foreach (BoundaryMemberSet member in members)
        {
            key.Append(';').Append(member == null || member.Geometry == null ? Tag.Null : member.Geometry.Tag);
            BoundaryMemberSetPlanarMill item = member as BoundaryMemberSetPlanarMill;
            if (item != null) key.Append('|').Append(item.ToolPosition).Append('|').Append(item.CustomStock).Append('|').Append(item.CustomTolerance);
        }
        return key.ToString();
    }
}
// END SEPARATE PLANAR BOUNDARY ADAPTER

// BEGIN PLANAR CURVE WINDOWS
public sealed class PlanarCurveOptions
{
    internal const string PlanePickPrompt = "Для смещения выберите At Distance в окне Plane и задайте расстояние.";
    public bool Part = true, Blank, SelectPlane = true;
    public string PreferenceNotice;
    internal string PreferencePath, LastMethod;
    internal double? OffsetMm;
    public bool Tanto, ClosedOutside, OpenRight;
    internal static PlanarCurveOptions Load(string path)
    {
        PlanarCurveOptions defaults = new PlanarCurveOptions();
        try
        {
            if (!File.Exists(path)) return defaults;
            string[] fields = File.ReadAllText(path).Trim().Split('|');
            int flags;
            if (!(fields.Length == 2 && fields[0] == "1" || fields.Length == 4 && fields[0] == "2")
                || !Int32.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out flags)
                || flags < 0 || flags > 63 || (flags & 3) == 0) return defaults;
            PlanarCurveOptions settings = new PlanarCurveOptions { Part = (flags & 1) != 0, Blank = (flags & 2) != 0, SelectPlane = (flags & 4) != 0,
                Tanto = (flags & 8) != 0, ClosedOutside = (flags & 16) != 0, OpenRight = (flags & 32) != 0 };
            if (fields.Length == 4 && fields[2].Length <= 48 && Array.TrueForAll(fields[2].ToCharArray(), Char.IsLetterOrDigit))
            {
                settings.LastMethod = fields[2];
                double mm;
                if (Double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out mm) && !Double.IsNaN(mm) && !Double.IsInfinity(mm))
                    settings.OffsetMm = mm;
            }
            return settings;
        }
        catch (Exception ex) { defaults.PreferenceNotice = "Не удалось прочитать последние настройки: " + ex.Message; return defaults; }
    }

    internal void Save(string path)
    {
        string temp = null;
        try
        {
            int flags = (Part ? 1 : 0) | (Blank ? 2 : 0) | (SelectPlane ? 4 : 0) | (Tanto ? 8 : 0) | (ClosedOutside ? 16 : 0) | (OpenRight ? 32 : 0);
            string value = "2|" + flags.ToString(CultureInfo.InvariantCulture) + "|" + LastMethod + "|"
                + (OffsetMm.HasValue ? OffsetMm.Value.ToString("R", CultureInfo.InvariantCulture) : "");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, value, new UTF8Encoding(false));
            if (!MoveFileExW(temp, path, 9)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            temp = null;
        }
        catch (Exception ex) { PreferenceNotice = "Не удалось сохранить последние настройки: " + ex.Message; }
        finally { if (temp != null) try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool MoveFileExW(string source, string target, uint flags);

    public bool UsesOutsideOrRight(bool closed) { return Tanto && (closed ? ClosedOutside : OpenRight); }
    public string ModeLabel { get { return Tanto ? "TANTO" : "ON"; } }
    public string ClosedSideLabel { get { return ClosedOutside ? "Outside — снаружи" : "Inside — внутри"; } }
    public string OpenSideLabel { get { return OpenRight ? "Right — справа" : "Left — слева"; } }
}

// These dialogs use the same in-memory Win32 dialog mechanism as the project's PDF
// exporter. The NX journal compiler only needs the base runtime and NXOpen.
public static class PlanarCurveWindows
{
    public static PlanarCurveOptions AskOptions(IntPtr owner, string title, string operation)
    {
        string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NXOpen-Scripts", "PlanarAddCurves.settings");
        return AskOptions(owner, title, operation, path);
    }

    internal static PlanarCurveOptions AskOptions(IntPtr owner, string title, string operation, string path)
    {
        Dialog dialog = new Dialog(title, "Кривые в PLANAR", operation, null, 0);
        dialog.Options = PlanarCurveOptions.Load(path);
        dialog.Options.PreferencePath = path;
        if (dialog.Show(owner) != OK) return null;
        dialog.Options.Save(path);
        return dialog.Options;
    }

    public static void ShowReport(IntPtr owner, string title, string headline, string report, int severity)
    {
        new Dialog(title, headline, null, report, severity).Show(owner);
    }

    private const int OK = 1, Cancel = 2;
    private const int Heading = 100, Summary = 101, On = 110, Tanto = 111;
    private const int Inside = 121, Outside = 122, Left = 124, Right = 125, Note = 127;
    private const int ReportEdit = 130, Copy = 131;
    private const uint TabStop = 0x00010000, Group = 0x00020000;
    private const uint ButtonCheckBox = 2;
    private const uint StaticNoPrefix = 0x80;
    private const uint WmSetFont = 0x30, BmSetCheck = 0xf1;
    private const int PlaneChoice = 128;

    private static int Rgb(int r, int g, int b) { return r | (g << 8) | (b << 16); }
    private static readonly int Canvas = Rgb(245, 247, 248), Ink = Rgb(31, 43, 49);
    private static readonly int Accent = Rgb(24, 116, 89), Muted = Rgb(93, 109, 115);

    private sealed class Dialog
    {
        internal PlanarCurveOptions Options;
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
            byte[] template = Template(title, IsReport ? 398 : 360, IsReport ? 250 : 260, IsReport);
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
                        Native.SetFocus(Item(IsReport ? OK : PartChoice));
                        return IntPtr.Zero; // Keep the explicitly assigned keyboard focus.
                    case 0x0f: case 0x318: // WM_PAINT, WM_PRINTCLIENT
                        if (SettingsWindow && ready) { Paint(hwnd, message, wParam, 0); return new IntPtr(1); }
                        break;
                    case 0x201: // drag the borderless header
                        if (SettingsWindow && ((short)(lParam.ToInt64() >> 16)) < Px(76))
                        {
                            Native.ReleaseCapture();
                            Native.SendMessageW(hwnd, 0xa1, new IntPtr(2), IntPtr.Zero);
                            return new IntPtr(1);
                        }
                        break;
                    case 0x02e0: // WM_DPICHANGED
                        if (SettingsWindow && ready)
                        {
                            Native.Rect suggested=(Native.Rect)Marshal.PtrToStructure(lParam,typeof(Native.Rect));
                            FitWindow(Native.MonitorFromRect(ref suggested,2),suggested,(uint)(wParam.ToInt64() & 0xffff));
                            IntPtr[] previousFonts=fonts.ToArray(); fonts.Clear(); SetPanelFonts();
                            foreach (int id in new int[]{PartChoice,BlankChoice,PlaneChoice,On,Tanto,Inside,Outside,Left,Right,CloseChoice,Cancel,OK,Note})
                                Native.SendMessageW(Item(id),WmSetFont,id==Note?fontSmall:fontBody,IntPtr.Zero);
                            foreach (IntPtr font in previousFonts) Native.DeleteObject(font);
                            RefreshStart(); return new IntPtr(1);
                        }
                        break;
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
                        if (ready && IsReport) { SetMinimumSize(lParam); return new IntPtr(1); }
                        break;
                    case 0x136: // WM_CTLCOLORDLG
                        return SettingsWindow ? Brush(wParam, Panel, White) : Brush(wParam, Canvas, Ink);
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
            if (SettingsWindow) { BuildStart(); return; }
            IntPtr normal = Native.SendMessageW(window, 0x31, IntPtr.Zero, IntPtr.Zero);
            Add("STATIC", Heading, headline, StaticNoPrefix, CreateFont(18, 700, "Segoe UI", normal));
            Add("STATIC", Summary, "Результат и проверка границ", StaticNoPrefix, normal);
            Add("EDIT", ReportEdit, "", TabStop | 0x00b008c4u, CreateFont(10, 400, "Consolas", normal));
            Native.SendMessageW(Item(ReportEdit), 0xc5, new IntPtr(Int32.MaxValue - 2), IntPtr.Zero);
            if (!Native.SetWindowTextW(Item(ReportEdit), report))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Не удалось показать текст отчёта.");
            Add("BUTTON", Copy, "Копировать всё", TabStop, normal);
            Add("BUTTON", OK, "Закрыть", TabStop | 1u, normal);
        }

        private static readonly int[] SideButtons = { Inside, Outside, Left, Right };
        private const int PartChoice = 140, BlankChoice = 141, CloseChoice = 142;
        private const int PanelWidth = 692, PanelHeight = 592;
        private static readonly int Shell = Rgb(34,37,41), Panel = Rgb(43,46,50), Tile = Rgb(48,52,56), Edge = Rgb(69,74,78);
        private static readonly int LineColor = Rgb(57,62,66), White = Rgb(243,244,243), Sub = Rgb(195,200,199), Quiet = Rgb(173,181,180);
        private static readonly int Mint = Rgb(0,205,134), MintInk = Rgb(7,46,34), Selected = Rgb(38,60,52), SelectedEdge = Rgb(61,128,101);
        private double scale = 1;
        private IntPtr fontTitle, fontHeading, fontStrong, fontBody, fontSmall, fontMono;
        private Native.SubclassProc buttonProc;
        private int hover;
        private bool SettingsWindow { get { return !IsReport; } }

        private int Px(int v) { return (int)Math.Round(v * scale); }
        private void PlacePx(int id, int x, int y, int w, int h)
        {
            Native.SetWindowPos(Item(id), IntPtr.Zero, Px(x), Px(y), Math.Max(1, Px(x + w) - Px(x)), Math.Max(1, Px(y + h) - Px(y)), 0x14);
        }
        private void FitWindow(IntPtr monitor, Native.Rect anchor, uint dpi)
        {
            Native.MonitorInfo info = new Native.MonitorInfo();
            info.Size = Marshal.SizeOf(typeof(Native.MonitorInfo));
            if (!Native.GetMonitorInfoW(monitor, ref info))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Native.Rect area = info.Work;
            scale = Math.Min((dpi == 0 ? 96 : dpi) / 96.0, Math.Min((area.Right - area.Left - 8.0) / PanelWidth, (area.Bottom - area.Top - 8.0) / PanelHeight));
            int w = Px(PanelWidth), h = Px(PanelHeight);
            int x = Math.Max(area.Left, Math.Min(area.Right - w, (anchor.Left + anchor.Right - w) / 2));
            int y = Math.Max(area.Top, Math.Min(area.Bottom - h, (anchor.Top + anchor.Bottom - h) / 2));
            Native.SetWindowPos(window, IntPtr.Zero, x, y, w, h, 0x14);
            IntPtr region = Native.CreateRoundRectRgn(0, 0, w + 1, h + 1, Px(42), Px(42));
            if (region != IntPtr.Zero && Native.SetWindowRgn(window, region, true) == 0)
                Native.DeleteObject(region);
        }
        private IntPtr PixelFont(string face, int pixels, int weight)
        {
            IntPtr f = Native.CreateFontW(-Px(pixels), 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, face);
            if (f == IntPtr.Zero)
                return Native.GetStockObject(17);
            fonts.Add(f);
            return f;
        }
        private void SetPanelFonts()
        {
            fontTitle = PixelFont("Segoe UI", 24, 650);
            fontHeading = PixelFont("Segoe UI", 16, 650);
            fontStrong = PixelFont("Segoe UI", 14, 600);
            fontBody = PixelFont("Segoe UI", 13, 400);
            fontSmall = PixelFont("Segoe UI", 12, 400);
            fontMono = PixelFont("Consolas", 13, 600);
        }
        private void AddChoice(int id, string caption, uint style)
        {
            Add("BUTTON", id, caption, style, fontBody);
            if (!Native.SetWindowSubclass(Item(id), buttonProc, new UIntPtr((uint)id), UIntPtr.Zero))
                throw new InvalidOperationException("Не удалось оформить элемент окна: " + caption);
        }
        private void BuildStart()
        {
            IntPtr owner = Native.GetWindow(window, 4);
            Native.Rect anchor;
            if (owner == IntPtr.Zero || !Native.GetWindowRect(owner, out anchor))
                Native.GetWindowRect(window, out anchor);
            FitWindow(Native.MonitorFromWindow(owner != IntPtr.Zero ? owner : window, 2), anchor, Native.GetDpiForWindow(window));
            SetPanelFonts();
            buttonProc = HandleButton;
            AddChoice(PartChoice, "Part Boundaries — Границы детали", Group | TabStop | ButtonCheckBox);
            AddChoice(BlankChoice, "Blank Boundaries — Границы заготовки", TabStop | ButtonCheckBox);
            AddChoice(PlaneChoice, "Выбрать плоскость границ", TabStop | ButtonCheckBox);
            Add("STATIC", Note, "", StaticNoPrefix, fontSmall);
            AddChoice(On, "ON — Центр на границе", Group | TabStop | 9);
            AddChoice(Tanto, "TANTO — Касание границы", 9);
            AddChoice(Inside, "Внутри (Inside)", Group | TabStop | 9);
            AddChoice(Outside, "Снаружи (Outside)", 9);
            AddChoice(Left, "Слева (Left)", Group | TabStop | 9);
            AddChoice(Right, "Справа (Right)", 9);
            AddChoice(Cancel, "Отмена", Group | TabStop);
            AddChoice(OK, "", TabStop | 1);
            AddChoice(CloseChoice, "Закрыть", TabStop);
            RefreshStart();
        }
        private void LayoutStart()
        {
            PlacePx(PartChoice, 22, 157, 319, 60);
            PlacePx(BlankChoice, 351, 157, 319, 60);
            PlacePx(PlaneChoice, 22, 226, 342, 23);
            PlacePx(Note, 51, 251, 616, 22);
            PlacePx(On, 22, 322, 319, 60);
            PlacePx(Tanto, 351, 322, 319, 60);
            PlacePx(Inside, 22, 418, 156, 36);
            PlacePx(Outside, 184, 418, 157, 36);
            PlacePx(Left, 351, 418, 156, 36);
            PlacePx(Right, 513, 418, 157, 36);
            PlacePx(CloseChoice, 641, 17, 30, 30);
            int primaryWidth = Options.SelectPlane ? 175 : 215;
            PlacePx(Cancel, PanelWidth - 22 - primaryWidth - 83, 532, 75, 38);
            PlacePx(OK, PanelWidth - 22 - primaryWidth, 532, primaryWidth, 38);
        }
        private void RefreshStart()
        {
            int[] ids =
            {
                PartChoice,
                BlankChoice,
                PlaneChoice,
                On,
                Tanto,
                Inside,
                Outside,
                Left,
                Right
            };
            bool[] state =
            {
                Options.Part,
                Options.Blank,
                Options.SelectPlane,
                !Options.Tanto,
                Options.Tanto,
                !Options.ClosedOutside,
                Options.ClosedOutside,
                !Options.OpenRight,
                Options.OpenRight
            };
            for (int i = 0; i < ids.Length; i++)
                Native.SendMessageW(Item(ids[i]), BmSetCheck, new IntPtr(state[i] ? 1 : 0), IntPtr.Zero);
            foreach (int id in SideButtons)
                Native.EnableWindow(Item(id), Options.Tanto);
            Native.EnableWindow(Item(OK), Options.Part || Options.Blank);
            Native.SetWindowTextW(Item(OK), NextLabel());
            Native.SetWindowTextW(Item(Note), Options.SelectPlane ? PlanarCurveOptions.PlanePickPrompt : "NX определит плоскость каждой новой границы автоматически.");
            LayoutStart();
            Native.RedrawWindow(window, IntPtr.Zero, IntPtr.Zero, 0x85); // invalidate, erase and include children
        }
        private string NextLabel()
        {
            return Options.SelectPlane ? "Выбрать плоскость" : Options.Part ? "Выбрать кривые детали" : Options.Blank ? "Выбрать кривые заготовки" : "Выбрать кривые";
        }
        private static bool IsSide(int id)
        {
            return id == Inside || id == Outside || id == Left || id == Right;
        }
        private void Box(IntPtr dc, int x, int y, int w, int h, int radius, int fill, int border)
        {
            Native.SetDCBrushColor(dc, fill);
            Native.SetDCPenColor(dc, border);
            if (radius == 0)
                Native.Rectangle(dc, Px(x), Px(y), Px(x + w), Px(y + h));
            else
                Native.RoundRect(dc, Px(x), Px(y), Px(x + w), Px(y + h), Px(2 * radius), Px(2 * radius));
        }
        private void Stroke(IntPtr dc, int x, int y, int xx, int yy, int color)
        {
            Native.SetDCPenColor(dc, color);
            Native.MoveToEx(dc, Px(x), Px(y), IntPtr.Zero);
            Native.LineTo(dc, Px(xx), Px(yy));
        }
        private void Circle(IntPtr dc, int x, int y, int d, int fill, int edge)
        {
            Native.SetDCPenColor(dc, edge);
            Native.SetDCBrushColor(dc, fill);
            Native.Ellipse(dc, Px(x), Px(y), Px(x + d), Px(y + d));
        }
        private void TextLine(IntPtr dc, string text, int x, int y, int w, int h, IntPtr font, int color, uint align)
        {
            Native.Rect rect = new Native.Rect
            {
                Left = Px(x),
                Top = Px(y),
                Right = Px(x + w),
                Bottom = Px(y + h)
            };
            Native.SelectObject(dc, font);
            Native.SetTextColor(dc, color);
            Native.DrawTextW(dc, text, -1, ref rect, 0x8824u | align); // single line, centered vertically, ellipsis, no prefix
        }
        private static void InitPaint(IntPtr dc)
        {
            Native.SelectObject(dc, Native.GetStockObject(18));
            Native.SelectObject(dc, Native.GetStockObject(19));
            Native.SetBkMode(dc, 1);
        }
        private void DrawSurface(IntPtr dc)
        {
            int saved = Native.SaveDC(dc);
            try
            {
                InitPaint(dc);
                Box(dc, 0, 0, PanelWidth, PanelHeight, 0, Panel, Panel);
                Box(dc, 0, 0, PanelWidth, 76, 0, Shell, Shell);
                Box(dc, 0, 76, PanelWidth, 37, 0, Rgb(38, 41, 44), LineColor);
                Box(dc, 0, 511, PanelWidth, 82, 0, Shell, LineColor);
                Box(dc, 22, 17, 42, 42, 12, Rgb(25, 60, 48), Rgb(49, 102, 79));
                int c = Rgb(120, 228, 178);
                for (int i = 0; i < 3; i++)
                {
                    int y = 29 + i * 5;
                    Stroke(dc, 30, y, 43, y + 7, c);
                    Stroke(dc, 43, y + 7, 56, y, c);
                }

                Stroke(dc, 30, 29, 43, 22, c);
                Stroke(dc, 43, 22, 56, 29, c);
                TextLine(dc, "Кривые в PLANAR", 78, 12, 510, 31, fontTitle, White, 0);
                int split = title.IndexOf(" — ", StringComparison.Ordinal);
                TextLine(dc, split >= 0 ? title.Substring(split + 3) : title, 79, 43, 515, 20, fontSmall, Quiet, 0);
                TextLine(dc, "Операция", 22, 82, 63, 24, fontSmall, Quiet, 0);
                TextLine(dc, operation, 89, 82, 325, 24, fontMono, White, 0);
                TextLine(dc, "PLANAR_MILL / PLANAR_PROFILE", 419, 82, 251, 24, fontSmall, Quiet, 2);
                TextLine(dc, "Границы операции", 22, 122, 345, 27, fontHeading, White, 0);
                TextLine(dc, "Можно выбрать оба набора", 374, 122, 296, 27, fontSmall, Quiet, 2);
                Stroke(dc, 22, 281, 670, 281, LineColor);
                TextLine(dc, "Положение инструмента", 22, 290, 475, 25, fontHeading, White, 0);
                TextLine(dc, "Замкнутые кривые", 22, 389, 318, 24, fontStrong, Sub, 0);
                TextLine(dc, "Открытые кривые", 351, 389, 318, 24, fontStrong, Sub, 0);
                TextLine(dc, Options.Tanto ? "Слева и справа — по направлению открытой кривой." : "Выбор стороны доступен в режиме TANTO.", 22, 463, 648, 26, fontSmall, Quiet, 0);
                bool any = Options.Part || Options.Blank;
                TextLine(dc, any ? "Порядок выбора" : "Отметьте хотя бы один набор границ.", 22, 527, 310, 21, fontSmall, Quiet, 0);
                if (any)
                {
                    List<string> order = new List<string>();
                    if (Options.SelectPlane)
                        order.Add("Плоскость");
                    if (Options.Part)
                        order.Add("Деталь");
                    if (Options.Blank)
                        order.Add("Заготовка");
                    TextLine(dc, String.Join("   ›   ", order.ToArray()), 22, 547, 310, 24, fontBody, White, 0);
                }
            }
            finally
            {
                Native.RestoreDC(dc, saved);
            }
        }
        private void Tick(IntPtr dc, int x, int y, int color)
        {
            Stroke(dc, x, y + 4, x + 4, y + 8, color);
            Stroke(dc, x + 4, y + 8, x + 11, y, color);
            Stroke(dc, x, y + 5, x + 4, y + 9, color);
            Stroke(dc, x + 4, y + 9, x + 11, y + 1, color);
        }
        private void CheckBox(IntPtr dc, int x, int y, bool selected, int bg)
        {
            Box(dc, x, y, 18, 18, 4, selected ? Mint : bg, selected ? Mint : Quiet);
            if (selected)
                Tick(dc, x + 3, y + 4, MintInk);
        }
        private void DrawButton(IntPtr hwnd, IntPtr dc, int id)
        {
            Native.Rect rect;
            Native.GetClientRect(hwnd, out rect);
            int w = (int)Math.Round(rect.Right / scale), h = (int)Math.Round(rect.Bottom / scale);
            int saved = Native.SaveDC(dc);
            try
            {
                InitPaint(dc);
                bool enabled = Native.IsWindowEnabled(hwnd), selected = Native.SendMessageW(hwnd, 0xf0, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero;
                int state = Native.SendMessageW(hwnd, 0xf2, IntPtr.Zero, IntPtr.Zero).ToInt32();
                bool hot = enabled && (hover == id || (state & 4) != 0);
                int bg = id == OK || id == Cancel || id == CloseChoice ? Shell : Panel;
                Box(dc, 0, 0, w + 1, h + 1, 0, bg, bg);
                bool plain = id == PlaneChoice || id == CloseChoice;
                int fill = !enabled ? Rgb(41, 44, 48) : selected ? Selected : Tile;
                int border = !enabled ? LineColor : selected ? SelectedEdge : Edge;
                if (id == OK && enabled)
                {
                    fill = Mint;
                    border = Mint;
                }

                if (plain)
                {
                    fill = hot && id == CloseChoice ? Tile : bg;
                    border = fill;
                }

                if (hot && !plain)
                {
                    border = id == OK ? Rgb(82, 239, 171) : Rgb(97, 152, 127);
                    if (id == OK)
                        fill = Rgb(39, 220, 150);
                }

                Box(dc, 0, 0, w, h, IsSide(id) ? 9 : id == CloseChoice ? 8 : 12, fill, border);
                int ink = enabled ? White : Rgb(151, 159, 157);
                if (id == PartChoice || id == BlankChoice)
                {
                    CheckBox(dc, 14, (h - 18) / 2, selected, fill);
                    TextLine(dc, id == PartChoice ? "Part Boundaries" : "Blank Boundaries", 44, 9, w - 52, 22, fontStrong, ink, 0);
                    TextLine(dc, id == PartChoice ? "Границы детали" : "Границы заготовки", 44, 30, w - 52, 21, fontSmall, Sub, 0);
                }
                else if (id == PlaneChoice)
                {
                    CheckBox(dc, 0, (h - 18) / 2, selected, fill);
                    TextLine(dc, "Выбрать плоскость границ", 28, 0, w - 30, h, fontStrong, ink, 0);
                }
                else if (id == On || id == Tanto)
                {
                    int glyph = selected ? Rgb(111, 232, 179) : Sub, square = selected ? Rgb(28, 50, 40) : Rgb(40, 44, 46);
                    Box(dc, 12, 11, 39, 38, 8, square, square);
                    int cy = id == On ? 30 : 25, line = id == On ? 30 : 34;
                    Circle(dc, 23, cy - 9, 19, square, glyph);
                    for (int i = 14; i < 49; i += 6)
                        Stroke(dc, i, line, i + 3, line, glyph);
                    Circle(dc, 31, cy - 1, 3, glyph, glyph);
                    TextLine(dc, id == On ? "ON" : "TANTO", 63, 7, w - 95, 24, fontHeading, ink, 0);
                    TextLine(dc, id == On ? "Центр на границе" : "Касание границы", 63, 30, w - 95, 20, fontSmall, Sub, 0);
                    Circle(dc, w - 27, 23, 14, fill, selected ? Mint : Quiet);
                    if (selected)
                        Circle(dc, w - 23, 27, 6, Mint, Mint);
                }
                else if (IsSide(id))
                {
                    string caption = id == Inside ? "Внутри  Inside" : id == Outside ? "Снаружи  Outside" : id == Left ? "Слева  Left" : "Справа  Right";
                    if (selected)
                        Tick(dc, 10, (h - 10) / 2, enabled ? Mint : Quiet);
                    TextLine(dc, caption, selected ? 19 : 4, 0, w - (selected ? 23 : 8), h, fontBody, ink, 1);
                }
                else if (id == CloseChoice)
                {
                    Stroke(dc, 10, 10, 20, 20, Quiet);
                    Stroke(dc, 20, 10, 10, 20, Quiet);
                }
                else
                {
                    TextLine(dc, id == OK ? NextLabel() : "Отмена", 3, 0, w - (id == OK ? 24 : 6), h, fontStrong, id == OK && enabled ? MintInk : ink, 1);
                    if (id == OK)
                    {
                        int x = w - 22, y = h / 2;
                        Stroke(dc, x - 2, y, x + 10, y, enabled ? MintInk : ink);
                        Stroke(dc, x + 5, y - 5, x + 10, y, enabled ? MintInk : ink);
                        Stroke(dc, x + 5, y + 5, x + 10, y, enabled ? MintInk : ink);
                    }
                }

                if ((state & 8) != 0 && (Native.SendMessageW(hwnd, 0x129, IntPtr.Zero, IntPtr.Zero).ToInt32() & 1) == 0)
                {
                    Native.Rect focus = new Native.Rect
                    {
                        Left = Px(4),
                        Top = Px(3),
                        Right = rect.Right - Px(4),
                        Bottom = rect.Bottom - Px(3)
                    };
                    Native.DrawFocusRect(dc, ref focus);
                }
            }
            finally
            {
                Native.RestoreDC(dc, saved);
            }
        }
        private IntPtr HandleButton(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr sub, UIntPtr data)
        {
            try
            {
                int id = (int)sub.ToUInt32();
                if (message == 0x0f || message == 0x318)
                    return Paint(hwnd, message, wParam, id);
                if (message == 0x14)
                    return new IntPtr(1);
                if (message == 0x200 && hover != id)
                {
                    hover = id;
                    Native.TrackMouse track = new Native.TrackMouse
                    {
                        Size = Marshal.SizeOf(typeof(Native.TrackMouse)),
                        Flags = 2,
                        Window = hwnd
                    };
                    Native.TrackMouseEvent(ref track);
                    Native.InvalidateRect(hwnd, IntPtr.Zero, false);
                }

                if (message == 0x2a3 && hover == id)
                {
                    hover = 0;
                    Native.InvalidateRect(hwnd, IntPtr.Zero, false);
                }

                if (message == 0x82)
                    Native.RemoveWindowSubclass(hwnd, buttonProc, sub);
                return Native.DefSubclassProc(hwnd, message, wParam, lParam);
            }
            catch (Exception ex)
            {
                if (callbackError == null)
                    callbackError = ex;
                Native.EndDialog(window, new IntPtr(Cancel));
                return IntPtr.Zero;
            }
        }
        private IntPtr Paint(IntPtr hwnd, uint message, IntPtr supplied, int id)
        {
            Native.PaintInfo paint = new Native.PaintInfo();
            IntPtr dc = message == 0x0f ? Native.BeginPaint(hwnd, out paint) : supplied;
            try
            {
                if (dc != IntPtr.Zero)
                {
                    if (id == 0)
                        DrawSurface(dc);
                    else
                        DrawButton(hwnd, dc, id);
                }
            }
            finally
            {
                if (message == 0x0f)
                    Native.EndPaint(hwnd, ref paint);
            }

            return IntPtr.Zero;
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

        private bool Command(int id, int notification)
        {
            if (notification != 0) return false; // BN_CLICKED / STN_CLICKED or a dialog key
            switch (id)
            {
                case PartChoice: Options.Part = !Options.Part; break;
                case BlankChoice: Options.Blank = !Options.Blank; break;
                case PlaneChoice: Options.SelectPlane = !Options.SelectPlane; break;
                case On: case Tanto: Options.Tanto = id == Tanto; break;
                case Inside: case Outside: if (Options.Tanto) Options.ClosedOutside = id == Outside; break;
                case Left: case Right: if (Options.Tanto) Options.OpenRight = id == Right; break;
                case Cancel: case CloseChoice: Native.EndDialog(window, new IntPtr(Cancel)); return true;
                case OK:
                    if (IsReport || Options.Part || Options.Blank) Native.EndDialog(window, new IntPtr(OK));
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
                default: return false;
            }
            RefreshStart();
            return true;
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
            if (SettingsWindow) return Brush(dc, Panel, Quiet);
            if (id == ReportEdit) return Brush(dc, 0xffffff, Ink);
            int color = id != Heading ? Muted : severity == 2 ? Rgb(162,54,49) : severity == 1 ? Rgb(136,96,28) : Accent;
            return Brush(dc, Canvas, color);
        }

        private void Layout()
        {
            if (SettingsWindow) { LayoutStart(); return; }
            Native.Rect client;
            if (!Native.GetClientRect(window, out client) || client.Right < 1 || client.Bottom < 1) return;
            Native.Rect units = Units(4, 8);
            int width = client.Right * 4 / Math.Max(1, units.Right);
            int height = client.Bottom * 8 / Math.Max(1, units.Bottom);
            const int pad = 12, gap = 7, buttonHeight = 19;
            int usable = width - 2 * pad, buttonsY = height - pad - buttonHeight;
            Place(Heading, pad, 8, usable, 21); Place(Summary, pad, 32, usable, 13);
            Place(ReportEdit, pad, 49, usable, buttonsY - 56);
            Place(OK, width - pad - 66, buttonsY, 66, buttonHeight);
            Place(Copy, width - pad - 66 - gap - 88, buttonsY, 88, buttonHeight);
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
            Native.Rect min = Units(275, 136);
            Native.MinMaxInfo limits = (Native.MinMaxInfo)Marshal.PtrToStructure(data, typeof(Native.MinMaxInfo));
            limits.MinTrack.X = Math.Max(limits.MinTrack.X, min.Right + outer.Right - outer.Left - client.Right);
            limits.MinTrack.Y = Math.Max(limits.MinTrack.Y, min.Bottom + outer.Bottom - outer.Top - client.Bottom);
            Marshal.StructureToPtr(limits, data, false);
        }
    }

    // DLGTEMPLATE with the default dialog class, a Unicode caption and a DPI-scaled dialog font.
    private static byte[] Template(string title, int width, int height, bool report)
    {
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode))
        {
            // WS_POPUP | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MAXIMIZEBOX | WS_CLIPCHILDREN
            // DS_MODALFRAME | DS_SETFONT | DS_CENTER; WS_EX_COMPOSITED buffers child updates during resize.
            writer.Write(report ? 0x82cd08c0u : 0x82000840u); writer.Write(0x02000000u); writer.Write((ushort)0);
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
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate IntPtr SubclassProc(IntPtr hwnd,uint msg,IntPtr w,IntPtr l,UIntPtr id,UIntPtr data);
        [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { internal int Size; internal Rect Monitor, Work; internal uint Flags; }
        [StructLayout(LayoutKind.Sequential)] internal struct TrackMouse { internal int Size; internal uint Flags; internal IntPtr Window; internal uint HoverTime; }
        [StructLayout(LayoutKind.Sequential)] internal struct PaintInfo { internal IntPtr Dc; internal int Erase; internal Rect Paint; internal int Restore,Update; internal uint Reserved1,Reserved2,Reserved3,Reserved4,Reserved5,Reserved6,Reserved7,Reserved8; }
        [DllImport("comctl32.dll")] internal static extern bool SetWindowSubclass(IntPtr hwnd,SubclassProc proc,UIntPtr id,UIntPtr data);
        [DllImport("comctl32.dll")] internal static extern bool RemoveWindowSubclass(IntPtr hwnd,SubclassProc proc,UIntPtr id);
        [DllImport("comctl32.dll")] internal static extern IntPtr DefSubclassProc(IntPtr hwnd,uint msg,IntPtr w,IntPtr l);
        [DllImport("user32.dll")] internal static extern bool TrackMouseEvent(ref TrackMouse track);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd,uint cmd);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
        [DllImport("user32.dll",ExactSpelling=true,SetLastError=true)] internal static extern bool GetMonitorInfoW(IntPtr monitor,ref MonitorInfo info);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateRoundRectRgn(int x,int y,int right,int bottom,int width,int height);
        [DllImport("user32.dll")] internal static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
        [DllImport("user32.dll")] internal static extern bool RedrawWindow(IntPtr hwnd,IntPtr rect,IntPtr region,uint flags);
        [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")] internal static extern IntPtr BeginPaint(IntPtr hwnd,out PaintInfo paint);
        [DllImport("user32.dll")] internal static extern bool EndPaint(IntPtr hwnd,ref PaintInfo paint);
        [DllImport("user32.dll")] internal static extern bool DrawFocusRect(IntPtr dc,ref Rect rect);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] internal static extern int DrawTextW(IntPtr dc,string text,int length,ref Rect rect,uint flags);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern int SaveDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern bool RestoreDC(IntPtr dc,int saved);
        [DllImport("gdi32.dll")] internal static extern int SetBkMode(IntPtr dc,int mode);
        [DllImport("gdi32.dll")] internal static extern int SetDCPenColor(IntPtr dc,int color);
        [DllImport("gdi32.dll")] internal static extern bool Ellipse(IntPtr dc,int left,int top,int right,int bottom);
        [DllImport("gdi32.dll")] internal static extern bool RoundRect(IntPtr dc,int left,int top,int right,int bottom,int width,int height);
        [DllImport("gdi32.dll")] internal static extern bool Rectangle(IntPtr dc,int left,int top,int right,int bottom);
        [DllImport("gdi32.dll")] internal static extern bool MoveToEx(IntPtr dc,int x,int y,IntPtr old);
        [DllImport("gdi32.dll")] internal static extern bool LineTo(IntPtr dc,int x,int y);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromRect(ref Rect rect,uint flags);
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
        [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] internal static extern bool SetWindowTextW(IntPtr hwnd, string text);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
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
