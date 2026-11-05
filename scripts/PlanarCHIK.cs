// PlanarCHIK
// SCRIPT_VERSION: V1.01
// NXOpen C# Journal
// Working filename: PlanarCHIK.cs
// Select one or multiple operations in Operation Navigator -> run journal
// For each selected PLANAR_MILL (PlanarMilling): set Tool Position = ON for all boundary members.
// Reads selection from Operation Navigator via UF UI ONT.
// Optional: enable Custom Member Data > Stock and set one value on every member.
// Stock is entered in work-part units. No files, logs or reports are created.

using System;
using System.Collections.Generic;
using System.Reflection;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using NXOpen.Utilities;

using CamOperation = NXOpen.CAM.Operation;

public class NXJournal
{
    static Session theSession;
    static UI theUI;
    static UFSession theUf;
    static Part workPart;

    const string SCRIPT_VERSION = "V1.01";
    const string TITLE = "PlanarCHIK — " + SCRIPT_VERSION;
    const bool REGENERATE_TOOLPATH = true; // false если не нужно перестраивать траекторию

    public static void Main(string[] args)
    {
        theSession = Session.GetSession();
        theUI = UI.GetUI();
        theUf = UFSession.GetUFSession();
        workPart = theSession.Parts.Work;

        if (workPart == null || workPart.CAMSetup == null)
            throw new Exception(TITLE + "\n\nНет CAM Setup. Открой Manufacturing и убедись, что CAM Setup создан.");

        // 1) Get all selected CAM operations from Operation Navigator
        List<CamOperation> ops = GetSelectedCamOperationsFromOperationNavigator();
        if (ops.Count == 0)
        {
            throw new Exception(
                TITLE + "\n\n" +
                "В выделении Operation Navigator не найдено CAM-операций.\n\n" +
                "Сделай так:\n" +
                "1) В Operation Navigator выдели (кликни/через Ctrl) одну или несколько операций.\n" +
                "2) Запусти journal снова."
            );
        }

        // Validate selection before asking questions or changing any operation.
        bool hasPlanarMill = false;
        for (int i = 0; i < ops.Count; i++)
        {
            if (ops[i] is PlanarMilling)
            {
                hasPlanarMill = true;
                break;
            }
        }
        if (!hasPlanarMill)
        {
            theUI.NXMessageBox.Show(TITLE, NXMessageBox.DialogType.Warning,
                "Среди выбранных объектов нет операций PLANAR_MILL.\n" +
                "Выделите одну или несколько операций PLANAR_MILL и запустите скрипт снова.");
            return;
        }

        int answer = theUI.NXMessageBox.Show(TITLE,
            NXMessageBox.DialogType.Question, "Добавить припуск?");
        if (answer != 1 && answer != 2) return;

        bool addStock = (answer == 1);
        double stockValue = 0.0;
        if (addStock && !TryAskStock(out stockValue)) return;

        Session.UndoMarkId um = theSession.SetUndoMark(Session.MarkVisibility.Visible, TITLE);

        int processed = 0;
        int changedTotal = 0;
        int skippedNotPlanar = 0;

        List<CAMObject> regenList = new List<CAMObject>();

        try
        {
            for (int i = 0; i < ops.Count; i++)
            {
                PlanarMilling planarOp = ops[i] as PlanarMilling;
                if (planarOp == null)
                {
                    skippedNotPlanar++;
                    continue;
                }

                int changed = SetToolPositionOnForPlanarMill(planarOp, addStock, stockValue);
                changedTotal += changed;
                processed++;

                if (REGENERATE_TOOLPATH)
                    regenList.Add(planarOp);
            }

            if (processed == 0)
            {
                throw new Exception(
                    "Среди выделенных объектов нет операций PLANAR_MILL (Planar Mill).\n" +
                    "Выдели одну/несколько PLANAR_MILL и запусти journal снова."
                );
            }

            // 3) Optional regenerate toolpath once for all processed operations
            if (REGENERATE_TOOLPATH && regenList.Count > 0)
            {
                CAMObject[] arr = regenList.ToArray();
                workPart.CAMSetup.GenerateToolPath(arr);
            }

            theSession.SetUndoMarkName(um, TITLE + (addStock ? " - Tool Position ON + Stock" : " - Tool Position ON"));
        }
        catch (Exception ex)
        {
            string rollbackMessage;
            try
            {
                theSession.UndoToMark(um, null);
                theSession.DeleteUndoMark(um, null);
                rollbackMessage = "Изменения этого запуска отменены.";
            }
            catch (Exception undoError)
            {
                rollbackMessage = "Не удалось полностью отменить изменения.\n" +
                    "Проверьте выбранные операции.\n" + undoError.Message;
            }
            theUI.NXMessageBox.Show(TITLE, NXMessageBox.DialogType.Error,
                "Не удалось завершить обработку.\n\n" + ex.Message + "\n\n" + rollbackMessage);
            return;
        }

        theUI.NXMessageBox.Show(TITLE, NXMessageBox.DialogType.Information,
            "Готово.\n" +
            "Обработано PLANAR_MILL: " + processed + "\n" +
            "Пропущено (не Planar Mill): " + skippedNotPlanar + "\n" +
            "Элементов границы, где Tool Position включен: " + changedTotal + "\n" +
            (addStock ? "Stock включён для всех обработанных элементов. Припуск: " +
                stockValue.ToString("G", System.Globalization.CultureInfo.CurrentCulture) +
                " " + GetUnitLabel() + ".\n" : "Параметры Stock не изменялись.\n") +
            (REGENERATE_TOOLPATH ? "Траектории перегенерированы." : "Траектории НЕ перегенерировались."));
    }

