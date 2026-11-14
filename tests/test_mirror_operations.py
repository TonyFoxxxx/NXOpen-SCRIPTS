"""Operation admission/coverage regressions, with fake API objects, not NX.

These tests exercise failures and lost coverage; they do not certify builder
signatures, DC 2606 compatibility, or the meaning of machining parameters.
"""
import importlib.util
from pathlib import Path
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock


SPEC = importlib.util.spec_from_file_location(
    'mirror_operations', Path(__file__).parents[1] / 'scripts/NX_Mirror_Project.py')
M = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(M)


class Operation:
    pass


class Geometry:
    def __init__(self):
        self.InitializeData = Mock()


def operation(kind='CavityMilling', tag=10):
    return type(kind, (Operation,), {'Name': 'OP_' + str(tag), 'Tag': tag})()


def editor(kind='CavityMillingBuilder', **fields):
    value = type(kind, (), {})()
    value.Validate, value.Destroy, value.Commit = Mock(return_value=True), Mock(), Mock()
    for name, field in fields.items():
        setattr(value, name, field)
    return value


def trial_for(node, builder, specialized=True):
    trial = object.__new__(M.Trial)
    trial.lines, trial.nodes = [], [node]
    trial.cam_builder_state, trial.cam_builder_labels = None, {}
    trial.nx = NS(CAM=NS(Operation=Operation, Geometry=Geometry,
                         GeometryGroup=type('GeometryGroup', (Geometry,), {})))
    factory = Mock(return_value=builder)
    name = M.OPERATION_BUILDERS[type(node).__name__] if specialized else 'CreateBuilder'
    trial.setup = NS(CAMOperationCollection=NS(**{name: factory}), CAMGroupCollection=NS())
    return trial, factory


