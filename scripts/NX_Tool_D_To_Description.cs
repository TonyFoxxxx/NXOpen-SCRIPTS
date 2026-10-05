// TOOL ⌀ — Description | Working file: NX_Tool_D_To_Description.cs
// SCRIPT_VERSION: V1.06
// Siemens NX / Designcenter, C# Journal.
// Run inside NX: Tools > Journal > Play (Alt+F8).
// Asks on every launch: full description (⌀*_T*_H*_D*) or diameter only (⌀*).
// Full examples: ⌀4_T4_H4_D4; ⌀4_T4_H4 if Cutcom Register is absent. Diameter only: ⌀4.
// All values are read from actual tool settings, never the tool name.
// Scope: supported tools in the CURRENT WORK PART. No final message on success.
// No reports, file writes, user attributes, automatic part save or toolpath generation.

using System;
using System.Collections.Generic;
using System.Globalization;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;

public class NXToolDDescriptionV1
{
    private const string SCRIPT_VERSION = "V1.06";
    private const string PREFIX = "\u2300"; // Diameter sign: ⌀10, ⌀6.5.
    private const string NUMBER_FORMAT = "0.######";
    private const string SCRIPT_NAME = "TOOL " + PREFIX + " — Description";
    private const string WINDOW_TITLE = SCRIPT_NAME + " — " + SCRIPT_VERSION;

    private const int TOOL_DIAMETER = 1000;      // UF_PARAM_TL_DIAMETER
    private const int TOOL_NUMBER = 1038;        // UF_PARAM_TL_NUMBER (T)
    private const int ADJUST_REGISTER = 1040;    // UF_PARAM_TL_ADJ_REG (H)
    private const int CUTCOM_REGISTER = 1041;    // UF_PARAM_TL_CUTCOM_REG (D)
    private const int CUTTER_DESCRIPTION = 1068; // UF_PARAM_TL_DESCRIPTION
    private const int INVALID_PARAM_INDEX = 1345036; // UF_CAM_ERROR_INVALID_INDEX

    private static Session session;
    private static UFSession uf;
    private static CAMSetup setup;

    public static void Main(string[] args)
    {
        session = Session.GetSession();
        uf = UFSession.GetUFSession();
        Part part = session.Parts.Work;
        if (part == null)
        {
            ShowError("Откройте деталь с CAM-проектом.");
            return;
        }

        try
        {
            setup = part.CAMSetup;
            if (setup == null)
                throw new InvalidOperationException("В рабочей детали нет CAM Setup.");
        }
        catch (Exception ex)
        {
            ShowError("Не удалось открыть CAM-проект. Перейдите в Manufacturing.\n\n" + ex.Message);
            return;
        }

        bool includeToolNumbers;
        try
        {
            if (!ChooseDescriptionMode(out includeToolNumbers))
                return;
        }
        catch (Exception ex)
        {
            ShowError("Не удалось выбрать вариант описания.\n\n" + ex.Message);
            return;
        }

        int changed = 0;
        int failed = 0;
        string firstError = null;
        string errorMessage = null;
        Session.UndoMarkId? batchMark = null;

        try
        {
            // Snapshot the collection before changing descriptions.
            List<Tool> tools = new List<Tool>();
            foreach (NCGroup group in setup.CAMGroupCollection)
            {
                Tool tool = group as Tool;
                if (tool != null && tool.OwningPart != null && tool.OwningPart.Tag == part.Tag)
                    tools.Add(tool);
            }

            batchMark = session.SetUndoMark(Session.MarkVisibility.Visible,
                WINDOW_TITLE + ": обновить описания инструментов");

            foreach (Tool tool in tools)
            {
                string name = tool.Name;
                Session.UndoMarkId itemMark = session.SetUndoMark(Session.MarkVisibility.Invisible,
                    WINDOW_TITLE);
                try
                {
                    if (Apply(tool, includeToolNumbers)) changed++;
                }
                catch (Exception ex)
                {
                    // Undo any partially changed description on this tool.
                    try { session.UndoToMark(itemMark, null); }
                    catch (Exception undoEx)
                    {
                        throw new InvalidOperationException("Не удалось отменить изменение инструмента "
                            + name + ": " + undoEx.Message + ". Исходная ошибка: " + ex.Message, undoEx);
                    }
                    failed++;
                    if (firstError == null) firstError = name + ": " + ex.Message;
                }
                finally
                {
                    try { session.DeleteUndoMark(itemMark, null); }
                    catch { /* A rollback may already have removed this mark. */ }
                }
            }

            if (changed > 0)
            {
                int errors = session.UpdateManager.DoUpdate(batchMark.Value);
                if (errors != 0)
                    throw new InvalidOperationException("NX сообщил об ошибках обновления: " + errors);
            }
            else
            {
                session.DeleteUndoMark(batchMark.Value, null);
                batchMark = null;
            }
        }
        catch (Exception ex)
        {
            errorMessage = "Не удалось завершить обновление описаний.\n\n" + ex.Message;
            if (batchMark.HasValue)
            {
                try
                {
                    session.UndoToMark(batchMark.Value, null);
                    session.DeleteUndoMark(batchMark.Value, null);
                    errorMessage += "\n\nИзменения этого запуска отменены.";
                }
                catch (Exception undoEx)
                {
                    errorMessage += "\n\nНе удалось полностью отменить запуск: " + undoEx.Message;
                }
            }
        }

        if (errorMessage == null && failed > 0)
        {
            errorMessage = "Не удалось изменить описание инструментов: " + failed
                + ". Изменения этих инструментов отменены.\n\nПервая ошибка:\n" + firstError;
        }

        try { uf.UiOnt.Refresh(); }
        catch (Exception ex)
        {
            if (errorMessage != null) errorMessage += "\n\n";
            errorMessage += "Не удалось обновить Operation Navigator: " + ex.Message;
        }

        if (errorMessage != null) ShowError(errorMessage);
    }

