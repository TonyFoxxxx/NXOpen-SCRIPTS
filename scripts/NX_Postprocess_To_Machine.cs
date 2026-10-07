// NX_Postprocess_To_Machine.cs
// SCRIPT_VERSION: V1.40
// Configure postprocessor paths and machine folders in the UI; optional operation numbering.
// Preserve NC processing, modal validation, output paths and other INI settings.
// Siemens NX / Designcenter, Windows. C# journal with an external INI.
// Keep NX_Postprocess_To_Machine.ini next to this journal.
// v1.21 | 2026-09-25: restore external-drive picker and volume checks.
// Discovery runs only when the dedicated button is clicked.
// Keep the NX folder tree, O-only output and explicit Output action.
// v1.20 | 2026-09-25: NX-order folder tree, cascading branch checkboxes;
// structural parents select programs but are never posted. Output O-folders only.
// Nested O-programs own their operations, preventing duplicate output.
// v1.19 | 2026-09-25: remove the external-drive button and discovery code.
// Select any available drive through the ordinary folder browser.
// No device enumeration or device-identity queries before/during output.
// v1.18 | 2026-09-25: choose O-prefixed program folders with checkboxes;
// explicit Output button after destination selection. Show original errors
// without NXMessageBox, including operation stage; no diagnostic files.
// v1.17 | 2026-09-25: remove compile-time DriveInfo/DriveType dependency.
// Enumerate volumes and read drive kind/free space with built-in Windows APIs.
// v1.16 | 2026-09-25: external-drive picker, refresh, USB Fixed disks;
// recheck volume identity before posting and each publish. INI unchanged.
// v1.15 | 2026-09-24: inspect inherited MCS per output program before posting;
// red Yes/No confirmation for mixed or unreadable MCS, default No.
// v1.14 | 2026-09-24: embedded folded-hands image on the acknowledgement
// button; no dependence on emoji fonts, no icon files or runtime downloads.
// v1.13 | 2026-09-24: ASCII-only source; Unicode escapes preserve Russian UI
// text, emoji and diameter symbols across NX journal source encodings.
// Do not convert these escapes to raw Unicode when editing this journal.
// v1.12 | 2026-09-24: allow continuation for every diameter check error;
// red error window, green acknowledgement button and red continuation button.
// No reports; only requested NC/BIN outputs persist. Temporary staging is cleaned.
// v1.11 | 2026-09-24: optional continuation for zero tool diameter settings.
// v1.10 | 2026-09-24: add the requested emoji to the acknowledgement button.
// v1.9 | 2026-09-24: check all project tool name diameters against NX settings;
// mismatches stop before posting, with a single acknowledgement button.
// v1.8 | 2026-09-24: separate fixed O prefix and digits-only number fields.
// v1.7 | 2026-09-24: remember the manual output folder, show its path, reuse it.
// v1.6 | 2026-09-24: folders/selected operations, manual O numbers, local folder
// picker, fixed BIN size, optional remembered project copy, no output backups.
// v1.4 | 2026-09-22: explicit char[] for TrimEnd avoids params-collection
// overload binding in NX journal compilers. INI format is unchanged.
// v1.3: INI controls machine roots, explicit destinations, posts, extensions
// Read on each launch or with the Reload button.
// Relative paths are resolved against the INI directory; no CWD fallback.
// Save post-path edits to the same INI after explicit confirmation in the UI.
// Choose whole O-prefixed program folders in the opening checklist.
// Project copy is optional. Only O-header digits change when explicitly assigned.
// Existing outputs require confirmation; replacements do NOT receive backups.
// No report files or persistent result folders; transient staging is removed.
// A post that emits extra NC sidecars is stopped before publishing.
// Last manual folder and project-copy checkbox are saved per Windows user.
// No direct WinForms/Drawing/Crypto references; supports NX's .NET 10 compiler.
// Native NX execution and SMB access cannot be exercised in this environment.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using IOPath = System.IO.Path;

#if !NX_POST_CORE_ONLY
using NXOpen;
using NXOpen.UF;
using NXOpen.Utilities;
using CamSetup = NXOpen.CAM.CAMSetup;
using CamObject = NXOpen.CAM.CAMObject;
using NCGroup = NXOpen.CAM.NCGroup;

public class NX_Postprocess_To_Machine
{
    private static Session session;

    public static void Main(string[] args)
    {
        bool published = false;
        List<OutputCopy> copies = new List<OutputCopy>();
        object owner = null;
        string stage = "\u041E\u0442\u043A\u0440\u044B\u0442\u0438\u0435 CAM-\u043F\u0440\u043E\u0435\u043A\u0442\u0430";
        try
        {
            session = Session.GetSession();
            Part work = session.Parts.Work;
            if (work == null) throw new InvalidOperationException("\u041E\u0442\u043A\u0440\u043E\u0439\u0442\u0435 CAM-\u043F\u0440\u043E\u0435\u043A\u0442.");
            CamSetup setup = work.CAMSetup;
            if (setup == null) throw new InvalidOperationException("\u0412 \u043F\u0440\u043E\u0435\u043A\u0442\u0435 \u043D\u0435\u0442 CAM-\u043E\u0431\u0440\u0430\u0431\u043E\u0442\u043A\u0438.");
            stage = "\u041F\u043E\u0434\u0433\u043E\u0442\u043E\u0432\u043A\u0430 \u043E\u043A\u043E\u043D \u0441\u043A\u0440\u0438\u043F\u0442\u0430";
            OperationSelectionSnapshot selectedOperations = OperationSelectionSnapshot.Capture();
            owner = RuntimeForms.CreateOwner();
            stage = "\u0412\u044B\u0431\u043E\u0440 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C \u0434\u043B\u044F \u0432\u044B\u0432\u043E\u0434\u0430";
            List<ProgramJob> jobs;
            bool numberOperations;
            using (ProgramFolderPicker dialog = new ProgramFolderPicker(setup.GetRoot(CamSetup.View.ProgramOrder), selectedOperations))
            {
                if (dialog.ShowDialog(owner) != "OK") return;
                jobs = dialog.Jobs;
                numberOperations = dialog.NumberOperations;
            }
            // Project-wide: include unused tools and tools outside the selected jobs.
            // This gate runs after program selection, before configuration, posting and file writes.
            stage = "\u041F\u0440\u043E\u0432\u0435\u0440\u043A\u0430 \u0434\u0438\u0430\u043C\u0435\u0442\u0440\u043E\u0432 \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442\u0430";
            ToolDiameterInspection diameterCheck = ToolDiameterInspection.Inspect(setup);
            if (!diameterCheck.Passed)
            {
                stage = "\u041E\u043A\u043D\u043E \u043F\u0440\u043E\u0432\u0435\u0440\u043A\u0438 \u0434\u0438\u0430\u043C\u0435\u0442\u0440\u043E\u0432 \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442\u0430";
                string response;
                using (ToolDiameterErrorDialog dialog = new ToolDiameterErrorDialog(diameterCheck))
                    response = dialog.ShowDialog(owner);
                if (response != "Ignore") return;
                // Explicitly skip this diameter check for the current run only.
                // Do not change tool data or remember the user's decision.
            }
            stage = "\u041F\u0440\u043E\u0432\u0435\u0440\u043A\u0430 MCS";
            if (!ConfirmProgramMcs(jobs, owner)) return;
            stage = "\u041E\u043F\u0440\u0435\u0434\u0435\u043B\u0435\u043D\u0438\u0435 \u043F\u0443\u0442\u0438 \u043F\u0440\u043E\u0435\u043A\u0442\u0430";
            string projectFile = work.FullPath;
            string projectDirectory = "";
            try { projectDirectory = PostFiles.ProjectDirectory(projectFile); }
            catch (Exception) { /* A saved filesystem part is required only for the project copy. */ }
            stage = "\u041E\u043F\u0440\u0435\u0434\u0435\u043B\u0435\u043D\u0438\u0435 \u043F\u0443\u0442\u0438 INI";
            string configPath = RouterConfig.ForJournal(session.ExecutingJournal);
            RouterConfig config; PostDefinition[] programPosts = null; MachineChoice choice;
            stage = "\u0427\u0442\u0435\u043D\u0438\u0435 \u0441\u043E\u0445\u0440\u0430\u043D\u0451\u043D\u043D\u044B\u0445 \u043D\u0430\u0441\u0442\u0440\u043E\u0435\u043A";
            RouterPreferences preferences = RouterPreferences.Load(RouterPreferences.DefaultPath());
            while (true)
            {
                stage = "\u0412\u044B\u0431\u043E\u0440 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430";
                using (PostPicker dialog = new PostPicker(configPath, GetNXEnvironment, jobs, programPosts))
                {
                    if (dialog.ShowDialog(owner) != "OK") return;
                    programPosts = dialog.SelectedPosts; config = dialog.Config;
                }
                stage = "\u041F\u0440\u043E\u0432\u0435\u0440\u043A\u0430 \u0444\u0430\u0439\u043B\u043E\u0432 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430";
                ProgramPostAssignments.Validate(programPosts, jobs.Count);
                List<string> machineWarnings;
                stage = "\u0427\u0442\u0435\u043D\u0438\u0435 \u043F\u0430\u043F\u043E\u043A \u0441\u0442\u0430\u043D\u043A\u043E\u0432";
                List<MachineTarget> machines = config.GetAvailableMachines(out machineWarnings);
                stage = "\u0412\u044B\u0431\u043E\u0440 \u043C\u0435\u0441\u0442\u0430 \u0432\u044B\u0432\u043E\u0434\u0430 \u0438 \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u044F";
                using (MachinePicker dialog = new MachinePicker(jobs, programPosts, machines, machineWarnings, config, projectFile, projectDirectory, preferences, GetNXEnvironment))
                {
                    string response = dialog.ShowDialog(owner);
                    if (response == "Retry") continue;
                    if (response != "OK") return;
                    choice = dialog.Choice;
                }
                break;
            }
            stage = numberOperations ? "\u041D\u0443\u043C\u0435\u0440\u0430\u0446\u0438\u044F \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439, \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u0435 \u0438 \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u0435 \u0423\u041F/BIN" : "\u041F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u0435 \u0438 \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u0435 \u0423\u041F/BIN";
            string message = OperationNumbering.Run(session, jobs, numberOperations,
                delegate { return PostprocessPrograms(setup, jobs, programPosts, choice, projectFile, projectDirectory, owner, copies, ref published); },
                delegate { return published; });
            stage = "\u041F\u043E\u043A\u0430\u0437 \u0440\u0435\u0437\u0443\u043B\u044C\u0442\u0430\u0442\u0430 \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u044F";
            if (message != null)
                RuntimeForms.Message(owner, message, ScriptInfo.WindowTitle("\u0420\u0435\u0437\u0443\u043B\u044C\u0442\u0430\u0442"), "OK", "Information", "Button1");
        }
        catch (Exception ex)
        {
            string text = (published ? "\u0424\u0430\u0439\u043B\u044B \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u044B, \u043D\u043E \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0438\u0435 \u0441\u043A\u0440\u0438\u043F\u0442\u0430 \u0432\u044B\u0437\u0432\u0430\u043B\u043E \u043E\u0448\u0438\u0431\u043A\u0443.\u000A\u000A" : "\u041F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u0435 \u043D\u0435 \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u043E.\u000A\u000A") + "\u042D\u0442\u0430\u043F: " + stage + "\n\n" + RuntimeForms.ErrorDetails(ex);
            string saved = PostFiles.SavedCopiesDescription(copies);
            text += saved.Length > 0 ? "\u000A\u000A\u0423\u0436\u0435 \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u043E (\u043A\u043E\u043F\u0438\u0438 \u043E\u0441\u0442\u0430\u0432\u043B\u0435\u043D\u044B):\u000A" + saved : "\u000A\u000A\u0412 \u043A\u043E\u043D\u0435\u0447\u043D\u044B\u0435 \u043F\u0430\u043F\u043A\u0438 \u0432\u044B\u0432\u043E\u0434\u0430 \u043D\u0438\u0447\u0435\u0433\u043E \u043D\u0435 \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u043E.";
            RuntimeForms.ReportFailure(owner, text, ScriptInfo.WindowTitle("\u041E\u0448\u0438\u0431\u043A\u0430"), ex);
        }
        finally { try { RuntimeForms.ReleaseOwner(owner); } catch { } }
    }

    internal static bool ConfirmProgramMcs(List<ProgramJob> jobs, object owner)
    {
        ProgramMcsInspection check = ProgramMcsInspection.Inspect(jobs);
        if (check.Passed) return true;
        using (ProgramMcsErrorDialog dialog = new ProgramMcsErrorDialog(check))
            return dialog.ShowDialog(owner) == "Yes";
    }

    // The complete output phase owns its temporary directory and always removes it.
    // Only the requested NC/BIN outputs are published; no reports are written.
    internal static string PostprocessPrograms(CamSetup setup, List<ProgramJob> jobs, PostDefinition[] posts,
        MachineChoice choice, string projectFile, string projectDirectory, object owner,
        List<OutputCopy> copies, ref bool published)
    {
        string workingDirectory = null;
        Exception processingError = null;
        List<PreparedOutput> prepared = new List<PreparedOutput>();
        try
        {
            if (choice.ExternalDrive != null) choice.ExternalDrive.EnsurePresent();
            string machineDirectory = choice.Target.DirectoryPath;
            ProgramPostAssignments.Validate(posts, jobs.Count);
            if (choice.ProgramNames == null || choice.ProgramNames.Length != jobs.Count ||
                choice.ProgramExtensions == null || choice.ProgramExtensions.Length != jobs.Count)
                throw new InvalidOperationException("\u0421\u043F\u0438\u0441\u043E\u043A \u0438\u043C\u0451\u043D \u0438\u043B\u0438 \u0440\u0430\u0441\u0448\u0438\u0440\u0435\u043D\u0438\u0439 \u043D\u0435 \u0441\u043E\u043E\u0442\u0432\u0435\u0442\u0441\u0442\u0432\u0443\u0435\u0442 \u043A\u043E\u043B\u0438\u0447\u0435\u0441\u0442\u0432\u0443 \u0423\u041F.");
            string[] extensions = new string[jobs.Count];
            for (int i = 0; i < extensions.Length; i++) extensions[i] = PostFiles.Extension(choice.ProgramExtensions[i]);
            List<string> names = new List<string>(choice.ProgramNames);
            string[][] callTargets = ProgramCallChain.Targets(choice.ProgramNames, choice.ProgramCallOrder);
            WorkOffsetPrograms.Validate(choice.WorkOffsets);
            if (choice.SaveToProject) projectDirectory = PostFiles.ProjectDirectory(projectFile);
            // Validate ALL target names, including project/BIN collisions, before invoking NX.
            HashSet<string> destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < names.Count; i++)
            {
                string fileName = names[i] + extensions[i];
                PostFiles.ValidateLeaf(fileName);
                if (choice.BinMode != FanucBinMode.Off && String.Equals(fileName, FanucBin.FileName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("\u0418\u043C\u044F \u0442\u0435\u043A\u0441\u0442\u043E\u0432\u043E\u0439 \u0423\u041F \u0441\u043E\u0432\u043F\u0430\u0434\u0430\u0435\u0442 \u0441 FANUCPRG.BIN. \u0418\u0437\u043C\u0435\u043D\u0438\u0442\u0435 \u0438\u043C\u044F/\u0440\u0430\u0441\u0448\u0438\u0440\u0435\u043D\u0438\u0435.");
                foreach (OutputCopy copy in PostFiles.CreateCopies(projectFile, machineDirectory, fileName, choice.SaveToProject))
                    if (!destinations.Add(copy.Destination)) throw new InvalidOperationException("\u041F\u043E\u0432\u0442\u043E\u0440\u044F\u0435\u0442\u0441\u044F \u043F\u0443\u0442\u044C \u0440\u0435\u0437\u0443\u043B\u044C\u0442\u0430\u0442\u0430: " + copy.Destination);
            }
            workingDirectory = PostFiles.NewTemporaryDirectory();
            List<FanucProgram> incoming = new List<FanucProgram>();
            // Whole groups remain whole; partial jobs pass ONLY the selected operations.
            // If any post or validation fails, no destination has been touched.
            for (int i = 0; i < jobs.Count; i++)
            {
                ProgramJob job = jobs[i]; PostDefinition post = posts[i];
                string name = names[i], fileName = name + extensions[i];
                string directory = IOPath.Combine(workingDirectory, (i + 1).ToString("D3", CultureInfo.InvariantCulture) + "_" + name);
                Directory.CreateDirectory(directory);
                string generated = IOPath.Combine(directory, fileName);
                setup.PostprocessWithPostprocessor(job.Objects.ToArray(), post.EventFile, post.DefinitionFile, generated,
                    choice.Units, CamSetup.PostprocessSettingsOutputWarning.No, CamSetup.PostprocessSettingsReviewTool.Off);
                PostFiles.ValidateOutput(directory, generated);
                if (choice.AssignNames || job.OutputName != null) ProgramNames.AssignNumber(generated, name);
                WorkOffsetPrograms.Apply(generated, name, choice.WorkOffsets);
                if (callTargets != null) ProgramCallChain.Apply(generated, name, callTargets[i]);
                if (choice.ExternalDrive != null) choice.ExternalDrive.EnsurePresent();
                PreparedOutput item = new PreparedOutput(generated, PostFiles.CreateCopies(projectFile, machineDirectory, fileName, choice.SaveToProject));
                PostFiles.SnapshotCopies(item.Copies); prepared.Add(item); copies.AddRange(item.Copies);
                if (choice.BinMode != FanucBinMode.Off) incoming.Add(FanucBin.FromNc(generated, name));
            }
            if (choice.BinMode != FanucBinMode.Off)
            {
                // Detect duplicate O numbers before reading any old image.
                FanucBin.Combine(null, incoming);
                List<OutputCopy> binCopies = PostFiles.CreateCopies(projectFile, machineDirectory, FanucBin.FileName, choice.SaveToProject);
                if (choice.ExternalDrive != null) choice.ExternalDrive.EnsurePresent();
                PostFiles.SnapshotCopies(binCopies);
                string binDirectory = IOPath.Combine(workingDirectory, "BIN"); Directory.CreateDirectory(binDirectory);
                for (int i = 0; i < binCopies.Count; i++)
                {
                    if (choice.ExternalDrive != null) choice.ExternalDrive.EnsurePresent();
                    OutputCopy copy = binCopies[i];
                    FanucImage existing = choice.BinMode == FanucBinMode.Merge && copy.Approved.Exists ? FanucBin.Read(copy.Destination) : null;
                    FanucImage combined = FanucBin.Combine(existing, incoming);
                    string candidate = IOPath.Combine(binDirectory, (i + 1).ToString(CultureInfo.InvariantCulture) + "_" + FanucBin.FileName);
                    FanucBin.Write(candidate, combined, choice.BinSizeMB);
                    if (!copy.Approved.Matches(FileSnapshot.Read(copy.Destination)))
                        throw new IOException("BIN \u0438\u0437\u043C\u0435\u043D\u0438\u043B\u0441\u044F \u0432\u043E \u0432\u0440\u0435\u043C\u044F \u043F\u043E\u0434\u0433\u043E\u0442\u043E\u0432\u043A\u0438. \u041F\u043E\u0432\u0442\u043E\u0440\u0438\u0442\u0435 \u0437\u0430\u043F\u0443\u0441\u043A:\u000A" + copy.Destination);
                    List<OutputCopy> one = new List<OutputCopy>(); one.Add(copy);
                    prepared.Add(new PreparedOutput(candidate, one)); copies.Add(copy);
                }
            }
            List<string> conflicts = new List<string>();
            foreach (OutputCopy copy in copies) if (copy.Approved.Exists) conflicts.Add(copy.Destination);
            string binSummary = choice.BinMode == FanucBinMode.Off ? "BIN: \u0432\u044B\u043A\u043B\u044E\u0447\u0435\u043D" :
                (choice.BinMode == FanucBinMode.Merge ? "BIN: \u0434\u043E\u0431\u0430\u0432\u043B\u0435\u043D\u0438\u0435/\u043E\u0431\u043D\u043E\u0432\u043B\u0435\u043D\u0438\u0435; \u043E\u0441\u0442\u0430\u043B\u044C\u043D\u044B\u0435 \u0423\u041F \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u044B" : "BIN: \u043D\u043E\u0432\u044B\u0439 \u043A\u043E\u043D\u0442\u0435\u0439\u043D\u0435\u0440 \u0422\u041E\u041B\u042C\u041A\u041E \u0438\u0437 \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0445 \u0423\u041F");
            if (choice.BinMode != FanucBinMode.Off)
                binSummary += "\u000A\u0420\u0430\u0437\u043C\u0435\u0440 \u0423\u041F \u0432 BIN: " + choice.BinSizeMB + " \u041C\u0411; \u0444\u0430\u0439\u043B: " + FanucBin.FileLengthForMB(choice.BinSizeMB).ToString("N0", CultureInfo.CurrentCulture) + " \u0431\u0430\u0439\u0442";
            if (conflicts.Count > 0)
            {
                int shown = Math.Min(12, conflicts.Count);
                string list = String.Join("\n", conflicts.GetRange(0, shown).ToArray());
                if (shown < conflicts.Count) list += "\u000A... \u0438 \u0435\u0449\u0451 " + (conflicts.Count - shown) + " \u0444\u0430\u0439\u043B\u043E\u0432.";
                string question = "\u0423\u0436\u0435 \u0441\u0443\u0449\u0435\u0441\u0442\u0432\u0443\u044E\u0442 \u0444\u0430\u0439\u043B\u044B:\u000A" + list + "\n\n" + binSummary;
                if (choice.BinMode == FanucBinMode.New)
                    question += "\u000A\u0412\u041D\u0418\u041C\u0410\u041D\u0418\u0415: \u0434\u0440\u0443\u0433\u0438\u0435 \u0423\u041F \u0438\u0437 \u0441\u0442\u0430\u0440\u043E\u0433\u043E BIN \u0431\u0443\u0434\u0443\u0442 \u0443\u0434\u0430\u043B\u0435\u043D\u044B. \u0420\u0435\u0437\u0435\u0440\u0432\u043D\u043E\u0439 \u043A\u043E\u043F\u0438\u0438 \u043D\u0435 \u0431\u0443\u0434\u0435\u0442.";
                if (choice.BinMode == FanucBinMode.Merge)
                    question += "\u000A\u0421\u043E\u0432\u043F\u0430\u0434\u0430\u044E\u0449\u0438\u0435 \u043D\u043E\u043C\u0435\u0440\u0430 O \u0431\u0443\u0434\u0443\u0442 \u0437\u0430\u043C\u0435\u043D\u0435\u043D\u044B \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u043C\u0438 \u0423\u041F.";
                question += "\u000A\u000A\u0417\u0430\u043C\u0435\u043D\u0438\u0442\u044C \u0411\u0415\u0417 \u0440\u0435\u0437\u0435\u0440\u0432\u043D\u044B\u0445 \u043A\u043E\u043F\u0438\u0439?";
                if (RuntimeForms.Message(owner, question, ScriptInfo.WindowTitle("\u0421\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u0435"), "YesNo", "Question", "Button2") != "Yes") return null;
            }
            if (choice.ExternalDrive != null) choice.ExternalDrive.EnsurePresent();
            // Check the complete batch before the first write; every individual copy rechecks.
            foreach (OutputCopy copy in copies)
                if (!copy.Approved.Matches(FileSnapshot.Read(copy.Destination)))
                    throw new IOException("\u0424\u0430\u0439\u043B \u0438\u0437\u043C\u0435\u043D\u0438\u043B\u0441\u044F \u043F\u043E\u0441\u043B\u0435 \u043F\u043E\u0434\u0433\u043E\u0442\u043E\u0432\u043A\u0438. \u041F\u043E\u0432\u0442\u043E\u0440\u0438\u0442\u0435 \u0437\u0430\u043F\u0443\u0441\u043A:\u000A" + copy.Destination);
            foreach (PreparedOutput item in prepared) PostFiles.PublishCopies(item.Source, item.Copies, choice.ExternalDrive);
            published = true;
            string message = "\u0421\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u043E \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C: " + jobs.Count + "\n" + String.Join(", ", names.ToArray()) +
                "\u000A\u000A\u041A\u043E\u043F\u0438\u044F \u0443 \u043F\u0440\u043E\u0435\u043A\u0442\u0430: " + (choice.SaveToProject ? projectDirectory : "\u043E\u0442\u043A\u043B\u044E\u0447\u0435\u043D\u0430") + "\u000A\u041F\u0430\u043F\u043A\u0430 \u0432\u044B\u0432\u043E\u0434\u0430: " + machineDirectory + "\n\n" + binSummary;
            message += "\n\n" + ProgramPostAssignments.Describe(choice.ProgramNames, posts, extensions);
            message += "\n\n" + WorkOffsetPrograms.Describe(choice.WorkOffsets);
            if (choice.ProgramCallOrder != null)
                message += "\n\n\u041F\u043E\u0440\u044F\u0434\u043E\u043A \u0432\u044B\u0437\u043E\u0432\u043E\u0432: " + ProgramCallChain.Describe(choice.ProgramNames, choice.ProgramCallOrder) +
                    "\n\u0417\u0430\u043F\u0443\u0441\u043A\u0430\u0442\u044C \u0441 " + choice.ProgramNames[choice.ProgramCallOrder[0]] + ".";
            return message;
        }
        catch (Exception ex) { processingError = ex; throw; }
        finally
        {
            try { PostFiles.DeleteTemporaryDirectory(workingDirectory); }
            catch (Exception cleanupError)
            {
                string message = "\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0443\u0434\u0430\u043B\u0438\u0442\u044C \u0432\u0440\u0435\u043C\u0435\u043D\u043D\u044B\u0435 \u0440\u0430\u0431\u043E\u0447\u0438\u0435 \u0444\u0430\u0439\u043B\u044B:\u000A" + workingDirectory + "\n\n" + cleanupError.Message;
                if (processingError != null) message = RuntimeForms.ActualMessage(processingError) + "\n\n" + message;
                throw new IOException(message, processingError ?? cleanupError);
            }
        }
    }

    private static string GetNXEnvironment(string name)
    {
        string value = null;
        try { value = session.GetEnvironmentVariableValue(name); }
        catch { /* Some variables are absent in an NX installation. */ }
        if (String.IsNullOrEmpty(value)) value = Environment.GetEnvironmentVariable(name);
        return value ?? "";
    }

    public static int GetUnloadOption(string dummy)
    {
        return (int)Session.LibraryUnloadOption.Immediately;
    }
}

internal sealed class ToolDiameterInspection
{
    internal const string MismatchMessage = "\u0412 \u043F\u0440\u043E\u0435\u043A\u0442\u0435 \u043F\u0440\u0438\u0441\u0443\u0442\u0441\u0442\u0432\u0443\u044E\u0442 \u0440\u0430\u0437\u043B\u0438\u0447\u0438\u044F \u0434\u0438\u0430\u043C\u0435\u0442\u0440\u043E\u0432 \u0432 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u0438 \u0438 \u043D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0430\u0445 \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442\u0430";
    internal readonly List<string> Differences = new List<string>();
    internal readonly List<string> ReadErrors = new List<string>();
    internal bool Passed { get { return Differences.Count == 0 && ReadErrors.Count == 0; } }
    internal string Message
    {
        get { return Differences.Count > 0 ? MismatchMessage : "\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043F\u0440\u043E\u0432\u0435\u0440\u0438\u0442\u044C \u0434\u0438\u0430\u043C\u0435\u0442\u0440\u044B \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442\u043E\u0432 \u043F\u0440\u043E\u0435\u043A\u0442\u0430"; }
    }
    internal string Details()
    {
        string text = String.Join("\r\n\r\n", Differences.ToArray());
        if (ReadErrors.Count > 0)
            text += (text.Length > 0 ? "\r\n\r\n" : "") + "\u041E\u0448\u0438\u0431\u043A\u0438 \u043F\u0440\u043E\u0432\u0435\u0440\u043A\u0438:\u000D\u000A" + String.Join("\r\n\r\n", ReadErrors.ToArray());
        return text;
    }
    internal static ToolDiameterInspection Inspect(CamSetup setup)
    {
        ToolDiameterInspection result = new ToolDiameterInspection();
        HashSet<Tag> seen = new HashSet<Tag>();
        UFSession uf = UFSession.GetUFSession();
        try
        {
            foreach (NCGroup group in setup.CAMGroupCollection)
            {
                NXOpen.CAM.Tool tool = group as NXOpen.CAM.Tool;
                if (tool == null || !seen.Add(tool.Tag)) continue;
                string name = "\u0418\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442 " + tool.Tag;
                try
                {
                    name = tool.Name;
                    double named;
                    if (!ToolDiameterNames.TryRead(name, out named)) continue;
                    double actual;
                    // Read the tool's own CAM diameter, never its holder, operation,
                    // display name or a builder default. UF lengths use part units.
                    uf.Param.AskDoubleValue(tool.Tag, UFConstants.UF_PARAM_TL_DIAMETER, out actual);
                    if (actual != 0 && !ToolDiameterNames.IsPositiveFinite(actual))
                        throw new InvalidOperationException("\u0412 \u043D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0430\u0445 \u043E\u0442\u0441\u0443\u0442\u0441\u0442\u0432\u0443\u0435\u0442 \u043F\u043E\u043B\u043E\u0436\u0438\u0442\u0435\u043B\u044C\u043D\u044B\u0439 \u0434\u0438\u0430\u043C\u0435\u0442\u0440.");
                    if (actual == 0 || !ToolDiameterNames.Equal(named, actual))
                        result.Differences.Add(DisplayName(name) + "\u000D\u000A\u0412 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u0438: " + ToolDiameterNames.Format(named) +
                            "; \u0432 \u043D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0430\u0445: " + ToolDiameterNames.Format(actual));
                }
                catch (Exception ex)
                {
                    // An unreadable or ambiguous diameter is never treated as a match.
                    result.ReadErrors.Add(DisplayName(name) + "\r\n" + RuntimeForms.ActualMessage(ex));
                }
            }
        }
        catch (Exception ex)
        { result.ReadErrors.Add("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043F\u0440\u043E\u0447\u0438\u0442\u0430\u0442\u044C \u0432\u0441\u0435 \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442\u044B \u043F\u0440\u043E\u0435\u043A\u0442\u0430: " + RuntimeForms.ActualMessage(ex)); }
        return result;
    }
    private static string DisplayName(string name)
    { return (name ?? "(\u0431\u0435\u0437 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u044F)").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\0", "\\0"); }
}

// A separate dialog provides explicit colored choices with a safe default.
internal sealed class ToolDiameterErrorDialog : IDisposable
{
    private readonly object window = RuntimeForms.New("Form");
    private object dialogFont;
    internal ToolDiameterErrorDialog(ToolDiameterInspection result)
    {
        RuntimeForms.Set(window, "Text", ScriptInfo.WindowTitle("\u041E\u0448\u0438\u0431\u043A\u0430 \u043F\u0440\u043E\u0432\u0435\u0440\u043A\u0438 \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442\u0430"));
        RuntimeForms.SetValue(window, "ClientSize", 720, 434);
        RuntimeForms.SetValue(window, "AutoScaleDimensions", 96F, 96F);
        RuntimeForms.SetEnum(window, "AutoScaleMode", "Dpi");
        RuntimeForms.SetValue(window, "Font", "Segoe UI", 10F);
        dialogFont = RuntimeForms.Get(window, "Font");
        RuntimeForms.SetEnum(window, "StartPosition", "CenterParent");
        RuntimeForms.SetEnum(window, "FormBorderStyle", "FixedDialog");
        RuntimeForms.Set(window, "ControlBox", false);
        RuntimeForms.Set(window, "MinimizeBox", false);
        RuntimeForms.Set(window, "MaximizeBox", false);
        RuntimeForms.Set(window, "ShowInTaskbar", false);
        RuntimeForms.SetColor(window, "BackColor", 255, 255, 255);
        RuntimeForms.SetColor(window, "ForeColor", 185, 28, 28);

        object message = RuntimeForms.New("Label");
        RuntimeForms.Set(message, "Text", result.Message);
        RuntimeForms.Set(message, "UseMnemonic", false);
        Place(message, 24, 20, 672, 60);
        object details = RuntimeForms.New("TextBox");
        RuntimeForms.Set(details, "Multiline", true);
        RuntimeForms.Set(details, "ReadOnly", true);
        RuntimeForms.SetEnum(details, "BorderStyle", "FixedSingle");
        RuntimeForms.SetColor(details, "BackColor", 255, 255, 255);
        RuntimeForms.SetColor(details, "ForeColor", 185, 28, 28);
        RuntimeForms.Set(details, "TabStop", false);
        RuntimeForms.SetEnum(details, "ScrollBars", "Vertical");
        RuntimeForms.Set(details, "Text", result.Details());
        Place(details, 24, 88, 672, 202);
        object hint = RuntimeForms.New("Label");
        RuntimeForms.Set(hint, "Text", "\u0414\u0438\u0430\u043C\u0435\u0442\u0440\u044B \u0443\u043A\u0430\u0437\u0430\u043D\u044B \u0432 \u0435\u0434\u0438\u043D\u0438\u0446\u0430\u0445 \u043F\u0440\u043E\u0435\u043A\u0442\u0430 NX.\r\n\u041F\u0440\u0438 \u043F\u0440\u043E\u0434\u043E\u043B\u0436\u0435\u043D\u0438\u0438 \u043E\u0448\u0438\u0431\u043A\u0438 \u043F\u0440\u043E\u0432\u0435\u0440\u043A\u0438 \u0434\u0438\u0430\u043C\u0435\u0442\u0440\u043E\u0432 \u0431\u0443\u0434\u0443\u0442 \u043F\u0440\u043E\u043F\u0443\u0449\u0435\u043D\u044B.");
        Place(hint, 24, 304, 672, 44);
        object question = RuntimeForms.New("Label");
        RuntimeForms.Set(question, "Text", "\u041F\u0440\u043E\u0434\u043E\u043B\u0436\u0438\u0442\u044C?");
        RuntimeForms.SetEnum(question, "TextAlign", "MiddleLeft");
        Place(question, 24, 366, 340, 44);

        object noButton = RuntimeForms.New("Button");
        RuntimeForms.Set(noButton, "Text", "\u041D\u0435\u0442");
        RuntimeForms.Set(noButton, "AccessibleName", "\u041D\u0435\u0442");
        RuntimeForms.Set(noButton, "AccessibleDescription", "\u041E\u0441\u0442\u0430\u043D\u043E\u0432\u0438\u0442\u044C \u0432\u044B\u043F\u043E\u043B\u043D\u0435\u043D\u0438\u0435");
        RuntimeForms.Set(noButton, "TabIndex", 0);
        RuntimeForms.SetEnum(noButton, "FlatStyle", "Flat");
        RuntimeForms.Set(RuntimeForms.Get(noButton, "FlatAppearance"), "BorderSize", 0);
        RuntimeForms.Set(noButton, "UseMnemonic", false);
        RuntimeForms.Set(noButton, "UseVisualStyleBackColor", false);
        RuntimeForms.SetColor(noButton, "BackColor", 21, 128, 61);
        RuntimeForms.SetColor(noButton, "ForeColor", 255, 255, 255);
        RuntimeForms.SetColor(RuntimeForms.Get(noButton, "FlatAppearance"), "MouseOverBackColor", 22, 101, 52);
        RuntimeForms.SetColor(RuntimeForms.Get(noButton, "FlatAppearance"), "MouseDownBackColor", 20, 83, 45);
        RuntimeForms.SetEnum(noButton, "DialogResult", "OK");
        Place(noButton, 420, 366, 132, 44);

        object yesButton = RuntimeForms.New("Button");
        RuntimeForms.Set(yesButton, "Text", "\u0414\u0430");
        RuntimeForms.Set(yesButton, "AccessibleName", "\u0414\u0430");
        RuntimeForms.Set(yesButton, "AccessibleDescription", "\u041F\u0440\u043E\u0434\u043E\u043B\u0436\u0438\u0442\u044C, \u043F\u0440\u043E\u043F\u0443\u0441\u0442\u0438\u0432 \u043E\u0448\u0438\u0431\u043A\u0438 \u043F\u0440\u043E\u0432\u0435\u0440\u043A\u0438");
        RuntimeForms.Set(yesButton, "TabIndex", 1);
        RuntimeForms.SetEnum(yesButton, "FlatStyle", "Flat");
        RuntimeForms.Set(RuntimeForms.Get(yesButton, "FlatAppearance"), "BorderSize", 0);
        RuntimeForms.Set(yesButton, "UseMnemonic", false);
        RuntimeForms.Set(yesButton, "UseVisualStyleBackColor", false);
        RuntimeForms.SetColor(yesButton, "BackColor", 224, 32, 32);
        RuntimeForms.SetColor(yesButton, "ForeColor", 255, 255, 255);
        RuntimeForms.SetColor(RuntimeForms.Get(yesButton, "FlatAppearance"), "MouseOverBackColor", 185, 28, 28);
        RuntimeForms.SetColor(RuntimeForms.Get(yesButton, "FlatAppearance"), "MouseDownBackColor", 153, 27, 27);
        RuntimeForms.SetEnum(yesButton, "DialogResult", "Ignore");
        Place(yesButton, 564, 366, 132, 44);
        // Keep the existing return values: only Ignore continues the current run.
        RuntimeForms.Set(window, "AcceptButton", noButton);
        RuntimeForms.Set(window, "CancelButton", noButton);
        RuntimeForms.Set(window, "ActiveControl", noButton);
    }
    private void Place(object control, int left, int top, int width, int height)
    {
        RuntimeForms.Set(control, "Left", left); RuntimeForms.Set(control, "Top", top);
        RuntimeForms.Set(control, "Width", width); RuntimeForms.Set(control, "Height", height);
        RuntimeForms.Add(window, control);
    }
    internal string ShowDialog(object owner) { return RuntimeForms.Show(window, owner); }
    public void Dispose()
    {
        try { RuntimeForms.Dispose(window); }
        finally { RuntimeForms.Dispose(dialogFont); dialogFont = null; }
    }
}

// Inspect the exact operation set of each output job. Only read NX objects;
// MCS identity comes from the nearest OrientGeometry, not its name or the WCS.
internal sealed class ProgramMcsInspection
{
    internal readonly List<ProgramMcsIssue> Issues = new List<ProgramMcsIssue>();
    internal bool Passed { get { return Issues.Count == 0; } }
    internal string Message
    {
        get
        {
            bool mixed = false;
            foreach (ProgramMcsIssue issue in Issues) if (issue.Mixed) mixed = true;
            if (Issues.Count == 1 && mixed)
                return "\u0412 \u043F\u0430\u043F\u043A\u0435 \u00AB" + Issues[0].Folder + "\u00BB \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438 \u0438\u0441\u043F\u043E\u043B\u044C\u0437\u0443\u044E\u0442 \u0440\u0430\u0437\u043D\u044B\u0435 MCS.";
            return mixed ? "\u0412 \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0445 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u043D\u044B\u0445 \u043F\u0430\u043F\u043A\u0430\u0445 \u043E\u0431\u043D\u0430\u0440\u0443\u0436\u0435\u043D\u044B \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438 \u0441 \u0440\u0430\u0437\u043D\u044B\u043C\u0438 MCS." :
                "\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043E\u043F\u0440\u0435\u0434\u0435\u043B\u0438\u0442\u044C MCS \u043D\u0435\u043A\u043E\u0442\u043E\u0440\u044B\u0445 \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0445 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439.";
        }
    }
    internal string Details()
    {
        List<string> sections = new List<string>();
        foreach (ProgramMcsIssue issue in Issues)
        {
            StringBuilder text = new StringBuilder();
            text.Append("\u041F\u0430\u043F\u043A\u0430: ").Append(issue.Folder).Append("\r\n");
            if (issue.Mixed) text.Append("\u0420\u0430\u0437\u043D\u044B\u0435 MCS \u0432\u043D\u0443\u0442\u0440\u0438 \u043E\u0434\u043D\u043E\u0439 \u0423\u041F.\r\n");
            text.Append("\u041E\u043F\u0435\u0440\u0430\u0446\u0438\u044F \u2192 MCS\r\n");
            Dictionary<string, Tag> names = new Dictionary<string, Tag>(StringComparer.Ordinal);
            bool duplicateName = false;
            foreach (ProgramMcsAssignment assignment in issue.Assignments)
            {
                text.Append(assignment.OperationName).Append(" \u2192 ");
                if (assignment.Error != null)
                    text.Append("\u043D\u0435 \u043E\u043F\u0440\u0435\u0434\u0435\u043B\u0435\u043D\u0430: ").Append(assignment.Error);
                else
                {
                    text.Append(assignment.McsName);
                    Tag previous;
                    if (names.TryGetValue(assignment.McsName, out previous) && previous != assignment.McsTag)
                        duplicateName = true;
                    names[assignment.McsName] = assignment.McsTag;
                }
                text.Append("\r\n");
            }
            if (duplicateName) text.Append("\u0412\u043D\u0438\u043C\u0430\u043D\u0438\u0435: \u0440\u0430\u0437\u043D\u044B\u0435 \u043E\u0431\u044A\u0435\u043A\u0442\u044B MCS \u0438\u043C\u0435\u044E\u0442 \u043E\u0434\u0438\u043D\u0430\u043A\u043E\u0432\u043E\u0435 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u0435.\r\n");
            if (issue.ReadError != null) text.Append("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043F\u0440\u043E\u0447\u0438\u0442\u0430\u0442\u044C \u0441\u043E\u0441\u0442\u0430\u0432 \u043F\u0430\u043F\u043A\u0438: ").Append(issue.ReadError).Append("\r\n");
            sections.Add(text.ToString().TrimEnd(new char[] { '\r', '\n' }));
        }
        return String.Join("\r\n\r\n", sections.ToArray());
    }
    internal static ProgramMcsInspection Inspect(List<ProgramJob> jobs)
    {
        ProgramMcsInspection result = new ProgramMcsInspection();
        foreach (ProgramJob job in jobs)
        {
            ProgramMcsIssue issue = new ProgramMcsIssue();
            issue.Folder = Display(String.IsNullOrEmpty(job.GroupPath) ? job.Group.Name : job.GroupPath);
            List<NXOpen.CAM.Operation> operations = new List<NXOpen.CAM.Operation>();
            HashSet<Tag> groups = new HashSet<Tag>(), seenOperations = new HashSet<Tag>();
            try
            {
                foreach (CamObject selected in job.Objects) Collect(selected, groups, seenOperations, operations);
                if (operations.Count == 0) throw new InvalidOperationException("\u0412 \u0432\u044B\u0432\u043E\u0434\u0438\u043C\u043E\u0439 \u0423\u041F \u043D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D\u043E \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439.");
            }
            catch (Exception ex) { issue.ReadError = RuntimeForms.ActualMessage(ex); }
            HashSet<Tag> identities = new HashSet<Tag>();
            bool unreadable = issue.ReadError != null;
            foreach (NXOpen.CAM.Operation operation in operations)
            {
                ProgramMcsAssignment assignment = new ProgramMcsAssignment();
                assignment.OperationName = "\u041E\u043F\u0435\u0440\u0430\u0446\u0438\u044F " + operation.Tag;
                try
                {
                    assignment.OperationName = Display(operation.Name);
                    NCGroup mcs = Resolve(operation);
                    assignment.McsTag = mcs.Tag;
                    assignment.McsName = Display(mcs.Name);
                    identities.Add(mcs.Tag);
                }
                catch (Exception ex) { assignment.Error = RuntimeForms.ActualMessage(ex); unreadable = true; }
                issue.Assignments.Add(assignment);
            }
            issue.Mixed = identities.Count > 1;
            if (issue.Mixed || unreadable) result.Issues.Add(issue);
        }
        return result;
    }
    private static void Collect(CamObject obj, HashSet<Tag> groups, HashSet<Tag> seenOperations,
        List<NXOpen.CAM.Operation> operations)
    {
        if (obj == null) throw new InvalidOperationException("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043F\u0440\u043E\u0447\u0438\u0442\u0430\u0442\u044C \u043E\u0431\u044A\u0435\u043A\u0442 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u044B.");
        NXOpen.CAM.Operation operation = obj as NXOpen.CAM.Operation;
        if (operation != null)
        {
            if (seenOperations.Add(operation.Tag)) operations.Add(operation);
            return;
        }
        NCGroup group = obj as NCGroup;
        if (group == null) throw new InvalidOperationException("\u041D\u0435\u0438\u0437\u0432\u0435\u0441\u0442\u043D\u044B\u0439 \u043E\u0431\u044A\u0435\u043A\u0442 \u0432 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u043D\u043E\u0439 \u043F\u0430\u043F\u043A\u0435: " + Display(obj.Name));
        if (!groups.Add(group.Tag)) throw new InvalidOperationException("\u041F\u043E\u0432\u0442\u043E\u0440\u043D\u0430\u044F \u0438\u043B\u0438 \u0446\u0438\u043A\u043B\u0438\u0447\u0435\u0441\u043A\u0430\u044F \u0441\u0441\u044B\u043B\u043A\u0430 \u0432 \u0434\u0435\u0440\u0435\u0432\u0435 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C.");
        foreach (CamObject member in group.GetMembers()) Collect(member, groups, seenOperations, operations);
    }
    private static NCGroup Resolve(NXOpen.CAM.Operation operation)
    {
        NCGroup group = operation.GetParent(CamSetup.View.Geometry);
        HashSet<Tag> seen = new HashSet<Tag>();
        while (group != null && group.Tag != Tag.Null)
        {
            if (!seen.Add(group.Tag)) throw new InvalidOperationException("\u0426\u0438\u043A\u043B\u0438\u0447\u0435\u0441\u043A\u0430\u044F \u0441\u0441\u044B\u043B\u043A\u0430 \u0432 \u0434\u0435\u0440\u0435\u0432\u0435 \u0433\u0435\u043E\u043C\u0435\u0442\u0440\u0438\u0438.");
            if (group is NXOpen.CAM.OrientGeometry) return group;
            group = group.GetParent();
        }
        throw new InvalidOperationException("MCS \u043D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D\u0430 \u0432 \u0432\u0435\u0442\u043A\u0435 \u0433\u0435\u043E\u043C\u0435\u0442\u0440\u0438\u0438 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438.");
    }
    private static string Display(string name)
    { return (name ?? "(\u0431\u0435\u0437 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u044F)").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\0", "\\0"); }
}

internal sealed class ProgramMcsIssue
{
    internal string Folder, ReadError;
    internal bool Mixed;
    internal readonly List<ProgramMcsAssignment> Assignments = new List<ProgramMcsAssignment>();
}

internal sealed class ProgramMcsAssignment
{
    internal string OperationName, McsName, Error;
    internal Tag McsTag;
}

internal sealed class ProgramMcsErrorDialog : IDisposable
{
    private readonly object window = RuntimeForms.New("Form");
    internal ProgramMcsErrorDialog(ProgramMcsInspection result)
    {
        RuntimeForms.Set(window, "Text", ScriptInfo.WindowTitle("\u041E\u0448\u0438\u0431\u043A\u0430 \u043F\u0440\u043E\u0432\u0435\u0440\u043A\u0438 MCS"));
        RuntimeForms.SetValue(window, "ClientSize", 820, 514);
        RuntimeForms.SetValue(window, "AutoScaleDimensions", 96F, 96F);
        RuntimeForms.SetEnum(window, "AutoScaleMode", "Dpi");
        RuntimeForms.SetEnum(window, "StartPosition", "CenterParent");
        RuntimeForms.SetEnum(window, "FormBorderStyle", "FixedDialog");
        RuntimeForms.Set(window, "ControlBox", false);
        RuntimeForms.Set(window, "MinimizeBox", false);
        RuntimeForms.Set(window, "MaximizeBox", false);
        RuntimeForms.Set(window, "ShowInTaskbar", false);
        RuntimeForms.SetColor(window, "BackColor", 153, 27, 27);
        RuntimeForms.SetColor(window, "ForeColor", 255, 255, 255);
        object message = RuntimeForms.New("Label");
        RuntimeForms.Set(message, "Text", result.Message);
        RuntimeForms.Set(message, "UseMnemonic", false);
        Place(message, 20, 18, 780, 60);
        object details = RuntimeForms.New("TextBox");
        RuntimeForms.Set(details, "Multiline", true);
        RuntimeForms.Set(details, "ReadOnly", true);
        RuntimeForms.Set(details, "WordWrap", false);
        RuntimeForms.Set(details, "TabStop", false);
        RuntimeForms.SetColor(details, "BackColor", 127, 29, 29);
        RuntimeForms.SetColor(details, "ForeColor", 255, 255, 255);
        RuntimeForms.SetEnum(details, "ScrollBars", "Both");
        RuntimeForms.Set(details, "Text", result.Details());
        Place(details, 20, 82, 780, 320);
        object question = RuntimeForms.New("Label");
        RuntimeForms.Set(question, "Text", "\u041F\u0440\u043E\u0434\u043E\u043B\u0436\u0438\u0442\u044C?");
        RuntimeForms.Set(question, "UseMnemonic", false);
        Place(question, 20, 417, 460, 30);
        object yes = MakeButton("\u0414\u0430", "Yes", 220, 38, 38);
        object no = MakeButton("\u041D\u0435\u0442", "No", 21, 128, 61);
        Place(yes, 510, 448, 140, 46);
        Place(no, 660, 448, 140, 46);
        // Explicit Yes is required for this run. Enter/Escape keep the default No.
        RuntimeForms.Set(window, "AcceptButton", no);
        RuntimeForms.Set(window, "CancelButton", no);
        RuntimeForms.Set(window, "ActiveControl", no);
    }
    private static object MakeButton(string text, string result, int red, int green, int blue)
    {
        object button = RuntimeForms.New("Button");
        RuntimeForms.Set(button, "Text", text);
        RuntimeForms.Set(button, "UseMnemonic", false);
        RuntimeForms.Set(button, "UseVisualStyleBackColor", false);
        RuntimeForms.SetColor(button, "BackColor", red, green, blue);
        RuntimeForms.SetColor(button, "ForeColor", 255, 255, 255);
        RuntimeForms.SetEnum(button, "DialogResult", result);
        return button;
    }
    private void Place(object control, int left, int top, int width, int height)
    {
        RuntimeForms.Set(control, "Left", left); RuntimeForms.Set(control, "Top", top);
        RuntimeForms.Set(control, "Width", width); RuntimeForms.Set(control, "Height", height);
        RuntimeForms.Add(window, control);
    }
    internal string ShowDialog(object owner) { return RuntimeForms.Show(window, owner); }
    public void Dispose() { RuntimeForms.Dispose(window); }
}


internal sealed class ProgramJob
{
    public NCGroup Group;
    public string GroupPath;
    public string OutputName; // Set only after accepting the selected-operation names dialog.
    public bool WholeGroup;
    public readonly List<CamObject> Objects = new List<CamObject>();
    public string SelectionDescription()
    {
        if (WholeGroup) return "\u041F\u0430\u043F\u043A\u0430 \u0446\u0435\u043B\u0438\u043A\u043E\u043C";
        List<string> names = new List<string>();
        foreach (CamObject operation in Objects) names.Add(operation.Name);
        return "\u0422\u043E\u043B\u044C\u043A\u043E \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0435 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438 (" + Objects.Count + "): " + String.Join(", ", names.ToArray());
    }
}

internal sealed class OperationSelectionSnapshot
{
    private Tag[] tags = new Tag[0];
    private Exception readError;

    internal static OperationSelectionSnapshot Capture()
    {
        OperationSelectionSnapshot snapshot = new OperationSelectionSnapshot();
        try
        {
            // Read the Operation Navigator before opening any modal script window.
            int count; Tag[] selected;
            UFSession.GetUFSession().UiOnt.AskSelectedNodes(out count, out selected);
            if (count < 0 || (count > 0 && (selected == null || selected.Length != count)))
                throw new InvalidOperationException("NX \u0432\u0435\u0440\u043D\u0443\u043B \u043D\u0435\u043F\u043E\u043B\u043D\u044B\u0439 \u0441\u043F\u0438\u0441\u043E\u043A \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0445 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439.");
            if (count > 0) snapshot.tags = (Tag[])selected.Clone();
        }
        catch (Exception ex) { snapshot.readError = ex; }
        // A selection-read failure must not prevent the normal folder-output mode.
        return snapshot;
    }

    internal List<ProgramJob> BuildJobs(NCGroup root)
    {
        if (readError != null)
            throw new InvalidOperationException("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043F\u0440\u043E\u0447\u0438\u0442\u0430\u0442\u044C \u0432\u044B\u0434\u0435\u043B\u0435\u043D\u0438\u0435 \u0432 \u043D\u0430\u0432\u0438\u0433\u0430\u0442\u043E\u0440\u0435 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439 NX.\n" + RuntimeForms.ActualMessage(readError), readError);
        if (tags.Length == 0)
            throw new InvalidOperationException("\u041E\u043F\u0435\u0440\u0430\u0446\u0438\u0438 \u043D\u0435 \u0432\u044B\u0434\u0435\u043B\u0435\u043D\u044B. \u0417\u0430\u043A\u0440\u043E\u0439\u0442\u0435 \u0441\u043A\u0440\u0438\u043F\u0442, \u0432\u044B\u0434\u0435\u043B\u0438\u0442\u0435 \u043D\u0443\u0436\u043D\u044B\u0435 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438 \u0432 \u043D\u0430\u0432\u0438\u0433\u0430\u0442\u043E\u0440\u0435 NX \u0438 \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u0441\u043A\u0440\u0438\u043F\u0442 \u0441\u043D\u043E\u0432\u0430.");
        foreach (Tag tag in tags)
            if (!(NXObjectManager.Get(tag) is NXOpen.CAM.Operation))
                throw new InvalidOperationException("\u0414\u043B\u044F \u044D\u0442\u043E\u0439 \u043A\u043D\u043E\u043F\u043A\u0438 \u0432\u044B\u0434\u0435\u043B\u0438\u0442\u0435 \u0442\u043E\u043B\u044C\u043A\u043E \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438 \u0432 \u043D\u0430\u0432\u0438\u0433\u0430\u0442\u043E\u0440\u0435 NX. \u0412 \u0432\u044B\u0434\u0435\u043B\u0435\u043D\u0438\u0438 \u043F\u0440\u0438\u0441\u0443\u0442\u0441\u0442\u0432\u0443\u0435\u0442 \u043F\u0430\u043F\u043A\u0430 \u0438\u043B\u0438 \u0434\u0440\u0443\u0433\u043E\u0439 \u043E\u0431\u044A\u0435\u043A\u0442.");
        // Retain the original grouping and NX Program Order, never expand a folder selection.
        return ProgramSelection.Build(root, tags);
    }
}

internal sealed class OperationNumbering
{
    private readonly List<NXOpen.CAM.Operation> operations = new List<NXOpen.CAM.Operation>();
    private readonly List<string> names = new List<string>();

    internal static OperationNumbering Plan(List<ProgramJob> jobs)
    {
        OperationNumbering result = new OperationNumbering();
        HashSet<Tag> seen = new HashSet<Tag>();
        foreach (ProgramJob job in jobs)
        {
            List<NXOpen.CAM.Operation> ordered = new List<NXOpen.CAM.Operation>();
            HashSet<Tag> groups = new HashSet<Tag>();
            foreach (CamObject obj in job.Objects) Collect(obj, ordered, groups);
            string numberFormat = ordered.Count <= 99 ? "D2" : "D3";
            for (int i = 0; i < ordered.Count; i++)
            {
                NXOpen.CAM.Operation operation = ordered[i];
                if (!seen.Add(operation.Tag)) throw new InvalidOperationException("\u041E\u043F\u0435\u0440\u0430\u0446\u0438\u044F \u043F\u043E\u043F\u0430\u043B\u0430 \u0432 \u043D\u0435\u0441\u043A\u043E\u043B\u044C\u043A\u043E \u0432\u044B\u0432\u043E\u0434\u0438\u043C\u044B\u0445 \u0423\u041F: " + operation.Name);
                string original = operation.Name ?? "";
                string body = Regex.Replace(original, @"\A(?:[0-9]+_)+", "");
                if (body.Length == 0)
                {
                    Match numeric = Regex.Match(original, @"([0-9]+)_$");
                    body = numeric.Success ? numeric.Groups[1].Value : "\u041E\u043F\u0435\u0440\u0430\u0446\u0438\u044F";
                }
                string name = (i + 1).ToString(numberFormat, CultureInfo.InvariantCulture) + "_" + body;
                if (name == original) continue;
                result.operations.Add(operation); result.names.Add(name);
            }
        }
        return result;
    }
    private static void Collect(CamObject obj, List<NXOpen.CAM.Operation> result, HashSet<Tag> groups)
    {
        NXOpen.CAM.Operation operation = obj as NXOpen.CAM.Operation;
        if (operation != null) { result.Add(operation); return; }
        NCGroup group = obj as NCGroup;
        if (group == null) return;
        if (!groups.Add(group.Tag)) throw new InvalidOperationException("\u041F\u043E\u0432\u0442\u043E\u0440\u043D\u0430\u044F \u043F\u0430\u043F\u043A\u0430 \u043F\u0440\u0438 \u043D\u0443\u043C\u0435\u0440\u0430\u0446\u0438\u0438 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439: " + group.Name);
        foreach (CamObject member in group.GetMembers()) Collect(member, result, groups);
    }
    private void Apply()
    {
        // Free old numbered names before assigning the new order, including swaps.
        string temporary = "NXRN_" + Guid.NewGuid().ToString("N").Substring(0, 16) + "_";
        for (int i = 0; i < operations.Count; i++) operations[i].SetName(temporary + i.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < operations.Count; i++)
        {
            operations[i].SetName(names[i]);
            if (!String.Equals(operations[i].Name, names[i], StringComparison.Ordinal))
                throw new InvalidOperationException("NX \u043D\u0435 \u043F\u0440\u0438\u043D\u044F\u043B \u0438\u043C\u044F \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438 \u00AB" + names[i] + "\u00BB. \u041F\u0440\u043E\u0432\u0435\u0440\u044C\u0442\u0435 \u0441\u043E\u0432\u043F\u0430\u0434\u0430\u044E\u0449\u0438\u0435 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u044F.");
        }
    }
    internal static string Run(Session session, List<ProgramJob> jobs, bool enabled, Func<string> output, Func<bool> published)
    {
        if (!enabled) return output();
        OperationNumbering plan = Plan(jobs);
        if (plan.operations.Count == 0) return output();
        Session.UndoMarkId mark = session.SetUndoMark(Session.MarkVisibility.Visible, ScriptInfo.WindowTitle("\u041D\u0443\u043C\u0435\u0440\u0430\u0446\u0438\u044F \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439"));
        Exception processingError = null;
        try { plan.Apply(); return output(); }
        catch (Exception ex) { processingError = ex; throw; }
        finally
        {
            // Cancel/failure before any published NC file restores the exact NX names.
            // Successful (or partially published) output retains matching names and its undo mark.
            if (!published())
            {
                try { session.UndoToMark(mark, null); session.DeleteUndoMark(mark, null); }
                catch (Exception restoreError)
                {
                    throw new InvalidOperationException("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0432\u043E\u0441\u0441\u0442\u0430\u043D\u043E\u0432\u0438\u0442\u044C \u0438\u043C\u0435\u043D\u0430 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439. \u0418\u0441\u043F\u043E\u043B\u044C\u0437\u0443\u0439\u0442\u0435 \u043E\u0442\u043C\u0435\u043D\u0443 \u0432 NX \u0438 \u043F\u0440\u043E\u0432\u0435\u0440\u044C\u0442\u0435 \u0434\u0435\u0440\u0435\u0432\u043E \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C.",
                        processingError == null ? restoreError : new AggregateException(processingError, restoreError));
                }
            }
        }
    }
}

internal static class ProgramSelection
{
    public static List<ProgramJob> Build(NCGroup root, Tag[] tags)
    {
        if (root == null) throw new InvalidOperationException("\u041D\u0435\u0442 \u0434\u0435\u0440\u0435\u0432\u0430 \u00AB\u041F\u043E\u0440\u044F\u0434\u043E\u043A \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u00BB.");
        if (tags == null || tags.Length == 0) throw new InvalidOperationException("\u041D\u0435 \u0432\u044B\u0431\u0440\u0430\u043D\u044B \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u044B \u0438\u043B\u0438 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438.");
        HashSet<Tag> selected = new HashSet<Tag>(tags), found = new HashSet<Tag>();
        if (selected.Contains(root.Tag)) throw new InvalidOperationException("\u041A\u043E\u0440\u0435\u043D\u044C \u0434\u0435\u0440\u0435\u0432\u0430 \u0432\u044B\u0431\u0438\u0440\u0430\u0442\u044C \u043D\u0435\u043B\u044C\u0437\u044F. \u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u043D\u044B\u0435 \u043F\u0430\u043F\u043A\u0438 \u0438\u043B\u0438 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438 \u0432\u043D\u0443\u0442\u0440\u0438 \u043D\u0438\u0445.");
        List<ProgramJob> jobs = new List<ProgramJob>();
        Walk(root, root, "", selected, found, false, jobs);
        if (found.Count != selected.Count)
            throw new InvalidOperationException("\u0427\u0430\u0441\u0442\u044C \u0432\u044B\u0431\u043E\u0440\u0430 \u043D\u0435 \u043E\u0442\u043D\u043E\u0441\u0438\u0442\u0441\u044F \u043A \u0434\u0435\u0440\u0435\u0432\u0443 \u00AB\u041F\u043E\u0440\u044F\u0434\u043E\u043A \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u00BB. \u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u0442\u043E\u043B\u044C\u043A\u043E \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u043D\u044B\u0435 \u043F\u0430\u043F\u043A\u0438 \u0438\u043B\u0438 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438.");
        if (jobs.Count == 0) throw new InvalidOperationException("\u0412 \u0432\u044B\u0431\u043E\u0440\u0435 \u043D\u0435\u0442 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439 \u0434\u043B\u044F \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u044F.");
        return jobs;
    }

    private static bool Walk(NCGroup group, NCGroup root, string path, HashSet<Tag> selected,
        HashSet<Tag> found, bool selectedAncestor, List<ProgramJob> jobs)
    {
        bool whole = selected.Contains(group.Tag);
        if (whole)
        {
            if (selectedAncestor) Overlap();
            found.Add(group.Tag);
        }
        CamObject[] members = group.GetMembers();
        ProgramJob job = new ProgramJob(); job.Group = group; job.GroupPath = path; job.WholeGroup = whole;
        if (whole) job.Objects.Add(group);
        // Gather this folder before recursing: result rows follow folder preorder.
        foreach (CamObject member in members)
        {
            if (!(member is NXOpen.CAM.Operation) || !selected.Contains(member.Tag)) continue;
            if (selectedAncestor || whole) Overlap();
            if (group.Tag == root.Tag)
                throw new InvalidOperationException("\u041E\u043F\u0435\u0440\u0430\u0446\u0438\u044F \u00AB" + member.Name + "\u00BB \u043D\u0430\u0445\u043E\u0434\u0438\u0442\u0441\u044F \u0432 \u043A\u043E\u0440\u043D\u0435. \u041F\u0435\u0440\u0435\u043D\u0435\u0441\u0438\u0442\u0435 \u0435\u0451 \u0432 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u043D\u0443\u044E \u043F\u0430\u043F\u043A\u0443, \u0447\u0442\u043E\u0431\u044B \u043E\u043F\u0440\u0435\u0434\u0435\u043B\u0438\u0442\u044C \u0438\u043C\u044F \u0423\u041F.");
            found.Add(member.Tag); job.Objects.Add(member);
        }
        if (job.Objects.Count > 0) jobs.Add(job);
        bool hasOperation = false;
        foreach (CamObject member in members)
        {
            if (member is NXOpen.CAM.Operation) hasOperation = true;
            NCGroup child = member as NCGroup;
            if (child != null)
            {
                string childPath = path.Length == 0 ? child.Name : path + " / " + child.Name;
                if (Walk(child, root, childPath, selected, found, selectedAncestor || whole, jobs)) hasOperation = true;
            }
        }
        if (whole && !hasOperation) throw new InvalidOperationException("\u041D\u0435\u0442 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439 \u0432 \u043F\u0430\u043F\u043A\u0435 \u00AB" + path + "\u00BB.");
        return hasOperation;
    }
    private static void Overlap()
    {
        throw new InvalidOperationException("\u041E\u0434\u043D\u043E\u0432\u0440\u0435\u043C\u0435\u043D\u043D\u043E \u0432\u044B\u0431\u0440\u0430\u043D\u044B \u043F\u0430\u043F\u043A\u0430 \u0438 \u0435\u0451 \u0441\u043E\u0434\u0435\u0440\u0436\u0438\u043C\u043E\u0435. \u041E\u0441\u0442\u0430\u0432\u044C\u0442\u0435 \u043F\u0430\u043F\u043A\u0443 \u0446\u0435\u043B\u0438\u043A\u043E\u043C \u0418\u041B\u0418 \u043E\u0442\u0434\u0435\u043B\u044C\u043D\u044B\u0435 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438, \u0447\u0442\u043E\u0431\u044B \u043D\u0435 \u0432\u044B\u0432\u043E\u0434\u0438\u0442\u044C \u0438\u0445 \u0434\u0432\u0430\u0436\u0434\u044B.");
    }
}

// All Forms objects stay behind object/reflection boundaries. In particular,
// no Form, Control, IWin32Window, Font or IHandle<T> is a compile-time type here.
internal static class RuntimeForms
{
    private static Assembly forms;
    private static readonly object formsLock = new object();
    internal static Type FormType(string name)
    {
        lock (formsLock)
        {
            if (forms == null) forms = SharedFormsAssembly.Load();
            return forms.GetType("System.Windows.Forms." + name, true);
        }
    }
    internal static object New(string name) { return Activator.CreateInstance(FormType(name)); }
    internal static object Get(object target, string name)
    { return target.GetType().GetProperty(name).GetValue(target, null); }
    internal static void Set(object target, string name, object value)
    { target.GetType().GetProperty(name).SetValue(target, value, null); }
    internal static void SetEnum(object target, string name, string value)
    {
        PropertyInfo property = target.GetType().GetProperty(name);
        property.SetValue(target, Enum.Parse(property.PropertyType, value), null);
    }
    internal static void SetValue(object target, string name, params object[] arguments)
    {
        PropertyInfo property = target.GetType().GetProperty(name);
        property.SetValue(target, Activator.CreateInstance(property.PropertyType, arguments), null);
    }
    internal static void SetColor(object target, string name, int red, int green, int blue)
    {
        // Resolve Color from the control property without a compile-time Drawing reference.
        PropertyInfo property = target.GetType().GetProperty(name);
        MethodInfo fromArgb = property.PropertyType.GetMethod("FromArgb", new Type[] { typeof(int), typeof(int), typeof(int) });
        property.SetValue(target, fromArgb.Invoke(null, new object[] { red, green, blue }), null);
    }
    internal static object SetPngImage(object target, string base64, int width, int height)
    {
        // Obtain Image/Bitmap from the actual Forms property, without a Drawing reference.
        // Decode and resize entirely in memory; the independent bitmap outlives the stream.
        PropertyInfo property = target.GetType().GetProperty("Image");
        Type imageType = property.PropertyType;
        MethodInfo fromStream = imageType.GetMethod("FromStream", new Type[] { typeof(Stream) });
        Type bitmapType = imageType.Assembly.GetType("System.Drawing.Bitmap", true);
        object original = null, scaled = null;
        using (MemoryStream stream = new MemoryStream(Convert.FromBase64String(base64), false))
        {
            try
            {
                original = fromStream.Invoke(null, new object[] { stream });
                scaled = Activator.CreateInstance(bitmapType, new object[] { original, width, height });
                property.SetValue(target, scaled, null);
                return scaled;
            }
            catch { Dispose(scaled); throw; }
            finally { Dispose(original); }
        }
    }
    internal static object Call(object target, string name, params object[] arguments)
    {
        try
        {
            return target.GetType().InvokeMember(name,
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, target, arguments);
        }
        catch (TargetInvocationException ex)
        { throw new InvalidOperationException(target.GetType().FullName + "." + name + ": " + ActualMessage(ex), ex.InnerException ?? ex); }
    }
    internal static string ActualMessage(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
        return ex.Message;
    }
    internal static string ErrorDetails(Exception error)
    {
        // Keep the original exception chain: a Forms/reflection wrapper must not
        // hide the NX error code or the message from the failed operation.
        List<string> details = new List<string>();
        for (Exception current = error; current != null; current = current.InnerException)
        {
            if (current is TargetInvocationException && current.InnerException != null) continue;
            string description = current.GetType().FullName + ": " + current.Message;
            try
            {
                PropertyInfo nativeCode = current.GetType().GetProperty("NativeErrorCode");
                if (nativeCode != null && nativeCode.PropertyType == typeof(int))
                {
                    int nativeError = (int)nativeCode.GetValue(current, null);
                    description += "\n\u041A\u043E\u0434 Win32: " + nativeError.ToString(CultureInfo.InvariantCulture);
                    if (nativeError == 1410)
                    {
                        description += "\n\u041A\u043E\u043D\u0444\u043B\u0438\u043A\u0442 \u043E\u043A\u043E\u043D\u043D\u044B\u0445 \u043A\u043B\u0430\u0441\u0441\u043E\u0432. \u041F\u043E\u043B\u043D\u043E\u0441\u0442\u044C\u044E \u0437\u0430\u043A\u0440\u043E\u0439\u0442\u0435 NX \u0438 \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u0441\u043A\u0440\u0438\u043F\u0442 \u0432 \u043D\u043E\u0432\u043E\u043C \u0441\u0435\u0430\u043D\u0441\u0435.";
                        string[] frames = (current.StackTrace ?? "").Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (frames.Length > 0) description += "\n" + String.Join("\n", frames, 0, Math.Min(6, frames.Length));
                    }
                }
                PropertyInfo code = current.GetType().GetProperty("ErrorCode");
                if (code != null && code.PropertyType == typeof(int))
                    description += "\n\u041A\u043E\u0434 \u043E\u0448\u0438\u0431\u043A\u0438: " + ((int)code.GetValue(current, null)).ToString(CultureInfo.InvariantCulture);
            }
            catch { /* The original message is sufficient if a code cannot be read. */ }
            if (!details.Contains(description)) details.Add(description);
        }
        return String.Join("\n\n", details.ToArray());
    }
    internal static void ReportFailure(object owner, string text, string caption, Exception original)
    {
        // Do not call NXMessageBox here: NX can throw "User abort" while showing
        // the error, replacing the actual exception with a second one.
        // No NX abort flags are cleared and no operation is retried.
        IntPtr handle = IntPtr.Zero;
        try { if (owner != null) handle = (IntPtr)Get(owner, "Handle"); } catch { }
        try
        {
            // MB_OK | MB_ICONERROR | MB_TASKMODAL | MB_SETFOREGROUND.
            if (MessageBoxW(handle, text, caption, 0x00012010U) != 0) return;
            if (handle != IntPtr.Zero && MessageBoxW(IntPtr.Zero, text, caption, 0x00012010U) != 0) return;
        }
        catch { /* WinForms is a fallback only if the native window cannot open. */ }
        try { Message(null, text, caption, "OK", "Error", "Button1"); }
        catch
        {
            // If both UI paths fail, NX's journal host still receives the original
            // problem and saved-copy status, never just the reporting failure.
            throw new InvalidOperationException(text, original);
        }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint flags);

    internal static void Add(object parent, object child)
    { Call(Get(parent, "Controls"), "Add", child); }
    internal static void On(object target, string eventName, EventHandler handler)
    { target.GetType().GetEvent(eventName).AddEventHandler(target, handler); }
    internal static void Dispose(object target)
    { IDisposable disposable = target as IDisposable; if (disposable != null) disposable.Dispose(); }
    internal static object CreateOwner()
    {
        IntPtr handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        if (handle == IntPtr.Zero) return null;
        object owner = New("NativeWindow");
        Call(owner, "AssignHandle", handle);
        return owner;
    }
    internal static void ReleaseOwner(object owner)
    { if (owner != null) Call(owner, "ReleaseHandle"); }
    internal static string Show(object dialog, object owner)
    { return (owner == null ? Call(dialog, "ShowDialog") : Call(dialog, "ShowDialog", owner)).ToString(); }
    internal static string Message(object owner, string text, string caption, string buttons, string icon, string defaultButton)
    {
        object b = Enum.Parse(FormType("MessageBoxButtons"), buttons);
        object i = Enum.Parse(FormType("MessageBoxIcon"), icon);
        object d = Enum.Parse(FormType("MessageBoxDefaultButton"), defaultButton);
        object[] args = owner == null ? new object[] { text, caption, b, i, d } :
            new object[] { owner, text, caption, b, i, d };
        try
        {
            object result = FormType("MessageBox").InvokeMember("Show",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.InvokeMethod, null, null, args);
            return result.ToString();
        }
        catch (TargetInvocationException ex)
        { throw new InvalidOperationException(ActualMessage(ex), ex.InnerException ?? ex); }
    }
}

internal sealed class MachineChoice
{
    public MachineTarget Target;
    public ExternalDriveSelection ExternalDrive;
    public string[] ProgramExtensions;
    public CamSetup.OutputUnits Units;
    public FanucBinMode BinMode;
    public int BinSizeMB;
    public bool SaveToProject;
    public bool AssignNames;
    public string[] ProgramNames;
    public int[] ProgramCallOrder; // null = no added calls; indices follow Jobs/ProgramNames.
    public int[] WorkOffsets; // null = disabled; manual fixture numbers 1-6, in user order.
}

internal abstract class RouterDialog : IDisposable
{
    protected readonly object Window = RuntimeForms.New("Form");
    protected readonly object Grid = RuntimeForms.New("FlowLayoutPanel");
    protected readonly object Header = RuntimeForms.New("Panel");
    protected readonly object Footer = RuntimeForms.New("Panel");
    protected readonly object Tips = RuntimeForms.New("ToolTip");

    protected RouterDialog(string title, int headerHeight)
    {
        RuntimeForms.Set(Window, "Text", ScriptInfo.WindowTitle(title));
        RuntimeForms.SetValue(Window, "ClientSize", 820, 590);
        RuntimeForms.SetValue(Window, "MinimumSize", 800, 480);
        RuntimeForms.SetValue(Window, "Padding", 18);
        RuntimeForms.SetValue(Window, "AutoScaleDimensions", 96F, 96F);
        RuntimeForms.SetEnum(Window, "AutoScaleMode", "Dpi");
        RuntimeForms.SetEnum(Window, "StartPosition", "CenterParent");
        RuntimeForms.Set(Window, "MinimizeBox", false);
        RuntimeForms.Set(Window, "ShowInTaskbar", false);
        RuntimeForms.Set(Grid, "AutoScroll", true);
        RuntimeForms.Set(Grid, "WrapContents", true);
        RuntimeForms.SetValue(Grid, "Padding", 0, 8, 0, 8);
        RuntimeForms.SetEnum(Grid, "Dock", "Fill");
        RuntimeForms.Set(Header, "Height", headerHeight);
        RuntimeForms.Set(Header, "Width", 784);
        RuntimeForms.SetEnum(Header, "Dock", "Top");
        RuntimeForms.Set(Footer, "Height", 52);
        RuntimeForms.Set(Footer, "Width", 784);
        RuntimeForms.SetEnum(Footer, "Dock", "Bottom");
        RuntimeForms.Add(Window, Grid);
        RuntimeForms.Add(Window, Header);
        RuntimeForms.Add(Window, Footer);
        object cancel = Button("\u041E\u0442\u043C\u0435\u043D\u0430", 112, 38);
        RuntimeForms.SetEnum(cancel, "Dock", "Right");
        RuntimeForms.SetEnum(cancel, "DialogResult", "Cancel");
        RuntimeForms.Add(Footer, cancel);
        RuntimeForms.Set(Window, "CancelButton", cancel);
    }
    public string ShowDialog(object owner) { return RuntimeForms.Show(Window, owner); }
    public void Dispose()
    {
        RuntimeForms.Dispose(Window);
        RuntimeForms.Dispose(Tips);
    }
    protected void Finish(string result)
    {
        RuntimeForms.SetEnum(Window, "DialogResult", result);
        RuntimeForms.Call(Window, "Close");
    }
    protected static object Button(string text, int width, int height)
    {
        object control = RuntimeForms.New("Button");
        RuntimeForms.Set(control, "Text", text);
        RuntimeForms.Set(control, "Width", width);
        RuntimeForms.Set(control, "Height", height);
        RuntimeForms.Set(control, "UseVisualStyleBackColor", true);
        RuntimeForms.Set(control, "UseMnemonic", false);
        RuntimeForms.Set(control, "AutoEllipsis", true);
        return control;
    }
    protected static void Position(object control, int x, int y, int width, int height)
    {
        RuntimeForms.Set(control, "Left", x); RuntimeForms.Set(control, "Top", y);
        RuntimeForms.Set(control, "Width", width); RuntimeForms.Set(control, "Height", height);
    }
    protected static object Label(string text, int x, int y, int width, int height)
    {
        object label = RuntimeForms.New("Label");
        RuntimeForms.Set(label, "Text", text);
        RuntimeForms.Set(label, "UseMnemonic", false);
        Position(label, x, y, width, height);
        return label;
    }
    protected object Tile(string text, string tip, int width, int height)
    {
        object button = Button(text, width, height);
        RuntimeForms.SetValue(button, "Margin", 0, 0, 10, 10);
        RuntimeForms.Call(Tips, "SetToolTip", button, tip);
        RuntimeForms.Add(Grid, button);
        return button;
    }
    protected void ClearGrid()
    {
        // Snapshot the collection before disposal, because disposal removes controls.
        System.Collections.IEnumerable collection = RuntimeForms.Get(Grid, "Controls") as System.Collections.IEnumerable;
        List<object> controls = new List<object>();
        foreach (object control in collection) controls.Add(control);
        foreach (object control in controls) RuntimeForms.Dispose(control);
    }
    protected void ShowProblem(Exception ex)
    {
        RuntimeForms.Message(Window, RuntimeForms.ActualMessage(ex), ScriptInfo.WindowTitle("\u041F\u0440\u0435\u0434\u0443\u043F\u0440\u0435\u0436\u0434\u0435\u043D\u0438\u0435"), "OK", "Warning", "Button1");
    }
}

internal sealed class SelectedOperationsPicker : RouterDialog
{
    private readonly List<ProgramJob> jobs;
    private readonly string[] names;
    private readonly object tree = RuntimeForms.New("TreeView");
    private readonly object numberInput = RuntimeForms.New("TextBox");
    private readonly object sourceLabel;
    private readonly List<object> programNodes = new List<object>();
    private int activeProgram = -1;
    private bool updating;
    private string previousNumber = "";

    internal SelectedOperationsPicker(List<ProgramJob> selected) : base("\u0412\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0435 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438 \u2014 \u0438\u043C\u0435\u043D\u0430 \u0423\u041F", 116)
    {
        if (selected == null || selected.Count == 0) throw new ArgumentException("\u041D\u0435 \u0432\u044B\u0431\u0440\u0430\u043D\u044B \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438 \u0434\u043B\u044F \u0432\u044B\u0432\u043E\u0434\u0430.");
        jobs = selected; names = new string[jobs.Count];
        object hint = Label("\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u0423\u041F \u0432 \u0434\u0435\u0440\u0435\u0432\u0435 \u0438 \u0438\u0437\u043C\u0435\u043D\u0438\u0442\u0435 \u0435\u0451 \u043D\u043E\u043C\u0435\u0440. \u0412\u043D\u0443\u0442\u0440\u0438 \u043F\u043E\u043A\u0430\u0437\u0430\u043D\u044B \u0442\u043E\u043B\u044C\u043A\u043E \u0432\u044B\u0432\u043E\u0434\u0438\u043C\u044B\u0435 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438.", 0, 0, 760, 36);
        RuntimeForms.SetEnum(hint, "Anchor", "Top, Left, Right"); RuntimeForms.Add(Header, hint);
        RuntimeForms.Add(Header, Label("\u041D\u043E\u043C\u0435\u0440 \u0423\u041F:", 0, 45, 112, 28));
        RuntimeForms.Add(Header, Label("O", 114, 45, 20, 28));
        Position(numberInput, 138, 42, 180, 28); RuntimeForms.Set(numberInput, "MaxLength", 8);
        RuntimeForms.Set(numberInput, "TabIndex", 0); RuntimeForms.Add(Header, numberInput);
        RuntimeForms.Call(Tips, "SetToolTip", numberInput, "\u0422\u043E\u043B\u044C\u043A\u043E \u0446\u0438\u0444\u0440\u044B: 1\u201399999999. \u0418\u043C\u044F \u0444\u0430\u0439\u043B\u0430 \u0438 O-\u043D\u043E\u043C\u0435\u0440 \u0431\u0443\u0434\u0443\u0442 \u043E\u0434\u0438\u043D\u0430\u043A\u043E\u0432\u044B\u043C\u0438; \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u044F \u0432 NX \u043D\u0435 \u0438\u0437\u043C\u0435\u043D\u044F\u0442\u0441\u044F.");
        sourceLabel = Label("", 0, 80, 760, 28);
        RuntimeForms.Set(sourceLabel, "AutoEllipsis", true); RuntimeForms.SetEnum(sourceLabel, "Anchor", "Top, Left, Right");
        RuntimeForms.Add(Header, sourceLabel);
        object controls = RuntimeForms.Get(Window, "Controls");
        RuntimeForms.Call(controls, "Remove", Grid); RuntimeForms.Dispose(Grid);
        RuntimeForms.SetEnum(tree, "Dock", "Fill"); RuntimeForms.Set(tree, "Sorted", false);
        RuntimeForms.Set(tree, "CheckBoxes", false); RuntimeForms.Set(tree, "LabelEdit", false);
        RuntimeForms.Set(tree, "ShowLines", true); RuntimeForms.Set(tree, "ShowPlusMinus", true);
        RuntimeForms.Set(tree, "ShowRootLines", true); RuntimeForms.Set(tree, "ShowNodeToolTips", true);
        RuntimeForms.Set(tree, "HideSelection", false); RuntimeForms.Set(tree, "Indent", 24);
        RuntimeForms.Set(tree, "ItemHeight", 26); RuntimeForms.Set(tree, "TabIndex", 1);
        RuntimeForms.Add(Window, tree); RuntimeForms.Call(controls, "SetChildIndex", tree, 0);
        RuntimeForms.Call(tree, "BeginUpdate");
        try
        {
            for (int i = 0; i < jobs.Count; i++)
            {
                ProgramJob job = jobs[i]; names[i] = job.OutputName ?? job.Group.Name;
                object node = RuntimeForms.New("TreeNode"); RuntimeForms.Set(node, "Tag", i);
                RuntimeForms.Set(node, "ToolTipText", job.GroupPath + "\n" + job.SelectionDescription());
                programNodes.Add(node); RefreshProgramNode(i);
                RuntimeForms.Call(RuntimeForms.Get(tree, "Nodes"), "Add", node);
                foreach (CamObject operation in job.Objects)
                {
                    if (!(operation is NXOpen.CAM.Operation)) throw new ArgumentException("\u0412 \u0441\u043F\u0438\u0441\u043A\u0435 \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0445 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439 \u043E\u0431\u043D\u0430\u0440\u0443\u0436\u0435\u043D\u0430 \u043F\u0430\u043F\u043A\u0430.");
                    object child = RuntimeForms.New("TreeNode");
                    RuntimeForms.Set(child, "Text", operation.Name); RuntimeForms.Set(child, "Tag", i);
                    RuntimeForms.Set(child, "ToolTipText", operation.Name);
                    RuntimeForms.Call(RuntimeForms.Get(node, "Nodes"), "Add", child);
                }
            }
            RuntimeForms.Call(tree, "ExpandAll");
        }
        finally { RuntimeForms.Call(tree, "EndUpdate"); }
        EventInfo selectedEvent = tree.GetType().GetEvent("AfterSelect");
        MethodInfo handler = GetType().GetMethod("AfterNodeSelect", BindingFlags.Instance | BindingFlags.NonPublic);
        selectedEvent.AddEventHandler(tree, Delegate.CreateDelegate(selectedEvent.EventHandlerType, this, handler));
        RuntimeForms.On(numberInput, "TextChanged", delegate { ChangeNumber(); });
        RuntimeForms.Set(tree, "SelectedNode", programNodes[0]); SelectProgram(0);
        RuntimeForms.Set(tree, "TopNode", programNodes[0]);
        object next = Button("\u0414\u0430\u043B\u0435\u0435", 112, 38); Position(next, 548, 6, 112, 38);
        RuntimeForms.SetEnum(next, "Anchor", "Bottom, Right"); RuntimeForms.Set(next, "TabIndex", 2);
        RuntimeForms.On(next, "Click", delegate { AcceptNames(); });
        RuntimeForms.Set(Window, "AcceptButton", next); RuntimeForms.Add(Footer, next);
        // RouterDialog supplies the only other button: Cancel, including Escape.
    }
    private void AfterNodeSelect(object sender, EventArgs args)
    {
        object node = RuntimeForms.Get(args, "Node");
        if (node != null) SelectProgram((int)RuntimeForms.Get(node, "Tag"));
    }
    private void SelectProgram(int index)
    {
        if (index < 0 || index >= jobs.Count) return;
        updating = true;
        try
        {
            activeProgram = index;
            string number = ProgramNames.DefaultNumber(names[index]);
            previousNumber = number.Length > 0 ? number.Substring(1) : "";
            RuntimeForms.Set(numberInput, "Text", previousNumber);
            string text = "\u0418\u0441\u0445\u043E\u0434\u043D\u0430\u044F \u043F\u0430\u043F\u043A\u0430: " + jobs[index].GroupPath;
            RuntimeForms.Set(sourceLabel, "Text", text); RuntimeForms.Call(Tips, "SetToolTip", sourceLabel, text);
        }
        finally { updating = false; }
    }
    private void ChangeNumber()
    {
        if (updating || activeProgram < 0) return;
        string text = (string)RuntimeForms.Get(numberInput, "Text");
        if (!Regex.IsMatch(text, @"\A[0-9]{0,8}\z"))
        {
            updating = true;
            try { RuntimeForms.Set(numberInput, "Text", previousNumber); RuntimeForms.Set(numberInput, "SelectionStart", previousNumber.Length); }
            finally { updating = false; }
            return;
        }
        previousNumber = text; names[activeProgram] = "O" + text;
        RefreshProgramNode(activeProgram);
    }
    private void RefreshProgramNode(int index)
    {
        RuntimeForms.Set(programNodes[index], "Text", names[index] + "   [" + jobs[index].GroupPath + "]");
    }
    private void AcceptNames()
    {
        try
        {
            string[] accepted = new string[names.Length]; HashSet<uint> numbers = new HashSet<uint>();
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    accepted[i] = ProgramNames.Normalize(names[i]);
                    if (!numbers.Add(UInt32.Parse(accepted[i].Substring(1), CultureInfo.InvariantCulture)))
                        throw new ArgumentException("\u041F\u043E\u0432\u0442\u043E\u0440\u044F\u0435\u0442\u0441\u044F \u043D\u043E\u043C\u0435\u0440 \u0423\u041F \u00AB" + accepted[i] + "\u00BB. \u0423\u043A\u0430\u0436\u0438\u0442\u0435 \u0440\u0430\u0437\u043D\u044B\u0435 \u043D\u043E\u043C\u0435\u0440\u0430; \u0432\u0435\u0434\u0443\u0449\u0438\u0435 \u043D\u0443\u043B\u0438 \u043D\u0435 \u0434\u0435\u043B\u0430\u044E\u0442 \u043D\u043E\u043C\u0435\u0440 \u0434\u0440\u0443\u0433\u0438\u043C.");
                }
                catch (ArgumentException)
                {
                    RuntimeForms.Set(tree, "SelectedNode", programNodes[i]); SelectProgram(i);
                    RuntimeForms.Call(numberInput, "Focus"); RuntimeForms.Call(numberInput, "SelectAll");
                    throw;
                }
            }
            // Commit only after validating every row; Cancel never changes jobs or NX.
            for (int i = 0; i < jobs.Count; i++) jobs[i].OutputName = accepted[i];
            Finish("OK");
        }
        catch (Exception ex) { ShowProblem(ex); }
    }
}

internal sealed class ProgramFolderEntry
{
    internal NCGroup Group;
    internal string Path;
    internal int ParentIndex, EndIndex;
    internal bool IsProgram, HasOperations, HasNestedProgram, HasPrograms, Checked;
    internal readonly List<CamObject> Operations = new List<CamObject>();
    internal bool HasOutput { get { return IsProgram && Operations.Count > 0; } }
}

internal static class ProgramFolderCatalog
{
    internal static List<ProgramFolderEntry> Read(NCGroup root)
    {
        if (root == null) throw new InvalidOperationException("\u041D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D \u043A\u043E\u0440\u0435\u043D\u044C \u0434\u0435\u0440\u0435\u0432\u0430 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C NX.");
        List<ProgramFolderEntry> result = new List<ProgramFolderEntry>();
        Walk(root, "", -1, -1, new HashSet<Tag>(), result);
        return result;
    }
    private static int Walk(NCGroup group, string path, int parentIndex, int programOwner,
        HashSet<Tag> seen, List<ProgramFolderEntry> result)
    {
        if (!seen.Add(group.Tag)) throw new InvalidOperationException("\u041F\u043E\u0432\u0442\u043E\u0440\u043D\u0430\u044F \u0438\u043B\u0438 \u0446\u0438\u043A\u043B\u0438\u0447\u0435\u0441\u043A\u0430\u044F \u0441\u0441\u044B\u043B\u043A\u0430 \u0432 \u0434\u0435\u0440\u0435\u0432\u0435 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C NX.");
        ProgramFolderEntry entry = new ProgramFolderEntry();
        entry.Group = group; entry.Path = parentIndex < 0 ? group.Name : path; entry.ParentIndex = parentIndex;
        entry.IsProgram = parentIndex >= 0 && (group.Name ?? "").StartsWith("O", StringComparison.OrdinalIgnoreCase);
        int index = result.Count; result.Add(entry);
        if (entry.IsProgram) programOwner = index;
        foreach (CamObject member in group.GetMembers())
        {
            if (member is NXOpen.CAM.Operation)
            {
                entry.HasOperations = true;
                // An operation belongs to its nearest O-program folder only.
                if (programOwner >= 0) result[programOwner].Operations.Add(member);
            }
            NCGroup child = member as NCGroup;
            if (child == null) continue;
            string childPath = path.Length == 0 ? child.Name : path + " / " + child.Name;
            int childIndex = Walk(child, childPath, index, programOwner, seen, result);
            ProgramFolderEntry childEntry = result[childIndex];
            entry.HasOperations |= childEntry.HasOperations;
            entry.HasNestedProgram |= childEntry.IsProgram || childEntry.HasNestedProgram;
            entry.HasPrograms |= childEntry.HasPrograms;
        }
        entry.HasPrograms |= entry.HasOutput;
        entry.EndIndex = result.Count;
        return index;
    }
    internal static List<ProgramJob> BuildJobs(List<ProgramFolderEntry> entries)
    {
        List<ProgramJob> jobs = new List<ProgramJob>();
        foreach (ProgramFolderEntry entry in entries)
        {
            if (!entry.Checked || !entry.HasOutput) continue;
            ProgramJob job = new ProgramJob();
            job.Group = entry.Group; job.GroupPath = entry.Path;
            // Normal O-programs keep their original group and group events.
            // A nested O-program is a separate output; never post its operations twice.
            job.WholeGroup = !entry.HasNestedProgram;
            if (job.WholeGroup) job.Objects.Add(entry.Group);
            else job.Objects.AddRange(entry.Operations);
            jobs.Add(job);
        }
        return jobs;
    }
}

internal sealed class ProgramFolderPicker : RouterDialog
{
    private readonly List<ProgramFolderEntry> entries;
    private readonly NCGroup programRoot;
    private readonly OperationSelectionSnapshot selectedOperations;
    private readonly List<object> nodes = new List<object>();
    private readonly object tree = RuntimeForms.New("TreeView");
    private readonly object countLabel, next;
    private readonly object numberOperations = RuntimeForms.New("CheckBox");
    private bool updating;
    internal List<ProgramJob> Jobs;
    internal bool NumberOperations { get { return (bool)RuntimeForms.Get(numberOperations, "Checked"); } }

    internal ProgramFolderPicker(NCGroup programRoot, OperationSelectionSnapshot selectedOperations) : base("\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u044B \u0434\u043B\u044F \u0432\u044B\u0432\u043E\u0434\u0430", 194)
    {
        this.programRoot = programRoot; this.selectedOperations = selectedOperations;
        entries = ProgramFolderCatalog.Read(programRoot);
        RuntimeForms.Add(Header, Label("\u041F\u0430\u043F\u043A\u0438 \u043F\u043E\u043A\u0430\u0437\u0430\u043D\u044B \u0441 \u0432\u043B\u043E\u0436\u0435\u043D\u043D\u043E\u0441\u0442\u044C\u044E \u0438 \u0432 \u043F\u043E\u0440\u044F\u0434\u043A\u0435 \u0434\u0435\u0440\u0435\u0432\u0430 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C NX.\n\u0413\u0430\u043B\u043E\u0447\u043A\u0430 \u043D\u0430 \u0440\u043E\u0434\u0438\u0442\u0435\u043B\u044C\u0441\u043A\u043E\u0439 \u043F\u0430\u043F\u043A\u0435 \u043E\u0442\u043C\u0435\u0447\u0430\u0435\u0442 \u0432\u0441\u044E \u0432\u0435\u0442\u043A\u0443.\n\u0412\u044B\u0432\u043E\u0434\u044F\u0442\u0441\u044F \u0442\u043E\u043B\u044C\u043A\u043E \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u044B \u0441 \u043B\u0430\u0442\u0438\u043D\u0441\u043A\u043E\u0439 O \u0432 \u043D\u0430\u0447\u0430\u043B\u0435 \u0438\u043C\u0435\u043D\u0438.", 0, 0, 760, 62));
        countLabel = Label("", 0, 72, 760, 26); RuntimeForms.Add(Header, countLabel);
        object selectedButton = Button("\u0412\u044B\u0432\u0435\u0441\u0442\u0438 \u0423\u041F \u0434\u043B\u044F \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0445 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439", 760, 38);
        Position(selectedButton, 0, 108, 760, 38); RuntimeForms.SetEnum(selectedButton, "Anchor", "Top, Left, Right");
        RuntimeForms.Call(Tips, "SetToolTip", selectedButton, "\u0418\u0441\u043F\u043E\u043B\u044C\u0437\u0443\u044E\u0442\u0441\u044F \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438, \u0432\u044B\u0434\u0435\u043B\u0435\u043D\u043D\u044B\u0435 \u0432 \u043D\u0430\u0432\u0438\u0433\u0430\u0442\u043E\u0440\u0435 NX \u043F\u0435\u0440\u0435\u0434 \u0437\u0430\u043F\u0443\u0441\u043A\u043E\u043C \u0441\u043A\u0440\u0438\u043F\u0442\u0430.");
        RuntimeForms.On(selectedButton, "Click", delegate { EditSelectedOperations(); }); RuntimeForms.Add(Header, selectedButton);
        Position(numberOperations, 0, 156, 760, 28);
        RuntimeForms.Set(numberOperations, "Text", "\u041D\u0443\u043C\u0435\u0440\u043E\u0432\u0430\u0442\u044C \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0438");
        RuntimeForms.Set(numberOperations, "Checked", false);
        RuntimeForms.SetEnum(numberOperations, "Anchor", "Top, Left, Right");
        RuntimeForms.Call(Tips, "SetToolTip", numberOperations, "\u041F\u0435\u0440\u0435\u0434 \u0432\u044B\u0432\u043E\u0434\u043E\u043C \u0434\u043E\u0431\u0430\u0432\u0438\u0442\u044C 01_, 02_, ... (\u0434\u043E 99 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439) \u0438\u043B\u0438 001_, 002_, ... (\u043E\u0442 100). \u0412 \u043A\u0430\u0436\u0434\u043E\u0439 \u0423\u041F \u2014 \u043D\u043E\u0432\u0430\u044F \u043D\u0443\u043C\u0435\u0440\u0430\u0446\u0438\u044F. \u0411\u0435\u0437 \u0433\u0430\u043B\u043E\u0447\u043A\u0438 \u0438\u043C\u0435\u043D\u0430 \u043D\u0435 \u043C\u0435\u043D\u044F\u044E\u0442\u0441\u044F.");
        RuntimeForms.Add(Header, numberOperations);
        // Replace the flat FlowLayoutPanel in the same docking position.
        object controls = RuntimeForms.Get(Window, "Controls");
        RuntimeForms.Call(controls, "Remove", Grid); RuntimeForms.Dispose(Grid);
        RuntimeForms.SetEnum(tree, "Dock", "Fill");
        RuntimeForms.Set(tree, "CheckBoxes", true); RuntimeForms.Set(tree, "Sorted", false);
        RuntimeForms.Set(tree, "ShowLines", true); RuntimeForms.Set(tree, "ShowPlusMinus", true);
        RuntimeForms.Set(tree, "ShowRootLines", true); RuntimeForms.Set(tree, "ShowNodeToolTips", true);
        RuntimeForms.Set(tree, "HideSelection", false); RuntimeForms.Set(tree, "Indent", 24);
        RuntimeForms.Set(tree, "ItemHeight", 26);
        RuntimeForms.Add(Window, tree); RuntimeForms.Call(controls, "SetChildIndex", tree, 0);
        RuntimeForms.Call(tree, "BeginUpdate");
        try
        {
            for (int i = 0; i < entries.Count; i++)
            {
                ProgramFolderEntry entry = entries[i]; object node = RuntimeForms.New("TreeNode");
                RuntimeForms.Set(node, "Text", entry.Group.Name ?? ""); RuntimeForms.Set(node, "Tag", i);
                string hint = entry.HasOutput ? "\u041F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u0430 \u0434\u043B\u044F \u0432\u044B\u0432\u043E\u0434\u0430: " + entry.Group.Name :
                    entry.HasPrograms ? "\u041E\u0431\u0449\u0430\u044F \u043F\u0430\u043F\u043A\u0430: \u0432\u044B\u0431\u043E\u0440 \u0432\u043B\u043E\u0436\u0435\u043D\u043D\u044B\u0445 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C \u0441 O." : "\u041D\u0435\u0442 \u0441\u0430\u043C\u043E\u0441\u0442\u043E\u044F\u0442\u0435\u043B\u044C\u043D\u044B\u0445 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C \u0441 O \u0434\u043B\u044F \u0432\u044B\u0432\u043E\u0434\u0430.";
                if (entry.HasNestedProgram && entry.HasOutput)
                    hint += "\n\u0412\u043B\u043E\u0436\u0435\u043D\u043D\u044B\u0435 O-\u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u044B \u0432\u044B\u0432\u043E\u0434\u044F\u0442\u0441\u044F \u043E\u0442\u0434\u0435\u043B\u044C\u043D\u043E, \u0431\u0435\u0437 \u043F\u043E\u0432\u0442\u043E\u0440\u0435\u043D\u0438\u044F \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439.";
                RuntimeForms.Set(node, "ToolTipText", entry.Path + "\n" + hint);
                if (!entry.HasPrograms) RuntimeForms.SetColor(node, "ForeColor", 128, 128, 128);
                object collection = RuntimeForms.Get(entry.ParentIndex < 0 ? tree : nodes[entry.ParentIndex], "Nodes");
                RuntimeForms.Call(collection, "Add", node); nodes.Add(node);
            }
            RuntimeForms.Call(tree, "ExpandAll");
            // Apply only the initial state; manual expansion remains available.
            for (int i = 0; i < entries.Count; i++)
                if (String.Equals(entries[i].Group.Name, "NONE", StringComparison.OrdinalIgnoreCase))
                    RuntimeForms.Call(nodes[i], "Collapse");
            if (nodes.Count > 0) RuntimeForms.Set(tree, "TopNode", nodes[0]);
        }
        finally { RuntimeForms.Call(tree, "EndUpdate"); }
        BindTreeEvent("BeforeCheck", "BeforeNodeCheck");
        BindTreeEvent("AfterCheck", "AfterNodeCheck");

        object all = Button("\u0412\u044B\u0431\u0440\u0430\u0442\u044C \u0432\u0441\u0435", 132, 38); Position(all, 0, 6, 132, 38);
        object clear = Button("\u0421\u043D\u044F\u0442\u044C \u0432\u044B\u0431\u043E\u0440", 132, 38); Position(clear, 144, 6, 132, 38);
        object expand = Button("\u0420\u0430\u0437\u0432\u0435\u0440\u043D\u0443\u0442\u044C", 120, 38); Position(expand, 288, 6, 120, 38);
        object collapse = Button("\u0421\u0432\u0435\u0440\u043D\u0443\u0442\u044C", 116, 38); Position(collapse, 420, 6, 116, 38);
        next = Button("\u0414\u0430\u043B\u0435\u0435", 112, 38); Position(next, 548, 6, 112, 38);
        RuntimeForms.SetEnum(next, "Anchor", "Bottom, Right"); RuntimeForms.Set(Window, "AcceptButton", next);
        RuntimeForms.On(all, "Click", delegate { SetBranch(0, true); });
        RuntimeForms.On(clear, "Click", delegate { SetBranch(0, false); });
        RuntimeForms.On(expand, "Click", delegate { RuntimeForms.Call(tree, "ExpandAll"); });
        RuntimeForms.On(collapse, "Click", delegate { RuntimeForms.Call(tree, "CollapseAll"); });
        RuntimeForms.On(next, "Click", delegate
        {
            try
            {
                List<ProgramJob> selected = ProgramFolderCatalog.BuildJobs(entries);
                if (selected.Count == 0) throw new InvalidOperationException("\u041E\u0442\u043C\u0435\u0442\u044C\u0442\u0435 \u0445\u043E\u0442\u044F \u0431\u044B \u043E\u0434\u043D\u0443 \u043D\u0435\u043F\u0443\u0441\u0442\u0443\u044E \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u0443 \u0441 O.");
                Jobs = selected; Finish("OK");
            }
            catch (Exception ex) { ShowProblem(ex); }
        });
        RuntimeForms.Add(Footer, all); RuntimeForms.Add(Footer, clear);
        RuntimeForms.Add(Footer, expand); RuntimeForms.Add(Footer, collapse); RuntimeForms.Add(Footer, next);
        RefreshCount();
    }
    private void EditSelectedOperations()
    {
        try
        {
            List<ProgramJob> selected = selectedOperations.BuildJobs(programRoot);
            using (SelectedOperationsPicker dialog = new SelectedOperationsPicker(selected))
            {
                // Cancel (including the window close button) leaves this picker open.
                if (dialog.ShowDialog(Window) != "OK") return;
            }
            Jobs = selected; Finish("OK");
        }
        catch (Exception ex) { ShowProblem(ex); }
    }
    private void BindTreeEvent(string eventName, string methodName)
    {
        // Bind native TreeView delegates without a compile-time WinForms reference.
        EventInfo eventInfo = tree.GetType().GetEvent(eventName);
        MethodInfo method = GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        eventInfo.AddEventHandler(tree, Delegate.CreateDelegate(eventInfo.EventHandlerType, this, method));
    }
    private void BeforeNodeCheck(object sender, EventArgs args)
    {
        if (updating) return;
        object node = RuntimeForms.Get(args, "Node");
        int index = (int)RuntimeForms.Get(node, "Tag");
        if (!entries[index].HasPrograms) RuntimeForms.Set(args, "Cancel", true);
    }
    private void AfterNodeCheck(object sender, EventArgs args)
    {
        if (updating) return;
        object node = RuntimeForms.Get(args, "Node");
        SetBranch((int)RuntimeForms.Get(node, "Tag"), (bool)RuntimeForms.Get(node, "Checked"));
    }
    private void SetBranch(int index, bool value)
    {
        if (index < 0 || index >= entries.Count) return;
        updating = true; RuntimeForms.Call(tree, "BeginUpdate");
        try
        {
            // Preorder intervals include every nested folder, even in a collapsed branch.
            for (int i = index; i < entries[index].EndIndex; i++)
            {
                entries[i].Checked = value; RuntimeForms.Set(nodes[i], "Checked", value);
            }
            if (!value)
                for (int parent = entries[index].ParentIndex; parent >= 0; parent = entries[parent].ParentIndex)
                {
                    // Structural parents must not suggest the entire branch is selected.
                    // An O-parent with its own operations remains an independent program.
                    if (entries[parent].HasOutput) continue;
                    entries[parent].Checked = false; RuntimeForms.Set(nodes[parent], "Checked", false);
                }
        }
        finally { RuntimeForms.Call(tree, "EndUpdate"); updating = false; RefreshCount(); }
    }
    private void RefreshCount()
    {
        int count = 0;
        foreach (ProgramFolderEntry entry in entries) if (entry.Checked && entry.HasOutput) count++;
        string text = entries.Count == 0 || !entries[0].HasPrograms ? "\u041D\u0435\u043F\u0443\u0441\u0442\u044B\u0435 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u044B \u0441 \u043B\u0430\u0442\u0438\u043D\u0441\u043A\u043E\u0439 O \u043D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D\u044B." : "\u041F\u0440\u043E\u0433\u0440\u0430\u043C\u043C \u043A \u0432\u044B\u0432\u043E\u0434\u0443: " + count;
        RuntimeForms.Set(countLabel, "Text", text); RuntimeForms.Set(next, "Enabled", count > 0);
    }
}


internal sealed class ProgramPostPicker : RouterDialog
{
    private readonly List<ProgramJob> jobs;
    private readonly List<PostDefinition> posts;
    private readonly string defaultExtension;
    private readonly List<object> rows = new List<object>();
    private readonly List<object> labels = new List<object>();
    private readonly List<object> choices = new List<object>();
    private bool arranging;
    public PostDefinition[] SelectedPosts;

    internal ProgramPostPicker(List<ProgramJob> jobs, List<PostDefinition> posts, string defaultExtension,
        PostDefinition[] previous) : base("\u0420\u0430\u0437\u0431\u0438\u0442\u044C \u043F\u043E \u0440\u0430\u0437\u043D\u044B\u043C \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430\u043C", 62)
    {
        if (jobs == null || jobs.Count == 0 || posts == null || posts.Count == 0)
            throw new ArgumentException("\u0414\u043B\u044F \u043D\u0430\u0437\u043D\u0430\u0447\u0435\u043D\u0438\u044F \u043D\u0443\u0436\u043D\u044B \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0435 \u0423\u041F \u0438 \u0441\u043F\u0438\u0441\u043E\u043A \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u043E\u0432.");
        this.jobs = jobs; this.posts = new List<PostDefinition>(posts); this.defaultExtension = defaultExtension;
        object hint = Label("\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440 \u0434\u043B\u044F \u043A\u0430\u0436\u0434\u043E\u0439 \u0423\u041F. \u041E\u0434\u0438\u043D \u043F\u043E\u0441\u0442 \u043C\u043E\u0436\u043D\u043E \u043D\u0430\u0437\u043D\u0430\u0447\u0438\u0442\u044C \u043D\u0435\u0441\u043A\u043E\u043B\u044C\u043A\u0438\u043C \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u0430\u043C.", 0, 0, 760, 50);
        RuntimeForms.SetEnum(hint, "Anchor", "Top, Left, Right"); RuntimeForms.Add(Header, hint);
        RuntimeForms.SetEnum(Grid, "FlowDirection", "TopDown"); RuntimeForms.Set(Grid, "WrapContents", false);
        for (int i = 0; i < jobs.Count; i++)
        {
            int index = i;
            ProgramJob job = jobs[i];
            object row = RuntimeForms.New("Panel"); Position(row, 0, 0, 754, 58);
            RuntimeForms.SetValue(row, "Margin", 0, 0, 0, 8);
            object label = Label((i + 1) + ". " + (job.OutputName ?? job.Group.Name) + "\n" + job.GroupPath, 0, 2, 302, 52);
            RuntimeForms.Set(label, "AutoEllipsis", true);
            RuntimeForms.Call(Tips, "SetToolTip", label, job.GroupPath + "\n" + job.SelectionDescription());
            object choice = RuntimeForms.New("ComboBox"); Position(choice, 314, 10, 432, 30);
            RuntimeForms.SetEnum(choice, "DropDownStyle", "DropDownList");
            RuntimeForms.Set(choice, "DropDownWidth", 620); RuntimeForms.Set(choice, "MaxDropDownItems", 16);
            object items = RuntimeForms.Get(choice, "Items");
            RuntimeForms.Call(items, "Add", "\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440...");
            int selected = 0;
            for (int p = 0; p < posts.Count; p++)
            {
                PostDefinition post = posts[p];
                RuntimeForms.Call(items, "Add", post.Name + " (" + IOPath.GetFileName(post.EventFile) + ")");
                if (previous != null && previous.Length == jobs.Count && previous[i] != null &&
                    String.Equals(previous[i].Key, post.Key, StringComparison.OrdinalIgnoreCase)) selected = p + 1;
            }
            RuntimeForms.Set(choice, "SelectedIndex", selected);
            rows.Add(row); labels.Add(label); choices.Add(choice);
            RuntimeForms.On(choice, "SelectedIndexChanged", delegate { DescribePost(index); });
            RuntimeForms.Add(row, label); RuntimeForms.Add(row, choice); RuntimeForms.Add(Grid, row);
            DescribePost(index);
        }
        object next = Button("\u0414\u0430\u043B\u0435\u0435", 112, 38); Position(next, 548, 6, 112, 38);
        RuntimeForms.SetEnum(next, "Anchor", "Bottom, Right");
        RuntimeForms.On(next, "Click", delegate { AcceptPosts(); });
        RuntimeForms.Set(Window, "AcceptButton", next); RuntimeForms.Add(Footer, next);
        RuntimeForms.On(Grid, "Resize", delegate { LayoutRows(); });
        RuntimeForms.On(Window, "Shown", delegate { LayoutRows(); });
        // Cancel is supplied by RouterDialog. Draft changes never mutate previous assignments.
    }
    private int Pixels(int value)
    {
        object dimensions = RuntimeForms.Get(Window, "AutoScaleDimensions");
        double scale = Convert.ToDouble(RuntimeForms.Get(dimensions, "Width"), CultureInfo.InvariantCulture) / 96.0;
        return Math.Max(1, (int)Math.Round(value * scale));
    }
    private void LayoutRows()
    {
        if (arranging) return;
        arranging = true;
        try
        {
            int width = Math.Max(Pixels(450), (int)RuntimeForms.Get(RuntimeForms.Get(Grid, "ClientSize"), "Width") - Pixels(28));
            int left = Math.Min(Pixels(320), width * 2 / 5);
            for (int i = 0; i < rows.Count; i++)
            {
                RuntimeForms.Set(rows[i], "Width", width);
                RuntimeForms.Set(labels[i], "Width", left - Pixels(12));
                RuntimeForms.Set(choices[i], "Left", left); RuntimeForms.Set(choices[i], "Width", width - left - Pixels(8));
            }
        }
        finally { arranging = false; }
    }
    private void DescribePost(int index)
    {
        int selected = (int)RuntimeForms.Get(choices[index], "SelectedIndex") - 1;
        string text = "\u041D\u0430\u0437\u043D\u0430\u0447\u044C\u0442\u0435 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440 \u0434\u043B\u044F \u044D\u0442\u043E\u0439 \u0423\u041F.";
        if (selected >= 0 && selected < posts.Count)
        {
            PostDefinition post = posts[selected];
            text = post.Name + "\n" + post.EventFile + "\n" + post.DefinitionFile;
        }
        RuntimeForms.Call(Tips, "SetToolTip", choices[index], text);
    }
    private void AcceptPosts()
    {
        try
        {
            PostDefinition[] selected = new PostDefinition[jobs.Count];
            for (int i = 0; i < jobs.Count; i++)
            {
                int index = (int)RuntimeForms.Get(choices[i], "SelectedIndex") - 1;
                if (index < 0 || index >= posts.Count)
                {
                    RuntimeForms.Call(Grid, "ScrollControlIntoView", rows[i]); RuntimeForms.Call(choices[i], "Focus");
                    throw new ArgumentException("\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440 \u0434\u043B\u044F \u0423\u041F \u00AB" + (jobs[i].OutputName ?? jobs[i].Group.Name) + "\u00BB.");
                }
                selected[i] = posts[index];
            }
            ProgramPostAssignments.Validate(selected, jobs.Count);
            ProgramPostAssignments.Extensions(selected, jobs.Count, defaultExtension);
            SelectedPosts = selected; Finish("OK");
        }
        catch (Exception ex) { ShowProblem(ex); }
    }
}

internal sealed class PostPathsDialog : RouterDialog
{
    private readonly PostIniDocument document;
    private readonly Func<string, string> environment;
    private readonly object list = RuntimeForms.New("ListBox");
    private readonly object nameInput = RuntimeForms.New("TextBox");
    private readonly object tclInput = RuntimeForms.New("TextBox");
    private readonly object defInput = RuntimeForms.New("TextBox");
    private readonly object details = RuntimeForms.New("Panel");
    private readonly object status;
    private readonly object remove;
    private int active = -1;
    private bool updating;

    internal PostPathsDialog(string configPath, Func<string, string> environment) : base("\u041F\u0443\u0442\u0438 \u043A \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430\u043C", 62)
    {
        this.environment = environment; document = new PostIniDocument(configPath);
        object hint = Label("\u0414\u043E\u0431\u0430\u0432\u044C\u0442\u0435 \u043F\u043E\u0441\u0442 \u0438\u043B\u0438 \u0432\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u0435\u0433\u043E \u0441\u043B\u0435\u0432\u0430. \u0418\u0437\u043C\u0435\u043D\u0435\u043D\u0438\u044F \u0437\u0430\u043F\u0438\u0441\u044B\u0432\u0430\u044E\u0442\u0441\u044F \u043F\u043E\u0441\u043B\u0435 \u043D\u0430\u0436\u0430\u0442\u0438\u044F \u00AB\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C\u00BB.\nINI: " + document.Path, 0, 0, 760, 52);
        RuntimeForms.Set(hint, "AutoEllipsis", true); RuntimeForms.SetEnum(hint, "Anchor", "Top, Left, Right");
        RuntimeForms.Call(Tips, "SetToolTip", hint, document.Path); RuntimeForms.Add(Header, hint);
        RuntimeForms.Call(RuntimeForms.Get(Window, "Controls"), "Remove", Grid); RuntimeForms.Dispose(Grid);
        object body = RuntimeForms.New("Panel"); RuntimeForms.SetEnum(body, "Dock", "Fill");
        RuntimeForms.Add(Window, body); RuntimeForms.Call(RuntimeForms.Get(Window, "Controls"), "SetChildIndex", body, 0);
        object left = RuntimeForms.New("Panel"); RuntimeForms.Set(left, "Width", 244); RuntimeForms.SetEnum(left, "Dock", "Left");
        object actions = RuntimeForms.New("Panel"); RuntimeForms.Set(actions, "Height", 46); RuntimeForms.SetEnum(actions, "Dock", "Bottom");
        RuntimeForms.SetEnum(list, "Dock", "Fill"); RuntimeForms.Set(list, "IntegralHeight", false);
        RuntimeForms.Set(list, "HorizontalScrollbar", true);
        object add = Button("\u0414\u043E\u0431\u0430\u0432\u0438\u0442\u044C\u2026", 118, 36); Position(add, 0, 8, 118, 36);
        remove = Button("\u0423\u0431\u0440\u0430\u0442\u044C", 114, 36); Position(remove, 126, 8, 114, 36);
        RuntimeForms.Call(Tips, "SetToolTip", remove, "\u0423\u0431\u0440\u0430\u0442\u044C \u0437\u0430\u043F\u0438\u0441\u044C \u0438\u0437 INI. \u0424\u0430\u0439\u043B\u044B TCL \u0438 DEF \u043D\u0435 \u0443\u0434\u0430\u043B\u044F\u044E\u0442\u0441\u044F.");
        RuntimeForms.Add(actions, add); RuntimeForms.Add(actions, remove);
        RuntimeForms.Add(left, list); RuntimeForms.Add(left, actions);
        RuntimeForms.SetEnum(details, "Dock", "Fill"); RuntimeForms.Set(details, "Width", 536);
        RuntimeForms.Add(body, details); RuntimeForms.Add(body, left);
        RuntimeForms.Add(details, Label("\u041D\u0430\u0437\u0432\u0430\u043D\u0438\u0435", 16, 8, 480, 22));
        Position(nameInput, 16, 32, 506, 28); RuntimeForms.SetEnum(nameInput, "Anchor", "Top, Left, Right"); RuntimeForms.Add(details, nameInput);
        RuntimeForms.Add(details, Label("TCL", 16, 80, 480, 22));
        Position(tclInput, 16, 104, 392, 28); RuntimeForms.SetEnum(tclInput, "Anchor", "Top, Left, Right"); RuntimeForms.Add(details, tclInput);
        object tclBrowse = Button("\u041E\u0431\u0437\u043E\u0440\u2026", 104, 30); Position(tclBrowse, 418, 102, 104, 30);
        RuntimeForms.SetEnum(tclBrowse, "Anchor", "Top, Right"); RuntimeForms.Add(details, tclBrowse);
        RuntimeForms.Add(details, Label("DEF", 16, 152, 480, 22));
        Position(defInput, 16, 176, 392, 28); RuntimeForms.SetEnum(defInput, "Anchor", "Top, Left, Right"); RuntimeForms.Add(details, defInput);
        object defBrowse = Button("\u041E\u0431\u0437\u043E\u0440\u2026", 104, 30); Position(defBrowse, 418, 174, 104, 30);
        RuntimeForms.SetEnum(defBrowse, "Anchor", "Top, Right"); RuntimeForms.Add(details, defBrowse);
        status = Label("", 16, 224, 506, 90); RuntimeForms.SetEnum(status, "Anchor", "Top, Left, Right"); RuntimeForms.Add(details, status);
        object save = Button("\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C", 124, 38); Position(save, 536, 6, 124, 38);
        RuntimeForms.SetEnum(save, "Anchor", "Bottom, Right"); RuntimeForms.Add(Footer, save); RuntimeForms.Set(Window, "AcceptButton", save);
        RuntimeForms.On(list, "SelectedIndexChanged", delegate { SelectEntry(); });
        RuntimeForms.On(nameInput, "TextChanged", delegate { EditEntry(); });
        RuntimeForms.On(tclInput, "TextChanged", delegate { EditEntry(); });
        RuntimeForms.On(defInput, "TextChanged", delegate { EditEntry(); });
        RuntimeForms.On(add, "Click", delegate { AddPost(); });
        RuntimeForms.On(remove, "Click", delegate { RemovePost(); });
        RuntimeForms.On(tclBrowse, "Click", delegate { Browse(true); });
        RuntimeForms.On(defBrowse, "Click", delegate { Browse(false); });
        RuntimeForms.On(save, "Click", delegate { Save(); });
        RefreshList(document.Posts.Count > 0 ? 0 : -1);
    }
    private void RefreshList(int selected)
    {
        updating = true;
        try
        {
            object items = RuntimeForms.Get(list, "Items"); RuntimeForms.Call(list, "BeginUpdate");
            try { RuntimeForms.Call(items, "Clear"); foreach (PostPathEntry p in document.Posts) RuntimeForms.Call(items, "Add", p.Name.Length == 0 ? "\u0411\u0435\u0437 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u044F" : p.Name); }
            finally { RuntimeForms.Call(list, "EndUpdate"); }
            RuntimeForms.Set(list, "SelectedIndex", selected);
        }
        finally { updating = false; }
        SelectEntry();
    }
    private void SelectEntry()
    {
        if (updating) return;
        active = (int)RuntimeForms.Get(list, "SelectedIndex");
        updating = true;
        try
        {
            bool selected = active >= 0 && active < document.Posts.Count;
            PostPathEntry post = selected ? document.Posts[active] : null;
            RuntimeForms.Set(details, "Enabled", selected); RuntimeForms.Set(remove, "Enabled", selected);
            RuntimeForms.Set(nameInput, "Text", selected ? post.Name : "");
            RuntimeForms.Set(tclInput, "Text", selected ? post.Tcl : "");
            RuntimeForms.Set(defInput, "Text", selected ? post.Def : "");
            RuntimeForms.Set(status, "Text", !selected ? "" : post.Def.Length == 0 ? "\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 DEF-\u0444\u0430\u0439\u043B \u0434\u043B\u044F \u044D\u0442\u043E\u0433\u043E \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430." : "\u041F\u0440\u0438 \u0432\u044B\u0431\u043E\u0440\u0435 TCL \u043E\u0434\u043D\u043E\u0438\u043C\u0451\u043D\u043D\u044B\u0439 DEF \u043F\u043E\u0434\u0441\u0442\u0430\u0432\u043B\u044F\u0435\u0442\u0441\u044F \u0430\u0432\u0442\u043E\u043C\u0430\u0442\u0438\u0447\u0435\u0441\u043A\u0438.");
            RuntimeForms.Call(Tips, "SetToolTip", tclInput, selected ? post.Tcl : "");
            RuntimeForms.Call(Tips, "SetToolTip", defInput, selected ? post.Def : "");
        }
        finally { updating = false; }
    }
    private void EditEntry()
    {
        if (updating || active < 0) return;
        PostPathEntry post = document.Posts[active];
        string name = (string)RuntimeForms.Get(nameInput, "Text"); bool renamed = name != post.Name;
        post.Name = name; post.Tcl = (string)RuntimeForms.Get(tclInput, "Text"); post.Def = (string)RuntimeForms.Get(defInput, "Text");
        if (renamed)
        {
            updating = true;
            try { RuntimeForms.Get(list, "Items").GetType().GetProperty("Item").SetValue(RuntimeForms.Get(list, "Items"), name.Length == 0 ? "\u0411\u0435\u0437 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u044F" : name, new object[] { active }); }
            finally { updating = false; }
        }
        RuntimeForms.Call(Tips, "SetToolTip", tclInput, post.Tcl); RuntimeForms.Call(Tips, "SetToolTip", defInput, post.Def);
        RuntimeForms.Set(status, "Text", post.Def.Length == 0 ? "\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 DEF-\u0444\u0430\u0439\u043B \u0434\u043B\u044F \u044D\u0442\u043E\u0433\u043E \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430." : "\u0418\u0437\u043C\u0435\u043D\u0435\u043D\u0438\u044F \u0435\u0449\u0451 \u043D\u0435 \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u044B.");
    }
    private string PickFile(bool tcl, string current)
    {
        object dialog = RuntimeForms.New("OpenFileDialog");
        try
        {
            RuntimeForms.Set(dialog, "Title", ScriptInfo.WindowTitle(tcl ? "\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 TCL" : "\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 DEF"));
            RuntimeForms.Set(dialog, "Filter", tcl ? "\u041F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440 (*.tcl)|*.tcl" : "\u041E\u043F\u0440\u0435\u0434\u0435\u043B\u0435\u043D\u0438\u0435 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430 (*.def)|*.def");
            RuntimeForms.Set(dialog, "CheckFileExists", true); RuntimeForms.Set(dialog, "CheckPathExists", true);
            RuntimeForms.Set(dialog, "Multiselect", false); RuntimeForms.Set(dialog, "RestoreDirectory", true);
            string initial = IOPath.GetDirectoryName(document.Path);
            if (!String.IsNullOrWhiteSpace(current))
            {
                try
                {
                    string resolved = RouterConfig.ResolvePath(current, initial, environment), directory = IOPath.GetDirectoryName(resolved);
                    if (Directory.Exists(directory)) initial = directory;
                    if (File.Exists(resolved)) RuntimeForms.Set(dialog, "FileName", resolved);
                }
                catch { /* A broken old path must not prevent choosing its replacement. */ }
            }
            RuntimeForms.Set(dialog, "InitialDirectory", initial);
            if (RuntimeForms.Show(dialog, Window) != "OK") return null;
            string selected = (string)RuntimeForms.Get(dialog, "FileName");
            string extension = tcl ? ".tcl" : ".def";
            if (!String.Equals(IOPath.GetExtension(selected), extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(selected))
                throw new ArgumentException("\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u0441\u0443\u0449\u0435\u0441\u0442\u0432\u0443\u044E\u0449\u0438\u0439 \u0444\u0430\u0439\u043B " + extension + ".");
            return IOPath.GetFullPath(selected);
        }
        finally { RuntimeForms.Dispose(dialog); }
    }
    private void AddPost()
    {
        try
        {
            string tcl = PickFile(true, active >= 0 ? document.Posts[active].Tcl : ""); if (tcl == null) return;
            document.Posts.Add(new PostPathEntry { Name = document.UniqueName(IOPath.GetFileNameWithoutExtension(tcl)), Tcl = tcl, Def = PostIniDocument.CompanionDef(tcl) });
            RefreshList(document.Posts.Count - 1); RuntimeForms.Call(nameInput, "Focus");
        }
        catch (Exception ex) { ShowProblem(ex); }
    }
    private void Browse(bool tcl)
    {
        try
        {
            if (active < 0) return;
            PostPathEntry post = document.Posts[active];
            string path = PickFile(tcl, tcl ? post.Tcl : post.Def); if (path == null) return;
            if (tcl)
            {
                string previousName = IOPath.GetFileNameWithoutExtension(post.Tcl);
                if (String.IsNullOrWhiteSpace(post.Name) || post.Name == previousName) post.Name = IOPath.GetFileNameWithoutExtension(path);
                post.Tcl = path; post.Def = PostIniDocument.CompanionDef(path);
            }
            else post.Def = path;
            RefreshList(active);
        }
        catch (Exception ex) { ShowProblem(ex); }
    }
    private void RemovePost()
    { if (active >= 0) { int previous = active; document.Posts.RemoveAt(active); RefreshList(Math.Min(previous, document.Posts.Count - 1)); } }
    private void Save()
    {
        try { document.Save(environment); Finish("OK"); }
        catch (Exception ex) { ShowProblem(ex); }
    }
}

internal sealed class PostPicker : RouterDialog
{
    private readonly string configPath;
    private readonly Func<string, string> environment;
    private readonly object search = RuntimeForms.New("TextBox");
    private readonly object source = RuntimeForms.New("Label");
    private List<PostDefinition> posts = new List<PostDefinition>();
    private readonly List<ProgramJob> jobs;
    private readonly PostDefinition[] previousPosts;
    private readonly object split = RuntimeForms.New("Button");
    public PostDefinition[] SelectedPosts;
    public RouterConfig Config;

    public PostPicker(string configPath, Func<string, string> environment, List<ProgramJob> jobs, PostDefinition[] previousPosts)
        : base("\u041A\u0430\u043A\u043E\u0439 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440 \u0438\u0441\u043F\u043E\u043B\u044C\u0437\u043E\u0432\u0430\u0442\u044C?", 186)
    {
        this.configPath = configPath;
        this.environment = environment; this.jobs = jobs;
        this.previousPosts = previousPosts == null ? null : (PostDefinition[])previousPosts.Clone();
        RuntimeForms.Add(Header, Label("\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440", 0, 0, 760, 26));
        Position(search, 0, 29, 760, 28);
        RuntimeForms.SetEnum(search, "Anchor", "Top, Left, Right");
        RuntimeForms.Call(Tips, "SetToolTip", search, "\u041F\u043E\u0438\u0441\u043A \u043F\u043E \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u044E \u0438\u043B\u0438 \u043F\u0443\u0442\u0438 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430");
        RuntimeForms.Add(Header, search);
        Position(source, 0, 66, 760, 66);
        RuntimeForms.Set(source, "UseMnemonic", false);
        RuntimeForms.Set(source, "AutoEllipsis", true);
        RuntimeForms.SetEnum(source, "Anchor", "Top, Left, Right");
        RuntimeForms.Add(Header, source);
        object edit = Button("\u041E\u0442\u043A\u0440\u044B\u0442\u044C INI", 130, 38);
        object reload = Button("\u041E\u0431\u043D\u043E\u0432\u0438\u0442\u044C", 122, 38);
        object configure = Button("\u041F\u0443\u0442\u0438 \u043A \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430\u043C", 272, 38);
        Position(configure, 0, 8, 272, 38); Position(edit, 284, 8, 130, 38); Position(reload, 426, 8, 122, 38);
        RuntimeForms.Add(Footer, configure);
        RuntimeForms.On(configure, "Click", delegate { ConfigurePosts(); });
        RuntimeForms.Add(Footer, edit); RuntimeForms.Add(Footer, reload);
        RuntimeForms.On(edit, "Click", delegate { EditConfig(); });
        RuntimeForms.On(reload, "Click", delegate { Reload(); });
        RuntimeForms.On(search, "TextChanged", delegate { DrawPosts(); });
        RuntimeForms.Set(split, "Text", "\u0420\u0430\u0437\u0431\u0438\u0442\u044C \u043F\u043E \u0440\u0430\u0437\u043D\u044B\u043C \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430\u043C");
        Position(split, 0, 140, 760, 38); RuntimeForms.SetEnum(split, "Anchor", "Top, Left, Right");
        RuntimeForms.Set(split, "UseVisualStyleBackColor", true);
        RuntimeForms.On(split, "Click", delegate { SplitPosts(); }); RuntimeForms.Add(Header, split);
        Reload();
    }

    private void Reload()
    {
        SelectedPosts = null;
        Config = null;
        posts = new List<PostDefinition>();
        try
        {
            if (!File.Exists(configPath))
            {
                RuntimeForms.Set(source, "Text", "\u041D\u0430\u0436\u043C\u0438\u0442\u0435 \u00AB\u041F\u0443\u0442\u0438 \u043A \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430\u043C\u00BB, \u0447\u0442\u043E\u0431\u044B \u0434\u043E\u0431\u0430\u0432\u0438\u0442\u044C \u043F\u043E\u0441\u0442\u044B \u0438 \u0441\u043E\u0437\u0434\u0430\u0442\u044C INI \u0440\u044F\u0434\u043E\u043C \u0441\u043E \u0441\u043A\u0440\u0438\u043F\u0442\u043E\u043C.");
                RuntimeForms.Set(split, "Enabled", false); DrawPosts(); return;
            }
            RouterConfig candidate = RouterConfig.Load(configPath, environment);
            List<PostDefinition> loaded = candidate.GetPosts(environment);
            Config = candidate;
            posts = loaded;
            string text = "INI: " + configPath + "\u000A\u041F\u043E\u0441\u043B\u0435 \u0438\u0437\u043C\u0435\u043D\u0435\u043D\u0438\u044F \u0444\u0430\u0439\u043B\u0430: \u0441\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u0435 \u0435\u0433\u043E \u0438 \u043D\u0430\u0436\u043C\u0438\u0442\u0435 \u00AB\u041E\u0431\u043D\u043E\u0432\u0438\u0442\u044C\u00BB.";
            RuntimeForms.Set(source, "Text", text);
            RuntimeForms.Call(Tips, "SetToolTip", source, text);
        }
        catch (Exception ex)
        {
            string text = "\u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438 \u043D\u0435 \u0437\u0430\u0433\u0440\u0443\u0436\u0435\u043D\u044B: " + RuntimeForms.ActualMessage(ex);
            RuntimeForms.Set(source, "Text", text);
            RuntimeForms.Call(Tips, "SetToolTip", source, text);
            ShowProblem(ex);
        }
        RuntimeForms.Set(split, "Enabled", Config != null && posts.Count > 0 && jobs.Count > 0);
        DrawPosts();
    }

    private void ConfigurePosts()
    {
        try
        {
            using (PostPathsDialog dialog = new PostPathsDialog(configPath, environment))
            {
                if (dialog.ShowDialog(Window) != "OK") return;
            }
            RuntimeForms.Set(search, "Text", ""); Reload();
        }
        catch (Exception ex) { ShowProblem(ex); }
    }

    private void SplitPosts()
    {
        try
        {
            if (Config == null || posts.Count == 0) return;
            using (ProgramPostPicker dialog = new ProgramPostPicker(jobs, posts, Config.DefaultExtension, previousPosts))
            {
                if (dialog.ShowDialog(Window) != "OK") return;
                SelectedPosts = dialog.SelectedPosts;
            }
            Finish("OK");
        }
        catch (Exception ex) { ShowProblem(ex); }
    }

    private void DrawPosts()
    {
        RuntimeForms.Call(Grid, "SuspendLayout");
        ClearGrid();
        string query = ((string)RuntimeForms.Get(search, "Text")).Trim();
        int visible = 0;
        foreach (PostDefinition entry in posts)
        {
            PostDefinition post = entry;
            string full = post.Name + "\n" + post.EventFile + "\n" + post.DefinitionFile;
            if (query.Length > 0 && full.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            object button = Tile(post.Name + "\n" + IOPath.GetFileName(post.EventFile), full, 238, 72);
            visible++;
            RuntimeForms.On(button, "Click", delegate
            {
                try { post.Validate(); SelectedPosts = ProgramPostAssignments.Repeat(post, jobs.Count); Finish("OK"); }
                catch (Exception ex) { ShowProblem(ex); }
            });
        }
        if (visible == 0)
            RuntimeForms.Add(Grid, Label("\u041D\u0435\u0442 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u043E\u0432 \u0434\u043B\u044F \u0432\u044B\u0431\u043E\u0440\u0430. \u0414\u043E\u0431\u0430\u0432\u044C\u0442\u0435 \u0438\u0445 \u0447\u0435\u0440\u0435\u0437 \u00AB\u041F\u0443\u0442\u0438 \u043A \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430\u043C\u00BB \u0438\u043B\u0438 \u0438\u0437\u043C\u0435\u043D\u0438\u0442\u0435 \u043F\u043E\u0438\u0441\u043A.", 0, 0, 700, 42));
        RuntimeForms.Call(Grid, "ResumeLayout");
    }

    private void EditConfig()
    {
        try
        {
            if (!File.Exists(configPath))
                throw new FileNotFoundException("\u041F\u043E\u043B\u043E\u0436\u0438\u0442\u0435 INI \u0438\u0437 \u043A\u043E\u043C\u043F\u043B\u0435\u043A\u0442\u0430 \u0440\u044F\u0434\u043E\u043C \u0441 CS-\u0444\u0430\u0439\u043B\u043E\u043C:\u000A" + configPath);
            string editor = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
            System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo(editor, "\"" + configPath + "\"");
            start.UseShellExecute = false;
            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(start)) { }
        }
        catch (Exception ex) { ShowProblem(ex); }
    }
}

internal sealed class ExternalDrivePicker : RouterDialog
{
    private readonly IExternalDriveProbe probe;
    private readonly object status;
    internal ExternalDriveSelection Selection;

    internal ExternalDrivePicker() : this(new WindowsExternalDriveProbe()) { }
    internal ExternalDrivePicker(IExternalDriveProbe source) : base("\u0412\u044B\u0432\u043E\u0434 \u043D\u0430 \u0432\u043D\u0435\u0448\u043D\u0438\u0439 \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u044C", 110)
    {
        probe = source;
        RuntimeForms.Add(Header, Label("\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u044C \u0434\u043B\u044F \u0432\u044B\u0432\u043E\u0434\u0430 \u0423\u041F \u0438 FANUCPRG.BIN.\n\u0412\u044B\u0432\u043E\u0434 \u2014 \u0432 \u043A\u043E\u0440\u0435\u043D\u044C \u0434\u0438\u0441\u043A\u0430, \u043F\u043E\u0441\u043B\u0435 \u043D\u0430\u0436\u0430\u0442\u0438\u044F \u00AB\u0412\u044B\u0432\u0435\u0441\u0442\u0438\u00BB \u0432 \u043C\u0435\u043D\u044E.", 0, 0, 760, 40));
        status = Label("", 0, 48, 760, 56);
        RuntimeForms.SetEnum(status, "Anchor", "Top, Left, Right");
        RuntimeForms.Add(Header, status);
        object refresh = Button("\u041E\u0431\u043D\u043E\u0432\u0438\u0442\u044C", 160, 38); Position(refresh, 0, 6, 160, 38);
        RuntimeForms.On(refresh, "Click", delegate { RefreshDrives(); });
        RuntimeForms.Add(Footer, refresh);
        RefreshDrives();
    }
    private void RefreshDrives()
    {
        ClearGrid(); Selection = null;
        try
        {
            List<string> warnings;
            List<ExternalDriveInfo> drives = ExternalDrives.Scan(probe, out warnings);
            string text = drives.Count == 0 ? "\u0412\u043D\u0435\u0448\u043D\u0438\u0435 \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u0438 \u043D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D\u044B" : "\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u044C. \u0412\u044B\u0432\u043E\u0434 \u0437\u0430\u043F\u0443\u0441\u043A\u0430\u0435\u0442\u0441\u044F \u043A\u043D\u043E\u043F\u043A\u043E\u0439 \u00AB\u0412\u044B\u0432\u0435\u0441\u0442\u0438\u00BB \u0432 \u043F\u0440\u0435\u0434\u044B\u0434\u0443\u0449\u0435\u043C \u043C\u0435\u043D\u044E.";
            text += "\n\u041F\u043E\u0441\u043B\u0435 \u043F\u043E\u0434\u043A\u043B\u044E\u0447\u0435\u043D\u0438\u044F \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u044F \u043D\u0430\u0436\u043C\u0438\u0442\u0435 \u00AB\u041E\u0431\u043D\u043E\u0432\u0438\u0442\u044C\u00BB.";
            if (warnings.Count > 0) text += "\n\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043F\u0440\u043E\u0447\u0438\u0442\u0430\u0442\u044C \u0447\u0430\u0441\u0442\u044C \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u0435\u0439: " + warnings.Count + ". \u041F\u043E\u0434\u0440\u043E\u0431\u043D\u043E\u0441\u0442\u0438 \u043F\u0440\u0438 \u043D\u0430\u0432\u0435\u0434\u0435\u043D\u0438\u0438.";
            RuntimeForms.Set(status, "Text", text);
            RuntimeForms.Call(Tips, "SetToolTip", status, String.Join("\n", warnings.ToArray()));
            foreach (ExternalDriveInfo item in drives)
            {
                ExternalDriveInfo info = item;
                object button = Tile(info.Details, info.Root + (info.ReadOnly ? " \u2014 \u0442\u043E\u043B\u044C\u043A\u043E \u0447\u0442\u0435\u043D\u0438\u0435" : " \u2014 \u0432\u044B\u0432\u0435\u0441\u0442\u0438 \u0432 \u043A\u043E\u0440\u0435\u043D\u044C \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u044F"), 366, 84);
                RuntimeForms.Set(button, "Enabled", !info.ReadOnly);
                RuntimeForms.On(button, "Click", delegate
                {
                    try { Selection = new ExternalDriveSelection(info, probe); Finish("OK"); }
                    catch (Exception ex) { ShowProblem(ex); RefreshDrives(); }
                });
            }
        }
        catch (Exception ex)
        {
            RuntimeForms.Set(status, "Text", "\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043F\u043E\u043B\u0443\u0447\u0438\u0442\u044C \u0441\u043F\u0438\u0441\u043E\u043A \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u0435\u0439. \u041D\u0430\u0436\u043C\u0438\u0442\u0435 \u00AB\u041E\u0431\u043D\u043E\u0432\u0438\u0442\u044C\u00BB.");
            ShowProblem(ex);
        }
    }
}


internal sealed class ProgramCallPicker : RouterDialog
{
    private readonly object list = RuntimeForms.New("ListBox");
    private readonly object enabled = RuntimeForms.New("CheckBox");
    private readonly object hint = RuntimeForms.New("Label");
    private readonly object up, down, first, last;
    private readonly string[] names;
    private readonly List<int> order;
    private readonly string dragToken = "NX_PROGRAM_CALL_ORDER_" + Guid.NewGuid().ToString("N");
    private int dragFrom = -1, mouseX, mouseY;
    private bool dragging;
    internal bool ChainEnabled;
    internal int[] Order;

    internal ProgramCallPicker(string[] programNames, int[] currentOrder, bool chainEnabled)
        : base("\u0412\u044B\u0437\u043E\u0432 \u043F\u043E\u0434\u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C", 162)
    {
        names = (string[])programNames.Clone();
        order = new List<int>(currentOrder ?? ProgramCallChain.Identity(names.Length));
        ProgramCallChain.ValidateOrder(order.ToArray(), names.Length);
        RuntimeForms.Set(enabled, "Text", "\u0412\u044B\u0437\u0432\u0430\u0442\u044C \u043E\u0441\u0442\u0430\u043B\u044C\u043D\u044B\u0435 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u044B \u0438\u0437 \u043F\u0435\u0440\u0432\u043E\u0439 \u0423\u041F (M98)");
        Position(enabled, 0, 0, 760, 28);
        RuntimeForms.Set(enabled, "Checked", chainEnabled);
        RuntimeForms.Add(Header, enabled);
        RuntimeForms.Add(Header, Label("\u041F\u0435\u0440\u0435\u043C\u0435\u0449\u0430\u0439\u0442\u0435 \u0423\u041F \u043C\u044B\u0448\u044C\u044E \u0438\u043B\u0438 \u043A\u043D\u043E\u043F\u043A\u0430\u043C\u0438 \u0441\u043F\u0440\u0430\u0432\u0430. \u041F\u0435\u0440\u0432\u0430\u044F \u0423\u041F \u2014 \u0433\u043B\u0430\u0432\u043D\u0430\u044F.\n\u0412 \u043A\u043E\u043D\u0446\u0435 \u043F\u0435\u0440\u0432\u043E\u0439 \u0423\u041F \u0434\u043E\u0431\u0430\u0432\u043B\u044F\u044E\u0442\u0441\u044F \u0432\u044B\u0437\u043E\u0432\u044B \u0432\u0441\u0435\u0445 \u043E\u0441\u0442\u0430\u043B\u044C\u043D\u044B\u0445 \u0432 \u043F\u043E\u0440\u044F\u0434\u043A\u0435 \u0441\u043F\u0438\u0441\u043A\u0430.", 0, 36, 760, 42));
        RuntimeForms.Add(Header, Label("\u0413\u043B\u0430\u0432\u043D\u0430\u044F \u0423\u041F: \u0432\u0441\u0435 M98 \u043F\u043E \u043E\u0447\u0435\u0440\u0435\u0434\u0438, \u0437\u0430\u0442\u0435\u043C M30. \u041E\u0441\u0442\u0430\u043B\u044C\u043D\u044B\u0435 \u0423\u041F: \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0438\u0435 M99.\n\u041F\u043E\u0441\u043B\u0435 \u043A\u0430\u0436\u0434\u043E\u0439 \u043F\u043E\u0434\u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u044B \u0443\u043F\u0440\u0430\u0432\u043B\u0435\u043D\u0438\u0435 \u0432\u043E\u0437\u0432\u0440\u0430\u0449\u0430\u0435\u0442\u0441\u044F \u0432 \u0433\u043B\u0430\u0432\u043D\u0443\u044E \u0423\u041F.", 0, 84, 760, 42));
        Position(hint, 0, 132, 760, 28); RuntimeForms.Add(Header, hint);

        RuntimeForms.Call(RuntimeForms.Get(Window, "Controls"), "Remove", Grid);
        RuntimeForms.Dispose(Grid);
        object body = RuntimeForms.New("Panel"); RuntimeForms.SetEnum(body, "Dock", "Fill");
        object side = RuntimeForms.New("Panel"); RuntimeForms.Set(side, "Width", 132); RuntimeForms.SetEnum(side, "Dock", "Right");
        RuntimeForms.Set(list, "IntegralHeight", false);
        RuntimeForms.Set(list, "HorizontalScrollbar", true);
        RuntimeForms.Set(list, "AllowDrop", true);
        RuntimeForms.SetEnum(list, "SelectionMode", "One");
        RuntimeForms.SetEnum(list, "Dock", "Fill");
        RuntimeForms.Add(body, list); RuntimeForms.Add(body, side);
        RuntimeForms.Add(Window, body);
        RuntimeForms.Call(RuntimeForms.Get(Window, "Controls"), "SetChildIndex", body, 0);
        up = OrderButton(side, "\u2191 \u0412\u0432\u0435\u0440\u0445", 8, delegate { MoveSelected(-1, false); });
        down = OrderButton(side, "\u2193 \u0412\u043D\u0438\u0437", 52, delegate { MoveSelected(1, false); });
        first = OrderButton(side, "\u0412 \u043D\u0430\u0447\u0430\u043B\u043E", 104, delegate { MoveSelected(0, true); });
        last = OrderButton(side, "\u0412 \u043A\u043E\u043D\u0435\u0446", 148, delegate { MoveSelected(order.Count - 1, true); });
        OrderButton(side, "\u041F\u043E\u0440\u044F\u0434\u043E\u043A NX", 208, delegate
        {
            order.Clear(); order.AddRange(ProgramCallChain.Identity(names.Length)); RefreshList(0);
        });
        object apply = Button("\u041F\u0440\u0438\u043C\u0435\u043D\u0438\u0442\u044C", 140, 38); Position(apply, 0, 6, 140, 38);
        RuntimeForms.On(apply, "Click", delegate
        {
            try
            {
                bool use = (bool)RuntimeForms.Get(enabled, "Checked");
                int[] selected = order.ToArray();
                if (use) ProgramCallChain.Targets(names, selected);
                ChainEnabled = use; Order = selected; Finish("OK");
            }
            catch (Exception ex) { ShowProblem(ex); }
        });
        RuntimeForms.Add(Footer, apply);
        RuntimeForms.On(enabled, "CheckedChanged", delegate { RefreshList((int)RuntimeForms.Get(list, "SelectedIndex")); });
        RuntimeForms.On(list, "SelectedIndexChanged", delegate { RefreshButtons(); });
        BindEvent("MouseDown", "MouseDown"); BindEvent("MouseMove", "MouseMove"); BindEvent("MouseUp", "MouseUp");
        BindEvent("DragEnter", "DragOver"); BindEvent("DragOver", "DragOver"); BindEvent("DragDrop", "DragDrop");
        RefreshList(0);
    }

    private object OrderButton(object parent, string label, int top, EventHandler action)
    {
        object button = Button(label, 120, 36); Position(button, 12, top, 120, 36);
        RuntimeForms.On(button, "Click", action); RuntimeForms.Add(parent, button); return button;
    }
    private void RefreshList(int selected)
    {
        bool use = (bool)RuntimeForms.Get(enabled, "Checked");
        string[][] targets = null;
        string note = use ? "\u0417\u0430\u043F\u0443\u0441\u043A \u0441 " + names[order[0]] + ". \u041F\u043E\u0441\u043B\u0435\u0434\u043E\u0432\u0430\u0442\u0435\u043B\u044C\u043D\u044B\u0445 \u0432\u044B\u0437\u043E\u0432\u043E\u0432: " + (order.Count - 1) + "." : "\u0412\u044B\u0437\u043E\u0432\u044B \u0432\u044B\u043A\u043B\u044E\u0447\u0435\u043D\u044B \u2014 M98 \u0438 \u043A\u043E\u043C\u0430\u043D\u0434\u044B \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0438\u044F \u043D\u0435 \u0438\u0437\u043C\u0435\u043D\u044F\u044E\u0442\u0441\u044F.";
        if (use)
        {
            try { targets = ProgramCallChain.Targets(names, order.ToArray()); }
            catch (Exception) { note = "\u041F\u0440\u043E\u0432\u0435\u0440\u044C\u0442\u0435 \u043D\u043E\u043C\u0435\u0440\u0430 \u0423\u041F: \u043D\u0443\u0436\u043D\u044B \u0440\u0430\u0437\u043D\u044B\u0435 \u043D\u043E\u043C\u0435\u0440\u0430 O1\u2013O9999."; }
        }
        RuntimeForms.Set(hint, "Text", note);
        RuntimeForms.Call(list, "BeginUpdate");
        try
        {
            object items = RuntimeForms.Get(list, "Items"); RuntimeForms.Call(items, "Clear");
            for (int i = 0; i < order.Count; i++)
            {
                string suffix = "";
                if (use && i == 0)
                {
                    suffix = " \u2014 \u0433\u043B\u0430\u0432\u043D\u0430\u044F; ";
                    if (targets != null)
                        foreach (string target in targets[order[0]]) suffix += "M98 P" + target + "; ";
                    suffix += "M30";
                }
                else if (use)
                    suffix = " \u2014 \u043F\u043E\u0434\u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u0430; M99 \u2192 \u0432\u043E\u0437\u0432\u0440\u0430\u0442 \u0432 " + names[order[0]];
                RuntimeForms.Call(items, "Add", (i + 1) + ". " + names[order[i]] + suffix);
            }
            RuntimeForms.Set(list, "SelectedIndex", Math.Max(0, Math.Min(selected, order.Count - 1)));
        }
        finally { RuntimeForms.Call(list, "EndUpdate"); }
        RefreshButtons();
    }
    private void RefreshButtons()
    {
        int index = (int)RuntimeForms.Get(list, "SelectedIndex");
        RuntimeForms.Set(up, "Enabled", index > 0); RuntimeForms.Set(first, "Enabled", index > 0);
        RuntimeForms.Set(down, "Enabled", index >= 0 && index + 1 < order.Count);
        RuntimeForms.Set(last, "Enabled", index >= 0 && index + 1 < order.Count);
    }
    private void MoveSelected(int value, bool absolute)
    {
        int from = (int)RuntimeForms.Get(list, "SelectedIndex");
        int to = absolute ? value : from + value;
        if (from < 0 || to < 0 || to >= order.Count) return;
        ProgramCallChain.Move(order, from, to); RefreshList(to);
    }
    private void BindEvent(string eventName, string methodName)
    {
        EventInfo eventInfo = list.GetType().GetEvent(eventName);
        MethodInfo method = GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        eventInfo.AddEventHandler(list, Delegate.CreateDelegate(eventInfo.EventHandlerType, this, method));
    }
    private void MouseDown(object sender, EventArgs args)
    {
        dragFrom = -1;
        if (RuntimeForms.Get(args, "Button").ToString() != "Left") return;
        mouseX = (int)RuntimeForms.Get(args, "X"); mouseY = (int)RuntimeForms.Get(args, "Y");
        dragFrom = (int)RuntimeForms.Call(list, "IndexFromPoint", mouseX, mouseY);
    }
    private void MouseUp(object sender, EventArgs args) { if (!dragging) dragFrom = -1; }
    private void MouseMove(object sender, EventArgs args)
    {
        if (dragging || dragFrom < 0 || RuntimeForms.Get(args, "Button").ToString() != "Left") return;
        int x = (int)RuntimeForms.Get(args, "X"), y = (int)RuntimeForms.Get(args, "Y");
        if (Math.Abs(x - mouseX) < 5 && Math.Abs(y - mouseY) < 5) return;
        try
        {
            dragging = true;
            RuntimeForms.Call(list, "DoDragDrop", dragToken, Enum.Parse(RuntimeForms.FormType("DragDropEffects"), "Move"));
        }
        catch (Exception ex) { ShowProblem(ex); }
        finally { dragging = false; dragFrom = -1; }
    }
    private bool OwnDrag(EventArgs args)
    {
        if (!dragging || dragFrom < 0 || dragFrom >= order.Count) return false;
        object data = RuntimeForms.Get(args, "Data");
        return data != null && String.Equals(RuntimeForms.Call(data, "GetData", "System.String") as string, dragToken, StringComparison.Ordinal);
    }
    private void DragOver(object sender, EventArgs args)
    {
        try { RuntimeForms.SetEnum(args, "Effect", OwnDrag(args) ? "Move" : "None"); }
        catch { RuntimeForms.SetEnum(args, "Effect", "None"); }
    }
    private void DragDrop(object sender, EventArgs args)
    {
        try
        {
            if (!OwnDrag(args)) return;
            Type pointType = list.GetType().GetProperty("Location").PropertyType;
            object screen = Activator.CreateInstance(pointType, new object[] { RuntimeForms.Get(args, "X"), RuntimeForms.Get(args, "Y") });
            object local = RuntimeForms.Call(list, "PointToClient", screen);
            int target = (int)RuntimeForms.Call(list, "IndexFromPoint", RuntimeForms.Get(local, "X"), RuntimeForms.Get(local, "Y"));
            if (target < 0) target = (int)RuntimeForms.Get(local, "Y") < 0 ? 0 : order.Count - 1;
            ProgramCallChain.Move(order, dragFrom, target); RefreshList(target);
        }
        catch (Exception ex) { ShowProblem(ex); }
    }
}

internal sealed class WorkOffsetPicker : RouterDialog
{
    private readonly object number = RuntimeForms.New("NumericUpDown");
    private readonly object code = RuntimeForms.New("Label");
    private readonly object list = RuntimeForms.New("ListBox");
    private readonly object summary = RuntimeForms.New("Label");
    private readonly List<int> offsets;
    private readonly object up, down, first, last, remove;
    internal bool ProcessingEnabled;
    internal int[] Offsets;

    internal WorkOffsetPicker(int[] current, bool use) : base("\u041E\u0431\u0440\u0430\u0431\u043E\u0442\u043A\u0430 \u043F\u043E \u043F\u0440\u0438\u0432\u044F\u0437\u043A\u0430\u043C", 186)
    {
        offsets = new List<int>(current ?? new int[] { 1, 2 });
        WorkOffsetPrograms.Validate(offsets.ToArray());
        RuntimeForms.SetValue(Window, "MinimumSize", 800, 580);
        ProcessingEnabled = use;
        RuntimeForms.Add(Header, Label("\u00AB\u041F\u0440\u0438\u043C\u0435\u043D\u0438\u0442\u044C \u043F\u0440\u0438\u0432\u044F\u0437\u043A\u0438\u00BB \u0432\u043A\u043B\u044E\u0447\u0430\u0435\u0442 \u043E\u0431\u0440\u0430\u0431\u043E\u0442\u043A\u0443 \u043A\u0430\u0436\u0434\u043E\u0433\u043E \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442\u0430 \u043D\u0430 \u0432\u0441\u0451\u043C \u0441\u043F\u0438\u0441\u043A\u0435.", 0, 0, 770, 28));
        RuntimeForms.Add(Header, Label("\u041E\u0434\u0438\u043D\u0430\u043A\u043E\u0432\u0430\u044F \u043E\u0431\u0440\u0430\u0431\u043E\u0442\u043A\u0430 \u043D\u0430 \u043D\u0435\u0441\u043A\u043E\u043B\u044C\u043A\u0438\u0445 \u0434\u0435\u0442\u0430\u043B\u044F\u0445: 1 = G54, 2 = G55, \u2026, 6 = G59.\n\u0414\u043B\u044F \u043A\u0430\u0436\u0434\u043E\u0439 \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u043E\u0439 \u0423\u041F \u043F\u0440\u0438\u043C\u0435\u043D\u044F\u0435\u0442\u0441\u044F \u044D\u0442\u043E\u0442 \u0441\u043F\u0438\u0441\u043E\u043A. MCS \u0432 NX \u043C\u0435\u043D\u044F\u0442\u044C \u043D\u0435 \u043D\u0443\u0436\u043D\u043E.", 0, 36, 770, 44));
        RuntimeForms.Add(Header, Label("\u041F\u0440\u0438\u0432\u044F\u0437\u043A\u0430:", 0, 94, 100, 26));
        Position(number, 108, 88, 76, 30); RuntimeForms.Set(number, "Minimum", 1M); RuntimeForms.Set(number, "Maximum", 6M);
        RuntimeForms.Set(number, "DecimalPlaces", 0); RuntimeForms.Set(number, "Value", 1M); RuntimeForms.Add(Header, number);
        Position(code, 198, 94, 96, 26); RuntimeForms.Add(Header, code);
        object add = Button("\u0414\u043E\u0431\u0430\u0432\u0438\u0442\u044C", 140, 36); Position(add, 312, 86, 140, 36);
        RuntimeForms.On(add, "Click", delegate
        {
            try
            {
                int value = Decimal.ToInt32((decimal)RuntimeForms.Get(number, "Value"));
                if (offsets.Contains(value)) throw new ArgumentException("\u042D\u0442\u0430 \u043F\u0440\u0438\u0432\u044F\u0437\u043A\u0430 \u0443\u0436\u0435 \u0435\u0441\u0442\u044C \u0432 \u0441\u043F\u0438\u0441\u043A\u0435.");
                offsets.Add(value); RefreshList(offsets.Count - 1);
                for (int candidate = 1; candidate <= 6; candidate++)
                    if (!offsets.Contains(candidate)) { RuntimeForms.Set(number, "Value", (decimal)candidate); break; }
            }
            catch (Exception ex) { ShowProblem(ex); }
        });
        RuntimeForms.Add(Header, add);
        RuntimeForms.Add(Header, Label("\u041F\u043E\u0440\u044F\u0434\u043E\u043A \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442\u043E\u0432 \u0438 \u043E\u043F\u0435\u0440\u0430\u0446\u0438\u0439 \u2014 \u043A\u0430\u043A \u0432 \u0438\u0441\u0445\u043E\u0434\u043D\u043E\u0439 \u0423\u041F. \u041C\u0435\u043D\u044F\u0435\u0442\u0441\u044F \u043F\u043E\u0440\u044F\u0434\u043E\u043A \u043F\u0440\u0438\u0432\u044F\u0437\u043E\u043A.\n\u0420\u0435\u0436\u0438\u043C \u0434\u043B\u044F 3-\u043E\u0441\u0435\u0432\u043E\u0433\u043E ISO-\u043A\u043E\u0434\u0430 \u0441 M06, G54\u2013G59 \u0438 \u043E\u0442\u0432\u043E\u0434\u043E\u043C G91 G28 Z0.", 0, 134, 770, 44));
        RuntimeForms.On(number, "ValueChanged", delegate { RefreshCode(); }); RefreshCode();

        RuntimeForms.Call(RuntimeForms.Get(Window, "Controls"), "Remove", Grid); RuntimeForms.Dispose(Grid);
        object body = RuntimeForms.New("Panel"); RuntimeForms.SetEnum(body, "Dock", "Fill");
        object side = RuntimeForms.New("Panel"); RuntimeForms.Set(side, "Width", 140); RuntimeForms.SetEnum(side, "Dock", "Right");
        RuntimeForms.Set(list, "IntegralHeight", false); RuntimeForms.Set(list, "HorizontalScrollbar", true);
        RuntimeForms.SetEnum(list, "Dock", "Fill"); RuntimeForms.SetEnum(list, "SelectionMode", "One");
        RuntimeForms.Add(body, list); RuntimeForms.Add(body, side); RuntimeForms.Add(Window, body);
        RuntimeForms.Call(RuntimeForms.Get(Window, "Controls"), "SetChildIndex", body, 0);
        up = OrderButton(side, "\u2191 \u0412\u0432\u0435\u0440\u0445", 0, delegate { Move(-1, false); });
        down = OrderButton(side, "\u2193 \u0412\u043D\u0438\u0437", 42, delegate { Move(1, false); });
        first = OrderButton(side, "\u0412 \u043D\u0430\u0447\u0430\u043B\u043E", 84, delegate { Move(0, true); });
        last = OrderButton(side, "\u0412 \u043A\u043E\u043D\u0435\u0446", 126, delegate { Move(offsets.Count - 1, true); });
        remove = OrderButton(side, "\u0423\u0434\u0430\u043B\u0438\u0442\u044C", 178, delegate
        {
            int index = (int)RuntimeForms.Get(list, "SelectedIndex");
            if (index < 0) return; offsets.RemoveAt(index); RefreshList(index);
        });
        RuntimeForms.Set(Footer, "Height", 92);
        Position(summary, 0, 0, 770, 38); RuntimeForms.SetEnum(summary, "Anchor", "Top, Left, Right");
        RuntimeForms.Set(summary, "AutoEllipsis", true); RuntimeForms.Add(Footer, summary);
        object cancel = RuntimeForms.Get(Window, "CancelButton"); RuntimeForms.SetEnum(cancel, "Dock", "None");
        Position(cancel, 672, 48, 112, 38); RuntimeForms.SetEnum(cancel, "Anchor", "Bottom, Right");
        object apply = Button("\u041F\u0440\u0438\u043C\u0435\u043D\u0438\u0442\u044C \u043F\u0440\u0438\u0432\u044F\u0437\u043A\u0438", 220, 38); Position(apply, 0, 48, 220, 38);
        RuntimeForms.SetEnum(apply, "Anchor", "Bottom, Left");
        RuntimeForms.On(apply, "Click", delegate
        {
            try
            {
                WorkOffsetPrograms.Validate(offsets.ToArray());
                Offsets = offsets.ToArray();
                ProcessingEnabled = true; Finish("OK");
            }
            catch (Exception ex) { ShowProblem(ex); }
        });
        RuntimeForms.Add(Footer, apply);
        object disable = Button("\u041E\u0442\u043A\u043B\u044E\u0447\u0438\u0442\u044C", 160, 38); Position(disable, 232, 48, 160, 38);
        RuntimeForms.SetEnum(disable, "Anchor", "Bottom, Left");
        RuntimeForms.On(disable, "Click", delegate
        {
            Offsets = offsets.Count > 0 ? offsets.ToArray() : new int[] { 1, 2 };
            ProcessingEnabled = false; Finish("OK");
        });
        RuntimeForms.Add(Footer, disable);
        RuntimeForms.On(list, "SelectedIndexChanged", delegate { RefreshButtons(); });
        RefreshList(0);
    }
    private object OrderButton(object parent, string text, int top, EventHandler action)
    {
        object button = Button(text, 128, 36); Position(button, 12, top, 128, 36);
        RuntimeForms.On(button, "Click", action); RuntimeForms.Add(parent, button); return button;
    }
    private void RefreshCode() { RuntimeForms.Set(code, "Text", "G" + (53 + Decimal.ToInt32((decimal)RuntimeForms.Get(number, "Value")))); }
    private void RefreshList(int selected)
    {
        RuntimeForms.Call(list, "BeginUpdate");
        try
        {
            object items = RuntimeForms.Get(list, "Items"); RuntimeForms.Call(items, "Clear");
            for (int i = 0; i < offsets.Count; i++) RuntimeForms.Call(items, "Add", (i + 1) + ".  \u041F\u0440\u0438\u0432\u044F\u0437\u043A\u0430 " + offsets[i] + " \u2014 G" + (53 + offsets[i]));
            RuntimeForms.Set(list, "SelectedIndex", offsets.Count == 0 ? -1 : Math.Max(0, Math.Min(selected, offsets.Count - 1)));
        }
        finally { RuntimeForms.Call(list, "EndUpdate"); }
        RefreshButtons(); RefreshSummary();
    }
    private void RefreshButtons()
    {
        int index = (int)RuntimeForms.Get(list, "SelectedIndex");
        RuntimeForms.Set(up, "Enabled", index > 0); RuntimeForms.Set(first, "Enabled", index > 0);
        RuntimeForms.Set(down, "Enabled", index >= 0 && index + 1 < offsets.Count);
        RuntimeForms.Set(last, "Enabled", index >= 0 && index + 1 < offsets.Count); RuntimeForms.Set(remove, "Enabled", index >= 0);
    }
    private void Move(int value, bool absolute)
    {
        int from = (int)RuntimeForms.Get(list, "SelectedIndex"), to = absolute ? value : from + value;
        ProgramCallChain.Move(offsets, from, to); RefreshList(to);
    }
    private void RefreshSummary()
    {
        string text = offsets.Count == 0 ? "\u0414\u043E\u0431\u0430\u0432\u044C\u0442\u0435 \u0445\u043E\u0442\u044F \u0431\u044B \u043E\u0434\u043D\u0443 \u043F\u0440\u0438\u0432\u044F\u0437\u043A\u0443." : WorkOffsetPrograms.Describe(offsets.ToArray());
        RuntimeForms.Set(summary, "Text", text); RuntimeForms.Call(Tips, "SetToolTip", summary, text);
    }
}


internal sealed class MachineRootsDialog : RouterDialog
{
    private readonly PostIniDocument document;
    private readonly Func<string, string> environment;
    private readonly object list = RuntimeForms.New("ListBox");
    private readonly object nameInput = RuntimeForms.New("TextBox");
    private readonly object pathInput = RuntimeForms.New("TextBox");
    private readonly object details = RuntimeForms.New("Panel");
    private readonly object remove;
    private int active = -1;
    private bool updating;
    internal List<MachineTarget> Machines { get { return document.EditedMachines; } }
    internal List<string> Warnings { get { return document.MachineWarnings; } }

    internal MachineRootsDialog(string configPath, Func<string, string> environment) : base("\u041F\u0430\u043F\u043A\u0438 \u0441\u043E \u0441\u0442\u0430\u043D\u043A\u0430\u043C\u0438", 72)
    {
        this.environment = environment; document = new PostIniDocument(configPath);
        object hint = Label("\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043E\u0431\u0449\u0443\u044E \u043F\u0430\u043F\u043A\u0443: \u0435\u0451 \u043F\u043E\u0434\u043F\u0430\u043F\u043A\u0438 \u0441 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u044F\u043C\u0438 \u0438\u0437 \u0446\u0438\u0444\u0440 \u043F\u043E\u044F\u0432\u044F\u0442\u0441\u044F \u043A\u0430\u043A \u0441\u0442\u0430\u043D\u043A\u0438.\n\u0418\u0437\u043C\u0435\u043D\u0435\u043D\u0438\u044F \u043F\u0440\u0438\u043C\u0435\u043D\u044F\u044E\u0442\u0441\u044F \u043F\u043E\u0441\u043B\u0435 \u043D\u0430\u0436\u0430\u0442\u0438\u044F \u00AB\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C\u00BB.", 0, 0, 760, 62);
        RuntimeForms.SetEnum(hint, "Anchor", "Top, Left, Right");
        RuntimeForms.Call(Tips, "SetToolTip", hint, document.Path); RuntimeForms.Add(Header, hint);
        RuntimeForms.Call(RuntimeForms.Get(Window, "Controls"), "Remove", Grid); RuntimeForms.Dispose(Grid);
        object body = RuntimeForms.New("Panel"); RuntimeForms.SetEnum(body, "Dock", "Fill");
        RuntimeForms.Add(Window, body); RuntimeForms.Call(RuntimeForms.Get(Window, "Controls"), "SetChildIndex", body, 0);
        object left = RuntimeForms.New("Panel"); RuntimeForms.Set(left, "Width", 244); RuntimeForms.SetEnum(left, "Dock", "Left");
        object actions = RuntimeForms.New("Panel"); RuntimeForms.Set(actions, "Height", 46); RuntimeForms.SetEnum(actions, "Dock", "Bottom");
        RuntimeForms.SetEnum(list, "Dock", "Fill"); RuntimeForms.Set(list, "IntegralHeight", false); RuntimeForms.Set(list, "HorizontalScrollbar", true);
        object add = Button("\u0414\u043E\u0431\u0430\u0432\u0438\u0442\u044C\u2026", 118, 36); Position(add, 0, 8, 118, 36);
        remove = Button("\u0423\u0431\u0440\u0430\u0442\u044C", 114, 36); Position(remove, 126, 8, 114, 36);
        RuntimeForms.Call(Tips, "SetToolTip", remove, "\u0423\u0431\u0440\u0430\u0442\u044C \u043F\u0443\u0442\u044C \u0438\u0437 \u0441\u043F\u0438\u0441\u043A\u0430. \u041F\u0430\u043F\u043A\u0438 \u0441\u0442\u0430\u043D\u043A\u043E\u0432 \u0438 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u044B \u043D\u0435 \u0443\u0434\u0430\u043B\u044F\u044E\u0442\u0441\u044F.");
        RuntimeForms.Add(actions, add); RuntimeForms.Add(actions, remove); RuntimeForms.Add(left, list); RuntimeForms.Add(left, actions);
        RuntimeForms.SetEnum(details, "Dock", "Fill"); RuntimeForms.Set(details, "Width", 536);
        RuntimeForms.Add(body, details); RuntimeForms.Add(body, left);
        RuntimeForms.Add(details, Label("\u041D\u0430\u0437\u0432\u0430\u043D\u0438\u0435", 16, 8, 480, 22));
        Position(nameInput, 16, 32, 506, 28); RuntimeForms.SetEnum(nameInput, "Anchor", "Top, Left, Right"); RuntimeForms.Add(details, nameInput);
        RuntimeForms.Add(details, Label("\u041E\u0431\u0449\u0430\u044F \u043F\u0430\u043F\u043A\u0430 \u0441\u043E \u0441\u0442\u0430\u043D\u043A\u0430\u043C\u0438", 16, 80, 480, 22));
        Position(pathInput, 16, 104, 392, 28); RuntimeForms.SetEnum(pathInput, "Anchor", "Top, Left, Right"); RuntimeForms.Add(details, pathInput);
        object browse = Button("\u041E\u0431\u0437\u043E\u0440\u2026", 104, 30); Position(browse, 418, 102, 104, 30); RuntimeForms.SetEnum(browse, "Anchor", "Top, Right"); RuntimeForms.Add(details, browse);
        object explanation = Label("\u041C\u043E\u0436\u043D\u043E \u0432\u044B\u0431\u0440\u0430\u0442\u044C \u043B\u043E\u043A\u0430\u043B\u044C\u043D\u0443\u044E \u0438\u043B\u0438 \u0441\u0435\u0442\u0435\u0432\u0443\u044E \u043F\u0430\u043F\u043A\u0443 \u043B\u0438\u0431\u043E \u0432\u0441\u0442\u0430\u0432\u0438\u0442\u044C \u0435\u0451 \u043F\u0443\u0442\u044C.\n\u041D\u0430\u043F\u0440\u0438\u043C\u0435\u0440, \u043F\u043E\u0434\u043F\u0430\u043F\u043A\u0438 01, 02 \u0438 15 \u043F\u043E\u044F\u0432\u044F\u0442\u0441\u044F \u043E\u0442\u0434\u0435\u043B\u044C\u043D\u044B\u043C\u0438 \u043A\u043D\u043E\u043F\u043A\u0430\u043C\u0438.\n\u0412\u043B\u043E\u0436\u0435\u043D\u043D\u044B\u0435 \u0443\u0440\u043E\u0432\u043D\u0438 \u043D\u0435 \u043F\u0440\u043E\u0441\u043C\u0430\u0442\u0440\u0438\u0432\u0430\u044E\u0442\u0441\u044F.", 16, 156, 506, 110);
        RuntimeForms.SetEnum(explanation, "Anchor", "Top, Left, Right"); RuntimeForms.Add(details, explanation);
        object save = Button("\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C", 124, 38); Position(save, 536, 6, 124, 38);
        RuntimeForms.SetEnum(save, "Anchor", "Bottom, Right"); RuntimeForms.Add(Footer, save); RuntimeForms.Set(Window, "AcceptButton", save);
        RuntimeForms.On(list, "SelectedIndexChanged", delegate { SelectEntry(); });
        RuntimeForms.On(nameInput, "TextChanged", delegate { EditEntry(); }); RuntimeForms.On(pathInput, "TextChanged", delegate { EditEntry(); });
        RuntimeForms.On(add, "Click", delegate { AddRoot(); }); RuntimeForms.On(remove, "Click", delegate { RemoveRoot(); });
        RuntimeForms.On(browse, "Click", delegate { Browse(); }); RuntimeForms.On(save, "Click", delegate { Save(); });
        RefreshList(document.MachineRoots.Count > 0 ? 0 : -1);
    }
    private void RefreshList(int selected)
    {
        updating = true;
        try
        {
            object items = RuntimeForms.Get(list, "Items"); RuntimeForms.Call(list, "BeginUpdate");
            try { RuntimeForms.Call(items, "Clear"); foreach (MachineRootEntry root in document.MachineRoots) RuntimeForms.Call(items, "Add", root.Name.Length == 0 ? "\u0411\u0435\u0437 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u044F" : root.Name); }
            finally { RuntimeForms.Call(list, "EndUpdate"); }
            RuntimeForms.Set(list, "SelectedIndex", selected);
        }
        finally { updating = false; }
        SelectEntry();
    }
    private void SelectEntry()
    {
        if (updating) return;
        active = (int)RuntimeForms.Get(list, "SelectedIndex"); updating = true;
        try
        {
            bool selected = active >= 0 && active < document.MachineRoots.Count;
            MachineRootEntry root = selected ? document.MachineRoots[active] : null;
            RuntimeForms.Set(details, "Enabled", selected); RuntimeForms.Set(remove, "Enabled", selected);
            RuntimeForms.Set(nameInput, "Text", selected ? root.Name : ""); RuntimeForms.Set(pathInput, "Text", selected ? root.DirectoryPath : "");
            RuntimeForms.Call(Tips, "SetToolTip", pathInput, selected ? root.DirectoryPath : "");
        }
        finally { updating = false; }
    }
    private void EditEntry()
    {
        if (updating || active < 0) return;
        MachineRootEntry root = document.MachineRoots[active]; string name = (string)RuntimeForms.Get(nameInput, "Text"); bool renamed = name != root.Name;
        root.Name = name; root.DirectoryPath = (string)RuntimeForms.Get(pathInput, "Text");
        if (renamed)
        {
            updating = true;
            try { RuntimeForms.Get(list, "Items").GetType().GetProperty("Item").SetValue(RuntimeForms.Get(list, "Items"), name.Length == 0 ? "\u0411\u0435\u0437 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u044F" : name, new object[] { active }); }
            finally { updating = false; }
        }
        RuntimeForms.Call(Tips, "SetToolTip", pathInput, root.DirectoryPath);
    }
    private string PickFolder(string current)
    {
        object dialog = RuntimeForms.New("FolderBrowserDialog");
        try
        {
            RuntimeForms.Set(dialog, "Description", ScriptInfo.WindowTitle("\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043E\u0431\u0449\u0443\u044E \u043F\u0430\u043F\u043A\u0443 \u0441\u043E \u0441\u0442\u0430\u043D\u043A\u0430\u043C\u0438"));
            PropertyInfo useTitle = dialog.GetType().GetProperty("UseDescriptionForTitle");
            if (useTitle != null && useTitle.CanWrite) useTitle.SetValue(dialog, true, null);
            RuntimeForms.Set(dialog, "ShowNewFolderButton", false);
            if (!String.IsNullOrWhiteSpace(current))
            {
                try { RuntimeForms.Set(dialog, "SelectedPath", RouterConfig.ResolvePath(current, IOPath.GetDirectoryName(document.Path), environment)); }
                catch { /* An obsolete path must not block choosing its replacement. */ }
            }
            if (RuntimeForms.Show(dialog, Window) != "OK") return null;
            string path = (string)RuntimeForms.Get(dialog, "SelectedPath");
            if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) throw new DirectoryNotFoundException("\u041F\u0430\u043F\u043A\u0430 \u043D\u0435\u0434\u043E\u0441\u0442\u0443\u043F\u043D\u0430:\n" + path);
            return IOPath.GetFullPath(path);
        }
        finally { RuntimeForms.Dispose(dialog); }
    }
    private void AddRoot()
    {
        try
        {
            string path = PickFolder(active >= 0 ? document.MachineRoots[active].DirectoryPath : ""); if (path == null) return;
            string name = IOPath.GetFileName(path.TrimEnd(new char[] { '\\', '/' }));
            document.MachineRoots.Add(new MachineRootEntry { Name = document.UniqueMachineRootName(name), DirectoryPath = path });
            RefreshList(document.MachineRoots.Count - 1); RuntimeForms.Call(nameInput, "Focus");
        }
        catch (Exception ex) { ShowProblem(ex); }
    }
    private void Browse()
    {
        try
        {
            if (active < 0) return;
            string path = PickFolder(document.MachineRoots[active].DirectoryPath); if (path == null) return;
            document.MachineRoots[active].DirectoryPath = path; RefreshList(active);
        }
        catch (Exception ex) { ShowProblem(ex); }
    }
    private void RemoveRoot()
    { if (active >= 0) { int previous = active; document.MachineRoots.RemoveAt(active); RefreshList(Math.Min(previous, document.MachineRoots.Count - 1)); } }
    private void Save()
    {
        try { document.SaveMachineRoots(environment); Finish("OK"); }
        catch (Exception ex) { ShowProblem(ex); }
    }
}

internal sealed class MachinePicker : RouterDialog
{
    private readonly string[] outputExtensions;
    private readonly object content = RuntimeForms.New("Panel");
    private readonly object binEnabled = RuntimeForms.New("CheckBox");
    private readonly object binDetails = RuntimeForms.New("Panel");
    private bool layoutReady, shown, arranging;
    private readonly object binMode = RuntimeForms.New("ComboBox");
    private readonly object binSize = RuntimeForms.New("ComboBox");
    private readonly object customSize = RuntimeForms.New("NumericUpDown");
    private readonly object sizeHint = RuntimeForms.New("Label");
    private readonly object outputOptions = RuntimeForms.New("GroupBox");
    private readonly object saveProject = RuntimeForms.New("CheckBox");
    private readonly object assignNames = RuntimeForms.New("CheckBox");
    private readonly object namesPanel = RuntimeForms.New("Panel");
    private readonly object machineHint = RuntimeForms.New("Label");
    private readonly object manualFolder = RuntimeForms.New("TextBox");
    private readonly object useFolder = RuntimeForms.New("Button");
    private readonly object outputButton = RuntimeForms.New("Button");
    private readonly object callSummary = RuntimeForms.New("Label");
    private readonly object offsetSummary = RuntimeForms.New("Label");
    private int[] workOffsets = new int[] { 1, 2 };
    private bool offsetsEnabled; // Always off on each new run; no new preference/INI keys.
    private int[] callOrder;
    private bool callsEnabled;
    private readonly object destinationLabel;
    private MachineTarget selectedTarget;
    private ExternalDriveSelection selectedMedia;
    private readonly List<object> nameInputs = new List<object>();
    private readonly List<ProgramJob> jobs;
    private readonly string projectFile;
    private readonly string configPath;
    private readonly Func<string, string> environment;
    private readonly RouterPreferences preferences;
    public MachineChoice Choice;

    public MachinePicker(List<ProgramJob> jobs, PostDefinition[] posts, List<MachineTarget> machines,
        List<string> machineWarnings, RouterConfig config, string projectFile, string projectDirectory, RouterPreferences preferences, Func<string, string> environment = null)
        : base("\u041A\u0443\u0434\u0430 \u0432\u044B\u0432\u0435\u0441\u0442\u0438 \u0423\u041F?", 256)
    {
        this.jobs = jobs; this.projectFile = projectFile; this.preferences = preferences;
        configPath = config.FilePath; this.environment = environment ?? Environment.GetEnvironmentVariable;
        outputExtensions = ProgramPostAssignments.Extensions(posts, jobs.Count, config.DefaultExtension);
        callOrder = ProgramCallChain.Identity(jobs.Count);
        destinationLabel = machineHint;
        RuntimeForms.SetValue(Window, "ClientSize", 820, 590);
        RuntimeForms.SetValue(Window, "MinimumSize", 0, 0);
        List<string> names = new List<string>(); foreach (ProgramJob job in jobs) names.Add(job.OutputName ?? job.Group.Name);
        string selection = String.Join(", ", names.ToArray());
        string shortSelection = selection.Length > 75 ? selection.Substring(0, 72) + "..." : selection;
        object summary = Label("\u041F\u0440\u043E\u0433\u0440\u0430\u043C\u043C: " + jobs.Count + " \u2014 " + shortSelection + "\u000A" + ProgramPostAssignments.Summary(posts), 0, 0, 760, 42);
        RuntimeForms.Set(summary, "AutoEllipsis", true);
        RuntimeForms.Call(Tips, "SetToolTip", summary, ProgramPostAssignments.Describe(names.ToArray(), posts, outputExtensions));
        RuntimeForms.Add(Header, summary);
        object configureMachines = Button("\u041F\u0430\u043F\u043A\u0438 \u0441\u043E \u0441\u0442\u0430\u043D\u043A\u0430\u043C\u0438\u2026", 294, 34); Position(configureMachines, 490, 44, 294, 34);
        RuntimeForms.SetEnum(configureMachines, "Anchor", "Top, Right"); RuntimeForms.Add(Header, configureMachines);
        RuntimeForms.Call(Tips, "SetToolTip", configureMachines, "\u0412\u044B\u0431\u0440\u0430\u0442\u044C \u043E\u0431\u0449\u0443\u044E \u043F\u0430\u043F\u043A\u0443, \u0432\u043D\u0443\u0442\u0440\u0438 \u043A\u043E\u0442\u043E\u0440\u043E\u0439 \u043D\u0430\u0445\u043E\u0434\u044F\u0442\u0441\u044F \u043F\u0430\u043F\u043A\u0438 \u0441\u0442\u0430\u043D\u043A\u043E\u0432.");
        RuntimeForms.On(configureMachines, "Click", delegate { ConfigureMachineRoots(); });
        RuntimeForms.Set(binEnabled, "Text", "FANUCPRG.BIN");
        Position(binEnabled, 0, 48, 260, 28);
        RuntimeForms.Set(binEnabled, "Checked", false); // Always off on each new run.
        RuntimeForms.Call(Tips, "SetToolTip", binEnabled, "\u0412\u043A\u043B\u044E\u0447\u0438\u0442\u0435 \u0434\u043B\u044F \u0441\u043E\u0437\u0434\u0430\u043D\u0438\u044F \u0438\u043B\u0438 \u043E\u0431\u043D\u043E\u0432\u043B\u0435\u043D\u0438\u044F BIN \u0432\u043C\u0435\u0441\u0442\u0435 \u0441 \u043E\u0442\u0434\u0435\u043B\u044C\u043D\u044B\u043C\u0438 \u0423\u041F.");
        RuntimeForms.Add(Header, binEnabled);
        Position(binDetails, 0, 82, 784, 116);
        RuntimeForms.SetEnum(binDetails, "Anchor", "Top, Left, Right");
        RuntimeForms.Set(binDetails, "Visible", false);
        RuntimeForms.Add(Header, binDetails);

        RuntimeForms.Set(outputOptions, "Text", "\u041F\u0430\u0440\u0430\u043C\u0435\u0442\u0440\u044B \u0432\u044B\u0432\u043E\u0434\u0430");
        Position(outputOptions, 0, 82, 784, 182);
        RuntimeForms.SetEnum(outputOptions, "Anchor", "Top, Left, Right");
        RuntimeForms.Set(outputOptions, "TabIndex", 20);
        RuntimeForms.Add(Header, outputOptions);
        RuntimeForms.Set(saveProject, "Text", "\u0421\u043E\u0445\u0440\u0430\u043D\u044F\u0442\u044C \u0442\u0430\u043A\u0436\u0435 \u0432 \u043F\u0430\u043F\u043A\u0443 \u043F\u0440\u043E\u0435\u043A\u0442\u0430");
        Position(saveProject, 12, 25, 330, 28);
        RuntimeForms.Set(saveProject, "TabIndex", 0);
        RuntimeForms.Set(saveProject, "Checked", preferences.SaveToProject);
        RuntimeForms.Add(outputOptions, saveProject);
        RuntimeForms.Call(Tips, "SetToolTip", saveProject, "\u0417\u0430\u043F\u043E\u043C\u0438\u043D\u0430\u0435\u0442\u0441\u044F \u043D\u0430 \u044D\u0442\u043E\u043C \u041F\u041A \u0434\u043B\u044F \u0442\u0435\u043A\u0443\u0449\u0435\u0433\u043E \u043F\u043E\u043B\u044C\u0437\u043E\u0432\u0430\u0442\u0435\u043B\u044F Windows. \u041E\u0442\u043D\u043E\u0441\u0438\u0442\u0441\u044F \u0438 \u043A \u0423\u041F, \u0438 \u043A BIN.");
        object projectLabel = Label(projectDirectory.Length > 0 ? projectDirectory : "\u041F\u0440\u043E\u0435\u043A\u0442 \u043D\u0435 \u0441\u043E\u0445\u0440\u0430\u043D\u0451\u043D \u0432 \u0434\u043E\u0441\u0442\u0443\u043F\u043D\u044B\u0439 .prt. \u0414\u043B\u044F \u0432\u044B\u0432\u043E\u0434\u0430 \u0441\u043D\u0438\u043C\u0438\u0442\u0435 \u0433\u0430\u043B\u043E\u0447\u043A\u0443 \u0438\u043B\u0438 \u0441\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u0435 \u043F\u0440\u043E\u0435\u043A\u0442.", 12, 64, 756, 24);
        RuntimeForms.Set(projectLabel, "AutoEllipsis", true);
        RuntimeForms.Call(Tips, "SetToolTip", projectLabel, (string)RuntimeForms.Get(projectLabel, "Text"));
        RuntimeForms.SetEnum(projectLabel, "Anchor", "Top, Left, Right");
        RuntimeForms.Add(outputOptions, projectLabel);
        RuntimeForms.On(saveProject, "CheckedChanged", delegate
        {
            try { preferences.Save((bool)RuntimeForms.Get(saveProject, "Checked")); }
            catch (Exception ex) { ShowProblem(new IOException("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0437\u0430\u043F\u043E\u043C\u043D\u0438\u0442\u044C \u0433\u0430\u043B\u043E\u0447\u043A\u0443. \u0422\u0435\u043A\u0443\u0449\u0438\u0439 \u0432\u044B\u0431\u043E\u0440 \u0434\u0435\u0439\u0441\u0442\u0432\u0443\u0435\u0442 \u0442\u043E\u043B\u044C\u043A\u043E \u0432 \u044D\u0442\u043E\u043C \u043E\u043A\u043D\u0435.\u000A" + ex.Message, ex)); }
        });

        RuntimeForms.Add(binDetails, Label("\u0420\u0435\u0436\u0438\u043C BIN:", 0, 3, 132, 24));
        Position(binMode, 140, 0, 600, 30);
        RuntimeForms.SetEnum(binMode, "DropDownStyle", "DropDownList");
        object binItems = RuntimeForms.Get(binMode, "Items");
        RuntimeForms.Call(binItems, "Add", "\u0414\u043E\u0431\u0430\u0432\u0438\u0442\u044C / \u043E\u0431\u043D\u043E\u0432\u0438\u0442\u044C \u0423\u041F \u0432 BIN; \u043E\u0441\u0442\u0430\u043B\u044C\u043D\u044B\u0435 \u0441\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C");
        RuntimeForms.Call(binItems, "Add", "\u041D\u043E\u0432\u044B\u0439 BIN \u2014 \u0442\u043E\u043B\u044C\u043A\u043E \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0435 \u0423\u041F");
        RuntimeForms.Set(binMode, "SelectedIndex", config.BinMode == FanucBinMode.New ? 1 : 0); RuntimeForms.Add(binDetails, binMode);
        RuntimeForms.Add(binDetails, Label("\u0420\u0430\u0437\u043C\u0435\u0440 \u0423\u041F \u0432 BIN:", 0, 45, 155, 24));
        Position(binSize, 160, 40, 170, 30);
        RuntimeForms.SetEnum(binSize, "DropDownStyle", "DropDownList");
        object sizeItems = RuntimeForms.Get(binSize, "Items");
        foreach (string size in new string[] { "2 \u041C\u0411", "4 \u041C\u0411", "8 \u041C\u0411", "\u0421\u0432\u043E\u0439 \u0440\u0430\u0437\u043C\u0435\u0440\u2026" }) RuntimeForms.Call(sizeItems, "Add", size);
        RuntimeForms.Set(binSize, "SelectedIndex", config.BinSizeMB == 2 ? 0 : config.BinSizeMB == 4 ? 1 : config.BinSizeMB == 8 ? 2 : 3);
        RuntimeForms.Add(binDetails, binSize);
        Position(customSize, 346, 40, 96, 30);
        RuntimeForms.Set(customSize, "Minimum", 1M); RuntimeForms.Set(customSize, "Maximum", 2048M);
        RuntimeForms.Set(customSize, "Value", (decimal)config.BinSizeMB);
        RuntimeForms.Set(customSize, "DecimalPlaces", 0); RuntimeForms.Add(binDetails, customSize);
        RuntimeForms.Add(binDetails, Label("\u041C\u0411 (\u0441\u0432\u043E\u0439 \u0440\u0430\u0437\u043C\u0435\u0440)", 451, 45, 230, 24));
        Position(sizeHint, 0, 78, 760, 38); RuntimeForms.Add(binDetails, sizeHint);
        RuntimeForms.On(binMode, "SelectedIndexChanged", delegate { RefreshSize(); });
        RuntimeForms.On(binSize, "SelectedIndexChanged", delegate { RefreshSize(); });
        RuntimeForms.On(customSize, "ValueChanged", delegate { RefreshSize(); });
        RuntimeForms.On(binEnabled, "CheckedChanged", delegate { RefreshSize(); RefreshSections(); });
        RefreshSize();

        RuntimeForms.Set(assignNames, "Text", "\u041D\u0430\u0437\u043D\u0430\u0447\u0438\u0442\u044C \u0438\u043C\u044F \u0423\u041F"); Position(assignNames, 352, 25, 184, 28);
        RuntimeForms.Set(assignNames, "Checked", false); // Always off on a new run; never persisted.
        RuntimeForms.Set(assignNames, "TabIndex", 1); RuntimeForms.Add(outputOptions, assignNames);
        RuntimeForms.Call(Tips, "SetToolTip", assignNames, "\u041F\u043E \u043E\u0434\u043D\u043E\u0439 \u0441\u0442\u0440\u043E\u043A\u0435 \u043D\u0430 \u0423\u041F, \u0432 \u043F\u043E\u0440\u044F\u0434\u043A\u0435 \u0434\u0435\u0440\u0435\u0432\u0430. \u0418\u0437\u043C\u0435\u043D\u044F\u0442\u0441\u044F \u0438\u043C\u044F \u0444\u0430\u0439\u043B\u0430 \u0438 O-\u043D\u043E\u043C\u0435\u0440; \u043F\u0430\u043F\u043A\u0438 NX \u043E\u0441\u0442\u0430\u043D\u0443\u0442\u0441\u044F \u043F\u0440\u0435\u0436\u043D\u0438\u043C\u0438.");
        Position(namesPanel, 12, 182, 756, 132); RuntimeForms.Set(namesPanel, "AutoScroll", true);
        RuntimeForms.SetEnum(namesPanel, "Anchor", "Top, Left, Right"); RuntimeForms.Set(namesPanel, "TabIndex", 3); RuntimeForms.Add(outputOptions, namesPanel);
        RuntimeForms.Add(namesPanel, Label("\u0418\u0441\u0445\u043E\u0434\u043D\u0430\u044F \u043F\u0430\u043F\u043A\u0430 (\u043F\u043E\u0440\u044F\u0434\u043E\u043A \u0434\u0435\u0440\u0435\u0432\u0430)", 0, 0, 510, 22));
        RuntimeForms.Add(namesPanel, Label("\u041D\u043E\u043C\u0435\u0440 \u0423\u041F", 525, 0, 180, 22));
        for (int i = 0; i < jobs.Count; i++)
        {
            ProgramJob job = jobs[i];
            object label = Label((i + 1) + ". " + job.GroupPath, 0, 30 + i * 34, 510, 28);
            RuntimeForms.Set(label, "AutoEllipsis", true);
            RuntimeForms.Call(Tips, "SetToolTip", label, job.GroupPath + "\n" + job.SelectionDescription());
            RuntimeForms.Add(namesPanel, label);
            RuntimeForms.Add(namesPanel, Label("O", 525, 30 + i * 34, 20, 24));
            object input = RuntimeForms.New("TextBox"); Position(input, 547, 27 + i * 34, 153, 28);
            RuntimeForms.Set(input, "MaxLength", 8);
            string number = ProgramNames.DefaultNumber(job.OutputName ?? job.Group.Name);
            RuntimeForms.Set(input, "Text", number.Length > 0 ? number.Substring(1) : "");
            DigitsOnly(input);
            RuntimeForms.Call(Tips, "SetToolTip", input, "\u0422\u043E\u043B\u044C\u043A\u043E \u0446\u0438\u0444\u0440\u044B: \u043D\u0430\u043F\u0440\u0438\u043C\u0435\u0440 1659. \u0411\u0443\u043A\u0432\u0430 O \u0434\u043E\u0431\u0430\u0432\u043B\u044F\u0435\u0442\u0441\u044F \u0430\u0432\u0442\u043E\u043C\u0430\u0442\u0438\u0447\u0435\u0441\u043A\u0438. \u0414\u043E\u043F\u0443\u0441\u0442\u0438\u043C\u044B 1\u20138 \u0446\u0438\u0444\u0440; \u043D\u043E\u043C\u0435\u0440 0 \u0437\u0430\u043F\u0440\u0435\u0449\u0451\u043D.");
            nameInputs.Add(input); RuntimeForms.Add(namesPanel, input);
            RuntimeForms.On(input, "TextChanged", delegate { RefreshCallSummary(); });
        }
        RuntimeForms.Add(Header, machineHint);
        RuntimeForms.On(assignNames, "CheckedChanged", delegate { RefreshNames(); RefreshCallSummary(); }); RefreshNames();
        DrawMachines(machines, machineWarnings);
        // Folder selection above; navigation and the explicit Output action below.
        RuntimeForms.Set(Footer, "Height", 104);
        object calls = Button("\u0412\u044B\u0437\u043E\u0432 \u043F\u043E\u0434\u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C", 216, 38); Position(calls, 552, 20, 216, 38);
        RuntimeForms.SetEnum(calls, "Anchor", "Top, Right"); RuntimeForms.Set(calls, "TabIndex", 2);
        RuntimeForms.Set(calls, "Enabled", jobs.Count > 1);
        RuntimeForms.On(calls, "Click", delegate { EditProgramCalls(); }); RuntimeForms.Add(outputOptions, calls);
        Position(callSummary, 12, 94, 756, 28);
        RuntimeForms.Set(callSummary, "AutoEllipsis", true); RuntimeForms.Set(callSummary, "UseMnemonic", false);
        RuntimeForms.SetEnum(callSummary, "Anchor", "Top, Left, Right"); RuntimeForms.Add(outputOptions, callSummary);
        RefreshCallSummary();
        object offsets = Button("\u041E\u0431\u0440\u0430\u0431\u043E\u0442\u043A\u0430 \u043F\u043E \u043F\u0440\u0438\u0432\u044F\u0437\u043A\u0430\u043C", 246, 38); Position(offsets, 12, 132, 246, 38);
        RuntimeForms.Set(offsets, "TabIndex", 3);
        RuntimeForms.On(offsets, "Click", delegate { EditWorkOffsets(); }); RuntimeForms.Add(outputOptions, offsets);
        Position(offsetSummary, 270, 132, 498, 38); RuntimeForms.Set(offsetSummary, "AutoEllipsis", true);
        RuntimeForms.Set(offsetSummary, "UseMnemonic", false); RuntimeForms.SetEnum(offsetSummary, "Anchor", "Top, Left, Right");
        RuntimeForms.Add(outputOptions, offsetSummary); RefreshOffsetSummary();
        object cancel = RuntimeForms.Get(Window, "CancelButton");
        RuntimeForms.SetEnum(cancel, "Dock", "None");
        Position(cancel, 672, 60, 112, 38);
        RuntimeForms.SetEnum(cancel, "Anchor", "Bottom, Right");
        object back = Button("\u2190 \u041A \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430\u043C", 210, 38); Position(back, 0, 60, 210, 38);
        RuntimeForms.SetEnum(back, "Anchor", "Bottom, Left");
        RuntimeForms.On(back, "Click", delegate { Finish("Retry"); }); RuntimeForms.Add(Footer, back);
        object external = Button("\u0412\u044B\u0432\u043E\u0434 \u043D\u0430 \u0432\u043D\u0435\u0448\u043D\u0438\u0439 \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u044C", 300, 38); Position(external, 222, 60, 300, 38);
        RuntimeForms.SetEnum(external, "Anchor", "Bottom, Left");
        RuntimeForms.On(external, "Click", delegate
        {
            try
            {
                using (ExternalDrivePicker picker = new ExternalDrivePicker())
                {
                    if (picker.ShowDialog(Window) != "OK") return;
                    ExternalDriveSelection selected = picker.Selection;
                    SelectTarget(new MachineTarget(selected.Caption, selected.Root), selected);
                }
            }
            catch (Exception ex) { ShowProblem(ex); }
        });
        RuntimeForms.Add(Footer, external);
        RuntimeForms.Set(destinationLabel, "UseMnemonic", false); RuntimeForms.Set(destinationLabel, "AutoEllipsis", true);
        RuntimeForms.Set(outputButton, "Text", "\u0412\u044B\u0432\u0435\u0441\u0442\u0438"); RuntimeForms.Set(outputButton, "UseVisualStyleBackColor", true);
        Position(outputButton, 534, 60, 126, 38); RuntimeForms.SetEnum(outputButton, "Anchor", "Bottom, Right");
        RuntimeForms.Set(outputButton, "Enabled", false);
        RuntimeForms.On(outputButton, "Click", delegate
        {
            if (selectedTarget != null) TryAccept(selectedTarget, selectedMedia);
        });
        RuntimeForms.Add(Footer, outputButton);
        object browse = Button("\u0412\u044B\u0431\u0440\u0430\u0442\u044C \u043F\u0430\u043F\u043A\u0443\u2026", 180, 38); Position(browse, 0, 8, 180, 38);
        Position(manualFolder, 192, 13, 404, 28);
        RuntimeForms.Set(manualFolder, "ReadOnly", true);
        RuntimeForms.SetEnum(manualFolder, "Anchor", "Top, Left, Right");
        RuntimeForms.Add(Footer, manualFolder);
        RuntimeForms.Set(useFolder, "Text", "\u0418\u0441\u043F\u043E\u043B\u044C\u0437\u043E\u0432\u0430\u0442\u044C \u043F\u0430\u043F\u043A\u0443");
        RuntimeForms.Set(useFolder, "UseVisualStyleBackColor", true);
        Position(useFolder, 608, 8, 176, 38);
        RuntimeForms.SetEnum(useFolder, "Anchor", "Top, Right");
        RuntimeForms.On(useFolder, "Click", delegate
        {
            string path = (string)RuntimeForms.Get(manualFolder, "Text");
            if (path.Length == 0) { ShowProblem(new InvalidOperationException("\u0421\u043D\u0430\u0447\u0430\u043B\u0430 \u043D\u0430\u0436\u043C\u0438\u0442\u0435 \u00AB\u0412\u044B\u0431\u0440\u0430\u0442\u044C \u043F\u0430\u043F\u043A\u0443\u2026\u00BB.")); return; }
            SelectTarget(new MachineTarget("\u0412\u044B\u0431\u0440\u0430\u043D\u043D\u0430\u044F \u043F\u0430\u043F\u043A\u0430", path), null);
        });
        RuntimeForms.Add(Footer, useFolder);
        ShowManualFolder(preferences.LastFolder);
        RuntimeForms.On(browse, "Click", delegate
        {
            object dialog = null;
            try
            {
                dialog = RuntimeForms.New("FolderBrowserDialog");
                RuntimeForms.Set(dialog, "Description", ScriptInfo.WindowTitle("\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043F\u0430\u043F\u043A\u0443 \u0434\u043B\u044F \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u044F \u0423\u041F \u0438 FANUCPRG.BIN"));
                // Modern Windows folder picker uses Description as its title.
                // Older Forms versions keep the same text inside the dialog.
                PropertyInfo useTitle = dialog.GetType().GetProperty("UseDescriptionForTitle");
                if (useTitle != null && useTitle.CanWrite) useTitle.SetValue(dialog, true, null);
                RuntimeForms.Set(dialog, "ShowNewFolderButton", true);
                string previous = (string)RuntimeForms.Get(manualFolder, "Text");
                if (previous.Length > 0 && Directory.Exists(previous)) RuntimeForms.Set(dialog, "SelectedPath", previous);
                else if (projectDirectory.Length > 0) RuntimeForms.Set(dialog, "SelectedPath", projectDirectory);
                if (RuntimeForms.Show(dialog, Window) == "OK")
                {
                    preferences.SaveLastFolder((string)RuntimeForms.Get(dialog, "SelectedPath"));
                    ShowManualFolder(preferences.LastFolder);
                    SelectTarget(new MachineTarget("\u0412\u044B\u0431\u0440\u0430\u043D\u043D\u0430\u044F \u043F\u0430\u043F\u043A\u0430", preferences.LastFolder), null);
                }
            }
            catch (Exception ex) { ShowProblem(ex); }
            finally { RuntimeForms.Dispose(dialog); }
        });
        RuntimeForms.Add(Footer, browse);
        // One scrollable content panel keeps every setting and machine reachable
        // on small displays; normal displays are fitted without scrolling.
        RuntimeForms.Set(Window, "AutoScroll", true);
        RuntimeForms.Set(Grid, "AutoScroll", false);
        foreach (object section in new object[] { Header, Grid, Footer })
        {
            RuntimeForms.Call(RuntimeForms.Get(Window, "Controls"), "Remove", section);
            RuntimeForms.SetEnum(section, "Dock", "None");
            RuntimeForms.SetEnum(section, "Anchor", "Top, Left");
            RuntimeForms.Add(content, section);
        }
        RuntimeForms.SetEnum(content, "Anchor", "Top, Left");
        RuntimeForms.Set(content, "TabStop", false);
        RuntimeForms.Set(Header, "TabIndex", 0); RuntimeForms.Set(Grid, "TabIndex", 1); RuntimeForms.Set(Footer, "TabIndex", 2);
        RuntimeForms.Add(Window, content);
        layoutReady = true;
        RefreshSections();
        RuntimeForms.On(Window, "Shown", delegate { shown = true; RefreshSections(); });
        RuntimeForms.On(Window, "Resize", delegate
        {
            if (!layoutReady || !shown || arranging) return;
            arranging = true;
            try { LayoutContent(CurrentContentWidth()); }
            finally { arranging = false; }
        });
    }

    private void DrawMachines(List<MachineTarget> machines, List<string> warnings)
    {
        ClearGrid();
        foreach (MachineTarget target in machines)
        {
            MachineTarget machine = target;
            object button = Tile(machine.Name, machine.DirectoryPath, 180, 68);
            RuntimeForms.On(button, "Click", delegate { SelectTarget(machine, null); });
        }
        if (machines.Count == 0) RuntimeForms.Add(Grid, Label("\u041D\u0435\u0442 \u0434\u043E\u0441\u0442\u0443\u043F\u043D\u044B\u0445 \u0441\u0442\u0430\u043D\u043A\u043E\u0432. \u041D\u0430\u0436\u043C\u0438\u0442\u0435 \u00AB\u041F\u0430\u043F\u043A\u0438 \u0441\u043E \u0441\u0442\u0430\u043D\u043A\u0430\u043C\u0438\u2026\u00BB \u0438\u043B\u0438 \u0432\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043C\u0435\u0441\u0442\u043E \u0432\u044B\u0432\u043E\u0434\u0430 \u0432\u043D\u0438\u0437\u0443 \u043E\u043A\u043D\u0430.", 0, 0, 700, 42));
        RuntimeForms.Set(machineHint, "Text", warnings.Count > 0 ? "\u0427\u0430\u0441\u0442\u044C \u043F\u0430\u043F\u043E\u043A \u0441\u0442\u0430\u043D\u043A\u043E\u0432 \u043D\u0435\u0434\u043E\u0441\u0442\u0443\u043F\u043D\u0430. \u041F\u0440\u043E\u0432\u0435\u0440\u044C\u0442\u0435 \u00AB\u041F\u0430\u043F\u043A\u0438 \u0441\u043E \u0441\u0442\u0430\u043D\u043A\u0430\u043C\u0438\u2026\u00BB \u0438\u043B\u0438 \u0432\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043C\u0435\u0441\u0442\u043E \u0432\u044B\u0432\u043E\u0434\u0430 \u0432\u0440\u0443\u0447\u043D\u0443\u044E." : "\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043C\u0435\u0441\u0442\u043E \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u044F \u0438 \u043D\u0430\u0436\u043C\u0438\u0442\u0435 \u00AB\u0412\u044B\u0432\u0435\u0441\u0442\u0438\u00BB.");
        RuntimeForms.Call(Tips, "SetToolTip", machineHint, warnings.Count > 0 ? String.Join("\n\n", warnings.ToArray()) : "");
    }
    private void ConfigureMachineRoots()
    {
        try
        {
            List<MachineTarget> machines; List<string> warnings;
            using (MachineRootsDialog dialog = new MachineRootsDialog(configPath, environment))
            {
                if (dialog.ShowDialog(Window) != "OK") return;
                machines = dialog.Machines; warnings = dialog.Warnings;
            }
            // Settings may replace/remove the selected machine; require a fresh destination choice.
            selectedTarget = null; selectedMedia = null; Choice = null;
            RuntimeForms.Set(outputButton, "Enabled", false);
            DrawMachines(machines, warnings);
            RefreshSections();
        }
        catch (Exception ex) { ShowProblem(ex); }
    }

    private string[] CurrentProgramNames()
    {
        bool assign = (bool)RuntimeForms.Get(assignNames, "Checked");
        List<string> original = new List<string>(), entered = new List<string>();
        foreach (ProgramJob job in jobs) original.Add(job.OutputName ?? job.Group.Name);
        foreach (object input in nameInputs) entered.Add((string)RuntimeForms.Get(input, "Text"));
        string[] names = ProgramNames.Resolve(original.ToArray(), assign ? entered.ToArray() : null, "");
        for (int i = 0; i < names.Length; i++) PostFiles.ValidateLeaf(names[i] + outputExtensions[i]);
        return names;
    }
    private void EditWorkOffsets()
    {
        try
        {
            using (WorkOffsetPicker dialog = new WorkOffsetPicker(workOffsets, offsetsEnabled))
            {
                if (dialog.ShowDialog(Window) != "OK") return;
                workOffsets = dialog.Offsets; offsetsEnabled = dialog.ProcessingEnabled;
            }
            RefreshOffsetSummary();
        }
        catch (Exception ex) { ShowProblem(ex); }
    }
    private void RefreshOffsetSummary()
    {
        string text = WorkOffsetPrograms.Describe(offsetsEnabled ? workOffsets : null);
        RuntimeForms.Set(offsetSummary, "Text", text); RuntimeForms.Call(Tips, "SetToolTip", offsetSummary, text);
    }

    private void EditProgramCalls()
    {
        try
        {
            using (ProgramCallPicker dialog = new ProgramCallPicker(CurrentProgramNames(), callOrder, callsEnabled))
            {
                if (dialog.ShowDialog(Window) != "OK") return;
                callOrder = dialog.Order; callsEnabled = dialog.ChainEnabled;
            }
            RefreshCallSummary();
        }
        catch (Exception ex) { ShowProblem(ex); }
    }
    private void RefreshCallSummary()
    {
        string text = jobs.Count < 2 ? "\u0414\u043B\u044F \u0432\u044B\u0437\u043E\u0432\u043E\u0432 \u0432\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043D\u0435 \u043C\u0435\u043D\u0435\u0435 \u0434\u0432\u0443\u0445 \u0423\u041F." : "\u0412\u044B\u0437\u043E\u0432\u044B M98 \u0432\u044B\u043A\u043B\u044E\u0447\u0435\u043D\u044B";
        if (callsEnabled)
        {
            try
            {
                string[] names = CurrentProgramNames();
                ProgramCallChain.Targets(names, callOrder);
                text = ProgramCallChain.Describe(names, callOrder);
            }
            catch { text = "\u0426\u0435\u043F\u043E\u0447\u043A\u0430 \u043D\u0430\u0441\u0442\u0440\u043E\u0435\u043D\u0430 \u2014 \u043F\u0440\u043E\u0432\u0435\u0440\u044C\u0442\u0435 \u043D\u043E\u043C\u0435\u0440\u0430 \u0423\u041F."; }
        }
        RuntimeForms.Set(callSummary, "Text", text); RuntimeForms.Call(Tips, "SetToolTip", callSummary, text);
    }

    private void SelectTarget(MachineTarget target, ExternalDriveSelection media)
    {
        try
        {
            if (media != null) media.EnsurePresent();
            if (!Directory.Exists(target.DirectoryPath))
                throw new DirectoryNotFoundException("\u041F\u0430\u043F\u043A\u0430 \u0432\u044B\u0432\u043E\u0434\u0430 \u043D\u0435\u0434\u043E\u0441\u0442\u0443\u043F\u043D\u0430:\n" + target.DirectoryPath);
            selectedTarget = target; selectedMedia = media;
            RuntimeForms.Set(destinationLabel, "Text", "\u0412\u044B\u0431\u0440\u0430\u043D\u043E: " + target.Name + " \u2014 " + target.DirectoryPath);
            RuntimeForms.Call(Tips, "SetToolTip", destinationLabel, target.DirectoryPath);
            RuntimeForms.Set(outputButton, "Enabled", true);
        }
        catch (Exception ex) { ShowProblem(ex); }
    }

    private void ShowManualFolder(string path)
    {
        RuntimeForms.Set(manualFolder, "Text", path);
        RuntimeForms.Set(useFolder, "Enabled", path.Length > 0);
        RuntimeForms.Call(Tips, "SetToolTip", manualFolder, path.Length == 0 ? "\u041F\u0430\u043F\u043A\u0430 \u0435\u0449\u0451 \u043D\u0435 \u0432\u044B\u0431\u0440\u0430\u043D\u0430. \u041D\u0430\u0436\u043C\u0438\u0442\u0435 \u00AB\u0412\u044B\u0431\u0440\u0430\u0442\u044C \u043F\u0430\u043F\u043A\u0443\u2026\u00BB." : path);
    }

    private static void DigitsOnly(object input)
    {
        string previous = (string)RuntimeForms.Get(input, "Text");
        bool restoring = false;
        RuntimeForms.On(input, "TextChanged", delegate
        {
            if (restoring) return;
            string text = (string)RuntimeForms.Get(input, "Text");
            if (Regex.IsMatch(text, @"\A[0-9]{0,8}\z")) { previous = text; return; }
            // Reject an invalid edit/paste as a whole: do not silently turn
            // e.g. 12.3 into a different program number 123.
            restoring = true;
            try
            {
                RuntimeForms.Set(input, "Text", previous);
                RuntimeForms.Set(input, "SelectionStart", previous.Length);
            }
            finally { restoring = false; }
        });
    }

    private int SelectedSize()
    {
        int index = (int)RuntimeForms.Get(binSize, "SelectedIndex");
        return index == 0 ? 2 : index == 1 ? 4 : index == 2 ? 8 : Decimal.ToInt32((decimal)RuntimeForms.Get(customSize, "Value"));
    }
    private void RefreshSize()
    {
        bool enabled = (bool)RuntimeForms.Get(binEnabled, "Checked");
        RuntimeForms.Set(binSize, "Enabled", enabled);
        RuntimeForms.Set(customSize, "Enabled", enabled && (int)RuntimeForms.Get(binSize, "SelectedIndex") == 3);
        RuntimeForms.Set(sizeHint, "Text", enabled ? "\u0424\u0430\u0439\u043B BIN: " + FanucBin.FileLengthForMB(SelectedSize()).ToString("N0", CultureInfo.CurrentCulture) +
            " \u0431\u0430\u0439\u0442. \u0420\u0430\u0437\u043C\u0435\u0440 \u043F\u0440\u0438\u043C\u0435\u043D\u044F\u0435\u0442\u0441\u044F \u0438 \u043F\u0440\u0438 \u043E\u0431\u043D\u043E\u0432\u043B\u0435\u043D\u0438\u0438.\u000A\u041F\u0440\u0438 \u043F\u0435\u0440\u0435\u043F\u043E\u043B\u043D\u0435\u043D\u0438\u0438 \u0432\u044B\u0432\u043E\u0434 \u043E\u0441\u0442\u0430\u043D\u043E\u0432\u0438\u0442\u0441\u044F; \u0430\u0432\u0442\u043E\u043C\u0430\u0442\u0438\u0447\u0435\u0441\u043A\u0438 \u0440\u0430\u0437\u043C\u0435\u0440 \u043D\u0435 \u0443\u0432\u0435\u043B\u0438\u0447\u0438\u0432\u0430\u0435\u0442\u0441\u044F." : "BIN \u0432\u044B\u043A\u043B\u044E\u0447\u0435\u043D. \u0411\u0443\u0434\u0443\u0442 \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u044B \u0442\u043E\u043B\u044C\u043A\u043E \u043E\u0442\u0434\u0435\u043B\u044C\u043D\u044B\u0435 \u0444\u0430\u0439\u043B\u044B \u0423\u041F.");
    }
    private void RefreshNames()
    {
        bool visible = (bool)RuntimeForms.Get(assignNames, "Checked");
        RuntimeForms.Set(namesPanel, "Visible", visible);
        RefreshSections();
    }

    private int Pixels(int logical)
    {
        if (!shown) return logical;
        object dimensions = RuntimeForms.Get(Window, "AutoScaleDimensions");
        double factor = Convert.ToDouble(RuntimeForms.Get(dimensions, "Width"), CultureInfo.InvariantCulture) / 96.0;
        return Math.Max(1, (int)Math.Round(logical * factor));
    }
    private static int Dimension(object control, string property)
    { return (int)RuntimeForms.Get(control, property); }
    private int PaddingSize(string property)
    { return Dimension(RuntimeForms.Get(Window, "Padding"), property); }
    private int ScrollbarSize(string property)
    {
        int system = (int)RuntimeForms.FormType("SystemInformation").GetProperty(property).GetValue(null, null);
        return Math.Max(system, Pixels(17));
    }
    private int CurrentContentWidth()
    {
        object client = RuntimeForms.Get(Window, "ClientSize");
        int available = Dimension(client, "Width") - PaddingSize("Horizontal");
        int availableHeight = Dimension(client, "Height");
        if ((bool)RuntimeForms.Get(RuntimeForms.Get(Window, "VerticalScroll"), "Visible"))
            available += ScrollbarSize("VerticalScrollBarWidth");
        if ((bool)RuntimeForms.Get(RuntimeForms.Get(Window, "HorizontalScroll"), "Visible"))
            availableHeight += ScrollbarSize("HorizontalScrollBarHeight");
        int width = Math.Max(Pixels(784), available);
        int horizontalBar = width > available ? ScrollbarSize("HorizontalScrollBarHeight") : 0;
        if ((long)Dimension(Header, "Height") + Dimension(Footer, "Height") + MachineGridHeight(width) +
            PaddingSize("Vertical") + horizontalBar > availableHeight)
            width = Math.Max(Pixels(784), available - ScrollbarSize("VerticalScrollBarWidth"));
        return width;
    }
    private int MachineGridHeight(int width)
    {
        Type sizeType = Grid.GetType().GetProperty("ClientSize").PropertyType;
        object constraint = Activator.CreateInstance(sizeType, new object[] { width, 0 });
        object preferred = RuntimeForms.Call(Grid, "GetPreferredSize", constraint);
        return Math.Max(Pixels(58), Dimension(preferred, "Height"));
    }
    private void LayoutContent(int width)
    {
        RuntimeForms.SetValue(Window, "AutoScrollPosition", 0, 0);
        int headerHeight = Dimension(Header, "Height"), footerHeight = Dimension(Footer, "Height");
        int gridHeight = MachineGridHeight(width);
        RuntimeForms.Call(content, "SuspendLayout");
        try
        {
            Position(content, PaddingSize("Left"), PaddingSize("Top"), width, headerHeight + gridHeight + footerHeight);
            Position(Header, 0, 0, width, headerHeight);
            Position(Grid, 0, headerHeight, width, gridHeight);
            Position(Footer, 0, headerHeight + gridHeight, width, footerHeight);
            RuntimeForms.SetValue(Window, "AutoScrollMinSize", width + PaddingSize("Horizontal"),
                headerHeight + gridHeight + footerHeight + PaddingSize("Vertical"));
        }
        finally { RuntimeForms.Call(content, "ResumeLayout", true); }
    }
    private void RefreshSections()
    {
        if (!layoutReady || arranging) return;
        arranging = true;
        try
        {
            bool bin = (bool)RuntimeForms.Get(binEnabled, "Checked");
            bool naming = (bool)RuntimeForms.Get(assignNames, "Checked");
            RuntimeForms.Set(binDetails, "Visible", bin);
            RuntimeForms.Set(binDetails, "Enabled", bin);
            RuntimeForms.Set(outputOptions, "Top", Pixels(bin ? 210 : 82));
            RuntimeForms.Set(outputOptions, "Height", Pixels(naming ? 326 : 182));
            int next = Dimension(outputOptions, "Bottom") + Pixels(8);
            Position(machineHint, 0, next, Math.Max(Pixels(784), Dimension(Header, "Width")), Pixels(34));
            RuntimeForms.SetEnum(machineHint, "Anchor", "Top, Left, Right");
            RuntimeForms.Set(Header, "Height", next + Pixels(34));
            if (shown) FitWindow(); else LayoutContent(Pixels(784));
        }
        finally { arranging = false; }
    }
    private void FitWindow()
    {
        if (RuntimeForms.Get(Window, "WindowState").ToString() != "Normal")
        { LayoutContent(CurrentContentWidth()); return; }
        // Measure the window frame without scrollbars left over from the old
        // layout; otherwise each refit could count their width or height twice.
        RuntimeForms.Set(Window, "AutoScroll", false);
        try
        {
            RuntimeForms.Call(Window, "PerformLayout");
            MethodInfo getArea = RuntimeForms.FormType("Screen").GetMethod("GetWorkingArea", new Type[] { RuntimeForms.FormType("Control") });
            object area = getArea.Invoke(null, new object[] { Window });
            object client = RuntimeForms.Get(Window, "ClientSize");
            int frameWidth = Dimension(Window, "Width") - Dimension(client, "Width");
            int frameHeight = Dimension(Window, "Height") - Dimension(client, "Height");
            int gap = Pixels(8);
            int maxWidth = Math.Max(1, Dimension(area, "Width") - 2 * gap);
            int maxHeight = Math.Max(1, Dimension(area, "Height") - 2 * gap);
            int centerX = Dimension(Window, "Left") + Dimension(Window, "Width") / 2;
            int centerY = Dimension(Window, "Top") + Dimension(Window, "Height") / 2;
            int minWidth = Math.Min(maxWidth, Pixels(784) + PaddingSize("Horizontal") + frameWidth);
            int minHeight = Math.Min(maxHeight, Pixels(420) + frameHeight);
            RuntimeForms.SetValue(Window, "MinimumSize", 0, 0);
            RuntimeForms.SetValue(Window, "MaximumSize", maxWidth, maxHeight);
            RuntimeForms.SetValue(Window, "MinimumSize", minWidth, minHeight);
            MachineDialogFit fit = MachineDialogFit.Calculate(Pixels(784), Math.Max(1, maxWidth - frameWidth),
                Math.Max(1, maxHeight - frameHeight), PaddingSize("Horizontal"), PaddingSize("Vertical"),
                Dimension(Header, "Height") + Dimension(Footer, "Height"), Pixels(190),
                ScrollbarSize("VerticalScrollBarWidth"), ScrollbarSize("HorizontalScrollBarHeight"), MachineGridHeight);
            RuntimeForms.SetValue(Window, "ClientSize", fit.ClientWidth, Math.Max(fit.ClientHeight, minHeight - frameHeight));
            LayoutContent(fit.ContentWidth);
            int left = Dimension(area, "Left") + gap, top = Dimension(area, "Top") + gap;
            RuntimeForms.Set(Window, "Left", Math.Max(left, Math.Min(centerX - Dimension(Window, "Width") / 2, left + maxWidth - Dimension(Window, "Width"))));
            RuntimeForms.Set(Window, "Top", Math.Max(top, Math.Min(centerY - Dimension(Window, "Height") / 2, top + maxHeight - Dimension(Window, "Height"))));
        }
        finally { RuntimeForms.Set(Window, "AutoScroll", true); }
    }
    private void TryAccept(MachineTarget target) { TryAccept(target, null); }
    private void TryAccept(MachineTarget target, ExternalDriveSelection media)
    {
        try
        {
            bool assign = (bool)RuntimeForms.Get(assignNames, "Checked");
            string[] names = CurrentProgramNames();
            if (callsEnabled) ProgramCallChain.Targets(names, callOrder);
            WorkOffsetPrograms.Validate(offsetsEnabled ? workOffsets : null);
            bool save = (bool)RuntimeForms.Get(saveProject, "Checked");
            if (save) PostFiles.ProjectDirectory(projectFile);
            if (!Directory.Exists(target.DirectoryPath)) throw new DirectoryNotFoundException("\u041F\u0430\u043F\u043A\u0430 \u0432\u044B\u0432\u043E\u0434\u0430 \u043D\u0435\u0434\u043E\u0441\u0442\u0443\u043F\u043D\u0430:\u000A" + target.DirectoryPath);
            if (media != null) media.EnsurePresent();
            Choice = new MachineChoice(); Choice.Target = target; Choice.ProgramExtensions = (string[])outputExtensions.Clone(); Choice.ExternalDrive = media;
            Choice.ProgramNames = names; Choice.AssignNames = assign; Choice.SaveToProject = save;
            Choice.ProgramCallOrder = callsEnabled ? (int[])callOrder.Clone() : null;
            Choice.WorkOffsets = offsetsEnabled ? (int[])workOffsets.Clone() : null;
            Choice.BinMode = !((bool)RuntimeForms.Get(binEnabled, "Checked")) ? FanucBinMode.Off :
                ((int)RuntimeForms.Get(binMode, "SelectedIndex") == 1 ? FanucBinMode.New : FanucBinMode.Merge);
            Choice.BinSizeMB = SelectedSize();
            Choice.Units = CamSetup.OutputUnits.PostDefined;
            // Retry persisting if a previous checkbox change could not be saved.
            if (preferences.SaveToProject != save) preferences.Save(save);
            Finish("OK");
        }
        catch (Exception ex) { ShowProblem(ex); }
    }
}

#endif

// Screen-bounded sizing only; the real FlowLayoutPanel measures machine tiles.
// Screen-bounded sizing only; the real FlowLayoutPanel measures machine tiles.
internal sealed class MachineDialogFit
{
    internal int ContentWidth, ClientWidth, ClientHeight;

    internal static MachineDialogFit Calculate(int minimumContentWidth, int maxClientWidth, int maxClientHeight,
        int horizontalPadding, int verticalPadding, int fixedHeight, int columnStep,
        int verticalScrollbar, int horizontalScrollbar, Func<int, int> gridHeight)
    {
        if (minimumContentWidth < 1 || maxClientWidth < 1 || maxClientHeight < 1 || columnStep < 1)
            throw new ArgumentOutOfRangeException("dimensions");
        int ceiling = Math.Max(minimumContentWidth, maxClientWidth - horizontalPadding);
        int width = minimumContentWidth;
        while (true)
        {
            int clientWidth = Math.Min(maxClientWidth, width + horizontalPadding);
            int extra = width + horizontalPadding > clientWidth ? horizontalScrollbar : 0;
            long neededHeight = (long)fixedHeight + verticalPadding + gridHeight(width) + extra;
            if (neededHeight <= maxClientHeight)
                return new MachineDialogFit { ContentWidth = width, ClientWidth = clientWidth, ClientHeight = (int)neededHeight };
            if (width == ceiling)
            {
                int scrollingWidth = Math.Max(minimumContentWidth, maxClientWidth - horizontalPadding - verticalScrollbar);
                return new MachineDialogFit { ContentWidth = scrollingWidth,
                    ClientWidth = Math.Min(maxClientWidth, scrollingWidth + horizontalPadding + verticalScrollbar),
                    ClientHeight = maxClientHeight };
            }
            // Widen by one tile column until every row fits, or use scrolling
            // at the display limit. Never discard or truncate machine tiles.
            width = (int)Math.Min((long)ceiling, (long)width + columnStep);
        }
    }
}

// This feature rewrites only an existing staged NC file; it creates no sidecars.
internal static class ProgramCallChain
{
    internal static int[] Identity(int count)
    {
        int[] result = new int[count];
        for (int i = 0; i < count; i++) result[i] = i;
        return result;
    }

    internal static void ValidateOrder(int[] order, int count)
    {
        if (order == null || order.Length != count || count < 2)
            throw new ArgumentException("\u0414\u043B\u044F \u0446\u0435\u043F\u043E\u0447\u043A\u0438 \u0432\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043D\u0435 \u043C\u0435\u043D\u0435\u0435 \u0434\u0432\u0443\u0445 \u0423\u041F.");
        bool[] seen = new bool[count];
        foreach (int index in order)
        {
            if (index < 0 || index >= count || seen[index])
                throw new ArgumentException("\u041F\u043E\u0440\u044F\u0434\u043E\u043A \u0432\u044B\u0437\u043E\u0432\u043E\u0432 \u0434\u043E\u043B\u0436\u0435\u043D \u0441\u043E\u0434\u0435\u0440\u0436\u0430\u0442\u044C \u043A\u0430\u0436\u0434\u0443\u044E \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u0443\u044E \u0423\u041F \u0440\u043E\u0432\u043D\u043E \u043E\u0434\u0438\u043D \u0440\u0430\u0437.");
            seen[index] = true;
        }
    }

    internal static void Move(List<int> order, int from, int to)
    {
        if (from < 0 || from >= order.Count || to < 0 || to >= order.Count || from == to) return;
        int value = order[from]; order.RemoveAt(from); order.Insert(to, value);
    }

    private static uint Number(string name)
    {
        string normalized;
        try { normalized = ProgramNames.Normalize(name); }
        catch (ArgumentException ex)
        {
            throw new ArgumentException("\u0414\u043B\u044F \u0446\u0435\u043F\u043E\u0447\u043A\u0438 \u043D\u0443\u0436\u043D\u044B \u0447\u0438\u0441\u043B\u043E\u0432\u044B\u0435 \u043D\u043E\u043C\u0435\u0440\u0430 \u0423\u041F: O1, O2 \u0438 \u0442. \u0434. \u0412\u043A\u043B\u044E\u0447\u0438\u0442\u0435 \u00AB\u041D\u0430\u0437\u043D\u0430\u0447\u0438\u0442\u044C \u0438\u043C\u044F \u0423\u041F\u00BB \u0438 \u0437\u0430\u0434\u0430\u0439\u0442\u0435 \u043D\u043E\u043C\u0435\u0440\u0430.\n" + ex.Message, ex);
        }
        uint number = UInt32.Parse(normalized.Substring(1), CultureInfo.InvariantCulture);
        // Longer P words can encode repeat counts on a four-digit FANUC control.
        // Do not guess the machine's optional extended-program-number format.
        if (number > 9999)
            throw new ArgumentException("\u0412 \u0446\u0435\u043F\u043E\u0447\u043A\u0435 M98 P \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u044E\u0442\u0441\u044F \u043D\u043E\u043C\u0435\u0440\u0430 O1\u2013O9999. \u0424\u043E\u0440\u043C\u0430\u0442 \u0432\u044B\u0437\u043E\u0432\u0430 5\u20138-\u0437\u043D\u0430\u0447\u043D\u044B\u0445 \u043D\u043E\u043C\u0435\u0440\u043E\u0432 \u0437\u0430\u0432\u0438\u0441\u0438\u0442 \u043E\u0442 \u0441\u0442\u043E\u0439\u043A\u0438 \u0438 \u0442\u0440\u0435\u0431\u0443\u0435\u0442 \u043E\u0442\u0434\u0435\u043B\u044C\u043D\u043E\u0439 \u043D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438.");
        return number;
    }

    internal static string[][] Targets(string[] names, int[] order)
    {
        if (order == null) return null;
        ValidateOrder(order, names.Length);
        uint[] numbers = new uint[names.Length];
        HashSet<uint> unique = new HashSet<uint>();
        for (int i = 0; i < names.Length; i++)
        {
            numbers[i] = Number(names[i]);
            if (!unique.Add(numbers[i]))
                throw new ArgumentException("\u0412 \u0446\u0435\u043F\u043E\u0447\u043A\u0435 \u043F\u043E\u0432\u0442\u043E\u0440\u044F\u0435\u0442\u0441\u044F O-\u043D\u043E\u043C\u0435\u0440: " + names[i] + ". \u0412\u0435\u0434\u0443\u0449\u0438\u0435 \u043D\u0443\u043B\u0438 \u043D\u0435 \u0434\u0435\u043B\u0430\u044E\u0442 \u043D\u043E\u043C\u0435\u0440 \u0434\u0440\u0443\u0433\u0438\u043C.");
        }
        string[][] result = new string[names.Length][];
        for (int i = 0; i < result.Length; i++) result[i] = new string[0];
        string[] calls = new string[order.Length - 1];
        for (int i = 1; i < order.Length; i++)
            calls[i - 1] = numbers[order[i]].ToString(CultureInfo.InvariantCulture);
        result[order[0]] = calls;
        // Nonempty = main program with all calls; empty = subprogram ending M99.
        // A null outer plan means the feature is disabled.
        return result;
    }

    internal static string Describe(string[] names, int[] order)
    {
        if (order == null) return "\u0412\u044B\u0437\u043E\u0432\u044B M98 \u0432\u044B\u043A\u043B\u044E\u0447\u0435\u043D\u044B";
        ValidateOrder(order, names.Length);
        List<string> labels = new List<string>();
        for (int i = 1; i < order.Length; i++) labels.Add(names[order[i]]);
        return "\u0413\u043B\u0430\u0432\u043D\u0430\u044F: " + names[order[0]] + "; \u0432\u044B\u0437\u043E\u0432\u044B \u043F\u043E \u043F\u043E\u0440\u044F\u0434\u043A\u0443: " + String.Join(", ", labels.ToArray());
    }

    internal static void Apply(string path, string ownName, string[] targetDigits)
    {
        if (targetDigits == null) return;
        if (new FileInfo(path).Length > 512L * 1024 * 1024)
            throw new IOException("\u0423\u041F \u043F\u0440\u0435\u0432\u044B\u0448\u0430\u0435\u0442 512 \u041C\u0411. \u0414\u043E\u0431\u0430\u0432\u043B\u0435\u043D\u0438\u0435 \u0432\u044B\u0437\u043E\u0432\u0430 M98 \u043E\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D\u043E.");
        byte[] raw = File.ReadAllBytes(path);
        byte[] changed = Rewrite(raw, ownName, targetDigits);
        if (!Object.ReferenceEquals(raw, changed)) File.WriteAllBytes(path, changed);
    }

    private sealed class Block
    {
        internal string Code;
        internal int Start, TokenStart;
    }

    private static IOException Error(string name, string detail)
    {
        return new IOException("\u0426\u0435\u043F\u043E\u0447\u043A\u0430 \u0432\u044B\u0437\u043E\u0432\u043E\u0432, " + name + ": " + detail + "\n\u0412\u044B\u0432\u043E\u0434 \u043F\u0430\u043A\u0435\u0442\u0430 \u043E\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D \u0434\u043E \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u044F \u0423\u041F.");
    }

    internal static byte[] Rewrite(byte[] raw, string ownName, string[] targetDigits)
    {
        if (targetDigits == null) return raw;
        uint own = Number(ownName);
        uint[] targets = new uint[targetDigits.Length];
        HashSet<uint> unique = new HashSet<uint>();
        for (int i = 0; i < targetDigits.Length; i++)
        {
            targets[i] = Number("O" + targetDigits[i]);
            if (targets[i] == own) throw Error(ownName, "\u0432\u044B\u0437\u043E\u0432 \u0441\u0430\u043C\u043E\u0439 \u0441\u0435\u0431\u044F \u0437\u0430\u043F\u0440\u0435\u0449\u0451\u043D.");
            if (!unique.Add(targets[i])) throw Error(ownName, "\u043F\u043E\u0432\u0442\u043E\u0440\u044F\u0435\u0442\u0441\u044F \u043D\u043E\u043C\u0435\u0440 \u0432\u044B\u0437\u044B\u0432\u0430\u0435\u043C\u043E\u0439 \u0423\u041F.");
        }
        bool main = targets.Length > 0;
        if (raw == null || raw.Length == 0) throw Error(ownName, "\u043F\u0443\u0441\u0442\u0430\u044F \u0423\u041F.");
        if (raw.Length >= 2 && ((raw[0] == 0xff && raw[1] == 0xfe) || (raw[0] == 0xfe && raw[1] == 0xff)))
            throw Error(ownName, "UTF-16 \u043D\u0435 \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u0442\u0441\u044F. \u041D\u0443\u0436\u043D\u0430 ASCII/UTF-8/\u043E\u0434\u043D\u043E\u0431\u0430\u0439\u0442\u043E\u0432\u0430\u044F \u0423\u041F.");
        int offset = raw.Length >= 3 && raw[0] == 0xef && raw[1] == 0xbb && raw[2] == 0xbf ? 3 : 0;
        string text = Encoding.ASCII.GetString(raw, offset, raw.Length - offset);
        if (text.IndexOf('\0') >= 0) throw Error(ownName, "\u043D\u0443\u043B\u0435\u0432\u044B\u0435 \u0431\u0430\u0439\u0442\u044B \u0438\u043B\u0438 \u043D\u0435\u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u043C\u0430\u044F \u043A\u043E\u0434\u0438\u0440\u043E\u0432\u043A\u0430.");
        char[] visible = text.ToCharArray();
        int depth = 0; bool lineComment = false;
        for (int i = 0; i < visible.Length; i++)
        {
            char c = text[i];
            if (c == '\r' || c == '\n') { lineComment = false; continue; }
            if (lineComment) { visible[i] = ' '; continue; }
            if (c == '(') { depth++; visible[i] = ' '; continue; }
            if (c == ')')
            {
                if (depth == 0) throw Error(ownName, "\u043B\u0438\u0448\u043D\u044F\u044F \u0437\u0430\u043A\u0440\u044B\u0432\u0430\u044E\u0449\u0430\u044F \u0441\u043A\u043E\u0431\u043A\u0430 \u043A\u043E\u043C\u043C\u0435\u043D\u0442\u0430\u0440\u0438\u044F.");
                depth--; visible[i] = ' '; continue;
            }
            if (depth > 0) { visible[i] = ' '; continue; }
            // A trailing semicolon/EOB and its annotation never count as NC words.
            if (c == ';') { lineComment = true; visible[i] = ' '; }
        }
        if (depth != 0) throw Error(ownName, "\u043D\u0435\u0437\u0430\u043A\u0440\u044B\u0442\u044B\u0439 \u043A\u043E\u043C\u043C\u0435\u043D\u0442\u0430\u0440\u0438\u0439.");
        string codeText = new string(visible);
        List<Block> blocks = new List<Block>();
        bool closed = false, openingPercent = false;
        string newline = null;
        for (int start = 0; start < text.Length; )
        {
            int end = start;
            while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;
            int next = end;
            if (next < text.Length)
            {
                if (text[next++] == '\r' && next < text.Length && text[next] == '\n') next++;
                if (newline == null) newline = text.Substring(end, next - end);
            }
            string line = codeText.Substring(start, end - start);
            string code = line.Trim();
            if (code.Length > 0)
            {
                if (code == "%")
                {
                    if (closed || (blocks.Count == 0 && openingPercent)) throw Error(ownName, "\u043B\u0438\u0448\u043D\u0438\u0439 \u0440\u0430\u0437\u0434\u0435\u043B\u0438\u0442\u0435\u043B\u044C %.");
                    if (blocks.Count == 0) openingPercent = true; else closed = true;
                }
                else
                {
                    if (closed || code.IndexOf('%') >= 0) throw Error(ownName, "\u043A\u043E\u0434 \u043F\u043E\u0441\u043B\u0435 \u0437\u0430\u043A\u0440\u044B\u0432\u0430\u044E\u0449\u0435\u0433\u043E % \u0438\u043B\u0438 \u043D\u0435\u0441\u043A\u043E\u043B\u044C\u043A\u043E \u0423\u041F \u0432 \u0444\u0430\u0439\u043B\u0435.");
                    int first = 0; while (first < line.Length && Char.IsWhiteSpace(line[first])) first++;
                    Block block = new Block(); block.Start = start; block.TokenStart = start + first; block.Code = code;
                    blocks.Add(block);
                }
            }
            start = next;
        }
        // A footer such as N13 (TOTAL MACHINING TIME ...) has no NC command.
        // Exclude only trailing sequence-only entries from end detection.
        // Their original bytes and comments remain in the output unchanged.
        while (blocks.Count > 1 && Regex.IsMatch(blocks[blocks.Count - 1].Code, @"^N[0-9]+$", RegexOptions.IgnoreCase))
            blocks.RemoveAt(blocks.Count - 1);
        if (blocks.Count < 2) throw Error(ownName, "\u043D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D\u044B \u0437\u0430\u0433\u043E\u043B\u043E\u0432\u043E\u043A \u0438 \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0438\u0435 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u044B.");
        Match header = Regex.Match(blocks[0].Code, @"^O([0-9]{1,8})$", RegexOptions.IgnoreCase);
        if (!header.Success || UInt32.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture) != own)
            throw Error(ownName, "O-\u043D\u043E\u043C\u0435\u0440 \u0432\u043D\u0443\u0442\u0440\u0438 \u0444\u0430\u0439\u043B\u0430 \u043D\u0435 \u0441\u043E\u0432\u043F\u0430\u0434\u0430\u0435\u0442 \u0441 \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u043C \u043D\u043E\u043C\u0435\u0440\u043E\u043C. \u0412\u043A\u043B\u044E\u0447\u0438\u0442\u0435 \u00AB\u041D\u0430\u0437\u043D\u0430\u0447\u0438\u0442\u044C \u0438\u043C\u044F \u0423\u041F\u00BB \u0438\u043B\u0438 \u0438\u0441\u043F\u0440\u0430\u0432\u044C\u0442\u0435 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440.");
        for (int i = 1; i < blocks.Count; i++)
        {
            if (Regex.IsMatch(blocks[i].Code, @"^O[0-9]", RegexOptions.IgnoreCase))
                throw Error(ownName, "\u0432 \u0444\u0430\u0439\u043B\u0435 \u043D\u0435\u0441\u043A\u043E\u043B\u044C\u043A\u043E \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C.");
            if (i + 1 < blocks.Count && Regex.IsMatch(blocks[i].Code, @"(?<![A-Z_])M[ \t]*0*(?:30|2|99)(?![0-9.])", RegexOptions.IgnoreCase))
                throw Error(ownName, "\u043A\u043E\u043C\u0430\u043D\u0434\u0430 \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0438\u044F \u0432\u0441\u0442\u0440\u0435\u0447\u0430\u0435\u0442\u0441\u044F \u0434\u043E \u043A\u043E\u043D\u0446\u0430 \u0444\u0430\u0439\u043B\u0430. \u041C\u0435\u0441\u0442\u043E \u0432\u044B\u0437\u043E\u0432\u0430 \u043D\u0435\u043E\u0434\u043D\u043E\u0437\u043D\u0430\u0447\u043D\u043E.");
        }
        Block last = blocks[blocks.Count - 1];
        Match ending = Regex.Match(last.Code, @"^(?:N[0-9]+[ \t]*)?M[ \t]*(?<digits>0*(?:30|2|99))[ \t]*$", RegexOptions.IgnoreCase);
        if (!ending.Success)
            throw Error(ownName, "\u043E\u0436\u0438\u0434\u0430\u0435\u0442\u0441\u044F \u043E\u0442\u0434\u0435\u043B\u044C\u043D\u044B\u0439 \u043F\u043E\u0441\u043B\u0435\u0434\u043D\u0438\u0439 \u043A\u0430\u0434\u0440 M30, M02 \u0438\u043B\u0438 M99 \u0431\u0435\u0437 \u0434\u0440\u0443\u0433\u0438\u0445 \u043A\u043E\u043C\u0430\u043D\u0434 \u0438 \u0443\u0441\u043B\u043E\u0432\u043D\u043E\u0433\u043E \u043F\u0440\u043E\u043F\u0443\u0441\u043A\u0430.");

        // Recognize a complete existing call list, including numbered comments
        // between its blocks. Never duplicate, silently reorder or delete it.
        List<uint> existing = new List<uint>();
        for (int i = blocks.Count - 2; i > 0; i--)
        {
            string code = blocks[i].Code;
            if (Regex.IsMatch(code, @"^N[0-9]+$", RegexOptions.IgnoreCase)) continue;
            if (!Regex.IsMatch(code, @"(?<![A-Z_])M[ \t]*0*98(?![0-9.])", RegexOptions.IgnoreCase)) break;
            Match call = Regex.Match(code, @"^(?:N[0-9]+[ \t]*)?M[ \t]*0*98[ \t]*P[ \t]*([0-9]{1,8})(?:[ \t]*L[ \t]*0*1)?[ \t]*$", RegexOptions.IgnoreCase);
            if (!call.Success)
                throw Error(ownName, "\u043F\u0435\u0440\u0435\u0434 \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0438\u0435\u043C \u0443\u0436\u0435 \u0435\u0441\u0442\u044C \u0441\u043B\u043E\u0436\u043D\u044B\u0439 \u0432\u044B\u0437\u043E\u0432 M98. \u041F\u0440\u043E\u0432\u0435\u0440\u044C\u0442\u0435 \u0435\u0433\u043E \u043F\u0435\u0440\u0435\u0434 \u0432\u044B\u0432\u043E\u0434\u043E\u043C.");
            existing.Add(UInt32.Parse(call.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        existing.Reverse();
        if (existing.Count > 0)
        {
            bool same = existing.Count == targets.Length;
            for (int i = 0; same && i < existing.Count; i++) same = existing[i] == targets[i];
            if (!same)
                throw Error(ownName, "\u043F\u0435\u0440\u0435\u0434 \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0438\u0435\u043C \u0443\u0436\u0435 \u0435\u0441\u0442\u044C \u0432\u044B\u0437\u043E\u0432\u044B M98, \u043D\u0435 \u0441\u043E\u0432\u043F\u0430\u0434\u0430\u044E\u0449\u0438\u0435 \u0441 \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u043C \u043F\u043E\u0440\u044F\u0434\u043A\u043E\u043C. \u041F\u0440\u043E\u0432\u0435\u0440\u044C\u0442\u0435 \u0438\u0445 \u043F\u0435\u0440\u0435\u0434 \u0432\u044B\u0432\u043E\u0434\u043E\u043C.");
        }
        if (newline == null) newline = "\r\n";
        int at = last.Start;
        bool commentPrefix = text.Substring(last.Start, last.TokenStart - last.Start).Trim().Length > 0;
        if (commentPrefix) at = last.TokenStart;
        StringBuilder calls = new StringBuilder();
        if (main && existing.Count == 0)
        {
            if (commentPrefix) calls.Append(newline);
            foreach (uint target in targets)
                calls.Append("M98 P").Append(target.ToString(CultureInfo.InvariantCulture)).Append(newline);
        }
        byte[] added = Encoding.ASCII.GetBytes(calls.ToString());
        System.Text.RegularExpressions.Group digits = ending.Groups["digits"];
        string desiredEnd = main ? "30" : "99";
        bool sameEnd = digits.Value.TrimStart('0') == desiredEnd;
        if (added.Length == 0 && sameEnd) return raw;
        byte[] endBytes = Encoding.ASCII.GetBytes(sameEnd ? digits.Value : desiredEnd);
        // Edit only the numeric part of the final M word: keep its N label,
        // whitespace, comments, encoding, line endings and post footer intact.
        int rawAt = offset + at;
        int endAt = offset + last.TokenStart + digits.Index;
        byte[] result = new byte[checked(raw.Length + added.Length + endBytes.Length - digits.Length)];
        Buffer.BlockCopy(raw, 0, result, 0, rawAt);
        Buffer.BlockCopy(added, 0, result, rawAt, added.Length);
        Buffer.BlockCopy(raw, rawAt, result, rawAt + added.Length, endAt - rawAt);
        Buffer.BlockCopy(endBytes, 0, result, endAt + added.Length, endBytes.Length);
        Buffer.BlockCopy(raw, endAt + digits.Length, result, endAt + added.Length + endBytes.Length, raw.Length - endAt - digits.Length);
        return result;
    }
}


// Repeats complete, consecutive tool sections in memory; no new files or NX edits.
// Deliberately accepts only explicit three-axis ISO milling, not arbitrary macros.
internal static class WorkOffsetPrograms
{
    internal static void Validate(int[] offsets)
    {
        if (offsets == null) return;
        if (offsets.Length < 1 || offsets.Length > 6)
            throw new ArgumentException("\u0414\u043E\u0431\u0430\u0432\u044C\u0442\u0435 \u043E\u0442 \u043E\u0434\u043D\u043E\u0439 \u0434\u043E \u0448\u0435\u0441\u0442\u0438 \u043F\u0440\u0438\u0432\u044F\u0437\u043E\u043A: 1\u20136 (G54\u2013G59).");
        HashSet<int> unique = new HashSet<int>();
        foreach (int offset in offsets)
            if (offset < 1 || offset > 6 || !unique.Add(offset))
                throw new ArgumentException("\u041F\u0440\u0438\u0432\u044F\u0437\u043A\u0438 \u0434\u043E\u043B\u0436\u043D\u044B \u0431\u044B\u0442\u044C \u0440\u0430\u0437\u043D\u044B\u043C\u0438: 1\u20136 (G54\u2013G59).");
    }

    internal static string Describe(int[] offsets)
    {
        if (offsets == null) return "\u041E\u0431\u0440\u0430\u0431\u043E\u0442\u043A\u0430 \u043F\u043E \u043F\u0440\u0438\u0432\u044F\u0437\u043A\u0430\u043C \u0432\u044B\u043A\u043B\u044E\u0447\u0435\u043D\u0430";
        Validate(offsets);
        List<string> names = new List<string>();
        foreach (int offset in offsets) names.Add(offset + " (G" + (53 + offset) + ")");
        return "\u041A\u0430\u0436\u0434\u044B\u0439 \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442: " + String.Join(" \u2192 ", names.ToArray());
    }

    internal static void Apply(string path, string name, int[] offsets)
    {
        if (offsets == null) return;
        Validate(offsets);
        if (new FileInfo(path).Length > 512L * 1024 * 1024)
            throw Error(name, 0, "\u0440\u0430\u0437\u043C\u0435\u0440 \u0423\u041F \u043F\u0440\u0435\u0432\u044B\u0448\u0430\u0435\u0442 512 \u041C\u0411.");
        byte[] raw = File.ReadAllBytes(path);
        byte[] result = Rewrite(raw, name, offsets);
        if (!Object.ReferenceEquals(raw, result)) File.WriteAllBytes(path, result);
    }

    private sealed class Word
    {
        internal char Letter;
        internal decimal Value;
        internal int Start, Length;
    }
    private sealed class Line
    {
        internal int Start, End, Number;
        internal bool Optional, Percent;
        internal readonly List<Word> Words = new List<Word>();
        internal bool Has(char letter) { foreach (Word w in Words) if (w.Letter == letter) return true; return false; }
        internal bool Has(char letter, decimal value)
        { foreach (Word w in Words) if (w.Letter == letter && w.Value == value) return true; return false; }
        internal decimal Value(char letter)
        { foreach (Word w in Words) if (w.Letter == letter) return w.Value; return -1; }
        internal bool Axes { get { return Has('X') || Has('Y') || Has('Z'); } }
        internal bool Empty { get { foreach (Word w in Words) if (w.Letter != 'N') return false; return true; } }
    }
    private sealed class State
    {
        internal readonly Dictionary<string, decimal> Values = new Dictionary<string, decimal>();
        internal State Copy() { State s = new State(); foreach (KeyValuePair<string, decimal> v in Values) s.Values.Add(v.Key, v.Value); return s; }
        internal decimal Get(string key) { decimal v; return Values.TryGetValue(key, out v) ? v : -1000000000M; }
        internal void Set(string key, decimal value) { Values[key] = value; }
        internal string Signature(params string[] keys)
        {
            StringBuilder b = new StringBuilder();
            foreach (string key in keys) b.Append(key).Append('=').Append(Get(key).ToString(CultureInfo.InvariantCulture)).Append(';');
            return b.ToString();
        }
    }
    private static IOException Error(string name, int line, string reason)
    {
        return new IOException("\u041E\u0431\u0440\u0430\u0431\u043E\u0442\u043A\u0430 \u043F\u043E \u043F\u0440\u0438\u0432\u044F\u0437\u043A\u0430\u043C, " + name + (line > 0 ? ", \u0441\u0442\u0440\u043E\u043A\u0430 " + line : "") + ": " + reason +
            "\n\u0412\u044B\u0432\u043E\u0434 \u043F\u0430\u043A\u0435\u0442\u0430 \u043E\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D \u0434\u043E \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u044F \u0423\u041F. \u0418\u0441\u043F\u0440\u0430\u0432\u044C\u0442\u0435 \u043D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430 \u0438\u043B\u0438 \u0432\u044B\u043A\u043B\u044E\u0447\u0438\u0442\u0435 \u043E\u0431\u0440\u0430\u0431\u043E\u0442\u043A\u0443 \u043F\u043E \u043F\u0440\u0438\u0432\u044F\u0437\u043A\u0430\u043C.");
    }
    private static string GGroup(decimal g)
    {
        if (g == 0 || g == 1 || g == 2 || g == 3 || g == 73 || g == 74 || g == 76 || (g >= 80 && g <= 89)) return "motion";
        if (g == 90 || g == 91) return "distance";
        if (g == 17 || g == 18 || g == 19) return "plane";
        if (g == 20 || g == 21) return "units";
        if (g == 40 || g == 41 || g == 42) return "cutter";
        if (g == 43 || g == 49) return "length";
        if (g >= 54 && g <= 59) return "offset";
        if (g == 61 || g == 64) return "path";
        if (g == 94 || g == 95) return "feedmode";
        if (g == 98 || g == 99) return "return";
        return "";
    }
    private static bool Cycle(decimal g) { return g == 73 || g == 74 || g == 76 || (g >= 81 && g <= 89); }
    private static void Update(State s, Line line)
    {
        foreach (Word w in line.Words)
        {
            if (w.Letter == 'G') { string key = GGroup(w.Value); if (key.Length > 0) s.Set(key, w.Value); }
            else if (w.Letter == 'M' && (w.Value == 3 || w.Value == 4 || w.Value == 5 || w.Value == 19)) s.Set("spindle", w.Value);
            else if (w.Letter == 'M' && (w.Value == 7 || w.Value == 8)) s.Set(w.Value == 7 ? "mist" : "flood", 1);
            else if (w.Letter == 'M' && w.Value == 9)
            {
                // Track only coolant circuits selected by this NC program.
                if (s.Values.ContainsKey("mist")) s.Set("mist", 0);
                if (s.Values.ContainsKey("flood")) s.Set("flood", 0);
            }
            else if ("FSHDRPQT".IndexOf(w.Letter) >= 0) s.Set(w.Letter.ToString(), w.Value);
        }
    }

    private static List<Line> Parse(string text, string name)
    {
        List<Line> lines = new List<Line>(); int number = 0;
        for (int start = text.Length > 0 && text[0] == '\u00EF' && text.Length >= 3 && text[1] == '\u00BB' && text[2] == '\u00BF' ? 3 : 0; start < text.Length; )
        {
            int end = start;
            while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;
            int next = end;
            if (next < text.Length && text[next++] == '\r' && next < text.Length && text[next] == '\n') next++;
            Line line = new Line(); line.Start = start; line.End = next; line.Number = ++number;
            char[] visible = text.Substring(start, end - start).ToCharArray(); int depth = 0; bool tail = false;
            for (int i = 0; i < visible.Length; i++)
            {
                char c = visible[i];
                if (tail) { visible[i] = ' '; continue; }
                if (c == '(') { depth++; visible[i] = ' '; }
                else if (c == ')') { if (depth == 0) throw Error(name, number, "\u043B\u0438\u0448\u043D\u044F\u044F \u0441\u043A\u043E\u0431\u043A\u0430 \u043A\u043E\u043C\u043C\u0435\u043D\u0442\u0430\u0440\u0438\u044F."); depth--; visible[i] = ' '; }
                else if (depth > 0) visible[i] = ' ';
                else if (c == ';') { tail = true; visible[i] = ' '; }
                else if (c > 127 || (c < 32 && c != '\t')) throw Error(name, number, "\u043D\u0435\u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u043C\u0430\u044F \u043A\u043E\u0434\u0438\u0440\u043E\u0432\u043A\u0430 \u0438\u043B\u0438 \u0441\u0438\u043C\u0432\u043E\u043B \u0432\u043D\u0435 \u043A\u043E\u043C\u043C\u0435\u043D\u0442\u0430\u0440\u0438\u044F.");
            }
            if (depth != 0) throw Error(name, number, "\u043C\u043D\u043E\u0433\u043E\u0441\u0442\u0440\u043E\u0447\u043D\u044B\u0435 \u0438\u043B\u0438 \u043D\u0435\u0437\u0430\u043A\u0440\u044B\u0442\u044B\u0435 \u043A\u043E\u043C\u043C\u0435\u043D\u0442\u0430\u0440\u0438\u0438 \u043D\u0435 \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u044E\u0442\u0441\u044F \u0432 \u044D\u0442\u043E\u043C \u0440\u0435\u0436\u0438\u043C\u0435.");
            string code = new string(visible);
            if (code.Trim() == "%") line.Percent = true;
            else
            {
                int p = 0; while (p < code.Length && Char.IsWhiteSpace(code[p])) p++;
                if (p < code.Length && code[p] == '/') { line.Optional = true; p++; }
                HashSet<char> letters = new HashSet<char>(); HashSet<string> groups = new HashSet<string>();
                while (p < code.Length)
                {
                    if (Char.IsWhiteSpace(code[p])) { p++; continue; }
                    Match m = Regex.Match(code.Substring(p), @"\A([A-Za-z])[ \t]*([+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+))");
                    decimal value;
                    if (!m.Success || !Decimal.TryParse(m.Groups[2].Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
                        throw Error(name, number, "\u043E\u0436\u0438\u0434\u0430\u0435\u0442\u0441\u044F \u043E\u0431\u044B\u0447\u043D\u044B\u0439 \u0447\u0438\u0441\u043B\u043E\u0432\u043E\u0439 ISO-\u043A\u043E\u0434; \u043C\u0430\u043A\u0440\u043E\u0441\u044B, \u0432\u044B\u0440\u0430\u0436\u0435\u043D\u0438\u044F \u0438 \u043F\u0435\u0440\u0435\u0445\u043E\u0434\u044B \u043D\u0435 \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u044E\u0442\u0441\u044F.");
                    char letter = Char.ToUpperInvariant(m.Groups[1].Value[0]);
                    if ("NGMOTHDFSPQRXYZIJK".IndexOf(letter) < 0 || (letter != 'G' && letter != 'M' && !letters.Add(letter)))
                        throw Error(name, number, "\u043D\u0435\u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u043C\u043E\u0435 \u0438\u043B\u0438 \u043F\u043E\u0432\u0442\u043E\u0440\u044F\u044E\u0449\u0435\u0435\u0441\u044F \u0441\u043B\u043E\u0432\u043E " + letter + ".");
                    if (letter == 'G')
                    {
                        string group = GGroup(value);
                        if (value != Decimal.Truncate(value) || (group.Length == 0 && value != 4 && value != 28))
                            throw Error(name, number, "G" + value + " \u043D\u0435 \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u0442\u0441\u044F \u0434\u043B\u044F \u043F\u043E\u0432\u0442\u043E\u0440\u0435\u043D\u0438\u044F. \u0414\u043E\u043F\u0443\u0441\u0442\u0438\u043C\u044B \u043E\u0431\u044B\u0447\u043D\u044B\u0435 3-\u043E\u0441\u0435\u0432\u044B\u0435 \u0423\u041F \u0441 G54\u2013G59.");
                        if (group.Length > 0 && !groups.Add(group)) throw Error(name, number, "\u043D\u0435\u0441\u043A\u043E\u043B\u044C\u043A\u043E G-\u043A\u043E\u043C\u0430\u043D\u0434 \u043E\u0434\u043D\u043E\u0439 \u0433\u0440\u0443\u043F\u043F\u044B \u0432 \u043A\u0430\u0434\u0440\u0435.");
                    }
                    if (letter == 'M' && (value != Decimal.Truncate(value) ||
                        !(value == 0 || value == 1 || value == 2 || value == 3 || value == 4 || value == 5 || value == 6 || value == 7 || value == 8 || value == 9 || value == 19 || value == 29 || value == 30)))
                        throw Error(name, number, "M" + value + " \u043D\u0435 \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u0442\u0441\u044F \u0434\u043B\u044F \u043F\u043E\u0432\u0442\u043E\u0440\u0435\u043D\u0438\u044F. \u0412\u044B\u0437\u043E\u0432\u044B \u043F\u043E\u0434\u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C \u0438\u0437 \u043C\u0435\u043D\u044E \u0434\u043E\u0431\u0430\u0432\u043B\u044F\u044E\u0442\u0441\u044F \u043F\u043E\u0441\u043B\u0435 \u043E\u0431\u0440\u0430\u0431\u043E\u0442\u043A\u0438 \u043F\u0440\u0438\u0432\u044F\u0437\u043E\u043A.");
                    if ("OTH DN".IndexOf(letter) >= 0 && (value != Decimal.Truncate(value) || value < 0))
                        throw Error(name, number, "\u043D\u0435\u0432\u0435\u0440\u043D\u044B\u0439 \u043D\u043E\u043C\u0435\u0440 " + letter + ".");
                    line.Words.Add(new Word { Letter = letter, Value = value, Start = start + p + m.Groups[2].Index, Length = m.Groups[2].Length });
                    p += m.Length;
                }
            }
            lines.Add(line); start = next;
        }
        return lines;
    }

    private static bool NeedsInitialFeedPerMinute(List<Line> lines)
    {
        // Some posts first emit G94 after a drilling cycle. Make this mode
        // explicit before cutting instead of comparing an unknown entry mode
        // with G94 on the next fixture. Never infer G94 in a program using G95,
        // or from a comment / optional G94 alone.
        bool declared = false;
        foreach (Line line in lines)
        {
            if (line.Has('G', 95)) return false;
            if (!line.Optional && line.Has('G', 94)) declared = true;
        }
        if (!declared) return false;
        foreach (bool skip in new bool[] { false, true })
        {
            State state = new State();
            foreach (Line line in lines)
            {
                if (skip && line.Optional) continue;
                Update(state, line);
                if (line.Has('G', 4) || line.Has('G', 28)) continue;
                decimal motion = state.Get("motion");
                bool explicitCycle = false;
                foreach (Word w in line.Words)
                    if (w.Letter == 'G' && Cycle(w.Value)) explicitCycle = true;
                bool arc = (motion == 2 || motion == 3) && (line.Has('I') || line.Has('J') || line.Has('K'));
                bool feed = motion == 1 || motion == 2 || motion == 3 || Cycle(motion);
                if (feed && (line.Axes || explicitCycle || arc) && state.Get("feedmode") < 0) return true;
            }
        }
        return false;
    }

    // Compare effective modal state at every movement on the first and repeated
    // pass. Run both with optional blocks executed and with them deleted.
    private static List<string> Trace(List<Line> lines, int from, int to, State state, bool skip, string name)
    {
        List<string> trace = new List<string>();
        bool offset = false, absolute = false, rapid = false, cancel = false, length = false, h = false, xy = false, z = false, home = false;
        for (int i = from; i < to; i++)
        {
            Line line = lines[i]; if (line.Optional && skip) continue;
            decimal previousMotion = state.Get("motion");
            foreach (Word w in line.Words)
                if (w.Letter == 'G')
                {
                    if (w.Value >= 54 && w.Value <= 59) offset = true;
                    if (w.Value == 90) absolute = true;
                    if (w.Value == 0) rapid = true;
                    if (w.Value == 40) cancel = true;
                    if (w.Value == 43 && !line.Optional) length = true;
                }
            if (line.Has('H') && !line.Optional) h = true;
            Update(state, line);
            if (line.Has('G', 4))
            {
                if (line.Axes) throw Error(name, line.Number, "\u043E\u0436\u0438\u0434\u0430\u0435\u0442\u0441\u044F \u0432\u044B\u0434\u0435\u0440\u0436\u043A\u0430 G04 P \u0431\u0435\u0437 \u043A\u043E\u043E\u0440\u0434\u0438\u043D\u0430\u0442 \u043E\u0441\u0435\u0439.");
                continue;
            }
            if (line.Has('G', 28))
            {
                if (state.Get("distance") != 91 || !line.Axes ||
                    (line.Has('X') && line.Value('X') != 0) || (line.Has('Y') && line.Value('Y') != 0) || (line.Has('Z') && line.Value('Z') != 0))
                    throw Error(name, line.Number, "\u0434\u043B\u044F \u0432\u043E\u0437\u0432\u0440\u0430\u0442\u0430 \u0442\u0440\u0435\u0431\u0443\u0435\u0442\u0441\u044F G91 G28 \u0441 \u043D\u0443\u043B\u0435\u0432\u044B\u043C\u0438 \u043F\u0440\u0438\u0440\u0430\u0449\u0435\u043D\u0438\u044F\u043C\u0438.");
                if (line.Has('Z')) home = !line.Optional && !line.Has('X') && !line.Has('Y');
                trace.Add("HOME;" + state.Signature("units", "cutter", "length", "H"));
                continue;
            }
            decimal motion = state.Get("motion"); bool cycle = Cycle(motion);
            bool explicitCycle = false;
            foreach (Word w in line.Words) if (w.Letter == 'G' && Cycle(w.Value)) explicitCycle = true;
            bool arc = (motion == 2 || motion == 3) && (line.Has('I') || line.Has('J') || line.Has('K'));
            if (!line.Axes && !explicitCycle && !arc) continue;
            if (motion != 0 && motion != 1 && motion != 2 && motion != 3 && !cycle)
                throw Error(name, line.Number, "\u0434\u0432\u0438\u0436\u0435\u043D\u0438\u0435 \u0431\u0435\u0437 \u044F\u0432\u043D\u043E\u0433\u043E \u0440\u0435\u0436\u0438\u043C\u0430 G00/G01/G02/G03/\u0446\u0438\u043A\u043B\u0430.");
            if (!xy)
            {
                if (!offset || !absolute || !rapid || !cancel || line.Optional || state.Get("distance") != 90 || motion != 0 ||
                    state.Get("cutter") != 40 || !line.Has('X') || !line.Has('Y') || line.Has('Z'))
                    throw Error(name, line.Number, "\u043D\u0430\u0447\u0430\u043B\u043E \u0443\u0447\u0430\u0441\u0442\u043A\u0430 \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442\u0430 \u0434\u043E\u043B\u0436\u043D\u043E \u0437\u0430\u0434\u0430\u0442\u044C G40, G90, G00, G54\u2013G59 \u0438 \u043F\u043E\u043B\u043D\u044B\u0439 \u043F\u043E\u0434\u0445\u043E\u0434 X/Y, \u0437\u0430\u0442\u0435\u043C \u043E\u0442\u0434\u0435\u043B\u044C\u043D\u044B\u0439 \u043F\u043E\u0434\u0445\u043E\u0434 Z \u0441 G43 H.");
                xy = true;
            }
            if (line.Has('Z') && !z)
            {
                if (!length || !h || line.Optional || motion != 0 || state.Get("distance") != 90 || state.Get("length") != 43 || state.Get("H") <= 0 || line.Has('X') || line.Has('Y'))
                    throw Error(name, line.Number, "\u043F\u0435\u0440\u0432\u044B\u0439 \u043F\u043E\u0434\u0445\u043E\u0434 \u043F\u043E Z \u0434\u043E\u043B\u0436\u0435\u043D \u0431\u044B\u0442\u044C \u043E\u0442\u0434\u0435\u043B\u044C\u043D\u044B\u043C \u0430\u0431\u0441\u043E\u043B\u044E\u0442\u043D\u044B\u043C G00 \u0441 \u0432\u043A\u043B\u044E\u0447\u0451\u043D\u043D\u043E\u0439 G43 H.");
                z = true;
            }
            if (cycle && previousMotion != motion && (!line.Has('Z') || !line.Has('R') || !line.Has('F')))
                throw Error(name, line.Number, "\u043F\u0435\u0440\u0432\u044B\u0439 \u043A\u0430\u0434\u0440 \u0446\u0438\u043A\u043B\u0430 \u0434\u043E\u043B\u0436\u0435\u043D \u044F\u0432\u043D\u043E \u0437\u0430\u0434\u0430\u0432\u0430\u0442\u044C Z, R \u0438 F.");
            if (motion != 0 && (!z || state.Get("F") <= 0 || state.Get("S") <= 0 || (state.Get("spindle") != 3 && state.Get("spindle") != 4)))
                throw Error(name, line.Number, "\u043F\u0435\u0440\u0435\u0434 \u043E\u0431\u0440\u0430\u0431\u043E\u0442\u043A\u043E\u0439 \u043D\u0443\u0436\u043D\u044B \u043F\u043E\u0434\u0445\u043E\u0434 \u043F\u043E Z, \u043F\u043E\u0434\u0430\u0447\u0430 F \u0438 \u0437\u0430\u043F\u0443\u0441\u043A \u0448\u043F\u0438\u043D\u0434\u0435\u043B\u044F M03/M04 \u0441 S.");
            string signature = state.Signature("motion", "distance", "plane", "units", "cutter", "path");
            if (line.Has('Z') || motion != 0) signature += state.Signature("length", "H");
            if (motion != 0) signature += state.Signature("feedmode", "F", "spindle", "S", "mist", "flood");
            if (state.Get("cutter") == 41 || state.Get("cutter") == 42) signature += state.Signature("D");
            if (cycle) signature += state.Signature("return", "R", "P", "Q");
            trace.Add(signature);
            if (line.Has('Z') || cycle) home = false;
        }
        if (!xy || !z || !home || (state.Get("cutter") != 40) || Cycle(state.Get("motion")))
            throw Error(name, from < lines.Count ? lines[from].Number : 0,
                "\u0443\u0447\u0430\u0441\u0442\u043E\u043A \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442\u0430 \u0434\u043E\u043B\u0436\u0435\u043D \u0437\u0430\u0432\u0435\u0440\u0448\u0430\u0442\u044C\u0441\u044F \u043E\u0442\u043C\u0435\u043D\u043E\u0439 \u043A\u043E\u0440\u0440\u0435\u043A\u0446\u0438\u0438/\u0446\u0438\u043A\u043B\u0430 \u0438 \u043E\u0442\u0434\u0435\u043B\u044C\u043D\u044B\u043C \u0431\u0435\u0437\u0443\u0441\u043B\u043E\u0432\u043D\u044B\u043C \u043E\u0442\u0432\u043E\u0434\u043E\u043C G91 G28 Z0; \u043E\u0434\u043D\u043E\u0433\u043E \u043E\u0442\u0432\u043E\u0434\u0430 \u0441 / \u043D\u0435\u0434\u043E\u0441\u0442\u0430\u0442\u043E\u0447\u043D\u043E.");
        return trace;
    }

    internal static byte[] Rewrite(byte[] raw, string name, int[] offsets)
    {
        if (offsets == null) return raw;
        Validate(offsets);
        if (raw == null || raw.Length == 0) throw Error(name, 0, "\u043F\u0443\u0441\u0442\u0430\u044F \u0423\u041F.");
        // Latin-1 maps bytes one-to-one, including original UTF-8/ANSI comments.
        string text = Encoding.GetEncoding(28591).GetString(raw);
        List<Line> lines = Parse(text, name);
        List<int> changes = new List<int>();
        int header = -1, ending = -1, baseOffset = -1; bool closed = false, opening = false;
        for (int i = 0; i < lines.Count; i++)
        {
            Line line = lines[i];
            if (line.Percent)
            {
                if (closed || (header < 0 && opening)) throw Error(name, line.Number, "\u043B\u0438\u0448\u043D\u0438\u0439 \u0440\u0430\u0437\u0434\u0435\u043B\u0438\u0442\u0435\u043B\u044C %.");
                if (header < 0) opening = true; else { if (ending < 0) throw Error(name, line.Number, "% \u0434\u043E \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0438\u044F \u0423\u041F."); closed = true; }
                continue;
            }
            if (line.Empty) continue;
            if (closed || ending >= 0) throw Error(name, line.Number, "\u043A\u043E\u043C\u0430\u043D\u0434\u044B \u043F\u043E\u0441\u043B\u0435 \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0438\u044F \u0423\u041F \u0438\u043B\u0438 \u043D\u0435\u0441\u043A\u043E\u043B\u044C\u043A\u043E \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C \u0432 \u0444\u0430\u0439\u043B\u0435.");
            if (header < 0)
            {
                if (line.Optional || line.Words.Count != 1 || !line.Has('O') || line.Value('O') < 1)
                    throw Error(name, line.Number, "\u043E\u0436\u0438\u0434\u0430\u0435\u0442\u0441\u044F \u043E\u0434\u0438\u043D \u0447\u0438\u0441\u043B\u043E\u0432\u043E\u0439 O-\u0437\u0430\u0433\u043E\u043B\u043E\u0432\u043E\u043A.");
                header = i; continue;
            }
            if (line.Has('O')) throw Error(name, line.Number, "\u043D\u0435\u0441\u043A\u043E\u043B\u044C\u043A\u043E O-\u0437\u0430\u0433\u043E\u043B\u043E\u0432\u043A\u043E\u0432.");
            foreach (Word w in line.Words)
                if (w.Letter == 'G' && w.Value >= 54 && w.Value <= 59)
                {
                    int current = Decimal.ToInt32(w.Value);
                    if (baseOffset >= 0 && baseOffset != current) throw Error(name, line.Number, "\u0438\u0441\u0445\u043E\u0434\u043D\u0430\u044F \u0423\u041F \u0443\u0436\u0435 \u0438\u0441\u043F\u043E\u043B\u044C\u0437\u0443\u0435\u0442 \u043D\u0435\u0441\u043A\u043E\u043B\u044C\u043A\u043E \u043F\u0440\u0438\u0432\u044F\u0437\u043E\u043A.");
                    baseOffset = current;
                }
            if (line.Has('M', 6))
            {
                if (line.Optional) throw Error(name, line.Number, "\u0443\u0441\u043B\u043E\u0432\u043D\u0430\u044F \u0441\u043C\u0435\u043D\u0430 \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442\u0430 \u043D\u0435 \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u0442\u0441\u044F.");
                int count = 0;
                foreach (Word w in line.Words)
                {
                    if (w.Letter == 'M') count++;
                    if (w.Letter != 'N' && w.Letter != 'T' && !(w.Letter == 'M' && w.Value == 6))
                        throw Error(name, line.Number, "M06 \u0434\u043E\u043B\u0436\u0435\u043D \u0431\u044B\u0442\u044C \u043E\u0442\u0434\u0435\u043B\u044C\u043D\u044B\u043C \u043A\u0430\u0434\u0440\u043E\u043C, \u0434\u043E\u043F\u0443\u0441\u043A\u0430\u044E\u0442\u0441\u044F N \u0438 T.");
                }
                if (count != 1) throw Error(name, line.Number, "\u043F\u043E\u0432\u0442\u043E\u0440\u044F\u044E\u0449\u0430\u044F\u0441\u044F \u043A\u043E\u043C\u0430\u043D\u0434\u0430 M06.");
                changes.Add(i);
            }
            if (line.Has('M', 30) || line.Has('M', 2))
            {
                if (line.Optional) throw Error(name, line.Number, "\u0443\u0441\u043B\u043E\u0432\u043D\u043E\u0435 \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0438\u0435 \u0423\u041F.");
                int count = 0;
                foreach (Word w in line.Words)
                {
                    if (w.Letter != 'N' && !(w.Letter == 'M' && (w.Value == 30 || w.Value == 2)))
                        throw Error(name, line.Number, "\u043E\u0436\u0438\u0434\u0430\u0435\u0442\u0441\u044F \u043E\u0442\u0434\u0435\u043B\u044C\u043D\u044B\u0439 \u043F\u043E\u0441\u043B\u0435\u0434\u043D\u0438\u0439 \u043A\u0430\u0434\u0440 M30/M02.");
                    if (w.Letter == 'M') count++;
                }
                if (count != 1) throw Error(name, line.Number, "\u043F\u043E\u0432\u0442\u043E\u0440\u044F\u044E\u0449\u0435\u0435\u0441\u044F \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0438\u0435 \u0423\u041F.");
                ending = i;
            }
        }
        if (header < 0 || ending < 0 || changes.Count == 0 || baseOffset < 0)
            throw Error(name, 0, "\u043D\u0443\u0436\u043D\u044B O-\u0437\u0430\u0433\u043E\u043B\u043E\u0432\u043E\u043A, \u0441\u043C\u0435\u043D\u044B \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442\u0430 M06, G54\u2013G59 \u0438 \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0438\u0435 M30/M02.");
        bool initializeFeed = NeedsInitialFeedPerMinute(lines);
        string initialFeed = "";
        if (initializeFeed)
        {
            // Match the line ending of the first M06; add no files or side data.
            int end = lines[changes[0]].End;
            string newline = text[end - 1] == '\n' ? (end > 1 && text[end - 2] == '\r' ? "\r\n" : "\n") : "\r";
            initialFeed = "G94" + newline;
        }
        foreach (bool skip in new bool[] { false, true })
        {
            State state = new State(); int at = header + 1;
            for (int section = 0; section < changes.Count; section++)
            {
                int change = changes[section], to = section + 1 < changes.Count ? changes[section + 1] : ending;
                for (; at <= change; at++)
                {
                    Line line = lines[at]; if (skip && line.Optional) continue;
                    Update(state, line);
                    if (section == 0 && line.Axes && !line.Has('G', 28))
                        throw Error(name, line.Number, "\u0434\u0432\u0438\u0436\u0435\u043D\u0438\u0435 \u0434\u043E \u043F\u0435\u0440\u0432\u043E\u0439 \u0441\u043C\u0435\u043D\u044B \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442\u0430 \u043D\u0435 \u043C\u043E\u0436\u0435\u0442 \u0431\u044B\u0442\u044C \u043F\u043E\u0432\u0442\u043E\u0440\u0435\u043D\u043E.");
                }
                if (state.Get("T") < 1) throw Error(name, lines[change].Number, "\u043D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043E\u043F\u0440\u0435\u0434\u0435\u043B\u0438\u0442\u044C T \u0434\u043B\u044F M06.");
                // Validate the exact G94 initialization emitted below after the first M06.
                if (section == 0 && initializeFeed) state.Set("feedmode", 94);
                List<string> original = Trace(lines, change + 1, to, state, skip, name);
                State repeated = state.Copy();
                List<string> again = Trace(lines, change + 1, to, repeated, skip, name);
                if (original.Count != again.Count) throw Error(name, lines[change].Number, "\u043D\u0435\u043E\u0434\u043D\u043E\u0437\u043D\u0430\u0447\u043D\u044B\u0439 \u043F\u043E\u0432\u0442\u043E\u0440 \u0443\u0447\u0430\u0441\u0442\u043A\u0430 \u0438\u043D\u0441\u0442\u0440\u0443\u043C\u0435\u043D\u0442\u0430.");
                for (int j = 0; j < original.Count; j++)
                    if (original[j] != again[j])
                        throw Error(name, lines[change].Number, "\u043F\u0440\u0438 \u043F\u043E\u0432\u0442\u043E\u0440\u0435 \u043C\u0435\u043D\u044F\u044E\u0442\u0441\u044F \u0443\u043D\u0430\u0441\u043B\u0435\u0434\u043E\u0432\u0430\u043D\u043D\u044B\u0435 \u0440\u0435\u0436\u0438\u043C\u044B \u0434\u0432\u0438\u0436\u0435\u043D\u0438\u044F, \u043A\u043E\u0440\u0440\u0435\u043A\u0446\u0438\u0438 \u0438\u043B\u0438 \u043F\u043E\u0434\u0430\u0447\u0438. \u041D\u0430\u0447\u0430\u043B\u043E \u0443\u0447\u0430\u0441\u0442\u043A\u0430 \u0434\u043E\u043B\u0436\u043D\u043E \u044F\u0432\u043D\u043E \u0432\u043E\u0441\u0441\u0442\u0430\u043D\u0430\u0432\u043B\u0438\u0432\u0430\u0442\u044C \u043D\u0435\u043E\u0431\u0445\u043E\u0434\u0438\u043C\u044B\u0435 \u0440\u0435\u0436\u0438\u043C\u044B.");
                at = to;
            }
        }
        long length = (long)raw.Length + initialFeed.Length;
        for (int i = 0; i < changes.Count; i++)
        {
            int from = lines[changes[i]].End, to = lines[i + 1 < changes.Count ? changes[i + 1] : ending].Start;
            length += (long)(to - from) * (offsets.Length - 1);
        }
        if (length > 512L * 1024 * 1024) throw Error(name, 0, "\u0440\u0435\u0437\u0443\u043B\u044C\u0442\u0430\u0442 \u043F\u043E\u0432\u0442\u043E\u0440\u0435\u043D\u0438\u044F \u043F\u0440\u0435\u0432\u044B\u0448\u0430\u0435\u0442 512 \u041C\u0411.");
        StringBuilder result = new StringBuilder((int)length);
        int position = 0;
        for (int i = 0; i < changes.Count; i++)
        {
            int from = lines[changes[i]].End, to = lines[i + 1 < changes.Count ? changes[i + 1] : ending].Start;
            Append(result, text, lines, position, from, offsets[0]);
            if (i == 0) result.Append(initialFeed);
            foreach (int offset in offsets) Append(result, text, lines, from, to, offset);
            position = to;
        }
        result.Append(text, position, text.Length - position);
        return Encoding.GetEncoding(28591).GetBytes(result.ToString());
    }
    private static void Append(StringBuilder result, string text, List<Line> lines, int from, int to, int offset)
    {
        int position = from;
        foreach (Line line in lines)
        {
            if (line.End <= from) continue;
            if (line.Start >= to) break;
            foreach (Word w in line.Words)
                if (w.Start >= from && w.Start < to && w.Letter == 'G' && w.Value >= 54 && w.Value <= 59)
                {
                    result.Append(text, position, w.Start - position).Append((53 + offset).ToString(CultureInfo.InvariantCulture));
                    position = w.Start + w.Length;
                }
        }
        result.Append(text, position, to - position);
    }
}


// Reuse the registered Windows Forms owner first; otherwise use the default context.
// Separate journal contexts can otherwise register the same Win32 window classes.
// Reflection keeps the same journal compatible with older .NET Framework NX hosts.
// Read-only ownership check: do not unregister, replace or patch host window classes.
internal static class NativeFormsOwner
{
    private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    internal static Assembly Find()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
        return Find(AppDomain.CurrentDomain.GetAssemblies(), RegisteredProcedure);
    }

    internal static Assembly Find(Assembly[] assemblies, Func<string, IntPtr> registeredProcedure)
    {
        Assembly owner = null;
        foreach (Assembly assembly in assemblies)
        {
            if (!String.Equals(assembly.GetName().Name, "System.Windows.Forms", StringComparison.Ordinal)) continue;
            if (!OwnsRegisteredClasses(assembly, registeredProcedure)) continue;
            if (owner != null && !Object.ReferenceEquals(owner, assembly))
                throw new InvalidOperationException("\u0412 \u044D\u0442\u043E\u043C \u0441\u0435\u0430\u043D\u0441\u0435 NX \u043E\u043A\u043E\u043D\u043D\u044B\u0435 \u043A\u043B\u0430\u0441\u0441\u044B \u043F\u0440\u0438\u043D\u0430\u0434\u043B\u0435\u0436\u0430\u0442 \u0440\u0430\u0437\u043D\u044B\u043C \u043A\u043E\u043F\u0438\u044F\u043C Windows Forms. \u041F\u043E\u043B\u043D\u043E\u0441\u0442\u044C\u044E \u0437\u0430\u043A\u0440\u043E\u0439\u0442\u0435 NX, \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u0435\u0433\u043E \u0437\u0430\u043D\u043E\u0432\u043E \u0438 \u0441\u043D\u0430\u0447\u0430\u043B\u0430 \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u044D\u0442\u043E\u0442 \u0441\u043A\u0440\u0438\u043F\u0442.");
            owner = assembly;
        }
        return owner;
    }

    private static FieldInfo Field(Type type, params string[] names)
    {
        foreach (string name in names)
        {
            FieldInfo field = type.GetField(name, Fields);
            if (field != null) return field;
        }
        return null;
    }

    private static bool OwnsRegisteredClasses(Assembly assembly, Func<string, IntPtr> registeredProcedure)
    {
        Type native = assembly.GetType("System.Windows.Forms.NativeWindow", false);
        Type wc = native == null ? null : native.GetNestedType("WindowClass", BindingFlags.NonPublic);
        if (wc == null) return false;
        // .NET 6/8+ and the older .NET Framework use different field spellings.
        FieldInfo cache = Field(wc, "s_cache", "cache");
        FieldInfo next = Field(wc, "_next", "next");
        FieldInfo name = Field(wc, "_windowClassName", "windowClassName");
        FieldInfo procedure = Field(wc, "_windProc", "windProc", "windowProc");
        if (cache == null || next == null || name == null || procedure == null) return false;
        object entry = cache.GetValue(null);
        HashSet<object> seen = new HashSet<object>();
        while (entry != null && seen.Add(entry))
        {
            string className = name.GetValue(entry) as string;
            Delegate callback = procedure.GetValue(entry) as Delegate;
            if (!String.IsNullOrEmpty(className) && callback != null)
            {
                IntPtr actual = registeredProcedure(className);
                // A cache entry alone is not evidence: the native registration
                // must still point to this exact assembly's managed callback.
                if (actual != IntPtr.Zero && actual == Marshal.GetFunctionPointerForDelegate(callback)) return true;
            }
            entry = next.GetValue(entry);
        }
        return false;
    }

    private static IntPtr RegisteredProcedure(string className)
    {
        WindowClassInfo info;
        return GetClassInfoW(GetModuleHandleW(null), className, out info) ? info.Procedure : IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClassInfo
    {
        internal uint Style;
        internal IntPtr Procedure;
        internal int ClassExtra, WindowExtra;
        internal IntPtr Instance, Icon, Cursor, Background, MenuName, ClassName;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClassInfoW(IntPtr instance, string className, out WindowClassInfo info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr GetModuleHandleW(string moduleName);
}


internal static class SharedFormsAssembly
{
    internal static Assembly Load()
    {
        Assembly registeredOwner = NativeFormsOwner.Find();
        if (registeredOwner != null)
        {
            // Keep the callback-owning assembly alive for the NX process. A
            // journal may unload, but its native class callbacks must not vanish.
            // Only an Assembly reference is stored; no journal UI or files.
            AppDomain.CurrentDomain.SetData("NX_Postprocess_To_Machine.FormsOwner", registeredOwner);
            return registeredOwner;
        }
        AssemblyName name = new AssemblyName("System.Windows.Forms");
        Type contextType = typeof(object).Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext", false);
        if (contextType == null)
        {
            // .NET Framework: one Forms instance per current AppDomain, as before.
            foreach (Assembly existing in AppDomain.CurrentDomain.GetAssemblies())
                if (String.Equals(existing.GetName().Name, name.Name, StringComparison.Ordinal)) return existing;
            return Assembly.Load(name);
        }

        object sharedContext = contextType.GetProperty("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
        MethodInfo getContext = contextType.GetMethod("GetLoadContext", BindingFlags.Public | BindingFlags.Static,
            null, new Type[] { typeof(Assembly) }, null);
        foreach (Assembly existing in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!String.Equals(existing.GetName().Name, name.Name, StringComparison.Ordinal)) continue;
            if (Object.ReferenceEquals(getContext.Invoke(null, new object[] { existing }), sharedContext)) return existing;
        }

        // Never use Assembly.Load here: from an NX journal it may bind in that
        // journal's private context. Never fall back to an arbitrary loaded copy.
        MethodInfo load = contextType.GetMethod("LoadFromAssemblyName", BindingFlags.Public | BindingFlags.Instance,
            null, new Type[] { typeof(AssemblyName) }, null);
        Assembly result;
        try { result = (Assembly)load.Invoke(sharedContext, new object[] { name }); }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0437\u0430\u0433\u0440\u0443\u0437\u0438\u0442\u044C \u043E\u0431\u0449\u0443\u044E \u043E\u043A\u043E\u043D\u043D\u0443\u044E \u0431\u0438\u0431\u043B\u0438\u043E\u0442\u0435\u043A\u0443 NX. \u041F\u043E\u043B\u043D\u043E\u0441\u0442\u044C\u044E \u0437\u0430\u043A\u0440\u043E\u0439\u0442\u0435 NX \u0438 \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u0435\u0433\u043E \u0437\u0430\u043D\u043E\u0432\u043E.", ex.InnerException ?? ex);
        }
        if (!Object.ReferenceEquals(getContext.Invoke(null, new object[] { result }), sharedContext))
            throw new InvalidOperationException("NX \u0432\u0435\u0440\u043D\u0443\u043B \u043E\u0442\u0434\u0435\u043B\u044C\u043D\u0443\u044E \u043A\u043E\u043F\u0438\u044E \u043E\u043A\u043E\u043D\u043D\u043E\u0439 \u0431\u0438\u0431\u043B\u0438\u043E\u0442\u0435\u043A\u0438 \u0432\u043C\u0435\u0441\u0442\u043E \u043E\u0431\u0449\u0435\u0439. \u041F\u043E\u043B\u043D\u043E\u0441\u0442\u044C\u044E \u0437\u0430\u043A\u0440\u043E\u0439\u0442\u0435 NX \u0438 \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u0435\u0433\u043E \u0437\u0430\u043D\u043E\u0432\u043E.");
        return result;
    }
}


internal static class ScriptInfo
{
    internal const string SCRIPT_VERSION = "V1.40";
    internal const string SCRIPT_NAME = "\u041F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u0435";

    internal static string WindowTitle(string detail)
    {
        string title = SCRIPT_NAME + " \u2014 " + SCRIPT_VERSION;
        return String.IsNullOrEmpty(detail) ? title : title + " \u2014 " + detail;
    }
}

// The following code has no NX/UI dependency and is tested separately.
internal static class ToolDiameterNames
{
    // D10, D10ST, D0.5/D0,5, D=10, and common diameter symbols.
    // The left boundary allows underscores but rejects D within words/3D.
    // Capture the entire numeric candidate so malformed fractions/decimals fail closed.
    private static readonly Regex Diameter = new Regex(
        "(?<![\\p{L}\\p{Nd}])(?:D|\u00D8|\u2300|\u0424)\\s*(?:[=:]\\s*)?(?<value>[+-]?(?:[0-9]+|[.,][0-9]+)(?:[.,/][0-9]+)*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    internal static bool TryRead(string name, out double diameter)
    {
        diameter = 0;
        MatchCollection matches = Diameter.Matches(name ?? "");
        if (matches.Count == 0) return false; // No diameter stated: nothing to compare.
        bool first = true;
        foreach (Match match in matches)
        {
            double value;
            if (!Double.TryParse(match.Groups["value"].Value.Replace(',', '.'),
                    NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) ||
                !IsPositiveFinite(value))
                throw new FormatException("\u041D\u0435\u043A\u043E\u0440\u0440\u0435\u043A\u0442\u043D\u044B\u0439 \u0434\u0438\u0430\u043C\u0435\u0442\u0440 \u0432 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u0438: " + match.Value);
            if (!first && !Equal(diameter, value))
                throw new FormatException("\u0412 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u0438 \u0443\u043A\u0430\u0437\u0430\u043D\u043E \u043D\u0435\u0441\u043A\u043E\u043B\u044C\u043A\u043E \u0440\u0430\u0437\u043D\u044B\u0445 \u0434\u0438\u0430\u043C\u0435\u0442\u0440\u043E\u0432.");
            diameter = value; first = false;
        }
        return true;
    }
    internal static bool IsPositiveFinite(double value)
    { return value > 0 && !Double.IsNaN(value) && !Double.IsInfinity(value); }
    internal static bool Equal(double named, double actual)
    { return Math.Abs(named - actual) <= 0.000001; }
    internal static string Format(double value)
    { return value.ToString("G15", CultureInfo.InvariantCulture); }
}

internal static class ProgramPostAssignments
{
    internal static PostDefinition[] Repeat(PostDefinition post, int count)
    {
        if (post == null || count < 1) throw new ArgumentException("\u041D\u0435 \u0432\u044B\u0431\u0440\u0430\u043D \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440 \u0438\u043B\u0438 \u0441\u043F\u0438\u0441\u043E\u043A \u0423\u041F \u043F\u0443\u0441\u0442.");
        PostDefinition[] result = new PostDefinition[count];
        for (int i = 0; i < count; i++) result[i] = post;
        return result;
    }
    private static void CheckCount(PostDefinition[] posts, int count)
    {
        if (posts == null || count < 1 || posts.Length != count)
            throw new ArgumentException("\u041A\u0430\u0436\u0434\u043E\u0439 \u0423\u041F \u0434\u043E\u043B\u0436\u0435\u043D \u0431\u044B\u0442\u044C \u043D\u0430\u0437\u043D\u0430\u0447\u0435\u043D \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440.");
        for (int i = 0; i < posts.Length; i++)
            if (posts[i] == null) throw new ArgumentException("\u041D\u0435 \u043D\u0430\u0437\u043D\u0430\u0447\u0435\u043D \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440 \u0434\u043B\u044F \u0423\u041F \u2116" + (i + 1) + ".");
    }
    internal static void Validate(PostDefinition[] posts, int count)
    {
        CheckCount(posts, count);
        HashSet<string> validated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PostDefinition post in posts) if (validated.Add(post.Key)) post.Validate();
    }
    internal static string[] Extensions(PostDefinition[] posts, int count, string defaultExtension)
    {
        CheckCount(posts, count);
        string[] result = new string[count];
        for (int i = 0; i < count; i++) result[i] = PostFiles.Extension(posts[i].DefaultExtension ?? defaultExtension);
        return result;
    }
    internal static string Summary(PostDefinition[] posts)
    {
        List<string> names = new List<string>();
        foreach (PostDefinition post in posts) if (!names.Contains(post.Name)) names.Add(post.Name);
        return (names.Count == 1 ? "\u041F\u043E\u0441\u0442: " : "\u041F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u044B: ") + String.Join(", ", names.ToArray());
    }
    internal static string Describe(string[] names, PostDefinition[] posts, string[] extensions)
    {
        List<string> lines = new List<string>();
        for (int i = 0; i < names.Length; i++) lines.Add(names[i] + extensions[i] + " \u2192 " + posts[i].Name);
        return String.Join("\n", lines.ToArray());
    }
}

public sealed class PostDefinition
{
    public string Name;
    public string EventFile;
    public string DefinitionFile;
    public string DefaultExtension;
    public PostDefinition(string name, string eventFile, string definitionFile)
    { Name = name; EventFile = eventFile; DefinitionFile = definitionFile; }
    public string Key { get { return (EventFile + "|" + DefinitionFile).ToUpperInvariant(); } }
    public void Validate()
    {
        if (!File.Exists(EventFile)) throw new FileNotFoundException("\u041D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D TCL-\u0444\u0430\u0439\u043B \u043F\u043E\u0441\u0442\u0430 \u00AB" + Name + "\u00BB:\u000A" + EventFile);
        if (!File.Exists(DefinitionFile)) throw new FileNotFoundException("\u041D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D DEF-\u0444\u0430\u0439\u043B \u043F\u043E\u0441\u0442\u0430 \u00AB" + Name + "\u00BB:\u000A" + DefinitionFile);
    }
}

public static class PostCatalog
{
    public static string FindRegistry(Func<string, string> environment)
    {
        string configured = environment("UGII_CAM_POST_CONFIG_FILE");
        if (!String.IsNullOrWhiteSpace(configured)) return Expand(configured, environment);
        string directory = environment("UGII_CAM_POST_DIR");
        if (!String.IsNullOrWhiteSpace(directory)) return IOPath.Combine(Expand(directory, environment), "template_post.dat");
        string baseDirectory = environment("UGII_BASE_DIR");
        if (!String.IsNullOrWhiteSpace(baseDirectory))
        {
            string standard = IOPath.Combine(Expand(baseDirectory, environment), @"MACH\resource\postprocessor\template_post.dat");
            if (File.Exists(standard)) return standard;
        }
        return "";
    }

    public static string Expand(string value, Func<string, string> environment)
    {
        string result = value.Trim().Trim('"');
        for (int attempt = 0; attempt < 12; attempt++)
        {
            string previous = result;
            result = Regex.Replace(result, @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}|%([A-Za-z_][A-Za-z0-9_]*)%|\$([A-Za-z_][A-Za-z0-9_]*)",
                delegate(Match match)
                {
                    string key = match.Groups[1].Success ? match.Groups[1].Value :
                        (match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value);
                    string replacement = environment(key);
                    return String.IsNullOrEmpty(replacement) ? match.Value : replacement;
                });
            if (result == previous) break;
        }
        return result;
    }

    public static List<string> Csv(string line)
    {
        List<string> fields = new List<string>();
        StringBuilder field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { fields.Add(field.ToString().Trim()); field.Length = 0; }
            else field.Append(c);
        }
        if (quoted) throw new FormatException("\u041D\u0435\u0437\u0430\u043A\u0440\u044B\u0442\u0430\u044F \u043A\u0430\u0432\u044B\u0447\u043A\u0430 \u0432 \u0441\u043F\u0438\u0441\u043A\u0435 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u043E\u0432.");
        fields.Add(field.ToString().Trim());
        return fields;
    }

    public static List<PostDefinition> Read(string path, Func<string, string> environment)
    {
        string text;
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && ((bytes[0] == 255 && bytes[1] == 254) || (bytes[0] == 254 && bytes[1] == 255)))
        { using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true)) text = reader.ReadToEnd(); }
        else
        {
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { text = Encoding.Default.GetString(bytes); }
        }
        List<PostDefinition> posts = new List<PostDefinition>();
        HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string directory = IOPath.GetDirectoryName(IOPath.GetFullPath(path));
        string[] lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string input in lines)
        {
            string line = input.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
            List<string> fields = Csv(line);
            if (fields.Count < 3 || fields[0].Length == 0 || fields[1].Length == 0 || fields[2].Length == 0)
                throw new FormatException("\u041D\u0435\u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u043C\u0430\u044F \u0441\u0442\u0440\u043E\u043A\u0430 \u0441\u043F\u0438\u0441\u043A\u0430 \u043F\u043E\u0441\u0442\u043E\u0432:\u000A" + line);
            string tcl = Resolve(fields[1], directory, environment);
            string def = Resolve(fields[2], directory, environment);
            PostDefinition post = new PostDefinition(fields[0], tcl, def);
            if (keys.Add(post.Key)) posts.Add(post);
        }
        return posts;
    }

    private static string Resolve(string value, string directory, Func<string, string> environment)
    {
        string expanded = Expand(value, environment);
        if (Regex.IsMatch(expanded, @"\$\{|%[A-Za-z_][A-Za-z0-9_]*%|\$[A-Za-z_]")) return expanded;
        return IOPath.GetFullPath(IOPath.IsPathRooted(expanded) ? expanded : IOPath.Combine(directory, expanded));
    }
}

// INI is authoritative; the post-path editor preserves unrelated sections.
internal sealed class PostPathEntry
{
    internal string Name = "", Tcl = "", Def = "", Extension;
    internal IniSection Original;
}

// Edits only explicit post sections. Other sections and untouched lines are preserved.
internal sealed class MachineRootEntry
{
    internal string Name = "", DirectoryPath = "";
    internal IniEntry Original;
}

internal sealed class PostIniDocument
{
    internal readonly string Path;
    internal readonly List<PostPathEntry> Posts = new List<PostPathEntry>();
    internal readonly List<MachineRootEntry> MachineRoots = new List<MachineRootEntry>();
    internal List<MachineTarget> EditedMachines;
    internal List<string> MachineWarnings;
    private readonly byte[] originalBytes;
    private readonly string originalText, newline;
    private readonly Encoding encoding;
    private readonly List<IniSection> sections;

    internal PostIniDocument(string path)
    {
        Path = IOPath.GetFullPath(path);
        if (File.Exists(Path))
        {
            originalBytes = File.ReadAllBytes(Path);
            int offset = 0;
            if (originalBytes.Length >= 2 && originalBytes[0] == 255 && originalBytes[1] == 254)
            { encoding = new UnicodeEncoding(false, true, true); offset = 2; }
            else if (originalBytes.Length >= 2 && originalBytes[0] == 254 && originalBytes[1] == 255)
            { encoding = new UnicodeEncoding(true, true, true); offset = 2; }
            else
            {
                bool bom = originalBytes.Length >= 3 && originalBytes[0] == 239 && originalBytes[1] == 187 && originalBytes[2] == 191;
                encoding = new UTF8Encoding(bom, true); if (bom) offset = 3;
            }
            try { originalText = encoding.GetString(originalBytes, offset, originalBytes.Length - offset); }
            catch (DecoderFallbackException) { throw new FormatException("\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u0435 INI \u0432 UTF-8 \u0438\u043B\u0438 UTF-16 \u0441 BOM:\n" + Path); }
        }
        else { encoding = new UTF8Encoding(true, true); originalText = "[Settings]\r\nPostList=auto\r\n"; }
        Match ending = Regex.Match(originalText, "\\r\\n|\\r|\\n");
        newline = ending.Success ? ending.Value : "\r\n";
        sections = IniReader.Parse(originalText, Path);
        foreach (IniSection section in sections)
        {
            if (String.Equals(section.Name, "MachineRoots", StringComparison.OrdinalIgnoreCase))
                foreach (IniEntry entry in section.Entries)
                    MachineRoots.Add(new MachineRootEntry { Name = entry.Key, DirectoryPath = entry.Value, Original = entry });
            if (!section.Name.StartsWith("Post ", StringComparison.OrdinalIgnoreCase)) continue;
            Posts.Add(new PostPathEntry { Name = section.Name.Substring(5).Trim(),
                Tcl = Value(section, "Tcl"), Def = Value(section, "Def"),
                Extension = section.Find("Extension") == null ? null : Value(section, "Extension"), Original = section });
        }
    }

    internal string UniqueMachineRootName(string name)
    {
        if (String.IsNullOrWhiteSpace(name)) name = "\u041F\u0430\u043F\u043A\u0430 \u0441\u0442\u0430\u043D\u043A\u043E\u0432";
        string result = name; int suffix = 2;
        while (MachineRoots.Exists(delegate(MachineRootEntry p) { return String.Equals(p.Name.Trim(), result, StringComparison.OrdinalIgnoreCase); }))
            result = name + "_" + (suffix++).ToString(CultureInfo.InvariantCulture);
        return result;
    }

    internal string BuildMachineRootsText(Func<string, string> environment)
    {
        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MachineRootEntry root in MachineRoots)
        {
            root.Name = root.Name.Trim(); root.DirectoryPath = root.DirectoryPath.Trim();
            if (root.DirectoryPath.Length >= 2 && root.DirectoryPath.StartsWith("\"") && root.DirectoryPath.EndsWith("\""))
                root.DirectoryPath = root.DirectoryPath.Substring(1, root.DirectoryPath.Length - 2).Trim();
            CheckText(root.Name, "\u043D\u0430\u0437\u0432\u0430\u043D\u0438\u0435 \u043F\u0430\u043F\u043A\u0438 \u0441\u0442\u0430\u043D\u043A\u043E\u0432", true);
            if (root.Name.IndexOf('=') >= 0 || root.Name.StartsWith(";") || root.Name.StartsWith("#"))
                throw new ArgumentException("\u041D\u0435\u0434\u043E\u043F\u0443\u0441\u0442\u0438\u043C\u043E\u0435 \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u0435 \u043F\u0430\u043F\u043A\u0438 \u0441\u0442\u0430\u043D\u043A\u043E\u0432: " + root.Name);
            CheckText(root.DirectoryPath, "\u043F\u0443\u0442\u044C \u043A \u043F\u0430\u043F\u043A\u0435 \u00AB" + root.Name + "\u00BB", false);
            if (!names.Add(root.Name)) throw new ArgumentException("\u041F\u043E\u0432\u0442\u043E\u0440\u044F\u0435\u0442\u0441\u044F \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u0435 \u00AB" + root.Name + "\u00BB.");
            // Keep an unchanged offline root; validate newly chosen paths without creating files.
            if (root.Original == null || root.DirectoryPath != root.Original.Value)
            {
                string resolved = RouterConfig.ResolvePath(root.DirectoryPath, IOPath.GetDirectoryName(Path), environment);
                PostFiles.Machines(resolved);
            }
        }
        if (originalBytes == null && MachineRoots.Count == 0) throw new ArgumentException("\u0414\u043E\u0431\u0430\u0432\u044C\u0442\u0435 \u043F\u0430\u043F\u043A\u0443 \u0441\u043E \u0441\u0442\u0430\u043D\u043A\u0430\u043C\u0438 \u043F\u0435\u0440\u0435\u0434 \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u0435\u043C.");
        List<string> lines = new List<string>();
        foreach (Match line in Regex.Matches(originalText, @"[^\r\n]*(?:\r\n|\r|\n|$)"))
            if (line.Length > 0) lines.Add(line.Value);
        IniSection section = sections.Find(delegate(IniSection s) { return String.Equals(s.Name, "MachineRoots", StringComparison.OrdinalIgnoreCase); });
        Dictionary<int, string> edits = new Dictionary<int, string>();
        int insertion = lines.Count;
        if (section != null)
        {
            int index = sections.IndexOf(section);
            if (index + 1 < sections.Count) insertion = sections[index + 1].Line - 1;
            foreach (IniEntry entry in section.Entries)
            {
                MachineRootEntry draft = MachineRoots.Find(delegate(MachineRootEntry p) { return p.Original == entry; });
                if (draft == null) { edits[entry.Line - 1] = ""; continue; }
                if (draft.Name == entry.Key && draft.DirectoryPath == entry.Value) continue;
                string oldLine = lines[entry.Line - 1];
                string prefix = draft.Name == entry.Key ? oldLine.Substring(0, oldLine.IndexOf('=') + 1) : draft.Name + "=";
                edits[entry.Line - 1] = ReplaceLine(oldLine, prefix + draft.DirectoryPath);
            }
        }
        StringBuilder added = new StringBuilder();
        foreach (MachineRootEntry root in MachineRoots)
            if (root.Original == null) added.Append(root.Name + "=" + root.DirectoryPath + newline);
        StringBuilder result = new StringBuilder();
        for (int i = 0; i <= lines.Count; i++)
        {
            if (i == insertion && added.Length > 0)
            {
                EnsureLineEnd(result);
                if (section == null) result.Append(newline + "[MachineRoots]" + newline);
                result.Append(added);
            }
            if (i < lines.Count) { string edit; result.Append(edits.TryGetValue(i, out edit) ? edit : lines[i]); }
        }
        string text = result.ToString();
        RouterConfig candidate = RouterConfig.Parse(text, Path, environment);
        EditedMachines = candidate.GetAvailableMachines(out MachineWarnings); // Detect ambiguous button names before saving.
        return text;
    }

    internal void SaveMachineRoots(Func<string, string> environment)
    { SaveText(BuildMachineRootsText(environment)); }

    private static string Value(IniSection section, string key)
    { IniEntry entry = section.Find(key); return entry == null ? "" : entry.Value; }

    internal string UniqueName(string name)
    {
        string result = name; int suffix = 2;
        while (Posts.Exists(delegate(PostPathEntry p) { return String.Equals(p.Name.Trim(), result, StringComparison.OrdinalIgnoreCase); }))
            result = name + "_" + (suffix++).ToString(CultureInfo.InvariantCulture);
        return result;
    }

    internal static string CompanionDef(string tcl)
    { string path = IOPath.ChangeExtension(tcl, ".def"); return File.Exists(path) ? path : ""; }

    private static void CheckText(string value, string description, bool name)
    {
        if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("\u0423\u043A\u0430\u0436\u0438\u0442\u0435 " + description + ".");
        foreach (char c in value)
            if (Char.IsControl(c) || c == '\uFEFF' || (name && (c == '[' || c == ']')))
                throw new ArgumentException("\u041D\u0435\u0434\u043E\u043F\u0443\u0441\u0442\u0438\u043C\u044B\u0439 \u0441\u0438\u043C\u0432\u043E\u043B: " + description + ".");
    }

    internal string BuildText(Func<string, string> environment)
    {
        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PostPathEntry post in Posts)
        {
            post.Name = post.Name.Trim(); post.Tcl = post.Tcl.Trim(); post.Def = post.Def.Trim();
            CheckText(post.Name, "\u043D\u0430\u0437\u0432\u0430\u043D\u0438\u0435 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430", true);
            CheckText(post.Tcl, "TCL \u0434\u043B\u044F \u00AB" + post.Name + "\u00BB", false);
            CheckText(post.Def, "DEF \u0434\u043B\u044F \u00AB" + post.Name + "\u00BB", false);
            if (!names.Add(post.Name)) throw new ArgumentException("\u041F\u043E\u0432\u0442\u043E\u0440\u044F\u0435\u0442\u0441\u044F \u043D\u0430\u0437\u0432\u0430\u043D\u0438\u0435 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0430 \u00AB" + post.Name + "\u00BB.");
        }
        if (originalBytes == null && Posts.Count == 0) throw new ArgumentException("\u0414\u043E\u0431\u0430\u0432\u044C\u0442\u0435 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440 \u043F\u0435\u0440\u0435\u0434 \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u0435\u043C.");
        List<string> lines = new List<string>();
        foreach (Match line in Regex.Matches(originalText, @"[^\r\n]*(?:\r\n|\r|\n|$)"))
            if (line.Length > 0) lines.Add(line.Value);
        Dictionary<int, string> edits = new Dictionary<int, string>();
        Dictionary<int, string> insertions = new Dictionary<int, string>();
        for (int s = 0; s < sections.Count; s++)
        {
            IniSection section = sections[s];
            if (!section.Name.StartsWith("Post ", StringComparison.OrdinalIgnoreCase)) continue;
            PostPathEntry draft = Posts.Find(delegate(PostPathEntry p) { return p.Original == section; });
            if (draft == null)
            {
                edits[section.Line - 1] = "";
                foreach (IniEntry entry in section.Entries) edits[entry.Line - 1] = "";
                continue;
            }
            if (draft.Name != section.Name.Substring(5).Trim())
                edits[section.Line - 1] = ReplaceLine(lines[section.Line - 1], "[Post " + draft.Name + "]");
            int end = s + 1 < sections.Count ? sections[s + 1].Line - 1 : lines.Count;
            UpdateValue(section, "Tcl", draft.Tcl, end, lines, edits, insertions);
            UpdateValue(section, "Def", draft.Def, end, lines, edits, insertions);
        }
        StringBuilder result = new StringBuilder();
        for (int i = 0; i <= lines.Count; i++)
        {
            string insertion;
            if (insertions.TryGetValue(i, out insertion)) { EnsureLineEnd(result); result.Append(insertion); }
            if (i < lines.Count) { string edit; result.Append(edits.TryGetValue(i, out edit) ? edit : lines[i]); }
        }
        foreach (PostPathEntry post in Posts)
        {
            if (post.Original != null) continue;
            EnsureLineEnd(result); result.Append(newline);
            result.Append("[Post " + post.Name + "]" + newline + "Tcl=" + post.Tcl + newline + "Def=" + post.Def + newline);
            if (post.Extension != null) result.Append("Extension=" + post.Extension + newline);
        }
        string text = result.ToString();
        RouterConfig candidate = RouterConfig.Parse(text, Path, environment);
        foreach (PostDefinition post in candidate.Posts) post.Validate();
        return text;
    }

    private void UpdateValue(IniSection section, string key, string value, int end, List<string> lines,
        Dictionary<int, string> edits, Dictionary<int, string> insertions)
    {
        IniEntry entry = section.Find(key);
        if (entry != null)
        {
            if (entry.Value == value) return;
            string line = lines[entry.Line - 1];
            edits[entry.Line - 1] = ReplaceLine(line, line.Substring(0, line.IndexOf('=') + 1) + value);
        }
        else
        {
            string before; insertions.TryGetValue(end, out before);
            insertions[end] = (before ?? "") + key + "=" + value + newline;
        }
    }
    private static string ReplaceLine(string original, string body)
    { return body + (original.EndsWith("\r\n") ? "\r\n" : original.EndsWith("\n") ? "\n" : original.EndsWith("\r") ? "\r" : ""); }
    private void EnsureLineEnd(StringBuilder text)
    { if (text.Length > 0 && text[text.Length - 1] != '\r' && text[text.Length - 1] != '\n') text.Append(newline); }
    private static bool Same(byte[] left, byte[] right)
    {
        if (left == null || right == null) return left == right;
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }

    internal void Save(Func<string, string> environment)
    { SaveText(BuildText(environment)); }

    private void SaveText(string text)
    {
        byte[] preamble = encoding.GetPreamble(), body = encoding.GetBytes(text);
        byte[] bytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length); Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
        if (Same(bytes, originalBytes))
        {
            if (!Same(File.ReadAllBytes(Path), originalBytes))
                throw new IOException("INI \u0438\u0437\u043C\u0435\u043D\u0451\u043D \u0434\u0440\u0443\u0433\u0438\u043C \u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u043C. \u0417\u0430\u043A\u0440\u043E\u0439\u0442\u0435 \u043D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438 \u0438 \u043E\u0442\u043A\u0440\u043E\u0439\u0442\u0435 \u0438\u0445 \u0441\u043D\u043E\u0432\u0430.");
            return;
        }
        if (File.Exists(Path) && (File.GetAttributes(Path) & FileAttributes.ReadOnly) != 0)
            throw new IOException("INI \u0434\u043E\u0441\u0442\u0443\u043F\u0435\u043D \u0442\u043E\u043B\u044C\u043A\u043E \u0434\u043B\u044F \u0447\u0442\u0435\u043D\u0438\u044F. \u0420\u0430\u0437\u0440\u0435\u0448\u0438\u0442\u0435 \u0437\u0430\u043F\u0438\u0441\u044C \u0432 \u0444\u0430\u0439\u043B:\n" + Path);
        // No sidecar, backup or temporary file: validate first, then write this INI under an exclusive writer lock.
        bool created = false;
        try
        {
            using (FileStream stream = new FileStream(Path, originalBytes == null ? FileMode.CreateNew : FileMode.Open,
                FileAccess.ReadWrite, FileShare.Read))
            {
                created = originalBytes == null;
                byte[] current = new byte[checked((int)stream.Length)]; int read = 0;
                while (read < current.Length) { int n = stream.Read(current, read, current.Length - read); if (n == 0) throw new EndOfStreamException(); read += n; }
                if (originalBytes != null && !Same(current, originalBytes))
                    throw new IOException("INI \u0438\u0437\u043C\u0435\u043D\u0451\u043D \u0434\u0440\u0443\u0433\u0438\u043C \u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u043C. \u0417\u0430\u043A\u0440\u043E\u0439\u0442\u0435 \u043D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438, \u043D\u0430\u0436\u043C\u0438\u0442\u0435 \u00AB\u041E\u0431\u043D\u043E\u0432\u0438\u0442\u044C\u00BB \u0438 \u043E\u0442\u043A\u0440\u043E\u0439\u0442\u0435 \u0438\u0445 \u0441\u043D\u043E\u0432\u0430.");
                try
                {
                    stream.Position = 0; stream.Write(bytes, 0, bytes.Length); stream.SetLength(bytes.Length); stream.Flush(true);
                }
                catch (Exception writeError)
                {
                    try { stream.Position = 0; stream.Write(current, 0, current.Length); stream.SetLength(current.Length); stream.Flush(true); }
                    catch (Exception restoreError) { throw new IOException("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0437\u0430\u043F\u0438\u0441\u0430\u0442\u044C \u0438 \u0432\u043E\u0441\u0441\u0442\u0430\u043D\u043E\u0432\u0438\u0442\u044C INI:\n" + Path, new AggregateException(writeError, restoreError)); }
                    throw;
                }
            }
        }
        catch
        {
            if (created) { try { File.Delete(Path); } catch { } }
            throw;
        }
    }
}

public sealed class IniEntry
{
    public string Key;
    public string Value;
    public int Line;
    public IniEntry(string key, string value, int line) { Key = key; Value = value; Line = line; }
}

public sealed class IniSection
{
    public string Name;
    public int Line;
    public readonly List<IniEntry> Entries = new List<IniEntry>();
    public IniSection(string name, int line) { Name = name; Line = line; }
    public IniEntry Find(string key)
    {
        foreach (IniEntry entry in Entries)
            if (String.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)) return entry;
        return null;
    }
}

public static class IniReader
{
    public static List<IniSection> Read(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("\u041D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D \u0444\u0430\u0439\u043B \u043D\u0430\u0441\u0442\u0440\u043E\u0435\u043A. \u041F\u043E\u043B\u043E\u0436\u0438\u0442\u0435 INI \u0440\u044F\u0434\u043E\u043C \u0441\u043E \u0441\u043A\u0440\u0438\u043F\u0442\u043E\u043C:\u000A" + path);
        byte[] bytes = File.ReadAllBytes(path);
        string text;
        try
        {
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254)
                text = new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2);
            else if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255)
                text = new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2);
            else text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        { throw new FormatException("\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u0435 INI \u0432 \u043A\u043E\u0434\u0438\u0440\u043E\u0432\u043A\u0435 UTF-8 \u0438\u043B\u0438 UTF-16 \u0441 BOM:\u000A" + path); }
        return Parse(text, path);
    }

    public static List<IniSection> Parse(string text, string path)
    {
        List<IniSection> sections = new List<IniSection>();
        IniSection current = null;
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim().TrimStart('\uFEFF');
            int number = i + 1;
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
            if (line[0] == '[')
            {
                if (!line.EndsWith("]")) throw Error(path, number, "\u041D\u0435\u0437\u0430\u043A\u0440\u044B\u0442\u0430\u044F \u0441\u0435\u043A\u0446\u0438\u044F [\u0438\u043C\u044F].");
                string name = line.Substring(1, line.Length - 2).Trim();
                if (name.Length == 0 || name.IndexOfAny(new char[] { '[', ']', '\0' }) >= 0)
                    throw Error(path, number, "\u041D\u0435\u0434\u043E\u043F\u0443\u0441\u0442\u0438\u043C\u043E\u0435 \u0438\u043C\u044F \u0441\u0435\u043A\u0446\u0438\u0438.");
                foreach (IniSection section in sections)
                    if (String.Equals(section.Name, name, StringComparison.OrdinalIgnoreCase))
                        throw Error(path, number, "\u041F\u043E\u0432\u0442\u043E\u0440\u043D\u0430\u044F \u0441\u0435\u043A\u0446\u0438\u044F [" + name + "].");
                current = new IniSection(name, number);
                sections.Add(current);
                continue;
            }
            int equals = line.IndexOf('=');
            if (current == null || equals <= 0)
                throw Error(path, number, "\u041E\u0436\u0438\u0434\u0430\u0435\u0442\u0441\u044F \u0441\u0442\u0440\u043E\u043A\u0430 \u043A\u043B\u044E\u0447=\u0437\u043D\u0430\u0447\u0435\u043D\u0438\u0435 \u0432\u043D\u0443\u0442\u0440\u0438 \u0441\u0435\u043A\u0446\u0438\u0438.");
            string key = line.Substring(0, equals).Trim();
            if (key.Length == 0 || key.IndexOfAny(new char[] { '[', ']', '\0' }) >= 0)
                throw Error(path, number, "\u041D\u0435\u0434\u043E\u043F\u0443\u0441\u0442\u0438\u043C\u043E\u0435 \u0438\u043C\u044F \u043F\u0430\u0440\u0430\u043C\u0435\u0442\u0440\u0430.");
            if (current.Find(key) != null) throw Error(path, number, "\u041F\u043E\u0432\u0442\u043E\u0440\u043D\u044B\u0439 \u043F\u0430\u0440\u0430\u043C\u0435\u0442\u0440 " + key + ".");
            string value = line.Substring(equals + 1).Trim();
            if (value.StartsWith("\"") || value.EndsWith("\""))
            {
                if (value.Length < 2 || !value.StartsWith("\"") || !value.EndsWith("\""))
                    throw Error(path, number, "\u041D\u0435\u0437\u0430\u043A\u0440\u044B\u0442\u0430\u044F \u043A\u0430\u0432\u044B\u0447\u043A\u0430 \u0432 \u0437\u043D\u0430\u0447\u0435\u043D\u0438\u0438.");
                value = value.Substring(1, value.Length - 2);
            }
            if (value.IndexOf('\0') >= 0) throw Error(path, number, "\u041D\u0435\u0434\u043E\u043F\u0443\u0441\u0442\u0438\u043C\u044B\u0439 \u043D\u0443\u043B\u0435\u0432\u043E\u0439 \u0441\u0438\u043C\u0432\u043E\u043B.");
            current.Entries.Add(new IniEntry(key, value, number));
        }
        return sections;
    }

    public static FormatException Error(string path, int line, string message)
    { return new FormatException("INI, \u0441\u0442\u0440\u043E\u043A\u0430 " + line.ToString(CultureInfo.InvariantCulture) + ": " + message + "\n" + path); }
}

// Read-only discovery: no WMI, PowerShell, elevation or probe files.
internal interface IExternalDriveProbe
{
    string[] GetRoots();
    ExternalDriveInfo Read(string root);
}

// Keep these values local: NX may not reference System.IO.FileSystem.DriveInfo.
internal enum ExternalDriveKind
{
    Unknown = 0, NoRootDirectory = 1, Removable = 2, Fixed = 3,
    Network = 4, CDRom = 5, Ram = 6
}

internal sealed class ExternalDriveInfo
{
    internal string Root, Label = "", VolumeId = "";
    internal uint Serial;
    internal ExternalDriveKind Type;
    internal int Bus = -1;
    internal bool Ready, SystemVolume, ReadOnly;
    internal long FreeBytes, TotalBytes;

    internal bool IsExternal
    {
        get
        {
            if (SystemVolume || (Type != ExternalDriveKind.Removable && Type != ExternalDriveKind.Fixed)) return false;
            // USB SSD/HDD can report Fixed; ExternalDriveKind alone is insufficient.
            return Type == ExternalDriveKind.Removable || Bus == 7 || Bus == 4 || Bus == 12 || Bus == 13;
        }
    }
    internal string Caption
    {
        get { return (String.IsNullOrWhiteSpace(Label) ? "\u0411\u0435\u0437 \u043C\u0435\u0442\u043A\u0438" : Label.Trim()) + " (" + Root.Substring(0, 2) + ")"; }
    }
    internal string Details
    {
        get
        {
            return Caption + "\n\u0421\u0432\u043E\u0431\u043E\u0434\u043D\u043E " + SizeText(FreeBytes) + " \u0438\u0437 " + SizeText(TotalBytes) +
                (ReadOnly ? "\n\u0422\u043E\u043B\u044C\u043A\u043E \u0447\u0442\u0435\u043D\u0438\u0435" : "");
        }
    }
    private static string SizeText(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.#", CultureInfo.CurrentCulture) + " \u0413\u0411";
        return (Math.Max(0, bytes) / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.CurrentCulture) + " \u041C\u0411";
    }
}

internal static class ExternalDrives
{
    internal static List<ExternalDriveInfo> Scan(IExternalDriveProbe probe, out List<string> warnings)
    {
        warnings = new List<string>();
        List<ExternalDriveInfo> drives = new List<ExternalDriveInfo>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in probe.GetRoots())
        {
            if (!seen.Add(root)) continue;
            try
            {
                ExternalDriveInfo info = probe.Read(root);
                if (info != null && info.IsExternal && info.Ready && info.VolumeId.Length > 0) drives.Add(info);
            }
            catch (Exception ex) { warnings.Add(root + " \u2014 " + ex.Message); }
        }
        drives.Sort(delegate(ExternalDriveInfo a, ExternalDriveInfo b) { return StringComparer.OrdinalIgnoreCase.Compare(a.Root, b.Root); });
        return drives;
    }
}

internal sealed class ExternalDriveSelection
{
    private readonly IExternalDriveProbe probe;
    private readonly string volumeId;
    private readonly uint serial;
    internal readonly string Root, Caption;

    internal ExternalDriveSelection(ExternalDriveInfo info, IExternalDriveProbe source)
    {
        if (info == null || !info.IsExternal || !info.Ready || String.IsNullOrEmpty(info.VolumeId))
            throw new IOException("\u0412\u043D\u0435\u0448\u043D\u0438\u0439 \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u044C \u043D\u0435\u0434\u043E\u0441\u0442\u0443\u043F\u0435\u043D. \u041E\u0431\u043D\u043E\u0432\u0438\u0442\u0435 \u0441\u043F\u0438\u0441\u043E\u043A \u0438 \u0432\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u044C \u0441\u043D\u043E\u0432\u0430.");
        probe = source; Root = info.Root; Caption = info.Caption; volumeId = info.VolumeId; serial = info.Serial;
        EnsurePresent();
    }
    internal void EnsurePresent()
    {
        ExternalDriveInfo current;
        try { current = probe.Read(Root); }
        catch (Exception ex) { throw new IOException("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043F\u0440\u043E\u0432\u0435\u0440\u0438\u0442\u044C \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0439 \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u044C " + Caption + ".\n" + ex.Message, ex); }
        if (current == null || !current.IsExternal || !current.Ready ||
            !String.Equals(current.Root, Root, StringComparison.OrdinalIgnoreCase) ||
            !String.Equals(current.VolumeId, volumeId, StringComparison.OrdinalIgnoreCase) || current.Serial != serial)
            throw new IOException("\u041D\u043E\u0441\u0438\u0442\u0435\u043B\u044C " + Caption + " \u043E\u0442\u043A\u043B\u044E\u0447\u0451\u043D \u0438\u043B\u0438 \u0437\u0430\u043C\u0435\u043D\u0451\u043D. \u0412\u044B\u0432\u043E\u0434 \u043E\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D. \u041F\u043E\u0434\u043A\u043B\u044E\u0447\u0438\u0442\u0435 \u043D\u0443\u0436\u043D\u044B\u0439 \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u044C \u0438 \u043F\u043E\u0432\u0442\u043E\u0440\u0438\u0442\u0435 \u0437\u0430\u043F\u0443\u0441\u043A.");
        if (current.ReadOnly) throw new IOException("\u041D\u043E\u0441\u0438\u0442\u0435\u043B\u044C " + Caption + " \u0434\u043E\u0441\u0442\u0443\u043F\u0435\u043D \u0442\u043E\u043B\u044C\u043A\u043E \u0434\u043B\u044F \u0447\u0442\u0435\u043D\u0438\u044F.");
    }
}

internal sealed class WindowsExternalDriveProbe : IExternalDriveProbe
{
    public string[] GetRoots()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            throw new PlatformNotSupportedException("\u041F\u043E\u0438\u0441\u043A \u0432\u043D\u0435\u0448\u043D\u0438\u0445 \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u0435\u0439 \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u0442\u0441\u044F \u0432 Windows.");
        uint mask = GetLogicalDrives();
        if (mask == 0) throw NativeError("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043F\u043E\u043B\u0443\u0447\u0438\u0442\u044C \u0441\u043F\u0438\u0441\u043E\u043A \u0434\u0438\u0441\u043A\u043E\u0432 Windows.");
        return RootsFromMask(mask);
    }
    internal static string[] RootsFromMask(uint mask)
    {
        List<string> roots = new List<string>();
        for (int i = 0; i < 26; i++)
            if ((mask & (1U << i)) != 0) roots.Add(((char)('A' + i)).ToString() + @":\");
        return roots.ToArray();
    }
    public ExternalDriveInfo Read(string root)
    {
        if (root == null || !Regex.IsMatch(root, @"\A[A-Za-z]:\\\z")) return null;
        // Avoid system "insert disk" popups for empty card readers or unplugged media.
        // Change only this thread and restore its exact mode on every exit.
        uint previousMode;
        if (!SetThreadErrorMode(GetThreadErrorMode() | 1U, out previousMode))
            throw NativeError("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043D\u0430\u0447\u0430\u0442\u044C \u043F\u0440\u043E\u0432\u0435\u0440\u043A\u0443 \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u044F.");
        try { return ReadCore(root); }
        finally { uint ignored; SetThreadErrorMode(previousMode, out ignored); }
    }
    private static ExternalDriveInfo ReadCore(string root)
    {
        ExternalDriveInfo info = new ExternalDriveInfo();
        info.Root = root; info.Type = (ExternalDriveKind)GetDriveTypeW(root);
        info.SystemVolume = String.Equals(root, IOPath.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase);
        if (info.SystemVolume || (info.Type != ExternalDriveKind.Fixed && info.Type != ExternalDriveKind.Removable)) return info;
        ulong available, total, totalFree;
        info.Ready = GetDiskFreeSpaceExW(root, out available, out total, out totalFree);
        if (!info.Ready) return info;
        info.Bus = ReadBus(root);
        if (!info.IsExternal) return info;
        // Capture identity on both sides of the metadata reads, in case a letter was reassigned.
        string before = VolumeId(root);
        StringBuilder label = new StringBuilder(261), fileSystem = new StringBuilder(261);
        uint maxComponent, flags, serial;
        if (!GetVolumeInformationW(root, label, (uint)label.Capacity, out serial, out maxComponent, out flags,
            fileSystem, (uint)fileSystem.Capacity)) throw NativeError("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043F\u0440\u043E\u0447\u0438\u0442\u0430\u0442\u044C \u0441\u0432\u0435\u0434\u0435\u043D\u0438\u044F \u043E \u043D\u043E\u0441\u0438\u0442\u0435\u043B\u0435.");
        info.Label = label.ToString(); info.Serial = serial; info.ReadOnly = (flags & 0x00080000U) != 0;
        info.FreeBytes = checked((long)available); info.TotalBytes = checked((long)total);
        info.VolumeId = VolumeId(root);
        if (!String.Equals(before, info.VolumeId, StringComparison.OrdinalIgnoreCase))
            throw new IOException("\u041D\u043E\u0441\u0438\u0442\u0435\u043B\u044C \u0438\u0437\u043C\u0435\u043D\u0438\u043B\u0441\u044F \u0432\u043E \u0432\u0440\u0435\u043C\u044F \u0447\u0442\u0435\u043D\u0438\u044F. \u041E\u0431\u043D\u043E\u0432\u0438\u0442\u0435 \u0441\u043F\u0438\u0441\u043E\u043A.");
        return info;
    }
    private static string VolumeId(string root)
    {
        StringBuilder name = new StringBuilder(64);
        if (!GetVolumeNameForVolumeMountPointW(root, name, (uint)name.Capacity))
            throw NativeError("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043E\u043F\u0440\u0435\u0434\u0435\u043B\u0438\u0442\u044C \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0439 \u0442\u043E\u043C.");
        return name.ToString();
    }
    private static IOException NativeError(string message)
    {
        int error = Marshal.GetLastWin32Error();
        return new IOException(message + " \u041A\u043E\u0434 Windows: " + error.ToString(CultureInfo.InvariantCulture) + ".");
    }
    private static int ReadBus(string root)
    {
        // A zero-access volume handle is enough for IOCTL_STORAGE_QUERY_PROPERTY.
        IntPtr handle = CreateFileW(@"\\.\" + root.Substring(0, 2), 0, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == new IntPtr(-1)) return -1;
        try
        {
            byte[] query = new byte[12], header = new byte[8]; uint returned;
            // StorageDeviceProperty = 0; PropertyStandardQuery = 0.
            if (!DeviceIoControl(handle, 0x002D1400, query, query.Length, header, header.Length, out returned, IntPtr.Zero) || returned < 8) return -1;
            uint size = BitConverter.ToUInt32(header, 4);
            if (size < 36 || size > 65536) return -1;
            byte[] descriptor = new byte[(int)size];
            if (!DeviceIoControl(handle, 0x002D1400, query, query.Length, descriptor, descriptor.Length, out returned, IntPtr.Zero) || returned < 36) return -1;
            // STORAGE_DEVICE_DESCRIPTOR.BusType is a 32-bit enum at byte offset 28.
            return BitConverter.ToInt32(descriptor, 28);
        }
        finally { CloseHandle(handle); }
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint GetLogicalDrives();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetDriveTypeW(string root);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(string root, out ulong available, out ulong total, out ulong totalFree);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetThreadErrorMode();
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadErrorMode(uint mode, out uint previousMode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(IntPtr device, uint code, byte[] input, int inputSize,
        [Out] byte[] output, int outputSize, out uint bytesReturned, IntPtr overlapped);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string root, StringBuilder name, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(string root, StringBuilder label, uint labelSize,
        out uint serial, out uint maxComponent, out uint flags, StringBuilder fileSystem, uint fileSystemSize);
}


public sealed class MachineTarget
{
    public string Name;
    public string DirectoryPath;
    public MachineTarget(string name, string directory) { Name = name; DirectoryPath = directory; }
}

public sealed class RouterConfig
{
    public const string IniFileName = "NX_Postprocess_To_Machine.ini";
    public string FilePath;
    public string DefaultExtension = ".nc";
    public FanucBinMode BinMode = FanucBinMode.Off;
    public int BinSizeMB = 8;
    // auto = installed NX catalog; empty = only explicit [Post name] sections.
    public string PostList = "auto";
    public readonly List<MachineTarget> MachineRoots = new List<MachineTarget>();
    public readonly List<MachineTarget> ExplicitMachines = new List<MachineTarget>();
    public readonly List<PostDefinition> Posts = new List<PostDefinition>();

    public static string ForJournal(string journalPath)
    {
        if (String.IsNullOrWhiteSpace(journalPath) || !IOPath.IsPathRooted(journalPath))
            throw new InvalidOperationException("NX \u043D\u0435 \u0441\u043E\u043E\u0431\u0449\u0438\u043B \u043F\u043E\u043B\u043D\u044B\u0439 \u043F\u0443\u0442\u044C \u0437\u0430\u043F\u0443\u0441\u043A\u0430\u0435\u043C\u043E\u0433\u043E \u0436\u0443\u0440\u043D\u0430\u043B\u0430. \u0417\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 CS-\u0444\u0430\u0439\u043B \u0447\u0435\u0440\u0435\u0437 \u00AB\u0412\u044B\u043F\u043E\u043B\u043D\u0438\u0442\u044C \u0436\u0443\u0440\u043D\u0430\u043B\u00BB.");
        return IOPath.Combine(IOPath.GetDirectoryName(IOPath.GetFullPath(journalPath)), IniFileName);
    }

    public static RouterConfig Load(string path, Func<string, string> environment)
    { return FromSections(IniReader.Read(IOPath.GetFullPath(path)), path, environment); }

    internal static RouterConfig Parse(string text, string path, Func<string, string> environment)
    { return FromSections(IniReader.Parse(text, path), path, environment); }

    private static RouterConfig FromSections(List<IniSection> sections, string path, Func<string, string> environment)
    {
        RouterConfig config = new RouterConfig();
        config.FilePath = IOPath.GetFullPath(path);
        string directory = IOPath.GetDirectoryName(config.FilePath);
        foreach (IniSection section in sections)
        {
            if (String.Equals(section.Name, "Settings", StringComparison.OrdinalIgnoreCase))
            {
                Allowed(section, path, new string[] { "ResultsRoot", "DefaultExtension", "PostList" });
                // Legacy ResultsRoot is accepted but ignored; reports are disabled.
                IniEntry value = section.Find("DefaultExtension");
                if (value != null) config.DefaultExtension = ReadExtension(value, path);
                value = section.Find("PostList");
                if (value != null) config.PostList = value.Value.Length == 0 ? "" :
                    (String.Equals(value.Value, "auto", StringComparison.OrdinalIgnoreCase) ? "auto" : Resolve(value, directory, environment, path));
            }
            else if (String.Equals(section.Name, "FanucBin", StringComparison.OrdinalIgnoreCase))
            {
                Allowed(section, path, new string[] { "Mode", "SizeMB" });
                IniEntry mode = section.Find("Mode"), size = section.Find("SizeMB");
                if (mode != null)
                {
                    if (String.Equals(mode.Value, "Off", StringComparison.OrdinalIgnoreCase)) config.BinMode = FanucBinMode.Off;
                    else if (String.Equals(mode.Value, "Merge", StringComparison.OrdinalIgnoreCase)) config.BinMode = FanucBinMode.Merge;
                    else if (String.Equals(mode.Value, "New", StringComparison.OrdinalIgnoreCase)) config.BinMode = FanucBinMode.New;
                    else throw IniReader.Error(path, mode.Line, "Mode: Off, Merge \u0438\u043B\u0438 New.");
                }
                if (size != null && (!Int32.TryParse(size.Value, NumberStyles.None, CultureInfo.InvariantCulture, out config.BinSizeMB) || config.BinSizeMB < 1 || config.BinSizeMB > 2048))
                    throw IniReader.Error(path, size.Line, "SizeMB \u0434\u043E\u043B\u0436\u0435\u043D \u0431\u044B\u0442\u044C \u0446\u0435\u043B\u044B\u043C \u0447\u0438\u0441\u043B\u043E\u043C \u043E\u0442 1 \u0434\u043E 2048.");
            }
            else if (String.Equals(section.Name, "MachineRoots", StringComparison.OrdinalIgnoreCase) ||
                     String.Equals(section.Name, "Machines", StringComparison.OrdinalIgnoreCase))
            {
                List<MachineTarget> targets = String.Equals(section.Name, "MachineRoots", StringComparison.OrdinalIgnoreCase) ?
                    config.MachineRoots : config.ExplicitMachines;
                foreach (IniEntry entry in section.Entries)
                    targets.Add(new MachineTarget(entry.Key, Resolve(entry, directory, environment, path)));
            }
            else if (section.Name.StartsWith("Post ", StringComparison.OrdinalIgnoreCase))
            {
                string name = section.Name.Substring(5).Trim();
                if (name.Length == 0) throw IniReader.Error(path, section.Line, "\u0423\u043A\u0430\u0436\u0438\u0442\u0435 \u0438\u043C\u044F: [Post FANUC_3_AXIS].");
                Allowed(section, path, new string[] { "Tcl", "Def", "Extension" });
                IniEntry tcl = section.Find("Tcl"), def = section.Find("Def");
                if (tcl == null || def == null)
                    throw IniReader.Error(path, section.Line, "\u0414\u043B\u044F \u043F\u043E\u0441\u0442\u0430 \u00AB" + name + "\u00BB \u043D\u0443\u0436\u043D\u044B Tcl= \u0438 Def=.");
                PostDefinition post = new PostDefinition(name, Resolve(tcl, directory, environment, path), Resolve(def, directory, environment, path));
                if (!String.Equals(IOPath.GetExtension(post.EventFile), ".tcl", StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(IOPath.GetExtension(post.DefinitionFile), ".def", StringComparison.OrdinalIgnoreCase))
                    throw IniReader.Error(path, section.Line, "Tcl \u0434\u043E\u043B\u0436\u0435\u043D \u0443\u043A\u0430\u0437\u044B\u0432\u0430\u0442\u044C \u043D\u0430 .tcl, Def \u2014 \u043D\u0430 .def.");
                IniEntry extension = section.Find("Extension");
                if (extension != null) post.DefaultExtension = ReadExtension(extension, path);
                foreach (PostDefinition existing in config.Posts)
                    if (existing.Key == post.Key) throw IniReader.Error(path, section.Line, "\u042D\u0442\u0430 \u043F\u0430\u0440\u0430 TCL/DEF \u0443\u0436\u0435 \u0437\u0430\u0434\u0430\u043D\u0430 \u0432 INI.");
                config.Posts.Add(post);
            }
            else throw IniReader.Error(path, section.Line, "\u041D\u0435\u0438\u0437\u0432\u0435\u0441\u0442\u043D\u0430\u044F \u0441\u0435\u043A\u0446\u0438\u044F [" + section.Name + "].");
        }
        // A machine list is optional: the manual folder picker is always available.
        return config;
    }

    private static void Allowed(IniSection section, string path, string[] names)
    {
        foreach (IniEntry entry in section.Entries)
        {
            bool known = false;
            foreach (string name in names)
                if (String.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase)) known = true;
            if (!known) throw IniReader.Error(path, entry.Line, "\u041D\u0435\u0438\u0437\u0432\u0435\u0441\u0442\u043D\u044B\u0439 \u043F\u0430\u0440\u0430\u043C\u0435\u0442\u0440 " + entry.Key + " \u0432 [" + section.Name + "].");
        }
    }

    private static string ReadExtension(IniEntry entry, string path)
    {
        try { return PostFiles.Extension(entry.Value); }
        catch (ArgumentException ex) { throw IniReader.Error(path, entry.Line, ex.Message); }
    }

    private static string Resolve(IniEntry entry, string directory, Func<string, string> environment, string iniPath)
    {
        try { return ResolvePath(entry.Value, directory, environment); }
        catch (Exception ex) { throw IniReader.Error(iniPath, entry.Line, entry.Key + ": " + ex.Message); }
    }

    public static string ResolvePath(string value, string directory, Func<string, string> environment)
    {
        if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("\u041F\u0443\u0442\u044C \u043D\u0435 \u0434\u043E\u043B\u0436\u0435\u043D \u0431\u044B\u0442\u044C \u043F\u0443\u0441\u0442\u044B\u043C.");
        string expanded = PostCatalog.Expand(value, environment);
        if (Regex.IsMatch(expanded, @"\$\{|%[A-Za-z_][A-Za-z0-9_]*%|\$[A-Za-z_]"))
            throw new ArgumentException("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0440\u0430\u0441\u043A\u0440\u044B\u0442\u044C \u043F\u0435\u0440\u0435\u043C\u0435\u043D\u043D\u0443\u044E \u0432 \u043F\u0443\u0442\u0438: " + value);
        // Windows drive-relative/root-relative paths depend on ambient process state.
        if (IOPath.DirectorySeparatorChar == '\\')
        {
            if (Regex.IsMatch(expanded, @"^[A-Za-z]:(?![\\/])") ||
                ((expanded.StartsWith("\\") || expanded.StartsWith("/")) &&
                 !expanded.StartsWith("\\\\") && !expanded.StartsWith("//")))
                throw new ArgumentException("\u0423\u043A\u0430\u0436\u0438\u0442\u0435 \u043F\u043E\u043B\u043D\u044B\u0439 \u043F\u0443\u0442\u044C \u0441 \u0434\u0438\u0441\u043A\u043E\u043C, UNC-\u043F\u0443\u0442\u044C \u0438\u043B\u0438 \u043E\u0442\u043D\u043E\u0441\u0438\u0442\u0435\u043B\u044C\u043D\u044B\u0439 \u043F\u0443\u0442\u044C \u0431\u0435\u0437 \u043D\u0430\u0447\u0430\u043B\u044C\u043D\u043E\u0433\u043E \u0441\u043B\u0435\u0448\u0430.");
            if (expanded.StartsWith(@"\\?\") || expanded.StartsWith(@"\\.\"))
                throw new ArgumentException("\u0421\u043B\u0443\u0436\u0435\u0431\u043D\u044B\u0435 \u043F\u0443\u0442\u0438 \u0443\u0441\u0442\u0440\u043E\u0439\u0441\u0442\u0432 \u043D\u0435 \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u044E\u0442\u0441\u044F.");
        }
        string result = IOPath.GetFullPath(IOPath.IsPathRooted(expanded) ? expanded : IOPath.Combine(directory, expanded));
        if (IOPath.DirectorySeparatorChar == '\\')
        {
            string root = IOPath.GetPathRoot(result);
            if (root.StartsWith("\\\\") && root.Trim('\\').Split('\\').Length < 2)
                throw new ArgumentException("\u0412 UNC-\u043F\u0443\u0442\u0438 \u043D\u0443\u0436\u043D\u044B \u0441\u0435\u0440\u0432\u0435\u0440 \u0438 \u043E\u0431\u0449\u0430\u044F \u043F\u0430\u043F\u043A\u0430.");
        }
        return result;
    }

    public List<PostDefinition> GetPosts(Func<string, string> environment)
    {
        List<PostDefinition> posts = new List<PostDefinition>();
        HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PostDefinition post in Posts) { posts.Add(post); keys.Add(post.Key); }
        string registry = PostList;
        if (registry == "auto")
        {
            registry = PostCatalog.FindRegistry(environment);
            if (registry.Length > 0 && !File.Exists(registry)) registry = "";
        }
        if (registry.Length > 0)
            foreach (PostDefinition post in PostCatalog.Read(registry, environment))
                if (keys.Add(post.Key)) posts.Add(post);
        return posts;
    }

    public List<MachineTarget> GetMachines()
    {
        List<MachineTarget> machines = new List<MachineTarget>();
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Explicit entries also let the user rename an automatically found machine.
        foreach (MachineTarget target in ExplicitMachines) AddMachine(machines, paths, names, target);
        foreach (MachineTarget root in MachineRoots)
        {
            List<string> numbers = PostFiles.Machines(root.DirectoryPath);
            foreach (string number in numbers)
            {
                string name = MachineRoots.Count > 1 ? root.Name + " \u2014 " + number : number;
                AddMachine(machines, paths, names, new MachineTarget(name, IOPath.Combine(root.DirectoryPath, number)));
            }
        }
        if (machines.Count == 0) throw new InvalidOperationException("\u0412 INI \u043D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D\u044B \u0434\u043E\u0441\u0442\u0443\u043F\u043D\u044B\u0435 \u0441\u0442\u0430\u043D\u043A\u0438:\u000A" + FilePath);
        return machines;
    }

    public List<MachineTarget> GetAvailableMachines(out List<string> warnings)
    {
        List<MachineTarget> machines = new List<MachineTarget>(); warnings = new List<string>();
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MachineTarget target in ExplicitMachines)
        {
            try { AddMachine(machines, paths, names, target); }
            catch (IOException ex) { warnings.Add(ex.Message); }
            catch (UnauthorizedAccessException ex) { warnings.Add(ex.Message); }
        }
        foreach (MachineTarget root in MachineRoots)
        {
            List<string> numbers;
            try { numbers = PostFiles.Machines(root.DirectoryPath); }
            catch (IOException ex) { warnings.Add(ex.Message); continue; }
            catch (UnauthorizedAccessException ex) { warnings.Add(ex.Message); continue; }
            foreach (string number in numbers)
            {
                string name = MachineRoots.Count > 1 ? root.Name + " \u2014 " + number : number;
                try { AddMachine(machines, paths, names, new MachineTarget(name, IOPath.Combine(root.DirectoryPath, number))); }
                catch (IOException ex) { warnings.Add(ex.Message); }
                catch (UnauthorizedAccessException ex) { warnings.Add(ex.Message); }
            }
        }
        return machines;
    }

    private void AddMachine(List<MachineTarget> machines, HashSet<string> paths, HashSet<string> names, MachineTarget target)
    {
        if (!Directory.Exists(target.DirectoryPath))
            throw new DirectoryNotFoundException("\u041F\u0430\u043F\u043A\u0430 \u00AB" + target.Name + "\u00BB \u043D\u0435\u0434\u043E\u0441\u0442\u0443\u043F\u043D\u0430:\u000A" + target.DirectoryPath + "\u000A\u041F\u0440\u043E\u0432\u0435\u0440\u044C\u0442\u0435 INI: " + FilePath);
        string normalized = IOPath.GetFullPath(target.DirectoryPath).TrimEnd(new char[] { IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar });
        if (!paths.Add(normalized)) return;
        if (!names.Add(target.Name))
            throw new FormatException("\u041E\u0434\u0438\u043D\u0430\u043A\u043E\u0432\u0430\u044F \u043F\u043E\u0434\u043F\u0438\u0441\u044C \u043A\u043D\u043E\u043F\u043A\u0438 \u00AB" + target.Name + "\u00BB \u0443 \u0440\u0430\u0437\u043D\u044B\u0445 \u043F\u0430\u043F\u043E\u043A. \u0423\u043A\u0430\u0436\u0438\u0442\u0435 \u0440\u0430\u0437\u043D\u044B\u0435 \u0438\u043C\u0435\u043D\u0430 \u0432 INI:\u000A" + FilePath);
        machines.Add(target);
    }
}

// SHA256 lives in different assemblies in .NET Framework and .NET 10.
// Resolve the installed implementation at runtime without adding compiler refs.
public static class RuntimeHash
{
    public static string Compute(Stream input)
    {
        Type type = Type.GetType("System.Security.Cryptography.SHA256, System.Security.Cryptography", false);
        if (type == null) type = typeof(object).Assembly.GetType("System.Security.Cryptography.SHA256", false);
        if (type == null) type = Type.GetType("System.Security.Cryptography.SHA256, System.Security.Cryptography.Algorithms", false);
        if (type == null) throw new InvalidOperationException("\u0412 \u0441\u0440\u0435\u0434\u0435 NX \u043D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D \u0441\u0438\u0441\u0442\u0435\u043C\u043D\u044B\u0439 SHA256 \u0434\u043B\u044F \u043F\u0440\u043E\u0432\u0435\u0440\u043A\u0438 \u043A\u043E\u043F\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u044F.");
        MethodInfo factory = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            null, Type.EmptyTypes, null);
        if (factory == null) throw new MissingMethodException(type.FullName, "Create");
        object algorithm = factory.Invoke(null, null);
        using ((IDisposable)algorithm)
        {
            MethodInfo compute = algorithm.GetType().GetMethod("ComputeHash", new Type[] { typeof(Stream) });
            return Convert.ToBase64String((byte[])compute.Invoke(algorithm, new object[] { input }));
        }
    }
}

public sealed class FileSnapshot
{
    public bool Exists;
    public string Hash;
    public static FileSnapshot Read(string path)
    {
        if (Directory.Exists(path)) throw new IOException("\u0412\u043C\u0435\u0441\u0442\u043E \u0444\u0430\u0439\u043B\u0430 \u0441\u0443\u0449\u0435\u0441\u0442\u0432\u0443\u0435\u0442 \u043F\u0430\u043F\u043A\u0430: " + path);
        FileSnapshot state = new FileSnapshot();
        try
        {
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            { state.Hash = RuntimeHash.Compute(input); state.Exists = true; }
        }
        catch (FileNotFoundException) { state.Exists = false; }
        return state;
    }
    public bool Matches(FileSnapshot other)
    { return Exists == other.Exists && (!Exists || Hash == other.Hash); }
}

public sealed class OutputCopy
{
    public string Description;
    public string Destination;
    public FileSnapshot Approved;
    public bool Saved;
    public OutputCopy(string description, string destination)
    { Description = description; Destination = destination; }
}

public static class PostFiles
{
    public static string ProjectDirectory(string partPath)
    {
        if (String.IsNullOrWhiteSpace(partPath) || !IOPath.IsPathRooted(partPath) ||
            partPath.StartsWith("@DB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("\u0421\u043D\u0430\u0447\u0430\u043B\u0430 \u0441\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u0435 \u0442\u0435\u043A\u0443\u0449\u0438\u0439 CAM-\u043F\u0440\u043E\u0435\u043A\u0442 \u0432 \u0444\u0430\u0439\u043B .prt.\u000A\u041A\u043E\u043F\u0438\u044F \u0423\u041F \u0431\u0443\u0434\u0435\u0442 \u0441\u043E\u0445\u0440\u0430\u043D\u044F\u0442\u044C\u0441\u044F \u0440\u044F\u0434\u043E\u043C \u0441 \u044D\u0442\u0438\u043C \u0444\u0430\u0439\u043B\u043E\u043C.");
        string full = IOPath.GetFullPath(partPath);
        if (!File.Exists(full))
            throw new FileNotFoundException("\u0424\u0430\u0439\u043B \u043F\u0440\u043E\u0435\u043A\u0442\u0430 \u043D\u0435\u0434\u043E\u0441\u0442\u0443\u043F\u0435\u043D. \u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u0435 \u043F\u0440\u043E\u0435\u043A\u0442 \u0438\u043B\u0438 \u043F\u0440\u043E\u0432\u0435\u0440\u044C\u0442\u0435 \u0434\u043E\u0441\u0442\u0443\u043F \u043A \u043D\u0435\u043C\u0443:\u000A" + full);
        string directory = IOPath.GetDirectoryName(full);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("\u041F\u0430\u043F\u043A\u0430 \u043F\u0440\u043E\u0435\u043A\u0442\u0430 \u043D\u0435\u0434\u043E\u0441\u0442\u0443\u043F\u043D\u0430:\u000A" + directory);
        return directory;
    }

    public static List<OutputCopy> CreateCopies(string partPath, string machineDirectory, string fileName)
    { return CreateCopies(partPath, machineDirectory, fileName, true); }

    public static List<OutputCopy> CreateCopies(string partPath, string machineDirectory, string fileName, bool saveToProject)
    {
        ValidateLeaf(fileName);
        string projectDirectory = saveToProject ? ProjectDirectory(partPath) : null;
        if (!Directory.Exists(machineDirectory))
            throw new DirectoryNotFoundException("\u041F\u0430\u043F\u043A\u0430 \u0432\u044B\u0432\u043E\u0434\u0430 \u043D\u0435\u0434\u043E\u0441\u0442\u0443\u043F\u043D\u0430:\u000A" + machineDirectory);
        List<string> destinations = new List<string>(), labels = new List<string>();
        if (saveToProject) { destinations.Add(IOPath.GetFullPath(IOPath.Combine(projectDirectory, fileName))); labels.Add("\u0420\u044F\u0434\u043E\u043C \u0441 \u043F\u0440\u043E\u0435\u043A\u0442\u043E\u043C"); }
        destinations.Add(IOPath.GetFullPath(IOPath.Combine(machineDirectory, fileName))); labels.Add("\u0412 \u043F\u0430\u043F\u043A\u0435 \u0432\u044B\u0432\u043E\u0434\u0430");
        string fullPartPath = !String.IsNullOrWhiteSpace(partPath) && IOPath.IsPathRooted(partPath) && !partPath.StartsWith("@DB", StringComparison.OrdinalIgnoreCase) ? IOPath.GetFullPath(partPath) : "";
        List<OutputCopy> copies = new List<OutputCopy>();
        for (int i = 0; i < destinations.Count; i++)
        {
            string destination = destinations[i];
            if (String.Equals(destination, fullPartPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("\u0418\u043C\u044F \u0432\u044B\u0445\u043E\u0434\u043D\u043E\u0439 \u0423\u041F \u0441\u043E\u0432\u043F\u0430\u043B\u043E \u0441 \u0444\u0430\u0439\u043B\u043E\u043C \u043F\u0440\u043E\u0435\u043A\u0442\u0430:\u000A" + fullPartPath +
                    "\u000A\u0418\u0437\u043C\u0435\u043D\u0438\u0442\u0435 \u0440\u0430\u0441\u0448\u0438\u0440\u0435\u043D\u0438\u0435 \u0423\u041F \u0438\u043B\u0438 \u0438\u043C\u044F \u043F\u0430\u043F\u043A\u0438 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u044B. \u0424\u0430\u0439\u043B \u043F\u0440\u043E\u0435\u043A\u0442\u0430 \u043D\u0435 \u043F\u0435\u0440\u0435\u0437\u0430\u043F\u0438\u0441\u044B\u0432\u0430\u0435\u0442\u0441\u044F.");
            if (destination.Length >= 248)
                throw new InvalidOperationException("\u0421\u043B\u0438\u0448\u043A\u043E\u043C \u0434\u043B\u0438\u043D\u043D\u044B\u0439 \u043F\u0443\u0442\u044C \u0432\u044B\u0445\u043E\u0434\u043D\u043E\u0433\u043E \u0444\u0430\u0439\u043B\u0430:\u000A" + destination);
            bool found = false;
            foreach (OutputCopy copy in copies)
            {
                if (!String.Equals(copy.Destination, destination, StringComparison.OrdinalIgnoreCase)) continue;
                copy.Description = "\u041F\u0430\u043F\u043A\u0430 \u043F\u0440\u043E\u0435\u043A\u0442\u0430 \u0438 \u043F\u0430\u043F\u043A\u0430 \u0432\u044B\u0432\u043E\u0434\u0430 \u0441\u043E\u0432\u043F\u0430\u0434\u0430\u044E\u0442";
                found = true;
                break;
            }
            if (!found) copies.Add(new OutputCopy(labels[i], destination));
        }
        return copies;
    }

    public static void SnapshotCopies(List<OutputCopy> copies)
    {
        foreach (OutputCopy copy in copies) copy.Approved = FileSnapshot.Read(copy.Destination);
    }

    public static void PublishCopies(string source, List<OutputCopy> copies) { PublishCopies(source, copies, null); }
    internal static void PublishCopies(string source, List<OutputCopy> copies, ExternalDriveSelection media)
    {
        if (media != null) media.EnsurePresent();
        if (copies.Count == 0) throw new InvalidOperationException("\u041D\u0435 \u0437\u0430\u0434\u0430\u043D\u044B \u043F\u0430\u043F\u043A\u0438 \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u044F \u0423\u041F.");
        // Keep the source read-locked for the whole operation on Windows, so both
        // destinations receive the same bytes. Successful copies are kept if a later one fails.
        using (FileStream sourceLock = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (sourceLock.Length == 0) throw new IOException("\u0412\u044B\u0445\u043E\u0434\u043D\u043E\u0439 \u0444\u0430\u0439\u043B \u043F\u0443\u0441\u0442.");
            // Check all approvals before the first mutation, then Publish rechecks each target.
            foreach (OutputCopy copy in copies)
            {
                if (copy.Approved == null)
                    throw new InvalidOperationException("\u041D\u0435 \u043F\u0440\u043E\u0432\u0435\u0440\u0435\u043D\u043E \u0441\u0443\u0449\u0435\u0441\u0442\u0432\u043E\u0432\u0430\u043D\u0438\u0435 \u0432\u044B\u0445\u043E\u0434\u043D\u043E\u0433\u043E \u0444\u0430\u0439\u043B\u0430:\u000A" + copy.Destination);
                if (!copy.Approved.Matches(FileSnapshot.Read(copy.Destination)))
                    throw new IOException("\u0424\u0430\u0439\u043B \u0438\u0437\u043C\u0435\u043D\u0438\u043B\u0441\u044F \u043F\u043E\u0441\u043B\u0435 \u043F\u043E\u0434\u0442\u0432\u0435\u0440\u0436\u0434\u0435\u043D\u0438\u044F. \u041F\u043E\u0432\u0442\u043E\u0440\u0438\u0442\u0435 \u0437\u0430\u043F\u0443\u0441\u043A:\u000A" + copy.Destination);
            }
            foreach (OutputCopy copy in copies)
            {
                Publish(source, copy.Destination, copy.Approved, media);
                copy.Saved = true;
            }
        }
    }

    public static string SavedCopiesDescription(List<OutputCopy> copies)
    {
        List<string> lines = new List<string>();
        foreach (OutputCopy copy in copies)
        {
            if (!copy.Saved) continue;
            string text = copy.Description + ":\n" + copy.Destination;
            lines.Add(text);
        }
        return String.Join("\n\n", lines.ToArray());
    }

    public static void ValidateLeaf(string name)
    {
        if (String.IsNullOrWhiteSpace(name) || name == "." || name == ".." ||
            name.EndsWith(".") || name.EndsWith(" ") || Regex.IsMatch(name, "[<>:\"/\\\\|?*\\x00-\\x1F]"))
            throw new ArgumentException("\u0418\u043C\u044F \u043F\u0430\u043F\u043A\u0438 \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u044B \u043D\u0435\u0434\u043E\u043F\u0443\u0441\u0442\u0438\u043C\u043E \u0434\u043B\u044F \u0444\u0430\u0439\u043B\u0430 Windows: \u00AB" + name + "\u00BB.\u000A\u0418\u0441\u043F\u0440\u0430\u0432\u044C\u0442\u0435 \u0438\u043C\u044F \u043F\u0430\u043F\u043A\u0438 \u0432 NX \u0438 \u043F\u043E\u0432\u0442\u043E\u0440\u0438\u0442\u0435 \u0437\u0430\u043F\u0443\u0441\u043A.");
        string stem = name.Split('.')[0].TrimEnd(' ');
        if (Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
            throw new ArgumentException("\u0418\u043C\u044F \u0437\u0430\u0440\u0435\u0437\u0435\u0440\u0432\u0438\u0440\u043E\u0432\u0430\u043D\u043E Windows: " + name);
    }
    public static string Extension(string input)
    {
        string extension = input.Trim();
        if (extension.Length == 0) return "";
        if (!extension.StartsWith(".")) extension = "." + extension;
        if (!Regex.IsMatch(extension, @"^\.[A-Za-z0-9_]{1,16}$"))
            throw new ArgumentException("\u0420\u0430\u0441\u0448\u0438\u0440\u0435\u043D\u0438\u0435 \u0434\u043E\u043B\u0436\u043D\u043E \u0438\u043C\u0435\u0442\u044C \u0432\u0438\u0434 .nc, .mpf, .h \u0438\u043B\u0438 \u0431\u044B\u0442\u044C \u043F\u0443\u0441\u0442\u044B\u043C.");
        return extension;
    }
    public static List<string> Machines(string root)
    {
        List<string> machines = new List<string>();
        try
        {
            foreach (string folder in Directory.GetDirectories(root))
            {
                string name = IOPath.GetFileName(folder);
                if (Regex.IsMatch(name, @"^[0-9]+$")) machines.Add(name);
            }
        }
        catch (Exception ex) { throw new IOException("\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043F\u0440\u043E\u0447\u0438\u0442\u0430\u0442\u044C \u043F\u0430\u043F\u043A\u0438 \u0441\u0442\u0430\u043D\u043A\u043E\u0432:\u000A" + root + "\n\n" + ex.Message, ex); }
        machines.Sort(delegate(string a, string b)
        {
            string aa = a.TrimStart('0'), bb = b.TrimStart('0');
            int result = aa.Length.CompareTo(bb.Length);
            if (result == 0) result = String.CompareOrdinal(aa, bb);
            return result == 0 ? String.CompareOrdinal(a, b) : result;
        });
        if (machines.Count == 0) throw new IOException("\u0412 \u043F\u0430\u043F\u043A\u0435 \u043D\u0435\u0442 \u043F\u043E\u0434\u043F\u0430\u043F\u043E\u043A \u0441 \u043D\u043E\u043C\u0435\u0440\u0430\u043C\u0438 \u0441\u0442\u0430\u043D\u043A\u043E\u0432:\u000A" + root);
        return machines;
    }
    public static string MachineDirectory(string root, string machine)
    {
        if (!Regex.IsMatch(machine, @"^[0-9]+$")) throw new ArgumentException("\u041D\u0435\u0434\u043E\u043F\u0443\u0441\u0442\u0438\u043C\u044B\u0439 \u043D\u043E\u043C\u0435\u0440 \u0441\u0442\u0430\u043D\u043A\u0430.");
        string directory = IOPath.Combine(root, machine);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("\u041F\u0430\u043F\u043A\u0430 \u0441\u0442\u0430\u043D\u043A\u0430 \u043D\u0435\u0434\u043E\u0441\u0442\u0443\u043F\u043D\u0430:\u000A" + directory);
        return directory;
    }
    public static string NewTemporaryDirectory()
    {
        string directory = IOPath.Combine(IOPath.GetTempPath(), "NXPOST_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void DeleteTemporaryDirectory(string directory)
    {
        // Called only with the directory created for this run, never with a user folder.
        if (!String.IsNullOrEmpty(directory) && Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private static bool IsWarningReport(string path, string primary)
    {
        string expected = IOPath.GetFileNameWithoutExtension(primary) + "_warning.out";
        return String.Equals(IOPath.GetFileName(path), expected, StringComparison.OrdinalIgnoreCase);
    }

    public static void ValidateOutput(string directory, string primary)
    {
        FileInfo file = new FileInfo(primary);
        if (!file.Exists || file.Length == 0)
            throw new IOException("NX \u043D\u0435 \u0441\u043E\u0437\u0434\u0430\u043B \u043D\u0435\u043F\u0443\u0441\u0442\u043E\u0439 \u0444\u0430\u0439\u043B \u0441 \u043E\u0436\u0438\u0434\u0430\u0435\u043C\u044B\u043C \u0438\u043C\u0435\u043D\u0435\u043C:\u000A" + primary +
                "\u000A\u041F\u0440\u043E\u0432\u0435\u0440\u044C\u0442\u0435 \u0441\u043E\u043E\u0431\u0449\u0435\u043D\u0438\u044F NX \u0438 \u043D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438 \u0438\u043C\u0435\u043D\u0438 \u0444\u0430\u0439\u043B\u0430 \u0432 \u043F\u043E\u0441\u0442\u043F\u0440\u043E\u0446\u0435\u0441\u0441\u043E\u0440\u0435. \u041F\u0435\u0440\u0435\u043D\u043E\u0441 \u043D\u0430 \u0441\u0442\u0430\u043D\u043E\u043A \u043D\u0435 \u0432\u044B\u043F\u043E\u043B\u043D\u0435\u043D.");
        if (Directory.GetDirectories(directory).Length > 0)
            throw new IOException("\u041F\u043E\u0441\u0442 \u0441\u043E\u0437\u0434\u0430\u043B \u0432\u043B\u043E\u0436\u0435\u043D\u043D\u044B\u0435 \u043F\u0430\u043F\u043A\u0438. \u0410\u0432\u0442\u043E\u043C\u0430\u0442\u0438\u0447\u0435\u0441\u043A\u0438\u0439 \u043F\u0435\u0440\u0435\u043D\u043E\u0441 \u043C\u043D\u043E\u0433\u043E\u043A\u043E\u043C\u043F\u043E\u043D\u0435\u043D\u0442\u043D\u043E\u0433\u043E \u0440\u0435\u0437\u0443\u043B\u044C\u0442\u0430\u0442\u0430 \u043D\u0435 \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u0442\u0441\u044F.");
        List<string> extra = new List<string>();
        foreach (string path in Directory.GetFiles(directory))
        {
            if (String.Equals(IOPath.GetFullPath(path), IOPath.GetFullPath(primary), StringComparison.OrdinalIgnoreCase)) continue;
            if (IsWarningReport(path, primary)) continue;
            string extension = IOPath.GetExtension(path).ToLowerInvariant();
            if (extension == ".lpt" || extension == ".log") continue;
            extra.Add(IOPath.GetFileName(path));
        }
        if (extra.Count > 0)
            throw new IOException("\u041F\u043E\u0441\u0442 \u0441\u043E\u0437\u0434\u0430\u043B \u0434\u043E\u043F\u043E\u043B\u043D\u0438\u0442\u0435\u043B\u044C\u043D\u044B\u0435 \u0444\u0430\u0439\u043B\u044B:\u000A" + String.Join("\n", extra.ToArray()) +
                "\u000A\u000A\u0414\u043B\u044F \u043E\u0434\u043D\u043E\u0439 \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u043E\u0439 \u043F\u0430\u043F\u043A\u0438 \u043F\u043E\u0441\u0442 \u0434\u043E\u043B\u0436\u0435\u043D \u0441\u043E\u0437\u0434\u0430\u0432\u0430\u0442\u044C \u043E\u0434\u0438\u043D \u0444\u0430\u0439\u043B \u0423\u041F. \u041F\u0430\u043A\u0435\u0442 \u043D\u0435 \u043F\u0435\u0440\u0435\u043D\u0435\u0441\u0451\u043D; \u0432\u0440\u0435\u043C\u0435\u043D\u043D\u044B\u0435 \u0444\u0430\u0439\u043B\u044B \u0443\u0434\u0430\u043B\u044F\u044E\u0442\u0441\u044F.");
    }

    public static string Publish(string source, string destination, FileSnapshot approved) { return Publish(source, destination, approved, null); }
    private static string Publish(string source, string destination, FileSnapshot approved, ExternalDriveSelection media)
    {
        if (media != null) media.EnsurePresent();
        string directory = IOPath.GetDirectoryName(destination);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("\u041F\u0430\u043F\u043A\u0430 \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u044F \u043D\u0435\u0434\u043E\u0441\u0442\u0443\u043F\u043D\u0430:\u000A" + directory);
        string temp = IOPath.Combine(directory, ".NXPOST_" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            // CreateNew and exclusive access never expose a partially copied NC file.
            using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (input.Length == 0) throw new IOException("\u0412\u044B\u0445\u043E\u0434\u043D\u043E\u0439 \u0444\u0430\u0439\u043B \u043F\u0443\u0441\u0442.");
                input.CopyTo(output);
                output.Flush(true);
            }
            if (media != null) media.EnsurePresent();
            if (!FileSnapshot.Read(source).Matches(FileSnapshot.Read(temp)))
                throw new IOException("\u041A\u043E\u043F\u0438\u044F \u0444\u0430\u0439\u043B\u0430 \u043D\u0435 \u043F\u0440\u043E\u0448\u043B\u0430 \u043F\u0440\u043E\u0432\u0435\u0440\u043A\u0443. \u0417\u0430\u043C\u0435\u043D\u0430 \u043D\u0435 \u0432\u044B\u043F\u043E\u043B\u043D\u0435\u043D\u0430.");
            if (!approved.Matches(FileSnapshot.Read(destination)))
                throw new IOException("\u0424\u0430\u0439\u043B \u0438\u0437\u043C\u0435\u043D\u0438\u043B\u0441\u044F \u043F\u043E\u0441\u043B\u0435 \u0432\u044B\u0431\u043E\u0440\u0430. \u041F\u043E\u0432\u0442\u043E\u0440\u0438\u0442\u0435 \u0437\u0430\u043F\u0443\u0441\u043A, \u0447\u0442\u043E\u0431\u044B \u043D\u0435 \u0437\u0430\u0442\u0435\u0440\u0435\u0442\u044C \u0447\u0443\u0436\u0443\u044E \u0432\u0435\u0440\u0441\u0438\u044E:\u000A" + destination);
            if (approved.Exists)
            {
                // No delete+copy fallback: if the share cannot replace, keep the original.
                File.Replace(temp, destination, null);
            }
            else File.Move(temp, destination); // No overwrite if a competing file appears.
            return "";
        }
        catch (Exception ex)
        {
            string note = "\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0441\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u0443:\u000A" + destination + "\n\n" + ex.Message;
            throw new IOException(note, ex);
        }
        finally { if (File.Exists(temp)) { try { File.Delete(temp); } catch { } } }
    }
}

public static class ProgramNames
{
    public static string DefaultNumber(string folder)
    {
        try { return Normalize(folder); } catch (ArgumentException) { return ""; }
    }
    public static string Normalize(string value)
    {
        Match match = Regex.Match((value ?? "").Trim(), @"^[Oo]?([0-9]{1,8})$");
        if (!match.Success || UInt32.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) == 0)
            throw new ArgumentException("\u0412\u0432\u0435\u0434\u0438\u0442\u0435 \u043D\u043E\u043C\u0435\u0440 \u0423\u041F \u043E\u0442 1 \u0434\u043E 99999999, \u043D\u0430\u043F\u0440\u0438\u043C\u0435\u0440 O1659 \u0438\u043B\u0438 1659.");
        return "O" + match.Groups[1].Value;
    }
    public static string[] Resolve(string[] folders, string[] entered, string suffix)
    {
        if (entered != null && entered.Length != folders.Length) throw new ArgumentException("\u041A\u043E\u043B\u0438\u0447\u0435\u0441\u0442\u0432\u043E \u043D\u043E\u043C\u0435\u0440\u043E\u0432 \u043D\u0435 \u0441\u043E\u043E\u0442\u0432\u0435\u0442\u0441\u0442\u0432\u0443\u0435\u0442 \u043A\u043E\u043B\u0438\u0447\u0435\u0441\u0442\u0432\u0443 \u0423\u041F.");
        string[] result = new string[folders.Length];
        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<uint> numbers = new HashSet<uint>();
        for (int i = 0; i < folders.Length; i++)
        {
            string name = entered == null ? folders[i] : Normalize(entered[i]);
            PostFiles.ValidateLeaf(name); PostFiles.ValidateLeaf(name + suffix);
            if (!names.Add(name)) throw new ArgumentException("\u041F\u043E\u0432\u0442\u043E\u0440\u044F\u0435\u0442\u0441\u044F \u0438\u043C\u044F \u0423\u041F \u00AB" + name + "\u00BB. \u0412\u043A\u043B\u044E\u0447\u0438\u0442\u0435 \u00AB\u041D\u0430\u0437\u043D\u0430\u0447\u0438\u0442\u044C \u0438\u043C\u044F \u0423\u041F\u00BB \u0438 \u0443\u043A\u0430\u0436\u0438\u0442\u0435 \u0440\u0430\u0437\u043D\u044B\u0435 \u043D\u043E\u043C\u0435\u0440\u0430.");
            if (entered != null && !numbers.Add(UInt32.Parse(name.Substring(1), CultureInfo.InvariantCulture)))
                throw new ArgumentException("\u041D\u043E\u043C\u0435\u0440 \u00AB" + name + "\u00BB \u043F\u043E\u0432\u0442\u043E\u0440\u044F\u0435\u0442\u0441\u044F. \u0412\u0435\u0434\u0443\u0449\u0438\u0435 \u043D\u0443\u043B\u0438 \u043D\u0435 \u0434\u0435\u043B\u0430\u044E\u0442 \u043D\u043E\u043C\u0435\u0440 \u0434\u0440\u0443\u0433\u0438\u043C.");
            result[i] = name;
        }
        return result;
    }

    public static void AssignNumber(string path, string name)
    {
        if (new FileInfo(path).Length > 512L * 1024 * 1024) throw new IOException("\u0423\u041F \u043F\u0440\u0435\u0432\u044B\u0448\u0430\u0435\u0442 512 \u041C\u0411. \u041D\u0430\u0437\u043D\u0430\u0447\u0435\u043D\u0438\u0435 \u043D\u043E\u043C\u0435\u0440\u0430 \u043E\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D\u043E.");
        byte[] changed = RewriteNumber(File.ReadAllBytes(path), name);
        File.WriteAllBytes(path, changed); // Staging only; the original destinations are untouched.
    }

    public static byte[] RewriteNumber(byte[] raw, string name)
    {
        name = Normalize(name);
        int offset = raw.Length >= 3 && raw[0] == 0xef && raw[1] == 0xbb && raw[2] == 0xbf ? 3 : 0;
        if (raw.Length >= 2 && ((raw[0] == 0xff && raw[1] == 0xfe) || (raw[0] == 0xfe && raw[1] == 0xff)))
            throw new IOException("\u041D\u0430\u0437\u043D\u0430\u0447\u0435\u043D\u0438\u0435 \u043D\u043E\u043C\u0435\u0440\u0430 \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u0442 ASCII/UTF-8/\u043E\u0434\u043D\u043E\u0431\u0430\u0439\u0442\u043E\u0432\u0443\u044E \u0423\u041F, \u043D\u043E \u043D\u0435 UTF-16.");
        // ASCII decoding preserves offsets: every input byte maps to one char.
        // Only header digits are replaced in the original byte array; all other
        // bytes, comments, encodings, line endings and motions stay unchanged.
        string text = Encoding.ASCII.GetString(raw, offset, raw.Length - offset);
        Match header = Regex.Match(text, @"\A[ \t\r\n]*(?:%[ \t]*(?:\r\n|\r|\n)[ \t\r\n]*)?O([0-9]{1,8})(?=[ \t\r\n(]|$)");
        if (!header.Success) throw new IOException("\u041D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D \u043E\u0434\u043D\u043E\u0437\u043D\u0430\u0447\u043D\u044B\u0439 O-\u043D\u043E\u043C\u0435\u0440 \u0432 \u043D\u0430\u0447\u0430\u043B\u0435 \u0423\u041F. \u041D\u0430\u0437\u043D\u0430\u0447\u0435\u043D\u0438\u0435 \u0438\u043C\u0435\u043D\u0438 \u0440\u0430\u0431\u043E\u0442\u0430\u0435\u0442 \u0441 FANUC-\u0444\u043E\u0440\u043C\u0430\u0442\u043E\u043C O1\u2026O99999999; \u0444\u0430\u0439\u043B \u043D\u0435 \u0438\u0437\u043C\u0435\u043D\u0451\u043D.");
        StringBuilder outside = new StringBuilder(); bool comment = false;
        for (int i = header.Length; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(')
            {
                if (comment) throw new IOException("\u0412\u043B\u043E\u0436\u0435\u043D\u043D\u044B\u0435 \u043A\u043E\u043C\u043C\u0435\u043D\u0442\u0430\u0440\u0438\u0438: \u043D\u0435\u043B\u044C\u0437\u044F \u0431\u0435\u0437\u043E\u043F\u0430\u0441\u043D\u043E \u043F\u0440\u043E\u0432\u0435\u0440\u0438\u0442\u044C \u0435\u0434\u0438\u043D\u0441\u0442\u0432\u0435\u043D\u043D\u044B\u0439 \u043D\u043E\u043C\u0435\u0440 \u0423\u041F.");
                comment = true; outside.Append(' ');
            }
            else if (c == ')')
            {
                if (!comment) throw new IOException("\u041B\u0438\u0448\u043D\u044F\u044F \u0437\u0430\u043A\u0440\u044B\u0432\u0430\u044E\u0449\u0430\u044F \u0441\u043A\u043E\u0431\u043A\u0430 \u0432 \u0423\u041F.");
                comment = false; outside.Append(' ');
            }
            else if (c == '\r' || c == '\n') outside.Append('\n');
            else outside.Append(comment ? ' ' : c);
        }
        if (comment || Regex.IsMatch(outside.ToString(), @"(?m)^[ \t]*(?:O[ \t]*[0-9]|:[ \t]*[0-9]|<)", RegexOptions.IgnoreCase))
            throw new IOException("\u0412 \u0423\u041F \u043D\u0435\u0441\u043A\u043E\u043B\u044C\u043A\u043E \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C \u0438\u043B\u0438 \u043D\u0435\u0437\u0430\u043A\u0440\u044B\u0442\u044B\u0439 \u043A\u043E\u043C\u043C\u0435\u043D\u0442\u0430\u0440\u0438\u0439. \u041D\u043E\u043C\u0435\u0440 \u043D\u0435 \u043D\u0430\u0437\u043D\u0430\u0447\u0435\u043D.");
        System.Text.RegularExpressions.Group digits = header.Groups[1]; int start = offset + digits.Index;
        byte[] replacement = Encoding.ASCII.GetBytes(name.Substring(1));
        byte[] output = new byte[raw.Length - digits.Length + replacement.Length];
        Buffer.BlockCopy(raw, 0, output, 0, start);
        Buffer.BlockCopy(replacement, 0, output, start, replacement.Length);
        Buffer.BlockCopy(raw, start + digits.Length, output, start + replacement.Length, raw.Length - start - digits.Length);
        return output;
    }
}

public sealed class RouterPreferences
{
    public string FilePath;
    public bool SaveToProject = true;
    public string LastFolder = "";
    public static string DefaultPath()
    {
        return IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NX_Postprocess_To_Machine", "LastChoice.ini");
    }
    public static RouterPreferences Load(string path)
    {
        RouterPreferences value = new RouterPreferences(); value.FilePath = IOPath.GetFullPath(path);
        if (!File.Exists(value.FilePath)) return value;
        foreach (IniSection section in IniReader.Read(value.FilePath))
            if (String.Equals(section.Name, "LastChoice", StringComparison.OrdinalIgnoreCase))
            {
                IniEntry entry = section.Find("SaveToProject");
                if (entry != null)
                {
                    if (entry.Value != "0" && entry.Value != "1") throw IniReader.Error(path, entry.Line, "SaveToProject: 0 \u0438\u043B\u0438 1.");
                    value.SaveToProject = entry.Value == "1";
                }
                entry = section.Find("LastFolder");
                if (entry != null) value.LastFolder = entry.Value;
            }
        return value;
    }
    public void Save(bool enabled)
    { SaveState(enabled, LastFolder); }

    public void SaveLastFolder(string folder)
    {
        if (String.IsNullOrWhiteSpace(folder) || !IOPath.IsPathRooted(folder))
            throw new ArgumentException("\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u043F\u043E\u043B\u043D\u044B\u0439 \u043F\u0443\u0442\u044C \u043A \u043F\u0430\u043F\u043A\u0435 \u0432\u044B\u0432\u043E\u0434\u0430.");
        string full = IOPath.GetFullPath(folder);
        if (full.IndexOfAny(new char[] { '\r', '\n', '\0', '"' }) >= 0)
            throw new ArgumentException("\u041F\u0443\u0442\u044C \u0441\u043E\u0434\u0435\u0440\u0436\u0438\u0442 \u043D\u0435\u0434\u043E\u043F\u0443\u0441\u0442\u0438\u043C\u044B\u0435 \u0441\u0438\u043C\u0432\u043E\u043B\u044B.");
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("\u041F\u0430\u043F\u043A\u0430 \u0432\u044B\u0432\u043E\u0434\u0430 \u043D\u0435\u0434\u043E\u0441\u0442\u0443\u043F\u043D\u0430:\u000A" + full);
        SaveState(SaveToProject, full);
    }

    private void SaveState(bool enabled, string lastFolder)
    {
        string directory = IOPath.GetDirectoryName(FilePath); Directory.CreateDirectory(directory);
        string temp = IOPath.Combine(directory, ".LASTCHOICE_" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes("; NX_Postprocess_To_Machine " + ScriptInfo.SCRIPT_VERSION + "\r\n[LastChoice]\r\nSaveToProject=" + (enabled ? "1" : "0") + "\r\nLastFolder=\"" + lastFolder + "\"\r\n");
            using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null); else File.Move(temp, FilePath);
            SaveToProject = enabled;
            LastFolder = lastFolder;
        }
        finally { if (File.Exists(temp)) { try { File.Delete(temp); } catch { } } }
    }
}

public sealed class PreparedOutput
{
    public readonly string Source;
    public readonly List<OutputCopy> Copies;
    public PreparedOutput(string source, List<OutputCopy> copies) { Source = source; Copies = copies; }
}

// FANUC Memory Card Program Tool v4 container, big endian, flat O-number entries.
// Fresh compact images are built in staging. No in-place writes to a live BIN.
// Layout and numeric-entry accounting checked against the supplied v4 executable.
public enum FanucBinMode { Off, Merge, New }

public sealed class FanucProgram
{
    public uint Number;
    public uint Stamp;
    public byte[] Body;
    public string Name { get { return "O" + Number.ToString(CultureInfo.InvariantCulture); } }
}

public sealed class FanucImage
{
    public int CapacityBlocks;
    public uint Serial;
    public readonly List<FanucProgram> Programs = new List<FanucProgram>();
}

public static class FanucBin
{
    public const string FileName = "FANUCPRG.BIN";
    public const int DataOffset = 0x3ee00;
    public const int MaxPrograms = 63;
    private const long MaxText = 512L * 1024 * 1024;

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException("FANUCPRG.BIN: " + message); }
    private static uint U32(byte[] b, int o)
    { return ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3]; }
    private static int U16(byte[] b, int o) { return (b[o] << 8) | b[o + 1]; }
    private static void Put32(byte[] b, int o, uint v)
    { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    private static void Put16(byte[] b, int o, int v)
    { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }
    private static byte[] ReadAt(FileStream f, long offset, int count)
    {
        Require(offset >= 0 && offset <= f.Length - count, "\u043E\u0431\u0440\u0430\u0449\u0435\u043D\u0438\u0435 \u0437\u0430 \u043F\u0440\u0435\u0434\u0435\u043B\u044B \u0444\u0430\u0439\u043B\u0430.");
        byte[] b = new byte[count]; f.Position = offset;
        int n = 0;
        while (n < count)
        { int got = f.Read(b, n, count - n); if (got == 0) throw new EndOfStreamException(); n += got; }
        return b;
    }
    private static byte Checksum(byte[] block, int index)
    {
        uint v = (uint)index;
        byte sum = (byte)(v ^ (v >> 8) ^ (v >> 16) ^ (v >> 24));
        foreach (byte b in block) sum ^= b;
        return sum;
    }
    private static int BlocksFor(int length)
    { return 1 + (int)((Math.Max(0L, (long)length - 466) + 499) / 500); }

    public static FanucImage Read(string path)
    {
        using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Require(f.Length >= DataOffset + 512 && f.Length <= 2147483648L, "\u043D\u0435\u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u043C\u044B\u0439 \u0440\u0430\u0437\u043C\u0435\u0440 \u043A\u043E\u043D\u0442\u0435\u0439\u043D\u0435\u0440\u0430.");
            byte[] h = ReadAt(f, 0, 512), mirror = ReadAt(f, 512, 512);
            Require(U32(h, 0) == 0x336699cc && U32(mirror, 0) == U32(h, 0) && U32(mirror, 4) == U32(h, 4), "\u043D\u0435\u0432\u0435\u0440\u043D\u0430\u044F \u0441\u0438\u0433\u043D\u0430\u0442\u0443\u0440\u0430 \u0438\u043B\u0438 \u0438\u0434\u0435\u043D\u0442\u0438\u0444\u0438\u043A\u0430\u0442\u043E\u0440.");
            Require(U32(h, 8) == 65, "\u043E\u0431\u043D\u043E\u0432\u043B\u0435\u043D\u0438\u0435 \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u0442 \u043A\u043E\u043D\u0442\u0435\u0439\u043D\u0435\u0440\u044B \u043D\u0430 63 \u0437\u0430\u043F\u0438\u0441\u0438; \u0432\u044B\u0431\u0440\u0430\u043D \u0434\u0440\u0443\u0433\u043E\u0439 \u0444\u043E\u0440\u043C\u0430\u0442.");
            int capacity = checked((int)U32(h, 12));
            Require(capacity > 0 && DataOffset + 512L * capacity == f.Length, "\u0440\u0430\u0437\u043C\u0435\u0440 \u0444\u0430\u0439\u043B\u0430 \u043D\u0435 \u0441\u043E\u043E\u0442\u0432\u0435\u0442\u0441\u0442\u0432\u0443\u0435\u0442 \u0437\u0430\u0433\u043E\u043B\u043E\u0432\u043A\u0443.");
            uint highBlock = U32(h, 16), highEntry = U32(h, 28);
            Require(highBlock <= capacity && highEntry >= 2 && highEntry <= 65, "\u043F\u043E\u0432\u0440\u0435\u0436\u0434\u0435\u043D\u044B \u0433\u0440\u0430\u043D\u0438\u0446\u044B \u043A\u0430\u0442\u0430\u043B\u043E\u0433\u0430.");
            Require(U32(h, 24) <= highBlock && U32(h, 36) >= 2 && U32(h, 36) <= 65, "\u043F\u043E\u0432\u0440\u0435\u0436\u0434\u0435\u043D\u044B \u0441\u0447\u0451\u0442\u0447\u0438\u043A\u0438.");
            // These bytes belong to a pending transaction journal. Never discard one.
            for (int i = 8; i < mirror.Length; i++) Require(mirror[i] == 0, "\u0435\u0441\u0442\u044C \u043D\u0435\u0437\u0430\u0432\u0435\u0440\u0448\u0451\u043D\u043D\u0430\u044F \u0442\u0440\u0430\u043D\u0437\u0430\u043A\u0446\u0438\u044F; \u043E\u0442\u043A\u0440\u043E\u0439\u0442\u0435 \u0438 \u0441\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u0435 \u0444\u0430\u0439\u043B \u0448\u0442\u0430\u0442\u043D\u043E\u0439 \u0443\u0442\u0438\u043B\u0438\u0442\u043E\u0439.");
            for (int i = 40; i < h.Length; i++) Require(h[i] == 0, "\u043D\u0435\u0438\u0437\u0432\u0435\u0441\u0442\u043D\u043E\u0435 \u0440\u0430\u0441\u0448\u0438\u0440\u0435\u043D\u0438\u0435 \u0437\u0430\u0433\u043E\u043B\u043E\u0432\u043A\u0430.");
            byte[] catalog = ReadAt(f, 0x400, 65 * 64);
            Require(U32(catalog, 64) == 0xa0000000, "\u043D\u0435\u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u043C\u0430\u044F \u043A\u043E\u0440\u043D\u0435\u0432\u0430\u044F \u043F\u0430\u043F\u043A\u0430.");
            FanucImage image = new FanucImage(); image.CapacityBlocks = capacity; image.Serial = U32(h, 4);
            HashSet<int> entries = new HashSet<int>(), blocks = new HashSet<int>();
            HashSet<uint> numbers = new HashSet<uint>();
            uint nextEntry = U32(catalog, 64 + 12), previousEntry = 1;
            long totalText = 0;
            while (nextEntry != 0)
            {
                Require(nextEntry >= 2 && nextEntry < highEntry && entries.Add((int)nextEntry), "\u0446\u0438\u043A\u043B \u0438\u043B\u0438 \u043D\u0435\u0432\u0435\u0440\u043D\u0430\u044F \u0441\u0441\u044B\u043B\u043A\u0430 \u0432 \u043A\u0430\u0442\u0430\u043B\u043E\u0433\u0435.");
                int o = (int)nextEntry * 64;
                Require(U32(catalog, o) == 0xe0000000, "\u043E\u0431\u043D\u043E\u0432\u043B\u0435\u043D\u0438\u0435 \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u0442 \u0442\u043E\u043B\u044C\u043A\u043E \u0423\u041F \u0441 \u043D\u043E\u043C\u0435\u0440\u0430\u043C\u0438 O \u0432 \u043A\u043E\u0440\u043D\u0435, \u0431\u0435\u0437 \u0432\u043B\u043E\u0436\u0435\u043D\u043D\u044B\u0445 \u043F\u0430\u043F\u043E\u043A \u0438 \u0441\u043F\u0435\u0446\u0438\u0430\u043B\u044C\u043D\u044B\u0445 \u0430\u0442\u0440\u0438\u0431\u0443\u0442\u043E\u0432.");
                Require(U32(catalog, o + 4) == 0 && U32(catalog, o + 8) == previousEntry && U32(catalog, o + 20) == 0, "\u043D\u0435\u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u043C\u044B\u0435 \u0430\u0442\u0440\u0438\u0431\u0443\u0442\u044B \u0438\u043B\u0438 \u043F\u043E\u0432\u0440\u0435\u0436\u0434\u0451\u043D\u043D\u044B\u0435 \u0441\u0432\u044F\u0437\u0438 \u0437\u0430\u043F\u0438\u0441\u0438.");
                uint number = U32(catalog, o + 28);
                Require(number > 0 && number <= 99999999 && numbers.Add(number), "\u043D\u0435\u0432\u0435\u0440\u043D\u044B\u0439 \u0438\u043B\u0438 \u043F\u043E\u0432\u0442\u043E\u0440\u044F\u044E\u0449\u0438\u0439\u0441\u044F \u043D\u043E\u043C\u0435\u0440 \u0423\u041F.");
                int nameLength = 0;
                while (nameLength < 32 && catalog[o + 32 + nameLength] != 0) nameLength++;
                string name = Encoding.ASCII.GetString(catalog, o + 32, nameLength);
                Require(name == "O" + number.ToString(CultureInfo.InvariantCulture), "\u0438\u043C\u044F \u0437\u0430\u043F\u0438\u0441\u0438 \u043D\u0435 \u0441\u043E\u043E\u0442\u0432\u0435\u0442\u0441\u0442\u0432\u0443\u0435\u0442 \u043D\u043E\u043C\u0435\u0440\u0443 \u0423\u041F.");
                uint first = U32(catalog, o + 16);
                Require(first < highBlock, "\u043D\u0430\u0447\u0430\u043B\u044C\u043D\u044B\u0439 \u0431\u043B\u043E\u043A \u0437\u0430 \u0433\u0440\u0430\u043D\u0438\u0446\u0430\u043C\u0438 \u0434\u0430\u043D\u043D\u044B\u0445.");
                int current = (int)first, previous = -1, count = 0, declaredCount = 0, last = 0;
                long tail = -1;
                using (MemoryStream body = new MemoryStream())
                {
                    while (true)
                    {
                        Require(current >= 0 && current < highBlock && blocks.Add(current), "\u0446\u0438\u043A\u043B, \u043E\u0431\u0449\u0438\u0435 \u0431\u043B\u043E\u043A\u0438 \u0438\u043B\u0438 \u0432\u044B\u0445\u043E\u0434 \u0437\u0430 \u0433\u0440\u0430\u043D\u0438\u0446\u044B \u0423\u041F.");
                        byte[] block = ReadAt(f, DataOffset + 512L * current, 512);
                        Require(Checksum(block, current) == 0, "\u043A\u043E\u043D\u0442\u0440\u043E\u043B\u044C\u043D\u0430\u044F \u0441\u0443\u043C\u043C\u0430 \u0431\u043B\u043E\u043A\u0430 " + current + " \u043D\u0435 \u0441\u043E\u0432\u043F\u0430\u043B\u0430.");
                        Require(block[11] == 0 && U32(block, 0) == (previous < 0 ? 0U : unchecked((uint)(previous - current))), "\u043F\u043E\u0432\u0440\u0435\u0436\u0434\u0451\u043D \u043E\u0431\u0440\u0430\u0442\u043D\u044B\u0439 \u0443\u043A\u0430\u0437\u0430\u0442\u0435\u043B\u044C \u0431\u043B\u043E\u043A\u0430.");
                        int n = U16(block, 8), start = 12;
                        if (count == 0)
                        {
                            tail = (long)current + unchecked((int)U32(block, 12));
                            declaredCount = checked((int)U32(block, 16));
                            Require(declaredCount > 0 && declaredCount <= highBlock, "\u043D\u0435\u0432\u0435\u0440\u043D\u043E\u0435 \u0447\u0438\u0441\u043B\u043E \u0431\u043B\u043E\u043A\u043E\u0432 \u0423\u041F.");
                            // The length of the first numeric-program block includes a virtual
                            // nine-byte O header, which is stored in the catalog, not in text.
                            n -= 9; start = 28;
                            Require(n >= 0 && n <= 484, "\u043D\u0435\u0432\u0435\u0440\u043D\u0430\u044F \u0434\u043B\u0438\u043D\u0430 \u043F\u0435\u0440\u0432\u043E\u0433\u043E \u0431\u043B\u043E\u043A\u0430.");
                        }
                        else Require(n >= 0 && n <= 500, "\u043D\u0435\u0432\u0435\u0440\u043D\u0430\u044F \u0434\u043B\u0438\u043D\u0430 \u0431\u043B\u043E\u043A\u0430.");
                        totalText += n; Require(totalText <= MaxText, "\u0441\u0443\u043C\u043C\u0430\u0440\u043D\u044B\u0439 \u0442\u0435\u043A\u0441\u0442 \u0423\u041F \u043F\u0440\u0435\u0432\u044B\u0448\u0430\u0435\u0442 512 \u041C\u0411.");
                        body.Write(block, start, n); count++; last = current;
                        int delta = unchecked((int)U32(block, 4));
                        if (delta == 0) break;
                        Require(count < declaredCount, "\u0446\u0435\u043F\u043E\u0447\u043A\u0430 \u0434\u043B\u0438\u043D\u043D\u0435\u0435 \u0443\u043A\u0430\u0437\u0430\u043D\u043D\u043E\u0433\u043E \u0447\u0438\u0441\u043B\u0430 \u0431\u043B\u043E\u043A\u043E\u0432.");
                        previous = current;
                        long following = (long)current + delta;
                        Require(following >= 0 && following < highBlock, "\u043D\u0435\u0432\u0435\u0440\u043D\u044B\u0439 \u0441\u043B\u0435\u0434\u0443\u044E\u0449\u0438\u0439 \u0431\u043B\u043E\u043A.");
                        current = (int)following;
                    }
                    Require(count == declaredCount && tail == last, "\u043D\u0435 \u0441\u043E\u0432\u043F\u0430\u043B\u0438 \u0434\u043B\u0438\u043D\u0430 \u0446\u0435\u043F\u043E\u0447\u043A\u0438 \u0438\u043B\u0438 \u043F\u043E\u0441\u043B\u0435\u0434\u043D\u0438\u0439 \u0431\u043B\u043E\u043A.");
                    FanucProgram program = new FanucProgram(); program.Number = number;
                    program.Stamp = U32(catalog, o + 24); program.Body = body.ToArray();
                    image.Programs.Add(program);
                }
                previousEntry = nextEntry; nextEntry = U32(catalog, o + 12);
            }
            Require(image.Programs.Count + 2 == U32(h, 36) && blocks.Count == U32(h, 24), "\u0441\u0447\u0451\u0442\u0447\u0438\u043A\u0438 \u043D\u0435 \u0441\u043E\u0432\u043F\u0430\u0434\u0430\u044E\u0442 \u0441 \u0434\u0435\u0439\u0441\u0442\u0432\u0443\u044E\u0449\u0438\u043C\u0438 \u0423\u041F.");
            for (int i = 2; i < highEntry; i++)
                Require((U32(catalog, i * 64) & 0x80000000) == 0 || entries.Contains(i), "\u0435\u0441\u0442\u044C \u0434\u0435\u0439\u0441\u0442\u0432\u0443\u044E\u0449\u0438\u0435 \u0437\u0430\u043F\u0438\u0441\u0438 \u0432\u043D\u0435 \u043A\u043E\u0440\u043D\u044F; \u043E\u0431\u043D\u043E\u0432\u043B\u0435\u043D\u0438\u0435 \u043E\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D\u043E.");
            return image;
        }
    }

    public static FanucProgram FromNc(string path, string groupName)
    {
        Require(new FileInfo(path).Length <= MaxText, "\u0423\u041F \u043F\u0440\u0435\u0432\u044B\u0448\u0430\u0435\u0442 512 \u041C\u0411.");
        byte[] raw = File.ReadAllBytes(path);
        int offset = raw.Length >= 3 && raw[0] == 0xef && raw[1] == 0xbb && raw[2] == 0xbf ? 3 : 0;
        for (int i = offset; i < raw.Length; i++)
            Require(raw[i] == 9 || raw[i] == 10 || raw[i] == 13 || (raw[i] >= 32 && raw[i] <= 126),
                "\u0423\u041F \u00AB" + groupName + "\u00BB \u0441\u043E\u0434\u0435\u0440\u0436\u0438\u0442 \u043D\u0435-ASCII \u0441\u0438\u043C\u0432\u043E\u043B\u044B. \u0418\u0441\u043F\u0440\u0430\u0432\u044C\u0442\u0435 \u043A\u043E\u0434\u0438\u0440\u043E\u0432\u043A\u0443/\u043A\u043E\u043C\u043C\u0435\u043D\u0442\u0430\u0440\u0438\u0438 \u0432 \u043F\u043E\u0441\u0442\u0435; \u0442\u0435\u043A\u0441\u0442 \u0430\u0432\u0442\u043E\u043C\u0430\u0442\u0438\u0447\u0435\u0441\u043A\u0438 \u043D\u0435 \u043F\u043E\u0434\u043C\u0435\u043D\u044F\u0435\u0442\u0441\u044F.");
        string text = Encoding.ASCII.GetString(raw, offset, raw.Length - offset).Replace("\r\n", "\n").Replace('\r', '\n');
        Match header = Regex.Match(text, @"\A[ \t\n]*(?:%[ \t]*\n[ \t\n]*)?O([0-9]{1,8})(?=[ \t\n(]|$)");
        Require(header.Success, "\u0432 \u043D\u0430\u0447\u0430\u043B\u0435 \u00AB" + groupName + "\u00BB \u043D\u0443\u0436\u0435\u043D \u043D\u043E\u043C\u0435\u0440 O1\u2026O99999999 (\u043E\u0434\u043D\u0430 \u0423\u041F \u0432 \u0444\u0430\u0439\u043B\u0435).");
        uint number = UInt32.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
        Require(number != 0, "\u043D\u043E\u043C\u0435\u0440 O0 \u043D\u0435 \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u0442\u0441\u044F.");
        Match namedNumber = Regex.Match(groupName, @"^O([0-9]{1,8})$", RegexOptions.IgnoreCase);
        if (namedNumber.Success)
            Require(UInt32.Parse(namedNumber.Groups[1].Value, CultureInfo.InvariantCulture) == number,
                "\u043F\u0430\u043F\u043A\u0430 \u00AB" + groupName + "\u00BB, \u043D\u043E \u0432\u043D\u0443\u0442\u0440\u0438 \u0423\u041F \u2014 O" + number + ". \u0418\u0441\u043F\u0440\u0430\u0432\u044C\u0442\u0435 \u043D\u043E\u043C\u0435\u0440 \u0432 \u043F\u043E\u0441\u0442\u0435.");
        string body = text.Substring(header.Length);
        // Remove only the O-line separator and the external tape delimiter.
        int p = 0; while (p < body.Length && (body[p] == ' ' || body[p] == '\t')) p++;
        if (p < body.Length && body[p] == '\n') body = body.Substring(p + 1);
        Match closing = Regex.Match(body, @"(?m)^[ \t]*%[ \t\n]*\z");
        if (closing.Success) body = body.Substring(0, closing.Index);
        Require(body.Length > 0, "\u043F\u0443\u0441\u0442\u043E\u0439 \u0442\u0435\u043A\u0441\u0442 \u0423\u041F.");
        if (!body.EndsWith("\n", StringComparison.Ordinal)) body += "\n";
        bool comment = false; StringBuilder outside = new StringBuilder(), normalized = new StringBuilder();
        const string codeCharacters = "\n#&*+,-./0123456789:<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]_abcdefghijklmnopqrstuvwxyz";
        const string commentCharacters = "\n \"#$&'*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]_abcdefghijklmnopqrstuvwxyz";
        foreach (char c in body)
        {
            if (c == '(') { Require(!comment, "\u0432\u043B\u043E\u0436\u0435\u043D\u043D\u044B\u0435 \u043A\u043E\u043C\u043C\u0435\u043D\u0442\u0430\u0440\u0438\u0438 \u043D\u0435 \u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u044E\u0442\u0441\u044F."); comment = true; outside.Append(' '); normalized.Append(c); }
            else if (c == ')') { Require(comment, "\u043B\u0438\u0448\u043D\u044F\u044F \u0437\u0430\u043A\u0440\u044B\u0432\u0430\u044E\u0449\u0430\u044F \u0441\u043A\u043E\u0431\u043A\u0430."); comment = false; outside.Append(' '); normalized.Append(c); }
            else if (comment)
            {
                // Match Memory Card Program Tool v4 import conventions.
                // Tabs/percent are omitted; line breaks inside a comment become semicolons.
                if (c == '\t' || c == '%') continue;
                Require(commentCharacters.IndexOf(c) >= 0, "\u043D\u0435\u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u043C\u044B\u0439 \u0441\u0438\u043C\u0432\u043E\u043B \u0432 \u043A\u043E\u043C\u043C\u0435\u043D\u0442\u0430\u0440\u0438\u0438: " + c);
                normalized.Append(c == '\n' ? ';' : c); outside.Append(' ');
            }
            else
            {
                if (c == ' ' || c == '\t') continue;
                Require(c != '%', "\u043D\u0430\u0439\u0434\u0435\u043D \u0432\u043D\u0443\u0442\u0440\u0435\u043D\u043D\u0438\u0439 %, \u0432\u043E\u0437\u043C\u043E\u0436\u043D\u043E, \u0432 \u0444\u0430\u0439\u043B\u0435 \u043D\u0435\u0441\u043A\u043E\u043B\u044C\u043A\u043E \u0423\u041F.");
                Require(codeCharacters.IndexOf(c) >= 0, "\u043D\u0435\u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u043C\u044B\u0439 \u0441\u0438\u043C\u0432\u043E\u043B \u0432\u043D\u0435 \u043A\u043E\u043C\u043C\u0435\u043D\u0442\u0430\u0440\u0438\u044F: " + c);
                outside.Append(c); normalized.Append(c);
            }
        }
        Require(!comment, "\u043D\u0435\u0437\u0430\u043A\u0440\u044B\u0442\u044B\u0439 \u043A\u043E\u043C\u043C\u0435\u043D\u0442\u0430\u0440\u0438\u0439.");
        Require(!Regex.IsMatch(outside.ToString(), @"(?m)^[ \t]*(?:O[0-9]|:[0-9]|<)", RegexOptions.IgnoreCase), "\u0432 \u0444\u0430\u0439\u043B\u0435 \u043D\u0435\u0441\u043A\u043E\u043B\u044C\u043A\u043E \u043D\u043E\u043C\u0435\u0440\u043E\u0432/\u0438\u043C\u0435\u043D \u043F\u0440\u043E\u0433\u0440\u0430\u043C\u043C.");
        FanucProgram result = new FanucProgram(); result.Number = number;
        result.Body = Encoding.ASCII.GetBytes(normalized.ToString());
        DateTime time = File.GetLastWriteTime(path);
        Require(time.Year >= 2000 && time.Year <= 2096, "\u043D\u0435\u043F\u043E\u0434\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0435\u043C\u0430\u044F \u0434\u0430\u0442\u0430 \u0423\u041F.");
        result.Stamp = (uint)(((time.Year - 2000) << 25) | (time.Month << 21) | (time.Day << 16) |
            (time.Hour << 11) | (time.Minute << 5) | (time.Second / 2));
        return result;
    }

    public static FanucImage Combine(FanucImage existing, List<FanucProgram> incoming)
    {
        FanucImage result = new FanucImage();
        if (existing != null)
        { result.CapacityBlocks = existing.CapacityBlocks; result.Serial = existing.Serial; result.Programs.AddRange(existing.Programs); }
        HashSet<uint> seen = new HashSet<uint>();
        foreach (FanucProgram item in incoming)
        {
            Require(seen.Add(item.Number), "\u0432 \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0445 \u043F\u0430\u043F\u043A\u0430\u0445 \u043E\u0434\u0438\u043D\u0430\u043A\u043E\u0432\u044B\u0439 \u043D\u043E\u043C\u0435\u0440 " + item.Name + ".");
            int at = result.Programs.FindIndex(delegate(FanucProgram p) { return p.Number == item.Number; });
            if (at < 0) result.Programs.Add(item); else result.Programs[at] = item;
        }
        Require(result.Programs.Count <= MaxPrograms, "\u043F\u043E\u043B\u0443\u0447\u0438\u043B\u043E\u0441\u044C \u0431\u043E\u043B\u044C\u0448\u0435 63 \u0423\u041F. \u0420\u0430\u0437\u0434\u0435\u043B\u0438\u0442\u0435 \u0438\u0445 \u043D\u0430 \u043A\u043E\u043D\u0442\u0435\u0439\u043D\u0435\u0440\u044B.");
        return result;
    }

    public static int CapacityForMB(int sizeMB)
    {
        Require(sizeMB >= 1 && sizeMB <= 2048, "SizeMB \u0434\u043E\u043B\u0436\u0435\u043D \u0431\u044B\u0442\u044C \u043E\u0442 1 \u0434\u043E 2048.");
        // Same nominal program-size conversion and 2 GB ceiling as FANUC v4.
        // 1 MB remains accepted for compatibility with an existing v1.5 INI.
        return (int)Math.Min(0x3ffe08L, 1 + ((long)sizeMB * 1048576 - 466 + 499) / 500);
    }
    public static long FileLengthForMB(int sizeMB)
    { return DataOffset + 512L * CapacityForMB(sizeMB); }

    public static void Write(string path, FanucImage image, int sizeMB)
    {
        Require(image.Programs.Count > 0 && image.Programs.Count <= MaxPrograms, "\u043D\u0443\u0436\u043D\u043E \u043E\u0442 1 \u0434\u043E 63 \u0423\u041F.");
        int capacity = CapacityForMB(sizeMB);
        long textLength = 0; int used = 0;
        HashSet<uint> numbers = new HashSet<uint>();
        foreach (FanucProgram p in image.Programs)
        {
            Require(p.Number > 0 && p.Number <= 99999999 && numbers.Add(p.Number) && p.Body != null && p.Body.Length > 0, "\u043D\u0435\u0432\u0435\u0440\u043D\u0430\u044F \u0438\u043B\u0438 \u043F\u043E\u0432\u0442\u043E\u0440\u043D\u0430\u044F \u0437\u0430\u043F\u0438\u0441\u044C \u0423\u041F.");
            textLength += p.Body.Length; used = checked(used + BlocksFor(p.Body.Length));
        }
        Require(textLength <= MaxText, "\u0441\u0443\u043C\u043C\u0430\u0440\u043D\u044B\u0439 \u0442\u0435\u043A\u0441\u0442 \u0423\u041F \u043F\u0440\u0435\u0432\u044B\u0448\u0430\u0435\u0442 512 \u041C\u0411.");
        Require(used <= capacity, "\u0423\u041F \u043D\u0435 \u043F\u043E\u043C\u0435\u0449\u0430\u044E\u0442\u0441\u044F \u0432 \u0432\u044B\u0431\u0440\u0430\u043D\u043D\u044B\u0435 " + sizeMB + " \u041C\u0411 (\u043D\u0443\u0436\u043D\u043E " + used + " \u0431\u043B\u043E\u043A\u043E\u0432, \u0434\u043E\u0441\u0442\u0443\u043F\u043D\u043E " + capacity + "). \u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u0431\u043E\u043B\u044C\u0448\u0438\u0439 \u0440\u0430\u0437\u043C\u0435\u0440 BIN. \u041A\u043E\u043D\u0435\u0447\u043D\u044B\u0435 \u0444\u0430\u0439\u043B\u044B \u043D\u0435 \u0438\u0437\u043C\u0435\u043D\u0435\u043D\u044B.");
        Require(DataOffset + 512L * capacity <= 2147483648L, "\u043A\u043E\u043D\u0442\u0435\u0439\u043D\u0435\u0440 \u043F\u0440\u0435\u0432\u044B\u0441\u0438\u0442 2 \u0413\u0411.");
        uint serial = image.Serial;
        if (serial == 0) serial = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0) & 0x7fff7fff;
        byte[] header = new byte[DataOffset];
        Put32(header, 0, 0x336699cc); Put32(header, 4, serial); Put32(header, 8, 65);
        Put32(header, 12, (uint)capacity); Put32(header, 16, (uint)used); Put32(header, 24, (uint)used);
        Put32(header, 28, (uint)(image.Programs.Count + 2)); Put32(header, 36, (uint)(image.Programs.Count + 2));
        Put32(header, 512, 0x336699cc); Put32(header, 516, serial);
        Put32(header, 0x440, 0xa0000000); Put32(header, 0x44c, 2);
        int first = 0;
        for (int i = 0; i < image.Programs.Count; i++)
        {
            FanucProgram p = image.Programs[i]; int o = 0x400 + (i + 2) * 64;
            Put32(header, o, 0xe0000000); Put32(header, o + 8, (uint)(i + 1));
            Put32(header, o + 12, i + 1 == image.Programs.Count ? 0U : (uint)(i + 3));
            Put32(header, o + 16, (uint)first); Put32(header, o + 24, p.Stamp); Put32(header, o + 28, p.Number);
            byte[] name = Encoding.ASCII.GetBytes(p.Name); Buffer.BlockCopy(name, 0, header, o + 32, name.Length);
            first += BlocksFor(p.Body.Length);
        }
        using (FileStream f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            f.SetLength(DataOffset + 512L * capacity); f.Write(header, 0, header.Length);
            int absolute = 0;
            foreach (FanucProgram p in image.Programs)
            {
                int count = BlocksFor(p.Body.Length), consumed = 0;
                for (int i = 0; i < count; i++, absolute++)
                {
                    byte[] block = new byte[512]; int start = i == 0 ? 28 : 12;
                    int n = Math.Min(p.Body.Length - consumed, i == 0 ? 466 : 500);
                    Put32(block, 0, i == 0 ? 0U : UInt32.MaxValue); Put32(block, 4, i + 1 == count ? 0U : 1U);
                    Put16(block, 8, n + (i == 0 ? 9 : 0));
                    if (i == 0) { Put32(block, 12, (uint)(count - 1)); Put32(block, 16, (uint)count); }
                    Buffer.BlockCopy(p.Body, consumed, block, start, n); consumed += n;
                    block[10] = Checksum(block, absolute); f.Write(block, 0, block.Length);
                }
            }
            f.Flush(true);
        }
        FanucImage verify = Read(path);
        Require(verify.Programs.Count == image.Programs.Count, "\u043F\u0440\u043E\u0432\u0435\u0440\u043A\u0430 \u0441\u043E\u0437\u0434\u0430\u043D\u043D\u043E\u0433\u043E \u043A\u0430\u0442\u0430\u043B\u043E\u0433\u0430 \u043D\u0435 \u043F\u0440\u043E\u0448\u043B\u0430.");
        for (int i = 0; i < image.Programs.Count; i++)
        {
            FanucProgram a = image.Programs[i], b = verify.Programs[i];
            Require(a.Number == b.Number && a.Stamp == b.Stamp && BytesEqual(a.Body, b.Body), "\u043E\u0431\u0440\u0430\u0442\u043D\u043E\u0435 \u0447\u0442\u0435\u043D\u0438\u0435 \u0438\u0437\u043C\u0435\u043D\u0438\u043B\u043E \u0441\u043E\u0434\u0435\u0440\u0436\u0438\u043C\u043E\u0435 \u0423\u041F.");
        }
    }
    public static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
