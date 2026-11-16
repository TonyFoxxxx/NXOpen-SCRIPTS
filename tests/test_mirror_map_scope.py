"""Alternative selection diagnostics; no assumptions about Siemens map coverage."""
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock, patch

from test_mirror_map_inputs import M, CollectorWorld
from test_mirror_replacement_probe import Edge


def planner():
    h = CollectorWorld()
    try: h.p.snapshot()
    except M.UnresolvedProbeReferences: pass
    probe = object.__new__(M.HistoricalReplacementMapProbe)
    probe.p, probe.t = h.p, h.t
    probe.h = NS(require_copy=Mock())
    probe.c = NS(created={64791:900001,73591:900002})
    h.p.map_inputs = NS(missing_collectors=set())
    return h, probe


class SelectionPlanTests(unittest.TestCase):
    def test_missing_historical_inputs_select_nearest_old_parents_only(self):
        h, probe = planner(); old = set(h.p.old)
        self.assertEqual(probe.diagnostic_selection(set()), (11,))
        self.assertEqual(h.p.old, old)
        self.assertNotIn(10, probe.diagnostic_selection(set()))
        report = '\n'.join(h.t.lines)
        self.assertIn('69574 → 69573 → 11', report)
        self.assertIn('64791', report); self.assertIn('73591', report)
        h.t.part.Features.CreateReplaceFeatureBuilder.assert_not_called()

    def test_existing_map_inputs_do_not_trigger_another_editor(self):
        h, probe = planner()
        self.assertIsNone(probe.diagnostic_selection({64791,73591}))
        self.assertNotIn('Путь по родителям', '\n'.join(h.t.lines))

    def test_missing_collector_requires_its_retained_consumer_path(self):
        h, probe = planner(); h.p.map_inputs.missing_collectors={170651}
        h.t.history_baseline[69595]['parents']={11:'Blend'}
        self.assertEqual(probe.diagnostic_selection({64791,73591}), (11,))
        self.assertIn('69595 → 11', '\n'.join(h.t.lines))

    def test_equal_full_selection_is_not_repeated(self):
        h, probe = planner()
        h.t.history_baseline[69573]['parents']={10:'Extrude',11:'Blend'}
        self.assertIsNone(probe.diagnostic_selection(set()))
        self.assertIn('совпал с полной историей', '\n'.join(h.t.lines))

    def test_retained_parent_chain_and_diamond_do_not_add_retained_features(self):
        h, probe = planner()
        h.t.history_baseline[69573]['parents']={22:'Sketch',23:'Projection'}
        self.assertEqual(probe.diagnostic_selection(set()), (11,))

    def test_cycle_in_retained_parents_rejects_plan(self):
        h, probe = planner(); h.t.history_baseline[69573]['parents']={69574:'Sketch'}
        with self.assertRaisesRegex(RuntimeError,'Цикл'): probe.diagnostic_selection(set())

    def test_foreign_parent_rejects_plan(self):
        h, probe = planner(); h.t.history_baseline[69573]['parents']={999999:'Foreign'}
        with self.assertRaisesRegex(RuntimeError,'Неполный'): probe.diagnostic_selection(set())

    def test_absent_unreadable_and_wrong_shape_parent_data_are_not_empty(self):
        for value in (None, '11', (11,11)):
            with self.subTest(value=value):
                h, probe = planner(); h.t.history_baseline[69573]['parents']=value
                with self.assertRaises(RuntimeError): probe.diagnostic_selection(set())
        h, probe = planner(); del h.t.history_baseline[69573]['parents']
        with self.assertRaises(KeyError): probe.diagnostic_selection(set())

    def test_no_old_ancestor_blocks_partial_selection(self):
        h, probe = planner(); h.p.map_inputs.missing_collectors={170651}
        with self.assertRaisesRegex(RuntimeError,'не найден заменяемый предок'): probe.diagnostic_selection(set())
        h.t.part.Features.CreateReplaceFeatureBuilder.assert_not_called()

    def test_only_deleted_consumers_cannot_justify_missing_required_input(self):
        h, probe = planner(); probe.c.created[81001]=900003
        h.p.initial_refs[11]+=((81001,b'blend input'),)
        with self.assertRaisesRegex(RuntimeError,'нет прямого сохраняемого потребителя'): probe.diagnostic_selection(set())

    def test_lost_copy_and_resource_latch_block_planning(self):
        h, probe = planner(); probe.h.require_copy.side_effect=RuntimeError('lost copy')
        with self.assertRaisesRegex(RuntimeError,'lost copy'): probe.diagnostic_selection(set())
        probe.t.resource_failure=M.NativeApiError(821049,'api','limit')
        with self.assertRaises(M.NativeApiError): probe.diagnostic_selection(set())


class ScopeWorld(CollectorWorld):
    def __init__(self):
        super().__init__()
        self.unknown=self.w.register(Edge(81001,self.w.body))
        self.before_inputs=lambda:self.rows.append(self.row(self.unknown,self.t.nx.Features.MatchedReferenceBuilder.Types.Object))
        self.on_reduced_update=lambda:None
        self.on_reduced_destroy=lambda:None
        self.after_unique=self.unique_rows
        factory=self.t.part.Features.CreateReplaceFeatureBuilder.side_effect
        def create():
            builder=factory()
            if len(self.map_builders)==3:
                destroy=builder.Destroy.side_effect
                def close():destroy();self.on_reduced_destroy()
                builder.Destroy.side_effect=close
            return builder
        self.t.part.Features.CreateReplaceFeatureBuilder.side_effect=create

    def unique_rows(self):
        if len(self.map_builders)==2:
            # Real V1.30.09 symptom: historical inputs absent from full view.
            self.rows[:]=[r for r in self.rows if r.ParentEntity.Tag not in (64791,73591)]
        elif len(self.map_builders)==3:
            # Deliberately synthetic response: changing scope is NOT known to
            # return these rows in DC 2606. Test handling, not kernel behavior.
            self.on_reduced_update()


