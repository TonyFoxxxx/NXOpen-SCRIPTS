// Экспорт в PDF
// SCRIPT_VERSION: V1.11
// Рабочий файл: NX_Export_Drawing_To_PDF.cs
// Файл для выдачи: NX_Export_Drawing_To_PDF_V1.11.cs
// Siemens NX / Designcenter, C# journal (Windows).
// Запуск: Tools / Сервис -> Journal / Журнал -> Play / Воспроизвести.
//
// Источник: текущий чертёжный лист NX целиком, в его собственном формате.
// Результат: <папка открытого проекта>\<введённое имя>.pdf.
// По умолчанию предлагается имя проекта; оно полностью выделено в поле ввода.
// Повторный запуск обновляет PDF под тем же именем.
// Единственный выходной файл — PDF; отчётов, логов и временных PDF нет.
// Проект .prt должен быть хотя бы один раз сохранён на диск.
//
// V1.11: восемь независимых групп линий и живой предпросмотр в NX.
// Контуры видов/разрезов и штриховка настраиваются независимо.
// Исходные свойства каждого затрагиваемого объекта хранятся только в памяти.
// После отмены, ошибки или экспорта: Undo, явное восстановление и проверка.
// Для изменения толщин не вызывается Update(), пересоздающий сечения видов.
// Включение отображения толщин временное и также восстанавливается.
// Числовые значения — девять стандартных физических толщин NX.
//
// V1.10: перед вводом имени — меню выбора толщин линий для текущего экспорта.
// Каждый запуск начинает с прежних значений профиля для формата листа.
// Контур, тонкие линии и общий знак шероховатости настраиваются отдельно.
// Выбор из стандартных толщин NX: 0.13 / 0.18 / 0.25 / 0.35 / 0.50 /
// 0.70 / 1.00 / 1.40 / 2.00 мм. Настройки между запусками не сохраняются.
//
// v1.8: окно «Введите имя файла» с выделенным именем проекта.
// Enter / «Сохранить» — экспорт; Esc / «Отмена» / крестик — выход без изменений.
// Расширение .pdf добавляется автоматически; путь в поле имени не принимается.
// Окно создаётся в памяти через Win32; дополнительные файлы и сборки не нужны.
//
// v1.7: Output Text = Text — надписи экспортируются в PDF как текст.
// Имя и папка PDF, оформление линий и восстановление .prt сохранены.
//
// v1.5: оформление линий существующего чертежа для вывода по ЕСКД.
// Значения профиля по умолчанию: основная 0.50 мм, тонкая 0.25 мм (А4, А3, А2).
// Для листов с большей стороной >= 841 мм: 0.70 / 0.35 мм.
// Осевые, скрытые линии, размеры, выноски, штриховка и рамки допусков
// получают тонкую толщину отдельно от видимого контура детали.
// Общий знак шероховатости, созданный ESKD-скриптом, остаётся увеличенным.
// ГОСТ 2.307-2011: размерные стрелки 3 мм / 20 градусов, выступы 2 мм.
// ГОСТ 2.305-2008: стрелки разрезов 5 мм / 20 градусов; штрихи 10 мм.
// ГОСТ 2.309-73: тонкие знаки шероховатости, графический стандарт ESKD.
// ГОСТ Р 2.308-2023: тонкие сплошные рамки и выноски допусков.
// Сохраняются шрифты ГОСТ типа А из NX_ESKD_Format_GOST_A, значения,
// ассоциативность, расположение, масштаб видов и геометрия разрезов.
// Буквы, посадки, шероховатость, справочные размеры и их текстовые
// пояснения не выводятся из геометрии и не подменяются автоматически.
// Скрипт не является полной инженерной проверкой чертежа.
//
// Текущий DrawingSheet целиком; физический размер листа, масштаб 1:1.
// Изменения оформления применяются только на время экспорта и отменяются.
// Прямая запись единственного PDF. Предыдущий PDF хранится в памяти.
// Без отчётов, журналов, временных файлов, установки шрифтов и сохранения .prt.
// API: Siemens NXOpen / uf_drf.h / uf_drf_types.h (NX 2406).
// Важно: UF_DRF_THICKNESS_ONE..NINE = 6..14; это НЕ коды OBJ ширин.
//
// Настройки экспорта:
// As Displayed; Standard Widths; Scale Factor 1.0000;
// Text; Raster Images = true; Image Resolution = High;
// Append = false; Add Watermark = false; Shaded Geometry = false;
// Custom Symbols in Foreground = false.

using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Reflection;
using NXOpen;
using NXOpen.Annotations;
using NXOpen.UF;
using NXOpen.Drawings;

public class NX_Export_Drawing_To_PDF
{
    private const string SCRIPT_VERSION = "V1.11";
    private const string SCRIPT_NAME = "Экспорт в PDF";
    private const string Title = SCRIPT_NAME + " — " + SCRIPT_VERSION + " — by Tony_Foxxx";
    private const string GeneralRoughnessAttribute = "NX_ESKD_GENERAL_ROUGHNESS";

    // Every entry ties the displayed physical width to all three NX enum types.
    // UF_DRF uses a separate set of codes for native centerlines and hatch objects.
    private sealed class WidthChoice
    {
        public readonly string Label;
        public readonly NXOpen.Preferences.Width View;
        public readonly DisplayableObject.ObjectWidth Object;
        public readonly LineWidth Annotation;
        public readonly int Native;

        public WidthChoice(string label, NXOpen.Preferences.Width view,
            DisplayableObject.ObjectWidth objectWidth, LineWidth annotation, int native)
        {
            Label = label;
            View = view;
            Object = objectWidth;
            Annotation = annotation;
            Native = native;
        }
    }

    private static readonly WidthChoice[] WidthChoices = new WidthChoice[]
    {
        new WidthChoice("0,13 мм", NXOpen.Preferences.Width.One,
            DisplayableObject.ObjectWidth.One, LineWidth.One, 6),
        new WidthChoice("0,18 мм", NXOpen.Preferences.Width.Two,
            DisplayableObject.ObjectWidth.Two, LineWidth.Two, 7),
        new WidthChoice("0,25 мм", NXOpen.Preferences.Width.Three,
            DisplayableObject.ObjectWidth.Three, LineWidth.Three, 8),
        new WidthChoice("0,35 мм", NXOpen.Preferences.Width.Four,
            DisplayableObject.ObjectWidth.Four, LineWidth.Four, 9),
        new WidthChoice("0,50 мм", NXOpen.Preferences.Width.Five,
            DisplayableObject.ObjectWidth.Five, LineWidth.Five, 10),
        new WidthChoice("0,70 мм", NXOpen.Preferences.Width.Six,
            DisplayableObject.ObjectWidth.Six, LineWidth.Six, 11),
        new WidthChoice("1,00 мм", NXOpen.Preferences.Width.Seven,
            DisplayableObject.ObjectWidth.Seven, LineWidth.Seven, 12),
        new WidthChoice("1,40 мм", NXOpen.Preferences.Width.Eight,
            DisplayableObject.ObjectWidth.Eight, LineWidth.Eight, 13),
        new WidthChoice("2,00 мм", NXOpen.Preferences.Width.Nine,
            DisplayableObject.ObjectWidth.Nine, LineWidth.Nine, 14)
    };

    private enum LineGroup
    {
        Contour, Dimensions, Axes, Hatch, Hidden, Frame, SectionMarks, GeneralRoughness
    }

    private static readonly string[] GroupLabels = new string[]
    {
        "Контуры видов и разрезов",
        "Размеры, выноски и обозначения",
        "Осевые линии и центровые метки",
        "Штриховка",
        "Скрытые линии",
        "Рамка листа ЕСКД",
        "Линии и стрелки обозначения разрезов",
        "Общий знак шероховатости"
    };

    private sealed class WidthProfile
    {
        private readonly int[] indices;

        public WidthChoice this[LineGroup group]
        {
            get { return WidthChoices[indices[(int)group]]; }
        }

        public int Index(LineGroup group) { return indices[(int)group]; }

        public bool Changed(LineGroup group, WidthProfile previous)
        {
            return previous == null || Index(group) != previous.Index(group);
        }

        // The same defaults as the previous revision, recalculated on every run.
        public WidthProfile(DrawingSheet sheet)
            : this(Math.Max(sheet.Length, sheet.Height) >= 841.0 - 0.001)
        {
        }

