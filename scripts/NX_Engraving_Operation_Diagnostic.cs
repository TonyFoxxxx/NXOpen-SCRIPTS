// NX_Engraving_Operation_Diagnostic.cs - read the selected planar engraving operation.
// SCRIPT_VERSION: V1
// Select one configured PLANAR_MILL / PLANAR_PROFILE in the Operation Navigator.
// Reports available UF parameters, inheritance, builder settings, groups, geometry and UDEs.
// No Commit, regeneration or part save. One JSON file only on Save JSON; no automatic output.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using NXOpen.Utilities;
using CamOperation = NXOpen.CAM.Operation;

public class NXEngravingOperationDiagnostic
{
    private const string SCRIPT_VERSION = "V1";
    private static readonly string Title = "Диагностика операции гравировки — " + SCRIPT_VERSION + " — "
        + Encoding.UTF8.GetString(Convert.FromBase64String("YnkgQFRvbnlfRm94eHggKFRHKQ=="));

    public static void Main(string[] args)
    {
        UFSession uf = UFSession.GetUFSession();
        EngravingReport report = new EngravingReport(Title);
        string stage = "Выбор операции";
        try
        {
            Session session = Session.GetSession();
            Part part = session.Parts.Work;
            bool initialized;
            uf.Cam.IsSessionInitialized(out initialized);
            if (part == null || !initialized)
                throw new InvalidOperationException("Откройте CAM-проект в Manufacturing.");
            int count; Tag[] selected;
            uf.UiOnt.AskSelectedNodes(out count, out selected);
            EngravingParamReader.Count(count, selected);
            HashSet<Tag> seen = new HashSet<Tag>();
            List<CamOperation> operations = new List<CamOperation>();
            foreach (Tag tag in selected ?? new Tag[0])
            {
                CamOperation candidate = NXObjectManager.Get(tag) as CamOperation;
                if (candidate != null && seen.Add(tag)) operations.Add(candidate);
            }
            if (operations.Count != 1 || !(operations[0] is PlanarMilling))
                throw new InvalidOperationException("Закройте окно редактирования операции. Выделите одну настроенную PLANAR_MILL или PLANAR_PROFILE в Operation Navigator и повторите запуск.");
            PlanarMilling operation = (PlanarMilling)operations[0];
            if (operation.IsOccurrence || operation.OwningPart == null || operation.OwningPart.Tag != part.Tag)
                throw new InvalidOperationException("Операция должна принадлежать рабочей CAM-детали.");

            EngravingSnapshot snapshot = new EngravingSnapshot(uf, report);
            stage = "Исходное состояние";
            bool modified = part.IsModified;
            string before = null;
            report.Try("Исходный контрольный снимок", delegate { before = snapshot.Identity(part, operation); });
            report.Line("Деталь: " + part.Name + "; единицы: " + part.PartUnits);
            report.Line("Операция: " + snapshot.Reference(operation));
            report.Record("context", "part", part.Name, "part_units", part.PartUnits.ToString(),
                "operation_name", operation.Name, "operation_tag", (uint)operation.Tag,
                "operation_class", operation.GetType().FullName, "nxopen_version", typeof(Session).Assembly.GetName().Version.ToString(),
                "runtime", Environment.Version.ToString());
            report.Line("NXOpen: " + typeof(Session).Assembly.GetName().Version + "; CLR: " + Environment.Version);
            report.Line("Дата: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
            report.Line("Числовые длины UF указаны в единицах детали; углы и подачи — в единицах соответствующего параметра.");
            report.Try("Тип операции", delegate { snapshot.Tree.Read("Operation.CamType", operation.GetNameOfType()); });
            report.Try("Режим профиля", delegate { snapshot.Tree.Read("Operation.ProfileType", operation.ProfileType); });
            report.Try("Template Type (105)", delegate { string s; uf.Param.AskStrValue(operation.Tag, 105, out s); snapshot.Tree.Read("Template.Type", s); });
            report.Try("Template Subtype (106)", delegate { string s; uf.Param.AskStrValue(operation.Tag, 106, out s); snapshot.Tree.Read("Template.Subtype", s); });

            stage = "Группы, инструмент и система координат";
            List<NCGroup> groups = snapshot.Parents(operation);
            stage = "Параметры операции";
            snapshot.Parameters(operation, true);
            stage = "Параметры родительских групп";
            foreach (NCGroup group in groups) snapshot.Parameters(group, false);
            stage = "Сохранённая геометрия";
            snapshot.Geometry(operation);
            stage = "События начала и конца";
            snapshot.Events(operation.Tag);
            stage = "Настройки редактора операции";
            report.Section("NXOpen: параметры редактора PLANAR_MILL");
            report.Try(stage, delegate { EngravingTree.WithBuilder(
                delegate { return part.CAMSetup.CAMOperationCollection.CreatePlanarMillingBuilder(operation); },
                delegate(object b) { snapshot.Tree.Read("Operation", b); },
                delegate(object b) { ((PlanarMillingBuilder)b).Destroy(); }); });

            stage = "Контроль после чтения";
            string after = null;
            report.Try("Итоговый контрольный снимок", delegate { after = snapshot.Identity(part, operation); });
            report.Section("Контроль после чтения");
            report.Line("IsModified до: " + modified + "; после: " + part.IsModified);
            if (before == null || after == null)
                report.Gap("Контроль состояния", "Один из контрольных снимков недоступен. Неизменность состава не проверена.");
            else if (before != after || modified != part.IsModified)
                report.Gap("Контроль состояния", "Изменился состав объектов/родителей/границ или флаг IsModified. Полная неизменность не подтверждена; автоматического отката нет.");
            else report.Line("Совпали: набор операций, родители выбранной операции, состав и считанные данные Part Boundaries, флаг IsModified.");
            report.Line("Commit, генерация траектории и сохранение проекта не вызывались. Полное побайтовое сравнение PRT не выполнялось.");
        }
        catch (Exception error) { report.Error(stage, error); }
        report.Finish();
        try { EngravingReportWindow.Show(uf.Ui.GetDefaultParent(), Title, report); }
        catch (Exception error)
        {
            report.Error("Окно отчёта", error);
            ListingWindow listing = Session.GetSession().ListingWindow;
            listing.SelectDevice(ListingWindow.DeviceType.Window, "");
            listing.Open(); listing.WriteLine(report.ToString());
            UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Error,
                "Окно копирования недоступно. Отчёт показан в Information: выделите текст и скопируйте его.\n" + error.Message);
        }
    }

