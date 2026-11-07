// NX_Clean_Operation_Names.cs — clean suffixes in all work-part CAM operations.
// SCRIPT_VERSION: V1
// Siemens NX / Designcenter, C# Journal. Tools > Journal > Play.
// Preserve the exact Z token; number collisions before Z in Program Order.
// One undo step. No files, attributes, save, regeneration or object deletion.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Operation = NXOpen.CAM.Operation;

public class NXCleanOperationNames
{
    private const string SCRIPT_VERSION = "V1";
    private static readonly string Title = "Очистка названий операций — " + SCRIPT_VERSION
        + " — " + Encoding.UTF8.GetString(Convert.FromBase64String("YnkgQFRvbnlfRm94eHggKFRHKQ=="));
    private const string UndoName = "Очистить названия операций";

    public static void Main(string[] args)
    {
        Session session = Session.GetSession();
        List<Operation> operations;
        List<string> originals = new List<string>();
        List<string> targets;
        List<int> changed = new List<int>();
        HashSet<string> reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> temporary;
        try
        {
            Part part = session.Parts.Work;
            if (part == null)
                throw new InvalidOperationException("Откройте рабочую деталь с CAM-проектом.");
            CAMSetup setup = part.CAMSetup;
            if (setup == null)
                throw new InvalidOperationException("В рабочей детали нет CAM Setup. Перейдите в Manufacturing.");
            operations = CollectOperations(part, setup);
            if (operations.Count == 0)
            {
                Show(NXMessageBox.DialogType.Information, "В рабочей детали нет CAM-операций.");
                return;
            }
            foreach (NCGroup group in setup.CAMGroupCollection) reserved.Add(group.Name);
            foreach (Operation operation in operations) originals.Add(operation.Name);
            targets = CleanOperationNameRules.BuildNames(originals, reserved);
            for (int i = 0; i < operations.Count; i++)
            {
                if (!String.Equals(originals[i], targets[i], StringComparison.Ordinal)) changed.Add(i);
                reserved.Add(originals[i]);
                reserved.Add(targets[i]);
            }
            if (changed.Count == 0)
            {
                Show(NXMessageBox.DialogType.Information, "Названия уже приведены в порядок. Изменений нет.");
                return;
            }
            temporary = TemporaryNames(changed.Count, reserved);
        }
        catch (Exception ex)
        {
            Show(NXMessageBox.DialogType.Error, "Переименование не начато.\n\n" + ex.Message);
            return;
        }

        Session.UndoMarkId mark;
        try { mark = session.SetUndoMark(Session.MarkVisibility.Visible, UndoName); }
        catch (Exception ex)
        {
            Show(NXMessageBox.DialogType.Error, "Не удалось создать отметку отмены. Имена не изменены.\n\n" + ex.Message);
            return;
        }
        try
        {
            // First free every changing name. This also permits swaps and gap removal.
            for (int i = 0; i < changed.Count; i++)
                SetExactName(operations[changed[i]], temporary[i]);
            foreach (int index in changed) SetExactName(operations[index], targets[index]);
            // Read back all operations, including unchanged ones and linked instances.
            for (int i = 0; i < operations.Count; i++)
                if (!String.Equals(operations[i].Name, targets[i], StringComparison.Ordinal))
                    throw new InvalidOperationException("NX изменил имя связанной операции: " + originals[i]);
        }
        catch (Exception ex)
        {
            string rollback = RollBack(session, mark, operations, originals);
            Refresh();
            Show(NXMessageBox.DialogType.Error, "Не удалось завершить переименование.\n\n" + ex.Message + "\n\n" + rollback);
            return;
        }

        string refreshError = Refresh();
        StringBuilder message = new StringBuilder();
        message.Append("Переименовано операций: ").Append(changed.Count)
            .Append(" из ").Append(operations.Count).Append(".\n");
        for (int i = 0; i < Math.Min(changed.Count, 10); i++)
        {
            int index = changed[i];
            message.Append("\n").Append(originals[index]).Append(" → ").Append(targets[index]);
        }
        if (changed.Count > 10) message.Append("\n…");
        message.Append("\n\nОтменить изменения: Ctrl+Z.");
        if (refreshError != null) message.Append("\n\nОбновите дерево операций вручную: ").Append(refreshError);
        Show(NXMessageBox.DialogType.Information, message.ToString());
    }

    private static List<Operation> CollectOperations(Part part, CAMSetup setup)
    {
        List<Operation> all = new List<Operation>();
        Dictionary<Tag, Operation> remaining = new Dictionary<Tag, Operation>();
        foreach (Operation operation in setup.CAMOperationCollection)
        {
            if (operation.OwningPart == null || operation.OwningPart.Tag != part.Tag)
                throw new InvalidOperationException("Операция не принадлежит рабочей детали: " + operation.Name);
            if (String.IsNullOrEmpty(operation.Name))
                throw new InvalidOperationException("Найдена операция без имени.");
            remaining.Add(operation.Tag, operation);
            all.Add(operation);
        }
        if (all.Count == 0) return all;
        NCGroup root = setup.GetRoot(CAMSetup.View.ProgramOrder);
        if (root == null) throw new InvalidOperationException("Не найден корень дерева «Порядок программ».");
        List<Operation> ordered = new List<Operation>();
        Walk(root, remaining, ordered, new HashSet<Tag>());
        // Include unassigned operations even if this NX build omits them from the root.
        foreach (Operation operation in all)
            if (remaining.Remove(operation.Tag)) ordered.Add(operation);
        return ordered;
    }

