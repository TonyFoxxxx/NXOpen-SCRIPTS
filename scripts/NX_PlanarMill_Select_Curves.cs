// NX_PlanarMill_Select_Curves.cs - centerlines from planar engraving faces.
// SCRIPT_VERSION: V1.05
// Select an existing PLANAR_MILL, run, then select the flat bottoms of the letters.
// Reconstructs approximate medial curves; round dots use a circle of half the face diameter.
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
    private const string SCRIPT_VERSION = "V1.05";
    private const string GroupAttribute = "NX_PM_AXIS_GROUP", ShapeAttribute = "NX_PM_AXIS_SHAPE";
    private const string SourceAttribute = "NX_PM_AXIS_FACE", CountAttribute = "NX_PM_AXIS_COUNT";
    private const string RevisionAttribute = "NX_PM_AXIS_REVISION", GeometryRevision = "5";
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
                + "\nКруглых точек D/2: " + dots + (skipped > 0 ? "\nРанее обработанных граней пропущено: " + skipped : "")
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
            Show(NXMessageBox.DialogType.Error, ex.Message + "\n\n" + state);
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
            FaceData data = new FaceData { Key = Hash(occurrence + "|" + prototype.OwningPart.Leaf + "|" + prototype.JournalIdentifier) };
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
            foreach (EngravingCenterlines.Region region in GroupRegions(pending[i], frame, joinTolerance))
                pending[i].Results.Add(EngravingRoutes.Build(EngravingCenterlines.Build(region, tolerance), region, 0.02 * unit, tolerance));
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
        int junctions = 0;
        foreach (Junction node in nodes) if (node.Ports.Count > 2)
        {
            junctions++; node.Radius = gap * 1.5; node.Limit = region.Distance(node.Point) * 0.85;
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
        double flat = Math.Min(tolerance * 0.125, gap * 0.01);
        List<List<B>> trimmed = null; Dictionary<int, B> links = null;
        for (int attempt = 0; attempt < 9; attempt++)
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
                    P a = EndPoint(trimmed, port), b = EndPoint(trimmed, pairs[port]);
                    B connector = Join(a, node.Point, b, tolerance);
                    links.Add(port, connector); links.Add(pairs[port], Reverse(connector));
                }
            bool valid = true;
            foreach (Junction node in nodes) if (node.Ports.Count > 2 && !Separated(node, trimmed, pairs, links, gap, flat))
            {
                valid = false; node.Radius *= 1.4;
                if (node.Radius > node.Limit) throw new GapFitException("Разделение узла выходит за ширину штриха.");
            }
            if (valid) break;
            if (attempt == 8) throw new GapFitException("Не удалось разделить узел без касаний.");
        }
        var result = new EngravingCenterlines.Result { WidthEstimate = source.WidthEstimate, SamplingStep = source.SamplingStep,
            BoundarySamples = source.BoundarySamples, SeparatedJunctions = junctions, JunctionGap = junctions > 0 ? gap : 0 };
        result.Circles.AddRange(source.Circles); bool[] used = new bool[edges.Count];
        for (int port = 0; port < pairs.Length; port++) if (pairs[port] < 0 && !used[port / 2])
            Walk(port, edges, trimmed, pairs, links, used, result);
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
        // A K-shaped junction has two medial T nodes. Reconstruct their single straight-stroke
        // intersection only when every external straight guide actually agrees on one point.
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
                double width = region.Distance(a.Point) + region.Distance(b.Point), length = 0;
                foreach (B c in edge.Curves) length += c.Line ? (c.D - c.A).Length : (c.B - c.A).Length + (c.C - c.B).Length + (c.D - c.C).Length;
                if (length > width * 1.5) continue;
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
                if (!valid || guides.Count < 4) continue;
                P center = new P(); bool found = false;
                for (int i = 1; i < guides.Count && !found; i++)
                {
                    B x = guides[0].Line, y = guides[i].Line; P u = (x.D - x.A).Unit(), v = (y.D - y.A).Unit(); double cross = P.Cross(u, v);
                    if (Math.Abs(cross) < 0.2) continue;
                    center = x.A + u * (P.Cross(y.A - x.A, v) / cross); found = true;
                }
                if (!found || (center - a.Point).Length > width * 1.25 || (center - b.Point).Length > width * 1.25 || !region.Inside(center)) continue;
                foreach (Guide guide in guides)
                {
                    P d = (guide.Line.D - guide.Line.A).Unit();
                    if (Math.Abs(P.Cross(d, center - guide.Line.A)) > tolerance * 0.25 || !region.SegmentInside(center, guide.Line.D, tolerance)) valid = false;
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
                var next = new EngravingCenterlines.Result { WidthEstimate = source.WidthEstimate, SamplingStep = source.SamplingStep, BoundarySamples = source.BoundarySamples };
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
            int bestA = -1, bestB = -1; double cost = double.MaxValue;
            for (int i = 0; i < available.Count; i++) for (int j = i + 1; j < available.Count; j++)
            {
                if (!hasHoles && available.Count > 3 && j != i + 1 && !(i == 0 && j == available.Count - 1)) continue;
                int a = available[i], b = available[j]; if (Root(parent, a / 2) == Root(parent, b / 2)) continue;
                double score = 1 + P.Dot(Direction(edges, a), Direction(edges, b));
                if (score < cost) { cost = score; bestA = a; bestB = b; }
            }
            if (bestA < 0) break;
            pairs[bestA] = bestB; pairs[bestB] = bestA; parent[Root(parent, bestA / 2)] = Root(parent, bestB / 2);
            available.Remove(bestA); available.Remove(bestB);
            if (hasHoles || node.Ports.Count == 3) break;
        }
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
        bool[] used, EngravingCenterlines.Result result)
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
        if (RoundDot(region, tolerance, out dot)) { result.Circles.Add(dot); return result; }
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
        CheckTopology(nodes, region.Loops.Count - 1);
        List<Stroke> strokes = Trace(nodes);
        if (strokes.Count == 0)
        {
            throw new InvalidOperationException("Не удалось получить устойчивую среднюю линию этой грани.");
        }
        foreach (Stroke stroke in strokes) Refine(stroke, region, step, tolerance);
        RestoreStraightStrokes(strokes, region, tolerance);
        CleanSnappedJunctions(strokes, region, tolerance);
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
        foreach (P p in loop) if (Math.Abs((p - center).Length - r) > tolerance * 0.5) return false;
        // Check segment midpoints as well: vertices of a polygon alone may lie on a circle.
        foreach (Segment e in region.Edges) if (Math.Abs(((e.A + e.B) * 0.5 - center).Length - r) > tolerance) return false;
        // The requested convention for a round punctuation mark: path diameter = face diameter / 2.
        circle = new Circle { Center = center, Radius = r / 2 }; return true;
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
                if (nodes[current].Links.Count >= 3 && nodes[i].Radius < nodes[current].Radius * 0.35
                    && (length < nodes[current].Radius * 4 + step
                        || IsMiterSpur(nodes, branch, i, current, axes, region, step, tolerance))) remove.AddRange(branch);
            }
            if (remove.Count == 0) break;
            foreach (Pair p in remove) { nodes[p.A].Links.Remove(p.B); nodes[p.B].Links.Remove(p.A); }
        }
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
        // Remove it only inside an empty disk of the face, never a loop surrounding an island.
        for (int i = strokes.Count - 1; i >= 0; i--)
        {
            Stroke stroke = strokes[i]; List<P> p = stroke.Points;
            if (stroke.Closed || p.Count < 2 || (p[0] - p[p.Count - 1]).Length > tolerance * 0.01) continue;
            double radius = region.Distance(p[0]) * 0.8; bool insideDisk = radius > tolerance;
            foreach (P point in p) if ((point - p[0]).Length > radius) insideDisk = false;
            int incident = 0;
            for (int j = 0; j < strokes.Count; j++) if (j != i)
            {
                List<P> other = strokes[j].Points;
                if ((other[0] - p[0]).Length <= tolerance * 0.01) incident++;
                if ((other[other.Count - 1] - p[0]).Length <= tolerance * 0.01) incident++;
            }
            if (insideDisk && incident >= 3) strokes.RemoveAt(i);
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
    private static List<StraightAxis> StraightAxes(Region region, double tolerance)
    {
        List<Segment> sides = new List<Segment>(); double width = 2 * region.Area / region.Perimeter;
        // Join only collinear outline pieces. Tessellated arcs must not become straight strokes.
        foreach (List<P> loop in region.Loops)
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
            for (int i = 0; i < p.Count; i++)
            {
                Segment side = new Segment(p[i], p[(i + 1) % p.Count]);
                if ((side.B - side.A).Length >= Math.Max(width * 0.65, tolerance * 12)) sides.Add(side);
            }
        }
        List<StraightAxis> axes = new List<StraightAxis>();
        for (int i = 0; i < sides.Count; i++) for (int j = i + 1; j < sides.Count; j++)
        {
            Segment a = sides[i], b = sides[j]; P direction = (a.B - a.A).Unit();
            if (Math.Abs(P.Cross(direction, (b.B - b.A).Unit())) > 1e-7) continue;
            double gap = P.Cross(direction, b.A - a.A), radius = Math.Abs(gap) * 0.5;
            if (radius < tolerance * 4 || radius > width) continue;
            double b0 = P.Dot(b.A - a.A, direction), b1 = P.Dot(b.B - a.A, direction);
            double low = Math.Max(0, Math.Min(b0, b1)), high = Math.Min((a.B - a.A).Length, Math.Max(b0, b1));
            if (high - low < radius * 0.9) continue;
            P origin = a.A + new P(-direction.Y, direction.X) * (gap * 0.5);
            bool inside = true;
            for (int k = 1; k <= 3; k++)
            {
                P q = origin + direction * (low + (high - low) * k / 4);
                if (!region.Inside(q) || Math.Abs(region.Distance(q) - radius) > tolerance * 2) inside = false;
            }
            if (!inside) continue;
            StraightAxis axis = new StraightAxis { Origin = origin, Direction = direction, Low = low, High = high, Radius = radius };
            axis.Sides.Add(a); axis.Sides.Add(b); axes.Add(axis);
        }
        // Collinear arms may be split by a crossing stroke. Keep one axis across that junction.
        for (int i = 0; i < axes.Count; i++) for (int j = axes.Count - 1; j > i; j--)
        {
            StraightAxis a = axes[i], b = axes[j];
            if (Math.Abs(P.Cross(a.Direction, b.Direction)) > 1e-7 || Math.Abs(a.Radius - b.Radius) > tolerance
                || (a.Project(b.Origin) - b.Origin).Length > tolerance * 0.1) continue;
            double t0 = a.Parameter(b.Origin + b.Direction * b.Low), t1 = a.Parameter(b.Origin + b.Direction * b.High);
            double low = Math.Min(t0, t1), high = Math.Max(t0, t1);
            if (Math.Max(low - a.High, a.Low - high) > a.Radius * 4) continue;
            P first = a.Origin + a.Direction * Math.Min(a.Low, low), last = a.Origin + a.Direction * Math.Max(a.High, high);
            if (!region.SegmentInside(first, last, tolerance)) continue;
            a.Low = Math.Min(a.Low, low); a.High = Math.Max(a.High, high); a.Sides.AddRange(b.Sides); axes.RemoveAt(j);
        }
        return axes;
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
        foreach (Stroke stroke in strokes) foreach (int index in new int[] { 0, stroke.Points.Count - 1 })
        { int found = before.IndexOf(stroke.Points[index]); if (found >= 0) stroke.Points[index] = after[found]; }
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
                if (!joined) { while (cursor < first) AddDistinct(output, p[cursor++], tolerance); AddDistinct(output, run.Axis.Project(p[first]), tolerance); }
                if (run.JoinNext) { AddDistinct(output, run.Corner, tolerance); corners.Add(output.Count - 1); cursor = runs[i + 1].First; }
                else { AddDistinct(output, run.Axis.Project(p[last]), tolerance); cursor = last + 1; }
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
