// Экспорт DXF из текущего вида
// SCRIPT_VERSION: V1.08
// Рабочий файл: NX_Export_Current_View_To_DXF.cs
// Файл для выдачи: NX_Export_Current_View_To_DXF_V1.08.cs
// Siemens NX / Designcenter: журнал C#.
// Запуск: Tools > Journal > Play (Alt+F8).
// Введите имя файла. Результат: <папка отображаемого проекта>\<имя>.dxf.
// Текущая ориентация модели, ортогональная 2D-проекция, масштаб 1:1.
// Не создаёт собственных отчётов, настроек, каталогов или временных файлов.
// ВНИМАНИЕ: штатный переводчик NX использует внутренние служебные файлы.
// Перевод DXF запускается штатным переводчиком NX в фоне.
// Подготовка и очистка геометрии выполняются в основном потоке NX.
// Проверка готового DXF и очистка двух штатных журналов — только файловые операции.
// Резервные данные старых журналов находятся в памяти; дополнительных файлов нет.
// Прежний DXF восстанавливается при ошибке ДО запуска фонового перевода.
// После запуска результат перевода контролируется штатной фоновой задачей NX.
// Проект не сохраняется. Служебные вид и лист существуют только в памяти.
// Имя проекта выделено; масштаб 1:1; в конце Fully Shaded с прежним Face Edges.
// при предварительном выделении рёбер/кривых доступен третий режим.
// Сначала выбирается режим, затем имя файла; отмена доступна в окне имени.
// StringList: только управляемые массивы C#.
// ожидание переводчика отключено, пошаговая диагностика отключена.
// Видимые объекты запоминаются ДО создания служебного вида и смены приложения.
// Для единственной петли не создаётся область для проверки вложенности.
// Чтобы вернуть пошаговую диагностику, добавьте #define DXF_TRACE первой строкой.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NXOpen;
using NXOpen.Drawings;
using NXOpen.UF;
using NXOpen.Preferences;

public class NX_Export_Current_View_To_DXF
{
    private const string SCRIPT_VERSION = "V1.08";
    private const string Title = "Экспорт DXF — " + SCRIPT_VERSION;
    private enum ExportMode { Cancel, VisibleEdges, OuterContour, SelectedCurves }
    private static string diagnosticRun = "";
    private static int diagnosticSequence;
    private static bool diagnosticWriteFailed;

    [System.Diagnostics.Conditional("DXF_TRACE")]
    private static void TraceStep(string step)
    {
        // Только штатный syslog открытого сеанса NX. Никаких File/Open/Append.
        // Ошибка диагностики не должна прерывать восстановление модели в finally.
        try
        {
            diagnosticSequence++;
            Session.GetSession().LogFile.WriteLine("[DXF " + SCRIPT_VERSION + " DIAG " + diagnosticRun + "] "
                + diagnosticSequence.ToString("D6", CultureInfo.InvariantCulture) + " "
                + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                + " " + step);
        }
        catch (Exception) { diagnosticWriteFailed = true; }
    }

    public static void Main(string[] args)
    {
        Session session = Session.GetSession();
        UI ui = UI.GetUI();
        diagnosticRun = Guid.NewGuid().ToString("N").Substring(0, 8);
        diagnosticSequence = 0;
        diagnosticWriteFailed = false;
        TraceStep("MAIN ENTER");

        try
        {
            // Снимок выделения ДО любых диалогов: окно ввода или выбор режима
            // могут снять подсветку, но экспорт должен использовать исходный выбор.
            NXObject[] selectedCurves = GetPreselectedCurves(ui);
            string[] previousCleanupWarnings = BackgroundLogCleanup.TakeWarnings();
            if (previousCleanupWarnings.Length > 0)
                ui.NXMessageBox.Show(Title, NXMessageBox.DialogType.Warning,
                    String.Join("\n\n", previousCleanupWarnings));
            if (diagnosticWriteFailed)
                throw new InvalidOperationException("Не удалось записать контрольную точку "
                    + "в системный журнал NX. Диагностический экспорт не начат.");
            TraceStep("BEGIN C001 session.Parts.Display");
            Part part = session.Parts.Display;
            TraceStep("END C001 session.Parts.Display");
            if (part == null || session.Parts.Work == null)
                throw new InvalidOperationException("Сначала откройте проект NX.");

            TraceStep("BEGIN C002 part.FullPath");
            string projectPath = part.FullPath;
            TraceStep("END C002 part.FullPath");
            if (String.IsNullOrEmpty(projectPath) || !Path.IsPathRooted(projectPath)
                || !File.Exists(projectPath))
                throw new InvalidOperationException(
                    "Сначала сохраните проект как файл .prt.\n"
                    + "Для экспорта нужна существующая папка проекта.");

            string projectDirectory = Path.GetDirectoryName(projectPath);
            if (String.IsNullOrEmpty(projectDirectory) || !Directory.Exists(projectDirectory))
                throw new InvalidOperationException("Не удалось определить папку проекта.");

            TraceStep("BEGIN C003 part.Views.WorkView");
            ModelingView currentView = part.Views.WorkView as ModelingView;
            TraceStep("END C003 part.Views.WorkView");
            if (currentView == null)
                throw new InvalidOperationException(
                    "Перейдите к виду 3D-модели и запустите скрипт снова.");

            ExportMode mode = AskExportMode(selectedCurves.Length);
            TraceStep("MODE=" + mode.ToString());
            if (mode == ExportMode.Cancel)
                return;

            // У штатного окна режимов три кнопки. При наличии выделения все три
            // задают режим экспорта; гарантированная отмена остаётся здесь,
            // до построения геометрии и запуска переводчика.
            string outputPath = AskOutputPath(ui, projectDirectory,
                Path.GetFileNameWithoutExtension(projectPath));
            if (outputPath == null)
                return;

            TraceStep("BEGIN FindSettingsFile");
            string settingsPath = FindSettingsFile(session);
            TraceStep("END FindSettingsFile");
            TraceStep("BEGIN ExportCurrentView");
            BackgroundLogCleanup background = new BackgroundLogCleanup(outputPath);
            try
            {
                ExportCurrentView(session, part, currentView, settingsPath, outputPath, mode, ui,
                    background, selectedCurves);
            }
            finally { background.ScriptReturned(); }
            TraceStep("END ExportCurrentView");
        }
        catch (Exception ex)
        {
            TraceStep("MANAGED EXCEPTION " + ex.ToString());
            TraceStep("BEGIN C004 ui.NXMessageBox.Show");
            ui.NXMessageBox.Show(Title, NXMessageBox.DialogType.Error, ex.Message);
            TraceStep("END C004 ui.NXMessageBox.Show");
        }
        finally { TraceStep("MAIN EXIT"); }
    }

    private static string AskOutputPath(UI ui, string directory, string projectName)
    {
        string enteredName = projectName;
        while (true)
        {
            // имя проекта без .prt; NXInputBox выделяет весь начальный текст.
            TraceStep("BEGIN NXInputBox.GetInputString");
            enteredName = NXOpenUI.NXInputBox.GetInputString(
                "Введите имя файла", Title, enteredName);
            TraceStep("END NXInputBox.GetInputString");
            // Отмена или пустая строка: закончить без экспорта.
            if (String.IsNullOrEmpty(enteredName))
                return null;

            string error;
            string filename = NormalizeFilename(enteredName, out error);
            if (filename == null)
            {
                TraceStep("BEGIN C005 ui.NXMessageBox.Show");
                ui.NXMessageBox.Show(Title, NXMessageBox.DialogType.Warning, error);
                TraceStep("END C005 ui.NXMessageBox.Show");
                continue;
            }

            string target = Path.Combine(directory, filename);
            if (Directory.Exists(target))
            {
                TraceStep("BEGIN C006 ui.NXMessageBox.Show");
                ui.NXMessageBox.Show(Title, NXMessageBox.DialogType.Warning,
                    "Папка с таким именем уже существует. Введите другое имя файла.");
                TraceStep("END C006 ui.NXMessageBox.Show");
                continue;
            }
            if (File.Exists(target))
            {
                TraceStep("BEGIN C007 ui.NXMessageBox.Show");
                int answer = ui.NXMessageBox.Show(Title, NXMessageBox.DialogType.Question,
                    "Файл уже существует:\n" + target + "\n\nЗаменить его?");
                TraceStep("END C007 ui.NXMessageBox.Show");
                if (answer != 1)
                    continue;
            }
            return target;
        }
    }

    private static NXObject[] GetPreselectedCurves(UI ui)
    {
        List<NXObject> curves = new List<NXObject>();
        HashSet<Tag> found = new HashSet<Tag>();
        Selection selection = ui.SelectionManager;
        int count = selection.GetNumSelectedObjects();
        for (int i = 0; i < count; i++)
        {
            NXObject obj = selection.GetSelectedTaggedObject(i) as NXObject;
            // Тела, грани, точки и элементы дерева не расширяем в набор рёбер.
            if ((obj is Edge || obj is Curve) && found.Add(obj.Tag)) curves.Add(obj);
        }
        return curves.ToArray();
    }

    private static ExportMode AskExportMode(int selectedCurveCount)
    {
        // Штатное модальное окно NX, без WinForms и дополнительных файлов интерфейса.
        bool hasSelectedCurves = selectedCurveCount > 0;
        UFUi.MessageButtons buttons = new UFUi.MessageButtons();
        buttons.button1 = true;
        buttons.label1 = "Все видимые рёбра";
        buttons.response1 = 11;
        buttons.button2 = true;
        buttons.label2 = "Только внешний контур модели";
        buttons.response2 = 22;
        buttons.button3 = true;
        buttons.label3 = hasSelectedCurves ? "Только выбранные кривые" : "Отмена";
        buttons.response3 = hasSelectedCurves ? 33 : 99;
        List<string> lines = new List<string> {
            "Что вывести в DXF?",
            "Все видимые рёбра — с видимыми отверстиями, уступами и карманами.",
            "Только внешний контур модели — без отверстий и внутренних рёбер."
        };
        if (hasSelectedCurves)
        {
            lines.Add("Только выбранные кривые — предварительно выделенные рёбра/кривые: "
                + selectedCurveCount.ToString(CultureInfo.InvariantCulture) + ".");
            lines.Add("После выбора режима появится окно имени файла с кнопкой «Отмена».");
        }
        int response;
        TraceStep("BEGIN UFUi.MessageDialog");
        UFSession.GetUFSession().Ui.MessageDialog(Title,
            UiMessageDialogType.UiMessageQuestion, lines.ToArray(), lines.Count,
            false, ref buttons, out response);
        TraceStep("END UFUi.MessageDialog");
        TraceStep("MODE RESPONSE=" + response.ToString(CultureInfo.InvariantCulture));
        return response == 11 ? ExportMode.VisibleEdges
            : response == 22 ? ExportMode.OuterContour
            : hasSelectedCurves && response == 33 ? ExportMode.SelectedCurves
            : ExportMode.Cancel;
    }

