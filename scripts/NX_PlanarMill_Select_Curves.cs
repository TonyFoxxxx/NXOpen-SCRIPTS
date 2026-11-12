// NX_PlanarMill_Select_Curves.cs - add engraving curves to an existing PLANAR_MILL.
// SCRIPT_VERSION: V1
// Siemens NX / Designcenter C# journal. Select one operation before running.
// Text features, sketches and explicit curves; connected chains become boundaries.
// Appends local part boundaries with Tool Position ON and zero member stock.
// No file creation, automatic save, toolpath generation or changes to source curves.

using System;
using System.Collections.Generic;
using System.Text;
using NXOpen;
using NXOpen.CAM;
using NXOpen.Features;
using NXOpen.UF;
using NXOpen.Utilities;
using CamOperation = NXOpen.CAM.Operation;
using V = PlanarCurveChains.V;

public class NXPlanarMillSelectCurves
{
    private const string SCRIPT_VERSION = "V1";
    private static readonly string Title = "Кривые для PLANAR_MILL — " + SCRIPT_VERSION + " — "
        + Encoding.UTF8.GetString(Convert.FromBase64String("YnkgQFRvbnlfRm94eHggKFRHKQ=="));

    private sealed class CurveInfo
    {
        internal Curve Curve;
        internal V Start, End;
        internal readonly List<V> PlanePoints = new List<V>();
    }

