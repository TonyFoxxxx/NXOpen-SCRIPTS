// NX_ESKD_Format_GOST_A.cs — Оформление чертежа ЕСКД
// SCRIPT_VERSION: V1.36
// Upper duplicate designation stays horizontal on every supported sheet.
// Title cells wrap first, then reduce text size; native fitting prevents overflow hashes.
// Public edition: external authorized font; font bytes are not distributed.
// Working filename: NX_ESKD_Format_GOST_A.cs (constant across updates).
// Designation and name start from the current part filename on every launch.
// Saved sheet attributes and INI LastUsed values cannot override these two fields.
// A descriptive filename without a recognizable code fills the complete name.
// The user can still edit both fields in the existing settings menu.
// Siemens NX / Designcenter 2606. CURRENT millimeter drawing sheet, A4 or larger.
// v1.32: unified editable menu for all INI fields, technical requirements and roughness.
// Baseline INI reload is explicit/read-only. Last successful values use [LastUsed]
// in the same INI; write only after the complete NX formatting operation succeeds.
// Cancel makes no changes. No new state/log/report/temp files.
// TT text starts with the agreed three requirements on every launch; user edits
// apply to this run only. Orientation starts from the actual current sheet.
// v1.31: support A4 from either initial orientation; preserve the current selection.
// A4 format label and 180-degree duplicate designation; no title-block scaling.
// Landscape A4 is an optional custom layout; its upper auxiliary panel moves
// to the top strip to avoid overlap. The unified menu identifies portrait as GOST A4.
// Existing A3-A0 layouts, view placement and scale behavior are retained.
// v1.30: restore the approved v1.25 title-block grid and native table line settings.
// No separate version cell; the stored version attribute is retained unchanged.
// Keep the unified dialog, now with six visible fields, and the nested material picker.
// v1.29: one native settings dialog for orientation and drawing fields.
// The existing material picker opens as an owned child; Cancel returns to settings.
// Required fields show inline errors. Apply starts all mutations; Cancel writes nothing.
// INI preview is read-only; the same authorized INI is created/migrated after Apply.
// v1.28: set First Angle Projection on sheets without drafting views.
// Existing views retain their projection; warn once per sheet and continue.
// A rejected projection edit is rolled back locally, without undoing formatting.
// v1.27: fix "Fit method is invalid" for both sheet orientations.
// Compact active table fit methods and recalculate their count; None is padding
// only. Explicit measured sizes precede native Wrap/AutoSizeText fitting.
// v1.26: GOST type-A character spacing for existing and future annotations;
// separate M-prefix gap=0.5 mm at h=3.5; live NX spacing measurements.
// GOST 2.304-81: h=3.5, d=h/14, a=2d; type-A italic outlines are preserved.
// GOST 2.303-68: explicit contour/thin line widths 0.50/0.25 or 0.70/0.35 mm.
// GOST R 2.316-2023: view/section lettering 7 mm for dimension lettering 3.5 mm.
// Main title block 185 x 55, with the earlier grid requested by the user; format designation,
// 20/5/5/5 margins; the chosen column 26 layout is horizontal (180-degree text).
// User chooses horizontal/vertical sheet before any file/model changes.
// Sheet dimensions swap; existing view scale and placement are retained.
// New technical requirements replace the old block; existing INI migrates once.
// Table text prefers discrete GOST sizes and measured wrapping, with native fitting as fallback.
// GOST 2.307-2011: 3-mm/20-degree dimension arrows, 2-mm extension overruns,
// 7-mm baseline/chain intervals. Actual dimension placement still needs review:
// contour-to-first dimension >=10 mm; parallel dimension lines >=7 mm.
// GOST 2.305-2008: section arrows 5+5 mm, 20 degrees, 2.5-mm stroke overhang.
// Letter clearance 2 mm is a chosen layout value, not a numerical GOST mandate.
// GOST 2.306-68: 2-mm section hatch spacing; preserve existing hatch directions.
// GOST 2.309-73: native general Ra 6.3 symbol, 7.5 mm from the inner frame.
// Preserve the confirmed v1.18 section geometry logic: Background=true,
// Medium creation quality, UseOffset=false, one guarded section-view update.
// Apply font/annotation defaults to the current part and existing annotations
// on all its drawing sheets; current-sheet frame/title are rebuilt only here.
// All Siemens template cleanup precedes new frame/title creation.
// No reports, logs or temporary files. Only the authorized INI/font installation.
// Dialogs use native Unicode Win32; no Forms or System.Drawing dependencies.
// Run: Tools (Инструменты) > Journal (Журнал) > Play (Воспроизвести).
// One visible Undo mark; no automatic part save. Hide, do not delete, old templates.
// Formatting automation cannot certify engineering contents, geometry or layout.
// Standards 2.303/2.304/2.307 dated 2026 start on 2027-10-01, not yet applicable
// at this revision's review date 2026-09-24.
// Primary API source: Siemens NXOpen/UF headers (NX 2406):
// https://github.com/ugopen/nxopen_lib/tree/main/NX2406/UGOPEN

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using NXOpen;
using NXOpen.Annotations;
using NXOpen.Drawings;
using NXOpen.UF;

public class NX_ESKD_Format_GOST_A
{
    private const string SCRIPT_VERSION = "V1.36";
    private static readonly string Title = "ЕСКД: GOST type A Italic — " + SCRIPT_VERSION +
        " — " + Encoding.UTF8.GetString(Convert.FromBase64String("YnkgQFRvbnlfRm94eHggKFRHKQ=="));
    private const string FontName = "NX_ESKD_GOST_A_Italic_v110";
    private const string FontFaceName = "NX_ESKD_GOST_A_Italic_v110";
    private const string OwnerAttribute = "NX_ESKD_FORMAT_OWNER";
    private const string SettingsFileName = "NX_ESKD_Settings.ini";
    private static string SettingsPath;
    private static Session S;
    private static UFSession U;
    private static Part P;
    private static DrawingSheet Sheet;
    private static int FontIndex;
    private static int ObjectNumber;
    private static string Prefix;
    private static readonly List<string> Warnings = new List<string>();
    private static readonly List<Tag> SheetViews = new List<Tag>();
    private static readonly List<Tag> Visible = new List<Tag>();
    private static readonly HashSet<Tag> CreatedFormatObjects = new HashSet<Tag>();
    private static readonly List<Tag> CreatedTables = new List<Tag>();
    private const string TechnicalRequirementsAttribute = "NX_ESKD_TECH_REQUIREMENTS";
    private const string GeneralRoughnessAttribute = "NX_ESKD_GENERAL_ROUGHNESS";
    private static DrawingExtras Extras;
    private static Tag TechnicalRequirementsSheet;

    public static void Main(string[] args)
    {
        S = Session.GetSession();
        U = UFSession.GetUFSession();
        Session.UndoMarkId mark = default(Session.UndoMarkId);
        bool started = false;
        bool fontInstalled = false;
        string step = "проверка открытого листа";
        Warnings.Clear(); SheetViews.Clear(); Visible.Clear(); ObjectNumber = 0; SettingsPath = "";
        HiddenTemplateObjects.Clear(); FailedTemplateObjects.Clear(); SiemensTemplateFound = false; Prefix = "";
        CreatedFormatObjects.Clear(); CreatedTables.Clear();
        CharacterSpacingFactor = 1.0; GeneralLineFactor = 1.0; LinePitchBaseRatio = 0.0; LinePitchSlopeRatio = 0.0; StyledSheets = 0; StyledTables = 0; StyledViewLabels = 0; StyledSectionLines = 0; StyledDimensions = 0; StyledSectionLabels = 0;
        SectionDefaultsVerified = false;
        SectionGeometryDefaultsVerified = false; UpdatedSectionViews = 0; PositionedSectionLetters = 0; SectionLetterPlacements.Clear(); NormalizedSymmetricTolerances = 0;
        StyledSectionHatches = 0; HatchDefaultsVerified = false;
        ProcessedSectionHatches.Clear(); SectionHatchTargets.Clear();
        Extras = null; TechnicalRequirementsSheet = Tag.Null;
        try
        {
            P = S.Parts.Work;
            if (P == null || S.Parts.Display == null || P.Tag != S.Parts.Display.Tag)
                throw new InvalidOperationException("Откройте чертеж как рабочую деталь.");
            if (S.ApplicationName != "UG_APP_DRAFTING")
                throw new InvalidOperationException("Перейдите в Drafting (Черчение) и откройте лист.");
            Sheet = P.DrawingSheets.CurrentDrawingSheet;
            if (Sheet == null) throw new InvalidOperationException("Откройте нужный лист чертежа.");
            if (P.PartUnits != BasePart.Units.Millimeters || Sheet.Units != DrawingSheet.Unit.Millimeters)
                throw new InvalidOperationException("Нужны единицы Millimeters (Миллиметры) у детали и листа.");
            // Check the sorted sides so the current orientation never affects support.
            if (!IsSupportedSheetSize(Sheet.Length, Sheet.Height))
                throw new InvalidOperationException("Нужен лист А4 (210 × 297 мм) или больше. " +
                    "Текущая ориентация может быть горизонтальной или вертикальной.");

            step = "получение объектов текущего листа";
            CollectSheetObjects();
            step = "чтение постоянных данных из INI";
            Fields fields = ReadFields();
            Fields iniSeed = Fields.From(fields.Values());
            SettingsPath = GetSettingsPath();
            IniSettings settings = IniSettings.Load(SettingsPath, iniSeed);
            settings.ApplyTo(fields, Sheet.HasUserAttribute("NX_ESKD_FIELD_2", NXObject.AttributeType.String, -1));
            Extras = settings.ReadDrawingExtras();
            fields = settings.ApplyLastUsed(fields, Extras);
            // TT text always starts from the approved three items, regardless of the last run.
            // The explicit INI button can load a different baseline for this run.
            Extras.Requirements = IniSettings.InitialRequirements();
            step = "ввод параметров чертежа";
            bool portrait;
            if (!DraftingSetupDialog.Show(U.Ui.GetDefaultParent(), fields, Extras, iniSeed, Sheet.Length, Sheet.Height,
                out fields, out Extras, out portrait)) return;
            double shortSide = Math.Min(Sheet.Length, Sheet.Height), longSide = Math.Max(Sheet.Length, Sheet.Height);
            double targetWidth = portrait ? shortSide : longSide;
            double targetHeight = portrait ? longSide : shortSide;

            // Use exactly the fields accepted in the unified dialog. No INI write
            // occurs until all drawing changes succeed; Cancel is entirely read-only.
            step = "подключение внешнего шрифта GOST type A Italic";
            string installedFontPath = ExternalFontInstaller.Install();
            fontInstalled = true;
            step = "настройка каталога шрифтов NX";
            string nxFontDirectory = ExternalFontInstaller.ConfigureNxFontDirectory(installedFontPath);

            mark = S.SetUndoMark(Session.MarkVisibility.Visible, Title);
            started = true;
            step = "подключение GOST type A Italic";
            FontIndex = ConnectDrawingFont(nxFontDirectory);

            TechnicalRequirementsSheet = FindTechnicalRequirementsSheet(fields);

            string owner = ReadAttribute(Sheet, OwnerAttribute, "");
            if (owner.Length == 0)
            {
                owner = Guid.NewGuid().ToString("N").Substring(0, 12);
                Sheet.SetUserAttribute(OwnerAttribute, -1, owner, Update.Option.Now);
            }
            Prefix = "ESKD_" + owner + "_";
            step = "обновление ранее созданного оформления";
            RemovePreviousFormat();
            step = "настройка рамки";
            DisableNativeBorder();
            step = "обновление списка объектов листа";
            CollectSheetObjects();
            step = "скрытие шаблона Siemens";
            HideSiemensTemplate();
            // Discover the old template at its original size first. The next
            // refresh/reapply pass also hides any objects regenerated by resizing.
            step = "изменение ориентации текущего листа";
            ApplySheetOrientation(targetWidth, targetHeight, mark);
            // Settle and clean the OLD template before creating anything new.
            step = "обновление листа перед созданием оформления";
            P.DrawingSheets.RefreshCurrentSheet();
            step = "завершение скрытия шаблона Siemens";
            ReapplyTemplateHiding();
            step = "проверка межбуквенного интервала шрифта";
            CalibrateCharacterSpacing();
            step = "проверка межстрочного интервала шрифта";
            CalibrateGeneralLineSpacing();
            step = "настройки новых аннотаций в текущей детали";
            ApplyProjectDraftingPreferences();
            step = "стрелки разрезов и отступы букв по ЕСКД";
            ApplyStylesToExistingSectionLines();
            step = "геометрия разрезов и оформление аннотаций на всех листах текущей детали";
            ApplyStylesToAllDrawingSheets();
            // Opening other sheets can regenerate the active sheet's template.
            // Complete this last cleanup before creating any ESKD objects.
            step = "завершение скрытия шаблона после обхода листов";
            P.DrawingSheets.RefreshCurrentSheet();
            ReapplyTemplateHiding();
            step = "создание рамки";
            BeginTableTextMeasurement();
            try
            {
                BuildFrame();
                step = "создание основной надписи";
                BuildTitleTable(fields);
                step = "создание служебных граф";
                BuildAuxiliaryTables(fields);
            }
            finally { EndTableTextMeasurement(); }
            step = "общая шероховатость по ГОСТ 2.309";
            BuildGeneralRoughness();
            step = "обновление листа";
            SaveFields(fields);
            P.DrawingSheets.RefreshCurrentSheet();
            // No template discovery or hiding is allowed after BuildFrame.
            step = "проверка видимости созданных таблиц ЕСКД";
            EnsureCreatedTablesVisible();
            step = "закрепление настроек новых размеров и обозначений";
            ApplyProjectDraftingPreferences();
            VerifyPartDefaults();
            step = "перерисовка листа";
            U.Disp.RegenerateDisplay();

            AuditFinalSectionLetters();
            AuditFinalSectionHatches();
            step = "проверка текста основной надписи";
            AuditCreatedTableText();

            // A persistence problem must not undo a successfully formatted drawing.
            // No LastUsed update is attempted on Cancel or an NX formatting failure.
            try { IniSettings.SaveLastUsed(SettingsPath, iniSeed, fields, Extras); }
            catch (Exception saved)
            {
                Warnings.Add("Чертёж оформлен, но последний ввод не удалось сохранить в INI:\n" +
                    SettingsPath + "\n" + saved.Message);
            }
        }
        catch (Exception ex)
        {

            string status = "Изменения чертежа не выполнялись.";
            if (started)
            {
                try
                {
                    S.UndoToMark(mark, null);
                    status = "Изменения чертежа этого запуска отменены.";
                    S.DeleteUndoMark(mark, null);
                }
                catch (Exception undo) { status = "Отмена не завершена: " + undo.Message; }
            }
            if (fontInstalled) status += "\nGOST type A Italic установлен для текущего пользователя Windows.";
            UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Error,
                "Этап: " + step + ".\n" + ex.Message + "\n\n" + status);
            return;
        }

