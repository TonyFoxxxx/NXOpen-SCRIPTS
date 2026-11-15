"""Controlled model-history conversion; mocked NX ownership, not kernel tests."""
import importlib.util
from pathlib import Path
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock

SPEC = importlib.util.spec_from_file_location('mirror_body_history', Path(__file__).parents[1] / 'scripts/NX_Mirror_Project.py')
M = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(M)


class Feature:
    def __init__(self, tag, kind, name='', parents=(), bodies=()):
        self.Tag, self.FeatureType, self.Name = tag,kind,name
        self.parents, self.bodies, self.expressions = list(parents),list(bodies),[]
        self.warnings = []
        self.OwningPart = None

    def GetParents(self):
        return self.parents[:]

    def GetExpressions(self):
        return self.expressions[:]

    def GetFeatureErrorMessages(self):
        return []

    def GetFeatureWarningMessages(self):
        return self.warnings[:]


class BodyFeature(Feature):
    def GetBodies(self):
        return self.bodies[:]


class Expression:
    def __init__(self, tag, owner):
        self.Tag, self.Name, self.Type = tag,'p'+str(tag),'Number'
        self.RightHandSide, self.Units = '16',NS(Name='MilliMeter')
        self.users, self.references = [owner],[]
        owner.expressions.append(self)

    def GetUsingFeatures(self):
        return self.users[:]

    def GetReferencingExpressions(self):
        return self.references[:]


class Body:
    def __init__(self, tag):
        self.Tag, self.features = tag,[]
        self.faces, self.edges = [NS(Tag=tag+1),NS(Tag=tag+2)],[NS(Tag=tag+3)]

    def GetFeatures(self):
        return self.features[:]

    def GetFaces(self):
        return self.faces[:]

    def GetEdges(self):
        return self.edges[:]