class OperationEditorTests(unittest.TestCase):
    def test_specialized_cavity_factory_cannot_bypass_catalog_or_return_null(self):
        for kind in ('RoughTurningBuilder', 'OperationBuilder', 'UnknownMillingBuilder', None):
            with self.subTest(kind=kind):
                builder = editor(kind) if kind else None
                trial, _ = trial_for(operation(), builder)
                fallback = trial.setup.CAMOperationCollection.CreateBuilder = Mock()
                with self.assertRaisesRegex(RuntimeError, 'CreateCavityMillingBuilder.*' + str(kind or 'NoneType')):
                    trial.check_cam_builders()
                fallback.assert_not_called()
                if builder:
                    builder.Destroy.assert_called_once()
                    builder.Validate.assert_not_called()
                    builder.Commit.assert_not_called()
                self.assertIsNone(trial.cam_builder_state)

    def test_specialized_factory_error_is_not_hidden_by_generic_retry(self):
        trial, factory = trial_for(operation(), editor())
        error = RuntimeError('NX rejected the cavity definition')
        factory.side_effect = error
        fallback = trial.setup.CAMOperationCollection.CreateBuilder = Mock()
        with self.assertRaises(RuntimeError) as caught:
            trial.check_cam_builders()
        self.assertIs(caught.exception, error)
        fallback.assert_not_called()
        self.assertIn('OP_10 [CavityMilling] / CreateCavityMillingBuilder', trial.stage)

    def test_cleanup_failure_retains_rejected_editor_in_exception_chain(self):
        builder = editor('UnknownMillingBuilder')
        builder.Destroy.side_effect = RuntimeError('editor cleanup failed')
        trial, _ = trial_for(operation(), builder)
        with self.assertRaisesRegex(RuntimeError, 'editor cleanup failed') as caught:
            trial.check_cam_builders()
        self.assertIn('CreateCavityMillingBuilder → UnknownMillingBuilder', str(caught.exception.__context__))
        builder.Validate.assert_not_called()

    def test_cavity_both_routes_reload_all_exposed_channels_twice_without_commit(self):
        for specialized in (True, False):
            with self.subTest(specialized=specialized):
                # Explicit independent fixture for the seven geometry channels.
                fields = {name: Geometry() for name in (
                    'PartGeometry', 'BlankGeometry', 'CheckGeometry', 'TrimGeometry',
                    'CutAreaGeometry', 'WallGeometry', 'DriveGeometry')}
                builder = editor(**fields)
                trial, factory = trial_for(operation(), builder, specialized)
                trial.check_cam_builders()
                baseline = trial.cam_builder_state
                trial.check_cam_builders()
                self.assertIs(trial.cam_builder_state, baseline)
                self.assertEqual(factory.call_count, 2)
                for field in fields.values():
                    self.assertEqual([c.args for c in field.InitializeData.call_args_list], [(True,), (True,)])
                self.assertEqual(builder.Validate.call_count, 2)
                self.assertEqual(builder.Destroy.call_count, 2)
                builder.Commit.assert_not_called()
                self.assertEqual(baseline[10]['Operation.Builder'], ('text', 'CavityMillingBuilder'))
                self.assertIn('Полнота настроек не подтверждена', '\n'.join(trial.lines))

    def test_switch_between_two_recognized_builders_does_not_pass_as_same_operation(self):
        node = operation('UnmappedOperation')
        first, second = editor(), editor('SurfaceContourBuilder')
        trial, factory = trial_for(node, first, False)
        factory.side_effect = [first, second]
        trial.check_cam_builders()
        baseline = trial.cam_builder_state
        with self.assertRaisesRegex(RuntimeError, 'Operation.Builder.*CavityMillingBuilder.*SurfaceContourBuilder'):
            trial.check_cam_builders()
        self.assertIs(trial.cam_builder_state, baseline)
        first.Destroy.assert_called_once()
        second.Destroy.assert_called_once()

    def test_disappearing_part_or_drive_geometry_is_detected_even_when_validate_passes(self):
        for name in ('PartGeometry', 'DriveGeometry'):
            for missing in (True, False):
                with self.subTest(name=name, missing=missing):
                    builder = editor(**{name: Geometry()})
                    trial, _ = trial_for(operation(), builder)
                    trial.check_cam_builders()
                    if missing:
                        delattr(builder, name)
                    else:
                        setattr(builder, name, None)
                    with self.assertRaisesRegex(RuntimeError, name + r'\.API'):
                        trial.check_cam_builders()
                    self.assertEqual(builder.Destroy.call_count, 2)
                    builder.Commit.assert_not_called()

    def test_geometry_getter_attribute_error_is_not_treated_as_absent_property(self):
        builder = editor()
        def fail(self):
            raise AttributeError('NX getter cannot read cut area')
        type(builder).CutAreaGeometry = property(fail)
        trial, _ = trial_for(operation(), builder)
        with self.assertRaisesRegex(AttributeError, 'cannot read cut area'):
            trial.check_cam_builders()
        self.assertIn('OP_10 [CavityMilling] / CutAreaGeometry', trial.stage)
        builder.Destroy.assert_called_once()
        builder.Validate.assert_not_called()
        self.assertIsNone(trial.cam_builder_state)

    def test_incomplete_dir_does_not_skip_readable_geometry_or_hide_descriptor_errors(self):
        geometry = Geometry()
        builder = editor(PartGeometry=geometry)
        type(builder).__dir__ = lambda self: []
        trial, _ = trial_for(operation(), builder)
        trial.check_cam_builders()
        geometry.InitializeData.assert_called_once_with(True)
        self.assertEqual(trial.cam_builder_state[10]['PartGeometry.API'], ('text', 'Geometry'))
        def fail(self):
            raise AttributeError('broken descriptor with incomplete dir')
        type(builder).DriveGeometry = property(fail)
        with self.assertRaisesRegex(AttributeError, 'broken descriptor'):
            trial.check_cam_builders()
        self.assertEqual(builder.Destroy.call_count, 2)

    def test_native_reload_error_retains_context_and_destroys_editor(self):
        geometry = Geometry()
        geometry.InitializeData.side_effect = M.NativeApiError(12345, 'InitializeData', 'invalid drive')
        builder = editor('SurfaceContourBuilder', DriveGeometry=geometry)
        trial, _ = trial_for(operation('SurfaceContour'), builder)
        with self.assertRaises(M.NativeApiError) as caught:
            trial.check_cam_builders()
        self.assertEqual(caught.exception.code, 12345)
        self.assertIn('OP_10 [SurfaceContour] / DriveGeometry', trial.stage)
        builder.Destroy.assert_called_once()
        builder.Validate.assert_not_called()

    def test_absent_null_and_unhandled_geometry_are_distinct_unverified_states(self):
        builder = editor(BlankGeometry=None, DriveGeometry=NS())
        trial, _ = trial_for(operation(), builder)
        trial.check_cam_builders()
        state = trial.cam_builder_state[10]
        self.assertEqual(state['PartGeometry.API'], ('text', 'нет свойства'))
        self.assertEqual(state['BlankGeometry.API'], ('text', 'NoneType'))
        self.assertEqual(state['DriveGeometry.API'], ('text', 'SimpleNamespace'))
        unverified = '\n'.join(trial.cam_extra_unverified)
        for name in ('PartGeometry', 'BlankGeometry', 'DriveGeometry'):
            self.assertIn(name, unverified)

    def test_generic_hole_and_fbm_editors_remain_unverified_without_feature_getter(self):
        for kind in ('HoleDrillingBuilder', 'HoleMakingBuilder', 'ThreadMillingBuilder',
                     'CylinderMillingBuilder', 'FeatureMillingBuilder', 'GenericFeatureOperationBuilder'):
            with self.subTest(kind=kind):
                trial, _ = trial_for(operation('UnmappedOperation'), editor(kind), False)
                trial.check_cam_builders()
                self.assertTrue(any('полное определение отверстий/FBM' in x for x in trial.cam_extra_unverified))

    def test_missing_factory_uses_generic_but_failed_validation_still_stops(self):
        builder = editor('HoleDrillingBuilder')
        builder.Validate.return_value = False
        trial, _ = trial_for(operation('HoleDrilling'), builder, False)
        with self.assertRaisesRegex(RuntimeError, 'не прошёл проверку: OP_10'):
            trial.check_cam_builders()
        builder.Destroy.assert_called_once()
        builder.Commit.assert_not_called()
        self.assertIsNone(trial.cam_builder_state)


