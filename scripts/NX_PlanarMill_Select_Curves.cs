// NX_PlanarMill_Select_Curves.cs - centerlines from planar engraving faces.
// SCRIPT_VERSION: V1.12
// Select an existing PLANAR_MILL, run, then select the flat bottoms of the letters.
// Reconstructs straight and curved centerlines; round/compact square dots use half-width circles.
// Free physical ends stop half the local stroke width before the terminal boundary.
// Rechecks corner spurs after merging nodes; preserves genuine short branches and islands.
// Restores sharp line/curve joints from the walls, preserving tangential fillets.
// Preserves through-strokes; separates junctions by 0.004-0.02 mm and inherits operation tolerances.
// Does not save files, generate a toolpath, or change the tool, depths, feeds or source body.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NXOpen;
using NXOpen.CAM;
using NXOpen.Features;
using NXOpen.UF;
using NXOpen.Utilities;
using CamOperation = NXOpen.CAM.Operation;
using V = PlanarCurveChains.V;
using P = EngravingCenterlines.P;
using B = EngravingCenterlines.Bezier;
using R = EngravingCenterlines.Region;

public class NXPlanarMillSelectCurves
{
    private const string SCRIPT_VERSION = "V1.12";
    private const string GroupAttribute = "NX_PM_AXIS_GROUP", ShapeAttribute = "NX_PM_AXIS_SHAPE";
    private const string SourceAttribute = "NX_PM_AXIS_FACE", CountAttribute = "NX_PM_AXIS_COUNT";
    private const string RevisionAttribute = "NX_PM_AXIS_REVISION", GeometryRevision = "12";
    private static readonly string Title = "Оси гравировки для PLANAR_MILL — " + SCRIPT_VERSION + " — "
        + Encoding.UTF8.GetString(Convert.FromBase64String("YnkgQFRvbnlfRm94eHggKFRHKQ=="));
    private sealed class CurveInfo
    {
        internal Curve Curve;
        internal V Start, End;
        internal readonly List<V> PlanePoints = new List<V>();
    }
    private sealed class EdgeData
    {
        internal string Key;
        internal List<V> Points;
    }
    private sealed class FaceData
    {
        internal string Key, Shape;
        internal Face SelectedFace;
        internal readonly List<EdgeData> Edges = new List<EdgeData>();
    }
    private sealed class FaceGroup
    {
        internal string Key, Shape;
        internal readonly List<FaceData> Faces = new List<FaceData>();
        internal readonly List<EngravingCenterlines.Result> Results = new List<EngravingCenterlines.Result>();
    }
    private sealed class Existing
    {
        internal string Shape, Revision;
        internal int Expected, Count;
        internal readonly HashSet<string> Faces = new HashSet<string>();
    }
    private sealed class FaceGeometryException : InvalidOperationException
    {
        internal readonly List<Face> Faces = new List<Face>();
        internal FaceGeometryException(FaceGroup group, int index, int count, Exception error)
            : base("Группа граней гравировки " + index + " из " + count + ":\n" + error.Message, error)
        { foreach (FaceData face in group.Faces) Faces.Add(face.SelectedFace); }
    }
    public static void Main(string[] args)
    {
        Session session = Session.GetSession(); UFSession uf = UFSession.GetUFSession(); Part part = session.Parts.Work;
        Session.UndoMarkId mark = default(Session.UndoMarkId); bool hasMark = false, committed = false;
        try
        {
            if (part == null || part.CAMSetup == null) throw new InvalidOperationException("Откройте CAM-проект в Manufacturing.");
            PlanarMilling operation = SelectedOperation(uf, part); EnsureNoInheritedBoundaries(uf, operation);
            HashSet<Tag> assigned = AssignedCurves(uf, operation);
            Face[] faces = SelectFaces(); if (faces == null) return;
            double unit = part.PartUnits == BasePart.Units.Inches ? 1 / 25.4 : 1;
            double tolerance = 0.002 * unit, joinTolerance = 0.000001 * unit;
            PlanarCurveChains.Frame frame; int skipped;
            List<FaceGroup> groups = Prepare(uf, part, faces, assigned, tolerance, joinTolerance, out frame, out skipped);
            if (groups.Count == 0) { Show(NXMessageBox.DialogType.Information, "Средние линии выбранных граней уже назначены этой операции."); return; }
            if (session.Parts.Work == null || session.Parts.Work.Tag != part.Tag)
                throw new InvalidOperationException("Рабочая деталь изменилась. Запустите скрипт заново.");
            EnsureNoInheritedBoundaries(uf, operation);
            if (!assigned.SetEquals(AssignedCurves(uf, operation))) throw new InvalidOperationException("Границы операции изменились во время выбора. Повторите запуск.");
            IntPtr[] oldBoundaries = Boundaries(uf, operation.Tag);
            List<Tag[]> oldMembers = new List<Tag[]>();
            foreach (IntPtr boundary in oldBoundaries) oldMembers.Add(MemberTags(uf, boundary));
            mark = session.SetUndoMark(Session.MarkVisibility.Visible, "Оси гравировки: " + operation.Name); hasMark = true;
            List<CurveInfo> curves = new List<CurveInfo>(); List<PlanarCurveChains.Chain> chains = new List<PlanarCurveChains.Chain>(); int dots = 0, junctions = 0;
            double minimumGap = double.MaxValue, maximumGap = 0;
            foreach (FaceGroup group in groups)
            {
                int expected = 0;
                foreach (EngravingCenterlines.Result result in group.Results)
                { expected += result.Circles.Count; foreach (List<EngravingCenterlines.Bezier> stroke in result.Curves) expected += stroke.Count; }
                foreach (EngravingCenterlines.Result result in group.Results)
                {
                    junctions += result.SeparatedJunctions;
                    if (result.SeparatedJunctions > 0) { minimumGap = Math.Min(minimumGap, result.JunctionGap / unit); maximumGap = Math.Max(maximumGap, result.JunctionGap / unit); }
                    for (int j = 0; j < result.Curves.Count; j++)
                    {
                        PlanarCurveChains.Chain chain = new PlanarCurveChains.Chain { Closed = result.Strokes[j].Closed };
                        foreach (EngravingCenterlines.Bezier data in result.Curves[j])
                        {
                            Curve curve = CreateCurve(uf, part, frame, data); Stamp(curve, group, expected);
                            AddCurve(uf, curve, curves, chain, joinTolerance);
                        }
                        CheckChain(curves, chain, joinTolerance); chains.Add(chain);
                    }
                    foreach (EngravingCenterlines.Circle circle in result.Circles)
                    {
                        Arc arc = part.Curves.CreateArc(World(frame, circle.Center),
                            new Vector3d(frame.X.X, frame.X.Y, frame.X.Z), new Vector3d(frame.Y.X, frame.Y.Y, frame.Y.Z), circle.Radius, 0, 2 * Math.PI);
                        Stamp(arc, group, expected);
                        PlanarCurveChains.Chain chain = new PlanarCurveChains.Chain { Closed = true };
                        AddCurve(uf, arc, curves, chain, joinTolerance); CheckChain(curves, chain, joinTolerance); chains.Add(chain); dots++;
                    }
                }
            }
            Plane boundaryPlane = part.Planes.CreatePlane(new Point3d(frame.Origin.X, frame.Origin.Y, frame.Origin.Z),
                new Vector3d(frame.Z.X, frame.Z.Y, frame.Z.Z), SmartObject.UpdateOption.WithinModeling);
            Matrix3x3 matrix = new Matrix3x3 { Xx = frame.X.X, Xy = frame.X.Y, Xz = frame.X.Z,
                Yx = frame.Y.X, Yy = frame.Y.Y, Yz = frame.Y.Z, Zx = frame.Z.X, Zy = frame.Z.Y, Zz = frame.Z.Z };
            PlanarMillingBuilder builder = part.CAMSetup.CAMOperationCollection.CreatePlanarMillingBuilder(operation);
            try
            {
                foreach (PlanarCurveChains.Chain chain in chains)
                {
                    NXObject[] objects = new NXObject[chain.Items.Count];
                    for (int i = 0; i < objects.Length; i++) objects[i] = curves[chain.Items[i]].Curve;
                    V startPoint = curves[chain.Items[0]].Start;
                    BoundarySetPlanarMill set = EngravingCamBoundary.Append(builder.PartBoundary, objects,
                        new Point3d(startPoint.X, startPoint.Y, startPoint.Z), matrix, boundaryPlane);
                    EngravingCamBoundary.Verify(set, chain.Closed);
                }
                builder.Commit();
            }
            finally { builder.Destroy(); }
            // Read saved boundary sets through the same NXOpen API used to create them.
            VerifyNewBoundaries(part, operation, oldMembers, curves, chains);
            HashSet<Tag> expectedTags = new HashSet<Tag>(assigned); foreach (CurveInfo curve in curves) expectedTags.Add(curve.Curve.Tag);
            if (!AssignedCurves(uf, operation).SetEquals(expectedTags) || Boundaries(uf, operation.Tag).Length != oldBoundaries.Length + chains.Count)
                throw new InvalidOperationException("NX сохранил другой состав границ. Изменение отменяется.");
            committed = true; hasMark = false;
            try { uf.UiOnt.Refresh(); } catch { }
            int closed = chains.FindAll(delegate(PlanarCurveChains.Chain c) { return c.Closed; }).Count;
            Show(NXMessageBox.DialogType.Information, "Операция: " + operation.Name + "\nСоздано кривых: " + curves.Count
                + "\nЦепочек кривых: " + chains.Count + " (разомкнутых: " + (chains.Count - closed) + ", замкнутых: " + closed + ")"
                + "\nТочек (окружностей D/2): " + dots + (skipped > 0 ? "\nРанее обработанных граней пропущено: " + skipped : "")
                + "\nСуществующих границ сохранено: " + oldBoundaries.Length
                + "\nРазделено узлов: " + junctions + (junctions > 0 ? "; зазор: " + minimumGap.ToString("0.###")
                    + (maximumGap > minimumGap * 1.001 ? "–" + maximumGap.ToString("0.###") : "") + " мм." : "")
                + "\n\nНовые кривые созданы в плоскости выбранных граней. Tool Position: ON; Stock: 0. Допуск задаётся в операции."
                + "\nПроверьте средние линии, уровни резания и рассчитайте траекторию штатной командой NX.\nОтмена: Ctrl+Z.");
        }
        catch (Exception ex)
        {
            string state = committed ? "Изменения уже внесены; отмена доступна через Ctrl+Z." : "Операция не изменена.";
            if (hasMark) try { session.UndoToMark(mark, null); session.DeleteUndoMark(mark, null); state = "Изменения этого запуска отменены."; }
                catch (Exception undoError) { state = "Не удалось полностью отменить изменения: " + undoError.Message; }
            // Highlight only the failed source group while its error is visible.
            // This is display feedback, not a partial commit or a geometry edit.
            var highlighted = new List<Face>(); FaceGeometryException geometry = ex as FaceGeometryException;
            if (geometry != null) foreach (Face face in geometry.Faces)
                try { face.Highlight(); highlighted.Add(face); } catch { }
            try { Show(NXMessageBox.DialogType.Error, ex.Message
                + (highlighted.Count > 0 ? "\n\nПроблемные грани подсвечены на время этого сообщения." : "") + "\n\n" + state); }
            finally { foreach (Face face in highlighted) try { face.Unhighlight(); } catch { } }
        }
        finally { try { uf.Ui.SetStatus(""); } catch { } }
    }
    private static Face[] SelectFaces()
    {
        TaggedObject[] objects;
        Selection.Response response = UI.GetUI().SelectionManager.SelectTaggedObjects(
            "Выберите плоские грани ДНА букв и круглых точек (не боковые стенки)", Title,
            Selection.SelectionScope.AnyInAssembly, Selection.SelectionAction.ClearAndEnableSpecific,
            false, false, new Selection.MaskTriple[] { new Selection.MaskTriple(UFConstants.UF_solid_type, 0, UFConstants.UF_UI_SEL_FEATURE_ANY_FACE) }, out objects);
        if (response == Selection.Response.Cancel || response == Selection.Response.Back || objects == null || objects.Length == 0) return null;
        Dictionary<Tag, Face> result = new Dictionary<Tag, Face>();
        foreach (TaggedObject item in objects)
        {
            Face face = item as Face;
            if (face == null || face.SolidFaceType != Face.FaceType.Planar)
                throw new InvalidOperationException("Выбирайте только плоские грани дна гравировки. Наклонные стенки, скругления и цилиндрические грани не подходят.");
            result[face.Tag] = face;
        }
        return new List<Face>(result.Values).ToArray();
    }
    private static List<FaceGroup> Prepare(UFSession uf, Part part, Face[] selected, HashSet<Tag> assigned,
        double tolerance, double joinTolerance, out PlanarCurveChains.Frame frame, out int skipped)
    {
        Dictionary<string, EdgeData> edgeCache = new Dictionary<string, EdgeData>(); List<FaceData> faces = new List<FaceData>(); List<V> all = new List<V>();
        for (int i = 0; i < selected.Length; i++)
        {
            uf.Ui.SetStatus("Чтение граней гравировки: " + (i + 1) + " / " + selected.Length);
            Face face = selected[i], prototype = face.IsOccurrence ? face.Prototype as Face : face;
            if (prototype == null) throw new InvalidOperationException("Не удалось прочитать исходную грань компонента.");
            if (!face.IsOccurrence && (face.OwningPart == null || face.OwningPart.Tag != part.Tag))
                throw new InvalidOperationException("Выбрана грань другой открытой детали. Выбирайте геометрию рабочей детали или её компонентов.");
            double[,] transform = null; string occurrence = "work";
            if (face.IsOccurrence)
            {
                if (Session.GetSession().Parts.Display.Tag != part.Tag)
                    throw new InvalidOperationException("Для выбора граней компонентов сделайте CAM-деталь одновременно рабочей и отображаемой.");
                Part sourcePart = prototype.OwningPart as Part;
                if (sourcePart == null || sourcePart.PartUnits != part.PartUnits)
                    throw new InvalidOperationException("Компонент и CAM-деталь должны иметь одинаковые единицы длины.");
                transform = new double[4, 4]; uf.Assem.AskTransformOfOcc(face.Tag, transform);
                occurrence = face.OwningComponent.JournalIdentifier;
            }
            FaceData data = new FaceData { SelectedFace = face, Key = Hash(occurrence + "|" + prototype.OwningPart.Leaf + "|" + prototype.JournalIdentifier) };
            List<string> shape = new List<string>();
            foreach (Edge edge in prototype.GetEdges())
            {
                string key = occurrence + "|" + ((uint)edge.Tag).ToString(CultureInfo.InvariantCulture); EdgeData entry;
                if (!edgeCache.TryGetValue(key, out entry))
                {
                    entry = new EdgeData { Key = key, Points = SampleEdge(uf, edge, transform, tolerance * 0.1) }; edgeCache.Add(key, entry);
                    all.AddRange(entry.Points);
                }
                data.Edges.Add(entry);
                StringBuilder points = new StringBuilder(); foreach (V p in entry.Points)
                    points.Append(p.X.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(p.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(p.Z.ToString("R", CultureInfo.InvariantCulture)).Append(';');
                shape.Add(points.ToString());
            }
            shape.Sort(StringComparer.Ordinal); data.Shape = Hash(string.Join("|", shape.ToArray())); faces.Add(data);
        }
        Matrix3x3 wcs = part.WCS.CoordinateSystem.Orientation.Element;
        frame = PlanarCurveChains.FindPlane(all, new V(wcs.Zx, wcs.Zy, wcs.Zz), tolerance * 0.25);
        List<FaceGroup> groups = GroupFaces(faces); Dictionary<string, Existing> existing = ReadExisting(assigned);
        List<FaceGroup> pending = new List<FaceGroup>(); skipped = 0;
        foreach (FaceGroup group in groups)
        {
            Existing previous;
            if (existing.TryGetValue(group.Key, out previous))
            {
                if (previous.Revision != GeometryRevision)
                    throw new InvalidOperationException("Для этих граней остались оси из прежней версии скрипта. Удалите их из границ операции и модели либо отмените прежний запуск через Undo, затем создайте оси заново.");
                if (previous.Shape != group.Shape || previous.Count != previous.Expected)
                    throw new InvalidOperationException("Для выбранной грани уже есть осевые кривые, но модель или состав границ изменился. Удалите прежний набор осей из операции и модели либо отмените предыдущий запуск, затем повторите расчёт.");
                skipped += group.Faces.Count; continue;
            }
            foreach (Existing old in existing.Values) foreach (FaceData face in group.Faces) if (old.Faces.Contains(face.Key))
                throw new InvalidOperationException("Часть выбранных граней уже использована в другом наборе осей. Выберите прежний набор целиком или удалите его перед новым расчётом.");
            pending.Add(group);
        }
        for (int i = 0; i < pending.Count; i++)
        {
            uf.Ui.SetStatus("Построение осей гравировки: " + (i + 1) + " / " + pending.Count);
            double unit = part.PartUnits == BasePart.Units.Inches ? 1 / 25.4 : 1;
            try
            {
                foreach (EngravingCenterlines.Region region in GroupRegions(pending[i], frame, joinTolerance))
                    pending[i].Results.Add(EngravingRoutes.Build(EngravingCenterlines.Build(region, tolerance), region, 0.02 * unit, tolerance));
            }
            catch (Exception ex) { throw new FaceGeometryException(pending[i], i + 1, pending.Count, ex); }
        }
        return pending;
    }
    private static List<V> SampleEdge(UFSession uf, Edge edge, double[,] transform, double tolerance)
    {
        IntPtr eval = IntPtr.Zero;
        try
        {
            uf.Eval.Initialize2(edge.Tag, out eval); double[] limits = new double[2]; uf.Eval.AskLimits(eval, limits);
            bool line; uf.Eval.IsLine(eval, out line); int parts = line ? 1 : 16;
            List<V> result = new List<V>(); V a = Evaluate(uf, eval, limits[0], transform); result.Add(a);
            for (int i = 0; i < parts; i++)
            {
                double t0 = limits[0] + (limits[1] - limits[0]) * i / parts, t1 = limits[0] + (limits[1] - limits[0]) * (i + 1) / parts;
                V b = Evaluate(uf, eval, t1, transform); Subdivide(uf, eval, transform, t0, t1, a, b, tolerance, result, 0); a = b;
            }
            return result;
        }
        finally { if (eval != IntPtr.Zero) uf.Eval.Free(eval); }
    }
    private static V Evaluate(UFSession uf, IntPtr evaluator, double t, double[,] m)
    {
        double[] a = new double[3]; uf.Eval.Evaluate(evaluator, 0, t, a, new double[3]);
        V p = m == null ? new V(a[0], a[1], a[2]) : new V(m[0, 0] * a[0] + m[0, 1] * a[1] + m[0, 2] * a[2] + m[0, 3],
            m[1, 0] * a[0] + m[1, 1] * a[1] + m[1, 2] * a[2] + m[1, 3], m[2, 0] * a[0] + m[2, 1] * a[1] + m[2, 2] * a[2] + m[2, 3]);
        if (!p.Finite) throw new InvalidOperationException("Ребро содержит некорректные координаты."); return p;
    }
    private static void Subdivide(UFSession uf, IntPtr evaluator, double[,] transform, double t0, double t1, V a, V b,
        double tolerance, List<V> points, int depth)
    {
        V middle = Evaluate(uf, evaluator, (t0 + t1) * 0.5, transform);
        double error = Math.Max(PointLine(middle, a, b), Math.Max(PointLine(Evaluate(uf, evaluator, t0 * 0.75 + t1 * 0.25, transform), a, b),
            PointLine(Evaluate(uf, evaluator, t0 * 0.25 + t1 * 0.75, transform), a, b)));
        if (error <= tolerance) { points.Add(b); return; }
        if (depth >= 18 || points.Count >= 50000) throw new InvalidOperationException("Не удалось точно прочитать сложное ребро грани.");
        Subdivide(uf, evaluator, transform, t0, (t0 + t1) * 0.5, a, middle, tolerance, points, depth + 1);
        Subdivide(uf, evaluator, transform, (t0 + t1) * 0.5, t1, middle, b, tolerance, points, depth + 1);
    }
    private static double PointLine(V p, V a, V b)
    {
        V d = b - a; double n = V.Dot(d, d), t = n > 0 ? Math.Max(0, Math.Min(1, V.Dot(p - a, d) / n)) : 0;
        return (p - a - d * t).Length;
    }
    private static int Root(int[] p, int i) { while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; } return i; }
    private static List<FaceGroup> GroupFaces(List<FaceData> faces)
    {
        int[] parent = new int[faces.Count]; for (int i = 0; i < parent.Length; i++) parent[i] = i;
        Dictionary<string, int> owner = new Dictionary<string, int>();
        for (int i = 0; i < faces.Count; i++) foreach (EdgeData edge in faces[i].Edges)
        { int j; if (owner.TryGetValue(edge.Key, out j)) parent[Root(parent, i)] = Root(parent, j); else owner.Add(edge.Key, i); }
        Dictionary<int, FaceGroup> groups = new Dictionary<int, FaceGroup>();
        for (int i = 0; i < faces.Count; i++) { int root = Root(parent, i); FaceGroup group; if (!groups.TryGetValue(root, out group)) { group = new FaceGroup(); groups.Add(root, group); } group.Faces.Add(faces[i]); }
        foreach (FaceGroup group in groups.Values)
        {
            group.Faces.Sort(delegate(FaceData a, FaceData b) { return StringComparer.Ordinal.Compare(a.Key, b.Key); });
            List<string> keys = new List<string>(), shapes = new List<string>(); foreach (FaceData face in group.Faces) { keys.Add(face.Key); shapes.Add(face.Shape); }
            group.Key = Hash(string.Join(";", keys.ToArray())); group.Shape = Hash(string.Join(";", shapes.ToArray()));
        }
        return new List<FaceGroup>(groups.Values);
    }
    private static List<EngravingCenterlines.Region> GroupRegions(FaceGroup group, PlanarCurveChains.Frame frame, double tolerance)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>(); Dictionary<string, EdgeData> entries = new Dictionary<string, EdgeData>();
        foreach (FaceData face in group.Faces) foreach (EdgeData edge in face.Edges)
        { int count; counts.TryGetValue(edge.Key, out count); counts[edge.Key] = count + 1; entries[edge.Key] = edge; }
        List<EdgeData> edges = new List<EdgeData>(); List<PlanarCurveChains.Ends> ends = new List<PlanarCurveChains.Ends>();
        foreach (KeyValuePair<string, int> entry in counts)
        {
            if (entry.Value > 2) throw new InvalidOperationException("Неоднозначное общее ребро выбранных граней.");
            if (entry.Value != 1) continue;
            EdgeData edge = entries[entry.Key]; edges.Add(edge); ends.Add(new PlanarCurveChains.Ends(edge.Points[0], edge.Points[edge.Points.Count - 1]));
        }
        List<List<P>> loops = new List<List<P>>();
        foreach (PlanarCurveChains.Chain chain in PlanarCurveChains.Build(ends, tolerance))
        {
            if (!chain.Closed) throw new InvalidOperationException("Контур выбранных граней не замкнут. Выберите полные плоские грани дна букв.");
            List<P> loop = new List<P>();
            for (int i = 0; i < chain.Items.Count; i++)
            {
                List<V> points = edges[chain.Items[i]].Points;
                for (int j = 0; j < points.Count - 1; j++)
                {
                    V p = points[chain.Reversed[i] ? points.Count - 1 - j : j] - frame.Origin;
                    loop.Add(new P(V.Dot(p, frame.X), V.Dot(p, frame.Y)));
                }
            }
            loops.Add(loop);
        }
        return EngravingCenterlines.Regions(loops);
    }
    private static Dictionary<string, Existing> ReadExisting(HashSet<Tag> assigned)
    {
        Dictionary<string, Existing> result = new Dictionary<string, Existing>();
        foreach (Tag tag in assigned)
        {
            NXObject item = NXObjectManager.Get(tag) as NXObject;
            if (item == null || !item.HasUserAttribute(GroupAttribute, NXObject.AttributeType.String, -1)) continue;
            string key = item.GetStringUserAttribute(GroupAttribute, -1), shape = item.GetStringUserAttribute(ShapeAttribute, -1);
            string revision = item.HasUserAttribute(RevisionAttribute, NXObject.AttributeType.String, -1) ? item.GetStringUserAttribute(RevisionAttribute, -1) : "1";
            int count = int.Parse(item.GetStringUserAttribute(CountAttribute, -1), CultureInfo.InvariantCulture); Existing entry;
            if (!result.TryGetValue(key, out entry)) { entry = new Existing { Shape = shape, Expected = count, Revision = revision }; result.Add(key, entry); }
            if (entry.Shape != shape || entry.Expected != count || entry.Revision != revision) throw new InvalidOperationException("Несогласованные метки ранее созданных осей гравировки.");
            entry.Count++;
            foreach (NXObject.AttributeInformation attribute in item.GetUserAttributes()) if (attribute.Title == SourceAttribute && attribute.Type == NXObject.AttributeType.String) entry.Faces.Add(attribute.StringValue);
        }
        return result;
    }
    private static void Stamp(Curve curve, FaceGroup group, int count)
    {
        curve.SetName("PM_ENGRAVING_CENTER");
        curve.SetUserAttribute(GroupAttribute, -1, group.Key, Update.Option.Now);
        curve.SetUserAttribute(ShapeAttribute, -1, group.Shape, Update.Option.Now);
        curve.SetUserAttribute(CountAttribute, -1, count.ToString(CultureInfo.InvariantCulture), Update.Option.Now);
        curve.SetUserAttribute(RevisionAttribute, -1, GeometryRevision, Update.Option.Now);
        for (int i = 0; i < group.Faces.Count; i++) curve.SetUserAttribute(SourceAttribute, i, group.Faces[i].Key, Update.Option.Now);
    }
    private static string Hash(string text)
    {
        // NX journal compilers use different standard assembly references in .NET Framework and .NET 10.
        Type type = typeof(object).Assembly.GetType("System.Security.Cryptography.SHA256", false);
        if (type == null) type = Type.GetType("System.Security.Cryptography.SHA256, System.Security.Cryptography", false);
        if (type == null) type = Type.GetType("System.Security.Cryptography.SHA256, System.Security.Cryptography.Algorithms", false);
        if (type == null) throw new InvalidOperationException("Не найден системный SHA-256 для проверки повторного выбора.");
        System.Reflection.MethodInfo factory = type.GetMethod("Create", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
        System.Reflection.MethodInfo compute = type.GetMethod("ComputeHash", new Type[] { typeof(byte[]) });
        if (factory == null || compute == null) throw new InvalidOperationException("Не удалось получить системный SHA-256.");
        object algorithm = factory.Invoke(null, null);
        using ((IDisposable)algorithm)
        { byte[] data = (byte[])compute.Invoke(algorithm, new object[] { Encoding.UTF8.GetBytes(text) }); return BitConverter.ToString(data).Replace("-", "").ToLowerInvariant(); }
    }
    private static Point3d World(PlanarCurveChains.Frame frame, P point)
    {
        return new Point3d(frame.Origin.X + frame.X.X * point.X + frame.Y.X * point.Y,
            frame.Origin.Y + frame.X.Y * point.X + frame.Y.Y * point.Y, frame.Origin.Z + frame.X.Z * point.X + frame.Y.Z * point.Y);
    }
    private static Curve CreateCurve(UFSession uf, Part part, PlanarCurveChains.Frame frame, EngravingCenterlines.Bezier curve)
    {
        Point3d a = World(frame, curve.A), d = World(frame, curve.D);
        if (curve.Line) return part.Curves.CreateLine(a, d);
        Point3d[] points = new Point3d[] { a, World(frame, curve.B), World(frame, curve.C), d }; double[,] poles = new double[4, 4];
        for (int i = 0; i < 4; i++) { poles[i, 0] = points[i].X; poles[i, 1] = points[i].Y; poles[i, 2] = points[i].Z; poles[i, 3] = 1; }
        UFCurve.Spline data = new UFCurve.Spline { num_poles = 4, order = 4, is_rational = 0,
            knots = new double[] { 0, 0, 0, 0, 1, 1, 1, 1 }, poles = poles, start_param = 0, end_param = 1 };
        Tag tag; int statesCount; UFCurve.State[] states; uf.Curve.CreateSpline(ref data, out tag, out statesCount, out states);
        Curve result = tag == Tag.Null ? null : NXObjectManager.Get(tag) as Curve;
        if (result == null || statesCount != 0) throw new InvalidOperationException("NX не подтвердил создание сплайна."); return result;
    }
    private static void AddCurve(UFSession uf, Curve curve, List<CurveInfo> curves, PlanarCurveChains.Chain chain, double tolerance)
    { chain.Items.Add(curves.Count); chain.Reversed.Add(false); curves.Add(InspectCurve(uf, curve, tolerance)); }
    private static void CheckChain(List<CurveInfo> curves, PlanarCurveChains.Chain chain, double tolerance)
    {
        for (int i = 1; i < chain.Items.Count; i++) if ((curves[chain.Items[i - 1]].End - curves[chain.Items[i]].Start).Length > tolerance)
            throw new InvalidOperationException("NX создал разрыв средней линии.");
        if (chain.Closed && (curves[chain.Items[0]].Start - curves[chain.Items[chain.Items.Count - 1]].End).Length > tolerance)
            throw new InvalidOperationException("NX не замкнул среднюю линию.");
    }
    // Existing operation selection, boundary validation and curve-inspection methods follow.

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

    private static void VerifyNewBoundaries(Part part, PlanarMilling operation, List<Tag[]> old,
        List<CurveInfo> curves, List<PlanarCurveChains.Chain> chains)
    {
        PlanarMillingBuilder reader = part.CAMSetup.CAMOperationCollection.CreatePlanarMillingBuilder(operation);
        try
        {
            BoundarySet[] sets = reader.PartBoundary.BoundaryList.GetContents();
            if (sets.Length != old.Count + chains.Count) throw new InvalidOperationException("NX изменил количество границ операции.");
            Dictionary<Tag, int> chainByTag = new Dictionary<Tag, int>();
            for (int i = 0; i < chains.Count; i++) foreach (int index in chains[i].Items) chainByTag.Add(curves[index].Curve.Tag, i);
            HashSet<int> verifiedChains = new HashSet<int>(), verifiedOld = new HashSet<int>();
            foreach (BoundarySet item in sets)
            {
                BoundarySetPlanarMill set = item as BoundarySetPlanarMill;
                if (set == null) throw new InvalidOperationException("NX вернул неподдерживаемый набор границ.");
                Tag[] tags = EngravingCamBoundary.Tags(set, true); bool previous = false;
                for (int i = 0; i < old.Count; i++) if (!verifiedOld.Contains(i) && SameMembers(tags, old[i]))
                { verifiedOld.Add(i); previous = true; break; }
                if (previous) continue;
                tags = EngravingCamBoundary.Tags(set);
                int index;
                if (tags.Length == 0 || !chainByTag.TryGetValue(tags[0], out index) || !verifiedChains.Add(index))
                    throw new InvalidOperationException("NX изменил состав цепочек гравировки.");
                Tag[] expected = new Tag[chains[index].Items.Count];
                for (int i = 0; i < expected.Length; i++) expected[i] = curves[chains[index].Items[i]].Curve.Tag;
                if (!SameMembers(tags, expected)) throw new InvalidOperationException("NX добавил или пропустил элемент границы гравировки.");
                EngravingCamBoundary.Verify(set, chains[index].Closed);
            }
            if (verifiedOld.Count != old.Count || verifiedChains.Count != chains.Count)
                throw new InvalidOperationException("NX не сохранил все прежние и новые границы.");
        }
        finally { reader.Destroy(); }
    }

    private static bool SameMembers(Tag[] a, Tag[] b)
    {
        if (a.Length != b.Length) return false;
        Dictionary<Tag, int> counts = new Dictionary<Tag, int>();
        foreach (Tag tag in a) { int count; counts.TryGetValue(tag, out count); counts[tag] = count + 1; }
        foreach (Tag tag in b) { int count; if (!counts.TryGetValue(tag, out count) || count == 0) return false; counts[tag] = count - 1; }
        return true;
    }

    private static void Show(NXMessageBox.DialogType type, string message) { UI.GetUI().NXMessageBox.Show(Title, type, message); }
    public static int GetUnloadOption(string unused) { return (int)Session.LibraryUnloadOption.Immediately; }
}

// BEGIN PORTABLE ENGRAVING ROUTING CORE
public static class EngravingRoutes
{
    private sealed class GapFitException : InvalidOperationException
    { internal GapFitException(string message) : base(message) { } }
    private sealed class Junction
    {
        internal P Point;
        internal double Radius, Limit;
        internal readonly List<int> Ports = new List<int>();
    }
    private sealed class Edge
    {
        internal List<B> Curves;
        internal int Start, End;
        internal bool Closed;
    }
    private sealed class Piece
    {
        internal P A, B;
        internal Piece(P a, P b) { A = a; B = b; }
    }
    public static EngravingCenterlines.Result Build(EngravingCenterlines.Result source, R region, double gap, double tolerance)
    {
        if (!(gap > tolerance * 4) || double.IsInfinity(gap)) throw new ArgumentException("The junction gap must exceed four fitting tolerances.");
        if (region.Loops.Count == 1) source = MergeSplitJunctions(source, region, tolerance);
        // Retry only a geometric gap-fit failure, always from the original curves. Never lose a stroke.
        // The minimum geometric gap is 0.004 mm, converted with the requested gap.
        double minimum = gap * 0.2, current = gap;
        while (true)
        {
            try { return BuildAtGap(source, region, current, tolerance); }
            catch (GapFitException ex)
            {
                if (current <= minimum * (1 + 1e-10))
                    throw new InvalidOperationException("Не удалось разделить узлы даже с минимальным зазором 0,004 мм. "
                        + ex.Message + " Выберите проблемный символ отдельно для проверки.");
                current = Math.Max(minimum, current * 0.5);
            }
        }
    }
    private static EngravingCenterlines.Result BuildAtGap(EngravingCenterlines.Result source, R region, double gap, double tolerance)
    {
        List<Junction> nodes = new List<Junction>(); List<Edge> edges = new List<Edge>();
        for (int i = 0; i < source.Curves.Count; i++)
        {
            List<B> path = source.Curves[i]; if (path.Count == 0) throw new InvalidOperationException("Пустая цепочка гравировки.");
            int a = Node(nodes, path[0].A, tolerance * 0.01), b = Node(nodes, path[path.Count - 1].D, tolerance * 0.01);
            nodes[a].Ports.Add(2 * i); nodes[b].Ports.Add(2 * i + 1);
            edges.Add(new Edge { Curves = path, Start = a, End = b, Closed = source.Strokes[i].Closed });
        }
        int[] pairs = new int[edges.Count * 2], parent = new int[edges.Count]; bool[] through = new bool[pairs.Length];
        for (int i = 0; i < pairs.Length; i++) pairs[i] = -1;
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        double flat = Math.Min(tolerance * 0.125, gap * 0.01);
        int junctions = 0;
        foreach (Junction node in nodes) if (node.Ports.Count > 2)
        {
            // Start with the clearance actually required by a perpendicular branch.
            // A blanket 1.5-gap radius consumes short arms even when the gap fits.
            // Oblique branches/elbows still grow below until their measured clearance fits.
            junctions++; node.Radius = gap + flat * 4; node.Limit = region.Distance(node.Point) * 0.85;
            if (node.Radius > node.Limit) throw new GapFitException("Зазор не помещается по ширине штриха.");
            PairPorts(node, edges, pairs, parent, region.Loops.Count > 1);
            // Retain one through-pair at the original node. Only the other arms need a gap from it.
            // At a symmetric plus both 90-degree elbows are rounded and separated as before.
            int keep = -1; double best = -0.5;
            foreach (int port in node.Ports) if (pairs[port] > port)
            {
                double score = P.Dot(Direction(edges, port), Direction(edges, pairs[port]));
                if (score < best) { best = score; keep = port; }
            }
            if (keep >= 0) { through[keep] = true; through[pairs[keep]] = true; }
        }
        List<List<B>> trimmed = null; Dictionary<int, B> links = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            trimmed = new List<List<B>>();
            for (int i = 0; i < edges.Count; i++)
            {
                Edge edge = edges[i];
                List<B> path = new List<B>(edge.Curves);
                if (nodes[edge.Start].Ports.Count > 2 && !through[2 * i]) path = TrimStart(path, nodes[edge.Start].Point, nodes[edge.Start].Radius, flat);
                if (nodes[edge.End].Ports.Count > 2 && !through[2 * i + 1]) path = Reverse(TrimStart(Reverse(path), nodes[edge.End].Point, nodes[edge.End].Radius, flat));
                trimmed.Add(path);
            }
            links = new Dictionary<int, B>();
            foreach (Junction node in nodes) if (node.Ports.Count > 2)
                foreach (int port in node.Ports) if (pairs[port] > port)
                {
                    SharpSeparateElbow(node, port, pairs[port], trimmed, through, region, flat);
                    P a = EndPoint(trimmed, port), b = EndPoint(trimmed, pairs[port]);
                    B connector = Join(a, node.Point, b, tolerance);
                    links.Add(port, connector); links.Add(pairs[port], Reverse(connector));
                }
            bool valid = true;
            foreach (Junction node in nodes) if (node.Ports.Count > 2 && !Separated(node, trimmed, pairs, links, gap, flat))
            {
                // Keep all nine earlier search radii after trying the smaller first
                // cut, so tight-angle junctions do not lose their previous reach.
                valid = false; node.Radius = Math.Max(gap * 1.5, node.Radius * 1.4);
                if (node.Radius > node.Limit) throw new GapFitException("Разделение узла выходит за ширину штриха.");
            }
            if (valid) break;
            if (attempt == 9) throw new GapFitException("Не удалось разделить узел без касаний.");
        }
        var result = new EngravingCenterlines.Result { WidthEstimate = source.WidthEstimate, SamplingStep = source.SamplingStep,
            BoundarySamples = source.BoundarySamples, SeparatedJunctions = junctions, JunctionGap = junctions > 0 ? gap : 0,
            WallReconstruction = source.WallReconstruction };
        result.Circles.AddRange(source.Circles); bool[] used = new bool[edges.Count];
        for (int port = 0; port < pairs.Length; port++) if (pairs[port] < 0 && !used[port / 2])
            Walk(port, edges, trimmed, pairs, links, used, result, tolerance);
        for (int i = 0; i < edges.Count; i++) if (!used[i])
            throw new InvalidOperationException("Соединение ветвей создало неожиданный замкнутый маршрут.");
        Validate(result, region, tolerance, flat);
        return result;
    }
    private static int Node(List<Junction> nodes, P point, double tolerance)
    {
        for (int i = 0; i < nodes.Count; i++) if ((nodes[i].Point - point).Length <= tolerance) return i;
        nodes.Add(new Junction { Point = point }); return nodes.Count - 1;
    }
    private sealed class Guide
    {
        internal int Port, Index;
        internal List<B> Path;
        internal B Line;
    }
    private static EngravingCenterlines.Result MergeSplitJunctions(EngravingCenterlines.Result source, R region, double tolerance)
    {
        // A K-shaped junction has two medial T nodes. Reconstruct one junction only if
        // four straight guides prove a through-stem and two arms on the same side of it.
        for (int pass = 0; pass < source.Curves.Count; pass++)
        {
            var nodes = new List<Junction>(); var edges = new List<Edge>();
            for (int i = 0; i < source.Curves.Count; i++)
            {
                var path = source.Curves[i]; int a = Node(nodes, path[0].A, tolerance * 0.01), b = Node(nodes, path[path.Count - 1].D, tolerance * 0.01);
                nodes[a].Ports.Add(2 * i); nodes[b].Ports.Add(2 * i + 1); edges.Add(new Edge { Curves = path, Start = a, End = b });
            }
            bool merged = false;
            for (int bridge = 0; bridge < edges.Count && !merged; bridge++)
            {
                Edge edge = edges[bridge]; Junction a = nodes[edge.Start], b = nodes[edge.End];
                if (edge.Start == edge.End || a.Ports.Count < 3 || b.Ports.Count < 3) continue;
                double width = region.Distance(a.Point) + region.Distance(b.Point);
                if ((a.Point - b.Point).Length > width * 1.5) continue;
                var bridgePieces = new List<Piece>();
                foreach (B c in edge.Curves) Flatten(c, tolerance * 0.125, bridgePieces, 0);
                P middle = (a.Point + b.Point) * 0.5; bool local = true;
                foreach (Piece piece in bridgePieces)
                    if ((piece.A - middle).Length > width || (piece.B - middle).Length > width) local = false;
                if (!local) continue;
                var ports = new List<int>(a.Ports); ports.AddRange(b.Ports); var guides = new List<Guide>(); bool valid = true;
                foreach (int port in ports) if (port / 2 != bridge)
                {
                    var path = port % 2 == 0 ? edges[port / 2].Curves : Reverse(edges[port / 2].Curves);
                    int best = -1; double longest = width * 0.25, before = 0;
                    for (int i = 0; i < path.Count; i++)
                    {
                        B c = path[i]; if (before > width * 2) break; double size = (c.D - c.A).Length;
                        if (c.Line && size > longest) { best = i; longest = size; }
                        before += size;
                    }
                    if (best < 0) { valid = false; break; }
                    guides.Add(new Guide { Port = port, Index = best, Path = path, Line = path[best] });
                }
                if (!valid || guides.Count != 4) continue;
                P center = new P(); bool found = false;
                double concurrence = Math.Max(tolerance * 0.25, width * 0.04);
                for (int i = 0; i < guides.Count && !found; i++) for (int j = i + 1; j < guides.Count && !found; j++)
                {
                    B x = guides[i].Line, y = guides[j].Line;
                    if (P.Dot((x.D - x.A).Unit(), (y.D - y.A).Unit()) > -0.995) continue;
                    P stem = (y.D - x.D).Unit();
                    if (Math.Abs(P.Cross(stem, x.A - x.D)) > concurrence
                        || Math.Abs(P.Cross(stem, y.A - x.D)) > concurrence) continue;
                    int k = -1, m = -1;
                    for (int n = 0; n < guides.Count; n++) if (n != i && n != j) { if (k < 0) k = n; else m = n; }
                    B armA = guides[k].Line, armB = guides[m].Line;
                    if (P.Cross(stem, armA.D - x.D) * P.Cross(stem, armB.D - x.D) <= 0) continue;
                    P u = (armA.D - armA.A).Unit(), v = (armB.D - armB.A).Unit(); double cross = P.Cross(u, v);
                    if (Math.Abs(cross) < 0.2) continue;
                    P apex = armA.A + u * (P.Cross(armB.A - armA.A, v) / cross);
                    center = x.D + stem * P.Dot(apex - x.D, stem);
                    if ((apex - center).Length > concurrence) continue;
                    found = true;
                }
                if (!found || (center - a.Point).Length > width * 1.25 || (center - b.Point).Length > width * 1.25 || !region.Inside(center)) continue;
                foreach (Guide guide in guides)
                {
                    P d = (guide.Line.D - guide.Line.A).Unit();
                    if (Math.Abs(P.Cross(d, center - guide.Line.A)) > concurrence || !region.SegmentInside(center, guide.Line.D, tolerance)) valid = false;
                }
                if (!valid) continue;
                var replacements = new Dictionary<int, List<B>>();
                foreach (Guide guide in guides)
                {
                    if (replacements.ContainsKey(guide.Port / 2)) { valid = false; break; }
                    var path = new List<B> { new B { A = center, D = guide.Line.D, Line = true } };
                    for (int i = guide.Index + 1; i < guide.Path.Count; i++) path.Add(guide.Path[i]);
                    replacements.Add(guide.Port / 2, guide.Port % 2 == 0 ? path : Reverse(path));
                }
                if (!valid) continue;
                var next = new EngravingCenterlines.Result { WidthEstimate = source.WidthEstimate, SamplingStep = source.SamplingStep,
                    BoundarySamples = source.BoundarySamples, WallReconstruction = source.WallReconstruction };
                next.Circles.AddRange(source.Circles);
                for (int i = 0; i < source.Curves.Count; i++) if (i != bridge)
                {
                    List<B> path; if (!replacements.TryGetValue(i, out path)) path = source.Curves[i];
                    next.Curves.Add(path); next.Strokes.Add(source.Strokes[i]);
                }
                source = next; merged = true;
            }
            if (!merged) break;
        }
        return source;
    }
    private static int Root(int[] p, int i) { while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; } return i; }
    private static void SharpSeparateElbow(Junction node, int a, int b, List<List<B>> paths,
        bool[] through, R region, double flat)
    {
        // K-like junction: a continuous straight stem and two straight arms on one side.
        // Give the two arms ONE sharp vertex clear of the stem, retaining their terminal ends.
        // Curved branches and the two-elbow layout of a plus retain their own construction.
        if (through[a] || through[b]) return;
        int stem = -1; foreach (int port in node.Ports) if (through[port]) { stem = port; break; }
        if (stem < 0) return;
        List<B> stemPath = paths[stem / 2];
        if (!(stem % 2 == 0 ? stemPath[0] : stemPath[stemPath.Count - 1]).Line) return;
        List<B> pa = paths[a / 2], pb = paths[b / 2];
        int ia = a % 2 == 0 ? 0 : pa.Count - 1, ib = b % 2 == 0 ? 0 : pb.Count - 1;
        B ca = pa[ia], cb = pb[ib]; if (!ca.Line || !cb.Line) return;
        P farA = a % 2 == 0 ? ca.D : ca.A, farB = b % 2 == 0 ? cb.D : cb.A;
        P u = (farA - node.Point).Unit(), v = (farB - node.Point).Unit();
        if (Math.Abs(P.Cross(u, v)) < 0.15) return;
        P bisector = (u + v).Unit(), apex = node.Point + bisector * node.Radius;
        if ((farA - node.Point).Length <= node.Radius * 2 || (farB - node.Point).Length <= node.Radius * 2
            || !region.SegmentInside(apex, farA, flat) || !region.SegmentInside(apex, farB, flat))
            throw new GapFitException("Недостаточно места для углового соединения двух прямых штрихов.");
        // Do not mutate source Bezier instances: gap retries start with the original geometry.
        pa[ia] = new B { A = a % 2 == 0 ? apex : farA, D = a % 2 == 0 ? farA : apex, Line = true };
        pb[ib] = new B { A = b % 2 == 0 ? apex : farB, D = b % 2 == 0 ? farB : apex, Line = true };
    }
    private static P Direction(List<Edge> edges, int port)
    {
        List<B> p = edges[port / 2].Curves; B b = port % 2 == 0 ? p[0] : Reverse(p[p.Count - 1]);
        P d = b.Line ? b.D - b.A : b.B - b.A; if (d.Length < 1e-12) d = b.D - b.A;
        return d.Unit();
    }
    private static void PairPorts(Junction node, List<Edge> edges, int[] pairs, int[] parent, bool hasHoles)
    {
        List<int> available = new List<int>(node.Ports);
        available.Sort(delegate(int a, int b) { P x = Direction(edges, a), y = Direction(edges, b); return Math.Atan2(x.Y, x.X).CompareTo(Math.Atan2(y.Y, y.X)); });
        // Without a loop, adjacent angular pairs give non-crossing elbows (+) or a stem and V (K).
        // At loops, retain one through-stroke and leave the other arms short, opening P/4/Phi junctions.
        while (available.Count >= 2)
        {
            int bestA = -1, bestB = -1; double cost = double.MaxValue, support = -1;
            for (int i = 0; i < available.Count; i++) for (int j = i + 1; j < available.Count; j++)
            {
                if (!hasHoles && available.Count > 3 && j != i + 1 && !(i == 0 && j == available.Count - 1)) continue;
                int a = available[i], b = available[j]; if (Root(parent, a / 2) == Root(parent, b / 2)) continue;
                double score = 1 + P.Dot(Direction(edges, a), Direction(edges, b));
                double length = StraightReach(edges, a) + StraightReach(edges, b);
                if (score < cost - 1e-8 || (Math.Abs(score - cost) <= 1e-8 && length > support))
                { cost = score; support = length; bestA = a; bestB = b; }
            }
            if (bestA < 0) break;
            pairs[bestA] = bestB; pairs[bestB] = bestA; parent[Root(parent, bestA / 2)] = Root(parent, bestB / 2);
            available.Remove(bestA); available.Remove(bestB);
            if (hasHoles || node.Ports.Count == 3) break;
        }
    }
    private static double StraightReach(List<Edge> edges, int port)
    {
        List<B> path = port % 2 == 0 ? edges[port / 2].Curves : Reverse(edges[port / 2].Curves);
        P direction = Direction(edges, port); double length = 0;
        foreach (B curve in path)
        {
            if (!curve.Line || P.Dot((curve.D - curve.A).Unit(), direction) < 1 - 1e-8) break;
            length += (curve.D - curve.A).Length;
        }
        return length;
    }
    private static P EndPoint(List<List<B>> paths, int port)
    { List<B> p = paths[port / 2]; return port % 2 == 0 ? p[0].A : p[p.Count - 1].D; }
    private static B Reverse(B b) { return new B { A = b.D, B = b.C, C = b.B, D = b.A, Line = b.Line }; }
    private static List<B> Reverse(List<B> path)
    { var result = new List<B>(); for (int i = path.Count - 1; i >= 0; i--) result.Add(Reverse(path[i])); return result; }
    private static void Split(B b, double t, out B left, out B right)
    {
        if (b.Line) { P m = b.At(t); left = new B { A = b.A, D = m, Line = true }; right = new B { A = m, D = b.D, Line = true }; return; }
        P ab = b.A * (1 - t) + b.B * t, bc = b.B * (1 - t) + b.C * t, cd = b.C * (1 - t) + b.D * t;
        P abc = ab * (1 - t) + bc * t, bcd = bc * (1 - t) + cd * t, middle = abc * (1 - t) + bcd * t;
        left = new B { A = b.A, B = ab, C = abc, D = middle }; right = new B { A = middle, B = bcd, C = cd, D = b.D };
    }
    private static List<B> TrimStart(List<B> path, P center, double radius, double flat)
    {
        for (int i = 0; i < path.Count; i++)
        {
            B b = path[i]; double previous = 0;
            // Control-polygon length bounds the number of probes even for a looping cubic.
            double length = b.Line ? (b.D - b.A).Length : (b.B - b.A).Length + (b.C - b.B).Length + (b.D - b.C).Length;
            int count = Math.Max(8, (int)Math.Ceiling(length / Math.Max(radius * 0.2, flat)));
            if (count > 100000) throw new InvalidOperationException("Слишком длинный сегмент для разделения узла.");
            for (int k = 1; k <= count; k++)
            {
                double t = (double)k / count;
                if ((b.At(t) - center).Length >= radius)
                {
                    double low = previous, high = t;
                    for (int n = 0; n < 45; n++) { double m = (low + high) * 0.5; if ((b.At(m) - center).Length < radius) low = m; else high = m; }
                    B left, right; Split(b, high, out left, out right); var result = new List<B>();
                    if ((right.D - right.A).Length > flat * 0.001 || !right.Line) result.Add(right);
                    for (int j = i + 1; j < path.Count; j++) result.Add(path[j]);
                    if (result.Count == 0) break; return result;
                }
                previous = t;
            }
        }
        throw new GapFitException("Недостаточно длины короткой ветви для разделения.");
    }
    private static B Join(P a, P center, P b, double tolerance)
    {
        if ((a - b).Length < tolerance * 0.001) return new B { A = a, D = b, Line = true };
        var chord = new EngravingCenterlines.Segment(a, b);
        if ((center - EngravingCenterlines.Closest(chord, center)).Length < tolerance * 0.001)
            return new B { A = a, D = b, Line = true };
        return new B { A = a, B = a + (center - a) * (2.0 / 3), C = b + (center - b) * (2.0 / 3), D = b };
    }
    private static List<Piece> Nearby(List<List<B>> paths, int port, Junction node, double flat)
    {
        var result = new List<Piece>(); List<B> curves = port % 2 == 0 ? paths[port / 2] : Reverse(paths[port / 2]);
        foreach (B b in curves)
        {
            var pieces = new List<Piece>(); Flatten(b, flat, pieces, 0);
            foreach (Piece piece in pieces) { result.Add(piece); if ((piece.B - node.Point).Length >= node.Radius * 2.5) return result; }
        }
        return result;
    }
    private static bool Separated(Junction node, List<List<B>> paths, int[] pairs, Dictionary<int, B> links, double gap, double flat)
    {
        var groups = new List<List<Piece>>(); var seen = new HashSet<int>();
        foreach (int port in node.Ports) if (seen.Add(port))
        {
            List<Piece> pieces = Nearby(paths, port, node, flat);
            if (pairs[port] >= 0) { seen.Add(pairs[port]); pieces.AddRange(Nearby(paths, pairs[port], node, flat)); Flatten(links[port], flat, pieces, 0); }
            groups.Add(pieces);
        }
        for (int i = 0; i < groups.Count; i++) for (int j = i + 1; j < groups.Count; j++)
            foreach (Piece a in groups[i]) foreach (Piece b in groups[j]) if (Distance(a, b) < gap + flat * 2) return false;
        return true;
    }
    private static void Walk(int firstPort, List<Edge> edges, List<List<B>> paths, int[] pairs, Dictionary<int, B> links,
        bool[] used, EngravingCenterlines.Result result, double tolerance)
    {
        var curves = new List<B>(); int port = firstPort, count = 0; bool closed = false;
        while (true)
        {
            int edge = port / 2; if (used[edge]) throw new InvalidOperationException("Повторный проход по штриху гравировки.");
            used[edge] = true; count++; curves.AddRange(port % 2 == 0 ? paths[edge] : Reverse(paths[edge]));
            int exitPort = port ^ 1;
            if (pairs[exitPort] < 0) { closed = count == 1 && edges[edge].Closed && pairs[port] < 0 && (curves[0].A - curves[curves.Count - 1].D).Length < 1e-12; break; }
            B link = links[exitPort]; if (!link.Line || (link.D - link.A).Length > 1e-12) curves.Add(link);
            port = pairs[exitPort];
        }
        var stroke = new EngravingCenterlines.Stroke { Closed = closed };
        // A through-stroke remains one geometric line, regardless of its medial graph nodes.
        for (int i = curves.Count - 1; i > 0; i--)
        {
            B a = curves[i - 1], b = curves[i]; P direction = b.D - a.A;
            var chord = new EngravingCenterlines.Segment(a.A, b.D);
            double t = P.Dot(a.D - a.A, direction), length2 = P.Dot(direction, direction);
            if (a.Line && b.Line && t > 0 && t < length2
                && (a.D - EngravingCenterlines.Closest(chord, a.D)).Length <= tolerance * 0.01
                && (b.A - EngravingCenterlines.Closest(chord, b.A)).Length <= tolerance * 0.01)
            { curves[i - 1] = new B { A = a.A, D = b.D, Line = true }; curves.RemoveAt(i); }
        }
        stroke.Points.Add(curves[0].A); foreach (B b in curves) stroke.Points.Add(b.D);
        result.Curves.Add(curves); result.Strokes.Add(stroke);
    }
    private static void Flatten(B b, double tolerance, List<Piece> output, int depth)
    {
        var chord = new EngravingCenterlines.Segment(b.A, b.D);
        double error = b.Line ? 0 : Math.Max((b.B - EngravingCenterlines.Closest(chord, b.B)).Length, (b.C - EngravingCenterlines.Closest(chord, b.C)).Length);
        if (error <= tolerance) { if ((b.D - b.A).Length > tolerance * 0.00001) output.Add(new Piece(b.A, b.D)); return; }
        if (depth >= 24) throw new InvalidOperationException("Не удалось проверить пересечения кривых.");
        B left, right; Split(b, 0.5, out left, out right); Flatten(left, tolerance, output, depth + 1); Flatten(right, tolerance, output, depth + 1);
    }
    private static double Distance(Piece a, Piece b)
    {
        P u = a.B - a.A, v = b.B - b.A; double denominator = P.Cross(u, v);
        // Nearly parallel segments have a noisy determinant. Do not turn rounding error into
        // a fictitious intersection; their endpoint-to-segment distance is stable in this case.
        if (Math.Abs(denominator) > Math.Max(1e-30, u.Length * v.Length * 1e-12))
        {
            double t = P.Cross(b.A - a.A, v) / denominator, s = P.Cross(b.A - a.A, u) / denominator;
            if (t >= 0 && t <= 1 && s >= 0 && s <= 1) return 0;
        }
        var x = new EngravingCenterlines.Segment(a.A, a.B); var y = new EngravingCenterlines.Segment(b.A, b.B);
        return Math.Min(Math.Min((a.A - EngravingCenterlines.Closest(y, a.A)).Length, (a.B - EngravingCenterlines.Closest(y, a.B)).Length),
            Math.Min((b.A - EngravingCenterlines.Closest(x, b.A)).Length, (b.B - EngravingCenterlines.Closest(x, b.B)).Length));
    }
    public static void Validate(EngravingCenterlines.Result result, R region, double tolerance, double flat)
    {
        var paths = new List<List<Piece>>();
        foreach (List<B> curves in result.Curves)
        {
            var pieces = new List<Piece>();
            for (int i = 0; i < curves.Count; i++)
            {
                if (i > 0 && (curves[i - 1].D - curves[i].A).Length > tolerance * 0.001) throw new InvalidOperationException("Разрыв внутри цепочки гравировки.");
                Flatten(curves[i], flat, pieces, 0);
            }
            foreach (Piece p in pieces) if (!region.SegmentInside(p.A, p.B, flat)) throw new InvalidOperationException("Разделённая ось вышла за границу буквы.");
            paths.Add(pieces);
        }
        for (int i = 0; i < paths.Count; i++)
        {
            List<Piece> p = paths[i];
            for (int a = 0; a < p.Count; a++) for (int b = a + 2; b < p.Count; b++)
            {
                if (result.Strokes[i].Closed && a == 0 && b == p.Count - 1) continue;
                if (Distance(p[a], p[b]) < flat * 0.01) throw new InvalidOperationException("Самопересечение цепочки гравировки.");
            }
            for (int j = i + 1; j < paths.Count; j++) foreach (Piece a in p) foreach (Piece b in paths[j])
                if (Distance(a, b) < tolerance + flat * 2) throw new InvalidOperationException("Цепочки гравировки пересекаются или касаются друг друга.");
        }
    }
}
// END PORTABLE ENGRAVING ROUTING CORE

