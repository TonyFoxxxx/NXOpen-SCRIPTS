"""Nested-source diagnostics and a separate map view; not NX compatibility."""
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock

from test_mirror_map_inputs import M, CollectorWorld, SelectionIntentRule
from test_mirror_collector_modes import StockCollectorWorld
from test_mirror_replacement_probe import Edge, Selection


def point(x=1.,y=2.,z=3.):return NS(X=x,Y=y,Z=z)


def section(h,tag,source,outputs=()):
    obj=h.t.nx.Section();obj.Tag=tag;h.w.register(obj)
    element=h.t.nx.SectionElementData(GetSectionElementData1=Mock(return_value=(source,None,point(),None,point(4.,5.,6.))),Dispose=Mock())
    data=h.t.nx.SectionData(GetRules=Mock(return_value=[SelectionIntentRule(h)]),GetStartConnector=Mock(return_value=None),
            GetEndConnector=Mock(return_value=None),GetSectionElementsData=Mock(return_value=[element]),Dispose=Mock())
    obj.GetSectionData=Mock(return_value=[data]);obj.GetOutputCurves=Mock(return_value=list(outputs))
    obj.Destroy=Mock();obj.MapSectionElements=Mock();obj.RemoveRules=Mock()
    return obj,data,element


class ProvenanceTests(unittest.TestCase):
    def world(self):
        h=CollectorWorld();unknown=h.w.register(Edge(81001,h.w.body))
        obj,data,element=section(h,82001,unknown)
        h.p.initial_refs={69574:((obj.Tag,b'projected curves'),)}
        reader=M.ReferenceProvenanceProbe(h.p,lambda:None,{unknown.Tag})
        return h,unknown,obj,data,element,reader

    def test_section_source_and_consumer_path_are_found_without_mapping_or_writes(self):
        h,unknown,obj,data,element,r=self.world();r.run()
        report='\n'.join(h.t.lines)
        self.assertIn('Путь к опоре 81001',report);self.assertIn('69574',report)
        self.assertIn('источник → 81001',report);self.assertEqual(h.p.map,{})
        data.Dispose.assert_called_once();element.Dispose.assert_called_once()
        obj.Destroy.assert_not_called();obj.MapSectionElements.assert_not_called();obj.RemoveRules.assert_not_called()
        self.assertEqual(r.checks.errors,[])

    def test_internal_outputs_are_distinguished_from_parent_curves(self):
        h,unknown,obj,data,element,r=self.world()
        element.GetSectionElementData1.return_value=(h.w.body.edges[0],None,point(),None,point())
        obj.GetOutputCurves.return_value=[unknown];r.run()
        report='\n'.join(h.t.lines)
        path=next(line for line in h.t.lines if line.startswith('Путь к опоре 81001'))
        self.assertIn('внутренний результат Section',path)
        self.assertNotIn('источник → 81001',report);self.assertEqual(h.p.map,{})

    def test_connectors_are_separate_edges_in_the_path(self):
        h,unknown,obj,data,element,r=self.world()
        element.GetSectionElementData1.return_value=(h.w.body.edges[0],unknown,point(),None,point())
        r.run();self.assertIn('начальный соединитель → 81001','\n'.join(h.t.lines))

    def test_shared_section_is_read_once_with_both_feature_contexts(self):
        h,unknown,obj,data,element,r=self.world()
        h.p.initial_refs[10]=((obj.Tag,b'old model section'),)
        r.run();obj.GetSectionData.assert_called_once()
        paths=[line for line in h.t.lines if line.startswith('Путь к опоре 81001')]
        self.assertEqual(len(paths),2)
        self.assertTrue(any('заменяемая история' in line for line in paths))
        self.assertTrue(any('сохраняемый элемент' in line for line in paths))

    def test_collector_grouped_objects_can_reveal_an_input_missing_from_getobjects(self):
        h,unknown,obj,data,element,r=self.world()
        h.p.initial_refs={69595:((h.collector.Tag,b'filtered input'),)}
        h.filters[10]=True
        h.collector.AskRulesAndCollectedEntities.side_effect=lambda:([unknown],h.rules,[1])
        r.run();report='\n'.join(h.t.lines)
        self.assertIn('Правило 1 / объект 1 → 81001',report)
        self.assertIn('SleepyEntity=True',report);self.assertIn('Фильтры не снимаются',report)
        h.collector.CopyCollector.assert_not_called();h.collector.RemoveEvaluationFilter.assert_not_called()
        self.assertEqual(h.p.map,{})

    def test_collector_cycles_are_bounded_and_each_container_is_read_once(self):
        h,unknown,obj,data,element,r=self.world()
        h.p.initial_refs={69595:((h.collector.Tag,b'cycle fixture'),)}
        h.collector.GetObjects.side_effect=lambda:[h.collector,unknown]
        r.run();h.collector.GetObjects.assert_called_once()
        self.assertEqual(r.seen,{h.collector.Tag});self.assertIn('Путь к опоре 81001','\n'.join(h.t.lines))

    def test_invalid_group_counts_are_not_used_as_complete_rule_data(self):
        h,unknown,obj,data,element,r=self.world()
        h.p.initial_refs={69595:((h.collector.Tag,b'bad group'),)}
        h.collector.AskRulesAndCollectedEntities.side_effect=lambda:([unknown],h.rules,[2])
        r.run();report='\n'.join(h.t.lines)
        self.assertIn('Не прочитано полное разбиение',report)
        self.assertNotIn('Правило 1 / объект 1 → 81001',report)
        self.assertIn('не доказательство отсутствия',report)

    def test_zero_count_is_an_explicit_diagnostic_value_not_an_inferred_empty_read(self):
        h,unknown,obj,data,element,r=self.world()
        h.p.initial_refs={69595:((h.collector.Tag,b'empty result'),)}
        h.collector.AskRulesAndCollectedEntities.side_effect=lambda:([],h.rules,[0])
        r.run();self.assertIn('counts=(0,)','\n'.join(h.t.lines));self.assertEqual(r.checks.errors,[])

    def test_unreadable_output_curves_do_not_hide_section_source_data(self):
        h,unknown,obj,data,element,r=self.world()
        obj.GetOutputCurves.side_effect=M.NativeApiError(7901,'outputs','unreadable')
        r.run();report='\n'.join(h.t.lines)
        self.assertIn('7901',report);self.assertIn('Путь к опоре 81001',report)
        self.assertEqual(len(r.checks.errors),1);data.Dispose.assert_called_once();element.Dispose.assert_called_once()

    def test_one_bad_element_does_not_skip_the_next_and_transients_are_disposed(self):
        h,unknown,obj,data,element,r=self.world()
        bad=h.t.nx.SectionElementData(GetSectionElementData1=Mock(side_effect=M.NativeApiError(7902,'element','unreadable')),Dispose=Mock())
        data.GetSectionElementsData.return_value=[bad,element]
        r.run();self.assertIn('7902','\n'.join(h.t.lines));self.assertIn('Путь к опоре 81001','\n'.join(h.t.lines))
        for transient in (bad,element,data):transient.Dispose.assert_called_once()

    def test_bad_element_shape_and_nonfinite_points_are_reported(self):
        for mode in ('shape','null_source','nonfinite'):
            h,unknown,obj,data,element,r=self.world()
            values={'shape':(None,), 'null_source':(None,None,point(),None,point()),
                    'nonfinite':(unknown,None,point(float('nan')),None,point())}
            element.GetSectionElementData1.return_value=values[mode]
            r.run();self.assertTrue(r.checks.errors);data.Dispose.assert_called_once();element.Dispose.assert_called_once()
            if mode=='nonfinite':self.assertIn('Не прочитаны конечные точки','\n'.join(h.t.lines))

    def test_shared_transient_wrappers_are_released_once_after_all_reads(self):
        h,unknown,obj,data,element,r=self.world();events=[]
        obj.GetSectionData.return_value=[data,data]
        def read():events.append('read');return (unknown,None,point(),None,point())
        element.GetSectionElementData1.side_effect=read
        element.Dispose.side_effect=lambda:events.append('element dispose')
        data.Dispose.side_effect=lambda:events.append('data dispose')
        r.run();self.assertEqual(events,['read','read','element dispose','data dispose'])

    def test_missing_dispose_method_does_not_hide_other_cleanup_results(self):
        h,unknown,obj,data,element,r=self.world();del element.Dispose
        r.run();self.assertIn('AttributeError','\n'.join(h.t.lines));data.Dispose.assert_called_once()

    def test_model_object_returned_as_section_data_is_never_disposed(self):
        h,unknown,obj,data,element,r=self.world();h.w.body.Dispose=Mock()
        obj.GetSectionData.return_value=[h.w.body,data]
        r.run();self.assertIn('Не подтверждён класс временных данных SectionData','\n'.join(h.t.lines))
        h.w.body.Dispose.assert_not_called();data.Dispose.assert_called_once();element.Dispose.assert_called_once()

    def test_model_object_returned_as_element_data_is_never_disposed(self):
        h,unknown,obj,data,element,r=self.world();unknown.Dispose=Mock()
        data.GetSectionElementsData.return_value=[unknown,element]
        r.run();self.assertIn('Не подтверждён класс временных данных SectionElementData','\n'.join(h.t.lines))
        unknown.Dispose.assert_not_called();data.Dispose.assert_called_once();element.Dispose.assert_called_once()

    def test_foreign_source_is_not_added_as_a_local_link(self):
        h,unknown,obj,data,element,r=self.world();unknown.OwningPart=NS(Tag=9999)
        r.run();report='\n'.join(h.t.lines)
        self.assertIn('Не подтверждена опора текущей копии',report)
        self.assertFalse(any(child==unknown.Tag for _,child in r.links.get(obj.Tag,())))

    def test_dispose_failure_keeps_api_code_and_other_transients_are_released(self):
        h,unknown,obj,data,element,r=self.world();element.Dispose.side_effect=M.NativeApiError(7903,'Dispose','failed')
        r.run();self.assertIn('7903','\n'.join(h.t.lines));data.Dispose.assert_called_once()

    def test_lost_copy_stops_reads_and_transient_cleanup_without_foreign_actions(self):
        h,unknown,obj,data,element,r=self.world();active=[True]
        def guard():
            if not active[0]:raise RuntimeError('lost copy')
        r.guard=r.checks.guard=guard
        def lose():active[0]=False;return (unknown,None,point(),None,point())
        element.GetSectionElementData1.side_effect=lose
        with self.assertRaisesRegex(RuntimeError,'lost copy'):r.run()
        element.Dispose.assert_not_called();data.Dispose.assert_not_called()
        obj.Destroy.assert_not_called()