    public static int GetUnloadOption(string dummy) { return (int)Session.LibraryUnloadOption.Immediately; }
}

public sealed class EngravingSnapshot
{
    private readonly UFSession uf;
    private readonly EngravingReport report;
    public readonly EngravingTree Tree;
    private readonly Dictionary<Tag, string> references = new Dictionary<Tag, string>();
    private readonly Dictionary<int, UFParam.IndexAttribute> attributes = new Dictionary<int, UFParam.IndexAttribute>();

    public EngravingSnapshot(UFSession uf, EngravingReport report)
    {
        this.uf = uf; this.report = report;
        Tree = new EngravingTree(report, Reference);
    }

    public string Reference(object value)
    {
        if (value is Tag) return TagReference((Tag)value);
        NXObject obj = value as NXObject;
        if (obj == null) return null;
        string label = obj.GetType().FullName + " Tag=" + obj.Tag + " Name=" + EngravingTree.Text(obj.Name);
        Expression expression = obj as Expression;
        if (expression != null)
            return label + " Formula=" + EngravingTree.Text(expression.RightHandSide) + " Type=" + expression.Type
                + " Units=" + (expression.Units == null ? "none" : expression.Units.TypeName);
        CoordinateSystem csys = obj as CoordinateSystem;
        if (csys != null)
        {
            double[] origin = new double[3], matrix = new double[9]; Tag matrixTag;
            uf.Csys.AskCsysInfo(csys.Tag, out matrixTag, origin); uf.Csys.AskMatrixValues(matrixTag, matrix);
            return label + " Origin=" + Tree.Value(origin) + " Axes=" + Tree.Value(matrix);
        }
        Point point = obj as Point;
        if (point != null) return label + " Coordinates=" + Tree.Value(point.Coordinates);
        Plane plane = obj as Plane;
        if (plane != null) return label + " Origin=" + Tree.Value(plane.Origin) + " Normal=" + Tree.Value(plane.Normal);
        ScCollector collector = obj as ScCollector;
        if (collector != null) return label + " Objects=" + Tree.Value(collector.GetObjects());
        return label;
    }

    private string TagReference(Tag tag)
    {
        if (tag == Tag.Null) return "Tag.Null";
        string label;
        if (references.TryGetValue(tag, out label)) return label;
        references[tag] = "Tag=" + tag; // Prevent a collector from following a cyclic reference.
        try { label = Reference(NXObjectManager.Get(tag)); }
        catch (Exception error) { report.Error("Ссылка Tag=" + tag, error); label = "Tag=" + tag + " [UNAVAILABLE]"; }
        references[tag] = label ?? ("Tag=" + tag);
        return references[tag];
    }

    public List<NCGroup> Parents(CamOperation operation)
    {
        report.Section("Родители по четырём представлениям навигатора");
        List<NCGroup> groups = new List<NCGroup>(); HashSet<Tag> all = new HashSet<Tag>();
        foreach (CAMSetup.View view in new CAMSetup.View[] { CAMSetup.View.ProgramOrder, CAMSetup.View.MachineTool, CAMSetup.View.Geometry, CAMSetup.View.MachineMethod })
        {
            CAMSetup.View current = view;
            report.Try("Родители " + view, delegate {
                HashSet<Tag> seen = new HashSet<Tag>();
                NCGroup parent = operation.GetParent(current);
                report.Line(current + ":");
                if (parent == null) report.Line("  null");
                while (parent != null)
                {
                    if (!seen.Add(parent.Tag) || seen.Count > 128) throw new InvalidOperationException("Цикл или слишком большая глубина родительских групп.");
                    report.Line("  " + Reference(parent));
                    report.Record("parent", "view", current.ToString(), "order", seen.Count, "tag", (uint)parent.Tag,
                        "name", parent.Name, "class", parent.GetType().FullName);
                    if (all.Add(parent.Tag)) groups.Add(parent);
                    parent = parent.GetParent();
                }
            });
        }
        return groups;
    }

