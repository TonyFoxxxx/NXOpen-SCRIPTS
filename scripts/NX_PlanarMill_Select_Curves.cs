// NX_PlanarMill_Select_Curves.cs - centerlines from planar engraving faces.
// SCRIPT_VERSION: V1.01
// Select an existing PLANAR_MILL, run, then select the flat bottoms of the letters.
// Reconstructs approximate medial curves; round dots use a circle of half the face diameter.
// Appends local boundaries, Tool Position ON, member stock 0; one undo transaction.
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

public class NXPlanarMillSelectCurves
{
    private const string SCRIPT_VERSION = "V1.01";
    private const string GroupAttribute = "NX_PM_AXIS_GROUP", ShapeAttribute = "NX_PM_AXIS_SHAPE";
    private const string SourceAttribute = "NX_PM_AXIS_FACE", CountAttribute = "NX_PM_AXIS_COUNT";
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
        internal string Shape;
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
            IntPtr[] oldBoundaries = Boundaries(uf, operation.Tag); HashSet<IntPtr> oldHandles = new HashSet<IntPtr>(oldBoundaries);
            mark = session.SetUndoMark(Session.MarkVisibility.Visible, "Оси гравировки: " + operation.Name); hasMark = true;
            List<CurveInfo> curves = new List<CurveInfo>(); List<PlanarCurveChains.Chain> chains = new List<PlanarCurveChains.Chain>(); int dots = 0;
            foreach (FaceGroup group in groups)
            {
                int expected = 0;
                foreach (EngravingCenterlines.Result result in group.Results)
                { expected += result.Circles.Count; foreach (List<EngravingCenterlines.Bezier> stroke in result.Curves) expected += stroke.Count; }
                foreach (EngravingCenterlines.Result result in group.Results)
                {
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
            foreach (PlanarCurveChains.Chain chain in chains)
            {
                Tag[] tags = new Tag[chain.Items.Count]; UFCambnd.AppData[] memberData = new UFCambnd.AppData[tags.Length];
                for (int i = 0; i < tags.Length; i++)
                {
                    tags[i] = curves[chain.Items[i]].Curve.Tag;
                    memberData[i] = new UFCambnd.AppData { has_stock = 1, stock = 0,
                        has_tool_position = 1, tool_position = CamToolPosition.CamToolPositionOn, tolerances = new double[2] };
                }
                UFCambnd.BoundaryData boundary = new UFCambnd.BoundaryData
                {
                    boundary_type = chain.Closed ? CamBoundaryType.CamBoundaryTypeClosed : CamBoundaryType.CamBoundaryTypeOpen,
                    plane_type = 0, origin = new double[] { frame.Origin.X, frame.Origin.Y, frame.Origin.Z },
                    matrix = new double[] { frame.X.X, frame.X.Y, frame.X.Z, frame.Y.X, frame.Y.Y, frame.Y.Z, frame.Z.X, frame.Z.Y, frame.Z.Z },
                    material_side = CamMaterialSide.CamMaterialSideInLeft,
                    app_data = new UFCambnd.AppData[] { new UFCambnd.AppData { has_stock = 1, stock = 0, tolerances = new double[2] } }
                };
                uf.Cambnd.AppendBndFromCurve(operation.Tag, CamGeomType.CamPart, tags.Length, tags, ref boundary, memberData);
            }
            VerifyNewBoundaries(uf, operation, oldHandles, curves, chains);
            PlanarMillingBuilder builder = part.CAMSetup.CAMOperationCollection.CreatePlanarMillingBuilder(operation);
            try { builder.Commit(); } finally { builder.Destroy(); }
            HashSet<Tag> expectedTags = new HashSet<Tag>(assigned); foreach (CurveInfo curve in curves) expectedTags.Add(curve.Curve.Tag);
            if (!AssignedCurves(uf, operation).SetEquals(expectedTags) || Boundaries(uf, operation.Tag).Length != oldBoundaries.Length + chains.Count)
                throw new InvalidOperationException("NX сохранил другой состав границ. Изменение отменяется.");
            committed = true; hasMark = false;
            try { uf.UiOnt.Refresh(); } catch { }
            int closed = chains.FindAll(delegate(PlanarCurveChains.Chain c) { return c.Closed; }).Count;
            Show(NXMessageBox.DialogType.Information, "Операция: " + operation.Name + "\nСоздано кривых: " + curves.Count
                + "\nГраниц: " + chains.Count + " (открытых: " + (chains.Count - closed) + ", замкнутых: " + closed + ")"
                + "\nКруглых точек D/2: " + dots + (skipped > 0 ? "\nРанее обработанных граней пропущено: " + skipped : "")
                + "\nСуществующих границ сохранено: " + oldBoundaries.Length
                + "\n\nНовые кривые созданы в плоскости выбранных граней. Tool Position: ON; Stock: 0."
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
            foreach (EngravingCenterlines.Region region in GroupRegions(pending[i], frame, joinTolerance))
                pending[i].Results.Add(EngravingCenterlines.Build(region, tolerance));
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
            int count = int.Parse(item.GetStringUserAttribute(CountAttribute, -1), CultureInfo.InvariantCulture); Existing entry;
            if (!result.TryGetValue(key, out entry)) { entry = new Existing { Shape = shape, Expected = count }; result.Add(key, entry); }
            if (entry.Shape != shape || entry.Expected != count) throw new InvalidOperationException("Несогласованные метки ранее созданных осей гравировки.");
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
        public int BoundarySamples;
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
        PruneCorners(nodes, step);
        nodes = MergeNearNodes(nodes, step * 2, region, true);
        nodes = MergeNearNodes(nodes, tolerance * 2, region);
        CheckTopology(nodes, region.Loops.Count - 1);
        List<Stroke> strokes = Trace(nodes);
        if (strokes.Count == 0)
        {
            throw new InvalidOperationException("Не удалось получить устойчивую среднюю линию этой грани.");
        }
        foreach (Stroke stroke in strokes)
        {
            Refine(stroke, region, step, tolerance);
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
    private static void PruneCorners(List<Node> nodes, double step)
    {
        // Remove only short terminal branches whose radius collapses towards a convex outline corner.
        // Genuine short strokes with a full-width rounded end survive this condition.
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
                if (nodes[current].Links.Count >= 3 && length < nodes[current].Radius * 1.85 + step
                    && nodes[i].Radius < nodes[current].Radius * 0.35) remove.AddRange(branch);
            }
            if (remove.Count == 0) break;
            foreach (Pair p in remove) { nodes[p.A].Links.Remove(p.B); nodes[p.B].Links.Remove(p.A); }
        }
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
    public static List<Bezier> Fit(Stroke stroke, Region region, double tolerance)
    {
        List<Bezier> result = new List<Bezier>(); List<P> p = stroke.Points;
        if (p.Count < 2) return result;
        if (stroke.Closed && p.Count >= 5)
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