        private WidthProfile(bool large)
            : this(new int[] { large ? 5 : 4, large ? 3 : 2,
                large ? 3 : 2, large ? 3 : 2, large ? 3 : 2,
                large ? 5 : 4, large ? 5 : 4, large ? 4 : 3 })
        {
        }

        public WidthProfile(int[] selectedIndices)
        {
            if (selectedIndices == null || selectedIndices.Length != GroupLabels.Length)
                throw new ArgumentException("Неверное число групп линий.");
            indices = (int[])selectedIndices.Clone();
            foreach (int index in indices)
                if (index < 0 || index >= WidthChoices.Length)
                    throw new ArgumentOutOfRangeException("index", "Выберите толщины из списка.");
        }
    }

    public static void Main(string[] args)
    {
        Session session = Session.GetSession();
        UI ui = UI.GetUI();

        try
        {
            // Берём лист отображаемой детали. Рабочим компонентом сборки
            // в это время может быть другая деталь.
            Part part = session.Parts.Display;
            if (part == null)
                throw new InvalidOperationException("Сначала откройте проект в NX.");

            DrawingSheet sheet = part.DrawingSheets.CurrentDrawingSheet;
            if (sheet == null)
                throw new InvalidOperationException(
                    "Откройте нужный чертёжный лист в NX и повторите экспорт.");

            if (sheet.Units != DrawingSheet.Unit.Millimeters)
                throw new InvalidOperationException(
                    "Для этого профиля ЕСКД нужен чертёжный лист в миллиметрах.");

            string partPath = part.FullPath;
            if (String.IsNullOrEmpty(partPath) || !Path.IsPathRooted(partPath))
                throw new InvalidOperationException(
                    "Сначала сохраните проект .prt в нужную папку на диске.");

            string projectFolder = Path.GetDirectoryName(partPath);
            if (String.IsNullOrEmpty(projectFolder) || !Directory.Exists(projectFolder))
                throw new DirectoryNotFoundException(
                    "Папка проекта недоступна:\n" + projectFolder);

            if (!File.Exists(partPath))
                throw new InvalidOperationException(
                    "Файл проекта не найден на диске. Сохраните .prt и повторите экспорт.\n\n" + partPath);

            // Снимок свойств — ДО первого изменения. Никаких файлов настроек.
            IntPtr parent = UFSession.GetUFSession().Ui.GetDefaultParent();
            AppearanceTransaction preview = new AppearanceTransaction(session, part, sheet, parent);
            string outputPdf = null;
            Exception operationError = null;
            bool pdfSaved = false;
            try
            {
                WidthProfile widths;
                if (!PdfExportDialogs.ShowWidths(parent, new WidthProfile(sheet),
                    preview.ApplyPreview, out widths)) return;
                string fileName;
                if (!PdfExportDialogs.Show(parent,
                    Path.GetFileNameWithoutExtension(partPath), out fileName)) return;
                outputPdf = Path.Combine(projectFolder, fileName);
                preview.ApplyForExport(widths);
                ExportSheet(part, sheet, outputPdf);
                pdfSaved = true;
            }
            catch (Exception ex)
            {
                operationError = ex;
                throw;
            }
            finally
            {
                try { preview.Restore(); }
                catch (Exception restoreError)
                {
                    throw new InvalidOperationException(
                        (pdfSaved ? "PDF создан:\n" + outputPdf + "\n\n" : String.Empty) +
                        "Не удалось полностью восстановить исходное оформление.\n" +
                        restoreError.Message +
                        (operationError == null ? String.Empty :
                            "\n\nОшибка операции: " + operationError.Message), restoreError);
                }
            }

            ui.NXMessageBox.Show(Title, NXMessageBox.DialogType.Information,
                "PDF сохранён:\n" + outputPdf);
        }
        catch (Exception ex)
        {
            ui.NXMessageBox.Show(Title, NXMessageBox.DialogType.Error, ex.Message);
        }
    }

    // Only properties actually written by this script are captured. All values live in RAM.
    private sealed class CapturedProperties
    {
        private readonly List<KeyValuePair<string, object>> values = new List<KeyValuePair<string, object>>();

        public CapturedProperties(object root, string[] paths)
        {
            foreach (string path in paths)
            {
                object owner;
                PropertyInfo property = Resolve(root, path, out owner);
                if (!property.CanRead || !property.CanWrite)
                    throw new InvalidOperationException("Нельзя сохранить и восстановить свойство NX: " + path);
                values.Add(new KeyValuePair<string, object>(path, property.GetValue(owner, null)));
            }
        }

        private static PropertyInfo Resolve(object root, string path, out object owner)
        {
            string[] names = path.Split('.');
            owner = root;
            for (int i = 0; i < names.Length; i++)
            {
                if (owner == null) throw new InvalidOperationException("Недоступно свойство NX: " + path);
                PropertyInfo property = owner.GetType().GetProperty(names[i], BindingFlags.Instance | BindingFlags.Public);
                if (property == null) throw new InvalidOperationException("Свойство NX не найдено: " + path);
                if (i == names.Length - 1) return property;
                owner = property.GetValue(owner, null);
            }
            throw new InvalidOperationException("Пустой путь свойства NX.");
        }

        private static bool EqualValue(object a, object b)
        {
            if (Object.Equals(a, b)) return true;
            if (a is double && b is double)
                return Math.Abs((double)a - (double)b) <= 1e-9 * Math.Max(1.0, Math.Abs((double)b));
            return Object.Equals(a, b);
        }

        public bool Restore(object root, List<string> errors)
        {
            bool changed = false;
            foreach (KeyValuePair<string, object> value in values)
            {
                try
                {
                    object owner;
                    PropertyInfo property = Resolve(root, value.Key, out owner);
                    if (EqualValue(property.GetValue(owner, null), value.Value)) continue;
                    property.SetValue(owner, value.Value, null);
                    changed = true;
                }
                catch (Exception ex) { errors.Add(value.Key + ": " + ErrorText(ex)); }
            }
            return changed;
        }

        public void Verify(object root, List<string> errors)
        {
            foreach (KeyValuePair<string, object> value in values)
            {
                try
                {
                    object owner;
                    PropertyInfo property = Resolve(root, value.Key, out owner);
                    if (!EqualValue(property.GetValue(owner, null), value.Value))
                        errors.Add("NX не восстановил " + value.Key);
                }
                catch (Exception ex) { errors.Add(value.Key + ": " + ErrorText(ex)); }
            }
        }
    }

    private static string ErrorText(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
        return ex.Message;
    }

    private static void ThrowRestoreErrors(List<string> errors)
    {
        if (errors.Count == 0) return;
        // Bounded text in the existing error window, never a report/log file.
        int count = Math.Min(errors.Count, 12);
        throw new InvalidOperationException(String.Join("\n", errors.GetRange(0, count).ToArray()) +
            (errors.Count > count ? "\nДругих ошибок: " + (errors.Count - count).ToString() : String.Empty));
    }

