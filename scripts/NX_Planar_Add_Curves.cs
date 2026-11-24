// NX_Planar_Add_Curves.cs - separate curve boundaries for PLANAR_MILL / PLANAR_PROFILE.
// SCRIPT_VERSION: V1.12
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
    private const string SCRIPT_VERSION = "V1.12";
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
        try
        {
            int flags = (Part ? 1 : 0) | (Blank ? 2 : 0) | (SelectPlane ? 4 : 0) | (Tanto ? 8 : 0) | (ClosedOutside ? 16 : 0) | (OpenRight ? 32 : 0);
            string value = "2|" + flags.ToString(CultureInfo.InvariantCulture) + "|" + LastMethod + "|" + (OffsetMm.HasValue ? OffsetMm.Value.ToString("R", CultureInfo.InvariantCulture) : "");
            WriteAtomic(path, value);
        }
        catch (Exception ex)
        {
            PreferenceNotice = "Не удалось сохранить последние настройки: " + ex.Message;
        }
    }

    internal static void WriteAtomic(string path, string value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, value, new UTF8Encoding(false));
            if (!MoveFileExW(temp, path, 9))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
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
        return AskOptions(owner, title, operation, PreferencePath());
    }
    private static string PreferencePath()
    {
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NXOpen-Scripts", "PlanarAddCurves.settings");
    }

    internal static PlanarCurveOptions AskOptions(IntPtr owner, string title, string operation, string path)
    {
        Dialog dialog = new Dialog(title, "Кривые в PLANAR", operation, null, 0);
        dialog.Options = PlanarCurveOptions.Load(path);
        dialog.Options.PreferencePath = path;
        dialog.LoadAppearance(path);
        if (dialog.Show(owner) != OK)
            return null;
        dialog.Options.Save(path);
        return dialog.Options;
    }

    public static void ShowReport(IntPtr owner, string title, string headline, string report, int severity)
    {
        ShowReport(owner, title, headline, report, severity, PreferencePath());
    }

    internal static void ShowReport(IntPtr owner, string title, string headline, string report, int severity, string path)
    {
        Dialog dialog = new Dialog(title, headline, null, report, severity);
        dialog.LoadAppearance(path);
        dialog.Show(owner);
    }

    private const int OK = 1, Cancel = 2;
    private const int Heading = 100, Summary = 101, On = 110, Tanto = 111;
    private const int Inside = 121, Outside = 122, Left = 124, Right = 125, Note = 127;
    private const int ReportEdit = 130, Copy = 131;
    private const uint TabStop = 0x00010000, Group = 0x00020000;
    private const uint OwnerDraw = 0x0b;
    private const uint StaticNoPrefix = 0x80;
    private const uint WmSetFont = 0x30;
    private const int PlaneChoice = 128;

    private static int Rgb(int r, int g, int b) { return r | (g << 8) | (b << 16); }
    private static int Mix(int first, int second, double amount)
    {
        int result = 0;
        for (int shift = 0; shift <= 16; shift += 8)
        {
            int a = (first >> shift) & 255, b = (second >> shift) & 255;
            result |= (int)Math.Round(a + (b - a) * amount) << shift;
        }

        return result;
    }

    private sealed class Appearance
    {
        internal bool Light;
        internal double Hue, Saturation, Value;
        internal Appearance()
        {
            SetHex("#00CD86", false);
        }
        internal Appearance Copy()
        {
            return (Appearance)MemberwiseClone();
        }
        internal int Color { get { return FromHsv(Hue, Saturation, Value); } }
        internal string Hex { get { return "#" + Reverse(Color).ToString("X6", CultureInfo.InvariantCulture); } }
        internal static int Reverse(int c)
        {
            return ((c & 255) << 16) | (c & 65280) | ((c >> 16) & 255);
        }
        internal static int FromHsv(double h, double s, double v)
        {
            double sector = (h % 360) / 60, c = v * s, x = c * (1 - Math.Abs(sector % 2 - 1));
            double r = 0, g = 0, b = 0, m = v - c;
            switch ((int)sector)
            {
                case 0:
                    r = c;
                    g = x;
                    break;
                case 1:
                    r = x;
                    g = c;
                    break;
                case 2:
                    g = c;
                    b = x;
                    break;
                case 3:
                    g = x;
                    b = c;
                    break;
                case 4:
                    r = x;
                    b = c;
                    break;
                default:
                    r = c;
                    b = x;
                    break;
            }

            return Rgb((int)Math.Round(255 * (r + m)), (int)Math.Round(255 * (g + m)), (int)Math.Round(255 * (b + m)));
        }
        internal bool SetHex(string input, bool allowShort)
        {
            string digits = input.Trim();
            if (digits.StartsWith("#", StringComparison.Ordinal))
                digits = digits.Substring(1);
            if (allowShort && digits.Length == 3)
                digits = new string (new char[] { digits[0], digits[0], digits[1], digits[1], digits[2], digits[2] });
            int rgb;
            if (digits.Length != 6 || !Int32.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb))
                return false;
            double r = ((rgb >> 16) & 255) / 255.0, g = ((rgb >> 8) & 255) / 255.0, b = (rgb & 255) / 255.0;
            Value = Math.Max(r, Math.Max(g, b));
            double delta = Value - Math.Min(r, Math.Min(g, b));
            if (Value != 0)
                Saturation = delta / Value;
            if (delta != 0)
                Hue = ((Value == r ? (g - b) / delta : Value == g ? 2 + (b - r) / delta : 4 + (r - g) / delta) * 60 + 360) % 360;
            return true;
        }
        private static double Channel(int c)
        {
            double n = (c & 255) / 255.0;
            return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4);
        }
        internal static double Luminance(int c)
        {
            return .2126 * Channel(c) + .7152 * Channel(c >> 8) + .0722 * Channel(c >> 16);
        }
        internal static double Contrast(double a, double b)
        {
            return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
        }
        internal int Indicator(int background)
        {
            int accent = Color, end = Light ? 0 : 0xffffff;
            double against = Luminance(background);
            for (int i = 0; i <= 20; ++i)
            {
                int c = Mix(accent, end, i / 20.0);
                if (Contrast(Luminance(c), against) >= 3)
                    return c;
            }

            return end;
        }
        internal void Save(string path)
        {
            PlanarCurveOptions.WriteAtomic(path, String.Format(CultureInfo.InvariantCulture, "1|{0}|{1}|{2:R}|{3:R}|{4:R}", new object[] { Light ? "light" : "dark", Hex, Hue, Saturation, Value }));
        }
        internal static Appearance Read(string path)
        {
            if (!File.Exists(path))
                return null;
            string[] p = File.ReadAllText(path).Trim().Split('|');
            if (p.Length != 6 || p[0] != "1" || (p[1] != "dark" && p[1] != "light"))
                return null;
            Appearance a = new Appearance();
            if (!a.SetHex(p[2], false))
                return null;
            a.Light = p[1] == "light";
            double h, s, v;
            if (Number(p[3], 360, out h) && Number(p[4], 1, out s) && Number(p[5], 1, out v) && FromHsv(h, s, v) == a.Color)
            {
                a.Hue = h;
                a.Saturation = s;
                a.Value = v;
            }

            return a;
        }
        private static bool Number(string text, double max, out double n)
        {
            return Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && n >= 0 && n <= max;
        }
    }
    // Use Windows' native antialiased vector painter; the journal needs no additional managed references.
    private sealed class VectorInk : IDisposable
    {
        private IntPtr graphics, pen, brush;
        internal static void Check(int status)
        {
            if (status != 0)
                throw new InvalidOperationException("Ошибка отрисовки окна Windows (" + status + ").");
        }
        internal VectorInk(IntPtr dc, double scale)
        {
            try
            {
                Check(Native.GdipCreateFromHDC(dc, out graphics));
                Check(Native.GdipSetPageUnit(graphics, 2)); // UnitPixel
                Check(Native.GdipSetSmoothingMode(graphics, 4));
                Check(Native.GdipScaleWorldTransform(graphics, (float)scale, (float)scale, 0));
                Check(Native.GdipCreatePen1(-1, 1, 0, out pen));
                Check(Native.GdipSetPenLineJoin(pen, 2));
                Check(Native.GdipSetPenStartCap(pen, 2));
                Check(Native.GdipSetPenEndCap(pen, 2));
                Check(Native.GdipCreateSolidFill(-1, out brush));
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        public void Dispose()
        {
            if (brush != IntPtr.Zero)
            {
                Native.GdipDeleteBrush(brush);
                brush = IntPtr.Zero;
            }

            if (pen != IntPtr.Zero)
            {
                Native.GdipDeletePen(pen);
                pen = IntPtr.Zero;
            }

            if (graphics != IntPtr.Zero)
            {
                Native.GdipDeleteGraphics(graphics);
                graphics = IntPtr.Zero;
            }
        }
        private static int Argb(int color)
        {
            return unchecked((int)0xff000000) | Appearance.Reverse(color);
        }
        private void Pen(int color, float width)
        {
            Check(Native.GdipSetPenColor(pen, Argb(color)));
            Check(Native.GdipSetPenWidth(pen, width));
        }
        internal void Line(float x, float y, float endX, float endY, int color, float width)
        {
            Pen(color, width);
            Check(Native.GdipDrawLine(graphics, pen, x, y, endX, endY));
        }
        internal void Outline(float[] xy, int color, float width)
        {
            Pen(color, width);
            Check(Native.GdipDrawPolygon(graphics, pen, xy, xy.Length / 2));
        }
        internal void Round(float x, float y, float w, float h, float radius, int fill, int edge)
        {
            x += .5f;
            y += .5f;
            w -= 1;
            h -= 1;
            float d = Math.Min(2 * radius, Math.Min(w, h));
            if (d <= 0)
                return;
            IntPtr path;
            Check(Native.GdipCreatePath(0, out path));
            try
            {
                Check(Native.GdipAddPathArc(path, x, y, d, d, 180, 90));
                Check(Native.GdipAddPathArc(path, x + w - d, y, d, d, 270, 90));
                Check(Native.GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0, 90));
                Check(Native.GdipAddPathArc(path, x, y + h - d, d, d, 90, 90));
                Check(Native.GdipClosePathFigure(path));
                if (fill >= 0)
                {
                    Check(Native.GdipSetSolidFillColor(brush, Argb(fill)));
                    Check(Native.GdipFillPath(graphics, brush, path));
                }

                Pen(edge, 1);
                Check(Native.GdipDrawPath(graphics, pen, path));
            }
            finally
            {
                Native.GdipDeletePath(path);
            }
        }
        internal void Ellipse(float x, float y, float diameter, int fill, int edge, float width)
        {
            float inset = width / 2, d = diameter - width;
            if (fill >= 0)
            {
                Check(Native.GdipSetSolidFillColor(brush, Argb(fill)));
                Check(Native.GdipFillEllipse(graphics, brush, x + inset, y + inset, d, d));
            }

            Pen(edge, width);
            Check(Native.GdipDrawEllipse(graphics, pen, x + inset, y + inset, d, d));
        }
    }

    // A Win32 region has binary coverage. Let the normal HWND handle controls and hit testing,
    // and use a click-through ARGB rim for the partly covered edge pixels, including on Windows 10.
    private sealed class RoundedFrame : IDisposable
    {
        private IntPtr rim;
        private int width,height,radius,edge=-1;
        internal void Move(IntPtr owner)
        {
            Native.Rect b;
            if (rim != IntPtr.Zero && Native.GetWindowRect(owner, out b))
                Native.SetWindowPos(rim, IntPtr.Zero, b.Left, b.Top, 0, 0, 0x215);
        }
        internal void Show(bool visible)
        {
            if (rim != IntPtr.Zero)
                Native.ShowWindow(rim, visible ? 8 : 0);
        }
        public void Dispose()
        {
            if (rim != IntPtr.Zero)
            {
                Native.DestroyWindow(rim);
                rim = IntPtr.Zero;
            }

            width = height = radius = 0;
            edge = -1;
        }
        internal void Fit(IntPtr owner, int w, int h, int r, int colour)
        {
            if (w < 2 * r || h < 2 * r || r < 1)
                return;
            if (width == w && height == h && radius == r && colour == edge)
            {
                Move(owner);
                return;
            }

            Native.Rect b;
            if (!Native.GetWindowRect(owner, out b))
                return;
            if (rim == IntPtr.Zero)
                rim = Native.CreateWindowExW(0x080800a0u, "STATIC", "", 0x80000000u, b.Left, b.Top, w, h, owner, IntPtr.Zero, Native.GetModuleHandleW(null), IntPtr.Zero);
            IntPtr region = IntPtr.Zero;
            bool smooth = rim != IntPtr.Zero && Rasterize(w, h, r, colour, b.Left, b.Top, out region);
            if (!smooth)
            {
                Dispose();
                region = Native.CreateRoundRectRgn(0, 0, w + 1, h + 1, r * 2, r * 2);
            }

            if (region != IntPtr.Zero && Native.SetWindowRgn(owner, region, true) == 0)
                Native.DeleteObject(region);
            width = w;
            height = h;
            radius = r;
            edge = colour;
            Show(Native.IsWindowVisible(owner));
        }
        private static int Pixel(int colour, int coverage)
        {
            int a = (coverage * 255 + 8) / 16;
            return unchecked(a << 24) | (((((colour & 255) * a + 127) / 255)) << 16) | (((((colour >> 8) & 255) * a + 127) / 255) << 8) | ((((colour >> 16) & 255) * a + 127) / 255);
        }
        private static int Corner(int x, int y, int r, int colour, bool backed)
        {
            int covered = 0;
            for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                {
                    double xx = x + (sx + .5) / 4, yy = y + (sy + .5) / 4;
                    double dx = r - xx, dy = r - yy, d = dx * dx + dy * dy;
                    if (d <= r * r && (!backed || xx < 1 || yy < 1 || d > (r - 1) * (r - 1)))
                        covered++;
                }

            return Pixel(colour, covered);
        }
        private bool Rasterize(int w, int h, int r, int colour, int left, int top, out IntPtr shape)
        {
            shape = IntPtr.Zero;
            Native.BitmapInfo info = Native.BitmapInfo.TopDown(w, h);
            IntPtr dc = Native.CreateCompatibleDC(IntPtr.Zero), bits, bitmap = Native.CreateDIBSection(dc, ref info, 0, out bits, IntPtr.Zero, 0);
            IntPtr prev = IntPtr.Zero, clip = IntPtr.Zero, row = IntPtr.Zero;
            try
            {
                if (dc == IntPtr.Zero || bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                    return false;
                prev = Native.SelectObject(dc, bitmap);
                clip = Native.CreateRectRgn(0, r, w, h - r);
                row = Native.CreateRectRgn(0, 0, 0, 0);
                if (clip == IntPtr.Zero || row == IntPtr.Zero)
                    return false;
                int[] pixels = new int[checked(w * h)];
                int solid = Pixel(colour, 16);
                for (int x = r; x < w - r; x++)
                {
                    pixels[x] = solid;
                    pixels[(h - 1) * w + x] = solid;
                }

                for (int y = r; y < h - r; y++)
                {
                    pixels[y * w] = solid;
                    pixels[y * w + w - 1] = solid;
                }

                for (int y = 0; y < r; y++)
                {
                    int inset = (int)Math.Ceiling(r - Math.Sqrt(r * (double)r - (r - y) * (double)(r - y)));
                    if (!Native.SetRectRgn(row, inset, y, w - inset, y + 1) || Native.CombineRgn(clip, clip, row, 2) == 0 || !Native.SetRectRgn(row, inset, h - 1 - y, w - inset, h - y) || Native.CombineRgn(clip, clip, row, 2) == 0)
                        return false;
                    for (int x = 0; x < r; x++)
                    {
                        int pixel = Corner(x, y, r, colour, x >= inset);
                        pixels[y * w + x] = pixels[y * w + w - 1 - x] = pixels[(h - 1 - y) * w + x] = pixels[(h - 1 - y) * w + w - 1 - x] = pixel;
                    }
                }

                Marshal.Copy(pixels, 0, bits, pixels.Length);
                Native.Point dst = new Native.Point
                {
                    X = left,
                    Y = top
                }, size = new Native.Point
                {
                    X = w,
                    Y = h
                }, src = new Native.Point();
                Native.Blend blend = new Native.Blend
                {
                    Alpha = 255,
                    Format = 1
                };
                if (!Native.UpdateLayeredWindow(rim, IntPtr.Zero, ref dst, ref size, dc, ref src, 0, ref blend, 2))
                    return false;
                shape = clip;
                clip = IntPtr.Zero;
                return true;
            }
            finally
            {
                if (row != IntPtr.Zero)
                    Native.DeleteObject(row);
                if (clip != IntPtr.Zero)
                    Native.DeleteObject(clip);
                if (prev != IntPtr.Zero)
                    Native.SelectObject(dc, prev);
                if (bitmap != IntPtr.Zero)
                    Native.DeleteObject(bitmap);
                if (dc != IntPtr.Zero)
                    Native.DeleteDC(dc);
            }
        }
    }

    // IDWriteTextLayout calls this standard Windows interface after shaping the text.
    // Pixel snapping and glyph rendering share physical-pixel coordinates.
    [StructLayout(LayoutKind.Sequential)]
    public struct LabelTransform
    {
        public float XX, XY, YX, YY, X, Y;
    }

    [ComVisible(true), Guid("EF8A8135-5CC6-45FE-8825-C5A0724EB819"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ILabelRenderer
    {
        [PreserveSig]
        int IsPixelSnappingDisabled(IntPtr context, out int disabled);
        [PreserveSig]
        int GetCurrentTransform(IntPtr context, out LabelTransform matrix);
        [PreserveSig]
        int GetPixelsPerDip(IntPtr context, out float pixelsPerDip);
        [PreserveSig]
        int DrawGlyphRun(IntPtr context, float x, float y, int measuring, IntPtr run, IntPtr description, IntPtr effect);
        [PreserveSig]
        int DrawUnderline(IntPtr context, float x, float y, IntPtr underline, IntPtr effect);
        [PreserveSig]
        int DrawStrikethrough(IntPtr context, float x, float y, IntPtr strike, IntPtr effect);
        [PreserveSig]
        int DrawInlineObject(IntPtr context, float x, float y, IntPtr item, int sideways, int rtl, IntPtr effect);
    }

    // The journal compiler has no DirectWrite wrapper. Bind the small subset of the
    // dwrite.h COM interfaces used here, retaining native ownership explicitly.
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class LabelPainter : ILabelRenderer, IDisposable
    {
        private struct LayoutKey : IEquatable<LayoutKey>
        {
            internal IntPtr Font;
            internal string Text;
            internal int Width, Height;
            internal uint Alignment;
            public bool Equals(LayoutKey other)
            {
                return Font == other.Font && Text == other.Text && Width == other.Width &&
                    Height == other.Height && Alignment == other.Alignment;
            }

            public override bool Equals(object value)
            {
                return value is LayoutKey && Equals((LayoutKey)value);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Font.GetHashCode() * 31 + Text.GetHashCode();
                    return ((hash * 31 + Width) * 31 + Height) * 31 + (int)Alignment;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Trimming
        {
            internal int Granularity, Delimiter, Count;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LogFont
        {
            internal int Height, Width, Escapement, Orientation, Weight;
            internal byte Italic, Underline, Strike, Charset, OutputPrecision, ClipPrecision, Quality, Pitch;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            internal string Family;
        }

        [DllImport("dwrite.dll", ExactSpelling = true)]
        private static extern int DWriteCreateFactory(int kind, ref Guid iid, out IntPtr factory);
        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern int GetObjectW(IntPtr handle, int size, out LogFont font);
        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")]
        private static extern bool GdiFlush();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetInterface(IntPtr self, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int NewTarget(IntPtr self, IntPtr dc, uint w, uint h, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr GetDc(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ResizeTarget(IntPtr self, uint w, uint h);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetScale(IntPtr self, float scale);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int NewParams(IntPtr self, float gamma, float contrast, float clearType, int geometry, int mode, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate int NewFormat(IntPtr self, string family, IntPtr collection, int weight, int style,
            int stretch, float size, string locale, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate int NewLayout(IntPtr self, string text, int length, IntPtr format, float width, float height,
            float pixelsPerDip, IntPtr transform, int useNatural, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetValue(IntPtr self, int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int NewEllipsis(IntPtr self, IntPtr format, out IntPtr sign);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetTrimming(IntPtr self, ref Trimming trimming, IntPtr sign);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int PaintLayout(IntPtr self, IntPtr context, IntPtr renderer, float x, float y);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int PaintRun(IntPtr self, float x, float y, int measuring, IntPtr run, IntPtr parameters, int colour, IntPtr bounds);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int PaintInline(IntPtr self, IntPtr context, IntPtr renderer, float x, float y, int sideways, int rtl, IntPtr effect);

        private readonly Dictionary<IntPtr, IntPtr> formats = new Dictionary<IntPtr, IntPtr>();
        private readonly Dictionary<LayoutKey, IntPtr> layouts = new Dictionary<LayoutKey, IntPtr>();
        private IntPtr factory, target, parameters, renderer, memory;
        private NewLayout createLayout;
        private PaintLayout paintLayout;
        private PaintRun paintRun;
        private int bufferWidth, bufferHeight, ink;
        private bool unavailable;
        private const uint SrcCopy = 0x00cc0020;

        private static T Method<T>(IntPtr instance, int slot)
            where T : class
        {
            IntPtr address = Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

        private static void Release(ref IntPtr value)
        {
            if (value != IntPtr.Zero)
                Marshal.Release(value);
            value = IntPtr.Zero;
        }

        private bool Initialize(IntPtr dc)
        {
            if (unavailable || renderer != IntPtr.Zero)
                return !unavailable;
            IntPtr gdiInterop = IntPtr.Zero;
            try
            {
                Guid iid = new Guid("B859EE5A-D838-4B5B-A2E8-1ADC7D93DB48");
                if (DWriteCreateFactory(0, ref iid, out factory) < 0 ||
                    Method<GetInterface>(factory, 17)(factory, out gdiInterop) < 0 || // GetGdiInterop
                    Method<NewTarget>(gdiInterop, 7)(gdiInterop, dc, 1, 1, out target) < 0 || // CreateBitmapRenderTarget
                    Method<SetScale>(target, 6)(target, 1) < 0) // SetPixelsPerDip
                    return false;
                // Flat pixel geometry and zero ClearType level give neutral edges on
                // RGB and BGR displays. GDI natural grid fitting keeps stems defined.
                if (Method<NewParams>(factory, 12)(factory, 1.8f, 1.0f, 0, 0, 3, out parameters) < 0)
                    return false;
                createLayout = Method<NewLayout>(factory, 19); // IDWriteFactory::CreateGdiCompatibleTextLayout
                paintRun = Method<PaintRun>(target, 3); // IDWriteBitmapRenderTarget::DrawGlyphRun
                renderer = Marshal.GetComInterfaceForObject(this, typeof(ILabelRenderer));
                return true;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (PlatformNotSupportedException)
            {
                return false;
            }
            catch (COMException)
            {
                return false;
            }
            finally
            {
                Release(ref gdiInterop);
                if (renderer == IntPtr.Zero)
                {
                    Dispose();
                    unavailable = true;
                }
            }
        }

        private bool SizeBuffer(int width, int height)
        {
            if (width <= bufferWidth && height <= bufferHeight)
                return true;
            int w = Math.Max(width, bufferWidth), h = Math.Max(height, bufferHeight);
            if (Method<ResizeTarget>(target, 10)(target, (uint)w, (uint)h) < 0)
                return false;
            memory = Method<GetDc>(target, 4)(target);
            if (memory == IntPtr.Zero)
                return false;
            bufferWidth = w;
            bufferHeight = h;
            return true;
        }

        private IntPtr Format(IntPtr font)
        {
            IntPtr format;
            if (formats.TryGetValue(font, out format))
                return format;
            LogFont info;
            if (GetObjectW(font, Marshal.SizeOf(typeof(LogFont)), out info) == 0)
                return IntPtr.Zero;
            string locale = CultureInfo.CurrentUICulture.Name;
            if (Method<NewFormat>(factory, 15)(factory, info.Family, IntPtr.Zero, info.Weight, info.Italic == 0 ? 0 : 2,
                5, Math.Abs(info.Height), locale.Length == 0 ? "en-US" : locale, out format) < 0)
                return IntPtr.Zero;
            IntPtr sign = IntPtr.Zero;
            try
            {
                Trimming trim = new Trimming { Granularity = 1 }; // character granularity
                if (Method<SetValue>(format, 4)(format, 2) < 0 || // vertical center
                    Method<SetValue>(format, 5)(format, 1) < 0 || // single line
                    Method<NewEllipsis>(factory, 20)(factory, format, out sign) < 0 ||
                    Method<SetTrimming>(format, 9)(format, ref trim, sign) < 0)
                    return IntPtr.Zero;
                IntPtr cached = format;
                formats.Add(font, cached);
                format = IntPtr.Zero;
                return cached;
            }
            finally
            {
                Release(ref sign);
                Release(ref format);
            }
        }

        private IntPtr Layout(LayoutKey key)
        {
            IntPtr layout;
            if (layouts.TryGetValue(key, out layout))
                return layout;
            IntPtr format = Format(key.Font);
            if (format == IntPtr.Zero || createLayout(factory, key.Text, key.Text.Length, format, key.Width,
                key.Height, 1, IntPtr.Zero, 1, out layout) < 0)
                return IntPtr.Zero;
            int alignment = key.Alignment == 1 ? 2 : key.Alignment == 2 ? 1 : 0;
            if (Method<SetValue>(layout, 3)(layout, alignment) < 0)
            {
                Release(ref layout);
                return IntPtr.Zero;
            }

            if (layouts.Count >= 160)
                ClearLayouts();
            layouts.Add(key, layout);
            if (paintLayout == null)
                paintLayout = Method<PaintLayout>(layout, 58); // IDWriteTextLayout::Draw
            return layout;
        }

        internal bool Draw(IntPtr dc, IntPtr font, string text, int left, int top, int w, int h, int colour, uint align)
        {
            if (String.IsNullOrEmpty(text) || w <= 0 || h <= 0)
                return true;
            if (!Initialize(dc) || !SizeBuffer(w, h))
                return false;
            IntPtr layout = Layout(new LayoutKey { Font = font, Text = text, Width = w, Height = h, Alignment = align });
            if (layout == IntPtr.Zero || !BitBlt(memory, 0, 0, w, h, dc, left, top, SrcCopy))
                return false;
            // The bitmap target writes into the DIB also used by GDI. Synchronize the
            // copy and render directly at device resolution before copying it back.
            GdiFlush();
            ink = colour;
            return paintLayout(layout, IntPtr.Zero, renderer, 0, 0) >= 0 &&
                BitBlt(dc, left, top, w, h, memory, 0, 0, SrcCopy);
        }

        public int IsPixelSnappingDisabled(IntPtr context, out int disabled)
        {
            disabled = 0;
            return 0;
        }

        public int GetPixelsPerDip(IntPtr context, out float value)
        {
            value = 1;
            return 0;
        }

        public int GetCurrentTransform(IntPtr context, out LabelTransform matrix)
        {
            matrix = new LabelTransform
            {
                XX = 1,
                YY = 1
            };
            return 0;
        }

        public int DrawGlyphRun(IntPtr context, float x, float y, int measuring, IntPtr run, IntPtr description, IntPtr effect)
        {
            return paintRun(target, x, y, measuring, run, parameters, ink, IntPtr.Zero);
        }

        public int DrawInlineObject(IntPtr context, float x, float y, IntPtr item, int sideways, int rtl, IntPtr effect)
        {
            if (item == IntPtr.Zero)
                return unchecked((int)0x80070057);
            return Method<PaintInline>(item, 3)(item, context, renderer, x, y, sideways, rtl, effect);
        }

        // These labels have no underline or strikethrough ranges.
        public int DrawUnderline(IntPtr context, float x, float y, IntPtr underline, IntPtr effect)
        {
            return 0;
        }

        public int DrawStrikethrough(IntPtr context, float x, float y, IntPtr strike, IntPtr effect)
        {
            return 0;
        }

        private void ClearLayouts()
        {
            foreach (IntPtr layout in layouts.Values)
                Marshal.Release(layout);
            layouts.Clear();
        }

        public void Dispose()
        {
            ClearLayouts();
            foreach (IntPtr format in formats.Values)
                Marshal.Release(format);
            formats.Clear();
            Release(ref renderer);
            Release(ref target);
            Release(ref parameters);
            Release(ref factory);
            memory = IntPtr.Zero;
            bufferWidth = 0;
            bufferHeight = 0;
            paintLayout = null;
            paintRun = null;
            createLayout = null;
        }
    }

    private sealed class Dialog
    {
        private readonly LabelPainter labelPainter = new LabelPainter();
        internal PlanarCurveOptions Options;
        private readonly string title, headline, operation, report;
        private readonly int severity;
        private readonly List<IntPtr> fonts = new List<IntPtr>();
        private IntPtr window;
        private readonly RoundedFrame mainFrame=new RoundedFrame(), paletteFrame=new RoundedFrame();
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
            UIntPtr graphicsToken = UIntPtr.Zero;
            IntPtr previousDpi = IntPtr.Zero;
            try
            {
                Native.GraphicsStartup startup = new Native.GraphicsStartup
                {
                    Version = 1
                };
                VectorInk.Check(Native.GdiplusStartup(out graphicsToken, ref startup, IntPtr.Zero));
                Marshal.Copy(template, 0, memory, template.Length);
                // Only the custom top-level dialog and its palette use this thread scope.
                // NX's own windows retain their original awareness contexts.
                if (SettingsWindow)
                    previousDpi = Native.SetThreadDpiAwarenessContext(new IntPtr(-4)); // Per Monitor V2
                IntPtr result = Native.DialogBoxIndirectParamW(Native.GetModuleHandleW(null), memory, owner, procedure, IntPtr.Zero);
                int error = Marshal.GetLastWin32Error();
                if (callbackError != null)
                    throw new InvalidOperationException("Ошибка окна журнала.", callbackError);
                if (result.ToInt64() == -1)
                    throw new System.ComponentModel.Win32Exception(error, "Не удалось открыть окно журнала.");
                return result.ToInt32();
            }
            finally
            {
                if (previousDpi != IntPtr.Zero)
                    Native.SetThreadDpiAwarenessContext(previousDpi);
                labelPainter.Dispose();
                paletteFrame.Dispose();
                mainFrame.Dispose();
                if (graphicsToken != UIntPtr.Zero)
                    Native.GdiplusShutdown(graphicsToken);
                GC.KeepAlive(procedure);
                GC.KeepAlive(paletteProc);
                GC.KeepAlive(buttonProc);
                Marshal.FreeHGlobal(memory);
                foreach (IntPtr font in fonts)
                    Native.DeleteObject(font);
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
                        if (SettingsWindow)
                            Native.SetDialogDpiChangeBehavior(hwnd, 6, 6); // LayoutStart handles resize and child layout.
                        BuildControls();
                        Native.SendMessageW(hwnd, 0x401, new IntPtr(OK), IntPtr.Zero); // DM_SETDEFID
                        ready = true;
                        Layout();
                        Native.SetFocus(Item(IsReport ? OK : PartChoice));
                        return IntPtr.Zero; // Keep the explicitly assigned keyboard focus.
                    case 0x0f:
                    case 0x318: // WM_PAINT, WM_PRINTCLIENT
                        if (SettingsWindow && ready)
                        {
                            Paint(hwnd, message, wParam, 0);
                            return new IntPtr(1);
                        }

                        break;
                    case 0x201: // drag the borderless header
                        if (SettingsWindow && palette != IntPtr.Zero)
                            ClosePalette(false);
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
                            labelPainter.Dispose();
                            Native.Rect suggested = (Native.Rect)Marshal.PtrToStructure(lParam, typeof(Native.Rect));
                            FitWindow(Native.MonitorFromRect(ref suggested, 2), suggested, (uint)(wParam.ToInt64() & 0xffff));
                            IntPtr[] previousFonts = fonts.ToArray();
                            fonts.Clear();
                            SetPanelFonts();
                            foreach (int id in new int[]
                            {
                                PartChoice,
                                BlankChoice,
                                PlaneChoice,
                                On,
                                Tanto,
                                Inside,
                                Outside,
                                Left,
                                Right,
                                Gear,
                                CloseChoice,
                                Cancel,
                                OK,
                                Note
                            }

                            )
                                Native.SendMessageW(Item(id), WmSetFont, id == Note ? fontSmall : fontBody, IntPtr.Zero);
                            if (palette != IntPtr.Zero)
                            {
                                SetPaletteFonts();
                                LayoutPalette();
                            }

                            foreach (IntPtr font in previousFonts)
                                Native.DeleteObject(font);
                            RefreshStart();
                            return new IntPtr(1);
                        }

                        break;
                    case 0x2b: // WM_DRAWITEM also arrives for focus and pressed states.
                        return DrawChoiceItem(hwnd, lParam);
                    case 0x111: // WM_COMMAND
                        if (Command((int)(wParam.ToInt64() & 0xffff), (int)((wParam.ToInt64() >> 16) & 0xffff)))
                            return new IntPtr(1);
                        break;
                    case 0x10: // WM_CLOSE (including the title-bar cross)
                        if (palette != IntPtr.Zero)
                        {
                            ClosePalette(true);
                            return new IntPtr(1);
                        }

                        Native.EndDialog(hwnd, new IntPtr(Cancel));
                        return new IntPtr(1);
                    case 2: // WM_DESTROY
                        ready = false;
                        if (SettingsWindow)
                        {
                            ClosePalette(false);
                            SaveLocalLook();
                            mainFrame.Dispose();
                        }

                        break;
                    case 3: // WM_MOVE
                        mainFrame.Move(hwnd);
                        if (ready && palette != IntPtr.Zero)
                            PositionPalette();
                        break;
                    case 0x18:
                        mainFrame.Show(wParam != IntPtr.Zero);
                        break; // WM_SHOWWINDOW
                    case 0x05: // WM_SIZE
                        if (ready)
                            Layout();
                        break;
                    case 0x24: // WM_GETMINMAXINFO
                        if (ready && IsReport)
                        {
                            SetMinimumSize(lParam);
                            return new IntPtr(1);
                        }

                        break;
                    case 0x136: // WM_CTLCOLORDLG
                        return Brush(wParam, Panel, White);
                    case 0x138: // WM_CTLCOLORSTATIC (also used by read-only EDITs)
                        return ControlBrush(wParam, Native.GetDlgCtrlID(lParam));
                }
            }
            catch (Exception error)
            {
                if (callbackError == null)
                    callbackError = error;
                Native.EndDialog(hwnd, new IntPtr(Cancel));
                return new IntPtr(1);
            }

            return IntPtr.Zero;
        }

        private IntPtr ParentOf(int id)
        {
            return id >= PaletteClose && id <= SaveShared ? palette : window;
        }
        private IntPtr Item(int id)
        {
            return Native.GetDlgItem(ParentOf(id), id);
        }

        private void BuildControls()
        {
            if (SettingsWindow)
            {
                BuildStart();
                return;
            }

            scale = Math.Max(1, (int)Native.GetDpiForWindow(window)) / 96.0;
            if (scale < .5) scale = 1;
            SetPanelFonts();
            buttonProc = HandleButton;
            Add("STATIC", Heading, headline, StaticNoPrefix, fontTitle);
            Add("STATIC", Summary, "Результат и проверка границ", StaticNoPrefix, fontBody);
            Subclass(Heading);
            Subclass(Summary);
            Add("EDIT", ReportEdit, "", TabStop | 0x00b008c4u, PixelFont("Consolas", 14, 400));
            Native.SendMessageW(Item(ReportEdit), 0xc5, new IntPtr(Int32.MaxValue - 2), IntPtr.Zero);
            if (!Native.SetWindowTextW(Item(ReportEdit), report))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Не удалось показать текст отчёта.");
            AddChoice(Copy, "Копировать всё", TabStop);
            AddChoice(OK, "Закрыть", TabStop | 1u);
            int dark = look.Light ? 0 : 1;
            Native.DwmSetWindowAttribute(window, 20, ref dark, 4);
        }

        private static readonly int[] SideButtons = { Inside, Outside, Left, Right };
        private const int PartChoice = 140, BlankChoice = 141, CloseChoice = 142;
        private const int Gear=150, PaletteClose=151, DarkTheme=152, LightTheme=153, Square=154, HueStrip=155, HexEdit=156, SaveShared=157;
        private const int PanelWidth=692, FullHeight=592, PaletteWidth=322, PaletteHeight=620;
        private int PanelHeight { get { return Options.Tanto ? FullHeight : FullHeight-110; } }
        private int FooterY { get { return PanelHeight-81; } }
        private int Shell, Panel, Tile, Edge, LineColor, White, Sub, Quiet, Recess, Context, Disabled, Faded;
        private int AccentFill, AccentInk, Strong, Selected, SelectedEdge, AppFill, AppEdge, HoverAccent;
        private Appearance look=new Appearance(), sharedLook;
        private string appearancePath, sharedPath, appearanceNotice;
        private bool appearanceDirty, updatingHex;
        private IntPtr palette;
        private Native.DialogProc paletteProc;
        private int dragControl;
        private double scale = 1;
        private IntPtr fontTitle, fontHeading, fontStrong, fontBody, fontSmall, fontMono;
        private Native.SubclassProc buttonProc;
        private int hover;
        private bool SettingsWindow { get { return !IsReport; } }

        internal void LoadAppearance(string path)
        {
            appearancePath = System.IO.Path.ChangeExtension(path, "appearance");
            sharedPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "Appearance.settings");
            sharedLook = ReadLook(sharedPath);
            look = ReadLook(appearancePath) ?? (sharedLook != null ? sharedLook.Copy() : new Appearance());
            UpdateColours();
        }
        private Appearance ReadLook(string path)
        {
            try
            {
                return Appearance.Read(path);
            }
            catch (Exception ex)
            {
                appearanceNotice = "Не удалось прочитать оформление: " + ex.Message;
                return null;
            }
        }
        private void UpdateColours()
        {
            bool light = look.Light;
            Shell = light ? Rgb(253, 254, 253) : Rgb(34, 37, 41);
            Panel = light ? Rgb(248, 250, 249) : Rgb(43, 46, 50);
            Tile = light ? 0xffffff : Rgb(48, 52, 56);
            Edge = light ? Rgb(203, 211, 207) : Rgb(72, 77, 81);
            LineColor = light ? Rgb(220, 227, 222) : Rgb(58, 64, 67);
            White = light ? Rgb(32, 41, 36) : Rgb(243, 244, 243);
            Sub = light ? Rgb(60, 76, 67) : Rgb(207, 212, 210);
            Quiet = light ? Rgb(70, 84, 75) : Rgb(187, 196, 192);
            Recess = light ? Rgb(234, 240, 236) : Rgb(38, 42, 45);
            Context = light ? Rgb(237, 241, 238) : Rgb(38, 41, 44);
            Disabled = light ? Rgb(238, 241, 239) : Rgb(42, 45, 48);
            Faded = light ? Rgb(103, 115, 108) : Rgb(152, 160, 158);
            AccentFill = look.Color;
            double luminance = Appearance.Luminance(AccentFill);
            AccentInk = Appearance.Contrast(luminance, 1) > Appearance.Contrast(luminance, 0) ? 0xffffff : 0;
            Strong = look.Indicator(light ? Recess : Rgb(56, 61, 64));
            Selected = Mix(Tile, AccentFill, .12);
            SelectedEdge = Mix(Edge, Strong, .62);
            AppFill = Mix(Shell, AccentFill, .14);
            AppEdge = Mix(Edge, Strong, .47);
            HoverAccent = Mix(AccentFill, AccentInk == 0 ? 0xffffff : 0, .12);
        }
        private void SaveLocalLook()
        {
            if (!appearanceDirty)
                return;
            try
            {
                look.Save(appearancePath);
                appearanceDirty = false;
            }
            catch (Exception ex)
            {
                appearanceNotice = "Не удалось сохранить оформление: " + ex.Message;
                if (Options != null)
                    Options.PreferenceNotice = appearanceNotice;
            }
        }

        private int Px(int v) { return (int)Math.Round(v * scale); }
        private void PlacePx(int id, int x, int y, int w, int h)
        {
            Native.SetWindowPos(Item(id), IntPtr.Zero, Px(x), Px(y), Math.Max(1, Px(x + w) - Px(x)), Math.Max(1, Px(y + h) - Px(y)), 0x14);
        }
        private static Native.Rect WorkArea(IntPtr monitor)
        {
            Native.MonitorInfo info = new Native.MonitorInfo();
            info.Size = Marshal.SizeOf(typeof(Native.MonitorInfo));
            if (!Native.GetMonitorInfoW(monitor, ref info))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return info.Work;
        }
        private void FitWindow(IntPtr monitor, Native.Rect anchor, uint dpi)
        {
            Native.Rect area = WorkArea(monitor);
            scale = Math.Min((dpi == 0 ? 96 : dpi) / 96.0, Math.Min((area.Right - area.Left - 8.0) / PanelWidth, (area.Bottom - area.Top - 8.0) / PaletteHeight));
            int w = Px(PanelWidth), h = Px(PanelHeight);
            int x = Math.Max(area.Left, Math.Min(area.Right - w, (anchor.Left + anchor.Right - w) / 2));
            int y = Math.Max(area.Top, Math.Min(area.Bottom - h, (anchor.Top + anchor.Bottom - h) / 2));
            Native.SetWindowPos(window, IntPtr.Zero, x, y, w, h, 0x14);
            Frame(window, w, h, Px(22));
        }
        private void Frame(IntPtr hwnd, int w, int h, int radius)
        {
            (hwnd == window ? mainFrame : paletteFrame).Fit(hwnd, w, h, radius, Edge);
        }
        private void FitHeight()
        {
            Native.Rect r;
            if (!Native.GetWindowRect(window, out r) || r.Bottom - r.Top == Px(PanelHeight))
                return;
            Native.Rect area = WorkArea(Native.MonitorFromWindow(window, 2));
            int w = Px(PanelWidth), h = Px(PanelHeight);
            Native.SetWindowPos(window, IntPtr.Zero, r.Left, Math.Max(area.Top, Math.Min(r.Top, area.Bottom - h)), w, h, 0x14);
            Frame(window, w, h, Px(22));
        }
        private IntPtr PixelFont(string face, int pixels, int weight)
        {
            IntPtr f = Native.CreateFontW(-Px(pixels), 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 4, 0, face);
            if (f == IntPtr.Zero)
                return Native.GetStockObject(17);
            fonts.Add(f);
            return f;
        }
        private void SetPanelFonts()
        {
            fontTitle = PixelFont("Segoe UI", 24, 700);
            fontHeading = PixelFont("Segoe UI", 16, 700);
            fontStrong = PixelFont("Segoe UI", 14, 600);
            fontBody = PixelFont("Segoe UI", 14, 400);
            fontSmall = PixelFont("Segoe UI", 13, 400);
            fontMono = PixelFont("Consolas", 13, 400);
        }
        private void AddChoice(int id, string caption, uint style)
        {
            Add("BUTTON", id, caption, style, fontBody);
            Subclass(id);
        }
        private void Subclass(int id)
        {
            if (!Native.SetWindowSubclass(Item(id), buttonProc, new UIntPtr((uint)id), UIntPtr.Zero))
                throw new InvalidOperationException("Не удалось оформить элемент окна: " + id);
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
            AddChoice(PartChoice, "Part Boundaries — Границы детали", Group | TabStop | OwnerDraw);
            AddChoice(BlankChoice, "Blank Boundaries — Границы заготовки", TabStop | OwnerDraw);
            AddChoice(PlaneChoice, "Выбрать плоскость границ", TabStop | OwnerDraw);
            Add("STATIC", Note, "", StaticNoPrefix, fontSmall);
            Subclass(Note);
            AddChoice(On, "ON — Центр на границе", Group | TabStop | OwnerDraw);
            AddChoice(Tanto, "TANTO — Касание границы", OwnerDraw);
            AddChoice(Inside, "Внутри (Inside)", Group | TabStop | OwnerDraw);
            AddChoice(Outside, "Снаружи (Outside)", OwnerDraw);
            AddChoice(Left, "Слева (Left)", Group | TabStop | OwnerDraw);
            AddChoice(Right, "Справа (Right)", OwnerDraw);
            AddChoice(Cancel, "Отмена", Group | TabStop);
            AddChoice(OK, "", TabStop | 1);
            AddChoice(Gear, "Оформление", TabStop);
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
            PlacePx(Gear, 602, 16, 34, 32);
            int primaryWidth = Options.SelectPlane ? 175 : 215;
            PlacePx(Cancel, PanelWidth - 22 - primaryWidth - 83, FooterY + 21, 75, 38);
            PlacePx(OK, PanelWidth - 22 - primaryWidth, FooterY + 21, primaryWidth, 38);
        }
        private void RefreshStart()
        {
            SetGroupTabStop(On, Tanto, Options.Tanto);
            SetGroupTabStop(Inside, Outside, Options.ClosedOutside);
            SetGroupTabStop(Left, Right, Options.OpenRight);
            foreach (int id in SideButtons)
            {
                Native.EnableWindow(Item(id), Options.Tanto);
                Native.ShowWindow(Item(id), Options.Tanto ? 5 : 0);
            }

            Native.EnableWindow(Item(OK), Options.Part || Options.Blank);
            Native.SetWindowTextW(Item(OK), NextLabel());
            Native.SetWindowTextW(Item(Note), Options.SelectPlane ? "Выберите плоскость в окне Plane" : "NX определит плоскость каждой новой границы автоматически.");
            FitHeight();
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
        private static int PairedChoice(int id)
        {
            switch (id)
            {
                case On: return Tanto;
                case Tanto: return On;
                case Inside: return Outside;
                case Outside: return Inside;
                case Left: return Right;
                case Right: return Left;
                case DarkTheme: return LightTheme;
                case LightTheme: return DarkTheme;
                default: return 0;
            }
        }
        private bool ChoiceChecked(int id)
        {
            switch (id)
            {
                case PartChoice: return Options.Part;
                case BlankChoice: return Options.Blank;
                case PlaneChoice: return Options.SelectPlane;
                case On: return !Options.Tanto;
                case Tanto: return Options.Tanto;
                case Inside: return !Options.ClosedOutside;
                case Outside: return Options.ClosedOutside;
                case Left: return !Options.OpenRight;
                case Right: return Options.OpenRight;
                case DarkTheme: return !look.Light;
                case LightTheme: return look.Light;
                default: return false;
            }
        }
        private void SetGroupTabStop(int first, int second, bool useSecond)
        {
            SetTabStop(Item(first), !useSecond);
            SetTabStop(Item(second), useSecond);
        }
        private static void SetTabStop(IntPtr hwnd, bool enabled)
        {
            int style = Native.GetWindowLongW(hwnd, -16);
            int updated = enabled ? style | (int)TabStop : style & ~(int)TabStop;
            if (style != updated)
                Native.SetWindowLongW(hwnd, -16, updated);
        }
        private IntPtr DrawChoiceItem(IntPtr parent, IntPtr data)
        {
            if (data == IntPtr.Zero)
                return IntPtr.Zero;
            Native.DrawItem item = (Native.DrawItem)Marshal.PtrToStructure(data, typeof(Native.DrawItem));
            if (item.Type != 4 || item.Window == IntPtr.Zero || item.Dc == IntPtr.Zero ||
                ParentOf((int)item.Id) != parent || Item((int)item.Id) != item.Window)
                return IntPtr.Zero;
            DrawButton(item.Window, item.Dc, (int)item.Id);
            return new IntPtr(1);
        }
        private void Box(IntPtr dc, int x, int y, int w, int h, int radius, int fill, int border)
        {
            if (radius > 0)
            {
                using (VectorInk ink = new VectorInk(dc, scale))
                    ink.Round(x, y, w, h, radius, fill, border);
                return;
            }

            Native.SetDCBrushColor(dc, fill);
            Native.SetDCPenColor(dc, border);
            Native.Rectangle(dc, Px(x), Px(y), Px(x + w), Px(y + h));
        }
        private void Stroke(IntPtr dc, int x, int y, int xx, int yy, int color)
        {
            using (VectorInk ink = new VectorInk(dc, scale))
                ink.Line(x + .5f, y + .5f, xx + .5f, yy + .5f, color, 1);
        }
        private void Circle(IntPtr dc, int x, int y, int d, int fill, int edge)
        {
            using (VectorInk ink = new VectorInk(dc, scale))
                ink.Ellipse(x, y, d, fill, edge, 1.1f);
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
            if (!labelPainter.Draw(dc, font, text, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, color, align))
            {
                Native.SetTextColor(dc, color);
                Native.DrawTextW(dc, text, -1, ref rect, 0x8824u | align);
            }
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
                Box(dc, 0, 76, PanelWidth, 37, 0, Context, LineColor);
                Box(dc, 0, FooterY, PanelWidth, 82, 0, Shell, LineColor);
                DrawMark(dc);
                TextLine(dc, "Кривые в PLANAR", 78, 12, 510, 31, fontTitle, White, 0);
                int split = title.IndexOf(" — ", StringComparison.Ordinal);
                TextLine(dc, split >= 0 ? title.Substring(split + 3) : title, 79, 43, 515, 20, fontSmall, Quiet, 0);
                TextLine(dc, "Операция", 22, 82, 76, 24, fontSmall, Quiet, 0);
                TextLine(dc, operation, 100, 82, 313, 24, fontMono, White, 0);
                TextLine(dc, "PLANAR_MILL / PLANAR_PROFILE", 419, 82, 251, 24, fontSmall, Quiet, 2);
                TextLine(dc, "Границы операции", 22, 122, 345, 27, fontHeading, White, 0);
                TextLine(dc, "Можно выбрать оба набора", 374, 122, 296, 27, fontSmall, Quiet, 2);
                Stroke(dc, 22, 281, 670, 281, LineColor);
                TextLine(dc, "Положение инструмента", 22, 290, 475, 25, fontHeading, White, 0);
                if (Options.Tanto)
                {
                    TextLine(dc, "Замкнутые кривые", 22, 389, 318, 24, fontStrong, Sub, 0);
                    TextLine(dc, "Открытые кривые", 351, 389, 318, 24, fontStrong, Sub, 0);
                    TextLine(dc, "Слева и справа — по направлению открытой кривой.", 22, 463, 648, 26, fontSmall, Quiet, 0);
                }

                bool any = Options.Part || Options.Blank;
                TextLine(dc, any ? "Порядок выбора" : "Отметьте хотя бы один набор границ.", 22, FooterY + 16, 310, 21, fontSmall, Quiet, 0);
                if (any)
                {
                    List<string> order = new List<string>();
                    if (Options.SelectPlane)
                        order.Add("Плоскость");
                    if (Options.Part)
                        order.Add("Деталь");
                    if (Options.Blank)
                        order.Add("Заготовка");
                    TextLine(dc, String.Join("   ›   ", order.ToArray()), 22, FooterY + 36, 310, 24, fontBody, White, 0);
                }
            }
            finally
            {
                Native.RestoreDC(dc, saved);
            }
        }
        private void DrawMark(IntPtr dc)
        {
            using (VectorInk ink = new VectorInk(dc, scale))
            {
                ink.Round(22, 17, 42, 42, 12, AppFill, AppEdge);
                int pass = Rgb(0, 169, 208);
                ink.Line(34.67f, 30.25f, 49.62f, 30.25f, pass, 1.35f);
                ink.Line(40.52f, 36.1f, 55.47f, 36.1f, pass, 1.35f);
                ink.Line(45.72f, 41.3f, 55.47f, 41.3f, pass, 1.35f);
                ink.Outline(new float[] { 28.3f, 30.25f, 34.15f, 30.25f, 34.15f, 36.1f, 40, 36.1f, 40, 41.3f, 45.2f, 41.3f, 45.2f, 47.15f, 51.05f, 47.15f, 51.05f, 53, 28.3f, 53 }, Strong, 1.35f);
                ink.Round(49.25f, 21.95f, 9.1f, 13.7f, 1.6f, Mix(AppFill, AccentFill, .10), Strong);
                ink.Line(49.95f, 28.6f, 57.45f, 23.85f, Strong, 1.1f);
                ink.Line(49.95f, 33.4f, 57.45f, 28.6f, Strong, 1.1f);
            }
        }
        private void DrawGear(IntPtr dc, int w, int h, int color)
        {
            float[] points = new float[8 * 5 * 2];
            double[] offset =
            {
                -.49,
                -.32,
                -.21,
                .12,
                .23
            }, radius =
            {
                7,
                7,
                9,
                9,
                7
            };
            for (int tooth = 0; tooth < 8; tooth++)
                for (int j = 0; j < 5; j++)
                {
                    double angle = (tooth + offset[j]) * Math.PI / 4 - Math.PI / 2;
                    int i = (tooth * 5 + j) * 2;
                    points[i] = (float)(w * .5 + Math.Cos(angle) * radius[j]);
                    points[i + 1] = (float)(h * .5 + Math.Sin(angle) * radius[j]);
                }

            using (VectorInk ink = new VectorInk(dc, scale))
            {
                ink.Outline(points, color, 1.5f);
                ink.Ellipse(w * .5f - 3.15f, h * .5f - 3.15f, 6.3f, -1, color, 1.4f);
            }
        }
        private void Tick(IntPtr dc, int x, int y, int color)
        {
            using (VectorInk ink = new VectorInk(dc, scale))
            {
                ink.Line(x, y + 4, x + 4, y + 8, color, 1.6f);
                ink.Line(x + 4, y + 8, x + 11, y, color, 1.6f);
            }
        }
        private void CheckBox(IntPtr dc, int x, int y, bool selected, int bg)
        {
            Box(dc, x, y, 18, 18, 5, selected ? AccentFill : bg, selected ? Strong : Quiet);
            if (selected)
                Tick(dc, x + 3, y + 4, AccentInk);
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
                bool enabled = Native.IsWindowEnabled(hwnd), selected = ChoiceChecked(id);
                int state = Native.SendMessageW(hwnd, 0xf2, IntPtr.Zero, IntPtr.Zero).ToInt32();
                bool hot = enabled && (hover == id || (state & 4) != 0);
                if (id >= PaletteClose && id <= SaveShared)
                {
                    DrawPaletteControl(dc, id, w, h, hot, state);
                    return;
                }

                if (IsReport)
                {
                    Box(dc, 0, 0, w + 1, h + 1, 0, Panel, Panel);
                    Box(dc, 0, 0, w, h, 9, id == OK ? (hot ? HoverAccent : AccentFill) : Tile, hot ? Strong : Edge);
                    TextLine(dc, WindowText(hwnd), 4, 0, w - 8, h, fontStrong, id == OK ? AccentInk : White, 1);
                    if ((state & 8) != 0)
                        FocusRect(dc, rect);
                    return;
                }

                int bg = id == OK || id == Cancel || id == Gear || id == CloseChoice ? Shell : Panel;
                Box(dc, 0, 0, w + 1, h + 1, 0, bg, bg);
                bool plain = id == PlaneChoice || id == CloseChoice;
                if (id == Gear)
                    selected = palette != IntPtr.Zero;
                int fill = !enabled ? Disabled : selected ? Selected : Tile;
                int border = !enabled ? LineColor : selected ? SelectedEdge : Edge;
                if (id == OK && enabled)
                {
                    fill = AccentFill;
                    border = SelectedEdge;
                }

                if (plain)
                {
                    fill = hot && id == CloseChoice ? Tile : bg;
                    border = fill;
                }

                if (hot && !plain)
                {
                    border = Strong;
                    if (id == OK)
                        fill = HoverAccent;
                }

                Box(dc, 0, 0, w, h, IsSide(id) ? 9 : id == CloseChoice ? 8 : 12, fill, border);
                int ink = enabled ? White : Faded;
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
                    int glyph = selected ? Strong : Sub, square = selected ? AppFill : Recess;
                    Box(dc, 12, 11, 39, 38, 8, square, square);
                    int cy = id == On ? 30 : 25, line = id == On ? 30 : 34;
                    Circle(dc, 23, cy - 9, 19, square, glyph);
                    for (int i = 14; i < 49; i += 6)
                        Stroke(dc, i, line, i + 3, line, glyph);
                    Circle(dc, 31, cy - 1, 3, glyph, glyph);
                    TextLine(dc, id == On ? "ON" : "TANTO", 63, 7, w - 95, 24, fontHeading, ink, 0);
                    TextLine(dc, id == On ? "Центр на границе" : "Касание границы", 63, 30, w - 95, 20, fontSmall, Sub, 0);
                    Circle(dc, w - 27, 23, 14, fill, selected ? Strong : Quiet);
                    if (selected)
                        Circle(dc, w - 23, 27, 6, Strong, Strong);
                }
                else if (IsSide(id))
                {
                    string caption = id == Inside ? "Внутри  Inside" : id == Outside ? "Снаружи  Outside" : id == Left ? "Слева  Left" : "Справа  Right";
                    if (selected)
                        Tick(dc, 10, (h - 10) / 2, enabled ? Strong : Quiet);
                    TextLine(dc, caption, selected ? 19 : 4, 0, w - (selected ? 23 : 8), h, fontBody, ink, 1);
                }
                else if (id == Gear)
                    DrawGear(dc, w, h, selected ? Strong : Sub);
                else if (id == CloseChoice)
                {
                    Stroke(dc, 10, 10, 20, 20, Quiet);
                    Stroke(dc, 20, 10, 10, 20, Quiet);
                }
                else
                {
                    TextLine(dc, id == OK ? NextLabel() : "Отмена", 3, 0, w - (id == OK ? 24 : 6), h, fontStrong, id == OK && enabled ? AccentInk : ink, 1);
                    if (id == OK)
                    {
                        int x = w - 22, y = h / 2;
                        Stroke(dc, x - 2, y, x + 10, y, enabled ? AccentInk : ink);
                        Stroke(dc, x + 5, y - 5, x + 10, y, enabled ? AccentInk : ink);
                        Stroke(dc, x + 5, y + 5, x + 10, y, enabled ? AccentInk : ink);
                    }
                }

                if ((state & 8) != 0 && (Native.SendMessageW(hwnd, 0x129, IntPtr.Zero, IntPtr.Zero).ToInt32() & 1) == 0)
                    FocusRect(dc, rect);
            }
            finally
            {
                Native.RestoreDC(dc, saved);
            }
        }
        private void FocusRect(IntPtr dc, Native.Rect rect)
        {
            rect.Left += Px(4);
            rect.Top += Px(3);
            rect.Right -= Px(4);
            rect.Bottom -= Px(3);
            Native.DrawFocusRect(dc, ref rect);
        }
        private bool hexEdited, closingPalette, positioningPalette;
        private static readonly int[] PaletteTabs={PaletteClose,DarkTheme,LightTheme,HexEdit,Square,HueStrip,SaveShared};
        private void OpenPalette()
        {
            if (palette != IntPtr.Zero)
            {
                ClosePalette(true);
                return;
            }

            string caption = "Оформление — " + title;
            byte[] bytes = Template(caption, 170, 260, false);
            IntPtr data = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, data, bytes.Length);
                paletteProc = HandlePalette;
                IntPtr popup = Native.CreateDialogIndirectParamW(Native.GetModuleHandleW(null), data, window, paletteProc, IntPtr.Zero);
                if (popup == IntPtr.Zero)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Не удалось открыть оформление.");
                if (callbackError != null)
                    return;
                PositionPalette(true);
                Native.ShowWindow(popup, 5);
                Native.SetFocus(Item(look.Light ? LightTheme : DarkTheme));
                IntPtr gear = Item(Gear);
                if (gear != IntPtr.Zero)
                    Native.InvalidateRect(gear, IntPtr.Zero, false);
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
        }
        private void ClosePalette(bool focusGear)
        {
            if (palette == IntPtr.Zero || closingPalette)
                return;
            closingPalette = true;
            try
            {
                FinishHex();
                if (dragControl != 0)
                {
                    dragControl = 0;
                    Native.ReleaseCapture();
                }

                SaveLocalLook();
                Native.DestroyWindow(palette);
                if (focusGear && ready)
                    Native.SetFocus(Item(Gear));
                IntPtr gear = Item(Gear);
                if (gear != IntPtr.Zero)
                    Native.InvalidateRect(gear, IntPtr.Zero, false);
            }
            finally
            {
                closingPalette = false;
            }
        }
        private void PositionPalette(bool makeRoom = false)
        {
            if (palette == IntPtr.Zero || positioningPalette)
                return;
            Native.Rect main;
            if (!Native.GetWindowRect(window, out main))
                return;
            Native.Rect work = WorkArea(Native.MonitorFromWindow(window, 2));
            int w = Px(PaletteWidth), h = Px(PaletteHeight), gap = Px(12);
            positioningPalette = true;
            try
            {
                int x, top = main.Top;
                if (main.Right + gap + w <= work.Right)
                    x = main.Right + gap;
                else if (main.Left - gap - w >= work.Left)
                    x = main.Left - gap - w;
                else if (makeRoom && main.Right - main.Left + gap + w + Px(8) <= work.Right - work.Left)
                {
                    int right = work.Right - w - gap - Px(4);
                    Native.SetWindowPos(window, IntPtr.Zero, right - (main.Right - main.Left), main.Top, 0, 0, 0x15);
                    x = right + gap;
                }
                else
                {
                    x = Math.Max(work.Left, Math.Min(work.Right - w, main.Right - w - Px(12)));
                    top += Px(59);
                }

                int y = Math.Max(work.Top, Math.Min(work.Bottom - h, top));
                Native.SetWindowPos(palette, IntPtr.Zero, x, y, w, h, 0x14);
            }
            finally
            {
                positioningPalette = false;
            }
        }
        private void SetPaletteFonts()
        {
            foreach (int id in PaletteTabs)
                Native.SendMessageW(Item(id), WmSetFont, id == HexEdit ? fontMono : fontBody, IntPtr.Zero);
        }
        private void LayoutPalette()
        {
            PlacePx(PaletteClose, 274, 12, 32, 31);
            PlacePx(DarkTheme, 20, 92, 136, 77);
            PlacePx(LightTheme, 165, 92, 137, 77);
            PlacePx(HexEdit, 228, 186, 66, 19);
            PlacePx(Square, 12, 218, 258, 258);
            PlacePx(HueStrip, 273, 218, 36, 258);
            PlacePx(SaveShared, 20, 542, 282, 38);
            Frame(palette, Px(PaletteWidth), Px(PaletteHeight), Px(19));
            PositionPalette();
        }
        private void SyncHex()
        {
            updatingHex = true;
            try
            {
                Native.SetWindowTextW(Item(HexEdit), look.Hex);
                hexEdited = false;
            }
            finally
            {
                updatingHex = false;
            }
        }
        private void RepaintLook(bool syncHex)
        {
            int previousEdge = Edge;
            UpdateColours();
            if (previousEdge != Edge)
            {
                Frame(window, Px(PanelWidth), Px(PanelHeight), Px(22));
                if (palette != IntPtr.Zero)
                    Frame(palette, Px(PaletteWidth), Px(PaletteHeight), Px(19));
            }

            if (palette != IntPtr.Zero)
            {
                if (syncHex)
                    SyncHex();
                Native.RedrawWindow(palette, IntPtr.Zero, IntPtr.Zero, dragControl != 0 ? 0x181u : 0x81u);
            }

            Native.RedrawWindow(window, IntPtr.Zero, IntPtr.Zero, dragControl != 0 ? 0x181u : 0x81u);
        }
        private void ChangedLook(bool syncHex, bool save)
        {
            appearanceDirty = true;
            appearanceNotice = null;
            if (save)
                SaveLocalLook();
            RepaintLook(syncHex);
        }
        private void FinishHex()
        {
            if (!hexEdited || palette == IntPtr.Zero)
                return;
            if (look.SetHex(WindowText(Item(HexEdit)), true))
                ChangedLook(true, true);
            else
            {
                appearanceNotice = "Введите HEX в формате #RRGGBB.";
                SyncHex();
                Native.InvalidateRect(palette, IntPtr.Zero, false);
            }
        }
        private void SaveGlobalLook()
        {
            FinishHex();
            SaveLocalLook();
            try
            {
                look.Save(sharedPath);
                sharedLook = look.Copy();
                appearanceNotice = "Общий стиль сохранён · " + look.Hex;
            }
            catch (Exception ex)
            {
                appearanceNotice = "Не удалось сохранить общий стиль: " + ex.Message;
            }

            Native.InvalidateRect(palette, IntPtr.Zero, false);
        }
        private IntPtr HandlePalette(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            try
            {
                switch (msg)
                {
                    case 0x2b: return DrawChoiceItem(hwnd, l);
                    case 0x110:
                        palette = hwnd;
                        Native.SetDialogDpiChangeBehavior(hwnd, 6, 6); // LayoutPalette uses the scale of the main window.
                        AddChoice(PaletteClose, "Закрыть оформление", TabStop);
                        AddChoice(DarkTheme, "Тёмная", Group | TabStop | OwnerDraw);
                        AddChoice(LightTheme, "Светлая", TabStop | OwnerDraw);
                        Add("EDIT", HexEdit, "", TabStop | 0x80, fontMono);
                        Subclass(HexEdit);
                        Native.SendMessageW(Item(HexEdit), 0xc5, new IntPtr(7), IntPtr.Zero);
                        AddChoice(Square, "Насыщенность и яркость. Управление стрелками.", TabStop);
                        AddChoice(HueStrip, "Оттенок. Управление стрелками.", TabStop);
                        AddChoice(SaveShared, "Для всех скриптов", TabStop);
                        LayoutPalette();
                        RepaintLook(true);
                        return new IntPtr(1);
                    case 0x0f:
                    case 0x318:
                        Paint(hwnd, msg, w, -1);
                        return new IntPtr(1);
                    case 0x14:
                        return new IntPtr(1);
                    case 0x111:
                        int id = (int)(w.ToInt64() & 65535), note = (int)((w.ToInt64() >> 16) & 65535);
                        if (id == HexEdit)
                        {
                            if (note == 0x300 && !updatingHex)
                            {
                                hexEdited = true;
                                if (look.SetHex(WindowText(Item(HexEdit)), false))
                                    ChangedLook(false, true);
                            }

                            if (note == 0x200 && !closingPalette)
                                FinishHex();
                            return new IntPtr(1);
                        }

                        if (note != 0)
                            break;
                        if (id == PaletteClose || id == Cancel)
                            ClosePalette(true);
                        else if (id == DarkTheme || id == LightTheme)
                        {
                            look.Light = id == LightTheme;
                            ChangedLook(true, true);
                        }
                        else if (id == SaveShared)
                            SaveGlobalLook();
                        else if (id == OK)
                            FinishHex();
                        return new IntPtr(1);
                    case 0x133:
                        return Brush(w, Recess, White); // WM_CTLCOLOREDIT
                    case 0x136:
                    case 0x138:
                        return Brush(w, Panel, White);
                    case 0x10:
                        ClosePalette(true);
                        return new IntPtr(1);
                    case 3:
                        paletteFrame.Move(hwnd);
                        break;
                    case 0x18:
                        paletteFrame.Show(w != IntPtr.Zero);
                        break;
                    case 2:
                        paletteFrame.Dispose();
                        break;
                    case 0x82:
                        palette = IntPtr.Zero;
                        dragControl = 0;
                        hexEdited = false;
                        break;
                }
            }
            catch (Exception ex)
            {
                if (callbackError == null)
                    callbackError = ex;
                Native.EndDialog(window, new IntPtr(Cancel));
            }

            return IntPtr.Zero;
        }
        private static string WindowText(IntPtr hwnd)
        {
            StringBuilder text = new StringBuilder(96);
            Native.GetWindowTextW(hwnd, text, text.Capacity);
            return text.ToString();
        }
        private void DrawPaletteSurface(IntPtr dc)
        {
            int saved = Native.SaveDC(dc);
            try
            {
                InitPaint(dc);
                Box(dc, 0, 0, PaletteWidth, PaletteHeight, 0, Panel, Panel);
                Box(dc, 0, 0, PaletteWidth, 57, 0, Shell, LineColor);
                TextLine(dc, "Оформление", 20, 12, 250, 31, fontHeading, White, 0);
                TextLine(dc, "Тема окна", 20, 64, 280, 24, fontStrong, White, 0);
                TextLine(dc, "Цвет подсветки", 20, 181, 164, 28, fontStrong, White, 0);
                Box(dc, 198, 180, 104, 31, 7, Recess, Edge);
                Box(dc, 207, 189, 13, 13, 4, AccentFill, Strong);
                TextLine(dc, "Вправо — насыщеннее, вверх — светлее.", 20, 476, 285, 20, fontSmall, Quiet, 0);
                TextLine(dc, "Полоса справа меняет оттенок.", 20, 496, 285, 20, fontSmall, Quiet, 0);
                Box(dc, 0, 529, PaletteWidth, PaletteHeight - 529, 0, Shell, LineColor);
                string status = appearanceNotice ?? (sharedLook == null ? "Общий стиль для совместимых скриптов." : "Общий стиль · " + (sharedLook.Light ? "светлая" : "тёмная") + " / " + sharedLook.Hex);
                TextLine(dc, status, 20, 584, 283, 24, fontSmall, Quiet, 0);
            }
            finally
            {
                Native.RestoreDC(dc, saved);
            }
        }
        private void DrawPaletteControl(IntPtr dc, int id, int w, int h, bool hot, int state)
        {
            bool footer = id == SaveShared, close = id == PaletteClose;
            int bg = footer || close ? Shell : Panel;
            Box(dc, 0, 0, w + 1, h + 1, 0, bg, bg);
            if (id == Square || id == HueStrip)
                DrawPicker(dc, id);
            else if (close)
            {
                if (hot)
                    Box(dc, 0, 0, w, h, 9, Tile, Edge);
                Stroke(dc, 11, 10, 21, 20, Quiet);
                Stroke(dc, 21, 10, 11, 20, Quiet);
            }
            else if (footer)
            {
                Box(dc, 0, 0, w, h, 11, Tile, hot ? Strong : Edge);
                Box(dc, 68, 12, 11, 8, 2, Tile, Sub);
                Box(dc, 72, 15, 11, 8, 2, Tile, Sub);
                TextLine(dc, "Для всех скриптов", 90, 0, w - 99, h, fontStrong, White, 0);
            }
            else
            {
                bool light = id == LightTheme, selected = look.Light == light;
                int face = selected ? Selected : Tile;
                Box(dc, 0, 0, w, h, 11, face, selected || hot ? SelectedEdge : Edge);
                int mini = light ? Rgb(245, 247, 244) : Rgb(55, 61, 58), edge = light ? Rgb(193, 203, 195) : Rgb(76, 84, 80);
                int bar = light ? 0xffffff : Rgb(38, 43, 40), field = light ? Rgb(224, 231, 224) : Rgb(71, 78, 73);
                Box(dc, 8, 8, w - 16, 40, 5, mini, edge);
                Box(dc, 10, 10, w - 20, 9, 2, bar, bar);
                Box(dc, 14, 13, 4, 3, 1, AccentFill, AccentFill);
                Box(dc, 21, 13, 21, 2, 0, edge, edge);
                Box(dc, 13, 23, w / 2 - 17, 10, 2, Mix(mini, AccentFill, .24), Mix(edge, AccentFill, .58));
                Box(dc, w / 2, 23, w / 2 - 14, 10, 2, field, edge);
                Box(dc, w - 36, 38, 21, 5, 2, AccentFill, AccentFill);
                TextLine(dc, light ? "Светлая" : "Тёмная", 12, 49, w - 44, 23, fontStrong, White, 0);
                Circle(dc, w - 26, 54, 13, face, selected ? Strong : Quiet);
                if (selected)
                    Circle(dc, w - 22, 58, 5, Strong, Strong);
            }

            if ((state & 8) != 0 && (Native.SendMessageW(Item(id), 0x129, IntPtr.Zero, IntPtr.Zero).ToInt32() & 1) == 0)
            {
                Native.Rect rect;
                Native.GetClientRect(Item(id), out rect);
                FocusRect(dc, rect);
            }
        }
        private int[] squarePixels, huePixels;
        private int squareWidth,squareHeight,pureKey=-1;
        private static double Clamp(double v, double max)
        {
            return Math.Max(0, Math.Min(v, max));
        }
        private Native.Rect PickerArea(int id)
        {
            Native.Rect r;
            Native.GetClientRect(Item(id), out r);
            r.Left = Px(id == Square ? 8 : 10);
            r.Right -= r.Left;
            r.Top = Px(8);
            r.Bottom -= r.Top;
            return r;
        }
        private int[] Gradient(bool square, int w, int h)
        {
            if (!square)
            {
                if (huePixels == null || huePixels.Length != h)
                {
                    huePixels = new int[h];
                    for (int y = 0; y < h; y++)
                        huePixels[y] = Appearance.Reverse(Appearance.FromHsv(360 * (1 - y / (h - 1.0)), 1, 1));
                }

                return huePixels;
            }

            int pure = Appearance.FromHsv(look.Hue, 1, 1);
            if (squareWidth == w && squareHeight == h && pureKey == pure)
                return squarePixels;
            if (squarePixels == null || squarePixels.Length != w * h)
                squarePixels = new int[w * h];
            for (int x = 0; x < w; x++)
            {
                int top = Mix(0xffffff, pure, x / (w - 1.0));
                int r = top & 255, g = (top >> 8) & 255, b = (top >> 16) & 255;
                for (int y = 0; y < h; y++)
                {
                    int v = h - 1 - y, half = (h - 1) / 2;
                    squarePixels[y * w + x] = (((r * v + half) / (h - 1)) << 16) | (((g * v + half) / (h - 1)) << 8) | ((b * v + half) / (h - 1));
                }
            }

            squareWidth = w;
            squareHeight = h;
            pureKey = pure;
            return squarePixels;
        }
        private void DrawPicker(IntPtr dc, int id)
        {
            bool square = id == Square;
            Native.Rect r = PickerArea(id);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w < 2 || h < 2)
                return;
            Native.BitmapInfo bmp = Native.BitmapInfo.TopDown(square ? w : 1, h);
            int radius = Px(square ? 9 : 7);
            // Clip regions use device coordinates. A child rendered into the parent's
            // buffer has a translated viewport, both with WS_EX_COMPOSITED and WM_PRINT.
            Native.Point origin = new Native.Point();
            Native.LPtoDP(dc, ref origin, 1);
            IntPtr region = Native.CreateRoundRectRgn(origin.X + r.Left, origin.Y + r.Top, origin.X + r.Right + 1, origin.Y + r.Bottom + 1, radius * 2, radius * 2);
            int saved = Native.SaveDC(dc);
            try
            {
                if (region != IntPtr.Zero)
                    Native.ExtSelectClipRgn(dc, region, 1);
                Native.StretchDIBits(dc, r.Left, r.Top, w, h, 0, 0, bmp.Width, h, Gradient(square, w, h), ref bmp, 0, 0x00cc0020);
            }
            finally
            {
                Native.RestoreDC(dc, saved);
                if (region != IntPtr.Zero)
                    Native.DeleteObject(region);
            }

            float unit = (float)scale;
            float x = (float)(r.Left + look.Saturation * (w - 1)) / unit;
            float y = (float)(r.Top + (1 - (square ? look.Value : look.Hue / 360)) * (h - 1)) / unit;
            using (VectorInk ink = new VectorInk(dc, scale))
            {
                ink.Round(r.Left / unit, r.Top / unit, w / unit, h / unit, square ? 9 : 7, -1, Mix(Edge, Quiet, .28));
                if (square)
                {
                    ink.Ellipse(x - 8, y - 8, 16, AccentFill, 0x151515, 1.2f);
                    ink.Ellipse(x - 6.8f, y - 6.8f, 13.6f, -1, 0xffffff, 1.8f);
                }
                else
                {
                    int pure = Appearance.FromHsv(look.Hue, 1, 1);
                    ink.Round(5, y - 5, 26, 10, 4, pure, 0x171717);
                    ink.Round(6, y - 4, 24, 8, 3, pure, 0xffffff);
                }
            }
        }
        private void SamplePicker(int id, IntPtr coords)
        {
            Native.Rect r = PickerArea(id);
            int x = (short)(coords.ToInt64() & 65535), y = (short)(coords.ToInt64() >> 16);
            double vertical = 1 - Clamp((y - r.Top) / (double)Math.Max(1, r.Bottom - r.Top - 1), 1);
            if (id == Square)
            {
                double s = Clamp((x - r.Left) / (double)Math.Max(1, r.Right - r.Left - 1), 1);
                if (s == look.Saturation && vertical == look.Value)
                    return;
                look.Saturation = s;
                look.Value = vertical;
            }
            else
            {
                if (look.Hue == vertical * 360)
                    return;
                look.Hue = vertical * 360;
            }

            ChangedLook(true, false);
        }
        private bool PaletteKey(int id, int key)
        {
            bool shift = Native.GetKeyState(0x10) < 0;
            if (key == 27)
            {
                ClosePalette(true);
                return true;
            }

            if (key == 9)
            {
                int index = Array.IndexOf(PaletteTabs, id);
                Native.SetFocus(Item(PaletteTabs[(index + (shift ? -1 : 1) + PaletteTabs.Length) % PaletteTabs.Length]));
                return true;
            }

            if (key == 13)
            {
                if (id == HexEdit)
                {
                    FinishHex();
                    Native.SendMessageW(Item(id), 0xb1, IntPtr.Zero, new IntPtr(-1));
                }
                else if (id != Square && id != HueStrip)
                    Native.SendMessageW(Item(id), 0xf5, IntPtr.Zero, IntPtr.Zero);
                return true;
            }

            if (key < 37 || key > 40)
                return false;
            int direction = key == 37 || key == 40 ? -1 : 1;
            if (id == Square)
            {
                double delta = direction * (shift ? 0.10 : 0.01);
                if (key == 37 || key == 39)
                    look.Saturation = Clamp(look.Saturation + delta, 1);
                else
                    look.Value = Clamp(look.Value + delta, 1);
            }
            else if (id == HueStrip)
                look.Hue = Clamp(look.Hue + direction * (shift ? 10 : 1), 360);
            else if (id == DarkTheme || id == LightTheme)
            {
                look.Light = id == DarkTheme;
                Native.SetFocus(Item(look.Light ? LightTheme : DarkTheme));
            }
            else
                return false;
            ChangedLook(true, true);
            return true;
        }
        private IntPtr HandleButton(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr sub, UIntPtr data)
        {
            try
            {
                int id = (int)sub.ToUInt32();
                int partner = PairedChoice(id);
                bool toggle = partner != 0 || id == PartChoice || id == BlankChoice || id == PlaneChoice;
                if (toggle && message == 0xf4) // BM_SETSTYLE from the dialog manager must not replace BS_OWNERDRAW.
                    return IntPtr.Zero;
                if (toggle && partner == 0 && message == 0x87)
                    return new IntPtr(0x2000); // DLGC_BUTTON
                if (toggle && message == 0xf0) // BM_GETCHECK
                    return new IntPtr(ChoiceChecked(id) ? 1 : 0);
                if (toggle && message == 0xf2) // BM_GETSTATE retains the system's focus and pressed bits.
                {
                    long state = Native.DefSubclassProc(hwnd, message, wParam, lParam).ToInt64();
                    return new IntPtr((state & ~3L) | (ChoiceChecked(id) ? 1L : 0L));
                }
                if (partner != 0 && id != DarkTheme && id != LightTheme)
                {
                    if (message == 0x87) // Preserve radio-group navigation in the native dialog manager.
                        return new IntPtr(0x2041); // DLGC_BUTTON | DLGC_RADIOBUTTON | DLGC_WANTARROWS
                    if (message == 0x100 && wParam.ToInt32() >= 37 && wParam.ToInt32() <= 40)
                    {
                        IntPtr other = Item(partner);
                        if (Native.IsWindowEnabled(other))
                        {
                            Native.SetFocus(other);
                            Native.SendMessageW(other, 0xf5, IntPtr.Zero, IntPtr.Zero);
                        }
                        return IntPtr.Zero;
                    }
                }
                if (id >= PaletteClose && id <= SaveShared)
                {
                    if (message == 0x87)
                        return new IntPtr(Native.DefSubclassProc(hwnd, message, wParam, lParam).ToInt64() | 5);
                    if (message == 0x100 && PaletteKey(id, wParam.ToInt32()))
                        return IntPtr.Zero;
                    if (message == 0x102 && (wParam.ToInt32() == 13 || wParam.ToInt32() == 9 || wParam.ToInt32() == 27))
                        return IntPtr.Zero;
                    if (id == Square || id == HueStrip)
                    {
                        if (message == 0x201)
                        {
                            Native.SetFocus(hwnd);
                            Native.SetCapture(hwnd);
                            dragControl = id;
                            SamplePicker(id, lParam);
                            return IntPtr.Zero;
                        }

                        if (dragControl == id)
                        {
                            if (message == 0x200)
                            {
                                SamplePicker(id, lParam);
                                return IntPtr.Zero;
                            }

                            if (message == 0x202 || message == 0x215 || message == 0x1f)
                            {
                                if (message == 0x202)
                                    SamplePicker(id, lParam);
                                dragControl = 0;
                                if (Native.GetCapture() == hwnd)
                                    Native.ReleaseCapture();
                                SaveLocalLook();
                                return IntPtr.Zero;
                            }
                        }
                    }
                }
                else if (message == 0x201 && id != Gear && palette != IntPtr.Zero)
                    ClosePalette(false);
                if (id != HexEdit && (message == 0x0f || message == 0x318))
                    return Paint(hwnd, message, wParam, id);
                if (message == 0x14 && id != HexEdit)
                    return new IntPtr(1);
                if (message == 0x200 && hover != id && id != HexEdit)
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
                    else if (id == -1)
                        DrawPaletteSurface(dc);
                    else if (id == Note || id == Heading || id == Summary)
                    {
                        int old = Native.SaveDC(dc);
                        try
                        {
                            Native.Rect note;
                            Native.GetClientRect(hwnd, out note);
                            InitPaint(dc);
                            int width = (int)Math.Ceiling(note.Right / scale);
                            int height = (int)Math.Ceiling(note.Bottom / scale);
                            Box(dc, 0, 0, width + 1, height + 1, 0, Panel, Panel);
                            IntPtr font = Native.SendMessageW(hwnd, 0x31, IntPtr.Zero, IntPtr.Zero);
                            TextLine(dc, id == Heading ? headline : WindowText(hwnd), 0, 0, width, height,
                                font, id == Heading ? ReportHeadingColour() : Quiet, 0);
                        }
                        finally { Native.RestoreDC(dc, old); }
                    }
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
            IntPtr child = Native.CreateWindowExW(0, kind, text, 0x54000000u | style, 0, 0, 1, 1, ParentOf(id), new IntPtr(id), Native.GetModuleHandleW(null), IntPtr.Zero);
            if (child == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Не удалось создать элемент " + kind + ".");
            Native.SendMessageW(child, WmSetFont, font, IntPtr.Zero);
        }


        private bool Command(int id, int notification)
        {
            if (notification != 0)
                return false; // BN_CLICKED / STN_CLICKED or a dialog key
            switch (id)
            {
                case PartChoice:
                    Options.Part = !Options.Part;
                    break;
                case BlankChoice:
                    Options.Blank = !Options.Blank;
                    break;
                case PlaneChoice:
                    Options.SelectPlane = !Options.SelectPlane;
                    break;
                case On:
                case Tanto:
                    Options.Tanto = id == Tanto;
                    break;
                case Inside:
                case Outside:
                    if (Options.Tanto)
                        Options.ClosedOutside = id == Outside;
                    break;
                case Left:
                case Right:
                    if (Options.Tanto)
                        Options.OpenRight = id == Right;
                    break;
                case Gear:
                    OpenPalette();
                    return true;
                case Cancel:
                    if (palette != IntPtr.Zero)
                    {
                        ClosePalette(true);
                        return true;
                    }

                    goto case CloseChoice;
                case CloseChoice:
                    Native.EndDialog(window, new IntPtr(Cancel));
                    return true;
                case OK:
                    if (IsReport || Options.Part || Options.Blank)
                        Native.EndDialog(window, new IntPtr(OK));
                    return true;
                case Copy:
                    if (!IsReport)
                        return false;
                    if (CopyText(window, report))
                    {
                        Native.SetWindowTextW(Item(Copy), "Скопировано");
                        Native.SetWindowTextW(Item(Summary), "Отчёт скопирован целиком.");
                    }
                    else
                        Native.SetWindowTextW(Item(Summary), "Буфер обмена недоступен. Повторите копирование.");
                    return true;
                default:
                    return false;
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
            if (SettingsWindow)
                return Brush(dc, Panel, Quiet);
            if (id == ReportEdit)
                return Brush(dc, Tile, White);
            return Brush(dc, Panel, id == Heading ? ReportHeadingColour() : Quiet);
        }
        private int ReportHeadingColour()
        {
            if (severity == 2) return look.Light ? Rgb(155, 50, 46) : Rgb(255, 159, 147);
            if (severity == 1) return look.Light ? Rgb(133, 85, 21) : Rgb(255, 209, 145);
            return Strong;
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
        [StructLayout(LayoutKind.Sequential)] internal struct DrawItem
        {
            internal uint Type, Id, ItemId, Action, State;
            internal IntPtr Window, Dc;
            internal Rect Bounds;
            internal UIntPtr Data;
        }
        [DllImport("user32.dll", ExactSpelling=true)] internal static extern int GetWindowLongW(IntPtr window, int index);
        [DllImport("user32.dll", ExactSpelling=true)] internal static extern int SetWindowLongW(IntPtr window, int index, int value);
        [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool SetDialogDpiChangeBehavior(IntPtr dialog, int mask, int values);
        [StructLayout(LayoutKind.Sequential)] internal struct GraphicsStartup { internal uint Version; internal IntPtr Debug; internal int SuppressThread,SuppressCodecs; }
        [StructLayout(LayoutKind.Sequential,Pack=1)] internal struct Blend { internal byte Op,Flags,Alpha,Format; }
        [DllImport("user32.dll")] internal static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr dstDc,ref Point dst,ref Point size,IntPtr srcDc,ref Point src,int key,ref Blend blend,uint flags);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
        [DllImport("gdi32.dll")] internal static extern bool SetRectRgn(IntPtr region,int left,int top,int right,int bottom);
        [DllImport("gdi32.dll")] internal static extern int CombineRgn(IntPtr dest,IntPtr a,IntPtr b,int operation);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [StructLayout(LayoutKind.Sequential)] internal struct BitmapInfo
        {
            internal uint Size; internal int Width,Height; internal ushort Planes,Bits;
            internal uint Compression,ImageSize; internal int X,Y; internal uint Colors,Important;
            internal static BitmapInfo TopDown(int width,int height)
            { return new BitmapInfo{Size=40,Width=width,Height=-height,Planes=1,Bits=32}; }
        }
        [DllImport("gdi32.dll")] internal static extern int StretchDIBits(IntPtr dc,int x,int y,int w,int h,int srcX,int srcY,int sw,int sh,[In] int[] pixels,ref BitmapInfo info,uint usage,uint rop);
        [DllImport("gdi32.dll")] internal static extern int ExtSelectClipRgn(IntPtr dc,IntPtr region,int mode);
        [DllImport("gdi32.dll")] internal static extern bool LPtoDP(IntPtr dc,ref Point point,int count);
        [DllImport("user32.dll",ExactSpelling=true,SetLastError=true)] internal static extern IntPtr CreateDialogIndirectParamW(IntPtr instance,IntPtr template,IntPtr owner,DialogProc proc,IntPtr data);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] internal static extern int GetWindowTextW(IntPtr hwnd,StringBuilder text,int capacity);
        [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr SetCapture(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetCapture();
        [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        [DllImport("gdiplus.dll",ExactSpelling=true)] internal static extern int GdiplusStartup(out UIntPtr token,ref GraphicsStartup input,IntPtr output);
        [DllImport("gdiplus.dll",ExactSpelling=true)] internal static extern void GdiplusShutdown(UIntPtr token);
        [DllImport("gdiplus.dll")] internal static extern int GdipCreateFromHDC(IntPtr dc,out IntPtr g);
        [DllImport("gdiplus.dll")] internal static extern int GdipDeleteGraphics(IntPtr g);
        [DllImport("gdiplus.dll")] internal static extern int GdipSetPageUnit(IntPtr g,int unit);
        [DllImport("gdiplus.dll")] internal static extern int GdipSetSmoothingMode(IntPtr g,int mode);
        [DllImport("gdiplus.dll")] internal static extern int GdipScaleWorldTransform(IntPtr g,float sx,float sy,int order);
        [DllImport("gdiplus.dll")] internal static extern int GdipCreatePen1(int argb,float width,int unit,out IntPtr pen);
        [DllImport("gdiplus.dll")] internal static extern int GdipDeletePen(IntPtr pen);
        [DllImport("gdiplus.dll")] internal static extern int GdipSetPenWidth(IntPtr pen,float width);
        [DllImport("gdiplus.dll")] internal static extern int GdipSetPenColor(IntPtr pen,int color);
        [DllImport("gdiplus.dll")] internal static extern int GdipSetPenLineJoin(IntPtr pen,int join);
        [DllImport("gdiplus.dll")] internal static extern int GdipSetPenStartCap(IntPtr pen,int cap);
        [DllImport("gdiplus.dll")] internal static extern int GdipSetPenEndCap(IntPtr pen,int cap);
        [DllImport("gdiplus.dll")] internal static extern int GdipCreateSolidFill(int argb,out IntPtr brush);
        [DllImport("gdiplus.dll")] internal static extern int GdipSetSolidFillColor(IntPtr brush,int argb);
        [DllImport("gdiplus.dll")] internal static extern int GdipDeleteBrush(IntPtr brush);
        [DllImport("gdiplus.dll")] internal static extern int GdipDrawLine(IntPtr g,IntPtr pen,float x,float y,float x2,float y2);
        [DllImport("gdiplus.dll")] internal static extern int GdipDrawPolygon(IntPtr g,IntPtr pen,[In] float[] xy,int points);
        [DllImport("gdiplus.dll")] internal static extern int GdipCreatePath(int fillMode,out IntPtr path);
        [DllImport("gdiplus.dll")] internal static extern int GdipAddPathArc(IntPtr path,float x,float y,float w,float h,float start,float sweep);
        [DllImport("gdiplus.dll")] internal static extern int GdipClosePathFigure(IntPtr path);
        [DllImport("gdiplus.dll")] internal static extern int GdipFillPath(IntPtr g,IntPtr brush,IntPtr path);
        [DllImport("gdiplus.dll")] internal static extern int GdipDrawPath(IntPtr g,IntPtr pen,IntPtr path);
        [DllImport("gdiplus.dll")] internal static extern int GdipDeletePath(IntPtr path);
        [DllImport("gdiplus.dll")] internal static extern int GdipFillEllipse(IntPtr g,IntPtr brush,float x,float y,float w,float h);
        [DllImport("gdiplus.dll")] internal static extern int GdipDrawEllipse(IntPtr g,IntPtr pen,float x,float y,float w,float h);
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
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd,int command);
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
