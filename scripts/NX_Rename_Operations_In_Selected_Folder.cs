// Переименование операций
// SCRIPT_VERSION: V1.04
// Рабочий файл: NX_Rename_Operations_In_Selected_Folder.cs
// Выделенные операции нумеруются по порядку дерева Operation Navigator.
// Выделение через Shift/Ctrl не влияет на порядок. Занятые имена пропускаются.
using System;
using System.Collections.Generic;
using System.Globalization;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using NXOpenUI;
using CamOperation = NXOpen.CAM.Operation;

public class NX_Rename_Operations_In_Selected_Folder
{
    private const string SCRIPT_VERSION = "V1.04";
    private const string DialogTitle = "Переименование операций — " + SCRIPT_VERSION;

    public static void Main(string[] args)
    {
        Session theSession = Session.GetSession();
        UI theUI = UI.GetUI();

        try
        {
            int selectedCount = theUI.SelectionManager.GetNumSelectedObjects();

            if (selectedCount == 0)
            {
                ShowMessage(
                    theUI,
                    NXMessageBox.DialogType.Warning,
                    "В Operation Navigator выберите одну папку с операциями " +
                    "или выделите одну / несколько операций и запустите скрипт снова.");
                return;
            }

            NCGroup selectedGroup = null;

            if (selectedCount == 1)
            {
                selectedGroup =
                    theUI.SelectionManager.GetSelectedTaggedObject(0) as NCGroup;
            }

            List<CamOperation> operations;

            if (selectedGroup != null)
            {
                operations = GetDirectOperations(selectedGroup);
            }
            else
            {
                operations = new List<CamOperation>();

                // Сохраняем список до показа окна ввода и любых переименований.
                // Здесь собираем только состав выделения. Порядок назначим
                // по дереву: при Shift список выделения NX может идти иначе.
                for (int i = 0; i < selectedCount; i++)
                {
                    CamOperation operation =
                        theUI.SelectionManager.GetSelectedTaggedObject(i) as CamOperation;

                    if (operation == null)
                    {
                        ShowMessage(
                            theUI,
                            NXMessageBox.DialogType.Warning,
                            "Выберите только операции либо одну папку с операциями.\n\n" +
                            "В текущем выделении есть папка или другой объект. " +
                            "Переименование не выполнено.");
                        return;
                    }

                    operations.Add(operation);
                }
            }

            if (operations.Count == 0)
            {
                ShowMessage(
                    theUI,
                    NXMessageBox.DialogType.Warning,
                    "В выбранной папке нет операций.");
                return;
            }

            string baseName = NXInputBox.GetInputString(
                selectedGroup != null
                    ? "Какое имя программам в папке назначить?"
                    : "Какое имя выделенным операциям назначить?",
                DialogTitle,
                "");

            if (baseName == null)
            {
                return;
            }

            baseName = baseName.Trim();

            if (baseName.Length == 0)
            {
                return;
            }

            Part workPart = theSession.Parts.Work;

            if (workPart == null || workPart.CAMSetup == null)
            {
                ShowMessage(
                    theUI,
                    NXMessageBox.DialogType.Warning,
                    "Откройте рабочую деталь с CAM-проектом и запустите скрипт снова.");
                return;
            }

            // Сначала проверяем весь CAM-проект и готовим все новые имена.
            // До завершения этой проверки операции не изменяются.
            Dictionary<string, bool> reservedNames = GetUnavailableNames(workPart, operations);

            if (selectedGroup == null && operations.Count > 1)
            {
                operations = OrderSelectedOperations(workPart.CAMSetup, operations);
            }

            List<string> newNames = CreateAvailableNames(baseName, operations.Count, reservedNames);

            Session.UndoMarkId undoMark = theSession.SetUndoMark(
                Session.MarkVisibility.Visible,
                DialogTitle);

            try
            {
                RenameOperations(operations, newNames, reservedNames);

                ShowMessage(
                    theUI,
                    NXMessageBox.DialogType.Information,
                    "Готово. Переименовано операций: " + operations.Count.ToString() + ".\n\n" +
                    (selectedGroup != null
                        ? "Имена назначены по порядку в папке:\n"
                        : "Имена назначены сверху вниз по дереву Operation Navigator:\n") +
                    FormatAssignedNames(newNames) +
                    "\n\nЗанятые имена в CAM-проекте автоматически пропущены.");
            }
            catch (Exception renameException)
            {
                string rollbackMessage;

                try
                {
                    theSession.UndoToMark(undoMark, DialogTitle);
                    rollbackMessage = "Изменения отменены.";
                }
                catch (Exception rollbackException)
                {
                    rollbackMessage =
                        "Не удалось автоматически отменить изменения. " +
                        "Проверьте имена операций.\n" + rollbackException.Message;
                }

                ShowMessage(
                    theUI,
                    NXMessageBox.DialogType.Error,
                    "Не удалось переименовать операции. " + rollbackMessage + "\n\n" +
                    renameException.Message);
            }
        }
        catch (Exception ex)
        {
            ShowMessage(
                theUI,
                NXMessageBox.DialogType.Error,
                "Ошибка выполнения скрипта:\n\n" + ex.Message);
        }
    }