// BEGIN ENGRAVING CAM BOUNDARY ADAPTER
public static class EngravingCamBoundary
{
    // BoundaryMemberSetPlanarMill uses its own integer convention: 0 = On.
    // This is also the setting used by the recorded journal in PlanarCHIK.cs; UF uses a different enum.
    private const int MemberOn = 0;
    public static BoundarySetPlanarMill Append(BoundaryPlanarMill target, NXObject[] curves, Point3d start, Matrix3x3 matrix, Plane plane)
    {
        if (curves == null || curves.Length == 0) throw new ArgumentException("Invalid engraving boundary.");
        BoundarySetPlanarMill set = target.CreateBoundarySetPlanarMill();
        set.PlaneType = BoundarySet.PlaneTypes.UserDefined; set.Plane = plane;
        set.BoundaryType = BoundarySet.BoundaryTypes.Open; set.ToolPosition = BoundarySet.ToolPositionTypes.On;
        set.ToolSide = BoundarySet.ToolSideTypes.InsideOrLeft;
        BoundaryPlanarMill.ToolPositionTypes initial = target.InitialToolPosition;
        try { target.InitialToolPosition = BoundaryPlanarMill.ToolPositionTypes.On; set.AppendCurves(curves, start, matrix); }
        finally { target.InitialToolPosition = initial; }
        // AppendCurves can infer defaults. Apply the engraving settings after the members exist.
        set.BoundaryType = BoundarySet.BoundaryTypes.Open; set.ToolPosition = BoundarySet.ToolPositionTypes.On;
        set.CustomStock = true; set.Stock = 0; set.CustomTolerance = false;
        foreach (BoundaryMemberSet item in set.BoundaryMemberList.GetContents())
        {
            BoundaryMemberSetPlanarMill member = item as BoundaryMemberSetPlanarMill;
            if (member == null) throw new InvalidOperationException("NX создал неподдерживаемый элемент границы.");
            member.ToolPosition = MemberOn; member.CustomStock = true; member.Stock = 0;
            member.CustomTolerance = false;
        }
        target.BoundaryList.Append(set); return set;
    }
    public static Tag[] Tags(BoundarySetPlanarMill set, bool allowMissing = false)
    {
        BoundaryMemberSet[] members = set.BoundaryMemberList.GetContents(); Tag[] tags = new Tag[members.Length];
        for (int i = 0; i < tags.Length; i++)
        {
            if (members[i] == null || members[i].Geometry == null)
            {
                if (!allowMissing) throw new InvalidOperationException("NX добавил элемент границы без исходной кривой.");
                tags[i] = Tag.Null;
            }
            else tags[i] = members[i].Geometry.Tag;
        }
        return tags;
    }
    public static void Verify(BoundarySetPlanarMill set, bool geometricLoop)
    {
        // A complete circle may be reported as Closed. It is safe only for an actually closed source chain.
        // Open chains must stay Open: otherwise CAM could insert an implicit closing cut.
        if (set.BoundaryType != BoundarySet.BoundaryTypes.Open && !(geometricLoop && set.BoundaryType == BoundarySet.BoundaryTypes.Closed))
            throw new InvalidOperationException("NX замкнул разомкнутую цепочку гравировки. Тип границы: " + set.BoundaryType + ".");
        CheckStock(set.CustomStock, set.Stock);
        BoundaryMemberSet[] members = set.BoundaryMemberList.GetContents();
        if (members.Length == 0) throw new InvalidOperationException("NX сохранил пустую границу гравировки.");
        foreach (BoundaryMemberSet item in members)
        {
            BoundaryMemberSetPlanarMill member = item as BoundaryMemberSetPlanarMill;
            if (member == null || member.ToolPosition != MemberOn)
                throw new InvalidOperationException("NX не сохранил Tool Position = On у элемента границы.");
            CheckStock(member.CustomStock, member.Stock);
        }
    }
    private static void CheckStock(bool customStock, double stock)
    {
        if (!customStock || double.IsNaN(stock) || Math.Abs(stock) > 1e-12) throw new InvalidOperationException("NX не сохранил нулевой припуск границы гравировки.");
    }
}
// END ENGRAVING CAM BOUNDARY ADAPTER


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