    public static void Main(string[] args)
    {
        Session session = Session.GetSession();
        UFSession uf = UFSession.GetUFSession();
        Part part = session.Parts.Work;
        Session.UndoMarkId mark = default(Session.UndoMarkId);
        bool hasMark = false;
        bool committed = false;
        try
        {
            if (part == null || part.CAMSetup == null)
                throw new InvalidOperationException("Откройте CAM-проект в Manufacturing.");
            PlanarMilling operation = SelectedOperation(uf, part);
            EnsureNoInheritedBoundaries(uf, operation);
            HashSet<Tag> assigned = AssignedCurves(uf, operation);
            List<Curve> selected = SelectCurves(uf, part);
            if (selected == null) return;
            int alreadyAssigned = selected.RemoveAll(delegate(Curve c) { return assigned.Contains(c.Tag); });
            if (selected.Count == 0)
            {
                Show(NXMessageBox.DialogType.Information, alreadyAssigned > 0
                    ? "Все выбранные кривые уже назначены этой операции."
                    : "В выбранных объектах нет рабочих кривых. Вспомогательная геометрия эскиза пропускается.");
                return;
            }
            // Do not connect across a visible gap. The tolerance is one nanometre in mm parts.
            double tolerance = part.PartUnits == BasePart.Units.Inches ? 1e-6 / 25.4 : 1e-6;
            List<CurveInfo> curves = new List<CurveInfo>();
            List<PlanarCurveChains.Ends> ends = new List<PlanarCurveChains.Ends>();
            List<V> planePoints = new List<V>();
            foreach (Curve curve in selected)
            {
                CurveInfo info = InspectCurve(uf, curve, tolerance);
                curves.Add(info);
                ends.Add(new PlanarCurveChains.Ends(info.Start, info.End));
                planePoints.AddRange(info.PlanePoints);
            }
            Matrix3x3 wcs = part.WCS.CoordinateSystem.Orientation.Element;
            V preferredNormal = new V(wcs.Zx, wcs.Zy, wcs.Zz);
            PlanarCurveChains.Frame frame = PlanarCurveChains.FindPlane(planePoints, preferredNormal, tolerance);
            List<PlanarCurveChains.Chain> chains = PlanarCurveChains.Build(ends, tolerance);
            if (session.Parts.Work == null || session.Parts.Work.Tag != part.Tag)
                throw new InvalidOperationException("Рабочая деталь изменилась. Запустите скрипт заново.");
            // Recheck the target after the selection dialog, before making any change.
            EnsureNoInheritedBoundaries(uf, operation);
            if (!assigned.SetEquals(AssignedCurves(uf, operation)))
                throw new InvalidOperationException("Границы операции изменились во время выбора. Повторите запуск.");
            IntPtr[] oldBoundaries = Boundaries(uf, operation.Tag);
            HashSet<IntPtr> oldHandles = new HashSet<IntPtr>(oldBoundaries);
            mark = session.SetUndoMark(Session.MarkVisibility.Visible, "Добавить кривые в " + operation.Name);
            hasMark = true;
            foreach (PlanarCurveChains.Chain chain in chains)
            {
                Tag[] tags = new Tag[chain.Items.Count];
                UFCambnd.AppData[] memberData = new UFCambnd.AppData[tags.Length];
                for (int i = 0; i < tags.Length; i++)
                {
                    tags[i] = curves[chain.Items[i]].Curve.Tag;
                    memberData[i] = new UFCambnd.AppData
                    {
                        has_stock = 1, stock = 0.0,
                        has_tool_position = 1, tool_position = CamToolPosition.CamToolPositionOn,
                        tolerances = new double[2]
                    };
                }
                UFCambnd.BoundaryData boundary = new UFCambnd.BoundaryData
                {
                    boundary_type = chain.Closed ? CamBoundaryType.CamBoundaryTypeClosed : CamBoundaryType.CamBoundaryTypeOpen,
                    plane_type = 0,
                    origin = new double[] { frame.Origin.X, frame.Origin.Y, frame.Origin.Z },
                    matrix = new double[] { frame.X.X, frame.X.Y, frame.X.Z, frame.Y.X, frame.Y.Y, frame.Y.Z, frame.Z.X, frame.Z.Y, frame.Z.Z },
                    material_side = CamMaterialSide.CamMaterialSideInLeft,
                    app_data = new UFCambnd.AppData[] { new UFCambnd.AppData
                        { has_stock = 1, stock = 0.0, tolerances = new double[2] } }
                };
                uf.Cambnd.AppendBndFromCurve(operation.Tag, CamGeomType.CamPart, tags.Length, tags, ref boundary, memberData);
            }
            VerifyNewBoundaries(uf, operation, oldHandles, curves, chains);
            // Commit the current CAM definition after the UF geometry update so NX
            // processes its dependencies. No machining parameter is assigned here.
            PlanarMillingBuilder builder = part.CAMSetup.CAMOperationCollection.CreatePlanarMillingBuilder(operation);
            try { builder.Commit(); }
            finally { builder.Destroy(); }
            HashSet<Tag> after = AssignedCurves(uf, operation);
            HashSet<Tag> expected = new HashSet<Tag>(assigned);
            foreach (CurveInfo info in curves) expected.Add(info.Curve.Tag);
            if (!after.SetEquals(expected) || Boundaries(uf, operation.Tag).Length != oldBoundaries.Length + chains.Count)
                throw new InvalidOperationException("NX сохранил другой состав границ. Изменение отменяется.");
            committed = true;
            hasMark = false;
            try { uf.UiOnt.Refresh(); } catch { /* Geometry is committed; a tree redraw is optional. */ }
            int closed = chains.FindAll(delegate(PlanarCurveChains.Chain c) { return c.Closed; }).Count;
            Show(NXMessageBox.DialogType.Information, "Операция: " + operation.Name
                + "\nДобавлено кривых: " + curves.Count
                + "\nГраниц: " + chains.Count + " (открытых: " + (chains.Count - closed) + ", замкнутых: " + closed + ")"
                + (alreadyAssigned > 0 ? "\nУже были назначены: " + alreadyAssigned : "")
                + "\n\nДля новых границ: инструмент НА кривой, припуск 0."
                + "\nПроверьте границы и рассчитайте траекторию штатной командой NX."
                + "\nОтмена изменения: Ctrl+Z.");
        }
        catch (Exception ex)
        {
            string rollback = committed ? "Границы уже добавлены. Проверьте операцию; отмена доступна через Ctrl+Z." : "Операция не изменена.";
            if (hasMark)
            {
                try { session.UndoToMark(mark, null); session.DeleteUndoMark(mark, null); rollback = "Изменения этого запуска отменены."; }
                catch (Exception undoError) { rollback = "Не удалось полностью отменить изменение. Проверьте границы операции.\n" + undoError.Message; }
            }
            Show(NXMessageBox.DialogType.Error, ex.Message + "\n\n" + rollback);
        }
    }

