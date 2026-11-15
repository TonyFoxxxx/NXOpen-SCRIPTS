"""Exact CAM snapshot guards and actionable in-memory differences, without NX."""
import copy
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock

from test_mirror_custom_points import M, Fixture, ZERO


def channel(tags=(101,102), members=(), state='read', owner=100):
    return (state,owner,'read',owner,tuple(tags),tuple(members))


class GeometryDifferenceTests(unittest.TestCase):
    def details(self, before, after):
        count, details = M.geometry_snapshot_differences(before,after,{100:'POCKET [CavityMilling]'})
        return count,'\n'.join(label+'\n'+'\n'.join(lines) for label,lines in details)

    def test_each_geometry_kind_identifies_node_field_and_ordered_change(self):
        for kind,name in enumerate(M.GEOMETRY_KINDS):
            with self.subTest(kind=kind):
                count,text = self.details({(100,kind):channel()},{(100,kind):channel((101,999))})
                self.assertEqual(count,1)
                self.assertIn('POCKET [CavityMilling] (tag=100) / '+name,text)
                self.assertIn('[2]: до=102; после=999',text)

    def test_reorder_and_duplicate_removal_are_not_normalized(self):
        for after,expected in ((channel((102,101)),'[1]: до=101; после=102'),
                               (channel((101,)),'[2]: до=102; после=<записи нет>')):
            count,text = self.details({(100,0):channel()},{(100,0):after})
            self.assertEqual(count,1);self.assertIn(expected,text)
        count,text = self.details({(100,0):channel((101,101))},{(100,0):channel((101,))})
        self.assertEqual(count,1)
        self.assertIn('[2]: до=101; после=<записи нет>',text)

    def test_same_selected_entities_do_not_hide_a_changed_provider(self):
        count,text = self.details({(100,2):channel(state='provider',owner=200)},
                                  {(100,2):channel(state='provider',owner=300)})
        self.assertEqual(count,1)
        self.assertIn('Источник элементов (tag): до=200; после=300',text)
        self.assertIn('Источник границ (tag): до=200; после=300',text)

    def test_unavailable_channel_and_readable_empty_channel_are_different(self):
        count,text = self.details({(100,1):channel((),state='unavailable')},
                                  {(100,1):channel(())})
        self.assertEqual(count,1)
        self.assertIn("Состояние элементов: до=<недоступно: unavailable>; после='read'",text)
        self.assertNotIn('CAM-ссылки:',text)

    def test_boundary_flags_are_named_and_material_side_is_not_ignored(self):
        key = (100,0,'boundary',3)
        for i,name in enumerate(M.BOUNDARY_SNAPSHOT_FIELDS):
            with self.subTest(field=name):
                after=[0]*6;after[i]=1
                count,text=self.details({key:(0,)*6},{key:tuple(after)})
                self.assertEqual(count,1)
                self.assertIn('/ граница 3 / параметры',text)
                self.assertIn(name+': до=0; после=1',text)

    def test_order_inside_specific_boundary_is_visible_beyond_long_unchanged_prefix(self):
        before=list(range(250));after=before[:];after[199]=900
        count,text=self.details({(100,0):channel((),(tuple(before),))},
                                {(100,0):channel((),(tuple(after),))})
        self.assertEqual(count,1)
        self.assertIn('Элементы границ по порядку[1][200]: до=199; после=900',text)

    def test_each_custom_point_depth_and_reference_is_reported_without_rounding(self):
        key=(100,'custom',1)
        before=((700,(0.0,0.0,None)),)
        for after,field in ((((999,(0.0,0.0,None)),),'Point tag'),
                            (((700,(1e-310,0.0,None)),),'upper_depth'),
                            (((700,(0.0,-1e-310,None)),),'lower_depth')):
            count,text=self.details({key:before},{key:after})
            self.assertEqual(count,1)
            self.assertIn('Cut Region Start',text)
            self.assertIn('Точка 1 / '+field,text)
            if field!='Point tag':self.assertIn('1e-310',text)
        count,text=self.details({(100,'custom',0):((700,(None,None,0.0)),)},
                                {(100,'custom',0):((700,(None,None,2.0)),)})
        self.assertIn('Predrill Engage',text)
        self.assertIn('Точка 1 / depth: до=0.0; после=2.0',text)

    def test_missing_custom_channel_unavailable_and_empty_have_distinct_reports(self):
        key=(100,'custom',1)
        scenarios=(({},{key:()},'Запись снимка добавлена'),
                   ({key:()},{},'Запись снимка удалена'),
                   ({key:'unavailable'},{key:()},'до=<недоступно: unavailable>; после=доступно; количество=0'))
        for before,after,expected in scenarios:
            with self.subTest(before=before,after=after):
                count,text=self.details(before,after)
                self.assertEqual(count,1);self.assertIn(expected,text)

    def test_added_and_removed_points_include_position_and_fields(self):
        key=(100,'custom',1);points=((700,(0.,0.,None)),)
        for before,after,expected in (((),points,'до=<записи нет>; после=700'),
                                      (points,(),'до=700; после=<записи нет>')):
            count,text=self.details({key:before},{key:after})
            self.assertEqual(count,1)
            self.assertIn('Точка 1 / Point tag: '+expected,text)
            self.assertIn('upper_depth',text)

    def test_diagnostic_limits_count_all_differences_and_show_actual_positions(self):
        before={(i,0):channel(tuple(range(100))) for i in range(90)}
        after={(i,0):channel(tuple(range(100,200))) for i in range(90)}
        count,details=M.geometry_snapshot_differences(before,after,{})
        self.assertEqual(count,90);self.assertEqual(len(details),80)
        self.assertIn('первые 16 изменённых позиций из 100','\n'.join(details[0][1]))
        count,text=self.details({},{(100,0):channel(tuple(range(100)))})
        self.assertEqual(count,1)
        self.assertIn('количество до=<записи нет>; после=100',text)
        self.assertIn('первые 16 изменённых позиций из 100',text)

    def test_mixed_keys_fallback_and_unchanged_snapshots_are_safe(self):
        before={(100,0):channel(),(100,'custom',1):(),(100,0,'boundary',1):(0,)*6}
        after=copy.deepcopy(before)
        self.assertEqual(self.details(before,after),(0,''))
        after['future']=(1,2,3)
        count,text=self.details(before,after)
        self.assertEqual(count,1);self.assertIn("Запись 'future'",text)
        self.assertIn('Запись снимка добавлена',text)


