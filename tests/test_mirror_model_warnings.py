"""Model-warning and retained-history guards; fake NX objects, no kernel claim."""
import unittest
from types import SimpleNamespace as NS
from unittest.mock import Mock

from test_mirror_body_history import M, Fixture, Feature, Expression, Body, BodyFeature


PARENT_WARNING = 'Parent of the point is either suppressed or modeled away.'
CONSTRAINT_WARNING = 'Sketch becomes under constrained due to the edit.'


class ModelWarningTests(unittest.TestCase):
    def test_existing_warnings_are_logged_and_do_not_block_unchanged_model(self):
        f = Fixture()
        f.sketch.Name = 'SKETCH_001'
        f.sketch.warnings = [PARENT_WARNING]
        f.before()
        f.after()
        self.assertIn('Исходные предупреждения модели: Before', '\n'.join(f.trial.lines))
        self.assertIn(PARENT_WARNING, '\n'.join(f.trial.lines))
        self.assertEqual(f.trial.history_baseline[1]['warnings'], (PARENT_WARNING,))

    def test_warning_only_change_is_rejected_with_feature_and_complete_text(self):
        f = Fixture()
        f.sketch.Name = 'SKETCH_003'
        f.before()
        f.sketch.warnings = [PARENT_WARNING, CONSTRAINT_WARNING]
        with self.assertRaises(RuntimeError) as caught:
            f.after()
        for text in ('SKETCH_003', PARENT_WARNING, CONSTRAINT_WARNING, 'элемент 1'):
            self.assertIn(text, str(caught.exception))
        self.assertIn('Проверка предупреждений', f.trial.stage)
        self.assertIn('Появилось: ' + PARENT_WARNING, '\n'.join(f.trial.lines))

    def test_warning_order_is_not_significant(self):
        f = Fixture()
        f.sketch.warnings = [PARENT_WARNING, CONSTRAINT_WARNING]
        f.before()
        f.trial.lines.clear()
        f.sketch.warnings.reverse()
        f.after()
        self.assertFalse(f.trial.lines)

    def test_increased_warning_multiplicity_is_not_silently_deduplicated(self):
        f = Fixture()
        f.sketch.warnings = [PARENT_WARNING]
        f.before()
        f.sketch.warnings.append(PARENT_WARNING)
        with self.assertRaisesRegex(RuntimeError, PARENT_WARNING):
            f.after()

    def test_disappearing_baseline_warning_is_not_a_new_failure(self):
        f = Fixture()
        f.sketch.warnings = [PARENT_WARNING]
        f.before()
        f.sketch.warnings.clear()
        f.after()
        self.assertIn('Больше не сообщается: ' + PARENT_WARNING, '\n'.join(f.trial.lines))

    def test_transient_warning_is_logged_without_replacing_original_baseline(self):
        f = Fixture()
        f.before()
        baseline = f.trial.history_baseline
        f.box.warnings = [PARENT_WARNING]
        f.trial.checkpoint_model_history('Before stock update')
        f.box.warnings.clear()
        f.after()
        self.assertIs(f.trial.history_baseline, baseline)
        self.assertIn('Before stock update', '\n'.join(f.trial.lines))
        self.assertEqual(baseline[21]['warnings'], ())

    def test_same_warning_on_another_feature_is_new(self):
        f = Fixture()
        f.sketch.warnings = [PARENT_WARNING]
        f.before()
        f.sketch.warnings.clear()
        f.mcs.warnings = [PARENT_WARNING]
        with self.assertRaisesRegex(RuntimeError, 'MCS.*DATUM_CSYS'):
            f.after()

    def test_unknown_feature_type_gets_the_same_warning_guard(self):
        f = Fixture()
        feature = Feature(50, 'CUSTOM_FEATURE_2606', 'Custom')
        f.trial.part.Features.append(feature)
        f.before()
        feature.warnings = ['Не удалось сохранить связь α → β']
        with self.assertRaisesRegex(RuntimeError, 'Не удалось сохранить связь α → β'):
            f.after()

    def test_permitted_body_conversion_does_not_exempt_warning_on_new_brep(self):
        f = Fixture()
        f.before()
        f.convert()
        f.brep.warnings = ['Invalid body']
        with self.assertRaisesRegex(RuntimeError, 'Invalid body'):
            f.after()
        self.assertFalse(f.trial.body_conversions)

    def test_every_affected_feature_is_reported(self):
        f = Fixture()
        f.sketch.Name, f.mcs.Name = 'SKETCH_001', 'MCS'
        f.before()
        f.sketch.warnings = [PARENT_WARNING]
        f.mcs.warnings = [CONSTRAINT_WARNING]
        with self.assertRaises(RuntimeError) as caught:
            f.after()
        for text in ('SKETCH_001', 'MCS', PARENT_WARNING, CONSTRAINT_WARNING):
            self.assertIn(text, str(caught.exception))

    def test_unreadable_or_malformed_warning_result_is_not_empty(self):
        for value in (None, 'warning', [None], [123], {}, iter([])):
            with self.subTest(value=value):
                f = Fixture()
                f.sketch.GetFeatureWarningMessages = Mock(return_value=value)
                t = f.workflow()
                with self.assertRaisesRegex(RuntimeError, 'GetFeatureWarningMessages'):
                    t.run_in_absolute_wcs()
                t.native.transform_entities.assert_not_called()
                t.setup.DeleteToolPath.assert_not_called()
                self.assertIn('GetFeatureWarningMessages', t.stage)

    def test_warning_query_exception_keeps_api_error_and_stage(self):
        f = Fixture()
        error = RuntimeError('NX warning query failed')
        error.ErrorCode = 12345
        f.sketch.GetFeatureWarningMessages = Mock(side_effect=error)
        t = f.workflow()
        with self.assertRaises(RuntimeError) as caught:
            t.run_in_absolute_wcs()
        self.assertIs(caught.exception, error)
        self.assertEqual(caught.exception.ErrorCode, 12345)
        self.assertIn('GetFeatureWarningMessages', t.stage)
        t.native.transform_entities.assert_not_called()

    def test_missing_warning_method_stops_before_transform(self):
        f = Fixture()
        f.sketch.GetFeatureWarningMessages = None
        t = f.workflow()
        with self.assertRaises(TypeError):
            t.run_in_absolute_wcs()
        t.native.transform_entities.assert_not_called()

    def test_missing_warning_snapshot_is_not_a_readable_empty_list(self):
        with self.assertRaises(KeyError):
            M.model_warning_changes({1: {}}, {1: {'warnings': ()}})

    def test_new_warning_during_preparation_stops_before_transform(self):
        f = Fixture()
        t = f.workflow()
        t.capture_parameters.side_effect = lambda: f.sketch.warnings.append(PARENT_WARNING)
        with self.assertRaisesRegex(RuntimeError, PARENT_WARNING):
            t.run_in_absolute_wcs()
        t.native.transform_entities.assert_not_called()
        t.setup.DeleteToolPath.assert_not_called()

    def test_persistent_warning_stops_before_cam_point_or_boundary_writes(self):
        f = Fixture()
        t = f.workflow()
        t.native.update_model.side_effect = lambda: f.sketch.warnings.append(PARENT_WARNING)
        t.transfer_custom_points = Mock()
        with self.assertRaisesRegex(RuntimeError, PARENT_WARNING):
            t.run_in_absolute_wcs()
        t.native.transform_entities.assert_called_once()
        t.setup.DeleteToolPath.assert_called_once()
        t.transfer_custom_points.assert_not_called()
        t.transfer_boundary_planes.assert_not_called()
        self.assertNotIn('ПРОЙДЕНО', '\n'.join(t.lines))

    def test_warning_from_final_builder_read_cannot_pass(self):
        f = Fixture()
        t = f.workflow()
        def read():
            if t.check_cam_builders.call_count == 2:
                f.sketch.warnings = [PARENT_WARNING]
        t.check_cam_builders.side_effect = read
        with self.assertRaisesRegex(RuntimeError, PARENT_WARNING):
            t.run_in_absolute_wcs()
        t.verify_mcs.assert_not_called()
        self.assertNotIn('ПРОЙДЕНО', '\n'.join(t.lines))