    private static bool ChooseDescriptionMode(out bool includeToolNumbers)
    {
        // Custom responses avoid treating standard NX cancellation codes as a choice.
        const int fullDescriptionResponse = 11;
        const int diameterOnlyResponse = 12;
        const int cancelResponse = 13;

        includeToolNumbers = false;
        string[] messages = { "Какой вариант описания использовать?" };
        UFUi.MessageButtons buttons = new UFUi.MessageButtons();
        buttons.button1 = true;
        buttons.label1 = "1) " + PREFIX + "*_T*_H*_D*";
        buttons.response1 = fullDescriptionResponse;
        buttons.button2 = true;
        buttons.label2 = "2) " + PREFIX + "*";
        buttons.response2 = diameterOnlyResponse;
        buttons.button3 = true;
        buttons.label3 = "Отмена";
        buttons.response3 = cancelResponse;

        int response;
        uf.Ui.MessageDialog(WINDOW_TITLE, UiMessageDialogType.UiMessageQuestion,
            messages, messages.Length, false, ref buttons, out response);

        if (response == fullDescriptionResponse)
        {
            includeToolNumbers = true;
            return true;
        }
        // Cancellation or any other response exits before undo marks or edits.
        return response == diameterOnlyResponse;
    }

    private static bool Apply(Tool tool, bool includeToolNumbers)
    {
        Tool.Types type;
        Tool.Subtypes subtype;
        tool.GetTypeAndSubtype(out type, out subtype);
        // These families expose a nominal cutting diameter through this parameter.
        if (type != Tool.Types.Mill && type != Tool.Types.Drill && type != Tool.Types.Barrel
            && type != Tool.Types.Tcutter && type != Tool.Types.MillForm)
            return false;

        double diameter;
        uf.Param.AskDoubleValue(tool.Tag, TOOL_DIAMETER, out diameter);
        if (Double.IsNaN(diameter) || Double.IsInfinity(diameter) || diameter <= 0.0
            || Math.Round(diameter, 6, MidpointRounding.AwayFromZero) <= 0.0)
            return false;

        int toolNumber = 0;
        int adjustRegister = 0;
        int? cutcomRegister = null;
        string value = PREFIX + diameter.ToString(NUMBER_FORMAT, CultureInfo.InvariantCulture);
        if (includeToolNumbers)
        {
            // T and H are required; D is optional only when NX reports it as absent.
            // A readable zero remains D0. Other read errors must not be hidden.
            toolNumber = ReadToolInteger(tool, TOOL_NUMBER, "Tool Number");
            adjustRegister = ReadToolInteger(tool, ADJUST_REGISTER, "Adjust Register");
            cutcomRegister = ReadOptionalCutcomRegister(tool);
            value += "_T" + toolNumber.ToString(CultureInfo.InvariantCulture)
                + "_H" + adjustRegister.ToString(CultureInfo.InvariantCulture);
            if (cutcomRegister.HasValue)
                value += "_D" + cutcomRegister.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (ReadGeneralDescription(tool) == value && ReadCutterDescription(tool) == value)
            return false;

        WriteDescriptions(tool, value);
        if (ReadGeneralDescription(tool) != value || ReadCutterDescription(tool) != value)
            throw new InvalidOperationException("NX не подтвердил записанный текст описания.");

        double afterDiameter;
        uf.Param.AskDoubleValue(tool.Tag, TOOL_DIAMETER, out afterDiameter);
        if (Double.IsNaN(afterDiameter) || Double.IsInfinity(afterDiameter)
            || Math.Abs(afterDiameter - diameter) > Math.Max(1e-9, Math.Abs(diameter) * 1e-12))
            throw new InvalidOperationException("Контроль диаметра после записи не пройден.");

        if (includeToolNumbers && (ReadToolInteger(tool, TOOL_NUMBER, "Tool Number") != toolNumber
            || ReadToolInteger(tool, ADJUST_REGISTER, "Adjust Register") != adjustRegister
            || ReadOptionalCutcomRegister(tool) != cutcomRegister))
            throw new InvalidOperationException("Контроль номеров T/H/D после записи не пройден.");

        return true;
    }

    private static int ReadToolInteger(Tool tool, int parameterIndex, string fieldName)
    {
        try
        {
            int value;
            uf.Param.AskIntValue(tool.Tag, parameterIndex, out value);
            return value;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось прочитать " + fieldName
                + " в настройках инструмента: " + ex.Message, ex);
        }
    }