// BEGIN PORTABLE ENGRAVING CENTERLINE CORE
public static class EngravingCenterlines
{
    public struct P
    {
        public double X, Y;
        public P(double x, double y) { X = x; Y = y; }
        public double Length { get { return Math.Sqrt(X * X + Y * Y); } }
        public static P operator +(P a, P b) { return new P(a.X + b.X, a.Y + b.Y); }
        public static P operator -(P a, P b) { return new P(a.X - b.X, a.Y - b.Y); }
        public static P operator *(P a, double s) { return new P(a.X * s, a.Y * s); }
        public static double Dot(P a, P b) { return a.X * b.X + a.Y * b.Y; }
        public static double Cross(P a, P b) { return a.X * b.Y - a.Y * b.X; }
        public P Unit() { double d = Length; return d > 1e-20 ? this * (1 / d) : new P(); }
    }
    public sealed class Segment
    {
        public P A, B;
        public Segment(P a, P b) { A = a; B = b; }
    }
    public sealed class Stroke
    {
        public readonly List<P> Points = new List<P>();
        public bool Closed;
        internal bool ContractedJunction;
        internal readonly List<P> CurvedCorners = new List<P>();
        public readonly HashSet<int> Corners = new HashSet<int>();
    }
    public sealed class Bezier
    {
        public P A, B, C, D;
        public bool Line;
        public P At(double t)
        {
            if (Line) return A * (1 - t) + D * t;
            double s = 1 - t;
            return A * (s * s * s) + B * (3 * s * s * t) + C * (3 * s * t * t) + D * (t * t * t);
        }
    }
    public sealed class Circle
    {
        public P Center;
        public double Radius;
    }
    public sealed class Result
    {
        public readonly List<Stroke> Strokes = new List<Stroke>();
        public readonly List<List<Bezier>> Curves = new List<List<Bezier>>();
        public readonly List<Circle> Circles = new List<Circle>();
        public int BoundarySamples, SeparatedJunctions;
        public double JunctionGap;
        public double WidthEstimate, SamplingStep;
        public bool WallReconstruction;
    }
    public sealed class Region
    {
        public readonly List<List<P>> Loops = new List<List<P>>();
        public readonly List<Segment> Edges = new List<Segment>();
        public double Area, Perimeter;
        public P Min, Max;
        public Region(List<List<P>> loops)
        {
            if (loops == null || loops.Count == 0) throw new InvalidOperationException("Нет контуров грани.");
            Min = new P(double.MaxValue, double.MaxValue); Max = new P(-double.MaxValue, -double.MaxValue);
            for (int k = 0; k < loops.Count; k++)
            {
                List<P> loop = new List<P>(loops[k]);
                if (loop.Count > 1 && (loop[0] - loop[loop.Count - 1]).Length < 1e-10) loop.RemoveAt(loop.Count - 1);
                if (loop.Count < 3) throw new InvalidOperationException("Контур грани вырожден.");
                Loops.Add(loop); Area += (k == 0 ? 1 : -1) * Math.Abs(SignedArea(loop));
                for (int i = 0; i < loop.Count; i++)
                {
                    P a = loop[i], b = loop[(i + 1) % loop.Count];
                    if (double.IsNaN(a.X) || double.IsInfinity(a.X) || double.IsNaN(a.Y) || double.IsInfinity(a.Y))
                        throw new InvalidOperationException("Некорректная геометрия грани.");
                    if ((b - a).Length <= 1e-12) continue;
                    Edges.Add(new Segment(a, b)); Perimeter += (b - a).Length;
                    Min.X = Math.Min(Min.X, a.X); Min.Y = Math.Min(Min.Y, a.Y);
                    Max.X = Math.Max(Max.X, a.X); Max.Y = Math.Max(Max.Y, a.Y);
                }
            }
            if (Area <= 1e-16 || Perimeter <= 1e-10) throw new InvalidOperationException("Площадь выбранной грани равна нулю.");
        }
        public bool Inside(P p)
        {
            bool inside = false;
            foreach (List<P> loop in Loops) if (InLoop(loop, p)) inside = !inside;
            return inside;
        }
        public double Distance(P p)
        {
            double d = double.MaxValue;
            foreach (Segment e in Edges) d = Math.Min(d, (p - Closest(e, p)).Length);
            return d;
        }
        public bool SegmentInside(P a, P b, double step)
        {
            if (!Inside(a) || !Inside(b)) return false;
            foreach (Segment edge in Edges)
            {
                if (Math.Max(a.X, b.X) < Math.Min(edge.A.X, edge.B.X) || Math.Min(a.X, b.X) > Math.Max(edge.A.X, edge.B.X)
                    || Math.Max(a.Y, b.Y) < Math.Min(edge.A.Y, edge.B.Y) || Math.Min(a.Y, b.Y) > Math.Max(edge.A.Y, edge.B.Y)) continue;
                double c = P.Cross(b - a, edge.A - a), d = P.Cross(b - a, edge.B - a);
                double e = P.Cross(edge.B - edge.A, a - edge.A), f = P.Cross(edge.B - edge.A, b - edge.A);
                if (((c <= 0 && d >= 0) || (c >= 0 && d <= 0)) && ((e <= 0 && f >= 0) || (e >= 0 && f <= 0))) return false;
            }
            return true;
        }
    }
    private struct Pair : IEquatable<Pair>
    {
        public int A, B;
        public Pair(int a, int b) { A = Math.Min(a, b); B = Math.Max(a, b); }
        public bool Equals(Pair p) { return A == p.A && B == p.B; }
        public override bool Equals(object p) { return p is Pair && Equals((Pair)p); }
        public override int GetHashCode() { unchecked { return A * 73856093 ^ B * 19349663; } }
    }
    private sealed class Triangle
    {
        public int A, B, C;
        public P Center;
        public double R2;
        public Triangle(int a, int b, int c, List<P> p)
        {
            A = a; B = b; C = c;
            P u = p[b] - p[a], v = p[c] - p[a]; double d = 2 * P.Cross(u, v);
            if (Math.Abs(d) < 1e-18) { R2 = -1; return; }
            double uu = P.Dot(u, u), vv = P.Dot(v, v);
            Center = p[a] + new P((uu * v.Y - vv * u.Y) / d, (u.X * vv - v.X * uu) / d);
            P r = Center - p[a]; R2 = P.Dot(r, r);
        }
        public Pair[] Sides() { return new Pair[] { new Pair(A, B), new Pair(B, C), new Pair(C, A) }; }
    }
    private sealed class Node
    {
        public P Point;
        public readonly HashSet<int> Links = new HashSet<int>();
        public double Radius;
    }
    public static double SignedArea(List<P> loop)
    {
        double s = 0; P o = loop[0];
        for (int i = 1; i + 1 < loop.Count; i++) s += P.Cross(loop[i] - o, loop[i + 1] - o);
        return s * 0.5;
    }
    public static bool InLoop(List<P> loop, P p)
    {
        bool inside = false;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            P a = loop[j], b = loop[i];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y)) inside = !inside;
        }
        return inside;
    }
    public static P Closest(Segment s, P p)
    {
        P d = s.B - s.A; double d2 = P.Dot(d, d);
        return s.A + d * (d2 > 0 ? Math.Max(0, Math.Min(1, P.Dot(p - s.A, d) / d2)) : 0);
    }
    public static List<Region> Regions(List<List<P>> loops)
    {
        loops.Sort(delegate(List<P> a, List<P> b) { return Math.Abs(SignedArea(b)).CompareTo(Math.Abs(SignedArea(a))); });
        int[] parent = new int[loops.Count], depth = new int[loops.Count];
        for (int i = 0; i < loops.Count; i++)
        {
            parent[i] = -1;
            for (int j = i - 1; j >= 0; j--) if (InLoop(loops[j], loops[i][0])) { parent[i] = j; depth[i] = depth[j] + 1; break; }
        }
        List<Region> result = new List<Region>();
        for (int i = 0; i < loops.Count; i++) if (depth[i] % 2 == 0)
        {
            List<List<P>> group = new List<List<P>>(); group.Add(loops[i]);
            for (int j = i + 1; j < loops.Count; j++) if (parent[j] == i) group.Add(loops[j]);
            result.Add(new Region(group));
        }
        return result;
    }
    public static Result Build(Region region, double tolerance)
    {
        if (!(tolerance > 0) || double.IsInfinity(tolerance)) throw new ArgumentException("tolerance");
        Result result = new Result();
        double width = 2 * region.Area / region.Perimeter;
        result.WidthEstimate = width;
        if (width < tolerance * 8) throw new InvalidOperationException("Штрих слишком узкий относительно допуска 0,002 мм.");
        double step = Math.Min(width / 8, Math.Sqrt(width * tolerance) * 0.7);
        result.SamplingStep = step;
        Circle dot;
        if (RoundDot(region, tolerance, out dot) || SquareDot(region, tolerance, out dot)) { result.Circles.Add(dot); return result; }
        if (WallGraph(region, tolerance, result)) return result;
        List<P> samples = Sample(region, step);
        result.BoundarySamples = samples.Count;
        if (samples.Count > 10000) throw new InvalidOperationException("Слишком сложная грань: более 10000 точек контура. Выбирайте отдельные грани букв.");
        P offset = (region.Min + region.Max) * 0.5; double scale = Math.Max(region.Max.X - region.Min.X, region.Max.Y - region.Min.Y);
        List<P> normalized = new List<P>(); foreach (P p in samples) normalized.Add((p - offset) * (1 / scale));
        List<Triangle> triangles = Delaunay(normalized);
        List<Node> nodes = new List<Node>(); int[] map = new int[triangles.Count];
        for (int i = 0; i < triangles.Count; i++)
        {
            map[i] = -1; P p = triangles[i].Center * scale + offset;
            if (!region.Inside(p)) continue;
            double radius = region.Distance(p);
            if (radius < tolerance * 0.05) continue;
            map[i] = nodes.Count; nodes.Add(new Node { Point = p, Radius = radius });
        }
        Dictionary<Pair, int> owners = new Dictionary<Pair, int>();
        for (int i = 0; i < triangles.Count; i++) foreach (Pair edge in triangles[i].Sides())
        {
            int other;
            if (!owners.TryGetValue(edge, out other)) { owners.Add(edge, i); continue; }
            int a = map[i], b = map[other];
            if (a >= 0 && b >= 0 && region.SegmentInside(nodes[a].Point, nodes[b].Point, step))
            { nodes[a].Links.Add(b); nodes[b].Links.Add(a); }
        }
        nodes = MergeNearNodes(nodes, tolerance * 0.05, region);
        PruneCorners(nodes, step, region, tolerance);
        nodes = MergeNearNodes(nodes, step * 2, region, true);
        nodes = MergeNearNodes(nodes, tolerance * 2, region);
        // Merging nearby medial nodes exposes the two continuing strokes of a
        // rounded/bevelled corner. Recheck wall support on that consolidated graph.
        PruneSideWallLeaves(nodes, region, step, tolerance);
        CheckTopology(nodes, region.Loops.Count - 1);
        List<Stroke> strokes = Trace(nodes);
        if (strokes.Count == 0)
        {
            throw new InvalidOperationException("Не удалось получить устойчивую среднюю линию этой грани.");
        }
        foreach (Stroke stroke in strokes) Refine(stroke, region, step, tolerance);
        RestoreStraightStrokes(strokes, region, tolerance);
        RestoreCurvedCorners(strokes, region, tolerance);
        CleanSnappedJunctions(strokes, region, tolerance);
        InsetTerminals(strokes, region, tolerance);
        foreach (Stroke stroke in strokes)
        {
            if (stroke.Points.Count < 2) continue;
            List<Bezier> curves = Fit(stroke, region, tolerance);
            if (curves.Count == 0) throw new InvalidOperationException("Пустая средняя линия.");
            result.Strokes.Add(stroke); result.Curves.Add(curves);
        }
        if (result.Curves.Count == 0) throw new InvalidOperationException("На грани не осталось кривых.");
        return result;
    }
    // Apply the same end convention to both reconstruction engines, BEFORE splitting
    // junctions for CAM. Only degree-one ends are physical terminals of the engraving.
    private static void InsetTerminals(List<Stroke> strokes, Region region, double tolerance)
    {
        var ends = new List<P>();
        foreach (Stroke s in strokes) { ends.Add(s.Points[0]); ends.Add(s.Points[s.Points.Count - 1]); }
        List<StraightAxis> axes = StraightAxes(region, tolerance);
        for (int n = 0; n < strokes.Count; n++)
        {
            Stroke s = strokes[n]; if (s.Closed) continue;
            var p = new List<P>(s.Points); double start = 0, finish = 0;
            for (int end = 0; end < 2; end++)
            {
                int degree = 0; P tip = ends[2 * n + end];
                foreach (P q in ends) if ((q - tip).Length <= tolerance * 0.01) degree++;
                if (degree != 1) continue;
                var oriented = new List<P>(p); if (end == 1) oriented.Reverse();
                double limit = 0;
                for (int i = 1; i < oriented.Count; i++)
                {
                    limit += (oriented[i] - oriented[i - 1]).Length;
                    int original = end == 0 ? i : p.Count - 1 - i;
                    bool curvedCorner = false;
                    foreach (P corner in s.CurvedCorners) if ((corner - oriented[i]).Length <= tolerance * 0.01) curvedCorner = true;
                    if (s.Corners.Contains(original) && (curvedCorner || IsWallCorner(oriented[i], axes, region, tolerance))) break;
                }
                double inset = TerminalInset(oriented, limit, axes, region, tolerance);
                if (end == 0) start = inset; else finish = inset;
            }
            double total = 0; foreach (double length in PathLengths(p)) total += length;
            if (total - start - finish <= tolerance * 0.02)
                throw new InvalidOperationException("Штрих слишком короткий для отступов на половину его ширины. Кривые не созданы.");
            P unused; P first = PathPoint(p, start, out unused), last;
            var reverse = new List<P>(p); reverse.Reverse(); last = PathPoint(reverse, finish, out unused);
            var output = new List<P> { first }; var corners = new HashSet<int>(); double distance = 0;
            for (int i = 0; i < p.Count; i++)
            {
                if (i > 0) distance += (p[i] - p[i - 1]).Length;
                if (distance > start + tolerance * 0.001 && distance < total - finish - tolerance * 0.001)
                { output.Add(p[i]); if (s.Corners.Contains(i)) corners.Add(output.Count - 1); }
            }
            output.Add(last);
            for (int i = 1; i < output.Count; i++)
                if (!region.SegmentInside(output[i - 1], output[i], tolerance))
                    throw new InvalidOperationException("Отступ конца штриха выходит за выбранную грань.");
            s.Points.Clear(); s.Points.AddRange(output); s.Corners.Clear();
            foreach (int corner in corners) s.Corners.Add(corner);
        }
    }
    private static IEnumerable<double> PathLengths(List<P> path)
    { for (int i = 1; i < path.Count; i++) yield return (path[i] - path[i - 1]).Length; }
    private static bool IsWallCorner(P point, List<StraightAxis> axes, Region region, double tolerance)
    {
        for (int i = 0; i < axes.Count; i++) for (int j = i + 1; j < axes.Count; j++)
        { P corner; if (SharpIntersection(axes[i], axes[j], region, tolerance, out corner) && (corner - point).Length <= tolerance) return true; }
        return false;
    }
    // Positive distance trims the existing centerline; negative distance extends its
    // terminal tangent. An already inset medial endpoint is never shortened twice.
    private static P PathPoint(List<P> path, double distance, out P inward)
    {
        for (int i = 1; i < path.Count; i++)
        {
            P v = path[i] - path[i - 1]; double length = v.Length;
            if (length <= 1e-20) continue;
            if (distance <= length || i == path.Count - 1)
            { inward = v * (1 / length); return path[i - 1] + inward * distance; }
            distance -= length;
        }
        throw new InvalidOperationException("Вырожденный конец штриха.");
    }
    private static double RayBoundary(P p, P direction, Region region, double epsilon)
    {
        double best = double.MaxValue;
        foreach (Segment e in region.Edges)
        {
            P v = e.B - e.A; double cross = P.Cross(direction, v);
            if (Math.Abs(cross) < v.Length * 1e-12) continue;
            double t = P.Cross(e.A - p, v) / cross, u = P.Cross(e.A - p, direction) / cross;
            if (t > epsilon && u >= -1e-9 && u <= 1 + 1e-9) best = Math.Min(best, t);
        }
        return best;
    }
    private static double TerminalInset(List<P> path, double limit, List<StraightAxis> axes, Region region, double tolerance)
    {
        P inward; PathPoint(path, 0, out inward); StraightAxis wall = null; double best = double.MaxValue;
        foreach (StraightAxis a in axes)
        {
            double score = (a.Project(path[0]) - path[0]).Length;
            P unused; P probe = PathPoint(path, Math.Min(limit * 0.5, a.Radius * 2), out unused);
            if (score < Math.Max(tolerance * 2, a.Radius * 0.8) && (a.Project(probe) - probe).Length < tolerance * 2
                && a.Outside(path[0]) < a.Radius * 2 && score < best) { best = score; wall = a; }
        }
        double epsilon = tolerance * 0.001;
        Func<double, double> clearance = delegate(double distance)
        {
            P direction; P p = PathPoint(path, distance, out direction);
            if (!region.Inside(p)) return -1;
            if (wall != null) direction = wall.Direction * (P.Dot(inward, wall.Direction) < 0 ? -1 : 1);
            P normal = new P(-direction.Y, direction.X);
            double radius;
            if (wall != null)
            {
                // Intersect the supporting side walls, not the oblique terminal cap.
                // This also measures local width for slightly tapered straight strokes.
                double positive = double.MaxValue, negative = double.MaxValue;
                foreach (Segment side in wall.Sides)
                {
                    P v = side.B - side.A; double d = P.Cross(normal, v);
                    if (Math.Abs(d) < v.Length * 0.5) continue;
                    double t = P.Cross(side.A - p, v) / d;
                    if (t > 0) positive = Math.Min(positive, t); else negative = Math.Min(negative, -t);
                }
                if (positive == double.MaxValue || negative == double.MaxValue)
                    throw new InvalidOperationException("Не найдены обе стенки у конца штриха.");
                radius = (positive + negative) * 0.5;
            }
            else
            {
                double positive = RayBoundary(p, normal, region, epsilon), negative = RayBoundary(p, normal * -1, region, epsilon);
                if (positive == double.MaxValue || negative == double.MaxValue)
                    throw new InvalidOperationException("Не найдены обе стенки у конца штриха.");
                radius = (positive + negative) * 0.5;
            }
            double forward = RayBoundary(p, direction * -1, region, epsilon);
            if (!(radius > epsilon) || double.IsInfinity(radius) || radius == double.MaxValue || forward == double.MaxValue)
                throw new InvalidOperationException("Не удалось измерить местную ширину у конца штриха.");
            return forward - radius;
        };
        double f = clearance(0), low = 0, high = 0;
        if (Math.Abs(f) <= epsilon) return 0;
        double step = Math.Max(tolerance, Math.Min(region.Distance(path[0]) + Math.Abs(f), limit) / 8);
        if (f > 0)
        {
            // Search outward only as far as the first boundary of this same terminal.
            double reach = RayBoundary(path[0], inward * -1, region, epsilon);
            low = -reach;
            if (reach == double.MaxValue) throw new InvalidOperationException("Не найден торец штриха.");
        }
        else
        {
            double maximum = limit - epsilon;
            for (high = Math.Min(step, maximum); high < maximum && clearance(high) < 0; high = Math.Min(high + step, maximum)) { }
            if (high <= 0 || clearance(high) < 0)
                throw new InvalidOperationException("Недостаточно длины до угла или узла для отступа на половину ширины штриха.");
        }
        for (int i = 0; i < 50; i++)
        { double mid = (low + high) * 0.5; if (clearance(mid) < 0) low = mid; else high = mid; }
        double inset = (low + high) * 0.5;
        if (Math.Abs(clearance(inset)) > tolerance)
            throw new InvalidOperationException("Не удалось выдержать отступ на половину ширины у конца штриха.");
        return inset;
    }
    private static bool SquareDot(Region region, double tolerance, out Circle circle)
    {
        circle = null; if (region.Loops.Count != 1) return false;
        var p = new List<P>(region.Loops[0]); bool changed = true;
        while (changed && p.Count > 4)
        {
            changed = false;
            for (int i = p.Count - 1; i >= 0 && p.Count > 4; i--)
                if ((p[i] - Closest(new Segment(p[(i + p.Count - 1) % p.Count], p[(i + 1) % p.Count]), p[i])).Length <= tolerance * 0.01)
                { p.RemoveAt(i); changed = true; }
        }
        if (p.Count != 4) return false;
        P a = p[1] - p[0], b = p[2] - p[1], c = p[3] - p[2], d = p[0] - p[3];
        // A compact square/near-square parallelogram, including the shear of italic
        // text. A long dash, a triangular serif and a counter are not punctuation.
        double min = Math.Min(Math.Min(a.Length, b.Length), Math.Min(c.Length, d.Length));
        double max = Math.Max(Math.Max(a.Length, b.Length), Math.Max(c.Length, d.Length));
        if (min <= tolerance * 8 || max > min * 1.25 || Math.Abs(P.Dot(a.Unit(), b.Unit())) > 0.45
            || (a + c).Length > min * 0.02 || (b + d).Length > min * 0.02
            || P.Cross(a, b) * P.Cross(b, c) <= 0 || P.Cross(c, d) * P.Cross(d, a) <= 0) return false;
        P center = (p[0] + p[1] + p[2] + p[3]) * 0.25;
        double halfWidth = region.Distance(center);
        if (!region.Inside(center) || halfWidth <= tolerance * 4) return false;
        circle = new Circle { Center = center, Radius = halfWidth * 0.5 }; return true;
    }
    private static bool RoundDot(Region region, double tolerance, out Circle circle)
    {
        circle = null; if (region.Loops.Count != 1) return false;
        List<P> loop = region.Loops[0]; P origin = loop[0], center = new P(); double area6 = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            P a = loop[i] - origin, b = loop[(i + 1) % loop.Count] - origin; double cross = P.Cross(a, b);
            center = center + (a + b) * cross; area6 += cross * 3;
        }
        if (Math.Abs(area6) < 1e-20) return false; center = center * (1 / area6) + origin;
        double r = 0; foreach (P p in loop) r += (p - center).Length; r /= loop.Count;
        if (r < tolerance * 4 || !region.Inside(center)) return false;
        // Imported outline fonts often encode punctuation as a slightly oval spline.
        // Accept compact near-circles by dimensionless shape tests, independently of CAM
        // tolerance. Elongated marks, polygons and counters remain ordinary geometry.
        bool nearCircle = loop.Count >= 16 && 4 * Math.PI * region.Area / (region.Perimeter * region.Perimeter) >= 0.985;
        // The arithmetic mean of vertex radii is biased by adaptive spline sampling.
        // Test a near-circle against its area radius: subdividing a boundary segment
        // cannot change this radius, the area centroid or the accepted shape envelope.
        double areaRadius = Math.Sqrt(region.Area / Math.PI), referenceRadius = nearCircle ? areaRadius : r;
        double radialLimit = nearCircle ? Math.Max(tolerance * 0.5, areaRadius * 0.08) : tolerance * 0.5;
        bool exactCircle = true;
        foreach (P p in loop)
        {
            double radius = (p - center).Length;
            if (Math.Abs(radius - referenceRadius) > radialLimit) return false;
            if (Math.Abs(radius - r) > tolerance * 0.5) exactCircle = false;
        }
        // Check segment midpoints as well: vertices of a polygon alone may lie on a circle.
        foreach (Segment e in region.Edges) if (Math.Abs(((e.A + e.B) * 0.5 - center).Length - referenceRadius) > Math.Max(tolerance, radialLimit)) return false;
        // The requested convention for a round punctuation mark: path diameter = face diameter / 2.
        circle = new Circle { Center = center, Radius = (exactCircle ? r : areaRadius) / 2 }; return true;
    }
    private static void CheckTopology(List<Node> nodes, int holes)
    {
        HashSet<int> visited = new HashSet<int>(); int components = 0, edges = 0, vertices = 0;
        for (int i = 0; i < nodes.Count; i++) if (nodes[i].Links.Count > 0)
        {
            vertices++; edges += nodes[i].Links.Count;
            if (visited.Contains(i)) continue;
            components++; Queue<int> queue = new Queue<int>(); queue.Enqueue(i); visited.Add(i);
            while (queue.Count > 0) foreach (int next in nodes[queue.Dequeue()].Links) if (visited.Add(next)) queue.Enqueue(next);
        }
        if (components > 1 || (components == 1 && edges / 2 - vertices + components != holes))
            throw new InvalidOperationException("Средняя линия потеряла связность или замкнутый контур. Изменения не выполняются.");
    }
    private static List<P> Sample(Region region, double step)
    {
        List<P> result = new List<P>();
        foreach (List<P> loop in region.Loops)
        {
            // Uniform spacing along the whole loop avoids a different sampling phase on every tiny STEP edge.
            double length = 0; for (int i = 0; i < loop.Count; i++) length += (loop[(i + 1) % loop.Count] - loop[i]).Length;
            int count = Math.Max(8, (int)Math.Ceiling(length / step));
            if (result.Count + count > 10000) throw new InvalidOperationException("Контур слишком большой для расчёта средней линии за один запуск.");
            int edge = 0; double before = 0, edgeLength = (loop[1] - loop[0]).Length;
            for (int j = 0; j < count; j++)
            {
                double distance = length * j / count;
                while (edge + 1 < loop.Count && before + edgeLength < distance)
                { before += edgeLength; edge++; edgeLength = (loop[(edge + 1) % loop.Count] - loop[edge]).Length; }
                result.Add(loop[edge] + (loop[(edge + 1) % loop.Count] - loop[edge]) * (edgeLength > 0 ? (distance - before) / edgeLength : 0));
            }
        }
        return result;
    }
    private static List<Triangle> Delaunay(List<P> points)
    {
        int count = points.Count;
        List<P> all = new List<P>(points); all.Add(new P(-32, -16)); all.Add(new P(32, -16)); all.Add(new P(0, 32));
        List<Triangle> triangles = new List<Triangle>(); triangles.Add(new Triangle(count, count + 1, count + 2, all));
        int[] order = new int[count]; for (int i = 0; i < count; i++) order[i] = i;
        Random random = new Random(82371);
        for (int i = count - 1; i > 0; i--) { int j = random.Next(i + 1), swap = order[i]; order[i] = order[j]; order[j] = swap; }
        foreach (int index in order)
        {
            Dictionary<Pair, int> boundary = new Dictionary<Pair, int>();
            for (int i = triangles.Count - 1; i >= 0; i--)
            {
                Triangle t = triangles[i]; P d = all[index] - t.Center;
                if (P.Dot(d, d) > t.R2 + 1e-13) continue;
                foreach (Pair edge in t.Sides()) { int n; boundary.TryGetValue(edge, out n); boundary[edge] = n + 1; }
                triangles.RemoveAt(i);
            }
            foreach (KeyValuePair<Pair, int> entry in boundary) if (entry.Value == 1)
            {
                Triangle t = new Triangle(entry.Key.A, entry.Key.B, index, all);
                if (t.R2 >= 0) triangles.Add(t);
            }
        }
        triangles.RemoveAll(delegate(Triangle t) { return t.A >= count || t.B >= count || t.C >= count; });
        return triangles;
    }
    private static int Root(int[] parent, int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
    private static List<Node> MergeNearNodes(List<Node> nodes, double distance, Region region, bool junctionsOnly = false)
    {
        int[] parent = new int[nodes.Count]; for (int i = 0; i < parent.Length; i++) parent[i] = i;
        for (int i = 0; i < nodes.Count; i++) foreach (int j in nodes[i].Links)
            if ((!junctionsOnly || (nodes[i].Links.Count >= 3 && nodes[j].Links.Count >= 3)) && (nodes[i].Point - nodes[j].Point).Length <= distance)
                parent[Root(parent, j)] = Root(parent, i);
        Dictionary<int, int> ids = new Dictionary<int, int>(); List<Node> merged = new List<Node>(); List<int> counts = new List<int>();
        for (int i = 0; i < nodes.Count; i++)
        {
            int root = Root(parent, i), id;
            if (!ids.TryGetValue(root, out id)) { id = merged.Count; ids.Add(root, id); merged.Add(new Node()); counts.Add(0); }
            merged[id].Point = merged[id].Point + nodes[i].Point; counts[id]++;
        }
        for (int i = 0; i < merged.Count; i++) { merged[i].Point = merged[i].Point * (1.0 / counts[i]); merged[i].Radius = region.Distance(merged[i].Point); }
        for (int i = 0; i < nodes.Count; i++) foreach (int j in nodes[i].Links)
        {
            int a = ids[Root(parent, i)], b = ids[Root(parent, j)];
            if (a != b) merged[a].Links.Add(b);
        }
        return merged;
    }
    private static void PruneCorners(List<Node> nodes, double step, Region region, double tolerance)
    {
        // Remove only short terminal branches whose radius collapses towards a convex outline corner.
        // Genuine short strokes with a full-width rounded end survive this condition.
        List<StraightAxis> axes = StraightAxes(region, tolerance);
        for (int pass = 0; pass < 4; pass++)
        {
            List<Pair> remove = new List<Pair>();
            for (int i = 0; i < nodes.Count; i++) if (nodes[i].Links.Count == 1)
            {
                List<Pair> branch = new List<Pair>(); int previous = -1, current = i; double length = 0;
                while (true)
                {
                    int next = -1; foreach (int j in nodes[current].Links) if (j != previous) { next = j; break; }
                    if (next < 0) break;
                    branch.Add(new Pair(current, next)); length += (nodes[current].Point - nodes[next].Point).Length;
                    previous = current; current = next;
                    if (nodes[current].Links.Count != 2) break;
                }
                // Acute miter corners create longer medial spurs than a right angle.
                // Retain the radius-collapse test so full-width short stems survive.
                if (nodes[current].Links.Count >= 3 &&
                    ((nodes[i].Radius < nodes[current].Radius * 0.35 && (length < nodes[current].Radius * 4 + step
                        || IsMiterSpur(nodes, branch, i, current, axes, region, step, tolerance)))
                    || ConvexCornerBranch(nodes, branch, i, current, length, region, step, tolerance))) remove.AddRange(branch);
            }
            if (remove.Count == 0) break;
            foreach (Pair p in remove) { nodes[p.A].Links.Remove(p.B); nodes[p.B].Links.Remove(p.A); }
        }
        PruneSideWallLeaves(nodes, region, step, tolerance);
    }
    private static void PruneSideWallLeaves(List<Node> nodes, Region region, double step, double tolerance)
    {
        var walls = new List<Segment>();
        var before = new Dictionary<Segment, Segment>(); var after = new Dictionary<Segment, Segment>();
        foreach (List<P> loop in region.Loops)
        {
            List<P> p = BoundaryVertices(loop, tolerance); int first = walls.Count;
            for (int i = 0; i < p.Count; i++) walls.Add(new Segment(p[i], p[(i + 1) % p.Count]));
            for (int i = 0; i < p.Count; i++)
            { before[walls[first + i]] = walls[first + (i + p.Count - 1) % p.Count]; after[walls[first + i]] = walls[first + (i + 1) % p.Count]; }
        }
        for (int pass = 0; pass < 4; pass++)
        {
            var remove = new List<Pair>();
            for (int tip = 0; tip < nodes.Count; tip++) if (nodes[tip].Links.Count == 1)
            {
                var branch = new List<Pair>(); int previous = -1, current = tip; double length = 0;
                while (true)
                {
                    int next = -1; foreach (int n in nodes[current].Links) if (n != previous) { next = n; break; }
                    if (next < 0) break;
                    branch.Add(new Pair(current, next)); length += (nodes[next].Point - nodes[current].Point).Length;
                    previous = current; current = next; if (nodes[current].Links.Count != 2) break;
                }
                double radius = nodes[current].Radius;
                if (nodes[current].Links.Count != 3 || length > radius * 1.5 || nodes[tip].Radius < radius * 0.4) continue;
                P origin = nodes[tip].Point, outward = (origin - nodes[current].Point).Unit();
                if (outward.Length < 0.5) continue;
                Segment hit = null; double distance = double.MaxValue;
                foreach (Segment wall in walls)
                {
                    P v = wall.B - wall.A; double cross = P.Cross(outward, v); if (Math.Abs(cross) < v.Length * 1e-12) continue;
                    double t = P.Cross(wall.A - origin, v) / cross, u = P.Cross(wall.A - origin, outward) / cross;
                    if (t > 0 && t < distance && u >= 0 && u <= 1) { hit = wall; distance = t; }
                }
                if (hit == null) continue;
                // A genuine cap terminates both side walls. A false corner leaf runs
                // into a wall that demonstrably continues along a neighbouring stroke.
                bool sideWall = false;
                List<Segment> left = BoundaryArm(hit, before, radius), right = BoundaryArm(hit, after, radius);
                var leftSupport = new List<double>(); var rightSupport = new List<double>();
                foreach (int neighbour in nodes[current].Links) if (neighbour != previous)
                {
                    int a = current, b = neighbour; double walked = 0, supported = 0, ls = 0, rs = 0;
                    while (true)
                    {
                        P u = nodes[b].Point - nodes[a].Point; double size = u.Length; walked += size;
                        P mid = (nodes[a].Point + nodes[b].Point) * 0.5;
                        if (Math.Abs(P.Dot(u.Unit(), (hit.B - hit.A).Unit())) > 0.92
                            && Math.Abs((mid - Closest(hit, mid)).Length - region.Distance(mid)) <= Math.Max(tolerance * 2, step * 0.25)) supported += size;
                        if (walked > radius * 0.3)
                        {
                            if (WallSupports(left, mid, u.Unit(), region, step, tolerance)) ls += size;
                            if (WallSupports(right, mid, u.Unit(), region, step, tolerance)) rs += size;
                        }
                        if (supported >= radius * 1.5 && (hit.B - hit.A).Length >= radius * 2) sideWall = true;
                        if (walked > radius * 5 || nodes[b].Links.Count != 2) break;
                        int next = -1; foreach (int n in nodes[b].Links) if (n != a) { next = n; break; }
                        if (next < 0) break; a = b; b = next;
                    }
                    leftSupport.Add(ls); rightSupport.Add(rs);
                }
                // A bevel between TWO continuing strokes is a joint, not a third cap.
                // For a real short arm, its side walls stop at that arm's junction and
                // cannot support the two other branches over their own stroke widths.
                if (leftSupport.Count == 2 && ((leftSupport[0] > radius && rightSupport[1] > radius)
                    || (leftSupport[1] > radius && rightSupport[0] > radius))) sideWall = true;
                if (sideWall) remove.AddRange(branch);
            }
            if (remove.Count == 0) break;
            foreach (Pair p in remove) { nodes[p.A].Links.Remove(p.B); nodes[p.B].Links.Remove(p.A); }
        }
    }
    private static List<Segment> BoundaryArm(Segment cap, Dictionary<Segment, Segment> next, double radius)
    {
        var result = new List<Segment>(); Segment edge = next[cap]; double length = 0;
        while (edge != cap && result.Count <= next.Count)
        {
            result.Add(edge); length += (edge.B - edge.A).Length; if (length > radius * 6) break;
            Segment following = next[edge];
            if (P.Dot((edge.B - edge.A).Unit(), (following.B - following.A).Unit()) < 0.5) break;
            edge = following;
        }
        return result;
    }
    private static bool WallSupports(List<Segment> walls, P point, P tangent, Region region, double step, double tolerance)
    {
        double radius = region.Distance(point), error = Math.Max(tolerance * 2, step * 0.25);
        foreach (Segment wall in walls)
            if (Math.Abs(P.Dot(tangent, (wall.B - wall.A).Unit())) > 0.85
                && Math.Abs((point - Closest(wall, point)).Length - radius) <= error) return true;
        return false;
    }
    private static bool ConvexCornerBranch(List<Node> nodes, List<Pair> branch, int tip, int junction,
        double length, Region region, double step, double tolerance)
    {
        double radius = nodes[junction].Radius, error = Math.Max(tolerance, step * 0.1);
        if (length > radius * 1.5 + step * 2 || nodes[tip].Radius > radius * 0.95) return false;
        for (int k = 0; k < region.Loops.Count; k++)
        {
            List<P> loop = BoundaryVertices(region.Loops[k], tolerance); double orientation = Math.Sign(SignedArea(loop)) * (k == 0 ? 1 : -1);
            for (int i = 0; i < loop.Count; i++)
            {
                P vertex = loop[i], a = loop[(i + loop.Count - 1) % loop.Count], b = loop[(i + 1) % loop.Count];
                P u = (vertex - a).Unit(), v = (b - vertex).Unit();
                // Adjacent walls of an obtuse convex corner support this entire leaf.
                // Opposite stroke walls, pointed arms, concave corners and cap centers
                // cannot pass this support test just by having a short branch.
                if (P.Cross(u, v) * orientation <= 1e-8 || P.Dot(u, v) < 0.1
                    || (vertex - nodes[junction].Point).Length > radius * 2 + step
                    || (vertex - nodes[tip].Point).Length >= (vertex - nodes[junction].Point).Length) continue;
                var x = new Segment(a, vertex); var y = new Segment(vertex, b); bool supported = true;
                foreach (Pair edge in branch) foreach (int n in new int[] { edge.A, edge.B })
                    if (Math.Abs((nodes[n].Point - Closest(x, nodes[n].Point)).Length - nodes[n].Radius) > error
                        || Math.Abs((nodes[n].Point - Closest(y, nodes[n].Point)).Length - nodes[n].Radius) > error) supported = false;
                if (supported) return true;
            }
        }
        return false;
    }
    private static bool IsMiterSpur(List<Node> nodes, List<Pair> branch, int tip, int junction,
        List<StraightAxis> axes, Region region, double step, double tolerance)
    {
        // An acute corner can have a long medial branch, even on a thin triangular ring.
        // Prove it belongs to the miter of TWO constant-width strokes: both outline walls
        // must have an opposite parallel wall and their center axes must meet inside the face.
        // A real tapered arm or a short flat-ended stem does not satisfy this construction.
        for (int i = 0; i < axes.Count; i++) for (int j = i + 1; j < axes.Count; j++)
        {
            StraightAxis a = axes[i], b = axes[j]; P center;
            if (!SharpIntersection(a, b, region, tolerance, out center)) continue;
            double radius = Math.Max(a.Radius, b.Radius);
            double miterReach = (a.Radius + b.Radius) / Math.Abs(P.Cross(a.Direction, b.Direction));
            if ((nodes[junction].Point - center).Length > Math.Max(radius * 3, miterReach)) continue;
            foreach (Segment x in a.Sides) foreach (Segment y in b.Sides)
                foreach (P vertex in new P[] { x.A, x.B })
                {
                    if (Math.Min((vertex - y.A).Length, (vertex - y.B).Length) > tolerance * 0.1) continue;
                    if ((nodes[tip].Point - vertex).Length > step * 3) continue;
                    P alongX = ((vertex - x.A).Length < tolerance * 0.1 ? x.B : x.A) - vertex;
                    P alongY = ((vertex - y.A).Length < tolerance * 0.1 ? y.B : y.A) - vertex;
                    P direction = (alongX.Unit() + alongY.Unit()).Unit(); double reach = (center - vertex).Length;
                    if (reach <= radius || direction.Length < 0.5 || P.Dot(nodes[junction].Point - center, direction) < -radius) continue;
                    bool miter = true;
                    foreach (Pair edge in branch) foreach (int index in new int[] { edge.A, edge.B })
                    {
                        P p = nodes[index].Point; double along = P.Dot(p - vertex, direction);
                        if (along < -step || along > reach + Math.Max(radius * 3, miterReach)
                            || Math.Abs(P.Cross(direction, p - vertex)) > step * 2
                            || Math.Abs((p - Closest(x, p)).Length - nodes[index].Radius) > step
                            || Math.Abs((p - Closest(y, p)).Length - nodes[index].Radius) > step) miter = false;
                    }
                    if (miter) return true;
                }
        }
        return false;
    }
    private static List<Stroke> Trace(List<Node> nodes)
    {
        List<Stroke> result = new List<Stroke>(); HashSet<Pair> used = new HashSet<Pair>();
        for (int i = 0; i < nodes.Count; i++) if (nodes[i].Links.Count != 2)
            foreach (int next in nodes[i].Links) if (!used.Contains(new Pair(i, next))) result.Add(Walk(nodes, used, i, next));
        for (int i = 0; i < nodes.Count; i++) foreach (int next in nodes[i].Links)
            if (!used.Contains(new Pair(i, next))) result.Add(Walk(nodes, used, i, next));
        return result;
    }
    private static Stroke Walk(List<Node> nodes, HashSet<Pair> used, int start, int next)
    {
        Stroke stroke = new Stroke(); stroke.Points.Add(nodes[start].Point); int current = start;
        while (used.Add(new Pair(current, next)))
        {
            stroke.Points.Add(nodes[next].Point);
            if (next == start) { stroke.Closed = true; break; }
            if (nodes[next].Links.Count != 2) break;
            int previous = current; current = next;
            foreach (int candidate in nodes[current].Links) if (candidate != previous) { next = candidate; break; }
        }
        return stroke;
    }
    private static void Refine(Stroke stroke, Region region, double step, double tolerance)
    {
        List<P> p = stroke.Points;
        // Keep branch junctions shared exactly. Interior points are moved to the equal-distance ridge of the actual segments.
        for (int pass = 0; pass < 2; pass++)
        {
            List<P> old = new List<P>(p);
            for (int i = 1; i + 1 < p.Count; i++)
            {
                P tangent = (old[i + 1] - old[i - 1]).Unit(); if (tangent.Length < 0.5) continue;
                P normal = new P(-tangent.Y, tangent.X), center = old[i];
                double negative = double.MaxValue, positive = double.MaxValue;
                foreach (Segment e in region.Edges)
                {
                    P v = e.B - e.A; double d = P.Cross(normal, v); if (Math.Abs(d) < 1e-14) continue;
                    double t = P.Cross(e.A - center, v) / d, u = P.Cross(e.A - center, normal) / d;
                    if (u < -1e-9 || u > 1 + 1e-9) continue;
                    if (t > 1e-10) positive = Math.Min(positive, t); else if (t < -1e-10) negative = Math.Min(negative, -t);
                }
                if (positive == double.MaxValue || negative == double.MaxValue) continue;
                double move = (positive - negative) * 0.5;
                if (Math.Abs(move) <= Math.Max(step * 0.8, tolerance) && positive + negative < region.Distance(center) * 3)
                { P q = center + normal * move; if (region.Inside(q)) p[i] = q; }
            }
        }
        // Remove numerical duplicate points only, not short engraving strokes.
        for (int i = p.Count - 2; i > 0; i--) if ((p[i] - p[i - 1]).Length < tolerance * 0.02) p.RemoveAt(i);
        if (stroke.Closed) p[p.Count - 1] = p[0];
    }
    private static void CleanSnappedJunctions(List<Stroke> strokes, Region region, double tolerance)
    {
        // Snapping two nearby branch nodes to the same straight-axis intersection contracts
        // their connecting edge. Retaining it would create a false out-and-back loop.
        // Keep loops around islands. In a simply connected face, an explicitly contracted
        // connector may also be removed within a small neighborhood visible from its new node.
        for (int i = strokes.Count - 1; i >= 0; i--)
        {
            Stroke stroke = strokes[i]; List<P> p = stroke.Points;
            if (stroke.Closed || p.Count < 2 || (p[0] - p[p.Count - 1]).Length > tolerance * 0.01) continue;
            bool contracted = stroke.ContractedJunction && region.Loops.Count == 1;
            double radius = region.Distance(p[0]) * (contracted ? 1.5 : 0.8); bool insideDisk = radius > tolerance;
            foreach (P point in p)
                if ((point - p[0]).Length > radius || (contracted && (point - p[0]).Length > tolerance * 0.01
                    && !region.SegmentInside(p[0], point, tolerance))) insideDisk = false;
            int incident = 0;
            for (int j = 0; j < strokes.Count; j++) if (j != i)
            {
                List<P> other = strokes[j].Points;
                if ((other[0] - p[0]).Length <= tolerance * 0.01) incident++;
                if ((other[other.Count - 1] - p[0]).Length <= tolerance * 0.01) incident++;
            }
            // A terminal medial spur can also collapse onto an ordinary corner (two
            // remaining incident strokes). It is not a new island or a real short arm:
            // its originally distinct ends were explicitly snapped to the same node.
            // In a face with holes the smaller inscribed disk proves that no island
            // lies in the discarded loop, even if there are other holes elsewhere.
            if (insideDisk && (incident >= 3 || (stroke.ContractedJunction && incident == 2)))
            {
                strokes.RemoveAt(i);
                // Removing a false spur at an ordinary corner leaves two pieces of the
                // SAME stroke. Join them now; otherwise CAM sees two chains touching.
                if (incident == 2) JoinOrdinaryCorner(strokes, p[0], tolerance);
                i = Math.Min(i, strokes.Count);
            }
        }
        // Projection can also duplicate a protected corner. Compact those points before fitting,
        // preserving endpoint coordinates and remapping corner indices instead of fitting zero spans.
        foreach (Stroke stroke in strokes)
        {
            List<P> old = new List<P>(stroke.Points), points = new List<P>(); HashSet<int> corners = new HashSet<int>();
            for (int i = 0; i < old.Count; i++)
            {
                if (points.Count == 0 || (old[i] - points[points.Count - 1]).Length > tolerance * 0.02) points.Add(old[i]);
                else if (i == old.Count - 1) points[points.Count - 1] = old[i];
                if (stroke.Corners.Contains(i)) corners.Add(points.Count - 1);
            }
            if (points.Count < 2) throw new InvalidOperationException("Неразличимый короткий штрих после выпрямления оси. Увеличьте размер надписи или проверьте исходную грань.");
            stroke.Points.Clear(); stroke.Points.AddRange(points); stroke.Corners.Clear();
            foreach (int corner in corners) stroke.Corners.Add(corner);
        }
    }
    private static void JoinOrdinaryCorner(List<Stroke> strokes, P point, double tolerance)
    {
        var indices = new List<int>(); var ends = new List<int>();
        for (int i = 0; i < strokes.Count; i++) for (int end = 0; end < 2; end++)
        {
            List<P> p = strokes[i].Points;
            if ((p[end == 0 ? 0 : p.Count - 1] - point).Length <= tolerance * 0.01)
            { indices.Add(i); ends.Add(end); }
        }
        if (indices.Count != 2 || indices[0] == indices[1]) return;
        Stroke joined = new Stroke();
        for (int side = 0; side < 2; side++)
        {
            Stroke old = strokes[indices[side]]; bool reverse = ends[side] == side;
            for (int k = 0; k < old.Points.Count; k++)
            {
                int source = reverse ? old.Points.Count - 1 - k : k;
                if (side == 1 && k == 0) continue;
                joined.Points.Add(old.Points[source]);
                if (old.Corners.Contains(source)) joined.Corners.Add(joined.Points.Count - 1);
            }
            if (side == 0) joined.Corners.Add(joined.Points.Count - 1);
            joined.CurvedCorners.AddRange(old.CurvedCorners);
        }
        joined.Closed = (joined.Points[0] - joined.Points[joined.Points.Count - 1]).Length <= tolerance * 0.01;
        strokes[indices[0]] = joined; strokes.RemoveAt(indices[1]);
    }
    private sealed class StraightAxis
    {
        internal P Origin, Direction;
        internal double Low, High, Radius;
        internal readonly List<Segment> Sides = new List<Segment>();
        internal double Parameter(P p) { return P.Dot(p - Origin, Direction); }
        internal P Project(P p) { return Origin + Direction * Parameter(p); }
        internal double Outside(P p) { double t = Parameter(p); return Math.Max(0, Math.Max(Low - t, t - High)); }
    }
    private sealed class StraightRun
    {
        internal int First, Last;
        internal StraightAxis Axis;
        internal bool JoinNext;
        internal P Corner;
    }
    private static List<P> BoundaryVertices(List<P> loop, double tolerance)
    {
        List<P> p = new List<P>(loop); bool changed = true;
        while (changed && p.Count > 3)
        {
            changed = false;
            for (int i = p.Count - 1; i >= 0 && p.Count > 3; i--)
            {
                P a = p[(i + p.Count - 1) % p.Count], b = p[i], c = p[(i + 1) % p.Count];
                if ((b - Closest(new Segment(a, c), b)).Length <= tolerance * 0.01)
                { p.RemoveAt(i); changed = true; }
            }
        }
        return p;
    }
    private static IEnumerable<Segment> BoundarySegments(Region region, double tolerance)
    {
        foreach (List<P> loop in region.Loops)
        {
            List<P> p = BoundaryVertices(loop, tolerance);
            for (int i = 0; i < p.Count; i++) yield return new Segment(p[i], p[(i + 1) % p.Count]);
        }
    }
    private static List<StraightAxis> StraightAxes(Region region, double tolerance)
    {
        List<Segment> sides = new List<Segment>(); double width = 2 * region.Area / region.Perimeter;
        // Join only collinear outline pieces. Tessellated arcs must not become straight strokes.
        foreach (List<P> loop in region.Loops)
        {
            List<P> p = BoundaryVertices(loop, tolerance);
            for (int i = 0; i < p.Count; i++)
            {
                Segment side = new Segment(p[i], p[(i + 1) % p.Count]);
                if ((side.B - side.A).Length >= Math.Max(width * 0.65, tolerance * 12)) sides.Add(side);
            }
        }
        List<StraightAxis> axes = new List<StraightAxis>();
        for (int i = 0; i < sides.Count; i++) for (int j = i + 1; j < sides.Count; j++)
        {
            Segment a = sides[i], b = sides[j]; P ua = (a.B - a.A).Unit(), ub = (b.B - b.A).Unit();
            if (P.Dot(ua, ub) < 0) ub = ub * -1;
            // Font outlines can have slightly tapered walls. Use their internal angle bisector,
            // not an exact-parallel test which discards otherwise straight letter strokes.
            if (Math.Abs(P.Cross(ua, ub)) > 0.30) continue;
            P direction = (ua + ub).Unit(), normal = new P(-direction.Y, direction.X);
            P na = new P(-ua.Y, ua.X), nb = new P(-ub.Y, ub.X);
            P origin = a.A + normal * (P.Dot(nb, b.A - a.A) / P.Dot(na + nb, normal));
            double a0 = P.Dot(a.A - origin, direction), a1 = P.Dot(a.B - origin, direction);
            double b0 = P.Dot(b.A - origin, direction), b1 = P.Dot(b.B - origin, direction);
            double low = Math.Max(Math.Min(a0, a1), Math.Min(b0, b1)), high = Math.Min(Math.Max(a0, a1), Math.Max(b0, b1));
            double r0 = Math.Abs(P.Cross(ua, origin + direction * low - a.A));
            double r1 = Math.Abs(P.Cross(ua, origin + direction * high - a.A)), radius = (r0 + r1) * 0.5;
            if (Math.Min(r0, r1) < tolerance * 4 || Math.Max(r0, r1) > width
                || Math.Max(r0, r1) > Math.Min(r0, r1) * 2.5 || high - low < radius * 0.9) continue;
            bool inside = true;
            for (int k = 1; k <= 3; k++)
            {
                P q = origin + direction * (low + (high - low) * k / 4);
                P footA = a.A + ua * P.Dot(q - a.A, ua), footB = b.A + ub * P.Dot(q - b.A, ub);
                // The cap of a short stroke can be nearer than either wall. Require visible
                // opposite walls, not a constant nearest-boundary distance near that cap.
                if ((footA - Closest(a, footA)).Length > tolerance * 2 || (footB - Closest(b, footB)).Length > tolerance * 2
                    || !region.SegmentInside(q, q + (footA - q) * 0.999, tolerance)
                    || !region.SegmentInside(q, q + (footB - q) * 0.999, tolerance)) inside = false;
            }
            if (!inside) continue;
            StraightAxis axis = new StraightAxis { Origin = origin, Direction = direction, Low = low, High = high, Radius = radius };
            axis.Sides.Add(a); axis.Sides.Add(b); axes.Add(axis);
        }
        // Collinear arms may be split by a crossing stroke. Keep one axis across that junction.
        for (int i = 0; i < axes.Count; i++) for (int j = axes.Count - 1; j > i; j--)
        {
            StraightAxis a = axes[i], b = axes[j];
            double angle = Math.Abs(P.Cross(a.Direction, b.Direction));
            if (angle > 0.035 || Math.Abs(a.Radius - b.Radius) > Math.Max(tolerance, Math.Min(a.Radius, b.Radius) * 0.15)) continue;
            double t0 = a.Parameter(b.Origin + b.Direction * b.Low), t1 = a.Parameter(b.Origin + b.Direction * b.High);
            double low = Math.Min(t0, t1), high = Math.Max(t0, t1);
            P middle = b.Origin + b.Direction * ((b.Low + b.High) * 0.5);
            if ((a.Project(middle) - middle).Length > Math.Max(tolerance * 0.1, Math.Min(a.Radius, b.Radius) * 0.08)) continue;
            if (Math.Max(low - a.High, a.Low - high) > a.Radius * 6) continue;
            P bFirst = b.Origin + b.Direction * (t0 < t1 ? b.Low : b.High), bLast = b.Origin + b.Direction * (t0 < t1 ? b.High : b.Low);
            P first = low < a.Low ? bFirst : a.Origin + a.Direction * a.Low;
            P last = high > a.High ? bLast : a.Origin + a.Direction * a.High;
            // Extents can lie exactly on a terminal cap. Test the interior of the stroke,
            // since a boundary point has no stable inside/outside classification after rotation.
            P direction = (last - first).Unit(); double inset = Math.Min((last - first).Length * 0.1, Math.Min(a.Radius, b.Radius) * 0.1);
            if (!region.SegmentInside(first + direction * inset, last - direction * inset, tolerance)) continue;
            a.Origin = first; a.Direction = (last - first).Unit(); a.Low = 0; a.High = (last - first).Length;
            a.Radius = (a.Radius + b.Radius) * 0.5; a.Sides.AddRange(b.Sides); axes.RemoveAt(j);
        }
        return axes;
    }
    // Reconstruct the stroke graph from pairs of visible boundary walls. This path does
    // not sample or prune a medial axis: pointed miters cannot introduce terminal spurs.
    private sealed class WallJunction
    {
        internal P Point;
        internal readonly HashSet<int> Axes = new HashSet<int>();
    }
    private static bool WallGraph(Region region, double tolerance, Result result)
    {
        List<StraightAxis> axes = StraightAxes(region, tolerance);
        if (axes.Count == 0 || axes.Count > 100) return false;
        double width = 2 * region.Area / region.Perimeter;
        // Every long boundary run must have a measured opposite wall. Unmatched short
        // runs are end caps; a curved/unpaired outline uses the curved-stroke solver.
        double covered = 0;
        foreach (List<P> loop in region.Loops)
        {
            bool[] supported = new bool[loop.Count]; double[] lengths = new double[loop.Count];
            for (int i = 0; i < loop.Count; i++)
            {
                P a = loop[i], b = loop[(i + 1) % loop.Count]; lengths[i] = (b - a).Length;
                foreach (StraightAxis axis in axes) foreach (Segment side in axis.Sides)
                    if ((a - Closest(side, a)).Length <= tolerance * 0.1 && (b - Closest(side, b)).Length <= tolerance * 0.1) supported[i] = true;
                if (supported[i]) covered += lengths[i];
            }
            int start = Array.FindIndex(supported, delegate(bool value) { return value; });
            if (start < 0) return false;
            double gap = 0;
            for (int k = 1; k <= loop.Count; k++)
            {
                int i = (start + k) % loop.Count;
                gap = supported[i] ? 0 : gap + lengths[i];
                if (gap > width * 2.1) return false;
            }
        }
        if (covered < region.Perimeter * 0.55) return false;
        var junctions = new List<WallJunction>();
        for (int i = 0; i < axes.Count; i++) for (int j = i + 1; j < axes.Count; j++)
        {
            StraightAxis a = axes[i], b = axes[j]; double cross = P.Cross(a.Direction, b.Direction);
            if (Math.Abs(cross) < 0.08) continue;
            P p = a.Origin + a.Direction * (P.Cross(b.Origin - a.Origin, b.Direction) / cross);
            double reach = (a.Radius + b.Radius) / Math.Abs(cross) + tolerance;
            if (a.Outside(p) > reach || b.Outside(p) > reach || !region.Inside(p)
                || !region.SegmentInside(p, a.Origin + a.Direction * ((a.Low + a.High) * 0.5), tolerance)
                || !region.SegmentInside(p, b.Origin + b.Direction * ((b.Low + b.High) * 0.5), tolerance)) continue;
            var node = new WallJunction { Point = p }; node.Axes.Add(i); node.Axes.Add(j); junctions.Add(node);
        }
        // Several pairwise intersections can describe one finite-width junction. Merge
        // only locally, never across a hole. The longest supported wall pair fixes the
        // through direction; the remaining branches are fitted to that common junction.
        for (int i = 0; i < junctions.Count; i++) for (int j = junctions.Count - 1; j > i; j--)
        {
            WallJunction a = junctions[i], b = junctions[j]; bool shared = false;
            foreach (int k in a.Axes) if (b.Axes.Contains(k)) shared = true;
            double distance = (a.Point - b.Point).Length;
            if (!shared || distance > (region.Loops.Count == 1 ? width * 1.1 : tolerance * 0.01)
                || (distance > tolerance * 0.01 && !region.SegmentInside(a.Point, b.Point, tolerance))) continue;
            foreach (int k in b.Axes) a.Axes.Add(k);
            P middle = (a.Point + b.Point) * 0.5; int longest = -1; double span = -1;
            foreach (int k in a.Axes) if (axes[k].High - axes[k].Low > span) { longest = k; span = axes[k].High - axes[k].Low; }
            a.Point = axes[longest].Project(middle); junctions.RemoveAt(j);
        }
        var nodes = new List<Node>();
        foreach (WallJunction junction in junctions) nodes.Add(new Node { Point = junction.Point, Radius = region.Distance(junction.Point) });
        for (int i = 0; i < axes.Count; i++)
        {
            StraightAxis axis = axes[i]; var sequence = new List<int>();
            for (int j = 0; j < junctions.Count; j++) if (junctions[j].Axes.Contains(i)) sequence.Add(j);
            sequence.Sort(delegate(int a, int b) { return axis.Parameter(nodes[a].Point).CompareTo(axis.Parameter(nodes[b].Point)); });
            for (int end = 0; end < 2; end++)
            {
                double value = end == 0 ? axis.Low : axis.High; P cap;
                bool hasCap = WallCap(axis, value, end == 0 ? -1 : 1, region, tolerance, out cap);
                bool connected = sequence.Count > 0 && (end == 0
                    ? axis.Parameter(nodes[sequence[0]].Point) <= value + axis.Radius * 2
                    : axis.Parameter(nodes[sequence[sequence.Count - 1]].Point) >= value - axis.Radius * 2);
                if (connected && !hasCap) continue;
                P endpoint = hasCap ? cap : axis.Origin + axis.Direction * (value + (end == 0 ? 1 : -1) * axis.Radius * 0.1);
                if (!region.Inside(endpoint)) return false;
                int index = nodes.Count; nodes.Add(new Node { Point = endpoint, Radius = axis.Radius });
                if (end == 0) sequence.Insert(0, index); else sequence.Add(index);
            }
            for (int j = 1; j < sequence.Count; j++)
            {
                int a = sequence[j - 1], b = sequence[j];
                if ((nodes[a].Point - nodes[b].Point).Length < tolerance * 0.01) continue;
                if (!region.SegmentInside(nodes[a].Point, nodes[b].Point, tolerance)) return false;
                nodes[a].Links.Add(b); nodes[b].Links.Add(a);
            }
        }
        try { CheckTopology(nodes, region.Loops.Count - 1); } catch (InvalidOperationException) { return false; }
        List<Stroke> strokes = Trace(nodes); if (strokes.Count == 0) return false;
        double restoredArea = 0;
        foreach (Stroke stroke in strokes)
        {
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                P a = stroke.Points[i - 1], b = stroke.Points[i]; double r = region.Distance((a + b) * 0.5);
                restoredArea += 2 * r * (b - a).Length;
            }
        }
        // Forward area consistency rejects an incomplete or excessive stroke network.
        // Wall coverage and hole count above test the shape, not only its total area.
        if (restoredArea < region.Area * 0.65 || restoredArea > region.Area * 1.4) return false;
        foreach (Stroke stroke in strokes) for (int i = 1; i + 1 < stroke.Points.Count; i++) stroke.Corners.Add(i);
        InsetTerminals(strokes, region, tolerance);
        foreach (Stroke stroke in strokes)
        {
            var path = new List<Bezier>();
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                path.Add(new Bezier { A = stroke.Points[i - 1], D = stroke.Points[i], Line = true });
                stroke.Corners.Add(i - 1); stroke.Corners.Add(i);
            }
            result.Strokes.Add(stroke); result.Curves.Add(path);
        }
        result.WallReconstruction = true;
        return true;
    }
    private static bool WallCap(StraightAxis axis, double parameter, int sign, Region region, double tolerance, out P endpoint)
    {
        endpoint = new P(); double best = double.MaxValue;
        foreach (Segment edge in BoundarySegments(region, tolerance))
        {
            P v = edge.B - edge.A; double cross = P.Cross(axis.Direction, v);
            if (v.Length > axis.Radius * 4 || Math.Abs(cross) < v.Length * 0.5) continue;
            double t = P.Cross(edge.A - axis.Origin, v) / cross;
            double u = P.Cross(edge.A - axis.Origin, axis.Direction) / cross;
            double distance = Math.Abs(t - parameter);
            if (u < 0 || u > 1 || distance > axis.Radius * 1.5 || distance >= best) continue;
            bool sideA = false, sideB = false;
            foreach (Segment side in axis.Sides)
            {
                if ((edge.A - Closest(side, edge.A)).Length <= tolerance * 0.1) sideA = true;
                if ((edge.B - Closest(side, edge.B)).Length <= tolerance * 0.1) sideB = true;
            }
            if (!sideA || !sideB) continue;
            P cap = axis.Origin + axis.Direction * (t - sign * Math.Max(tolerance * 0.1, axis.Radius * 0.1));
            if (!region.Inside(cap)) continue;
            best = distance; endpoint = cap;
        }
        return best < double.MaxValue;
    }

    private static int NearestAxis(P p, List<StraightAxis> axes, double tolerance)
    {
        int best = -1; double score = double.MaxValue;
        for (int i = 0; i < axes.Count; i++)
        {
            StraightAxis a = axes[i]; double perpendicular = (p - a.Project(p)).Length, outside = a.Outside(p);
            if (perpendicular > Math.Max(tolerance * 4, a.Radius * 0.35) || outside > a.Radius * 2) continue;
            double distance = perpendicular + outside * 0.15;
            if (distance < score) { score = distance; best = i; }
        }
        return best;
    }
    private static bool SharpIntersection(StraightAxis a, StraightAxis b, Region region, double tolerance, out P corner)
    {
        corner = new P(); double cross = P.Cross(a.Direction, b.Direction);
        if (Math.Abs(cross) < 0.15) return false;
        // Require an actual sharp outline vertex shared by the two directions. Preserve fillets.
        bool sharp = false;
        foreach (Segment x in a.Sides) foreach (Segment y in b.Sides)
            if (Math.Min(Math.Min((x.A - y.A).Length, (x.A - y.B).Length), Math.Min((x.B - y.A).Length, (x.B - y.B).Length)) < tolerance * 0.1) sharp = true;
        if (!sharp) return false;
        corner = a.Origin + a.Direction * (P.Cross(b.Origin - a.Origin, b.Direction) / cross);
        // A fixed four-radius reach clips the miter of acute triangles (e.g. the digit 4).
        // Offset-line intersections have an angle-dependent reach, bounded by these two widths.
        double reach = (a.Radius + b.Radius) / Math.Abs(cross) + tolerance;
        return a.Outside(corner) <= reach && b.Outside(corner) <= reach
            && region.Inside(corner) && region.Distance(corner) >= Math.Min(a.Radius, b.Radius) * 0.5;
    }
    private static void RestoreStraightStrokes(List<Stroke> strokes, Region region, double tolerance)
    {
        List<StraightAxis> axes = StraightAxes(region, tolerance); if (axes.Count == 0) return;
        List<P> before = new List<P>(), after = new List<P>();
        foreach (Stroke stroke in strokes) foreach (int index in new int[] { 0, stroke.Points.Count - 1 })
        {
            P p = stroke.Points[index]; if (before.Contains(p)) continue;
            int nearest = NearestAxis(p, axes, tolerance); if (nearest < 0) continue;
            StraightAxis axis = axes[nearest]; P q = axis.Project(p); double distance = double.MaxValue;
            foreach (StraightAxis other in axes)
            {
                P corner; if (!SharpIntersection(axis, other, region, tolerance, out corner)) continue;
                double d = (corner - p).Length;
                if (d <= Math.Min(axis.Radius, other.Radius) && d < distance) { q = corner; distance = d; }
            }
            if (!region.Inside(q)) continue;
            bool safe = true;
            foreach (Stroke s in strokes)
            {
                if (s.Points[0].Equals(p) && !region.SegmentInside(q, s.Points[1], tolerance)) safe = false;
                if (s.Points[s.Points.Count - 1].Equals(p) && !region.SegmentInside(q, s.Points[s.Points.Count - 2], tolerance)) safe = false;
            }
            if (safe) { before.Add(p); after.Add(q); }
        }
        // All incident branches must receive exactly the same new junction position.
        foreach (Stroke stroke in strokes)
        {
            bool distinct = (stroke.Points[0] - stroke.Points[stroke.Points.Count - 1]).Length > tolerance * 0.01;
            foreach (int index in new int[] { 0, stroke.Points.Count - 1 })
            { int found = before.IndexOf(stroke.Points[index]); if (found >= 0) stroke.Points[index] = after[found]; }
            stroke.ContractedJunction = distinct && (stroke.Points[0] - stroke.Points[stroke.Points.Count - 1]).Length <= tolerance * 0.01;
        }
        foreach (Stroke stroke in strokes)
        {
            List<P> p = stroke.Points; List<StraightRun> runs = new List<StraightRun>();
            for (int i = 0; i < p.Count; )
            {
                int label = NearestAxis(p[i], axes, tolerance), first = i++;
                while (i < p.Count && NearestAxis(p[i], axes, tolerance) == label) i++;
                if (label < 0) continue;
                StraightAxis axis = axes[label]; int last = i - 1;
                if (Math.Abs(axis.Parameter(p[last]) - axis.Parameter(p[first])) >= Math.Max(axis.Radius * 0.5, tolerance * 8))
                    runs.Add(new StraightRun { First = first, Last = last, Axis = axis });
            }
            if (runs.Count > 0)
            {
                StraightRun firstRun = runs[0], lastRun = runs[runs.Count - 1];
                if (AxisReachesEndpoint(firstRun.Axis, p, 0, firstRun.First, tolerance)) firstRun.First = 0;
                if (AxisReachesEndpoint(lastRun.Axis, p, p.Count - 1, lastRun.Last, tolerance)) lastRun.Last = p.Count - 1;
            }
            for (int i = 0; i + 1 < runs.Count; i++)
            {
                StraightRun a = runs[i], b = runs[i + 1]; P corner;
                if (!SharpIntersection(a.Axis, b.Axis, region, tolerance, out corner)) continue;
                double radius = Math.Max(a.Axis.Radius, b.Axis.Radius), distance = double.MaxValue, length = 0;
                for (int k = a.Last; k < b.First; k++)
                { distance = Math.Min(distance, (corner - Closest(new Segment(p[k], p[k + 1]), corner)).Length); length += (p[k + 1] - p[k]).Length; }
                double cornerReach = Math.Max(radius, Math.Max(a.Axis.Outside(corner), b.Axis.Outside(corner)));
                if (length <= Math.Max(radius * 8, cornerReach * 4) && distance <= cornerReach + tolerance * 2
                    && region.SegmentInside(a.Axis.Project(p[a.First]), corner, tolerance)
                    && region.SegmentInside(corner, b.Axis.Project(p[b.Last]), tolerance))
                { a.JoinNext = true; a.Corner = corner; }
            }
            List<P> output = new List<P>(); HashSet<int> corners = new HashSet<int>(); int cursor = 0;
            for (int i = 0; i < runs.Count; i++)
            {
                StraightRun run = runs[i]; bool joined = i > 0 && runs[i - 1].JoinNext;
                int first = run.First, last = run.Last;
                // A join to a genuinely curved part is adjusted only within the fitting tolerance.
                if (!joined && first != 0) while (first < last && (p[first] - run.Axis.Project(p[first])).Length > tolerance) first++;
                if (!run.JoinNext && last != p.Count - 1) while (last > first && (p[last] - run.Axis.Project(p[last])).Length > tolerance) last--;
                // Protect the endpoints of each recovered straight run as well as sharp corners:
                // a subsequent spline fit must not absorb a long line into a junction's bend.
                if (!joined)
                {
                    while (cursor < first) AddDistinct(output, p[cursor++], tolerance);
                    AddDistinct(output, run.Axis.Project(p[first]), tolerance); corners.Add(output.Count - 1);
                }
                if (run.JoinNext) { AddDistinct(output, run.Corner, tolerance); corners.Add(output.Count - 1); cursor = runs[i + 1].First; }
                else { AddDistinct(output, run.Axis.Project(p[last]), tolerance); corners.Add(output.Count - 1); cursor = last + 1; }
            }
            while (cursor < p.Count) AddDistinct(output, p[cursor++], tolerance);
            if (output.Count < 2) continue;
            // Keep protected shared junctions and geometric loop closure exact.
            output[0] = p[0]; output[output.Count - 1] = p[p.Count - 1];
            bool safeOutput = true;
            for (int i = 1; i < output.Count; i++) if (!region.SegmentInside(output[i - 1], output[i], tolerance)) safeOutput = false;
            if (!safeOutput) continue;
            p.Clear(); p.AddRange(output); foreach (int corner in corners) if (corner > 0 && corner < p.Count - 1) stroke.Corners.Add(corner);
        }
    }
    // At a sharp line/curve joint the medial ridge rounds the inner outline vertex.
    // Continue the curved wall locally and measure across to the actual opposite wall;
    // intersect that midline with the proven straight axis. Tangential fillets do not qualify.
    private sealed class CurvedWall
    {
        internal P Origin, Direction, Center;
        internal double Span, Radius, Angle, Turn, Inward;
        internal readonly double[] Coefficients = new double[3];
        internal readonly HashSet<Segment> Edges = new HashSet<Segment>();
        internal P At(double t)
        {
            if (Radius > 0) { double a = Angle + Turn * t / Radius; return Center + new P(Math.Cos(a), Math.Sin(a)) * Radius; }
            double x = t / Span, y = x * (Coefficients[0] + x * (Coefficients[1] + x * Coefficients[2]));
            return Origin + Direction * t + new P(-Direction.Y, Direction.X) * y;
        }
        internal P Tangent(double t)
        {
            if (Radius > 0) { double a = Angle + Turn * t / Radius; return new P(-Math.Sin(a), Math.Cos(a)) * Turn; }
            double x = t / Span, y = (Coefficients[0] + x * (2 * Coefficients[1] + x * 3 * Coefficients[2])) / Span;
            return (Direction + new P(-Direction.Y, Direction.X) * y).Unit();
        }
    }
    private static CurvedWall FitCurvedWall(List<P> p, double radius, double tolerance)
    {
        if (p.Count < 5 || (p[p.Count - 1] - p[0]).Length < radius * 1.5) return null;
        P a = p[0], b = p[p.Count / 2] - a, c = p[p.Count - 1] - a; double cross = P.Cross(b, c);
        if (Math.Abs(cross) > c.Length * c.Length * 1e-5)
        {
            P center = a + new P((P.Dot(b, b) * c.Y - P.Dot(c, c) * b.Y) / (2 * cross),
                (b.X * P.Dot(c, c) - c.X * P.Dot(b, b)) / (2 * cross));
            double r = (a - center).Length, turn = Math.Sign(cross), angle = Math.Atan2(a.Y - center.Y, a.X - center.X);
            double delta = Math.Atan2(P.Cross(a - center, p[p.Count - 1] - center), P.Dot(a - center, p[p.Count - 1] - center));
            bool valid = r > radius && delta * turn > 0 && Math.Abs(delta) < 1.5;
            foreach (P point in p) if (Math.Abs((point - center).Length - r) > tolerance * 0.5) valid = false;
            if (valid) return new CurvedWall { Origin = a, Center = center, Radius = r, Angle = angle, Turn = turn, Span = Math.Abs(delta) * r };
        }
        var wall = new CurvedWall { Origin = a, Direction = c.Unit(), Span = c.Length };
        P normal = new P(-wall.Direction.Y, wall.Direction.X); double[,] matrix = new double[3, 4];
        // Arc-length weights make the fit insensitive to uneven STEP subdivisions.
        for (int i = 1; i < p.Count; i++)
        {
            double t = P.Dot(p[i] - a, wall.Direction) / wall.Span, y = P.Dot(p[i] - a, normal);
            if (t <= 0 || t > 1.000001) return null;
            double weight = ((p[i] - p[i - 1]).Length + (i + 1 < p.Count ? (p[i + 1] - p[i]).Length : 0)) / wall.Span;
            for (int row = 0; row < 3; row++)
            { for (int col = 0; col < 3; col++) matrix[row, col] += weight * Math.Pow(t, row + col + 2); matrix[row, 3] += weight * Math.Pow(t, row + 1) * y; }
        }
        for (int col = 0; col < 3; col++)
        {
            int pivot = col; for (int row = col + 1; row < 3; row++) if (Math.Abs(matrix[row, col]) > Math.Abs(matrix[pivot, col])) pivot = row;
            if (Math.Abs(matrix[pivot, col]) < 1e-12) return null;
            for (int k = col; k < 4; k++) { double value = matrix[col, k]; matrix[col, k] = matrix[pivot, k]; matrix[pivot, k] = value; }
            double divisor = matrix[col, col]; for (int k = col; k < 4; k++) matrix[col, k] /= divisor;
            for (int row = 0; row < 3; row++) if (row != col)
            { double factor = matrix[row, col]; for (int k = col; k < 4; k++) matrix[row, k] -= factor * matrix[col, k]; }
        }
        for (int i = 0; i < 3; i++) wall.Coefficients[i] = matrix[i, 3];
        foreach (P point in p) if ((wall.At(P.Dot(point - a, wall.Direction)) - point).Length > tolerance * 2) return null;
        return wall;
    }
    private static bool CurvedMidpoint(CurvedWall wall, StraightAxis axis, List<Segment> edges, Region region, double t, double tolerance, out P mid)
    {
        mid = new P(); P p = wall.At(t), tangent = wall.Tangent(t), normal = new P(-tangent.Y, tangent.X) * wall.Inward;
        Segment hit = null; double distance = double.MaxValue;
        foreach (Segment edge in edges) if (!wall.Edges.Contains(edge))
        {
            P v = edge.B - edge.A; double cross = P.Cross(normal, v); if (Math.Abs(cross) < v.Length * 1e-10) continue;
            double d = P.Cross(edge.A - p, v) / cross, u = P.Cross(edge.A - p, normal) / cross;
            if (d > tolerance * 0.01 && d < distance && u >= -1e-9 && u <= 1 + 1e-9) { distance = d; hit = edge; }
        }
        if (hit == null || distance < axis.Radius * 0.4 || distance > axis.Radius * 5
            || Math.Abs(P.Dot((hit.B - hit.A).Unit(), tangent)) < 0.65) return false;
        foreach (Segment side in axis.Sides)
            if (((hit.A - side.A).Length < tolerance * 0.1 && (hit.B - side.B).Length < tolerance * 0.1)
                || ((hit.B - side.A).Length < tolerance * 0.1 && (hit.A - side.B).Length < tolerance * 0.1)) return false;
        mid = p + normal * (distance * 0.5); return region.Inside(mid);
    }
    private static void RestoreCurvedCorners(List<Stroke> strokes, Region region, double tolerance)
    {
        List<StraightAxis> axes = StraightAxes(region, tolerance); if (axes.Count == 0) return;
        var edges = new List<Segment>(); var loops = new List<List<Segment>>();
        foreach (List<P> loop in region.Loops)
        {
            var chain = new List<Segment>(); List<P> p = BoundaryVertices(loop, tolerance);
            for (int i = 0; i < p.Count; i++) chain.Add(new Segment(p[i], p[(i + 1) % p.Count]));
            loops.Add(chain); edges.AddRange(chain);
        }
        for (int loopIndex = 0; loopIndex < loops.Count; loopIndex++)
        {
            List<Segment> loop = loops[loopIndex]; double orientation = Math.Sign(SignedArea(region.Loops[loopIndex])) * (loopIndex == 0 ? 1 : -1);
            for (int index = 0; index < loop.Count; index++)
            {
                Segment previous = loop[(index + loop.Count - 1) % loop.Count], next = loop[index]; P vertex = next.A;
                if (P.Cross((previous.B - previous.A).Unit(), (next.B - next.A).Unit()) * orientation >= -0.15) continue;
                foreach (StraightAxis axis in axes) foreach (int sign in new int[] { -1, 1 })
                {
                    Segment line = sign == 1 ? previous : next; bool attached = false;
                    foreach (Segment side in axis.Sides)
                        if (((side.A - vertex).Length < tolerance * 0.1 || (side.B - vertex).Length < tolerance * 0.1)
                            && Math.Abs(P.Dot((line.B - line.A).Unit(), axis.Direction)) > 0.95
                            && (line.B - line.A).Length >= axis.Radius * 2) attached = true;
                    if (!attached) continue;
                    var points = new List<P> { vertex }; var owned = new List<Segment>(); double length = 0; P direction = new P();
                    int edgeIndex = sign == 1 ? index : (index + loop.Count - 1) % loop.Count;
                    for (int count = 0; count < loop.Count; count++)
                    {
                        Segment edge = loop[edgeIndex]; P end = sign == 1 ? edge.B : edge.A, u = (end - points[points.Count - 1]).Unit();
                        if (count > 0 && P.Dot(u, direction) < 0.85) break;
                        owned.Add(edge); double size = (end - points[points.Count - 1]).Length, left = axis.Radius * 6 - length;
                        if (size > left) end = points[points.Count - 1] + u * left;
                        points.Add(end); length += Math.Min(size, left); direction = u;
                        if (length >= axis.Radius * 6) break;
                        edgeIndex = (edgeIndex + sign + loop.Count) % loop.Count;
                    }
                    CurvedWall wall = FitCurvedWall(points, axis.Radius, tolerance); if (wall == null) continue;
                    if (Math.Abs(P.Cross(wall.Tangent(0), axis.Direction)) < 0.2) continue;
                    foreach (Segment edge in owned) wall.Edges.Add(edge);
                    P sample = wall.At(wall.Span * 0.6), tangent = wall.Tangent(wall.Span * 0.6), normal = new P(-tangent.Y, tangent.X) * (axis.Radius * 0.1);
                    bool leftInside = region.Inside(sample + normal), rightInside = region.Inside(sample - normal);
                    if (leftInside == rightInside) continue; wall.Inward = leftInside ? 1 : -1;
                    P first; if (!CurvedMidpoint(wall, axis, edges, region, 0, tolerance, out first)) continue;
                    double lastT = 0, lastCross = P.Cross(first - axis.Origin, axis.Direction), rootT = double.NaN;
                    double reach = Math.Min(axis.Radius * 4, wall.Span * 0.8); P root = new P();
                    for (int k = 1; k <= 64; k++)
                    {
                        double t = -reach * k / 64; P mid;
                        if (!CurvedMidpoint(wall, axis, edges, region, t, tolerance, out mid)) break;
                        double cross = P.Cross(mid - axis.Origin, axis.Direction);
                        if (cross * lastCross <= 0)
                        {
                            double low = t, high = lastT; bool valid = true;
                            for (int j = 0; j < 40; j++)
                            {
                                double u = (low + high) * 0.5;
                                if (!CurvedMidpoint(wall, axis, edges, region, u, tolerance, out mid)) { valid = false; break; }
                                if (P.Cross(mid - axis.Origin, axis.Direction) * lastCross > 0) high = u; else low = u;
                            }
                            if (valid) { rootT = (low + high) * 0.5; root = axis.Project(mid); } break;
                        }
                        lastT = t; lastCross = cross;
                    }
                    if (double.IsNaN(rootT) || (root - vertex).Length > axis.Radius * 4 || !region.Inside(root)) continue;
                    foreach (Stroke stroke in strokes) SpliceCurvedCorner(stroke, wall, axis, edges, region, rootT, root, tolerance);
                }
            }
        }
    }
    private static void SpliceCurvedCorner(Stroke stroke, CurvedWall wall, StraightAxis axis, List<Segment> edges, Region region, double rootT, P root, double tolerance)
    {
        var samples = new List<P>(); var parameters = new List<double>();
        for (int k = 0; k <= 64; k++)
        { double t = wall.Span * 0.9 * k / 64; P p; if (!CurvedMidpoint(wall, axis, edges, region, t, tolerance, out p)) break; samples.Add(p); parameters.Add(t); }
        for (int reversed = 0; reversed < 2; reversed++)
        {
            var old = new List<P>(stroke.Points); var oldCorners = new HashSet<int>();
            if (reversed != 0) old.Reverse(); foreach (int c in stroke.Corners) oldCorners.Add(reversed == 0 ? c : old.Count - 1 - c);
            for (int i = 0; i + 2 < old.Count; i++)
            {
                if ((old[i] - axis.Project(old[i])).Length > tolerance * 0.1 || (old[i + 1] - axis.Project(old[i + 1])).Length > tolerance * 0.1
                    || (old[i + 1] - old[i]).Length < axis.Radius || (root - old[i + 1]).Length > axis.Radius * 4
                    || (root - old[i + 1]).Length < tolerance || !region.SegmentInside(old[i], root, tolerance)) continue;
                int join = -1; double joinT = 0, walked = 0;
                for (int k = i + 2; k < old.Count - 1; k++)
                {
                    walked += (old[k] - old[k - 1]).Length; if (walked > axis.Radius * 8 || oldCorners.Contains(k)) break;
                    if ((old[k] - axis.Project(old[k])).Length < axis.Radius * 1.2) continue;
                    double best = double.MaxValue, t = 0;
                    for (int n = 1; n < samples.Count; n++)
                    {
                        P v = samples[n] - samples[n - 1]; if (v.Length <= tolerance * 0.001) continue;
                        double u = Math.Max(0, Math.Min(1, P.Dot(old[k] - samples[n - 1], v) / P.Dot(v, v)));
                        double d = (samples[n - 1] + v * u - old[k]).Length;
                        if (d < best) { best = d; t = parameters[n - 1] + (parameters[n] - parameters[n - 1]) * u; }
                    }
                    if (best <= tolerance * 2 && t > Math.Min(axis.Radius * 1.5, wall.Span * 0.25)) { join = k; joinT = t; break; }
                }
                if (join < 0) continue;
                var output = new List<P>(); var corners = new HashSet<int>();
                for (int k = 0; k <= i; k++) { output.Add(old[k]); if (oldCorners.Contains(k)) corners.Add(output.Count - 1); }
                output.Add(root); corners.Add(output.Count - 1); bool valid = true;
                int count = Math.Max(4, (int)Math.Ceiling((joinT - rootT) / Math.Sqrt(axis.Radius * tolerance) * 2));
                for (int k = 1; k < count; k++)
                { P p; if (!CurvedMidpoint(wall, axis, edges, region, rootT + (joinT - rootT) * k / count, tolerance, out p)) { valid = false; break; } output.Add(p); }
                for (int k = join; k < old.Count; k++) { output.Add(old[k]); if (oldCorners.Contains(k)) corners.Add(output.Count - 1); }
                for (int k = 1; k < output.Count; k++) if (!region.SegmentInside(output[k - 1], output[k], tolerance)) valid = false;
                if (!valid) continue;
                if (reversed != 0) { output.Reverse(); var mapped = new HashSet<int>(); foreach (int c in corners) mapped.Add(output.Count - 1 - c); corners = mapped; }
                stroke.Points.Clear(); stroke.Points.AddRange(output); stroke.Corners.Clear(); foreach (int c in corners) stroke.Corners.Add(c);
                stroke.CurvedCorners.Add(root);
                return;
            }
        }
    }
    private static void AddDistinct(List<P> points, P point, double tolerance)
    { if (points.Count == 0 || (points[points.Count - 1] - point).Length > tolerance * 0.001) points.Add(point); }
    private static bool AxisReachesEndpoint(StraightAxis axis, List<P> p, int endpoint, int runEnd, double tolerance)
    {
        if ((p[endpoint] - axis.Project(p[endpoint])).Length > tolerance * 0.1) return false;
        double length = 0; int first = Math.Min(endpoint, runEnd), last = Math.Max(endpoint, runEnd);
        for (int i = first; i <= last; i++)
        {
            if ((p[i] - axis.Project(p[i])).Length > axis.Radius * 0.6) return false;
            if (i > first) length += (p[i] - p[i - 1]).Length;
        }
        return length <= axis.Radius * 3;
    }

    public static List<Bezier> Fit(Stroke stroke, Region region, double tolerance)
    {
        List<Bezier> result = new List<Bezier>(); List<P> p = stroke.Points;
        if (p.Count < 2) return result;
        if (stroke.Corners.Count > 0)
        {
            int first = 0;
            for (int last = 1; last < p.Count; last++) if (stroke.Corners.Contains(last) || last == p.Count - 1)
            {
                FitRange(p, first, last, (p[first + 1] - p[first]).Unit(),
                    (p[last - 1] - p[last]).Unit(), tolerance, region, result, 0);
                first = last;
            }
        }
        else if (stroke.Closed && p.Count >= 5)
        {
            int middle = (p.Count - 1) / 2;
            P t0 = (p[1] - p[p.Count - 2]).Unit(), tm = (p[middle + 1] - p[middle - 1]).Unit();
            FitRange(p, 0, middle, t0, tm * -1, tolerance, region, result, 0);
            FitRange(p, middle, p.Count - 1, tm, t0 * -1, tolerance, region, result, 0);
        }
        else FitRange(p, 0, p.Count - 1, Tangent(p, 0, 1), Tangent(p, p.Count - 1, -1), tolerance, region, result, 0);
        return result;
    }
    private static P Tangent(List<P> p, int i, int direction)
    {
        int j = i + direction * Math.Min(4, direction > 0 ? p.Count - 1 - i : i);
        return (p[j] - p[i]).Unit();
    }
    private static void FitRange(List<P> p, int first, int last, P start, P end, double tol, Region region, List<Bezier> result, int depth)
    {
        P a = p[first], d = p[last]; double chord = (d - a).Length;
        if (last - first < 1 || depth > 40) throw new InvalidOperationException("Не удалось сгладить среднюю линию.");
        Segment line = new Segment(a, d); double lineError = 0, lastProjection = -1;
        bool monotone = chord > tol * 0.02;
        for (int i = first; i <= last; i++)
        {
            lineError = Math.Max(lineError, (p[i] - Closest(line, p[i])).Length);
            double projection = chord > 0 ? P.Dot(p[i] - a, d - a) / (chord * chord) : 0;
            if (projection + 1e-5 < lastProjection) monotone = false; lastProjection = projection;
        }
        if (monotone && lineError <= tol && SafeCurve(new Bezier { A = a, D = d, Line = true }, region, tol))
        { result.Add(new Bezier { A = a, D = d, Line = true }); return; }
        int count = last - first + 1; double[] u = new double[count];
        for (int i = 1; i < count; i++) u[i] = u[i - 1] + (p[first + i] - p[first + i - 1]).Length;
        if (u[count - 1] < tol * 0.02) throw new InvalidOperationException("Кривая вырождена.");
        double total = u[count - 1]; for (int i = 1; i < count; i++) u[i] /= total;
        double aa = 0, ab = 0, bb = 0, ra = 0, rb = 0;
        for (int i = 0; i < count; i++)
        {
            double t = u[i], s = 1 - t, b1 = 3 * s * s * t, b2 = 3 * s * t * t;
            P va = start * b1, vb = end * b2, residual = p[first + i] - a * (s * s * s + b1) - d * (b2 + t * t * t);
            aa += P.Dot(va, va); ab += P.Dot(va, vb); bb += P.Dot(vb, vb); ra += P.Dot(va, residual); rb += P.Dot(vb, residual);
        }
        double determinant = aa * bb - ab * ab;
        double alpha = determinant > 1e-20 ? (ra * bb - rb * ab) / determinant : chord / 3;
        double beta = determinant > 1e-20 ? (rb * aa - ra * ab) / determinant : chord / 3;
        if (alpha <= tol * 0.01 || beta <= tol * 0.01 || alpha > total || beta > total) alpha = beta = chord / 3;
        Bezier curve = new Bezier { A = a, B = a + start * alpha, C = d + end * beta, D = d };
        double error = 0; int split = (first + last) / 2;
        for (int i = 1; i < count - 1; i++)
        {
            double e = (curve.At(u[i]) - p[first + i]).Length;
            if (e > error) { error = e; split = first + i; }
        }
        if (count > 2 && error <= tol && SafeCurve(curve, region, tol)) { result.Add(curve); return; }
        if (count == 2)
        {
            if (!SafeCurve(new Bezier { A = a, D = d, Line = true }, region, tol)) throw new InvalidOperationException("Средняя линия выходит за выбранную грань.");
            result.Add(new Bezier { A = a, D = d, Line = true }); return;
        }
        if (split <= first || split >= last) split = (first + last) / 2;
        P tangent = (p[split + 1] - p[split - 1]).Unit();
        FitRange(p, first, split, start, tangent * -1, tol, region, result, depth + 1);
        FitRange(p, split, last, tangent, end, tol, region, result, depth + 1);
    }
    private static bool SafeCurve(Bezier curve, Region region, double tolerance)
    {
        if (region == null) return true;
        if (curve.Line) return region.SegmentInside(curve.A, curve.D, tolerance);
        return SafePiece(curve.A, curve.B, curve.C, curve.D, region, tolerance, 0);
    }
    private static bool SafePiece(P a, P b, P c, P d, Region region, double tolerance, int depth)
    {
        if (!region.Inside(a) || !region.Inside(d)) return false;
        Segment chord = new Segment(a, d);
        double flatness = Math.Max((b - Closest(chord, b)).Length, (c - Closest(chord, c)).Length);
        if (flatness <= tolerance * 0.125 && region.SegmentInside(a, d, tolerance))
        {
            double clearance = double.MaxValue;
            foreach (Segment edge in region.Edges) clearance = Math.Min(clearance, Math.Min(Math.Min((a - Closest(edge, a)).Length, (d - Closest(edge, d)).Length),
                Math.Min((edge.A - Closest(chord, edge.A)).Length, (edge.B - Closest(chord, edge.B)).Length)));
            if (clearance > flatness + tolerance * 0.001) return true;
        }
        if (depth >= 20) return false;
        P ab = (a + b) * 0.5, bc = (b + c) * 0.5, cd = (c + d) * 0.5, abc = (ab + bc) * 0.5, bcd = (bc + cd) * 0.5, middle = (abc + bcd) * 0.5;
        return SafePiece(a, ab, abc, middle, region, tolerance, depth + 1) && SafePiece(middle, bcd, cd, d, region, tolerance, depth + 1);
    }
}
// END PORTABLE ENGRAVING CENTERLINE CORE
