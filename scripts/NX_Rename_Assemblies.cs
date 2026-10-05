// NX_Rename_Assemblies — Переименование компонентов и файлов сборки
// SCRIPT_VERSION: V1.05
// Siemens NX / Designcenter 2606, Windows, Native (без Teamcenter).
// Рабочее имя: NX_Rename_Assemblies.cs
// Выдаваемое имя: NX_Rename_Assemblies_V1.05.cs
// Запуск: Инструменты -> Журнал -> Воспроизвести; выбрать этот .cs.
// V1.05: исправлена перерисовка при изменении размера окна и движении разделителя.
// Потяните вертикальную границу между деревом и полями мышью влево/вправо.
// Ручная ширина сохраняется до закрытия окна; настройки на диск не записываются.
// Для подписей длиннее доступной области остаётся горизонтальная прокрутка.
//
// Корень проекта = папка отображаемого в NX файла .prt, без поиска в подпапках.
// Все .prt из корня загружаются в сеанс для проверки ссылок. Окно показывает
// дерево сборок, подсборок и отдельных деталей. Выбор -> новое имя -> применить.
// Одному файлу соответствует одно новое имя во всех его вхождениях проекта.
// Сохраняются выбранная деталь и содержащие её сборки, включая их текущие правки.
// Успешное завершение: новое имя в NX и на диске, старого .prt больше нет.
// Старый .prt удаляется только после проверенного сохранения затронутых сборок.
// Имена экземпляров задаются и проверяются до SaveAs; имя NXObject не используется.
// «Завершить после сбоя»: выбрать новый файл в дереве, затем вручную указать
// его старое имя. Сначала сохраняются ссылки, затем закрывается и удаляется
// выбранный старый .prt, если на него не ссылается ни одна проверенная сборка.
// При сбое до этапа удаления исходный .prt сохраняется. Если NX уже сохранил
// новое имя, оба файла остаются до устранения ошибки. Дополнительных копий нет.
// Ошибка до записи файлов: возвращаются имена экземпляров, можно повторить.
// Ошибка после начала записи: повторное применение в этом окне блокируется.
//
// Область проверки: все .prt непосредственно в корне и загруженные сборки сеанса.
// Закрытые сборки в других папках не проверяются. Внешние компоненты видны, но
// не переименовываются. Если затронута открытая внешняя сборка, операция блокируется.
// Для неполностью загруженной структуры переименование блокируется.
// Изменение только регистра имени файла намеренно блокируется (без временных имён).
// Скрипт не создаёт ini, логи, отчёты, папки, архивы и временные файлы.
// Служебные файлы и системные диалоги самого NX определяются настройками NX;
// их создание/заголовки не контролируются этим журналом.
// Не выполнять Undo для отката файловой операции: Undo NX не восстанавливает .prt.
// Внешние ссылки выражений/WAVE в закрытых файлах вне проекта не обрабатываются.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;

public class NX_Rename_Assemblies
{
    public const string SCRIPT_VERSION = "V1.05";
    public const string SCRIPT_NAME = "Переименование компонентов";
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    public static string WindowTitle(string suffix)
    {
        return SCRIPT_NAME + " — " + SCRIPT_VERSION +
            (String.IsNullOrEmpty(suffix) ? "" : " — " + suffix);
    }

    public static void Main(string[] args)
    {
        try
        {
            Session session = Session.GetSession();
            UFSession uf = UFSession.GetUFSession();
            bool managed;
            uf.UF.IsUgmanagerActive(out managed);
            if (managed)
                throw new InvalidOperationException("Скрипт предназначен для файлов .prt в обычной папке, без Teamcenter.");
            Part display = session.Parts.Display;
            if (display == null || String.IsNullOrEmpty(display.FullPath) ||
                !Path.IsPathRooted(display.FullPath) || !File.Exists(display.FullPath))
                throw new InvalidOperationException("Откройте и сохраните сборку проекта, затем запустите скрипт.");
            string root = Path.GetDirectoryName(FullPath(display.FullPath));
            IntPtr owner = Process.GetCurrentProcess().MainWindowHandle;
            new RenameDialog(new Catalog(session, uf, root), display.Tag).Show(owner);
        }
        catch (Exception ex)
        {
            Native.MessageBoxW(IntPtr.Zero, ex.ToString(), WindowTitle("Ошибка"), 0x10);
        }
    }

    public static int GetUnloadOption(string dummy)
    {
        return (int)Session.LibraryUnloadOption.Immediately;
    }

    private static string FullPath(string path)
    {
        return Path.GetFullPath(path);
    }

    private static bool SamePath(string a, string b)
    {
        if (String.IsNullOrEmpty(a) || String.IsNullOrEmpty(b)) return false;
        return PathComparer.Equals(FullPath(a), FullPath(b));
    }

    private static bool InRoot(string file, string root)
    {
        if (String.IsNullOrEmpty(file) || !Path.IsPathRooted(file)) return false;
        return PathComparer.Equals(Path.GetDirectoryName(FullPath(file)), root);
    }

