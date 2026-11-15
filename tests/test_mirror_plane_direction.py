"""Plane/direction/point CSYS regressions; this model is not the NX kernel."""
import math
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock

from test_mirror_semantics import M, SmartWorld, xyz


class PlaneDirectionWorld(SmartWorld):
    def __init__(self, axis='X', upper=False, point_height=0):
        super().__init__()
        world = self
        Object = self.Face.__bases__[0]
        self.axis, self.factories = axis, []
        self.point_height = point_height
        zsign = 1 if upper else -1
        self.faces[0].Origin, self.faces[0].Normal = xyz(0,0,-19), xyz(0,0,zsign)
        self.faces[1].Normal = xyz(zsign,0,0) if axis == 'X' else xyz(0,1,0)

        class Edge(Object):
            def __init__(self):
                super().__init__()
                self.position = [-47.5,25.5,-19 + point_height]

        class Point(Object):
            def __init__(self, edge):
                super().__init__(parents=(edge.Tag,))
                self.edge = edge
                self.Coordinates = xyz(*edge.position)
            def Evaluate(self):
                world.events.append(('point',self.Tag))
                self.Coordinates = xyz(*self.edge.position)

        class Direction(Object):
            def __init__(self, face):
                super().__init__(parents=(face.Tag,))
                self.face = face
                self.Vector = face.Normal
            def Evaluate(self):
                world.events.append(('direction',self.Tag))
                self.Vector = self.face.Normal

        class Scalar(Object):
            Value = 1.0

        class PlaneDirXform(Object):
            Scale = 1.0
            def __init__(self, plane, direction, point, update=2, scale=1, flip_axis=False, flip_z=False, mode=axis):
                self.scalar = Scalar()
                super().__init__(parents=(self.scalar.Tag,direction.Tag,plane.Tag,point.Tag))
                self.inputs = (plane,direction,point)
                self.Scale, self.flip_axis, self.flip_z, self.mode = scale,flip_axis,flip_z,mode
                self.fixed = None
                self.Evaluate()
            def Evaluate(self):
                world.events.append(('xform',self.Tag))
                plane, direction, point = self.inputs
                n, d, p, o = (M.xyz(plane.Normal),M.xyz(direction.Vector),M.xyz(point.Coordinates),M.xyz(plane.Origin))
                n2 = sum(v*v for v in n)
                length = sum(d[i]*n[i] for i in range(3))/n2
                projected = [d[i]-length*n[i] for i in range(3)]
                norm = math.sqrt(sum(v*v for v in projected))
                a = [v/norm*(-1 if self.flip_axis else 1) for v in projected]
                z = [v/math.sqrt(n2)*(-1 if self.flip_z else 1) for v in n]
                x,y = (a,M.cross(z,a)) if self.mode == 'X' else (M.cross(a,z),a)
                distance = sum((p[i]-o[i])*n[i] for i in range(3))/n2
                self.cached = ([p[i]-distance*n[i] for i in range(3)],x+y+z)
            def values(self):
                return self.fixed or self.cached
            @property
            def Origin(self):
                return xyz(*self.values()[0])
            @property
            def Orientation(self):
                return NS(**dict(zip(M.MATRIX_FIELDS,self.values()[1])))

        self.nx.Direction, self.nx.Point, self.nx.Scalar = Direction,Point,Scalar
        self.nx.Edge, self.nx.Xform = Edge,PlaneDirXform
        self.Xform = PlaneDirXform
        self.edge = Edge()
        self.point, self.direction = Point(self.edge),Direction(self.faces[1])
        self.support = self.Plane(xyz(0,0,-19),xyz(0,0,zsign),2)
        self.support.SetGeometry([self.faces[0]])
        self.support.Evaluate()
        self.xform = PlaneDirXform(self.support,self.direction,self.point)
        self.csys = self.Csys(self.xform)
        # In NX, the temporary CSYS becomes invalid after ReplaceParameters.
        original_replace = self.csys.ReplaceParameters
        def replace(other):
            original_replace(other)
            other._deleted = True
        self.csys.ReplaceParameters = Mock(side_effect=replace)

    def trial(self, mirror=None):
        trial = super().trial(mirror or M.MirrorPlane((-79,0,0),(-1,0,0)))
        def factory(mode):
            def create(*args):
                self.factories.append((mode,args))
                return self.Xform(*args,mode=mode)
            return create
        trial.part.Xforms.CreateXformByPlaneXDirPoint = factory('X')
        trial.part.Xforms.CreateXformByPlaneYDirPoint = factory('Y')
        return trial

    def reflect(self, trial):
        self.reflect_faces(trial.plane)
        self.edge.position = trial.plane.point(self.edge.position)
        self.events.clear()


