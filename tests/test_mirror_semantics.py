"""Regression probes for CAM editability and associative CSYS definitions.

The small object model emulates documented NX construction semantics. It is not
an NX kernel and cannot certify a PRT or actual toolpath generation.
"""
import importlib.util
from pathlib import Path
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock


SPEC = importlib.util.spec_from_file_location('mirror_semantics', Path(__file__).parents[1] / 'scripts/NX_Mirror_Project.py')
M = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(M)


def xyz(x=0, y=0, z=0):
    return NS(X=float(x), Y=float(y), Z=float(z))


class SmartWorld:
    """Three-plane CSYS: first two normals determine X/Y, three planes the origin."""
    def __init__(self):
        self.objects, self.parents = {}, {}
        self.next_tag = 100
        world = self

        class Object:
            def __init__(self, tag=None, parents=None):
                self.Tag = tag or world.next_tag
                world.next_tag += 1
                self.OwningPart = NS(Tag=1)
                self.IsOccurrence = False
                world.objects[self.Tag], world.parents[self.Tag] = self, parents

        class Face(Object):
            def __init__(self, tag, origin, normal):
                super().__init__(tag)
                self.Origin, self.Normal = xyz(*origin), xyz(*normal)

        class Plane(Object):
            def __init__(self, origin, normal, update):
                super().__init__()
                self.Origin, self.Normal, self.Flip = origin, normal, False
            def SetMethod(self, value):
                self.Method = value
            def SetGeometry(self, objects):
                self.sources = list(objects)
                world.parents[self.Tag] = tuple(o.Tag for o in objects)
            def GetGeometry(self):
                return self.sources
            def Evaluate(self):
                self.Origin = self.sources[0].Origin
                sign = -1 if self.Flip else 1
                self.Normal = xyz(*(sign*v for v in M.xyz(self.sources[0].Normal)))
            def SetFlip(self, flip):
                self.Flip = flip
            def SetVisibility(self, value):
                self.Visibility = value

        class Xform(Object):
            Scale = 1.0
            def __init__(self, a=None, b=None, c=None, update=2, scale=1.0):
                super().__init__(parents=(a.Tag,b.Tag,c.Tag) if a else None)
                self.inputs, self.Scale = [a,b,c] if a else [], scale
                self.fixed = None if a else ([7,8,9],[1,0,0,0,1,0,0,0,1])
            @property
            def Origin(self):
                return xyz(*self.values()[0])
            @property
            def Orientation(self):
                return NS(**dict(zip(M.MATRIX_FIELDS,self.values()[1])))
            def values(self):
                if self.fixed is not None:
                    return tuple(list(row) for row in self.fixed)
                normals = [M.xyz(p.Normal) for p in self.inputs]
                distances = [M.dot(n,M.xyz(p.Origin)) for n,p in zip(normals,self.inputs)]
                cross = [M.cross(normals[1],normals[2]), M.cross(normals[2],normals[0]), M.cross(normals[0],normals[1])]
                determinant = M.dot(normals[0],cross[0])
                origin = [sum(d*n[i] for d,n in zip(distances,cross))/determinant for i in range(3)]
                return origin, normals[0] + normals[1] + M.cross(normals[0],normals[1])

        class Csys(Object):
            def __init__(self, xform, update=2):
                super().__init__(parents=(xform.Tag,))
                self.xform = xform
            def ReplaceParameters(self, other):
                self.xform = other.xform
                world.parents[self.Tag] = (self.xform.Tag,)
                del world.objects[other.Tag]
                del world.parents[other.Tag]
            def Evaluate(self):
                pass

        self.Face, self.Plane, self.Xform, self.Csys = Face, Plane, Xform, Csys
        self.faces = [Face(10,[2,0,0],[1,0,0]), Face(11,[0,3,0],[0,1,0]), Face(12,[0,0,4],[0,0,1])]
        self.xform = Xform(*self.faces)
        self.csys = Csys(self.xform)
        self.nx = NS(Face=Face, Plane=Plane, DatumPlane=type('DatumPlane',(),{}), Xform=Xform,
                     Scalar=type('Scalar',(),{}), Point3d=xyz, Vector3d=xyz,
                     SmartObject=NS(UpdateOption=NS(AfterModeling=2), VisibilityOption=NS(Invisible=0)),
                     PlaneTypes=NS(MethodType=NS(Coincident=1)),
                     TaggedObjectManager=NS(GetTaggedObject=lambda tag:self.objects[tag]))

    def trial(self, mirror=None):
        trial = object.__new__(M.Trial)
        trial.nx, trial.part = self.nx, NS(Tag=1, Planes=NS(CreatePlane=self.Plane),
            Xforms=NS(CreateXform=self.Xform), CoordinateSystems=NS(CreateCoordinateSystem=self.Csys))
        trial.plane = mirror or M.MirrorPlane([0,0,0],[1,0,0])
        trial.refs, trial.lines = {}, []
        trial.mcs_definitions, trial.mcs_verified_definitions = {}, {}
        trial.mcs_fixed_xforms = {}
        def set_fixed_xform(tag, origin, axes):
            if self.parents[tag] is not None:
                raise AssertionError('Only a numerical Xform may be edited')
            self.objects[tag].fixed = (list(origin),list(axes))
        trial.native = NS(smart_parents=lambda tag:self.parents[tag],
            face_plane=lambda tag:M.MirrorPlane(M.xyz(self.objects[tag].Origin),M.xyz(self.objects[tag].Normal)),
            parameter_status=lambda tag,index:trial.mcs_sources[(tag,index)][0],
            parameter_definer=lambda tag,index:trial.mcs_sources[(tag,index)][1],
            set_fixed_xform=Mock(side_effect=set_fixed_xform))
        trial.frames, trial.mcs_expected, trial.mcs_sources = {}, {}, {}
        trial.mcs_bindings = []
        trial.mcs_links = {}
        trial.uf = NS(Param=NS(AskTagValue=lambda tag,index:trial.mcs_expected[(tag,index)], SetTagValue=Mock()),
            Csys=NS(AskCsysInfo=lambda t:(t,self.objects[t].xform.values()[0]),
                    AskMatrixValues=lambda t:self.objects[t].xform.values()[1]))
        self.add_csys(trial,self.csys,1,'MCS')
        return trial

    def add_csys(self, trial, csys, node_tag, label):
        tag = csys.Tag
        origin, axes = csys.xform.values()
        trial.frames[tag] = (origin,axes)
        trial.mcs_bindings.append((NS(Tag=node_tag,Name=label),59,tag))
        trial.mcs_expected[(node_tag,59)] = tag
        trial.mcs_sources[(node_tag,59)] = (M.PARAM_OVERRIDDEN,node_tag,label + ' / MCS')
        trial.capture_csys_definition(tag,origin,axes)

    def reflect_faces(self, mirror):
        for face in self.faces:
            face.Origin = xyz(*mirror.point(M.xyz(face.Origin)))
            face.Normal = xyz(*mirror.vector(M.xyz(face.Normal)))