    // Отдельная проверка имени; пути и расширение пользователь задавать не должен.
    internal static string NormalizeName(string value)
    {
        if (value == null || value.Length == 0)
            throw new InvalidOperationException("Введите новое имя.");
        if (value != value.Trim())
            throw new InvalidOperationException("Уберите пробелы в начале и конце имени.");
        string name = value;
        if (name.EndsWith(".prt", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);
        if (name.Length == 0 || name == "." || name == "..")
            throw new InvalidOperationException("Введите имя файла без пути.");
        if (name.EndsWith(".") || name.EndsWith(" "))
            throw new InvalidOperationException("Имя не должно заканчиваться точкой или пробелом.");
        foreach (char c in name)
        {
            if (Char.IsControl(c) || "<>:\"/\\|?*".IndexOf(c) >= 0)
                throw new InvalidOperationException("В имени недопустимы символы: < > : \" / \\ | ? * и управляющие символы.");
        }
        string device = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (device == "CON" || device == "PRN" || device == "AUX" || device == "NUL" ||
            device == "CLOCK$" || device == "CONIN$" || device == "CONOUT$" ||
            ((device.StartsWith("COM") || device.StartsWith("LPT")) && device.Length == 4 &&
             "123456789¹²³".IndexOf(device[3]) >= 0))
            throw new InvalidOperationException("Это зарезервированное имя Windows. Выберите другое.");
        if (name.Length > 128)
            throw new InvalidOperationException("Для имени компонента используйте не более 128 символов.");
        return name;
    }

    private sealed class PartInfo
    {
        public Part Part;
        public string Path;
        public bool Local;
        public readonly List<LinkInfo> Children = new List<LinkInfo>();
        public string ReadError;
        public string FileName { get { return System.IO.Path.GetFileName(Path); } }
    }

    private sealed class LinkInfo
    {
        public PartInfo Owner;
        public PartInfo Child;
        public Component Component;
        public Tag InstanceTag;
        public string StoredPath;
        public string Name;
        public bool Suppressed;
        public string Problem;
    }

    // Снимок на диске нужен, чтобы не удалить/перезаписать файл, изменённый
    // другим пользователем после открытия окна. Никаких файлов снимка не создаётся.
    private sealed class DiskStamp
    {
        public long Length;
        public DateTime ModifiedUtc;
        public DiskStamp(string path)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists) throw new IOException("Файл отсутствует: " + path);
            Length = file.Length;
            ModifiedUtc = file.LastWriteTimeUtc;
        }
        public void Check(string path)
        {
            DiskStamp now = new DiskStamp(path);
            if (now.Length != Length || now.ModifiedUtc != ModifiedUtc)
                throw new IOException("Файл изменился на диске после чтения проекта:\r\n" + path +
                    "\r\nЗакройте это окно и согласуйте актуальную версию файла в NX.");
        }
    }

    private sealed class RenamePlan
    {
        public PartInfo Target;
        public string OldPath;
        public string NewPath;
        public string NewName;
        public bool FileChange;
        public PartInfo RecoveryOriginal;
        public string RetiredPath
        {
            get { return RecoveryOriginal != null ? RecoveryOriginal.Path : (FileChange ? OldPath : null); }
        }
        public readonly List<LinkInfo> Occurrences = new List<LinkInfo>();
        public readonly List<PartInfo> Parents = new List<PartInfo>();
        public readonly List<PartInfo> SaveOrder = new List<PartInfo>();
    }

    private sealed class Catalog
    {
        public readonly Session Session;
        public readonly UFSession Uf;
        public readonly string Root;
        public readonly List<PartInfo> LocalParts = new List<PartInfo>();
        public readonly List<PartInfo> AllParts = new List<PartInfo>();
        public readonly List<string> Errors = new List<string>();
        private readonly Dictionary<string, DiskStamp> stamps = new Dictionary<string, DiskStamp>(PathComparer);
        private readonly Dictionary<Tag, PartInfo> byTag = new Dictionary<Tag, PartInfo>();
        private readonly Dictionary<string, PartInfo> byPath = new Dictionary<string, PartInfo>(PathComparer);
        private HashSet<string> filesAtScan = new HashSet<string>(PathComparer);
        public bool MustRestart;

        public Catalog(Session session, UFSession uf, string root)
        {
            Session = session; Uf = uf; Root = root;
        }

        private string[] RootFiles()
        {
            List<string> files = new List<string>();
            foreach (string path in Directory.GetFiles(Root, "*", SearchOption.TopDirectoryOnly))
                if (String.Equals(Path.GetExtension(path), ".prt", StringComparison.OrdinalIgnoreCase))
                    files.Add(FullPath(path));
            files.Sort(PathComparer);
            return files.ToArray();
        }

        private Part FindLoaded(string path)
        {
            foreach (BasePart part in Session.Parts)
                if (part is Part && SamePath(part.FullPath, path)) return (Part)part;
            return null;
        }

        private void CheckLoad(PartLoadStatus status, string context)
        {
            if (status == null) return;
            try
            {
                for (int i = 0; i < status.NumberUnloadedParts; i++)
                    Errors.Add(context + ": " + status.GetPartName(i) + " — " + status.GetStatusDescription(i));
            }
            finally { status.Dispose(); }
        }

        public void Load(Action<string> progress)
        {
            if (MustRestart) throw new InvalidOperationException("После сбоя закройте окно и устраните указанную ошибку.");
            // Не обновлять эталон молча при внешнем изменении уже прочитанных файлов.
            foreach (KeyValuePair<string, DiskStamp> pair in stamps) pair.Value.Check(pair.Key);
            Errors.Clear(); LocalParts.Clear(); AllParts.Clear(); byTag.Clear(); byPath.Clear();
            string[] files = RootFiles();
            filesAtScan = new HashSet<string>(files, PathComparer);
            if (files.Length == 0) throw new InvalidOperationException("В корневой папке нет файлов .prt.");
            foreach (string file in files)
                if (!stamps.ContainsKey(file)) stamps.Add(file, new DiskStamp(file));

            LoadOptions options = Session.Parts.LoadOptions;
            LoadOptions.LoadComponents oldComponents = options.ComponentsToLoad;
            bool oldPartial = options.UsePartialLoading;
            bool oldLight = options.UseLightweightRepresentations;
            bool oldFamily = options.GenerateMissingPartFamilyMembers;
            bool oldSubstitution = options.AllowSubstitution;
            try
            {
                options.ComponentsToLoad = LoadOptions.LoadComponents.All;
                options.UsePartialLoading = false;
                options.UseLightweightRepresentations = false;
                options.GenerateMissingPartFamilyMembers = false;
                options.AllowSubstitution = false;
                for (int i = 0; i < files.Length; i++)
                {
                    string file = files[i];
                    progress("Чтение проекта: " + (i + 1) + " / " + files.Length + " — " + Path.GetFileName(file));
                    try
                    {
                        Part part = FindLoaded(file);
                        if (part == null)
                        {
                            PartLoadStatus status = null;
                            BasePart opened;
                            try { opened = Session.Parts.OpenBase(file, out status); }
                            finally { CheckLoad(status, Path.GetFileName(file)); }
                            part = opened as Part;
                        }
                        if (part == null || !SamePath(part.FullPath, file))
                            throw new InvalidOperationException("NX загрузил другой файл или неподдерживаемый тип документа.");
                        CheckLoad(part.LoadFully(), Path.GetFileName(file));
                        if (!part.IsFullyLoaded)
                            throw new InvalidOperationException("Файл загружен не полностью.");
                        stamps[file].Check(file);
                    }
                    catch (Exception ex) { Errors.Add(Path.GetFileName(file) + ": " + ex.Message); }
                }
            }
            finally
            {
                options.AllowSubstitution = oldSubstitution;
                options.GenerateMissingPartFamilyMembers = oldFamily;
                options.UseLightweightRepresentations = oldLight;
                options.UsePartialLoading = oldPartial;
                options.ComponentsToLoad = oldComponents;
            }
            RebuildGraph();
            foreach (string file in files)
                if (!byPath.ContainsKey(file)) Errors.Add("Не загружен файл: " + file);
            HashSet<Tag> checkedParts = new HashSet<Tag>();
            foreach (PartInfo part in LocalParts) CheckBranch(part, checkedParts, new HashSet<Tag>());
        }

        private void RebuildGraph()
        {
            LocalParts.Clear(); AllParts.Clear(); byTag.Clear(); byPath.Clear();
            foreach (BasePart basePart in Session.Parts)
            {
                Part part = basePart as Part;
                if (part == null) continue;
                PartInfo info = new PartInfo();
                info.Part = part;
                info.Path = part.FullPath;
                info.Local = InRoot(info.Path, Root);
                byTag.Add(part.Tag, info);
                if (!String.IsNullOrEmpty(info.Path) && Path.IsPathRooted(info.Path))
                    byPath[FullPath(info.Path)] = info;
                AllParts.Add(info);
                if (info.Local) LocalParts.Add(info);
            }
            foreach (PartInfo info in AllParts)
            {
                try
                {
                    Component root = info.Part.ComponentAssembly.RootComponent;
                    if (root == null) continue;
                    foreach (Component component in root.GetChildren())
                    {
                        LinkInfo link = new LinkInfo();
                        link.Owner = info; link.Component = component;
                        info.Children.Add(link);
                        try
                        {
                            link.Suppressed = component.IsSuppressed;
                            string refset, instance;
                            Uf.Assem.AskComponentData(component.Tag, out link.StoredPath, out refset, out instance,
                                new double[3], new double[9], new double[4, 4]);
                            link.Name = instance;
                            link.InstanceTag = Uf.Assem.AskInstOfPartOcc(component.Tag);
                            if (link.InstanceTag == Tag.Null)
                                throw new InvalidOperationException("Не определён экземпляр компонента: " + instance);
                            Part prototype = component.Prototype as Part;
                            if (prototype != null) byTag.TryGetValue(prototype.Tag, out link.Child);
                            if (link.Child == null)
                                link.Problem = "Компонент не загружен: " + link.Name + " (" + link.StoredPath + ").";
                        }
                        catch (Exception ex) { link.Problem = "Не удалось прочитать компонент: " + ex.Message; }
                    }
                }
                catch (Exception ex) { info.ReadError = ex.Message; }
            }
            LocalParts.Sort(delegate(PartInfo a, PartInfo b) { return PathComparer.Compare(a.FileName, b.FileName); });
        }

        private void CheckBranch(PartInfo part, HashSet<Tag> done, HashSet<Tag> branch)
        {
            if (branch.Contains(part.Part.Tag))
            {
                Errors.Add("Циклическая ссылка в сборке: " + part.Path); return;
            }
            if (!done.Add(part.Part.Tag)) return;
            branch.Add(part.Part.Tag);
            if (!String.IsNullOrEmpty(part.ReadError)) Errors.Add(part.FileName + ": " + part.ReadError);
            foreach (LinkInfo link in part.Children)
            {
                if (!String.IsNullOrEmpty(link.Problem)) Errors.Add(part.FileName + ": " + link.Problem);
                if (link.Child != null) CheckBranch(link.Child, done, branch);
            }
            branch.Remove(part.Part.Tag);
        }

        private void CheckWritable(PartInfo info)
        {
            if (!info.Local) throw new InvalidOperationException("Затронута сборка за пределами корневой папки:\r\n" + info.Path);
            if (!info.Part.IsFullyLoaded) throw new InvalidOperationException("Не полностью загружен файл: " + info.Path);
            if (!File.Exists(info.Path)) throw new IOException("Файл отсутствует: " + info.Path);
            if (info.Part.IsReadOnly || (File.GetAttributes(info.Path) & FileAttributes.ReadOnly) != 0)
                throw new IOException("Файл доступен только для чтения: " + info.Path);
            if ((File.GetAttributes(info.Path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Переименование файлов-ссылок не поддерживается: " + info.Path);
            DiskStamp stamp;
            if (!stamps.TryGetValue(FullPath(info.Path), out stamp))
                throw new IOException("Файл отсутствует в снимке проекта. Обновите дерево: " + info.Path);
            stamp.Check(info.Path);
        }

        public RenamePlan Plan(PartInfo target, string requestedName, PartInfo recoveryOriginal = null)
        {
            if (MustRestart) throw new InvalidOperationException("Операции заблокированы после сбоя. Смотрите сообщение об ошибке.");
            if (target == null) throw new InvalidOperationException("Выберите компонент или сборку в дереве.");
            if (!target.Local) throw new InvalidOperationException("Этот компонент находится вне корневой папки проекта.");
            if (Errors.Count != 0)
                throw new InvalidOperationException("Структура проекта прочитана не полностью. Нажмите «Ошибки чтения».");
            string name = NormalizeName(requestedName);
            RenamePlan plan = new RenamePlan();
            plan.Target = target; plan.OldPath = FullPath(target.Part.FullPath);
            if (!SamePath(target.Path, plan.OldPath))
                throw new InvalidOperationException("Имя файла изменилось в NX. Обновите дерево.");
            plan.NewName = name; plan.NewPath = Path.Combine(Root, name + ".prt");
            plan.RecoveryOriginal = recoveryOriginal;
            if (recoveryOriginal != null && !SamePath(plan.OldPath, plan.NewPath))
                throw new InvalidOperationException("При завершении после сбоя используйте уже созданное новое имя файла.");
            if (plan.NewPath.Length >= 260)
                throw new InvalidOperationException("Полный путь слишком длинный. Сократите имя (путь должен быть короче 260 символов).");
            plan.FileChange = !SamePath(plan.OldPath, plan.NewPath);
            if (!plan.FileChange && !String.Equals(Path.GetFileNameWithoutExtension(plan.OldPath), name, StringComparison.Ordinal))
                throw new InvalidOperationException("Изменение только регистра букв не поддерживается без промежуточного имени.");
            if (plan.FileChange)
            {
                if (File.Exists(plan.NewPath) || Directory.Exists(plan.NewPath))
                    throw new IOException("Такое имя уже занято: " + plan.NewPath);
                foreach (BasePart open in Session.Parts)
                {
                    if (open.Tag == target.Part.Tag) continue;
                    if (String.Equals(Path.GetFileName(open.FullPath), Path.GetFileName(plan.NewPath), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("В NX уже загружен файл с таким именем: " + open.FullPath);
                }
            }
            HashSet<Tag> affected = new HashSet<Tag>();
            affected.Add(target.Part.Tag);
            foreach (PartInfo part in AllParts)
            {
                // При недоступной структуре внешней открытой сборки нельзя доказать,
                // что она не содержит переименовываемый файл.
                if (!String.IsNullOrEmpty(part.ReadError))
                    throw new InvalidOperationException("Не удалось проверить открытую сборку: " + part.Path + "\r\n" + part.ReadError);
                foreach (LinkInfo link in part.Children)
                {
                    if (link.Child == target)
                        plan.Occurrences.Add(link);
                    else if (link.Child == null && MightReferTo(link.StoredPath, plan.OldPath))
                        throw new InvalidOperationException("Не загружено вхождение файла в сборке " + part.FileName + ". Загрузите его в NX.");
                }
            }
            bool changed;
            do
            {
                changed = false;
                foreach (PartInfo parent in AllParts)
                    foreach (LinkInfo link in parent.Children)
                        if (link.Child != null && affected.Contains(link.Child.Part.Tag) && affected.Add(parent.Part.Tag))
                        {
                            plan.Parents.Add(parent); changed = true;
                        }
            } while (changed);
            bool aliasChange = false;
            foreach (LinkInfo link in plan.Occurrences)
                if (!SameInstanceName(ReadInstanceName(link.InstanceTag), name)) aliasChange = true;
            if (!plan.FileChange && !aliasChange && recoveryOriginal == null)
                throw new InvalidOperationException("Имя файла и всех его вхождений уже совпадает с указанным.");
            HashSet<Tag> done = new HashSet<Tag>();
            foreach (PartInfo part in LocalParts)
                OrderForSave(part, affected, done, new HashSet<Tag>(), plan.SaveOrder);
            foreach (PartInfo parent in plan.Parents) CheckWritable(parent);
            CheckWritable(target);
            if (recoveryOriginal != null) CheckRecoveryOriginal(recoveryOriginal, target);
            return plan;
        }

        private static bool MightReferTo(string reference, string target)
        {
            if (String.IsNullOrEmpty(reference)) return true;
            string leaf = Path.GetFileName(reference);
            if (!leaf.EndsWith(".prt", StringComparison.OrdinalIgnoreCase)) leaf += ".prt";
            return String.Equals(leaf, Path.GetFileName(target), StringComparison.OrdinalIgnoreCase);
        }

        private static void OrderForSave(PartInfo part, HashSet<Tag> affected, HashSet<Tag> done,
            HashSet<Tag> branch, List<PartInfo> order)
        {
            if (!affected.Contains(part.Part.Tag)) return;
            if (branch.Contains(part.Part.Tag)) throw new InvalidOperationException("Циклическая структура сборки.");
            if (done.Contains(part.Part.Tag)) return;
            branch.Add(part.Part.Tag);
            foreach (LinkInfo link in part.Children)
                if (link.Child != null) OrderForSave(link.Child, affected, done, branch, order);
            branch.Remove(part.Part.Tag); done.Add(part.Part.Tag); order.Add(part);
        }

        private void CheckSave(PartSaveStatus status, string path)
        {
            if (status == null) throw new IOException("NX не вернул результат сохранения: " + path);
            try
            {
                if (status.NumberUnsavedParts > 0 || status.NumberUnsavedObjects > 0)
                {
                    StringBuilder text = new StringBuilder("NX не подтвердил полное сохранение: " + path);
                    for (int i = 0; i < status.NumberUnsavedParts; i++)
                    {
                        BasePart failed = status.GetPart(i);
                        text.Append("\r\n").Append(failed == null ? "Деталь" : failed.FullPath);
                        text.Append(" — код ").Append(status.GetStatus(i));
                    }
                    if (status.NumberUnsavedObjects > 0)
                        text.Append("\r\nНесохранённых объектов: ").Append(status.NumberUnsavedObjects);
                    throw new IOException(text.ToString());
                }
            }
            finally { status.Dispose(); }
            FileInfo saved = new FileInfo(path);
            if (!saved.Exists || saved.Length == 0) throw new IOException("Не найден сохранённый файл: " + path);
        }

        private static bool SameInstanceName(string actual, string requested)
        {
            // Регистр обозначения в NX не определяет файловую ссылку.
            // Не допускаются усечение, пропуск символов, подстановка другого имени.
            return String.Equals(actual, requested, StringComparison.OrdinalIgnoreCase);
        }

        private string ReadInstanceName(Tag instance)
        {
            if (instance == Tag.Null) throw new InvalidOperationException("Не определён экземпляр компонента.");
            string path, refset, name;
            Uf.Assem.AskComponentData(instance, out path, out refset, out name,
                new double[3], new double[9], new double[4, 4]);
            return name;
        }

        private void VerifyInstanceNames(RenamePlan plan)
        {
            foreach (LinkInfo link in plan.Occurrences)
            {
                string actual = ReadInstanceName(link.InstanceTag);
                if (!SameInstanceName(actual, plan.NewName))
                    throw new IOException("Не подтверждено имя экземпляра в сборке:\r\n" + link.Owner.Path +
                        "\r\nЗадано: " + plan.NewName + "\r\nПолучено из NX: " + actual);
            }
        }

        private void VerifyLinks(RenamePlan plan)
        {
            if (!SamePath(plan.Target.Part.FullPath, plan.NewPath))
                throw new IOException("NX не изменил путь детали на ожидаемый:\r\n" + plan.Target.Part.FullPath);
            foreach (LinkInfo link in plan.Occurrences)
            {
                Part prototype = link.Component.Prototype as Part;
                if (prototype == null || prototype.Tag != plan.Target.Part.Tag || !SamePath(prototype.FullPath, plan.NewPath))
                    throw new IOException("Не подтверждена связь компонента в сборке: " + link.Owner.Path);
            }
            VerifyInstanceNames(plan);
        }

        private void CheckRecoveryOriginal(PartInfo original, PartInfo target)
        {
            if (!SamePath(original.Path, original.Part.FullPath))
                throw new InvalidOperationException("Путь старого файла изменился в NX. Обновите дерево.");
            if (original == target || original.Part.Tag == target.Part.Tag || SamePath(original.Path, target.Part.FullPath))
                throw new InvalidOperationException("Старый и новый файл должны различаться.");
            CheckWritable(original);
            if (original.Part.IsModified)
                throw new InvalidOperationException("В выбранном старом файле есть несохранённые правки. Его удаление запрещено.");
            Part work = Session.Parts.Work, display = Session.Parts.Display;
            if ((work != null && work.Tag == original.Part.Tag) || (display != null && display.Tag == original.Part.Tag))
                throw new InvalidOperationException("Старый файл сейчас является рабочим или отображаемым. Сделайте рабочей основную сборку.");
            foreach (PartInfo part in AllParts)
            {
                if (!String.IsNullOrEmpty(part.ReadError))
                    throw new InvalidOperationException("Не удалось проверить сборку: " + part.Path);
                foreach (LinkInfo link in part.Children)
                {
                    Part prototype = link.Component.Prototype as Part;
                    if ((prototype != null && (prototype.Tag == original.Part.Tag || SamePath(prototype.FullPath, original.Path))) ||
                        (prototype == null && MightReferTo(link.StoredPath, original.Path)))
                        throw new InvalidOperationException("На старый файл ещё ссылается сборка:\r\n" + part.Path +
                            "\r\nЕго удаление заблокировано. Завершайте операцию в исходном открытом сеансе NX.");
                }
            }
        }

        public string Execute(RenamePlan requested, Action<string> progress)
        {
            // Повторная проверка непосредственно перед первым изменением.
            RenamePlan plan = Plan(requested.Target, requested.NewName, requested.RecoveryOriginal);
            if (!filesAtScan.SetEquals(RootFiles()))
                throw new IOException("Состав .prt в папке изменился. Нажмите «Обновить дерево» перед переименованием.");
            foreach (KeyValuePair<string, DiskStamp> pair in stamps) pair.Value.Check(pair.Key);
            Dictionary<Tag, string> originalNames = new Dictionary<Tag, string>();
            foreach (LinkInfo link in plan.Occurrences)
                if (!originalNames.ContainsKey(link.InstanceTag)) originalNames.Add(link.InstanceTag, ReadInstanceName(link.InstanceTag));
            List<string> savedParts = new List<string>();
            string stage = "Переименование вхождений";
            bool namesMayHaveChanged = false;
            bool filesMayHaveChanged = false;
            try
            {
                progress(stage + "…");
                // UF_ASSEM_rename_instance меняет именно имя экземпляра сборки.
                // NXObject.Name и подпись навигатора могут содержать другое значение.
                foreach (KeyValuePair<Tag, string> pair in originalNames)
                    if (!SameInstanceName(pair.Value, plan.NewName))
                    {
                        namesMayHaveChanged = true;
                        Uf.Assem.RenameInstance(pair.Key, plan.NewName);
                    }
                VerifyInstanceNames(plan);
                if (plan.FileChange)
                {
                    stage = "Сохранение файла с новым именем"; progress(stage + "…");
                    stamps[plan.OldPath].Check(plan.OldPath);
                    if (File.Exists(plan.NewPath) || Directory.Exists(plan.NewPath))
                        throw new IOException("Новое имя стало занято: " + plan.NewPath);
                    filesMayHaveChanged = true;
                    CheckSave(plan.Target.Part.SaveAs(plan.NewPath), plan.NewPath);
                }
                VerifyLinks(plan);
                stage = "Сохранение сборок";
                foreach (PartInfo part in plan.SaveOrder)
                {
                    if (part == plan.Target && plan.FileChange) continue;
                    progress(stage + ": " + part.FileName);
                    stamps[FullPath(part.Part.FullPath)].Check(part.Part.FullPath);
                    filesMayHaveChanged = true;
                    CheckSave(part.Part.Save(BasePart.SaveComponents.False, BasePart.CloseAfterSave.False), part.Part.FullPath);
                    savedParts.Add(part.Part.FullPath);
                    stamps[FullPath(part.Part.FullPath)] = new DiskStamp(part.Part.FullPath);
                }
                VerifyLinks(plan);
                string retired = plan.RetiredPath;
                if (retired != null)
                {
                    stage = "Проверка старого файла"; progress(stage + "…");
                    HashSet<string> expected = new HashSet<string>(filesAtScan, PathComparer);
                    if (plan.FileChange) expected.Add(plan.NewPath);
                    if (!expected.SetEquals(RootFiles()))
                        throw new IOException("Состав папки изменился во время операции. Старый файл сохранён.");
                    if (plan.RecoveryOriginal != null)
                    {
                        CheckRecoveryOriginal(plan.RecoveryOriginal, plan.Target);
                        Tag originalTag = plan.RecoveryOriginal.Part.Tag;
                        // Не закрывать зависимые детали и не отбрасывать правки.
                        plan.RecoveryOriginal.Part.Close(BasePart.CloseWholeTree.False, BasePart.CloseModified.DontCloseModified, null);
                        foreach (BasePart open in Session.Parts)
                            if (open.Tag == originalTag || SamePath(open.FullPath, retired))
                                throw new IOException("NX не освободил старый файл; удаление отменено: " + retired);
                    }
                    stage = "Удаление старого имени"; progress(stage + "…");
                    stamps[retired].Check(retired);
                    // Только точный старый путь из плана, после всех сохранений.
                    File.Delete(retired);
                    if (File.Exists(retired)) throw new IOException("Старый файл остался на диске: " + retired);
                    stamps.Remove(retired);
                    stamps[plan.NewPath] = new DiskStamp(plan.NewPath);
                }
                stage = "Обновление дерева после сохранения";
                filesAtScan = new HashSet<string>(RootFiles(), PathComparer);
                RebuildGraph();
            }
            catch (Exception ex)
            {
                if (!filesMayHaveChanged)
                {
                    // До первой записи .prt можно вернуть только изменённые имена.
                    List<string> rollbackErrors = new List<string>();
                    if (namesMayHaveChanged)
                        foreach (KeyValuePair<Tag, string> pair in originalNames)
                            try
                            {
                                if (!SameInstanceName(ReadInstanceName(pair.Key), pair.Value))
                                    Uf.Assem.RenameInstance(pair.Key, pair.Value);
                                if (!SameInstanceName(ReadInstanceName(pair.Key), pair.Value))
                                    throw new IOException("NX не вернул прежнее имя: " + pair.Value);
                            }
                            catch (Exception restore) { rollbackErrors.Add(restore.Message); }
                    if (rollbackErrors.Count > 0) MustRestart = true;
                    throw new InvalidOperationException("Операция остановлена. Этап: " + stage + ".\r\n\r\n" + ex.Message +
                        "\r\n\r\nВ этой попытке файлы не сохранялись и не удалялись." +
                        (rollbackErrors.Count == 0 ? " Изменённые имена вхождений возвращены." :
                        "\r\nНе удалось вернуть все имена:\r\n" + String.Join("\r\n", rollbackErrors.ToArray())), ex);
                }
                MustRestart = true;
                StringBuilder result = new StringBuilder();
                result.Append("Операция остановлена. Этап: ").Append(stage).Append(".\r\n\r\n").Append(ex.Message);
                result.Append("\r\n\r\nСостояние файлов:");
                HashSet<string> paths = new HashSet<string>(PathComparer);
                paths.Add(plan.OldPath); paths.Add(plan.NewPath);
                if (plan.RetiredPath != null) paths.Add(plan.RetiredPath);
                foreach (string path in paths) result.Append("\r\n").Append(path)
                    .Append(File.Exists(path) ? " — существует" : " — отсутствует");
                result.Append("\r\n\r\nПуть детали в текущем сеансе NX:\r\n").Append(plan.Target.Part.FullPath);
                if (savedParts.Count > 0)
                    result.Append("\r\n\r\nNX подтвердил сохранение:\r\n").Append(String.Join("\r\n", savedParts.ToArray()));
                result.Append("\r\n\r\nФайловый откат не выполнялся. Сохраните сеанс NX открытым. " +
                    "Не удаляйте оставшиеся .prt вручную. После устранения причины запустите журнал снова: " +
                    "выберите новый файл и «Завершить после сбоя», если старый файл ещё существует.");
                throw new InvalidOperationException(result.ToString(), ex);
            }
            return (plan.RecoveryOriginal != null ? "Прерванное переименование завершено:\r\n" + plan.NewPath :
                plan.FileChange ? "Файл и компоненты переименованы:\r\n" + plan.NewPath : "Имена компонентов обновлены.") +
                "\r\nСохранено затронутых сборок: " + plan.Parents.Count + "." +
                (plan.RetiredPath == null ? "" : "\r\nСтарый файл удалён:\r\n" + plan.RetiredPath);
        }
    }


    // Интерфейс использует только встроенные user32/comctl32/gdi32 Windows.
    // Шаблон диалога и подписи дерева находятся в памяти; файлов .dlx/DLL нет.
    // Все вызовы NX выполняются в том же потоке, в котором запущен журнал.
    private abstract class NativeDialog
    {
        protected IntPtr Handle;
        protected int Dpi = 96;
        private IntPtr font;
        private bool ownsFont;
        private readonly List<IntPtr> controls = new List<IntPtr>();
        private readonly List<ChildPlacement> layoutMoves = new List<ChildPlacement>();
        private bool layoutInProgress;
        private struct ChildPlacement
        {
            public IntPtr Window;
            public int X, Y, Width, Height;
        }
        private Native.DialogProc callback;
        private Exception callbackError;
        protected abstract string Suffix { get; }
        protected virtual int InitialWidth { get { return 1140; } }
        protected virtual int InitialHeight { get { return 780; } }
        protected virtual int MinimumWidth { get { return 950; } }
        protected virtual int MinimumHeight { get { return 670; } }
        protected virtual bool CanClose { get { return true; } }

        public void Show(IntPtr owner)
        {
            Native.InitControls init = new Native.InitControls();
            init.Size = (uint)Marshal.SizeOf(typeof(Native.InitControls));
            init.Classes = 0x00000002; // ICC_TREEVIEW_CLASSES
            if (!Native.InitCommonControlsEx(ref init))
                throw new InvalidOperationException("Не удалось создать системные элементы окна Windows.");
            // DLGTEMPLATE: без ресурсов на диске, без дополнительных контролов.
            IntPtr template = Marshal.AllocHGlobal(24);
            callback = DialogProcedure;
            try
            {
                for (int i = 0; i < 24; i++) Marshal.WriteByte(template, i, 0);
                // WS_POPUP | CAPTION | SYSMENU | THICKFRAME | MAXIMIZEBOX | CLIPCHILDREN
                Marshal.WriteInt32(template, 0, unchecked((int)0x82CD0000));
                // WS_EX_DLGMODALFRAME | WS_EX_COMPOSITED: общий буфер для окна и его элементов.
                Marshal.WriteInt32(template, 4, 0x02000001);
                Marshal.WriteInt16(template, 14, 400);
                Marshal.WriteInt16(template, 16, 300);
                IntPtr result = Native.DialogBoxIndirectParamW(Native.GetModuleHandleW(null), template,
                    owner, callback, IntPtr.Zero);
                int error = Marshal.GetLastWin32Error();
                if (callbackError != null) throw new InvalidOperationException(
                    "Не удалось выполнить действие в окне:\r\n" + callbackError, callbackError);
                if (result == new IntPtr(-1))
                    throw new InvalidOperationException("Не удалось открыть окно. Код Windows: " + error);
            }
            finally
            {
                Marshal.FreeHGlobal(template);
                if (ownsFont && font != IntPtr.Zero) Native.DeleteObject(font);
                font = IntPtr.Zero;
                Handle = IntPtr.Zero;
                GC.KeepAlive(callback);
            }
        }

        protected int U(int value) { return (int)Math.Round(value * Dpi / 96.0); }
        protected abstract void Build();
        protected abstract void Layout(int width, int height);
        protected virtual void Started() { }
        protected virtual void Command(int id, int code, IntPtr control) { }
        protected virtual void Notify(IntPtr data) { }
        protected virtual IntPtr InitialFocus { get { return IntPtr.Zero; } }
        protected virtual void InterfaceFailed() { }
        protected virtual void DpiChanged() { }
        protected virtual bool ExtraMessage(uint message, IntPtr wParam, IntPtr lParam) { return false; }

        private IntPtr DialogProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (Handle != IntPtr.Zero && ExtraMessage(message, wParam, lParam)) return new IntPtr(1);
                switch (message)
                {
                    case 0x0110: // WM_INITDIALOG
                        Handle = window;
                        Native.SetWindowTextW(Handle, WindowTitle(Suffix));
                        try { Dpi = Math.Max(96, (int)Native.GetDpiForWindow(Handle)); }
                        catch (EntryPointNotFoundException) { Dpi = 96; }
                        SetFont();
                        Build();
                        CenterWindow();
                        Relayout();
                        Native.PostMessageW(Handle, 0x8001, IntPtr.Zero, IntPtr.Zero);
                        if (InitialFocus != IntPtr.Zero)
                        {
                            Native.SetFocus(InitialFocus);
                            return IntPtr.Zero;
                        }
                        return new IntPtr(1);
                    case 0x8001:
                        Started(); return new IntPtr(1);
                    case 0x0005: // WM_SIZE
                        if (Handle != IntPtr.Zero && wParam.ToInt64() != 1) Relayout();
                        return new IntPtr(1);
                    case 0x0232: // WM_EXITSIZEMOVE: финальный кадр после изменения размера окна.
                        RepaintContents(); return new IntPtr(1);
                    case 0x0024: // WM_GETMINMAXINFO
                        Native.MinMaxInfo limits = (Native.MinMaxInfo)Marshal.PtrToStructure(lParam, typeof(Native.MinMaxInfo));
                        Native.Rect work = WorkArea(window);
                        limits.MinTrack.X = Math.Min(U(MinimumWidth), work.Right - work.Left);
                        limits.MinTrack.Y = Math.Min(U(MinimumHeight), work.Bottom - work.Top);
                        Marshal.StructureToPtr(limits, lParam, false);
                        return new IntPtr(1);
                    case 0x02E0: // WM_DPICHANGED: меняются только собственные элементы.
                        Dpi = (int)(wParam.ToInt64() & 0xFFFF);
                        if (Dpi < 96) Dpi = 96;
                        SetFont();
                        Native.Rect area = (Native.Rect)Marshal.PtrToStructure(lParam, typeof(Native.Rect));
                        Native.SetWindowPos(Handle, IntPtr.Zero, area.Left, area.Top,
                            area.Right - area.Left, area.Bottom - area.Top, 0x0014);
                        Relayout();
                        DpiChanged();
                        return new IntPtr(1);
                    case 0x0055: // WM_NOTIFYFORMAT / NFR_UNICODE
                        Native.SetDialogResult(window, new IntPtr(2));
                        return new IntPtr(1);
                    case 0x004E: // WM_NOTIFY
                        Notify(lParam); return IntPtr.Zero;
                    case 0x0111: // WM_COMMAND
                        int id = (int)(wParam.ToInt64() & 0xFFFF);
                        int code = (int)((wParam.ToInt64() >> 16) & 0xFFFF);
                        if (id == 2 && code == 0)
                        {
                            if (CanClose) Native.EndDialog(window, new IntPtr(1));
                        }
                        else Command(id, code, lParam);
                        return new IntPtr(1);
                    case 0x0010: // WM_CLOSE; Windows ведёт модальный цикл и возвращает фокус NX.
                        if (CanClose) Native.EndDialog(window, new IntPtr(1));
                        return new IntPtr(1);
                }
            }
            catch (Exception ex)
            {
                // Исключения не должны пересекать границу unmanaged callback.
                if (callbackError == null) callbackError = ex;
                InterfaceFailed();
                if (CanClose) Native.EndDialog(window, new IntPtr(1));
                else Native.PostMessageW(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                return new IntPtr(1);
            }
            return IntPtr.Zero;
        }

        private void SetFont()
        {
            IntPtr old = font;
            bool deleteOld = ownsFont;
            font = Native.CreateFontW(-U(14), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
            ownsFont = font != IntPtr.Zero;
            if (!ownsFont) font = Native.GetStockObject(17); // DEFAULT_GUI_FONT
            foreach (IntPtr child in controls)
                Native.SendMessageW(child, 0x0030, font, new IntPtr(1)); // WM_SETFONT
            if (deleteOld && old != IntPtr.Zero) Native.DeleteObject(old);
        }

        private static Native.Rect WorkArea(IntPtr window)
        {
            Native.MonitorInfo info = new Native.MonitorInfo();
            info.Size = (uint)Marshal.SizeOf(typeof(Native.MonitorInfo));
            if (Native.GetMonitorInfoW(Native.MonitorFromWindow(window, 2), ref info)) return info.Work;
            Native.Rect result = new Native.Rect();
            result.Right = Native.GetSystemMetrics(0); result.Bottom = Native.GetSystemMetrics(1);
            return result;
        }

        private void CenterWindow()
        {
            Native.Rect work = WorkArea(Handle);
            int width = Math.Min(U(InitialWidth), work.Right - work.Left);
            int height = Math.Min(U(InitialHeight), work.Bottom - work.Top);
            Native.SetWindowPos(Handle, IntPtr.Zero, work.Left + (work.Right - work.Left - width) / 2,
                work.Top + (work.Bottom - work.Top - height) / 2, width, height, 0x0014);
        }

        protected void Relayout()
        {
            if (Handle == IntPtr.Zero || layoutInProgress) return;
            Native.Rect rect;
            if (!Native.GetClientRect(Handle, out rect) || rect.Right <= 0 || rect.Bottom <= 0) return;
            layoutInProgress = true;
            layoutMoves.Clear();
            try
            {
                // Сначала рассчитываем ВСЕ положения, затем меняем их без промежуточной отрисовки.
                Layout(rect.Right, rect.Bottom);
                ApplyLayoutMoves();
            }
            finally
            {
                layoutMoves.Clear();
                try { RepaintContents(); }
                finally { layoutInProgress = false; }
            }
        }

        private void ApplyLayoutMoves()
        {
            if (layoutMoves.Count == 0) return;
            // NOZORDER | NOACTIVATE | NOREDRAW | NOCOPYBITS:
            // не переносим старые пиксели разделителя и текста на новые позиции.
            const uint flags = 0x011C;
            IntPtr batch = Native.BeginDeferWindowPos(layoutMoves.Count);
            if (batch != IntPtr.Zero)
            {
                foreach (ChildPlacement move in layoutMoves)
                {
                    batch = Native.DeferWindowPos(batch, move.Window, IntPtr.Zero,
                        move.X, move.Y, move.Width, move.Height, flags);
                    if (batch == IntPtr.Zero) break;
                }
                if (batch != IntPtr.Zero && Native.EndDeferWindowPos(batch)) return;
                // После отказа DeferWindowPos его дескриптор больше не используется.
            }
            // При отказе пакетного API повторяем весь набор без перерисовки между элементами.
            foreach (ChildPlacement move in layoutMoves)
                Native.SetWindowPos(move.Window, IntPtr.Zero, move.X, move.Y, move.Width, move.Height, flags);
        }

        protected void RepaintContents()
        {
            // Включая фон, рамки и дочерние элементы: очищаются и прежние позиции разделителя.
            // RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW | RDW_FRAME.
            if (Handle != IntPtr.Zero) Native.RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x0585);
        }

        // Только при первом подборе ширины: окно не выходит за рабочую область монитора.
        protected void EnsureClientWidth(int requested)
        {
            Native.Rect client, outer;
            if (!Native.GetClientRect(Handle, out client) || !Native.GetWindowRect(Handle, out outer)) return;
            Native.Rect work = WorkArea(Handle);
            int oldWidth = outer.Right - outer.Left;
            int width = Math.Min(work.Right - work.Left, requested + oldWidth - client.Right);
            if (width <= oldWidth) return;
            int x = Math.Max(work.Left, Math.Min(outer.Left - (width - oldWidth) / 2, work.Right - width));
            Native.SetWindowPos(Handle, IntPtr.Zero, x, outer.Top, width, outer.Bottom - outer.Top, 0x0014);
        }

        protected IntPtr Control(string type, string text, uint style, int id, uint extended)
        {
            // WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS.
            IntPtr child = Native.CreateWindowExW(extended, type, text, 0x54000000 | style,
                0, 0, 1, 1, Handle, new IntPtr(id), Native.GetModuleHandleW(null), IntPtr.Zero);
            if (child == IntPtr.Zero)
                throw new InvalidOperationException("Не удалось создать элемент окна " + type +
                    ". Код Windows: " + Marshal.GetLastWin32Error());
            controls.Add(child);
            Native.SendMessageW(child, 0x0030, font, new IntPtr(1));
            return child;
        }

        protected IntPtr Label(string text) { return Control("STATIC", text, 0x80, 0, 0); } // SS_NOPREFIX
        protected IntPtr Button(string text, int id) { return Control("BUTTON", text, 0x00010000, id, 0); }
        protected IntPtr ReadBox(bool multiline, int id, bool tab)
        {
            uint style = 0x0800u | (tab ? 0x00010000u : 0u); // ES_READONLY
            if (multiline) style |= 0x00200044; // WS_VSCROLL | ES_MULTILINE | ES_AUTOVSCROLL
            else style |= 0x80; // ES_AUTOHSCROLL
            IntPtr result = Control("EDIT", "", style, id, 0x200);
            Native.SendMessageW(result, 0x00C5, new IntPtr(0x7FFFFFFE), IntPtr.Zero); // EM_SETLIMITTEXT
            return result;
        }
        protected void Move(IntPtr child, int x, int y, int width, int height)
        {
            if (child == IntPtr.Zero) return;
            ChildPlacement move = new ChildPlacement { Window = child, X = x, Y = y,
                Width = Math.Max(1, width), Height = Math.Max(1, height) };
            if (layoutInProgress) layoutMoves.Add(move);
            else
            {
                Native.SetWindowPos(child, IntPtr.Zero, x, y, move.Width, move.Height, 0x011C);
                RepaintContents();
            }
        }
        protected void Text(IntPtr child, string value) { Native.SetWindowTextW(child, value ?? ""); }
        protected string Text(IntPtr child)
        {
            StringBuilder value = new StringBuilder(Native.GetWindowTextLengthW(child) + 1);
            Native.GetWindowTextW(child, value, value.Capacity);
            return value.ToString();
        }
        protected void Enabled(IntPtr child, bool value) { Native.EnableWindow(child, value); }
    }

    private sealed class DetailsDialog : NativeDialog
    {
        private readonly string suffix;
        private readonly string message;
        private IntPtr text, close;
        public DetailsDialog(string name, string value) { suffix = name; message = value; }
        protected override string Suffix { get { return suffix; } }
        protected override int InitialWidth { get { return 820; } }
        protected override int InitialHeight { get { return 510; } }
        protected override int MinimumWidth { get { return 520; } }
        protected override int MinimumHeight { get { return 300; } }
        protected override IntPtr InitialFocus { get { return close; } }
        protected override void Build()
        {
            text = ReadBox(true, 201, true); Text(text, message);
            close = Button("ОК", 2);
            Native.SendMessageW(Handle, 0x0401, new IntPtr(2), IntPtr.Zero); // DM_SETDEFID
        }
        protected override void Layout(int width, int height)
        {
            int m = U(14), h = U(32);
            Move(text, m, m, width - 2 * m, height - 3 * m - h);
            Move(close, width - m - U(100), height - m - h, U(100), h);
        }
    }

    private sealed class NodeInfo
    {
        public PartInfo Part;
        public LinkInfo Link;
        public NodeInfo Parent;
        public IntPtr Handle;
        public bool ChildrenLoaded;
        public string Label;
        public int Depth;
    }

    private sealed class RecoveryDialog : NativeDialog
    {
        private readonly Catalog catalog;
        private readonly PartInfo target;
        private readonly List<PartInfo> candidates = new List<PartInfo>();
        private IntPtr info, targetLabel, targetPath, oldLabel, choice, preview, state, finish, close;
        private RenamePlan plan;
        private bool busy;
        public bool Completed;
        public string Result;
        public RecoveryDialog(Catalog data, PartInfo selected) { catalog = data; target = selected; }
        protected override string Suffix { get { return "Завершить после сбоя"; } }
        protected override int InitialWidth { get { return 930; } }
        protected override int InitialHeight { get { return 620; } }
        protected override int MinimumWidth { get { return 760; } }
        protected override int MinimumHeight { get { return 560; } }
        protected override bool CanClose { get { return !busy; } }
        protected override IntPtr InitialFocus { get { return choice; } }
        protected override void InterfaceFailed() { catalog.MustRestart = true; }
        protected override void Build()
        {
            info = Label("Выберите прежний файл ЭТОГО ЖЕ компонента. После сохранения ссылок выбранный старый .prt будет удалён.\r\nЗавершайте операцию в том сеансе NX, где появилось новое имя.");
            targetLabel = Label("Уже созданный файл с новым именем — останется в проекте");
            targetPath = ReadBox(true, 211, true); Text(targetPath, target.Path);
            oldLabel = Label("Исходный файл с прежним именем — будет удалён после проверок");
            choice = Control("COMBOBOX", "", 0x00210003, 212, 0); // CBS_DROPDOWNLIST
            foreach (PartInfo item in catalog.LocalParts)
                if (item != target)
                {
                    candidates.Add(item);
                    Native.SendTextMessageW(choice, 0x0143, IntPtr.Zero, item.FileName); // CB_ADDSTRING
                }
            // Старый файл всегда выбирает пользователь; похожее имя не доказательство.
            Native.SendMessageW(choice, 0x014E, new IntPtr(-1), IntPtr.Zero);
            preview = ReadBox(true, 213, true);
            state = Label("Выберите прежний файл. Новое имя повторно не создаётся.");
            finish = Button("Завершить переименование", 214); Enabled(finish, false);
            close = Button("Закрыть", 2);
            Native.SendMessageW(Handle, 0x0401, new IntPtr(214), IntPtr.Zero);
        }
        protected override void Layout(int width, int height)
        {
            if (info == IntPtr.Zero) return;
            int m = U(14), w = width - 2 * m;
            Move(info, m, m, w, U(56));
            Move(targetLabel, m, U(76), w, U(22));
            Move(targetPath, m, U(100), w, U(48));
            Move(oldLabel, m, U(160), w, U(22));
            Move(choice, m, U(186), w, U(230));
            int buttonsY = height - m - U(32), stateY = buttonsY - U(86);
            Move(preview, m, U(226), w, stateY - U(234));
            Move(state, m, stateY, w, U(78));
            Move(close, width - m - U(100), buttonsY, U(100), U(32));
            Move(finish, width - m - U(100) - U(12) - U(260), buttonsY, U(260), U(32));
        }
        protected override void Command(int id, int code, IntPtr control)
        {
            if (busy) return;
            if (id == 212 && code == 1) UpdateRecovery(); // CBN_SELCHANGE
            else if (id == 214 && code == 0 && plan != null)
            {
                busy = true; Enabled(finish, false); Enabled(choice, false); Enabled(close, false);
                try
                {
                    Result = catalog.Execute(plan, delegate(string value) { Text(state, value); Native.UpdateWindow(state); });
                    Completed = true;
                    Native.EndDialog(Handle, new IntPtr(1));
                }
                catch (Exception ex) { new DetailsDialog("Ошибка завершения", ex.Message).Show(Handle); }
                finally
                {
                    busy = false;
                    if (!Completed)
                    {
                        Enabled(close, true); Enabled(choice, !catalog.MustRestart);
                        UpdateRecovery();
                    }
                }
            }
        }
        private void UpdateRecovery()
        {
            plan = null; Enabled(finish, false); Text(preview, "");
            try
            {
                int index = Native.SendMessageW(choice, 0x0147, IntPtr.Zero, IntPtr.Zero).ToInt32();
                if (index < 0 || index >= candidates.Count) throw new InvalidOperationException("Выберите прежний файл.");
                plan = catalog.Plan(target, Path.GetFileNameWithoutExtension(target.Path), candidates[index]);
                StringBuilder text = new StringBuilder("Будут сохранены:\r\n");
                foreach (PartInfo part in plan.SaveOrder)
                    text.Append(part.Path).Append(part.Part.IsModified ? "  [есть несохранённые правки]" : "").Append("\r\n");
                text.Append("\r\nОстанется:\r\n").Append(plan.NewPath);
                text.Append("\r\n\r\nПосле сохранения будет удалён:\r\n").Append(plan.RetiredPath);
                Text(preview, text.ToString());
                Text(state, "Проверьте пару старого и нового имён. Нажмите «Завершить переименование».");
                Enabled(finish, true);
            }
            catch (Exception ex) { plan = null; Text(state, ex.Message); }
        }
    }

    private sealed class RenameDialog : NativeDialog
    {
        private readonly Catalog catalog;
        private readonly Tag initialPart;
        private readonly Dictionary<IntPtr, NodeInfo> nodes = new Dictionary<IntPtr, NodeInfo>();
        private IntPtr rootLabel, rootPath, treeLabel, tree, divider;
        private IntPtr currentLabel, selectedPath, nameLabel, newName;
        private IntPtr pathLabel, futurePath, validation, saveLabel, preview, note;
        private IntPtr apply, reload, errors, recover, close, status;
        private bool busy;
        private bool changingSelection;
        private double treeWidthDip = 520;
        private bool manualTreeWidth, draggingDivider, fitQueued, initialFitDone;
        private int dragOffset;
        private Native.Rect dividerArea;
        private IntPtr sizeCursor;
        private RenamePlan currentPlan;
        private const int ApplyId = 101, ReloadId = 102, ErrorsId = 103, NameId = 104, RecoverId = 105;
        public RenameDialog(Catalog data, Tag displayPart) { catalog = data; initialPart = displayPart; }
        protected override string Suffix { get { return ""; } }
        protected override bool CanClose { get { return !busy; } }
        protected override IntPtr InitialFocus { get { return tree; } }
        protected override void InterfaceFailed() { catalog.MustRestart = true; }

        protected override void Build()
        {
            rootLabel = Label("Корневая папка проекта (без подпапок)");
            rootPath = ReadBox(false, 110, false); Text(rootPath, catalog.Root);
            treeLabel = Label("Сборки, подсборки и детали");
            // TVS_HASBUTTONS | HASLINES | LINESATROOT | SHOWSELALWAYS, WS_TABSTOP
            tree = Control("SysTreeView32", "", 0x00010027, 111, 0x200);
            Native.SendMessageW(tree, 0x2005, new IntPtr(1), IntPtr.Zero); // CCM_SETUNICODEFORMAT
            // SS_ETCHEDVERT, без SS_NOTIFY: мышь проходит к родительскому диалогу.
            divider = Control("STATIC", "", 0x0011, 0, 0);
            sizeCursor = Native.LoadCursorW(IntPtr.Zero, new IntPtr(32644)); // IDC_SIZEWE (системный)
            currentLabel = Label("Текущий файл выбранного компонента");
            selectedPath = ReadBox(true, 112, true);
            nameLabel = Label("Новое имя компонента и файла (без .prt)");
            newName = Control("EDIT", "", 0x00010080, NameId, 0x200);
            Native.SendMessageW(newName, 0x00C5, new IntPtr(256), IntPtr.Zero);
            pathLabel = Label("Новое имя на диске");
            futurePath = ReadBox(true, 113, true);
            validation = Label("");
            saveLabel = Label("Будут сохранены");
            preview = ReadBox(true, 114, true);
            note = Label("Одно имя применяется ко всем вхождениям этого файла.\r\nТекущие правки перечисленных деталей и сборок тоже сохранятся.");
            recover = Button("Завершить после сбоя", RecoverId); Enabled(recover, false);
            errors = Button("Ошибки чтения", ErrorsId);
            reload = Button("Обновить дерево", ReloadId);
            apply = Button("Переименовать и сохранить", ApplyId);
            close = Button("Закрыть", 2);
            status = Label("Чтение проекта…");
            Enabled(apply, false); Enabled(errors, false);
            Native.SendMessageW(Handle, 0x0401, new IntPtr(ApplyId), IntPtr.Zero);
        }

        protected override void Layout(int width, int height)
        {
            if (tree == IntPtr.Zero) return;
            int m = U(14), gap = U(12), line = U(22), buttonH = U(32);
            Move(rootLabel, m, m, width - 2 * m, line);
            Move(rootPath, m, m + line, width - 2 * m, U(28));
            int top = m + line + U(28) + gap;
            int buttonY = height - m - U(24) - U(8) - buttonH;
            int bottom = buttonY - gap;
            int leftWidth = TreeWidthFor(width);
            int rightX = m + leftWidth + gap, rightWidth = width - m - rightX;
            dividerArea = new Native.Rect { Left = m + leftWidth, Top = top, Right = rightX, Bottom = bottom };
            Move(divider, dividerArea.Left + (gap - U(2)) / 2, top, U(2), bottom - top);
            Move(treeLabel, m, top, leftWidth, line);
            Move(tree, m, top + line, leftWidth, bottom - top - line);
            int y = top;
            Move(currentLabel, rightX, y, rightWidth, line); y += line;
            Move(selectedPath, rightX, y, rightWidth, U(48)); y += U(58);
            Move(nameLabel, rightX, y, rightWidth, line); y += line;
            Move(newName, rightX, y, rightWidth, U(28)); y += U(38);
            Move(pathLabel, rightX, y, rightWidth, line); y += line;
            Move(futurePath, rightX, y, rightWidth, U(48)); y += U(56);
            Move(validation, rightX, y, rightWidth, U(52)); y += U(58);
            Move(saveLabel, rightX, y, rightWidth, line); y += line;
            int noteH = U(50);
            Move(preview, rightX, y, rightWidth, bottom - y - noteH - U(8));
            Move(note, rightX, bottom - noteH, rightWidth, noteH);
            int x = width - m;
            x -= U(100); Move(close, x, buttonY, U(100), buttonH);
            x -= U(250) + U(8); Move(apply, x, buttonY, U(250), buttonH);
            x -= U(155) + U(8); Move(reload, x, buttonY, U(155), buttonH);
            x -= U(145) + U(8); Move(errors, x, buttonY, U(145), buttonH);
            x -= U(210) + U(8); Move(recover, x, buttonY, U(210), buttonH);
            Move(status, m, height - m - U(24), width - 2 * m, U(24));
        }

        private int TreeWidthFor(int clientWidth)
        {
            int available = Math.Max(2, clientWidth - 2 * U(14) - U(12));
            int maximum = Math.Max(1, available - U(430));
            int minimum = Math.Min(U(280), maximum);
            return Math.Max(minimum, Math.Min((int)Math.Round(treeWidthDip * Dpi / 96.0), maximum));
        }

        private bool OnDivider(int x, int y)
        {
            return x >= dividerArea.Left && x < dividerArea.Right &&
                y >= dividerArea.Top && y < dividerArea.Bottom;
        }

        protected override bool ExtraMessage(uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == 0x8002) // Подбор после завершения заполнения/раскрытия дерева.
            {
                fitQueued = false; FitTreeWidth(); return true;
            }
            if (message == 0x0020 && !busy) // WM_SETCURSOR
            {
                Native.Point point;
                if (sizeCursor != IntPtr.Zero && Native.GetCursorPos(out point) &&
                    Native.ScreenToClient(Handle, ref point) &&
                    (draggingDivider || OnDivider(point.X, point.Y)))
                {
                    Native.SetCursor(sizeCursor);
                    Native.SetDialogResult(Handle, new IntPtr(1));
                    return true;
                }
            }
            else if (message == 0x0201 && !busy) // WM_LBUTTONDOWN
            {
                int x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                int y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                if (OnDivider(x, y))
                {
                    dragOffset = x - dividerArea.Left;
                    Native.SetCapture(Handle);
                    draggingDivider = Native.GetCapture() == Handle;
                    if (draggingDivider)
                    {
                        manualTreeWidth = true;
                        treeWidthDip = (dividerArea.Left - U(14)) * 96.0 / Dpi;
                        if (sizeCursor != IntPtr.Zero) Native.SetCursor(sizeCursor);
                    }
                    return true;
                }
            }
            else if (message == 0x0200 && draggingDivider) // WM_MOUSEMOVE
            {
                if ((wParam.ToInt64() & 1) == 0) StopDragging(); // Потерянное отпускание кнопки.
                else DragDivider(unchecked((short)(lParam.ToInt64() & 0xFFFF)));
                return true;
            }
            else if (message == 0x0202 && draggingDivider) // WM_LBUTTONUP
            {
                DragDivider(unchecked((short)(lParam.ToInt64() & 0xFFFF)));
                StopDragging(); return true;
            }
            else if (message == 0x0215) draggingDivider = false; // WM_CAPTURECHANGED
            else if (message == 0x001F || message == 0x0002) StopDragging(); // WM_CANCELMODE / WM_DESTROY
            return false;
        }

        private void DragDivider(int x)
        {
            Native.Rect client;
            if (!Native.GetClientRect(Handle, out client)) return;
            manualTreeWidth = true;
            treeWidthDip = (x - dragOffset - U(14)) * 96.0 / Dpi;
            treeWidthDip = TreeWidthFor(client.Right) * 96.0 / Dpi;
            Relayout();
            if (sizeCursor != IntPtr.Zero) Native.SetCursor(sizeCursor);
        }

        private void StopDragging()
        {
            if (!draggingDivider) return;
            draggingDivider = false;
            if (Native.GetCapture() == Handle) Native.ReleaseCapture();
            RepaintContents();
        }

        protected override void DpiChanged()
        {
            StopDragging(); QueueTreeFit();
        }

        private void QueueTreeFit()
        {
            if (manualTreeWidth || fitQueued || Handle == IntPtr.Zero) return;
            fitQueued = Native.PostMessageW(Handle, 0x8002, IntPtr.Zero, IntPtr.Zero);
        }

        private void FitTreeWidth()
        {
            if (manualTreeWidth || tree == IntPtr.Zero || nodes.Count == 0) return;
            IntPtr dc = Native.GetDC(tree);
            if (dc == IntPtr.Zero) return;
            IntPtr previousFont = IntPtr.Zero;
            int needed = U(320);
            try
            {
                IntPtr treeFont = Native.SendMessageW(tree, 0x0031, IntPtr.Zero, IntPtr.Zero); // WM_GETFONT
                if (treeFont != IntPtr.Zero) previousFont = Native.SelectObject(dc, treeFont);
                int indent = Math.Max(U(16), Native.SendMessageW(tree, 0x1106, IntPtr.Zero, IntPtr.Zero).ToInt32()); // TVM_GETINDENT
                foreach (NodeInfo node in nodes.Values)
                {
                    Native.Size extent;
                    if (Native.GetTextExtentPoint32W(dc, node.Label, node.Label.Length, out extent))
                        needed = Math.Max(needed, extent.Width + (node.Depth + 1) * indent + U(40));
                }
            }
            finally
            {
                if (previousFont != IntPtr.Zero && previousFont != new IntPtr(-1)) Native.SelectObject(dc, previousFont);
                Native.ReleaseDC(tree, dc);
            }
            // При раскрытии веток автоматически только расширяем дерево; выбор строк ширину не меняет.
            treeWidthDip = initialFitDone ? Math.Max(treeWidthDip, needed * 96.0 / Dpi) : needed * 96.0 / Dpi;
            if (!initialFitDone)
            {
                initialFitDone = true;
                EnsureClientWidth(needed + 2 * U(14) + U(12) + U(430));
            }
            Relayout();
        }

        protected override void Started() { LoadProject(); }
        protected override void Command(int id, int code, IntPtr control)
        {
            if (id == NameId && code == 0x0300) // EN_CHANGE
            {
                if (!changingSelection && !busy) UpdatePlan();
                return;
            }
            if (code != 0 || busy) return;
            if (id == ApplyId) ApplyRename();
            else if (id == RecoverId && !catalog.MustRestart) CompletePrevious();
            else if (id == ReloadId && !catalog.MustRestart) LoadProject();
            else if (id == ErrorsId) ShowDetails("Ошибки чтения", String.Join("\r\n\r\n", catalog.Errors.ToArray()));
        }

        protected override void Notify(IntPtr data)
        {
            if (data == IntPtr.Zero || tree == IntPtr.Zero) return;
            Native.NotifyHeader header = (Native.NotifyHeader)Marshal.PtrToStructure(data, typeof(Native.NotifyHeader));
            if (header.From != tree) return;
            if (header.Code == -451 || header.Code == -402) SelectionChanged(); // TVN_SELCHANGEDW/A
            else if (header.Code == -454 || header.Code == -405) // TVN_ITEMEXPANDINGW/A
            {
                Native.TreeNotification value = (Native.TreeNotification)Marshal.PtrToStructure(data, typeof(Native.TreeNotification));
                if ((value.Action & 2) != 0) PopulateChildren(value.NewItem.Item);
            }
        }

        private void SetBusy(bool value)
        {
            if (value) StopDragging();
            busy = value;
            Enabled(tree, !value); Enabled(newName, !value && !catalog.MustRestart);
            Enabled(close, !value); Enabled(reload, !value && !catalog.MustRestart);
            Enabled(errors, !value && catalog.Errors.Count > 0);
            Enabled(recover, !value && !catalog.MustRestart && catalog.Errors.Count == 0 && SelectedPart() != null);
            Enabled(apply, !value && currentPlan != null && !catalog.MustRestart);
        }
        private void Progress(string value) { Text(status, value); Native.UpdateWindow(status); }
        private void LoadProject()
        {
            SetBusy(true); currentPlan = null;
            try
            {
                catalog.Load(Progress);
                FillTree(initialPart);
                Progress("Файлов в корне: " + catalog.LocalParts.Count +
                    (catalog.Errors.Count == 0 ? ". Выберите компонент." : ". Есть ошибки чтения; переименование заблокировано."));
            }
            catch (Exception ex)
            {
                catalog.Errors.Add(ex.Message);
                Progress("Не удалось полностью прочитать проект.");
                ShowDetails("Ошибка", ex.Message);
            }
            finally { SetBusy(false); UpdatePlan(); }
        }

        private NodeInfo InsertNode(string label, PartInfo part, LinkInfo link, NodeInfo parent, bool expandable)
        {
            Native.TreeInsert value = new Native.TreeInsert();
            value.Parent = parent == null ? Native.TreeRoot : parent.Handle;
            value.After = Native.TreeLast;
            value.Item.Mask = 0x0041; // TVIF_TEXT | TVIF_CHILDREN
            value.Item.Children = expandable ? 1 : 0;
            value.Item.Text = Marshal.StringToHGlobalUni(label);
            try
            {
                IntPtr item = Native.InsertTree(tree, 0x1132, IntPtr.Zero, ref value); // TVM_INSERTITEMW
                if (item == IntPtr.Zero) throw new InvalidOperationException("Не удалось добавить компонент в дерево.");
                NodeInfo node = new NodeInfo { Part = part, Link = link, Parent = parent, Handle = item,
                    ChildrenLoaded = !expandable, Label = label, Depth = parent == null ? 0 : parent.Depth + 1 };
                nodes.Add(item, node);
                return node;
            }
            finally { Marshal.FreeHGlobal(value.Item.Text); }
        }

        private NodeInfo MakeNode(PartInfo part, LinkInfo link, NodeInfo parent)
        {
            string label = part == null ? (link.Name ?? "Недоступный компонент") : Path.GetFileNameWithoutExtension(part.Path);
            if (part != null && link != null && !String.IsNullOrEmpty(link.Name) && link.Name != label)
                label = link.Name + "  [" + part.FileName + "]";
            if (part != null && !part.Local) label += "  (внешний файл)";
            if (part == null) label += "  (недоступен)";
            if (link != null && link.Suppressed) label += "  (подавлен)";
            bool cycle = false;
            for (NodeInfo ancestor = parent; ancestor != null; ancestor = ancestor.Parent)
                if (part != null && ancestor.Part == part) { cycle = true; break; }
            if (cycle) label += "  (циклическая ссылка)";
            return InsertNode(label, part, link, parent, !cycle && part != null && part.Children.Count > 0);
        }

        private void PopulateChildren(IntPtr handle)
        {
            NodeInfo node;
            if (!nodes.TryGetValue(handle, out node) || node.ChildrenLoaded || node.Part == null) return;
            node.ChildrenLoaded = true;
            foreach (LinkInfo link in node.Part.Children) MakeNode(link.Child, link, node);
            if (!changingSelection) QueueTreeFit();
        }

        private void FillTree(Tag preferred)
        {
            changingSelection = true;
            Native.SendMessageW(tree, 0x000B, IntPtr.Zero, IntPtr.Zero); // WM_SETREDRAW
            try
            {
                Native.SendMessageW(tree, 0x1101, IntPtr.Zero, Native.TreeRoot); // TVM_DELETEITEM
                nodes.Clear();
                HashSet<Tag> children = new HashSet<Tag>();
                foreach (PartInfo part in catalog.LocalParts)
                    foreach (LinkInfo link in part.Children)
                        if (link.Child != null && link.Child.Local) children.Add(link.Child.Part.Tag);
                NodeInfo first = null, preferredNode = null;
                foreach (PartInfo part in catalog.LocalParts)
                {
                    if (children.Contains(part.Part.Tag)) continue;
                    NodeInfo node = MakeNode(part, null, null);
                    if (first == null) first = node;
                    if (part.Part.Tag == preferred) preferredNode = node;
                }
                if (first == null)
                    foreach (PartInfo part in catalog.LocalParts)
                    {
                        NodeInfo node = MakeNode(part, null, null);
                        if (first == null) first = node;
                        if (part.Part.Tag == preferred) preferredNode = node;
                    }
                NodeInfo index = InsertNode("Все файлы корневой папки", null, null, null, catalog.LocalParts.Count > 0);
                index.ChildrenLoaded = true;
                foreach (PartInfo part in catalog.LocalParts)
                {
                    NodeInfo node = MakeNode(part, null, index);
                    if (part.Part.Tag == preferred && preferredNode == null) preferredNode = node;
                }
                NodeInfo selected = preferredNode ?? first;
                if (selected != null)
                {
                    Native.SendMessageW(tree, 0x110B, new IntPtr(9), selected.Handle); // TVM_SELECTITEM / TVGN_CARET
                    Native.SendMessageW(tree, 0x1102, new IntPtr(2), selected.Handle); // TVM_EXPAND
                    Native.SendMessageW(tree, 0x1114, IntPtr.Zero, selected.Handle); // TVM_ENSUREVISIBLE
                }
            }
            finally
            {
                Native.SendMessageW(tree, 0x000B, new IntPtr(1), IntPtr.Zero);
                Native.InvalidateRect(tree, IntPtr.Zero, true);
                changingSelection = false;
            }
            SelectionChanged();
            QueueTreeFit();
        }

        private PartInfo SelectedPart()
        {
            IntPtr selected = Native.SendMessageW(tree, 0x110A, new IntPtr(9), IntPtr.Zero); // TVM_GETNEXTITEM / TVGN_CARET
            NodeInfo node;
            return nodes.TryGetValue(selected, out node) ? node.Part : null;
        }
        private void SelectionChanged()
        {
            if (changingSelection) return;
            PartInfo part = SelectedPart();
            changingSelection = true;
            try
            {
                Text(selectedPath, part == null ? "" : part.Path);
                Text(newName, part == null ? "" : Path.GetFileNameWithoutExtension(part.Path));
            }
            finally { changingSelection = false; }
            UpdatePlan();
        }
        private void UpdatePlan()
        {
            currentPlan = null; Enabled(apply, false);
            PartInfo selected = SelectedPart();
            Enabled(recover, !busy && !catalog.MustRestart && catalog.Errors.Count == 0 && selected != null && selected.Local);
            Text(preview, ""); Text(futurePath, ""); Text(validation, "");
            if (busy) return;
            try
            {
                currentPlan = catalog.Plan(SelectedPart(), Text(newName));
                Text(futurePath, currentPlan.NewPath);
                StringBuilder text = new StringBuilder();
                foreach (PartInfo part in currentPlan.SaveOrder)
                    text.Append(part == currentPlan.Target ? Path.GetFileName(currentPlan.NewPath) : part.FileName)
                        .Append(part.Part.IsModified ? "  [есть несохранённые изменения]" : "").Append("\r\n");
                text.Append("\r\nВхождений в файлах сборок: ").Append(currentPlan.Occurrences.Count);
                if (currentPlan.FileChange) text.Append("\r\nПосле сохранения будет удалено старое имя:\r\n").Append(Path.GetFileName(currentPlan.OldPath));
                Text(preview, text.ToString());
                Text(validation, "Готово к переименованию."); Enabled(apply, true);
            }
            catch (Exception ex)
            {
                currentPlan = null; Text(validation, ex.Message);
                try { Text(futurePath, Path.Combine(catalog.Root, NormalizeName(Text(newName)) + ".prt")); } catch { }
            }
        }
        private void ApplyRename()
        {
            if (busy || currentPlan == null || catalog.MustRestart) return;
            RenamePlan plan = currentPlan;
            bool completed = false;
            SetBusy(true);
            try
            {
                string result = catalog.Execute(plan, Progress);
                completed = true;
                FillTree(plan.Target.Part.Tag);
                Progress("Готово. Можно выбрать следующий компонент.");
                ShowDetails("Готово", result);
            }
            catch (Exception ex)
            {
                if (completed)
                {
                    catalog.MustRestart = true;
                    Progress("Файлы переименованы, но не удалось обновить окно.");
                    ShowDetails("Ошибка обновления окна", "Переименование и сохранение завершены. Ошибка обновления интерфейса:\r\n" +
                        ex.Message + "\r\n\r\nЗакройте окно и запустите журнал снова.");
                    return;
                }
                Progress(catalog.MustRestart ? "Операция остановлена. Прочитайте сообщение об ошибке." : "Переименование не выполнено.");
                ShowDetails("Ошибка", ex.Message);
            }
            finally { SetBusy(false); UpdatePlan(); }
        }
        private void CompletePrevious()
        {
            PartInfo target = SelectedPart();
            if (busy || target == null || !target.Local || catalog.MustRestart) return;
            SetBusy(true);
            try
            {
                RecoveryDialog dialog = new RecoveryDialog(catalog, target);
                dialog.Show(Handle);
                if (dialog.Completed)
                {
                    FillTree(target.Part.Tag);
                    Progress("Прерванное переименование завершено.");
                    ShowDetails("Готово", dialog.Result);
                }
            }
            finally { SetBusy(false); UpdatePlan(); }
        }
        private void ShowDetails(string suffix, string message) { new DetailsDialog(suffix, message).Show(Handle); }
    }

    private static class Native
    {
        public static readonly IntPtr TreeRoot = new IntPtr(-65536); // TVI_ROOT
        public static readonly IntPtr TreeLast = new IntPtr(-65534); // TVI_LAST
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate IntPtr DialogProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct InitControls { public uint Size, Classes; }
        [StructLayout(LayoutKind.Sequential)]
        public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct Size { public int Width, Height; }
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MinMaxInfo { public Point Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
        [StructLayout(LayoutKind.Sequential)]
        public struct NotifyHeader { public IntPtr From, Id; public int Code; }
        [StructLayout(LayoutKind.Sequential)]
        public struct TreeItem
        {
            public uint Mask;
            public IntPtr Item;
            public uint State, StateMask;
            public IntPtr Text;
            public int TextMax, Image, SelectedImage, Children;
            public IntPtr Param;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct TreeInsert
        {
            public IntPtr Parent, After;
            public TreeItem Item;
            // Хвост TVITEMEXW обеспечивает полный размер TVINSERTSTRUCTW.
            public int Integral;
            public uint StateEx;
            public IntPtr Window;
            public int ExpandedImage, Reserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct TreeNotification
        {
            public NotifyHeader Header;
            public uint Action;
            public TreeItem OldItem, NewItem;
            public Point Drag;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern IntPtr GetModuleHandleW(string name);
        [DllImport("comctl32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitCommonControlsEx(ref InitControls controls);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr DialogBoxIndirectParamW(IntPtr instance, IntPtr template, IntPtr owner, DialogProc callback, IntPtr parameter);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EndDialog(IntPtr dialog, IntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr CreateWindowExW(uint extended, string className, string title, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern IntPtr SendTextMessageW(IntPtr window, uint message, IntPtr wParam, string text);
        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern IntPtr InsertTree(IntPtr window, uint message, IntPtr wParam, ref TreeInsert value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowTextW(IntPtr window, string text);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int GetWindowTextW(IntPtr window, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int GetWindowTextLengthW(IntPtr window);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnableWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool enabled);
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr SetFocus(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern IntPtr LoadCursorW(IntPtr instance, IntPtr name);
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr SetCursor(IntPtr cursor);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ScreenToClient(IntPtr window, ref Point point);
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr SetCapture(IntPtr window);
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr GetCapture();
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr BeginDeferWindowPos(int count);
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr DeferWindowPos(IntPtr batch, IntPtr window, IntPtr after,
            int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EndDeferWindowPos(IntPtr batch);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateWindow(IntPtr window);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InvalidateRect(IntPtr window, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int MessageBoxW(IntPtr owner, string message, string title, uint style);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", ExactSpelling = true)]
        private static extern int SetWindowLong32(IntPtr window, int index, int value);
        public static void SetDialogResult(IntPtr window, IntPtr value)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(window, 0, value); // DWLP_MSGRESULT
            else SetWindowLong32(window, 0, value.ToInt32());
        }
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern IntPtr CreateFontW(int height, int width, int angle, int orientation, int weight,
            uint italic, uint underline, uint strikeout, uint charset, uint output, uint clip, uint quality, uint pitch, string face);
        [DllImport("gdi32.dll", ExactSpelling = true)]
        public static extern IntPtr GetStockObject(int index);
        [DllImport("gdi32.dll", ExactSpelling = true)]
        public static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetTextExtentPoint32W(IntPtr dc, string text, int count, out Size size);
        [DllImport("gdi32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr value);
    }
}