    private static PlanarMilling SelectedOperation(UFSession uf, Part part)
    {
        int count; Tag[] tags;
        uf.UiOnt.AskSelectedNodes(out count, out tags);
        HashSet<Tag> seen = new HashSet<Tag>();
        List<CamOperation> operations = new List<CamOperation>();
        if (tags != null) foreach (Tag tag in tags)
        {
            TaggedObject item = NXObjectManager.Get(tag);
            CamOperation operation = item as CamOperation;
            if (operation != null && seen.Add(operation.Tag)) operations.Add(operation);
        }
        if (operations.Count != 1 || !(operations[0] is PlanarMilling))
            throw new InvalidOperationException("Закройте окно редактирования операции.\nВыделите одну существующую PLANAR_MILL в навигаторе операций и запустите скрипт.");
        if (operations[0].OwningPart == null || operations[0].OwningPart.Tag != part.Tag)
            throw new InvalidOperationException("Операция должна находиться в рабочей детали.");
        return (PlanarMilling)operations[0];
    }

    private static List<Curve> SelectCurves(UFSession uf, Part part)
    {
        Selection.MaskTriple[] masks = new Selection.MaskTriple[]
        {
            new Selection.MaskTriple(UFConstants.UF_feature_type, 0, 0),
            new Selection.MaskTriple(UFConstants.UF_sketch_type, 0, 0),
            new Selection.MaskTriple(UFConstants.UF_line_type, 0, 0),
            new Selection.MaskTriple(UFConstants.UF_circle_type, 0, 0),
            new Selection.MaskTriple(UFConstants.UF_conic_type, 0, 0),
            new Selection.MaskTriple(UFConstants.UF_spline_type, 0, 0)
        };
        TaggedObject[] objects;
        Selection.Response response = UI.GetUI().SelectionManager.SelectTaggedObjects(
            "Выберите надпись Text, эскиз или кривые рамкой", Title,
            Selection.SelectionScope.WorkPart, Selection.SelectionAction.ClearAndEnableSpecific,
            true, false, masks, out objects);
        if (response == Selection.Response.Cancel || response == Selection.Response.Back || objects == null || objects.Length == 0)
            return null;
        Dictionary<Tag, Curve> curves = new Dictionary<Tag, Curve>();
        foreach (TaggedObject item in objects)
        {
            NXObject nx = item as NXObject;
            if (nx == null || nx.IsOccurrence || nx.OwningPart == null || nx.OwningPart.Tag != part.Tag)
                throw new InvalidOperationException("Выбирайте геометрию рабочей детали. Компоненты сборки в V1 не поддерживаются.");
            NXOpen.Features.Text text = item as NXOpen.Features.Text;
            SketchFeature sketchFeature = item as SketchFeature;
            Sketch sketch = item as Sketch;
            if (text != null)
            {
                TextBuilder builder = part.Features.CreateTextBuilder(text);
                try
                {
                    if (builder.CanCreateBoundingBox)
                        throw new InvalidOperationException("У надписи включено создание ограничивающей рамки. Отключите рамку в параметрах Text, чтобы она не попала в гравировку.");
                }
                finally { builder.Destroy(); }
                AddObjects(uf, part, curves, text.GetEntities());
            }
            else if (sketchFeature != null) AddObjects(uf, part, curves, sketchFeature.Sketch.GetAllGeometry());
            else if (sketch != null) AddObjects(uf, part, curves, sketch.GetAllGeometry());
            else if (item is Curve) AddObjects(uf, part, curves, new NXObject[] { (Curve)item });
            else throw new InvalidOperationException("Поддерживаются надписи Text, эскизы и отдельные кривые. Выбран другой объект: " + nx.Name);
        }
        List<Curve> result = new List<Curve>(curves.Values);
        result.Sort(delegate(Curve a, Curve b) { return ((uint)a.Tag).CompareTo((uint)b.Tag); });
        return result;
    }

    private static void AddObjects(UFSession uf, Part part, Dictionary<Tag, Curve> output, NXObject[] objects)
    {
        foreach (NXObject item in objects)
        {
            Curve curve = item as Curve;
            if (curve == null) continue;
            if (curve.IsOccurrence || curve.OwningPart == null || curve.OwningPart.Tag != part.Tag)
                throw new InvalidOperationException("Кривая находится вне рабочей детали.");
            if (curve.IsReference) continue;
            Tag sketchTag;
            uf.Sket.AskSketchOfGeom(curve.Tag, out sketchTag);
            if (sketchTag != Tag.Null)
            {
                UFSket.ReferenceStatus status;
                uf.Sket.AskReferenceStatus(sketchTag, curve.Tag, out status);
                if (status == UFSket.ReferenceStatus.Reference) continue;
            }
            output[curve.Tag] = curve;
        }
    }