class ViewWorld(StockCollectorWorld):
    def __init__(self):
        super().__init__()
        self.unknown=self.w.register(Edge(81001,self.w.body))
        self.view_rows=[self.row(self.w.body,self.t.nx.Features.MatchedReferenceBuilder.Types.Object),
                        self.row(self.unknown,self.t.nx.Features.MatchedReferenceBuilder.Types.Object)]
        self.on_view_update=self.on_view_read=self.on_view_destroy=lambda:None
        self.primary=self.view=None
        original=self.t.part.Features.CreateReplaceFeatureBuilder.side_effect
        def create():
            if self.primary is None:
                builder=self.primary=original();update=builder.UpdateMap.side_effect
                def initial():
                    update()
                    self.rows[:]=[r for r in self.rows if r.ParentEntity.Tag!=self.stock_collector.Tag]
                    self.rows.append(self.row(self.unknown,self.t.nx.Features.MatchedReferenceBuilder.Types.Object))
                builder.UpdateMap.side_effect=initial
                return builder
            def read():self.on_view_read();return self.view_rows[:]
            self.view=NS(SelectFeature=Selection(),ReplacementFeature=Selection(),
                UpdateMap=Mock(side_effect=lambda:self.on_view_update()),
                FeatureReferences=NS(GetMatchedReferences=Mock(side_effect=read),Validate=Mock()),
                SetMatchingForParent=Mock(),SetMatchingForIndex=Mock(),AutomatchMap=Mock(),
                Validate=Mock(),Commit=Mock(),Destroy=Mock(side_effect=lambda:self.on_view_destroy()))
            self.map_builders.append(self.view)
            return self.view
        self.t.part.Features.CreateReplaceFeatureBuilder.side_effect=create


