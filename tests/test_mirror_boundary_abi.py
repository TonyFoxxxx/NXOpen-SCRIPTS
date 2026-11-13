"""Exercise CAM boundary pointers through a compiled C library, without NX."""
import ctypes
import importlib.util
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import Mock


ROOT = Path(__file__).parents[1]
SPEC = importlib.util.spec_from_file_location('mirror_boundary', ROOT / 'scripts/NX_Mirror_Project.py')
M = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(M)


class BoundaryAbiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        compiler = shutil.which('cc') or shutil.which('gcc') or shutil.which('clang')
        if not compiler or sys.platform == 'win32' or ctypes.sizeof(ctypes.c_void_p) != 8:
            raise unittest.SkipTest('The C ABI probe requires a 64-bit POSIX C compiler')
        cls.folder = tempfile.TemporaryDirectory()
        cls.addClassCleanup(cls.folder.cleanup)
        output = Path(cls.folder.name) / 'boundary.so'
        link = '-dynamiclib' if sys.platform == 'darwin' else '-shared'
        subprocess.run([compiler, '-std=c11', '-Wall', '-Wextra', '-Werror', link, '-fPIC',
                        str(ROOT / 'tests/fixtures/CamBoundaryAbi.c'), '-o', str(output)],
                       check=True, capture_output=True, text=True, timeout=30)
        cls.library = ctypes.CDLL(str(output))

    def setUp(self):
        self.native = object.__new__(M.NativeCam)
        self.native.libs = [self.library]
        u, i, p = ctypes.c_uint32, ctypes.c_int, ctypes.c_void_p
        ip, pp = ctypes.POINTER(i), ctypes.POINTER(p)
        self.native.free = self.native.bind('UF_free', (p,), None)
        self.native.boundaries = self.native.bind('UF_CAMBND_ask_boundaries', (u,i,ip,pp))
        self.native.boundary_data = self.native.bind('UF_CAMBND_ask_boundary_data', (p,ctypes.POINTER(M._BoundaryData)))
        self.native.members = self.native.bind('UF_CAMBND_ask_boundary_items', (p,ip,pp))
        self.native.member_entity = self.native.bind('UF_CAMBND_ask_item_entity', (p,ctypes.POINTER(u)))
        self.native.fail_message = lambda code, buffer: setattr(buffer, 'value', b'C ABI probe error')
        self.reset = self.native.bind('fixture_reset', (i,i), None)
        self.stat = self.native.bind('fixture_stat', (i,))
        self.layout = self.native.bind('fixture_layout', (i,), ctypes.c_size_t)
        self.reset(1,0)

    def tearDown(self):
        self.assertEqual(self.stat(1), 0, 'Only returned arrays may be freed')
        self.assertEqual(self.stat(2), 0, 'All returned arrays must be freed')

    def handle(self):
        return self.native.pointers(self.native.boundaries, 100, 1)[0]

    def trial(self):
        class Operation:
            pass
        class PlanarMilling(Operation):
            Tag, Name = 100, 'PLSK_CHIST_Z0'
        def empty_items(tag, kind, count, data):
            ctypes.cast(count, ctypes.POINTER(ctypes.c_int))[0] = 0
            ctypes.cast(data, ctypes.POINTER(ctypes.c_void_p))[0] = None
            return 0
        self.native.items = empty_items
        self.native.custom_count = lambda *a: 0
        trial = object.__new__(M.Trial)
        trial.native, trial.nodes = self.native, [PlanarMilling()]
        trial.refs, trial.unavailable = {}, set()
        trial.reference = lambda tag: trial.refs.setdefault(tag, 'edge')
        trial.nx = types.SimpleNamespace(CAM=types.SimpleNamespace(Operation=Operation))
        trial.uf = types.SimpleNamespace(Cambnd=types.SimpleNamespace(
            AskBoundaryData=Mock(side_effect=AssertionError('Never mix native handles with the Python wrapper'))))
        return trial

    def test_structure_size_offsets_and_outputs_match_compiled_c(self):
        self.assertEqual(ctypes.sizeof(M._BoundaryData), self.layout(0))
        self.assertEqual(self.layout(0), 128)
        for index, (name, _) in enumerate(M._BoundaryData._fields_, 1):
            self.assertEqual(getattr(M._BoundaryData, name).offset, self.layout(index), name)
        data = M._BoundaryData()
        self.assertEqual(self.native.boundary_data(self.handle(), ctypes.byref(data)), 0)
        self.assertEqual((data.boundary_type,data.plane_type,data.material_side), (1,1,2))
        self.assertEqual(list(data.origin), [100,101,102])
        self.assertEqual(list(data.matrix), list(range(200,209)))
        self.assertEqual((data.ignore_holes,data.ignore_islands,data.ignore_chamfers), (1,0,1))
        self.assertIsNone(data.app_data)

    def test_64_bit_handle_survives_array_free_and_member_read(self):
        handle = self.handle()
        self.assertGreater(handle, 0xffffffff)
        self.assertEqual(self.stat(0), 1)
        self.assertEqual(self.native.boundary_plane_type(handle), 1)
        members = self.native.pointers(self.native.members, handle)
        self.assertEqual(self.native.entity(self.native.member_entity, members[0]), 0xf0000042)
        self.assertEqual(self.stat(0), 2)

    def test_blank_snapshot_reads_native_boundary_and_preserves_member_references(self):
        trial = self.trial()
        before = trial.geometry_snapshot(collect=True)
        self.assertEqual(before[(100,1)][4:], ((0xf0000042,),((0xf0000042,),)))
        self.assertIn(0xf0000042, trial.refs)
        self.assertEqual(before, trial.geometry_snapshot())
        trial.uf.Cambnd.AskBoundaryData.assert_not_called()
        self.assertEqual(self.stat(3), 2)

    def test_manual_plane_is_still_rejected_before_member_processing(self):
        self.reset(0,0)
        trial = self.trial()
        with self.assertRaisesRegex(RuntimeError, 'заданной плоскостью'):
            trial.geometry_snapshot(collect=True)
        self.assertIn('PLSK_CHIST_Z0 [PlanarMilling]/Blank / граница 1 из 1 / параметры', trial.stage)
        self.assertEqual(self.stat(0), 1)

    def test_boundary_data_error_is_fatal_even_for_wrong_type(self):
        self.reset(1,1345005)
        with self.assertRaises(M.NativeApiError) as caught:
            self.trial().geometry_snapshot(collect=True)
        self.assertEqual(caught.exception.code, 1345005)
        self.assertEqual(caught.exception.context, 'UF_CAMBND_ask_boundary_data')

    def test_null_handle_and_unknown_plane_stop_before_geometry_changes(self):
        with self.assertRaisesRegex(RuntimeError, 'пустой дескриптор'):
            self.native.boundary_plane_type(None)
        self.assertEqual(self.stat(3), 0)
        self.reset(7,0)
        with self.assertRaisesRegex(RuntimeError, 'неизвестный тип плоскости 7'):
            self.native.boundary_plane_type(self.handle())


if __name__ == '__main__':
    unittest.main()