    private static CurveInfo InspectCurve(UFSession uf, Curve curve, double tolerance)
    {
        if (!(curve is Line) && !(curve is Arc) && !(curve is Spline) && !(curve is Ellipse))
            throw new InvalidOperationException("В V1 поддерживаются отрезки, дуги, окружности, эллипсы и сплайны.");
        if (curve.GetLength() <= tolerance)
            throw new InvalidOperationException("Обнаружена кривая нулевой длины. Уберите её из выбора.");
        CurveInfo info = new CurveInfo { Curve = curve };
        IntPtr evaluator = IntPtr.Zero;
        try
        {
            uf.Eval.Initialize2(curve.Tag, out evaluator);
            double[] limits = new double[2]; uf.Eval.AskLimits(evaluator, limits);
            for (int i = 0; i <= 16; i++)
            {
                double[] p = new double[3];
                uf.Eval.Evaluate(evaluator, 0, limits[0] + (limits[1] - limits[0]) * i / 16.0, p, new double[3]);
                V point = new V(p[0], p[1], p[2]);
                if (!point.Finite) throw new InvalidOperationException("Кривая содержит некорректные координаты.");
                info.PlanePoints.Add(point);
                if (i == 0) info.Start = point;
                if (i == 16) info.End = point;
            }
            Spline spline = curve as Spline;
            // Control points make the coplanarity check exact for supported splines.
            if (spline != null) foreach (Point3d pole in spline.Get3DPoles())
                info.PlanePoints.Add(new V(pole.X, pole.Y, pole.Z));
        }
        finally { if (evaluator != IntPtr.Zero) uf.Eval.Free(evaluator); }
        return info;
    }

    private static IntPtr[] Boundaries(UFSession uf, Tag tag)
    {
        int count; IntPtr[] data;
        uf.Cambnd.AskBoundaries(tag, CamGeomType.CamPart, out count, out data);
        if (count == 0) return new IntPtr[0];
        if (data == null || data.Length != count) throw new InvalidOperationException("Не удалось прочитать границы операции.");
        return data;
    }

    private static void EnsureNoInheritedBoundaries(UFSession uf, PlanarMilling operation)
    {
        bool inherited; uf.Cambnd.IsInherited(operation.Tag, CamGeomType.CamPart, out inherited);
        if (!inherited) return;
        if (Boundaries(uf, operation.Tag).Length > 0)
            throw new InvalidOperationException("Операция наследует границы детали. Сначала задайте для неё локальные границы в окне PLANAR_MILL.");
        NCGroup parent = operation.GetParent(CAMSetup.View.Geometry);
        HashSet<Tag> seen = new HashSet<Tag>();
        while (parent != null && seen.Add(parent.Tag))
        {
            if (Boundaries(uf, parent.Tag).Length > 0)
                throw new InvalidOperationException("У родительской геометрии есть наследуемые границы. Сначала задайте для этой операции локальные границы.");
            parent = parent.GetParent();
        }
    }

    private static Tag[] MemberTags(UFSession uf, IntPtr boundary)
    {
        int count; IntPtr[] members;
        uf.Cambnd.AskBoundaryItems(boundary, out count, out members);
        if (count == 0) return new Tag[0];
        if (members == null || members.Length != count) throw new InvalidOperationException("Не удалось прочитать элементы границы.");
        Tag[] result = new Tag[count];
        for (int i = 0; i < count; i++) uf.Cambnd.AskItemEntity(members[i], out result[i]);
        return result;
    }

    private static HashSet<Tag> AssignedCurves(UFSession uf, PlanarMilling operation)
    {
        HashSet<Tag> result = new HashSet<Tag>();
        foreach (IntPtr boundary in Boundaries(uf, operation.Tag))
            foreach (Tag tag in MemberTags(uf, boundary)) if (tag != Tag.Null) result.Add(tag);
        return result;
    }