    public void Parameters(CAMObject obj, bool includeCatalog)
    {
        report.Section("UF PARAM: " + Reference(obj));
        SortedSet<int> required = new SortedSet<int>(), candidates = new SortedSet<int>();
        report.Try("Список required " + obj.Tag, delegate {
            int count; int[] ids; uf.Param.AskRequiredParams(obj.Tag, out count, out ids);
            EngravingParamReader.Count(count, ids);
            if (ids != null) foreach (int id in ids) { required.Add(id); candidates.Add(id); }
        });
        report.Try("Список inherited " + obj.Tag, delegate {
            // uf_param.h fixes this ABI buffer to UF_PARAM_REQUIRED_LIST_SIZE = 1200.
            int count; int[] ids = new int[1200]; uf.Param.AskInheritedParams(obj.Tag, out count, ids);
            if (count < 0 || count > ids.Length) throw new InvalidOperationException("Некорректный размер inherited params.");
            for (int i = 0; i < count; i++) { required.Add(ids[i]); candidates.Add(ids[i]); }
        });
        if (includeCatalog)
        {
            // Discover optional public indices from this installed SDK, without scanning arbitrary numbers.
            foreach (FieldInfo field in typeof(UFConstants).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.IsLiteral && field.FieldType == typeof(int) && field.Name.StartsWith("UF_PARAM_", StringComparison.Ordinal))
                    candidates.Add((int)field.GetRawConstantValue());
            candidates.Add(105); candidates.Add(106);
        }
        int valid = 0, invalid = 0, unregistered = 0;
        foreach (int id in candidates)
        {
            UFParam.IndexAttribute attr;
            try
            {
                if (!attributes.TryGetValue(id, out attr)) { uf.Param.AskParamAttributes(id, out attr); attributes[id] = attr; }
            }
            catch (Exception error)
            {
                unregistered++;
                if (required.Contains(id)) report.Error("Атрибуты UF " + obj.Tag + "/" + id, error);
                continue;
            }
            int index = id; UFParam.IndexAttribute data = attr;
            report.Try("UF " + obj.Tag + "/" + id + " " + attr.name, delegate {
                UFParam.Status status; uf.Param.AskParamStatus(obj.Tag, index, out status);
                if (status == UFParam.Status.InvalidIndex)
                {
                    invalid++;
                    if (required.Contains(index)) report.Gap("UF " + index, "Required/inherited parameter returned InvalidIndex.");
                    return;
                }
                valid++;
                string prefix = "[" + index + "] " + data.name + " | " + data.type + " | " + status;
                report.Line(prefix);
                Dictionary<string, object> record = report.Record("parameter", "owner_tag", (uint)obj.Tag,
                    "owner_name", obj.Name, "index", index, "name", data.name, "data_type", data.type.ToString(),
                    "data_type_id", (int)data.type, "status", status.ToString(), "value_available", false, "source_available", false);
                report.Try(prefix + " / source", delegate {
                    Tag definer; uf.Param.AskParamDefiner(obj.Tag, index, out definer);
                    report.Line("  source = " + TagReference(definer));
                    record["source_tag"] = (uint)definer; record["source"] = TagReference(definer); record["source_available"] = true;
                });
                try
                {
                    object value = EngravingParamReader.Read(uf.Param, obj.Tag, index, data.type);
                    report.Line("  value = " + Tree.Value(value)); record["value"] = Tree.Encode(value); record["value_available"] = true;
                }
                catch (NotSupportedException error) { report.Gap(prefix, error.Message); }
            });
        }
        report.Line("Параметры: доступны по статусу " + valid + "; неприменимы " + invalid + "; незарегистрированные индексы каталога " + unregistered + ".");
    }

    public void Geometry(CamOperation operation)
    {
        report.Section("Геометрия, источники и сохранённые границы UF");
        foreach (CamGeomType kind in Enum.GetValues(typeof(CamGeomType)))
        {
            CamGeomType current = kind;
            report.Try("Геометрия " + kind, delegate {
                Tag provider; uf.Camgeom.AskGeomProvider(operation.Tag, current, out provider);
                report.Line(current + " provider = " + TagReference(provider));
                report.Record("geometry_provider", "geometry_type", current.ToString(), "owner_tag", (uint)operation.Tag, "provider_tag", (uint)provider);
                if (provider == Tag.Null) { report.Line("  геометрия не назначена"); return; }
                int count; IntPtr[] items; uf.Camgeom.AskItems(provider, current, out count, out items);
                EngravingParamReader.Count(count, items); report.Line("  items = " + count);
                if (items != null) for (int i = 0; i < items.Length; i++)
                {
                    IntPtr item = items[i]; string path = current + ".Item[" + i + "]";
                    report.Try(path, delegate {
                        Tag tag; uf.Camgeom.AskItemEntity(item, out tag); report.Line(path + " = " + TagReference(tag));
                        report.Record("geometry_item", "path", path, "owner_tag", (uint)provider, "tag", (uint)tag, "reference", TagReference(tag));
                        UFCamgeom.AppData app; uf.Camgeom.AskItemAppData(item, out app); Tree.Read(path + ".Settings", app);
                    });
                }
            });
        }
        foreach (CamGeomType kind in new CamGeomType[] { CamGeomType.CamPart, CamGeomType.CamBlank, CamGeomType.CamCheck, CamGeomType.CamTrim })
        {
            CamGeomType current = kind;
            report.Try("Boundaries " + kind, delegate {
                Tag provider; uf.Camgeom.AskGeomProvider(operation.Tag, current, out provider);
                DumpBoundaries(operation.Tag, current, Tree, report);
                if (provider != Tag.Null && provider != operation.Tag) DumpBoundaries(provider, current, Tree, report);
            });
        }
    }

