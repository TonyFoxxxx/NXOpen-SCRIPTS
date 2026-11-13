"""Portable failure/coverage checks; these do not simulate NX geometry."""
import importlib.util
from pathlib import Path
import types
import unittest

SPEC = importlib.util.spec_from_file_location(
    "mirror_diagnostic", Path(__file__).resolve().parents[1] / "scripts/NX_Mirror_Project_Diagnostic.py")
M = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(M)


class DiagnosticTests(unittest.TestCase):
    def test_incomplete_native_array_is_not_treated_as_empty_geometry(self):
        with self.assertRaises(ValueError):
            M.counted((3, [10, 20]))
        self.assertEqual(M.counted((0, None)), [])

    def test_uf_struct_shapes(self):
        self.assertEqual(M.field({"PointAcs": [1, 2, 3]}, "point_acs"), [1, 2, 3])
        self.assertEqual(M.field(types.SimpleNamespace(Name="Tool Axis"), "name"), "Tool Axis")
        with self.assertRaises(ValueError):
            M.field({"name": "one", "Name": "two"}, "name")

    def test_runtime_enum_values_are_resolved_without_numeric_guesses(self):
        container = types.SimpleNamespace(Type3d=9001, Type3dLength=9002)
        self.assertEqual(M.enum_name(9002, container), "Type3dLength")
        self.assertEqual(M.enum_name(12345, container), "12345")

    def test_read_error_is_explicit_and_counted(self):
        reader = M.Inventory(None, None, None, None)
        def fail():
            raise RuntimeError("part unavailable")
        result = reader.read("P2", fail)
        self.assertIn("unavailable", result)
        self.assertEqual(reader.issues[0]["context"], "P2")
        self.assertEqual(sum(reader.issue_counts.values()), 1)

    def test_large_arrays_announce_omission(self):
        result = M.plain(list(range(M.MAX_ITEMS + 5)))
        self.assertEqual(result["omitted"], 5)
        self.assertEqual(result["total"], M.MAX_ITEMS + 5)

    def test_partial_part_never_accesses_geometry(self):
        class PartialPart:
            Tag = 7
            Leaf = "partial.prt"
            IsFullyLoaded = False
            IsModified = False
            @property
            def Bodies(self):
                raise AssertionError("must not inspect or load partial geometry")
        part = PartialPart()
        reader = M.Inventory(None, None, None, None)
        reader.part_id(part)
        result = reader.inspect_part(part)
        self.assertIn("Not inspected", result["content"])

    def test_repeated_prototypes_are_kept_as_separate_occurrences(self):
        prototype = types.SimpleNamespace(Tag=88)
        class Component:
            IsSuppressed = False
            def __init__(self, tag, children=()):
                self.Tag, self.Name = tag, "c" + str(tag)
                self.Prototype, self.children = prototype, list(children)
            def GetPosition(self):
                return [float(self.Tag), 0.0, 0.0], [1.0, 0.0, 0.0]
            def GetChildren(self):
                return self.children
        root = Component(1, (Component(2), Component(3)))
        reader = M.Inventory(None, None, None, None)
        nodes = reader.walk_components(root)
        self.assertEqual(len(nodes), 3)
        self.assertEqual(len(reader.part_objects), 1)
        self.assertNotEqual(nodes[1]["position"], nodes[2]["position"])

    def test_unknown_parameter_type_does_not_use_an_arbitrary_getter(self):
        class Param:
            def AskRequiredParams(self, tag):
                return 1, [42]
            def AskParamAttributes(self, index):
                return {"name": "Clearance point", "type": 123456}
            def AskParamStatus(self, tag, index):
                return "overridden"
            def __getattr__(self, name):
                raise AssertionError("must not guess parameter reader: " + name)
        reader = M.Inventory(None, types.SimpleNamespace(Param=Param()), None, None)
        result = reader.parameters(types.SimpleNamespace(Tag=10))
        self.assertIn("unavailable", result["spatial_and_reference_values"][0]["value"])

    def test_spatial_value_keeps_its_native_coordinates(self):
        class Param:
            def AskRequiredParams(self, tag):
                return 1, [42]
            def AskParamAttributes(self, index):
                return {"name": "Start Point", "type": 99}
            def AskParamStatus(self, tag, index):
                return "overridden"
            def Ask3dValue(self, tag, index):
                return (25.0, -7.0, 4.0)
        uf_module = types.SimpleNamespace(Param=types.SimpleNamespace(Type=types.SimpleNamespace(Type3dLength=99)))
        reader = M.Inventory(None, types.SimpleNamespace(Param=Param()), None, uf_module)
        result = reader.parameters(types.SimpleNamespace(Tag=10))
        self.assertEqual(result["spatial_and_reference_values"][0]["value"], [25.0, -7.0, 4.0])


if __name__ == "__main__":
    unittest.main()