class RetainedHistoryTests(unittest.TestCase):
    def damaged_workflow(self, mutation):
        f = Fixture()
        f.sketch.Name = 'SKETCH_001'
        f.sketch.parents = [f.mcs]
        Expression(500, f.sketch)
        t = f.workflow()
        def transform(*args):
            f.convert()
            mutation(f)
            f.sketch.warnings = [PARENT_WARNING]
        t.native.transform_entities.side_effect = transform
        t.transfer_mcs_and_stock = Mock()
        t.transfer_custom_points = Mock()
        return f, t

    def assert_early_stop(self, t):
        with self.assertRaisesRegex(RuntimeError, 'сохраняемые модельные элементы'):
            t.run_in_absolute_wcs()
        t.native.transform_entities.assert_called_once()
        t.transfer_mcs_and_stock.assert_not_called()
        t.setup.DeleteToolPath.assert_not_called()
        t.native.update_model.assert_not_called()
        t.transfer_custom_points.assert_not_called()
        t.transfer_boundary_planes.assert_not_called()
        self.assertIn('сразу после отражения', t.stage)
        self.assertNotIn('ПРОЙДЕНО', '\n'.join(t.lines))

    def test_reported_sketch_parent_and_parameter_loss_stop_before_mcs(self):
        f, t = self.damaged_workflow(lambda f: (f.sketch.parents.clear(), f.sketch.expressions.clear()))
        self.assert_early_stop(t)
        for text in ('SKETCH_001', 'Выражения: количество 1 → 0', 'Родители: количество 1 → 0', PARENT_WARNING):
            self.assertIn(text, '\n'.join(t.lines))
        self.assertEqual(set(t.history_baseline[1]['expressions']), {500})

    def test_deleted_retained_feature_stops_before_mcs(self):
        _, t = self.damaged_workflow(lambda f: f.trial.part.Features.remove(f.sketch))
        self.assert_early_stop(t)

    def test_unknown_retained_type_is_not_exempt(self):
        f, t = self.damaged_workflow(lambda f: f.sketch.parents.clear())
        f.sketch.FeatureType = 'CUSTOM_FEATURE_2606'
        self.assert_early_stop(t)

    def test_retained_feature_cannot_replace_body_parent_with_new_brep(self):
        f, t = self.damaged_workflow(lambda f: setattr(f.sketch, 'parents', [f.brep]))
        f.sketch.parents = [f.extrude]
        self.assert_early_stop(t)

    def test_body_history_is_still_checked_by_final_ownership_guard(self):
        f = Fixture()
        f.before()
        f.convert()
        f.trial.checkpoint_model_history('Reflection')
        f.trial.verify_retained_model_history()
        self.assertFalse(f.trial.body_conversions)
        f.body.faces.clear()
        with self.assertRaisesRegex(RuntimeError, 'идентификаторы граней/рёбер'):
            f.trial.verify_model_history()

    def test_pending_stock_is_not_accepted_by_the_early_guard(self):
        f = Fixture()
        f.before()
        f.convert()
        f.box.parents.clear()
        f.trial.checkpoint_model_history('Reflection before stock update')
        f.trial.verify_retained_model_history()
        with self.assertRaisesRegex(RuntimeError, 'выходят за разрешённый переход'):
            f.trial.verify_model_history()

    def test_shared_body_feature_cannot_use_early_conversion_exemption(self):
        f = Fixture()
        f.extrude.bodies.append(f.box_body)
        f.before()
        f.convert()
        f.trial.checkpoint_model_history('Reflection')
        with self.assertRaisesRegex(RuntimeError, 'сохраняемые модельные элементы'):
            f.trial.verify_retained_model_history()