    private static List<CamOperation> GetDirectOperations(NCGroup group)
    {
        List<CamOperation> operations = new List<CamOperation>();
        CAMObject[] members = group.GetMembers();

        for (int i = 0; i < members.Length; i++)
        {
            CamOperation operation = members[i] as CamOperation;

            if (operation != null)
            {
                operations.Add(operation);
            }
        }

        return operations;
    }

    private static List<CamOperation> OrderSelectedOperations(
        CAMSetup setup,
        List<CamOperation> selectedOperations)
    {
        // Список выделения используется только как набор нужных операций.
        // Обходим дерево активного вида в том же порядке GetMembers(),
        // который уже используется при выборе папки.
        Dictionary<Tag, bool> remainingTags = new Dictionary<Tag, bool>();

        for (int i = 0; i < selectedOperations.Count; i++)
        {
            remainingTags.Add(selectedOperations[i].Tag, true);
        }

        NCGroup root = setup.GetRoot(GetNavigatorView());

        if (root == null)
        {
            throw new Exception("Не удалось получить дерево Operation Navigator.");
        }

        List<CamOperation> orderedOperations = new List<CamOperation>();
        CollectSelectedInTreeOrder(root, remainingTags, orderedOperations);

        if (remainingTags.Count != 0)
        {
            throw new Exception(
                "Не удалось определить положение всех выделенных операций " +
                "в текущем дереве Operation Navigator.\n" +
                "Выделите операции в виде «Порядок программ» и запустите скрипт снова.\n\n" +
                "Имена операций не изменены.");
        }

        return orderedOperations;
    }

    private static CAMSetup.View GetNavigatorView()
    {
        UFUiOnt.TreeMode treeMode;
        UFSession.GetUFSession().UiOnt.AskView(out treeMode);

        switch (treeMode)
        {
            case UFUiOnt.TreeMode.Order:
                return CAMSetup.View.ProgramOrder;
            case UFUiOnt.TreeMode.MachineTool:
                return CAMSetup.View.MachineTool;
            case UFUiOnt.TreeMode.GeometryMode:
                return CAMSetup.View.Geometry;
            case UFUiOnt.TreeMode.MachineMode:
                return CAMSetup.View.MachineMethod;
            default:
                throw new Exception(
                    "Не удалось определить текущий вид Operation Navigator.\n" +
                    "Переключитесь в вид «Порядок программ» и запустите скрипт снова.");
        }
    }

    private static void CollectSelectedInTreeOrder(
        NCGroup group,
        Dictionary<Tag, bool> remainingTags,
        List<CamOperation> orderedOperations)
    {
        CAMObject[] members = group.GetMembers();

        for (int i = 0; i < members.Length && remainingTags.Count > 0; i++)
        {
            CamOperation operation = members[i] as CamOperation;

            if (operation != null)
            {
                if (remainingTags.Remove(operation.Tag))
                {
                    orderedOperations.Add(operation);
                }
            }
            else
            {
                NCGroup childGroup = members[i] as NCGroup;

                if (childGroup != null)
                {
                    CollectSelectedInTreeOrder(childGroup, remainingTags, orderedOperations);
                }
            }
        }
    }

