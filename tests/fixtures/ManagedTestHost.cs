using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Entry point wrapper for managed test executables (including tests launched outside the suite).
internal static class ManagedTestHost
{
    [DllImport("kernel32.dll", ExactSpelling=true)]
    private static extern int WerGetFlags(IntPtr process, out uint flags);
    [DllImport("kernel32.dll", ExactSpelling=true)]
    private static extern int WerSetFlags(uint flags);

    public static int Run(Func<int> test)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                uint flags;
                Marshal.ThrowExceptionForHR(WerGetFlags(new IntPtr(-1), out flags));
                Marshal.ThrowExceptionForHR(WerSetFlags(flags | 0x20)); // WER_FAULT_REPORTING_NO_UI for this process
            }
            return test();
        }
        catch (Exception error)
        {
            return Record(error);
        }
    }

    public static int Record(Exception error)
    {
        string diagnostic = DateTime.UtcNow.ToString("o") + Environment.NewLine + error;
        Console.Error.WriteLine(diagnostic);
        try
        {
            string directory = Environment.GetEnvironmentVariable("NXOPEN_TEST_LOG_DIR");
            if (String.IsNullOrEmpty(directory)) directory = Path.Combine(Path.GetTempPath(), "NXOpen-SCRIPTS", "test-logs");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "managed-" + Guid.NewGuid().ToString("N") + ".log");
            File.WriteAllText(path, diagnostic + Environment.NewLine, new UTF8Encoding(false));
            Console.Error.WriteLine("Managed diagnostic: " + path);
        }
        catch (Exception logError) { Console.Error.WriteLine("Could not write managed diagnostic: " + logError.Message); }
        return 1;
    }
}
