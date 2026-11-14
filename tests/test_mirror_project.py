import ctypes
import importlib.util
import math
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import Mock, patch


SPEC = importlib.util.spec_from_file_location("mirror_trial", Path(__file__).parents[1] / "scripts/NX_Mirror_Project.py")
M = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(M)


def axis_plane(axis=0, offset=0.0):
    origin, normal = [0.0]*3, [0.0]*3
    origin[axis], normal[axis] = offset, 1.0
    return M.MirrorPlane(origin, normal)


class GeometryTests(unittest.TestCase):
    def test_boundary_coordinates_and_open_material_side_follow_reflection(self):
        angle = 0.7
        axes = [math.cos(angle),math.sin(angle),0,-math.sin(angle),math.cos(angle),0,0,0,1]
        plane = M.MirrorPlane([4,2,7],[1,2,3])
        mirrored = M.reflected_boundary_frame(axes,plane)
        x, y, z = mirrored[:3], mirrored[3:6], mirrored[6:]
        self.assertAlmostEqual(M.dot(M.cross(x,y),z),1)
        origin = [7,-4,2]
        delta = [5*axes[i]+3*axes[i+3] for i in range(3)]
        point = [a+b for a,b in zip(origin,delta)]
        new_delta = [a-b for a,b in zip(plane.point(point),plane.point(origin))]
        self.assertTrue(M.close_values([M.dot(new_delta,x),M.dot(new_delta,y),M.dot(new_delta,z)],[5,3,0]))
        old_left = M.cross(axes[6:],axes[:3])
        self.assertTrue(M.close_values(M.cross(z,x),plane.vector(old_left)))
        self.assertTrue(M.close_values(M.reflected_boundary_frame(mirrored,plane),axes))

    def test_oblique_plane_reflection_and_opposite_normal(self):
        plane = M.MirrorPlane([1, 1, 0], [1, 1, 0])
        opposite = M.MirrorPlane([1, 1, 0], [-7, -7, 0])
        point = [3, 0, 5]
        self.assertTrue(M.close_values(plane.point(point), [2, -1, 5]))
        self.assertTrue(M.close_values(plane.point(plane.point(point)), point))
        self.assertTrue(M.close_values(plane.point(point), opposite.point(point)))
        frame = [1,0,0,0,1,0,0,0,1]
        mirrored = M.reflected_frame(frame, plane)
        self.assertAlmostEqual(M.dot(M.cross(mirrored[:3], mirrored[3:6]), mirrored[6:]), 1.0)

    def test_tilted_box_is_checked_in_fixed_plane_frame(self):
        plane = M.MirrorPlane([7,-4,2], [1,2,3])
        basis = [plane.basis[i:i+3] for i in (0,3,6)]
        corner, lengths = [12,-2,9], [4,8,3]
        before = M.local_box(plane, corner, basis, lengths)
        reflected_directions = [plane.vector(row) for row in basis]
        after = M.local_box(plane, plane.point(corner), reflected_directions, lengths)
        self.assertTrue(M.close_values(after, M.reflected_box(before, 0, 0)))

    def test_invalid_plane_normal_is_rejected(self):
        for normal in ([0,0,0], [float('nan'),0,1]):
            with self.assertRaises(ValueError):
                M.MirrorPlane([0,0,0], normal)

    def test_offset_reflection_is_involution_and_fixes_plane(self):
        for axis in range(3):
            for offset in (-42.5, 0.0, 17.0):
                point = [8.0, -12.0, 36.0]
                reflected = axis_plane(axis, offset).point(point)
                self.assertEqual(point, axis_plane(axis, offset).point(reflected))
                on_plane = list(point)
                on_plane[axis] = offset
                self.assertEqual(on_plane, axis_plane(axis, offset).point(on_plane))

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
                mirrored = M.reflected_frame(frame, axis_plane(axis))
                x, y, z = mirrored[:3], mirrored[3:6], mirrored[6:]
                self.assertAlmostEqual(M.dot(M.cross(x, y), z), 1.0)
                self.assertTrue(M.close_values(M.reflected_frame(mirrored, axis_plane(axis)), frame))
                self.assertEqual(z, M.reflect(frame[6:], axis))

    def test_invalid_mcs_and_nonfinite_verification_are_rejected(self):
        for frame in ([1,0,0,0,1,0,0,0,-1], [2,0,0,0,1,0,0,0,1]):
            with self.assertRaises(ValueError):
                M.reflected_frame(frame, axis_plane())
        self.assertFalse(M.close_values([float("nan")], [float("nan")]))


