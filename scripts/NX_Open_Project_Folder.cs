// Открыть папку проекта
// SCRIPT_VERSION: V1.01
// Working file: NX_Open_Project_Folder.cs
// Siemens NX / Designcenter, Windows. C# journal, NXOpen.
// Opens the folder containing the current work part (.prt).
// If no work part is available, uses the displayed part.
// Does not save or modify the part.

using System;
using System.Diagnostics;
using System.IO;
using NXOpen;

public class NX_Open_Project_Folder
{
    private const string SCRIPT_NAME = "Открыть папку проекта";
    private const string SCRIPT_VERSION = "V1.01";
    private const string Title = SCRIPT_NAME + " — " + SCRIPT_VERSION;

    public static void Main(string[] args)
    {
        try
        {
            Session session = Session.GetSession();
            Part part = session.Parts.Work;

            if (part == null)
                part = session.Parts.Display;

            if (part == null)
            {
                ShowMessage("Нет открытого проекта.\nОткройте файл .prt и запустите скрипт снова.",
                    NXMessageBox.DialogType.Information);
                return;
            }

            string partPath = part.FullPath;

            // Never resolve an unnamed/relative part against NX's working directory.
            if (String.IsNullOrEmpty(partPath) || !Path.IsPathRooted(partPath))
            {
                ShowMessage("У текущего проекта нет полного пути к файлу на диске.\n"
                    + "Сохраните проект в папку на диске и запустите скрипт снова.",
                    NXMessageBox.DialogType.Warning);
                return;
            }

            string folderPath = Path.GetDirectoryName(partPath);

            if (String.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                ShowMessage("Папка проекта не найдена или недоступна:\n"
                    + folderPath + "\n\nПроверьте доступ к диску или сетевой папке.",
                    NXMessageBox.DialogType.Warning);
                return;
            }

            // Open the directory through Windows Shell; no command-line quoting needed.
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = folderPath;
            startInfo.UseShellExecute = true;
            startInfo.Verb = "open";

            Process process = Process.Start(startInfo);
            if (process != null)
                process.Dispose();
        }
        catch (Exception ex)
        {
            ShowMessage("Не удалось открыть папку проекта.\n\n" + ex.Message,
                NXMessageBox.DialogType.Error);
        }
    }

    private static void ShowMessage(string message, NXMessageBox.DialogType type)
    {
        UI.GetUI().NXMessageBox.Show(Title, type, message);
    }

    public static int GetUnloadOption(string dummy)
    {
        return (int)Session.LibraryUnloadOption.Immediately;
    }
}