    static string GetUnitLabel()
    {
        return workPart.PartUnits == BasePart.Units.Inches ? "in" : "mm";
    }

    static bool TryAskStock(out double stockValue)
    {
        stockValue = 0.0;
        double[] values = new double[] { 0.0 };
        string[] labels = new string[] { "Stock, " + GetUnitLabel() };

        while (true)
        {
            int unused = 0;
            int response;
            int lockStatus = theUf.Ui.LockUgAccess(UFConstants.UF_UI_FROM_CUSTOM);
            if (lockStatus != UFConstants.UF_UI_LOCK_SET)
            {
                theUI.NXMessageBox.Show(TITLE, NXMessageBox.DialogType.Warning,
                    "Закройте другие окна ввода NX и запустите скрипт снова.\nОперации не изменены.");
                return false;
            }
            try
            {
                // Native NX input: the first argument sets the cue; NX owns the window caption.
                response = theUf.Ui.GetInputDoubles(
                    TITLE + " — Введите величину припуска (" + GetUnitLabel() + ")",
                    labels, 1, values, ref unused);
            }
            finally
            {
                theUf.Ui.UnlockUgAccess(UFConstants.UF_UI_FROM_CUSTOM);
            }

            // GetInputDoubles: 1 = Back, 2 = Cancel,
            // 3 = Accept defaults, 4 = Accept entered values, 8 = Dialog unavailable.
            if (response == 1 || response == 2) return false;
            if (response != 3 && response != 4)
            {
                theUI.NXMessageBox.Show(TITLE, NXMessageBox.DialogType.Error,
                    "Не удалось открыть окно ввода припуска.\nОперации не изменены.");
                return false;
            }

            if (!double.IsNaN(values[0]) && !double.IsInfinity(values[0]))
            {
                stockValue = values[0];
                return true;
            }

            theUI.NXMessageBox.Show(TITLE, NXMessageBox.DialogType.Warning,
                "Введите конечное числовое значение припуска.");
            values[0] = 0.0;
        }
    }

