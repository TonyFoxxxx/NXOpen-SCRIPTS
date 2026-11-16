"""Selection options versus actual local BodyDumb inputs; not NX validation."""
import copy
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock

from test_mirror_map_inputs import M, CollectorWorld, SelectionIntentRule, BodyDumbRule
from test_mirror_replacement_probe import Body, BodyFeature


class StockCollectorWorld(CollectorWorld):
    """A protected Part Box alongside the existing dependent body collector.

    The report establishes Part Box ownership, not its actual mode values;
    NonFeatureMode=True here is a synthetic option-preservation scenario.
    The fake builder explicitly supplies both map rows. This is not evidence
    that NX will include a non-feature collector in its dependency map.
    """
    def __init__(self):
        super().__init__()
        self.rules=[SelectionIntentRule(self)]
        self.stock_collector=self.clone_collector();self.clones.clear();self.clone_events.clear()
        self.stock_collector.fixture_modes=[False,True]
        self.stock_collector.fixture_rules=[SelectionIntentRule(self)]
        self.stock_body=self.w.make_body(75000)
        self.stock_feature=BodyFeature(75010,'Tooling Box','Part Box',bodies=[self.stock_body])
        self.stock_feature.Timestamp,self.stock_feature.Suppressed,self.stock_feature.IsInternal=101,False,False
        self.w.add_feature(self.stock_feature);self.stock_body.features=[self.stock_feature]
        self.w.references[75010]=((self.stock_collector.Tag,b'Select Object for Part Box'),)
        self.t.nx.Features.ToolingBoxBuilder=NS(RefCsysType=NS(SelectedCsys=2),
            Types=NS(BoundedCylinder=3,CenterAndLengths=4))
        self.box=NS(Type=1,ReferenceCsysType=0,CsysAssociative=True,
            NonAlignedMinimumBox=False,SingleOffset=False,BoundedObject=self.stock_collector,
            Validate=Mock(return_value=True),Destroy=Mock(),CalculateBoxSize=Mock())
        self.flags=('Type','ReferenceCsysType','CsysAssociative','NonAlignedMinimumBox','SingleOffset')
        self.offsets=('Clearance','OffsetNegativeX','OffsetNegativeY','OffsetNegativeZ',
                      'OffsetPositiveX','OffsetPositiveY','OffsetPositiveZ','RadialOffset')
        for i,name in enumerate(self.offsets):setattr(self.box,name,NS(RightHandSide=str(i+1),Value=float(i+1)))
        self.t.tooling_builder=Mock(return_value=self.box)
        self.t.stock_features[75010]=(self.stock_feature,(75000,),self.t.stock_state(self.box),(2.,3.,4.))
        self.t.protected_bodies.add(75000)
        self.t.bodies=self.t.part.Bodies[:];self.t.bounds=dict(self.w.boxes)
        self.w.freeze();self.p=self.w.probe

    def snapshot(self):
        result=super().snapshot();c=self.stock_collector
        result['stock_collector']=(c.fixture_members[:],c.fixture_modes[:],c.fixture_filters.copy(),
            [(type(r),r.Type,r.tags[:]) for r in c.fixture_rules])
        result['stock_settings']=({n:getattr(self.box,n) for n in self.flags},
            {n:(getattr(self.box,n).RightHandSide,getattr(self.box,n).Value) for n in self.offsets})
        return result

    def restore(self,saved):
        super().restore(saved);c=self.stock_collector
        members,modes,filters,rules=saved['stock_collector']
        c.fixture_members,c.fixture_modes,c.fixture_filters=members[:],modes[:],filters.copy()
        c.fixture_rules=[]
        for cls,kind,tags in rules:
            rule=cls(self);rule.Type,rule.tags=kind,tags[:];c.fixture_rules.append(rule)
        flags,offsets=saved['stock_settings']
        for n,value in flags.items():setattr(self.box,n,value)
        for n,(rhs,value) in offsets.items():getattr(self.box,n).RightHandSide=rhs;getattr(self.box,n).Value=value

    def map_builder(self):
        builder=super().map_builder();populate=builder.UpdateMap.side_effect
        def update():
            populate()
            if builder.DisplayUniqueInputsToMap:
                self.rows[:]=[r for r in self.rows if r.ParentEntity.Tag!=self.stock_collector.Tag]
            else:self.rows.append(self.row(self.stock_collector,self.t.nx.Features.MatchedReferenceBuilder.Types.BodyCollector))
        builder.UpdateMap.side_effect=update
        return builder