    private static Dictionary<string, bool> GetUnavailableNames(
        Part workPart,
        List<CamOperation> operations)
    {
        Dictionary<Tag, bool> selectedTags = new Dictionary<Tag, bool>();

        for (int i = 0; i < operations.Count; i++)
        {
            if (operations[i].OwningPart == null ||
                !operations[i].OwningPart.Tag.Equals(workPart.Tag))
            {
                throw new Exception("Все выбранные операции должны принадлежать рабочей детали.");
            }

            if (selectedTags.ContainsKey(operations[i].Tag))
            {
                throw new Exception("Одна операция присутствует в выделении несколько раз. Выделите операции заново.");
            }

            selectedTags[operations[i].Tag] = true;
        }

        // Регистр не учитываем: CHIST_1 и chist_1 считаем занятым именем.
        Dictionary<string, bool> names =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        CAMSetup setup = workPart.CAMSetup;
        CamOperation[] allOperations = setup.CAMOperationCollection.ToArray();

        for (int i = 0; i < allOperations.Length; i++)
        {
            // Старые имена переименовываемых операций освободятся после
            // назначения временных имен; их можно использовать повторно.
            if (!selectedTags.ContainsKey(allOperations[i].Tag))
            {
                names[allOperations[i].Name] = true;
            }
        }

        // Папки программ, инструменты, геометрия и методы тоже могут
        // занимать нужное имя, поэтому проверяем все CAM-группы.
        NCGroup[] allGroups = setup.CAMGroupCollection.ToArray();

        for (int i = 0; i < allGroups.Length; i++)
        {
            names[allGroups[i].Name] = true;
        }

        return names;
    }

    private static List<string> CreateAvailableNames(
        string baseName,
        int count,
        Dictionary<string, bool> reservedNames)
    {
        List<string> names = new List<string>();
        long nextNumber = 1;

        for (int i = 0; i < count; i++)
        {
            string candidate;

            do
            {
                candidate = baseName + "_" + nextNumber.ToString(CultureInfo.InvariantCulture);
                nextNumber++;
            }
            while (reservedNames.ContainsKey(candidate));

            names.Add(candidate);
            reservedNames[candidate] = true;
        }

        return names;
    }

    private static void RenameOperations(
        List<CamOperation> operations,
        List<string> newNames,
        Dictionary<string, bool> reservedNames)
    {
        // Для временных имен запрещены также все исходные имена выбранных
        // операций и уже запланированные окончательные имена.
        for (int i = 0; i < operations.Count; i++)
        {
            reservedNames[operations[i].Name] = true;
        }

        string temporaryBase =
            "TMP" + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
        List<string> temporaryNames =
            CreateAvailableNames(temporaryBase, operations.Count, reservedNames);

        for (int i = 0; i < operations.Count; i++)
        {
            SetOperationName(operations[i], temporaryNames[i]);
        }

        for (int i = 0; i < operations.Count; i++)
        {
            SetOperationName(operations[i], newNames[i]);
        }
    }

    private static string FormatAssignedNames(List<string> names)
    {
        int shownCount = Math.Min(names.Count, 10);
        string text = string.Join("\n", names.GetRange(0, shownCount).ToArray());

        if (names.Count > shownCount)
        {
            text += "\n...\n" + names[names.Count - 1];
        }

        return text;
    }

    private static void SetOperationName(
        CamOperation operation,
        string newName)
    {
        string oldName = operation.Name;

        try
        {
            operation.SetName(newName);
        }
        catch (Exception ex)
        {
            throw new Exception(
                "NX отклонил имя \"" + newName + "\".\n" +
                "Операция до переименования: \"" + oldName + "\".\n\n" +
                ex.Message,
                ex);
        }
    }

    private static void ShowMessage(
        UI theUI,
        NXMessageBox.DialogType dialogType,
        string message)
    {
        theUI.NXMessageBox.Show(DialogTitle, dialogType, message);
    }

    public static int GetUnloadOption(string dummy)
    {
        return (int)Session.LibraryUnloadOption.Immediately;
    }
}