    private static void VerifyNewBoundaries(UFSession uf, PlanarMilling operation, HashSet<IntPtr> old,
        List<CurveInfo> curves, List<PlanarCurveChains.Chain> chains)
    {
        IntPtr[] current = Boundaries(uf, operation.Tag);
        if (current.Length != old.Count + chains.Count || !old.IsSubsetOf(new HashSet<IntPtr>(current)))
            throw new InvalidOperationException("NX изменил существующие границы при добавлении. Изменение отменяется.");
        Dictionary<Tag, int> remaining = new Dictionary<Tag, int>();
        foreach (CurveInfo curve in curves) remaining.Add(curve.Curve.Tag, 1);
        Dictionary<Tag, int> chainByTag = new Dictionary<Tag, int>();
        for (int i = 0; i < chains.Count; i++)
            foreach (int index in chains[i].Items) chainByTag.Add(curves[index].Curve.Tag, i);
        HashSet<int> verifiedChains = new HashSet<int>();
        foreach (IntPtr boundary in current)
        {
            if (old.Contains(boundary)) continue;
            Tag[] tags = MemberTags(uf, boundary);
            int newIndex;
            if (tags.Length == 0 || !chainByTag.TryGetValue(tags[0], out newIndex) || !verifiedChains.Add(newIndex))
                throw new InvalidOperationException("NX сформировал неожиданную новую границу.");
            HashSet<Tag> expected = new HashSet<Tag>();
            foreach (int index in chains[newIndex].Items) expected.Add(curves[index].Curve.Tag);
            if (tags.Length != expected.Count || !expected.SetEquals(tags))
                throw new InvalidOperationException("NX сформировал неожиданный состав новой границы.");
            UFCambnd.BoundaryData data; uf.Cambnd.AskBoundaryData(boundary, out data);
            bool closed = data.boundary_type == CamBoundaryType.CamBoundaryTypeClosed;
            if (closed != chains[newIndex].Closed) throw new InvalidOperationException("NX изменил открытость границы.");
            int count; IntPtr[] members; uf.Cambnd.AskBoundaryItems(boundary, out count, out members);
            foreach (IntPtr member in members)
            {
                UFCambnd.AppData app; uf.Cambnd.AskItemAppData(member, out app);
                if (app.has_tool_position == 0 || app.tool_position != CamToolPosition.CamToolPositionOn || app.has_stock == 0 || Math.Abs(app.stock) > 1e-12)
                    throw new InvalidOperationException("NX не подтвердил положение НА кривой или нулевой припуск.");
            }
            foreach (Tag tag in tags)
            {
                if (!remaining.ContainsKey(tag) || --remaining[tag] < 0)
                    throw new InvalidOperationException("Кривая была назначена несколько раз.");
            }
        }
        foreach (int count in remaining.Values) if (count != 0) throw new InvalidOperationException("Назначены не все выбранные кривые.");
    }

    private static void Show(NXMessageBox.DialogType type, string message) { UI.GetUI().NXMessageBox.Show(Title, type, message); }
    public static int GetUnloadOption(string unused) { return (int)Session.LibraryUnloadOption.Immediately; }
}