    private static void Walk(NCGroup group, Dictionary<Tag, Operation> remaining,
        List<Operation> ordered, HashSet<Tag> visited)
    {
        if (!visited.Add(group.Tag))
            throw new InvalidOperationException("Обнаружена повторная связь в дереве программ.");
        CAMObject[] members = group.GetMembers();
        if (members == null) throw new InvalidOperationException("Не удалось прочитать папку: " + group.Name);
        foreach (CAMObject member in members)
        {
            Operation operation = member as Operation;
            if (operation != null)
            {
                Operation original;
                if (remaining.TryGetValue(operation.Tag, out original))
                {
                    ordered.Add(original);
                    remaining.Remove(operation.Tag);
                }
                continue;
            }
            NCGroup child = member as NCGroup;
            if (child != null) Walk(child, remaining, ordered, visited);
        }
    }

    private static List<string> TemporaryNames(int count, HashSet<string> reserved)
    {
        List<string> result = new List<string>();
        string prefix = "TMPN" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        long number = 0;
        while (result.Count < count)
        {
            string name = prefix + "_" + number.ToString(CultureInfo.InvariantCulture);
            number++;
            if (reserved.Add(name)) result.Add(name);
        }
        return result;
    }

    private static void SetExactName(Operation operation, string name)
    {
        string oldName = operation.Name;
        try
        {
            operation.SetName(name);
            if (!String.Equals(operation.Name, name, StringComparison.Ordinal))
                throw new InvalidOperationException("NX вернул другое имя: " + operation.Name);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось назначить имя «" + name
                + "» операции «" + oldName + "».\n" + ex.Message, ex);
        }
    }

    private static string RollBack(Session session, Session.UndoMarkId mark,
        List<Operation> operations, List<string> originals)
    {
        try
        {
            session.UndoToMark(mark, null);
            for (int i = 0; i < operations.Count; i++)
                if (!String.Equals(operations[i].Name, originals[i], StringComparison.Ordinal))
                    throw new InvalidOperationException("Не восстановлено имя: " + originals[i]);
        }
        catch (Exception ex)
        {
            return "Не удалось полностью отменить изменения. Проверьте имена и выполните отмену в NX.\n" + ex.Message;
        }
        try { session.DeleteUndoMark(mark, null); }
        catch { /* Keep the marker if NX cannot remove it; names are already restored. */ }
        return "Изменения этого запуска отменены.";
    }

    private static string Refresh()
    {
        try { UFSession.GetUFSession().UiOnt.Refresh(); return null; }
        catch (Exception ex) { return ex.Message; }
    }

    private static void Show(NXMessageBox.DialogType type, string message)
    {
        UI.GetUI().NXMessageBox.Show(Title, type, message);
    }

    public static int GetUnloadOption(string dummy)
    {
        return (int)Session.LibraryUnloadOption.Immediately;
    }
}

// Pure naming rules shared by the journal and its executable regression checks.
internal static class CleanOperationNameRules
{
    private static readonly Regex CopyToken = new Regex(
        @"\A(?:COPY|INSTANCE|INSTANT|DUPLICATE|КОПИЯ|ЭКЗЕМПЛЯР)[0-9]*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CounterToken = new Regex(@"\A[0-9]+\z", RegexOptions.CultureInvariant);
    private static readonly Regex ZToken = new Regex(
        @"\AZ[+\-−]?(?:[0-9]+(?:[.,][0-9]*)?|[.,][0-9]+)(?:[eE][+\-]?[0-9]+)?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static void Split(string name, out string stem, out string z)
    {
        if (String.IsNullOrEmpty(name)) throw new ArgumentException("Operation name is empty.");
        string[] tokens = name.Split('_');
        int last = tokens.Length - 1;
        z = "";
        // Only the trailing service chain is removed. Embedded COPY words and
        // meaningful tokens such as D11, R1, 3D, NEW, MIRROR and STEP_2_FINISH stay.
        // Always keep at least the first token: a numeric-only name is still a name.
        while (last > 0)
        {
            string token = tokens[last];
            if (token.Length == 0 || CopyToken.IsMatch(token) || CounterToken.IsMatch(token))
            {
                last--;
                continue;
            }
            if (z.Length == 0 && ZToken.IsMatch(token))
            {
                z = "_" + token; // Keep spelling, precision and sign exactly; never parse a double.
                last--;
                continue;
            }
            break;
        }
        stem = String.Join("_", tokens, 0, last + 1);
        if (stem.Length == 0)
        {
            // Do not invent a base for an unusual name consisting only of suffixes.
            stem = name;
            z = "";
        }
    }

    internal static List<string> BuildNames(IList<string> originals, IEnumerable<string> reserved)
    {
        HashSet<string> taken = new HashSet<string>(reserved, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, long> next = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        List<string> result = new List<string>();
        foreach (string original in originals)
        {
            string stem, z;
            Split(original, out stem, out z);
            string family = stem + "\0" + z;
            long number;
            if (!next.TryGetValue(family, out number)) number = 0;
            string candidate;
            do
            {
                candidate = stem + (number == 0 ? "" : "_" + number.ToString(CultureInfo.InvariantCulture)) + z;
                number++;
            }
            while (!taken.Add(candidate));
            next[family] = number;
            result.Add(candidate);
        }
        return result;
    }
}