class GeometryGuardTests(unittest.TestCase):
    def trial(self, current):
        t=object.__new__(M.Trial)
        t.geometry_labels={100:'POCKET [CavityMilling]'}
        t.current_geometry_labels={100:'renamed',999:'LAST_DEBURR [TpMultiAxisDeburring]'}
        def read():
            t.stage='Чтение геометрии: LAST_DEBURR / Drive / границы'
            return current
        t.geometry_snapshot=Mock(side_effect=read)
        return t

    def test_reported_stale_last_read_stage_is_replaced_with_changed_node(self):
        expected={(100,0):channel()};actual={(100,0):channel((999,102))}
        frozen=copy.deepcopy(expected);t=self.trial(actual)
        with self.assertRaises(RuntimeError) as caught:
            t.check_geometry_snapshot(expected,'до переноса пользовательских точек','Изменились CAM-ссылки')
        self.assertIn('Сравнение CAM-геометрии:',t.stage)
        self.assertIn('POCKET [CavityMilling]',t.stage)
        self.assertNotIn('LAST_DEBURR',t.stage)
        self.assertIn('[1]: до=101; после=999',str(caught.exception))
        self.assertEqual(expected,frozen)
        t.geometry_snapshot.assert_called_once_with()

    def test_diagnostic_uses_frozen_labels_without_touching_deleted_wrappers(self):
        t=self.trial({})
        t.nodes=Mock(side_effect=AssertionError('must not inspect NX nodes'))
        t.nx=NS(TaggedObjectManager=NS(GetTaggedObject=Mock(side_effect=AssertionError('must not query NX'))))
        with self.assertRaisesRegex(RuntimeError,'POCKET.*Part'):
            t.check_geometry_snapshot({(100,0):channel()},'итоговая проверка','Изменились ссылки')
        t.nx.TaggedObjectManager.GetTaggedObject.assert_not_called()

    def test_read_error_keeps_api_code_traceback_and_reader_stage(self):
        t=self.trial({});error=M.NativeApiError(1234,'UF_CAMGEOM_ask_items','failed')
        def read():
            t.stage='Чтение геометрии: POCKET / Part / элементы'
            raise error
        t.geometry_snapshot.side_effect=read
        with self.assertRaises(M.NativeApiError) as caught:
            t.check_geometry_snapshot({},'итоговая проверка','Изменились ссылки')
        self.assertIs(caught.exception,error)
        self.assertEqual(t.stage,'Чтение геометрии: POCKET / Part / элементы')

    def test_equal_snapshots_do_not_replace_baseline_or_create_errors(self):
        before={(100,0):channel()};after=copy.deepcopy(before);t=self.trial(after)
        self.assertIsNone(t.check_geometry_snapshot(before,'test','failure'))
        self.assertIsNot(before,after)
        self.assertEqual(before,after)

    def test_all_changed_keys_still_fail_when_report_is_limited(self):
        before={(i,0):channel() for i in range(90)};t=self.trial({})
        with self.assertRaisesRegex(RuntimeError,'Показаны первые 80 изменённых записей из 90'):
            t.check_geometry_snapshot(before,'test','failure')

    def test_snapshot_freezes_initial_labels_and_reflects_new_names_separately(self):
        f=Fixture(points=());before=f.capture()
        f.node.Name='RENAMED'
        self.assertEqual(f.trial.geometry_snapshot(),before)
        self.assertEqual(f.trial.geometry_labels[100],'PERIMETER_FINISH [PlanarMilling]')
        self.assertEqual(f.trial.current_geometry_labels[100],'RENAMED [PlanarMilling]')

    def test_pre_transfer_mismatch_reports_depth_and_never_writes_points(self):
        f=Fixture();before=f.capture()
        f.records[(100,1)]=((0,(60.,-25.,16.),(1e-310,0.,0.)),)
        with self.assertRaisesRegex(RuntimeError,'Точка 1 / upper_depth: до=0.0; после=1e-310'):
            f.trial.transfer_custom_points(before)
        self.assertIn('PERIMETER_FINISH [PlanarMilling]',f.trial.stage)
        f.trial.native.replace_custom_points.assert_not_called()

    def test_post_transfer_mismatch_reports_the_changed_field_and_stops(self):
        f=Fixture();before=f.capture()
        def replace(tag,kind,points,operation_class):
            f.records[(tag,kind)]=tuple((pt,coords,(0.,2.,0.)) for pt,coords,depth in points)
        f.trial.native.replace_custom_points.side_effect=replace
        with self.assertRaisesRegex(RuntimeError,'Точка 1 / lower_depth: до=0.0; после=2.0'):
            f.trial.transfer_custom_points(before)
        self.assertIn('после переноса пользовательских точек',f.trial.stage)
        f.trial.native.replace_custom_points.assert_called_once()

    def test_boundary_plane_transfer_keeps_guard_and_reports_boundary_parameter(self):
        key=(100,0,'boundary',1)
        before={key:(0,0,0,0,0,0)};t=self.trial({key:(0,0,1,0,0,0)})
        t.boundary_planes={(100,0,1):((0,0,0),(1,0,0,0,1,0,0,0,1))}
        t.native=NS(set_boundary_plane=Mock())
        with self.assertRaisesRegex(RuntimeError,'material_side: до=0; после=1'):
            t.transfer_boundary_planes(before)
        self.assertIn('до переноса плоскостей границ',t.stage)
        t.native.set_boundary_plane.assert_not_called()

    def test_full_workflow_final_guard_names_changed_channel_after_cam_read(self):
        import test_mirror_body_history as history
        f=history.Fixture();t=f.workflow()
        t.geometry_labels={100:'POCKET [CavityMilling]'}
        before={(100,0):channel()};after={(100,0):channel((101,999))}
        t.geometry_snapshot.side_effect=[before,after]
        with self.assertRaisesRegex(RuntimeError,'CAM-ссылки.*\n|Изменились ссылки') as caught:
            t.run_in_absolute_wcs()
        self.assertIn('итоговая проверка после чтения CAM',t.stage)
        self.assertIn('POCKET [CavityMilling]',t.stage)
        self.assertIn('[2]: до=102; после=999',str(caught.exception))
        self.assertEqual(t.check_cam_builders.call_count,2)
        t.setup.DeleteToolPath.assert_called_once()


if __name__=='__main__':
    unittest.main()