class ScopeViewTests(unittest.TestCase):
    def reject(self,h):
        before=h.snapshot()
        with self.assertRaises(RuntimeError) as caught:h.p.run()
        self.assertEqual(h.snapshot(),before)
        for builder in h.map_builders:
            builder.Commit.assert_not_called();builder.Validate.assert_not_called()
            builder.SetMatchingForParent.assert_not_called()
        h.t.setup.DeleteToolPath.assert_not_called();h.t.transfer_mcs_and_stock.assert_not_called()
        return caught.exception,'\n'.join(h.t.lines)

    def test_third_editor_uses_exact_subset_without_changing_main_selection(self):
        h=ScopeWorld();error,report=self.reject(h)
        self.assertEqual(len(h.map_builders),3)
        primary,full,reduced=h.map_builders
        self.assertEqual({f.Tag for f in primary.SelectFeature.values},{10,11})
        self.assertEqual({f.Tag for f in full.SelectFeature.values},{10,11})
        self.assertEqual([f.Tag for f in reduced.SelectFeature.values],[11])
        for builder in h.map_builders:builder.Destroy.assert_called_once()
        self.assertIn('81001',str(error));self.assertIn('ПРОСМОТР СУЖЕННОГО ВЫБОРА',report)
        self.assertIn('появились tag=[64791, 73591]',report)
        self.assertEqual(h.marks,{})

    def test_subset_editor_requires_destroy_and_integrity_of_previous_view(self):
        h=ScopeWorld();seen=[]
        h.on_reduced_update=lambda:seen.append(h.map_builders[1].Destroy.call_count)
        self.reject(h);self.assertEqual(seen,[1])
        h=ScopeWorld()
        def damage():
            if len(h.map_builders)==2:h.w.boxes[601]=(99.,)*6
        h.after_destroy_map=damage
        self.reject(h);self.assertEqual(len(h.map_builders),2)

    def test_partial_full_view_blocks_selection_experiment(self):
        h=ScopeWorld();original=h.after_unique
        def rows():
            original()
            if len(h.map_builders)==2:h.rows.append(NS())
        h.after_unique=rows
        _,report=self.reject(h);self.assertEqual(len(h.map_builders),2)
        self.assertIn('полный просмотр или сохранность',report)

    def test_reduced_view_rechecks_selection_and_modes_after_update(self):
        for field in ('selection','DoAutomaticGeomMatch','DeleteOriginalFeature'):
            with self.subTest(field=field):
                h=ScopeWorld()
                def damage():
                    if field=='selection':h.map_builders[-1].SelectFeature.values.clear()
                    else:setattr(h.map_builders[-1],field,field=='DoAutomaticGeomMatch')
                h.on_reduced_update=damage
                _,report=self.reject(h)
                self.assertIn('Суженный просмотр остановлен',report)
                self.assertNotIn('Сравнение полного и суженного выбора',report)

    def test_failed_reduced_destroy_prevents_post_view_checks(self):
        h=ScopeWorld();h.on_reduced_destroy=Mock(side_effect=RuntimeError('destroy failed'))
        _,report=self.reject(h)
        self.assertIn('закрытие редактора не подтверждено; далее откат',report)
        self.assertEqual(report.count('Независимая диагностика после ошибки карты: пройдено'),3)

    def test_failed_factory_is_not_reported_as_confirmed_editor_closure(self):
        h=ScopeWorld();factory=h.t.part.Features.CreateReplaceFeatureBuilder.side_effect
        def create():
            if len(h.map_builders)==2:raise RuntimeError('factory failed')
            return factory()
        h.t.part.Features.CreateReplaceFeatureBuilder.side_effect=create
        _,report=self.reject(h);self.assertEqual(len(h.map_builders),2)
        self.assertIn('factory failed',report)
        self.assertIn('Проверки после суженного просмотра заблокированы',report)

    def test_reusing_any_closed_editor_is_rejected(self):
        for index in (0,1):
            with self.subTest(index=index):
                h=ScopeWorld();factory=h.t.part.Features.CreateReplaceFeatureBuilder.side_effect
                def create():
                    return h.map_builders[index] if len(h.map_builders)==2 else factory()
                h.t.part.Features.CreateReplaceFeatureBuilder.side_effect=create
                _,report=self.reject(h)
                self.assertIn('Не получен отдельный редактор',report)
                for b in h.map_builders:b.Destroy.assert_called_once()

    def test_resource_failure_in_third_view_stops_reads_but_preserves_owned_undo(self):
        h=ScopeWorld();h.on_reduced_update=Mock(side_effect=M.NativeApiError(821049,'UpdateMap','limit'))
        _,report=self.reject(h)
        self.assertEqual(len(h.map_builders),3)
        self.assertIn('после суженного просмотра остановлены из-за 821049',report)
        self.assertNotIn('Локальный Undo новых кривых выполнен',report)
        self.assertEqual(sum(e[0]=='undo' for e in h.events),4)

    def test_lost_copy_in_third_view_blocks_foreign_cleanup_and_undo(self):
        h=ScopeWorld();h.on_reduced_update=lambda:setattr(h.t.session.Parts,'Work',NS(Tag=9999))
        with self.assertRaises(RuntimeError):h.p.run()
        h.map_builders[-1].Destroy.assert_not_called()
        self.assertEqual(sum(e[0]=='undo' for e in h.events),3)
        self.assertEqual(set(h.marks),{103})


if __name__=='__main__':unittest.main()
