"""Exercise CAM boundary pointers and parameter statuses through compiled C, without NX."""
import ctypes
from enum import Enum
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
        self.native.set_boundary = self.native.bind('UF_CAMBND_set_boundary_plane', (p,ctypes.POINTER(ctypes.c_double),ctypes.POINTER(ctypes.c_double)))
        self.native.model_update = self.native.bind('UF_MODL_update', ())
        self.native.param_status = self.native.bind('UF_PARAM_ask_param_status', (u,i,ip))
        self.parameter = self.native.bind('fixture_param_status', (u,i,i,i), None)
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
        trial.plane = M.MirrorPlane((2,4,6),(1,2,3))
        trial.boundary_planes, trial.current_boundaries = {}, {}
        trial.moved_boundary_count = 0
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

    def test_manual_plane_transfers_and_preserves_boundary_settings(self):
        self.reset(0,0)
        trial = self.trial()
        before = trial.geometry_snapshot(collect=True)
        trial.transfer_boundary_planes(before)
        self.assertEqual(trial.geometry_snapshot(), before)
        trial.verify_boundary_planes()
        self.assertEqual(self.stat(4), 1)
        self.assertEqual(trial.moved_boundary_count, 1)
        self.assertEqual(before[(100,1,'boundary',1)], (0,1,2,1,0,1))

    def test_inherited_boundary_is_written_once(self):
        self.reset(0,0)
        trial = self.trial()
        second = type(trial.nodes[0])()
        second.Tag, second.Name = 101, 'SECOND_OPERATION'
        trial.nodes.append(second)
        before = trial.geometry_snapshot(collect=True)
        self.assertEqual(len(trial.boundary_planes), 2)
        trial.transfer_boundary_planes(before)
        self.assertEqual(self.stat(4), 1)
        trial.geometry_snapshot()
        trial.verify_boundary_planes()

    def test_already_moved_plane_is_not_reflected_twice(self):
        self.reset(0,0)
        trial = self.trial()
        before = trial.geometry_snapshot(collect=True)
        origin, axes = trial.boundary_planes[(100,1,1)]
        self.native.set_boundary_plane(self.handle(), trial.plane.point(origin), M.reflected_boundary_frame(axes,trial.plane))
        trial.transfer_boundary_planes(before)
        trial.geometry_snapshot()
        trial.verify_boundary_planes()
        self.assertEqual(self.stat(4), 2)

    def test_setter_failure_stops_trial(self):
        self.reset(0,0)
        trial = self.trial()
        before = trial.geometry_snapshot(collect=True)
        self.native.bind('fixture_write_error',(ctypes.c_int,),None)(12345)
        with self.assertRaises(M.NativeApiError) as caught:
            trial.transfer_boundary_planes(before)
        self.assertEqual(caught.exception.context, 'UF_CAMBND_set_boundary_plane')
        self.assertEqual(caught.exception.code, 12345)

    def test_failed_plane_update_is_detected_by_readback(self):
        self.reset(0,0)
        trial = self.trial()
        before = trial.geometry_snapshot(collect=True)
        self.native.set_boundary_plane = lambda *args: None
        trial.transfer_boundary_planes(before)
        trial.geometry_snapshot()
        with self.assertRaisesRegex(RuntimeError,'Не подтверждено'):
            trial.verify_boundary_planes()

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

    def parameter_trial(self, rows):
        # A plain Enum reproduces NX's non-integer ParamStatusMemberType.
        status_type = Enum('ParamStatusMemberType', 'Default Inherited Overridden')
        kind_type = Enum('Type', 'TypeTag Type3d Type3dLength')
        trial = object.__new__(M.Trial)
        trial.native, trial.plane = self.native, M.MirrorPlane([0,0,0], [1,0,0])
        trial.nodes = [types.SimpleNamespace(Tag=tag, Name='NODE_' + str(tag))
                       for tag in sorted({key[0] for key in rows})]
        trial.root = trial.nodes[0]
        trial.frames, trial.refs, trial.mcs_expected = {}, {}, {}
        trial.mcs_bindings, trial.raw_spatial = [], []
        trial.reference = lambda tag: trial.refs.setdefault(tag, object())
        kinds = {index: kind for (_,index), (kind,_,_,_) in rows.items()}
        for (tag,index), (_,state,_,error) in rows.items():
            self.parameter(tag,index,state,error)
        trial.nx = types.SimpleNamespace(UF=types.SimpleNamespace(Param=types.SimpleNamespace(Type=kind_type)))
        trial.uf = types.SimpleNamespace(
            Param=types.SimpleNamespace(
                AskRequiredParams=lambda tag: (sum(t == tag for t,_ in rows), [i for t,i in rows if t == tag]),
                AskParamAttributes=lambda index: types.SimpleNamespace(type=getattr(kind_type,kinds[index]), name='P_' + str(index)),
                AskTagValue=lambda tag,index: rows[(tag,index)][2],
                AskParamStatus=Mock(return_value=status_type.Overridden),
                Ask3dValue=Mock(side_effect=lambda tag,index: rows[(tag,index)][2])),
            Csys=types.SimpleNamespace(
                AskCsysInfo=Mock(side_effect=lambda tag: (tag + 100,[1,2,3])),
                AskMatrixValues=lambda tag: [1,0,0,0,1,0,0,0,1]))
        return trial

    def test_parameter_status_reads_native_states_and_unsigned_tags(self):
        for state in (0,1,2):
            with self.subTest(state=state):
                self.parameter(0xf0000042,59,state,0)
                self.assertEqual(self.native.parameter_status(0xf0000042,59), state)

    def test_invalid_unknown_and_unwritten_parameter_statuses_stop(self):
        for state, message in ((3,'недопустим'), (7,'Неизвестный статус'), (-1,'Неизвестный статус')):
            with self.subTest(state=state):
                self.parameter(100,59,state,0)
                with self.assertRaisesRegex(RuntimeError,message):
                    self.native.parameter_status(100,59)

    def test_parameter_api_errors_are_not_treated_as_inherited(self):
        for code in (1345005,12345):
            with self.subTest(code=code):
                self.parameter(100,59,1,code)
                with self.assertRaises(M.NativeApiError) as caught:
                    self.native.parameter_status(100,59)
                self.assertEqual(caught.exception.code,code)
                self.assertEqual(caught.exception.context,'UF_PARAM_ask_param_status')

    def test_parameter_capture_preserves_root_local_and_inherited_mcs_rcs(self):
        trial = self.parameter_trial({
            (1,59): ('TypeTag',0,81,0), (1,60): ('TypeTag',0,81,0),
            (2,59): ('TypeTag',2,82,0), (2,60): ('TypeTag',1,81,0),
            (3,59): ('TypeTag',1,82,0), (3,60): ('TypeTag',1,81,0),
            (4,59): ('TypeTag',0,81,0), (4,60): ('TypeTag',2,83,0),
        })
        trial.capture_parameters()
        self.assertEqual([(node.Tag,index,tag) for node,index,tag in trial.mcs_bindings],
                         [(1,59,81),(1,60,81),(2,59,82),(4,60,83)])
        self.assertEqual(trial.mcs_expected, {(1,59):81,(1,60):81,(2,59):82,(2,60):81,
                                            (3,59):82,(3,60):81,(4,59):81,(4,60):83})
        self.assertEqual(set(trial.frames), {81,82,83})
        self.assertEqual(trial.uf.Csys.AskCsysInfo.call_count, 3)
        trial.uf.Param.AskParamStatus.assert_not_called()

    def test_parameter_capture_reports_only_nonzero_local_spatial_values(self):
        trial = self.parameter_trial({
            (1,59): ('TypeTag',0,81,0),
            (1,100): ('Type3d',2,[1,0,0],0),
            (1,101): ('Type3dLength',2,[0,2,0],0),
            (1,102): ('Type3d',1,[3,0,0],0),
            (1,103): ('Type3dLength',0,[0,4,0],0),
            (1,104): ('Type3d',2,[0,0,0],0),
        })
        trial.capture_parameters()
        self.assertEqual(trial.raw_spatial, ['NODE_1: P_100 = [1, 0, 0]', 'NODE_1: P_101 = [0, 2, 0]'])
        self.assertEqual([call.args for call in trial.uf.Param.Ask3dValue.call_args_list],
                         [(1,100),(1,101),(1,104)])
        trial.uf.Param.AskParamStatus.assert_not_called()

    def test_parameter_failure_identifies_node_and_stops_before_transform(self):
        trial = self.parameter_trial({(1,59): ('TypeTag',3,81,0)})
        trial.geometry_snapshot = Mock(return_value={})
        trial.capture_topology = Mock()
        self.native.transform_entities = Mock()
        with self.assertRaisesRegex(RuntimeError,'недопустим'):
            trial.run_in_absolute_wcs()
        trial.capture_topology.assert_not_called()
        self.native.transform_entities.assert_not_called()
        self.assertIn('NODE_1',trial.stage)
        self.assertIn('параметр 59',trial.stage)


if __name__ == '__main__':
    unittest.main()