class AssociativityTests(unittest.TestCase):
    def test_three_planes_keeps_selected_faces_and_csys_tag_with_reflected_tool_z(self):
        for plane in (M.MirrorPlane([0,0,0],[1,0,0]), M.MirrorPlane([4,-3,2],[1,2,3])):
            with self.subTest(plane=plane.normal):
                world = SmartWorld()
                trial = world.trial(plane)
                self.assertEqual(trial.mcs_definitions[world.csys.Tag][1],(10,11,12))
                world.reflect_faces(plane)
                replacement = trial.transfer_mcs()
                trial.verify_mcs(replacement)
                self.assertEqual(replacement,{world.csys.Tag:world.csys.Tag})
                trial.uf.Param.SetTagValue.assert_not_called()
                # Every new plane points to the same selected original face.
                for plane, source in zip(world.csys.xform.inputs,world.faces):
                    self.assertEqual(plane.GetGeometry(),[source])
                self.assertEqual(len(world.csys.xform.inputs),3)

    def test_dynamic_replacement_is_rejected_even_with_identical_coordinates(self):
        world = SmartWorld()
        trial = world.trial()
        world.reflect_faces(trial.plane)
        replacement = trial.transfer_mcs()
        world.parents[world.csys.Tag] = None
        with self.assertRaisesRegex(RuntimeError,'Dynamic'):
            trial.verify_mcs(replacement)

    def test_changed_support_plane_definition_stops_before_rebuilding(self):
        world = SmartWorld()
        trial = world.trial()
        world.reflect_faces(trial.plane)
        world.parents[10] = (11,)
        with self.assertRaisesRegex(RuntimeError,'опорной плоскости'):
            trial.transfer_mcs()

    def test_unknown_associative_method_is_not_flattened_to_numeric(self):
        world = SmartWorld()
        world.parents[world.xform.Tag] = (10,11)
        trial = world.trial()
        world.reflect_faces(trial.plane)
        with self.assertRaisesRegex(RuntimeError,'замена на Dynamic запрещена'):
            trial.transfer_mcs()
        trial.uf.Param.SetTagValue.assert_not_called()

    def test_failed_smart_plane_evaluation_does_not_replace_existing_definition(self):
        world = SmartWorld()
        trial = world.trial()
        world.reflect_faces(trial.plane)
        before = world.csys.xform
        trial.part.Planes.CreatePlane = Mock(side_effect=RuntimeError('plane update failed'))
        with self.assertRaisesRegex(RuntimeError,'plane update failed'):
            trial.transfer_mcs()
        self.assertIs(world.csys.xform,before)