    private static string NormalizeFilename(string input, out string error)
    {
        error = null;
        string name = (input ?? "").Trim();
        if (name.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);

        if (String.IsNullOrWhiteSpace(name) || name == "." || name == "..")
            error = "Введите имя файла.";
        else if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                 || name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0)
            error = "Введите только имя файла, без пути.\nНедопустимые символы: < > : \" / \\ | ? *";
        else if (name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal))
            error = "Имя файла не должно заканчиваться точкой или пробелом.";
        else
        {
            string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL"
                || stem == "CONIN$" || stem == "CONOUT$"
                || ((stem.StartsWith("COM", StringComparison.Ordinal)
                     || stem.StartsWith("LPT", StringComparison.Ordinal))
                    && stem.Length == 4 && "123456789¹²³".IndexOf(stem[3]) >= 0))
                error = "Это имя зарезервировано Windows. Введите другое имя.";
        }

        return error == null ? name + ".dxf" : null;
    }

    private static string FindSettingsFile(Session session)
    {
        List<string> roots = new List<string>();
        string[] variables = { "UGII_BASE_DIR", "UGII_DXFDWG_DIR", "DXFDWG_DIR", "UGII_ROOT_DIR" };
        foreach (string variable in variables)
        {
            string value = null;
            try { TraceStep("BEGIN C008 session.GetEnvironmentVariableValue");
                  value = session.GetEnvironmentVariableValue(variable);
                  TraceStep("END C008 session.GetEnvironmentVariableValue"); }
            catch (NXException) { /* Переменная необязательна; проверяем окружение Windows. */ }
            if (String.IsNullOrWhiteSpace(value))
                value = Environment.GetEnvironmentVariable(variable);
            if (!String.IsNullOrWhiteSpace(value))
                roots.Add(value.Trim().Trim('"'));
        }
        // Путь NXOpen.dll также работает при отсутствии переменных в окружении Windows.
        string assemblyDirectory = Path.GetDirectoryName(typeof(Session).Assembly.Location);
        if (!String.IsNullOrEmpty(assemblyDirectory))
            roots.Add(assemblyDirectory);

        foreach (string root in roots)
        {
            string directory = root;
            for (int level = 0; level < 3 && !String.IsNullOrEmpty(directory); level++)
            {
                string candidate = Path.Combine(directory, "DXFDWG", "dxfdwg.def");
                if (File.Exists(candidate))
                    return candidate;
                candidate = Path.Combine(directory, "dxfdwg.def");
                if (File.Exists(candidate))
                    return candidate;
                DirectoryInfo parent = Directory.GetParent(directory);
                directory = parent == null ? null : parent.FullName;
            }
        }
        throw new FileNotFoundException(
            "Не найден штатный файл NX DXFDWG\\dxfdwg.def.\n"
            + "Проверьте установку модуля экспорта AutoCAD DXF/DWG.");
    }

    private static void ExportCurrentView(Session session, Part part, ModelingView currentView,
        string settingsPath, string outputPath, ExportMode mode, UI ui,
        BackgroundLogCleanup background, NXObject[] selectedCurves)
    {
        // Резервные данные существуют только в оперативной памяти.
        FileSnapshot previousOutput = new FileSnapshot(outputPath);
        List<FileSnapshot> translatorLogs = new List<FileSnapshot>();
        translatorLogs.Add(new FileSnapshot(Path.ChangeExtension(outputPath, ".log")));
        translatorLogs.Add(new FileSnapshot(outputPath + ".log"));
        background.SetLogs(translatorLogs.ToArray());

        UFSession uf = UFSession.GetUFSession();
        EnsureUpdateAvailable(session);
        TraceStep("BEGIN C009 session.Parts.Work");
        Part originalWorkPart = session.Parts.Work;
        TraceStep("END C009 session.Parts.Work");
        TraceStep("BEGIN C010 session.ApplicationName");
        string originalApplication = session.ApplicationName;
        TraceStep("END C010 session.ApplicationName");
        TraceStep("BEGIN C011 currentView.Tag");
        Tag originalViewTag = currentView.Tag;
        TraceStep("END C011 currentView.Tag");
        TraceStep("BEGIN C012 currentView.Matrix");
        Matrix3x3 originalMatrix = currentView.Matrix;
        TraceStep("END C012 currentView.Matrix");
        TraceStep("BEGIN C013 currentView.Origin");
        Point3d originalOrigin = currentView.Origin;
        TraceStep("END C013 currentView.Origin");
        TraceStep("BEGIN C014 currentView.Scale");
        double originalScale = currentView.Scale;
        TraceStep("END C014 currentView.Scale");
        // ShadedWithEdges и ShadedWithBodyColorEdges сохраняют включённый Face Edges.
        // Запоминаем состояние до создания служебного вида и работы переводчика.
        View.RenderingStyleType originalRenderingStyle = currentView.RenderingStyle;
        TraceStep("BEGIN C015 currentView.VisualizationVisualPreferences.DisplayAppearance");
        ViewVisualizationVisual.DisplayAppearanceOptions originalAppearance =
            currentView.VisualizationVisualPreferences.DisplayAppearance;
        TraceStep("END C015 currentView.VisualizationVisualPreferences.DisplayAppearance");
        int originalDisplayState;
        Tag originalDrawing = Tag.Null;
        bool originalBorderDisplay = true;
        originalDisplayState = UFConstants.UF_DRAW_MODELING_VIEW;
        if (mode == ExportMode.VisibleEdges)
        {
            TraceStep("BEGIN C016 uf.Draw.AskDisplayState");
            uf.Draw.AskDisplayState(out originalDisplayState);
            TraceStep("END C016 uf.Draw.AskDisplayState");
            TraceStep("BEGIN C017 part.DrawingSheets.CurrentDrawingSheet");
            DrawingSheet activeSheet = part.DrawingSheets.CurrentDrawingSheet;
            TraceStep("END C017 part.DrawingSheets.CurrentDrawingSheet");
            if (activeSheet != null) {
                                         TraceStep("BEGIN C018 activeSheet.Tag");
                                         originalDrawing = activeSheet.Tag;
                                         TraceStep("END C018 activeSheet.Tag");
                                     }
            TraceStep("BEGIN C019 uf.Draw.AskBorderDisplay");
            uf.Draw.AskBorderDisplay(out originalBorderDisplay);
            TraceStep("END C019 uf.Draw.AskBorderDisplay");
        }

        // Запрашиваем видимость ровно один раз, пока исходный вид отображается.
        // После SaveAs/перехода в черчение этот же объект View может уже не быть
        // отображаемым; повторный AskVisibleObjects тогда вызывает ошибку NX.
        TraceStep("BEGIN currentView.AskVisibleObjects snapshot");
        DisplayableObject[] visibleObjects = mode == ExportMode.SelectedCurves
            ? new DisplayableObject[0] : currentView.AskVisibleObjects();
        TraceStep("END currentView.AskVisibleObjects snapshot");
        // Размер листа зависит от габарита модели, а масштаб проекции всегда 1:1.
        TraceStep("BEGIN GetVisibleBodies");
        Body[] visibleBodies = mode == ExportMode.SelectedCurves
            ? new Body[0] : GetVisibleBodies(visibleObjects);
        TraceStep("END GetVisibleBodies; bodies=" + visibleBodies.Length.ToString(CultureInfo.InvariantCulture));
        TraceStep("BEGIN GetSheetSize");
        double sheetSize = mode == ExportMode.VisibleEdges
            ? GetSheetSize(uf, part, visibleBodies) : 0.0;
        TraceStep("END GetSheetSize");
        TraceStep("BEGIN VisibleLayers");
        string layerMask = VisibleLayers(part);
        TraceStep("END VisibleLayers");
        View exportView = null;
        DrawingSheet exportSheet = null;
        DxfdwgCreator exporter = null;
        bool outputTouched = false;
        bool complete = false;
        bool draftingTouched = false;
        bool borderChanged = false;
        string stage = "Подготовка вида";
        string failure = null;
        List<string> cleanupErrors = new List<string>();
        List<DisplayableObject> temporarilyBlanked = new List<DisplayableObject>();
        List<Tag> temporaryCurves = new List<Tag>();
        List<Tag> temporaryRegions = new List<Tag>();
        List<Tag> temporaryAuxiliaries = new List<Tag>();
        Dictionary<int, NXOpen.Layer.State> changedLayers =
            new Dictionary<int, NXOpen.Layer.State>();

        try
        {
            if (session.Parts.Work.Tag != part.Tag)
                {
                    TraceStep("BEGIN C020 session.Parts.SetWork");
                    session.Parts.SetWork(part);
                    TraceStep("END C020 session.Parts.SetWork");
                }

            // Снимок фактического ракурса, в том числе после поворота мышью.
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant();
            TraceStep("BEGIN C021 part.Views.SaveAsPreservingCase");
            exportView = part.Views.SaveAsPreservingCase(
                currentView, "DXF_VIEW_" + suffix, true, false);
            TraceStep("END C021 part.Views.SaveAsPreservingCase");
            TraceStep("BEGIN C022 exportView.ChangePerspective");
            exportView.ChangePerspective(false);
            TraceStep("END C022 exportView.ChangePerspective");

            NXObject[] outline = null;
            if (mode == ExportMode.VisibleEdges)
            {
                stage = "Подготовка видимых объектов";
                // В DXF нужны рёбра тел: самостоятельные кривые и эскизы исключаем.
                // Исходную видимость каждого такого объекта возвращаем в finally.
                TraceStep("BEGIN blank auxiliary objects from snapshot");
                foreach (DisplayableObject obj in visibleObjects)
                {
                    if ((obj is Curve || obj is Sketch || obj is NXOpen.Point) && !obj.IsBlanked)
                    {
                        temporarilyBlanked.Add(obj);
                        TraceStep("BEGIN C023 obj.Blank");
                        obj.Blank();
                        TraceStep("END C023 obj.Blank");
                    }
                }
                TraceStep("END blank auxiliary objects from snapshot");
                TraceStep("BEGIN C024 part.PartUnits");
                DrawingSheet.Unit units = part.PartUnits == BasePart.Units.Millimeters
                    ? DrawingSheet.Unit.Millimeters : DrawingSheet.Unit.Inches;
                TraceStep("END C024 part.PartUnits");
                // Инициализируем черчение до создания листа. Флаг ставим до перехода.
                stage = "Переход к черчению";
                draftingTouched = true;
                if (session.ApplicationName != "UG_APP_DRAFTING")
                {
                    TraceStep("BEGIN C025 session.ApplicationSwitchImmediate");
                    session.ApplicationSwitchImmediate("UG_APP_DRAFTING");
                    TraceStep("END C025 session.ApplicationSwitchImmediate");
                    if (session.ApplicationName != "UG_APP_DRAFTING")
                        throw new InvalidOperationException("NX не завершил переход в черчение.");
                    TraceStep("BEGIN C026 part.Drafting.EnterDraftingApplication");
                    part.Drafting.EnterDraftingApplication();
                    TraceStep("END C026 part.Drafting.EnterDraftingApplication");
                }
                // Пустой лист, без шаблона, рамки и основной надписи. Файл .prt не создаётся.
                stage = "Создание служебного листа";
                TraceStep("BEGIN C027 part.DrawingSheets.InsertSheet");
                exportSheet = part.DrawingSheets.InsertSheet("DXF_SHEET_" + suffix,
                    units, sheetSize, sheetSize, 1.0, 1.0, DrawingSheet.ProjectionAngleType.FirstAngle);
                TraceStep("END C027 part.DrawingSheets.InsertSheet");
                TraceStep("BEGIN exportSheet.Open");
                exportSheet.Open();
                TraceStep("END exportSheet.Open");
                borderChanged = true;
                TraceStep("BEGIN C028 uf.Draw.SetBorderDisplay");
                uf.Draw.SetBorderDisplay(false);
                TraceStep("END C028 uf.Draw.SetBorderDisplay");

                stage = "Создание проекции видимых рёбер";
                Tag drawingView = CreateVisibleEdgesView(uf, exportSheet, exportView, sheetSize);
                // UpdateOneView вычисляет силуэты и скрытие рёбер поверхностями.
                stage = "Расчёт скрытия рёбер";
                TraceStep("BEGIN C029 uf.Draw.UpdateOneView");
                uf.Draw.UpdateOneView(exportSheet.Tag, drawingView);
                TraceStep("END C029 uf.Draw.UpdateOneView");
            }
            else if (mode == ExportMode.SelectedCurves)
            {
                stage = "Проекция выбранных кривых";
                outline = CreateSelectedCurvesProjection(uf, part, exportView, selectedCurves,
                    temporaryCurves, temporaryAuxiliaries);
            }
            else
            {
                stage = "Построение внешнего контура";
                outline = CreateOuterContour(uf, part, exportView, visibleBodies,
                    temporaryCurves, temporaryRegions, temporaryAuxiliaries, changedLayers);
            }

            stage = "Настройка экспорта DXF";
            TraceStep("BEGIN C030 session.DexManager.CreateDxfdwgCreator");
            exporter = session.DexManager.CreateDxfdwgCreator();
            TraceStep("END C030 session.DexManager.CreateDxfdwgCreator");
            TraceStep("BEGIN C031 exporter.SettingsFile");
            exporter.SettingsFile = settingsPath;
            TraceStep("END C031 exporter.SettingsFile");
            TraceStep("BEGIN C032 exporter.ExportDestination");
            exporter.ExportDestination = BaseCreator.ExportDestinationOption.NativeFileSystem;
            TraceStep("END C032 exporter.ExportDestination");
            TraceStep("BEGIN C033 exporter.ExportFrom");
            exporter.ExportFrom = DxfdwgCreator.ExportFromOption.DisplayPart;
            TraceStep("END C033 exporter.ExportFrom");
            TraceStep("BEGIN C034 exporter.InputFile");
            exporter.InputFile = part.FullPath;
            TraceStep("END C034 exporter.InputFile");
            TraceStep("BEGIN C035 exporter.OutputFile");
            exporter.OutputFile = outputPath;
            TraceStep("END C035 exporter.OutputFile");
            TraceStep("BEGIN C036 exporter.OutputFileType");
            exporter.OutputFileType = DxfdwgCreator.OutputFileTypeOption.Dxf;
            TraceStep("END C036 exporter.OutputFileType");
            TraceStep("BEGIN C037 exporter.AutoCADRevision");
            exporter.AutoCADRevision = DxfdwgCreator.AutoCADRevisionOptions.R2007;
            TraceStep("END C037 exporter.AutoCADRevision");
            // Все видимые рёбра: чертёжный вид. Остальные режимы: заданные кривые.
            TraceStep("BEGIN C038 exporter.ExportData");
            exporter.ExportData = mode == ExportMode.VisibleEdges
                ? DxfdwgCreator.ExportDataOption.Drawing
                : DxfdwgCreator.ExportDataOption.Modeling;
            TraceStep("END C038 exporter.ExportData");
            TraceStep("BEGIN C039 exporter.ExportAs");
            exporter.ExportAs = DxfdwgCreator.ExportAsOption.TwoD;
            TraceStep("END C039 exporter.ExportAs");
            TraceStep("BEGIN C040 exporter.OutputTo");
            exporter.OutputTo = DxfdwgCreator.OutputToOption.Modeling;
            TraceStep("END C040 exporter.OutputTo");
            TraceStep("BEGIN C041 exporter.ExportScaleOption");
            exporter.ExportScaleOption = DxfdwgCreator.ExportScaleOptions.UserSpecified;
            TraceStep("END C041 exporter.ExportScaleOption");
            TraceStep("BEGIN C042 exporter.ExportScaleValue");
            exporter.ExportScaleValue = 1.0;
            TraceStep("END C042 exporter.ExportScaleValue");
            TraceStep("BEGIN C043 exporter.ExportSplinesAs");
            exporter.ExportSplinesAs = DxfdwgCreator.ExportSplinesAsOptions.Spline;
            TraceStep("END C043 exporter.ExportSplinesAs");
            TraceStep("BEGIN C044 exporter.DrawingList");
            exporter.DrawingList = exportSheet == null ? "" : exportSheet.Name;
            TraceStep("END C044 exporter.DrawingList");
            TraceStep("BEGIN C045 exporter.ViewList");
            exporter.ViewList = mode != ExportMode.VisibleEdges ? exportView.Name : "";
            TraceStep("END C045 exporter.ViewList");
            TraceStep("BEGIN C046 exporter.ViewEditMode");
            exporter.ViewEditMode = true;
            TraceStep("END C046 exporter.ViewEditMode");
            TraceStep("BEGIN C047 exporter.LayerMask");
            exporter.LayerMask = layerMask;
            TraceStep("END C047 exporter.LayerMask");
            TraceStep("BEGIN C048 exporter.ExportSelectionBlock.SelectionScope");
            exporter.ExportSelectionBlock.SelectionScope = mode == ExportMode.VisibleEdges
                ? ObjectSelector.Scope.EntirePart : ObjectSelector.Scope.SelectedObjects;
            TraceStep("END C048 exporter.ExportSelectionBlock.SelectionScope");
            if (outline != null)
                {
                    TraceStep("BEGIN C049 exporter.ExportSelectionBlock.SelectionComp.SetArray");
                    exporter.ExportSelectionBlock.SelectionComp.SetArray(outline);
                    TraceStep("END C049 exporter.ExportSelectionBlock.SelectionComp.SetArray");
                }
            TraceStep("BEGIN C050 exporter.ObjectTypes.Curves");
            exporter.ObjectTypes.Curves = true;
            TraceStep("END C050 exporter.ObjectTypes.Curves");
            TraceStep("BEGIN C051 exporter.ObjectTypes.Annotations");
            exporter.ObjectTypes.Annotations = false;
            TraceStep("END C051 exporter.ObjectTypes.Annotations");
            TraceStep("BEGIN C052 exporter.ObjectTypes.Structures");
            exporter.ObjectTypes.Structures = true;
            TraceStep("END C052 exporter.ObjectTypes.Structures");
            TraceStep("BEGIN C053 exporter.FlattenAssembly");
            exporter.FlattenAssembly = true;
            TraceStep("END C053 exporter.FlattenAssembly");
            TraceStep("BEGIN C054 exporter.OverlappingEntities");
            exporter.OverlappingEntities = true;
            TraceStep("END C054 exporter.OverlappingEntities");
            TraceStep("BEGIN C055 exporter.FileSaveFlag");
            exporter.FileSaveFlag = false;
            TraceStep("END C055 exporter.FileSaveFlag");
            TraceStep("BEGIN C056 exporter.ProcessHoldFlag");
            // Штатный переводчик NX получает задание; журнал не ждёт завершения файла.
            exporter.ProcessHoldFlag = false;
            TraceStep("END C056 exporter.ProcessHoldFlag");

            if (previousOutput.Existed)
                File.Delete(outputPath);
            outputTouched = true;
            stage = "Запись DXF";
            TraceStep("BEGIN C057 exporter.Commit");
            exporter.Commit();
            // Сразу отмечаем запуск: поздняя ошибка очистки не должна восстанавливать
            // старый DXF поверх файла, который сейчас пишет штатный переводчик.
            complete = true;
            background.TranslationLaunched();
            TraceStep("END C057 exporter.Commit");
        }
        catch (Exception ex)
        {
            TraceStep("MANAGED EXCEPTION " + ex.ToString());
            failure = stage + ": " + ex.Message;
        }
        finally
        {
            bool exporterReleased = exporter == null;
            if (exporter != null)
                {
                    TraceStep("BEGIN C058 exporter.Destroy");
                    Cleanup(delegate
                {
                    exporter.Destroy();
                    exporterReleased = true;
                }, cleanupErrors, "Завершение экспортера");
                    TraceStep("END C058 exporter.Destroy");
                }

            // сначала покидаем служебный чертёж, пока ВСЕ его объекты живы.
            // Не удаляем вид, которым ещё пользуется графическое окно NX.
            bool modelRestored = false;
            TraceStep("BEGIN C059 uf.Obj.AskStatus");
            Cleanup(delegate
            {
                if (draftingTouched)
                {
                    if (originalDrawing != Tag.Null
                        && uf.Obj.AskStatus(originalDrawing) == UFConstants.UF_OBJ_ALIVE)
                    {
                        DrawingSheet sheet = NXOpen.Utilities.NXObjectManager.Get(originalDrawing)
                            as DrawingSheet;
                        if (sheet != null)
                        {
                            TraceStep("BEGIN original DrawingSheet.Open");
                            sheet.Open();
                            TraceStep("END original DrawingSheet.Open");
                        }
                    }
                    TraceStep("BEGIN Drafting.SetDrawingLayout(false)");
                    part.Drafting.SetDrawingLayout(false);
                    TraceStep("END Drafting.SetDrawingLayout(false)");
                    if (session.ApplicationName != "UG_APP_MODELING")
                    {
                        TraceStep("BEGIN ApplicationSwitchImmediate MODELING");
                        session.ApplicationSwitchImmediate("UG_APP_MODELING");
                        TraceStep("END ApplicationSwitchImmediate MODELING");
                    }
                    if (session.ApplicationName != "UG_APP_MODELING")
                        throw new InvalidOperationException("NX не завершил возврат к модели.");
                    TraceStep("BEGIN Drafting.ExitDraftingApplication");
                    part.Drafting.ExitDraftingApplication();
                    TraceStep("END Drafting.ExitDraftingApplication");
                }

                // Получаем объект заново после работы переводчика и смены приложения.
                if (uf.Obj.AskStatus(originalViewTag) != UFConstants.UF_OBJ_ALIVE)
                    throw new InvalidOperationException("Исходный вид модели недоступен.");
                TraceStep("BEGIN NXObjectManager.Get original view");
                ModelingView restoredView = NXOpen.Utilities.NXObjectManager.Get(originalViewTag)
                    as ModelingView;
                TraceStep("END NXObjectManager.Get original view");
                if (restoredView == null)
                    throw new InvalidOperationException("Не удалось восстановить рабочий вид модели.");
                TraceStep("BEGIN restoredView.MakeWork");
                restoredView.MakeWork();
                TraceStep("END restoredView.MakeWork");
                if (!(part.Views.WorkView is ModelingView))
                    throw new InvalidOperationException("В окне NX всё ещё открыт чертёжный вид.");
                modelRestored = true;
            }, cleanupErrors, "Возврат к модели перед очисткой");
            TraceStep("END C059 uf.Obj.AskStatus");

            if (modelRestored && exporterReleased)
                {
                    TraceStep("BEGIN C060 exportView.Tag");
                    Cleanup(delegate
                {
                    // UpdateManager сам согласует удаление листа с его чертёжными видами.
                    // Именованный модельный вид удаляем только после зависимого листа.
                    if (exportSheet != null)
                        DeleteOwnedObjects(session, uf, part,
                            new Tag[] { exportSheet.Tag }, cleanupErrors);

                    List<Tag> geometry = new List<Tag>();
                    geometry.AddRange(temporaryRegions);
                    geometry.AddRange(temporaryCurves);
                    geometry.AddRange(temporaryAuxiliaries);
                    DeleteOwnedObjects(session, uf, part, geometry.ToArray(), cleanupErrors);
                    if (exportView != null)
                        DeleteOwnedObjects(session, uf, part,
                            new Tag[] { exportView.Tag }, cleanupErrors);
                }, cleanupErrors, "Удаление служебных объектов");
                    TraceStep("END C060 exportView.Tag");
                }
            else if (exportView != null || exportSheet != null || temporaryCurves.Count > 0)
                cleanupErrors.Add("Очистка служебных объектов пропущена: NX не подтвердил "
                    + "завершение экспортера или возврат к модели.");

            foreach (KeyValuePair<int, NXOpen.Layer.State> layer in changedLayers)
                {
                    TraceStep("BEGIN C061 part.Layers.SetState");
                    Cleanup(delegate { part.Layers.SetState(layer.Key, layer.Value, false); },
                    cleanupErrors, "Восстановление состояния слоя");
                    TraceStep("END C061 part.Layers.SetState");
                }
            foreach (DisplayableObject obj in temporarilyBlanked)
                {
                    TraceStep("BEGIN C062 obj.Unblank");
                    Cleanup(delegate { obj.Unblank(); }, cleanupErrors,
                    "Восстановление видимости вспомогательной геометрии");
                    TraceStep("END C062 obj.Unblank");
                }
            if (borderChanged)
                {
                    TraceStep("BEGIN C063 uf.Draw.SetBorderDisplay");
                    Cleanup(delegate { uf.Draw.SetBorderDisplay(originalBorderDisplay); },
                    cleanupErrors, "Восстановление настройки рамок");
                    TraceStep("END C063 uf.Draw.SetBorderDisplay");
                }

            if (session.ApplicationName != originalApplication)
                {
                    TraceStep("BEGIN C064 session.ApplicationSwitchImmediate");
                    Cleanup(delegate
                {
                    session.ApplicationSwitchImmediate(originalApplication);
                    if (session.ApplicationName != originalApplication)
                        throw new InvalidOperationException("NX не завершил смену приложения.");
                    if (originalApplication == "UG_APP_DRAFTING")
                    {
                        part.Drafting.EnterDraftingApplication();
                        part.Drafting.SetDrawingLayout(
                            originalDisplayState != UFConstants.UF_DRAW_MODELING_VIEW);
                    }
                }, cleanupErrors, "Возврат приложения NX");
                    TraceStep("END C064 session.ApplicationSwitchImmediate");
                }
            if (session.Parts.Work == null || session.Parts.Work.Tag != originalWorkPart.Tag)
                {
                    TraceStep("BEGIN C065 session.Parts.SetWork");
                    Cleanup(delegate { session.Parts.SetWork(originalWorkPart); },
                    cleanupErrors, "Возврат рабочей детали");
                    TraceStep("END C065 session.Parts.SetWork");
                }

            TraceStep("BEGIN C066 part.ModelingViews.WorkView");
            Cleanup(delegate
            {
                ModelingView restoredView = part.ModelingViews.WorkView;
                TraceStep("BEGIN restoredView.SetRotationTranslationScale");
                restoredView.SetRotationTranslationScale(originalMatrix, originalOrigin, originalScale);
                TraceStep("END restoredView.SetRotationTranslationScale");
                TraceStep("BEGIN restoredView.DisplayAppearance");
                restoredView.VisualizationVisualPreferences.DisplayAppearance = originalAppearance;
                TraceStep("END restoredView.DisplayAppearance");
            }, cleanupErrors, "Восстановление ракурса модели");
            TraceStep("END C066 part.ModelingViews.WorkView");

            // Отдельный шаг: выполняется даже при ошибке экспорта или другой очистки.
            TraceStep("BEGIN C067 part.ModelingViews.WorkView.RenderingStyle");
            Cleanup(delegate
            {
                TraceStep("BEGIN WorkView.RenderingStyle with original Face Edges");
                part.ModelingViews.WorkView.RenderingStyle =
                    ShadedStyleWithOriginalEdges(originalRenderingStyle);
                TraceStep("END WorkView.RenderingStyle with original Face Edges");
                TraceStep("BEGIN WorkView.Regenerate");
                part.ModelingViews.WorkView.Regenerate();
                TraceStep("END WorkView.Regenerate");
            }, cleanupErrors, "Включение Fully Shaded и восстановление Face Edges");
            TraceStep("END C067 part.ModelingViews.WorkView.RenderingStyle");

            if (outputTouched && !complete)
            {
                Cleanup(delegate { previousOutput.Restore(); },
                    cleanupErrors, "Восстановление прежнего DXF");
                foreach (FileSnapshot log in translatorLogs)
                    Cleanup(delegate { log.Restore(); }, cleanupErrors,
                        "Очистка служебного журнала " + log.Filename);
            }
            // После успешного запуска два штатных журнала обрабатываются позже,
            // когда DXF завершён и файлы больше не заняты переводчиком.
        }

        string message = complete
            ? "Экспорт DXF запущен в фоне:\n" + outputPath + "\n\n"
                + (mode == ExportMode.VisibleEdges ? "Все видимые рёбра"
                    : mode == ExportMode.OuterContour ? "Только внешний контур модели"
                    : "Только выбранные кривые")
                + ". Масштаб 1:1.\n"
                + "Можно продолжать работу в NX. Дождитесь завершения фоновой задачи "
                + "перед открытием DXF."
            : "Не удалось запустить экспорт DXF.\n\n" + failure;
        if (diagnosticWriteFailed)
            cleanupErrors.Add("Часть контрольных точек не удалось записать в системный журнал NX.");
        if (cleanupErrors.Count > 0)
            message += "\n\n" + String.Join("\n", cleanupErrors.ToArray());
        TraceStep("BEGIN C068 ui.NXMessageBox.Show");
        ui.NXMessageBox.Show(Title,
            !complete ? NXMessageBox.DialogType.Error
                : cleanupErrors.Count > 0 ? NXMessageBox.DialogType.Warning
                : NXMessageBox.DialogType.Information,
            message);
        TraceStep("END C068 ui.NXMessageBox.Show");
    }

    private static View.RenderingStyleType ShadedStyleWithOriginalEdges(
        View.RenderingStyleType originalStyle)
    {
        if (originalStyle == View.RenderingStyleType.ShadedWithEdges
            || originalStyle == View.RenderingStyleType.ShadedWithBodyColorEdges)
            return originalStyle;
        return View.RenderingStyleType.Shaded;
    }

    private static NXObject[] CreateSelectedCurvesProjection(UFSession uf, Part part,
        View view, NXObject[] selectedCurves, List<Tag> temporaryCurves,
        List<Tag> temporaryAuxiliaries)
    {
        if (selectedCurves == null || selectedCurves.Length == 0)
            throw new InvalidOperationException("Перед запуском выделите нужные рёбра или кривые.");

        List<Tag> sourceCurves = new List<Tag>();
        foreach (NXObject obj in selectedCurves)
        {
            if (obj == null || uf.Obj.AskStatus(obj.Tag) != UFConstants.UF_OBJ_ALIVE)
                throw new InvalidOperationException("Один из выбранных объектов больше недоступен.");
            if (obj is Edge)
            {
                // В проекцию передаём независимую кривую, а не тело-владелец ребра.
                // Используем выбранный тег, включая occurrence в сборке: подмена
                // на Prototype потеряла бы положение компонента в текущем виде.
                Tag extracted = Tag.Null;
                try
                {
                    TraceStep("BEGIN uf.Modl.CreateCurveFromEdge");
                    uf.Modl.CreateCurveFromEdge(obj.Tag, out extracted);
                    TraceStep("END uf.Modl.CreateCurveFromEdge");
                }
                finally
                {
                    if (extracted != Tag.Null && !temporaryCurves.Contains(extracted))
                        temporaryCurves.Add(extracted);
                }
                if (extracted == Tag.Null)
                    throw new InvalidOperationException("NX не смог извлечь кривую выбранного ребра.");
                sourceCurves.Add(extracted);
            }
            else if (obj is Curve)
            {
                // Исходная самостоятельная кривая НЕ входит в список удаления.
                // Проектор с copy_flag=1 создаст для экспорта отдельную копию.
                sourceCurves.Add(obj.Tag);
            }
            else
                throw new InvalidOperationException("В выделении допустимы только рёбра и кривые.");
        }

        Matrix3x3 matrix = view.Matrix;
        Tag plane = Tag.Null;
        try
        {
            uf.Modl.CreatePlane(new double[3],
                new double[] { matrix.Zx, matrix.Zy, matrix.Zz }, out plane);
        }
        finally { if (plane != Tag.Null) temporaryAuxiliaries.Add(plane); }

        // Выбранные рёбра могут быть разомкнутыми или не соединяться друг с другом.
        // Проверка замкнутой петли и отсев внутренних контуров здесь не выполняются.
        Tag[] projected = ProjectCurves(uf, sourceCurves.ToArray(), plane,
            temporaryCurves, temporaryAuxiliaries);
        List<NXObject> result = new List<NXObject>();
        foreach (Tag tag in projected)
        {
            Curve curve = NXOpen.Utilities.NXObjectManager.Get(tag) as Curve;
            if (curve == null) continue; // Ребро вдоль направления взгляда может дать точку.
            if (curve.IsOccurrence || curve.OwningPart == null || curve.OwningPart.Tag != part.Tag)
                throw new InvalidOperationException("NX создал проекцию вне текущего проекта.");
            curve.Unblank();
            curve.Layer = part.Layers.WorkLayer;
            curve.LineFont = DisplayableObject.ObjectFont.Solid;
            result.Add(curve);
        }
        if (result.Count == 0)
            throw new InvalidOperationException("Выбранные рёбра не образуют кривых в этой проекции.\n"
                + "Проверьте направление текущего вида.");
        return result.ToArray();
    }

    private static Tag CreateVisibleEdgesView(UFSession uf, DrawingSheet sheet,
        View modelView, double sheetSize)
    {
        TraceStep("ENTER CreateVisibleEdgesView");
        UFDraw.ViewInfo info;
        TraceStep("BEGIN C069 uf.Draw.InitializeViewInfo");
        uf.Draw.InitializeViewInfo(out info);
        TraceStep("END C069 uf.Draw.InitializeViewInfo");
        info.view_scale = 1.0;
        info.use_ref_pt = false;
        info.inherit_boundary = false;
        info.transfer_annotation = false;
        info.inherit_pmi = false;
        Tag drawingView;
        TraceStep("BEGIN C070 uf.Draw.ImportView");
        uf.Draw.ImportView(sheet.Tag, modelView.Tag,
            new double[] { sheetSize / 2.0, sheetSize / 2.0 }, ref info, out drawingView);
        TraceStep("END C070 uf.Draw.ImportView");

        UFDraw.ViewPrfs preferences;
        TraceStep("BEGIN C071 uf.Draw.AskViewDisplay");
        uf.Draw.AskViewDisplay(drawingView, out preferences);
        TraceStep("END C071 uf.Draw.AskViewDisplay");
        preferences.hidden_line = UFDraw.HiddenLine.HiddenLineRemovalOn;
        preferences.hidden_line_font = UFConstants.UF_OBJ_FONT_INVISIBLE;
        preferences.edges_hidden_by_own_solid = true;
        preferences.edge_hiding_edge = UFDraw.EdgeHidingEdge.EdgeHidingEdgeOn;
        preferences.interfering_solids = 1; // Учитывать пересечения тел без добавления линий пересечения.
        preferences.referenced_edges_only = false;
        preferences.silhouettes = UFDraw.Silhouette.SilhouettesOn;
        preferences.smooth = UFDraw.Smooth.SmoothOn;
        preferences.smooth_edge_font = UFConstants.UF_OBJ_FONT_SOLID;
        preferences.smooth_edge_gap_size = 0.0;
        preferences.uvhatch = UFDraw.Uvhatch.UvhatchOff;
        preferences.virtual_intersect = UFDraw.VirtualIntersect.VirtualIntersectOff;
        preferences.simplify_small_features = 0;
        preferences.visible_line_font = UFConstants.UF_OBJ_FONT_SOLID;
        double modelTolerance;
        TraceStep("BEGIN C072 uf.Modl.AskDistanceTolerance");
        uf.Modl.AskDistanceTolerance(out modelTolerance);
        TraceStep("END C072 uf.Modl.AskDistanceTolerance");
        preferences.tolerance = modelTolerance;
        TraceStep("BEGIN C073 uf.Draw.SetViewDisplay");
        uf.Draw.SetViewDisplay(drawingView, ref preferences);
        TraceStep("END C073 uf.Draw.SetViewDisplay");
        return drawingView;
    }

    private static Body[] GetVisibleBodies(DisplayableObject[] visibleObjects)
    {
        TraceStep("ENTER GetVisibleBodies");
        List<Body> bodies = new List<Body>();
        HashSet<Tag> found = new HashSet<Tag>();
        TraceStep("BEGIN body selection from snapshot");
        foreach (DisplayableObject obj in visibleObjects)
        {
            Body body = obj as Body;
            if (body != null && found.Add(body.Tag)) bodies.Add(body);
        }
        TraceStep("END body selection from snapshot");
        if (bodies.Count == 0)
            throw new InvalidOperationException("В текущем виде нет видимых тел для экспорта рёбер.");
        return bodies.ToArray();
    }

    private static double GetSheetSize(UFSession uf, Part part, Body[] bodies)
    {
        TraceStep("ENTER GetSheetSize");
        double[] minimum = { Double.PositiveInfinity, Double.PositiveInfinity, Double.PositiveInfinity };
        double[] maximum = { Double.NegativeInfinity, Double.NegativeInfinity, Double.NegativeInfinity };
        foreach (Body body in bodies)
        {
            double[] box = new double[6];
            TraceStep("BEGIN C074 uf.Modl.AskBoundingBox");
            uf.Modl.AskBoundingBox(body.Tag, box);
            TraceStep("END C074 uf.Modl.AskBoundingBox");
            for (int axis = 0; axis < 3; axis++)
            {
                minimum[axis] = Math.Min(minimum[axis], box[axis]);
                maximum[axis] = Math.Max(maximum[axis], box[axis + 3]);
            }
        }
        double dx = maximum[0] - minimum[0];
        double dy = maximum[1] - minimum[1];
        double dz = maximum[2] - minimum[2];
        double diagonal = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        TraceStep("BEGIN C075 part.PartUnits");
        double margin = part.PartUnits == BasePart.Units.Millimeters ? 20.0 : 20.0 / 25.4;
        TraceStep("END C075 part.PartUnits");
        return diagonal + 2.0 * margin;
    }

    private static NXObject[] CreateOuterContour(UFSession uf, Part part, View view,
        Body[] bodies, List<Tag> temporaryCurves, List<Tag> temporaryRegions,
        List<Tag> temporaryAuxiliaries, Dictionary<int, NXOpen.Layer.State> changedLayers)
    {
        TraceStep("ENTER CreateOuterContour");
        Tag[] bodyTags = new Tag[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            if (!bodies[i].IsSolidBody)
                throw new InvalidOperationException(
                    "Режим внешнего контура требует твёрдотельную модель.\n"
                    + "Для открытых поверхностей используйте «Все видимые рёбра».");
            TraceStep("BEGIN C076 bodies[i].Tag");
            bodyTags[i] = bodies[i].Tag;
            TraceStep("END C076 bodies[i].Tag");
            TraceStep("BEGIN C077 bodies[i].Layer");
            int layer = bodies[i].Layer;
            TraceStep("END C077 bodies[i].Layer");
            TraceStep("BEGIN C078 part.Layers.GetState");
            NXOpen.Layer.State state = part.Layers.GetState(layer);
            TraceStep("END C078 part.Layers.GetState");
            if (state == NXOpen.Layer.State.Visible)
            {
                if (!changedLayers.ContainsKey(layer)) changedLayers.Add(layer, state);
                TraceStep("BEGIN C079 part.Layers.SetState");
                part.Layers.SetState(layer, NXOpen.Layer.State.Selectable, false);
                TraceStep("END C079 part.Layers.SetState");
            }
        }

        double distanceTolerance, angleTolerance;
        TraceStep("BEGIN C080 uf.Modl.AskDistanceTolerance");
        uf.Modl.AskDistanceTolerance(out distanceTolerance);
        TraceStep("END C080 uf.Modl.AskDistanceTolerance");
        TraceStep("BEGIN C081 uf.Modl.AskAngleTolerance");
        uf.Modl.AskAngleTolerance(out angleTolerance);
        TraceStep("END C081 uf.Modl.AskAngleTolerance");
        int loopCount = 0;
        int[] counts = null;
        Tag[][] loops = null;
        try
        {
            // Один вызов для всех тел: NX объединяет перекрывающиеся проекции.
            // Точные кривые NX, а не силуэт по пикселям или сетке треугольников.
            TraceStep("BEGIN C082 uf.Curve.CreatePreciseOutline");
            uf.Curve.CreatePreciseOutline(bodyTags.Length, bodyTags, view.Tag,
                out loopCount, out counts, out loops,
                new double[] { distanceTolerance, angleTolerance });
            TraceStep("END C082 uf.Curve.CreatePreciseOutline");
        }
        finally
        {
            // Запоминаем также частичный результат, если ядро вернуло ошибку.
            if (loops != null)
                foreach (Tag[] loop in loops)
                    if (loop != null)
                        foreach (Tag curve in loop)
                            if (curve != Tag.Null && !temporaryCurves.Contains(curve))
                                temporaryCurves.Add(curve);
        }
        if (loopCount <= 0 || loops == null || loops.Length != loopCount
            || counts == null || counts.Length != loopCount)
            throw new InvalidOperationException("NX не смог построить замкнутый внешний контур этого вида.");

        // Precise Outline может оставлять сегменты на разной глубине модели.
        // Проекция на общую плоскость выполняется ядром NX и сохраняет точные кривые.
        TraceStep("BEGIN C083 view.Matrix");
        Matrix3x3 matrix = view.Matrix;
        TraceStep("END C083 view.Matrix");
        Tag plane = Tag.Null;
        try
        {
            TraceStep("BEGIN C084 uf.Modl.CreatePlane");
            uf.Modl.CreatePlane(new double[3],
                new double[] { matrix.Zx, matrix.Zy, matrix.Zz }, out plane);
            TraceStep("END C084 uf.Modl.CreatePlane");
        }
        finally { if (plane != Tag.Null) temporaryAuxiliaries.Add(plane); }
        for (int i = 0; i < loopCount; i++)
        {
            if (loops[i] == null || loops[i].Length == 0 || loops[i].Length != counts[i])
                throw new InvalidOperationException("NX вернул неполный контур модели.");
            loops[i] = ProjectLoop(uf, loops[i], plane, distanceTolerance,
                temporaryCurves, temporaryAuxiliaries);
            counts[i] = loops[i].Length;
        }

        // Каждую петлю временно заполняем точной плоской гранью. Это позволяет
        // проверять вложенность ядром NX без аппроксимации экспортируемых кривых.
        Tag[] regions = new Tag[loopCount];
        // При одной петле проверять вложенность не во что; область не требуется.
        for (int i = 0; loopCount > 1 && i < loopCount; i++)
        {
            if (loops[i] == null || loops[i].Length == 0 || loops[i].Length != counts[i])
                throw new InvalidOperationException("NX вернул неполный контур модели.");
            // UF_STRING: один замкнутый профиль, не более 402 сегментов.
            // Проверяем документированный предел до передачи массива в ядро NX.
            if (loops[i].Length > 402)
                throw new InvalidOperationException(
                    "В одном контуре больше 402 сегментов. Этот способ выделения "
                    + "внешней границы не поддерживает такой контур.\n"
                    + "Используйте режим «Все видимые рёбра».");

            // это управляемые массивы .NET, а не буферы, выделенные UF.
            // Не вызываем для них InitStringList/CreateStringList/FreeStringList.
            // Массив тегов копируем, чтобы вызов с ref не затронул список петель.
            StringList section = new StringList();
            section.num = 1;
            section._string = new int[] { loops[i].Length };
            section.dir = new int[] { 1 }; // UF_MODL_CURVE_START_FROM_BEGIN.
            section.id = (Tag[])loops[i].Clone();
            TraceStep("MANAGED StringList ready; loop="
                + (i + 1).ToString(CultureInfo.InvariantCulture) + "/"
                + loopCount.ToString(CultureInfo.InvariantCulture) + "; segments="
                + section.id.Length.ToString(CultureInfo.InvariantCulture));
            try
            {
                TraceStep("BEGIN C087 uf.Modl.CreateBplane");
                uf.Modl.CreateBplane(ref section,
                    new double[] { distanceTolerance, angleTolerance, 0.0 }, out regions[i]);
                TraceStep("END C087 uf.Modl.CreateBplane");
            }
            finally
            {
                // Геометрию NX удаляет прежняя штатная очистка через UpdateManager.
                // За массивы section отвечает .NET; нативное освобождение не нужно.
                if (regions[i] != Tag.Null) temporaryRegions.Add(regions[i]);
            }
            if (regions[i] == Tag.Null)
                throw new InvalidOperationException("NX не создал область контура.");
            TraceStep("REGION READY; loop="
                + (i + 1).ToString(CultureInfo.InvariantCulture));
        }

        List<NXObject> outer = new List<NXObject>();
        for (int i = 0; i < loopCount; i++)
        {
            bool nested = false;
            for (int j = 0; j < loopCount && !nested; j++)
            {
                if (i == j) continue;
                nested = IsLoopInsideRegion(uf, loops[i], regions[j]);
            }
            if (nested) continue;
            foreach (Tag tag in loops[i])
            {
                DisplayableObject curve = NXOpen.Utilities.NXObjectManager.Get(tag) as DisplayableObject;
                if (curve == null)
                    throw new InvalidOperationException("NX создал неподдерживаемый объект контура.");
                // Служебная плоская область могла скрыть свои родительские кривые.
                TraceStep("BEGIN C089 curve.Unblank");
                curve.Unblank();
                TraceStep("END C089 curve.Unblank");
                TraceStep("BEGIN C090 curve.Layer");
                curve.Layer = part.Layers.WorkLayer;
                TraceStep("END C090 curve.Layer");
                TraceStep("BEGIN C091 curve.LineFont");
                curve.LineFont = DisplayableObject.ObjectFont.Solid;
                TraceStep("END C091 curve.LineFont");
                outer.Add(curve);
            }
        }
        if (outer.Count == 0)
            throw new InvalidOperationException("Не удалось отделить наружный контур от внутренних петель.");
        return outer.ToArray();
    }

    private static Tag[] ProjectLoop(UFSession uf, Tag[] loop, Tag plane,
        double tolerance, List<Tag> temporaryCurves, List<Tag> temporaryAuxiliaries)
    {
        TraceStep("ENTER ProjectLoop");
        Tag[] projected = ProjectCurves(uf, loop, plane, temporaryCurves, temporaryAuxiliaries);
        // У внешнего контура нужна замкнутая цепочка для проверки вложенности.
        return OrderClosedLoop(uf, projected, tolerance);
    }

    private static Tag[] ProjectCurves(UFSession uf, Tag[] curves, Tag plane,
        List<Tag> temporaryCurves, List<Tag> temporaryAuxiliaries)
    {
        TraceStep("ENTER ProjectCurves");
        UFCurve.Proj projection = new UFCurve.Proj();
        projection.proj_type = 1; // По нормали к плоскости; точная ортогональная проекция.
        projection.proj_vec = new double[3];
        projection.x_vector = new double[3];
        projection.ref_pnt = new double[3];
        Tag group = Tag.Null;
        try
        {
            // copy_flag = 1: новые независимые кривые, исходные кривые не изменяются.
            TraceStep("BEGIN C092 uf.Curve.CreateProjCurves");
            uf.Curve.CreateProjCurves(curves.Length, curves, 1, new Tag[] { plane },
                1, ref projection, out group);
            TraceStep("END C092 uf.Curve.CreateProjCurves");
        }
        finally { if (group != Tag.Null) temporaryAuxiliaries.Add(group); }
        Tag[] projected;
        int count;
        TraceStep("BEGIN C093 uf.Group.AskGroupData");
        uf.Group.AskGroupData(group, out projected, out count);
        TraceStep("END C093 uf.Group.AskGroupData");
        if (projected != null)
            foreach (Tag curve in projected)
                if (curve != Tag.Null && !temporaryCurves.Contains(curve)) temporaryCurves.Add(curve);
        if (count == 0 || projected == null || count != projected.Length)
            throw new InvalidOperationException("NX не смог спроецировать кривые на плоскость вида.");
        return projected;
    }

    private static Tag[] OrderClosedLoop(UFSession uf, Tag[] curves, double tolerance)
    {
        TraceStep("ENTER OrderClosedLoop");
        double[][] starts = new double[curves.Length][];
        double[][] ends = new double[curves.Length][];
        for (int i = 0; i < curves.Length; i++)
        {
            starts[i] = CurvePoint(uf, curves[i], 0.0);
            ends[i] = CurvePoint(uf, curves[i], 1.0);
        }
        Tag[] ordered = new Tag[curves.Length];
        bool[] used = new bool[curves.Length];
        ordered[0] = curves[0];
        used[0] = true;
        double[] end = ends[0];
        double squaredTolerance = tolerance * tolerance;
        for (int position = 1; position < curves.Length; position++)
        {
            int next = -1;
            bool reverse = false;
            for (int i = 1; i < curves.Length; i++)
            {
                if (used[i]) continue;
                if (SquaredDistance(end, starts[i]) <= squaredTolerance)
                { next = i; break; }
                if (SquaredDistance(end, ends[i]) <= squaredTolerance)
                { next = i; reverse = true; break; }
            }
            if (next < 0)
                throw new InvalidOperationException("В проекции контура есть разрыв больше допуска модели.");
            ordered[position] = curves[next];
            used[next] = true;
            end = reverse ? starts[next] : ends[next];
        }
        if (SquaredDistance(end, starts[0]) > squaredTolerance)
            throw new InvalidOperationException("Проекция внешнего контура не замкнулась.");
        return ordered;
    }

    private static double[] CurvePoint(UFSession uf, Tag curve, double fraction)
    {
        TraceStep("ENTER CurvePoint");
        IntPtr evaluator = IntPtr.Zero;
        try
        {
            TraceStep("BEGIN C094 uf.Eval.Initialize2");
            uf.Eval.Initialize2(curve, out evaluator);
            TraceStep("END C094 uf.Eval.Initialize2");
            double[] limits = new double[2];
            TraceStep("BEGIN C095 uf.Eval.AskLimits");
            uf.Eval.AskLimits(evaluator, limits);
            TraceStep("END C095 uf.Eval.AskLimits");
            double[] point = new double[3];
            TraceStep("BEGIN C096 uf.Eval.Evaluate");
            uf.Eval.Evaluate(evaluator, 0,
                limits[0] + (limits[1] - limits[0]) * fraction, point, new double[3]);
            TraceStep("END C096 uf.Eval.Evaluate");
            return point;
        }
        finally { if (evaluator != IntPtr.Zero) {
                                                    TraceStep("BEGIN C097 uf.Eval.Free");
                                                    uf.Eval.Free(evaluator);
                                                    TraceStep("END C097 uf.Eval.Free");
                                                } }
    }

    private static double SquaredDistance(double[] a, double[] b)
    {
        double dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
        return dx * dx + dy * dy + dz * dz;
    }

    private static bool IsLoopInsideRegion(UFSession uf, Tag[] loop, Tag region)
    {
        TraceStep("ENTER IsLoopInsideRegion");
        // Для непересекающихся петель принадлежность одной точки определяет вложенность.
        // Если точка попала в касание, проверяем другие точки, не удаляя петлю наугад.
        double[] fractions = { 0.5, 0.25, 0.75, 0.125, 0.875 };
        foreach (Tag curve in loop)
        {
            IntPtr evaluator = IntPtr.Zero;
            try
            {
                TraceStep("BEGIN C098 uf.Eval.Initialize2");
                uf.Eval.Initialize2(curve, out evaluator);
                TraceStep("END C098 uf.Eval.Initialize2");
                double[] limits = new double[2];
                TraceStep("BEGIN C099 uf.Eval.AskLimits");
                uf.Eval.AskLimits(evaluator, limits);
                TraceStep("END C099 uf.Eval.AskLimits");
                foreach (double fraction in fractions)
                {
                    double[] point = new double[3];
                    TraceStep("BEGIN C100 uf.Eval.Evaluate");
                    uf.Eval.Evaluate(evaluator, 0,
                        limits[0] + (limits[1] - limits[0]) * fraction, point, new double[3]);
                    TraceStep("END C100 uf.Eval.Evaluate");
                    int status;
                    TraceStep("BEGIN C101 uf.Modl.AskPointContainment");
                    uf.Modl.AskPointContainment(point, region, out status);
                    TraceStep("END C101 uf.Modl.AskPointContainment");
                    if (status == 1) return true;
                    if (status == 2) return false;
                    if (status != 3)
                        throw new InvalidOperationException("NX не определил вложенность контуров.");
                }
            }
            finally { if (evaluator != IntPtr.Zero) {
                                                        TraceStep("BEGIN C102 uf.Eval.Free");
                                                        uf.Eval.Free(evaluator);
                                                        TraceStep("END C102 uf.Eval.Free");
                                                    } }
        }
        throw new InvalidOperationException(
            "Контуры совпадают или касаются в пределах допуска модели.\n"
            + "Не удалось однозначно выделить внешний контур.");
    }

    private static void EnsureUpdateAvailable(Session session)
    {
        TraceStep("ENTER EnsureUpdateAvailable");
        TraceStep("BEGIN UpdateManager.GetUpdateLock");
        if (session.UpdateManager.GetUpdateLock())
            throw new InvalidOperationException("Обновление модели NX заблокировано. "
                + "Завершите текущую команду и повторите экспорт.");
        TraceStep("END UpdateManager.GetUpdateLock");
        TraceStep("BEGIN UpdateManager.GetObjectsOnDeleteList");
        if (session.UpdateManager.GetObjectsOnDeleteList().Length != 0)
            throw new InvalidOperationException("В NX есть незавершённая операция удаления. "
                + "Завершите её перед экспортом.");
        TraceStep("END UpdateManager.GetObjectsOnDeleteList");
    }

    private static void DeleteOwnedObjects(Session session, UFSession uf, Part part,
        Tag[] ownedTags, List<string> cleanupErrors)
    {
        TraceStep("ENTER DeleteOwnedObjects");
        if (ownedTags.Length == 0) return;
        EnsureUpdateAvailable(session);
        List<TaggedObject> objects = new List<TaggedObject>();
        HashSet<Tag> owned = new HashSet<Tag>();
        Queue<Tag> pending = new Queue<Tag>(ownedTags);
        HashSet<Tag> activeViews = new HashSet<Tag>();
        TraceStep("BEGIN part.Views.GetActiveViews");
        foreach (View active in part.Views.GetActiveViews()) activeViews.Add(active.Tag);
        TraceStep("END part.Views.GetActiveViews");

        // Только объекты, созданные этим запуском; не поиск по имени или всему проекту.
        while (pending.Count > 0)
        {
            Tag tag = pending.Dequeue();
            if (tag == Tag.Null || !owned.Add(tag)
                || uf.Obj.AskStatus(tag) != UFConstants.UF_OBJ_ALIVE) continue;
            NXObject obj = NXOpen.Utilities.NXObjectManager.Get(tag) as NXObject;
            if (obj == null || obj.IsOccurrence || obj.OwningPart == null
                || obj.OwningPart.Tag != part.Tag)
                throw new InvalidOperationException("Не подтверждена принадлежность служебного объекта.");
            if (activeViews.Contains(tag))
                throw new InvalidOperationException("Служебный вид ещё используется окном NX.");

            DrawingSheet sheet = obj as DrawingSheet;
            if (sheet != null)
            {
                if (activeViews.Contains(sheet.View.Tag))
                    throw new InvalidOperationException("Служебный лист ещё открыт в окне NX.");
                foreach (DraftingView view in sheet.GetDraftingViews())
                    if (activeViews.Contains(view.Tag))
                        throw new InvalidOperationException("Чертёжный вид ещё открыт в окне NX.");
            }

            int type, subtype;
            TraceStep("BEGIN C103 uf.Obj.AskTypeAndSubtype");
            uf.Obj.AskTypeAndSubtype(tag, out type, out subtype);
            TraceStep("END C103 uf.Obj.AskTypeAndSubtype");
            if (type == UFConstants.UF_group_type)
            {
                // Группы только от CreateProjCurves(copy_flag=1): все их кривые новые.
                // Собираем их и при частичном сбое проекции, но не удаляем рекурсивно.
                Tag[] members;
                int count;
                TraceStep("BEGIN C104 uf.Group.AskGroupData");
                uf.Group.AskGroupData(tag, out members, out count);
                TraceStep("END C104 uf.Group.AskGroupData");
                if (members != null)
                    foreach (Tag member in members) pending.Enqueue(member);
            }
            objects.Add(obj);
        }
        if (objects.Count == 0) return;

        // Экспортер способен очищать прежние метки Undo. Эту создаём ПОСЛЕ него,
        // сразу перед штатным обновлением. Отката всего экспорта здесь нет.
        TraceStep("BEGIN C105 session.UpdateManager");
        NXOpen.Update update = session.UpdateManager;
        TraceStep("END C105 session.UpdateManager");
        TraceStep("BEGIN C106 update.GetDefaultUpdateFailureAction");
        NXOpen.Update.FailureOption previousFailureAction = update.GetDefaultUpdateFailureAction();
        TraceStep("END C106 update.GetDefaultUpdateFailureAction");
        TraceStep("BEGIN C107 session.SetUndoMark");
        Session.UndoMarkId mark = session.SetUndoMark(
            Session.MarkVisibility.Invisible, Title + " — Очистка объектов экспорта");
        TraceStep("END C107 session.SetUndoMark");
        try
        {
            TraceStep("BEGIN C108 update.SetDefaultUpdateFailureAction");
            update.SetDefaultUpdateFailureAction(NXOpen.Update.FailureOption.Undo);
            TraceStep("END C108 update.SetDefaultUpdateFailureAction");
            TraceStep("BEGIN C109 update.AddObjectsToDeleteList");
            int errors = update.AddObjectsToDeleteList(objects.ToArray());
            TraceStep("END C109 update.AddObjectsToDeleteList");
            if (errors != 0)
                throw new InvalidOperationException("NX не принял служебные объекты для удаления: "
                    + errors.ToString(CultureInfo.InvariantCulture));
            TraceStep("BEGIN C110 update.DoUpdate");
            errors = update.DoUpdate(mark);
            TraceStep("END C110 update.DoUpdate");
            if (errors != 0)
                throw new InvalidOperationException("NX сообщил об ошибке обновления при очистке: "
                    + errors.ToString(CultureInfo.InvariantCulture));
            foreach (Tag tag in owned)
                if (tag != Tag.Null && uf.Obj.AskStatus(tag) == UFConstants.UF_OBJ_ALIVE)
                    throw new InvalidOperationException("Не все служебные объекты удалось удалить.");
        }
        finally
        {
            // После ошибки не оставляем отложенное удаление на следующую команду NX.
            // Чужие объекты из очереди не удаляем и очередь целиком не очищаем.
            TraceStep("BEGIN C111 update.GetObjectsOnDeleteList");
            Cleanup(delegate
            {
                List<TaggedObject> leftovers = new List<TaggedObject>();
                foreach (TaggedObject obj in update.GetObjectsOnDeleteList())
                    if (owned.Contains(obj.Tag)) leftovers.Add(obj);
                if (leftovers.Count > 0) update.RemoveObjectsFromDeleteList(leftovers.ToArray());
            }, cleanupErrors, "Завершение очереди очистки");
            TraceStep("END C111 update.GetObjectsOnDeleteList");
            TraceStep("BEGIN C112 update.SetDefaultUpdateFailureAction");
            Cleanup(delegate { update.SetDefaultUpdateFailureAction(previousFailureAction); },
                cleanupErrors, "Восстановление настройки обновления NX");
            TraceStep("END C112 update.SetDefaultUpdateFailureAction");
            TraceStep("BEGIN C113 session.DoesUndoMarkExist");
            Cleanup(delegate
            {
                if (session.DoesUndoMarkExist(mark, null)) session.DeleteUndoMark(mark, null);
            }, cleanupErrors, "Удаление метки очистки");
            TraceStep("END C113 session.DoesUndoMarkExist");
        }
    }

    private static void Cleanup(Action action, List<string> errors, string description)
    {
        TraceStep("CLEANUP BEGIN " + description);
        try { action(); TraceStep("CLEANUP END " + description); }
        catch (Exception ex)
        {
            TraceStep("CLEANUP EXCEPTION " + description + " " + ex.ToString());
            errors.Add(description + ": " + ex.Message);
        }
    }

    private static string VisibleLayers(Part part)
    {
        TraceStep("ENTER VisibleLayers");
        List<string> layers = new List<string>();
        for (int layer = 1; layer <= 256; layer++)
        {
            if (part.Layers.GetState(layer) != NXOpen.Layer.State.Hidden)
                layers.Add(layer.ToString(CultureInfo.InvariantCulture));
        }
        if (layers.Count == 0)
            throw new InvalidOperationException("В текущем проекте нет видимых слоёв.");
        return String.Join(",", layers.ToArray());
    }

    private static void CheckDxf(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            throw new IOException("Штатный экспортер NX не создал DXF.");

        // В фоне проверяем только закрытый переводчиком файл.
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.None))
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
        {
            string first = reader.ReadLine();
            if (first != null && first.StartsWith("AutoCAD Binary DXF", StringComparison.Ordinal))
            {
                // Экспорт настроен на R2007: бинарный код группы занимает 2 байта.
                // Одного заголовка недостаточно, нужен конечный маркер (0, "EOF").
                if (stream.Length < 28)
                    throw new IOException("NX ещё не завершил бинарный DXF.");
                stream.Seek(-6, SeekOrigin.End);
                byte[] tail = new byte[6];
                int read = 0;
                while (read < tail.Length)
                {
                    int count = stream.Read(tail, read, tail.Length - read);
                    if (count == 0) break;
                    read += count;
                }
                if (read != 6 || tail[0] != 0 || tail[1] != 0 || tail[2] != 69
                    || tail[3] != 79 || tail[4] != 70 || tail[5] != 0)
                    throw new IOException("NX ещё не завершил бинарный DXF.");
                return;
            }

            string codeLine = first;
            string section = "";
            bool sectionNameFollows = false;
            bool hasGeometry = false;
            bool hasEnd = false;
            while (codeLine != null)
            {
                string value = reader.ReadLine();
                int code;
                if (value == null || !Int32.TryParse(codeLine.Trim(), out code))
                    throw new IOException("NX создал неполный или некорректный файл DXF.");
                value = value.Trim();
                if (sectionNameFollows && code == 2)
                {
                    section = value;
                    sectionNameFollows = false;
                }
                if (code == 0)
                {
                    if (value == "SECTION") sectionNameFollows = true;
                    else if (value == "ENDSEC") section = "";
                    else if (value == "EOF") { hasEnd = true; break; }
                    else if (section == "ENTITIES"
                        && (value == "LINE" || value == "ARC" || value == "CIRCLE"
                            || value == "ELLIPSE" || value == "SPLINE"
                            || value == "LWPOLYLINE" || value == "POLYLINE" || value == "INSERT"))
                        hasGeometry = true;
                }
                codeLine = reader.ReadLine();
            }
            if (!hasEnd || !hasGeometry)
                throw new IOException("NX не вывел контур в DXF. Проверьте видимость геометрии модели.");
        }
    }

    // Этот помощник не хранит объекты NX и не вызывает NXOpen/UF, даже для сообщений.
    // Геометрия и интерфейс обслуживаются только основным потоком в ExportCurrentView.
    private sealed class BackgroundLogCleanup
    {
        private const string RegistryKey = "NX.CurrentViewDxf.BackgroundFiles.v1";
        private const string WarningsKey = RegistryKey + ".Warnings";
        private readonly string outputPath;
        private readonly string jobId = Guid.NewGuid().ToString("N");
        private readonly string[] reservedPaths;
        private FileSnapshot[] logs = new FileSnapshot[0];
        private volatile bool scriptReturned;
        private volatile bool translationLaunched;

        // Общие BCL-объекты позволяют согласовать повторные запуски журнала,
        // даже если NX загружает каждый запуск в новую сборку .NET.
        private static object RegistryLock { get { return String.Intern(RegistryKey); } }

        private static Dictionary<string, string> Jobs()
        {
            Dictionary<string, string> jobs = AppDomain.CurrentDomain.GetData(RegistryKey)
                as Dictionary<string, string>;
            if (jobs == null)
            {
                jobs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                AppDomain.CurrentDomain.SetData(RegistryKey, jobs);
            }
            return jobs; // Вызывается только под RegistryLock.
        }

        public static string[] TakeWarnings()
        {
            lock (RegistryLock)
            {
                List<string> warnings = AppDomain.CurrentDomain.GetData(WarningsKey)
                    as List<string>;
                if (warnings == null) return new string[0];
                string[] result = warnings.ToArray();
                warnings.Clear();
                return result;
            }
        }

        private static void RememberWarning(string message)
        {
            lock (RegistryLock)
            {
                List<string> warnings = AppDomain.CurrentDomain.GetData(WarningsKey)
                    as List<string>;
                if (warnings == null)
                {
                    warnings = new List<string>();
                    AppDomain.CurrentDomain.SetData(WarningsKey, warnings);
                }
                warnings.Add(message);
            }
        }

        public BackgroundLogCleanup(string filename)
        {
            outputPath = Path.GetFullPath(filename);
            reservedPaths = new string[] { outputPath,
                Path.ChangeExtension(outputPath, ".log"), outputPath + ".log" };
            lock (RegistryLock)
            {
                Dictionary<string, string> jobs = Jobs();
                foreach (string path in reservedPaths)
                    if (jobs.ContainsKey(path))
                        throw new InvalidOperationException(
                            "Предыдущий фоновый экспорт с этим именем ещё не завершён.\n"
                            + "Дождитесь его завершения или введите другое имя файла.");
                foreach (string path in reservedPaths) jobs.Add(path, jobId);
            }
            try
            {
                // Запускаем файловый помощник ДО передачи задания переводчику.
                // Если поток создать нельзя, никаких изменений модели ещё нет.
                System.Threading.Thread worker = new System.Threading.Thread(Run);
                worker.IsBackground = true;
                worker.Name = "NX DXF file cleanup";
                worker.Start();
            }
            catch
            {
                ReleasePaths();
                throw;
            }
        }

        public void SetLogs(FileSnapshot[] snapshots) { logs = snapshots; }
        public void TranslationLaunched() { translationLaunched = true; }
        public void ScriptReturned() { scriptReturned = true; }

        private void ReleasePaths()
        {
            lock (RegistryLock)
            {
                Dictionary<string, string> jobs = Jobs();
                foreach (string path in reservedPaths)
                {
                    string owner;
                    if (jobs.TryGetValue(path, out owner) && owner == jobId)
                        jobs.Remove(path);
                }
            }
        }

        private static string FileStamp(string filename)
        {
            FileInfo info = new FileInfo(filename);
            if (!info.Exists) return "missing";
            return info.Length.ToString(CultureInfo.InvariantCulture) + ":"
                + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        private void Run()
        {
            // Ни одно исключение файлового помощника не должно выйти в процесс NX.
            try
            {
                while (!scriptReturned) System.Threading.Thread.Sleep(100);
                if (!translationLaunched) return;

                System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
                string previousStamp = null;
                string verifiedOutputStamp = null;
                int quietPolls = 0;
                bool[] restored = new bool[logs.Length];
                string lastProblem = "Файл DXF пока не завершён.";
                while (elapsed.Elapsed < TimeSpan.FromMinutes(30))
                {
                    System.Threading.Thread.Sleep(500);
                    try
                    {
                        string outputStamp = FileStamp(outputPath);
                        string stamp = outputStamp;
                        for (int i = 0; i < logs.Length; i++)
                            if (!restored[i]) stamp += "|" + FileStamp(logs[i].Filename);
                        quietPolls = stamp == previousStamp ? quietPolls + 1 : 0;
                        previousStamp = stamp;
                        // Не трогаем файлы во время записи. После двух секунд покоя
                        // проверяем DXF до EOF, открывая его с исключительным доступом.
                        if (quietPolls < 4) continue;
                        if (verifiedOutputStamp != outputStamp)
                        {
                            CheckDxf(outputPath);
                            verifiedOutputStamp = outputStamp;
                        }
                        for (int i = 0; i < logs.Length; i++)
                        {
                            if (restored[i]) continue;
                            if (File.Exists(logs[i].Filename))
                                using (FileStream probe = new FileStream(logs[i].Filename,
                                    FileMode.Open, FileAccess.Read, FileShare.None)) { }
                            logs[i].Restore();
                            restored[i] = true;
                        }
                        return;
                    }
                    catch (IOException ex) { lastProblem = ex.Message; }
                    catch (UnauthorizedAccessException ex) { lastProblem = ex.Message; }
                }
                RememberWarning("Не удалось подтвердить завершение фонового экспорта "
                    + "или убрать его служебный журнал:\n" + outputPath + "\n" + lastProblem
                    + "\nПроверьте состояние штатной задачи NX. Файл DXF не изменялся помощником.");
            }
            catch (Exception ex)
            {
                try { RememberWarning("Ошибка файловой очистки после фонового экспорта:\n"
                    + outputPath + "\n" + ex.Message); }
                catch (Exception) { }
            }
            finally
            {
                try { ReleasePaths(); }
                catch (Exception) { }
            }
        }
    }

    private sealed class FileSnapshot
    {
        public readonly string Filename;
        public readonly bool Existed;
        private readonly byte[] bytes;
        private readonly DateTime writeTime;
        private readonly DateTime creationTime;
        private readonly FileAttributes attributes;

        public FileSnapshot(string filename)
        {
            Filename = filename;
            Existed = File.Exists(filename);
            if (!Existed) return;
            attributes = File.GetAttributes(filename);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                throw new IOException("Файл доступен только для чтения:\n" + filename);
            writeTime = File.GetLastWriteTimeUtc(filename);
            creationTime = File.GetCreationTimeUtc(filename);
            bytes = File.ReadAllBytes(filename);
        }

        public void Restore()
        {
            if (!Existed)
            {
                if (File.Exists(Filename)) File.Delete(Filename);
                return;
            }
            // Не переписываем старые журналы, если переводчик их не изменял.
            if (File.Exists(Filename)
                && File.GetLastWriteTimeUtc(Filename) == writeTime
                && new FileInfo(Filename).Length == bytes.LongLength)
            {
                byte[] current = File.ReadAllBytes(Filename);
                bool equal = current.Length == bytes.Length;
                for (int i = 0; equal && i < current.Length; i++)
                    equal = current[i] == bytes[i];
                if (equal) return;
            }
            File.WriteAllBytes(Filename, bytes);
            File.SetCreationTimeUtc(Filename, creationTime);
            File.SetLastWriteTimeUtc(Filename, writeTime);
            File.SetAttributes(Filename, attributes);
        }
    }

    public static int GetUnloadOption(string dummy)
    {
        // Файловая очистка может завершиться после возврата Main.
        return (int)Session.LibraryUnloadOption.AtTermination;
    }
}