class IndependentViewTests(unittest.TestCase):
    def reject(self,h):
        before=h.snapshot()
        with self.assertRaises(M.ProbeCheckFailures) as caught:h.p.run()
        self.assertEqual(h.snapshot(),before);self.assertEqual(h.marks,{})
        self.assertIn('не включил исходные коллекции',str(caught.exception))
        self.assertIn('Нет проверенного соответствия',str(caught.exception))
        h.primary.SetMatchingForParent.assert_not_called();h.primary.Commit.assert_not_called()
        if h.view is not None:
            for method in ('Commit','Validate','SetMatchingForParent','SetMatchingForIndex','AutomatchMap'):
                getattr(h.view,method).assert_not_called()
            h.view.FeatureReferences.Validate.assert_not_called()
        h.t.transfer_mcs_and_stock.assert_not_called();h.t.setup.DeleteToolPath.assert_not_called()
        return '\n'.join(h.t.lines)

    def test_unique_view_preserves_original_failure_and_requires_rollback(self):
        h=ViewWorld();report=self.reject(h)
        self.assertIsNot(h.primary,h.view);self.assertTrue(h.view.DisplayUniqueInputsToMap)
        self.assertFalse(h.primary.DisplayUniqueInputsToMap);h.view.UpdateMap.assert_called_once();h.view.Destroy.assert_called_once()
        self.assertIn('Type=73 / Object',report);self.assertIn('объекты найдены=(1000,)',report)
        self.assertIn('не доказывает перенос конкретного потребителя',report)
        self.assertIn('Исторические кривые-кандидаты, отсутствующие',report)
        self.assertNotIn(81001,h.p.map)

    def test_view_starts_after_primary_destroy_and_verified_snapshot(self):
        h=ViewWorld();seen=[]
        h.on_view_update=lambda:seen.append((h.primary.Destroy.call_count,
            sum('Независимая диагностика после ошибки карты: пройдено' in line for line in h.t.lines)))
        self.reject(h);self.assertEqual(seen,[(1,2)])

    def test_model_damage_blocks_new_editor(self):
        h=ViewWorld();h.after_destroy_map=lambda:setattr(h.box.Clearance,'Value',0.)
        report=self.reject(h)
        self.assertIsNone(h.view);self.assertEqual(h.t.part.Features.CreateReplaceFeatureBuilder.call_count,1)
        self.assertIn('сохранность не подтверждена',report)

    def test_view_configuration_drift_blocks_row_read(self):
        h=ViewWorld();h.on_view_update=lambda:setattr(h.view,'DoAutomaticGeomMatch',True)
        report=self.reject(h);h.view.FeatureReferences.GetMatchedReferences.assert_not_called()
        self.assertIn('Не подтверждён режим диагностического редактора',report);h.view.Destroy.assert_called_once()

    def test_changed_feature_selection_is_not_accepted(self):
        h=ViewWorld();h.on_view_update=lambda:h.view.SelectFeature.values.clear()
        report=self.reject(h);self.assertIn('Изменился выбор элементов',report)
        h.view.FeatureReferences.GetMatchedReferences.assert_not_called()

    def test_bad_row_does_not_hide_later_unique_row(self):
        h=ViewWorld();h.view_rows.insert(0,NS())
        report=self.reject(h);self.assertIn('AttributeError',report)
        self.assertIn('Уникальная строка 3: tag=81001',report);self.assertIn('Сравнение представлений частичное',report)

    def test_update_failure_keeps_primary_failure_and_destroys_view(self):
        h=ViewWorld()
        def fail():raise M.NativeApiError(7904,'UpdateMap','view failed')
        h.on_view_update=fail;report=self.reject(h)
        self.assertIn('7904',report);h.view.Destroy.assert_called_once();self.assertIn('Локальный Undo новых кривых выполнен',report)

    def test_view_destroy_failure_blocks_audit_but_keeps_owned_undo(self):
        h=ViewWorld()
        def fail():raise M.NativeApiError(7905,'Destroy','view close failed')
        h.on_view_destroy=fail;report=self.reject(h)
        self.assertIn('7905',report);self.assertIn('закрытие редактора не подтверждено; далее откат',report)
        self.assertIn('Локальный Undo новых кривых выполнен',report)

    def test_lost_copy_during_view_prevents_destroy_and_foreign_undo(self):
        h=ViewWorld();h.on_view_update=lambda:setattr(h.t.session.Parts,'Work',NS(Tag=9999))
        with self.assertRaises(RuntimeError):h.p.run()
        h.view.Destroy.assert_not_called();h.view.Commit.assert_not_called()
        self.assertEqual(set(h.marks),{103});self.assertEqual(h.t.session.Parts.Work.Tag,9999)

    def test_reused_closed_builder_is_rejected_without_second_destroy(self):
        h=ViewWorld();factory=h.t.part.Features.CreateReplaceFeatureBuilder.side_effect
        h.t.part.Features.CreateReplaceFeatureBuilder.side_effect=lambda:factory() if h.primary is None else h.primary
        report=self.reject(h);self.assertIn('Не получен отдельный редактор',report)
        h.primary.Destroy.assert_called_once();h.primary.UpdateMap.assert_called_once()

    def test_post_view_state_damage_is_reported_before_rollback(self):
        h=ViewWorld();h.on_view_destroy=lambda:setattr(h.box.Clearance,'Value',0.)
        report=self.reject(h);self.assertIn('Изменились параметры защищённой заготовки',report)
        self.assertIn('Локальный Undo новых кривых выполнен',report)


if __name__=='__main__':unittest.main()