class SketchTargetTests(unittest.TestCase):
    def workflow(self):
        f = Fixture()
        t = f.workflow()
        t.nx.Edge = type('Edge', (t.nx.Curve,), {})
        curve = t.nx.Curve()
        curve.Tag = 101
        t.refs[101] = curve
        t.native.sketch_of_geometry = Mock(return_value=700)
        t.native.smart_parents = Mock(return_value=None)
        t.bounding_box = Mock(return_value=[1, 2, 3, 5, 6, 7])
        return f, t

    def test_sketch_curve_is_not_a_raw_target_even_when_uf_so_says_false(self):
        _, t = self.workflow()
        t.refs[102] = t.nx.Curve()
        t.refs[103] = t.nx.Curve()
        t.native.sketch_of_geometry.side_effect = lambda tag: 700 if tag in (101, 102) else 0
        self.assertEqual(set(t.select_transform_targets()), {1000, 103})
        self.assertEqual(t.sketch_geometry, {101: 700, 102: 700})
        self.assertEqual(set(t.bounds), {101, 102, 103})
        t.native.smart_parents.assert_called_once_with(103)
        self.assertIn('эскиз=700', '\n'.join(t.lines))

    def test_sketch_point_uses_the_same_dependency_and_position_checks(self):
        _, t = self.workflow()
        t.refs[101] = t.nx.Point()
        self.assertEqual(set(t.select_transform_targets()), {1000})
        self.assertEqual(t.sketch_geometry, {101: 700})
        self.assertIn(101, t.bounds)

    def test_selected_body_edge_is_not_queried_as_sketch_or_transformed_separately(self):
        _, t = self.workflow()
        t.refs[101] = t.nx.Edge()
        # Deliberately model Edge as Curve subclass: explicit exclusion remains.
        self.assertEqual(set(t.select_transform_targets()), {1000})
        t.native.sketch_of_geometry.assert_not_called()
        t.native.smart_parents.assert_not_called()
        self.assertEqual(t.sketch_geometry, {})

    def test_non_sketch_smart_curve_keeps_existing_smart_object_behavior(self):
        _, t = self.workflow()
        t.native.sketch_of_geometry.return_value = 0
        t.native.smart_parents.return_value = (123,)
        self.assertEqual(set(t.select_transform_targets()), {1000})
        t.native.smart_parents.assert_called_once_with(101)

    def test_unreadable_membership_stops_before_geometry_mcs_and_path_changes(self):
        _, t = self.workflow()
        error = M.NativeApiError(12345, 'UF_SKET_ask_sketch_of_geom', 'failed')
        t.native.sketch_of_geometry.side_effect = error
        t.transfer_mcs_and_stock = Mock()
        with self.assertRaises(M.NativeApiError) as caught:
            t.run_in_absolute_wcs()
        self.assertIs(caught.exception, error)
        self.assertIn('UF_SKET_ask_sketch_of_geom', t.stage)
        t.native.transform_entities.assert_not_called()
        t.transfer_mcs_and_stock.assert_not_called()
        t.setup.DeleteToolPath.assert_not_called()

    def test_missing_membership_query_does_not_fall_back_to_raw_transform(self):
        _, t = self.workflow()
        del t.native.sketch_of_geometry
        with self.assertRaises(AttributeError):
            t.run_in_absolute_wcs()
        t.native.transform_entities.assert_not_called()

    def test_curve_following_model_is_verified_without_a_separate_transform(self):
        _, t = self.workflow()
        t.native.update_model.side_effect = lambda: setattr(t.bounding_box, 'return_value', [-5, 2, 3, -1, 6, 7])
        t.run_in_absolute_wcs()
        t.native.transform_entities.assert_called_once_with([1000], [0]*16)
        self.assertEqual(t.native.sketch_of_geometry.call_count, 2)
        t.verify_mcs.assert_called_once()
        t.verify_boundary_planes.assert_called_once()
        self.assertIn('ПРОЙДЕНО', '\n'.join(t.lines))

    def test_omitting_sketch_curve_from_raw_transform_does_not_omit_position_check(self):
        _, t = self.workflow()
        with self.assertRaisesRegex(RuntimeError, 'Не подтверждено отражение объекта 101'):
            t.run_in_absolute_wcs()
        self.assertIn(101, t.bounds)
        t.verify_mcs.assert_not_called()
        self.assertNotIn('ПРОЙДЕНО', '\n'.join(t.lines))

    def test_correct_position_does_not_allow_lost_or_replaced_sketch_membership(self):
        for after in (0, 701):
            with self.subTest(after=after):
                _, t = self.workflow()
                t.native.sketch_of_geometry.side_effect = [700, after]
                t.native.update_model.side_effect = lambda: setattr(t.bounding_box, 'return_value', [-5, 2, 3, -1, 6, 7])
                with self.assertRaisesRegex(RuntimeError, 'Изменилась принадлежность геометрии эскизу'):
                    t.run_in_absolute_wcs()
                self.assertNotIn('ПРОЙДЕНО', '\n'.join(t.lines))

    def test_new_sketch_warning_cannot_pass_with_correct_reflected_curve(self):
        f, t = self.workflow()
        def update():
            t.bounding_box.return_value = [-5, 2, 3, -1, 6, 7]
            f.sketch.warnings = [PARENT_WARNING]
        t.native.update_model.side_effect = update
        with self.assertRaisesRegex(RuntimeError, PARENT_WARNING):
            t.run_in_absolute_wcs()
        self.assertNotIn('ПРОЙДЕНО', '\n'.join(t.lines))

    def test_sketch_history_remains_protected_when_its_curves_are_not_raw_targets(self):
        f, t = self.workflow()
        Expression(500, f.sketch)
        t.native.transform_entities.side_effect = lambda *args: (f.convert(), f.sketch.expressions.clear())
        with self.assertRaisesRegex(RuntimeError, 'сохраняемые модельные элементы'):
            t.run_in_absolute_wcs()
        t.native.transform_entities.assert_called_once_with([1000], [0]*16)
        t.setup.DeleteToolPath.assert_not_called()