    private static void AddPaths(List<string> paths, string prefix, string names)
    {
        foreach (string name in names.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            paths.Add(prefix + name);
    }

    private static string[] AnnotationPaths(bool dimension)
    {
        List<string> paths = new List<string>();
        AddPaths(paths, "AnnotationStyle.LineArrowStyle.",
            "FirstArrowLineWidth SecondArrowLineWidth FirstExtensionLineWidth SecondExtensionLineWidth " +
            "FirstArrowheadWidth SecondArrowheadWidth FirstArrowLineFont SecondArrowLineFont " +
            "FirstExtensionLineFont SecondExtensionLineFont FirstArrowheadFont SecondArrowheadFont");
        AddPaths(paths, "AnnotationStyle.SymbolStyle.",
            "CenterlineSymbolWidth GdtSymbolWidth GdtSymbolFont IdSymbolWidth IdSymbolFont " +
            "IntersectionSymbolWidth TargetSymbolWidth SurfaceFinishWidth SurfaceFinishFont " +
            "DraftingSurfaceFinishStandard EdgeConditionWidth");
        if (dimension)
        {
            AddPaths(paths, "AnnotationStyle.LineArrowStyle.",
                "ArrowheadLength ArrowheadIncludedAngle FirstArrowType SecondArrowType " +
                "LinePastArrowDistance LinePastArrowDistance2");
            AddPaths(paths, "AnnotationStyle.UnitsStyle.",
                "DecimalPointCharacter DisplayLeadingDimensionZeros DisplayTrailingZeros");
            AddPaths(paths, "AnnotationStyle.LetteringStyle.",
                "DimensionTextSize AppendedTextSize ToleranceTextSize TwoLineToleranceTextSize DimLineSpaceFactor");
        }
        return paths.ToArray();
    }

    private static string[] SectionLinePaths()
    {
        List<string> paths = new List<string>();
        AddPaths(paths, "ViewSectionLine.",
            "TypeStandard Style ArrowheadAngle ArrowheadLength ArrowLength Overhang UseLineLength " +
            "LineLength LineColorFontWidth.LineWidth BendAndEndSegmentWidthFactor");
        return paths.ToArray();
    }

    private static string[] SectionLabelPaths()
    {
        List<string> paths = new List<string>();
        paths.Add("AnnotationStyle.LetteringStyle.GeneralTextSize");
        AddPaths(paths, "ViewSectionLabel.",
            "CustomizedViewLabel ViewLabelOption LetterFormat LabelPrefix LabelCharacterHeightFactor " +
            "PrefixCharacterHeightFactor ScaleCharacterHeightFactor ScalePrefix IncludeParentheses");
        return paths.ToArray();
    }

    private abstract class SavedState
    {
        public readonly string Description;
        protected SavedState(string description) { Description = description; }
        public abstract void Restore(Part part, UFSession uf);
    }

    private sealed class ViewState : SavedState
    {
        private readonly Tag tag;
        private readonly CapturedProperties properties;
        public ViewState(DraftingView view) : base("Вид " + view.Name)
        {
            tag = view.Tag;
            properties = new CapturedProperties(view.Style,
                new string[] { "VisibleLines.VisibleWidth", "HiddenLines.HiddenlineWidth" });
        }
        public override void Restore(Part part, UFSession uf)
        {
            DraftingView view = (DraftingView)NXOpen.Utilities.NXObjectManager.Get(tag);
            List<string> errors = new List<string>();
            if (properties.Restore(view.Style, errors))
            {
                try { view.Commit(); }
                catch (Exception ex) { errors.Add(ErrorText(ex)); }
            }
            properties.Verify(view.Style, errors);
            ThrowRestoreErrors(errors);
        }
    }

    private enum SettingsKind { Annotation, SectionLine, SectionLabel }

    private sealed class SettingsState : SavedState
    {
        private readonly Tag owner;
        private readonly SettingsKind kind;
        private readonly CapturedProperties properties;

        // For a section label, owner is the view tag, not the regeneratable label tag.
        public SettingsState(Part part, UFSession uf, Tag ownerTag, SettingsKind settingsKind, string[] paths)
            : base(settingsKind.ToString() + " " + ownerTag.ToString())
        {
            owner = ownerTag;
            kind = settingsKind;
            Builder builder = OpenBuilder(part, uf);
            try { properties = new CapturedProperties(builder, paths); }
            finally { builder.Destroy(); }
        }

        private Builder OpenBuilder(Part part, UFSession uf)
        {
            Tag tag = owner;
            if (kind == SettingsKind.SectionLabel) uf.Draw.AskViewLabel(owner, out tag);
            if (tag == Tag.Null) throw new InvalidOperationException("Исходная подпись вида недоступна.");
            DisplayableObject obj = NXOpen.Utilities.NXObjectManager.Get(tag) as DisplayableObject;
            if (obj == null) throw new InvalidOperationException("Исходный объект недоступен: " + tag.ToString());
            if (kind == SettingsKind.SectionLine)
                return part.SettingsManager.CreateDrawingEditSectionLineSettingsBuilder(new SectionLine[] { (SectionLine)obj });
            if (kind == SettingsKind.SectionLabel)
                return part.SettingsManager.CreateDrawingEditViewLabelSettingsBuilder(new DisplayableObject[] { obj });
            return part.SettingsManager.CreateAnnotationEditSettingsBuilder(new DisplayableObject[] { obj });
        }

        public override void Restore(Part part, UFSession uf)
        {
            List<string> errors = new List<string>();
            Builder builder = OpenBuilder(part, uf);
            try
            {
                if (properties.Restore(builder, errors))
                {
                    try { builder.Commit(); }
                    catch (Exception ex) { errors.Add(ErrorText(ex)); }
                }
            }
            finally { builder.Destroy(); }
            // Read the committed NX state through a NEW builder, not the setter's cache.
            builder = OpenBuilder(part, uf);
            try { properties.Verify(builder, errors); }
            finally { builder.Destroy(); }
            ThrowRestoreErrors(errors);
        }
    }

    private sealed class NativeWidthState : SavedState
    {
        private readonly Tag tag;
        private readonly int index, original;
        public NativeWidthState(UFSession uf, Tag objectTag, int preferenceIndex)
            : base("Толщина осевой/штриховки " + objectTag.ToString())
        {
            tag = objectTag;
            index = preferenceIndex;
            int[] integers = new int[100];
            double[] reals = new double[70];
            string radius, diameter;
            uf.Drf.AskObjectPreferences(tag, integers, reals, out radius, out diameter);
            original = integers[index];
        }
        public override void Restore(Part part, UFSession uf)
        {
            SetNativeDraftingWidth(uf, tag, index, original); // Includes native read-back verification.
        }
    }

    private sealed class CurveWidthState : SavedState
    {
        private readonly Tag tag;
        private readonly DisplayableObject.ObjectWidth width;
        public CurveWidthState(Curve curve) : base("Линия " + curve.Tag.ToString())
        {
            tag = curve.Tag;
            width = curve.LineWidth;
        }
        public override void Restore(Part part, UFSession uf)
        {
            Curve curve = (Curve)NXOpen.Utilities.NXObjectManager.Get(tag);
            if (curve.LineWidth != width)
            {
                curve.LineWidth = width;
                curve.RedisplayObject();
            }
            if (curve.LineWidth != width) throw new InvalidOperationException("NX не восстановил толщину линии.");
        }
    }

    private static List<SavedState> CaptureAppearance(Part part, DrawingSheet sheet, UFSession uf)
    {
        List<SavedState> states = new List<SavedState>();
        DraftingView[] views = sheet.GetDraftingViews();
        HashSet<Tag> sheetViews = new HashSet<Tag>();
        sheetViews.Add(sheet.View.Tag);
        foreach (DraftingView view in views)
        {
            sheetViews.Add(view.Tag);
            if (!view.IsDecoration) states.Add(new ViewState(view));
        }
        foreach (SectionLine line in part.Drafting.SectionLines.ToArray())
        {
            Tag parent;
            ReadSectionSegments(uf, line.Tag, out parent);
            if (sheetViews.Contains(parent))
                states.Add(new SettingsState(part, uf, line.Tag, SettingsKind.SectionLine, SectionLinePaths()));
        }
        HashSet<Tag> labels = new HashSet<Tag>();
        foreach (DraftingView view in views)
        {
            if (view.IsDecoration) continue;
            Tag label;
            uf.Draw.AskViewLabel(view.Tag, out label);
            if (label == Tag.Null) continue;
            labels.Add(label);
            if (!(view is SectionView)) continue;
            DisplayableObject obj = NXOpen.Utilities.NXObjectManager.Get(label) as DisplayableObject;
            if (obj != null && !obj.IsBlanked)
                states.Add(new SettingsState(part, uf, view.Tag, SettingsKind.SectionLabel, SectionLabelPaths()));
        }
        HashSet<Tag> sheetObjects = new HashSet<Tag>();
        foreach (Tag view in sheetViews) CollectViewObjects(uf, view, sheetObjects);
        List<Tag> candidates = new List<Tag>();
        Tag tag = Tag.Null;
        while ((tag = uf.Obj.CycleAll(part.Tag, tag)) != Tag.Null)
            if (!labels.Contains(tag) && IsOnSheet(uf, tag, sheetViews, sheetObjects)) candidates.Add(tag);
        foreach (Tag item in candidates)
        {
            int type, subtype;
            uf.Obj.AskTypeAndSubtype(item, out type, out subtype);
            if (type == UFConstants.UF_drafting_entity_type && subtype == UFConstants.UF_draft_crosshatch_subtype)
            {
                states.Add(new NativeWidthState(uf, item, 68));
                continue;
            }
            DisplayableObject obj;
            try { obj = NXOpen.Utilities.NXObjectManager.Get(item) as DisplayableObject; }
            catch (NXException) { continue; }
            if (obj == null || obj.IsBlanked || obj.OwningPart == null || obj.OwningPart.Tag != part.Tag) continue;
            if (obj is Centerline)
                states.Add(new NativeWidthState(uf, item, 66));
            else if (obj is Annotation && !(obj is TableSection))
                states.Add(new SettingsState(part, uf, item, SettingsKind.Annotation, AnnotationPaths(obj is Dimension)));
            else if (obj is Curve)
            {
                Curve curve = (Curve)obj;
                int dependent; string owner;
                uf.View.AskViewDependentStatus(item, out dependent, out owner);
                if (dependent == 1 && (curve.LineFont == DisplayableObject.ObjectFont.Centerline || IsEskdFrameLine(curve.Name)))
                    states.Add(new CurveWidthState(curve));
            }
        }
        return states;
    }

    private sealed class AppearanceTransaction
    {
        private readonly Session session;
        private readonly Part part;
        private readonly DrawingSheet sheet;
        private readonly UFSession uf;
        private readonly IntPtr parent;
        private readonly List<SavedState> states;
        private readonly bool originalShowWidths;
        private readonly Session.UndoMarkId mark;
        private WidthProfile previous;
        private bool touched, restored;

        public AppearanceTransaction(Session nxSession, Part drawingPart, DrawingSheet drawingSheet, IntPtr nxParent)
        {
            session = nxSession;
            part = drawingPart;
            sheet = drawingSheet;
            parent = nxParent;
            uf = UFSession.GetUFSession();
            originalShowWidths = part.Preferences.LineVisualization.ShowWidths;
            // Fail before any changes if even one required original property cannot be read.
            states = CaptureAppearance(part, sheet, uf);
            mark = session.SetUndoMark(Session.MarkVisibility.Invisible, Title + ": предпросмотр PDF");
        }

        public void ApplyPreview(WidthProfile widths)
        {
            bool changed = previous == null;
            for (int i = 0; !changed && i < GroupLabels.Length; i++)
                changed = widths.Changed((LineGroup)i, previous);
            if (!changed) return;
            touched = true; // Set BEFORE writing, so partial failures also trigger restoration.
            part.Preferences.LineVisualization.ShowWidths = true;
            ApplyExportAppearance(part, sheet, widths, previous, false);
            previous = widths;
            PdfExportDialogs.RefreshParent(parent);
        }

        public void ApplyForExport(WidthProfile widths)
        {
            touched = true;
            ApplyExportAppearance(part, sheet, widths, previous, true);
            previous = widths;
        }

        public void Restore()
        {
            if (restored) return;
            restored = true;
            List<string> errors = new List<string>();
            if (touched)
            {
                try { session.UndoToMark(mark, null); }
                catch (Exception ex) { errors.Add("Undo NX: " + ErrorText(ex)); }
                // Some NX preferences are outside ordinary Undo. Explicitly restore all of them
                // even if Undo failed; one failed object must never prevent restoring the rest.
                foreach (SavedState state in states)
                {
                    try { state.Restore(part, uf); }
                    catch (Exception ex) { errors.Add(state.Description + ": " + ErrorText(ex)); }
                }
                try
                {
                    part.Preferences.LineVisualization.ShowWidths = originalShowWidths;
                    if (part.Preferences.LineVisualization.ShowWidths != originalShowWidths)
                        errors.Add("NX не восстановил настройку отображения толщин.");
                }
                catch (Exception ex) { errors.Add("Отображение толщин: " + ErrorText(ex)); }
                try { uf.Disp.RegenerateDisplay(); PdfExportDialogs.RefreshParent(parent); }
                catch (Exception ex) { errors.Add("Обновление экрана: " + ErrorText(ex)); }
            }
            if (errors.Count == 0)
            {
                try { session.DeleteUndoMark(mark, null); }
                catch (Exception ex) { errors.Add("Завершение отката: " + ErrorText(ex)); }
            }
            ThrowRestoreErrors(errors);
        }
    }

    private static bool TryPdfFileName(string input, out string fileName, out string error)
    {
        fileName = null;
        error = null;
        string name = (input ?? String.Empty).Trim();
        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4).TrimEnd();
        if (name.Length == 0 || name == "." || name == "..")
        {
            error = "Введите имя файла.";
            return false;
        }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.IndexOfAny(new char[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }) >= 0)
        {
            error = "Введите только имя, без пути и символов < > : \" / \\ | ? *.";
            return false;
        }
        if (name.EndsWith(".", StringComparison.Ordinal))
        {
            error = "Имя файла не должно оканчиваться точкой.";
            return false;
        }
        // Запрещены имена устройств Windows, в том числе с любым расширением.
        int dot = name.IndexOf('.');
        string device = (dot < 0 ? name : name.Substring(0, dot)).TrimEnd().ToUpperInvariant();
        if (device == "CON" || device == "PRN" || device == "AUX" || device == "NUL" ||
            device == "CONIN$" || device == "CONOUT$" ||
            (device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) ||
                device.StartsWith("LPT", StringComparison.Ordinal)) &&
                "123456789\u00B9\u00B2\u00B3".IndexOf(device[3]) >= 0))
        {
            error = "Это имя зарезервировано Windows. Введите другое имя.";
            return false;
        }
        fileName = name + ".pdf";
        if (fileName.Length > 255)
        {
            error = "Слишком длинное имя: максимум 251 символ без .pdf.";
            fileName = null;
            return false;
        }
        return true;
    }

    // Native Windows dialog: no WinForms/System.Drawing references and no .dlx file.
    private static class PdfExportDialogs
    {
        private const int EditId = 101;
        private const int HintId = 202;
        private const int MaxTextLength = 255;
        private const int FirstWidthId = 301;
        private const int PreviewHintId = 220;
        private const string PreviewHint = "Выбор сразу отображается на чертеже. Изменения временные.\r\n" +
            "Доступны девять стандартных толщин NX: 0,13–2,00 мм.";

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr DialogProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr DialogBoxIndirectParamW(IntPtr instance, IntPtr template,
            IntPtr parent, DialogProcedure procedure, IntPtr parameter);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EndDialog(IntPtr window, IntPtr result);
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr GetDlgItem(IntPtr window, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDlgItemTextW(IntPtr window, int id, string text);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetDlgItemTextW(IntPtr window, int id, StringBuilder text, int capacity);
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr SetFocus(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr SendMessageTextW(IntPtr window, uint message, IntPtr wParam, string text);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern UIntPtr SetTimer(IntPtr window, UIntPtr id, uint interval, IntPtr callback);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool KillTimer(IntPtr window, UIntPtr id);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RedrawWindow(IntPtr window, IntPtr rectangle, IntPtr region, uint flags);

        public static void RefreshParent(IntPtr parent)
        {
            // Paint the disabled owner and its graphics children without enabling NX commands.
            RedrawWindow(parent, IntPtr.Zero, IntPtr.Zero, 0x0181); // INVALIDATE | ALLCHILDREN | UPDATENOW.
        }

        public static bool ShowWidths(IntPtr parent, WidthProfile initial,
            Action<WidthProfile> applyPreview, out WidthProfile widths)
        {
            widths = null;
            WidthProfile selected = null;
            if (parent == IntPtr.Zero)
                throw new InvalidOperationException("Не удалось получить главное окно NX для выбора толщин.");
            if (initial == null) throw new ArgumentNullException("initial");
            if (applyPreview == null) throw new ArgumentNullException("applyPreview");

            byte[] template = BuildWidthsTemplate();
            Exception callbackError = null;
            bool ready = false, busy = false, cancelRequested = false;
            Action<IntPtr> updatePreview = delegate(IntPtr window)
            {
                if (cancelRequested) { KillTimer(window, new UIntPtr(1)); return; }
                if (busy) { SchedulePreview(window); return; }
                KillTimer(window, new UIntPtr(1));
                busy = true;
                try
                {
                    SetDlgItemTextW(window, PreviewHintId, "Обновление предпросмотра…");
                    RedrawWindow(window, IntPtr.Zero, IntPtr.Zero, 0x0181);
                    int[] indices = new int[GroupLabels.Length];
                    for (int i = 0; i < indices.Length; i++)
                        indices[i] = ReadWidthList(window, FirstWidthId + i);
                    WidthProfile next = new WidthProfile(indices);
                    applyPreview(next);
                    selected = next;
                    SetDlgItemTextW(window, PreviewHintId, PreviewHint);
                }
                finally { busy = false; }
                if (cancelRequested) EndDialog(window, new IntPtr(2));
            };
            DialogProcedure callback = delegate(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
            {
                try
                {
                    if (message == 0x0110) // WM_INITDIALOG
                    {
                        for (int i = 0; i < GroupLabels.Length; i++)
                            FillWidthList(window, FirstWidthId + i, initial.Index((LineGroup)i));
                        ready = true;
                        SchedulePreview(window);
                        SetFocus(GetDlgItem(window, FirstWidthId));
                        return IntPtr.Zero;
                    }
                    if (message == 0x0113 && wParam.ToInt64() == 1) // WM_TIMER.
                    {
                        updatePreview(window);
                        return new IntPtr(1);
                    }
                    if (message == 0x0010) // WM_CLOSE
                    {
                        cancelRequested = true;
                        KillTimer(window, new UIntPtr(1));
                        if (!busy) EndDialog(window, new IntPtr(2));
                        return new IntPtr(1);
                    }
                    if (message == 0x0111) // WM_COMMAND
                    {
                        int id = (int)(wParam.ToInt64() & 0xFFFF);
                        if (id == 2) // IDCANCEL / Escape.
                        {
                            cancelRequested = true;
                            KillTimer(window, new UIntPtr(1));
                            if (!busy) EndDialog(window, new IntPtr(2));
                            return new IntPtr(1);
                        }
                        if (id == 1) // IDOK / Enter.
                        {
                            if (busy) return new IntPtr(1);
                            updatePreview(window); // Flush even if the debounce timer has not fired.
                            if (!cancelRequested) EndDialog(window, new IntPtr(1));
                            return new IntPtr(1);
                        }
                        int notification = (int)((wParam.ToInt64() >> 16) & 0xFFFF);
                        if (ready && id >= FirstWidthId && id < FirstWidthId + GroupLabels.Length &&
                            notification == 1) // CBN_SELCHANGE: keyboard and mouse selections.
                        {
                            SchedulePreview(window);
                            return new IntPtr(1);
                        }
                    }
                }
                catch (Exception ex)
                {
                    callbackError = ex;
                    KillTimer(window, new UIntPtr(1));
                    EndDialog(window, new IntPtr(2));
                    return new IntPtr(1);
                }
                return IntPtr.Zero;
            };

            IntPtr memory = Marshal.AllocHGlobal(template.Length);
            try
            {
                Marshal.Copy(template, 0, memory, template.Length);
                IntPtr response = DialogBoxIndirectParamW(IntPtr.Zero, memory, parent, callback, IntPtr.Zero);
                int nativeError = Marshal.GetLastWin32Error();
                if (callbackError != null)
                    throw new InvalidOperationException("Ошибка окна толщин: " + callbackError.Message, callbackError);
                long code = response.ToInt64();
                if (code != 1 && code != 2)
                    throw new InvalidOperationException("Не удалось открыть окно толщин. Код Windows: " + nativeError);
                if (code == 2) return false;
                widths = selected;
                return true;
            }
            finally
            {
                GC.KeepAlive(callback);
                Marshal.FreeHGlobal(memory);
            }
        }

        private static void SchedulePreview(IntPtr window)
        {
            // Coalesce quick changes; no worker thread may call the NX API.
            if (SetTimer(window, new UIntPtr(1), 150, IntPtr.Zero) == UIntPtr.Zero)
                throw new InvalidOperationException("Не удалось запустить обновление предпросмотра.");
        }

        private static void FillWidthList(IntPtr window, int id, int selectedIndex)
        {
            IntPtr combo = GetDlgItem(window, id);
            if (combo == IntPtr.Zero)
                throw new InvalidOperationException("Не удалось создать список толщин.");
            // No CBS_SORT: item indices must match WidthChoices exactly.
            for (int i = 0; i < WidthChoices.Length; i++)
            {
                long index = SendMessageTextW(combo, 0x0143, IntPtr.Zero, WidthChoices[i].Label).ToInt64(); // CB_ADDSTRING
                if (index != i)
                    throw new InvalidOperationException("Не удалось заполнить список толщин.");
            }
            long result = SendMessageW(combo, 0x014E, new IntPtr(selectedIndex), IntPtr.Zero).ToInt64(); // CB_SETCURSEL
            if (result != selectedIndex)
                throw new InvalidOperationException("Не удалось установить текущую толщину.");
        }

        private static int ReadWidthList(IntPtr window, int id)
        {
            IntPtr combo = GetDlgItem(window, id);
            if (combo == IntPtr.Zero)
                throw new InvalidOperationException("Список толщин недоступен.");
            long index = SendMessageW(combo, 0x0147, IntPtr.Zero, IntPtr.Zero).ToInt64(); // CB_GETCURSEL
            if (index < 0 || index >= WidthChoices.Length)
                throw new InvalidOperationException("Выберите толщину из списка.");
            return (int)index;
        }

        private static byte[] BuildWidthsTemplate()
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode))
            {
                writer.Write(0x80C808C0u);
                writer.Write(0u);
                writer.Write((ushort)(4 + 2 * GroupLabels.Length));
                writer.Write((short)0); writer.Write((short)0);
                writer.Write((short)400); writer.Write((short)326);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                WriteString(writer, Title + " — Толщины линий");
                writer.Write((ushort)10);
                WriteString(writer, "Segoe UI");

                AddControl(writer, 0x50000080u, 12, 10, 376, 14, 211, 0x0082,
                    "Толщина линий в PDF, мм");
                for (int i = 0; i < GroupLabels.Length; i++)
                {
                    AddControl(writer, 0x50000080u, 12, (short)(35 + 28 * i), 288, 24,
                        (ushort)(230 + i), 0x0082, GroupLabels[i]);
                    // WS_VSCROLL | WS_TABSTOP | CBS_DROPDOWNLIST; deliberately no CBS_SORT.
                    AddControl(writer, 0x50210003u, 310, (short)(32 + 28 * i), 78, 140,
                        (ushort)(FirstWidthId + i), 0x0085, "");
                }
                AddControl(writer, 0x50000080u, 12, 262, 376, 32, PreviewHintId, 0x0082, PreviewHint);
                AddControl(writer, 0x50010001u, 220, 301, 80, 16, 1, 0x0080, "Далее");
                AddControl(writer, 0x50010000u, 308, 301, 80, 16, 2, 0x0080, "Отмена");
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static bool Show(IntPtr parent, string initialName, out string fileName)
        {
            fileName = null;
            string selectedFileName = null;
            string initial = initialName ?? String.Empty;
            if (parent == IntPtr.Zero)
                throw new InvalidOperationException("Не удалось получить главное окно NX для ввода имени.");
            if (initial.Length > MaxTextLength)
                throw new InvalidOperationException("Имя проекта слишком длинное для поля имени PDF.");

            byte[] template = BuildTemplate();
            Exception callbackError = null;
            DialogProcedure callback = delegate(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
            {
                try
                {
                    if (message == 0x0110) // WM_INITDIALOG
                    {
                        IntPtr edit = GetDlgItem(window, EditId);
                        if (edit == IntPtr.Zero || !SetDlgItemTextW(window, EditId, initial))
                            throw new InvalidOperationException("Не удалось создать поле имени файла.");
                        SendMessageW(edit, 0x00C5, new IntPtr(MaxTextLength), IntPtr.Zero); // EM_LIMITTEXT
                        SelectName(window);
                        return IntPtr.Zero; // Keep the focus assigned explicitly above.
                    }
                    if (message == 0x0010) // WM_CLOSE
                    {
                        EndDialog(window, new IntPtr(2));
                        return new IntPtr(1);
                    }
                    if (message == 0x0111) // WM_COMMAND
                    {
                        int id = (int)(wParam.ToInt64() & 0xFFFF);
                        if (id == 2) // IDCANCEL, including Escape.
                        {
                            EndDialog(window, new IntPtr(2));
                            return new IntPtr(1);
                        }
                        if (id == 1) // IDOK / Enter.
                        {
                            StringBuilder text = new StringBuilder(MaxTextLength + 1);
                            GetDlgItemTextW(window, EditId, text, text.Capacity);
                            string validName, error;
                            if (!TryPdfFileName(text.ToString(), out validName, out error))
                            {
                                SetDlgItemTextW(window, HintId, error);
                                SelectName(window);
                                return new IntPtr(1); // Keep the same dialog open for correction.
                            }
                            selectedFileName = validName;
                            EndDialog(window, new IntPtr(1));
                            return new IntPtr(1);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Exceptions must never cross the unmanaged dialog callback.
                    callbackError = ex;
                    EndDialog(window, new IntPtr(2));
                    return new IntPtr(1);
                }
                return IntPtr.Zero;
            };

            IntPtr memory = Marshal.AllocHGlobal(template.Length);
            try
            {
                Marshal.Copy(template, 0, memory, template.Length);
                IntPtr response = DialogBoxIndirectParamW(IntPtr.Zero, memory, parent, callback, IntPtr.Zero);
                int nativeError = Marshal.GetLastWin32Error();
                if (callbackError != null)
                    throw new InvalidOperationException("Ошибка окна ввода имени: " + callbackError.Message, callbackError);
                long code = response.ToInt64();
                if (code != 1 && code != 2)
                    throw new InvalidOperationException("Не удалось открыть окно ввода имени. Код Windows: " + nativeError);
                if (code == 2) return false;
                fileName = selectedFileName;
                return true;
            }
            finally
            {
                GC.KeepAlive(callback);
                Marshal.FreeHGlobal(memory);
            }
        }

        private static void SelectName(IntPtr window)
        {
            IntPtr edit = GetDlgItem(window, EditId);
            SetFocus(edit);
            SendMessageW(edit, 0x00B1, IntPtr.Zero, new IntPtr(-1)); // EM_SETSEL: select all.
        }

        private static byte[] BuildTemplate()
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode))
            {
                // WS_POPUP | WS_CAPTION | WS_SYSMENU | DS_MODALFRAME | DS_SETFONT | DS_CENTER.
                writer.Write(0x80C808C0u);
                writer.Write(0u);
                writer.Write((ushort)5);
                writer.Write((short)0); writer.Write((short)0);
                writer.Write((short)350); writer.Write((short)106);
                writer.Write((ushort)0); // No menu.
                writer.Write((ushort)0); // Default dialog class.
                WriteString(writer, Title);
                writer.Write((ushort)10);
                WriteString(writer, "Segoe UI");

                AddControl(writer, 0x50000080u, 12, 10, 326, 14, 201, 0x0082, "Введите имя файла");
                // WS_TABSTOP | ES_AUTOHSCROLL | ES_NOHIDESEL, visible bordered EDIT.
                AddControl(writer, 0x50810180u, 12, 29, 326, 18, EditId, 0x0081, "");
                AddControl(writer, 0x50000080u, 12, 53, 326, 24, HintId, 0x0082,
                    "Расширение .pdf добавится автоматически. Папка — рядом с проектом.");
                AddControl(writer, 0x50010001u, 170, 83, 80, 16, 1, 0x0080, "Сохранить");
                AddControl(writer, 0x50010000u, 258, 83, 80, 16, 2, 0x0080, "Отмена");
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void AddControl(BinaryWriter writer, uint style,
            short x, short y, short width, short height, ushort id, ushort classId, string text)
        {
            while ((writer.BaseStream.Position & 3) != 0) writer.Write((byte)0);
            writer.Write(style); writer.Write(0u);
            writer.Write(x); writer.Write(y); writer.Write(width); writer.Write(height);
            writer.Write(id);
            writer.Write((ushort)0xFFFF); writer.Write(classId);
            WriteString(writer, text);
            writer.Write((ushort)0);
        }

        private static void WriteString(BinaryWriter writer, string text)
        {
            writer.Write(Encoding.Unicode.GetBytes(text ?? String.Empty));
            writer.Write((ushort)0);
        }
    }

    private static void ExportSheet(Part part, DrawingSheet sheet, string outputPdf)
    {
        PrintPDFBuilder pdf = null;
        try
        {
            pdf = part.PlotManager.CreatePrintPdfbuilder();
            pdf.Action = PrintPDFBuilder.ActionOption.Native;
            pdf.Append = false;
            pdf.Filename = outputPdf;

            pdf.Colors = PrintPDFBuilder.Color.AsDisplayed;
            pdf.Size = PrintPDFBuilder.SizeOption.ScaleFactor;
            pdf.Scale = 1.0;
            pdf.Units = PrintPDFBuilder.UnitsOption.Metric;
            pdf.OutputText = PrintPDFBuilder.OutputTextOption.Text;
            pdf.AddWatermark = false;
            pdf.Watermark = String.Empty;
            pdf.RasterImages = true;
            pdf.ShadedGeometry = false;
            pdf.CustomSymbolsInForeground = false;
            pdf.ImageResolution = PrintPDFBuilder.ImageResolutionOption.High;

            // Стандартные физические толщины NX, без Single Width
            // и без пользовательской таблицы WidthDefinition.
            pdf.Widths = PrintPDFBuilder.Width.StandardWidths;

            // Явный лист задаёт границы PDF по размеру листа NX.
            // При ScaleFactor = 1.0 NX выводит целый лист в масштабе 1:1,
            // с его ориентацией и предусмотренными чертежом полями.
            pdf.SourceBuilder.SetSheets(new NXObject[] { sheet });

            // Предыдущий PDF храним только в оперативной памяти.
            // Копии на диске, файлы в TEMP и диагностические отчёты не создаются.
            byte[] previousPdf = File.Exists(outputPdf) ? File.ReadAllBytes(outputPdf) : null;
            try
            {
                try
                {
                    pdf.Commit();
                }
                finally
                {
                    pdf.Destroy();
                    pdf = null;
                }
                VerifyPdf(outputPdf);
            }
            catch (Exception writeError)
            {
                try
                {
                    if (previousPdf != null)
                    {
                        // При блокировке PDF мог остаться нетронутым.
                        if (!HasSameContents(outputPdf, previousPdf))
                            File.WriteAllBytes(outputPdf, previousPdf);
                    }
                    else if (File.Exists(outputPdf))
                        File.Delete(outputPdf);
                }
                catch (Exception restoreError)
                {
                    throw new IOException(
                        "Ошибка записи PDF:\n" + outputPdf + "\n" + writeError.Message +
                        "\n\nНе удалось восстановить прежний файл или удалить неполный PDF:\n" +
                        restoreError.Message, writeError);
                }
                throw new IOException(
                    "Не удалось записать PDF:\n" + outputPdf +
                    "\n\nЗакройте PDF в других программах и проверьте права на запись." +
                    (previousPdf == null ? String.Empty : "\nПрежний PDF сохранён.") +
                    "\n\nПричина: " + writeError.Message, writeError);
            }
        }
        finally
        {
            if (pdf != null) pdf.Destroy();
        }
    }

    private static void ApplyExportAppearance(Part part, DrawingSheet sheet,
        WidthProfile widths, WidthProfile previous, bool applyStandards)
    {
        UFSession uf = UFSession.GetUFSession();
        DraftingView[] views = sheet.GetDraftingViews();
        foreach (DraftingView view in views)
        {
            if (view.IsDecoration) continue;
            bool contour = applyStandards || widths.Changed(LineGroup.Contour, previous);
            bool hidden = applyStandards || widths.Changed(LineGroup.Hidden, previous);
            if (!contour && !hidden) continue;
            if (contour) view.Style.VisibleLines.VisibleWidth = widths[LineGroup.Contour].View;
            if (hidden) view.Style.HiddenLines.HiddenlineWidth = widths[LineGroup.Hidden].View;
            view.Commit();
            // Commit applies the style. Update would resection and recreate hatch objects.
        }

        HashSet<Tag> sheetViews = new HashSet<Tag>();
        sheetViews.Add(sheet.View.Tag);
        foreach (DraftingView view in views) sheetViews.Add(view.Tag);

        if (applyStandards || widths.Changed(LineGroup.SectionMarks, previous))
            StyleSectionLines(part, uf, sheetViews, widths, applyStandards);
        HashSet<Tag> labels = StyleViewLabels(part, uf, sheet.GetDraftingViews(), applyStandards);

        // Обновления видов могут пересоздавать аннотации: собираем теги после них.
        HashSet<Tag> sheetObjects = new HashSet<Tag>();
        foreach (Tag view in sheetViews) CollectViewObjects(uf, view, sheetObjects);
        List<Tag> candidates = new List<Tag>();
        Tag tag = Tag.Null;
        while ((tag = uf.Obj.CycleAll(part.Tag, tag)) != Tag.Null)
            if (IsOnSheet(uf, tag, sheetViews, sheetObjects)) candidates.Add(tag);

        foreach (Tag item in candidates)
        {
            if (labels.Contains(item)) continue;
            int type, subtype;
            uf.Obj.AskTypeAndSubtype(item, out type, out subtype);
            if (type == UFConstants.UF_drafting_entity_type &&
                subtype == UFConstants.UF_draft_crosshatch_subtype)
            {
                // У автоматической штриховки может не быть managed-обёртки.
                // Поэтому редактируем её native tag до NXObjectManager.Get.
                // Угол, шаг, материал и границы сохраняются.
                if (applyStandards || widths.Changed(LineGroup.Hatch, previous))
                    SetNativeDraftingWidth(uf, item, 68, widths[LineGroup.Hatch].Native);
                continue;
            }
            DisplayableObject obj;
            try { obj = NXOpen.Utilities.NXObjectManager.Get(item) as DisplayableObject; }
            catch (NXException) { continue; } // UF containers have no managed wrapper.
            if (obj == null || obj.IsBlanked || obj.OwningPart == null || obj.OwningPart.Tag != part.Tag)
                continue;

            try
            {
                if (obj is Centerline)
                {
                    // У осевой своя толщина внутри drafting entity.
                    // Одна только VisibleWidth вида эту толщину не меняет.
                    if (applyStandards || widths.Changed(LineGroup.Axes, previous))
                        SetNativeDraftingWidth(uf, item, 66, widths[LineGroup.Axes].Native);
                    continue;
                }

                Annotation annotation = obj as Annotation;
                if (annotation != null && !(annotation is TableSection))
                {
                    if (applyStandards || widths.Changed(LineGroup.Dimensions, previous) ||
                        widths.Changed(LineGroup.Axes, previous) ||
                        widths.Changed(LineGroup.GeneralRoughness, previous))
                        StyleAnnotation(part, annotation, widths, applyStandards);
                    continue;
                }

                Curve curve = obj as Curve;
                if (curve != null)
                {
                    // Только явно зависимые от листа/вида кривые, не рёбра модели.
                    int dependent; string owner;
                    uf.View.AskViewDependentStatus(item, out dependent, out owner);
                    if (dependent != 1) continue;
                    if (curve.LineFont == DisplayableObject.ObjectFont.Centerline)
                    {
                        if (applyStandards || widths.Changed(LineGroup.Axes, previous))
                        {
                            curve.LineWidth = widths[LineGroup.Axes].Object;
                            curve.RedisplayObject();
                        }
                    }
                    else if (IsEskdFrameLine(curve.Name) &&
                        (applyStandards || widths.Changed(LineGroup.Frame, previous)))
                    {
                        curve.LineWidth = widths[LineGroup.Frame].Object;
                        curve.RedisplayObject();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Не удалось оформить объект " + obj.GetType().Name + " (" + item.ToString() +
                    ") для PDF. Экспорт остановлен.\n" + ex.Message, ex);
            }
        }
        // Не выполняем новый пересчёт видов после индивидуальных толщин:
        // он способен пересоздать штриховку и вернуть старые предпочтения.
        uf.Disp.RegenerateDisplay();
    }

    private static bool IsEskdFrameLine(string name)
    {
        // Имена рамки принадлежат нашему ESKD-скрипту: ESKD_<владелец>_L...
        if (String.IsNullOrEmpty(name) || !name.StartsWith("ESKD_", StringComparison.Ordinal)) return false;
        int marker = name.LastIndexOf("_L", StringComparison.Ordinal);
        if (marker < 5 || marker + 2 >= name.Length) return false;
        for (int i = marker + 2; i < name.Length; i++)
            if (name[i] < '0' || name[i] > '9') return false;
        return true;
    }

    private static void SetNativeDraftingWidth(UFSession uf, Tag tag, int index, int width)
    {
        int[] integers = new int[100];
        double[] reals = new double[70];
        string radius, diameter;
        uf.Drf.AskObjectPreferences(tag, integers, reals, out radius, out diameter);
        if (integers[index] == width) return;
        integers[index] = width;
        uf.Drf.SetObjectPreferences(tag, integers, reals, radius, diameter);
        uf.Drf.AskObjectPreferences(tag, integers, reals, out radius, out diameter);
        if (integers[index] != width)
            throw new InvalidOperationException("NX не подтвердил толщину осевой/штриховки.");
    }

    private static void StyleAnnotation(Part part, Annotation annotation, WidthProfile widths, bool applyStandards)
    {
        bool dimension = annotation is Dimension;
        bool enlargedRoughness = annotation.HasUserAttribute(
            GeneralRoughnessAttribute, NXObject.AttributeType.String, -1) &&
            annotation.GetStringUserAttribute(GeneralRoughnessAttribute, -1) == "Ra";
        EditSettingsBuilder builder = part.SettingsManager.CreateAnnotationEditSettingsBuilder(
            new DisplayableObject[] { annotation });
        try
        {
            StyleBuilder style = builder.AnnotationStyle;
            LineWidth annotationWidth = widths[LineGroup.Dimensions].Annotation;
            SetThinLeaders(style.LineArrowStyle, annotationWidth, applyStandards);
            SymbolStyleBuilder symbols = style.SymbolStyle;
            symbols.CenterlineSymbolWidth = widths[LineGroup.Axes].Annotation;
            symbols.GdtSymbolWidth = annotationWidth;
            symbols.IdSymbolWidth = annotationWidth;
            symbols.IntersectionSymbolWidth = annotationWidth;
            symbols.TargetSymbolWidth = annotationWidth;
            symbols.SurfaceFinishWidth = enlargedRoughness ? widths[LineGroup.GeneralRoughness].Annotation : annotationWidth;
            symbols.EdgeConditionWidth = annotationWidth;
            if (applyStandards)
            {
                symbols.GdtSymbolFont = DisplayableObject.ObjectFont.Solid;
                symbols.IdSymbolFont = DisplayableObject.ObjectFont.Solid;
                symbols.SurfaceFinishFont = DisplayableObject.ObjectFont.Solid;
                symbols.DraftingSurfaceFinishStandard = SurfaceFinishStandard.Eskd;
            }
            // Пользовательские символы и сварные швы могут содержать несколько
            // классов линий; их содержимое и стандарт здесь не переопределяются.
            if (dimension && applyStandards) StyleDimension(style);
            builder.Commit();
        }
        finally { builder.Destroy(); }
    }

    private static void SetThinLeaders(LineArrowStyleBuilder lines, LineWidth width, bool applyStandards)
    {
        lines.FirstArrowLineWidth = width;
        lines.SecondArrowLineWidth = width;
        lines.FirstExtensionLineWidth = width;
        lines.SecondExtensionLineWidth = width;
        lines.FirstArrowheadWidth = width;
        lines.SecondArrowheadWidth = width;
        if (!applyStandards) return;
        lines.FirstArrowLineFont = DisplayableObject.ObjectFont.Solid;
        lines.SecondArrowLineFont = DisplayableObject.ObjectFont.Solid;
        lines.FirstExtensionLineFont = DisplayableObject.ObjectFont.Solid;
        lines.SecondExtensionLineFont = DisplayableObject.ObjectFont.Solid;
        lines.FirstArrowheadFont = DisplayableObject.ObjectFont.Solid;
        lines.SecondArrowheadFont = DisplayableObject.ObjectFont.Solid;
    }

    private static ArrowheadType NormalizeDimensionArrow(ArrowheadType original)
    {
        // Не заменяем допустимые точки, засечки и начало координат на стрелки.
        if (original == ArrowheadType.OpenArrow || original == ArrowheadType.ClosedArrow ||
            original == ArrowheadType.ClosedSolidArrow) return ArrowheadType.FilledArrow;
        return original;
    }

    private static void StyleDimension(StyleBuilder style)
    {
        LineArrowStyleBuilder lines = style.LineArrowStyle;
        lines.ArrowheadLength = 3.0;
        lines.ArrowheadIncludedAngle = 20.0;
        lines.FirstArrowType = NormalizeDimensionArrow(lines.FirstArrowType);
        lines.SecondArrowType = NormalizeDimensionArrow(lines.SecondArrowType);
        lines.LinePastArrowDistance = 2.0;
        lines.LinePastArrowDistance2 = 2.0;
        style.UnitsStyle.DecimalPointCharacter = DecimalPointCharacter.Comma;
        style.UnitsStyle.DisplayLeadingDimensionZeros = true;
        style.UnitsStyle.DisplayTrailingZeros = false;
        // Точность значения/допуска и знак диаметра/радиуса сохраняются:
        // они заданы конструктором и ассоциативной размерной аннотацией.
        LetteringStyleBuilder text = style.LetteringStyle;
        text.DimensionTextSize = 3.5;
        text.AppendedTextSize = 3.5;
        text.ToleranceTextSize = 3.5;
        text.TwoLineToleranceTextSize = 2.5;
        text.DimLineSpaceFactor = 1.0 / 3.5;
        // Шрифт, наклон и его контуры уже настроены ESKD-скриптом.
        // Не накладываем синтетический наклон на наклонный шрифт второй раз.
    }

    private static bool IsOnSheet(UFSession uf, Tag tag, HashSet<Tag> views, HashSet<Tag> objects)
    {
        try
        {
            int dependent; string name;
            uf.View.AskViewDependentStatus(tag, out dependent, out name);
            if (dependent == 1 && !String.IsNullOrEmpty(name))
            {
                Tag owner;
                uf.View.AskTagOfViewName(name, out owner);
                if (owner != Tag.Null) return views.Contains(owner);
            }
        }
        catch (NXException) { } // Some internal UF tags have no view dependence.
        return objects.Contains(tag);
    }

    private static HashSet<Tag> StyleViewLabels(Part part, UFSession uf, DraftingView[] views, bool applyStandards)
    {
        HashSet<Tag> labels = new HashSet<Tag>();
        foreach (DraftingView view in views)
        {
            if (view.IsDecoration) continue;
            Tag viewTag = view.Tag;
            Tag label;
            uf.Draw.AskViewLabel(viewTag, out label);
            if (label == Tag.Null) continue;
            labels.Add(label);
            if (!applyStandards || !(view is SectionView)) continue;
            DisplayableObject obj = NXOpen.Utilities.NXObjectManager.Get(label) as DisplayableObject;
            if (obj == null || obj.IsBlanked) continue;
            EditViewLabelSettingsBuilder builder = part.SettingsManager.CreateDrawingEditViewLabelSettingsBuilder(
                new DisplayableObject[] { obj });
            try
            {
                builder.AnnotationStyle.LetteringStyle.GeneralTextSize = 3.5;
                ViewSectionLabelBuilder style = builder.ViewSectionLabel;
                style.CustomizedViewLabel = false;
                style.ViewLabelOption = ViewLabelTypes.Letter;
                style.LetterFormat = LetterFormatTypes.AA;
                style.LabelPrefix = String.Empty;
                // ГОСТ Р 2.316-2023, 5.10: буквы вдвое выше размерных чисел.
                style.LabelCharacterHeightFactor = 7.0 / 3.5;
                style.PrefixCharacterHeightFactor = 7.0 / 3.5;
                style.ScaleCharacterHeightFactor = 7.0 / 3.5;
                style.ScalePrefix = String.Empty;
                style.IncludeParentheses = true;
                // Буква, положение подписи и показ масштаба сохраняются.
                builder.Commit();
            }
            finally { builder.Destroy(); }
            uf.Draw.AskViewLabel(viewTag, out label);
            if (label != Tag.Null) labels.Add(label);
        }
        return labels;
    }

    private static void StyleSectionLines(Part part, UFSession uf, HashSet<Tag> views,
        WidthProfile widths, bool applyStandards)
    {
        foreach (SectionLine line in part.Drafting.SectionLines.ToArray())
        {
            Tag parent;
            ReadSectionSegments(uf, line.Tag, out parent);
            if (!views.Contains(parent)) continue;
            EditSectionLineSettingsBuilder builder = part.SettingsManager.CreateDrawingEditSectionLineSettingsBuilder(
                new SectionLine[] { line });
            try
            {
                ViewSectionLineBuilder style = builder.ViewSectionLine;
                if (!applyStandards)
                {
                    style.LineColorFontWidth.LineWidth = widths[LineGroup.SectionMarks].Object;
                    style.BendAndEndSegmentWidthFactor = 1.0;
                    builder.Commit();
                    continue;
                }
                style.TypeStandard = ViewSectionLineBuilder.DisplayType.ThickEndsArrowstowardsLine;
                style.Style = ViewSectionLineBuilder.StyleType.Filled;
                style.ArrowheadAngle = 20.0;
                style.ArrowheadLength = 5.0;
                style.ArrowLength = 10.0;
                style.Overhang = 2.5;
                style.UseLineLength = true;
                style.LineLength = 10.0;
                style.LineColorFontWidth.LineWidth = widths[LineGroup.SectionMarks].Object;
                style.BendAndEndSegmentWidthFactor = 1.0;
                // Не меняем UseOffset, Gap, геометрию секущих и направление взгляда.
                builder.Commit();
            }
            finally { builder.Destroy(); }
        }
    }

    private static Tag[] ReadSectionSegments(UFSession uf, Tag lineTag, out Tag parent)
    {
        // Read only: these routines do not move the section plane or its segments.
        UFDraw.SxlineType type;
        uf.Draw.AskSxlineType(lineTag, out type);
        double[] step = new double[3], arrow = new double[3];
        int viewCount, segmentCount;
        Tag[] views, segments;
        UFDraw.SxlineStatus status;
        switch (type)
        {
            case UFDraw.SxlineType.SimpleSxline:
                uf.Draw.AskSimpleSxline(lineTag, step, arrow, out parent, out viewCount, out views, out segmentCount, out segments, out status); break;
            case UFDraw.SxlineType.SteppedSxline:
                uf.Draw.AskSteppedSxline(lineTag, step, arrow, out parent, out viewCount, out views, out segmentCount, out segments, out status); break;
            case UFDraw.SxlineType.HalfSxline:
                uf.Draw.AskHalfSxline(lineTag, step, arrow, out parent, out viewCount, out views, out segmentCount, out segments, out status); break;
            case UFDraw.SxlineType.FoldedSxline:
                uf.Draw.AskFoldedSxline(lineTag, step, arrow, out parent, out viewCount, out views, out segmentCount, out segments, out status); break;
            case UFDraw.SxlineType.UnfoldedSxline:
                uf.Draw.AskUnfoldedSxline(lineTag, step, arrow, out parent, out viewCount, out views, out segmentCount, out segments, out status); break;
            case UFDraw.SxlineType.RevolvedSxline:
                UFDrf.Object rotation;
                int firstLegCount;
                UFDraw.SxlineLeg leg;
                uf.Draw.AskRevolvedSxline(lineTag, step, arrow, out parent, out rotation, out viewCount, out views,
                    out segmentCount, out firstLegCount, out leg, out segments, out status); break;
            default:
                throw new InvalidOperationException("Не поддержан тип линии разреза: " + type.ToString());
        }
        return segments ?? new Tag[0];
    }

    private static void CollectViewObjects(UFSession uf, Tag view, HashSet<Tag> objects)
    {
        int visibleCount, clippedCount;
        Tag[] visible, clipped;
        uf.View.AskVisibleObjects(view, out visibleCount, out visible, out clippedCount, out clipped);
        if (visible != null) foreach (Tag tag in visible) objects.Add(tag);
        if (clipped != null) foreach (Tag tag in clipped) objects.Add(tag);

        Tag item = Tag.Null;
        do
        {
            uf.View.CycleObjects(view, UFView.CycleObjectsEnum.DependentObjects, ref item);
            if (item != Tag.Null) objects.Add(item);
        } while (item != Tag.Null);
    }

    private static bool HasSameContents(string filename, byte[] expected)
    {
        if (!File.Exists(filename)) return false;
        using (FileStream stream = File.OpenRead(filename))
        {
            if (stream.Length != expected.LongLength) return false;
            byte[] buffer = new byte[8192];
            int offset = 0;
            int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (count > expected.Length - offset) return false;
                for (int i = 0; i < count; i++)
                    if (buffer[i] != expected[offset + i]) return false;
                offset += count;
            }
            return offset == expected.Length;
        }
    }

    private static void VerifyPdf(string filename)
    {
        if (!File.Exists(filename) || new FileInfo(filename).Length < 5)
            throw new IOException("NX не создал корректный PDF.");

        using (FileStream stream = File.OpenRead(filename))
        {
            byte[] header = new byte[5];
            if (stream.Read(header, 0, header.Length) != header.Length ||
                header[0] != '%' || header[1] != 'P' || header[2] != 'D' ||
                header[3] != 'F' || header[4] != '-')
                throw new IOException(
                    "Созданный файл не имеет заголовка PDF.");
        }
    }

    public static int GetUnloadOption(string dummy)
    {
        return (int)Session.LibraryUnloadOption.Immediately;
    }
}
