// Нумерация папок программ — Siemens NX / Designcenter.
// SCRIPT_VERSION: V1.03
// Рабочий файл: NX_Number_Program_Folders.cs
// Номера следуют порядку дерева; вставка сдвигает последующие папки.
// Обрабатываются только папки Program Order с латинской заглавной O.
// Зависимости: штатные NXOpen и библиотеки .NET среды NX; Microsoft Excel не нужен.
// Запуск: Tools -> Journal -> Play. Сохраните .prt после успешного выполнения.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;

public class NX_Number_Program_Folders
{
    public const string SCRIPT_VERSION = "V1.03";
    private const string Title = "Нумерация папок программ — " + SCRIPT_VERSION;
    private const string SettingsFile = @"C:\ProgramData\3_NX_DATA\NX_Numbering_Settings_v1.0.ini";
    private const string IdAttribute = "NX_NUM_V1_ID";
    private const string NameAttribute = "NX_NUM_V1_NAME";
    private const string OwnerAttribute = "NX_NUM_V1_OWNER";
    private const string Pending = "Зарезервирован";
    private const string Assigned = "Присвоен";
    private const string Cancelled = "Отменён";

    public static void Main(string[] args)
    {
        try
        {
            Session session = Session.GetSession();
            string result = Execute(session, session.Parts.Work, SettingsFile);
            UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Information, result);
        }
        catch (Exception ex)
        {
            UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Error, ErrorText(ex));
        }
    }

    // Отдельный метод позволяет проверить алгоритм вне установленного NX.
    public static string Execute(Session session, Part part, string settingsPath)
    {
        if (part == null || part.CAMSetup == null)
            throw new Exception("Откройте рабочую деталь с CAM-проектом.");
        string projectPath = part.FullPath;
        if (String.IsNullOrEmpty(projectPath) || !Path.IsPathRooted(projectPath) || !File.Exists(projectPath))
            throw new Exception("Сначала сохраните рабочий CAM-проект в файл .prt, затем запустите скрипт снова.");
        projectPath = Path.GetFullPath(projectPath);
        NCGroup root = part.CAMSetup.GetRoot(CAMSetup.View.ProgramOrder);
        if (root == null) throw new Exception("Не найден корень дерева «Порядок программ».");

        List<Folder> folders = new List<Folder>();
        CollectFolders(root, folders, new Dictionary<Tag, bool>());
        int eligible = 0;
        for (int i = 0; i < folders.Count; i++) if (folders[i].Eligible) eligible++;
        if (eligible == 0)
            return "Папок, начинающихся с латинской заглавной O, в дереве «Порядок программ» нет.";

        Settings settings = Settings.Load(settingsPath);
        if (!File.Exists(settings.RegistryPath))
            throw new Exception("Не найден Excel-реестр:\n" + settings.RegistryPath +
                "\n\nДля первого запуска положите сюда пустой реестр из комплекта. " +
                "Если номера уже выдавались, восстановите рабочий реестр; не заменяйте его пустым.");

        // Отдельная блокировка защищает от двух одновременно запущенных журналов NX.
        FileStream gate;
        try { gate = new FileStream(settings.RegistryPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new Exception("Реестр уже используется другим запуском скрипта. Повторите после его завершения."); }
        using (gate)
        {
            Registry registry = Registry.Load(settings.RegistryPath);
            List<PlanItem> plan = BuildPlan(part, projectPath, folders, settings, registry);
            int newCount = 0, shiftedCount = 0, renameCount = 0;
            List<Record> newRecords = new List<Record>();
            List<RecordSnapshot> snapshots = new List<RecordSnapshot>();
            for (int i = 0; i < plan.Count; i++)
            {
                PlanItem p = plan[i];
                if (p.IsNewFolder) newCount++;
                else if (p.AssignmentChanged) shiftedCount++;
                if (p.IsNewRecord) newRecords.Add(p.Record);
                else if (p.AssignmentChanged || p.Record.State == Pending)
                    snapshots.Add(new RecordSnapshot(p.Record));
                if (p.Folder.OriginalName != p.Record.Name) renameCount++;
            }
            if (newCount == 0 && renameCount == 0 && snapshots.Count == 0)
                return "Все подходящие папки уже пронумерованы по порядку дерева.\n" +
                    "Сохранено номеров: " + plan.Count.ToString() + ". Новых папок нет.\n\nРеестр:\n" + settings.RegistryPath;

            // Резервирование на диске ПЕРЕД изменением NX предотвращает повторную
            // выдачу даже при аварийном закрытии NX между двумя сохранениями реестра.
            for (int i = 0; i < newRecords.Count; i++) registry.Append(newRecords[i]);
            if (newRecords.Count > 0) registry.SaveAtomic();

            Session.UndoMarkId undoMark;
            try { undoMark = session.SetUndoMark(Session.MarkVisibility.Visible, Title); }
            catch (Exception ex)
            {
                throw new Exception("NX не создал точку отмены. Папки не изменены. " +
                    "Зарезервированные номера остаются занятыми.\n" + ErrorText(ex));
            }
            bool metadataChanged = false;
            try
            {
                ApplyPlan(part, projectPath, plan);
                metadataChanged = newRecords.Count > 0 || snapshots.Count > 0;
                DateTime assignedAt = DateTime.Now;
                for (int i = 0; i < plan.Count; i++)
                {
                    PlanItem p = plan[i];
                    if (p.IsNewRecord) registry.SetState(p.Record, Assigned, "");
                    else if (p.AssignmentChanged) registry.Reassign(p.Record, p.Folder.OriginalName, assignedAt);
                    else if (p.Record.State == Pending)
                        registry.SetState(p.Record, Assigned, AppendNote(p.Record.Note, "Завершено после прерванного запуска."));
                }
                if (metadataChanged) registry.SaveAtomic();
            }
            catch (Exception ex)
            {
                bool undone = false;
                string rollback;
                try { session.UndoToMark(undoMark, Title); undone = true; rollback = "Изменения этого запуска в NX отменены."; }
                catch (Exception undoEx) { rollback = "Автоматическая отмена не удалась. Проверьте папки в NX.\n" + ErrorText(undoEx); }
                try
                {
                    // Номера и ID строк неизменны. При ошибке возвращаем также
                    // прежние дату, исходное имя и примечание существующих записей.
                    for (int i = 0; i < snapshots.Count; i++) registry.Restore(snapshots[i]);
                    for (int i = 0; i < newRecords.Count; i++)
                        registry.SetState(newRecords[i], undone ? Cancelled : Pending, ShortText(ErrorText(ex), 500));
                    if (newRecords.Count > 0 || metadataChanged) registry.SaveAtomic();
                }
                catch (Exception logEx) { rollback += "\nНе удалось обновить состояние в реестре: " + ErrorText(logEx); }
                throw new Exception("Переименование не завершено.\n" + rollback +
                    "\nЗарезервированные номера остаются занятыми и повторно не выдаются.\n\n" + ErrorText(ex));
            }

            StringBuilder message = new StringBuilder();
            message.Append("Готово. Новых папок: ").Append(newCount).Append(".\n");
            message.Append("Изменено номеров у прежних папок: ").Append(shiftedCount).Append(".\n");
            message.Append("Прежних папок без смены номера: ").Append(plan.Count - newCount - shiftedCount).Append(".\n");
            int shown = 0;
            for (int i = 0; i < plan.Count; i++) if (plan[i].AssignmentChanged || plan[i].Folder.OriginalName != plan[i].Record.Name)
            {
                if (shown++ < 12) message.Append("\n").Append(plan[i].Folder.OriginalName).Append(" → ").Append(plan[i].Record.Name);
            }
            if (shown > 12) message.Append("\n…");
            message.Append("\n\nРеестр:\n").Append(settings.RegistryPath);
            message.Append("\n\nСохраните CAM-проект (.prt), чтобы номера и служебные отметки сохранились после закрытия NX.");
            return message.ToString();
        }
    }

    private sealed class Folder
    {
        public NCGroup Group;
        public string OriginalName, Id, StoredName, Owner;
        public bool Eligible;
    }
    private sealed class PlanItem
    {
        public Folder Folder;
        public Record Record, PreviousRecord;
        public bool IsNewFolder, IsNewRecord, AssignmentChanged;
    }

    private static void CollectFolders(NCGroup parent, List<Folder> folders, Dictionary<Tag, bool> visited)
    {
        if (visited.ContainsKey(parent.Tag)) throw new Exception("Обнаружен повторный узел в дереве программ.");
        visited.Add(parent.Tag, true);
        CAMObject[] members = parent.GetMembers();
        for (int i = 0; i < members.Length; i++)
        {
            NCGroup group = members[i] as NCGroup;
            if (group == null) continue;
            Folder folder = new Folder();
            folder.Group = group;
            folder.OriginalName = group.Name ?? "";
            folder.Eligible = folder.OriginalName.StartsWith("O", StringComparison.Ordinal);
            folder.Id = Attribute(group, IdAttribute);
            folder.StoredName = Attribute(group, NameAttribute);
            folder.Owner = Attribute(group, OwnerAttribute);
            folders.Add(folder);
            // Вложенные папки проверяются и под родителем, имя которого не начинается с O.
            CollectFolders(group, folders, visited);
        }
    }

    private static string Attribute(NXObject obj, string title)
    {
        if (!obj.HasUserAttribute(title, NXObject.AttributeType.String, -1)) return "";
        return obj.GetStringUserAttribute(title, -1) ?? "";
    }

    private static List<PlanItem> BuildPlan(Part part, string projectPath, List<Folder> folders, Settings settings, Registry registry)
    {
        // Копирование папки в NX копирует и атрибуты. Старую отметку учитываем
        // только один раз, предпочитая папку с точным зарегистрированным именем.
        Dictionary<string, Folder> owners = new Dictionary<string, Folder>(StringComparer.Ordinal);
        for (int i = 0; i < folders.Count; i++)
        {
            Folder f = folders[i];
            if (f.Id.Length == 0 || !SamePath(f.Owner, projectPath)) continue;
            Folder owner;
            if (!owners.TryGetValue(f.Id, out owner) ||
                (f.OriginalName == f.StoredName && owner.OriginalName != owner.StoredName)) owners[f.Id] = f;
        }

        List<PlanItem> plan = new List<PlanItem>();
        List<Record> slots = new List<Record>();
        Dictionary<Tag, bool> targets = new Dictionary<Tag, bool>();
        for (int i = 0; i < folders.Count; i++)
        {
            Folder f = folders[i];
            if (!f.Eligible) continue;
            PlanItem p = new PlanItem(); p.Folder = f;
            Folder owner;
            bool ownMarker = f.Id.Length > 0 && SamePath(f.Owner, projectPath) &&
                owners.TryGetValue(f.Id, out owner) && Object.ReferenceEquals(owner, f);
            if (ownMarker)
            {
                Record r;
                if (!registry.ById.TryGetValue(f.Id, out r))
                    throw new Exception("Папка «" + f.OriginalName + "» уже имеет отметку нумерации, " +
                        "но её записи нет в этом Excel-реестре.\nВосстановите соответствующий рабочий реестр. Папки не изменены.");
                if (r.Name != f.StoredName || !SamePath(r.ProjectPath, projectPath) || r.State == Cancelled)
                    throw new Exception("Отметка папки «" + f.OriginalName + "» не совпадает с реестром. Папки не изменены.");
                p.PreviousRecord = r;
                slots.Add(r);
            }
            else p.IsNewFolder = true;
            targets[f.Group.Tag] = true;
            plan.Add(p);
        }

        // Перераспределяются только номера живых подходящих папок этого проекта.
        // Чужие, удалённые, отменённые и не подтверждённые атрибутами записи
        // остаются занятыми. Без вставки/перестановки старые номера сохраняются.
        slots.Sort(delegate(Record a, Record b) { return a.Number.CompareTo(b.Number); });
        int existingCount = slots.Count;
        Dictionary<string, bool> unavailable = AllCamNames(part, targets);
        for (int i = 0; i < slots.Count; i++)
        {
            if (unavailable.ContainsKey(slots[i].Name))
                throw new Exception("Нельзя присвоить имя «" + slots[i].Name + "»: оно занято другим CAM-объектом. Папки не изменены.");
            unavailable[slots[i].Name] = true;
        }
        long next = settings.StartNumber;
        for (int i = 0; i < registry.Records.Count; i++)
            if (registry.Records[i].Number >= next) next = (long)registry.Records[i].Number + 1;

        while (slots.Count < plan.Count)
        {
            string name = "";
            while (next <= settings.EndNumber)
            {
                name = settings.Prefix + next.ToString(CultureInfo.InvariantCulture);
                if (!unavailable.ContainsKey(name) && !registry.ByName.ContainsKey(name)) break;
                next++;
            }
            if (next > settings.EndNumber)
                throw new Exception("Недостаточно свободных номеров в диапазоне " + settings.Prefix + settings.StartNumber +
                    "–" + settings.Prefix + settings.EndNumber + ".\nПапки и реестр не изменены. Измените выделенный диапазон в .ini.");
            Record r = new Record();
            r.Name = name; r.Number = (int)next++; r.ProjectName = Path.GetFileNameWithoutExtension(projectPath);
            r.ProjectPath = projectPath; r.When = DateTime.Now;
            r.State = Pending; r.Id = Guid.NewGuid().ToString("N"); r.Note = "";
            slots.Add(r); unavailable[name] = true;
        }
        // ID обозначает запись выданного номера, а не навечно закреплённую папку.
        // При сдвиге вся отметка переносится вместе с номером. Поэтому в XLSX
        // не появляются дубли ID/номеров даже при обмене O1664 и O1665 местами.
        for (int i = 0; i < plan.Count; i++)
        {
            PlanItem p = plan[i]; p.Record = slots[i];
            p.IsNewRecord = i >= existingCount;
            p.AssignmentChanged = !Object.ReferenceEquals(p.PreviousRecord, p.Record);
            if (p.IsNewRecord) p.Record.OldName = p.Folder.OriginalName;
        }
        return plan;
    }

    private static Dictionary<string, bool> AllCamNames(Part part, Dictionary<Tag, bool> excluded)
    {
        Dictionary<string, bool> names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        NCGroup[] groups = part.CAMSetup.CAMGroupCollection.ToArray();
        for (int i = 0; i < groups.Length; i++) if (!excluded.ContainsKey(groups[i].Tag)) names[groups[i].Name] = true;
        NXOpen.CAM.Operation[] operations = part.CAMSetup.CAMOperationCollection.ToArray();
        for (int i = 0; i < operations.Length; i++) names[operations[i].Name] = true;
        return names;
    }

    private static void ApplyPlan(Part part, string projectPath, List<PlanItem> plan)
    {
        Dictionary<string, bool> names = AllCamNames(part, new Dictionary<Tag, bool>());
        for (int i = 0; i < plan.Count; i++) names[plan[i].Record.Name] = true;
        string tempBase = "TMPN" + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
        int serial = 1;
        for (int i = 0; i < plan.Count; i++)
        {
            PlanItem p = plan[i];
            if (p.Folder.OriginalName == p.Record.Name) continue;
            string temp;
            do { temp = tempBase + "_" + (serial++).ToString(CultureInfo.InvariantCulture); } while (names.ContainsKey(temp));
            names[temp] = true; SetName(p.Folder.Group, temp);
        }
        for (int i = 0; i < plan.Count; i++)
        {
            PlanItem p = plan[i];
            if (p.Folder.Group.Name != p.Record.Name) SetName(p.Folder.Group, p.Record.Name);
            if (p.AssignmentChanged)
            {
                p.Folder.Group.SetUserAttribute(IdAttribute, -1, p.Record.Id, Update.Option.Now);
                p.Folder.Group.SetUserAttribute(NameAttribute, -1, p.Record.Name, Update.Option.Now);
                p.Folder.Group.SetUserAttribute(OwnerAttribute, -1, projectPath, Update.Option.Now);
            }
        }
    }

    private static void SetName(NCGroup group, string name)
    {
        string previous = group.Name;
        try
        {
            group.SetName(name);
            if (group.Name != name) throw new Exception("NX вернул другое имя: " + group.Name);
        }
        catch (Exception ex) { throw new Exception("NX не присвоил имя «" + name + "» папке «" + previous + "».\n" + ErrorText(ex)); }
    }

    private static bool SamePath(string a, string b)
    {
        if (String.IsNullOrEmpty(a) || String.IsNullOrEmpty(b)) return false;
        return String.Equals(a.Replace('/', '\\'), b.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
    }
    private static string ShortText(string text, int length) { return text.Length <= length ? text : text.Substring(0, length); }
    private static string AppendNote(string existing, string entry)
    {
        string note = String.IsNullOrEmpty(existing) ? entry : existing + "\n" + entry;
        // Ограничение текста ячейки Excel: оставляем самые свежие события.
        return note.Length <= 32000 ? note : "…\n" + note.Substring(note.Length - 31998);
    }
    private static string ErrorText(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
        return ex.Message;
    }

    private sealed class Settings
    {
        public int StartNumber, EndNumber;
        public string Prefix, RegistryPath;
        public static Settings Load(string path)
        {
            if (!File.Exists(path)) throw new Exception("Не найден файл настроек:\n" + path);
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            // UTF-8 с BOM — штатная кодировка файлов из комплекта.
            string[] lines = File.ReadAllLines(path, new UTF8Encoding(false, true));
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].Trim().TrimStart('\uFEFF');
                if (s.Length == 0 || s.StartsWith(";") || s.StartsWith("#")) continue;
                if (s.StartsWith("[") && s.EndsWith("]")) { section = s.Substring(1, s.Length - 2).Trim(); continue; }
                if (!section.Equals("Numbering", StringComparison.OrdinalIgnoreCase)) continue;
                int equal = s.IndexOf('=');
                if (equal <= 0) throw new Exception("Ошибка .ini в строке " + (i + 1) + ": ожидается Параметр=Значение.");
                string key = s.Substring(0, equal).Trim(); string val = s.Substring(equal + 1).Trim();
                if (values.ContainsKey(key)) throw new Exception("Параметр .ini указан дважды: " + key);
                if (!key.Equals("Prefix", StringComparison.OrdinalIgnoreCase) && !key.Equals("StartNumber", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("EndNumber", StringComparison.OrdinalIgnoreCase) && !key.Equals("RegistryFile", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Неизвестный параметр .ini: " + key);
                values.Add(key, val);
            }
            string[] required = { "Prefix", "StartNumber", "EndNumber", "RegistryFile" };
            for (int i = 0; i < required.Length; i++) if (!values.ContainsKey(required[i]) || values[required[i]].Length == 0)
                throw new Exception("В .ini отсутствует значение: " + required[i]);
            Settings result = new Settings(); result.Prefix = values["Prefix"];
            if (result.Prefix.Length > 8) throw new Exception("Prefix: допустимо от 1 до 8 заглавных латинских букв.");
            for (int i = 0; i < result.Prefix.Length; i++) if (result.Prefix[i] < 'A' || result.Prefix[i] > 'Z')
                throw new Exception("Prefix должен содержать заглавные латинские буквы. Для O1659 укажите O.");
            if (!Int32.TryParse(values["StartNumber"], NumberStyles.None, CultureInfo.InvariantCulture, out result.StartNumber) ||
                !Int32.TryParse(values["EndNumber"], NumberStyles.None, CultureInfo.InvariantCulture, out result.EndNumber) ||
                result.StartNumber < 1 || result.EndNumber < result.StartNumber)
                throw new Exception("Проверьте StartNumber и EndNumber: нужны целые положительные числа, начало не больше конца.");
            string registry = values["RegistryFile"];
            result.RegistryPath = Path.GetFullPath(Path.IsPathRooted(registry) ? registry : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), registry));
            if (!Path.GetExtension(result.RegistryPath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new Exception("RegistryFile должен указывать на файл .xlsx.");
            return result;
        }
    }

    private sealed class Record
    {
        public string Name, ProjectName, ProjectPath, OldName, State, Id, Note;
        public int Number, Row;
        public DateTime When;
    }

    private sealed class RecordSnapshot
    {
        public Record Record;
        public string OldName, State, Note;
        public DateTime When;
        public RecordSnapshot(Record r)
        { Record = r; OldName = r.OldName; State = r.State; Note = r.Note; When = r.When; }
    }

    // Прямое чтение/обновление Open XML. Все посторонние части книги сохраняются.
    private sealed class Registry
    {
        private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly string[] Headers = { "Имя папки", "Номер", "Проект", "Полный путь проекта", "Дата и время", "Прежнее имя", "Состояние", "ID записи", "Примечание" };
        public List<Record> Records = new List<Record>();
        public Dictionary<string, Record> ById = new Dictionary<string, Record>(StringComparer.Ordinal);
        public Dictionary<string, Record> ByName = new Dictionary<string, Record>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<int, bool> numbers = new Dictionary<int, bool>();
        private Dictionary<string, byte[]> entries;
        private List<string> strings = new List<string>();
        private Dictionary<int, XmlElement> rows = new Dictionary<int, XmlElement>();
        private XmlDocument sheet;
        private XmlNamespaceManager nsm;
        private XmlElement data;
        private string path, sheetPath;
        private byte[] lastSaved;
        private string[] styles = new string[9];
        private int lastRow = 1;

        public static Registry Load(string path)
        {
            Registry r = new Registry(); r.path = path;
            r.lastSaved = ReadExclusive(path);
            r.entries = ZipParts.Read(r.lastSaved);
            XmlDocument workbook = Parse(r.Entry("xl/workbook.xml"));
            XmlNamespaceManager wns = Manager(workbook);
            XmlElement properties = workbook.SelectSingleNode("/s:workbook/s:workbookPr", wns) as XmlElement;
            if (properties != null && (properties.GetAttribute("date1904") == "1" || properties.GetAttribute("date1904") == "true"))
                throw new Exception("Реестр использует систему дат 1904. Нужен исходный формат книги из комплекта.");
            XmlElement info = workbook.SelectSingleNode("/s:workbook/s:sheets/s:sheet[@name='Реестр']", wns) as XmlElement;
            if (info == null) throw new Exception("В книге отсутствует лист «Реестр».");
            string rid = info.GetAttribute("id", RelNs);
            XmlDocument relations = Parse(r.Entry("xl/_rels/workbook.xml.rels"));
            foreach (XmlNode node in relations.DocumentElement.ChildNodes)
            {
                XmlElement rel = node as XmlElement;
                if (rel != null && rel.GetAttribute("Id") == rid)
                {
                    string target = rel.GetAttribute("Target");
                    if (rel.GetAttribute("TargetMode").Equals("External", StringComparison.OrdinalIgnoreCase))
                        throw new Exception("Лист реестра ссылается на внешний файл. Нужен обычный .xlsx из комплекта.");
                    r.sheetPath = ResolvePackagePart("xl/workbook.xml", target);
                }
            }
            if (String.IsNullOrEmpty(r.sheetPath)) throw new Exception("Не найден XML листа «Реестр».");
            if (r.entries.ContainsKey("xl/sharedStrings.xml"))
            {
                XmlDocument shared = Parse(r.Entry("xl/sharedStrings.xml"));
                foreach (XmlNode si in shared.DocumentElement.ChildNodes) r.strings.Add(ReadText(si));
            }
            r.sheet = Parse(r.Entry(r.sheetPath)); r.nsm = Manager(r.sheet);
            r.data = r.sheet.SelectSingleNode("/s:worksheet/s:sheetData", r.nsm) as XmlElement;
            if (r.data == null) throw new Exception("Повреждена структура листа «Реестр».");
            foreach (XmlNode node in r.data.ChildNodes)
            {
                XmlElement row = node as XmlElement; if (row == null || row.LocalName != "row") continue;
                int number;
                if (!Int32.TryParse(row.GetAttribute("r"), out number) || number < 1 || r.rows.ContainsKey(number))
                    throw new Exception("Некорректный номер строки в реестре.");
                r.rows.Add(number, row);
            }
            for (int c = 0; c < Headers.Length; c++) if (r.Value(1, c) != Headers[c])
                throw new Exception("Неверный заголовок столбца " + Column(c) + ". Ожидается: «" + Headers[c] + "».");
            for (int c = 0; c < 9; c++)
            {
                XmlElement cell = r.Cell(2, c); r.styles[c] = cell == null ? "" : cell.GetAttribute("s");
            }
            foreach (KeyValuePair<int, XmlElement> pair in r.rows)
            {
                if (pair.Key <= 1) continue;
                string[] v = new string[9]; bool empty = true;
                for (int c = 0; c < 9; c++) { v[c] = r.Value(pair.Key, c); if (v[c].Length > 0) empty = false; }
                if (empty) continue;
                Record rec = new Record(); rec.Row = pair.Key;
                int number; double stamp; Guid parsedId;
                if (!Int32.TryParse(v[1], NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < 1 ||
                    !Double.TryParse(v[4], NumberStyles.Float, CultureInfo.InvariantCulture, out stamp) ||
                    !Guid.TryParseExact(v[7], "N", out parsedId) || v[2].Length == 0 || v[3].Length == 0 ||
                    (v[6] != Pending && v[6] != Assigned && v[6] != Cancelled))
                    throw new Exception("Неполная или повреждённая запись в строке " + pair.Key + ". Папки не изменены.");
                string tail = number.ToString(CultureInfo.InvariantCulture);
                if (!v[0].EndsWith(tail, StringComparison.Ordinal) || v[0].Length <= tail.Length)
                    throw new Exception("Имя папки и номер не совпадают в строке " + pair.Key + ".");
                rec.Name = v[0]; rec.Number = number; rec.ProjectName = v[2]; rec.ProjectPath = v[3];
                rec.When = DateTime.FromOADate(stamp); rec.OldName = v[5]; rec.State = v[6]; rec.Id = v[7]; rec.Note = v[8];
                r.AddIndex(rec); r.lastRow = Math.Max(r.lastRow, pair.Key);
            }
            if (r.styles[4].Length == 0) throw new Exception("В реестре отсутствует формат даты/времени. Используйте исходную структуру книги.");
            return r;
        }

        private void AddIndex(Record r)
        {
            if (ById.ContainsKey(r.Id) || ByName.ContainsKey(r.Name) || numbers.ContainsKey(r.Number))
                throw new Exception("В Excel-реестре повторяется номер, имя или ID записи: " + r.Name + ".");
            Records.Add(r); ById.Add(r.Id, r); ByName.Add(r.Name, r); numbers.Add(r.Number, true);
        }
        public void Append(Record r)
        {
            if (lastRow >= 1048576) throw new Exception("В Excel-реестре закончились строки.");
            r.Row = ++lastRow; AddIndex(r); WriteRecord(r); UpdateRange();
        }
        public void SetState(Record r, string state, string note)
        {
            r.State = state; r.Note = note; Put(r.Row, 6, state, false); Put(r.Row, 8, note, false);
        }
        public void Reassign(Record r, string oldName, DateTime when)
        {
            string entry = when.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) +
                ": порядок дерева, «" + oldName + "» → " + r.Name +
                ". Предыдущее назначение: " + r.When.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) +
                ", исходное имя «" + r.OldName + "».";
            r.Note = AppendNote(r.Note, entry);
            r.OldName = oldName; r.When = when; r.State = Assigned;
            WriteRecord(r);
        }
        public void Restore(RecordSnapshot saved)
        {
            Record r = saved.Record;
            r.OldName = saved.OldName; r.State = saved.State; r.Note = saved.Note; r.When = saved.When;
            WriteRecord(r);
        }
        private void WriteRecord(Record r)
        {
            string[] values = { r.Name, r.Number.ToString(CultureInfo.InvariantCulture), r.ProjectName, r.ProjectPath,
                r.When.ToOADate().ToString("R", CultureInfo.InvariantCulture), r.OldName, r.State, r.Id, r.Note };
            for (int i = 0; i < values.Length; i++) Put(r.Row, i, values[i], i == 1 || i == 4);
        }
        private void Put(int rowNumber, int column, string value, bool numeric)
        {
            XmlElement row;
            if (!rows.TryGetValue(rowNumber, out row))
            {
                row = sheet.CreateElement("row", Ns); row.SetAttribute("r", rowNumber.ToString(CultureInfo.InvariantCulture));
                row.SetAttribute("ht", "24"); row.SetAttribute("customHeight", "1");
                XmlNode next = null;
                foreach (XmlNode existing in data.ChildNodes)
                    if (existing is XmlElement && Int32.Parse(((XmlElement)existing).GetAttribute("r"), CultureInfo.InvariantCulture) > rowNumber) { next = existing; break; }
                data.InsertBefore(row, next); rows.Add(rowNumber, row);
            }
            XmlElement old = Cell(rowNumber, column);
            XmlElement cell = sheet.CreateElement("c", Ns);
            string address = Column(column) + rowNumber.ToString(CultureInfo.InvariantCulture);
            cell.SetAttribute("r", address);
            string style = old != null ? old.GetAttribute("s") : styles[column];
            if (style.Length > 0) cell.SetAttribute("s", style);
            if (numeric) { XmlElement v = sheet.CreateElement("v", Ns); v.InnerText = value; cell.AppendChild(v); }
            else
            {
                cell.SetAttribute("t", "inlineStr"); XmlElement inline = sheet.CreateElement("is", Ns);
                XmlElement text = sheet.CreateElement("t", Ns); text.SetAttribute("space", "http://www.w3.org/XML/1998/namespace", "preserve");
                text.InnerText = value ?? ""; inline.AppendChild(text); cell.AppendChild(inline);
            }
            if (old != null) row.ReplaceChild(cell, old);
            else
            {
                XmlNode next = null;
                foreach (XmlNode current in row.ChildNodes)
                {
                    XmlElement c = current as XmlElement;
                    if (c != null && c.LocalName == "c" && ColumnIndex(c.GetAttribute("r")) > column) { next = c; break; }
                }
                row.InsertBefore(cell, next);
            }
        }
        private XmlElement Cell(int row, int col)
        {
            XmlElement element; if (!rows.TryGetValue(row, out element)) return null;
            string address = Column(col) + row.ToString(CultureInfo.InvariantCulture);
            foreach (XmlNode node in element.ChildNodes)
            {
                XmlElement c = node as XmlElement;
                if (c != null && c.LocalName == "c" && c.GetAttribute("r") == address) return c;
            }
            return null;
        }
        private string Value(int row, int col)
        {
            XmlElement cell = Cell(row, col); if (cell == null) return "";
            if (cell.SelectSingleNode("s:f", nsm) != null) throw new Exception("Формулы в записях реестра не поддерживаются: " + cell.GetAttribute("r"));
            string type = cell.GetAttribute("t");
            if (type == "inlineStr") return ReadText(cell);
            XmlNode v = cell.SelectSingleNode("s:v", nsm);
            string value = v == null ? "" : v.InnerText;
            if (type == "s")
            {
                int index; if (!Int32.TryParse(value, out index) || index < 0 || index >= strings.Count) throw new Exception("Повреждена таблица строк Excel.");
                return strings[index];
            }
            return value;
        }
        private void UpdateRange()
        {
            string range = "A1:I" + Math.Max(2, lastRow).ToString(CultureInfo.InvariantCulture);
            XmlElement dimension = sheet.SelectSingleNode("/s:worksheet/s:dimension", nsm) as XmlElement;
            if (dimension != null) dimension.SetAttribute("ref", range);
            XmlElement filter = sheet.SelectSingleNode("/s:worksheet/s:autoFilter", nsm) as XmlElement;
            if (filter != null) filter.SetAttribute("ref", range);
            List<string> keys = new List<string>(entries.Keys);
            for (int i = 0; i < keys.Count; i++) if (keys[i].StartsWith("xl/tables/", StringComparison.Ordinal) && keys[i].EndsWith(".xml", StringComparison.Ordinal))
            {
                XmlDocument table = Parse(entries[keys[i]]);
                if (table.DocumentElement.GetAttribute("name") != "NXNumberingRegister") continue;
                table.DocumentElement.SetAttribute("ref", range);
                foreach (XmlNode node in table.DocumentElement.ChildNodes)
                    if (node is XmlElement && node.LocalName == "autoFilter") ((XmlElement)node).SetAttribute("ref", range);
                entries[keys[i]] = Bytes(table);
            }
        }

        public void SaveAtomic()
        {
            entries[sheetPath] = Bytes(sheet);
            byte[] bytes = ZipParts.Write(entries);
            // Не перезаписываем книгу, которую кто-то успел изменить извне.
            byte[] current = ReadExclusive(path);
            if (!EqualBytes(current, lastSaved)) throw new Exception("Excel-реестр изменён другой программой. Повторите запуск после проверки реестра.");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream fs = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { fs.Write(bytes, 0, bytes.Length); fs.Flush(true); }
                // Никакого удаления оригинала при ошибке. Replace либо заменит его
                // полностью, либо оставит исходный файл; .bak содержит предыдущую версию.
                File.Replace(temporary, path, path + ".bak");
                lastSaved = bytes;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static byte[] ReadExclusive(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                using (MemoryStream ms = new MemoryStream()) { fs.CopyTo(ms); return ms.ToArray(); }
            }
            catch (IOException ex) { throw new Exception("Не удалось открыть Excel-реестр для записи. Закройте его в Excel и повторите запуск.\n" + path + "\n" + ex.Message); }
        }
        private byte[] Entry(string name)
        {
            byte[] value; if (!entries.TryGetValue(name, out value)) throw new Exception("В книге отсутствует обязательная часть: " + name);
            return value;
        }
        private static bool EqualBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        private static XmlDocument Parse(byte[] bytes)
        {
            XmlDocument doc = new XmlDocument(); doc.XmlResolver = null;
            XmlReaderSettings s = new XmlReaderSettings(); s.DtdProcessing = DtdProcessing.Prohibit; s.XmlResolver = null;
            using (MemoryStream ms = new MemoryStream(bytes)) using (XmlReader xr = XmlReader.Create(ms, s)) doc.Load(xr);
            return doc;
        }
        private static byte[] Bytes(XmlDocument doc)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                XmlWriterSettings s = new XmlWriterSettings(); s.Encoding = new UTF8Encoding(false); s.Indent = false;
                using (XmlWriter xw = XmlWriter.Create(ms, s)) doc.Save(xw);
                return ms.ToArray();
            }
        }
        private static XmlNamespaceManager Manager(XmlDocument doc)
        {
            XmlNamespaceManager result = new XmlNamespaceManager(doc.NameTable); result.AddNamespace("s", Ns); return result;
        }
        private static string ReadText(XmlNode root)
        {
            StringBuilder text = new StringBuilder();
            if (root.LocalName == "t") text.Append(root.InnerText);
            else foreach (XmlNode child in root.ChildNodes)
                if (child.LocalName != "rPh" && child.LocalName != "phoneticPr") text.Append(ReadText(child));
            return text.ToString();
        }
        private static string Column(int col) { return ((char)('A' + col)).ToString(); }
        private static int ColumnIndex(string address)
        {
            int n = 0;
            for (int i = 0; i < address.Length && address[i] >= 'A' && address[i] <= 'Z'; i++) n = n * 26 + address[i] - 'A' + 1;
            return n - 1;
        }
    }

    // Reflection избавляет C#-журнал NX от требования вручную подключать
    // System.IO.Compression.dll в настройках компиляции журнала.
    private static class ZipParts
    {
        private static Type ArchiveType()
        {
            string[] names = {
                "System.IO.Compression.ZipArchive, System.IO.Compression",
                "System.IO.Compression.ZipArchive, System.IO.Compression, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"
            };
            for (int i = 0; i < names.Length; i++)
            {
                Type t = Type.GetType(names[i], false); if (t != null) return t;
            }
            throw new Exception("Не найден штатный компонент System.IO.Compression. Проверьте библиотеки .NET, поставляемые вместе с NX.");
        }
        private static object Archive(Stream stream, bool create)
        {
            Type t = ArchiveType(); Type mode = t.Assembly.GetType("System.IO.Compression.ZipArchiveMode", true);
            return Activator.CreateInstance(t, new object[] { stream, Enum.Parse(mode, create ? "Create" : "Read"), true });
        }
        public static Dictionary<string, byte[]> Read(byte[] bytes)
        {
            Dictionary<string, byte[]> result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                object archive = Archive(ms, false);
                try
                {
                    IEnumerable entries = (IEnumerable)archive.GetType().GetProperty("Entries").GetValue(archive, null);
                    foreach (object entry in entries)
                    {
                        Type t = entry.GetType(); string name = (string)t.GetProperty("FullName").GetValue(entry, null);
                        if (result.ContainsKey(name)) throw new Exception("Повторная часть XLSX: " + name);
                        using (Stream source = (Stream)t.GetMethod("Open", Type.EmptyTypes).Invoke(entry, null))
                        using (MemoryStream content = new MemoryStream()) { source.CopyTo(content); result.Add(name, content.ToArray()); }
                    }
                }
                finally { ((IDisposable)archive).Dispose(); }
            }
            return result;
        }
        public static byte[] Write(Dictionary<string, byte[]> entries)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                object archive = Archive(ms, true);
                try
                {
                    MethodInfo create = archive.GetType().GetMethod("CreateEntry", new Type[] { typeof(string) });
                    foreach (KeyValuePair<string, byte[]> pair in entries)
                    {
                        object entry = create.Invoke(archive, new object[] { pair.Key });
                        using (Stream output = (Stream)entry.GetType().GetMethod("Open", Type.EmptyTypes).Invoke(entry, null))
                            output.Write(pair.Value, 0, pair.Value.Length);
                    }
                }
                finally { ((IDisposable)archive).Dispose(); }
                return ms.ToArray();
            }
        }
    }

    // Путь внутри XLSX — последовательность имён ZIP-частей, а не путь Windows.
    // Разрешаем относительные ссылки без дополнительной сборки System.Private.Uri.
    private static string ResolvePackagePart(string basePart, string target)
    {
        if (String.IsNullOrEmpty(target) || target.IndexOf(':') >= 0 ||
            target.StartsWith("//", StringComparison.Ordinal) || target.IndexOf('?') >= 0 || target.IndexOf('#') >= 0)
            throw new Exception("Некорректная ссылка на лист внутри XLSX: " + target);
        target = target.Replace('\\', '/');
        string combined = target.StartsWith("/", StringComparison.Ordinal)
            ? target.Substring(1)
            : basePart.Substring(0, basePart.LastIndexOf('/') + 1) + target;
        List<string> parts = new List<string>();
        string[] tokens = combined.Split('/');
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if (token.Length == 0 || token == ".") continue;
            if (token == "..")
            {
                if (parts.Count == 0) throw new Exception("Ссылка на лист выходит за пределы XLSX.");
                parts.RemoveAt(parts.Count - 1);
            }
            else parts.Add(token);
        }
        if (parts.Count == 0) throw new Exception("Пустая ссылка на лист внутри XLSX.");
        return String.Join("/", parts.ToArray());
    }

    public static int GetUnloadOption(string dummy) { return (int)Session.LibraryUnloadOption.Immediately; }
}