    private void DumpBoundaries(Tag owner, CamGeomType kind, EngravingTree tree, EngravingReport output)
    {
        int count; IntPtr[] boundaries; uf.Cambnd.AskBoundaries(owner, kind, out count, out boundaries);
        EngravingParamReader.Count(count, boundaries);
        output.Line("Boundaries " + kind + " owner=" + owner + " count=" + count);
        if (boundaries == null) return;
        for (int i = 0; i < boundaries.Length; i++)
        {
            string path = "Boundary.Owner[" + owner + "]." + kind + "[" + i + "]";
            UFCambnd.BoundaryData definition; UFCambnd.AppData data;
            uf.Cambnd.AskBoundaryData(boundaries[i], out definition); uf.Cambnd.AskBoundaryAppData(boundaries[i], out data);
            tree.Read(path + ".Definition", definition); tree.Read(path + ".Settings", data);
            int memberCount; IntPtr[] members; uf.Cambnd.AskBoundaryItems(boundaries[i], out memberCount, out members);
            EngravingParamReader.Count(memberCount, members); output.Line(path + ".Members = " + memberCount);
            if (members == null) continue;
            for (int j = 0; j < members.Length; j++)
            {
                Tag entity; uf.Cambnd.AskItemEntity(members[j], out entity); uf.Cambnd.AskItemAppData(members[j], out data);
                output.Line(path + ".Member[" + j + "] = Tag " + entity);
                output.Record("boundary_member", "path", path, "member", j, "tag", (uint)entity);
                tree.Read(path + ".Member[" + j + "].Settings", data);
            }
        }
    }

    public void Events(Tag owner)
    {
        report.Section("UDE начала и конца операции");
        foreach (UdeSetType kind in new UdeSetType[] { UdeSetType.UdeStartSet, UdeSetType.UdeEndSet })
        {
            UdeSetType current = kind;
            report.Try(kind.ToString(), delegate {
                int count; IntPtr[] events; uf.Param.AskUdes(owner, current, out count, out events);
                EngravingParamReader.Count(count, events); report.Line(current + " count=" + count);
                if (events == null) return;
                int eventIndex = -1;
                foreach (IntPtr item in events)
                {
                    eventIndex++;
                    string name; uf.Ude.AskName(item, out name); report.Line("UDE " + EngravingTree.Text(name));
                    int n; string[] names; uf.Ude.AskParams(item, out n, out names); EngravingParamReader.Count(n, names);
                    if (names == null) continue;
                    foreach (string key in names)
                    {
                        string parameter = key; IntPtr ev = item;
                        report.Try("UDE " + name + "/" + key, delegate {
                            UFUde.ParamType type; UFUde.ParamToggle toggle; bool optional = false;
                            uf.Ude.AskParamType(ev, parameter, out type); uf.Ude.IsParamOptional(ev, parameter, ref optional);
                            string state = "required";
                            if (optional) { uf.Ude.AskParamToggle(ev, parameter, out toggle); state = toggle.ToString(); }
                            object value;
                            switch ((int)type)
                            {
                                case 0: int i; uf.Ude.AskInteger(ev, parameter, out i); value = i; break;
                                case 1: double d; uf.Ude.AskDouble(ev, parameter, out d); value = d; break;
                                case 2: case 4: string s; uf.Ude.AskString(ev, parameter, out s); value = s; break;
                                case 3: bool b; uf.Ude.AskBoolean(ev, parameter, out b); value = b; break;
                                case 5: Tag p; uf.Ude.AskPoint(ev, parameter, out p); value = p; break;
                                case 6: Tag v; uf.Ude.AskVector(ev, parameter, out v); value = v; break;
                                default: throw new NotSupportedException("Unsupported UDE type: " + type);
                            }
                            report.Line("  " + key + " | " + type + " | " + state + " = " + Tree.Value(value));
                            report.Record("ude_parameter", "owner_tag", (uint)owner, "set", current.ToString(), "event_index", eventIndex, "ude", name,
                                "parameter", key, "data_type", type.ToString(), "state", state, "value", Tree.Encode(value));
                        });
                    }
                }
            });
        }
    }

    public string Identity(Part part, CamOperation operation)
    {
        List<string> tags = new List<string>();
        foreach (CamOperation op in part.CAMSetup.CAMOperationCollection) tags.Add(op.Tag.ToString());
        tags.Sort(StringComparer.Ordinal);
        EngravingReport check = new EngravingReport("state");
        check.Line(String.Join(",", tags.ToArray()));
        foreach (CAMSetup.View view in new CAMSetup.View[] { CAMSetup.View.ProgramOrder, CAMSetup.View.Geometry, CAMSetup.View.MachineTool, CAMSetup.View.MachineMethod })
        {
            NCGroup parent = operation.GetParent(view); check.Line(view + "=" + (parent == null ? Tag.Null : parent.Tag));
        }
        EngravingTree tree = new EngravingTree(check, delegate(object o) { return o is Tag ? o.ToString() : null; });
        DumpBoundaries(operation.Tag, CamGeomType.CamPart, tree, check);
        if (check.Incomplete != 0) throw new InvalidOperationException("Не удалось прочитать исходный состав границ для проверки.");
        return check.ToString();
    }
}

// BEGIN ENGRAVING DIAGNOSTIC PARAM READER
public static class EngravingParamReader
{
    public static void Count(int count, Array values)
    {
        if (count < 0 || (values == null ? count != 0 : values.Length != count))
            throw new InvalidOperationException("API array count does not match the returned data: " + count + "/" + (values == null ? "null" : values.Length.ToString()));
    }
    public static object Read(UFParam api, Tag obj, int index, UFParam.Type type)
    {
        switch (type)
        {
            case UFParam.Type.TypeLogical: bool b; api.AskLogicalValue(obj, index, out b); return b;
            case UFParam.Type.TypeInt: int i; api.AskIntValue(obj, index, out i); return i;
            case UFParam.Type.TypeDouble: case UFParam.Type.TypeDoubleLength: double d; api.AskDoubleValue(obj, index, out d); return d;
            case UFParam.Type.TypeTag: Tag tag; api.AskTagValue(obj, index, out tag); return tag;
            case UFParam.Type.TypeString: string s; api.AskStrValue(obj, index, out s); return s;
            case UFParam.Type.Type2d: case UFParam.Type.Type2dLength: double[] p2 = new double[2]; api.Ask2dValue(obj, index, p2); return p2;
            case UFParam.Type.Type3d: case UFParam.Type.Type3dLength: double[] p3 = new double[3]; api.Ask3dValue(obj, index, p3); return p3;
            case UFParam.Type.TypeVlaReal: case UFParam.Type.TypeVlaLength:
                int nd; double[] da; api.AskDoubleVla(obj, index, out nd, out da); Count(nd, da); return da ?? new double[0];
            case UFParam.Type.TypeVlaInt:
                int ni; int[] ia; api.AskIntVla(obj, index, out ni, out ia); Count(ni, ia); return ia ?? new int[0];
            case UFParam.Type.TypeVlaTag:
                int nt; Tag[] ta; api.AskTagVla(obj, index, out nt, out ta); Count(nt, ta); return ta ?? new Tag[0];
            default: throw new NotSupportedException("[UNSUPPORTED] " + type + ": no typed UF reader; internal pointers are not dereferenced. See the NXOpen section where available.");
        }
    }
}
// END ENGRAVING DIAGNOSTIC PARAM READER

