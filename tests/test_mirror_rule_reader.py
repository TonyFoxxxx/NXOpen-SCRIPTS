"""Base-rule wrapper contracts from V1.30.01, not a test of NX bindings."""
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
        self.assertIn('BodyDumbRule.GetData(rule)',messages[0])
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

    def test_native_descriptor_can_refuse_base_wrapper_without_fallback_or_commit(self):
        h=self.world();error=TypeError('descriptor GetData requires a BodyDumbRule instance')
        with patch.object(BodyDumbRule,'GetData',side_effect=error) as reader:
            result=self.rejected(h,'descriptor GetData',TypeError)
        self.assertIs(result,error);reader.assert_called_once()
        self.assertIsInstance(reader.call_args.args[0],SelectionIntentRule)
        self.assertIn('BodyDumbRule.GetData(rule)',h.t.stage)
        h.w.replace.UpdateMap.assert_not_called()

    def test_generic_native_reader_failure_preserves_original_code(self):
        h=self.world();error=M.NativeApiError(7331,'BodyDumbRule.GetData','read failed')
        with patch.object(BodyDumbRule,'GetData',side_effect=error) as reader:
            result=self.rejected(h,exception=M.NativeApiError)
        self.assertIs(result,error);self.assertEqual(result.code,7331);reader.assert_called_once()
        h.w.replace.UpdateMap.assert_not_called()

    def test_typed_reader_failure_is_not_retried_as_generic(self):
        h=CollectorWorld();error=M.NativeApiError(7332,'GetData','typed failed')
        with patch.object(BodyDumbRule,'GetData',side_effect=error) as reader:
            result=self.rejected(h,exception=M.NativeApiError)
        self.assertIs(result,error);reader.assert_called_once()
        self.assertIn('rule.GetData()',h.t.stage)

    def test_unreadable_or_wrong_generic_result_never_uses_getobjects_as_definition(self):
        for value in (None,[],[NS(Tag=538463)],[NS(Tag=1000),NS(Tag=1000)],[[NS(Tag=1000)]]):
            h=self.world()
            with self.subTest(value=value),patch.object(BodyDumbRule,'GetData',return_value=value) as reader:
                self.rejected(h,exception=Exception)
            reader.assert_called_once();h.w.replace.UpdateMap.assert_not_called()

    def test_rule_type_change_during_read_is_rejected_before_map_update(self):
        h=self.world();original=BodyDumbRule.GetData
        def changed(rule):
            value=original(rule);rule.Type='BodyFeature';return value
        with patch.object(BodyDumbRule,'GetData',side_effect=changed):self.rejected(h,'Изменился тип правила')
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
        self.rejected(h,'BodyDumbRule.GetData')

    def test_generic_definition_damage_after_undo_is_not_reported_as_success(self):
        h=self.world()
        def damage():
            if h.events[-1]==('undo',103):h.rules[0].tags.clear()
        h.after_undo=damage
        with self.assertRaisesRegex(RuntimeError,'BodyDumbRule.GetData'):h.p.run()
        self.assertEqual(set(h.marks),{103})
        self.assertNotIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))

    def test_reader_error_stays_primary_when_destroy_fails_too(self):
        h=self.world()
        def cleanup():raise M.NativeApiError(7335,'Destroy','cleanup failed')
        h.after_destroy_map=cleanup
        with patch.object(BodyDumbRule,'GetData',side_effect=M.NativeApiError(7334,'GetData','primary')):
            result=self.rejected(h,exception=M.NativeApiError)
        self.assertEqual(result.code,7334);self.assertIn('7335','\n'.join(h.t.lines))


if __name__=='__main__':unittest.main()