class OperationInventoryTests(unittest.TestCase):
    def setup_tree(self):
        class Group:
            Tag, Name = 1, 'GEOMETRY'
            def GetMembers(self):
                return [shared]
        shared, detached = operation(tag=10), operation('UnknownOperation', 20)
        root = Group()
        nx = NS(CAM=NS(NCGroup=Group, CAMSetup=NS(View=NS(Geometry=1))))
        setup = NS(GetRoot=lambda view: root, CAMOperationCollection=[shared, detached])
        return setup, nx, root, shared, detached

    def test_collection_only_operation_is_included_once_with_geometry_groups(self):
        setup, nx, root, shared, detached = self.setup_tree()
        actual_root, nodes = M.cam_nodes(setup, nx)
        self.assertIs(actual_root, root)
        self.assertEqual(nodes, [root, shared, detached])

    def test_unknown_detached_operation_cannot_escape_pre_transform_validation(self):
        setup, nx, _, shared, detached = self.setup_tree()
        _, nodes = M.cam_nodes(setup, nx)
        cavity, unknown = editor(), editor('MillUserDefinedBuilder')
        trial, _ = trial_for(shared, cavity)
        trial.nodes = nodes
        trial.setup.CAMOperationCollection.CreateBuilder = Mock(return_value=unknown)
        trial.native = NS(transform_entities=Mock())
        trial.setup.DeleteToolPath = Mock()
        with self.assertRaisesRegex(RuntimeError, 'OP_20.*MillUserDefinedBuilder'):
            trial.run_in_absolute_wcs()
        unknown.Destroy.assert_called_once()
        unknown.Validate.assert_not_called()
        trial.native.transform_entities.assert_not_called()
        trial.setup.DeleteToolPath.assert_not_called()


if __name__ == '__main__':
    unittest.main()