        if (Warnings.Count > 0)
            UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Warning,
                "Оформление завершено с замечаниями:\n\n" + String.Join("\n\n", Warnings.ToArray()));

    }

    private static bool OnSheet(Annotation a)
    {
        if (a == null || a.OwningPart == null || a.OwningPart.Tag != P.Tag) return false;
        bool onCurrentSheet;
        if (TrySheetMembership(a.Tag, out onCurrentSheet)) return onCurrentSheet;

        // A table section and its UF tabular note have different tags. NX can
        // report visibility/view dependence on the parent instead of the section.
        TableSection section = a as TableSection;
        if (section != null)
        {
            Tag table;
            U.Tabnot.AskTabularNoteOfSection(section.Tag, out table);
            if (TrySheetMembership(table, out onCurrentSheet)) return onCurrentSheet;
        }

        // UF_DRAW_ask_view_of_note is for notes associated with drawing views.
        // Annotation.GetViews() is PMI-only and must not be used here (3655004).
        if (a is BaseNote)
        {
            try
            {
                Tag ownerView;
                U.Draw.AskViewOfNote(a.Tag, out ownerView);
                if (ownerView != Tag.Null) return SheetViews.Contains(ownerView);
            }
            catch (NXException) { } // Sheet notes need not have an associated member view.
        }
        return false; // Do not modify annotations whose sheet cannot be established.
    }

    private static void RemovePreviousFormat()
    {
        List<Tag> remove = new List<Tag>();
        Tag tag = Tag.Null;
        while ((tag = U.Obj.CycleAll(P.Tag, tag)) != Tag.Null)
        {
            string name;
            try { U.Obj.AskName(tag, out name); }
            catch (NXException) { continue; } // Some internal objects cannot have names.
            if (name != null && name.StartsWith(Prefix, StringComparison.Ordinal)) remove.Add(tag);
        }
        foreach (Tag old in remove) U.Obj.DeleteObject(old);
    }

    private static void Mark(Tag tag, string kind)
    {
        ObjectNumber++;
        U.Obj.SetName(tag, Prefix + kind + ObjectNumber.ToString(CultureInfo.InvariantCulture));
        CreatedFormatObjects.Add(tag);
    }

    private static readonly HashSet<Tag> SheetLayoutObjects = new HashSet<Tag>();
    private static readonly HashSet<Tag> HiddenTemplateObjects = new HashSet<Tag>();
    private static readonly HashSet<Tag> FailedTemplateObjects = new HashSet<Tag>();
    private static bool SiemensTemplateFound;
    private static readonly HashSet<string> SheetViewNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AllCurrentViewNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static string ViewKey(string name)
    {
        string value = (name ?? "").Trim();
        // uc6409 returns drafting view names with the suffix @0.
        if (value.EndsWith("@0", StringComparison.Ordinal)) value = value.Substring(0, value.Length - 2);
        return value;
    }

    private static void CollectSheetObjects()
    {
        // Membership is refreshed independently of the successfully hidden objects.
        SheetViews.Clear(); Visible.Clear(); SheetLayoutObjects.Clear();
        SheetViewNames.Clear(); AllCurrentViewNames.Clear();
        SheetViewNames.Add(ViewKey(Sheet.Name));
        SheetViewNames.Add(ViewKey(Sheet.View.Name));
        foreach (string name in SheetViewNames) AllCurrentViewNames.Add(name);
        SheetViews.Add(Sheet.View.Tag);
        foreach (DraftingView view in Sheet.GetDraftingViews())
        {
            SheetViews.Add(view.Tag);
            AllCurrentViewNames.Add(ViewKey(view.Name));
        }
        foreach (Tag view in SheetViews)
        {
            int nv, nc; Tag[] visible, clipped;
            U.View.AskVisibleObjects(view, out nv, out visible, out nc, out clipped);
            if (visible != null) foreach (Tag tag in visible) if (!Visible.Contains(tag)) Visible.Add(tag);
            if (clipped != null) foreach (Tag tag in clipped) if (!Visible.Contains(tag)) Visible.Add(tag);
            // Unlike the visible-object query this also returns blanked layout
            // objects, and does not depend on zoom or the current sheet crop.
            Tag item = Tag.Null;
            do
            {
                U.View.CycleObjects(view, UFView.CycleObjectsEnum.DependentObjects, ref item);
                if (item == Tag.Null) break;
                if (!Visible.Contains(item)) Visible.Add(item);
                if (view == Sheet.View.Tag) SheetLayoutObjects.Add(item);
            } while (item != Tag.Null);
        }
        ExpandLayoutContainers();
    }

    private static void ExpandLayoutContainers()
    {
        List<Tag> pending = new List<Tag>(SheetLayoutObjects);
        for (int i = 0; i < pending.Count; i++)
        {
            int type, subtype; U.Obj.AskTypeAndSubtype(pending[i], out type, out subtype);
            if (type != UFConstants.UF_group_type) continue;
            int count; Tag[] members; U.Group.AskGroupData(pending[i], out members, out count);
            foreach (Tag member in members)
                if (SheetLayoutObjects.Add(member)) pending.Add(member);
        }
        foreach (TitleBlock block in P.DraftingManager.TitleBlocks.ToArray())
        {
            if (!IsSheetLayoutObject(block.Tag)) continue;
            DefineTitleBlockBuilder builder = P.DraftingManager.TitleBlocks.CreateDefineTitleBlockBuilder(block);
            try
            {
                foreach (TableSection component in builder.Components.GetArray())
                {
                    Tag table; U.Tabnot.AskTabularNoteOfSection(component.Tag, out table);
                    SheetLayoutObjects.Add(component.Tag); SheetLayoutObjects.Add(table);
                }
            }
            finally { builder.Destroy(); }
        }
    }

    private static bool TrySheetMembership(Tag tag, out bool onCurrentSheet)
    {
        onCurrentSheet = false;
        if (tag == Tag.Null) return false;
        if (SheetLayoutObjects.Contains(tag)) { onCurrentSheet = true; return true; }
        try
        {
            int dependent; string name;
            U.View.AskViewDependentStatus(tag, out dependent, out name);
            if (dependent == 1 && !String.IsNullOrEmpty(name))
            {
                if (AllCurrentViewNames.Contains(ViewKey(name))) { onCurrentSheet = true; return true; }
                Tag owner; U.View.AskTagOfViewName(name, out owner);
                if (owner != Tag.Null) { onCurrentSheet = SheetViews.Contains(owner); return true; }
            }
        }
        catch (NXException) { } // Internal table tags can lack this property.
        if (Visible.Contains(tag)) { onCurrentSheet = true; return true; }
        return false;
    }

    private static bool IsSheetLayoutObject(Tag tag)
    {
        if (SheetLayoutObjects.Contains(tag)) return true;
        try
        {
            int dependent; string name;
            U.View.AskViewDependentStatus(tag, out dependent, out name);
            return dependent == 1 && SheetViewNames.Contains(ViewKey(name));
        }
        catch (NXException) { return false; }
    }

    private static string PlainTemplateText(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(text ?? "", "<[^>]*>", " "), "\\s+", " ").ToUpperInvariant();
    }

    private static bool SiemensText(string text)
    {
        string s = PlainTemplateText(text);
        if (s.Contains("SIEMENS") || s.Contains("THIS DRAWING HAS BEEN PRODUCED") ||
            s.Contains("EXAMPLE TEMPLATE") || s.Contains("ALL DIMENSIONS IN") || s.Contains("SH_PROJECTION")) return true;
        int matches = 0;
        foreach (string key in new string[] { "FIRST ISSUED", "DRAWN BY", "CHECKED BY", "APPROVED BY", "SHEET REV" })
            if (s.Contains(key)) matches++;
        return matches >= 2;
    }

    private sealed class ExistingTable
    {
        public Tag Tag;
        public readonly List<Tag> Sections = new List<Tag>();
        public string Text;
        public bool Current;
    }

    private static string TableText(Tag table)
    {
        int nr, nc; U.Tabnot.AskNmRows(table, out nr); U.Tabnot.AskNmColumns(table, out nc);
        StringBuilder text = new StringBuilder();
        for (int r = 0; r < nr; r++)
        {
            Tag row; U.Tabnot.AskNthRow(table, r, out row);
            for (int c = 0; c < nc; c++)
            {
                Tag column, cell; string value;
                U.Tabnot.AskNthColumn(table, c, out column);
                U.Tabnot.AskCellAtRowCol(row, column, out cell);
                U.Tabnot.AskCellText(cell, out value); text.Append(value).Append(" ");
                U.Tabnot.AskEvaluatedCellText(cell, out value); text.Append(value).Append(" ");
            }
        }
        return text.ToString();
    }

    private static List<ExistingTable> ExistingTables()
    {
        List<ExistingTable> result = new List<ExistingTable>();
        Tag tag = Tag.Null;
        do
        {
            U.Obj.CycleObjsInPart(P.Tag, UFConstants.UF_tabular_note_type, ref tag);
            if (tag == Tag.Null) break;
            int type, subtype; U.Obj.AskTypeAndSubtype(tag, out type, out subtype);
            if (subtype != UFConstants.UF_tabular_note_subtype || IsOwnFormatTag(tag)) continue;
            ExistingTable table = new ExistingTable(); table.Tag = tag;
            bool current;
            if (TrySheetMembership(tag, out current) && current) table.Current = true;
            int count; U.Tabnot.AskNmSections(tag, out count);
            for (int i = 0; i < count; i++)
            {
                Tag section; U.Tabnot.AskNthSection(tag, i, out section); table.Sections.Add(section);
                if (TrySheetMembership(section, out current) && current) table.Current = true;
            }
            if (table.Current) { table.Text = TableText(tag); result.Add(table); }
        } while (tag != Tag.Null);
        return result;
    }

    private static bool InTitleArea(Point3d at)
    { return at.X >= Sheet.Length - 190.0 && at.X <= Sheet.Length + 0.5 && at.Y >= -0.5 && at.Y <= 65.0; }

    private static bool TryOrigin(Tag tag, out Point3d origin)
    {
        origin = new Point3d();
        try
        {
            Annotation a = NXOpen.Utilities.NXObjectManager.Get(tag) as Annotation;
            if (a != null) { origin = a.AnnotationOrigin; return true; }
            double[] pt = new double[3]; U.Drf.AskOrigin(tag, pt);
            origin = new Point3d(pt[0], pt[1], pt[2]); return true;
        }
        catch (NXException) { }
        try
        {
            double[] box = new double[6]; U.Modl.AskBoundingBox(tag, box);
            origin = new Point3d(box[0], box[1], box[2]); return true;
        }
        catch (NXException) { return false; }
    }

    private static bool TableInTitleArea(ExistingTable table)
    {
        foreach (Tag section in table.Sections)
        { Point3d at; if (TryOrigin(section, out at) && InTitleArea(at)) return true; }
        return false;
    }

    private static bool IsLiveTag(Tag tag)
    {
        if (tag == Tag.Null) return false;
        // UF_OBJ_cycle_all also returns temporary/condemned internal objects.
        // Siemens documents that NX may delete or reuse these at any time.
        try { return U.Obj.AskStatus(tag) == UFConstants.UF_OBJ_ALIVE; }
        catch (NXException) { return false; }
    }

    private static bool HasOwnFormatName(Tag tag)
    {
        if (tag == Tag.Null || String.IsNullOrEmpty(Prefix)) return false;
        try
        {
            string name; U.Obj.AskName(tag, out name);
            return name != null && name.StartsWith(Prefix, StringComparison.Ordinal);
        }
        catch (NXException) { return false; }
    }

    private static bool IsOwnFormatTag(Tag tag)
    {
        if (CreatedFormatObjects.Contains(tag) || HasOwnFormatName(tag)) return true;
        // Technical requirements can live on the first sheet while the format
        // is rebuilt on another sheet. Never classify them as Siemens content.
        if (IsLiveTag(tag))
        {
            try
            {
                NXObject owned = NXOpen.Utilities.NXObjectManager.Get(tag) as NXObject;
                if (owned != null && owned.HasUserAttribute(TechnicalRequirementsAttribute, NXObject.AttributeType.String, -1))
                    return true;
            }
            catch (NXException) { } // Some native layout objects have no managed wrapper.
        }
        // The visible TableSection need not inherit the name of its UF table.
        try
        {
            TableSection section = NXOpen.Utilities.NXObjectManager.Get(tag) as TableSection;
            if (section == null) return false;
            Tag parent; U.Tabnot.AskTabularNoteOfSection(tag, out parent);
            return HasOwnFormatName(parent);
        }
        catch (NXException) { return false; }
    }

    private static void HideTemplateTag(Tag tag)
    {
        RequireTemplateCleanupPhase();
        if (!IsLiveTag(tag) || HiddenTemplateObjects.Contains(tag) || IsOwnFormatTag(tag)) return;
        DisplayableObject display = null;
        try { display = NXOpen.Utilities.NXObjectManager.Get(tag) as DisplayableObject; }
        catch (NXException) { } // Some UF parent tables have no managed wrapper.
        if (display != null)
        {
            try
            {
                display.Blank();
                HiddenTemplateObjects.Add(tag);
            }
            catch (NXException ex)
            {
                // A refresh can invalidate an object between lookup and use.
                // A failure for an existing object remains visible to the user.
                if (IsLiveTag(tag) && FailedTemplateObjects.Add(tag))
                    Warnings.Add("Не удалось скрыть один из элементов шаблона Siemens: " +
                        ex.Message + ". Проверьте штамп и знак проекции на листе.");

            }
            return;
        }
        // Non-displayable parent containers are optional. Their visible table
        // sections and TitleBlock are processed separately by HideTable.
        try
        {
            U.Obj.SetBlankStatus(tag, UFConstants.UF_OBJ_BLANKED);
            HiddenTemplateObjects.Add(tag);
        }
        catch (NXException)
        {  }
    }

    private static void HideTable(ExistingTable table)
    {
        HideTemplateTag(table.Tag);
        foreach (Tag section in table.Sections) HideTemplateTag(section);
    }

    private static void HideSiemensTemplate()
    {
        RequireTemplateCleanupPhase();
        List<ExistingTable> tables = ExistingTables();
        bool foundTemplate = SiemensTemplateFound;
        foreach (ExistingTable table in tables)
            if (SiemensText(table.Text)) { HideTable(table); foundTemplate = true; }

        foreach (TitleBlock block in P.DraftingManager.TitleBlocks.ToArray())
        {
            DefineTitleBlockBuilder builder = P.DraftingManager.TitleBlocks.CreateDefineTitleBlockBuilder(block);
            try
            {
                TableSection[] components = builder.Components.GetArray();
                bool current = false, template = false;
                foreach (TableSection component in components)
                    foreach (ExistingTable table in tables)
                        if (table.Sections.Contains(component.Tag))
                        { current = true; if (SiemensText(table.Text) || TableInTitleArea(table)) template = true; }
                if (!current || !template) continue;
                foreach (TableSection component in components)
                {
                    Tag parent; U.Tabnot.AskTabularNoteOfSection(component.Tag, out parent);
                    HideTemplateTag(parent); HideTemplateTag(component.Tag);
                }
                HideTemplateTag(block.Tag); foundTemplate = true;
            }
            finally { builder.Destroy(); }
        }

        foreach (BaseNote note in P.Notes.ToArray())
        {
            // Inspect text even when a child reports IsBlanked=true: a container
            // may still display it. Sheet ownership remains required.
            if (!IsOwnFormatTag(note.Tag) && OnSheet(note) && SiemensText(String.Join(" ", note.GetText())))
            { HideTemplateTag(note.Tag); foundTemplate = true; }
        }
        if (foundTemplate)
            foreach (ExistingTable table in tables) if (TableInTitleArea(table)) HideTable(table);

        // Logo images, sheet notes and projection symbols may be separate from
        // the TitleBlock container. Only inspect objects owned by the sheet,
        // never projected geometry or annotations owned by a member view.
        Tag item = Tag.Null;
        List<Tag> layout = new List<Tag>();
        while ((item = U.Obj.CycleAll(P.Tag, item)) != Tag.Null)
            if (IsLiveTag(item) && IsSheetLayoutObject(item)) layout.Add(item);
        foreach (Tag tag in layout)
        {
            if (!IsLiveTag(tag) || HiddenTemplateObjects.Contains(tag) || IsOwnFormatTag(tag)) continue;
            TaggedObject obj;
            try { obj = NXOpen.Utilities.NXObjectManager.Get(tag); }
            catch (NXException) { continue; }
            DisplayableObject display = obj as DisplayableObject;
            if (display == null || obj is Dimension) continue;
            string name = display.Name ?? "";
            if (name.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            string typeName = obj.GetType().Name;
            bool symbol = typeName.IndexOf("CustomSymbol", StringComparison.OrdinalIgnoreCase) >= 0;
            bool image = typeName.IndexOf("Image", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         typeName.IndexOf("Raster", StringComparison.OrdinalIgnoreCase) >= 0;
            bool format = obj is BaseNote || obj is TableSection || obj is TitleBlock || symbol || image;
            Point3d at;
            if (format && TryOrigin(tag, out at))
            {
                bool projection = symbol && at.X >= Sheet.Length - 250 && at.X <= Sheet.Length - 170 && at.Y >= 0 && at.Y <= 50;
                if (SiemensText(name) || (foundTemplate && (InTitleArea(at) || projection))) HideTemplateTag(tag);
            }
            if (foundTemplate && obj is NXOpen.Curve)
            {
                double[] box = new double[6];
                U.Modl.AskBoundingBox(tag, box);
                bool title = InTitleArea(new Point3d(box[0], box[1], 0)) && InTitleArea(new Point3d(box[3], box[4], 0));
                bool projection = box[0] >= Sheet.Length - 250 && box[3] <= Sheet.Length - 170 && box[1] >= 5 && box[4] <= 50;
                if (title || projection) HideTemplateTag(tag);
            }
        }
        SiemensTemplateFound = foundTemplate;

    }

    private static void ReapplyTemplateHiding()
    {
        RequireTemplateCleanupPhase();
        // This pass runs before BuildFrame/BuildTitleTable, never afterwards.
        // RefreshCurrentSheet may remove/recreate generated template objects.
        // Never send all cached tags straight to the native blanking function.
        List<Tag> previous = new List<Tag>(HiddenTemplateObjects);
        HiddenTemplateObjects.Clear();
        foreach (Tag tag in previous)
        {
            if (!IsLiveTag(tag)) continue;
            HideTemplateTag(tag); // Same managed/native path as the first pass.
        }
        CollectSheetObjects();
        HideSiemensTemplate(); // Find replacement sections and separate symbols.

    }

    private static void RequireTemplateCleanupPhase()
    {
        // A second search by position cannot reliably distinguish regenerated
        // NX table display objects from the old Siemens template. Close the
        // entire cleanup phase as soon as the first ESKD object is created.
        if (CreatedFormatObjects.Count != 0)
            throw new InvalidOperationException("Скрытие старого шаблона вызвано после создания оформления ЕСКД.");
    }

    private static void EnsureCreatedTablesVisible()
    {
        foreach (Tag table in CreatedTables)
        {
            if (!IsLiveTag(table))
                throw new InvalidOperationException("NX не сохранил созданную таблицу ЕСКД после обновления листа.");
            U.Tabnot.Update(table);
            // A UF table may have a non-displayable parent; its current sections
            // are the visible objects. Resolve them again after the refresh.
            UnblankCreatedObject(table, false);
            int count; U.Tabnot.AskNmSections(table, out count);
            if (count < 1)
                throw new InvalidOperationException("У созданной таблицы ЕСКД нет отображаемых секций.");
            for (int i = 0; i < count; i++)
            {
                Tag section; U.Tabnot.AskNthSection(table, i, out section);
                CreatedFormatObjects.Add(section);
                UnblankCreatedObject(section, true);
            }
        }

    }

    private static void UnblankCreatedObject(Tag tag, bool requiredSection)
    {
        if (!CreatedFormatObjects.Contains(tag) || !IsLiveTag(tag))
            throw new InvalidOperationException("Не найдена секция созданной таблицы ЕСКД.");
        DisplayableObject display = null;
        try { display = NXOpen.Utilities.NXObjectManager.Get(tag) as DisplayableObject; }
        catch (NXException) { } // A native table parent need not have an NXOpen wrapper.
        if (display != null)
        {
            display.Unblank();
            if (display.IsBlanked)
                throw new InvalidOperationException("NX оставил созданную таблицу ЕСКД скрытой.");
        }
        else if (requiredSection)
        {
            // Only the known new table section is allowed through this fallback.
            U.Obj.SetBlankStatus(tag, UFConstants.UF_OBJ_NOT_BLANKED);
        }
    }

    private const double SheetSizeTolerance = 0.01;

    private static bool IsSupportedSheetSize(double width, double height)
    {
        if (Double.IsNaN(width) || Double.IsInfinity(width) ||
            Double.IsNaN(height) || Double.IsInfinity(height)) return false;
        return Math.Min(width, height) >= 210.0 - SheetSizeTolerance &&
            Math.Max(width, height) >= 297.0 - SheetSizeTolerance;
    }

    private static string BasicSheetFormat(double width, double height)
    {
        double shorter = Math.Min(width, height), longer = Math.Max(width, height);
        double[,] basic = new double[,] { { 210, 297 }, { 297, 420 }, { 420, 594 }, { 594, 841 }, { 841, 1189 } };
        string[] names = new string[] { "А4", "А3", "А2", "А1", "А0" };
        for (int i = 0; i < names.Length; i++)
            if (Math.Abs(shorter - basic[i, 0]) < SheetSizeTolerance &&
                Math.Abs(longer - basic[i, 1]) < SheetSizeTolerance) return names[i];
        return "";
    }

    private static void ApplySheetOrientation(double width, double height, Session.UndoMarkId mark)
    {
        if (Math.Abs(Sheet.Length - width) < 0.001 && Math.Abs(Sheet.Height - height) < 0.001) return;
        RequireTemplateCleanupPhase();
        Tag sheetTag = Sheet.Tag;
        double numerator, denominator;
        Sheet.GetScale(out numerator, out denominator);
        DraftingDrawingSheet drawingSheet = Sheet as DraftingDrawingSheet;
        if (drawingSheet == null)
            throw new InvalidOperationException("Текущий лист не является листом Drafting (Черчение).");
        DraftingDrawingSheetBuilder builder = P.DraftingDrawingSheets.CreateDraftingDrawingSheetBuilder(drawingSheet);
        try
        {
            // Edit the same sheet. Keep its scale, projection method, name,
            // number and associated views; no replacement template is inserted.
            builder.Option = DrawingSheetBuilder.SheetOption.CustomSize;
            builder.Height = height;
            builder.Length = width;
            builder.Commit();
        }
        finally { builder.Destroy(); }
        int errors = S.UpdateManager.DoUpdate(mark);
        if (errors != 0)
            throw new InvalidOperationException("NX сообщил ошибки при изменении формата листа: " +
                errors.ToString(CultureInfo.InvariantCulture) + ".");
        Sheet = NXOpen.Utilities.NXObjectManager.Get(sheetTag) as DrawingSheet;
        if (Sheet == null || Math.Abs(Sheet.Length - width) > 0.001 || Math.Abs(Sheet.Height - height) > 0.001)
            throw new InvalidOperationException("NX не подтвердил выбранные ширину и высоту листа.");
        double afterNumerator, afterDenominator;
        Sheet.GetScale(out afterNumerator, out afterDenominator);
        if (Math.Abs(afterNumerator - numerator) > 0.000001 || Math.Abs(afterDenominator - denominator) > 0.000001)
            throw new InvalidOperationException("NX изменил масштаб при смене ориентации листа. Изменения будут отменены.");
        DisableNativeBorder();
    }

    private static void ApplyFirstAngleProjection(string sheetName)
    {
        Tag sheetTag = Sheet.Tag;
        Session.UndoMarkId localMark = default(Session.UndoMarkId);
        bool marked = false;
        try
        {
            if (Sheet.ProjectionAngle == DrawingSheet.ProjectionAngleType.FirstAngle) return;

            // Treat a sheet with any real drafting view as an existing drawing.
            // Do this before creating a builder or an Undo mark. Do not remove,
            // move or convert views to work around NX's projected-view lock.
            // Decoration views belonging to a sheet template do not count.
            foreach (DraftingView view in Sheet.GetDraftingViews())
            {
                if (view.IsDecoration) continue;
                Warnings.Add("Лист «" + sheetName + "»: уже содержит виды. " +
                    "Сохранено 3rd Angle Projection (Проецирование в третьем угле). " +
                    "Переключение в первый угол пропущено; остальное оформление продолжено. " +
                    "На новом листе запускайте скрипт до добавления видов — первый угол установится автоматически.");
                return;
            }

            DraftingDrawingSheet drawingSheet = Sheet as DraftingDrawingSheet;
            if (drawingSheet == null)
                throw new InvalidOperationException("NX не предоставил редактируемый лист Drafting (Черчение).");
            double width = Sheet.Length, height = Sheet.Height;
            double numerator, denominator;
            Sheet.GetScale(out numerator, out denominator);
            localMark = S.SetUndoMark(Session.MarkVisibility.Invisible, "Первый угол проецирования ЕСКД");
            marked = true;
            DraftingDrawingSheetBuilder builder =
                P.DraftingDrawingSheets.CreateDraftingDrawingSheetBuilder(drawingSheet);
            try
            {
                // Change only projection. The chosen portrait/landscape size,
                // scale, sheet name and existing sheet contents stay in place.
                builder.ProjectionAngle = DrawingSheetBuilder.SheetProjectionAngle.First;
                builder.Commit();
            }
            finally { builder.Destroy(); }
            int errors = S.UpdateManager.DoUpdate(localMark);
            if (errors != 0)
                throw new InvalidOperationException("NX сообщил ошибки обновления листа: " +
                    errors.ToString(CultureInfo.InvariantCulture) + ".");
            Sheet = NXOpen.Utilities.NXObjectManager.Get(sheetTag) as DrawingSheet;
            if (Sheet == null || Sheet.ProjectionAngle != DrawingSheet.ProjectionAngleType.FirstAngle)
                throw new InvalidOperationException("NX не подтвердил 1st Angle Projection (Проецирование в первом угле).");
            double afterNumerator, afterDenominator;
            Sheet.GetScale(out afterNumerator, out afterDenominator);
            if (Math.Abs(Sheet.Length - width) > 0.001 || Math.Abs(Sheet.Height - height) > 0.001 ||
                Math.Abs(afterNumerator - numerator) > 0.000001 ||
                Math.Abs(afterDenominator - denominator) > 0.000001)
                throw new InvalidOperationException("NX изменил размер или масштаб листа вместе с проецированием.");
        }
        catch (Exception ex)
        {
            // A rejected edit must not undo the main script. Only revert this
            // optional projection change; the caller continues styling the sheet.
            if (marked)
            {
                S.UndoToMark(localMark, null);
                Sheet = (DrawingSheet)NXOpen.Utilities.NXObjectManager.Get(sheetTag);
            }
            Warnings.Add("Лист «" + sheetName + "»: не удалось установить " +
                "1st Angle Projection (Проецирование в первом угле). " +
                "Исходное проецирование сохранено; остальное оформление продолжено. Причина: " + ex.Message);
        }
        finally { if (marked) S.DeleteUndoMark(localMark, null); }
    }

    private static void DisableNativeBorder()
    {
        if (Sheet.BordersAndZones == null) return;
        BordersAndZonesBuilder b = P.Drafting.BordersAndZonesObjects.CreateBordersAndZonesBuilder(Sheet.BordersAndZones);
        try
        {
            // Method.None is rejected by NX 2606 (580048). Keep the existing
            // method and disable the border/mark/zone features individually.
            b.BorderAndZoneStyle.SheetBorderSettingsStyle.CreateBorders = false;
            b.BorderAndZoneStyle.SheetBorderSettingsStyle.CreateTrimmingMarks = false;
            b.BorderAndZoneStyle.SheetZoneSettingsStyle.CreateZones = false;
            b.BorderAndZoneStyle.SheetZoneSettingsStyle.CreateZoneLabels = false;
            b.BorderAndZoneStyle.SheetZoneSettingsStyle.CreateZoneMarkings = false;
            b.BorderAndZoneStyle.SheetMarginSettingsStyle.CreateUntrimmedMargins = false;
            b.Commit();
        }
        finally { b.Destroy(); }
    }

    private static Lettering FontLettering(Lettering text, double height)
    {
        text.Cfw.Font = FontIndex;
        text.Cfw.Color = 216;
        text.Cfw.Width = LineWidth.Thin;
        // The sole face already contains the user's inclined glyph contours.
        text.Italic = false;
        text.AspectRatio = 1.0;
        text.SymbolAspectRatio = 1.0;
        text.CharacterSpaceFactor = CharacterSpacingFactor;
        text.Size = height;
        return text;
    }

    // Values are in millimeters on the drawing, independent of view scale.
    private const double MainTextHeight = 3.5;
    private const double StackedToleranceHeight = 2.5;
    private const double DimensionLineGap = 1.0;
    private const double AppendedTextGapFactor = 1.0 / 7.0;
    // GOST 2.304-81, table 1: type A has a=2d=h/7, d=h/14.
    // NX exposes a font-dependent multiplier, NOT a distance in millimeters.
    // Calibrate against the supplied font's H and M advances before applying it.
    private static double CharacterSpacingFactor = 1.0;
    private static double GeneralLineFactor = 1.0;
    private static double LinePitchBaseRatio = 0.0, LinePitchSlopeRatio = 0.0;
    private sealed class LineProfile
    {
        public readonly NXOpen.Preferences.Width Contour, ThinView;
        public readonly DisplayableObject.ObjectWidth ContourObject, ThinObject;
        public readonly LineWidth ThinAnnotation, GeneralRoughness;
        public readonly int NativeThin;
        public LineProfile(DrawingSheet sheet)
        {
            // Standard NX widths: Three=0.25, Four=0.35, Five=0.50, Six=0.70 mm.
            // GOST 2.303: s=0.5..1.4 mm; thin=s/3..s/2. Table 2 requires
            // at least 0.3 mm when the longer sheet side is >=841 mm.
            bool large = Math.Max(sheet.Length, sheet.Height) >= 840.999;
            Contour = large ? NXOpen.Preferences.Width.Six : NXOpen.Preferences.Width.Five;
            ThinView = large ? NXOpen.Preferences.Width.Four : NXOpen.Preferences.Width.Three;
            ContourObject = large ? DisplayableObject.ObjectWidth.Six : DisplayableObject.ObjectWidth.Five;
            ThinObject = large ? DisplayableObject.ObjectWidth.Four : DisplayableObject.ObjectWidth.Three;
            ThinAnnotation = large ? LineWidth.Four : LineWidth.Three;
            GeneralRoughness = large ? LineWidth.Five : LineWidth.Four;
            NativeThin = large ? 9 : 8; // UF_DRF widths, NOT UF_OBJ widths.
        }
    }

    private static void ApplyAnnotationLineWidths(StyleBuilder style)
    {
        LineWidth thin = new LineProfile(Sheet).ThinAnnotation;
        LineArrowStyleBuilder lines = style.LineArrowStyle;
        lines.FirstArrowheadWidth = thin; lines.SecondArrowheadWidth = thin;
        lines.FirstArrowLineWidth = thin; lines.SecondArrowLineWidth = thin;
        lines.FirstExtensionLineWidth = thin; lines.SecondExtensionLineWidth = thin;
        SymbolStyleBuilder symbols = style.SymbolStyle;
        symbols.CenterlineSymbolWidth = thin;
        symbols.GdtSymbolWidth = thin; symbols.GdtSymbolFont = DisplayableObject.ObjectFont.Solid;
        symbols.IdSymbolWidth = thin; symbols.IdSymbolFont = DisplayableObject.ObjectFont.Solid;
        symbols.IntersectionSymbolWidth = thin; symbols.TargetSymbolWidth = thin;
        symbols.SurfaceFinishWidth = thin; symbols.SurfaceFinishFont = DisplayableObject.ObjectFont.Solid;
        symbols.DraftingSurfaceFinishStandard = SurfaceFinishStandard.Eskd;
        symbols.EdgeConditionWidth = thin;
    }

    private static void ApplyCurrentViewLineWidths(string sheetName)
    {
        LineProfile widths = new LineProfile(Sheet);
        foreach (DraftingView view in Sheet.GetDraftingViews())
        {
            if (view.IsDecoration) continue;
            Tag tag = view.Tag;
            try
            {
                view.Style.VisibleLines.VisibleWidth = widths.Contour;
                view.Style.HiddenLines.HiddenlineWidth = widths.ThinView;
                view.Commit();
                // Sections are updated once by the existing geometry guard below.
                if (!(view is SectionView)) view.Update();
            }
            catch (Exception ex) { AddStyleWarning("Толщины линий вида, лист " + sheetName, tag, ex); }
        }
    }

    private static void SetNativeThinLine(Tag tag, int parameterIndex)
    {
        int[] integers = new int[100]; double[] reals = new double[70];
        string radius, diameter;
        U.Drf.AskObjectPreferences(tag, integers, reals, out radius, out diameter);
        int width = new LineProfile(Sheet).NativeThin;
        integers[parameterIndex] = width;
        U.Drf.SetObjectPreferences(tag, integers, reals, radius, diameter);
        U.Drf.AskObjectPreferences(tag, integers, reals, out radius, out diameter);
        if (integers[parameterIndex] != width)
            throw new InvalidOperationException("NX не подтвердил толщину осевой линии.");
    }
    private static int StyledSheets;
    private static int StyledTables;
    private static int StyledViewLabels;
    private static int StyledDimensions;
    private static int StyledSectionLabels;
    // GOST 2.307-2011, figure 25: approximately 20 degrees; head >= 2.5 mm.
    // 3 mm is the chosen common drawing value (also in the recorded journal).
    private const double DimensionArrowHeadLength = 3.0;
    private const double DimensionArrowAngle = 20.0;

    private static void ApplyLetteringStyle(LetteringStyleBuilder b)
    {
        NXColor black = P.Colors.Find(216);
        // This family contains the user's already inclined outlines as its only
        // face. Synthetic Italic would incline them a second time.
        b.GeneralTextFont = FontIndex;
        b.DimensionTextFont = FontIndex;
        b.AppendedTextFont = FontIndex;
        b.ToleranceTextFont = FontIndex;
        b.GeneralTextItalicized = false;
        b.DimensionTextItalicized = false;
        b.AppendedTextItalicized = false;
        b.ToleranceTextItalicized = false;
        b.GeneralTextSize = MainTextHeight;
        b.DimensionTextSize = MainTextHeight;
        b.AppendedTextSize = MainTextHeight;
        // +/- on one line uses the same height as the nominal (GOST 2.307).
        b.ToleranceTextSize = MainTextHeight;
        b.TwoLineToleranceTextSize = StackedToleranceHeight;
        b.GeneralTextAspectRatio = 1.0;
        b.DimensionTextAspectRatio = 1.0;
        b.AppendedTextAspectRatio = 1.0;
        b.ToleranceTextAspectRatio = 1.0;
        b.GeneralTextSymbolAspectRatio = 1.0;
        b.DimensionTextSymbolAspectRatio = 1.0;
        b.AppendedTextSymbolAspectRatio = 1.0;
        b.ToleranceTextSymbolAspectRatio = 1.0;
        b.GeneralStandardTextCharacterSpaceFactor = CharacterSpacingFactor;
        b.DimensionStandardTextCharacterSpaceFactor = CharacterSpacingFactor;
        b.AppendedStandardTextCharacterSpaceFactor = CharacterSpacingFactor;
        b.ToleranceStandardTextCharacterSpaceFactor = CharacterSpacingFactor;
        // StandardText... only covers preferences/create mode. Current-font
        // properties are also required to change ALREADY EXISTING annotations.
        b.GeneralTextCharSpaceFactor = CharacterSpacingFactor;
        b.DimensionTextCharSpaceFactor = CharacterSpacingFactor;
        b.AppendedTextCharSpaceFactor = CharacterSpacingFactor;
        b.ToleranceTextCharSpaceFactor = CharacterSpacingFactor;
        b.GeneralTextLineSpaceFactor = GeneralLineFactor;
        b.DimensionTextLineSpaceFactor = GeneralLineFactor;
        b.AppendedTextLineSpaceFactor = GeneralLineFactor;
        // Stacked upper/lower deviations are a separate layout, not prose lines.
        b.ToleranceTextLineSpaceFactor = 0.2;
        // A typed prefix M may be a separate appended-text block, not part of
        // the nominal string. Its gap has a DIFFERENT control in NX: factor*h.
        b.AppendedTextSpaceFactor = AppendedTextGapFactor;
        b.ToleranceTextSpaceFactor = ToleranceTextGapFactor;
        b.DimLineSpaceFactor = DimensionLineGap / MainTextHeight;
        b.GeneralTextColor = black;
        b.DimensionTextColor = black;
        b.AppendedTextColor = black;
        b.ToleranceTextColor = black;
        b.GeneralTextLineWidth = LineWidth.Thin;
        b.DimensionTextLineWidth = LineWidth.Thin;
        b.AppendedTextLineWidth = LineWidth.Thin;
        b.ToleranceTextLineWidth = LineWidth.Thin;
    }

    private static ArrowheadType NormalizeDimensionArrow(ArrowheadType original)
    {
        if (original == ArrowheadType.OpenArrow || original == ArrowheadType.ClosedArrow ||
            original == ArrowheadType.ClosedSolidArrow) return ArrowheadType.FilledArrow;
        // GOST 2.307 permits dots/ticks for crowded chains. Preserve those and
        // special ordinate origins; changing their meaning is not a style edit.
        return original;
    }

    private static void ApplyDimensionAppearance(StyleBuilder b, bool existing = false)
    {
        LineArrowStyleBuilder a = b.LineArrowStyle;
        a.ArrowheadLength = DimensionArrowHeadLength;
        a.ArrowheadIncludedAngle = DimensionArrowAngle;
        a.DotArrowheadDiameter = 1.0;
        ApplyAnnotationLineWidths(b);
        a.FirstArrowType = existing ? NormalizeDimensionArrow(a.FirstArrowType) : ArrowheadType.FilledArrow;
        a.SecondArrowType = existing ? NormalizeDimensionArrow(a.SecondArrowType) : ArrowheadType.FilledArrow;
        NXColor black = P.Colors.Find(216);
        a.FirstArrowheadColor = black;
        a.SecondArrowheadColor = black;
        a.FirstArrowLineColor = black;
        a.SecondArrowLineColor = black;
        a.FirstExtensionLineColor = black;
        a.SecondExtensionLineColor = black;
        a.FirstArrowheadFont = DisplayableObject.ObjectFont.Solid;
        a.SecondArrowheadFont = DisplayableObject.ObjectFont.Solid;
        a.FirstArrowLineFont = DisplayableObject.ObjectFont.Solid;
        a.SecondArrowLineFont = DisplayableObject.ObjectFont.Solid;
        a.FirstExtensionLineFont = DisplayableObject.ObjectFont.Solid;
        a.SecondExtensionLineFont = DisplayableObject.ObjectFont.Solid;
        a.FirstPosToExtensionLineDistance = 0.0;
        a.SecondPosToExtensionLineDistance = 0.0;
        a.LinePastArrowDistance = 2.0;
        a.LinePastArrowDistance2 = 2.0;
        b.DimensionStyle.BaselineOffset = 7.0;
        b.DimensionStyle.ChainOffset = 7.0;
        b.DimensionStyle.Orientation = TextOrientation.OverDimensionLine;
        b.DimensionStyle.DimZeroToleranceDisplayStyle = ZeroToleranceDisplayStyle.Omitted;
        b.UnitsStyle.DecimalPointCharacter = DecimalPointCharacter.Comma;
        // Dimension > Text > Units > Decimal Places (the user's screenshot).
        // Angular and tolerance precision are independent and remain unchanged.
        b.DimensionStyle.DimensionValuePrecision = 3;
        b.UnitsStyle.DisplayLeadingDimensionZeros = true;
        b.UnitsStyle.DisplayTrailingZeros = false;
        // Preserve tolerance values/type, unit conversion and associativity.
    }

    private static bool DimensionPrecisionMatches(StyleBuilder b)
    {
        return b.DimensionStyle.DimensionValuePrecision == 3 &&
            b.UnitsStyle.DecimalPointCharacter == DecimalPointCharacter.Comma &&
            b.UnitsStyle.DisplayLeadingDimensionZeros && !b.UnitsStyle.DisplayTrailingZeros;
    }

    private static bool DimensionArrowStyleMatches(LineArrowStyleBuilder b, bool existing = false)
    {
        const double eps = 0.0001;
        return (existing ? b.FirstArrowType == NormalizeDimensionArrow(b.FirstArrowType) : b.FirstArrowType == ArrowheadType.FilledArrow) &&
            (existing ? b.SecondArrowType == NormalizeDimensionArrow(b.SecondArrowType) : b.SecondArrowType == ArrowheadType.FilledArrow) &&
            Math.Abs(b.ArrowheadLength - DimensionArrowHeadLength) < eps &&
            Math.Abs(b.ArrowheadIncludedAngle - DimensionArrowAngle) < eps &&
            b.FirstArrowheadFont == DisplayableObject.ObjectFont.Solid &&
            b.SecondArrowheadFont == DisplayableObject.ObjectFont.Solid;
    }

    private static bool SectionLabelStyleMatches(ViewSectionLabelBuilder b)
    {
        return String.IsNullOrWhiteSpace(b.LabelPrefix) && !b.CustomizedViewLabel &&
            b.ViewLabelOption == ViewLabelTypes.Letter && b.LetterFormat == LetterFormatTypes.AA;
    }

    private static void ApplySectionLabelStyle(ViewSectionLabelBuilder b)
    {
        // Use NX's associative letter-dash-letter label, not literal note text.
        b.CustomizedViewLabel = false;
        b.ViewLabelOption = ViewLabelTypes.Letter;
        b.LetterFormat = LetterFormatTypes.AA;
        b.LabelPrefix = "";
        b.LabelPosition = LabelPositionTypes.Above;
        b.LabelCharacterHeightFactor = SectionLetterHeight / MainTextHeight;
        b.PrefixCharacterHeightFactor = SectionLetterHeight / MainTextHeight;
        b.ScaleCharacterHeightFactor = SectionLetterHeight / MainTextHeight;
        b.ScalePrefix = "";
        b.IncludeParentheses = true;
        // Keep the actual section letter and existing scale/show choices;
        // only the label format becomes automatic A-A without SECTION.
    }

    private static void ApplyViewLetterSizes(ViewLabelBuilder b)
    {
        b.LabelCharacterHeightFactor = SectionLetterHeight / MainTextHeight;
        b.PrefixCharacterHeightFactor = SectionLetterHeight / MainTextHeight;
        b.ScaleCharacterHeightFactor = SectionLetterHeight / MainTextHeight;
        b.ScalePrefix = ""; b.IncludeParentheses = true;
        string prefix = (b.LabelPrefix ?? "").Trim();
        if (prefix.Equals("VIEW", StringComparison.OrdinalIgnoreCase)) b.LabelPrefix = "";
        // Keep customized label contents, references and the actual letters.
    }

    private static void ApplyDetailLetterSizes(ViewDetailLabelBuilder b)
    {
        b.LabelCharacterHeightFactor = SectionLetterHeight / MainTextHeight;
        b.PrefixCharacterHeightFactor = SectionLetterHeight / MainTextHeight;
        b.ScaleCharacterHeightFactor = SectionLetterHeight / MainTextHeight;
        b.ScalePrefix = ""; b.IncludeParentheses = true;
        if ((b.LabelPrefix ?? "").Trim().Equals("DETAIL", StringComparison.OrdinalIgnoreCase)) b.LabelPrefix = "";
        if ((b.ParentLabelPrefix ?? "").Trim().Equals("DETAIL", StringComparison.OrdinalIgnoreCase)) b.ParentLabelPrefix = "";
    }

    // GOST 2.305-2008, figure 14: head >= 5 mm, shaft >= 5 mm,
    // included angle 15..20 degrees. Clause 6.5: arrow meets the cutting
    // stroke 2..3 mm from its end. Clause 6.6 places letters near arrows;
    // letter clearance is our 2 mm layout choice, not a prescribed value.
    // It is implemented by PositionCurrentSectionLetters, NEVER by Gap/UseOffset.
    private const double SectionArrowHeadLength = 5.0;
    private const double SectionArrowTotalLength = 10.0;
    private const double SectionArrowAngle = 20.0;
    private const double SectionStrokeOverhang = 2.5;
    private const double SectionEndStrokeLength = 10.0;
    private static int StyledSectionLines;

    private static void ApplySectionArrowStyle(ViewSectionLineBuilder b)
    {
        b.TypeStandard = ViewSectionLineBuilder.DisplayType.ThickEndsArrowstowardsLine;
        b.Style = ViewSectionLineBuilder.StyleType.Filled;
        b.ArrowheadAngle = SectionArrowAngle;
        b.ArrowheadLength = SectionArrowHeadLength;
        b.ArrowLength = SectionArrowTotalLength;
        b.Overhang = SectionStrokeOverhang;
        b.UseLineLength = true;
        b.LineLength = SectionEndStrokeLength;
        // LabelLocation is for ISO128 lines, not this ESKD line type.
        // UseOffset enables a GEOMETRIC section corridor. v1.11-v1.17
        // wrongly treated it as a label offset, clipping background geometry.
        b.UseOffset = false;
        // Gap belongs to that corridor; leave its inactive value untouched.
        b.LineColorFontWidth.LineColor = P.Colors.Find(216);
        b.LineColorFontWidth.LineWidth = new LineProfile(Sheet).ContourObject;
        b.BendAndEndSegmentWidthFactor = 1.0;
        // Preserve the section plane, view direction, letter, display choices,
        // parent view and all existing segment geometry/associativity.
    }

    private static bool SectionDefaultsVerified;

    private static string SectionNumber(double value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static void AddSectionNumberDifference(List<string> differences, string name,
        double actual, double requested)
    {
        if (Double.IsNaN(actual) || Double.IsInfinity(actual) || Math.Abs(actual - requested) > 0.0001)
            differences.Add(name + ": задано " + SectionNumber(requested) + ", получено " + SectionNumber(actual));
    }

    private static bool CheckSectionArrowStyle(ViewSectionLineBuilder b, string context)
    {
        // Compare the requested ESKD properties. LabelLocation is documented
        // for ISO128 section lines only, so it is neither assigned nor tested.
        ViewSectionLineBuilder.DisplayType type = b.TypeStandard;
        ViewSectionLineBuilder.StyleType style = b.Style;
        bool useLength = b.UseLineLength;
        bool useOffset = b.UseOffset;
        double angle = b.ArrowheadAngle;
        double head = b.ArrowheadLength;
        double length = b.ArrowLength;
        double overhang = b.Overhang;
        double endLength = b.LineLength;
        List<string> differences = new List<string>();
        if (type != ViewSectionLineBuilder.DisplayType.ThickEndsArrowstowardsLine)
            differences.Add("Type Standard (Тип линии): задано ThickEndsArrowstowardsLine, получено " + type.ToString());
        if (style != ViewSectionLineBuilder.StyleType.Filled)
            differences.Add("Style (Форма наконечника): задано Filled, получено " + style.ToString());
        if (!useLength) differences.Add("Use Line Length (Использовать длину штриха): задано True, получено False");
        if (useOffset) differences.Add("Use Offset (Геометрическое смещение разреза): задано False, получено True");
        AddSectionNumberDifference(differences, "Arrowhead Angle (Угол наконечника), градусы", angle, SectionArrowAngle);
        AddSectionNumberDifference(differences, "Arrowhead Length (Длина наконечника), мм", head, SectionArrowHeadLength);
        AddSectionNumberDifference(differences, "Arrow Length (Длина стрелки), мм", length, SectionArrowTotalLength);
        AddSectionNumberDifference(differences, "Overhang (Выступ штриха), мм", overhang, SectionStrokeOverhang);
        AddSectionNumberDifference(differences, "Line Length (Длина концевого штриха), мм", endLength, SectionEndStrokeLength);

        if (differences.Count == 0) return true;
        Warnings.Add(context + ": параметры подтверждены не полностью.\n" +
            String.Join("\n", differences.ToArray()) + "\nПроверьте стрелки и буквы на чертеже.");
        return false;
    }

    private static void ApplyStylesToExistingSectionLines()
    {
        StyledSectionLines = 0;
        List<Tag> tags = new List<Tag>();
        foreach (SectionLine item in P.Drafting.SectionLines.ToArray())
        {
            try { tags.Add(item.Tag); }
            catch (NXException ex) { AddStyleWarning("Список линий разрезов", Tag.Null, ex); }
        }
        foreach (Tag tag in tags)
        {
            try
            {
                if (!IsLiveTag(tag)) continue;
                SectionLine line = NXOpen.Utilities.NXObjectManager.Get(tag) as SectionLine;
                if (line == null || line.OwningPart == null || line.OwningPart.Tag != P.Tag) continue;
                Tag updatedTag = tag;
                EditSectionLineSettingsBuilder b = P.SettingsManager.CreateDrawingEditSectionLineSettingsBuilder(
                    new SectionLine[] { line });
                try
                {

                    ApplySectionArrowStyle(b.ViewSectionLine);
                    Tag parent; ReadSectionSegments(tag, out parent);
                    foreach (DrawingSheet owner in P.DrawingSheets.ToArray())
                        foreach (DraftingView candidate in owner.GetDraftingViews())
                            if (candidate.Tag == parent)
                                b.ViewSectionLine.LineColorFontWidth.LineWidth = new LineProfile(owner).ContourObject;
                    NXObject committed = b.Commit();
                    if (committed is SectionLine) updatedTag = committed.Tag;
                }
                finally { b.Destroy(); }

                if (!IsLiveTag(updatedTag))
                    throw new InvalidOperationException("Линия разреза изменилась при обновлении; оформление не подтверждено.");
                SectionLine fresh = NXOpen.Utilities.NXObjectManager.Get(updatedTag) as SectionLine;
                if (fresh == null) throw new InvalidOperationException("Не удалось перечитать линию разреза.");
                EditSectionLineSettingsBuilder check = P.SettingsManager.CreateDrawingEditSectionLineSettingsBuilder(
                    new SectionLine[] { fresh });
                bool verified;
                try
                {
                    verified = CheckSectionArrowStyle(check.ViewSectionLine,
                        "Линия разреза (tag " + updatedTag.ToString() + ")");
                }
                finally { check.Destroy(); }
                if (verified) StyledSectionLines++;
            }
            catch (Exception ex) { AddStyleWarning("Линия разреза", tag, ex); }
        }
    }

    private const double SectionLetterClearance = 2.0;
    // GOST R 2.316-2023, 5.10: lettering identifying views is about twice
    // the dimension lettering. 7 mm is a standard size and exactly 2 x 3.5.
    private const double SectionLetterHeight = 7.0;
    private static int PositionedSectionLetters;
    private static readonly List<SectionLetterPlacement> SectionLetterPlacements = new List<SectionLetterPlacement>();

    private sealed class SectionArrowTarget
    {
        public Tag LineTag, SegmentTag, CurveTag, ParentView;
        public Tag[] LineSegments;
        public Point3d Start, End;
        public double NX, NY, HeadHalfWidth;
        public string Letter;
        public Point3d Center { get { return new Point3d((Start.X + End.X) / 2.0, (Start.Y + End.Y) / 2.0, 0.0); } }
    }

    private sealed class SectionLetterPlacement
    {
        public Tag NoteTag;
        public SectionArrowTarget Arrow;
        public string Context;
    }

    private static Tag[] ReadSectionSegments(Tag lineTag, out Tag parent)
    {
        // Read only: these routines do not move the section plane or its segments.
        UFDraw.SxlineType type;
        U.Draw.AskSxlineType(lineTag, out type);
        double[] step = new double[3], arrow = new double[3];
        int viewCount, segmentCount;
        Tag[] views, segments;
        UFDraw.SxlineStatus status;
        switch (type)
        {
            case UFDraw.SxlineType.SimpleSxline:
                U.Draw.AskSimpleSxline(lineTag, step, arrow, out parent, out viewCount, out views, out segmentCount, out segments, out status); break;
            case UFDraw.SxlineType.SteppedSxline:
                U.Draw.AskSteppedSxline(lineTag, step, arrow, out parent, out viewCount, out views, out segmentCount, out segments, out status); break;
            case UFDraw.SxlineType.HalfSxline:
                U.Draw.AskHalfSxline(lineTag, step, arrow, out parent, out viewCount, out views, out segmentCount, out segments, out status); break;
            case UFDraw.SxlineType.FoldedSxline:
                U.Draw.AskFoldedSxline(lineTag, step, arrow, out parent, out viewCount, out views, out segmentCount, out segments, out status); break;
            case UFDraw.SxlineType.UnfoldedSxline:
                U.Draw.AskUnfoldedSxline(lineTag, step, arrow, out parent, out viewCount, out views, out segmentCount, out segments, out status); break;
            case UFDraw.SxlineType.RevolvedSxline:
                UFDrf.Object rotation;
                int firstLegCount;
                UFDraw.SxlineLeg leg;
                U.Draw.AskRevolvedSxline(lineTag, step, arrow, out parent, out rotation, out viewCount, out views,
                    out segmentCount, out firstLegCount, out leg, out segments, out status); break;
            default:
                throw new InvalidOperationException("Не поддержан тип линии для размещения букв: " + type.ToString());
        }
        return segments ?? new Tag[0];
    }

    private static List<SectionArrowTarget> ReadCurrentSectionArrows(string sheetName)
    {
        List<SectionArrowTarget> result = new List<SectionArrowTarget>();
        List<Tag> lineTags = new List<Tag>();
        foreach (SectionLine line in P.Drafting.SectionLines.ToArray()) lineTags.Add(line.Tag);
        foreach (Tag lineTag in lineTags)
        {
            try
            {
                if (!IsLiveTag(lineTag)) continue;
                SectionLine line = NXOpen.Utilities.NXObjectManager.Get(lineTag) as SectionLine;
                if (line == null || line.IsBlanked) continue;
                Tag parent;
                Tag[] segments = ReadSectionSegments(lineTag, out parent);
                if (!SheetViews.Contains(parent)) continue;
                string letter;
                double halfWidth;
                EditSectionLineSettingsBuilder b = P.SettingsManager.CreateDrawingEditSectionLineSettingsBuilder(new SectionLine[] { line });
                try
                {
                    if (!b.ViewSectionLine.Display) continue;
                    letter = b.ViewCommonViewLabel.Letter;
                    halfWidth = b.ViewSectionLine.ArrowheadLength * Math.Tan(b.ViewSectionLine.ArrowheadAngle * Math.PI / 360.0);
                }
                finally { b.Destroy(); }
                List<SectionArrowTarget> lineArrows = new List<SectionArrowTarget>();
                foreach (Tag segment in segments)
                {
                    UFDraw.SxsegInfo info;
                    Tag curveTag;
                    UFDrf.Object[] associations;
                    U.Draw.AskSxlineSxseg(segment, out info, out curveTag, out associations);
                    if (info.sxseg_type != UFDraw.SxsegType.SxsegArrow || !IsLiveTag(curveTag)) continue;
                    NXOpen.Line curve = NXOpen.Utilities.NXObjectManager.Get(curveTag) as NXOpen.Line;
                    if (curve == null) continue;
                    SectionArrowTarget target = new SectionArrowTarget();
                    target.LineTag = lineTag; target.SegmentTag = segment; target.CurveTag = curveTag;
                    target.LineSegments = segments;
                    target.ParentView = parent; target.Start = curve.StartPoint; target.End = curve.EndPoint;
                    target.Letter = letter; target.HeadHalfWidth = halfWidth;
                    double dx = target.End.X - target.Start.X, dy = target.End.Y - target.Start.Y;
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    if (length < 0.0001 || Math.Abs(target.Start.Z) > 0.001 || Math.Abs(target.End.Z) > 0.001)
                        throw new InvalidOperationException("Стрелка не представлена линией в плоскости листа.");
                    target.NX = -dy / length; target.NY = dx / length;
                    lineArrows.Add(target);
                }
                if (lineArrows.Count < 2)
                    throw new InvalidOperationException("Не найдены две концевые стрелки; расположение букв не изменено.");
                // Choose the outside of the section line, separately for each end.
                double cx = 0.0, cy = 0.0;
                foreach (SectionArrowTarget target in lineArrows) { cx += target.Center.X; cy += target.Center.Y; }
                cx /= lineArrows.Count; cy /= lineArrows.Count;
                foreach (SectionArrowTarget target in lineArrows)
                {
                    double side = (target.Center.X - cx) * target.NX + (target.Center.Y - cy) * target.NY;
                    if (Math.Abs(side) < 0.001)
                        throw new InvalidOperationException("Неоднозначна внешняя сторона стрелки; буквы требуют проверки.");
                    if (side < 0.0) { target.NX = -target.NX; target.NY = -target.NY; }
                }
                result.AddRange(lineArrows);
            }
            catch (Exception ex) { AddStyleWarning("Стрелки для размещения букв, лист " + sheetName, lineTag, ex); }
        }
        return result;
    }

    private static SectionArrowTarget FindLetterArrow(Note note, List<SectionArrowTarget> arrows, AssociativeText text)
    {
        // Identify letters by their native geometric dependency AND evaluated
        // section letter. Do not parse internal <W...> control sequences, and
        // never treat an arbitrary note containing "A" as a section letter.
        if (!note.HasAssociativeOrigin) return null;
        Point3d origin;
        Annotation.AssociativeOriginData data = note.GetAssociativeOrigin(out origin);
        if (data.OriginType != AssociativeOriginType.RelativeToGeometry || data.PointOnGeometry == null) return null;
        int count;
        Tag[] parents;
        HashSet<Tag> dependencies = new HashSet<Tag>();
        dependencies.Add(data.PointOnGeometry.Tag);
        string stored = String.Join("", note.GetText());
        try
        {
            NXObject referenced;
            string attribute;
            if (text.GetObjectAttribute(stored, out referenced, out attribute) && referenced != null)
                dependencies.Add(referenced.Tag);
        }
        catch (NXException) { } // A note may not consist of one attribute reference.
        try
        {
            U.So.AskParents(data.PointOnGeometry.Tag,
                UFConstants.UF_SO_ASK_ALL_PARENTS | UFConstants.UF_SO_ASK_PARENTS_RECURSIVELY, out count, out parents);
            if (parents != null) foreach (Tag parent in parents) dependencies.Add(parent);
        }
        catch (NXException) { } // The referenced object may already identify the line.
        string evaluated = text.GetEvaluatedText(note, stored).Trim();
        SectionArrowTarget best = null;
        double bestDistance = Double.MaxValue;
        Tag matchedLine = Tag.Null;
        foreach (SectionArrowTarget target in arrows)
        {
            bool linked = dependencies.Contains(target.LineTag) || dependencies.Contains(target.CurveTag);
            foreach (Tag segment in target.LineSegments) if (dependencies.Contains(segment)) linked = true;
            if (!linked) continue;
            if (!String.Equals(evaluated, target.Letter, StringComparison.Ordinal))
            {

                continue;
            }
            if (matchedLine != Tag.Null && matchedLine != target.LineTag)
                throw new InvalidOperationException("Надпись связана с несколькими линиями разрезов; перенос отменён.");
            matchedLine = target.LineTag;
            double dx = origin.X - target.Center.X, dy = origin.Y - target.Center.Y;
            double distance = dx * dx + dy * dy;
            if (distance < bestDistance) { bestDistance = distance; best = target; }
        }
        return best;
    }

    private static double ReadLetterClearance(Tag noteTag, SectionArrowTarget arrow, out Point3d center, out double support)
    {
        double[] upperLeft = new double[3];
        double width, height;
        U.Drf.AskAnnotationTextBox(noteTag, upperLeft, out width, out height);
        if (width <= 0.0 || height <= 0.0 || Double.IsNaN(width) || Double.IsNaN(height) ||
            Double.IsInfinity(width) || Double.IsInfinity(height))
            throw new InvalidOperationException("NX не вернул измеримый габарит буквы.");
        center = new Point3d(upperLeft[0] + width / 2.0, upperLeft[1] - height / 2.0, upperLeft[2]);
        support = (Math.Abs(arrow.NX) * width + Math.Abs(arrow.NY) * height) / 2.0;
        return (center.X - arrow.Center.X) * arrow.NX + (center.Y - arrow.Center.Y) * arrow.NY - support - arrow.HeadHalfWidth;
    }

    private static void PositionCurrentSectionLetters(string sheetName)
    {
        List<SectionArrowTarget> arrows = ReadCurrentSectionArrows(sheetName);
        if (arrows.Count == 0) return;
        List<Tag> notes = new List<Tag>();
        foreach (BaseNote note in P.Notes.ToArray()) notes.Add(note.Tag);
        HashSet<Tag> usedArrows = new HashSet<Tag>();
        using (AssociativeText text = P.Annotations.CreateAssociativeText())
        {
            foreach (Tag tag in notes)
            {
                Session.UndoMarkId localMark = default(Session.UndoMarkId);
                bool marked = false;
                string context = "Буква разреза, лист " + sheetName;
                try
                {
                    if (!IsLiveTag(tag)) continue;
                    Note note = NXOpen.Utilities.NXObjectManager.Get(tag) as Note;
                    if (note == null || note.IsBlanked || !OnSheet(note)) continue;
                    SectionArrowTarget arrow = FindLetterArrow(note, arrows, text);
                    if (arrow == null) continue;
                    if (usedArrows.Contains(arrow.SegmentTag))
                        throw new InvalidOperationException("Для одной стрелки обнаружено несколько букв; проверьте обозначение.");
                    localMark = S.SetUndoMark(Session.MarkVisibility.Invisible, "Буква у стрелки ЕСКД"); marked = true;
                    using (LetteringPreferences lp = note.GetLetteringPreferences())
                    {
                        lp.SetGeneralText(FontLettering(lp.GetGeneralText(), SectionLetterHeight));
                        lp.Angle = 0.0;
                        note.SetLetteringPreferences(lp);
                    }
                    if (S.UpdateManager.DoUpdate(localMark) != 0)
                        throw new InvalidOperationException("NX не обновил размер буквы перед проверкой отступа.");
                    note = NXOpen.Utilities.NXObjectManager.Get(tag) as Note;
                    if (note == null) throw new InvalidOperationException("Не удалось перечитать букву после оформления.");
                    Point3d origin;
                    Annotation.AssociativeOriginData data = note.GetAssociativeOrigin(out origin);
                    if (data.PointOnGeometry == null)
                        throw new InvalidOperationException("У буквы нет штатной привязки к геометрии стрелки.");
                    Tag associatedPoint = data.PointOnGeometry.Tag;
                    Point3d center;
                    double support;
                    double before = ReadLetterClearance(tag, arrow, out center, out support);
                    double offset = SectionLetterClearance + arrow.HeadHalfWidth + support;
                    Point3d wanted = new Point3d(arrow.Center.X + arrow.NX * offset, arrow.Center.Y + arrow.NY * offset, center.Z);
                    Point3d moved = new Point3d(origin.X + wanted.X - center.X, origin.Y + wanted.Y - center.Y, origin.Z);
                    note.SetAssociativeOrigin(data, moved);
                    if (S.UpdateManager.DoUpdate(localMark) != 0)
                        throw new InvalidOperationException("NX не обновил положение буквы разреза.");
                    note = NXOpen.Utilities.NXObjectManager.Get(tag) as Note;
                    if (note == null || !note.HasAssociativeOrigin)
                        throw new InvalidOperationException("Не сохранилась ассоциативная привязка буквы.");
                    Annotation.AssociativeOriginData afterData = note.GetAssociativeOrigin(out origin);
                    if (afterData.OriginType != data.OriginType || afterData.PointOnGeometry == null || afterData.PointOnGeometry.Tag != associatedPoint)
                        throw new InvalidOperationException("Изменилась геометрическая привязка буквы.");
                    double actual = ReadLetterClearance(tag, arrow, out center, out support);
                    if (Math.Abs(actual - SectionLetterClearance) > 0.05)
                        throw new InvalidOperationException("NX не подтвердил зазор до буквы: " + SectionNumber(actual) + " мм вместо 2 мм.");

                    SectionLetterPlacement placement = new SectionLetterPlacement();
                    placement.NoteTag = tag; placement.Arrow = arrow; placement.Context = context;
                    SectionLetterPlacements.Add(placement);
                    usedArrows.Add(arrow.SegmentTag); PositionedSectionLetters++;
                }
                catch (Exception ex)
                {
                    if (marked) S.UndoToMark(localMark, null);
                    AddStyleWarning(context, tag, ex);
                }
                finally { if (marked) S.DeleteUndoMark(localMark, null); }
            }
        }
        foreach (SectionArrowTarget arrow in arrows)
            if (!usedArrows.Contains(arrow.SegmentTag))
                Warnings.Add("Лист " + sheetName + ": не подтверждено положение буквы у стрелки " + arrow.SegmentTag.ToString() +
                    ". Не удалось однозначно найти штатную ассоциативную надпись.");
    }

    private static void AuditFinalSectionLetters()
    {
        foreach (SectionLetterPlacement placement in SectionLetterPlacements)
        {
            try
            {
                if (!IsLiveTag(placement.NoteTag)) throw new InvalidOperationException("Буква была пересоздана при обновлении листа.");
                NXOpen.Line curve = NXOpen.Utilities.NXObjectManager.Get(placement.Arrow.CurveTag) as NXOpen.Line;
                if (curve == null) throw new InvalidOperationException("Стрелка была пересоздана при обновлении листа.");
                placement.Arrow.Start = curve.StartPoint; placement.Arrow.End = curve.EndPoint;
                Point3d center;
                double support;
                double gap = ReadLetterClearance(placement.NoteTag, placement.Arrow, out center, out support);

                if (Math.Abs(gap - SectionLetterClearance) > 0.05)
                    throw new InvalidOperationException("После обновления листа зазор до буквы стал " + SectionNumber(gap) + " мм.");
            }
            catch (Exception ex) { AddStyleWarning(placement.Context + ", итоговая проверка", placement.NoteTag, ex); }
        }
    }

    private static int UpdatedSectionViews;
    private static bool SectionGeometryDefaultsVerified;
    // The confirmed working setup uses Medium in part preferences and in the
    // new-section builder. Both existing GOOD/BAD sections already use Medium.
    // This matches that working baseline; it is not an ESKD quality requirement
    // or proof that Fine alone caused the missing geometry.
    private const NXOpen.Preferences.GeneralViewQualityOption SectionCreationQuality =
        NXOpen.Preferences.GeneralViewQualityOption.Medium;

    private sealed class SectionVisibilityState
    {
        public int Visible;
        public int Clipped;
        public int Erased;
        public int ErasedEdges;
        public int ErasedBodies;
        public int SystemErasedBodies;
    }

    private static SectionVisibilityState ReadSectionVisibility(Tag viewTag)
    {
        SectionVisibilityState state = new SectionVisibilityState();
        Tag[] visible, clipped;
        U.View.AskVisibleObjects(viewTag, out state.Visible, out visible, out state.Clipped, out clipped);
        // Cycle only while reading. NX forbids changing VDEs during a cycle.
        Tag objectTag = Tag.Null;
        HashSet<Tag> visited = new HashSet<Tag>();
        while (true)
        {
            U.View.CycleObjects(viewTag, UFView.CycleObjectsEnum.ErasedObjects, ref objectTag);
            if (objectTag == Tag.Null) break;
            if (!visited.Add(objectTag))
                throw new InvalidOperationException("NX повторил объект при чтении скрытий разреза.");
            state.Erased++;
            TaggedObject item = NXOpen.Utilities.NXObjectManager.Get(objectTag);
            if (item is Edge) state.ErasedEdges++;
            if (!(item is Body)) continue;
            state.ErasedBodies++;
            int editCount;
            UFView.VdeDataAndType[] edits;
            U.View.AskVdeDataWithType(objectTag, out editCount, out edits);
            bool fullSystemErase = false;
            bool userEdit = false;
            if (edits != null)
                for (int i = 0; i < editCount && i < edits.Length; i++)
                {
                    UFView.VdeDataAndType edit = edits[i];
                    if (edit.view_tag != viewTag) continue;
                    if (edit.vde_type == UFView.VdeType.VdeUser) userEdit = true;
                    // Here font=0 belongs to a VDE erase record, NOT to
                    // ViewPrfs.visible_line_font (where 0 means Original).
                    if (edit.vde_type == UFView.VdeType.VdeSystem && edit.font == 0 &&
                        Math.Abs(edit.start_parameter) < 0.000001 &&
                        Math.Abs(edit.end_parameter - 1.0) < 0.000001)
                        fullSystemErase = true;
                }
            if (fullSystemErase && !userEdit)
            {
                state.SystemErasedBodies++;
            }
        }
        return state;
    }

    private static void SetNativeSectionBackground(Tag viewTag, bool enabled)
    {
        // Read/modify/write ONLY sx_background. Do not replace the entire
        // preferences structure with defaults or remove any VDE records.
        UFDraw.SxviewPrfs prefs;
        U.Draw.AskSxviewDisplay(viewTag, out prefs);
        prefs.sx_background = enabled ? UFDraw.SxBackground.SxBackgroundOn :
            UFDraw.SxBackground.SxBackgroundOff;
        U.Draw.SetSxviewDisplay(viewTag, ref prefs);
    }

    private static void UpdateCurrentSectionGeometry(string sheetName)
    {
        // This pass runs after section-line styling and before annotation tags
        // are collected, because updating a section can regenerate its labels.
        List<Tag> tags = new List<Tag>();
        foreach (DraftingView view in Sheet.GetDraftingViews())
        {
            try { if (view is SectionView) tags.Add(view.Tag); }
            catch (NXException ex) { AddStyleWarning("Список разрезов, лист " + sheetName, Tag.Null, ex); }
        }
        foreach (Tag tag in tags)
        {
            Session.UndoMarkId localMark = default(Session.UndoMarkId);
            bool marked = false;
            string context = "Геометрия разреза, лист " + sheetName;
            try
            {
                if (!IsLiveTag(tag)) continue;
                SectionView view = NXOpen.Utilities.NXObjectManager.Get(tag) as SectionView;
                if (view == null) continue;
                context = "Разрез " + view.Name + ", лист " + sheetName;
                bool originalForeground = view.Style.Section.Foreground;
                bool originalCrosshatch = view.Style.Section.CrossHatch;
                SectionVisibilityState before = ReadSectionVisibility(tag);

                Tag drawingTag;
                U.Draw.AskDrawingOfView(tag, out drawingTag);
                if (drawingTag == Tag.Null)
                    throw new InvalidOperationException("Не найден лист, которому принадлежит разрез.");
                localMark = S.SetUndoMark(Session.MarkVisibility.Invisible, "Восстановление фона разреза ЕСКД");
                marked = true;

                // Preserve the fix for missing background geometry caused by the
                // section-line UseOffset corridor. It has now been disabled
                // for existing lines and part defaults before this update.
                // Do not toggle Background Off or delete VDE records.
                SetNativeSectionBackground(tag, true);
                U.Draw.UpdateOneView(drawingTag, tag);

                SectionView fresh = NXOpen.Utilities.NXObjectManager.Get(tag) as SectionView;
                if (fresh == null) throw new InvalidOperationException("Не удалось перечитать обновлённый разрез.");
                UFDraw.SxviewPrfs native;
                U.Draw.AskSxviewDisplay(tag, out native);
                if (!fresh.Style.Section.Background || native.sx_background != UFDraw.SxBackground.SxBackgroundOn)
                    throw new InvalidOperationException("NX не подтвердил Background (Геометрия за секущей плоскостью).");
                if (fresh.Style.Section.Foreground != originalForeground ||
                    fresh.Style.Section.CrossHatch != originalCrosshatch)
                    throw new InvalidOperationException("При восстановлении изменились передний план или штриховка.");
                SectionVisibilityState after = ReadSectionVisibility(tag);

                if (before.SystemErasedBodies == 0 && after.SystemErasedBodies > 0 && after.Visible < before.Visible)
                    throw new InvalidOperationException("После пересчёта появилось системное скрытие тела и уменьшилось число видимых объектов. Пересчёт отменён.");
                UpdatedSectionViews++;

                if (after.SystemErasedBodies > 0)
                    Warnings.Add(context + ": NX системно скрывает тел: " + after.SystemErasedBodies.ToString() +
                        ". Проверьте внутренние кромки.");
                // Visibility counts alone do not certify drawing correctness.
            }
            catch (Exception ex)
            {
                if (marked) S.UndoToMark(localMark, null);

                AddStyleWarning(context, tag, ex);
            }
            finally { if (marked) S.DeleteUndoMark(localMark, null); }
        }
    }

    private static void VerifyNewSectionDefaults(bool savedBackground,
        NXOpen.Preferences.GeneralViewQualityOption savedQuality)
    {
        SectionGeometryDefaultsVerified = false;

        // Independently read actual part preferences and an uncommitted creation
        // builder. No test view is inserted. The initial builder has no parent;
        // these checks do not certify the geometry of a future placed section.
        try
        {
            bool directBackground = P.ViewPreferences.Section.Background;
            NXOpen.Preferences.GeneralViewQualityOption directQuality = P.ViewPreferences.General.ViewQuality;

            SectionViewBuilder probe = P.DraftingViews.CreateSectionViewBuilder(null);
            try
            {
                ViewStyleSectionBuilder style = probe.ViewStyle.ViewStyleSection;
                NXOpen.Preferences.GeneralViewQualityOption newQuality = probe.ViewStyle.ViewStyleGeneral.ViewQuality;
                bool corridorOff = !probe.ViewStyle.ViewSectionLineStyleBuilder.UseOffset;
                SectionGeometryDefaultsVerified = corridorOff && savedBackground && directBackground && style.Background &&
                    savedQuality == SectionCreationQuality && directQuality == SectionCreationQuality &&
                    newQuality == SectionCreationQuality;

                if (!SectionGeometryDefaultsVerified)
                    Warnings.Add("Не все настройки новых видов подтверждены: требуются " +
                        "View Quality (Качество вида) = Medium (Среднее) и " +
                        "Background (Геометрия за секущей плоскостью) = On (Включено), " +
                        "Use Offset (Геометрическое смещение) = Off (Выключено).");
            }
            finally { probe.Destroy(); }
        }
        catch (Exception ex)
        {

            AddStyleWarning("Не удалось проверить настройки будущего разреза", Tag.Null, ex);
        }
    }

    private static int NormalizedSymmetricTolerances;
    // NX multiplies this by the tolerance character height. Doubling the old
    // 0.2 increases the nominal-to-tolerance gap without spreading glyphs.
    // This is a layout choice, not a millimeter value prescribed by GOST.
    private const double ToleranceTextGapFactor = 0.4;

    private static bool IsSymmetricPair(double upper, double lower)
    {
        // Require exact equality of the stored magnitudes, not equality after
        // display rounding. Unequal engineering limits must never be collapsed.
        return !Double.IsNaN(upper) && !Double.IsNaN(lower) &&
            !Double.IsInfinity(upper) && !Double.IsInfinity(lower) &&
            upper > 0.0 && lower < 0.0 && upper == -lower;
    }

    private static void NormalizeSymmetricToleranceDefaults(DimensionStyleBuilder b)
    {
        // This builder stores defaults for both linear units and angles. Only
        // switch the shared presentation if every stored pair is symmetric.
        // Never enable tolerances where the current default is None.
        if (b.ToleranceType == ToleranceType.BilateralTwoLines &&
            IsSymmetricPair(b.UpperToleranceMetric, b.LowerToleranceMetric) &&
            IsSymmetricPair(b.UpperToleranceEnglish, b.LowerToleranceEnglish) &&
            IsSymmetricPair(b.UpperToleranceDegrees, b.LowerToleranceDegrees))
            b.ToleranceType = ToleranceType.BilateralOneLine;
    }

    private static void NormalizeSymmetricTolerance(Tag tag, string context)
    {
        // One-line bilateral means +/- the upper value in NX. Use it only
        // where both effective limits are already equal and opposite.
        Session.UndoMarkId localMark = default(Session.UndoMarkId);
        bool marked = false;
        try
        {
            Dimension dimension = NXOpen.Utilities.NXObjectManager.Get(tag) as Dimension;
            if (dimension == null || dimension.ToleranceType != ToleranceType.BilateralTwoLines) return;
            double upper = dimension.UpperToleranceValue;
            double lower = dimension.LowerToleranceValue;
            double upperMetric = dimension.UpperMetricToleranceValue;
            double lowerMetric = dimension.LowerMetricToleranceValue;
            if (!IsSymmetricPair(upper, lower) || !IsSymmetricPair(upperMetric, lowerMetric)) return;
            double nominal = dimension.ComputedSize;
            int nominalPlaces = dimension.NominalDecimalPlaces;
            int metricNominalPlaces = dimension.MetricNominalDecimalPlaces;
            int tolerancePlaces = dimension.ToleranceDecimalPlaces;
            int metricTolerancePlaces = dimension.MetricToleranceDecimalPlaces;

            localMark = S.SetUndoMark(Session.MarkVisibility.Invisible, "Симметричный допуск ЕСКД");
            marked = true;
            dimension.ToleranceType = ToleranceType.BilateralOneLine;
            Dimension fresh = NXOpen.Utilities.NXObjectManager.Get(tag) as Dimension;
            if (fresh == null || fresh.ToleranceType != ToleranceType.BilateralOneLine ||
                fresh.UpperToleranceValue != upper || fresh.UpperMetricToleranceValue != upperMetric ||
                fresh.ComputedSize != nominal || fresh.NominalDecimalPlaces != nominalPlaces ||
                fresh.MetricNominalDecimalPlaces != metricNominalPlaces ||
                fresh.ToleranceDecimalPlaces != tolerancePlaces ||
                fresh.MetricToleranceDecimalPlaces != metricTolerancePlaces)
                throw new InvalidOperationException("Не подтверждена запись через ± с сохранением номинала, пределов и точности.");
            NormalizedSymmetricTolerances++;

        }
        catch (Exception ex)
        {
            if (marked) S.UndoToMark(localMark, null);
            AddStyleWarning("Запись симметричного допуска: " + context, tag, ex);
        }
        finally { if (marked) S.DeleteUndoMark(localMark, null); }
    }

    private static bool DimensionSpacingMatches(LetteringStyleBuilder b)
    {
        return Math.Abs(b.DimensionTextCharSpaceFactor - CharacterSpacingFactor) < 0.0001 &&
            Math.Abs(b.AppendedTextCharSpaceFactor - CharacterSpacingFactor) < 0.0001 &&
            Math.Abs(b.ToleranceTextCharSpaceFactor - CharacterSpacingFactor) < 0.0001 &&
            Math.Abs(b.AppendedTextSpaceFactor - AppendedTextGapFactor) < 0.0001 &&
            Math.Abs(b.ToleranceTextSpaceFactor - ToleranceTextGapFactor) < 0.0001 &&
            Math.Abs(b.DimLineSpaceFactor - DimensionLineGap / MainTextHeight) < 0.0001;
    }

    // Native crosshatch parameters from uf_drf.h use zero-based indices:
    // mpr[11] angle, mpr[12] distance, mpr[13] boundary tolerance;
    // mpi[31] material, mpi[67] color, mpi[68] line width/density.
    private const double SectionHatchDistance = 2.0;
    private const int HatchDistanceIndex = 12;
    private static int StyledSectionHatches;
    private static bool HatchDefaultsVerified;
    private static readonly HashSet<Tag> ProcessedSectionHatches = new HashSet<Tag>();
    private static readonly List<SectionHatchTarget> SectionHatchTargets = new List<SectionHatchTarget>();

    private sealed class SectionHatchTarget
    {
        public Tag HatchTag;
        public Tag ViewTag;
        public string Context;
        public int ExpectedWidth;
    }

    private sealed class HatchSettingsSnapshot
    {
        public Tag HatchTag;
        public readonly int[] Integers = new int[100];
        public readonly double[] Reals = new double[70];
        public string RadiusText, DiameterText;
        public double Distance { get { return Reals[HatchDistanceIndex]; } }
    }

    private static bool IsCrosshatchObject(Tag tag)
    {
        if (!IsLiveTag(tag)) return false;
        int type, subtype;
        U.Obj.AskTypeAndSubtype(tag, out type, out subtype);
        return type == UFConstants.UF_drafting_entity_type &&
            subtype == UFConstants.UF_draft_crosshatch_subtype;
    }

    private static HatchSettingsSnapshot ReadSectionHatchSettings(Tag hatchTag)
    {
        // Input is the actual annotation tag, never a section solid or a
        // cutting-line segment. No HatchBuilder is opened for existing hatches.
        if (!IsCrosshatchObject(hatchTag))
            throw new InvalidOperationException("Объект не является штриховкой: " + hatchTag.ToString());
        HatchSettingsSnapshot value = new HatchSettingsSnapshot();
        value.HatchTag = hatchTag;
        U.Drf.AskObjectPreferences(hatchTag, value.Integers, value.Reals,
            out value.RadiusText, out value.DiameterText);
        if (Double.IsNaN(value.Distance) || Double.IsInfinity(value.Distance) || value.Distance <= 0.0)
            throw new InvalidOperationException("NX вернул недопустимый шаг штриховки.");
        return value;
    }

    private static bool SameHatchAppearance(HatchSettingsSnapshot a, HatchSettingsSnapshot b)
    {
        return a.Integers[31] == b.Integers[31] &&
            a.Integers[67] == b.Integers[67] &&
            Math.Abs(a.Reals[11] - b.Reals[11]) < 0.000001 &&
            Math.Abs(a.Reals[13] - b.Reals[13]) < 0.000001;
    }

    private static List<SectionHatchTarget> FindCurrentSectionHatches(string sheetName)
    {
        Dictionary<Tag, string> contexts = new Dictionary<Tag, string>();
        Dictionary<string, Tag> viewNames = new Dictionary<string, Tag>(StringComparer.OrdinalIgnoreCase);
        Dictionary<Tag, HashSet<Tag>> viewObjects = new Dictionary<Tag, HashSet<Tag>>();
        Dictionary<Tag, bool> hatchEnabled = new Dictionary<Tag, bool>();
        foreach (DraftingView view in Sheet.GetDraftingViews())
        {
            SectionView section = view as SectionView;
            if (section == null) continue;
            Tag viewTag = section.Tag;
            contexts[viewTag] = "Штриховка разреза " + section.Name + ", лист " + sheetName;
            viewNames[ViewKey(section.Name)] = viewTag;
            hatchEnabled[viewTag] = section.Style.Section.CrossHatch;
            HashSet<Tag> objects = new HashSet<Tag>();
            try
            {
                int visibleCount, clippedCount;
                Tag[] visible, clipped;
                U.View.AskVisibleObjects(viewTag, out visibleCount, out visible, out clippedCount, out clipped);
                if (visible != null) foreach (Tag tag in visible) objects.Add(tag);
                if (clipped != null) foreach (Tag tag in clipped) objects.Add(tag);
            }
            catch (Exception)
            {
                // View dependency is the primary owner test and remains usable.

            }
            viewObjects[viewTag] = objects;
        }

        List<SectionHatchTarget> result = new List<SectionHatchTarget>();
        Dictionary<Tag, int> matched = new Dictionary<Tag, int>();
        foreach (Tag view in contexts.Keys) matched[view] = 0;
        if (contexts.Count == 0) return result;
        // Cycle actual drafting entities. The old SX-solid-to-hatch route
        // failed in the user's NX 2606 with "Tag is not a section line segment".
        Tag candidate = Tag.Null;
        for (;;)
        {
            U.Obj.CycleObjsInPart(P.Tag, UFConstants.UF_drafting_entity_type, ref candidate);
            if (candidate == Tag.Null) break;
            try
            {
                if (!IsCrosshatchObject(candidate)) continue;
                Tag ownerView = Tag.Null;
                string ownerName = "";
                int dependent = 0;
                try { U.View.AskViewDependentStatus(candidate, out dependent, out ownerName); }
                catch (NXException)
                {  }
                if (dependent == 1 && !String.IsNullOrEmpty(ownerName))
                {
                    if (!viewNames.TryGetValue(ViewKey(ownerName), out ownerView))
                    {
                        Tag namedView;
                        U.View.AskTagOfViewName(ownerName, out namedView);
                        if (contexts.ContainsKey(namedView)) ownerView = namedView;
                    }
                }
                if (ownerView == Tag.Null)
                {
                    foreach (KeyValuePair<Tag, HashSet<Tag>> pair in viewObjects)
                        if (pair.Value.Contains(candidate))
                        {
                            if (ownerView != Tag.Null)
                                throw new InvalidOperationException("Штриховка найдена сразу в нескольких разрезах.");
                            ownerView = pair.Key;
                        }
                }

                if (ownerView == Tag.Null) continue;
                SectionHatchTarget target = new SectionHatchTarget();
                target.HatchTag = candidate; target.ViewTag = ownerView; target.Context = contexts[ownerView];
                result.Add(target); matched[ownerView]++;
            }
            catch (Exception ex)
            {

                AddStyleWarning("Поиск штриховки, лист " + sheetName, candidate, ex);
            }
        }
        foreach (Tag view in contexts.Keys)
        {

            if (hatchEnabled[view] && matched[view] == 0)
                Warnings.Add(contexts[view] + ": штриховка включена, но её объект не найден. Шаг существующей штриховки не подтверждён.");
        }
        return result;
    }

    private static void StyleCurrentSectionHatches(string sheetName)
    {
        List<SectionHatchTarget> targets;
        try { targets = FindCurrentSectionHatches(sheetName); }
        catch (Exception ex)
        {

            AddStyleWarning("Поиск штриховок, лист " + sheetName, Tag.Null, ex);
            return;
        }
        foreach (SectionHatchTarget target in targets)
        {
            if (ProcessedSectionHatches.Contains(target.HatchTag)) continue;
            Session.UndoMarkId localMark = default(Session.UndoMarkId);
            bool marked = false;
            string stage = "чтение параметров штриховки";
            try
            {
                HatchSettingsSnapshot before = ReadSectionHatchSettings(target.HatchTag);
                target.ExpectedWidth = new LineProfile(Sheet).NativeThin;
                if (Math.Abs(before.Distance - SectionHatchDistance) >= 0.000001 ||
                    before.Integers[68] != target.ExpectedWidth)
                {
                    stage = "запись шага штриховки";
                    localMark = S.SetUndoMark(Session.MarkVisibility.Invisible, "Шаг штриховки 2 мм");
                    marked = true;
                    double[] parameters = (double[])before.Reals.Clone();
                    int[] integers = (int[])before.Integers.Clone();
                    parameters[HatchDistanceIndex] = SectionHatchDistance;
                    integers[68] = target.ExpectedWidth;
                    // Change only distance and physical line width. Preserve
                    // material, angle, colors, boundary tolerance and geometry.
                    U.Drf.SetObjectPreferences(target.HatchTag, integers, parameters,
                        before.RadiusText, before.DiameterText);
                }
                stage = "проверка записанного шага";
                HatchSettingsSnapshot after = ReadSectionHatchSettings(target.HatchTag);
                if (!(Math.Abs(after.Distance - SectionHatchDistance) < 0.000001))
                    throw new InvalidOperationException("NX не подтвердил шаг штриховки 2 мм.");
                if (after.Integers[68] != target.ExpectedWidth)
                    throw new InvalidOperationException("NX не подтвердил тонкую линию штриховки.");
                if (!SameHatchAppearance(before, after))
                    throw new InvalidOperationException("Изменились угол, материал, цвет или точность границы штриховки.");
                ProcessedSectionHatches.Add(target.HatchTag);
                SectionHatchTargets.Add(target);

            }
            catch (Exception ex)
            {
                if (marked) S.UndoToMark(localMark, null);

                AddStyleWarning(target.Context + ", этап: " + stage, target.HatchTag, ex);
            }
            finally { if (marked) S.DeleteUndoMark(localMark, null); }
        }
    }

    private static void VerifyHatchDefaults(double savedDistance)
    {
        HatchDefaultsVerified = false;
        try
        {
            UFDrf.HatchFillPreferences native = new UFDrf.HatchFillPreferences();
            U.Drf.AskHatchFillPreferences(ref native);
            // An uncommitted creation builder checks the defaults seen by the
            // Crosshatch command. No test annotation or section is inserted.
            HatchBuilder probe = P.Annotations.Hatches.CreateHatchBuilder(null);
            double creationDistance;
            try { creationDistance = probe.HatchFillSettings.Distance; }
            finally { probe.Destroy(); }
            HatchDefaultsVerified = Math.Abs(savedDistance - SectionHatchDistance) < 0.000001 &&
                Math.Abs(native.hatch_distance - SectionHatchDistance) < 0.000001 &&
                Math.Abs(creationDistance - SectionHatchDistance) < 0.000001;

            if (!HatchDefaultsVerified)
                throw new InvalidOperationException("Не все настройки новой штриховки подтверждены как 2 мм. Проверьте шаг штриховки разреза.");
        }
        catch (Exception ex) { AddStyleWarning("Проверка шага новой штриховки", Tag.Null, ex); }
    }

    private static void AuditFinalSectionHatches()
    {
        // Read only after the final sheet refresh. Do not reopen sheets or
        // rebuild views after the new ESKD tables have been created.
        StyledSectionHatches = 0;
        foreach (SectionHatchTarget target in SectionHatchTargets)
        {
            try
            {
                HatchSettingsSnapshot value = ReadSectionHatchSettings(target.HatchTag);

                if (!(Math.Abs(value.Distance - SectionHatchDistance) < 0.000001))
                    throw new InvalidOperationException("После итогового обновления шаг штриховки отличается от 2 мм.");
                if (value.Integers[68] != target.ExpectedWidth)
                    throw new InvalidOperationException("После итогового обновления изменилась толщина штриховки.");
                StyledSectionHatches++;
            }
            catch (Exception ex)
            {

                AddStyleWarning(target.Context + ", итоговая проверка", target.HatchTag, ex);
            }
        }
    }

    private static void ApplyProjectDraftingPreferences()
    {
        NXOpen.Drafting.PreferencesBuilder b = P.SettingsManager.CreatePreferencesBuilder();
        try
        {
            ApplyLetteringStyle(b.AnnotationStyle.LetteringStyle);
            ApplyDimensionAppearance(b.AnnotationStyle);
            NormalizeSymmetricToleranceDefaults(b.AnnotationStyle.DimensionStyle);
            b.TableCellStyle.SlantAngle = 0.0;

            b.AnnotationStyle.HatchStyle.HatchDistance = SectionHatchDistance;
            b.AnnotationStyle.HatchStyle.LineWidth = new LineProfile(Sheet).ThinAnnotation;
            // GOST 2.306, 5: default 45 degrees. Existing 30/60-degree or
            // opposite-direction hatches remain valid and keep their angles.
            b.AnnotationStyle.HatchStyle.HatchAngle = 45.0;
            b.AnnotationStyle.SymbolStyle.DraftingSurfaceFinishStandard = SurfaceFinishStandard.Eskd;
            ApplySectionLabelStyle(b.ViewSectionLabel);
            ApplyViewLetterSizes(b.ViewLabel);
            ApplyViewLetterSizes(b.ViewProjectedLabel);
            ApplyDetailLetterSizes(b.ViewDetailLabel);
            b.ViewWorkflow.UseLineAntialiasing = true;

            // Match the supplied correct-section creation defaults. Do not
            // reintroduce Fine from the older recorded appearance journal.
            b.ViewStyle.ViewStyleGeneral.ViewQuality = SectionCreationQuality;
            b.ViewStyle.ViewStyleVisibleLines.VisibleWidth = new LineProfile(Sheet).Contour;
            b.ViewStyle.ViewStyleHiddenLines.Width = new LineProfile(Sheet).ThinView;
            ApplySectionArrowStyle(b.ViewStyle.ViewSectionLineStyleBuilder);

            // GOST 2.305-2008, 4.7: a section may show geometry behind the
            // cutting plane. Explicitly enable it for the requested full view.
            b.ViewStyle.ViewStyleSection.Background = true;
            b.Commit();
        }
        finally { b.Destroy(); }

        // Write through the direct per-part preference as well, after the
        // composite preferences builder has been destroyed. Existing views
        // have their own display state and are rebuilt separately below.
        P.ViewPreferences.Section.Background = true;

        // Set tabular-note creation defaults explicitly as well.
        UFTabnot.CellPrefs cp;
        U.Tabnot.AskDefaultCellPrefs(out cp);
        SetTableFont(ref cp);
        cp.text_height = StackedToleranceHeight;
        cp.line_space_factor = LineFactorForHeight(cp.text_height);
        try { U.Tabnot.SetDefaultCellPrefs(ref cp); }
        catch (NXException ex)
        {
            throw new InvalidOperationException("Настройки текста новых таблиц: " + ex.Message, ex);
        }
        VerifyDefaultTableFitMethods();

        // Exact display switch from Drawing settings.cs and the user's message.
        // It reveals existing object colors; it does not recolor model geometry.
        P.Preferences.ColorSettingVisualization.MonochromeDisplay = false;
        P.Preferences.LineVisualization.SetPixelWidths(new int[] { 1, 1, 1, 1, 2, 2, 3, 3, 3 });
    }

    private static double[] MeasureCalibrationText(Tag tag, string[] lines, double height,
        double characterFactor, double lineFactor, Session.UndoMarkId mark)
    {
        Note note = NXOpen.Utilities.NXObjectManager.Get(tag) as Note;
        if (note == null) throw new InvalidOperationException("NX не создал контрольную строку шрифта.");
        note.SetText(lines);
        using (LetteringPreferences lp = note.GetLetteringPreferences())
        {
            Lettering text = FontLettering(lp.GetGeneralText(), height);
            text.CharacterSpaceFactor = characterFactor;
            text.LineSpaceFactor = lineFactor;
            lp.SetGeneralText(text); lp.Angle = 0; lp.HorizTextJust = TextJustification.Left;
            note.SetLetteringPreferences(lp);
        }
        // SetText/SetLetteringPreferences edits must be updated before measuring.
        // The temporary NX note is deleted below; no diagnostic files are created.
        if (S.UpdateManager.DoUpdate(mark) != 0 || !IsLiveTag(tag))
            throw new InvalidOperationException("NX не обновил контрольный текст перед измерением.");
        double width, measuredHeight;
        U.Drf.AskAnnotationTextBox(tag, new double[3], out width, out measuredHeight);
        if (Double.IsNaN(width) || Double.IsInfinity(width) || width <= 0 ||
            Double.IsNaN(measuredHeight) || Double.IsInfinity(measuredHeight) || measuredHeight <= 0)
            throw new InvalidOperationException("NX вернул пустой габарит контрольного текста.");
        return new double[] { width, measuredHeight };
    }

    private static double MeasureAdvance(Tag tag, string glyph, double height,
        double factor, Session.UndoMarkId mark)
    {
        double one = MeasureCalibrationText(tag, new string[] { glyph }, height, factor, 1.0, mark)[0];
        double two = MeasureCalibrationText(tag, new string[] { glyph + glyph }, height, factor, 1.0, mark)[0];
        return two - one;
    }

    private static void CalibrateCharacterSpacing()
    {
        // Measured from the actual embedded type-A font: cap height=700 units,
        // H/Н advance=450 (7d glyph + 2d gap), M/М advance=550 (9d + 2d), d=50.
        // Subtracting single/double-glyph rectangles cancels italic overhang.
        // It is wrong to compare the horizontal bounding boxes of inclined glyphs:
        // those boxes can overlap even when the GOST inter-glyph gap is correct.
        Tag probe = Tag.Null;
        Session.UndoMarkId mark = S.SetUndoMark(Session.MarkVisibility.Invisible, "Интервал шрифта ГОСТ А");
        try
        {
            U.Drf.CreateNote(1, new string[] { "Н" }, new double[] { 25, 25, 0 }, 0, out probe);
            double atZero = MeasureAdvance(probe, "Н", MainTextHeight, 0.0, mark);
            double atOne = MeasureAdvance(probe, "Н", MainTextHeight, 1.0, mark);
            double target = MainTextHeight * 9.0 / 14.0;
            double factor;
            if (Math.Abs(atOne - target) <= 0.02) factor = 1.0;
            else
            {
                double slope = atOne - atZero;
                if (Math.Abs(slope) < 0.00001)
                    throw new InvalidOperationException("NX не меняет измеримый межбуквенный шаг.");
                factor = (target - atZero) / slope;
            }
            if (Double.IsNaN(factor) || Double.IsInfinity(factor) || factor < 0 || factor > 10)
                throw new InvalidOperationException("NX вернул недопустимый коэффициент межбуквенного интервала.");
            if (Math.Abs(MeasureAdvance(probe, "Н", MainTextHeight, factor, mark) - target) > 0.02 ||
                Math.Abs(MeasureAdvance(probe, "M", MainTextHeight, factor, mark) - MainTextHeight * 11.0 / 14.0) > 0.03 ||
                Math.Abs(MeasureAdvance(probe, "Н", StackedToleranceHeight, factor, mark) - StackedToleranceHeight * 9.0 / 14.0) > 0.02)
                throw new InvalidOperationException("Контрольные строки не подтвердили пропорциональный интервал для размеров 3,5 и 2,5 мм.");
            CharacterSpacingFactor = factor;
        }
        catch (Exception ex)
        {
            // Keep the font's documented standard spacing, never restore the
            // old zero spacing and never insert literal spaces into dimensions.
            CharacterSpacingFactor = 1.0;
            Warnings.Add("Установлен стандартный межбуквенный интервал шрифта (коэффициент 1). " +
                "Измерительная проверка NX не завершилась: " + ex.Message +
                " Проверьте просвет в М6: для высоты 3,5 мм номинальное значение по ГОСТ — 0,5 мм.");
        }
        finally
        {
            if (probe != Tag.Null && IsLiveTag(probe)) U.Obj.DeleteObject(probe);
            S.DeleteUndoMark(mark, null);
        }
    }

    private static void CalibrateGeneralLineSpacing()
    {
        Tag probe = Tag.Null;
        Session.UndoMarkId mark = S.SetUndoMark(Session.MarkVisibility.Invisible, "Шаг строк ГОСТ А");
        try
        {
            U.Drf.CreateNote(1, new string[] { "Н" }, new double[] { 25, 25, 0 }, 0, out probe);
            double h = MeasureCalibrationText(probe, new string[] { "Н" }, MainTextHeight, CharacterSpacingFactor, 1, mark)[1];
            double p1 = MeasureCalibrationText(probe, new string[] { "Н", "Н" }, MainTextHeight, CharacterSpacingFactor, 1, mark)[1] - h;
            double p2 = MeasureCalibrationText(probe, new string[] { "Н", "Н" }, MainTextHeight, CharacterSpacingFactor, 2, mark)[1] - h;
            if (p1 <= 0 || Math.Abs(p2 - p1) < 0.00001)
                throw new InvalidOperationException("NX не вернул измеримый шаг строк.");
            double factor = 1.0 + (5.5 - p1) / (p2 - p1);
            if (factor < 0 || factor > 10 || Double.IsNaN(factor) || Double.IsInfinity(factor))
                throw new InvalidOperationException("NX вернул недопустимый коэффициент шага строк.");
            double measured = MeasureCalibrationText(probe, new string[] { "Н", "Н" }, MainTextHeight, CharacterSpacingFactor, factor, mark)[1] - h;
            if (Math.Abs(measured - 5.5) > 0.02)
                throw new InvalidOperationException("Контрольный шаг строк отличается от 5,5 мм.");
            GeneralLineFactor = factor;
            LinePitchBaseRatio = (2.0 * p1 - p2) / MainTextHeight;
            LinePitchSlopeRatio = (p2 - p1) / MainTextHeight;
        }
        catch (Exception ex)
        {
            GeneralLineFactor = 1.0;
            Warnings.Add("Не удалось автоматически подтвердить шаг строк 5,5 мм: " + ex.Message +
                " Проверьте межстрочный интервал технических требований.");
        }
        finally
        {
            if (probe != Tag.Null && IsLiveTag(probe)) U.Obj.DeleteObject(probe);
            S.DeleteUndoMark(mark, null);
        }
    }

    private static string NormalizeInlineFont(string text)
    {
        if (String.IsNullOrEmpty(text)) return text;
        // Replace only NX's explicit numeric font token, leaving attributes,
        // dimension control codes, symbols, wording and numerical values intact.
        return System.Text.RegularExpressions.Regex.Replace(text, @"<F[0-9]+>",
            "<F" + FontIndex.ToString(CultureInfo.InvariantCulture) + ">",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static void NormalizeNoteFonts(BaseNote note)
    {
        string[] lines = note.GetText();
        bool changed = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string replacement = NormalizeInlineFont(lines[i]);
            if (replacement != lines[i]) { lines[i] = replacement; changed = true; }
        }
        if (changed) note.SetText(lines);
    }

    private static void SetTableFont(ref UFTabnot.CellPrefs cp)
    {
        cp.text_font = FontIndex;
        cp.text_aspect_ratio = 1.0;
        cp.text_slant = 0.0;
        cp.is_italic = false;
        cp.char_space_factor = CharacterSpacingFactor;
        double[] standard = new double[] { 2.5, 3.5, 5, 7, 10, 14, 20, 28, 40 };
        foreach (double height in standard)
            if (cp.text_height <= height + 0.001) { cp.text_height = height; break; }
        cp.line_space_factor = LineFactorForHeight(cp.text_height);
        NormalizeTableFitMethods(ref cp);
        cp.text_density = 3;
        cp.text_color = 216;
        // Keep the table's rotation, grid and contents. Do not silently shrink
        // below type-A sizes or remove spaces to make a crowded cell fit.
    }

    private static void StyleExistingTable(Tag table)
    {
        int nr, nc;
        U.Tabnot.AskNmRows(table, out nr);
        U.Tabnot.AskNmColumns(table, out nc);
        for (int r = 0; r < nr; r++)
        {
            Tag row; U.Tabnot.AskNthRow(table, r, out row);
            for (int c = 0; c < nc; c++)
            {
                Tag column, cell;
                U.Tabnot.AskNthColumn(table, c, out column);
                U.Tabnot.AskCellAtRowCol(row, column, out cell);
                UFTabnot.CellPrefs cp; U.Tabnot.AskCellPrefs(cell, out cp);
                SetTableFont(ref cp);
                U.Tabnot.SetCellPrefs(cell, ref cp);
                if (!cp.is_a_formula && cp.referenced_spreadsheet == Tag.Null && !cp.is_protected)
                {
                    string text; U.Tabnot.AskCellText(cell, out text);
                    string normalized = NormalizeInlineFont(text);
                    if (normalized != text) U.Tabnot.SetCellText(cell, normalized);
                }
            }
        }
        U.Tabnot.Update(table);
    }

    private static void AddStyleWarning(string context, Tag tag, Exception ex)
    {
        // Never read a managed object's Tag or Name while handling its failure.
        string item = context + " (tag " + tag.ToString() + "): ";
        Warnings.Add(item + ex.Message);

    }

    private static List<Tag> CurrentAnnotationTags()
    {
        // Opening a sheet and editing labels can delete/regenerate annotations.
        // Collect AFTER those operations, and keep native tag values only.
        List<Tag> result = new List<Tag>();
        Tag tag = Tag.Null;
        while ((tag = U.Obj.CycleAll(P.Tag, tag)) != Tag.Null)
        {
            if (!IsLiveTag(tag)) continue;
            try
            {
                Annotation a = NXOpen.Utilities.NXObjectManager.Get(tag) as Annotation;
                if (a != null && !(a is TableSection) && !a.IsBlanked && OnSheet(a)) result.Add(tag);
            }
            catch (NXException) { } // Temporary/non-wrapped UF objects are not candidates.
        }
        return result;
    }

    private static void StyleCurrentViewLabels(HashSet<Tag> processed, string sheetName)
    {
        List<Tag> viewTags = new List<Tag>();
        foreach (DraftingView view in Sheet.GetDraftingViews())
        {
            try { viewTags.Add(view.Tag); }
            catch (NXException ex) { AddStyleWarning("Список видов, лист " + sheetName, Tag.Null, ex); }
        }
        foreach (Tag viewTag in viewTags)
        {
            string context = "Подпись вида, лист " + sheetName;
            try
            {
                if (!IsLiveTag(viewTag)) continue;
                DraftingView view = NXOpen.Utilities.NXObjectManager.Get(viewTag) as DraftingView;
                if (view == null) continue;
                context = "Подпись вида " + view.Name + ", лист " + sheetName;
                bool isSection = view is SectionView;
                Tag label;
                U.Draw.AskViewLabel(viewTag, out label);
                if (!IsLiveTag(label)) continue;
                processed.Add(label); // Exclude generated labels from the generic note editor.
                DisplayableObject obj = NXOpen.Utilities.NXObjectManager.Get(label) as DisplayableObject;
                if (obj == null || obj.IsBlanked) continue;
                EditViewLabelSettingsBuilder b = P.SettingsManager.CreateDrawingEditViewLabelSettingsBuilder(
                    new DisplayableObject[] { obj });
                try
                {
                    ApplyLetteringStyle(b.AnnotationStyle.LetteringStyle);
                    if (isSection) ApplySectionLabelStyle(b.ViewSectionLabel);
                    else if (view is DetailView) ApplyDetailLetterSizes(b.ViewDetailLabel);
                    else if (view is ProjectedView) ApplyViewLetterSizes(b.ViewProjectedLabel);
                    else ApplyViewLetterSizes(b.ViewLabel);
                    b.Commit();
                }
                finally { b.Destroy(); }

                // A regenerated label can have a new tag. Ask its parent view
                // again; do not dereference obj or view after the commit.
                Tag updatedLabel;
                U.Draw.AskViewLabel(viewTag, out updatedLabel);
                if (!IsLiveTag(updatedLabel))
                    throw new InvalidOperationException("Не найдена подпись вида после обновления.");
                processed.Add(updatedLabel);
                if (isSection)
                {
                    DisplayableObject fresh = NXOpen.Utilities.NXObjectManager.Get(updatedLabel) as DisplayableObject;
                    if (fresh == null) throw new InvalidOperationException("Не удалось перечитать подпись разреза.");
                    EditViewLabelSettingsBuilder check = P.SettingsManager.CreateDrawingEditViewLabelSettingsBuilder(
                        new DisplayableObject[] { fresh });
                    try
                    {
                        if (!SectionLabelStyleMatches(check.ViewSectionLabel))
                            throw new InvalidOperationException("NX не подтвердил формат А-А без префикса SECTION.");
                    }
                    finally { check.Destroy(); }
                    StyledSectionLabels++;
                }
                StyledViewLabels++;
            }
            catch (Exception ex) { AddStyleWarning(context, viewTag, ex); }
        }
    }

    private static int ApplyStylesToAllDrawingSheets()
    {
        Tag originalTag = Sheet.Tag;
        List<Tag> sheetTags = new List<Tag>();
        foreach (DrawingSheet current in P.DrawingSheets.ToArray()) sheetTags.Add(current.Tag);
        HashSet<Tag> processed = new HashSet<Tag>();
        HashSet<Tag> tables = new HashSet<Tag>();
        int count = 0;
        StyledSheets = 0; StyledTables = 0; StyledViewLabels = 0;
        StyledDimensions = 0; StyledSectionLabels = 0;
        try
        {
            foreach (Tag sheetTag in sheetTags)
            {
                if (!IsLiveTag(sheetTag)) continue;
                DrawingSheet current = NXOpen.Utilities.NXObjectManager.Get(sheetTag) as DrawingSheet;
                if (current == null) continue;
                string sheetName = current.Name;
                if (current.Units != DrawingSheet.Unit.Millimeters)
                {
                    Warnings.Add("Пропущен немиллиметровый лист: " + sheetName + ".");
                    continue;
                }
                current.Open();
                Sheet = (DrawingSheet)NXOpen.Utilities.NXObjectManager.Get(sheetTag);
                ApplyFirstAngleProjection(sheetName);
                CollectSheetObjects();
                ApplyCurrentViewLineWidths(sheetName);
                UpdateCurrentSectionGeometry(sheetName);
                CollectSheetObjects();
                StyleCurrentViewLabels(processed, sheetName);
                CollectSheetObjects(); // Refresh membership after generated labels change.

                foreach (Tag tag in CurrentAnnotationTags())
                {
                    string context = "Аннотация, лист " + sheetName;
                    try
                    {
                        // In v1.12 even a.Tag in IsLiveTag(a.Tag) could throw:
                        // a was a cached, already invalid managed object.
                        if (!IsLiveTag(tag) || processed.Contains(tag)) continue;
                        Annotation a = NXOpen.Utilities.NXObjectManager.Get(tag) as Annotation;
                        if (a == null || a is TableSection || a.IsBlanked || !OnSheet(a)) continue;
                        // General roughness has an enlarged main symbol (GOST 2.309,
                        // 2.6). Keep its dedicated height on the other sheets.
                        if (a.HasUserAttribute(GeneralRoughnessAttribute, NXObject.AttributeType.String, -1)) continue;
                        context = a.GetType().Name + ", лист " + sheetName;
                        bool isDimension = a is Dimension;
                        if (a is Centerline)
                        {
                            SetNativeThinLine(tag, 66);
                            processed.Add(tag); count++;
                            continue;
                        }
                        Tag updatedTag = tag;
                        EditSettingsBuilder b = P.SettingsManager.CreateAnnotationEditSettingsBuilder(
                            new DisplayableObject[] { a });
                        try
                        {
                            ApplyLetteringStyle(b.AnnotationStyle.LetteringStyle);
                            ApplyAnnotationLineWidths(b.AnnotationStyle);
                            if (isDimension) ApplyDimensionAppearance(b.AnnotationStyle, true);
                            NXObject committed = b.Commit();
                            if (committed is Annotation) updatedTag = committed.Tag;
                        }
                        finally { b.Destroy(); }

                        // Resolve afresh after Commit/Destroy. Never use a again.
                        if (!IsLiveTag(updatedTag))
                            throw new InvalidOperationException("Объект изменился при обновлении; оформление не подтверждено.");
                        Annotation fresh = NXOpen.Utilities.NXObjectManager.Get(updatedTag) as Annotation;
                        if (fresh == null) throw new InvalidOperationException("Не удалось перечитать аннотацию.");
                        if (isDimension)
                        {
                            EditSettingsBuilder check = P.SettingsManager.CreateAnnotationEditSettingsBuilder(
                                new DisplayableObject[] { fresh });
                            try
                            {
                                if (!DimensionArrowStyleMatches(check.AnnotationStyle.LineArrowStyle, true))
                                    throw new InvalidOperationException("NX не подтвердил настройки размерных стрелок.");
                                if (!DimensionSpacingMatches(check.AnnotationStyle.LetteringStyle))
                                    throw new InvalidOperationException("NX не подтвердил межбуквенный интервал или отступы префикса и отклонений.");
                                if (!DimensionPrecisionMatches(check.AnnotationStyle))
                                    throw new InvalidOperationException("NX не подтвердил три знака после запятой у размера.");
                            }
                            finally { check.Destroy(); }
                            StyledDimensions++;
                            NormalizeSymmetricTolerance(updatedTag, context);
                        }
                        else
                        {
                            BaseNote note = fresh as BaseNote;
                            if (note != null) NormalizeNoteFonts(note);
                        }
                        processed.Add(tag); processed.Add(updatedTag);
                        count++;
                    }
                    catch (Exception ex) { AddStyleWarning(context, tag, ex); }
                }
                foreach (ExistingTable table in ExistingTables())
                {
                    try
                    {
                        if (!IsLiveTag(table.Tag) || HiddenTemplateObjects.Contains(table.Tag) || tables.Contains(table.Tag)) continue;
                        bool shown = false;
                        foreach (Tag section in table.Sections)
                        {
                            if (!IsLiveTag(section)) continue;
                            DisplayableObject obj = NXOpen.Utilities.NXObjectManager.Get(section) as DisplayableObject;
                            if (obj != null && !obj.IsBlanked) shown = true;
                        }
                        if (!shown) continue;
                        StyleExistingTable(table.Tag);
                        tables.Add(table.Tag); StyledTables++;
                    }
                    catch (Exception ex) { AddStyleWarning("Таблица, лист " + sheetName, table.Tag, ex); }
                }
                StyleCurrentSectionHatches(sheetName);
                PositionCurrentSectionLetters(sheetName);
                if (sheetTag == TechnicalRequirementsSheet)
                {
                    try { BuildTechnicalRequirements(); }
                    catch (Exception ex)
                    { throw new InvalidOperationException("Технические требования на листе " + sheetName + ": " + ex.Message, ex); }
                }
                StyledSheets++;
            }
        }
        finally
        {
            Sheet = (DrawingSheet)NXOpen.Utilities.NXObjectManager.Get(originalTag);
            Sheet.Open();
            Sheet = (DrawingSheet)NXOpen.Utilities.NXObjectManager.Get(originalTag);
            CollectSheetObjects();
        }
        return count;
    }

    private static void VerifyPartDefaults()
    {
        bool savedSectionBackground = false;
        double savedHatchDistance = Double.NaN;
        NXOpen.Preferences.GeneralViewQualityOption savedViewQuality =
            default(NXOpen.Preferences.GeneralViewQualityOption);
        NXOpen.Drafting.PreferencesBuilder b = P.SettingsManager.CreatePreferencesBuilder();
        try
        {
            LetteringStyleBuilder l = b.AnnotationStyle.LetteringStyle;
            if (l.GeneralTextFont != FontIndex || l.DimensionTextFont != FontIndex ||
                l.AppendedTextFont != FontIndex || l.ToleranceTextFont != FontIndex ||
                Math.Abs(l.GeneralStandardTextCharacterSpaceFactor - CharacterSpacingFactor) > 0.0001 ||
                Math.Abs(l.DimensionStandardTextCharacterSpaceFactor - CharacterSpacingFactor) > 0.0001 ||
                Math.Abs(l.AppendedStandardTextCharacterSpaceFactor - CharacterSpacingFactor) > 0.0001 ||
                Math.Abs(l.ToleranceStandardTextCharacterSpaceFactor - CharacterSpacingFactor) > 0.0001 ||
                Math.Abs(l.DimensionTextSize - MainTextHeight) > 0.001 ||
                Math.Abs(l.TwoLineToleranceTextSize - StackedToleranceHeight) > 0.001 ||
                Math.Abs(l.ToleranceTextSize - MainTextHeight) > 0.001 ||
                !DimensionSpacingMatches(l))
                throw new InvalidOperationException("NX не сохранил настройки новых аннотаций в текущей детали.");
            if (!DimensionArrowStyleMatches(b.AnnotationStyle.LineArrowStyle))
                throw new InvalidOperationException("NX не сохранил настройки стрелок для новых размеров.");
            if (!DimensionPrecisionMatches(b.AnnotationStyle))
                throw new InvalidOperationException("NX не сохранил Decimal Places (Знаков после запятой) = 3 для новых размеров.");
            if (!SectionLabelStyleMatches(b.ViewSectionLabel))
                throw new InvalidOperationException("NX не сохранил подпись новых разрезов А-А без SECTION.");
            savedHatchDistance = b.AnnotationStyle.HatchStyle.HatchDistance;
            savedSectionBackground = b.ViewStyle.ViewStyleSection.Background;
            savedViewQuality = b.ViewStyle.ViewStyleGeneral.ViewQuality;

            // A section-style mismatch is reported with actual values. It
            // must not undo already formatted dimensions, labels and tables.
            try
            {
                SectionDefaultsVerified = CheckSectionArrowStyle(b.ViewStyle.ViewSectionLineStyleBuilder,
                    "Настройки новых линий разрезов текущего .prt");
            }
            catch (Exception ex)
            {
                SectionDefaultsVerified = false;

                AddStyleWarning("Проверка настроек новых линий разрезов", Tag.Null, ex);
            }
        }
        finally { b.Destroy(); }
        VerifyNewSectionDefaults(savedSectionBackground, savedViewQuality);
        VerifyHatchDefaults(savedHatchDistance);
        if (P.Preferences.ColorSettingVisualization.MonochromeDisplay)
            throw new InvalidOperationException("NX не отключил Monochrome Display (Монохромное отображение).");
    }

    private static void Line(double x1, double y1, double x2, double y2)
    {
        NXOpen.Line line = P.Curves.CreateLine(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0));
        line.Color = 216;
        line.LineFont = DisplayableObject.ObjectFont.Solid;
        line.LineWidth = new LineProfile(Sheet).ContourObject;
        int dependent; string viewName;
        U.View.AskViewDependentStatus(line.Tag, out dependent, out viewName);
        if (dependent == 0) U.View.ConvertToView(Sheet.View.Tag, line.Tag);
        Mark(line.Tag, "L");
    }

    // GOST R 2.316-2023, 6.2, 6.6, 6.9, 6.11; GOST R 2.105-2019, 5.1.4.
    // Engineering text is an editable template, not a universal GOST prescription.
    private sealed class DrawingExtras
    {
        public bool RequirementsEnabled, RoughnessEnabled, RoughnessExceptions;
        public string RoughnessRa;
        public string[] Requirements;
    }

    private static Tag FindTechnicalRequirementsSheet(Fields fields)
    {
        if (!Extras.RequirementsEnabled) return Tag.Null;
        int count;
        if (Int32.TryParse(fields.SheetCount, out count) && count <= 1) return Sheet.Tag;
        if (fields.SheetNumber == "1") return Sheet.Tag;
        DrawingSheet[] sheets = P.DrawingSheets.ToArray();
        if (sheets.Length == 1) return Sheet.Tag;
        foreach (DrawingSheet candidate in sheets)
        {
            string number = ReadAttribute(candidate, "NX_ESKD_FIELD_8", "");
            if (number.Length == 0)
            {
                DraftingDrawingSheet ds = candidate as DraftingDrawingSheet;
                if (ds == null) continue;
                DraftingDrawingSheetBuilder b = P.DraftingDrawingSheets.CreateDraftingDrawingSheetBuilder(ds);
                try { number = b.Number; } finally { b.Destroy(); }
            }
            if (number.Trim() == "1")
            {
                if (candidate.Units != DrawingSheet.Unit.Millimeters)
                    throw new InvalidOperationException("Первый лист для технических требований должен быть в миллиметрах.");
                return candidate.Tag;
            }
        }
        throw new InvalidOperationException("Для технических требований не найден лист с номером 1. " +
            "Задайте номер первого листа в Sheet (Лист) или укажите один лист в основной надписи, если это отдельный чертёж.");
    }

    private static Note FindRequirementsNote()
    {
        List<Note> owned = new List<Note>(), candidates = new List<Note>();
        foreach (BaseNote item in P.Notes.ToArray())
        {
            Note note = item as Note;
            if (note == null || note.IsBlanked || !OnSheet(note)) continue;
            if (note.HasUserAttribute(TechnicalRequirementsAttribute, NXObject.AttributeType.String, -1))
            { owned.Add(note); continue; }
            if (note.HasAssociativeOrigin || !IsSheetLayoutObject(note.Tag)) continue;
            string value = String.Join("\n", note.GetText());
            bool numbered = System.Text.RegularExpressions.Regex.IsMatch(value, @"(?m)^\s*(?:<[^>]+>)*1[.)]\s+");
            bool requirements = System.Text.RegularExpressions.Regex.IsMatch(value,
                "неуказан|заусен|изготов|выполнить|шероховат|гравиров|технические требования",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (numbered && requirements && note.AnnotationOrigin.Y > 60 && note.AnnotationOrigin.Y < Sheet.Height * 0.65)
                candidates.Add(note);
        }
        if (owned.Count > 1 || (owned.Count == 0 && candidates.Count > 1))
            throw new InvalidOperationException("На первом листе найдено несколько блоков технических требований. " +
                "Объедините их в одну заметку перед запуском, чтобы не потерять пункты.");
        return owned.Count == 1 ? owned[0] : candidates.Count == 1 ? candidates[0] : null;
    }

    private static Note UpdateRequirementsNote(Tag tag, Session.UndoMarkId updateMark)
    {
        // Siemens documents that SimpleDraftingAid edits are not applied until
        // Update.DoUpdate. Reading a text rectangle immediately after SetText
        // returned the previous width in v1.24 (and a zero space-width delta).
        // No display-only regeneration or file write can replace this update.
        int errors = S.UpdateManager.DoUpdate(updateMark);
        if (errors != 0)
            throw new InvalidOperationException("NX сообщил ошибки при обновлении текста технических требований: " +
                errors.ToString(CultureInfo.InvariantCulture) + ".");
        // Updating annotations can invalidate a cached managed handle.
        Note updated = IsLiveTag(tag) ? NXOpen.Utilities.NXObjectManager.Get(tag) as Note : null;
        if (updated == null)
            throw new InvalidOperationException("После обновления NX не найдена заметка технических требований.");
        return updated;
    }

    private static double NoteWidth(ref Note note, string value, Session.UndoMarkId updateMark)
    {
        Tag tag = note.Tag;
        note.SetText(new string[] { value });
        note = UpdateRequirementsNote(tag, updateMark);
        double width, height;
        U.Drf.AskAnnotationTextBox(tag, new double[3], out width, out height);
        if (Double.IsNaN(width) || Double.IsInfinity(width) || width <= 0)
            throw new InvalidOperationException("NX не вернул ширину текста технических требований.");
        return width;
    }

    private static void BuildTechnicalRequirements()
    {
        // One local mark for all TT text updates. The existing visible mark
        // still undoes the whole script; no extra user-visible Undo steps.
        Session.UndoMarkId updateMark = S.SetUndoMark(Session.MarkVisibility.Invisible, "Обновление технических требований");
        try { BuildTechnicalRequirementsCore(updateMark); }
        finally { S.DeleteUndoMark(updateMark, null); }
    }

    private static void BuildTechnicalRequirementsCore(Session.UndoMarkId updateMark)
    {
        Note previous = FindRequirementsNote();
        // The requested template is authoritative for both new and existing TT.
        // Edit the same note so rerunning does not add a second block.
        string[] paragraphs = Extras.Requirements;
        if (paragraphs.Length == 0)
        {
            if (previous != null && ReadAttribute(previous, TechnicalRequirementsAttribute, "preserve") == "template")
                U.Obj.DeleteObject(previous.Tag);
            return;
        }
        // A single native editable note; measurement does not create any files.
        DraftingNoteBuilder builder = P.Annotations.CreateDraftingNoteBuilder(previous);
        Tag noteTag;
        try
        {
            ApplyLetteringStyle(builder.Style.LetteringStyle);
            builder.Style.LetteringStyle.HorizontalTextJustification = TextJustification.Left;
            builder.TextAlignment = DraftingNoteBuilder.TextAlign.Top;
            builder.VerticalText = false;
            builder.Origin.Plane.PlaneMethod = PlaneBuilder.PlaneMethodType.XyPlane;
            builder.Origin.SetInferRelativeToGeometry(false);
            builder.Origin.Anchor = OriginBuilder.AlignmentPosition.TopLeft;
            builder.Origin.OriginPoint = new Point3d(Sheet.Length - 187, 100, 0);
            builder.Text.SetEditorText(new string[] { "НН" });
            NXObject made = builder.Commit();
            noteTag = made != null ? made.Tag : previous != null ? previous.Tag : Tag.Null;
        }
        finally { builder.Destroy(); }
        Note note = IsLiveTag(noteTag) ? NXOpen.Utilities.NXObjectManager.Get(noteTag) as Note : null;
        if (note == null) throw new InvalidOperationException("NX не создал заметку технических требований.");
        using (LetteringPreferences lp = note.GetLetteringPreferences())
        {
            Lettering text = FontLettering(lp.GetGeneralText(), MainTextHeight);
            text.LineSpaceFactor = GeneralLineFactor;
            lp.SetGeneralText(text); lp.Angle = 0; lp.HorizTextJust = TextJustification.Left;
            note.SetLetteringPreferences(lp);
        }
        double plain = NoteWidth(ref note, "НН", updateMark);
        double space = (NoteWidth(ref note, "Н          Н", updateMark) - plain) / 10.0;
        if (!(space > 0.05 && space < 5.0))
            throw new InvalidOperationException("Не удалось измерить абзацный отступ технических требований.");
        int count = (int)Math.Round(15.0 / space);
        if (count * space < 12.5 || count * space > 17.0)
            throw new InvalidOperationException("Абзацный отступ не попадает в диапазон 12,5–17 мм.");
        string indent = new String(' ', count);
        List<string> lines = new List<string>();
        bool needsHeading = false;
        foreach (BaseNote other in P.Notes.ToArray())
            if (other.Tag != noteTag && !other.IsBlanked && OnSheet(other) &&
                String.Join(" ", other.GetText()).IndexOf("Техническая характеристика", StringComparison.OrdinalIgnoreCase) >= 0)
                needsHeading = true;
        if (needsHeading) lines.Add("Технические требования");
        const double columnWidth = 179.0; // 185 mm title block minus 3 mm on each side.
        for (int i = 0; i < paragraphs.Length; i++)
        {
            string line = indent + (i + 1).ToString(CultureInfo.InvariantCulture) + ".";
            string[] words = NormalizeTitleText(paragraphs[i]).Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string word in words)
            {
                string proposed = line.Length == 0 ? word : line + " " + word;
                if (NoteWidth(ref note, proposed, updateMark) <= columnWidth) { line = proposed; continue; }
                if (line.Length > 0) lines.Add(line);
                if (NoteWidth(ref note, word, updateMark) > columnWidth)
                    throw new InvalidOperationException("В технических требованиях есть слово шире колонки 179 мм: " + word);
                line = word;
            }
            if (line.Length > 0) lines.Add(line);
        }
        note.SetText(lines.ToArray());
        note = UpdateRequirementsNote(noteTag, updateMark);
        double[] upperLeft = new double[3]; double length, height;
        U.Drf.AskAnnotationTextBox(noteTag, upperLeft, out length, out height);
        if (!(height > 0) || length > columnWidth + 0.1 || height + 80.0 > Sheet.Height - 20.0)
            throw new InvalidOperationException("Технические требования не помещаются над основной надписью. Сократите блок в INI.");
        // Leave 20 mm above the 55 mm title block for spacing/change-table rows.
        Point3d origin = note.AnnotationOrigin;
        note.AnnotationOrigin = new Point3d(origin.X + Sheet.Length - 187.0 - upperLeft[0],
            origin.Y + 80.0 + height - upperLeft[1], origin.Z);
        note = UpdateRequirementsNote(noteTag, updateMark);
        int dependent; string viewName;
        U.View.AskViewDependentStatus(noteTag, out dependent, out viewName);
        if (dependent == 0) U.View.ConvertToView(Sheet.View.Tag, noteTag);
        note.SetUserAttribute(TechnicalRequirementsAttribute, -1, "template", Update.Option.Now);
        note.SetName("ESKD_TECH_REQUIREMENTS");
        note.Unblank();
        // These are document contents, not title-frame objects. Their own
        // attribute protects them during the subsequent template cleanup.
    }

    private static DraftingSurfaceFinish NewRoughness(string value, bool companion)
    {
        DraftingSurfaceFinishBuilder b = P.Annotations.DraftingSurfaceFinishSymbols.CreateDraftingSurfaceFinishBuilder(null);
        Tag tag;
        try
        {
            ApplyLetteringStyle(b.Style.LetteringStyle);
            b.Style.LetteringStyle.GeneralTextSize = companion ? MainTextHeight : 5.0;
            b.Style.SymbolStyle.DraftingSurfaceFinishStandard = SurfaceFinishStandard.Eskd;
            b.Style.SymbolStyle.SurfaceFinishColor = P.Colors.Find(216);
            b.Style.SymbolStyle.SurfaceFinishFont = DisplayableObject.ObjectFont.Solid;
            LineProfile widths = new LineProfile(Sheet);
            b.Style.SymbolStyle.SurfaceFinishWidth = companion ? widths.ThinAnnotation : widths.GeneralRoughness;
            b.Finish = companion ? DraftingSurfaceFinishBuilder.FinishType.Basic : DraftingSurfaceFinishBuilder.FinishType.Modifier;
            b.Parentheses = companion ? BaseSurfaceFinishBuilder.ParenthesesType.Both : BaseSurfaceFinishBuilder.ParenthesesType.None;
            b.A1 = value; b.A2 = ""; b.B = ""; b.C = ""; b.D = ""; b.E = ""; b.F1 = ""; b.F2 = "";
            b.SingleRoughnessValue = true;
            b.Angle = 0; b.InvertSymbol = false; b.InvertText = false;
            b.Leader.Leaders.Clear();
            b.Origin.Plane.PlaneMethod = PlaneBuilder.PlaneMethodType.XyPlane;
            b.Origin.SetInferRelativeToGeometry(false);
            b.Origin.Anchor = OriginBuilder.AlignmentPosition.TopLeft;
            b.Origin.OriginPoint = new Point3d(Sheet.Length - 65, Sheet.Height - 25, 0);
            NXObject created = b.Commit();
            tag = created == null ? Tag.Null : created.Tag;
        }
        finally { b.Destroy(); }
        DraftingSurfaceFinish result = IsLiveTag(tag) ? NXOpen.Utilities.NXObjectManager.Get(tag) as DraftingSurfaceFinish : null;
        if (result == null) throw new InvalidOperationException("NX не создал обозначение общей шероховатости.");
        result.SetUserAttribute(GeneralRoughnessAttribute, -1, companion ? "exceptions" : "Ra", Update.Option.Now);
        Mark(tag, companion ? "R_OTHER_" : "R_MAIN_");
        int dependent; string viewName;
        U.View.AskViewDependentStatus(tag, out dependent, out viewName);
        if (dependent == 0) U.View.ConvertToView(Sheet.View.Tag, tag);
        result.Unblank();
        return result;
    }

    private static double[] RoughnessBounds(DraftingSurfaceFinish symbol)
    {
        // UF_MODL_ask_bounding_box supports wireframe/solid objects, not
        // annotations. Query the native annotation layout rectangle instead.
        double[] upperLeft = new double[3]; double length, height;
        U.Drf.AskAnnotationTextBox(symbol.Tag, upperLeft, out length, out height);
        double[] bounds = new double[] { upperLeft[0], upperLeft[1] - height, upperLeft[2],
            upperLeft[0] + length, upperLeft[1], upperLeft[2] };
        for (int i = 0; i < bounds.Length; i++)
            if (Double.IsNaN(bounds[i]) || Double.IsInfinity(bounds[i]))
                throw new InvalidOperationException("Не удалось измерить обозначение общей шероховатости.");
        if (bounds[3] <= bounds[0] || bounds[4] <= bounds[1])
            throw new InvalidOperationException("NX вернул пустой габарит обозначения шероховатости.");
        return bounds;
    }

    private static void MoveRoughness(DraftingSurfaceFinish symbol, double dx, double dy)
    {
        Point3d origin = symbol.AnnotationOrigin;
        symbol.AnnotationOrigin = new Point3d(origin.X + dx, origin.Y + dy, origin.Z);
    }

    private static void BuildGeneralRoughness()
    {
        if (!Extras.RoughnessEnabled) return;
        // Replace an existing unattached sheet-level general symbol, keeping
        // surface-specific symbols belonging to drawing views untouched.
        foreach (DraftingSurfaceFinish old in P.Annotations.DraftingSurfaceFinishSymbols.ToArray())
        {
            if (old.IsBlanked || !OnSheet(old) || !IsSheetLayoutObject(old.Tag) || old.HasAssociativeOrigin) continue;
            Point3d p = old.AnnotationOrigin;
            if (p.X < Sheet.Length - 90 || p.Y < Sheet.Height - 45) continue;
            DraftingSurfaceFinishBuilder check = P.Annotations.DraftingSurfaceFinishSymbols.CreateDraftingSurfaceFinishBuilder(old);
            bool noLeader;
            try { noLeader = check.Leader.Leaders.Length == 0; } finally { check.Destroy(); }
            if (noLeader) old.Blank();
        }
        DraftingSurfaceFinish main = NewRoughness("Ra " + Extras.RoughnessRa, false);
        double[] a = RoughnessBounds(main);
        DraftingSurfaceFinish companion = null;
        if (Extras.RoughnessExceptions)
        {
            companion = NewRoughness("", true);
            double[] c = RoughnessBounds(companion);
            MoveRoughness(companion, a[3] + 3.0 - c[0], a[1] - c[1]);
        }
        double right = a[3], top = a[4];
        if (companion != null)
        {
            double[] c = RoughnessBounds(companion);
            right = Math.Max(right, c[3]); top = Math.Max(top, c[4]);
        }
        // Figure 12 of GOST 2.309-73: 5-10 mm from the INNER drawing frame.
        double dx = Sheet.Length - 5.0 - 7.5 - right;
        double dy = Sheet.Height - 5.0 - 7.5 - top;
        MoveRoughness(main, dx, dy);
        if (companion != null) MoveRoughness(companion, dx, dy);
        double[] placed = RoughnessBounds(main);
        if (companion != null)
        {
            double[] c = RoughnessBounds(companion);
            placed[3] = Math.Max(placed[3], c[3]); placed[4] = Math.Max(placed[4], c[4]);
        }
        if (Math.Abs(Sheet.Length - 5 - placed[3] - 7.5) > 0.1 ||
            Math.Abs(Sheet.Height - 5 - placed[4] - 7.5) > 0.1)
            throw new InvalidOperationException("NX не подтвердил отступ общей шероховатости от рамки 7,5 мм.");
    }

    private static void BuildFrame()
    {
        double right = Sheet.Length - 5.0, top = Sheet.Height - 5.0;
        Line(20, 5, right, 5); Line(right, 5, right, top);
        Line(right, top, 20, top); Line(20, top, 20, 5);
    }

    private static int[] Cfw(bool thick)
    {
        // Use the same native table line settings as the approved v1.25.
        // This applies to the title block, duplicate designation and side tables.
        return new int[] { 216, 1, thick ? 1 : 3 };
    }

    private sealed class Table
    {
        public Tag Tag;
        public Tag[] Rows;
        public Tag[] Columns;
        public double Width;
        public double Height;
    }

    private static Table NewTable(double x, double top, double[] widths, double[] heights)
    {
        UFTabnot.SectionPrefs sp;
        U.Tabnot.AskDefaultSectionPrefs(out sp);
        sp.attach_point = UFTabnot.AttachPoint.AttachPointTopLeft;
        sp.header_location = UFTabnot.HeaderLocation.HeaderLocationNone;
        sp.max_height = 1000.0;
        sp.use_double_width_border = false;
        Tag tag; U.Tabnot.Create(ref sp, new double[] { x, top, 0 }, out tag);
        Mark(tag, "T");
        CreatedTables.Add(tag);

        int nr, nc; U.Tabnot.AskNmRows(tag, out nr); U.Tabnot.AskNmColumns(tag, out nc);
        for (int r = nr - 1; r >= heights.Length; r--)
        { Tag row; U.Tabnot.AskNthRow(tag, r, out row); U.Tabnot.RemoveRow(row); U.Obj.DeleteObject(row); }
        for (int c = nc - 1; c >= widths.Length; c--)
        { Tag col; U.Tabnot.AskNthColumn(tag, c, out col); U.Tabnot.RemoveColumn(col); U.Obj.DeleteObject(col); }
        for (int r = nr; r < heights.Length; r++)
        { Tag row; U.Tabnot.CreateRow(heights[r], out row); U.Tabnot.AddRow(tag, row, UFConstants.UF_TABNOT_APPEND); }
        for (int c = nc; c < widths.Length; c++)
        { Tag col; U.Tabnot.CreateColumn(widths[c], out col); U.Tabnot.AddColumn(tag, col, UFConstants.UF_TABNOT_APPEND); }

        Table table = new Table(); table.Tag = tag;
        table.Rows = new Tag[heights.Length]; table.Columns = new Tag[widths.Length];
        Tag section; U.Tabnot.AskNthSection(tag, 0, out section);
        CreatedFormatObjects.Add(section);
        for (int c = 0; c < widths.Length; c++)
        {
            U.Tabnot.AskNthColumn(tag, c, out table.Columns[c]);
            U.Tabnot.SetColumnWidth(table.Columns[c], widths[c]);
            U.Tabnot.SetColumnHeadCfw(table.Columns[c], section, Cfw(true));
            table.Width += widths[c];
        }
        for (int r = 0; r < heights.Length; r++)
        {
            U.Tabnot.AskNthRow(tag, r, out table.Rows[r]);
            U.Tabnot.SetRowHeight(table.Rows[r], heights[r]);
            U.Tabnot.SetRowHeadCfw(table.Rows[r], Cfw(true));
            table.Height += heights[r];
            for (int c = 0; c < widths.Length; c++)
            {
                Tag cell = Cell(table, r, c);
                UFTabnot.CellPrefs cp; U.Tabnot.AskCellPrefs(cell, out cp);
                cp.format = UFTabnot.Format.FormatText;
                cp.text_font = FontIndex; cp.text_height = 2.5;
                cp.text_aspect_ratio = 1.0; cp.text_slant = 0.0;
                cp.is_italic = false; cp.is_vertical = false; cp.text_angle = 0.0;
                cp.text_density = 3; cp.text_color = 216;
                cp.char_space_factor = CharacterSpacingFactor; cp.line_space_factor = GeneralLineFactor;
                cp.horiz_just = UFTabnot.Just.JustCenter; cp.vert_just = UFTabnot.Just.JustMiddle;
                cp.is_hidden = false; cp.is_a_formula = false; cp.is_protected = false;
                cp.prefix = ""; cp.suffix = ""; cp.formula_suffix = "";
                cp.referenced_spreadsheet = Tag.Null;
                SetCellFit(ref cp);
                cp.bottom_line_cfw = Cfw(r == heights.Length - 1);
                cp.right_line_cfw = Cfw(c == widths.Length - 1);
                U.Tabnot.SetCellPrefs(cell, ref cp);
            }
        }
        return table;
    }

    // Native UF_TABNOT_fit_method_max is 8. Do not use the erroneous zero
    // exposed by some generated UFConstants assemblies for this array size.
    private const int TableFitMethodCapacity = 8;

    private static bool IsAllowedTableFitMethod(UFTabnot.FitMethod method)
    {
        // Siemens TableCellStyleBuilder lists actual methods as 1..8.
        // UF FitMethodNone (0) is only unused-array padding, never active.
        int value = (int)method;
        return value >= (int)UFTabnot.FitMethod.FitMethodOverwriteBorder &&
            value <= (int)UFTabnot.FitMethod.FitMethodTruncate &&
            method != UFTabnot.FitMethod.FitMethodRemoveSpaces;
    }

    private static void NormalizeTableFitMethods(ref UFTabnot.CellPrefs cp)
    {
        // Only the first nm_fit_methods entries are active. Do not activate
        // stale entries from the unused tail or leave None holes in that prefix.
        UFTabnot.FitMethod[] source = cp.fit_methods;
        int count = source == null ? 0 : Math.Min(Math.Max(cp.nm_fit_methods, 0),
            Math.Min(source.Length, TableFitMethodCapacity));
        List<UFTabnot.FitMethod> kept = new List<UFTabnot.FitMethod>();
        for (int i = 0; i < count; i++)
            if (IsAllowedTableFitMethod(source[i]) && !kept.Contains(source[i]))
                kept.Add(source[i]);
        if (kept.Count == 0)
        {
            SetCellFit(ref cp);
            return;
        }
        // Allocate a new array: copied CellPrefs structs may share the original.
        cp.fit_methods = new UFTabnot.FitMethod[TableFitMethodCapacity];
        kept.CopyTo(cp.fit_methods);
        cp.nm_fit_methods = kept.Count;
    }

    private static void SetCellFit(ref UFTabnot.CellPrefs cp)
    {
        cp.fit_methods = new UFTabnot.FitMethod[TableFitMethodCapacity];
        cp.fit_methods[0] = UFTabnot.FitMethod.FitMethodWrap;
        cp.fit_methods[1] = UFTabnot.FitMethod.FitMethodAutoSizeText;
        cp.nm_fit_methods = 2;
        // Keep this order: wrap first, then reduce text if NX's own cell
        // layout still does not fit. Never resize rows/columns, truncate text
        // or remove spaces. None occurs only in the inactive array tail.
    }

    private static void VerifyDefaultTableFitMethods()
    {
        UFTabnot.CellPrefs saved;
        U.Tabnot.AskDefaultCellPrefs(out saved);
        bool valid = saved.fit_methods != null &&
            saved.fit_methods.Length == TableFitMethodCapacity &&
            saved.nm_fit_methods >= 1 && saved.nm_fit_methods <= TableFitMethodCapacity;
        if (valid)
            for (int i = 0; i < saved.nm_fit_methods; i++)
                if (!IsAllowedTableFitMethod(saved.fit_methods[i])) { valid = false; break; }
        if (!valid)
            throw new InvalidOperationException("NX не сохранил допустимые способы подгонки текста новых таблиц.");
    }

    private static bool IsOverflowHashText(string text)
    {
        int count = 0;
        foreach (char ch in text ?? "")
        {
            if (Char.IsWhiteSpace(ch)) continue;
            if (ch != '#') return false;
            count++;
        }
        return count >= 3;
    }

    private static void AuditCreatedTableText()
    {
        // Check the final evaluated cell text after the sheet refresh, not
        // merely the stored input or the bounds of the separate measuring note.
        foreach (Tag table in CreatedTables)
        {
            U.Tabnot.Update(table);
            int rows, columns;
            U.Tabnot.AskNmRows(table, out rows); U.Tabnot.AskNmColumns(table, out columns);
            for (int r = 0; r < rows; r++)
            {
                Tag row; U.Tabnot.AskNthRow(table, r, out row);
                for (int c = 0; c < columns; c++)
                {
                    Tag column, cell;
                    U.Tabnot.AskNthColumn(table, c, out column);
                    U.Tabnot.AskCellAtRowCol(row, column, out cell);
                    string source; U.Tabnot.AskCellText(cell, out source);
                    if (String.IsNullOrWhiteSpace(source)) continue;
                    UFTabnot.CellPrefs cp; U.Tabnot.AskCellPrefs(cell, out cp);
                    if (cp.fit_methods == null || cp.fit_methods.Length < 2 || cp.nm_fit_methods != 2 ||
                        cp.fit_methods[0] != UFTabnot.FitMethod.FitMethodWrap ||
                        cp.fit_methods[1] != UFTabnot.FitMethod.FitMethodAutoSizeText)
                        throw new InvalidOperationException("NX не сохранил перенос и уменьшение текста ячейки: «" + source + "».");
                    string evaluated; U.Tabnot.AskEvaluatedCellText(cell, out evaluated);
                    if (IsOverflowHashText(evaluated) && !IsOverflowHashText(source))
                        throw new InvalidOperationException("NX не смог разместить текст в графе после переноса и уменьшения: «" +
                            source + "». Сократите эту запись в меню.");
                }
            }
        }
    }

    private static double TableLinePitch(double height)
    {
        // Rounded minimum pitches from GOST 2.304-81 table 1.
        return height <= 2.5 ? 4.0 : height <= 3.5 ? 5.5 : height <= 5.0 ? 8.0 :
            height <= 7.0 ? 11.0 : height <= 10.0 ? 16.0 : height <= 14.0 ? 22.0 :
            height <= 20.0 ? 31.0 : Math.Ceiling(height * 22.0 / 14.0);
    }

    private static double TableTextWidth(string text, double height, double lineFactor)
    {
        return MeasureCalibrationText(TableTextProbe, new string[] { text }, height,
            CharacterSpacingFactor, lineFactor, TableTextMark)[0];
    }

    private static string[] WrapTableText(string text, double width, double height, double lineFactor)
    {
        List<string> lines = new List<string>();
        foreach (string paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string proposed = line.Length == 0 ? word : line + " " + word;
                if (TableTextWidth(proposed, height, lineFactor) <= width + 0.01)
                { line = proposed; continue; }
                if (line.Length > 0) { lines.Add(line); line = ""; }
                string remaining = word;
                while (TableTextWidth(remaining, height, lineFactor) > width + 0.01)
                {
                    int length = TableWordBreak(remaining, width, height, lineFactor);
                    // A single wide glyph or an NX control token is left
                    // intact for the next font size/native fitting pass.
                    if (length <= 0 || length >= remaining.Length) break;
                    lines.Add(remaining.Substring(0, length));
                    remaining = remaining.Substring(length);
                }
                line = remaining;
            }
            // Preserve explicit paragraph breaks, including intentional blank lines.
            lines.Add(line);
        }
        return lines.ToArray();
    }

    private static int TableWordBreak(string word, double width, double height, double lineFactor)
    {
        // Do not cut NX embedded annotation codes or split Unicode text elements.
        if (word.IndexOf('<') >= 0 && word.IndexOf('>') >= 0) return 0;
        int[] starts = StringInfo.ParseCombiningCharacters(word);
        int low = 1, high = starts.Length, best = 0;
        while (low <= high)
        {
            int count = low + (high - low) / 2;
            int end = count == starts.Length ? word.Length : starts[count];
            if (TableTextWidth(word.Substring(0, end), height, lineFactor) <= width + 0.01)
            { best = end; low = count + 1; }
            else high = count - 1;
        }
        // Prefer an existing separator near the end of the fitting prefix.
        // Never add a hyphen or discard punctuation from the designation.
        for (int i = starts.Length - 1; i >= 0; i--)
        {
            int end = i + 1 < starts.Length ? starts[i + 1] : word.Length;
            if (end > best) continue;
            if (end < best / 2) break;
            char ch = word[starts[i]];
            if (ch == '-' || ch == '.' || ch == '/' || ch == '_' ||
                Char.GetUnicodeCategory(ch) == UnicodeCategory.DashPunctuation)
                return end;
        }
        return best;
    }

    private static Tag Cell(Table t, int r, int c)
    { Tag cell; U.Tabnot.AskCellAtRowCol(t.Rows[r], t.Columns[c], out cell); return cell; }

    private static void Merge(Table t, int r1, int c1, int r2, int c2, string text, double height, double angle)
    {
        Tag first = Cell(t, r1, c1);
        if (r1 != r2 || c1 != c2) U.Tabnot.MergeCells(first, Cell(t, r2, c2));
        UFTabnot.CellPrefs cp; U.Tabnot.AskCellPrefs(first, out cp);
        double width = 0, cellHeight = 0, value;
        for (int c = c1; c <= c2; c++) { U.Tabnot.AskColumnWidth(t.Columns[c], out value); width += value; }
        for (int r = r1; r <= r2; r++) { U.Tabnot.AskRowHeight(t.Rows[r], out value); cellHeight += value; }
        if (Math.Abs(Math.Sin(angle * Math.PI / 180.0)) > 0.5)
        { double swap = width; width = cellHeight; cellHeight = swap; }
        string fitted = FitTableText(NormalizeTitleText(text), height, width - 2.0, cellHeight - 2.0, out height);
        cp.text_height = height; cp.text_angle = angle * Math.PI / 180.0;
        cp.line_space_factor = LineFactorForHeight(height);
        SetCellFit(ref cp);
        U.Tabnot.SetCellPrefs(first, ref cp);
        U.Tabnot.SetCellText(first, fitted);
    }

    private static Tag TableTextProbe = Tag.Null;
    private static Session.UndoMarkId TableTextMark;

    private static void BeginTableTextMeasurement()
    {
        TableTextProbe = Tag.Null;
        TableTextMark = S.SetUndoMark(Session.MarkVisibility.Invisible, "Размещение текста основной надписи");
        try { U.Drf.CreateNote(1, new string[] { "Н" }, new double[] { 25, 25, 0 }, 0, out TableTextProbe); }
        catch { S.DeleteUndoMark(TableTextMark, null); throw; }
    }

    private static void EndTableTextMeasurement()
    {
        try { if (TableTextProbe != Tag.Null && IsLiveTag(TableTextProbe)) U.Obj.DeleteObject(TableTextProbe); }
        finally { TableTextProbe = Tag.Null; S.DeleteUndoMark(TableTextMark, null); }
    }

    private static double LineFactorForHeight(double height)
    {
        if (Math.Abs(LinePitchSlopeRatio) < 0.00001) return GeneralLineFactor;
        return Math.Max(0.0, (TableLinePitch(height) / height - LinePitchBaseRatio) / LinePitchSlopeRatio);
    }

    private static string FitTableText(string text, double requestedHeight, double width,
        double availableHeight, out double height)
    {
        height = requestedHeight;
        if (String.IsNullOrWhiteSpace(text)) return "";
        // Try wrapping at each standard size before selecting a smaller one.
        // The real native cell also has Wrap -> AutoSizeText as a final guard:
        // measuring a separate NX note alone did not prevent hash overflow.
        double[] sizes = new double[] { 7.0, 5.0, 3.5, 2.5 };
        string smallestWrapped = text;
        foreach (double candidate in sizes)
        {
            if (candidate > requestedHeight + 0.001) continue;
            double lineFactor = LineFactorForHeight(candidate);
            string[] lines = WrapTableText(text, width, candidate, lineFactor);
            smallestWrapped = String.Join("\n", lines);
            double[] measured = MeasureCalibrationText(TableTextProbe, lines, candidate,
                CharacterSpacingFactor, lineFactor, TableTextMark);
            // Include the full line pitch even when the note's ink bounds
            // understate the height needed by a multiline table cell.
            double blockHeight = Math.Max(measured[1], candidate + (lines.Length - 1) * TableLinePitch(candidate));
            if (measured[0] <= width + 0.01 && blockHeight <= availableHeight + 0.01)
            { height = candidate; return smallestWrapped; }
        }
        // Preserve every character for exceptional text that cannot fit at
        // 2.5 mm. Let NX reduce it instead of producing ######## or truncating.
        // Report the concrete readability/standard-size exception once at end.
        height = Math.Min(requestedHeight, 2.5);
        Warnings.Add("Длинный текст не помещается в графе шрифтом 2,5 мм после переноса: «" + text +
            "». Включено дополнительное уменьшение шрифта NX. Чтобы сохранить размер не менее 2,5 мм, сократите запись.");
        return smallestWrapped;
    }

    private static void HeavyEdge(Table t, int r, int c, bool right, bool bottom)
    {
        Tag cell = Cell(t, r, c); UFTabnot.CellPrefs cp; U.Tabnot.AskCellPrefs(cell, out cp);
        if (right) cp.right_line_cfw = Cfw(true);
        if (bottom) cp.bottom_line_cfw = Cfw(true);
        U.Tabnot.SetCellPrefs(cell, ref cp);
    }

    private static void CheckSize(Table table, double width, double height)
    {
        double w = 0, h = 0, value;
        foreach (Tag col in table.Columns) { U.Tabnot.AskColumnWidth(col, out value); w += value; }
        foreach (Tag row in table.Rows) { U.Tabnot.AskRowHeight(row, out value); h += value; }
        if (Math.Abs(w - width) > 0.001 || Math.Abs(h - height) > 0.001)
            throw new InvalidOperationException("NX изменил размеры таблицы. Ожидалось " + width + " x " + height + " мм.");
    }

    private static void BuildTitleTable(Fields f)
    {
        // Restore the approved v1.25 grid, without a separate version cell.
        // X boundaries: 0,7,17,40,55,65,135,140,145,150,155,167,185.
        // Eleven rows, 5 mm each, indexed downwards from the top.
        Table t = NewTable(Sheet.Length - 190.0, 60.0,
            new double[] { 7, 10, 23, 15, 10, 70, 5, 5, 5, 5, 12, 18 },
            new double[] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 });
        for (int r = 0; r < 11; r++) HeavyEdge(t, r, 4, true, false);
        for (int r = 3; r < 11; r++) HeavyEdge(t, r, 5, true, false);
        for (int c = 5; c < 12; c++) { HeavyEdge(t, 2, c, false, true); HeavyEdge(t, 7, c, false, true); }
        for (int c = 6; c < 12; c++) { HeavyEdge(t, 3, c, false, true); HeavyEdge(t, 6, c, false, true); }
        for (int r = 3; r < 7; r++) { HeavyEdge(t, r, 8, true, false); HeavyEdge(t, r, 10, true, false); }
        HeavyEdge(t, 7, 9, true, false);

        Merge(t, 0, 5, 2, 11, f.Designation, 5, 0);
        Merge(t, 3, 5, 7, 5, f.Name, 5, 0);
        Merge(t, 8, 5, 10, 5, f.Material, 3.5, 0);
        Merge(t, 3, 6, 3, 8, "Лит.", 2.5, 0);
        Merge(t, 3, 9, 3, 10, "Масса", 2.5, 0);
        Merge(t, 3, 11, 3, 11, "Масштаб", 2.5, 0);
        for (int i = 0; i < 3; i++)
            Merge(t, 4, 6 + i, 6, 6 + i, i < f.Letter.Length ? f.Letter[i].ToString() : "", 3.5, 0);
        Merge(t, 4, 9, 6, 10, f.Mass, 3.5, 0);
        double n, d; Sheet.GetScale(out n, out d);
        string scale = n.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU")) + ":" + d.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"));
        Merge(t, 4, 11, 6, 11, scale, 3.5, 0);
        Merge(t, 7, 6, 7, 9, "Лист " + f.SheetNumber, 2.5, 0);
        Merge(t, 7, 10, 7, 11, "Листов " + f.SheetCount, 2.5, 0);
        Merge(t, 8, 6, 10, 11, f.Company, 3.5, 0);
        string[] revisionHeaders = new string[] { "Изм.", "Лист", "№ докум.", "Подп.", "Дата" };
        for (int c = 0; c < 5; c++) Merge(t, 4, c, 4, c, revisionHeaders[c], 2.5, 0);
        string[] roles = new string[] { "Разраб.", "Пров.", "Т. контр.", "", "Н. контр.", "Утв." };
        for (int r = 5; r < 11; r++) Merge(t, r, 0, r, 1, roles[r - 5], 2.5, 0);
        Merge(t, 5, 2, 5, 2, f.Developer, 2.5, 0);
        Merge(t, 6, 2, 6, 2, f.Checker, 2.5, 0);
        Merge(t, 7, 2, 7, 2, f.TechnicalControl, 2.5, 0);
        Merge(t, 9, 2, 9, 2, f.NormControl, 2.5, 0);
        Merge(t, 10, 2, 10, 2, f.Approver, 2.5, 0);
        U.Tabnot.Update(t.Tag);
        CheckSize(t, 185, 55);
        AddFormatDesignation();
    }

    private static void AddFormatDesignation()
    {
        double shorter = Math.Min(Sheet.Length, Sheet.Height), longer = Math.Max(Sheet.Length, Sheet.Height);
        string format = BasicSheetFormat(Sheet.Length, Sheet.Height);
        if (format.Length == 0)
        {
            Warnings.Add("Лист имеет нестандартные для основного ряда размеры " + shorter + " × " + longer +
                " мм. Уточните обозначение дополнительного формата по ГОСТ 2.301 и заполните графу «Формат».");
        }
        Tag tag;
        U.Drf.CreateNote(1, new string[] { "Формат " + format }, new double[] { Sheet.Length - 45, 4, 0 }, 0, out tag);
        Mark(tag, "FORMAT_");
        MeasureCalibrationText(tag, new string[] { "Формат " + format }, 2.5,
            CharacterSpacingFactor, LineFactorForHeight(2.5), TableTextMark);
        double[] upperLeft = new double[3]; double width, height;
        U.Drf.AskAnnotationTextBox(tag, upperLeft, out width, out height);
        Note note = (Note)NXOpen.Utilities.NXObjectManager.Get(tag);
        Point3d origin = note.AnnotationOrigin;
        note.AnnotationOrigin = new Point3d(origin.X + Sheet.Length - 7 - upperLeft[0] - width,
            origin.Y + 4.0 - upperLeft[1], origin.Z);
        int dependent; string viewName;
        U.View.AskViewDependentStatus(tag, out dependent, out viewName);
        if (dependent == 0) U.View.ConvertToView(Sheet.View.Tag, tag);
        note.Unblank();
    }

    private static void BuildAuxiliaryTables(Fields f)
    {
        // Restore the horizontal 70 x 14 upper frame requested by the user,
        // independently of the sheet format and its initial/final orientation.
        // The duplicate designation remains upside down, as in the earlier layout.
        Table reverse = NewTable(20, Sheet.Height - 5, new double[] { 70.0 }, new double[] { 14.0 });
        Merge(reverse, 0, 0, 0, 0, f.Designation, 5, 180);
        U.Tabnot.Update(reverse.Tag); CheckSize(reverse, 70, 14);

        Table bottom = NewTable(8, 150, new double[] { 5, 7 }, new double[] { 35, 25, 25, 35, 25 });
        string[] labels = new string[] { "Подп. и дата", "Инв. № дубл.", "Взам. инв. №", "Подп. и дата", "Инв. № подл." };
        for (int r = 0; r < 5; r++)
        {
            Merge(bottom, r, 0, r, 0, labels[r], 2.5, 90);
            HeavyEdge(bottom, r, 0, false, true); HeavyEdge(bottom, r, 1, false, true);
        }
        U.Tabnot.Update(bottom.Tag); CheckSize(bottom, 12, 145);

        if (Sheet.Height - 5.0 - 120.0 >= 155.0)
        {
            // A4 portrait and all previously supported standard sheet sizes.
            // Keep both 60-mm fields and a gap above the lower 145-mm panel.
            Table upper = NewTable(8, Sheet.Height - 5, new double[] { 5, 7 }, new double[] { 60, 60 });
            Merge(upper, 0, 0, 0, 0, "Перв. примен.", 2.5, 90);
            Merge(upper, 1, 0, 1, 0, "Справ. №", 2.5, 90);
            HeavyEdge(upper, 0, 0, false, true); HeavyEdge(upper, 0, 1, false, true);
            U.Tabnot.Update(upper.Tag); CheckSize(upper, 12, 120);
        }
        else
        {
            // User-selected landscape A4 is a custom layout, not GOST A4.
            // The two side panels cannot both fit its 200-mm inner height.
            // Transpose the upper panel into the top strip, preserving its
            // 60-mm fields and 5/7-mm label/value bands. The designation at
            // x=20..90 remains separate; the lower left panel is unchanged.
            Table upper = NewTable(100, Sheet.Height - 5, new double[] { 60, 60 }, new double[] { 5, 7 });
            Merge(upper, 0, 0, 0, 0, "Перв. примен.", 2.5, 0);
            Merge(upper, 0, 1, 0, 1, "Справ. №", 2.5, 0);
            HeavyEdge(upper, 0, 0, true, false); HeavyEdge(upper, 1, 0, true, false);
            U.Tabnot.Update(upper.Tag); CheckSize(upper, 120, 12);
        }
    }

    private static string NormalizeTitleText(string value)
    {
        // The supplied log contains Cyrillic "и" followed by U+0306 in the
        // part name. The GOST font has precomposed "й", not that separate mark.
        // Canonical composition also handles "е" + U+0308 as "ё". Form C
        // preserves engineering symbols, punctuation, spacing and line breaks.
        return (value ?? "").Normalize(NormalizationForm.FormC);
    }

    private static string ReadAttribute(NXObject obj, string key, string fallback)
    {
        return obj.HasUserAttribute(key, NXObject.AttributeType.String, -1)
            ? obj.GetStringUserAttribute(key, -1) : fallback;
    }

    private sealed class Fields
    {
        public string Designation, Name, Material, Company, Developer, Checker, Mass, Letter, SheetNumber, SheetCount;
        public string TechnicalControl, NormControl, Approver, Version; // Retain the v1.26-v1.29 attribute; no visible version cell.
        public string[] Values()
        {
            return new string[] { Designation, Name, Material, Company, Developer, Checker, Mass, Letter,
                SheetNumber, SheetCount, TechnicalControl, NormControl, Approver, Version };
        }
        public static Fields From(string[] v)
        {
            // Normalize values read from existing attributes, part names,
            // INI defaults and input dialogs; do not rename the physical file.
            v = (string[])v.Clone();
            for (int i = 0; i < v.Length; i++) v[i] = NormalizeTitleText(v[i]);
            // The first ten indices remain compatible with v1.4 sheet attributes.
            Fields f = new Fields(); f.Designation = v[0]; f.Name = v[1]; f.Material = v[2]; f.Company = v[3];
            f.Developer = v[4]; f.Checker = v[5]; f.Mass = v[6]; f.Letter = v[7]; f.SheetNumber = v[8]; f.SheetCount = v[9];
            f.TechnicalControl = v[10]; f.NormControl = v[11]; f.Approver = v[12]; f.Version = v.Length > 13 ? v[13] : "1"; return f;
        }
    }

    private static Fields ReadFields()
    {
        string fileName = Path.GetFileName(P.FullPath);
        if (String.IsNullOrWhiteSpace(fileName)) fileName = P.Name;
        string designation, name;
        SplitDrawingFileName(fileName, out designation, out name);
        string number = "";
        DraftingDrawingSheet draftingSheet = Sheet as DraftingDrawingSheet;
        if (draftingSheet == null)
            throw new InvalidOperationException("Текущий лист не является листом Drafting (Черчение).");
        DraftingDrawingSheetBuilder b = P.DraftingDrawingSheets.CreateDraftingDrawingSheetBuilder(draftingSheet);
        try { number = b.Number; } finally { b.Destroy(); }
        string[] defaults = new string[] { designation, name, "", "", "", "", "", "", number,
            P.DrawingSheets.ToArray().Length.ToString(CultureInfo.InvariantCulture), "", "", "", "1" };
        // Slots 0 and 1 always come from the current filename, even after Save As
        // or an earlier manual edit of the title block. Keep other saved fields.
        for (int i = 2; i < defaults.Length; i++) defaults[i] = ReadAttribute(Sheet, "NX_ESKD_FIELD_" + i, defaults[i]);
        return Fields.From(defaults);
    }

    private static void SplitDrawingFileName(string fileName, out string designation, out string name)
    {
        string stem = NormalizeTitleText(fileName).Trim();
        // Strip only the NX part extension: dots inside a designation are data.
        // P.Name may already have no extension, including for a new unsaved part.
        if (stem.EndsWith(".prt", StringComparison.OrdinalIgnoreCase))
            stem = stem.Substring(0, stem.Length - 4).TrimEnd();
        designation = "";
        name = stem;
        int end = 0;
        while (end < stem.Length && !Char.IsWhiteSpace(stem[end])) end++;
        string candidate = stem.Substring(0, end);
        if (!IsDesignationToken(candidate)) return;
        designation = candidate;
        name = stem.Substring(end).Trim();
    }

    private static bool IsDesignationToken(string value)
    {
        if (String.IsNullOrEmpty(value) || !Char.IsLetterOrDigit(value[0]) ||
            !Char.IsLetterOrDigit(value[value.Length - 1])) return false;
        bool hasDigit = false, hasLetter = false, hasSeparator = false;
        foreach (char c in value)
        {
            if (Char.IsDigit(c)) { hasDigit = true; continue; }
            if (Char.IsLetter(c)) { hasLetter = true; continue; }
            if (c == '.' || c == '_' || Char.GetUnicodeCategory(c) == UnicodeCategory.DashPunctuation)
            { hasSeparator = true; continue; }
            return false;
        }
        // Numeric or separated codes: 20027, TXF-20027, НЛС-ПФ-4723.03,
        // АБВГ.123456.001. Ordinary words (including Втулка2) remain a name.
        // This is filename recognition, not validation of an ESKD designation.
        return hasDigit && (hasSeparator || !hasLetter);
    }

    private static void SaveFields(Fields f)
    {
        string[] values = f.Values();
        for (int i = 0; i < values.Length; i++) Sheet.SetUserAttribute("NX_ESKD_FIELD_" + i, -1, values[i], Update.Option.Now);
    }

    private static class DraftingSetupDialog
    {
        private const int ApplyId = 1, CancelId = 2;
        private const int LandscapeId = 20, PortraitId = 21, MaterialButtonId = 30, IniButtonId = 31, StatusId = 90;
        private const int EditBase = 101, LabelBase = 201, MaxTextLength = 1024;
        private const int RequirementsEnabledId = 401, RequirementsHintId = 402, RequirementsEditId = 403;
        private const int RoughnessEnabledId = 411, RaLabelId = 412, RaEditId = 413, ExceptionsId = 414;
        private const int MaxRequirementsLength = 24000, MaxRaLength = 64;
        private const short DialogWidth = 740, DialogHeight = 390;

        private sealed class FieldSpec
        {
            public readonly int ValueIndex;
            public readonly string Label;
            public readonly bool Required, Inline;
            public readonly short X, Y, Width;
            public FieldSpec(int index, string label, bool required, short x, short y, short width, bool inline)
            { ValueIndex = index; Label = label; Required = required; X = x; Y = y; Width = width; Inline = inline; }
        }

        private static readonly FieldSpec[] Inputs = new FieldSpec[] {
            new FieldSpec(0, "Обозначение", true, 108, 70, 254, true),
            new FieldSpec(1, "Наименование", true, 108, 94, 254, true),
            new FieldSpec(2, "Материал", false, 108, 118, 137, true),
            new FieldSpec(6, "Масса, кг", false, 12, 162, 80, false),
            new FieldSpec(8, "Номер листа", false, 102, 162, 80, false),
            new FieldSpec(9, "Листов всего", true, 192, 162, 80, false),
            new FieldSpec(7, "Литера", false, 282, 162, 80, false),
            new FieldSpec(3, "Организация", false, 108, 193, 254, true),
            new FieldSpec(4, "Разработал", false, 108, 217, 254, true),
            new FieldSpec(5, "Проверил", false, 108, 241, 254, true),
            new FieldSpec(10, "Тех. контроль", false, 108, 265, 254, true),
            new FieldSpec(11, "Нормоконтроль", false, 108, 289, 254, true),
            new FieldSpec(12, "Утвердил", false, 108, 313, 254, true)
        };

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
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern int GetDlgCtrlID(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDlgItemTextW(IntPtr window, int id, string text);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetDlgItemTextW(IntPtr window, int id, StringBuilder text, int capacity);
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr SetFocus(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CheckRadioButton(IntPtr window, int first, int last, int selected);
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern uint IsDlgButtonChecked(IntPtr window, int id);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InvalidateRect(IntPtr window, IntPtr rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr GetSysColorBrush(int index);
        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern uint SetTextColor(IntPtr hdc, uint color);
        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern int SetBkMode(IntPtr hdc, int mode);

        public static bool Show(IntPtr parent, Fields initial, DrawingExtras initialExtras, Fields iniSeed,
            double width, double height, out Fields acceptedFields, out DrawingExtras acceptedExtras, out bool acceptedPortrait)
        {
            acceptedFields = initial; acceptedExtras = initialExtras; acceptedPortrait = height > width;
            if (parent == IntPtr.Zero) throw new InvalidOperationException("Не удалось получить главное окно NX.");
            string[] values = initial.Values();
            foreach (FieldSpec input in Inputs)
                if ((values[input.ValueIndex] ?? "").Length > MaxTextLength)
                    throw new InvalidOperationException("Поле «" + input.Label + "» содержит более 1024 символов.");
            bool selectedPortrait = height > width, showValidation = false, statusError = false, loading = false;
            DrawingExtras selectedExtras = initialExtras;
            HashSet<int> errorLabels = new HashSet<int>();
            byte[] template = BuildTemplate(width, height);
            Exception callbackError = null;
            DialogProcedure callback = delegate(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
            {
                try
                {
                    if (message == 0x0110) // WM_INITDIALOG
                    {
                        loading = true;
                        try
                        {
                            for (int i = 0; i < Inputs.Length; i++)
                                SendMessageW(GetDlgItem(window, EditBase + i), 0x00C5, new IntPtr(MaxTextLength), IntPtr.Zero);
                            SendMessageW(GetDlgItem(window, RequirementsEditId), 0x00C5, new IntPtr(MaxRequirementsLength), IntPtr.Zero);
                            SendMessageW(GetDlgItem(window, RaEditId), 0x00C5, new IntPtr(MaxRaLength), IntPtr.Zero);
                            WriteInputs(window, values, selectedExtras);
                        }
                        finally { loading = false; }
                        CheckRadioButton(window, LandscapeId, PortraitId, selectedPortrait ? PortraitId : LandscapeId);
                        FocusControl(window, EditBase);
                        return IntPtr.Zero;
                    }
                    if (message == 0x0138) // WM_CTLCOLORSTATIC
                    {
                        int id = GetDlgCtrlID(lParam);
                        if (errorLabels.Contains(id) || (id == StatusId && statusError))
                        {
                            SetTextColor(wParam, 0x002222B8u); // COLORREF: dark red.
                            SetBkMode(wParam, 1);
                            return GetSysColorBrush(15); // Borrowed COLOR_3DFACE brush.
                        }
                    }
                    if (message == 0x0010) // WM_CLOSE
                    { EndDialog(window, new IntPtr(CancelId)); return new IntPtr(1); }
                    if (message == 0x0111) // WM_COMMAND
                    {
                        int id = (int)(wParam.ToInt64() & 0xFFFF);
                        int notification = (int)((wParam.ToInt64() >> 16) & 0xFFFF);
                        if (id == CancelId && notification == 0)
                        { EndDialog(window, new IntPtr(CancelId)); return new IntPtr(1); }
                        bool editChanged = notification == 0x0300 &&
                            ((id >= EditBase && id < EditBase + Inputs.Length) || id == RequirementsEditId || id == RaEditId);
                        bool checkChanged = notification == 0 &&
                            (id == RequirementsEnabledId || id == RoughnessEnabledId || id == ExceptionsId);
                        if (!loading && showValidation && (editChanged || checkChanged))
                        {
                            ReadInputs(window, values);
                            DrawingExtras ignored; string error;
                            statusError = UpdateValidation(window, values, errorLabels, out ignored, out error) >= 0;
                            SetDlgItemTextW(window, StatusId, statusError ? error : "Параметры заполнены. Можно применить.");
                            InvalidateRect(window, IntPtr.Zero, true);
                            return new IntPtr(1);
                        }
                        if (notification == 0 && id == MaterialButtonId)
                        {
                            // The owned picker leaves all other controls and orientation intact.
                            ReadInputs(window, values);
                            string chosen;
                            int response = MaterialPicker.Show(window, values[2], out chosen);
                            if (response == 5)
                            {
                                values[2] = NormalizeTitleText(chosen ?? "").Trim();
                                SetDlgItemTextW(window, EditBase + 2, values[2]);
                            }
                            FocusControl(window, EditBase + 2);
                            return new IntPtr(1);
                        }
                        if (notification == 0 && id == IniButtonId)
                        {
                            // Read-only baseline reload. LastUsed must never mask this button.
                            // Stage all values before changing any control, so a bad INI loses no input.
                            try
                            {
                                if (!File.Exists(SettingsPath))
                                    throw new InvalidOperationException("INI ещё не создан. Он появится после успешного оформления.");
                                ReadInputs(window, values);
                                Fields reloaded = Fields.From(values);
                                IniSettings baseline = IniSettings.Load(SettingsPath, iniSeed);
                                baseline.ApplyTo(reloaded, false);
                                DrawingExtras reloadedExtras = baseline.ReadDrawingExtras();
                                loading = true;
                                try { WriteInputs(window, reloaded.Values(), reloadedExtras); }
                                finally { loading = false; }
                                values = reloaded.Values();
                                string error = ""; DrawingExtras ignored;
                                errorLabels.Clear();
                                statusError = showValidation && UpdateValidation(window, values, errorLabels, out ignored, out error) >= 0;
                                SetDlgItemTextW(window, StatusId, statusError ? error : "Загружены базовые значения INI.\r\nДля оформления нажмите «Применить».");
                            }
                            catch (Exception ex)
                            {
                                statusError = true;
                                // Keep the error inside the same menu; no cascade of message boxes.
                                SetDlgItemTextW(window, StatusId, ShortIniError(ex));
                            }
                            InvalidateRect(window, IntPtr.Zero, true);
                            return new IntPtr(1);
                        }
                        if (notification == 0 && id == ApplyId)
                        {
                            ReadInputs(window, values);
                            showValidation = true;
                            string error;
                            int invalid = UpdateValidation(window, values, errorLabels, out selectedExtras, out error);
                            statusError = invalid >= 0;
                            if (statusError)
                            {
                                SetDlgItemTextW(window, StatusId, error);
                                InvalidateRect(window, IntPtr.Zero, true);
                                FocusControl(window, invalid);
                                return new IntPtr(1);
                            }
                            selectedPortrait = IsDlgButtonChecked(window, PortraitId) == 1;
                            EndDialog(window, new IntPtr(ApplyId));
                            return new IntPtr(1);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Managed exceptions must not cross the native callback boundary.
                    callbackError = ex; EndDialog(window, new IntPtr(CancelId)); return new IntPtr(1);
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
                    throw new InvalidOperationException("Ошибка окна настройки чертежа: " + callbackError.Message, callbackError);
                long code = response.ToInt64();
                if (code == CancelId) return false;
                if (code != ApplyId)
                    throw new InvalidOperationException("Не удалось открыть настройки чертежа. Код Windows: " + nativeError + ".");
                acceptedFields = Fields.From(values); acceptedExtras = selectedExtras; acceptedPortrait = selectedPortrait;
                return true;
            }
            finally { GC.KeepAlive(callback); Marshal.FreeHGlobal(memory); }
        }

        private static string ShortIniError(Exception ex)
        {
            // The full path is already known to the user; show the actionable inner cause.
            string message = (ex.InnerException ?? ex).Message.Replace('\r', ' ').Replace('\n', ' ');
            if (message.Length > 150) message = message.Substring(0, 147) + "...";
            return "INI: " + message;
        }

        private static void WriteInputs(IntPtr window, string[] values, DrawingExtras extras)
        {
            for (int i = 0; i < Inputs.Length; i++)
                if (!SetDlgItemTextW(window, EditBase + i, values[Inputs[i].ValueIndex] ?? ""))
                    throw new InvalidOperationException("Не удалось заполнить поле «" + Inputs[i].Label + "».");
            SetDlgItemTextW(window, RequirementsEditId, FormatRequirements(extras.Requirements));
            SetDlgItemTextW(window, RaEditId, extras.RoughnessRa ?? "6,3");
            SendMessageW(GetDlgItem(window, RequirementsEnabledId), 0x00F1, new IntPtr(extras.RequirementsEnabled ? 1 : 0), IntPtr.Zero);
            SendMessageW(GetDlgItem(window, RoughnessEnabledId), 0x00F1, new IntPtr(extras.RoughnessEnabled ? 1 : 0), IntPtr.Zero);
            SendMessageW(GetDlgItem(window, ExceptionsId), 0x00F1, new IntPtr(extras.RoughnessExceptions ? 1 : 0), IntPtr.Zero);
        }

        private static string ReadText(IntPtr window, int id, int limit)
        {
            StringBuilder text = new StringBuilder(limit + 1);
            GetDlgItemTextW(window, id, text, text.Capacity);
            return NormalizeTitleText(text.ToString()).Trim();
        }

        private static void ReadInputs(IntPtr window, string[] values)
        {
            for (int i = 0; i < Inputs.Length; i++)
                values[Inputs[i].ValueIndex] = ReadText(window, EditBase + i, MaxTextLength);
        }

        private static string FormatRequirements(string[] requirements)
        {
            List<string> lines = new List<string>();
            foreach (string text in requirements ?? new string[0])
                lines.Add((lines.Count + 1).ToString(CultureInfo.InvariantCulture) + ". " + text);
            return String.Join("\r\n", lines.ToArray());
        }

        private static bool ParseRequirements(string text, out string[] requirements, out string error)
        {
            List<string> items = new List<string>();
            error = "";
            foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string item = line.Trim();
                // Remove a displayed list number only when followed by whitespace.
                // A requirement starting with a decimal such as 0.3 must stay intact.
                item = System.Text.RegularExpressions.Regex.Replace(item, @"^\d+[.)]\s+", "").Trim();
                if (item.Length == 0) continue;
                if (item.Length > MaxTextLength) { error = "Тех. требования: в одном пункте не более 1024 символов."; break; }
                items.Add(item);
                if (items.Count > 20) { error = "Тех. требования: допускается не более 20 пунктов."; break; }
            }
            requirements = items.ToArray();
            return error.Length == 0;
        }

        private static int UpdateValidation(IntPtr window, string[] values, HashSet<int> errorLabels,
            out DrawingExtras extras, out string message)
        {
            errorLabels.Clear();
            int first = -1; message = "";
            for (int i = 0; i < Inputs.Length; i++)
            {
                FieldSpec input = Inputs[i]; string value = values[input.ValueIndex] ?? "";
                string error = input.Required && String.IsNullOrWhiteSpace(value) ? "Заполните поле «" + input.Label + "»." : "";
                if (input.ValueIndex == 7 && value.Length > 3) error = "Литера: допускается не более трёх символов.";
                if (error.Length == 0) continue;
                errorLabels.Add(LabelBase + i);
                if (first < 0) { first = EditBase + i; message = error; }
            }
            extras = new DrawingExtras();
            extras.RequirementsEnabled = IsDlgButtonChecked(window, RequirementsEnabledId) == 1;
            extras.RoughnessEnabled = IsDlgButtonChecked(window, RoughnessEnabledId) == 1;
            extras.RoughnessExceptions = IsDlgButtonChecked(window, ExceptionsId) == 1;
            string requirementsError;
            if (!ParseRequirements(ReadText(window, RequirementsEditId, MaxRequirementsLength), out extras.Requirements, out requirementsError))
            {
                errorLabels.Add(RequirementsHintId);
                if (first < 0) { first = RequirementsEditId; message = requirementsError; }
            }
            if (extras.RequirementsEnabled && extras.Requirements.Length == 0)
            {
                errorLabels.Add(RequirementsHintId);
                if (first < 0) { first = RequirementsEditId; message = "Введите тех. требования или снимите флажок их добавления."; }
            }
            if (!IniSettings.TryNormalizeRa(ReadText(window, RaEditId, MaxRaLength), out extras.RoughnessRa))
            {
                errorLabels.Add(RaLabelId);
                if (first < 0) { first = RaEditId; message = "Ra: введите положительное число, например 6,3."; }
            }
            return first;
        }

        private static void FocusControl(IntPtr window, int id)
        {
            IntPtr edit = GetDlgItem(window, id);
            SetFocus(edit);
            SendMessageW(edit, 0x00B1, IntPtr.Zero, new IntPtr(-1)); // EM_SETSEL
        }

        private static byte[] BuildTemplate(double width, double height)
        {
            string shortSide = Math.Min(width, height).ToString("0.###", CultureInfo.InvariantCulture);
            string longSide = Math.Max(width, height).ToString("0.###", CultureInfo.InvariantCulture);
            string format = BasicSheetFormat(width, height);
            string orientationLabel = format == "А4"
                ? "Ориентация — ширина × высота. А4 по ЕСКД: вертикальный"
                : "Ориентация — ширина × высота";
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode))
            {
                writer.Write(0x80C808C0u); // Unicode modal dialog, centered, with a system font.
                writer.Write(0u);
                long countPosition = stream.Position;
                writer.Write((ushort)0);
                writer.Write((short)0); writer.Write((short)0);
                writer.Write(DialogWidth); writer.Write(DialogHeight);
                writer.Write((ushort)0); writer.Write((ushort)0);
                WriteString(writer, Title + " — Настройки");
                writer.Write((ushort)9); WriteString(writer, "Segoe UI");
                ushort controls = 0;
                AddControl(writer, ref controls, 0x50000080u, 12, 8, 716, 14, 80, 0x0082,
                    (format.Length > 0 ? format + " — " : "") + "Данные листа, основной надписи и технические требования");
                AddControl(writer, ref controls, 0x50000007u, 12, 26, 716, 37, 81, 0x0080, orientationLabel);
                AddControl(writer, ref controls, 0x50030009u, 24, 41, 330, 18, LandscapeId, 0x0080,
                    "Горизонтальный — " + longSide + " × " + shortSide + " мм");
                AddControl(writer, ref controls, 0x50010009u, 384, 41, 330, 18, PortraitId, 0x0080,
                    "Вертикальный — " + shortSide + " × " + longSide + " мм");
                for (int i = 0; i < Inputs.Length; i++)
                {
                    FieldSpec input = Inputs[i];
                    short labelX = input.Inline ? (short)12 : input.X;
                    short labelY = (short)(input.Inline ? input.Y + 2 : input.Y - 17);
                    short labelWidth = input.Inline ? (short)90 : input.Width;
                    AddControl(writer, ref controls, 0x50000080u, labelX, labelY, labelWidth, 14,
                        LabelBase + i, 0x0082, input.Label + (input.Required ? " *" : ""));
                    AddControl(writer, ref controls, 0x50810080u | (i == 0 ? 0x00020000u : 0u),
                        input.X, input.Y, input.Width, 18, EditBase + i, 0x0081, "");
                    if (i == 2)
                        AddControl(writer, ref controls, 0x50010000u, 251, 117, 111, 20,
                            MaterialButtonId, 0x0080, "Выбрать материал...");
                }
                AddControl(writer, ref controls, 0x50010003u, 380, 70, 348, 18, RequirementsEnabledId, 0x0080,
                    "Добавлять технические требования");
                AddControl(writer, ref controls, 0x50000080u, 380, 94, 348, 22, RequirementsHintId, 0x0082,
                    "Каждый пункт — с новой строки.\r\nНумерация добавляется автоматически.");
                // ES_MULTILINE | ES_AUTOVSCROLL | ES_WANTRETURN, WS_VSCROLL.
                // No ES_AUTOHSCROLL: soft wrapping; Enter inserts a real new paragraph.
                AddControl(writer, ref controls, 0x50A11044u, 380, 118, 348, 139, RequirementsEditId, 0x0081, "");
                AddControl(writer, ref controls, 0x50000080u, 380, 263, 348, 12, 404, 0x0082,
                    "При каждом запуске — исходные 3 пункта.");
                AddControl(writer, ref controls, 0x50000007u, 380, 279, 348, 54, 410, 0x0080, "Общая шероховатость");
                AddControl(writer, ref controls, 0x50010003u, 392, 291, 318, 16, RoughnessEnabledId, 0x0080,
                    "Добавлять в правом верхнем углу");
                AddControl(writer, ref controls, 0x50000080u, 392, 315, 30, 14, RaLabelId, 0x0082, "Ra, мкм");
                AddControl(writer, ref controls, 0x50810080u, 428, 311, 74, 18, RaEditId, 0x0081, "");
                AddControl(writer, ref controls, 0x50010003u, 513, 311, 203, 18, ExceptionsId, 0x0080, "Знак в скобках");
                AddControl(writer, ref controls, 0x50010000u, 12, 350, 230, 24, IniButtonId, 0x0080,
                    "Взять из настроек файла .ini");
                AddControl(writer, ref controls, 0x50000080u, 252, 344, 272, 39, StatusId, 0x0082,
                    "* Обязательные поля.\r\n«Применить» — оформить и запомнить ввод.");
                AddControl(writer, ref controls, 0x50010001u, 536, 350, 92, 24, ApplyId, 0x0080, "Применить");
                AddControl(writer, ref controls, 0x50010000u, 636, 350, 92, 24, CancelId, 0x0080, "Отмена");
                writer.Flush();
                long end = stream.Position; stream.Position = countPosition;
                writer.Write(controls); writer.Flush(); stream.Position = end;
                return stream.ToArray();
            }
        }

        private static void AddControl(BinaryWriter writer, ref ushort count, uint style,
            short x, short y, short width, short height, int id, ushort classId, string text)
        {
            while ((writer.BaseStream.Position & 3) != 0) writer.Write((byte)0);
            writer.Write(style); writer.Write(0u);
            writer.Write(x); writer.Write(y); writer.Write(width); writer.Write(height);
            writer.Write((ushort)id); writer.Write((ushort)0xFFFF); writer.Write(classId);
            WriteString(writer, text); writer.Write((ushort)0); count++;
        }

        private static void WriteString(BinaryWriter writer, string text)
        { writer.Write(Encoding.Unicode.GetBytes(text ?? "")); writer.Write((ushort)0); }
    }

    private static string GetSettingsPath()
    {
        // A journal is compiled into a temporary DLL; Assembly.Location is NOT
        // the directory of the user's .cs file. NX provides the original path.
        string journal = S.ExecutingJournal;
        if (String.IsNullOrEmpty(journal) || !Path.IsPathRooted(journal))
            throw new InvalidOperationException("NX не сообщил полный путь к скрипту. Запустите файл .cs через " +
                "Tools (Инструменты) > Journal (Журнал) > Play (Воспроизвести).");
        return Path.Combine(Path.GetDirectoryName(journal), SettingsFileName);
    }

    private sealed class IniSettings
    {
        private const int MaxIniBytes = 262144;
        private const string RequirementsTemplateMarker = "; NX_ESKD_TECH_REQUIREMENTS_TEMPLATE=H12_3_ITEMS_V1";
        private static readonly string[] RequirementsTemplate = new string[] {
            "* Размер для справок",
            "Неуказанные предельные отклонения размеров: H12, h12, ±IT12/2",
            "Острые кромки притупить"
        };
        // Same indices as Fields.Values(); the legacy hidden Version attribute is not editable.
        private static readonly string[] LastFieldNames = new string[] {
            "Designation", "Name", "Material", "Company", "Developer", "Checker", "Mass", "Letter",
            "SheetNumber", "SheetCount", "TechnicalControl", "NormControl", "Approver"
        };
        private readonly Dictionary<string, string> entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] AllowedKeys = new string[] {
            "TitleBlock.Company", "TitleBlock.Developer", "TitleBlock.Checker", "TitleBlock.TechnicalControl",
            "TitleBlock.NormControl", "TitleBlock.Approver", "TitleBlock.Letter", "Defaults.Material",
            "TechnicalRequirements.Enabled", "SurfaceRoughness.Enabled", "SurfaceRoughness.Ra", "SurfaceRoughness.Exceptions",
            "LastUsed.RequirementsEnabled", "LastUsed.RoughnessEnabled", "LastUsed.RoughnessRa", "LastUsed.RoughnessExceptions" };

        public static string[] InitialRequirements()
        { return (string[])RequirementsTemplate.Clone(); }

        public static IniSettings Load(string path, Fields previous)
        {
            try
            {
                bool exists = File.Exists(path);
                if (exists && new FileInfo(path).Length > MaxIniBytes)
                    throw new InvalidOperationException("Файл настроек больше 256 КБ. Проверьте, что выбран правильный INI.");
                // Loading and reloading are always read-only. An absent INI is previewed in memory.
                string content = exists ? new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path)) : Template(previous);
                if (content.Length > 0 && content[0] == '\uFEFF') content = content.Substring(1);
                IniSettings settings = Parse(SplitLines(content));
                settings.AddDrawingDefaults();
                if (settings.Get("TitleBlock.Letter").Length > 3)
                    throw new InvalidOperationException("Параметр Letter (Литера) должен содержать не более трёх символов.");
                settings.ReadDrawingExtras();
                return settings;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Не удалось прочитать настройки:\n" + path +
                    "\n" + ex.Message + "\nСохраните INI в кодировке UTF-8.", ex);
            }
        }

        private static string[] SplitLines(string content)
        { return content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'); }

        private static IniSettings Parse(string[] lines)
        {
            IniSettings settings = new IniSettings();
            string section = "";
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("["))
                {
                    if (!line.EndsWith("]")) throw IniError(i, "Не закрыта секция в квадратных скобках.");
                    section = line.Substring(1, line.Length - 2).Trim();
                    if (!String.Equals(section, "TitleBlock", StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(section, "Defaults", StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(section, "TechnicalRequirements", StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(section, "SurfaceRoughness", StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(section, "LastUsed", StringComparison.OrdinalIgnoreCase))
                        throw IniError(i, "Неизвестная секция: " + section + ".");
                    continue;
                }
                int separator = line.IndexOf('=');
                if (separator <= 0 || section.Length == 0)
                    throw IniError(i, "Нужна строка Параметр=Значение внутри секции.");
                string key = section + "." + line.Substring(0, separator).Trim();
                bool known = false;
                foreach (string allowed in AllowedKeys)
                    if (String.Equals(key, allowed, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
                if (!known && section.Equals("LastUsed", StringComparison.OrdinalIgnoreCase))
                    foreach (string name in LastFieldNames)
                        if (key.Equals("LastUsed." + name, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
                if (!known && key.StartsWith("TechnicalRequirements.Item", StringComparison.OrdinalIgnoreCase))
                {
                    int item;
                    known = Int32.TryParse(key.Substring("TechnicalRequirements.Item".Length), out item) && item >= 1 && item <= 20 &&
                        key.Equals("TechnicalRequirements.Item" + item.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
                }
                if (!known) throw IniError(i, "Неизвестный параметр: " + key + ".");
                if (settings.entries.ContainsKey(key)) throw IniError(i, "Параметр указан дважды: " + key + ".");
                // Quotes, semicolons, backslashes and '=' inside a value are literal data.
                // Comments must be on separate lines.
                string value = line.Substring(separator + 1).Trim();
                if (value.Length > 1024) throw IniError(i, "Значение длиннее 1024 символов.");
                settings.entries.Add(key, value);
            }
            return settings;
        }

        private static InvalidOperationException IniError(int index, string message)
        { return new InvalidOperationException("Строка INI " + (index + 1).ToString(CultureInfo.InvariantCulture) + ": " + message); }

        private string Get(string key)
        { string value; return entries.TryGetValue(key, out value) ? value : ""; }

        private void AddDefault(string key, string value)
        { if (!entries.ContainsKey(key)) entries.Add(key, value); }

        private void AddDrawingDefaults()
        {
            // Fill older INI versions in memory only. Never rewrite their baseline sections.
            AddDefault("TechnicalRequirements.Enabled", "1");
            AddDefault("SurfaceRoughness.Enabled", "1");
            AddDefault("SurfaceRoughness.Ra", "6.3");
            AddDefault("SurfaceRoughness.Exceptions", "1");
            bool hasItems = false;
            for (int i = 1; i <= 20; i++)
                if (entries.ContainsKey("TechnicalRequirements.Item" + i.ToString(CultureInfo.InvariantCulture)))
                { hasItems = true; break; }
            // An explicitly shortened or blank baseline list must not grow new items on reload.
            if (!hasItems)
                for (int i = 0; i < RequirementsTemplate.Length; i++)
                    AddDefault("TechnicalRequirements.Item" + (i + 1).ToString(CultureInfo.InvariantCulture), RequirementsTemplate[i]);
        }

        private bool BooleanValue(string key)
        {
            string value = Get(key);
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            throw new InvalidOperationException("В INI параметр " + key + " должен быть 1 или 0.");
        }

        public static bool TryNormalizeRa(string text, out string normalized)
        {
            normalized = "";
            double ra;
            if (!Double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out ra) || !(ra > 0) || Double.IsInfinity(ra)) return false;
            normalized = ra.ToString("0.############", CultureInfo.InvariantCulture).Replace('.', ',');
            return normalized != "0";
        }

        public DrawingExtras ReadDrawingExtras()
        {
            DrawingExtras value = new DrawingExtras();
            value.RequirementsEnabled = BooleanValue("TechnicalRequirements.Enabled");
            value.RoughnessEnabled = BooleanValue("SurfaceRoughness.Enabled");
            value.RoughnessExceptions = BooleanValue("SurfaceRoughness.Exceptions");
            if (!TryNormalizeRa(Get("SurfaceRoughness.Ra"), out value.RoughnessRa))
                throw new InvalidOperationException("В INI параметр SurfaceRoughness.Ra должен быть положительным числом, например 6.3.");
            List<string> requirements = new List<string>();
            for (int i = 1; i <= 20; i++)
            {
                string text = NormalizeTitleText(Get("TechnicalRequirements.Item" + i.ToString(CultureInfo.InvariantCulture))).Trim();
                if (text.Length > 0) requirements.Add(text);
            }
            value.Requirements = requirements.ToArray();
            return value;
        }

        public void ApplyTo(Fields fields, bool hasSavedMaterial)
        {
            // This method applies only baseline sections, including intentionally blank values.
            fields.Company = Get("TitleBlock.Company");
            fields.Developer = Get("TitleBlock.Developer");
            fields.Checker = Get("TitleBlock.Checker");
            fields.TechnicalControl = Get("TitleBlock.TechnicalControl");
            fields.NormControl = Get("TitleBlock.NormControl");
            fields.Approver = Get("TitleBlock.Approver");
            fields.Letter = Get("TitleBlock.Letter");
            if (!hasSavedMaterial) fields.Material = Get("Defaults.Material");
        }

        public Fields ApplyLastUsed(Fields fields, DrawingExtras extras)
        {
            string[] values = fields.Values();
            // Legacy Designation/Name keys remain accepted when parsing older INI
            // files, but cannot replace the values read from this part's filename.
            for (int i = 2; i < LastFieldNames.Length; i++)
            {
                string saved;
                if (entries.TryGetValue("LastUsed." + LastFieldNames[i], out saved)) values[i] = saved;
            }
            if (entries.ContainsKey("LastUsed.RequirementsEnabled")) extras.RequirementsEnabled = BooleanValue("LastUsed.RequirementsEnabled");
            if (entries.ContainsKey("LastUsed.RoughnessEnabled")) extras.RoughnessEnabled = BooleanValue("LastUsed.RoughnessEnabled");
            if (entries.ContainsKey("LastUsed.RoughnessExceptions")) extras.RoughnessExceptions = BooleanValue("LastUsed.RoughnessExceptions");
            if (entries.ContainsKey("LastUsed.RoughnessRa") && !TryNormalizeRa(Get("LastUsed.RoughnessRa"), out extras.RoughnessRa))
                throw new InvalidOperationException("В INI параметр LastUsed.RoughnessRa должен быть положительным числом, например 6.3.");
            // No saved TT text or orientation: startup restores the agreed text and actual sheet orientation.
            return Fields.From(values);
        }

        private static string SingleLine(string text)
        { return (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); }

        private static string WithoutLastUsed(string content)
        {
            StringBuilder retained = new StringBuilder();
            bool skip = false;
            // Preserve the exact baseline characters and line endings, including comments.
            foreach (System.Text.RegularExpressions.Match match in
                System.Text.RegularExpressions.Regex.Matches(content, @"[^\r\n]*(?:\r\n|\r|\n|$)"))
            {
                string original = match.Value;
                if (original.Length == 0) continue;
                string line = original.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                    skip = line.Substring(1, line.Length - 2).Trim().Equals("LastUsed", StringComparison.OrdinalIgnoreCase);
                if (!skip) retained.Append(original);
            }
            return retained.ToString();
        }

        private static string LastUsedSection(Fields fields, DrawingExtras extras, string newline)
        {
            List<string> lines = new List<string>();
            lines.Add("[LastUsed]");
            lines.Add("; Последний успешно применённый ввод. Базовые секции выше не изменяются.");
            lines.Add("; Текст ТТ не запоминается: при запуске всегда используются согласованные 3 пункта.");
            lines.Add("; Ориентация при запуске определяется по текущему листу NX.");
            lines.Add("; Обозначение и наименование при запуске берутся из имени текущего файла .prt.");
            string[] values = fields.Values();
            for (int i = 2; i < LastFieldNames.Length; i++) lines.Add(LastFieldNames[i] + "=" + SingleLine(values[i]));
            lines.Add("RequirementsEnabled=" + (extras.RequirementsEnabled ? "1" : "0"));
            lines.Add("RoughnessEnabled=" + (extras.RoughnessEnabled ? "1" : "0"));
            lines.Add("RoughnessRa=" + SingleLine(extras.RoughnessRa));
            lines.Add("RoughnessExceptions=" + (extras.RoughnessExceptions ? "1" : "0"));
            return String.Join(newline, lines.ToArray()) + newline;
        }

        public static void SaveLastUsed(string path, Fields seed, Fields fields, DrawingExtras extras)
        {
            // Called only after every NX formatting step and final audit succeeds.
            // Re-read under the write lock to preserve baseline edits made while the dialog was open.
            // No separate state, temporary, backup, log or report files are created.
            using (FileStream file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                if (file.Length > MaxIniBytes) throw new InvalidOperationException("Размер INI превышает 256 КБ.");
                byte[] original = new byte[(int)file.Length];
                int read = 0;
                while (read < original.Length)
                {
                    int count = file.Read(original, read, original.Length - read);
                    if (count == 0) throw new EndOfStreamException("Не удалось полностью прочитать INI.");
                    read += count;
                }
                bool withBom = original.Length == 0 || (original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF);
                string content = original.Length == 0 ? Template(seed) : new UTF8Encoding(false, true).GetString(original);
                if (content.Length > 0 && content[0] == '\uFEFF') content = content.Substring(1);
                string baseline = WithoutLastUsed(content);
                string newline = content.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : content.IndexOf('\n') >= 0 ? "\n" : "\r\n";
                if (baseline.Length > 0 && !baseline.EndsWith("\r") && !baseline.EndsWith("\n")) baseline += newline;
                string updated = baseline + LastUsedSection(fields, extras, newline);
                // Validate the full result before touching any existing bytes.
                IniSettings checkedSettings = Parse(SplitLines(updated));
                checkedSettings.AddDrawingDefaults();
                DrawingExtras checkedExtras = checkedSettings.ReadDrawingExtras();
                checkedSettings.ApplyLastUsed(Fields.From(fields.Values()), checkedExtras);
                byte[] payload = new UTF8Encoding(false, true).GetBytes((withBom ? "\uFEFF" : "") + updated);
                if (payload.Length > MaxIniBytes) throw new InvalidOperationException("Настройки не помещаются в INI размером 256 КБ.");
                try
                {
                    file.Position = 0; file.Write(payload, 0, payload.Length); file.SetLength(payload.Length); file.Flush();
                }
                catch
                {
                    // Best-effort recovery uses the original bytes already held in memory.
                    try { file.Position = 0; file.Write(original, 0, original.Length); file.SetLength(original.Length); file.Flush(); }
                    catch { }
                    throw;
                }
            }
        }

        private static string Template(Fields f)
        {
            return String.Join("\r\n", new string[] {
                "; NX ESKD — базовые настройки и последний применённый ввод, " + SCRIPT_VERSION + ".",
                "; Храните NX_ESKD_Settings.ini в одной папке со скриптом .cs. Кодировка UTF-8.",
                "; Базовые секции загружаются кнопкой «Взять из настроек файла .ini».",
                "; При запуске поверх базовых значений загружается секция [LastUsed].",
                "; Обозначение и наименование всегда берутся из имени текущего файла .prt; их можно изменить в окне.",
                "; [LastUsed] обновляется только после успешного оформления. Базовые значения не перезаписываются.",
                "; Все поля можно изменить в общем окне. Пустое значение означает пустую графу.",
                "; Кавычки и знак = внутри значений сохраняются. Комментарии пишите отдельными строками.",
                "; При обновлении скрипта сохраняйте этот заполненный INI.", "",
                "[TitleBlock]",
                "Company=" + SingleLine(f.Company),
                "Developer=" + SingleLine(f.Developer),
                "Checker=" + SingleLine(f.Checker),
                "TechnicalControl=" + SingleLine(f.TechnicalControl),
                "NormControl=" + SingleLine(f.NormControl),
                "Approver=" + SingleLine(f.Approver),
                "; Литера — до трёх символов. Если не назначена, оставьте пустым.",
                "Letter=" + SingleLine(f.Letter), "",
                "[Defaults]",
                "; Материал можно ввести вручную или выбрать кнопкой в общем окне.",
                "Material=" + SingleLine(f.Material), "",
                "[TechnicalRequirements]",
                "; Enabled=1: добавлять/обновлять ТТ на первом листе; 0: не добавлять/не обновлять.",
                "; При каждом запуске редактор начинает с согласованных трёх пунктов.",
                "; Item1...Item20 загружаются только по кнопке чтения базового INI.",
                "; Текст без номера; пустые пункты пропускаются. Это редактируемые требования к детали.",
                "Enabled=1",
                "Item1=" + RequirementsTemplate[0], "Item2=" + RequirementsTemplate[1], "Item3=" + RequirementsTemplate[2],
                RequirementsTemplateMarker, "",
                "[SurfaceRoughness]",
                "; Enabled=1: добавлять общую шероховатость на текущем листе; 0: отключить добавление.",
                "; Ra в мкм; допустимы 6.3 и 6,3. Exceptions=1: дополнительный знак в скобках.",
                "Enabled=1", "Ra=6.3", "Exceptions=1", "" });
        }
    }

    private static int ConnectDrawingFont(string nxFontDirectory)
    {
        try
        {
            int index = P.Fonts.AddFont(FontName, FontCollection.Type.Standard);
            if (!P.Fonts.DoesFontExist(index) || P.Fonts.GetFontType(index) != FontCollection.Type.Standard ||
                !String.Equals(P.Fonts.GetFontName(index), FontName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("NX вернул другое имя или тип шрифта.");
            return index;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Шрифт " + FontName + " установлен, но NX пока не подтвердил его подключение.\n" +
                "Сохраните работу, полностью закройте NX и откройте его заново, затем повторите запуск " + SCRIPT_VERSION + ".\n" +
                "Каталог для NX уже настроен: " + nxFontDirectory + ".\n" +
                "Параметры введены; оформление чертежа ещё не выполнено.\nПричина NX: " + ex.Message, ex);
        }
    }

    // A Unicode Win32 dialog has a visible label inside the window. The legacy
    // UF AskStringInput cue is displayed in NX's status area, not in the dialog.
    // No System.Windows.Forms or System.Drawing reference is required.
    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-dialogboxindirectparamw
    private sealed class MaterialGroup
    {
        public readonly string Category, Label, Standard, Prefix, Note;
        public readonly string[] Grades;
        public MaterialGroup(string category, string label, string standard, string prefix, string grades, string note)
        {
            Category = category; Label = label; Standard = standard; Prefix = prefix; Note = note;
            Grades = grades.Split('|');
        }
        public string MaterialText(string grade)
        { return (Prefix.Length == 0 ? "" : Prefix + " ") + grade + " ГОСТ " + Standard; }
    }

    private static class MaterialPicker
    {
        // Catalog is embedded; the drawing workstation needs no Internet connection.
        // Scope/status checked against Rosstandart on 2026-09-22. See the supplied catalog.
        private static readonly MaterialGroup[] Groups = new MaterialGroup[]
        {
            new MaterialGroup("Конструкционные стали", "Качественные углеродистые", "1050-2013", "Сталь", "10|15|20|25|30|35|40|45|50|55|60", ""),
            new MaterialGroup("Конструкционные стали", "Легированные", "4543-2016", "Сталь", "20Х|40Х|45Х|30ХГСА|40ХН|40ХН2МА|30ХН2МА|38Х2МЮА|18ХГТ|20ХГР|20ХН3А|20Х2Н4А|12ХН3А|12Х2Н4А|30ХМА|35ХМ|38ХН3МФА", ""),
            new MaterialGroup("Конструкционные стали", "Обыкновенного качества", "380-2005", "Сталь", "Ст3сп|Ст3пс|Ст5сп", "ГОСТ на марки и химический состав; требования к прокату задаются отдельно."),
            new MaterialGroup("Конструкционные стали", "Повышенной прочности", "19281-2014", "Сталь", "09Г2С|10ХСНД|15ХСНД|17Г1С", "На 22.09.2026 действует редакция 2014. ГОСТ 19281-2025 вводится с 01.12.2026; после этой даты уточните запись вручную."),
            new MaterialGroup("Конструкционные стали", "Автоматные", "1414-75", "Сталь", "А12|А20|А30|А40Г|АС14|АС35Г2", ""),
            new MaterialGroup("Конструкционные стали", "Рессорно-пружинные", "14959-2016", "Сталь", "65Г|60С2А|50ХФА|60С2ХА", ""),
            new MaterialGroup("Конструкционные стали", "Подшипниковые", "801-2022", "Сталь", "ШХ15|ШХ15СГ|ШХ20СГ", ""),
            new MaterialGroup("Инструментальные стали", "Нелегированные", "1435-99", "Сталь", "У7|У8|У8А|У9|У10|У10А|У12|У12А", ""),
            new MaterialGroup("Инструментальные стали", "Легированные и штамповые", "5950-2000", "Сталь", "9ХС|ХВГ|Х12|Х12МФ|Х6ВФ|4Х5МФС|4Х5В2ФС|5ХНМ|5ХНВ|3Х2В8Ф|7Х3", ""),
            new MaterialGroup("Инструментальные стали", "Быстрорежущие", "19265-73", "Сталь", "Р6М5|Р18|Р6М5К5|Р9К5|Р6М5Ф3", ""),
            new MaterialGroup("Нержавеющие и жаропрочные", "Нержавеющие и жаростойкие", "5632-2014", "Сталь", "12Х18Н10Т|08Х18Н10|08Х18Н10Т|08Х17Н13М2Т|10Х17Н13М2Т|12Х13|20Х13|30Х13|40Х13|14Х17Н2|95Х18|20Х23Н18|12Х18Н9|03Х17Н14М3", "ГОСТ на марки. Состояние поставки и ГОСТ на пруток, лист или поковку уточняются отдельно."),
            new MaterialGroup("Нержавеющие и жаропрочные", "Жаропрочные никелевые", "5632-2014", "Сплав", "ХН77ТЮР|ХН78Т", "ГОСТ на марки. Не назначает термообработку и форму полуфабриката."),
            new MaterialGroup("Нержавеющие и жаропрочные", "Теплоустойчивые", "20072-74", "Сталь", "12Х1МФ|15Х5|12МХ|15Х5М", ""),
            new MaterialGroup("Чугуны и стальное литьё", "Серый чугун", "1412-85", "", "СЧ15|СЧ20|СЧ25|СЧ30|СЧ35", ""),
            new MaterialGroup("Чугуны и стальное литьё", "Высокопрочный чугун", "7293-85", "", "ВЧ40|ВЧ50|ВЧ60|ВЧ70|ВЧ80", ""),
            new MaterialGroup("Чугуны и стальное литьё", "Ковкий чугун", "1215-79", "", "КЧ30-6|КЧ35-10|КЧ45-7|КЧ60-3", ""),
            new MaterialGroup("Чугуны и стальное литьё", "Сталь для отливок", "977-88", "Сталь", "20Л|25Л|35Л|45Л|35ХГСЛ|110Г13Л", ""),
            new MaterialGroup("Алюминий", "Деформируемые сплавы", "4784-2019", "Сплав", "АМг2|АМг3|АМг5|АМг6|АМц|АД31|АД33|Д1|Д16|Д19|Д20|В95|АК4-1|АК6|АК8", "ГОСТ на марку и состав. Например, Д16 — марка; Т, Т1, М — состояния полуфабриката, которые задают отдельно по его стандарту."),
            new MaterialGroup("Алюминий", "Литейные сплавы", "1583-93", "Сплав", "АК12|АК9ч|АК7ч|АК5М2|АК12М2|АК12М2МгН|АМг5Мц", "АК12 также обозначался АЛ2, АК9ч — АЛ4, АК7ч — АЛ9. Способ литья и термообработку указывают по требованиям детали."),
            new MaterialGroup("Латуни и бронзы", "Латуни деформируемые", "15527-2004", "Латунь", "Л63|Л68|Л70|Л90|ЛС59-1|ЛС63-3|ЛЖМц59-1-1|ЛАЖ60-1-1", ""),
            new MaterialGroup("Латуни и бронзы", "Латуни литейные", "17711-93", "Латунь", "ЛЦ40С|ЛЦ40Мц3Ж|ЛЦ40Мц1,5|ЛЦ16К4", ""),
            new MaterialGroup("Латуни и бронзы", "Безоловянные деформируемые", "18175-78", "Бронза", "БрАЖ9-4|БрАЖМц10-3-1,5|БрБ2|БрКМц3-1|БрХ1|БрАМц9-2", ""),
            new MaterialGroup("Латуни и бронзы", "Оловянные деформируемые", "5017-2006", "Бронза", "БрОФ6,5-0,15|БрОФ7-0,2|БрОЦ4-3|БрОЦС4-4-2,5|БрОФ4-0,25", ""),
            new MaterialGroup("Латуни и бронзы", "Оловянные литейные", "613-79", "Бронза", "БрО5Ц5С5|БрО4Ц4С17|БрО6Ц6С3|БрО10Ф1|БрО10С10", ""),
            new MaterialGroup("Латуни и бронзы", "Безоловянные литейные", "493-79", "Бронза", "БрА9Ж3Л|БрА10Ж3Мц2|БрА10Ж4Н4Л", ""),
            new MaterialGroup("Титан и магний", "Титановые деформируемые", "19807-91", "Титан", "ВТ1-0|ВТ1-00|ВТ6|ВТ5|ВТ8|ВТ14|ВТ20|ВТ22|ОТ4|ОТ4-1|ПТ-3В", ""),
            new MaterialGroup("Титан и магний", "Магниевые деформируемые", "14957-76", "Сплав", "МА2|МА2-1|МА5|МА8|МА14", ""),
            new MaterialGroup("Титан и магний", "Магниевые литейные", "2856-79", "Сплав", "МЛ5|МЛ6|МЛ10|МЛ12|МЛ19", ""),
            new MaterialGroup("Другие металлы и сплавы", "Медь", "859-2014", "Медь", "М1|М1р|М2|М3|М0б", ""),
            new MaterialGroup("Другие металлы и сплавы", "Никель и медно-никелевые", "492-2006", "Сплав", "НП2|НП3|МН19|МНЖМц30-1-1|МНЦ15-20|МНМц3-12", ""),
            new MaterialGroup("Другие металлы и сплавы", "Цинковые литейные", "25140-93", "Сплав", "ЦА4|ЦА4М1|ЦА4М3|ЦА8М1|ЦА30М5", "Марки отливок по ГОСТ 25140. ГОСТ 19424-97 относится к литейным сплавам в чушках."),
            new MaterialGroup("Другие металлы и сплавы", "Баббиты", "1320-74", "Баббит", "Б83|Б88|Б16|БН|БС6", ""),
            new MaterialGroup("Другие металлы и сплавы", "Припои оловянно-свинцовые", "21930-76", "Припой", "ПОС30|ПОС40|ПОС61|ПОС90", "ГОСТ на припои в чушках. Проволока и прутки — отдельный стандарт ГОСТ 21931-76."),
            new MaterialGroup("Твёрдые сплавы", "Спечённые твёрдые сплавы", "3882-74", "Сплав", "ВК6|ВК8|ВК10|ВК15|ВК20|Т5К10|Т15К6|Т30К4|ТТ7К12", ""),
        };

        public static int Show(IntPtr parent, string initial, out string value)
        {
            value = initial ?? "";
            List<string> categories = new List<string>();
            foreach (MaterialGroup group in Groups)
                if (!categories.Contains(group.Category)) categories.Add(group.Category);
            string category = null;
            MaterialGroup selectedGroup = null;
            int page = 0;
            while (true)
            {
                List<MaterialGroup> filtered = new List<MaterialGroup>();
                if (category != null)
                    foreach (MaterialGroup group in Groups) if (group.Category == category) filtered.Add(group);
                if (selectedGroup == null && filtered.Count == 1) selectedGroup = filtered[0];
                List<string> choices = new List<string>();
                string heading, help, back;
                if (category == null)
                {
                    choices.AddRange(categories);
                    heading = "Материал: выберите группу";
                    help = "Далее выберите тип сплава и марку. Название и ГОСТ будут подставлены в основную надпись.";
                    back = "К настройкам";
                }
                else if (selectedGroup == null)
                {
                    foreach (MaterialGroup group in filtered) choices.Add(group.Label + "\nГОСТ " + group.Standard);
                    heading = category + ": выберите тип";
                    help = "Для деформируемых и литейных сплавов используются разные стандарты.";
                    back = "К группам";
                }
                else
                {
                    choices.AddRange(selectedGroup.Grades);
                    heading = selectedGroup.Label + " — ГОСТ " + selectedGroup.Standard;
                    help = "Выберите марку. Перед применением будет показана полная запись материала.";
                    back = "Назад";
                }
                int response = ButtonMenuDialog.Show(parent, heading, help, value, choices.ToArray(), page, back, value.Length > 0);
                if (response == ButtonMenuDialog.Cancel) return 2;
                if (response == ButtonMenuDialog.PreviousPage) { page--; continue; }
                if (response == ButtonMenuDialog.NextPage) { page++; continue; }
                if (response == ButtonMenuDialog.KeepCurrent) return 5;
                if (response == ButtonMenuDialog.Empty) { value = ""; return 5; }
                if (response == ButtonMenuDialog.Manual)
                {
                    string entered;
                    int manual = ManualEntry(parent, value, out entered);
                    value = entered;
                    if (manual == 2 || manual == 5) return manual;
                    continue;
                }
                if (response == ButtonMenuDialog.Back)
                {
                    if (selectedGroup != null && filtered.Count > 1) selectedGroup = null;
                    else if (category != null) { category = null; selectedGroup = null; }
                    else return 1;
                    page = 0;
                    continue;
                }
                int chosen = response - ButtonMenuDialog.FirstChoice;
                if (chosen < 0 || chosen >= choices.Count)
                    throw new InvalidOperationException("Окно материала вернуло неизвестную кнопку.");
                if (category == null) { category = categories[chosen]; page = 0; continue; }
                if (selectedGroup == null) { selectedGroup = filtered[chosen]; page = 0; continue; }
                string proposed = selectedGroup.MaterialText(selectedGroup.Grades[chosen]);
                string confirmed;
                int confirmation = Confirm(parent, selectedGroup, proposed, out confirmed);
                if (confirmation == 2) return 2;
                if (confirmation == 5) { value = confirmed; return 5; }
                // Back from confirmation returns to the same page of grades.
            }
        }

        private static int Confirm(IntPtr parent, MaterialGroup group, string proposed, out string value)
        {
            value = proposed;
            while (true)
            {
                string help = group.Note.Length > 0 ? group.Note :
                    "Проверьте запись. Состояние поставки, термообработку и дополнительные требования можно уточнить вручную по документации детали.";
                int response = ButtonMenuDialog.Show(parent, "Запись в графу «Материал»", help, value,
                    new string[] { "Использовать эту запись" }, 0, "К маркам", false);
                if (response == ButtonMenuDialog.FirstChoice) return 5;
                if (response == ButtonMenuDialog.Back) return 1;
                if (response == ButtonMenuDialog.Cancel) return 2;
                if (response == ButtonMenuDialog.Empty) { value = ""; return 5; }
                if (response == ButtonMenuDialog.Manual)
                {
                    string entered;
                    int manual = ManualEntry(parent, value, out entered);
                    value = entered;
                    if (manual == 2 || manual == 5) return manual;
                }
            }
        }

        private static int ManualEntry(IntPtr parent, string initial, out string value)
        {
            return LabeledInputDialog.Show(parent, "Материал — ручной ввод",
                "Введите полную запись материала, включая нужный ГОСТ или ТУ. Например: Сталь 40Х ГОСТ 4543-2016. " +
                "«Назад» возвращает к выбору материала; «Применить» принимает введённый текст.",
                initial, out value);
        }
    }

    private static class ButtonMenuDialog
    {
        public const int Cancel = 2, Back = 3, PreviousPage = 4, NextPage = 5;
        public const int KeepCurrent = 6, Manual = 7, Empty = 8, FirstChoice = 100;
        private const int PageSize = 12;

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
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr SetFocus(IntPtr window);

        public static int Show(IntPtr parent, string heading, string help, string current,
            string[] choices, int page, string backLabel, bool canKeep)
        {
            if (parent == IntPtr.Zero) throw new InvalidOperationException("Не удалось получить главное окно NX.");
            if (choices == null || choices.Length == 0) throw new InvalidOperationException("Список материалов пуст.");
            int pages = (choices.Length + PageSize - 1) / PageSize;
            if (page < 0 || page >= pages) throw new InvalidOperationException("Неверный номер страницы каталога.");
            int first = page * PageSize;
            int visible = Math.Min(PageSize, choices.Length - first);
            byte[] template = BuildTemplate(heading, help, current, choices, first, visible, page, pages, backLabel, canKeep);
            Exception callbackError = null;
            DialogProcedure callback = delegate(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
            {
                try
                {
                    if (message == 0x0110) // WM_INITDIALOG
                    {
                        SetFocus(GetDlgItem(window, FirstChoice + first));
                        return IntPtr.Zero;
                    }
                    if (message == 0x0010) // WM_CLOSE
                    {
                        EndDialog(window, new IntPtr(Cancel)); return new IntPtr(1);
                    }
                    if (message == 0x0111) // WM_COMMAND
                    {
                        int id = (int)(wParam.ToInt64() & 0xFFFF);
                        int notification = (int)((wParam.ToInt64() >> 16) & 0xFFFF);
                        bool allowed = id == Cancel || id == Back || id == Manual || id == Empty ||
                            (id == KeepCurrent && canKeep) ||
                            (id == PreviousPage && page > 0) || (id == NextPage && page + 1 < pages) ||
                            (id >= FirstChoice + first && id < FirstChoice + first + visible);
                        if (notification == 0 && allowed) // BN_CLICKED; Escape also yields IDCANCEL.
                        {
                            EndDialog(window, new IntPtr(id)); return new IntPtr(1);
                        }
                    }
                }
                catch (Exception ex)
                {
                    callbackError = ex; EndDialog(window, new IntPtr(Cancel)); return new IntPtr(1);
                }
                return IntPtr.Zero;
            };
            IntPtr memory = Marshal.AllocHGlobal(template.Length);
            try
            {
                Marshal.Copy(template, 0, memory, template.Length);
                IntPtr answer = DialogBoxIndirectParamW(IntPtr.Zero, memory, parent, callback, IntPtr.Zero);
                int nativeError = Marshal.GetLastWin32Error();
                if (callbackError != null) throw new InvalidOperationException("Ошибка окна материалов: " + callbackError.Message, callbackError);
                long code = answer.ToInt64();
                if (code < 2 || code > FirstChoice + choices.Length - 1)
                    throw new InvalidOperationException("Не удалось открыть окно материалов. Код Windows: " + nativeError + ".");
                return (int)code;
            }
            finally { GC.KeepAlive(callback); Marshal.FreeHGlobal(memory); }
        }

        private static byte[] BuildTemplate(string heading, string help, string current, string[] choices,
            int first, int visible, int page, int pages, string backLabel, bool canKeep)
        {
            int rows = (visible + 2) / 3;
            int navigationY = 110 + rows * 36;
            int bottomY = navigationY + 26;
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode))
            {
                writer.Write(0x80C808C0u); // Modal, caption, system menu, centered, chosen font.
                writer.Write(0u);
                writer.Write((ushort)(visible + 11)); // Three text controls, choices, page label and seven buttons.
                writer.Write((short)0); writer.Write((short)0);
                writer.Write((short)524); writer.Write((short)(bottomY + 34));
                writer.Write((ushort)0); writer.Write((ushort)0);
                WriteString(writer, Title + " — Материал");
                writer.Write((ushort)10); WriteString(writer, "Segoe UI");
                AddControl(writer, 0x50000080u, 12, 9, 500, 18, 201, 0x0082, heading);
                AddControl(writer, 0x50000080u, 12, 31, 500, 36, 202, 0x0082, help);
                string display = String.IsNullOrEmpty(current) ? "Материал пока не указан" : current;
                if (display.Length > 200) display = display.Substring(0, 197) + "...";
                AddControl(writer, 0x50000080u, 12, 73, 500, 30, 203, 0x0082, "Запись: " + display);
                for (int i = 0; i < visible; i++)
                {
                    uint style = 0x50012000u; // Child, visible, tab stop, multiline push button.
                    if (i == 0) style |= 1u; // Default button for keyboard navigation.
                    AddControl(writer, style, (short)(12 + (i % 3) * 170), (short)(110 + (i / 3) * 36),
                        160, 30, (ushort)(FirstChoice + first + i), 0x0080, choices[first + i]);
                }
                AddControl(writer, 0x50000080u, 12, (short)(navigationY + 4), 240, 16, 204, 0x0082,
                    "Страница " + (page + 1) + " из " + pages);
                AddControl(writer, 0x50010000u | (page > 0 ? 0u : 0x08000000u), 272, (short)navigationY, 116, 20,
                    PreviousPage, 0x0080, "Предыдущая");
                AddControl(writer, 0x50010000u | (page + 1 < pages ? 0u : 0x08000000u), 396, (short)navigationY, 116, 20,
                    NextPage, 0x0080, "Следующая");
                AddControl(writer, 0x50010000u, 12, (short)bottomY, 94, 22, Back, 0x0080, backLabel);
                AddControl(writer, 0x50012000u | (canKeep ? 0u : 0x08000000u), 112, (short)bottomY, 108, 22,
                    KeepCurrent, 0x0080, "Оставить текущее");
                AddControl(writer, 0x50010000u, 226, (short)bottomY, 92, 22, Manual, 0x0080, "Вручную / ТУ");
                AddControl(writer, 0x50010000u, 324, (short)bottomY, 92, 22, Empty, 0x0080, "Без материала");
                AddControl(writer, 0x50010000u, 422, (short)bottomY, 90, 22, Cancel, 0x0080, "Отмена");
                writer.Flush(); return stream.ToArray();
            }
        }

        private static void AddControl(BinaryWriter writer, uint style, short x, short y, short width,
            short height, ushort id, ushort classId, string text)
        {
            while ((writer.BaseStream.Position & 3) != 0) writer.Write((byte)0);
            writer.Write(style); writer.Write(0u);
            writer.Write(x); writer.Write(y); writer.Write(width); writer.Write(height);
            writer.Write(id); writer.Write((ushort)0xFFFF); writer.Write(classId);
            WriteString(writer, text); writer.Write((ushort)0);
        }

        private static void WriteString(BinaryWriter writer, string text)
        { writer.Write(Encoding.Unicode.GetBytes(text ?? "")); writer.Write((ushort)0); }
    }

    private static class LabeledInputDialog
    {
        private const int EditId = 101;
        private const int BackId = 3;
        private const int MaxTextLength = 1024;

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
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDlgItemTextW(IntPtr window, int id, string text);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetDlgItemTextW(IntPtr window, int id, StringBuilder text, int capacity);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnableWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool enabled);
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr SetFocus(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        // Material text entry: 5 = Apply, 1 = Back, 2 = Cancel/close/Escape.
        public static int Show(IntPtr parent, string label, string hint,
            string initialValue, out string enteredValue)
        {
            string currentValue = initialValue ?? "";
            enteredValue = currentValue;
            if (currentValue.Length > MaxTextLength)
                throw new InvalidOperationException("Поле «" + label + "» содержит более " +
                    MaxTextLength.ToString(CultureInfo.InvariantCulture) + " символов.");
            if (parent == IntPtr.Zero)
                throw new InvalidOperationException("Не удалось получить главное окно NX для ввода данных.");

            string caption = Title + " — " + label;
            byte[] template = BuildTemplate(caption, label, hint);
            Exception callbackError = null;
            DialogProcedure callback = delegate(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
            {
                try
                {
                    if (message == 0x0110) // WM_INITDIALOG
                    {
                        IntPtr edit = GetDlgItem(window, EditId);
                        if (edit == IntPtr.Zero || !SetDlgItemTextW(window, EditId, currentValue))
                            throw new InvalidOperationException("Не удалось создать поле ввода «" + label + "».");
                        EnableWindow(GetDlgItem(window, BackId), true);
                        SendMessageW(edit, 0x00C5, new IntPtr(MaxTextLength), IntPtr.Zero); // EM_LIMITTEXT
                        SetFocus(edit);
                        SendMessageW(edit, 0x00B1, IntPtr.Zero, new IntPtr(-1)); // EM_SETSEL
                        return IntPtr.Zero; // Focus was assigned explicitly.
                    }
                    if (message == 0x0010) // WM_CLOSE
                    {
                        EndDialog(window, new IntPtr(2));
                        return new IntPtr(1);
                    }
                    if (message == 0x0111) // WM_COMMAND
                    {
                        int id = (int)(wParam.ToInt64() & 0xFFFF);
                        if (id == 2) // IDCANCEL also handles Escape.
                        {
                            EndDialog(window, new IntPtr(2));
                            return new IntPtr(1);
                        }
                        if (id == 1 || id == BackId)
                        {
                            StringBuilder text = new StringBuilder(MaxTextLength + 1);
                            GetDlgItemTextW(window, EditId, text, text.Capacity);
                            currentValue = text.ToString();
                            EndDialog(window, new IntPtr(id == BackId ? 1 : 5));
                            return new IntPtr(1);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Never let a managed exception cross the native callback boundary.
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
                // DialogBox disables/re-enables its NX owner and runs the modal loop.
                IntPtr response = DialogBoxIndirectParamW(IntPtr.Zero, memory, parent, callback, IntPtr.Zero);
                int nativeError = Marshal.GetLastWin32Error();
                if (callbackError != null)
                    throw new InvalidOperationException("Ошибка окна «" + label + "»: " + callbackError.Message, callbackError);
                long code = response.ToInt64();
                if (code != 1 && code != 2 && code != 5)
                    throw new InvalidOperationException("Не удалось открыть окно «" + label +
                        "». Код Windows: " + nativeError.ToString(CultureInfo.InvariantCulture) + ".");
                enteredValue = currentValue;
                return (int)code;
            }
            finally
            {
                GC.KeepAlive(callback);
                Marshal.FreeHGlobal(memory);
            }
        }

        private static byte[] BuildTemplate(string caption, string label, string hint)
        {
            // Standard DLGTEMPLATE followed by six aligned DLGITEMTEMPLATE records.
            // All sizes are dialog units; Windows scales them with the chosen UI font.
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode))
            {
                // WS_POPUP | WS_CAPTION | WS_SYSMENU | DS_MODALFRAME | DS_SETFONT | DS_CENTER.
                writer.Write(0x80C808C0u);
                writer.Write(0u);
                writer.Write((ushort)6);
                writer.Write((short)0); writer.Write((short)0);
                writer.Write((short)360); writer.Write((short)132);
                writer.Write((ushort)0); // No menu.
                writer.Write((ushort)0); // Default dialog class.
                WriteString(writer, caption);
                writer.Write((ushort)10);
                WriteString(writer, "Segoe UI");

                // Label and hint are separate, always-visible STATIC controls.
                AddControl(writer, 0x50000080u, 0u, 12, 10, 336, 16, 201, 0x0082, label + ":");
                AddControl(writer, 0x50810080u, 0u, 12, 31, 336, 16, EditId, 0x0081, "");
                AddControl(writer, 0x50000080u, 0u, 12, 54, 336, 42, 202, 0x0082, hint);
                AddControl(writer, 0x50010000u, 0u, 100, 106, 76, 16, BackId, 0x0080, "Назад");
                AddControl(writer, 0x50010001u, 0u, 182, 106, 80, 16, 1, 0x0080, "Применить");
                AddControl(writer, 0x50010000u, 0u, 268, 106, 80, 16, 2, 0x0080, "Отмена");
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void AddControl(BinaryWriter writer, uint style, uint extendedStyle,
            short x, short y, short width, short height, ushort id, ushort classId, string text)
        {
            while ((writer.BaseStream.Position & 3) != 0) writer.Write((byte)0);
            writer.Write(style); writer.Write(extendedStyle);
            writer.Write(x); writer.Write(y); writer.Write(width); writer.Write(height);
            writer.Write(id);
            writer.Write((ushort)0xFFFF); writer.Write(classId);
            WriteString(writer, text);
            writer.Write((ushort)0); // No creation data.
        }

        private static void WriteString(BinaryWriter writer, string text)
        {
            writer.Write(Encoding.Unicode.GetBytes(text ?? ""));
            writer.Write((ushort)0);
        }
    }

    // User-supplied GOST type A Italic.ttf. Glyph outlines and effective metrics are preserved.
    // Internal family name matches InstalledStem; the only face has Regular style metadata.
    // It is visually inclined by the original outlines, without synthetic italics.
    // Unused trailing loca/hmtx records removed; SFNT alignment/checksums normalized.
    // Original SHA-256: 2dc556a737c16eb93f0776b753a58f123fbadaff8aea5d00e1fad8008e09f3bd
    // Embedded SHA-256: 648fa9e071903291a0e707312aa44f1f4638309440087363ff752989eb9e0c7b
    // https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-getfontdata
    private static class ExternalFontInstaller
    {
        private const string InstalledStem = "NX_ESKD_GOST_A_Italic_v110";
        private const string LoadedMarker = "NX_ESKD_FONT_648fa9e071903291a0e707312aa44f1f4638309440087363ff752989eb9e0c7b";
        private const string RegistryPath = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";
        private const string RegistryName = "NX_ESKD_GOST_A_Italic_v110 (TrueType)";
        private const uint FontCrc32 = 0x582A8BB4u;
        private const int FontByteCount = 57764;
        private const uint FrPrivate = 0x10u;

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int AddFontResourceExW(string name, uint flags, IntPtr reserved);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation,
            int weight, uint italic, uint underline, uint strikeout, uint charset,
            uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern uint GetFontData(IntPtr hdc, uint table, uint offset, [Out] byte[] buffer, uint bytes);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int RegCreateKeyExW(IntPtr root, string subKey, uint reserved, string keyClass,
            uint options, uint desiredAccess, IntPtr security, out IntPtr key, out uint disposition);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int RegSetValueExW(IntPtr key, string name, uint reserved,
            uint type, byte[] data, uint bytes);

        [DllImport("advapi32.dll", ExactSpelling = true)]
        private static extern int RegCloseKey(IntPtr key);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SendNotifyMessageW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

        public static string Install()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new InvalidOperationException("Подключение внешнего шрифта рассчитана на Windows.");

            byte[] fontBytes = ReadExternalFont();
            if (fontBytes.Length != FontByteCount || Crc32(fontBytes) != FontCrc32)
                throw new InvalidOperationException("Файл шрифта не соответствует ожидаемой редакции.");

            string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (String.IsNullOrEmpty(localData))
                throw new InvalidOperationException("Windows не вернул папку локальных данных пользователя.");
            string folder = Path.Combine(localData, "Microsoft", "Windows", "Fonts");
            Directory.CreateDirectory(folder);
            string path = FindOrWriteFont(folder, fontBytes);

            bool loaded = String.Equals(AppDomain.CurrentDomain.GetData(LoadedMarker) as string,
                path, StringComparison.OrdinalIgnoreCase);
            if (!loaded)
            {
                // A process-private registration takes precedence over another file
                // with the same family name. It remains needed until NX exits.
                if (AddFontResourceExW(path, FrPrivate, IntPtr.Zero) == 0)
                    throw new InvalidOperationException("Windows не смог подключить внешний шрифт к NX.");
                AppDomain.CurrentDomain.SetData(LoadedMarker, path);
            }

            VerifySelectedFont(fontBytes);
            RegisterForCurrentUser(path);

            if (!loaded && AddFontResourceExW(path, 0, IntPtr.Zero) == 0)
                Warnings.Add("Шрифт подключен к NX и зарегистрирован для пользователя. " +
                    "Другим открытым приложениям может потребоваться перезапуск для его отображения.");

            // Own-thread windows are notified synchronously; other threads do not
            // block the journal if an unrelated application has stopped responding.
            if (!SendNotifyMessageW(new IntPtr(0xffff), 0x001Du, UIntPtr.Zero, IntPtr.Zero))
                Warnings.Add("Шрифт установлен, но Windows не доставил уведомление всем окнам.");
            return path;
        }

        public static string ConfigureNxFontDirectory(string installedPath)
        {
            // NX Drafting also needs a font file in its standard-font search directory.
            // Registering a font with Windows GDI alone does not populate that directory.
            const string variable = "UGII_STANDARD_FONT_DIR";
            string activeDirectory = S.GetEnvironmentVariableValue(variable) ?? "";
            string userDirectory = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User) ?? "";
            bool createUserSetting = String.IsNullOrWhiteSpace(activeDirectory) && String.IsNullOrWhiteSpace(userDirectory);
            string directory = String.IsNullOrWhiteSpace(activeDirectory) ? userDirectory : activeDirectory;
            if (String.IsNullOrWhiteSpace(directory))
                directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Siemens", "NX_ESKD", "Fonts");
            directory = Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"'));
            if (!Path.IsPathRooted(directory) || directory.IndexOf(';') >= 0)
                throw new InvalidOperationException("NX использует нестандартную настройку каталога шрифтов: " + directory +
                    ". Она сохранена без замены. Шрифт установлен в Windows: " + installedPath);

            try
            {
                Directory.CreateDirectory(directory);
                FindOrWriteFont(directory, File.ReadAllBytes(installedPath));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Не удалось поместить шрифт в каталог NX: " + directory +
                    ". Существующая настройка сохранена. " + ex.Message, ex);
            }
            if (createUserSetting)
                Environment.SetEnvironmentVariable(variable, directory, EnvironmentVariableTarget.User);
            S.SetEnvironmentVariableValue(variable, directory);
            return directory;
        }

        private static string FindOrWriteFont(string folder, byte[] bytes)
        {
            for (int i = 0; i < 100; i++)
            {
                string suffix = i == 0 ? "" : "_" + i.ToString(CultureInfo.InvariantCulture);
                string path = Path.Combine(folder, InstalledStem + suffix + ".ttf");
                if (File.Exists(path))
                {
                    if (BytesEqual(File.ReadAllBytes(path), bytes)) return path;
                    continue; // Leave a different existing file intact.
                }
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                        stream.Write(bytes, 0, bytes.Length);
                }
                catch (IOException)
                {
                    // Another simultaneous installer may have created this path.
                    if (File.Exists(path) && BytesEqual(File.ReadAllBytes(path), bytes)) return path;
                    throw;
                }
                if (!BytesEqual(File.ReadAllBytes(path), bytes))
                    throw new IOException("Не удалось проверить записанный файл шрифта: " + path);
                return path;
            }
            throw new IOException("Не удалось подобрать имя для файла шрифта.");
        }

        private static void RegisterForCurrentUser(string path)
        {
            IntPtr currentUser = new IntPtr(unchecked((int)0x80000001));
            IntPtr key; uint disposition;
            int error = RegCreateKeyExW(currentUser, RegistryPath, 0, null, 0, 0x0002u,
                IntPtr.Zero, out key, out disposition);
            if (error != 0)
                throw new InvalidOperationException("Не удалось открыть настройки шрифтов пользователя. Код Windows: " + error);
            try
            {
                // REG_SZ is UTF-16 and includes the terminating null character.
                byte[] value = Encoding.Unicode.GetBytes(path + "\0");
                error = RegSetValueExW(key, RegistryName, 0, 1, value, (uint)value.Length);
                if (error != 0)
                    throw new InvalidOperationException("Не удалось зарегистрировать шрифт для пользователя. Код Windows: " + error);
            }
            finally { RegCloseKey(key); }
        }

        private static void VerifySelectedFont(byte[] expected)
        {
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) throw new InvalidOperationException("Windows не создал контекст проверки шрифта.");
            IntPtr font = IntPtr.Zero, previous = IntPtr.Zero;
            bool selected = false;
            try
            {
                font = CreateFontW(-32, 0, 0, 0, 400, 0, 0, 0, 204, 4, 0, 0, 0, FontName);
                if (font == IntPtr.Zero) throw new InvalidOperationException("Windows не создал начертание " + FontFaceName + ".");
                previous = SelectObject(dc, font);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1))
                    throw new InvalidOperationException("Windows не выбрал начертание " + FontFaceName + ".");
                selected = true;
                // Verify the selected face by its actual font tables. GDI can
                // repack a legacy TTF, so whole-file byte counts are not a reliable
                // face identity check. Glyphs, cmap, name and metrics must match.
                foreach (string table in new string[] { "name", "cmap", "glyf", "hmtx", "hhea", "maxp", "loca", "post" })
                {
                    int offset, length; FindFontTable(expected, table, out offset, out length);
                    if (table == "hmtx")
                    {
                        int hhea, ignored, maxp; FindFontTable(expected, "hhea", out hhea, out ignored);
                        FindFontTable(expected, "maxp", out maxp, out ignored);
                        int metrics = ReadFontUInt16(expected, hhea + 34);
                        int glyphs = ReadFontUInt16(expected, maxp + 4);
                        length = checked(4 * metrics + 2 * (glyphs - metrics));
                    }
                    uint id = (uint)table[0] | ((uint)table[1] << 8) | ((uint)table[2] << 16) | ((uint)table[3] << 24);
                    uint size = GetFontData(dc, id, 0, null, 0);
                    if (size == UInt32.MaxValue || size < (uint)length)
                        throw new InvalidOperationException("Windows выбрал другой файл вместо " + FontFaceName + ". Таблица: " + table + ".");
                    byte[] actual = new byte[length];
                    if (GetFontData(dc, id, 0, actual, (uint)length) != (uint)length)
                        throw new InvalidOperationException("Не удалось проверить шрифт " + FontFaceName + ".");
                    for (int i = 0; i < length; i++)
                        if (actual[i] != expected[offset + i])
                            throw new InvalidOperationException("Windows подставляет другое начертание вместо " + FontFaceName +
                                ". Перезапустите NX и повторите запуск " + SCRIPT_VERSION + ".");
                }
            }
            finally
            {
                if (selected) SelectObject(dc, previous);
                if (font != IntPtr.Zero) DeleteObject(font);
                DeleteDC(dc);
            }
        }

        private static int ReadFontUInt16(byte[] bytes, int offset)
        { return (bytes[offset] << 8) | bytes[offset + 1]; }

        private static int ReadFontInt32(byte[] bytes, int offset)
        { return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]; }

        private static void FindFontTable(byte[] bytes, string name, out int offset, out int length)
        {
            int count = ReadFontUInt16(bytes, 4);
            for (int i = 0; i < count; i++)
            {
                int row = 12 + i * 16;
                if (Encoding.ASCII.GetString(bytes, row, 4) != name) continue;
                offset = ReadFontInt32(bytes, row + 8); length = ReadFontInt32(bytes, row + 12);
                if (offset < 0 || length < 0 || offset > bytes.Length - length)
                    throw new InvalidOperationException("Повреждена таблица шрифта: " + name);
                return;
            }
            throw new InvalidOperationException("В шрифте нет таблицы: " + name);
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static uint Crc32(byte[] bytes)
        {
            uint crc = 0xffffffffu;
            foreach (byte b in bytes)
            {
                crc ^= b;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 1u) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
            }
            return ~crc;
        }

        // The public package does not redistribute the ASCON font. Reuse only
        // an authorized external copy of the exact family/revision used by V1.34.
        private static byte[] ReadExternalFont()
        {
            List<string> folders = new List<string>();
            if (!String.IsNullOrEmpty(SettingsPath)) folders.Add(Path.GetDirectoryName(SettingsPath));
            string active = S.GetEnvironmentVariableValue("UGII_STANDARD_FONT_DIR") ?? "";
            string configured = Environment.GetEnvironmentVariable("UGII_STANDARD_FONT_DIR", EnvironmentVariableTarget.User) ?? "";
            if (!String.IsNullOrWhiteSpace(active)) folders.Add(active);
            if (!String.IsNullOrWhiteSpace(configured)) folders.Add(configured);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!String.IsNullOrEmpty(local))
            {
                folders.Add(Path.Combine(local, "Microsoft", "Windows", "Fonts"));
                folders.Add(Path.Combine(local, "Siemens", "NX_ESKD", "Fonts"));
            }
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!String.IsNullOrEmpty(windows)) folders.Add(Path.Combine(windows, "Fonts"));
            foreach (string item in folders)
            {
                string folder = Environment.ExpandEnvironmentVariables(item.Trim().Trim('"'));
                if (!Path.IsPathRooted(folder) || folder.IndexOf(';') >= 0 || !Directory.Exists(folder)) continue;
                for (int i = 0; i < 100; i++)
                {
                    string suffix = i == 0 ? "" : "_" + i.ToString(CultureInfo.InvariantCulture);
                    string path = Path.Combine(folder, InstalledStem + suffix + ".ttf");
                    try
                    {
                        if (!File.Exists(path) || new FileInfo(path).Length != FontByteCount) continue;
                        byte[] bytes = File.ReadAllBytes(path);
                        if (bytes.Length == FontByteCount && Crc32(bytes) == FontCrc32) return bytes;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            throw new InvalidOperationException("Нужен внешний шрифт " + InstalledStem + ".ttf совместимой редакции.\n" +
                "Шрифт не входит в публичный комплект. Используйте разрешённую вам копию из прежней установки " +
                "или обратитесь к автору. Произвольный шрифт с похожим названием не подходит.\n" +
                "Положите совместимый файл рядом со скриптом и запустите журнал снова. См. docs/configuration.md.");
        }
    }

    public static int GetUnloadOption(string dummy) { return (int)Session.LibraryUnloadOption.Immediately; }
}
