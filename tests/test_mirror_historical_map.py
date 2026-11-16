"""Historical Edge -> Curve proposal contracts; no NX commit or compatibility claim."""
import copy
from types import SimpleNamespace as NS
import unittest
from unittest.mock import patch

from test_mirror_historical_probe import M, HistoryWorld
from test_mirror_probe_mcs import report_mcs_world, attach_mcs, xyz


class HistoricalMapTests(unittest.TestCase):
    def rejected(self,h,text=None,exception=RuntimeError):
        before=h.snapshot()
        with self.assertRaisesRegex(exception,text or '.*') as caught:h.p.run()
        self.assertEqual(h.snapshot(),before)
        self.assertEqual(h.marks,{})
        self.assertIsNone(h.p.copy);self.assertIsNone(h.p.copy_feature);self.assertEqual(h.p.map,{})
        if hasattr(h.w,'replace'):h.w.replace.Commit.assert_not_called()
        h.t.setup.DeleteToolPath.assert_not_called();h.t.transfer_mcs_and_stock.assert_not_called()
        return caught.exception

    def row(self,h,tag=64791):return next(r for r in h.rows if r.ParentEntity.Tag==tag)

    def test_two_historical_curves_and_final_supports_are_mapped_without_commit(self):
        h=HistoryWorld();before=h.snapshot()
        with self.assertRaises(M.HistoricalProbeComplete) as complete:h.p.run()
        self.assertIn('без Commit',str(complete.exception))
        self.assertEqual(h.snapshot(),before)
        self.assertEqual({e[1]:e[2] for e in h.map_events if isinstance(e,tuple)},
            {11:41,64791:900001,73591:900002,538463:4001,560022:4002,
             64812:4010,65210:4011,66044:4012,66038:4013})
        self.assertEqual(h.map_events[0],'update');self.assertEqual(h.map_events[-1],'destroy')
        self.assertIsNone(h.p.copy);self.assertIsNone(h.p.copy_feature);self.assertEqual(h.p.map,{})
        h.assert_probe_candidates_only(self)

    def test_enum_values_come_from_installed_api_not_integer_guesses(self):
        h=HistoryWorld();types=h.t.nx.Features.MatchedReferenceBuilder
        types.Types.Curve='native-curve';types.ResolvedStatus.ByUser='native-user'
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertEqual(self.row(h).Type,'native-curve')
        self.assertEqual(self.row(h).MatchedStatus,'native-user')

    def test_missing_historical_row_prevents_any_mapping_assignment(self):
        h=HistoryWorld();h.after_map=lambda:h.rows.remove(self.row(h))
        self.rejected(h,'не включил.*опоры')
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_missing_final_topology_reference_is_still_rejected(self):
        h=HistoryWorld();h.after_map=lambda:h.rows.remove(self.row(h,66044))
        self.rejected(h,'не включил.*опоры')
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_unknown_row_cannot_be_ignored(self):
        h=HistoryWorld()
        def unknown():
            row=copy.copy(h.rows[0]);row.ParentEntity=h.w.objects[65349];h.rows.append(row)
        h.after_map=unknown
        self.rejected(h,'Нет проверенного соответствия')
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_section_collector_or_other_historical_row_type_is_not_coerced(self):
        for kind in (None,'Section','CurveCollector',999):
            h=HistoryWorld();h.after_map=lambda:setattr(self.row(h),'Type',kind)
            with self.subTest(kind=kind):self.rejected(h,'одиночная Curve-строка')
            self.assertEqual(self.row(h).Type,kind)
            h.w.replace.SetMatchingForParent.assert_not_called()

    def test_historical_reverse_direction_is_rejected_without_overwriting_it(self):
        h=HistoryWorld();h.after_map=lambda:setattr(self.row(h),'ReverseDirection',True)
        self.rejected(h,'без разворота')
        self.assertIs(self.row(h).ReverseDirection,True)
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_missing_type_or_status_property_is_not_defaulted(self):
        for name,hook in (('Type','after_map'),('MatchedStatus','after_match')):
            h=HistoryWorld()
            def remove():
                row=self.row(h)
                if hasattr(row,name):delattr(row,name)
            setattr(h,hook,remove)
            with self.subTest(name=name):self.rejected(h,exception=AttributeError)

    def test_unresolved_initial_or_unknown_status_fails_even_if_validate_is_true(self):
        for status in (83,84,None,'unexpected'):
            h=HistoryWorld();h.after_match=lambda:setattr(self.row(h),'MatchedStatus',status)
            with self.subTest(status=status):self.rejected(h,'тип/статус')
            self.assertEqual(self.row(h).MatchedStatus,status)

    def test_system_resolved_status_requires_the_exact_explicit_target(self):
        h=HistoryWorld()
        h.after_match=lambda:setattr(self.row(h),'MatchedStatus',h.t.nx.Features.MatchedReferenceBuilder.ResolvedStatus.BySystem)
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertEqual(self.row(h).MatchedEntity.Tag,900001)
        h.assert_probe_candidates_only(self)

    def test_wrong_or_null_readback_target_is_rejected(self):
        for target in (None,65349):
            h=HistoryWorld();h.after_match=lambda:setattr(self.row(h),'MatchedEntity',None if target is None else h.w.objects[target])
            with self.subTest(target=target):self.rejected(h,'сопоставление опоры')

    def test_changed_row_type_or_direction_after_assignment_is_rejected(self):
        for name,value in (('Type','Section'),('ReverseDirection',True),('ReverseDirection',0)):
            h=HistoryWorld();h.after_match=lambda:setattr(self.row(h),name,value)
            with self.subTest(name=name,value=value):self.rejected(h)

    def test_duplicate_parent_rows_are_preserved_and_assigned_once(self):
        h=HistoryWorld();h.after_map=lambda:h.rows.append(copy.copy(self.row(h)))
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertEqual(len([r for r in h.rows if r.ParentEntity.Tag==64791]),2)
        self.assertEqual(len([e for e in h.map_events if isinstance(e,tuple) and e[1]==64791]),1)
        self.assertTrue(all(r.MatchedEntity.Tag==900001 for r in h.rows if r.ParentEntity.Tag==64791))

    def test_row_loss_after_assignment_is_detected(self):
        h=HistoryWorld()
        def remove():
            if len(h.rows)==9:h.rows.remove(self.row(h))
        h.after_match=remove
        self.rejected(h,'состав опор редактора')

    def test_row_and_full_builder_validation_failures_never_commit(self):
        for which in ('row','references','builder'):
            h=HistoryWorld()
            def fail():
                target=self.row(h) if which=='row' else h.w.replace.FeatureReferences if which=='references' else h.w.replace
                target.Validate.return_value=False
            h.after_map=fail
            with self.subTest(which=which):self.rejected(h)

    def test_changed_original_or_replacement_selection_is_rejected(self):
        for field in ('SelectFeature','ReplacementFeature'):
            h=HistoryWorld();h.after_match=lambda:getattr(h.w.replace,field).values.clear()
            with self.subTest(field=field):self.rejected(h,'другой выбор')

    def test_candidate_curve_shape_is_read_again_after_map_preparation(self):
        h=HistoryWorld()
        def drift():h.candidates[900001]['data']['start']=(99.,2.,3.)
        h.after_map=drift
        self.rejected(h,'определение новой кривой')
        self.assertNotIn('Карта отражённого тела и исторических кривых проверена редактором','\n'.join(h.t.lines))

    def test_original_feature_states_and_expression_definitions_remain_guarded(self):
        for field in ('Timestamp','Suppressed','IsInternal','expression'):
            h=HistoryWorld()
            def drift():
                if field=='expression':h.t.part.Expressions[0].RightHandSide='91'
                else:setattr(h.w.objects[69573],field,999 if field=='Timestamp' else True)
            h.after_map=drift
            with self.subTest(field=field):self.rejected(h)

    def test_cam_centerline_selection_and_on_are_preserved(self):
        for drift in ('selection','on'):
            h=HistoryWorld()
            def damage():
                key=(900,0) if drift=='selection' else (900,0,'boundary_app',1,1)
                row=list(h.w.cam[key]);row[4 if drift=='selection' else -1]=(900001,) if drift=='selection' else 2
                h.w.cam[key]=tuple(row)
            h.after_map=damage
            with self.subTest(drift=drift):self.rejected(h,'CAM')

    def test_original_mcs_face_plane_is_checked_during_map(self):
        h=HistoryWorld();world,trial,planes=report_mcs_world();attach_mcs(h.t,trial)
        initial=planes[0].Origin
        h.after_map=lambda:setattr(planes[0],'Origin',xyz(1,0,-12))
        h.after_undo=lambda:setattr(planes[0],'Origin',initial)
        self.rejected(h,'Не подтверждены начало и оси СК')
        self.assertIs(planes[0].Origin,initial)

    def test_error_after_setting_match_preserves_code_stage_and_closes_builder(self):
        h=HistoryWorld()
        def fail():raise M.NativeApiError(7301,'SetMatchingForParent','mapping rejected')
        h.after_match=fail
        error=self.rejected(h,exception=M.NativeApiError)
        self.assertEqual(error.code,7301)
        self.assertIn('SetMatchingForParent',h.t.stage)
        h.w.replace.Destroy.assert_called_once_with()

    def test_destroy_failure_remains_a_failure_after_successful_local_undo(self):
        h=HistoryWorld()
        def fail():raise M.NativeApiError(7302,'Destroy','cleanup failed')
        h.after_destroy_map=fail
        error=self.rejected(h,exception=M.NativeApiError)
        self.assertEqual(error.code,7302)
        self.assertIn('Ошибка закрытия редактора карты','\n'.join(h.t.lines))

    def test_primary_and_destroy_errors_are_both_retained(self):
        h=HistoryWorld()
        def fail_map():raise M.NativeApiError(7303,'map','primary')
        def fail_destroy():raise M.NativeApiError(7304,'Destroy','secondary')
        h.after_map,h.after_destroy_map=fail_map,fail_destroy
        error=self.rejected(h,exception=M.NativeApiError)
        self.assertEqual(error.code,7303)
        self.assertIn('7304','\n'.join(h.t.lines));self.assertIn('Traceback','\n'.join(h.t.lines))

    def test_failed_undo_keeps_mark_and_does_not_claim_body_removal(self):
        h=HistoryWorld();h.after_map=lambda:h.fail.update(undo=M.NativeApiError(7305,'Undo','failed'))
        with self.assertRaises(M.NativeApiError) as caught:h.p.run()
        self.assertEqual(caught.exception.code,7305)
        self.assertEqual(set(h.marks),{103})
        self.assertIn(h.w.candidate,h.t.part.Bodies)
        self.assertNotIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))
        h.w.replace.Commit.assert_not_called()

    def test_surviving_body_after_undo_cannot_use_stale_candidate_exemption(self):
        h=HistoryWorld()
        def survive():
            if h.events[-1]==('undo',103):h.t.part.Bodies.append(h.w.candidate)
        h.after_undo=survive
        with self.assertRaisesRegex(RuntimeError,'состав тел'):h.p.run()
        self.assertIsNone(h.p.copy)
        self.assertEqual(set(h.marks),{103})
        self.assertNotIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))

    def test_loss_of_active_copy_after_extraction_blocks_parameter_removal(self):
        h=HistoryWorld();complete=h.w.after_extract
        def lose():complete();h.t.session.Parts.Work=NS(Tag=999)
        h.w.after_extract=lose
        with self.assertRaises(RuntimeError):h.p.run()
        h.t.native.delete_candidate_parameters.assert_not_called()
        h.t.native.transform_entities.assert_not_called()
        self.assertEqual([e[1] for e in h.events if e[0]=='undo'],[100,101,102])

    def test_loss_of_active_copy_during_map_blocks_matching_and_foreign_undo(self):
        h=HistoryWorld();h.after_map=lambda:setattr(h.t.session.Parts,'Work',NS(Tag=999))
        with self.assertRaises(RuntimeError):h.p.run()
        h.w.replace.SetMatchingForParent.assert_not_called();h.w.replace.Commit.assert_not_called()
        self.assertEqual([e[1] for e in h.events if e[0]=='undo'],[100,101,102])

    def test_owner_mismatch_is_rejected_before_body_extraction(self):
        h=HistoryWorld();original=M.HistoricalReplacementMapProbe.run
        def wrong_owner(probe):
            owner,curve=probe.h.shared_curves[64791]
            probe.h.shared_curves[64791]=(owner+1,curve)
            original(probe)
        with patch.object(M.HistoricalReplacementMapProbe,'run',wrong_owner):self.rejected(h,'не принадлежат выбранному телу')
        h.t.part.Features.CreateExtractFaceBuilder.assert_not_called()

    def test_missing_curve_blocks_body_creation_even_after_previous_read_success(self):
        h=HistoryWorld();original=M.HistoricalReplacementMapProbe.run
        def missing(probe):
            del probe.c.created[64791]
            original(probe)
        with patch.object(M.HistoricalReplacementMapProbe,'run',missing):self.rejected(h,'для каждой исторической опоры')
        h.t.part.Features.CreateExtractFaceBuilder.assert_not_called()

    def test_body_extraction_failure_is_rolled_back_with_prior_curve_candidates(self):
        h=HistoryWorld();complete=h.w.after_extract
        def partial():complete();raise M.NativeApiError(7306,'Extract','partial allocation')
        h.w.after_extract=partial
        error=self.rejected(h,exception=M.NativeApiError)
        self.assertEqual(error.code,7306)
        h.t.native.delete_candidate_parameters.assert_not_called()
        h.t.part.Features.CreateReplaceFeatureBuilder.assert_not_called()

    def test_current_feature_must_be_original_or_candidate_brep(self):
        h=HistoryWorld();h.after_map=lambda:setattr(h,'current',69573)
        self.rejected(h,'Текущий элемент при подготовке карты')

    def test_ambiguous_body_topology_never_reaches_builder_map(self):
        h=HistoryWorld();complete=h.w.after_extract
        def ambiguous():
            complete();h.w.boxes[4011]=h.w.boxes[4010]
        h.w.after_extract=ambiguous
        self.rejected(h,'единственное соответствие')
        h.t.part.Features.CreateReplaceFeatureBuilder.assert_not_called()

    def test_changed_candidate_topology_is_rejected_after_mapping(self):
        h=HistoryWorld();h.after_map=lambda:h.w.candidate.edges.pop()
        self.rejected(h,'идентификаторы его граней/рёбер')

    def test_candidate_geometry_is_rechecked_after_builder_destroy(self):
        h=HistoryWorld();h.after_destroy_map=lambda:h.w.boxes.update({4010:(2.,20.,0.,6.,21.,1.)})
        self.rejected(h,'Не подтверждено отражение геометрии кандидата 4010')
        self.assertIn('Проверка отражённого тела-кандидата 4000',h.t.stage)