class Fixture:
    def __init__(self):
        self.body, self.blank_body, self.box_body = Body(1000),Body(2000),Body(3000)
        self.sketch = Feature(1,'SKETCH')
        self.extrude = BodyFeature(10,'EXTRUDE',parents=[self.sketch],bodies=[self.body])
        self.blends = [BodyFeature(tag,'BLEND',parents=[self.extrude],bodies=[self.body]) for tag in (11,12)]
        self.blank = BodyFeature(20,'Tooling Box','Blank',bodies=[self.blank_body])
        self.box = BodyFeature(21,'Tooling Box','Part Box',parents=[self.extrude]+self.blends,bodies=[self.box_body])
        self.mcs = Feature(22,'DATUM_CSYS','MCS')
        self.group = Feature(90,'FSET','CAM_WORKPIECE')
        self.model_features = [self.extrude]+self.blends
        self.body.features = self.model_features[:]
        self.blank_body.features, self.box_body.features = [self.blank],[self.box]
        for i,feature in enumerate(self.model_features+[self.blank,self.box,self.mcs]):
            Expression(100+i,feature)
        self.brep = BodyFeature(30,'BREP',bodies=[self.body])
        t = self.trial = object.__new__(M.Trial)
        t.part = NS(Tag=5000,Features=[self.sketch]+self.model_features+[self.blank,self.box,self.mcs,self.group],
                    Bodies=[self.body,self.blank_body,self.box_body])
        for feature in t.part.Features+[self.brep]:
            feature.OwningPart = t.part
        t.nx = NS(Features=NS(BodyFeature=BodyFeature))
        t.bodies = t.part.Bodies[:]
        t.protected_bodies, t.stock_features = {2000,3000},{20:None,21:None}
        t.history_baseline, t.history_previous = None,None
        t.body_history, t.body_conversions, t.lines = {},[],[]
        t.custom_points, t.current_custom_points, t.custom_point_labels = {},{},{}

    def initialize(self):
        operation = NS(Tag=900,IsMirror=lambda:False)
        class Group:
            Tag = 901
            def GetMembers(self):
                return [operation]
        t = self.trial
        t.nx.CAM = NS(NCGroup=Group,CAMSetup=NS(View=NS(Geometry=1)))
        t.part.CAMSetup = NS(CAMOperationCollection=[operation],GetRoot=lambda view:Group())
        self.trial = M.Trial(None,t.nx,None,t.part,M.MirrorPlane([0,0,0],[1,0,0]),[],native=NS())
        return self.trial

    def topology(self):
        return {b.Tag:(tuple(f.Tag for f in b.faces),tuple(e.Tag for e in b.edges)) for b in self.trial.bodies}

    def before(self):
        self.trial.checkpoint_model_history('Before')
        self.trial.capture_body_history(self.topology())

    def convert(self, *args):
        t = self.trial
        t.part.Features = [f for f in t.part.Features if f not in self.model_features]+[self.brep]
        self.body.features = [self.brep]
        self.box.parents = [self.brep]

    def after(self):
        self.trial.checkpoint_model_history('After reflection')
        self.trial.verify_model_history()

    def workflow(self):
        t = self.trial
        t.check_cam_builders, t.capture_stock, t.capture_parameters = Mock(),Mock(),Mock()
        t.geometry_snapshot = Mock(return_value={})
        t.capture_topology = self.topology
        t.operation_snapshot = lambda:[]
        t.frames, t.refs, t.bounds, t.operations = {},{},{},[]
        t.nx.Curve, t.nx.Point = type('Curve',(),{}),type('Point',(),{})
        t.native = NS(reflection_matrix=lambda plane:[0]*16,transform_entities=Mock(side_effect=self.convert),update_model=Mock())
        t.native.sketch_of_geometry = Mock(return_value=0)
        t.native.entity_parents = Mock(return_value=())
        t.plane = M.MirrorPlane([0,0,0],[1,0,0])
        t.transfer_mcs, t.update_stock = Mock(return_value={}),Mock()
        t.mcs_stock_order = Mock(return_value=[])
        t.setup = NS(DeleteToolPath=Mock())
        t.transfer_boundary_planes, t.verify_boundary_planes, t.verify_mcs = Mock(),Mock(),Mock()
        t.uf = NS(Obj=NS(DeleteObject=Mock()))
        t.box_csys, t.moved_boundary_count, t.cam_builder_state = 99,0,{}
        t.mcs_definitions, t.mcs_fixed_xforms, t.unavailable, t.raw_spatial = {},{},set(),[]
        t.mcs_plane_directions = {}
        return t