    static int SetToolPositionOnForPlanarMill(PlanarMilling planarOp, bool addStock, double stockValue)
    {
        PlanarMillingBuilder b = null;
        int changed = 0;

        try
        {
            b = workPart.CAMSetup.CAMOperationCollection.CreatePlanarMillingBuilder(planarOp);

            BoundaryPlanarMill partBoundary = b.PartBoundary;
            BoundarySetList bList = partBoundary.BoundaryList;

            for (int si = 0; ; si++)
            {
                BoundarySetPlanarMill bs = null;
                try { bs = bList.FindItem(si) as BoundarySetPlanarMill; }
                catch { break; }

                if (bs == null) break;

                for (int mi = 0; ; mi++)
                {
                    BoundaryMemberSetPlanarMill mem = null;
                    try { mem = bs.BoundaryMemberList.FindItem(mi) as BoundaryMemberSetPlanarMill; }
                    catch { break; }

                    if (mem == null) break;

                    mem.ToolPosition = 0; // 0 = "Вкл" (как в твоём записанном journal)
                    if (addStock)
                    {
                        mem.CustomStock = true;
                        mem.Stock = stockValue;
                    }
                    changed++;
                }
            }

            b.Commit();
        }
        finally
        {
            if (b != null) b.Destroy();
        }

        return changed;
    }

    // ---------- Operation Navigator selection via UF UI ONT ----------

    static List<CamOperation> GetSelectedCamOperationsFromOperationNavigator()
    {
        List<CamOperation> result = new List<CamOperation>();

        Tag[] nodes = AskOntSelectedNodes();
        if (nodes == null || nodes.Length == 0) return result;

        // remove duplicates by Tag
        Dictionary<Tag, bool> seen = new Dictionary<Tag, bool>();

        for (int i = 0; i < nodes.Length; i++)
        {
            Tag objTag = AskOntObjectOfNode(nodes[i]);
            if (objTag == Tag.Null) objTag = nodes[i];

            if (seen.ContainsKey(objTag)) continue;
            seen[objTag] = true;

            TaggedObject tobj = null;
            try { tobj = NXObjectManager.Get(objTag); }
            catch { tobj = null; }

            if (tobj == null)
            {
                // fallback try node tag itself
                try { tobj = NXObjectManager.Get(nodes[i]); }
                catch { tobj = null; }
            }

            if (tobj == null) continue;

            CamOperation op = tobj as CamOperation;
            if (op != null) result.Add(op);
        }

        return result;
    }

    static Tag[] AskOntSelectedNodes()
    {
        object uiOnt = GetUfUiOntObject();
        if (uiOnt == null) return null;

        MethodInfo mi = uiOnt.GetType().GetMethod("AskSelectedNodes");
        if (mi == null) return null;

        ParameterInfo[] ps = mi.GetParameters();
        object[] args;

        if (ps.Length == 2)
        {
            args = new object[2];
            args[0] = 0;    // out int
            args[1] = null; // out Tag[]
            mi.Invoke(uiOnt, args);
            return args[1] as Tag[];
        }
        else if (ps.Length == 1)
        {
            args = new object[1];
            args[0] = null; // out Tag[]
            mi.Invoke(uiOnt, args);
            return args[0] as Tag[];
        }

        return null;
    }

    static Tag AskOntObjectOfNode(Tag node)
    {
        object uiOnt = GetUfUiOntObject();
        if (uiOnt == null) return Tag.Null;

        MethodInfo mi = uiOnt.GetType().GetMethod("AskObjectOfNode");
        if (mi != null)
        {
            ParameterInfo[] ps = mi.GetParameters();
            if (ps.Length == 2)
            {
                object[] args = new object[2];
                args[0] = node;
                args[1] = Tag.Null;
                try
                {
                    mi.Invoke(uiOnt, args);
                    return (Tag)args[1];
                }
                catch { }
            }
            else if (ps.Length == 1 && mi.ReturnType == typeof(Tag))
            {
                try
                {
                    object res = mi.Invoke(uiOnt, new object[] { node });
                    if (res is Tag) return (Tag)res;
                }
                catch { }
            }
        }

        return Tag.Null;
    }

    static object GetUfUiOntObject()
    {
        PropertyInfo pi = theUf.GetType().GetProperty("UiOnt");
        if (pi == null) return null;
        try { return pi.GetValue(theUf, null); }
        catch { return null; }
    }

    public static int GetUnloadOption(string dummy)
    {
        return (int)Session.LibraryUnloadOption.Immediately;
    }
}