// BEGIN ENGRAVING DIAGNOSTIC TREE
public sealed class EngravingReport
{
    private readonly StringBuilder text = new StringBuilder();
    private readonly string title;
    private readonly List<object> records = new List<object>();
    private readonly List<object> problems = new List<object>();
    public int Incomplete { get; private set; }
    public EngravingReport(string title) { this.title = title; Line(title); }
    public void Line(string value) { text.Append(value).Append("\r\n"); }
    public void Section(string title) { Line(""); Line("=== " + title + " ==="); }
    public Dictionary<string, object> Record(string kind, params object[] pairs)
    {
        Dictionary<string, object> record = new Dictionary<string, object>(); record["kind"] = kind;
        for (int i = 0; i < pairs.Length; i += 2) record[(string)pairs[i]] = pairs[i + 1];
        records.Add(record); return record;
    }
    public void Gap(string path, string detail)
    {
        Incomplete++; Line("[UNAVAILABLE] " + path + ": " + detail);
        problems.Add(new Dictionary<string, object> { { "path", path }, { "detail", detail } });
    }
    public void Error(string path, Exception error)
    {
        while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
        Gap(path, error.GetType().FullName + ": " + error.Message);
        PropertyInfo code = error.GetType().GetProperty("ErrorCode");
        if (code != null) { try { Line("NX/API code: " + code.GetValue(error, null)); } catch { Line("NX/API code: unavailable"); } }
        Line(error.ToString());
        Record("error", "path", path, "exception_type", error.GetType().FullName, "message", error.Message, "stack", error.ToString());
    }
    public void Try(string path, Action action) { try { action(); } catch (Exception e) { Error(path, e); } }
    public void Finish()
    {
        Section("Итог");
        Line(Incomplete == 0 ? "Доступные разделы считаны без ошибок." : "Отчёт частичный. Недоступных чтений / неподдерживаемых разделов: " + Incomplete + ". Их места обозначены UNAVAILABLE / UNSUPPORTED.");
        Line("Это отчёт для разработки, а не файл шаблона. Внутренние данные, не раскрытые API, не выгружаются.");
        Line("Нажмите «Сохранить JSON» и передайте файл в чат, либо используйте «Копировать всё». Автоматической отправки нет.");
    }
    public override string ToString() { return text.ToString(); }
    public string ToJson()
    {
        return EngravingJson.Write(new Dictionary<string, object> {
            { "schema", "nx-engraving-operation-diagnostic" }, { "schema_version", 1 }, { "title", title },
            { "created_utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
            { "partial", Incomplete != 0 }, { "unavailable_count", Incomplete }, { "records", records },
            { "problems", problems }, { "report", ToString() }
        });
    }
}

public sealed class EngravingTree
{
    private readonly EngravingReport report;
    private readonly Func<object, string> reference;
    private readonly Dictionary<object, string> visited = new Dictionary<object, string>(new IdentityComparer());
    private int nodes;
    public EngravingTree(EngravingReport report, Func<object, string> reference) { this.report = report; this.reference = reference; }
    public static string Text(string value) { return value == null ? "null" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\""; }
    public object Encode(object value)
    {
        if (value == null) return null;
        string r = reference(value);
        if (r != null)
        {
            Dictionary<string, object> item = new Dictionary<string, object> { { "reference", r }, { "type", value.GetType().FullName } };
            if (value.GetType().IsEnum) item["id"] = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
            return item;
        }
        Type type = value.GetType();
        if (type.IsEnum) return new Dictionary<string, object> { { "type", type.FullName }, { "name", value.ToString() }, { "value", Convert.ToInt64(value, CultureInfo.InvariantCulture) } };
        if (type.IsPrimitive || value is string || value is decimal) return value;
        Array array = value as Array;
        if (array != null) { List<object> items = new List<object>(); foreach (object item in array) items.Add(Encode(item)); return items; }
        Dictionary<string, object> fields = new Dictionary<string, object> { { "type", type.FullName } };
        if (type.IsValueType) foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) fields[f.Name] = Encode(f.GetValue(value));
        return fields;
    }
    public string Value(object value)
    {
        if (value == null) return "null";
        string r = reference(value); if (r != null) return r;
        Type type = value.GetType();
        if (value is string) return Text((string)value);
        if (type.IsEnum) return value + " (" + Convert.ToInt64(value, CultureInfo.InvariantCulture) + ")";
        if (value is double) return ((double)value).ToString("R", CultureInfo.InvariantCulture);
        if (value is float) return ((float)value).ToString("R", CultureInfo.InvariantCulture);
        if (type.IsPrimitive || value is decimal) return Convert.ToString(value, CultureInfo.InvariantCulture);
        Array array = value as Array;
        if (array != null)
        {
            StringBuilder b = new StringBuilder("[");
            for (int i = 0; i < array.Length; i++) { if (i != 0) b.Append(", "); b.Append(Value(array.GetValue(i))); }
            return b.Append(']').ToString();
        }
        if (type.IsValueType)
        {
            List<string> fields = new List<string>();
            foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) fields.Add(f.Name + "=" + Value(f.GetValue(value)));
            return "{" + String.Join(", ", fields.ToArray()) + "}";
        }
        return "<" + type.FullName + ">";
    }
    public void Read(string path, object value) { Walk(path, value, 0); }
    private void Walk(string path, object value, int depth)
    {
        if (value == null) { report.Line(path + " = null"); report.Record("setting", "path", path, "value", null); return; }
        Type type = value.GetType();
        string r = reference(value);
        if (r != null || type.IsPrimitive || type.IsEnum || value is string || value is decimal || type.IsValueType)
        { report.Line(path + " = " + (r ?? Value(value))); report.Record("setting", "path", path, "value", Encode(value)); return; }
        if (++nodes > 20000 || depth > 16) { report.Gap(path, "Traversal limit reached; remaining data is not treated as empty."); return; }
        string earlier;
        if (visited.TryGetValue(value, out earlier)) { report.Line(path + " -> " + earlier); report.Record("alias", "path", path, "target", earlier); return; }
        visited[value] = path;
        Array array = value as Array;
        if (array != null)
        {
            report.Line(path + ".Count = " + array.Length);
            report.Record("list", "path", path, "count", array.Length);
            for (int i = 0; i < array.Length; i++) { int index = i; report.Try(path + "[" + i + "]", delegate { Walk(path + "[" + index + "]", array.GetValue(index), depth + 1); }); }
            return;
        }
        if (type.Namespace != "NXOpen.CAM") { report.Gap(path, "Unexpanded type " + type.FullName); return; }
        report.Line(path + " : " + type.FullName);
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Array.Sort(properties, delegate(PropertyInfo a, PropertyInfo b) { return StringComparer.Ordinal.Compare(a.Name, b.Name); });
        foreach (PropertyInfo property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0 || property.GetGetMethod() == null) continue;
            string child = path + "." + property.Name;
            if (property.Name == "Tag") continue;
            if (property.Name == "PreviewBuilder" || property.Name == "ToolPathEditor")
            { report.Line(child + " = [EXCLUDED: preview / path editor]"); continue; }
            PropertyInfo item = property;
            report.Try(child, delegate { Walk(child, item.GetValue(value, null), depth + 1); });
        }
        // Only this documented list accessor is invoked by reflection. Never enumerate arbitrary Get/Ask methods.
        if (type.Name.EndsWith("List", StringComparison.Ordinal))
        {
            MethodInfo contents = type.GetMethod("GetContents", Type.EmptyTypes);
            if (contents != null && contents.ReturnType.IsArray)
                report.Try(path + ".GetContents", delegate { Walk(path + ".Items", contents.Invoke(value, null), depth + 1); });
        }
    }
    public static void WithBuilder(Func<object> create, Action<object> read, Action<object> destroy)
    {
        object builder = create();
        if (builder == null) throw new InvalidOperationException("Builder is null.");
        Exception failure = null;
        try { read(builder); }
        catch (Exception error) { failure = error; }
        finally
        {
            try { destroy(builder); }
            catch (Exception cleanup) { if (failure != null) throw new AggregateException("Read and Destroy both failed.", failure, cleanup); throw; }
        }
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private sealed class IdentityComparer : IEqualityComparer<object>
    {
        public new bool Equals(object a, object b) { return Object.ReferenceEquals(a, b); }
        public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
    }
}
// END ENGRAVING DIAGNOSTIC TREE

