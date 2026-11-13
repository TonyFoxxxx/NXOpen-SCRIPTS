import ctypes
import importlib.util
import math
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch


SPEC = importlib.util.spec_from_file_location("mirror_trial", Path(__file__).parents[1] / "scripts/NX_Mirror_Project.py")
M = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(M)


class GeometryTests(unittest.TestCase):
    def test_offset_reflection_is_involution_and_fixes_plane(self):
        for axis in range(3):
            for offset in (-42.5, 0.0, 17.0):
                point = [8.0, -12.0, 36.0]
                reflected = M.reflect(point, axis, offset)
                self.assertEqual(point, M.reflect(reflected, axis, offset))
                on_plane = list(point)
                on_plane[axis] = offset
                self.assertEqual(on_plane, M.reflect(on_plane, axis, offset))

    def test_bounding_box_encloses_reflected_corners(self):
        box = [-8.0, 3.0, -2.0, 19.0, 41.0, 10.0]
        for axis in range(3):
            expected = M.reflected_box(box, axis, 13.5)
            corners = [M.reflect([box[x], box[1+y], box[2+z]], axis, 13.5)
                       for x in (0, 3) for y in (0, 3) for z in (0, 3)]
            actual = [min(p[i] for p in corners) for i in range(3)] + [max(p[i] for p in corners) for i in range(3)]
            self.assertEqual(expected, actual)

    def test_both_reported_setups_and_rotated_frame_remain_right_handed(self):
        c, s = math.cos(0.7), math.sin(0.7)
        frames = [[1,0,0,0,1,0,0,0,1], [1,0,0,0,-1,0,0,0,-1], [c,s,0,-s,c,0,0,0,1]]
        for frame in frames:
            for axis in range(3):
                mirrored = M.reflected_frame(frame, axis)
                x, y, z = mirrored[:3], mirrored[3:6], mirrored[6:]
                self.assertAlmostEqual(M.dot(M.cross(x, y), z), 1.0)
                self.assertTrue(M.close_values(M.reflected_frame(mirrored, axis), frame))
                self.assertEqual(z, M.reflect(frame[6:], axis))

    def test_invalid_mcs_and_nonfinite_verification_are_rejected(self):
        for frame in ([1,0,0,0,1,0,0,0,-1], [2,0,0,0,1,0,0,0,1]):
            with self.assertRaises(ValueError):
                M.reflected_frame(frame, 0)
        self.assertFalse(M.close_values([float("nan")], [float("nan")]))