class FixedXformTests(unittest.TestCase):
    def world(self):
        world = SmartWorld()
        world.xform = world.Xform()
        world.csys = world.Csys(world.xform)
        return world

    def test_smart_csys_with_numeric_xform_retains_both_objects_and_definition(self):
        for mirror in (M.MirrorPlane([0,0,0],[1,0,0]),M.MirrorPlane([4,-3,2],[1,2,3])):
            with self.subTest(normal=mirror.normal):
                world = self.world()
                trial = world.trial(mirror)
                original_graph = dict(world.parents)
                self.assertEqual(trial.mcs_fixed_xforms,{world.csys.Tag:world.xform.Tag})
                replacement = trial.transfer_mcs()
                trial.verify_mcs(replacement)
                self.assertEqual(world.parents,original_graph)
                self.assertIs(world.csys.xform,world.xform)
                trial.native.set_fixed_xform.assert_called_once()
                trial.uf.Param.SetTagValue.assert_not_called()

    def test_numeric_root_and_two_three_plane_setups_transfer_together(self):
        world = self.world()
        trial = world.trial()
        for node_tag in (2,3):
            world.add_csys(trial,world.Csys(world.Xform(*world.faces)),node_tag,'MCS_' + str(node_tag))
        world.reflect_faces(trial.plane)
        trial.verify_mcs(trial.transfer_mcs())
        self.assertEqual(len(trial.frames),3)
        self.assertEqual(sum(bool(planes) for _,planes in trial.mcs_definitions.values()),2)
        trial.native.set_fixed_xform.assert_called_once()
        trial.uf.Param.SetTagValue.assert_not_called()

    def test_already_moved_xform_is_not_reflected_again(self):
        world = self.world()
        trial = world.trial()
        origin,axes = world.xform.values()
        world.xform.fixed = (trial.plane.point(origin),M.reflected_frame(axes,trial.plane))
        trial.verify_mcs(trial.transfer_mcs())
        trial.native.set_fixed_xform.assert_not_called()

    def test_shared_xform_and_inherited_operation_are_moved_once(self):
        world = self.world()
        trial = world.trial()
        world.add_csys(trial,world.Csys(world.xform),2,'SECOND')
        trial.mcs_expected[(3,59)] = world.csys.Tag
        trial.mcs_sources[(3,59)] = (M.PARAM_INHERITED,1,'OPERATION / MCS')
        trial.verify_mcs(trial.transfer_mcs())
        trial.native.set_fixed_xform.assert_called_once()
        trial.uf.Param.SetTagValue.assert_not_called()

    def test_parameterized_xform_with_zero_parents_is_not_treated_as_numeric(self):
        world = self.world()
        world.parents[world.xform.Tag] = ()
        trial = world.trial()
        self.assertFalse(trial.mcs_fixed_xforms)
        with self.assertRaisesRegex(RuntimeError,'замена на Dynamic запрещена'):
            trial.transfer_mcs()
        trial.native.set_fixed_xform.assert_not_called()
        self.assertIn('MCS / MCS','\n'.join(trial.lines))
        self.assertIn('Xform ' + str(world.xform.Tag) + ': родители ()','\n'.join(trial.lines))

    def test_scaled_xform_is_not_silently_normalized(self):
        world = self.world()
        world.xform.Scale = 2.0
        trial = world.trial()
        self.assertFalse(trial.mcs_fixed_xforms)
        with self.assertRaisesRegex(RuntimeError,'замена на Dynamic запрещена'):
            trial.transfer_mcs()
        trial.native.set_fixed_xform.assert_not_called()

    def test_changed_dependencies_stop_before_numeric_write(self):
        world = self.world()
        trial = world.trial()
        world.parents[world.xform.Tag] = (10,)
        with self.assertRaisesRegex(RuntimeError,'Изменилось определение'):
            trial.transfer_mcs()
        trial.native.set_fixed_xform.assert_not_called()

    def test_failed_native_write_is_fatal(self):
        world = self.world()
        trial = world.trial()
        trial.native.set_fixed_xform.side_effect = RuntimeError('native setter failed')
        with self.assertRaisesRegex(RuntimeError,'native setter failed'):
            trial.transfer_mcs()

    def test_successful_native_call_must_really_update_the_csys(self):
        world = self.world()
        trial = world.trial()
        trial.native.set_fixed_xform.side_effect = None
        with self.assertRaisesRegex(RuntimeError,'Не подтверждён перенос CSYS'):
            trial.transfer_mcs()


