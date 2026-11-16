"""Collector/unique-input map contracts; synthetic API, not Siemens NX."""
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock

from test_mirror_historical_probe import M, HistoryWorld


class BodyDumbRule:
    def __init__(self, h):
        self.h, self.Type, self.tags = h, h.body_rule_type, [1000]
    def GetData(self): return [self.h.w.objects[t] for t in self.tags]


class CollectorWorld(HistoryWorld):
    """V1.30 shape: retained EXTRACT_BODY has a ScCollector of source body.

    The fixture supplies the unique-input view explicitly; it does not prove
    that DC 2606 will produce this view or accept the proposed matching.
    """
    def __init__(self):
        super().__init__()
        self.body_rule_type = 'installed-BodyDumb-enum'
        self.t.nx.BodyDumbRule = BodyDumbRule
        self.t.nx.SelectionIntentRule = NS(RuleType=NS(BodyDumb=self.body_rule_type))
        self.t.nx.Features.MatchedReferenceBuilder.Types.BodyCollector = 'installed-BodyCollector-enum'
        self.collector = self.t.nx.ScCollector()
        self.collector.Tag = 170651
        self.w.register(self.collector)
        self.members, self.rules, self.modes = [1000], [BodyDumbRule(self)], [False,False]
        self.collector.GetObjects = Mock(side_effect=lambda:[self.w.objects[t] for t in self.members])
        self.collector.GetRules = Mock(side_effect=lambda:self.rules[:])
        self.collector.GetMultiComponent = lambda:self.modes[0]
        self.collector.GetNonFeatureMode = lambda:self.modes[1]
        # Existing retained fixture feature stands for the external EXTRACT_BODY.
        self.w.references[69595] += ((170651,b'Body for Extracted Body'),)
        self.after_unique, self.before_inputs = lambda:None, lambda:None
        self.hide_member = self.retain_collector = False

    def snapshot(self):
        result = super().snapshot()
        result['collector'] = (self.members[:], [(type(r),r.Type,r.tags[:]) for r in self.rules], self.modes[:])
        return result

    def restore(self,saved):
        super().restore(saved)
        members,rules,modes = saved['collector']
        self.members,self.modes = members[:],modes[:]
        self.rules=[]
        for cls,kind,tags in rules:
            rule=cls(self);rule.Type,rule.tags=kind,tags[:];self.rules.append(rule)

    def row(self, obj, kind):
        return NS(ParentEntity=obj,MatchedEntity=None,ReverseDirection=False,Type=kind,
            MatchedStatus=self.t.nx.Features.MatchedReferenceBuilder.ResolvedStatus.Initial,Validate=Mock(return_value=True))

    def map_builder(self):
        builder=super().map_builder()
        builder.DisplayUniqueInputsToMap=False
        update=builder.UpdateMap.side_effect
        def populate():
            if builder.DisplayUniqueInputsToMap:
                if not self.retain_collector:self.rows[:]=[r for r in self.rows if r.ParentEntity.Tag!=170651]
                if not self.hide_member:self.rows.append(self.row(self.w.body,self.t.nx.Features.MatchedReferenceBuilder.Types.Object))
                update();self.after_unique()
            else:
                self.rows.append(self.row(self.collector,self.t.nx.Features.MatchedReferenceBuilder.Types.BodyCollector))
                update();self.before_inputs()
        builder.UpdateMap.side_effect=populate
        return builder


