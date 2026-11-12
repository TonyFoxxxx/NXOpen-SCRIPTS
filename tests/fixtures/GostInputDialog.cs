using System;
using System.Reflection;
using R = GostRuntimeForms;

internal static class GostInputDialogChecks
{
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); }

    private static object Field(object target, string name)
    { return target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }

    private static object Control(object form, string name)
    { return ((Array)R.Call(R.Get(form, "Controls"), "Find", name, true)).GetValue(0); }

    [STAThread]
    public static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            foreach (AssemblyName reference in Assembly.GetExecutingAssembly().GetReferencedAssemblies())
                Check(!reference.Name.StartsWith("System.Windows.Forms")
                    && !reference.Name.StartsWith("System.Drawing")
                    && reference.Name != "System.Private.Windows.Core", "UI compile-time dependency: " + reference.Name);

            for (int pass = 0; pass < 4; pass++)
            {
                bool accept = pass % 2 == 0;
                object nativeForm;
                using (GostTextInputForm input = new GostTextInputForm("GOST input checks"))
                {
                    nativeForm = Field(input, "form");
                    Check(input.FontPath.EndsWith("GOST_A.FON"), "default font path");
                    Check(input.Inscription == "25_12_\u0410\u0411\u0412", "default inscription");
                    Check(input.HeightValue == 5 && input.AngleValue == 0, "default dimensions");
                    object height = Control(nativeForm, "heightValue"), angle = Control(nativeForm, "angleValue");
                    Check((decimal)R.Get(height, "Minimum") == .1m && (decimal)R.Get(height, "Maximum") == 1000m, "height bounds");
                    Check((decimal)R.Get(angle, "Minimum") == -360m && (decimal)R.Get(angle, "Maximum") == 360m, "rotation bounds");
                    Check((int)R.Get(Control(nativeForm, "inscription"), "MaxLength") == 250, "text limit");
                    object picker = R.New("OpenFileDialog");
                    try
                    {
                        R.Set(picker, "Filter", "GOST A (*.fon)|*.fon");
                        Check(picker.GetType().GetMethod("ShowDialog", new Type[] { R.FormType("IWin32Window") }) != null, "owned font picker");
                    }
                    finally { R.Dispose(picker); }

                    // Exercise real modal accept/cancel paths without visible windows.
                    R.Set(nativeForm, "Opacity", 0.0); R.Set(nativeForm, "ShowInTaskbar", false);
                    Exception callbackError = null;
                    R.On(nativeForm, "Shown", delegate
                    {
                        try
                        {
                            R.Set(Control(nativeForm, "fontPath"), "Text", "C:\\Fonts\\GOST_A.FON");
                            R.Set(Control(nativeForm, "inscription"), "Text", "125-12-\u0410\u0411\u0412");
                            R.Set(height, "Value", .125m); R.Set(angle, "Value", -90.25m);
                            Check(input.HeightValue == .125 && input.AngleValue == -90.25, "entered numeric values");
                            Check(input.Inscription == "125-12-\u0410\u0411\u0412" && input.FontPath == "C:\\Fonts\\GOST_A.FON", "entered text values");
                            Check(Object.ReferenceEquals(R.Get(nativeForm, accept ? "AcceptButton" : "CancelButton"), Control(nativeForm, accept ? "create" : "cancel")), "keyboard button binding");
                            R.Call(Control(nativeForm, accept ? "create" : "cancel"), "PerformClick");
                        }
                        catch (Exception ex) { callbackError = ex; R.Call(nativeForm, "Close"); }
                    });
                    Check(input.ShowDialog() == accept, "dialog result");
                    if (callbackError != null) throw callbackError;
                }
                Check((bool)R.Get(nativeForm, "IsDisposed"), "form resources released");
            }
            Console.WriteLine("GOST input: defaults, edits, accept/cancel and 4 repeated dialogs passed");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