class CamEditorTests(unittest.TestCase):
    def trial(self, failure=None):
        class Geometry:
            def InitializeData(self, reload):
                if failure:
                    raise RuntimeError(failure)
        class GeometryGroup(Geometry):
            pass
        node = type('FeatureGeometry',(),{'Tag':1,'Name':'WORKPIECE'})()
        builder = NS(PartGeometry=Geometry(), Validate=Mock(return_value=True), Destroy=Mock(), Commit=Mock())
        trial = object.__new__(M.Trial)
        trial.nx = NS(CAM=NS(Geometry=Geometry,GeometryGroup=GeometryGroup))
        trial.nodes, trial.cam_builder_state = [node], None
        trial.setup = NS(CAMGroupCollection=NS(CreateMillGeomBuilder=lambda obj:builder), CAMOperationCollection=NS())
        return trial,builder

    def test_selected_geometry_does_not_hide_invalid_bounding_feature(self):
        trial,builder = self.trial('The bounding geometry is invalid because the feature parameters have been removed in Modeling.')
        with self.assertRaisesRegex(RuntimeError,'bounding geometry is invalid'):
            trial.check_cam_builders()
        self.assertIn('WORKPIECE',trial.stage)
        builder.Destroy.assert_called_once()
        builder.Commit.assert_not_called()
        self.assertIsNone(trial.cam_builder_state)

    def test_successful_cam_read_is_verified_without_committing_or_generation(self):
        trial,builder = self.trial()
        trial.check_cam_builders()
        trial.check_cam_builders()
        self.assertEqual(trial.cam_builder_state,{1:{}})
        self.assertEqual(builder.Destroy.call_count,2)
        builder.Commit.assert_not_called()

    def test_builder_false_validation_is_a_failure_and_builder_is_destroyed(self):
        trial,builder = self.trial()
        builder.Validate.return_value = False
        with self.assertRaisesRegex(RuntimeError,'не прошёл проверку'):
            trial.check_cam_builders()
        builder.Destroy.assert_called_once()

    def test_dependent_surface_operation_failure_identifies_the_operation(self):
        trial,builder = self.trial()
        trial.nodes = [type('VolumeBased25DMillingOperation',(),{'Tag':3,'Name':'PLSK_PREDV'})()]
        trial.setup.CAMOperationCollection.CreateVolumeBased25dMillingOperationBuilder = lambda obj:builder
        builder.Validate.side_effect = RuntimeError('bounding geometry is invalid')
        with self.assertRaisesRegex(RuntimeError,'bounding geometry is invalid'):
            trial.check_cam_builders()
        self.assertIn('PLSK_PREDV',trial.stage)
        builder.Destroy.assert_called_once()
        builder.Commit.assert_not_called()