class CopyTests(unittest.TestCase):
    def test_copy_preserves_source_and_existing_destination(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / "тест.prt"
            source.write_bytes(b"PRT\x00\xff" * 1024)
            existing = Path(folder) / "тест_MIRROR_TEST_001.prt"
            existing.write_bytes(b"existing result")
            target, original = M.copy_exclusive(str(source))
            self.assertEqual(Path(target).name, "тест_MIRROR_TEST_002.prt")
            self.assertEqual(existing.read_bytes(), b"existing result")
            self.assertEqual(source.read_bytes(), Path(target).read_bytes())
            self.assertEqual(original, M.digest(str(source)))
            self.assertEqual(len(list(Path(folder).iterdir())), 3)

    def test_failed_copy_verification_removes_only_its_own_output(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / "original.prt"
            source.write_bytes(b"source")
            with patch.object(M, "digest", return_value="mismatched"):
                with self.assertRaisesRegex(RuntimeError, "сумма"):
                    M.copy_exclusive(str(source))
            self.assertEqual([source], list(Path(folder).iterdir()))
            self.assertEqual(source.read_bytes(), b"source")


class NativeArrayTests(unittest.TestCase):
    def reader(self):
        reader = object.__new__(M.NativeCam)
        reader.freed = []
        reader.free = lambda ptr: reader.freed.append(ptr.value)
        def check(code, context):
            if code:
                raise RuntimeError(str(code))
        reader.check = check
        return reader

    def test_opaque_handles_keep_64_bits_and_array_is_freed(self):
        reader = self.reader()
        array = (ctypes.c_void_p * 2)(0x1122334455667788, 0x2233445566778899)
        def ask(tag, kind, count, data):
            ctypes.cast(count, ctypes.POINTER(ctypes.c_int))[0] = 2
            ctypes.cast(data, ctypes.POINTER(ctypes.c_void_p))[0] = ctypes.addressof(array)
            return 0
        self.assertEqual(reader.pointers(ask, 10, 0), list(array))
        self.assertEqual(reader.freed, [ctypes.addressof(array)])

    def test_collectors_use_32_bit_tag_stride_not_pointer_stride(self):
        reader = self.reader()
        array = (ctypes.c_uint32 * 3)(100, 200, 0xf0000001)
        def ask(item, count, data):
            ctypes.cast(count, ctypes.POINTER(ctypes.c_int))[0] = 3
            ctypes.cast(data, ctypes.POINTER(ctypes.c_void_p))[0] = ctypes.addressof(array)
            return 0
        reader.collector = ask
        self.assertEqual(reader.collector_entities(55), list(array))
        self.assertEqual(len(reader.freed), 1)

    def test_error_frees_allocated_array(self):
        reader = self.reader()
        array = (ctypes.c_void_p * 1)(10)
        def ask(count, data):
            ctypes.cast(data, ctypes.POINTER(ctypes.c_void_p))[0] = ctypes.addressof(array)
            return 99
        with self.assertRaises(RuntimeError):
            reader.pointers(ask)
        self.assertEqual(len(reader.freed), 1)

    def test_null_pointer_with_nonzero_count_is_rejected_before_dereference(self):
        reader = self.reader()
        def ask(count, data):
            ctypes.cast(count, ctypes.POINTER(ctypes.c_int))[0] = 1
            return 0
        with self.assertRaisesRegex(RuntimeError, "массив"):
            reader.pointers(ask)
        self.assertEqual(reader.freed, [])

    def test_custom_point_records_and_container_are_both_freed(self):
        reader = self.reader()
        records = [(ctypes.c_double * 7)(), (ctypes.c_double * 7)()]
        array = (ctypes.c_void_p * 2)(*(ctypes.addressof(x) for x in records))
        def ask(tag, kind, count, data):
            ctypes.cast(count, ctypes.POINTER(ctypes.c_int))[0] = 2
            ctypes.cast(data, ctypes.POINTER(ctypes.c_void_p))[0] = ctypes.addressof(array)
            return 0
        reader.custom = ask
        self.assertEqual(reader.custom_count(123, 1), 2)
        self.assertEqual(reader.freed, [ctypes.addressof(x) for x in records] + [ctypes.addressof(array)])

    def test_native_matrix_must_pass_independent_point_validation(self):
        reader = self.reader()
        reader.mirror_matrix = lambda origin, normal, matrix: 0
        def broken_map(point, matrix, result):
            for index in range(3):
                result[index] = point[index]
        reader.map_point = broken_map
        with self.assertRaisesRegex(RuntimeError, "Матрица NX"):
            reader.reflection_matrix(0, 17.0)


class TransactionTests(unittest.TestCase):
    def test_dirty_source_is_rejected_before_copying(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / "source.prt"
            source.write_bytes(b"prt")
            part = types.SimpleNamespace(Tag=1, FullPath=str(source), IsModified=True, IsFullyLoaded=True,
                ComponentAssembly=types.SimpleNamespace(RootComponent=None))
            session = types.SimpleNamespace(Parts=types.SimpleNamespace(Display=part, Work=part))
            with patch.object(M, "copy_exclusive", side_effect=AssertionError("must not copy")):
                with self.assertRaisesRegex(RuntimeError, "несохранённые"):
                    M.execute(session, None, None, 0, 0)
            self.assertEqual(len(list(Path(folder).iterdir())), 1)

    def run_with_trial(self, fails):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / "source.prt"
            source.write_bytes(b"prt-binary")
            original = types.SimpleNamespace(Tag=1, FullPath=str(source), IsModified=False, IsFullyLoaded=True,
                ComponentAssembly=types.SimpleNamespace(RootComponent=None))
            events = []
            class Parts:
                Display = original
                Work = original
                def OpenBaseDisplay(self, path):
                    self.Display = types.SimpleNamespace(Tag=2, FullPath=path)
                    status = types.SimpleNamespace(NumberUnloadedParts=0, Dispose=lambda: events.append("dispose"))
                    return self.Display, status
                def SetWork(self, part):
                    self.Work = part
            session = types.SimpleNamespace(Parts=Parts(),
                SetUndoMark=lambda *a: 55,
                UndoToMark=lambda mark, name: events.append(("undo", mark)),
                DeleteUndoMark=lambda mark, name: events.append(("delete-mark", mark)))
            nx = types.SimpleNamespace(Session=types.SimpleNamespace(MarkVisibility=types.SimpleNamespace(Visible=1)))
            class Trial:
                stage = "reflection"
                def __init__(self, *args):
                    self.part = args[3]
                def run(self):
                    self.assert_copy()
                    if fails:
                        raise RuntimeError("kernel rejected reflection")
                def assert_copy(self):
                    if self.part.Tag != 2:
                        raise AssertionError("source mutated")
            with patch.object(M, "Trial", Trial):
                report = M.execute(session, nx, None, 0, 0)
            self.assertEqual(source.read_bytes(), b"prt-binary")
            target = Path(folder) / "source_MIRROR_TEST_001.prt"
            self.assertEqual(target.read_bytes(), b"prt-binary")
            return events, report

    def test_kernel_failure_undoes_copy_and_does_not_save(self):
        events, report = self.run_with_trial(True)
        self.assertIn(("undo", 55), events)
        self.assertIn("ТЕСТ НЕ ПРОЙДЕН", report)
        self.assertIn("kernel rejected reflection", report)

    def test_success_keeps_visible_undo_and_requires_manual_save(self):
        events, report = self.run_with_trial(False)
        self.assertNotIn(("undo", 55), events)
        self.assertNotIn(("delete-mark", 55), events)
        self.assertIn("ЕЩЁ НЕ СОХРАНЁН", report)
        self.assertNotIn("ТЕСТ НЕ ПРОЙДЕН", report)


if __name__ == "__main__":
    unittest.main()