class GeneralFeatureHistoryTests(unittest.TestCase):
    def test_initialization_inspects_all_features_without_a_name_catalog(self):
        f = Fixture()
        f.extrude.FeatureType = 'DELETE FACE'
        f.blends[0].FeatureType = 'UNLISTED_BODY_FEATURE_2606'
        other = Feature(50,'UNLISTED_NON_BODY_FEATURE')
        f.trial.part.Features.append(other)
        for feature in f.trial.part.Features:
            feature.GetParents = Mock(wraps=feature.GetParents)
        t = f.initialize()
        for feature in t.part.Features:
            feature.GetParents.assert_called_once()
        self.assertEqual(t.operations[0].Tag,900)

    def test_unlisted_feature_external_parent_still_blocks_initialization(self):
        f = Fixture()
        f.extrude.FeatureType = 'UNLISTED_BODY_FEATURE'
        f.sketch.OwningPart = NS(Tag=6000)
        with self.assertRaisesRegex(RuntimeError,'внешнего родителя.*UNLISTED_BODY_FEATURE'):
            f.initialize()

    def test_unlisted_feature_parent_query_error_is_not_treated_as_no_parents(self):
        f = Fixture()
        f.extrude.FeatureType = 'DELETE FACE'
        error = RuntimeError('parent query failed')
        f.extrude.GetParents = Mock(side_effect=error)
        with self.assertRaises(RuntimeError) as caught:
            f.initialize()
        self.assertIs(caught.exception,error)

    def test_delete_face_and_unlisted_body_features_use_the_same_verified_conversion(self):
        # Deliberately fictitious label: a finite list of modeling names must
        # not become a second operation/feature catalog.
        for kind in ('DELETE FACE','REVOLVE','BOOLEAN','PATTERN','UNLISTED_BODY_FEATURE_2606'):
            with self.subTest(kind=kind):
                f = Fixture()
                f.extrude.FeatureType = kind
                f.before()
                f.convert()
                f.after()
                self.assertEqual(f.trial.body_conversions,[(1000,(10,11,12),30)])
                self.assertEqual(f.trial.history_baseline[10]['type'],kind)
                self.assertIn(f.sketch,f.trial.part.Features)

    def test_missing_body_feature_class_stops_before_transform_or_path_deletion(self):
        f = Fixture()
        t = f.workflow()
        del t.nx.Features.BodyFeature
        with self.assertRaisesRegex(RuntimeError,'Недоступен NXOpen.Features.BodyFeature'):
            t.run_in_absolute_wcs()
        t.native.transform_entities.assert_not_called()
        t.setup.DeleteToolPath.assert_not_called()

    def test_body_ownership_query_failure_stops_before_transform_and_identifies_feature(self):
        f = Fixture()
        f.extrude.FeatureType, f.extrude.Name = 'DELETE FACE','Remove patch'
        error = RuntimeError('NX body ownership failed')
        f.extrude.GetBodies = Mock(side_effect=error)
        t = f.workflow()
        with self.assertRaises(RuntimeError) as caught:
            t.run_in_absolute_wcs()
        self.assertIs(caught.exception,error)
        self.assertIn('Remove patch [DELETE FACE] (элемент 10)',t.stage)
        t.native.transform_entities.assert_not_called()
        t.setup.DeleteToolPath.assert_not_called()

    def test_missing_or_null_body_query_does_not_fall_back_to_label_or_membership(self):
        for query in (None,Mock(return_value=None)):
            with self.subTest(query=query):
                f = Fixture()
                f.extrude.FeatureType = 'DELETE FACE'
                f.extrude.GetBodies = query
                t = f.workflow()
                with self.assertRaises(TypeError):
                    t.run_in_absolute_wcs()
                t.native.transform_entities.assert_not_called()

    def test_unreadable_expression_users_stop_before_transform(self):
        for query in ('GetUsingFeatures','GetReferencingExpressions'):
            with self.subTest(query=query):
                f = Fixture()
                f.extrude.FeatureType = 'UNLISTED_BODY_FEATURE'
                error = RuntimeError(query+' failed')
                setattr(f.extrude.expressions[0],query,Mock(side_effect=error))
                t = f.workflow()
                with self.assertRaises(RuntimeError) as caught:
                    t.run_in_absolute_wcs()
                self.assertIs(caught.exception,error)
                t.native.transform_entities.assert_not_called()
                t.setup.DeleteToolPath.assert_not_called()

    def test_expression_inventory_change_before_transform_is_rejected(self):
        f = Fixture()
        f.extrude.GetExpressions = Mock(side_effect=[f.extrude.expressions[:],[]])
        t = f.workflow()
        with self.assertRaisesRegex(RuntimeError,'Состав выражений изменился ещё до отражения'):
            t.run_in_absolute_wcs()
        t.native.transform_entities.assert_not_called()

    def test_no_body_or_multiple_bodies_cannot_authorize_generic_feature_conversion(self):
        for bodies in ([],[1000,2000]):
            with self.subTest(bodies=bodies):
                f = Fixture()
                f.extrude.FeatureType = 'UNLISTED_BODY_FEATURE'
                f.extrude.GetBodies = Mock(return_value=[NS(Tag=tag) for tag in bodies])
                f.before()
                f.convert()
                with self.assertRaisesRegex(RuntimeError,'UNLISTED_BODY_FEATURE.*GetBodies'):
                    f.after()
                self.assertFalse(f.trial.body_conversions)

    def test_protected_stock_feature_cannot_enter_model_body_conversion(self):
        f = Fixture()
        f.model_features.append(f.blank)
        f.body.features.append(f.blank)
        f.blank.GetBodies = Mock(return_value=[f.body])
        f.before()
        f.convert()
        with self.assertRaisesRegex(RuntimeError,'Blank.*защищённая заготовка'):
            f.after()
        f.blank.GetBodies.assert_not_called()

    def test_unlisted_non_body_feature_is_monitored_and_cannot_disappear(self):
        for remove in (False,True):
            with self.subTest(remove=remove):
                f = Fixture()
                other = Feature(50,'UNLISTED_NON_BODY_FEATURE','Other')
                f.trial.part.Features.append(other)
                f.before()
                f.convert()
                if remove:
                    f.trial.part.Features.remove(other)
                    with self.assertRaisesRegex(RuntimeError,'Other.*UNLISTED_NON_BODY_FEATURE'):
                        f.after()
                else:
                    f.after()

    def test_generic_body_feature_with_external_parameter_user_still_fails(self):
        f = Fixture()
        f.extrude.FeatureType = 'DELETE FACE'
        f.extrude.expressions[0].users.append(f.mcs)
        f.before()
        f.convert()
        with self.assertRaisesRegex(RuntimeError,'используется вне заменяемой истории'):
            f.after()

    def test_generic_body_conversion_still_rejects_replaced_face_identifiers(self):
        f = Fixture()
        f.extrude.FeatureType = 'DELETE FACE'
        f.before()
        f.convert()
        f.body.faces[0] = NS(Tag=9999)
        with self.assertRaisesRegex(RuntimeError,'идентификаторы граней/рёбер'):
            f.after()

    def test_generic_body_conversion_keeps_full_workflow_guards(self):
        for kind in ('DELETE FACE','UNLISTED_BODY_FEATURE'):
            with self.subTest(kind=kind):
                f = Fixture()
                f.extrude.FeatureType = kind
                t = f.workflow()
                t.run_in_absolute_wcs()
                self.assertEqual(t.check_cam_builders.call_count,2)
                self.assertEqual(t.geometry_snapshot.call_count,2)
                t.verify_mcs.assert_called_once()
                t.verify_boundary_planes.assert_called_once()
                t.setup.DeleteToolPath.assert_called_once()
                self.assertEqual(len(t.body_conversions),1)

    def test_lost_cam_selection_still_fails_after_generic_body_conversion(self):
        f = Fixture()
        f.extrude.FeatureType = 'DELETE FACE'
        t = f.workflow()
        t.geometry_snapshot.side_effect = [{},{'lost_selection':1}]
        with self.assertRaisesRegex(RuntimeError,'Изменились ссылки на выбранную CAM'):
            t.run_in_absolute_wcs()
        self.assertEqual(len(t.body_conversions),1)
        self.assertNotIn('ПРОЙДЕНО','\n'.join(t.lines))