class StockTests(unittest.TestCase):
    def test_protected_stock_is_excluded_from_raw_transform(self):
        trial = object.__new__(M.Trial)
        trial.check_cam_builders, trial.capture_stock = Mock(), Mock()
        trial.model_history = lambda:{}
        trial.geometry_snapshot = lambda **kw:{}
        trial.capture_parameters, trial.capture_topology = Mock(), Mock(return_value={})
        trial.operation_snapshot = lambda:[]
        trial.frames, trial.refs, trial.bounds, trial.lines = {}, {}, {}, []
        trial.bodies = [NS(Tag=1),NS(Tag=2)]
        trial.protected_bodies, trial.operations = {2}, []
        trial.nx = NS(Curve=type('Curve',(),{}),Point=type('Point',(),{}))
        trial.native = NS(reflection_matrix=lambda plane:[0]*16,transform_entities=Mock())
        trial.plane = M.MirrorPlane([0,0,0],[1,0,0])
        trial.transfer_mcs, trial.update_stock = Mock(return_value={}), Mock()
        trial.setup = NS(DeleteToolPath=Mock(side_effect=RuntimeError('reached path deletion')))
        with self.assertRaisesRegex(RuntimeError,'reached path deletion'):
            trial.run_in_absolute_wcs()
        trial.native.transform_entities.assert_called_once_with([1],[0]*16)
        trial.update_stock.assert_called_once()

    def test_selected_stock_faces_are_still_verified(self):
        trial = object.__new__(M.Trial)
        trial.plane = M.MirrorPlane([0,0,0],[1,0,0])
        trial.protected_bodies, trial.refs, trial.bounds = {1}, {3:NS(Tag=3)}, {}
        trial.bodies = [NS(Tag=1,GetFaces=lambda:[NS(Tag=2),NS(Tag=3)],GetEdges=lambda:[NS(Tag=4)])]
        trial.reference = lambda tag:trial.refs.setdefault(tag,NS(Tag=tag))
        trial.uf = NS(Csys=NS(CreateMatrix=lambda v:90,CreateTempCsys=lambda o,m:91))
        trial.bounding_box = Mock(return_value=[0,0,0,1,1,1])
        trial.capture_topology()
        self.assertEqual(set(trial.bounds),{1,3})

    def stock_trial(self, changed=False, replacement_tag=7):
        trial = object.__new__(M.Trial)
        trial.plane, trial.bounds = M.MirrorPlane([0,0,0],[1,0,0]), {1:[2,3,4,8,9,10]}
        feature = NS(Tag=7,Name='Bounding block')
        state = {'offsets':(1,2,3),'type':'BoundedBlock'}
        trial.stock_features = {7:(feature,(1,),state,[0,0,0])}
        builder = NS(Type='BoundedBlock',CalculateBoxSize=Mock(),Validate=Mock(return_value=True),
                     Commit=Mock(return_value=NS(Tag=replacement_tag)),Destroy=Mock())
        trial.tooling_builder = lambda f:builder
        values = [state,dict(state,offsets=(0,0,0)) if changed else state]
        trial.stock_state = Mock(side_effect=values)
        trial.bounding_box = lambda tag:[2,3,4,8,9,10]
        trial.nx = NS(Features=NS(ToolingBoxBuilder=NS(Types=NS(CenterAndLengths='CenterAndLengths'))))
        trial.native = NS(update_model=Mock())
        return trial,builder

    def test_stock_is_updated_by_editing_existing_feature(self):
        trial,builder = self.stock_trial()
        trial.update_stock()
        builder.CalculateBoxSize.assert_called_once()
        builder.Commit.assert_called_once()
        builder.Destroy.assert_called_once()

    def test_changed_allowances_or_recreated_feature_stop_the_trial(self):
        for kwargs, message in (({'changed':True},'припуски'),({'replacement_tag':99},'заменил')):
            with self.subTest(kwargs=kwargs):
                trial,builder = self.stock_trial(**kwargs)
                with self.assertRaisesRegex(RuntimeError,message):
                    trial.update_stock()
                builder.Destroy.assert_called_once()


if __name__ == '__main__':
    unittest.main()