// BEGIN ENGRAVING DIAGNOSTIC JSON
public static class EngravingJson
{
    public static string Write(object value) { StringBuilder b = new StringBuilder(); WriteValue(b, value, 0); return b.Append('\n').ToString(); }
    private static void Quoted(StringBuilder b, string value)
    {
        b.Append('"');
        foreach (char c in value)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < 32 || Char.IsSurrogate(c)) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else b.Append(c);
        }
        b.Append('"');
    }
    private static void WriteValue(StringBuilder b, object value, int depth)
    {
        if (depth > 64) throw new InvalidOperationException("JSON nesting limit exceeded.");
        if (value == null) { b.Append("null"); return; }
        if (value is string || value is char) { Quoted(b, value.ToString()); return; }
        if (value is bool) { b.Append((bool)value ? "true" : "false"); return; }
        if (value is double && (Double.IsNaN((double)value) || Double.IsInfinity((double)value)))
        { WriteValue(b, new Dictionary<string, object> { { "nonfinite", ((double)value).ToString(CultureInfo.InvariantCulture) } }, depth + 1); return; }
        if (value is float && (Single.IsNaN((float)value) || Single.IsInfinity((float)value)))
        { WriteValue(b, new Dictionary<string, object> { { "nonfinite", ((float)value).ToString(CultureInfo.InvariantCulture) } }, depth + 1); return; }
        System.Collections.IDictionary dict = value as System.Collections.IDictionary;
        if (dict != null)
        {
            b.Append('{'); int index = 0;
            foreach (System.Collections.DictionaryEntry entry in dict)
            {
                if (index++ > 0) b.Append(','); b.Append('\n').Append(' ', (depth + 1) * 2);
                Quoted(b, (string)entry.Key); b.Append(": "); WriteValue(b, entry.Value, depth + 1);
            }
            if (index > 0) b.Append('\n').Append(' ', depth * 2); b.Append('}'); return;
        }
        System.Collections.IEnumerable list = value as System.Collections.IEnumerable;
        if (list != null)
        {
            b.Append('['); bool first = true;
            foreach (object item in list) { if (!first) b.Append(','); b.Append('\n').Append(' ', (depth + 1) * 2); WriteValue(b, item, depth + 1); first = false; }
            if (!first) b.Append('\n').Append(' ', depth * 2); b.Append(']'); return;
        }
        if (value is double) { b.Append(((double)value).ToString("R", CultureInfo.InvariantCulture)); return; }
        if (value is float) { b.Append(((float)value).ToString("R", CultureInfo.InvariantCulture)); return; }
        if (value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong || value is decimal)
        { b.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
        throw new NotSupportedException("Unsupported JSON value: " + value.GetType().FullName);
    }
}
// END ENGRAVING DIAGNOSTIC JSON

