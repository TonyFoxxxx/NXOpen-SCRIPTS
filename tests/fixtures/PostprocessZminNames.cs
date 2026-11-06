// Production naming, conflict allocation and Apply are appended by the Python test.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NXOpen;
using NXOpen.UF;
using CamObject = NXOpen.CAM.CAMObject;
using Tag = System.Int32;

internal static class World
{
    internal static List<CamObject> Objects = new List<CamObject>();
    internal static string Refuse;
    internal static int Writes;
    internal static void Reset(params string[] names)
    {
        Objects.Clear(); Refuse = null; Writes = 0;
        for (int i = 0; i < names.Length; i++) Objects.Add(new NXOpen.CAM.Operation { Tag = i + 1, Name = names[i] });
    }
}
namespace NXOpen.CAM
{
    public class CAMObject { public int Tag; public string Name; }
    public class Operation : CAMObject
    {
        public void SetName(string name)
        {
            if (name == World.Refuse) throw new InvalidOperationException("injected failure");
            foreach (CAMObject other in World.Objects)
                if (other != this && String.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("occupied name: " + name);
            Name = name; World.Writes++;
        }
    }
}
namespace NXOpen
{
    public class Session
    {
        public enum MarkVisibility { Invisible }
        public struct UndoMarkId { internal string[] Names; }
        public UndoMarkId SetUndoMark(MarkVisibility visibility, string title)
        {
            List<string> names = new List<string>();
            foreach (CamObject obj in World.Objects) names.Add(obj.Name);
            return new UndoMarkId { Names = names.ToArray() };
        }
        public void UndoToMark(UndoMarkId mark, string title)
        { for (int i = 0; i < World.Objects.Count; i++) World.Objects[i].Name = mark.Names[i]; }
        public void DeleteUndoMark(UndoMarkId mark, string title) { }
    }
}
namespace NXOpen.UF
{
    public class UFSession
    {
        public class Navigator { public void Refresh() { } }
        public Navigator UiOnt = new Navigator();
        public static UFSession GetUFSession() { return new UFSession(); }
    }
}
internal static class ScriptInfo { internal static string WindowTitle(string text) { return text; } }
internal static class ZminGeometry
{
    internal static double Finite(double value)
    {
        if (Double.IsNaN(value) || Double.IsInfinity(value)) throw new ArgumentException("nonfinite");
        return value;
    }
}
internal static class NameChecks
{
    private static int count;
    private static void Check(bool condition, string label)
    { count++; if (!condition) throw new Exception(label); }
    private static void Equal(List<string> actual, List<string> expected)
    {
        Check(actual.Count == expected.Count, "name count");
        for (int i = 0; i < actual.Count; i++) Check(actual[i] == expected[i], actual[i] + " != " + expected[i]);
    }
    private static List<string> Current()
    {
        List<string> names = new List<string>();
        foreach (CamObject obj in World.Objects) names.Add(obj.Name);
        return names;
    }
    private static void Apply(params double[] depths)
    {
        OperationZmin plan = OperationZmin.ForTest(depths);
        plan.Apply(new Session());
    }
    public static int Main()
    {
        try
        {
            // GENERATED_CASES
            World.Reset("MILL_Z-8_COPY", "MILL_Z-8_COPY_COPY", "MILL_Z-6");
            World.Objects.Add(new CamObject { Tag = 99, Name = "mill_1_z-6" });
            Apply(-6, -6);
            Equal(Current(), new List<string> { "MILL_2_Z-6", "MILL_3_Z-6", "MILL_Z-6", "mill_1_z-6" });
            int writes = World.Writes;
            Apply(-6, -6);
            Check(World.Writes == writes, "rerun changed allocated names");

            World.Reset("MILL_Z-6", "MILL_Z-7");
            Apply(-7, -6);
            Equal(Current(), new List<string> { "MILL_Z-7", "MILL_Z-6" });

            World.Reset("MILL_COPY", "MILL_COPY_COPY");
            World.Refuse = "MILL_1_Z-6";
            bool failed = false;
            try { Apply(-6, -6); } catch (InvalidOperationException) { failed = true; }
            Check(failed, "injected write failure was ignored");
            Equal(Current(), new List<string> { "MILL_COPY", "MILL_COPY_COPY" });
            Console.WriteLine("Production C# Zmin naming checks: " + count + " passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

// INJECTED_METHODS
    internal static OperationZmin ForTest(double[] depths)
    {
        OperationZmin result = new OperationZmin();
        result.scope = "selection"; result.total = depths.Length;
        result.allObjects.AddRange(World.Objects);
        for (int i = 0; i < depths.Length; i++)
        {
            var operation = (NXOpen.CAM.Operation)World.Objects[i];
            string target = NameWithZmin(operation.Name, depths[i]);
            if (target == operation.Name) result.unchanged++;
            else result.planned.Add(new Rename { Operation = operation, Old = operation.Name, New = target });
        }
        result.RemoveConflicts();
        return result;
    }