class ReportedV125HistoryTests(unittest.TestCase):
    """Observed failure pattern, with synthetic objects/tags, not NX execution.

    Excluding two selected sketch arcs is insufficient when the common model
    transform still damages retained history. A datum-parent remap alone does
    not explain or authorize the remaining sketch/projection losses.
    """
    def workflow(self):
        f = Fixture()
        root = f.extrude
        root.FeatureType, root.parents, root.expressions = 'BREP', [], []
        datums = [f.mcs] + [Feature(tag, 'DATUM_CSYS') for tag in (40, 41, 42)]
        for datum in datums:
            datum.parents = [root]
        first = f.sketch
        first.parents = [root, datums[0]]
        project = Feature(50, 'CPROJ', parents=[root, datums[1]])
        second = Feature(60, 'SKETCH', parents=[root, first, datums[2]])
        third = Feature(61, 'SKETCH', parents=[first, datums[3], project])
        warning_only = Feature(70, 'SKETCH', parents=[datums[1]])
        for tag, name in ((500, 'p58'), (501, 'p59')):
            expression = Expression(tag, first)
            expression.Name, expression.RightHandSide = name, '1'
        for tag, name, value, units in ((502, 'Pattern_p225', '4', None),
                                       (503, 'Pattern_p228', '360', NS(Name='Degrees'))):
            expression = Expression(tag, third)
            expression.Name, expression.RightHandSide, expression.Units = name, value, units
        added = datums[1:] + [project, second, third, warning_only]
        for feature in added:
            feature.OwningPart = f.trial.part
        f.trial.part.Features.extend(added)
        t = f.workflow()
        arc_class = type('Arc', (t.nx.Curve,), {})
        t.refs = {tag: arc_class() for tag in (101, 102)}
        t.native.sketch_of_geometry.return_value = 700
        t.native.smart_parents = Mock(side_effect=AssertionError('Sketch arcs must not reach the free-object path'))
        t.bounding_box = Mock(return_value=[1, 2, 3, 5, 6, 7])
        t.transfer_mcs_and_stock, t.transfer_custom_points = Mock(), Mock()

        def observed_transform(*args):
            f.convert()
            for datum in datums:
                datum.parents = [f.brep]
            first.parents, first.expressions = [datums[0]], []
            project.parents = [datums[1]]
            second.parents = [datums[2]]
            third.parents, third.expressions = [datums[3], project], []
            warning_only.warnings = [PARENT_WARNING]
            second.warnings = [PARENT_WARNING, CONSTRAINT_WARNING, 'Sketch contains failed associative curves.']
            third.warnings = [PARENT_WARNING, CONSTRAINT_WARNING]

        t.native.transform_entities.side_effect = observed_transform
        return t, datums, (first, project, second, third), warning_only

    def run_failed(self):
        t, datums, damaged, warning_only = self.workflow()
        with self.assertRaisesRegex(RuntimeError, 'сохраняемые модельные элементы; элементов: 8') as caught:
            t.run_in_absolute_wcs()
        return t, datums, damaged, warning_only, str(caught.exception)

    def test_observed_damage_fails_before_mcs_despite_excluded_sketch_arcs(self):
        t, datums, damaged, _, error = self.run_failed()
        t.native.transform_entities.assert_called_once_with([1000], [0]*16)
        self.assertEqual(t.sketch_geometry, {101: 700, 102: 700})
        t.native.smart_parents.assert_not_called()
        for feature in datums + list(damaged):
            self.assertIn('элемент ' + str(feature.Tag), error)
        for query in (t.transfer_mcs_and_stock, t.setup.DeleteToolPath,
                      t.native.update_model, t.transfer_custom_points, t.transfer_boundary_planes):
            query.assert_not_called()
        self.assertEqual(set(t.history_baseline[damaged[0].Tag]['expressions']), {500, 501})
        self.assertEqual(set(t.history_baseline[damaged[3].Tag]['expressions']), {502, 503})
        self.assertNotIn('ПРОЙДЕНО', '\n'.join(t.lines))

    def test_v12502_three_bodies_and_23_parentless_points_still_reject_history_damage(self):
        # V1.25.02 reports these input categories/counts, not which individual
        # target caused the damage. Synthetic tags/history below exercise the
        # guard with that selection; they do not reproduce the NX kernel.
        t, datums, damaged, _ = self.workflow()
        for body_tag, feature_tag in ((1100,80),(1200,81)):
            body = Body(body_tag)
            feature = BodyFeature(feature_tag,'BREP',bodies=[body])
            feature.OwningPart = t.part
            body.features = [feature]
            t.bodies.append(body)
            t.part.Bodies.append(body)
            t.part.Features.append(feature)
        points = list(range(200,223))
        for tag in points:
            point = t.nx.Point()
            point.Tag = tag
            t.refs[tag] = point
        t.native.sketch_of_geometry.side_effect = lambda tag: 700 if tag in (101,102) else 0
        t.native.smart_parents = Mock(return_value=None)
        t.native.entity_parents.return_value = ()
        with self.assertRaisesRegex(RuntimeError, 'сохраняемые модельные элементы; элементов: 8') as caught:
            t.run_in_absolute_wcs()
        t.native.transform_entities.assert_called_once_with([1000,1100,1200]+points, [0]*16)
        self.assertEqual([c.args[0] for c in t.native.entity_parents.call_args_list],points)
        self.assertEqual(t.model_geometry,{tag:() for tag in points})
        self.assertEqual(t.sketch_geometry,{101:700,102:700})
        self.assertEqual(set(t.bounds),set(points)|{101,102})
        self.assertIn('Прямые цели UF_MODL_transform_entities: 26',t.lines)
        for feature in datums + list(damaged):
            self.assertIn('элемент ' + str(feature.Tag),str(caught.exception))
        for query in (t.transfer_mcs_and_stock,t.setup.DeleteToolPath,t.native.update_model,
                      t.transfer_custom_points,t.transfer_boundary_planes):
            query.assert_not_called()
        self.assertNotIn('ПРОЙДЕНО','\n'.join(t.lines))

    def test_datum_parent_changes_are_not_the_only_rejection_reason(self):
        t, datums, damaged, _, _ = self.run_failed()
        allowed_for_analysis = {feature.Tag for feature in datums}
        retained = set(t.history_baseline) - {10, 11, 12} - set(t.stock_features)
        remaining = [(tag, details) for tag, details in
            M.model_history_changes(t.history_baseline, t.history_previous)
            if tag in retained - allowed_for_analysis]
        self.assertEqual({tag for tag, _ in remaining}, {feature.Tag for feature in damaged})
        self.assertIn('Выражения: количество 2 → 0', '\n'.join(line for _, details in remaining for line in details))

    def test_warning_only_sketch_and_failed_associative_curves_are_reported(self):
        t, _, damaged, warning_only, _ = self.run_failed()
        structural = {tag for tag, _ in M.model_history_changes(t.history_baseline, t.history_previous)}
        self.assertNotIn(warning_only.Tag, structural)
        warnings = {tag: added for tag, added, _ in M.model_warning_changes(t.history_baseline, t.history_previous)}
        self.assertEqual(set(warnings), {warning_only.Tag, damaged[2].Tag, damaged[3].Tag})
        self.assertIn('Sketch contains failed associative curves.', warnings[damaged[2].Tag])
        with self.assertRaisesRegex(RuntimeError, 'Sketch contains failed associative curves'):
            t.verify_model_warnings('reported model transform')