    private static int? ReadOptionalCutcomRegister(Tool tool)
    {
        try
        {
            UFParam.Status status;
            uf.Param.AskParamStatus(tool.Tag, CUTCOM_REGISTER, out status);
            if (status == UFParam.Status.InvalidIndex)
                return null;

            int value;
            uf.Param.AskIntValue(tool.Tag, CUTCOM_REGISTER, out value);
            return value;
        }
        catch (NXException ex)
        {
            // An explicit invalid-index error also identifies an absent parameter.
            if (ex.ErrorCode == INVALID_PARAM_INDEX)
                return null;

            throw new InvalidOperationException("Не удалось прочитать Cutcom Register"
                + " в настройках инструмента: " + ex.Message, ex);
        }
    }

    private static string ReadGeneralDescription(Tool tool)
    {
        NCGroupBuilder builder = setup.CAMGroupCollection.CreateNcgroupBuilder(tool);
        try { return builder.Description ?? ""; }
        finally { builder.Destroy(); }
    }

    private static string ReadCutterDescription(Tool tool)
    {
        string value;
        uf.Param.AskStrValue(tool.Tag, CUTTER_DESCRIPTION, out value);
        return value ?? "";
    }

    private static void WriteDescriptions(Tool tool, string value)
    {
        // General Description and Cutter Description can be distinct fields.
        if (ReadGeneralDescription(tool) != value)
        {
            NCGroupBuilder builder = setup.CAMGroupCollection.CreateNcgroupBuilder(tool);
            try
            {
                builder.Description = value;
                builder.Commit();
            }
            finally { builder.Destroy(); }
        }
        if (ReadCutterDescription(tool) != value)
            uf.Param.SetStrValue(tool.Tag, CUTTER_DESCRIPTION, value);
    }

    private static void ShowError(string message)
    {
        UI.GetUI().NXMessageBox.Show(WINDOW_TITLE,
            NXMessageBox.DialogType.Error, message);
    }

    public static int GetUnloadOption(string dummy)
    {
        return (int)Session.LibraryUnloadOption.Immediately;
    }
}