class BodyHistoryTests(unittest.TestCase):
    def test_reported_extrude_two_blends_conversion_is_accepted_and_logged(self):
        f = Fixture()
        f.before()
        original = f.trial.history_baseline
        f.convert()
        f.after()
        self.assertEqual(f.trial.body_conversions,[(1000,(10,11,12),30)])
        self.assertIs(f.trial.history_baseline,original)
        self.assertIn('EXTRUDE',original[10]['type'])
        self.assertIn('Разрешён переход истории модельного тела 1000','\n'.join(f.trial.lines))
        self.assertIn(f.sketch,f.trial.part.Features)
        self.assertEqual(f.box.expressions[0].Tag,104)

    def test_feature_labels_alone_do_not_authorize_conversion_without_ownership_snapshot(self):
        f = Fixture()
        f.trial.checkpoint_model_history('Before')
        f.convert()
        with self.assertRaisesRegex(RuntimeError,'выходят за разрешённый переход'):
            f.after()

    def test_unchanged_history_does_not_need_or_report_conversion(self):
        f = Fixture()
        f.before()
        f.after()
        self.assertFalse(f.trial.body_conversions)
        self.assertFalse(f.trial.lines)

    def test_source_feature_shared_with_stock_is_rejected(self):
        f = Fixture()
        f.extrude.bodies.append(f.blank_body)
        f.before()
        f.convert()
        with self.assertRaisesRegex(RuntimeError,'принадлежность истории только одному телу'):
            f.after()

    def test_protected_stock_is_not_captured_for_conversion(self):
        f = Fixture()
        f.blank_body.GetFeatures = Mock(side_effect=AssertionError('Protected stock must not be a conversion candidate'))
        f.before()
        self.assertEqual(set(f.trial.body_history),{1000})
        f.blank_body.GetFeatures.assert_not_called()

    def test_non_body_features_cannot_be_erased_even_with_get_bodies_and_a_familiar_label(self):
        for kind in ('DATUM_CSYS','DATUM_PLANE','SKETCH','FSET','EXTRUDE','NEW_CUSTOM_FEATURE'):
            with self.subTest(kind=kind):
                f = Fixture()
                f.extrude.__class__ = Feature
                f.extrude.FeatureType = kind
                f.extrude.GetBodies = Mock(return_value=[f.body])
                f.before()
                f.convert()
                with self.assertRaisesRegex(RuntimeError,'вне NXOpen.Features.BodyFeature'):
                    f.after()
                f.extrude.GetBodies.assert_not_called()

    def test_partial_history_removal_is_not_accepted(self):
        f = Fixture()
        f.before()
        f.convert()
        f.trial.part.Features.append(f.blends[0])
        with self.assertRaisesRegex(RuntimeError,'полная замена'):
            f.after()

    def test_changed_body_identity_is_rejected(self):
        f = Fixture()
        f.before()
        f.convert()
        f.body.Tag = 4000
        with self.assertRaisesRegex(RuntimeError,'идентификатор тела'):
            f.after()

    def test_same_count_but_replaced_face_or_edge_is_rejected(self):
        for field in ('faces','edges'):
            with self.subTest(field=field):
                f = Fixture()
                f.before()
                f.convert()
                getattr(f.body,field)[0] = NS(Tag=9999)
                with self.assertRaisesRegex(RuntimeError,'идентификаторы граней/рёбер'):
                    f.after()

    def test_brep_must_belong_only_to_original_body(self):
        f = Fixture()
        f.before()
        f.convert()
        f.brep.bodies = [f.box_body]
        with self.assertRaisesRegex(RuntimeError,'BREP того же модельного тела'):
            f.after()

    def test_brep_with_extra_parent_or_expression_is_rejected(self):
        for extra in ('parent','expression'):
            with self.subTest(extra=extra):
                f = Fixture()
                f.before()
                f.convert()
                if extra == 'parent':
                    f.brep.parents = [f.sketch]
                else:
                    Expression(900,f.brep)
                with self.assertRaisesRegex(RuntimeError,'BREP того же модельного тела'):
                    f.after()

    def test_new_feature_must_be_brep(self):
        f = Fixture()
        f.before()
        f.convert()
        f.brep.FeatureType = 'BLOCK'
        with self.assertRaisesRegex(RuntimeError,'BREP того же модельного тела'):
            f.after()

    def test_two_new_body_features_are_not_accepted(self):
        f = Fixture()
        f.before()
        f.convert()
        f.body.features.append(Feature(31,'BREP',bodies=[f.body]))
        with self.assertRaisesRegex(RuntimeError,'один элемент BREP'):
            f.after()

    def test_stock_parent_remap_cannot_drop_an_unrelated_parent(self):
        f = Fixture()
        f.box.parents.append(f.sketch)
        f.before()
        f.convert()
        with self.assertRaisesRegex(RuntimeError,'Part Box'):
            f.after()

    def test_stock_parent_must_map_to_this_body_not_another_feature(self):
        f = Fixture()
        f.before()
        f.convert()
        f.box.parents = [f.blank]
        with self.assertRaisesRegex(RuntimeError,'Part Box'):
            f.after()

    def test_stock_expression_replacement_or_deletion_still_fails(self):
        for replace in (True,False):
            with self.subTest(replace=replace):
                f = Fixture()
                f.before()
                f.convert()
                f.box.expressions.clear()
                if replace:
                    Expression(900,f.box)
                with self.assertRaisesRegex(RuntimeError,'Part Box'):
                    f.after()

    def test_stock_cannot_disappear_or_become_brep(self):
        for delete in (True,False):
            with self.subTest(delete=delete):
                f = Fixture()
                f.before()
                f.convert()
                if delete:
                    f.trial.part.Features.remove(f.box)
                else:
                    f.box.FeatureType = 'BREP'
                with self.assertRaisesRegex(RuntimeError,'Part Box'):
                    f.after()

    def test_unrelated_feature_or_mcs_changes_still_fail(self):
        for change in ('add','delete_sketch','mcs_expression'):
            with self.subTest(change=change):
                f = Fixture()
                f.before()
                f.convert()
                if change == 'add':
                    f.trial.part.Features.append(Feature(31,'BREP','Unexpected'))
                elif change == 'delete_sketch':
                    f.trial.part.Features.remove(f.sketch)
                else:
                    Expression(900,f.mcs)
                with self.assertRaisesRegex(RuntimeError,'выходят за разрешённый переход'):
                    f.after()

    def test_external_expression_or_feature_user_blocks_conversion(self):
        for usage in ('expression','feature'):
            with self.subTest(usage=usage):
                f = Fixture()
                if usage == 'expression':
                    f.extrude.expressions[0].references = [f.box.expressions[0]]
                else:
                    f.extrude.expressions[0].users.append(f.mcs)
                f.before()
                f.convert()
                with self.assertRaisesRegex(RuntimeError,'используется вне заменяемой истории'):
                    f.after()

    def test_internal_expression_dependencies_can_disappear_together(self):
        f = Fixture()
        f.extrude.expressions[0].references = [f.blends[0].expressions[0]]
        f.extrude.expressions[0].users.append(f.blends[0])
        f.before()
        f.convert()
        f.after()
        self.assertEqual(len(f.trial.body_conversions),1)

    def test_missing_dependency_snapshot_is_not_treated_as_no_users(self):
        f = Fixture()
        f.before()
        del f.trial.body_history[1000]['dependencies'][100]
        f.convert()
        with self.assertRaisesRegex(RuntimeError,'Не прочитаны зависимости выражения: 100'):
            f.after()

    def test_full_workflow_accepts_conversion_and_keeps_cam_mcs_and_path_checks(self):
        f = Fixture()
        t = f.workflow()
        t.run_in_absolute_wcs()
        self.assertEqual(t.check_cam_builders.call_count,2)
        self.assertEqual(t.geometry_snapshot.call_count,2)
        t.verify_mcs.assert_called_once()
        t.verify_boundary_planes.assert_called_once()
        t.setup.DeleteToolPath.assert_called_once()
        self.assertIsNone(t.box_csys)
        report = '\n'.join(t.lines)
        self.assertIn('Модельные тела с заменой истории построения на BREP: 1',report)
        self.assertIn('ПРОЙДЕНО',report)

    def test_full_workflow_cam_reference_change_still_fails_after_accepted_conversion(self):
        f = Fixture()
        t = f.workflow()
        t.geometry_snapshot.side_effect = [{},{'lost_selection':1}]
        with self.assertRaisesRegex(RuntimeError,'Изменились ссылки на выбранную CAM'):
            t.run_in_absolute_wcs()
        self.assertEqual(len(t.body_conversions),1)
        self.assertNotIn('ПРОЙДЕНО','\n'.join(t.lines))

    def test_full_workflow_cam_editor_error_still_fails(self):
        f = Fixture()
        t = f.workflow()
        t.check_cam_builders.side_effect = [None,RuntimeError('bounding geometry invalid')]
        with self.assertRaisesRegex(RuntimeError,'bounding geometry invalid'):
            t.run_in_absolute_wcs()
        self.assertNotIn('ПРОЙДЕНО','\n'.join(t.lines))


if __name__ == '__main__':
    unittest.main()
