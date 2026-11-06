// NX_Delete_Empty_Tools.cs — delete empty CAM tools in the current work part.
// SCRIPT_VERSION: V1
// Siemens NX / Designcenter, C# Journal. Tools > Journal > Play.
// An empty tool has no members and is not an ancestor of any CAM operation.
// Operations are preserved regardless of toolpath generation or suppression.
// One undo step. No files, logs, attributes, automatic save or regeneration.

using System;
using System.Collections.Generic;
using System.Text;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Operation = NXOpen.CAM.Operation;

public class NXDeleteEmptyTools
{
    private const string SCRIPT_VERSION = "V1";
    private static readonly string Title = "Удаление пустых инструментов — " + SCRIPT_VERSION
        + " — " + Encoding.UTF8.GetString(Convert.FromBase64String("YnkgQFRvbnlfRm94eHggKFRHKQ=="));
    private const int DeleteResponse = 11;
    private const int CancelResponse = 12;

    private sealed class Snapshot
    {
        internal readonly HashSet<Tag> Groups = new HashSet<Tag>();
        internal readonly Dictionary<Tag, Tag> OperationParents = new Dictionary<Tag, Tag>();
        internal readonly List<Tool> EmptyTools = new List<Tool>();
        internal readonly HashSet<Tag> EmptyTags = new HashSet<Tag>();
    }

    public static void Main(string[] args)
    {
        Session session = Session.GetSession();
        UFSession uf = UFSession.GetUFSession();
        Part part = session.Parts.Work;
        if (part == null)
        {
            Show(NXMessageBox.DialogType.Information, "Откройте деталь с CAM-проектом.");
            return;
        }

        CAMSetup setup;
        Snapshot before;
        try
        {
            setup = part.CAMSetup;
            if (setup == null)
                throw new InvalidOperationException("В рабочей детали нет CAM Setup. Перейдите в Manufacturing.");
            before = Inspect(part, setup);
            if (before.EmptyTools.Count == 0)
            {
                Show(NXMessageBox.DialogType.Information, "Пустых инструментов не найдено.");
                return;
            }
            if (!Confirm(uf, before.EmptyTools)) return;
            if (session.Parts.Work == null || session.Parts.Work.Tag != part.Tag)
                throw new InvalidOperationException("Рабочая деталь изменилась. Запустите скрипт заново.");
            // Re-read after the modal dialog; never delete using a stale candidate list.
            Snapshot current = Inspect(part, setup);
            if (!before.Groups.SetEquals(current.Groups)
                || !SameParents(before.OperationParents, current.OperationParents)
                || !before.EmptyTags.SetEquals(current.EmptyTags))
                throw new InvalidOperationException("Структура CAM изменилась. Запустите скрипт заново.");
            before = current;
            TaggedObject[] pending = session.UpdateManager.GetObjectsOnDeleteList();
            if (pending == null || pending.Length != 0)
                throw new InvalidOperationException("В NX есть незавершённое удаление. Завершите текущую команду и повторите запуск.");
        }
        catch (Exception ex)
        {
            Show(NXMessageBox.DialogType.Error, "Удаление не начато.\n\n" + ex.Message);
            return;
        }

        Session.UndoMarkId mark;
        try
        {
            mark = session.SetUndoMark(Session.MarkVisibility.Visible, "Удалить пустые инструменты");
        }
        catch (Exception ex)
        {
            Show(NXMessageBox.DialogType.Error, "Не удалось создать отметку отмены. Удаление не начато.\n\n" + ex.Message);
            return;
        }

        try
        {
            TaggedObject[] targets = new TaggedObject[before.EmptyTools.Count];
            for (int i = 0; i < targets.Length; i++) targets[i] = before.EmptyTools[i];
            int errors = session.UpdateManager.AddObjectsToDeleteList(targets);
            if (errors != 0)
                throw new InvalidOperationException("NX сообщил об ошибках подготовки удаления: " + errors);

            // Dependency expansion must not pull operations or other CAM groups into deletion.
            TaggedObject[] queued = session.UpdateManager.GetObjectsOnDeleteList();
            if (queued == null) throw new InvalidOperationException("Не удалось проверить очередь удаления NX.");
            foreach (TaggedObject item in queued)
            {
                if (item is Operation || (item is NCGroup && !before.EmptyTags.Contains(item.Tag)))
                    throw new InvalidOperationException("NX добавил связанный CAM-объект в очередь удаления. Удаление отменено.");
            }

            errors = session.UpdateManager.DoUpdate(mark);
            if (errors != 0)
                throw new InvalidOperationException("NX сообщил об ошибках удаления: " + errors);

            Snapshot after = Inspect(part, setup);
            HashSet<Tag> expectedGroups = new HashSet<Tag>(before.Groups);
            expectedGroups.ExceptWith(before.EmptyTags);
            if (!expectedGroups.SetEquals(after.Groups)
                || !SameParents(before.OperationParents, after.OperationParents))
                throw new InvalidOperationException("Проверка результата удаления не пройдена: изменились другие CAM-объекты или остались удаляемые инструменты.");
        }
        catch (Exception ex)
        {
            string rollback = RollBack(session, mark);
            Refresh(uf);
            Show(NXMessageBox.DialogType.Error, "Не удалось завершить удаление.\n\n" + ex.Message + "\n\n" + rollback);
            return;
        }

        string refreshError = Refresh(uf);
        Show(NXMessageBox.DialogType.Information, "Удалено пустых инструментов: " + before.EmptyTools.Count
            + ".\n\nОтменить удаление: Ctrl+Z."
            + (refreshError == null ? "" : "\n\nОбновите дерево операций вручную: " + refreshError));
    }