public static class EngravingReportWindow
{
    public static void Show(IntPtr owner, string title, EngravingReport report) { new Dialog(title, report).Show(owner); }
    private sealed class Dialog
    {
        private readonly string title, text, json;
        private IntPtr window, font;
        private Native.DialogProc procedure;
        private Exception failure;
        private bool ready;
        internal Dialog(string title, EngravingReport report) { this.title = title; text = report.ToString(); json = report.ToJson(); }
        internal void Show(IntPtr owner)
        {
            byte[] data;
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode))
            {
                writer.Write(0x82cd08c0u); writer.Write(0x02000000u); writer.Write((ushort)0);
                writer.Write((short)0); writer.Write((short)0); writer.Write((short)500); writer.Write((short)300);
                writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(Encoding.Unicode.GetBytes(title + "\0"));
                writer.Write((ushort)10); writer.Write(Encoding.Unicode.GetBytes("Segoe UI\0")); data = stream.ToArray();
            }
            IntPtr memory = Marshal.AllocHGlobal(data.Length); procedure = Handle;
            try
            {
                Marshal.Copy(data, 0, memory, data.Length);
                IntPtr result = Native.DialogBoxIndirectParamW(Native.GetModuleHandleW(null), memory, owner, procedure, IntPtr.Zero);
                if (failure != null) throw new InvalidOperationException("Ошибка окна диагностики.", failure);
                if (result.ToInt64() == -1) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { GC.KeepAlive(procedure); Marshal.FreeHGlobal(memory); if (font != IntPtr.Zero) Native.DeleteObject(font); }
        }
        private IntPtr Handle(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (message == 0x110)
                {
                    window = hwnd;
                    IntPtr normal = Native.SendMessageW(window, 0x31, IntPtr.Zero, IntPtr.Zero);
                    Add("STATIC", 100, "Сохраните JSON и прикрепите файл к сообщению в чате.", 0x80u, normal);
                    uint dpi = Native.GetDpiForWindow(window);
                    font = Native.CreateFontW(-(int)Math.Round(10 * (dpi > 0 ? dpi : 96) / 72.0), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Consolas");
                    Add("EDIT", 101, "", 0x00b108c4u, font == IntPtr.Zero ? normal : font);
                    Native.SendMessageW(Item(101), 0xc5, new IntPtr(Int32.MaxValue - 2), IntPtr.Zero);
                    if (!Native.SetWindowTextW(Item(101), text)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    Add("BUTTON", 102, "Сохранить JSON", 0x10000u, normal);
                    Add("BUTTON", 103, "Копировать всё", 0x10000u, normal);
                    Add("BUTTON", 1, "Закрыть", 0x10001u, normal);
                    ready = true; Layout(); Native.SetFocus(Item(102)); return IntPtr.Zero;
                }
                if (message == 0x05 && ready) Layout();
                if (message == 0x24 && ready)
                {
                    Native.Rect rect = new Native.Rect { Right = 340, Bottom = 170 };
                    Native.MapDialogRect(window, ref rect);
                    Native.MinMaxInfo limits = (Native.MinMaxInfo)Marshal.PtrToStructure(lParam, typeof(Native.MinMaxInfo));
                    limits.MinTrack.X = rect.Right; limits.MinTrack.Y = rect.Bottom;
                    Marshal.StructureToPtr(limits, lParam, false); return new IntPtr(1);
                }
                if (message == 0x10) { Native.EndDialog(window, IntPtr.Zero); return new IntPtr(1); }
                if (message == 0x111 && ((wParam.ToInt64() >> 16) & 0xffff) == 0)
                {
                    int id = (int)(wParam.ToInt64() & 0xffff);
                    if (id == 1 || id == 2) Native.EndDialog(window, IntPtr.Zero);
                    if (id == 103) Native.SetWindowTextW(Item(100), Copy(window, text) ? "Отчёт скопирован целиком." : "Буфер обмена занят. Повторите копирование.");
                    if (id == 102)
                    {
                        try
                        {
                            string path = ChooseFile(window, title);
                            if (path != null)
                            {
                                if (!String.Equals(System.IO.Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
                                    throw new ArgumentException("Выберите имя файла с расширением .json.");
                                File.WriteAllText(path, json, new UTF8Encoding(false));
                                Native.SetWindowTextW(Item(100), "JSON сохранён: " + path);
                            }
                        }
                        catch (Exception error) { Native.MessageBoxW(window, "JSON не сохранён полностью.\n" + error, title, 0x10u); }
                    }
                    return new IntPtr(1);
                }
            }
            catch (Exception error) { failure = error; Native.EndDialog(hwnd, IntPtr.Zero); return new IntPtr(1); }
            return IntPtr.Zero;
        }
        private IntPtr Item(int id) { return Native.GetDlgItem(window, id); }
        private void Add(string kind, int id, string caption, uint style, IntPtr fontHandle)
        {
            IntPtr child = Native.CreateWindowExW(0, kind, caption, 0x54000000u | style, 0, 0, 1, 1, window, new IntPtr(id), Native.GetModuleHandleW(null), IntPtr.Zero);
            if (child == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Native.SendMessageW(child, 0x30, fontHandle, IntPtr.Zero);
        }
        private void Layout()
        {
            Native.Rect client, units = new Native.Rect { Right = 4, Bottom = 8 };
            if (!Native.GetClientRect(window, out client) || !Native.MapDialogRect(window, ref units)) return;
            int w = client.Right * 4 / Math.Max(1, units.Right), h = client.Bottom * 8 / Math.Max(1, units.Bottom);
            Place(100, 10, 9, w - 20, 19); Place(101, 10, 30, w - 20, h - 68);
            Place(102, 10, h - 29, 95, 20); Place(103, 112, h - 29, 95, 20); Place(1, w - 80, h - 29, 70, 20);
        }
        private void Place(int id, int x, int y, int w, int h)
        {
            Native.Rect r = new Native.Rect { Left = x, Top = y, Right = x + w, Bottom = y + h };
            Native.MapDialogRect(window, ref r); Native.SetWindowPos(Item(id), IntPtr.Zero, r.Left, r.Top, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top), 0x14u);
        }
    }
    private static string ChooseFile(IntPtr owner, string title)
    {
        Native.OpenFileName dialog = new Native.OpenFileName();
        dialog.Size = Marshal.SizeOf(typeof(Native.OpenFileName)); dialog.Owner = owner;
        dialog.Filter = "JSON (*.json)\0*.json\0\0"; dialog.FilterIndex = 1;
        string name = "NX_Engraving_Diagnostic_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".json";
        dialog.File = Marshal.AllocHGlobal(32768 * 2);
        dialog.MaxFile = 32768; dialog.Title = title + " — Сохранить JSON";
        dialog.DefaultExt = "json"; dialog.Flags = 0x0008080au; // Explorer, path exists, overwrite prompt, no cwd change.
        try
        {
            byte[] initial = Encoding.Unicode.GetBytes(name + "\0"); Marshal.Copy(initial, 0, dialog.File, initial.Length);
            if (Native.GetSaveFileNameW(ref dialog)) return Marshal.PtrToStringUni(dialog.File);
            int error = Native.CommDlgExtendedError(); if (error != 0) throw new InvalidOperationException("Save dialog error: " + error);
            return null;
        }
        finally { Marshal.FreeHGlobal(dialog.File); }
    }
    private static bool Copy(IntPtr owner, string text)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
        IntPtr memory = Native.GlobalAlloc(2, new UIntPtr((uint)bytes.Length)); if (memory == IntPtr.Zero) return false;
        try
        {
            IntPtr pointer = Native.GlobalLock(memory); if (pointer == IntPtr.Zero) return false;
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); } finally { Native.GlobalUnlock(memory); }
            if (!Native.OpenClipboard(owner)) return false;
            try
            {
                if (!Native.EmptyClipboard() || Native.SetClipboardData(13, memory) == IntPtr.Zero) return false;
                memory = IntPtr.Zero; return true;
            }
            finally { Native.CloseClipboard(); }
        }
        finally { if (memory != IntPtr.Zero) Native.GlobalFree(memory); }
    }
    private static class Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate IntPtr DialogProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct MinMaxInfo { internal Point Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct OpenFileName
        {
            internal int Size; internal IntPtr Owner, Instance; internal string Filter; internal IntPtr CustomFilter;
            internal int MaxCustomFilter, FilterIndex; internal IntPtr File; internal int MaxFile;
            internal IntPtr FileTitle; internal int MaxFileTitle; internal string InitialDir, Title; internal uint Flags;
            internal short FileOffset, Extension; internal string DefaultExt; internal IntPtr CustomData, Hook, TemplateName, Reserved;
            internal int ReservedValue, FlagsEx;
        }
        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)] internal static extern IntPtr DialogBoxIndirectParamW(IntPtr instance, IntPtr template, IntPtr owner, DialogProc proc, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] internal static extern IntPtr CreateWindowExW(uint exStyle, string type, string text, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr id, IntPtr instance, IntPtr param);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern IntPtr GetModuleHandleW(string module);
        [DllImport("user32.dll")] internal static extern bool EndDialog(IntPtr hwnd, IntPtr result);
        [DllImport("user32.dll")] internal static extern IntPtr GetDlgItem(IntPtr hwnd, int id);
        [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] internal static extern bool SetWindowTextW(IntPtr hwnd, string text);
        [DllImport("user32.dll", ExactSpelling = true)] internal static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool MapDialogRect(IntPtr hwnd, ref Rect rect);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint flags);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern IntPtr CreateFontW(int height, int width, int angle, int orientation, int weight, uint italic, uint underline, uint strikeout, uint charset, uint precision, uint clip, uint quality, uint pitch, string face);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll")] internal static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] internal static extern bool CloseClipboard();
        [DllImport("user32.dll")] internal static extern bool EmptyClipboard();
        [DllImport("user32.dll")] internal static extern IntPtr SetClipboardData(uint format, IntPtr memory);
        [DllImport("kernel32.dll")] internal static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] internal static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] internal static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] internal static extern IntPtr GlobalFree(IntPtr memory);
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern bool GetSaveFileNameW(ref OpenFileName file);
        [DllImport("comdlg32.dll")] internal static extern int CommDlgExtendedError();
    }
}