class MapInputsTests(unittest.TestCase):
    def rejected(self,h,text='.*',exception=RuntimeError):
        before=h.snapshot()
        with self.assertRaisesRegex(exception,text) as caught:h.p.run()
        self.assertEqual(h.snapshot(),before)
        self.assertEqual(h.marks,{})
        h.w.replace.Commit.assert_not_called()
        h.t.setup.DeleteToolPath.assert_not_called();h.t.transfer_mcs_and_stock.assert_not_called()
        return caught.exception

    def test_body_collector_is_expanded_and_body_is_explicitly_mapped_without_commit(self):
        h=CollectorWorld();before=h.snapshot()
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertEqual(h.snapshot(),before);self.assertEqual(h.marks,{})
        self.assertEqual(h.map_events.count('update'),2)
        pairs={e[1]:e[2] for e in h.map_events if isinstance(e,tuple)}
        self.assertEqual(pairs[1000],4000)
        self.assertEqual(pairs[64791],900001);self.assertEqual(pairs[73591],900002)
        self.assertNotIn(170651,pairs)
        self.assertIn('пропущенных/лишних нет','\n'.join(h.t.lines))
        h.assert_probe_candidates_only(self)

    def test_missing_body_row_cannot_be_hidden_by_true_validate(self):
        h=CollectorWorld();h.hide_member=True
        self.rejected(h,'отсутствуют \\[1000\\]');h.w.replace.SetMatchingForParent.assert_not_called()

    def test_unexpanded_collector_cannot_be_silently_skipped(self):
        h=CollectorWorld();h.retain_collector=True
        self.rejected(h,'лишние \\[170651\\]');h.w.replace.SetMatchingForParent.assert_not_called()

    def test_existing_historical_row_cannot_disappear_during_expansion(self):
        h=CollectorWorld();h.after_unique=lambda:h.rows.remove(next(r for r in h.rows if r.ParentEntity.Tag==64791))
        self.rejected(h,'отсутствуют \\[64791\\]');h.w.replace.SetMatchingForParent.assert_not_called()

    def test_unexpected_rows_cannot_be_accepted_even_if_geometric_mapping_exists(self):
        h=CollectorWorld();h.after_unique=lambda:h.rows.append(h.row(h.w.objects[10],h.t.nx.Features.MatchedReferenceBuilder.Types.Object))
        self.rejected(h,'лишние \\[10\\]');h.w.replace.SetMatchingForParent.assert_not_called()

    def test_direct_row_type_or_direction_change_is_rejected(self):
        for field,value in [('Type',999),('ReverseDirection',True),('ReverseDirection',0)]:
            h=CollectorWorld()
            h.after_unique=lambda:setattr(next(r for r in h.rows if r.ParentEntity.Tag==64791),field,value)
            with self.subTest(field=field,value=value):self.rejected(h,'определение строки')

    def test_body_input_direction_must_not_be_reversed(self):
        h=CollectorWorld();h.after_unique=lambda:setattr(next(r for r in h.rows if r.ParentEntity.Tag==1000),'ReverseDirection',True)
        self.rejected(h,'определение строки')

    def test_unsupported_rule_is_reported_before_switching_map_mode(self):
        h=CollectorWorld();h.rules[0].Type='BodyFeature'
        self.rejected(h,'Нет проверенного чтения правила.*170651')
        self.assertFalse(h.w.replace.DisplayUniqueInputsToMap)
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_rule_class_must_match_installed_api_and_not_only_type_name(self):
        class OtherRule(BodyDumbRule):pass
        h=CollectorWorld();h.t.nx.BodyDumbRule=OtherRule
        self.rejected(h,'Нет проверенного чтения правила')

    def test_unreadable_rules_are_not_empty_selection(self):
        h=CollectorWorld();h.collector.GetRules.side_effect=lambda:None
        self.rejected(h,'ScCollector.GetRules')
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_empty_definition_members_or_rules_are_rejected(self):
        for field in ('members','rules','definition'):
            h=CollectorWorld()
            if field=='definition':h.rules[0].tags=[]
            else:setattr(h,field,[])
            with self.subTest(field=field):self.rejected(h)

    def test_face_collector_cannot_be_treated_as_source_body(self):
        h=CollectorWorld();h.members=[538463]
        self.rejected(h,'Нет исходного определения ScCollector')

    def test_negation_or_duplicate_rule_members_cannot_be_ignored(self):
        h=CollectorWorld();h.rules.append(BodyDumbRule(h))
        self.rejected(h,'Определение и вычисленный выбор')

    def test_nonlocal_nonfeature_or_unreadable_modes_are_rejected(self):
        for modes in ([True,False],[False,True],[0,False],[False,None]):
            h=CollectorWorld();h.modes=modes
            with self.subTest(modes=modes):self.rejected(h,'локальный модельный режим')

    def test_collector_row_type_and_reverse_are_checked(self):
        for field,value in [('Type',999),('ReverseDirection',True)]:
            h=CollectorWorld();h.before_inputs=lambda:setattr(h.rows[-1],field,value)
            with self.subTest(field=field):self.rejected(h,'BodyCollector без разворота')

    def test_unknown_collector_without_retained_consumer_is_rejected(self):
        h=CollectorWorld();h.w.references[69595]=tuple((p,n) for p,n in h.w.references[69595] if p!=170651)
        self.rejected(h,'BodyCollector без разворота')

    def test_collector_changes_during_mode_update_are_detected(self):
        h=CollectorWorld();h.after_unique=lambda:h.rules[0].tags.clear()
        self.rejected(h,'BodyDumbRule.GetData');h.w.replace.SetMatchingForParent.assert_not_called()

    def test_definition_is_captured_before_first_update_map(self):
        h=CollectorWorld();h.before_inputs=lambda:h.rules[0].tags.clear()
        self.rejected(h,'BodyDumbRule.GetData')
        self.assertFalse(h.w.replace.DisplayUniqueInputsToMap)
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_previously_captured_collector_must_be_present_in_initial_map(self):
        h=CollectorWorld();h.before_inputs=lambda:h.rows.pop()
        self.rejected(h,'не включил исходные коллекции')
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_body_row_status_is_checked_even_when_validate_returns_true(self):
        h=CollectorWorld()
        def unresolved():
            next(r for r in h.rows if r.ParentEntity.Tag==1000).MatchedStatus=h.t.nx.Features.MatchedReferenceBuilder.ResolvedStatus.Unresolved
        h.after_match=unresolved
        self.rejected(h,'тип/статус строки раскрытой карты 1000')

    def test_body_row_type_cannot_change_during_matching(self):
        h=CollectorWorld();h.after_match=lambda:setattr(next(r for r in h.rows if r.ParentEntity.Tag==1000),'Type',999)
        self.rejected(h,'тип/статус строки раскрытой карты 1000')

    def test_unique_inputs_property_must_be_confirmed_by_readback(self):
        h=CollectorWorld();factory=h.map_builder
        class RefusesUnique:
            @property
            def DisplayUniqueInputsToMap(self):return False
            @DisplayUniqueInputsToMap.setter
            def DisplayUniqueInputsToMap(self,value):pass
        def refusing():
            original=factory();replacement=RefusesUnique()
            replacement.__dict__.update(original.__dict__)
            h.w.replace=replacement
            return replacement
        h.t.part.Features.CreateReplaceFeatureBuilder.side_effect=refusing
        self.rejected(h,'не подтвердил DisplayUniqueInputsToMap=True')
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_collector_changes_during_matching_are_detected(self):
        h=CollectorWorld();h.after_match=lambda:h.members.clear()
        self.rejected(h,'ScCollector.GetObjects')

    def test_collector_changes_on_builder_destroy_are_detected(self):
        h=CollectorWorld();h.after_destroy_map=lambda:h.rules.clear()
        self.rejected(h,'Не прочитаны правила')

    def test_collector_damage_after_undo_cannot_be_reported_as_restored(self):
        h=CollectorWorld()
        def damage():
            if h.events[-1]==('undo',103):h.rules.clear()
        h.after_undo=damage
        with self.assertRaisesRegex(RuntimeError,'Не прочитаны правила'):h.p.run()
        self.assertEqual(set(h.marks),{103})
        self.assertNotIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))

    def test_native_getter_failure_preserves_code_and_does_not_assign(self):
        h=CollectorWorld();h.collector.GetObjects.side_effect=M.NativeApiError(7306,'GetObjects','native failed')
        error=self.rejected(h,exception=M.NativeApiError)
        self.assertEqual(error.code,7306);h.w.replace.SetMatchingForParent.assert_not_called()

    def test_update_map_failure_preserves_primary_when_destroy_also_fails(self):
        h=CollectorWorld()
        def fail():raise M.NativeApiError(7307,'UpdateMap','native failed')
        def cleanup():raise M.NativeApiError(7308,'Destroy','cleanup failed')
        h.after_unique,h.after_destroy_map=fail,cleanup
        error=self.rejected(h,exception=M.NativeApiError)
        self.assertEqual(error.code,7307);self.assertIn('7308','\n'.join(h.t.lines))

    def test_change_of_unique_mode_or_automatic_matching_is_rejected(self):
        for field,value in [('DisplayUniqueInputsToMap',False),('DoAutomaticGeomMatch',True)]:
            h=CollectorWorld();h.after_unique=lambda:setattr(h.w.replace,field,value)
            with self.subTest(field=field):self.rejected(h,'режим')
            h.w.replace.SetMatchingForParent.assert_not_called()

    def test_original_feature_selection_cannot_change_during_expansion(self):
        h=CollectorWorld();h.after_unique=lambda:h.w.replace.SelectFeature.values.clear()
        self.rejected(h,'другой выбор');h.w.replace.SetMatchingForParent.assert_not_called()

    def test_lost_work_part_during_second_update_prevents_match_and_foreign_undo(self):
        h=CollectorWorld();h.after_unique=lambda:setattr(h.t.session.Parts,'Work',NS(Tag=999))
        with self.assertRaises(RuntimeError):h.p.run()
        h.w.replace.SetMatchingForParent.assert_not_called();h.w.replace.Commit.assert_not_called()
        self.assertEqual([e[1] for e in h.events if e[0]=='undo'],[100,101,102])


if __name__=='__main__':unittest.main()