class CopyTests(unittest.TestCase):
    def test_repeated_copy_suffix_is_flattened_but_active_source_contents_are_used(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            original = root / 'part.prt'
            original.write_bytes(b'original project')
            for number in (1,2):
                (root / ('part_MIRROR_TEST_' + str(number).zfill(3) + '.prt')).write_bytes(b'previous')
            source = root / 'part_MIRROR_TEST_002_MIRROR_TEST_001.prt'
            source.write_bytes(b'active copy contents')
            target,_ = M.copy_exclusive(str(source))
            self.assertEqual(Path(target).name,'part_MIRROR_TEST_003.prt')
            self.assertEqual(Path(target).read_bytes(),b'active copy contents')
            self.assertEqual(original.read_bytes(),b'original project')
            self.assertEqual((root / 'part_MIRROR_TEST_002.prt').read_bytes(),b'previous')
            self.assertTrue(source.exists())

    def test_name_of_loaded_but_deleted_part_is_not_reused(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / 'part.prt'
            source.write_bytes(b'project')
            loaded = Path(folder) / 'part_MIRROR_TEST_001.prt'
            target,_ = M.copy_exclusive(str(source),[str(loaded)])
            self.assertEqual(Path(target).name,'part_MIRROR_TEST_002.prt')
            self.assertFalse(loaded.exists())
            self.assertEqual(Path(target).read_bytes(),source.read_bytes())

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
            reader.reflection_matrix(axis_plane(0, 17.0))


class ChannelTests(unittest.TestCase):
    def test_snapshot_retains_provider_geometry_and_marks_unsupported_channel(self):
        reader = object.__new__(M.NativeCam)
        reader.items, reader.boundaries, reader.item_entity = object(), object(), object()
        def pointers(function, tag, kind):
            if function is reader.items and kind == 0:
                if tag == 1:
                    raise M.NativeApiError(1345005, 'ask_items', 'wrong type')
                return [1234]
            if function is reader.items and kind == 3:
                raise M.NativeApiError(1345005, 'ask_items', 'wrong type')
            return []
        reader.pointers = pointers
        reader.geometry_provider = lambda tag, kind: 2 if kind == 0 else 0
        reader.entity = lambda *a, **kw: 700
        trial = object.__new__(M.Trial)
        trial.nodes = [types.SimpleNamespace(Tag=1, Name='WORKPIECE')]
        trial.native = reader
        trial.unavailable, trial.refs = set(), {}
        trial.reference = lambda tag: trial.refs.setdefault(tag, 'face')
        trial.nx = types.SimpleNamespace(ScCollector=type('Collector', (), {}),
            TaggedObjectManager=types.SimpleNamespace(GetTaggedObject=lambda tag: object()),
            CAM=types.SimpleNamespace(Operation=type('Operation', (), {})))
        snapshot = trial.geometry_snapshot(collect=True)
        self.assertEqual(snapshot[(1,0)][0:2], ('provider',2))
        self.assertEqual(snapshot[(1,0)][4], (700,))
        self.assertEqual(snapshot[(1,3)][0], 'unavailable')
        self.assertIn(700, trial.refs)
        self.assertEqual(len(trial.unavailable), 1)
        self.assertEqual(snapshot, trial.geometry_snapshot())

    def test_wrong_type_on_container_uses_real_geometry_provider(self):
        reader = object.__new__(M.NativeCam)
        calls = []
        def pointers(function, tag, kind):
            calls.append(tag)
            if tag == 1:
                raise M.NativeApiError(1345005, 'ask_items', 'Object is not the desired type')
            return [1234]
        reader.pointers = pointers
        reader.geometry_provider = lambda tag, kind: 2
        self.assertEqual(reader.channel(None, 1, 0), ('provider', 2, [1234]))
        self.assertEqual(calls, [1,2])

    def test_unavailable_is_not_reported_as_valid_empty_geometry(self):
        reader = object.__new__(M.NativeCam)
        def fail(*args):
            raise M.NativeApiError(1345005, 'ask_items', 'wrong type')
        reader.pointers = fail
        reader.geometry_provider = lambda tag, kind: 0
        self.assertEqual(reader.channel(None, 1, 0), ('unavailable', 0, []))
        reader.pointers = lambda *args: []
        self.assertEqual(reader.channel(None, 1, 0), ('read', 1, []))

    def test_unknown_error_is_not_ignored(self):
        reader = object.__new__(M.NativeCam)
        def fail(*args):
            raise M.NativeApiError(99999, 'ask_items', 'unexpected')
        reader.pointers = fail
        reader.geometry_provider = lambda *args: self.fail('must not retry unrelated errors')
        with self.assertRaises(M.NativeApiError):
            reader.channel(None, 1, 0)

    def test_wrong_type_at_provider_is_reported_without_loop(self):
        reader = object.__new__(M.NativeCam)
        def fail(*args):
            raise M.NativeApiError(1345005, 'ask_items', 'wrong type')
        reader.pointers = fail
        reader.geometry_provider = lambda *args: 1
        self.assertEqual(reader.channel(None, 1, 0), ('unavailable', 1, []))


class NativeModellingTests(unittest.TestCase):
    def reader(self):
        reader = object.__new__(M.NativeCam)
        reader.fail_message = lambda code, buffer: setattr(buffer, 'value', b'UF model error')
        return reader

    def face_reader(self, face_type=22, normal=(1,1,0), error=0):
        reader = self.reader()
        dp, ip = ctypes.POINTER(ctypes.c_double), ctypes.POINTER(ctypes.c_int)
        @ctypes.CFUNCTYPE(ctypes.c_int, ctypes.c_uint32, ip, dp, dp, dp, dp, dp, ip)
        def ask(tag, kind, point, direction, box, radius, radial, sense):
            reader.last_face = tag
            if error:
                return error
            kind[0], sense[0] = face_type, -1
            for i, v in enumerate((1,1,0)):
                point[i], direction[i] = v, normal[i]
            for i in range(6):
                box[i] = i + 100.0
            radius[0], radial[0] = 12.0, 34.0
            return 0
        reader.face_data = ask
        return reader

    def test_face_selection_without_python_modelling_methods(self):
        class Face:
            Tag = 0xf0000001
            IsOccurrence = False
            OwningPart = types.SimpleNamespace(Tag=1)
        nx = types.SimpleNamespace(Face=Face, DatumPlane=type('Datum', (), {}))
        native = self.face_reader()
        # There is deliberately no Modl / AskFaceData Python wrapper.
        plane = M.plane_from_object(Face(), types.SimpleNamespace(Tag=1), nx, native)
        self.assertEqual(native.last_face, Face.Tag)
        self.assertEqual(plane.origin, (1,1,0))
        self.assertTrue(M.close_values(plane.point([3,0,5]), [2,-1,5]))

    def test_native_face_error_is_not_misreported_as_nonplanar(self):
        with self.assertRaises(M.NativeApiError) as caught:
            self.face_reader(error=12345).face_plane(10)
        self.assertEqual(caught.exception.code, 12345)
        self.assertEqual(caught.exception.context, 'UF_MODL_ask_face_data')

    def test_curved_face_and_invalid_normal_are_rejected(self):
        with self.assertRaisesRegex(ValueError, 'плоская'):
            self.face_reader(face_type=16).face_plane(10)
        with self.assertRaisesRegex(ValueError, 'нулевую'):
            self.face_reader(normal=(0,0,0)).face_plane(10)
        with self.assertRaisesRegex(ValueError, 'Некорректная'):
            self.face_reader(normal=(0,float('nan'),0)).face_plane(10)

    def test_exact_bounds_pass_csys_and_read_three_double_rows(self):
        native = self.reader()
        dp = ctypes.POINTER(ctypes.c_double)
        seen = []
        @ctypes.CFUNCTYPE(ctypes.c_int, ctypes.c_uint32, ctypes.c_uint32,
                         dp, ctypes.POINTER(ctypes.c_double * 3), dp)
        def ask(tag, csys, corner, directions, distances):
            seen.append((tag, csys))
            rows = ((0,1,0), (0,0,1), (1,0,0))
            for i in range(3):
                corner[i], distances[i] = (1,2,3)[i], (5,7,10)[i]
                for j in range(3):
                    directions[i][j] = rows[i][j]
            return 0
        native.exact_box = ask
        trial = object.__new__(M.Trial)
        trial.refs, trial.plane, trial.box_csys = {}, axis_plane(), 0xf0000002
        trial.native = native
        trial.uf = types.SimpleNamespace(Modl=types.SimpleNamespace())
        actual = trial.bounding_box(0xf0000001)
        self.assertEqual(seen, [(0xf0000001,0xf0000002)])
        self.assertEqual(actual, [1,-10,2,11,-3,7])

    def test_transform_passes_unsigned_tags_and_all_sixteen_doubles(self):
        native, seen = self.reader(), []
        @ctypes.CFUNCTYPE(ctypes.c_int, ctypes.c_int,
                         ctypes.POINTER(ctypes.c_uint32), ctypes.POINTER(ctypes.c_double))
        def transform(count, tags, matrix):
            seen.append(([tags[i] for i in range(count)], [matrix[i] for i in range(16)]))
            return 0
        native.transform = transform
        native.transform_entities([1,0xf0000001], list(range(16)))
        self.assertEqual(seen, [([1,0xf0000001],list(range(16)))])

    def test_invalid_transform_is_rejected_before_native_call(self):
        native = self.reader()
        native.transform = lambda *a: self.fail('invalid input must not reach NX')
        for tags in ([], [0], [-1], [0x100000000]):
            with self.assertRaises(ValueError):
                native.transform_entities(tags, [0.0]*16)
        for matrix in ([0.0]*15, [float('inf')]*16):
            with self.assertRaises(ValueError):
                native.transform_entities([1], matrix)

    def test_all_native_modelling_failures_keep_code_and_function(self):
        native = self.reader()
        native.transform = lambda *a: 999
        native.exact_box = lambda *a: 999
        native.model_update = ctypes.CFUNCTYPE(ctypes.c_int)(lambda: 999)
        for call, context in (
            (lambda: native.bounding_box_exact(1,2), 'UF_MODL_ask_bounding_box_exact'),
            (lambda: native.transform_entities([1],[0.0]*16), 'UF_MODL_transform_entities'),
            (native.update_model, 'UF_MODL_update'),
        ):
            with self.subTest(context=context), self.assertRaises(M.NativeApiError) as caught:
                call()
            self.assertEqual(caught.exception.code, 999)
            self.assertEqual(caught.exception.context, context)


class CsysArrayTests(unittest.TestCase):
    def trial(self, plane):
        matrices, systems = {}, {}
        def require_doubles(value, count):
            if type(value) is not list:
                raise TypeError('Expecting List of double type, found ' + type(value).__name__)
            self.assertEqual(len(value), count)
            self.assertTrue(all(type(v) is float for v in value))
        def create_matrix(values):
            require_doubles(values, 9)
            tag = 100 + len(matrices)
            matrices[tag] = values[:]
            return tag
        def create_csys(origin, matrix, temporary):
            require_doubles(origin, 3)
            self.assertIn(matrix, matrices)
            tag = 200 + len(systems)
            systems[tag] = (origin[:], matrices[matrix], temporary)
            return tag
        csys = types.SimpleNamespace(
            CreateMatrix=create_matrix,
            CreateTempCsys=lambda origin,matrix: create_csys(origin,matrix,True),
            CreateCsys=lambda origin,matrix: create_csys(origin,matrix,False))
        trial = object.__new__(M.Trial)
        trial.plane, trial.refs, trial.bounds = plane, {}, {}
        trial.protected_bodies = set()
        trial.reference = lambda tag: trial.refs.setdefault(tag, object())
        trial.bodies = [types.SimpleNamespace(Tag=1,
            GetFaces=lambda: [types.SimpleNamespace(Tag=3),types.SimpleNamespace(Tag=2)],
            GetEdges=lambda: [types.SimpleNamespace(Tag=5),types.SimpleNamespace(Tag=4)])]
        trial.uf = types.SimpleNamespace(Csys=csys, Param=types.SimpleNamespace(SetTagValue=Mock()))
        trial.nx = types.SimpleNamespace(Point=type('Point', (), {}), Curve=type('Curve', (), {}))
        trial.native = types.SimpleNamespace(bounding_box_exact=Mock(return_value=(
            [1.0,2.0,3.0], [[1.0,0.0,0.0],[0.0,1.0,0.0],[0.0,0.0,1.0]], [4.0,5.0,6.0])))
        return trial, systems

    def test_topology_accepts_immutable_plane_with_strict_uf_list_arguments(self):
        for normal in ((-1,0,0), (1,2,3)):
            with self.subTest(normal=normal):
                plane = M.MirrorPlane((0,-24.99999999999998,8), normal)
                original_origin, original_basis = plane.origin, plane.basis
                trial, systems = self.trial(plane)
                topology = trial.capture_topology()
                self.assertEqual(topology, {1: ((2,3),(4,5))})
                self.assertEqual(set(trial.bounds), {1,2,3,4,5})
                self.assertEqual(systems[trial.box_csys], (list(original_origin),list(original_basis),True))
                self.assertEqual([call.args for call in trial.native.bounding_box_exact.call_args_list],
                                 [(tag,trial.box_csys) for tag in (1,3,2,5,4)])
                self.assertIs(plane.origin, original_origin)
                self.assertIs(plane.basis, original_basis)
                self.assertIsInstance(plane.origin, tuple)
                self.assertIsInstance(plane.basis, tuple)

    def test_next_mcs_creation_also_meets_strict_uf_double_list_contract(self):
        plane = M.MirrorPlane((4,2,7), (1,2,3))
        trial, systems = self.trial(plane)
        old_origin, old_axes = [3.0,-4.0,5.0], [1.0,0.0,0.0,0.0,1.0,0.0,0.0,0.0,1.0]
        trial.frames = {91: (old_origin,old_axes)}
        trial.mcs_bindings = [(types.SimpleNamespace(Tag=7,Name='MCS_1'),59,91)]
        trial.mcs_definitions = {91: ({91: None}, None)}
        trial.mcs_sources = {(7,59): (M.PARAM_DEFAULT,0,'MCS_1')}
        trial.transfer_mcs()
        self.assertEqual(len(systems), 1)
        target = next(tag for tag, (_,_,temporary) in systems.items() if not temporary)
        origin, axes, _ = systems[target]
        self.assertTrue(M.close_values(origin, plane.point(old_origin)))
        self.assertTrue(M.close_values(axes, M.reflected_frame(old_axes, plane)))
        trial.uf.Param.SetTagValue.assert_called_once_with(7,59,target)


class PlaneSelectionTests(unittest.TestCase):

    def test_datum_plane_and_other_part_rejection(self):
        class Datum:
            Tag = 20
            IsOccurrence = False
            OwningPart = types.SimpleNamespace(Tag=1)
            Origin = types.SimpleNamespace(X=2,Y=0,Z=0)
            Normal = types.SimpleNamespace(X=3,Y=0,Z=0)
        nx = types.SimpleNamespace(Face=type('Face', (), {}), DatumPlane=Datum)
        plane = M.plane_from_object(Datum(), types.SimpleNamespace(Tag=1), nx, None)
        self.assertEqual(plane.point([7,8,9]), [-3,8,9])
        with self.assertRaisesRegex(ValueError, 'текущей детали'):
            M.plane_from_object(Datum(), types.SimpleNamespace(Tag=2), nx, None)

    def test_cancel_selection_does_not_read_geometry(self):
        selection = types.SimpleNamespace(
            MaskTriple=lambda: types.SimpleNamespace(),
            SelectionScope=types.SimpleNamespace(WorkPart=1),
            SelectionAction=types.SimpleNamespace(ClearAndEnableSpecific=1),
            Response=types.SimpleNamespace(Cancel=2, Back=3))
        ui = types.SimpleNamespace(SelectionManager=types.SimpleNamespace(SelectTaggedObjects=lambda *a: (2,None)))
        nx = types.SimpleNamespace(Selection=selection, UI=types.SimpleNamespace(GetUI=lambda: ui),
            UF=types.SimpleNamespace(UFConstants=types.SimpleNamespace(UF_solid_type=70,UF_UI_SEL_FEATURE_PLANAR_FACE=20,UF_datum_plane_type=197)))
        with patch.object(M, 'plane_from_object', side_effect=AssertionError('must not read')):
            self.assertIsNone(M.select_mirror_plane(nx, None, None))

    def test_geometry_picker_requires_exactly_one_object_and_ok(self):
        selected = object()
        responses = iter([(1,[]),(1,[object(),object()]),(1,[selected])])
        prompts, messages = [], []
        def pick(*args):
            prompts.append(args)
            return next(responses)
        selection = types.SimpleNamespace(MaskTriple=lambda: types.SimpleNamespace(),
            SelectionScope=types.SimpleNamespace(WorkPart=1),
            SelectionAction=types.SimpleNamespace(ClearAndEnableSpecific=1),
            Response=types.SimpleNamespace(Cancel=2,Back=3))
        ui = types.SimpleNamespace(SelectionManager=types.SimpleNamespace(SelectTaggedObjects=pick),
            NXMessageBox=types.SimpleNamespace(Show=lambda *a: messages.append(a)))
        nx = types.SimpleNamespace(Selection=selection, UI=types.SimpleNamespace(GetUI=lambda:ui),
            NXMessageBox=types.SimpleNamespace(DialogType=types.SimpleNamespace(Information=1)),
            UF=types.SimpleNamespace(UFConstants=types.SimpleNamespace(UF_solid_type=70,UF_UI_SEL_FEATURE_PLANAR_FACE=20,UF_datum_plane_type=197)))
        with patch.object(M,'plane_from_object',return_value='plane') as reader:
            self.assertEqual(M.select_mirror_plane(nx,'native','part'),'plane')
        reader.assert_called_once_with(selected,'part',nx,'native')
        self.assertEqual(len(messages),2)
        self.assertTrue(all('OK' in args[0] and 'одну' in args[0] for args in prompts))


class WcsTests(unittest.TestCase):
    def trial(self, fails):
        nx = types.SimpleNamespace(Matrix3x3=lambda:types.SimpleNamespace(),
            Point3d=lambda x,y,z:types.SimpleNamespace(X=x,Y=y,Z=z))
        class Wcs:
            def SetOriginAndMatrix(self,origin,matrix):
                self.CoordinateSystem = types.SimpleNamespace(Origin=origin,Orientation=types.SimpleNamespace(Element=matrix))
        wcs = Wcs()
        origin, axes = [11,22,33], [0,1,0,-1,0,0,0,0,1]
        M.set_wcs_values(wcs,nx,origin,axes)
        trial = object.__new__(M.Trial)
        trial.part, trial.nx = types.SimpleNamespace(WCS=wcs), nx
        def run():
            self.assertEqual(M.wcs_values(wcs),([0,0,0],[1,0,0,0,1,0,0,0,1]))
            if fails:
                raise RuntimeError('failed in copy')
        trial.run_in_absolute_wcs = run
        return trial, origin, axes

    def test_shifted_rotated_wcs_is_restored_after_success(self):
        trial, origin, axes = self.trial(False)
        trial.run()
        self.assertEqual(M.wcs_values(trial.part.WCS),(origin,axes))

    def test_shifted_rotated_wcs_is_restored_after_failure(self):
        trial, origin, axes = self.trial(True)
        with self.assertRaisesRegex(RuntimeError,'failed in copy'):
            trial.run()
        self.assertEqual(M.wcs_values(trial.part.WCS),(origin,axes))


class TransactionTests(unittest.TestCase):
    def test_dirty_trial_copy_does_not_prompt_to_save_a_broken_result(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / 'part_MIRROR_TEST_003.prt'
            source.write_bytes(b'prt')
            part = types.SimpleNamespace(Tag=1, FullPath=str(source), IsModified=True, IsFullyLoaded=True,
                ComponentAssembly=types.SimpleNamespace(RootComponent=None))
            session = types.SimpleNamespace(Parts=types.SimpleNamespace(Display=part, Work=part))
            with self.assertRaisesRegex(RuntimeError, 'исправный исходный проект') as caught:
                M.preflight_source(session)
            self.assertNotIn('Сохраните',str(caught.exception))

    def test_dirty_source_is_rejected_before_copying(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / "source.prt"
            source.write_bytes(b"prt")
            part = types.SimpleNamespace(Tag=1, FullPath=str(source), IsModified=True, IsFullyLoaded=True,
                ComponentAssembly=types.SimpleNamespace(RootComponent=None))
            session = types.SimpleNamespace(Parts=types.SimpleNamespace(Display=part, Work=part))
            with patch.object(M, "copy_exclusive", side_effect=AssertionError("must not copy")):
                with self.assertRaisesRegex(RuntimeError, "несохранённые"):
                    M.execute(session, None, None, axis_plane())
            self.assertEqual(len(list(Path(folder).iterdir())), 1)

    def run_with_trial(self, fails, rollback_fails=False, display_fails=False, delete_mark_fails=False):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / "source.prt"
            source.write_bytes(b"prt-binary")
            original = types.SimpleNamespace(Tag=1, FullPath=str(source), IsModified=False, IsFullyLoaded=True,
                ComponentAssembly=types.SimpleNamespace(RootComponent=None))
            events = []
            class Parts:
                Display = original
                Work = original
                def __iter__(self):
                    return iter([original])
                def SetDisplay(self, part, maintain_work, entire):
                    if display_fails:
                        raise RuntimeError('cannot restore display')
                    self.Display = part
                    events.append(('display',part.Tag))
                    return object(), types.SimpleNamespace(Dispose=lambda: events.append('display-status-dispose'))
                def OpenBaseDisplay(self, path):
                    self.Display = types.SimpleNamespace(Tag=2, FullPath=path)
                    status = types.SimpleNamespace(NumberUnloadedParts=0, Dispose=lambda: events.append("dispose"))
                    return self.Display, status
                def SetWork(self, part):
                    self.Work = part
            def undo(mark, name):
                events.append(('undo',mark))
                if rollback_fails:
                    raise RuntimeError('undo failed')
            def delete_mark(mark, name):
                if delete_mark_fails:
                    raise RuntimeError('cannot delete mark')
                events.append(('delete-mark',mark))
            session = types.SimpleNamespace(Parts=Parts(),
                SetUndoMark=lambda *a: 55,
                UndoToMark=undo, DeleteUndoMark=delete_mark)
            nx = types.SimpleNamespace(Session=types.SimpleNamespace(MarkVisibility=types.SimpleNamespace(Visible=1)))
            class Trial:
                stage = "reflection"
                def __init__(self, *args):
                    self.part = args[3]
                def run(self):
                    self.assert_copy()
                    if isinstance(fails, Exception):
                        raise fails
                    if fails:
                        raise RuntimeError("kernel rejected reflection")
                def assert_copy(self):
                    if self.part.Tag != 2:
                        raise AssertionError("source mutated")
            with patch.object(M, "Trial", Trial):
                report = M.execute(session, nx, None, axis_plane())
            events.append(('active',session.Parts.Display.Tag,session.Parts.Work.Tag))
            self.assertEqual(source.read_bytes(), b"prt-binary")
            target = Path(folder) / "source_MIRROR_TEST_001.prt"
            self.assertEqual(target.read_bytes(), b"prt-binary")
            return events, report

    def test_kernel_failure_undoes_copy_and_does_not_save(self):
        events, report = self.run_with_trial(True)
        self.assertIn(("undo", 55), events)
        self.assertIn("ТЕСТ НЕ ПРОЙДЕН", report)
        self.assertIn("kernel rejected reflection", report)
        self.assertIn(('active',1,1),events)
        self.assertIn('display-status-dispose',events)
        self.assertIn('Снова активен исходный проект',report)

    def test_boundary_access_violation_is_reported_and_rolls_back(self):
        events, report = self.run_with_trial(OSError('exception: access violation reading boundary'))
        self.assertIn(('undo',55), events)
        self.assertIn('ТЕСТ НЕ ПРОЙДЕН', report)
        self.assertIn('access violation reading boundary', report)

    def test_failed_undo_keeps_failed_copy_visible_and_reports_it(self):
        events,report = self.run_with_trial(True,rollback_fails=True)
        self.assertIn(('active',2,2),events)
        self.assertNotIn(('display',1),events)
        self.assertIn('Откат не выполнен',report)
        self.assertNotIn('Снова активен исходный проект',report)

    def test_display_failure_does_not_misreport_successful_undo(self):
        events,report = self.run_with_trial(True,display_fails=True)
        self.assertIn('Изменения теста в копии отменены',report)
        self.assertIn('Не удалось вернуть исходный проект',report)
        self.assertNotIn('Откат не выполнен',report)
        self.assertIn(('active',2,2),events)

    def test_undo_mark_cleanup_failure_still_restores_source(self):
        events,report = self.run_with_trial(True,delete_mark_fails=True)
        self.assertIn('не удалось удалить метку Undo',report)
        self.assertNotIn('Откат не выполнен',report)
        self.assertIn(('active',1,1),events)

    def test_success_keeps_visible_undo_and_requires_manual_save(self):
        events, report = self.run_with_trial(False)
        self.assertNotIn(("undo", 55), events)
        self.assertNotIn(("delete-mark", 55), events)
        self.assertIn("ЕЩЁ НЕ СОХРАНЁН", report)
        self.assertNotIn("ТЕСТ НЕ ПРОЙДЕН", report)
        self.assertIn(('active',2,2),events)
        self.assertNotIn(('display',1),events)


if __name__ == "__main__":
    unittest.main()