    private static Snapshot Inspect(Part part, CAMSetup setup)
    {
        Snapshot state = new Snapshot();
        NCGroup root = setup.GetRoot(CAMSetup.View.MachineTool);
        if (root == null) throw new InvalidOperationException("Не найден корень дерева инструментов.");
        HashSet<Tag> usedGroups = new HashSet<Tag>();
        foreach (Operation operation in setup.CAMOperationCollection)
        {
            NCGroup parent = operation.GetParent(CAMSetup.View.MachineTool);
            if (parent == null)
                throw new InvalidOperationException("Не удалось определить инструмент операции: " + operation.Name);
            state.OperationParents.Add(operation.Tag, parent.Tag);
            HashSet<Tag> visited = new HashSet<Tag>();
            while (parent != null)
            {
                if (!visited.Add(parent.Tag))
                    throw new InvalidOperationException("Обнаружен цикл в дереве инструментов.");
                usedGroups.Add(parent.Tag);
                if (parent.Tag == root.Tag) break;
                parent = parent.GetParent();
            }
            if (parent == null)
                throw new InvalidOperationException("Неполная связь с деревом инструментов у операции: " + operation.Name);
        }

        foreach (NCGroup group in setup.CAMGroupCollection)
        {
            state.Groups.Add(group.Tag);
            Tool tool = group as Tool;
            if (tool == null || tool.Tag == root.Tag || tool.OwningPart == null
                || tool.OwningPart.Tag != part.Tag || usedGroups.Contains(tool.Tag)) continue;
            CAMObject[] members = tool.GetMembers();
            if (members == null)
                throw new InvalidOperationException("Не удалось проверить содержимое инструмента: " + tool.Name);
            if (members.Length != 0) continue;
            if (state.EmptyTags.Add(tool.Tag)) state.EmptyTools.Add(tool);
        }
        return state;
    }

    private static bool SameParents(Dictionary<Tag, Tag> a, Dictionary<Tag, Tag> b)
    {
        if (a.Count != b.Count) return false;
        foreach (KeyValuePair<Tag, Tag> item in a)
        {
            Tag parent;
            if (!b.TryGetValue(item.Key, out parent) || parent != item.Value) return false;
        }
        return true;
    }

    private static bool Confirm(UFSession uf, List<Tool> candidates)
    {
        List<string> lines = new List<string>();
        lines.Add("Удалить пустые инструменты: " + candidates.Count + "?");
        lines.Add("Инструменты с операциями сохраняются, даже если траектории не рассчитаны.");
        lines.Add(" ");
        foreach (Tool tool in candidates) lines.Add(tool.Name);
        UFUi.MessageButtons buttons = new UFUi.MessageButtons();
        buttons.button1 = true;
        buttons.label1 = "Удалить";
        buttons.response1 = DeleteResponse;
        buttons.button2 = true;
        buttons.label2 = "Отмена";
        buttons.response2 = CancelResponse;
        int response;
        uf.Ui.MessageDialog(Title, UiMessageDialogType.UiMessageQuestion,
            lines.ToArray(), lines.Count, false, ref buttons, out response);
        return response == DeleteResponse;
    }

    private static string RollBack(Session session, Session.UndoMarkId mark)
    {
        string cleanupError = null;
        // The queue was empty on entry: every queued object belongs to this attempt.
        try
        {
            TaggedObject[] queued = session.UpdateManager.GetObjectsOnDeleteList();
            if (queued == null) throw new InvalidOperationException("Очередь удаления недоступна.");
            if (queued.Length > 0) session.UpdateManager.RemoveObjectsFromDeleteList(queued);
        }
        catch (Exception ex) { cleanupError = ex.Message; }
        try
        {
            session.UndoToMark(mark, null);
        }
        catch (Exception ex)
        {
            return "Не удалось полностью отменить изменения. Проверьте CAM-проект и выполните отмену в NX.\n"
                + ex.Message + (cleanupError == null ? "" : "\nОчередь удаления: " + cleanupError);
        }
        try { session.DeleteUndoMark(mark, null); }
        catch { /* Retaining an extra undo marker does not change the restored model. */ }
        return "Изменения этого запуска отменены."
            + (cleanupError == null ? "" : "\nНе удалось очистить очередь удаления: " + cleanupError);
    }

    private static string Refresh(UFSession uf)
    {
        try { uf.UiOnt.Refresh(); return null; }
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
