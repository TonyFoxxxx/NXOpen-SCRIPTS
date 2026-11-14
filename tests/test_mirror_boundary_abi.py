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
        self.native.param_definer = self.native.bind('UF_PARAM_ask_param_definer', (u,i,ctypes.POINTER(u)))
        self.native.so_test = self.native.bind('UF_SO_is_so', (u,ctypes.POINTER(ctypes.c_ubyte)))
        self.native.so_parents = self.native.bind('UF_SO_ask_parents', (u,i,ip,pp))
        self.native.so_error = self.native.bind('UF_SO_ask_update_error_code', (u,ip))
        dp = ctypes.POINTER(ctypes.c_double)
        self.native.xform_point = self.native.bind('UF_SO_set_point_of_xform', (u,dp))
        self.native.xform_axes = self.native.bind('UF_SO_set_xy_direction_of_xform', (u,dp,dp))
        self.xform_read = self.native.bind('fixture_xform_read', (dp,), None)
        self.xform_calls = self.native.bind('fixture_xform_calls', (i,))
        self.xform_failure = self.native.bind('fixture_xform_failure', (i,i), None)
        self.parameter = self.native.bind('fixture_param_status', (u,i,i,i), None)
        self.definer = self.native.bind('fixture_param_definer', (u,i,u,i), None)
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

    def test_smart_parents_uses_unsigned_tag_array_and_frees_it(self):
        self.assertEqual(self.native.smart_parents(4), (0xf0000001,29,0xf0000003))
        self.assertEqual(self.stat(0),1)

    def test_smart_empty_and_dumb_are_distinct_and_errors_are_fatal(self):
        self.assertIsNone(self.native.smart_parents(1))
        self.assertEqual(self.native.smart_parents(3),())
        with self.assertRaisesRegex(RuntimeError,'777'):
            self.native.smart_parents(2)
        self.assertEqual(self.stat(0),0)

    def test_smart_parent_array_is_freed_even_on_failed_read(self):
        with self.assertRaises(M.NativeApiError):
            self.native.smart_parents(5)
        self.assertEqual(self.stat(0),1)

    def test_numeric_xform_writes_unsigned_tag_and_double_point_and_axes(self):
        plane = M.MirrorPlane([4,-3,2],[1,2,3])
        origin = plane.point([7,8,9])
        axes = M.reflected_frame([1,0,0,0,1,0,0,0,1],plane)
        self.native.set_fixed_xform(0xf0000050,origin,axes)
        values = (ctypes.c_double * 9)()
        self.xform_read(values)
        self.assertEqual(list(values),origin + axes[:6])
        self.assertEqual([self.xform_calls(i) for i in (0,1)],[1,1])

    def test_numeric_xform_setter_rejects_smart_or_invalid_frames_before_writing(self):
        identity = [1,0,0,0,1,0,0,0,1]
        for tag,origin,axes in ((3,[0,0,0],identity),(0xf0000050,[float('nan'),0,0],identity),
                               (0xf0000050,[0,0,0],[1,0,0,0,1,0,0,0,-1])):
            with self.subTest(tag=tag,origin=origin,axes=axes), self.assertRaises((RuntimeError,ValueError)):
                self.native.set_fixed_xform(tag,origin,axes)
        self.assertEqual([self.xform_calls(i) for i in (0,1)],[0,0])

    def test_either_xform_setter_error_propagates_for_transaction_rollback(self):
        for step,context in ((0,'UF_SO_set_point_of_xform'),(1,'UF_SO_set_xy_direction_of_xform')):
            with self.subTest(step=step):
                self.reset(1,0)
                self.xform_failure(step,12345)
                with self.assertRaises(M.NativeApiError) as caught:
                    self.native.set_fixed_xform(0xf0000050,[1,2,3],[1,0,0,0,1,0,0,0,1])
                self.assertEqual(caught.exception.context,context)
                self.assertEqual(caught.exception.code,12345)
                self.assertEqual([self.xform_calls(i) for i in (0,1)],[1,step])

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
        kind_type = Enum('Type', 'TypeTag Type3d Type3dLength TypeLogical')
        trial = object.__new__(M.Trial)
        trial.native, trial.plane = self.native, M.MirrorPlane([0,0,0], [1,0,0])
        trial.nodes = [types.SimpleNamespace(Tag=tag, Name='NODE_' + str(tag))
                       for tag in sorted({key[0] for key in rows})]
        trial.root = trial.nodes[0]
        trial.frames, trial.refs, trial.mcs_expected = {}, {}, {}
        trial.mcs_sources = {}
        trial.mcs_links = {}
        trial.mcs_definitions, trial.mcs_verified_definitions = {}, {}
        trial.mcs_fixed_xforms = {}
        trial.check_cam_builders = lambda: None
        trial.model_history = lambda: {}
        trial.capture_stock = lambda: None
        trial.mcs_bindings, trial.raw_spatial = [], []
        trial.reference = lambda tag: trial.refs.setdefault(tag, object())
        kinds = {index: kind for (_,index), (kind,_,_,_) in rows.items()}
        for (tag,index), (_,state,_,error) in rows.items():
            self.parameter(tag,index,state,error)
            if state == 1 and index in (59,60):
                source = next(t for (t,i), (_,s,value,_) in rows.items()
                              if i == index and s != 1 and value == rows[(tag,index)][2])
                self.definer(tag,index,source,0)
        trial.nx = types.SimpleNamespace(UF=types.SimpleNamespace(Param=types.SimpleNamespace(Type=kind_type)))
        trial.uf = types.SimpleNamespace(
            Param=types.SimpleNamespace(
                AskRequiredParams=lambda tag: (sum(t == tag for t,_ in rows), [i for t,i in rows if t == tag]),
                AskParamAttributes=lambda index: types.SimpleNamespace(type=getattr(kind_type,kinds[index]), name='P_' + str(index)),
                AskTagValue=lambda tag,index: rows[(tag,index)][2],
                AskLogicalValue=lambda tag,index: rows[(tag,index)][2],
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
                         [(1,59,81),(1,60,81),(2,59,82),(4,59,81),(4,60,83)])
        self.assertEqual(trial.mcs_expected, {(1,59):81,(1,60):81,(2,59):82,(2,60):81,
                                            (3,59):82,(3,60):81,(4,59):81,(4,60):83})
        self.assertEqual(set(trial.frames), {81,82,83})
        self.assertEqual(trial.uf.Csys.AskCsysInfo.call_count, 3)
        trial.uf.Param.AskParamStatus.assert_not_called()

    def test_native_parameter_definer_keeps_unsigned_tag_and_reports_error(self):
        self.parameter(0xf0000042,59,1,0)
        self.definer(0xf0000042,59,0xe0000001,0)
        self.assertEqual(self.native.parameter_definer(0xf0000042,59),0xe0000001)
        self.definer(0xf0000042,59,0,1345005)
        with self.assertRaises(M.NativeApiError) as caught:
            self.native.parameter_definer(0xf0000042,59)
        self.assertEqual(caught.exception.context,'UF_PARAM_ask_param_definer')
        self.assertEqual(caught.exception.code,1345005)

    def test_hidden_parameter_provider_is_captured_without_overriding_child(self):
        trial = self.parameter_trial({(1,59): ('TypeTag',2,81,0),(2,59): ('TypeTag',1,81,0)})
        trial.nodes = trial.nodes[1:]
        trial.nx.TaggedObjectManager = types.SimpleNamespace(
            GetTaggedObject=lambda tag: types.SimpleNamespace(Tag=tag,Name='PROVIDER'))
        trial.capture_parameters()
        self.assertEqual([(node.Tag,index,tag) for node,index,tag in trial.mcs_bindings],[(1,59,81)])
        self.assertEqual(trial.mcs_sources[(2,59)][:2],(1,1))
        self.assertEqual(set(trial.mcs_expected),{(1,59),(2,59)})

    def test_zero_self_and_cyclic_inheritance_providers_stop_before_transform(self):
        for provider in (0,2):
            with self.subTest(provider=provider):
                trial = self.parameter_trial({(1,59): ('TypeTag',2,81,0),(2,59): ('TypeTag',1,81,0)})
                self.definer(2,59,provider,0)
                with self.assertRaisesRegex(RuntimeError,'Не определён источник'):
                    trial.capture_parameters()
        trial = self.parameter_trial({(1,59): ('TypeTag',2,81,0),(2,59): ('TypeTag',1,81,0),(3,59): ('TypeTag',1,81,0)})
        self.definer(2,59,3,0)
        self.definer(3,59,2,0)
        with self.assertRaisesRegex(RuntimeError,'Цикл наследования'):
            trial.capture_parameters()

    def transferred_mcs_trial(self):
        rows = {(1,41): ('TypeLogical',2,True,0),
                (1,59): ('TypeTag',0,81,0), (1,60): ('TypeTag',0,81,0),
                (2,59): ('TypeTag',1,81,0), (2,60): ('TypeTag',1,81,0),
                (3,59): ('TypeTag',2,82,0), (4,59): ('TypeTag',0,81,0)}
        trial = self.parameter_trial(rows)
        trial.capture_parameters()
        trial.mcs_definitions = {tag: ({tag: None}, None) for tag in trial.frames}
        values = {key: row[2] for key,row in rows.items()}
        frames, matrices = {}, {}
        def create_matrix(axes):
            tag = 1000 + len(matrices)
            matrices[tag] = list(axes)
            return tag
        def create_csys(origin, matrix):
            tag = 2000 + len(frames)
            frames[tag] = (list(origin),matrix)
            return tag
        def set_value(tag, index, value):
            self.parameter(tag,index,2,0)
            values[(tag,index)] = value
        def get_value(tag, index):
            if self.native.parameter_status(tag,index) == 1:
                return get_value(self.native.parameter_definer(tag,index),index)
            return values[(tag,index)]
        trial.uf.Csys = types.SimpleNamespace(CreateMatrix=create_matrix,CreateCsys=create_csys,
            AskCsysInfo=lambda tag: (frames[tag][1],frames[tag][0]),
            AskMatrixValues=lambda tag: matrices[tag],
            SetOrigin=lambda tag, origin: frames.__setitem__(tag, (origin, frames.get(tag, (None,None))[1])),
            EditMatrixOfObject=lambda tag, matrix: frames.__setitem__(tag, (frames[tag][0],matrix)))
        trial.uf.Param.SetTagValue = Mock(side_effect=set_value)
        trial.uf.Param.AskTagValue = get_value
        replacement = trial.transfer_mcs()
        return trial,replacement,values,frames,matrices

    def test_default_and_local_frames_move_while_children_keep_inheritance(self):
        trial,replacement,values,frames,matrices = self.transferred_mcs_trial()
        trial.verify_mcs(replacement)
        written = [call.args[:2] for call in trial.uf.Param.SetTagValue.call_args_list]
        self.assertEqual(written,[(1,59),(1,60),(4,59)])
        self.assertEqual(replacement[82],82, 'An existing local CSYS keeps its tag')
        self.assertEqual(self.native.parameter_status(2,59),1)
        self.assertEqual(self.native.parameter_definer(2,59),1)
        self.assertEqual(self.native.parameter_status(4,59),2)
        self.assertIn('MCS',trial.stage)

    def test_equivalent_csys_tags_are_accepted_only_with_preserved_inheritance(self):
        trial,replacement,values,frames,matrices = self.transferred_mcs_trial()
        frames[9000] = frames[replacement[81]]
        values[(1,59)] = 9000
        values[(1,60)] = 9000
        trial.verify_mcs(replacement)
        self.assertEqual(trial.uf.Param.AskTagValue(2,59),9000)
        self.assertEqual(self.native.parameter_status(2,59),1)

    def test_expected_tag_with_wrong_origin_or_axes_is_rejected_with_details(self):
        for change in ('origin','axes'):
            with self.subTest(change=change):
                trial,replacement,values,frames,matrices = self.transferred_mcs_trial()
                frame = replacement[81]
                origin,matrix = frames[frame]
                if change == 'origin':
                    frames[frame] = ([origin[0]+1,origin[1],origin[2]],matrix)
                else:
                    matrices[matrix] = [1.0,0.0,0.0,0.0,1.0,0.0,0.0,0.0,1.0]
                with self.assertRaisesRegex(RuntimeError,'не совпадают начало или оси') as caught:
                    trial.verify_mcs(replacement)
                report = str(caught.exception)
                for detail in ('NODE_1','MCS','Начало ожидаемое','Оси ожидаемые','фактическая='):
                    self.assertIn(detail,report)
                self.assertIn('Проверка MCS/RCS',trial.stage)
                self.assertIn('параметр 59',trial.stage)

    def test_matching_coordinates_do_not_hide_detached_or_reparented_inheritance(self):
        for mode in ('local','other_provider'):
            with self.subTest(mode=mode):
                trial,replacement,values,frames,matrices = self.transferred_mcs_trial()
                if mode == 'local':
                    self.parameter(2,59,2,0)
                    values[(2,59)] = replacement[81]
                else:
                    self.definer(2,59,3,0)
                with self.assertRaisesRegex(RuntimeError,'изменилось наследование') as caught:
                    trial.verify_mcs(replacement)
                self.assertIn('NODE_2',str(caught.exception))

    def test_failed_frame_assignment_is_not_repaired_by_overriding_children(self):
        trial,replacement,values,frames,matrices = self.transferred_mcs_trial()
        self.parameter(1,59,0,0)
        with self.assertRaisesRegex(RuntimeError,'изменилось наследование'):
            trial.verify_mcs(replacement)
        self.assertFalse(any(call.args[0] == 2 for call in trial.uf.Param.SetTagValue.call_args_list))

    def test_rcs_link_flag_is_preserved_and_a_change_is_rejected(self):
        trial,replacement,values,frames,matrices = self.transferred_mcs_trial()
        self.assertEqual(trial.mcs_links,{1:True})
        trial.verify_mcs(replacement)
        trial.uf.Param.AskLogicalValue = Mock(return_value=False)
        with self.assertRaisesRegex(RuntimeError,'Изменился параметр связи RCS с MCS'):
            trial.verify_mcs(replacement)
        self.assertIn('Проверка связи RCS с MCS',trial.stage)

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