// BEGIN PORTABLE CURVE CHAIN CORE
public static class PlanarCurveChains
{
    public struct V
    {
        public double X, Y, Z;
        public V(double x, double y, double z) { X = x; Y = y; Z = z; }
        public bool Finite { get { return !(double.IsNaN(X) || double.IsNaN(Y) || double.IsNaN(Z) || double.IsInfinity(X) || double.IsInfinity(Y) || double.IsInfinity(Z)); } }
        public double Length { get { return Math.Sqrt(Dot(this, this)); } }
        public static V operator -(V a, V b) { return new V(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static V operator *(V a, double b) { return new V(a.X * b, a.Y * b, a.Z * b); }
        public static double Dot(V a, V b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        public static V Cross(V a, V b) { return new V(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
        public V Unit() { double length = Length; if (length <= 1e-30) throw new InvalidOperationException("Не удалось определить плоскость кривых."); return this * (1 / length); }
    }
    public struct Ends { public V A, B; public Ends(V a, V b) { A = a; B = b; } }
    public sealed class Chain
    {
        public readonly List<int> Items = new List<int>();
        public readonly List<bool> Reversed = new List<bool>();
        public bool Closed;
    }
    public sealed class Frame { public V Origin, X, Y, Z; }
    private sealed class Node { public readonly List<V> Points = new List<V>(); public readonly List<int> Edges = new List<int>(); }
    private struct Cell : IEquatable<Cell>
    {
        public long X, Y, Z;
        public Cell(long x, long y, long z) { X = x; Y = y; Z = z; }
        public bool Equals(Cell o) { return X == o.X && Y == o.Y && Z == o.Z; }
        public override bool Equals(object o) { return o is Cell && Equals((Cell)o); }
        public override int GetHashCode() { unchecked { return X.GetHashCode() * 73856093 ^ Y.GetHashCode() * 19349663 ^ Z.GetHashCode() * 83492791; } }
    }
    public static Frame FindPlane(List<V> points, V preferredNormal, double tolerance)
    {
        if (points.Count < 2) throw new InvalidOperationException("Недостаточно точек для определения плоскости.");
        V origin = points[0], x = new V(); double longest = 0;
        foreach (V point in points)
        {
            if (!point.Finite) throw new InvalidOperationException("Некорректные координаты кривой.");
            V delta = point - origin; if (delta.Length > longest) { longest = delta.Length; x = delta; }
        }
        if (longest <= tolerance) throw new InvalidOperationException("Выбрана геометрия нулевого размера.");
        x = x.Unit(); V z = new V(); double widest = 0;
        foreach (V point in points) { V cross = V.Cross(x, point - origin); if (cross.Length > widest) { widest = cross.Length; z = cross; } }
        if (widest <= tolerance)
        {
            z = preferredNormal - x * V.Dot(preferredNormal, x);
            if (z.Length <= 1e-8) z = V.Cross(x, Math.Abs(x.X) < 0.8 ? new V(1, 0, 0) : new V(0, 1, 0));
        }
        z = z.Unit(); if (V.Dot(z, preferredNormal) < 0) z = z * -1;
        foreach (V point in points) if (Math.Abs(V.Dot(point - origin, z)) > tolerance)
            throw new InvalidOperationException("Кривые должны лежать в одной плоскости. Выберите одну плоскую надпись или эскиз.");
        return new Frame { Origin = origin, X = x, Y = V.Cross(z, x).Unit(), Z = z };
    }
    public static List<Chain> Build(List<Ends> curves, double tolerance)
    {
        if (!(tolerance > 0) || double.IsInfinity(tolerance)) throw new ArgumentException("tolerance");
        List<Node> nodes = new List<Node>(); Dictionary<Cell, List<int>> grid = new Dictionary<Cell, List<int>>();
        int[] a = new int[curves.Count], b = new int[curves.Count]; bool[] used = new bool[curves.Count];
        for (int i = 0; i < curves.Count; i++)
        {
            a[i] = FindNode(nodes, grid, curves[i].A, tolerance); b[i] = FindNode(nodes, grid, curves[i].B, tolerance);
            nodes[a[i]].Edges.Add(i); nodes[b[i]].Edges.Add(i);
        }
        List<Chain> result = new List<Chain>();
        for (int i = 0; i < nodes.Count; i++) if (nodes[i].Edges.Count != 2)
            foreach (int edge in nodes[i].Edges) if (!used[edge]) result.Add(Walk(nodes, a, b, used, i, edge));
        for (int i = 0; i < curves.Count; i++) if (!used[i]) result.Add(Walk(nodes, a, b, used, a[i], i));
        return result;
    }
    private static int FindNode(List<Node> nodes, Dictionary<Cell, List<int>> grid, V point, double tolerance)
    {
        if (!point.Finite) throw new ArgumentException("Non-finite curve endpoint");
        Cell cell = new Cell(checked((long)Math.Floor(point.X / tolerance)), checked((long)Math.Floor(point.Y / tolerance)), checked((long)Math.Floor(point.Z / tolerance)));
        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
        {
            List<int> bucket;
            if (!grid.TryGetValue(new Cell(cell.X + dx, cell.Y + dy, cell.Z + dz), out bucket)) continue;
            foreach (int index in bucket)
            {
                bool nearAll = true;
                foreach (V other in nodes[index].Points) if ((other - point).Length > tolerance) { nearAll = false; break; }
                if (nearAll) { nodes[index].Points.Add(point); return index; }
            }
        }
        int id = nodes.Count; Node node = new Node(); node.Points.Add(point); nodes.Add(node);
        List<int> target; if (!grid.TryGetValue(cell, out target)) { target = new List<int>(); grid.Add(cell, target); } target.Add(id);
        return id;
    }
    private static Chain Walk(List<Node> nodes, int[] a, int[] b, bool[] used, int start, int edge)
    {
        Chain chain = new Chain(); int node = start;
        while (!used[edge])
        {
            used[edge] = true; chain.Items.Add(edge); chain.Reversed.Add(a[edge] != node);
            node = a[edge] == node ? b[edge] : a[edge];
            if (node == start) { chain.Closed = true; break; }
            if (nodes[node].Edges.Count != 2) break;
            int next = -1; foreach (int candidate in nodes[node].Edges) if (!used[candidate]) { next = candidate; break; }
            if (next < 0) break; edge = next;
        }
        return chain;
    }
}
// END PORTABLE CURVE CHAIN CORE
