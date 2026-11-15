"""Model-dependent curve/point selection; simulated NX contracts, no kernel claim."""
import unittest
from unittest.mock import Mock

from test_mirror_body_history import M, Fixture


class ModelParentSelectionTests(unittest.TestCase):
    def workflow(self, kind='Curve', parents=(1000,)):
        f = Fixture()
        t = f.workflow()
        obj = getattr(t.nx, kind)()
        obj.Tag = 101
        t.refs = {101: obj}
        t.native.smart_parents = Mock(return_value=None)
        t.native.entity_parents.return_value = parents
        t.bounding_box = Mock(return_value=[1,2,3,5,6,7])
        t.transfer_mcs_and_stock = Mock(return_value={})
        t.native.update_model.side_effect = lambda: setattr(t.bounding_box, 'return_value', [-5,2,3,-1,6,7])
        return f,t

    def test_non_sketch_non_smart_geometry_with_model_parents_is_not_raw_input(self):
        for kind in ('Curve','Point'):
            with self.subTest(kind=kind):
                _,t = self.workflow(kind)
                self.assertEqual(set(t.select_transform_targets()), {1000})
                t.native.sketch_of_geometry.assert_called_once_with(101)
                t.native.smart_parents.assert_called_once_with(101)
                t.native.entity_parents.assert_called_once_with(101)
                self.assertEqual(t.model_geometry, {101:(1000,)})
                self.assertIn(101,t.bounds)
                self.assertIn('родители=(1000,)', '\n'.join(t.lines))

    def test_custom_curve_subclass_uses_dependencies_without_feature_name_allowlist(self):
        _,t = self.workflow()
        t.refs[101] = type('UnknownCurve2606', (t.nx.Curve,), {'Tag':101})()
        self.assertEqual(set(t.select_transform_targets()), {1000})
        self.assertIn('класс=UnknownCurve2606', '\n'.join(t.lines))

    def test_explicit_empty_model_parent_result_keeps_independent_geometry(self):
        for kind in ('Curve','Point'):
            with self.subTest(kind=kind):
                _,t = self.workflow(kind, ())
                t.run_in_absolute_wcs()
                t.native.transform_entities.assert_called_once_with([1000,101], [0]*16)
                self.assertEqual(t.model_geometry, {101:()})
                self.assertEqual(t.native.entity_parents.call_count, 2)

    def test_sketch_and_smart_geometry_do_not_enter_free_geometry_fallback(self):
        for sketch,smart in ((700,None),(0,()),(0,(1000,))):
            with self.subTest(sketch=sketch,smart=smart):
                _,t = self.workflow()
                t.native.sketch_of_geometry.return_value = sketch
                t.native.smart_parents.return_value = smart
                self.assertEqual(set(t.select_transform_targets()), {1000})
                self.assertIn(101,t.bounds)
                t.native.entity_parents.assert_not_called()

    def test_body_edges_are_never_independently_transformed_or_parent_queried(self):
        _,t = self.workflow()
        t.nx.Edge = type('Edge', (t.nx.Curve,), {'Tag':101})
        t.refs[101] = t.nx.Edge()
        self.assertEqual(set(t.select_transform_targets()), {1000})
        t.native.sketch_of_geometry.assert_not_called()
        t.native.smart_parents.assert_not_called()
        t.native.entity_parents.assert_not_called()

    def test_model_parent_read_error_stops_before_any_geometry_mcs_or_path_write(self):
        _,t = self.workflow()
        error = M.NativeApiError(9876,'UF_MODL_ask_entity_parents','read failed')
        t.native.entity_parents.side_effect = error
        with self.assertRaises(M.NativeApiError) as caught:
            t.run_in_absolute_wcs()
        self.assertIs(caught.exception,error)
        self.assertIn('Curve 101 / UF_MODL_ask_entity_parents', t.stage)
        t.native.transform_entities.assert_not_called()
        t.transfer_mcs_and_stock.assert_not_called()
        t.setup.DeleteToolPath.assert_not_called()

    def test_missing_model_parent_reader_does_not_enable_raw_fallback(self):
        _,t = self.workflow()
        del t.native.entity_parents
        with self.assertRaises(AttributeError):
            t.run_in_absolute_wcs()
        t.native.transform_entities.assert_not_called()

    def test_dependent_curve_requires_reflected_position_and_same_parents(self):
        _,t = self.workflow()
        t.run_in_absolute_wcs()
        t.native.transform_entities.assert_called_once_with([1000], [0]*16)
        self.assertEqual(t.native.entity_parents.call_count,2)
        self.assertIn('ПРОЙДЕНО', '\n'.join(t.lines))

    def test_omitting_dependent_geometry_cannot_make_unchanged_position_pass(self):
        _,t = self.workflow()
        t.native.update_model.side_effect = None
        with self.assertRaisesRegex(RuntimeError, 'Не подтверждено отражение объекта 101'):
            t.run_in_absolute_wcs()
        self.assertNotIn('ПРОЙДЕНО', '\n'.join(t.lines))

    def test_smart_curve_omission_also_requires_its_reflected_position(self):
        _,t = self.workflow()
        t.native.smart_parents.return_value = (1000,)
        t.native.update_model.side_effect = None
        with self.assertRaisesRegex(RuntimeError, 'Не подтверждено отражение объекта 101'):
            t.run_in_absolute_wcs()
        self.assertIn(101,t.bounds)
        t.native.entity_parents.assert_not_called()

    def test_reflected_geometry_cannot_drop_or_replace_model_parents(self):
        for actual in ((),(1001,)):
            with self.subTest(actual=actual):
                _,t = self.workflow()
                t.native.entity_parents.side_effect = [(1000,),actual]
                with self.assertRaisesRegex(RuntimeError, 'Изменились модельные родители геометрии 101'):
                    t.run_in_absolute_wcs()
                self.assertNotIn('ПРОЙДЕНО', '\n'.join(t.lines))

    def test_independent_geometry_cannot_acquire_model_parents_silently(self):
        _,t = self.workflow(parents=())
        t.native.entity_parents.side_effect = [(),(1000,)]
        with self.assertRaisesRegex(RuntimeError, 'Изменились модельные родители'):
            t.run_in_absolute_wcs()

    def test_parent_order_is_not_an_axis_selection_and_multiplicity_is_retained(self):
        _,t = self.workflow(parents=(1000,1002,1000))
        t.native.entity_parents.side_effect = [(1000,1002,1000),(1002,1000,1000)]
        t.run_in_absolute_wcs()
        _,t = self.workflow(parents=(1000,1002,1000))
        t.native.entity_parents.side_effect = [(1000,1002,1000),(1002,1000)]
        with self.assertRaisesRegex(RuntimeError, 'Изменились модельные родители'):
            t.run_in_absolute_wcs()

    def test_final_parent_query_error_is_not_a_success(self):
        _,t = self.workflow()
        error = M.NativeApiError(9877,'UF_MODL_ask_entity_parents','final read failed')
        t.native.entity_parents.side_effect = [(1000,),error]
        with self.assertRaises(M.NativeApiError) as caught:
            t.run_in_absolute_wcs()
        self.assertIs(caught.exception,error)
        self.assertIn('Проверка модельных родителей', t.stage)
        self.assertNotIn('ПРОЙДЕНО', '\n'.join(t.lines))

    def test_exact_raw_input_list_is_recorded_even_when_transform_fails(self):
        _,t = self.workflow()
        t.refs[102] = t.nx.Point()
        t.native.entity_parents.side_effect = lambda tag: (1000,) if tag == 101 else ()
        t.native.transform_entities.side_effect = RuntimeError('transform failed')
        with self.assertRaisesRegex(RuntimeError, 'transform failed'):
            t.run_in_absolute_wcs()
        t.native.transform_entities.assert_called_once_with([1000,102], [0]*16)
        report = t.lines[t.lines.index('Прямые цели UF_MODL_transform_entities: 2'):]
        self.assertEqual(report, ['Прямые цели UF_MODL_transform_entities: 2',
                                  '  tag=1000; класс=Body', '  tag=102; класс=Point'])
        self.assertNotIn(2000,t.native.transform_entities.call_args.args[0])
        self.assertNotIn(3000,t.native.transform_entities.call_args.args[0])


if __name__ == '__main__':
    unittest.main()