class PlaneDirectionTests(unittest.TestCase):
    def test_reported_lower_setup_retains_direct_objects_and_y_z(self):
        w = PlaneDirectionWorld()
        t = w.trial()
        csys_tag = w.csys.Tag
        supports = (w.support,w.direction,w.point)
        self.assertEqual(t.frames[csys_tag],([-47.5,25.5,-19],[-1,0,0,0,1,0,0,0,-1]))
        w.reflect(t)
        replacement = t.transfer_mcs()
        t.verify_mcs(replacement)
        self.assertEqual(replacement,{csys_tag:csys_tag})
        self.assertEqual(t.csys_frame(csys_tag),([-110.5,25.5,-19],[-1,0,0,0,1,0,0,0,-1]))
        self.assertEqual(w.csys.xform.inputs,supports)
        self.assertEqual(w.support.GetGeometry(),[w.faces[0]])
        self.assertEqual(w.parents[w.point.Tag],(w.edge.Tag,))
        self.assertEqual(w.parents[w.direction.Tag],(w.faces[1].Tag,))
        self.assertEqual(w.factories[0][1][-2:],(True,False))
        t.native.set_fixed_xform.assert_not_called()
        t.uf.Param.SetTagValue.assert_not_called()

    def test_x_and_y_definitions_upper_lower_and_oblique_mirror(self):
        for axis in ('X','Y'):
            for upper in (False,True):
                for normal in ((1,0,0),(1,2,3),(0,0,1)):
                    with self.subTest(axis=axis,upper=upper,normal=normal):
                        w = PlaneDirectionWorld(axis,upper)
                        t = w.trial(M.MirrorPlane((13,-5,7),normal))
                        original,axes = t.frames[w.csys.Tag]
                        self.assertEqual(t.mcs_plane_directions[w.csys.Tag]['axis'],axis)
                        w.reflect(t)
                        t.verify_mcs(t.transfer_mcs())
                        origin,actual = t.csys_frame(w.csys.Tag)
                        self.assertTrue(M.close_values(origin,t.plane.point(original)))
                        self.assertTrue(M.close_values(actual[3:6],t.plane.vector(axes[3:6])))
                        self.assertTrue(M.close_values(actual[6:],t.plane.vector(axes[6:])))
                        self.assertTrue(M.close_values(actual[:3],M.cross(actual[3:6],actual[6:])))

    def test_point_above_plane_preserves_projection_and_point_definition(self):
        w = PlaneDirectionWorld(point_height=6)
        t = w.trial(M.MirrorPlane((5,6,7),(1,2,3)))
        original = M.xyz(w.point.Coordinates)
        w.reflect(t)
        t.verify_mcs(t.transfer_mcs())
        self.assertTrue(M.close_values(M.xyz(w.point.Coordinates),t.plane.point(original)))
        self.assertEqual(w.csys.xform.inputs[2].Tag,w.point.Tag)

    def test_explicit_dependency_evaluation_precedes_xform_and_csys(self):
        w = PlaneDirectionWorld(); t = w.trial(); w.reflect(t)
        t.transfer_mcs()
        xform_index = w.events.index(('xform',w.xform.Tag))
        for kind,obj in (('plane',w.support),('direction',w.direction),('point',w.point)):
            self.assertLess(w.events.index((kind,obj.Tag)),xform_index)
        self.assertLess(xform_index,w.events.index(('csys',w.csys.Tag)))

    def test_already_correct_native_definition_is_not_replaced(self):
        w = PlaneDirectionWorld(axis='Y'); t = w.trial(); w.reflect(t)
        t.verify_mcs(t.transfer_mcs())
        self.assertFalse(w.factories)
        w.csys.ReplaceParameters.assert_not_called()

    def test_shared_supports_and_inherited_rcs_remain_shared(self):
        w = PlaneDirectionWorld(); t = w.trial()
        second = w.Csys(w.Xform(w.support,w.direction,w.point))
        w.add_csys(t,second,2,'SECOND')
        t.mcs_expected[(3,60)] = w.csys.Tag
        t.mcs_sources[(3,60)] = (M.PARAM_INHERITED,1,'OP / RCS')
        w.reflect(t)
        t.verify_mcs(t.transfer_mcs())
        self.assertEqual(w.csys.xform.inputs,second.xform.inputs)
        self.assertEqual(M.xyz(w.direction.Vector),[1,0,0])
        self.assertFalse(w.support.Flip)
        t.uf.Param.SetTagValue.assert_not_called()

    def test_same_plane_other_face_is_rejected_even_if_graph_is_unchanged(self):
        w = PlaneDirectionWorld(); t = w.trial(); w.reflect(t)
        other = w.Face(900,M.xyz(w.faces[0].Origin),M.xyz(w.faces[0].Normal))
        w.support.sources = [other]
        with self.assertRaisesRegex(RuntimeError,'прямой выбор'):
            t.transfer_mcs()
        w.csys.ReplaceParameters.assert_not_called()

    def test_changed_point_edge_or_direction_face_is_rejected(self):
        for name in ('point','direction'):
            with self.subTest(name=name):
                w = PlaneDirectionWorld(); t = w.trial(); w.reflect(t)
                w.parents[getattr(w,name).Tag] = (w.faces[2].Tag,)
                with self.assertRaisesRegex(RuntimeError,'опоры Plane / Direction / Point'):
                    t.transfer_mcs()
                w.csys.ReplaceParameters.assert_not_called()

    def test_changed_plane_method_is_not_hidden_by_matching_coordinates(self):
        w = PlaneDirectionWorld(); t = w.trial(); w.reflect(t)
        w.support.Method = 2
        with self.assertRaisesRegex(RuntimeError,'Способ ожидался'):
            t.transfer_mcs()

    def test_stale_point_plane_or_direction_stops_before_replacement(self):
        for name in ('point','plane','direction'):
            with self.subTest(name=name):
                w = PlaneDirectionWorld(); t = w.trial(); w.reflect(t)
                if name == 'point': w.point.Evaluate = Mock()
                elif name == 'plane': w.faces[0].Origin = xyz(0,0,-18)
                else: w.faces[1].Normal = xyz(1,0,1)  # Same in-plane projection, different vector.
                with self.assertRaisesRegex(RuntimeError,'отражение опоры'):
                    t.transfer_mcs()
                w.csys.ReplaceParameters.assert_not_called()

    def test_candidate_wrong_frame_never_replaces_existing_csys(self):
        w = PlaneDirectionWorld(); t = w.trial(); w.reflect(t)
        create = t.part.Xforms.CreateXformByPlaneXDirPoint
        def wrong(*args):
            candidate = create(*args)
            candidate.fixed = ([123,25.5,-19],[-1,0,0,0,1,0,0,0,-1])
            return candidate
        t.part.Xforms.CreateXformByPlaneXDirPoint = wrong
        with self.assertRaisesRegex(RuntimeError,'Не подтверждён перенос'):
            t.transfer_mcs()
        w.csys.ReplaceParameters.assert_not_called()

    def test_candidate_different_direct_point_is_not_accepted(self):
        w = PlaneDirectionWorld(); t = w.trial(); w.reflect(t)
        create = t.part.Xforms.CreateXformByPlaneXDirPoint
        def wrong(*args):
            candidate = create(*args)
            extra = w.nx.Point(w.edge)
            w.parents[candidate.Tag] = (candidate.scalar.Tag,w.direction.Tag,w.support.Tag,extra.Tag)
            return candidate
        t.part.Xforms.CreateXformByPlaneXDirPoint = wrong
        with self.assertRaisesRegex(RuntimeError,'прямые опоры'):
            t.transfer_mcs()
        w.csys.ReplaceParameters.assert_not_called()

    def test_replacement_must_store_verified_frame_and_graph(self):
        for corruption in ('frame','graph'):
            with self.subTest(corruption=corruption):
                w = PlaneDirectionWorld(); t = w.trial(); w.reflect(t)
                replace = w.csys.ReplaceParameters
                def wrong(other):
                    replace(other)
                    if corruption == 'frame':
                        w.csys.xform.fixed = ([0,0,0],[1,0,0,0,1,0,0,0,1])
                    else:
                        w.parents[w.csys.Tag] = (w.xform.Tag,)
                w.csys.ReplaceParameters = wrong
                with self.assertRaises(RuntimeError):
                    t.transfer_mcs()

    def test_late_update_drift_is_rejected_without_silent_repair(self):
        for corruption in ('frame','point','direction','selection','scale'):
            with self.subTest(corruption=corruption):
                w = PlaneDirectionWorld(); t = w.trial(); w.reflect(t)
                replacement = t.transfer_mcs()
                if corruption == 'frame': w.csys.xform.fixed = ([0,0,0],[1,0,0,0,1,0,0,0,1])
                elif corruption == 'point': w.point.Coordinates = xyz(0,0,0)
                elif corruption == 'direction': w.direction.Vector = xyz(0,1,0)
                elif corruption == 'selection': w.support.sources = []
                else: w.csys.xform.scalar.Value = 2
                with self.assertRaises(RuntimeError): t.verify_mcs(replacement)
                self.assertEqual(len(w.factories),1)

    def test_old_xform_and_scalar_can_be_retired_but_supports_must_remain(self):
        w = PlaneDirectionWorld(); t = w.trial(); w.reflect(t)
        replacement = t.transfer_mcs()
        w.retire([w.xform.scalar,w.xform])
        t.verify_mcs(replacement)
        w.retire([w.direction])
        with self.assertRaises((RuntimeError,KeyError)): t.verify_mcs(replacement)

    def test_live_support_wrapper_is_reacquired_and_owner_checked(self):
        w = PlaneDirectionWorld(); t = w.trial(); w.reflect(t)
        replacement = t.transfer_mcs()
        tag = w.direction.Tag
        newer = object.__new__(w.nx.Direction)
        newer.__dict__.update(w.direction.__dict__)
        w.objects[tag] = newer
        w.direction._deleted = True
        t.verify_mcs(replacement)
        self.assertIs(t.refs[tag],newer)
        newer.OwningPart = NS(Tag=999)
        with self.assertRaisesRegex(RuntimeError,'Недоступен действующий объект'):
            t.verify_mcs(replacement)

    def test_missing_factory_or_failed_api_is_not_a_dynamic_fallback(self):
        for factory in (None,Mock(side_effect=RuntimeError('NX factory failed'))):
            with self.subTest(factory=factory):
                w = PlaneDirectionWorld(); t = w.trial(); w.reflect(t)
                t.part.Xforms.CreateXformByPlaneXDirPoint = factory
                with self.assertRaises(RuntimeError): t.transfer_mcs()
                w.csys.ReplaceParameters.assert_not_called()
                t.native.set_fixed_xform.assert_not_called()

    def test_factory_must_return_new_objects_owned_by_the_copy(self):
        for change in ('old_xform','old_csys','foreign_csys'):
            with self.subTest(change=change):
                w = PlaneDirectionWorld(); t = w.trial(); w.reflect(t)
                if change == 'old_xform':
                    t.part.Xforms.CreateXformByPlaneXDirPoint = lambda *args:w.xform
                elif change == 'old_csys':
                    t.part.CoordinateSystems.CreateCoordinateSystem = lambda *args:w.csys
                else:
                    def create(*args):
                        candidate = w.Csys(*args)
                        candidate.OwningPart = NS(Tag=900)
                        return candidate
                    t.part.CoordinateSystems.CreateCoordinateSystem = create
                with self.assertRaises(RuntimeError): t.transfer_mcs()
                w.csys.ReplaceParameters.assert_not_called()

    def test_ambiguous_or_parameterized_pattern_is_not_authorized(self):
        for change in ('scale','parameterized','extra','direction','origin'):
            with self.subTest(change=change):
                w = PlaneDirectionWorld()
                if change == 'scale': w.xform.Scale = 2
                elif change == 'parameterized': w.parents[w.xform.scalar.Tag] = ()
                elif change == 'extra': w.parents[w.xform.Tag] += (w.faces[2].Tag,)
                elif change == 'direction': w.direction.Vector = xyz(1,1,0)
                else: w.xform.fixed = ([11,22,33],[-1,0,0,0,1,0,0,0,-1])
                t = w.trial()
                self.assertFalse(t.mcs_plane_directions)
                w.reflect(t)
                with self.assertRaisesRegex(RuntimeError,'замена на Dynamic запрещена'):
                    t.transfer_mcs()
                self.assertFalse(w.factories)

    def test_nonfinite_and_perpendicular_direction_fail_closed(self):
        for vector in ((float('nan'),0,0),(float('inf'),0,0),(0,0,1)):
            with self.subTest(vector=vector):
                w = PlaneDirectionWorld()
                w.direction.Vector = xyz(*vector)
                with self.assertRaises(RuntimeError): w.trial()

    def test_dependency_plan_updates_stock_before_all_linked_supports(self):
        w = PlaneDirectionWorld(); t = w.trial()
        t.stock_features = {200:(NS(Name='Blank',GetParents=lambda:[]),(300,),{'BoundedObject':(),'Csys':0},())}
        order = t.mcs_stock_order({300:((w.faces[0].Tag,w.faces[1].Tag),(w.edge.Tag,))})
        self.assertEqual(order,[('stock',200),('mcs',w.csys.Tag)])
        t.checkpoint_model_history = Mock()
        t.update_stock = Mock(side_effect=lambda tags:w.reflect(t))
        t.verify_mcs(t.transfer_mcs_and_stock(order))
        t.update_stock.assert_called_once_with((200,))

    def test_full_workflow_rechecks_definition_after_model_update_and_cam_read(self):
        for drift in (None,'model','cam'):
            with self.subTest(drift=drift):
                w = PlaneDirectionWorld(); t = w.trial()
                body = NS(Tag=1000,GetFaces=lambda:w.faces,GetEdges=lambda:[w.edge])
                topology = {1000:(tuple(f.Tag for f in w.faces),(w.edge.Tag,))}
                t.bodies, t.part.Bodies = [body],[body]
                t.nx.Curve = type('Curve', (), {})
                t.bounds, t.protected_bodies, t.operations = {},set(),[]
                t.check_cam_builders = Mock()
                t.capture_stock, t.capture_parameters, t.capture_body_history = Mock(),Mock(),Mock()
                t.checkpoint_model_history, t.verify_model_history = Mock(),Mock()
                t.verify_retained_model_history, t.verify_model_warnings = Mock(),Mock()
                t.capture_topology = Mock(return_value=topology)
                t.operation_snapshot = Mock(return_value=[])
                t.geometry_snapshot = Mock(return_value={})
                t.native.reflection_matrix = Mock(return_value=[0]*16)
                t.native.sketch_of_geometry = Mock(return_value=0)
                t.native.entity_parents = Mock(return_value=())
                t.native.transform_entities = Mock(side_effect=lambda *args:w.reflect(t))
                t.setup = NS(DeleteToolPath=Mock())
                t.custom_points, t.boundary_planes = {},{}
                t.transfer_custom_points, t.verify_custom_points = Mock(),Mock()
                t.transfer_boundary_planes, t.verify_boundary_planes = Mock(),Mock()
                t.box_csys, t.moved_boundary_count, t.cam_builder_state = 999,0,{}
                t.uf.Obj = NS(DeleteObject=Mock())
                t.body_conversions, t.raw_spatial, t.unavailable, t.cam_extra_unverified = [],[],set(),set()
                if drift == 'model':
                    t.native.update_model.side_effect = lambda:setattr(w.point,'Coordinates',xyz(0,0,0))
                elif drift == 'cam':
                    def read():
                        if t.check_cam_builders.call_count == 2:
                            w.support.sources = []
                    t.check_cam_builders.side_effect = read
                if drift:
                    with self.assertRaises(RuntimeError): t.run_in_absolute_wcs()
                else:
                    t.run_in_absolute_wcs()
                    self.assertEqual(t.check_cam_builders.call_count,2)
                    t.verify_model_history.assert_called_once()
                    t.verify_boundary_planes.assert_called_once()
                t.native.transform_entities.assert_called_once()
                t.setup.DeleteToolPath.assert_called_once()
                self.assertEqual(len(w.factories),1)


if __name__ == '__main__':
    unittest.main()