class BoundarySelectionReportTests(unittest.TestCase):
    def trial(self):
        t = object.__new__(M.Trial)
        t.nodes = [NS(Tag=900, Name='R1_CHERN')]
        objects = {tag: type(kind, (), {})() for tag, kind in ((100, 'Body'), (101, 'Face'), (102, 'Line'))}
        data = M._BoundaryData()
        data.plane_type, data.boundary_type, data.material_side = 1, 2, 1
        t.native = NS(items=object(), boundaries=object(), item_entity=object(),
                      members=object(), member_entity=object(), read_boundary=lambda _: data,
                      pointers=lambda *args: [101, 102, 101], entity=lambda _, item, **kw: item)
        t.native.channel = lambda function, node, kind: ('read', node,
            ([100] if function is t.native.items else [200]) if kind == 0 else [])
        t.nx = NS(ScCollector=type('ScCollector', (), {}), TaggedObjectManager=NS(GetTaggedObject=objects.__getitem__),
                  CAM=NS(Operation=type('Operation', (), {})))
        t.refs, t.unavailable, t.lines = {}, set(), []
        t.reference = lambda tag: t.refs.setdefault(tag, objects[tag])
        return t

    def test_initial_report_names_actual_types_order_duplicates_and_raw_enum(self):
        t = self.trial()
        snapshot = t.geometry_snapshot(collect=True)
        report = '\n'.join(t.lines)
        for text in ('Исходная CAM-граница: R1_CHERN', 'boundary_type=2',
                     'Элемент 1: tag=101; класс NXOpen=Face',
                     'Элемент 2: tag=102; класс NXOpen=Line',
                     'Элемент 3: tag=101; класс NXOpen=Face'):
            self.assertIn(text, report)
        self.assertEqual(snapshot[(900, 0)][4:], ((100, 101, 102, 101), ((101, 102, 101),)))
        self.assertEqual(snapshot[(900, 0, 'boundary', 1)], (1, 2, 1, 0, 0, 0))

    def test_repeat_snapshot_does_not_duplicate_report_or_query_member_types(self):
        t = self.trial()
        snapshot = t.geometry_snapshot(collect=True)
        lines = t.lines[:]
        t.refs.clear()
        t.reference = Mock(side_effect=AssertionError('Do not re-read frozen selections'))
        self.assertEqual(snapshot, t.geometry_snapshot())
        self.assertEqual(t.lines, lines)
        t.reference.assert_not_called()


if __name__ == '__main__':
    unittest.main()