class CollectorModeTests(unittest.TestCase):
    def world(self,modes=(True,True),generic=True):
        h=CollectorWorld();h.modes=list(modes)
        if generic:h.rules=[SelectionIntentRule(h)]
        return h

    def untouched(self,h):
        h.collector.RemoveEvaluationFilter.assert_not_called();h.collector.Destroy.assert_not_called()
        h.w.replace.Commit.assert_not_called();h.t.setup.DeleteToolPath.assert_not_called()
        h.t.transfer_mcs_and_stock.assert_not_called()

    def rejected(self,h,message='.*',exception=RuntimeError):
        before=h.snapshot()
        with self.assertRaisesRegex(exception,message) as caught:h.p.run()
        self.assertEqual(h.snapshot(),before);self.assertEqual(h.marks,{})
        self.untouched(h)
        return caught.exception

    def test_each_boolean_option_combination_preserves_explicit_local_body_rules(self):
        for generic in (False,True):
            for modes in ((False,False),(False,True),(True,False),(True,True)):
                h=self.world(modes,generic);before=h.snapshot()
                with self.subTest(generic=generic,modes=modes),self.assertRaises(M.HistoricalProbeComplete):h.p.run()
                self.assertEqual(h.snapshot(),before);self.untouched(h)
                report='\n'.join(h.t.lines)
                self.assertIn('MultiComponent='+str(modes[0])+' (bool)',report)
                self.assertIn('NonFeatureMode='+str(modes[1])+' (bool)',report)
                self.assertEqual(h.p.map_inputs.states[h.collector.Tag][2],modes)

    def test_report_shape_part_box_nonfeature_selection_keeps_full_stock_settings(self):
        h=StockCollectorWorld();before=h.snapshot();settings=copy.deepcopy(h.t.stock_state(h.box))
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertEqual(h.snapshot(),before);self.untouched(h)
        self.assertEqual(h.t.stock_state(h.box),settings)
        self.assertEqual(h.p.map_inputs.states[h.stock_collector.Tag][2],(False,True))
        h.stock_collector.RemoveEvaluationFilter.assert_not_called();h.stock_collector.Destroy.assert_not_called()
        h.box.CalculateBoxSize.assert_not_called();self.assertGreater(h.box.Validate.call_count,0)

    def test_enabled_modes_do_not_permit_other_selected_bodies(self):
        h=self.world();other=h.w.register(Body(77000))
        h.members.append(other.Tag);h.rules[0].tags.append(other.Tag)
        self.rejected(h,'требуется выбранное исходное тело')
        h.w.replace.UpdateMap.assert_not_called()

    def test_enabled_multicomponent_mode_does_not_permit_foreign_or_occurrence_inputs(self):
        for key,value in [('OwningPart',NS(Tag=9999)),('IsOccurrence',True)]:
            h=self.world();other=h.w.register(Body(77000));setattr(other,key,value)
            h.members.append(other.Tag);h.rules[0].tags.append(other.Tag)
            with self.subTest(key=key):self.rejected(h,'Не подтверждена опора текущей копии')
            h.w.replace.UpdateMap.assert_not_called()

    def test_nonfeature_mode_does_not_allow_unknown_selection_intent(self):
        h=self.world();h.rules[0].Type='unreadable-feature-based-rule'
        self.rejected(h,'Нет проверенного чтения правила')
        h.w.replace.UpdateMap.assert_not_called()

    def test_nonfeature_mode_does_not_hide_filtered_extra_rule_inputs(self):
        h=self.world();h.filters[10]=True
        h.w.register(Body(77000));h.hidden_tags.add(77000);h.rules[0].tags.append(77000)
        self.rejected(h,'требуется выбранное исходное тело')
        h.w.replace.UpdateMap.assert_not_called();h.clones[0].Destroy.assert_called_once()

    def test_modes_are_preserved_on_each_unfiltered_clone(self):
        h=self.world();h.filters[10]=True;before=h.snapshot()
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertEqual(h.snapshot(),before);self.untouched(h)
        self.assertGreater(len(h.clones),2)
        for c in h.clones:self.assertEqual(c.fixture_modes,[True,True]);c.Destroy.assert_called_once()

    def test_clone_mode_reset_is_rejected_before_filter_writes(self):
        h=self.world();h.filters[10]=True;h.after_clone=lambda c:c.fixture_modes.__setitem__(1,False)
        self.rejected(h,'CopyCollector не сохранил')
        h.clones[0].RemoveEvaluationFilter.assert_not_called();h.clones[0].Destroy.assert_called_once()

    def test_mode_change_during_rule_read_is_detected_in_same_read(self):
        h=self.world();reader=h.collected_rules
        def change():
            result=reader();h.modes[1]=False;return result
        h.collector.AskRulesAndCollectedEntities.side_effect=change
        self.rejected(h,'Изменились режимы при чтении')
        h.w.replace.UpdateMap.assert_not_called()

    def test_nonboolean_modes_report_exact_getter_value_and_stage(self):
        for index,name in enumerate(('MultiComponent','NonFeatureMode')):
            for value in (None,0,1,'True'):
                h=self.world();h.modes[index]=value
                with self.subTest(name=name,value=value):
                    error=self.rejected(h,'Не прочитан bool режима')
                self.assertIn(name,str(error));self.assertIn('значение='+repr(value),str(error))
                self.assertIn('/ Get'+name,h.t.stage)
                h.collector.GetRules.assert_not_called()

    def test_getter_failure_preserves_native_code_and_actual_getter_stage(self):
        for name in ('MultiComponent','NonFeatureMode'):
            h=self.world();error=M.NativeApiError(7601,'Get'+name,'mode read failed')
            setattr(h.collector,'Get'+name,Mock(side_effect=error))
            with self.subTest(name=name):self.assertIs(self.rejected(h,exception=M.NativeApiError),error)
            self.assertIn('/ Get'+name,h.t.stage)

    def test_mode_change_during_update_map_still_stops_before_matching(self):
        h=self.world();h.after_unique=lambda:h.modes.__setitem__(0,False)
        self.rejected(h,'Изменилось прочитанное состояние ScCollector')
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_mode_change_during_matching_cannot_pass_successful_validate(self):
        h=self.world();h.after_match=lambda:h.modes.__setitem__(1,False)
        self.rejected(h,'Изменилось прочитанное состояние ScCollector')

    def test_mode_change_on_builder_destroy_is_not_ignored(self):
        h=self.world();h.after_destroy_map=lambda:h.modes.__setitem__(0,False)
        self.rejected(h,'Изменилось прочитанное состояние ScCollector')

    def test_mode_damage_after_undo_invalidates_restoration(self):
        h=self.world()
        def damage():
            if h.events[-1]==('undo',103):h.modes[1]=False
        h.after_undo=damage
        with self.assertRaisesRegex(RuntimeError,'Изменилось прочитанное состояние ScCollector'):h.p.run()
        self.assertEqual(set(h.marks),{103});self.untouched(h)
        self.assertNotIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))

    def test_nonfeature_mode_does_not_allow_omitting_a_collector_from_map(self):
        h=self.world();h.before_inputs=lambda:h.rows.pop()
        self.rejected(h,'не включил исходные коллекции')
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_nonfeature_mode_does_not_allow_incomplete_expanded_map(self):
        h=self.world();h.hide_member=True
        self.rejected(h,'отсутствуют \\[1000\\]')
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_part_box_offsets_and_modes_remain_guarded_with_nonfeature_selection(self):
        for change in (lambda h:setattr(h.box.Clearance,'Value',0.0),
                       lambda h:setattr(h.box,'SingleOffset',True)):
            h=StockCollectorWorld();h.after_unique=lambda:change(h)
            with self.subTest(change=change):self.rejected(h,'параметры защищённой заготовки')
            h.box.CalculateBoxSize.assert_not_called()

    def test_part_box_collector_mode_change_after_capture_is_rejected(self):
        h=StockCollectorWorld();h.after_unique=lambda:h.stock_collector.fixture_modes.__setitem__(1,False)
        self.rejected(h,'Изменилось прочитанное состояние ScCollector')
        h.w.replace.SetMatchingForParent.assert_not_called()


if __name__=='__main__':unittest.main()
