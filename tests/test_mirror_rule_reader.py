"""BodyDumb per-rule membership contracts; no Siemens runtime is loaded."""
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock, patch

from test_mirror_map_inputs import M, CollectorWorld, SelectionIntentRule, BodyDumbRule


class RuleReaderTests(unittest.TestCase):
    def world(self):
        h=CollectorWorld()
        # Deliberately no instance GetData on the reported base wrapper.
        h.rules=[SelectionIntentRule(h)]
        return h

    def rejected(self,h,text='.*',exception=RuntimeError):
        before=h.snapshot()
        with self.assertRaisesRegex(exception,text) as caught:h.p.run()
        self.assertEqual(h.snapshot(),before);self.assertEqual(h.marks,{})
        h.w.replace.Commit.assert_not_called()
        h.t.setup.DeleteToolPath.assert_not_called();h.t.transfer_mcs_and_stock.assert_not_called()
        return caught.exception

    def test_report_base_wrapper_type_34_reads_definition_and_completes_proposal(self):
        h=self.world();h.rules[0].Type=34;h.t.nx.SelectionIntentRule.RuleType.BodyDumb=34
        self.assertFalse(isinstance(h.rules[0],h.t.nx.BodyDumbRule))
        self.assertFalse(hasattr(h.rules[0],'GetData'))
        before=h.snapshot()
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertEqual(h.snapshot(),before);self.assertEqual(h.marks,{})
        h.assert_probe_candidates_only(self)
        messages=[s for s in h.t.lines if s.startswith('Чтение правила ScCollector')]
        self.assertEqual(len(messages),1)
        self.assertIn('оболочка=SelectionIntentRule; Type=34; BodyDumb установленного NX=34',messages[0])
        self.assertIn('AskRulesAndCollectedEntities',messages[0])
        h.collector.AskRulesAndCollectedEntities.assert_called()
        self.assertIn('counts=(1,)', '\n'.join(h.t.lines))
        pairs={e[1]:e[2] for e in h.map_events if isinstance(e,tuple)}
        self.assertEqual((pairs[1000],pairs[64791],pairs[73591]),(4000,900001,900002))

    def test_installed_enum_is_used_even_when_its_value_is_not_34(self):
        h=self.world();h.rules[0].Type=912;h.t.nx.SelectionIntentRule.RuleType.BodyDumb=912
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        h.w.replace.Commit.assert_not_called()

    def test_literal_34_does_not_authorize_a_different_installed_rule_type(self):
        h=self.world();h.rules[0].Type=34;h.t.nx.SelectionIntentRule.RuleType.BodyDumb=912
        with patch.object(BodyDumbRule,'GetData') as reader:self.rejected(h,'BodyDumb установленного NX=912')
        reader.assert_not_called();h.w.replace.UpdateMap.assert_not_called()

    def test_unknown_rule_never_calls_body_reader_even_if_collector_has_right_body(self):
        h=self.world();h.rules[0].Type='BodyFeature'
        with patch.object(BodyDumbRule,'GetData') as reader:self.rejected(h,'Нет проверенного чтения правила')
        reader.assert_not_called();h.w.replace.SetMatchingForParent.assert_not_called()

    def test_real_descriptor_refusal_is_reproduced_but_never_called_on_base(self):
        h=self.world()
        with self.assertRaisesRegex(TypeError, "doesn't apply"):
            BodyDumbRule.GetData(h.rules[0])
        with patch.object(BodyDumbRule,'GetData',side_effect=AssertionError('base cannot be cast')) as reader:
            with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        reader.assert_not_called();h.w.replace.Commit.assert_not_called()

    def test_grouped_native_reader_failure_preserves_original_code(self):
        h=self.world();error=M.NativeApiError(7331,'AskRulesAndCollectedEntities','read failed')
        h.collector.AskRulesAndCollectedEntities.side_effect=error
        result=self.rejected(h,exception=M.NativeApiError)
        self.assertIs(result,error);self.assertEqual(result.code,7331)
        h.collector.AskRulesAndCollectedEntities.assert_called_once()
        h.w.replace.UpdateMap.assert_not_called()
        self.assertIn('AskRulesAndCollectedEntities',h.t.stage)

    def test_typed_reader_failure_is_not_retried_as_generic(self):
        h=CollectorWorld();error=M.NativeApiError(7332,'GetData','typed failed')
        with patch.object(BodyDumbRule,'GetData',side_effect=error) as reader:
            result=self.rejected(h,exception=M.NativeApiError)
        self.assertIs(result,error);reader.assert_called_once()
        self.assertIn('rule.GetData()',h.t.stage)

    def test_unreadable_or_wrong_grouped_result_never_uses_getobjects_as_definition(self):
        for value in (None,[],(),([NS(Tag=1000)],),([NS(Tag=1000)],[],[1]),
                      ([NS(Tag=1000)],[object()],[1]),(None,[],[]),([],None,[]),([],[],None)):
            h=self.world();h.collector.AskRulesAndCollectedEntities.side_effect=None
            h.collector.AskRulesAndCollectedEntities.return_value=value
            with self.subTest(value=value):self.rejected(h,exception=Exception)
            h.collector.AskRulesAndCollectedEntities.assert_called_once()
            h.w.replace.UpdateMap.assert_not_called()

    def test_rule_type_change_during_grouped_read_is_rejected_before_map_update(self):
        h=self.world()
        def changed():
            value=h.collected_rules();h.rules[0].Type='BodyFeature';return value
        h.collector.AskRulesAndCollectedEntities.side_effect=changed
        self.rejected(h,'Нет проверенного чтения правила')
        h.w.replace.UpdateMap.assert_not_called()

    def test_unreadable_rule_type_preserves_native_error(self):
        h=self.world()
        class Unreadable(SelectionIntentRule):
            @property
            def Type(self):raise M.NativeApiError(7333,'SelectionIntentRule.Type','type failed')
        h.collector.GetRules.side_effect=lambda:[object.__new__(Unreadable)]
        with patch.object(BodyDumbRule,'GetData') as reader:
            result=self.rejected(h,exception=M.NativeApiError)
        self.assertEqual(result.code,7333);reader.assert_not_called()

    def test_generic_and_typed_views_with_same_data_have_same_snapshot(self):
        h=self.world();views=[SelectionIntentRule(h),BodyDumbRule(h)];count=[0]
        def rules():count[0]+=1;return [views[count[0]%2]]
        h.collector.GetRules.side_effect=rules
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertGreater(count[0],4);h.w.replace.Commit.assert_not_called()

    def test_generic_definition_damage_on_destroy_is_detected_and_rolled_back(self):
        h=self.world();h.after_destroy_map=lambda:h.rules[0].tags.clear()
        self.rejected(h,'AskRulesAndCollectedEntities')

    def test_generic_definition_damage_after_undo_is_not_reported_as_success(self):
        h=self.world()
        def damage():
            if h.events[-1]==('undo',103):h.rules[0].tags.clear()
        h.after_undo=damage
        with self.assertRaisesRegex(RuntimeError,'AskRulesAndCollectedEntities'):h.p.run()
        self.assertEqual(set(h.marks),{103})
        self.assertNotIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))

    def test_reader_error_stays_primary_when_destroy_fails_too(self):
        h=self.world()
        def cleanup():raise M.NativeApiError(7335,'Destroy','cleanup failed')
        h.after_destroy_map=cleanup
        h.collector.AskRulesAndCollectedEntities.side_effect=M.NativeApiError(7334,'AskRulesAndCollectedEntities','primary')
        result=self.rejected(h,exception=M.NativeApiError)
        self.assertEqual(result.code,7334);self.assertIn('7335','\n'.join(h.t.lines))

    def test_missing_grouped_api_stops_before_map_without_unbound_retry(self):
        h=self.world();del h.collector.AskRulesAndCollectedEntities
        with patch.object(BodyDumbRule,'GetData') as reader:
            self.rejected(h,'AskRulesAndCollectedEntities',AttributeError)
        reader.assert_not_called();h.w.replace.UpdateMap.assert_not_called()

    def test_counts_must_partition_all_objects_exactly_without_coercion(self):
        for counts in ([],[0],[-1],[2],[True],[1.0],['1'],[1,1],[M.MAX_OBJECTS+1]):
            h=self.world()
            h.collector.AskRulesAndCollectedEntities.side_effect=lambda:([h.w.body],h.rules[:],counts)
            with self.subTest(counts=counts):self.rejected(h,'разбиение объектов')
            h.w.replace.UpdateMap.assert_not_called()

    def test_grouped_objects_must_be_exact_source_body_not_other_geometry(self):
        for tags in ([538463],[4000],[1000,1000]):
            h=self.world()
            h.collector.AskRulesAndCollectedEntities.side_effect=lambda:([h.w.objects[t] for t in tags],h.rules[:],[len(tags)])
            with self.subTest(tags=tags):self.rejected(h,'требуется выбранное|неоднозначный')
            h.w.replace.UpdateMap.assert_not_called()

    def test_rule_arrays_must_agree_even_when_total_members_match(self):
        h=self.world()
        h.collector.AskRulesAndCollectedEntities.side_effect=lambda:([h.w.body],h.rules*2,[1,0])
        self.rejected(h,'состав/порядок правил');h.w.replace.UpdateMap.assert_not_called()

    def test_second_rule_with_same_body_is_not_silently_deduplicated(self):
        h=self.world();h.rules.append(SelectionIntentRule(h))
        self.rejected(h,'Определение и вычисленный выбор')
        h.w.replace.UpdateMap.assert_not_called()

    def test_nonboolean_filter_values_are_not_coerced_or_treated_as_false(self):
        for key in (10,20,30,40,50):
            for value in (None,0,1,'True'):
                h=self.world();h.filters[key]=value
                with self.subTest(key=key,value=value):self.rejected(h,'Не прочитан bool фильтра')
                h.collector.AskRulesAndCollectedEntities.assert_not_called()
                h.w.replace.UpdateMap.assert_not_called()

    def test_new_installed_filter_is_also_checked(self):
        h=self.world();h.t.nx.ScEvaluationFiltertype.NewInstalledFilter=61;h.filters[61]=True
        before=h.snapshot()
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertEqual(h.snapshot(),before)
        h.collector.IsEvaluationFilterEnabled.assert_any_call(61)
        h.collector.RemoveEvaluationFilter.assert_not_called()
        self.assertTrue(h.clones)
        for clone in h.clones:clone.RemoveEvaluationFilter.assert_called_once_with(61)

    def test_missing_filter_enum_or_unreadable_filter_preserves_failure(self):
        h=self.world();del h.t.nx.ScEvaluationFiltertype.SheetBody
        self.rejected(h,'перечисление фильтров')
        h.collector.AskRulesAndCollectedEntities.assert_not_called()
        h=self.world();error=M.NativeApiError(7340,'IsEvaluationFilterEnabled','not readable')
        h.collector.IsEvaluationFilterEnabled.side_effect=error
        self.assertIs(self.rejected(h,exception=M.NativeApiError),error)
        h.collector.AskRulesAndCollectedEntities.assert_not_called()

    def test_typed_returned_wrapper_is_cross_checked_not_ignored(self):
        h=self.world();view=BodyDumbRule(h);view.tags=[]
        h.collector.AskRulesAndCollectedEntities.side_effect=lambda:([h.w.body],[view],[1])
        self.rejected(h,'BodyDumbRule.GetData');h.w.replace.UpdateMap.assert_not_called()

    def test_typed_returned_wrapper_error_is_not_retried_or_replaced_by_outputs(self):
        h=self.world();view=BodyDumbRule(h)
        h.collector.AskRulesAndCollectedEntities.side_effect=lambda:([h.w.body],[view],[1])
        error=M.NativeApiError(7341,'GetData','derived view failure')
        with patch.object(BodyDumbRule,'GetData',side_effect=error) as reader:
            self.assertIs(self.rejected(h,exception=M.NativeApiError),error)
        reader.assert_called_once();h.w.replace.UpdateMap.assert_not_called()

    def test_unknown_returned_rule_is_rejected_even_with_correct_body(self):
        h=self.world();view=SelectionIntentRule(h);view.Type='BodyFeature'
        h.collector.AskRulesAndCollectedEntities.side_effect=lambda:([h.w.body],[view],[1])
        self.rejected(h,'Нет проверенного чтения правила');h.w.replace.UpdateMap.assert_not_called()

    def test_collector_selection_is_read_back_after_grouped_call(self):
        h=self.world()
        def changed():
            result=h.collected_rules();h.members=[];return result
        h.collector.AskRulesAndCollectedEntities.side_effect=changed
        self.rejected(h,'ScCollector.GetObjects повторно')
        h.w.replace.UpdateMap.assert_not_called()

    def test_rule_list_is_read_back_after_grouped_call(self):
        h=self.world()
        def changed():
            result=h.collected_rules();h.rules=[];return result
        h.collector.AskRulesAndCollectedEntities.side_effect=changed
        self.rejected(h,'Не прочитаны правила');h.w.replace.UpdateMap.assert_not_called()

    def test_filter_drift_during_map_update_is_detected_and_undone(self):
        h=self.world();h.after_unique=lambda:h.filters.update({40:True})
        self.rejected(h,'SheetBody');h.w.replace.SetMatchingForParent.assert_not_called()

    def test_filter_drift_after_undo_is_not_reported_as_restoration(self):
        h=self.world()
        def damage():
            if h.events[-1]==('undo',103):h.filters[40]=True
        h.after_undo=damage
        with self.assertRaisesRegex(RuntimeError,'SheetBody'):h.p.run()
        self.assertEqual(set(h.marks),{103})
        self.assertNotIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))


if __name__=='__main__':unittest.main()
