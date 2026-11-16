"""Disposable filter-comparison protocol, not a DC 2606 kernel simulation."""
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock

from test_mirror_map_inputs import M, CollectorWorld, SelectionIntentRule
from test_mirror_replacement_probe import Body


class CollectorFilterTests(unittest.TestCase):
    def world(self):
        h=CollectorWorld();h.rules=[SelectionIntentRule(h)];h.filters[10]=True
        return h

    def assert_original_not_written(self,h):
        h.collector.RemoveEvaluationFilter.assert_not_called()
        h.collector.Destroy.assert_not_called()
        h.w.replace.Commit.assert_not_called()
        h.t.setup.DeleteToolPath.assert_not_called()
        h.t.transfer_mcs_and_stock.assert_not_called()

    def rejected(self,h,message='.*',exception=RuntimeError):
        before=h.snapshot()
        with self.assertRaisesRegex(exception,message) as caught:h.p.run()
        self.assertEqual(h.snapshot(),before);self.assertEqual(h.marks,{})
        self.assert_original_not_written(h)
        return caught.exception

    def hidden_body(self,h):
        h.w.register(Body(70001));h.hidden_tags.add(70001);h.rules[0].tags.append(70001)

    def test_sleepy_filter_is_preserved_and_each_clone_removed_before_next_stage(self):
        h=self.world();before=h.snapshot()
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertEqual(h.snapshot(),before);self.assertEqual(h.marks,{})
        self.assert_original_not_written(h)
        self.assertGreater(len(h.clones),2)  # Re-checks do not trust a cached filtered result.
        for c in h.clones:
            c.RemoveEvaluationFilter.assert_called_once_with(10);c.Destroy.assert_called_once()
        self.assertEqual(h.t.part.ScCollectors,[h.collector])
        self.assertIn('SleepyEntity=True (bool)','\n'.join(h.t.lines))
        self.assertIn('исходные фильтры сохранены, временная коллекция удалена','\n'.join(h.t.lines))
        pairs={e[1]:e[2] for e in h.map_events if isinstance(e,tuple)}
        self.assertEqual(pairs[1000],4000)

    def test_multiple_enabled_filters_change_only_on_owned_clones(self):
        h=self.world();h.filters={k:True for k in h.filters};before=h.snapshot()
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertEqual(h.snapshot(),before);self.assert_original_not_written(h)
        for c in h.clones:
            self.assertEqual({a.args[0] for a in c.RemoveEvaluationFilter.call_args_list},set(h.filters))
            c.Destroy.assert_called_once()

    def test_no_enabled_filter_does_not_create_a_collector(self):
        h=self.world();h.filters[10]=False
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        h.collector.CopyCollector.assert_not_called();self.assert_original_not_written(h)

    def test_specialized_getdata_retains_complete_reader_without_a_clone(self):
        h=CollectorWorld();h.filters[10]=True
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        h.collector.CopyCollector.assert_not_called();self.assert_original_not_written(h)

    def test_hidden_rule_body_is_detected_in_unfiltered_clone(self):
        h=self.world();self.hidden_body(h)
        self.rejected(h,'требуется выбранное исходное тело.*70001')
        h.w.replace.UpdateMap.assert_not_called()
        self.assertEqual(len(h.clones),1);h.clones[0].Destroy.assert_called_once()

    def test_hidden_input_added_during_map_update_cannot_evade_snapshot_checks(self):
        h=self.world();h.w.register(Body(70001));h.hidden_tags.add(70001)
        h.after_unique=lambda:h.rules[0].tags.append(70001)
        self.rejected(h,'требуется выбранное исходное тело.*70001')
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_copy_returning_original_is_never_modified_or_destroyed(self):
        h=self.world();h.collector.CopyCollector.side_effect=lambda:h.collector
        self.rejected(h,'единственную новую коллекцию')
        self.assertEqual(h.clones,[])

    def test_copy_returning_another_preexisting_collector_is_never_destroyed(self):
        h=self.world();old=h.clone_collector()
        h.clone_events.clear();h.collector.CopyCollector.side_effect=lambda:old
        self.rejected(h,'единственную новую коллекцию')
        old.RemoveEvaluationFilter.assert_not_called();old.Destroy.assert_not_called()

    def test_foreign_or_occurrence_clone_is_left_for_owned_undo(self):
        for key,value in [('OwningPart',NS(Tag=999)),('IsOccurrence',True)]:
            h=self.world();h.after_clone=lambda c:setattr(c,key,value)
            with self.subTest(key=key):self.rejected(h)
            h.clones[0].RemoveEvaluationFilter.assert_not_called();h.clones[0].Destroy.assert_not_called()

    def test_clone_must_be_in_inventory_before_being_mutated(self):
        h=self.world();h.after_clone=lambda c:h.t.part.ScCollectors.remove(c)
        self.rejected(h,'единственную новую коллекцию')
        h.clones[0].RemoveEvaluationFilter.assert_not_called();h.clones[0].Destroy.assert_not_called()

    def test_partial_copy_without_returned_handle_is_undone_and_not_assumed_empty(self):
        h=self.world();error=M.NativeApiError(7401,'CopyCollector','partial copy')
        def partial():h.clone_collector();raise error
        h.collector.CopyCollector.side_effect=partial
        self.assertIs(self.rejected(h,exception=M.NativeApiError),error)
        h.clones[0].Destroy.assert_not_called()
        self.assertIn('состав ScCollectors','\n'.join(h.t.lines))
        self.assertIsNone(h.p.map_inputs.pending_collectors)

    def test_copy_creating_two_collectors_is_not_accepted(self):
        h=self.world()
        def duplicate():
            first=h.clone_collector();h.clone_collector();return first
        h.collector.CopyCollector.side_effect=duplicate
        self.rejected(h,'единственную новую коллекцию')
        for c in h.clones:c.Destroy.assert_not_called();c.RemoveEvaluationFilter.assert_not_called()

    def test_clone_must_preserve_original_filters_and_modes_before_comparison(self):
        for change in (lambda c:c.fixture_filters.update({10:False}),
                       lambda c:c.fixture_modes.__setitem__(1,True)):
            h=self.world();h.after_clone=change
            with self.subTest(change=change):self.rejected(h)
            h.clones[0].RemoveEvaluationFilter.assert_not_called();h.clones[0].Destroy.assert_called_once()

    def test_filter_removal_requires_exact_readback_not_a_success_return(self):
        h=self.world();h.after_remove_filter=lambda c:c.fixture_filters.update({10:True})
        self.rejected(h,'Не подтверждено изменение фильтра')
        h.clones[0].Destroy.assert_called_once();h.w.replace.UpdateMap.assert_not_called()

    def test_filter_removal_must_not_change_other_clone_filters(self):
        h=self.world();h.after_remove_filter=lambda c:c.fixture_filters.update({40:True})
        self.rejected(h,'Не подтверждено изменение фильтра')
        h.w.replace.UpdateMap.assert_not_called()

    def test_shared_filter_storage_that_changes_original_is_rejected_and_undone(self):
        h=self.world();h.after_clone=lambda c:setattr(c,'fixture_filters',h.filters)
        self.rejected(h,'Изменились фильтры исходной коллекции')
        self.assertTrue(h.filters[10]);h.w.replace.UpdateMap.assert_not_called()

    def test_copy_that_changes_original_rule_is_detected_before_filter_removal(self):
        h=self.world();h.after_clone=lambda c:h.rules[0].tags.clear()
        self.rejected(h,'AskRulesAndCollectedEntities')
        h.clones[0].RemoveEvaluationFilter.assert_not_called();h.clones[0].Destroy.assert_called_once()

    def test_filter_removal_error_preserves_api_code_and_destroys_owned_clone(self):
        h=self.world();error=M.NativeApiError(7402,'RemoveEvaluationFilter','remove failed')
        h.after_clone=lambda c:setattr(c.RemoveEvaluationFilter,'side_effect',error)
        self.assertIs(self.rejected(h,exception=M.NativeApiError),error)
        h.clones[0].Destroy.assert_called_once();h.w.replace.UpdateMap.assert_not_called()

    def test_destroy_failure_is_not_reported_as_successful_deletion(self):
        h=self.world();error=M.NativeApiError(7403,'ScCollector.Destroy','destroy failed')
        h.after_clone=lambda c:setattr(c.Destroy,'side_effect',error)
        self.assertIs(self.rejected(h,exception=M.NativeApiError),error)
        h.clones[0].Destroy.assert_called_once()
        self.assertNotIn('временная коллекция удалена','\n'.join(h.t.lines))
        h.w.replace.UpdateMap.assert_not_called()

    def test_noop_destroy_cannot_leave_an_unreported_collector(self):
        h=self.world();h.after_clone=lambda c:setattr(c.Destroy,'side_effect',lambda:None)
        self.rejected(h,'состав ScCollectors')
        self.assertNotIn('временная коллекция удалена','\n'.join(h.t.lines))

    def test_primary_read_error_and_cleanup_error_keep_both_codes(self):
        h=self.world()
        def failed(c):
            c.AskRulesAndCollectedEntities.side_effect=M.NativeApiError(7404,'AskRulesAndCollectedEntities','primary')
            c.Destroy.side_effect=M.NativeApiError(7405,'Destroy','cleanup')
        h.after_clone=failed
        error=self.rejected(h,exception=M.NativeApiError)
        self.assertEqual(error.code,7404);self.assertIn('7405','\n'.join(h.t.lines))

    def test_original_filter_change_during_destroy_is_detected(self):
        h=self.world();h.after_clone_destroy=lambda c:h.filters.update({10:False})
        self.rejected(h,'После Destroy изменилось определение')
        h.w.replace.UpdateMap.assert_not_called()

    def test_active_part_loss_after_copy_forbids_filter_writes_and_destroy(self):
        h=self.world();foreign=NS(Tag=9999)
        h.after_clone=lambda c:setattr(h.t.session.Parts,'Work',foreign)
        with self.assertRaisesRegex(RuntimeError,'рабочей и отображаемой'):h.p.run()
        h.clones[0].RemoveEvaluationFilter.assert_not_called();h.clones[0].Destroy.assert_not_called()
        self.assertEqual(h.t.session.Parts.Work,foreign);self.assertEqual(set(h.marks),{103})
        self.assertNotIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))
        self.assert_original_not_written(h)

    def test_partial_copy_surviving_undo_is_reported_as_unverified_restoration(self):
        h=self.world()
        def failed():h.clone_collector();raise M.NativeApiError(7406,'CopyCollector','partial')
        h.collector.CopyCollector.side_effect=failed
        def damage():
            if h.events[-1]==('undo',103):
                c=h.clones[0];h.w.register(c);h.t.part.ScCollectors.append(c)
        h.after_undo=damage
        with self.assertRaises(M.NativeApiError):h.p.run()
        self.assertIn('Не подтверждено удаление временных ScCollectors','\n'.join(h.t.lines))
        self.assertEqual(set(h.marks),{103})
        self.assertNotIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))

    def test_original_filter_damage_after_failed_copy_and_undo_is_detected(self):
        h=self.world();h.collector.CopyCollector.side_effect=M.NativeApiError(7407,'CopyCollector','fail')
        def damage():
            if h.events[-1]==('undo',103):h.filters[10]=False
        h.after_undo=damage
        with self.assertRaises(M.NativeApiError):h.p.run()
        self.assertIn('Изменилось прочитанное состояние ScCollector','\n'.join(h.t.lines))
        self.assertEqual(set(h.marks),{103})
        self.assertNotIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))


if __name__=='__main__':unittest.main()
