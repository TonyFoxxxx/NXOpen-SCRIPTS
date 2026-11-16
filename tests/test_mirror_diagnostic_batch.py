"""Collect independent failures without authorizing an incomplete NX map."""
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock, patch

from test_mirror_map_inputs import M, CollectorWorld, SelectionIntentRule
from test_mirror_collector_modes import StockCollectorWorld
from test_mirror_replacement_probe import Edge, Face
from test_mirror_probe_mcs import report_mcs_world, attach_mcs


class ProbeBatchTests(unittest.TestCase):
    def batch(self,guard=lambda:None):
        t=NS(lines=[],stage='before')
        return t,M.ProbeChecks(t,guard,'batch')

    def test_errors_do_not_replace_results_with_empty_success(self):
        t,b=self.batch();error=M.NativeApiError(7701,'read','failed')
        self.assertEqual(b.run('bad',Mock(side_effect=error)),(False,None))
        self.assertEqual(b.run('empty',lambda:()),(True,()))
        self.assertEqual(b.run('none',lambda:None),(True,None))
        self.assertEqual(b.passed,2)
        with self.assertRaises(M.NativeApiError) as caught:b.finish('dependent mutation')
        self.assertIs(caught.exception,error)
        self.assertIn('7701','\n'.join(t.lines));self.assertIn('Traceback','\n'.join(t.lines))
        self.assertIn('Не выполнялись','\n'.join(t.lines))

    def test_multiple_errors_keep_each_context_code_and_traceback(self):
        t,b=self.batch()
        for i in range(2):b.run('object '+str(i),Mock(side_effect=M.NativeApiError(7702+i,'api','failed '+str(i))))
        b.run('later independent check',lambda:42)
        with self.assertRaises(M.ProbeCheckFailures) as caught:b.finish('assignment')
        self.assertEqual(len(caught.exception.errors),2)
        for text in ('object 0','object 1','7702','7703','failed 0','failed 1'):
            self.assertIn(text,'\n'.join(t.lines))
        self.assertEqual(b.passed,1)

    def test_guard_failure_before_read_is_not_collected_as_an_ordinary_data_error(self):
        guard=Mock(side_effect=RuntimeError('lost copy'));t,b=self.batch(guard);read=Mock()
        with self.assertRaisesRegex(RuntimeError,'lost copy'):b.run('read',read)
        read.assert_not_called();self.assertEqual(b.errors,[])

    def test_guard_failure_after_read_never_counts_as_a_pass(self):
        guard=Mock(side_effect=[None,RuntimeError('lost copy')]);t,b=self.batch(guard)
        with self.assertRaisesRegex(RuntimeError,'lost copy'):b.run('read',lambda:42)
        self.assertEqual(b.passed,0)

    def test_successful_batch_does_not_block_the_next_stage(self):
        t,b=self.batch();self.assertEqual(b.run('read',lambda:42),(True,42));b.finish('assignment')
        self.assertEqual(b.errors,[]);self.assertNotIn('Не выполнялись','\n'.join(t.lines))


class DiagnosticMapTests(unittest.TestCase):
    def add_unknown(self,h,tag=81001,cls=Edge):
        obj=h.w.register(cls(tag,h.w.body))
        return h.row(obj,h.t.nx.Features.MatchedReferenceBuilder.Types.Curve)

    def reject(self,h,pattern='.*',exception=RuntimeError):
        before=h.snapshot()
        with self.assertRaisesRegex(exception,pattern) as caught:h.p.run()
        self.assertEqual(h.snapshot(),before);self.assertEqual(h.marks,{})
        h.w.replace.Commit.assert_not_called();h.t.setup.DeleteToolPath.assert_not_called()
        h.t.transfer_mcs_and_stock.assert_not_called()
        return caught.exception

    def test_all_unknown_edges_faces_and_missing_collectors_are_reported_together(self):
        h=CollectorWorld();rows=[self.add_unknown(h),self.add_unknown(h,81002,Face)]
        def populate():h.rows.pop();h.rows.extend(rows)
        h.before_inputs=populate
        error=self.reject(h,exception=M.ProbeCheckFailures)
        report='\n'.join(h.t.lines)
        for text in ('Edge 81001','Face 81002','не включил исходные коллекции','170651',
                     'UF-владелец 81001: не прочитан','UF-владелец 81002: не прочитан'):
            self.assertIn(text,report)
        self.assertGreaterEqual(len(error.errors),3)
        h.w.replace.SetMatchingForParent.assert_not_called()
        self.assertEqual(h.w.replace.UpdateMap.call_count,1)
        self.assertIs(h.w.replace.DisplayUniqueInputsToMap,False)
        self.assertIn('Независимая диагностика после ошибки карты: пройдено',report)

    def test_unknown_owner_read_success_is_diagnostic_and_never_a_self_mapping(self):
        h=CollectorWorld();row=self.add_unknown(h)
        original=h.t.native.reference_body.side_effect
        h.t.native.reference_body.side_effect=lambda tag,kind:h.p.body_tag if tag==81001 else original(tag,kind)
        h.before_inputs=lambda:h.rows.append(row)
        self.reject(h,'Нет проверенного соответствия')
        self.assertIn('UF-владелец 81001: 1000','\n'.join(h.t.lines))
        self.assertNotIn(81001,h.p.map);h.w.replace.SetMatchingForParent.assert_not_called()

    def test_bad_map_row_does_not_trigger_more_filter_clones_before_undo(self):
        h=CollectorWorld();h.filters[10]=True;row=self.add_unknown(h)
        h.rules=[SelectionIntentRule(h)]
        clones_at_map=[];clones_after_destroy=[]
        def prepend():clones_at_map.append(len(h.clones));h.rows.insert(0,row)
        h.before_inputs=prepend
        h.after_destroy_map=lambda:clones_after_destroy.append(len(h.clones))
        self.reject(h)
        self.assertEqual(clones_at_map,clones_after_destroy)
        self.assertGreater(clones_at_map[0],0)
        self.assertIn('Коллекция в карте:','\n'.join(h.t.lines))

    def test_each_owner_api_error_keeps_its_code_and_later_rows_are_still_read(self):
        h=CollectorWorld();rows=[self.add_unknown(h),self.add_unknown(h,81002)]
        original=h.t.native.reference_body.side_effect
        def read(tag,kind):
            if tag in (81001,81002):raise M.NativeApiError(7800+tag-81000,'owner '+str(tag),'owner failed')
            return original(tag,kind)
        h.t.native.reference_body.side_effect=read;h.before_inputs=lambda:h.rows.extend(rows)
        self.reject(h,exception=M.ProbeCheckFailures)
        report='\n'.join(h.t.lines)
        self.assertIn('7801',report);self.assertIn('7802',report)
        self.assertIn('Исходная строка карты: tag=81002',report)

    def test_unreadable_row_does_not_prevent_diagnostics_for_following_row(self):
        h=CollectorWorld();row=self.add_unknown(h)
        h.before_inputs=lambda:h.rows.extend([NS(),row])
        self.reject(h,exception=M.ProbeCheckFailures)
        report='\n'.join(h.t.lines)
        self.assertIn('AttributeError',report);self.assertIn('Edge 81001',report)
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_all_bad_expanded_rows_are_checked_before_any_assignment(self):
        h=CollectorWorld()
        def damage():
            for tag in (64791,73591):next(r for r in h.rows if r.ParentEntity.Tag==tag).ReverseDirection=True
        h.after_unique=damage
        self.reject(h,exception=M.ProbeCheckFailures)
        report='\n'.join(h.t.lines)
        for tag in (64791,73591):self.assertIn('раскрытии карты: '+str(tag),report)
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_target_checks_collect_multiple_historical_direction_errors(self):
        h=CollectorWorld()
        def reverse():
            for tag in (64791,73591):next(r for r in h.rows if r.ParentEntity.Tag==tag).ReverseDirection=True
        h.before_inputs=reverse
        self.reject(h,exception=M.ProbeCheckFailures)
        report='\n'.join(h.t.lines)
        for tag in (64791,73591):self.assertIn('исторического Edge '+str(tag),report)
        h.w.replace.SetMatchingForParent.assert_not_called()

    def test_multiple_post_matching_errors_block_full_validate_and_commit(self):
        h=CollectorWorld()
        def unresolved():
            for tag in (64791,73591):next(r for r in h.rows if r.ParentEntity.Tag==tag).MatchedStatus=999
        h.after_match=unresolved
        self.reject(h,exception=M.ProbeCheckFailures)
        report='\n'.join(h.t.lines)
        for tag in (64791,73591):self.assertIn('карты '+str(tag),report)
        h.w.replace.Validate.assert_not_called()

    def test_after_error_audit_runs_after_destroy_and_before_owned_undo(self):
        h=CollectorWorld();row=self.add_unknown(h);h.before_inputs=lambda:h.rows.append(row)
        seen=[];reader=h.t.native.history_curve.side_effect
        def curve(tag):
            if 'Независимая диагностика' in h.t.stage:
                seen.append((h.map_events[-1],h.events[-1]))
            return reader(tag)
        h.t.native.history_curve.side_effect=curve
        self.reject(h)
        self.assertTrue(seen)
        self.assertTrue(all(map_event=='destroy' and event[0]!='undo' for map_event,event in seen))

    def test_audit_collects_geometry_expression_cam_and_operation_damage(self):
        h=CollectorWorld();row=self.add_unknown(h);h.before_inputs=lambda:h.rows.append(row)
        operations=h.t.operation_snapshot.return_value
        audited_curves=[];reader=h.t.native.history_curve.side_effect
        def read_curve(tag):
            if 'Независимая диагностика' in h.t.stage:audited_curves.append(tag)
            return reader(tag)
        h.t.native.history_curve.side_effect=read_curve
        def damage():
            h.w.boxes[601]=(99.,99.,99.,100.,100.,100.)
            h.t.part.Expressions[0].RightHandSide='damaged'
            h.w.cam[(900,0)]=('changed',)
            h.t.operation_snapshot.return_value=[]
        h.after_destroy_map=damage
        h.after_undo=lambda:setattr(h.t.operation_snapshot,'return_value',operations)
        self.reject(h)
        report='\n'.join(h.t.lines)
        for text in ('положение 601','выражения','CAM-ссылки','операции'):
            self.assertIn(text,report)
        self.assertEqual(len(set(audited_curves)),2)
        self.assertIn('Локальный Undo новых кривых выполнен',report)

    def test_stock_failure_does_not_skip_independent_mcs_selection_checks(self):
        h=StockCollectorWorld();row=self.add_unknown(h);h.before_inputs=lambda:h.rows.append(row)
        world,trial,planes=report_mcs_world();attach_mcs(h.t,trial)
        saved=planes[0].sources[:]
        def damage():h.box.Clearance.Value=0.;planes[0].sources.reverse()
        h.after_destroy_map=damage;h.after_undo=lambda:setattr(planes[0],'sources',saved[:])
        self.reject(h)
        report='\n'.join(h.t.lines)
        self.assertIn('параметры защищённой заготовки 75010',report)
        self.assertIn('прямой выбор',report)
        h.box.CalculateBoxSize.assert_not_called()

    def test_model_warning_is_reported_even_with_an_independent_history_error(self):
        h=CollectorWorld();row=self.add_unknown(h);h.before_inputs=lambda:h.rows.append(row)
        feature=h.w.sketch
        def damage():feature.parents=[];feature.warnings=['Parent point modeled away']
        h.after_destroy_map=damage
        self.reject(h)
        report='\n'.join(h.t.lines)
        self.assertIn('Изменилась исходная история',report)
        self.assertIn('Новые предупреждения NX',report)
        self.assertIn('Parent point modeled away',report)

    def test_failed_editor_destroy_blocks_audit_but_owned_undo_still_runs(self):
        h=CollectorWorld();row=self.add_unknown(h);h.before_inputs=lambda:h.rows.append(row)
        h.after_destroy_map=Mock(side_effect=M.NativeApiError(7803,'Destroy','close failed'))
        with patch.object(M.HistoricalReplacementMapProbe,'diagnose_after_failure') as audit:self.reject(h)
        audit.assert_not_called();report='\n'.join(h.t.lines)
        self.assertIn('закрытие редактора карты не подтверждено',report)
        self.assertIn('7803',report);self.assertIn('Локальный Undo новых кривых выполнен',report)

    def test_lost_copy_during_row_diagnostics_stops_later_reads_and_foreign_undo(self):
        h=CollectorWorld();row=self.add_unknown(h);later=NS()
        h.before_inputs=lambda:h.rows.extend([row,later])
        original=h.t.native.reference_body.side_effect
        def owner(tag,kind):
            if tag==81001:h.t.session.Parts.Work=NS(Tag=9999);return 1000
            return original(tag,kind)
        h.t.native.reference_body.side_effect=owner
        with self.assertRaisesRegex(RuntimeError,'рабочей и отображаемой'):h.p.run()
        self.assertNotIn('AttributeError','\n'.join(h.t.lines))
        self.assertEqual(set(h.marks),{103});h.w.replace.SetMatchingForParent.assert_not_called()
        h.w.replace.Destroy.assert_not_called()

    def test_lost_copy_during_audit_stops_candidate_reads_and_undo(self):
        h=CollectorWorld();row=self.add_unknown(h);h.before_inputs=lambda:h.rows.append(row)
        def switched():h.t.session.Parts.Work=NS(Tag=9999);return []
        h.after_destroy_map=lambda:setattr(h.t.operation_snapshot,'side_effect',switched)
        with self.assertRaises(RuntimeError):h.p.run()
        report='\n'.join(h.t.lines)
        self.assertIn('Продолжение независимой диагностики остановлено',report)
        self.assertNotIn('Локальный Undo новых кривых выполнен',report)
        self.assertEqual(set(h.marks),{103});self.assertEqual(h.t.session.Parts.Work.Tag,9999)

    def test_lost_copy_during_stock_audit_prevents_builder_destroy_and_later_reads(self):
        h=StockCollectorWorld();row=self.add_unknown(h);h.before_inputs=lambda:h.rows.append(row)
        destroyed=[];operation_reads=[]
        def switch():
            destroyed.append(h.box.Destroy.call_count)
            h.t.session.Parts.Work=NS(Tag=9999)
            return True
        def arm():
            operation_reads.append(h.t.operation_snapshot.call_count)
            h.box.Validate.side_effect=switch
        h.after_destroy_map=arm
        with self.assertRaises(RuntimeError):h.p.run()
        self.assertEqual(destroyed,[h.box.Destroy.call_count])
        self.assertEqual(operation_reads,[h.t.operation_snapshot.call_count])
        self.assertEqual(set(h.marks),{103})
        self.assertNotIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))

    def test_audit_failure_cannot_turn_primary_map_error_into_success(self):
        h=CollectorWorld();row=self.add_unknown(h);h.before_inputs=lambda:h.rows.append(row)
        with patch.object(M.HistoricalReplacementMapProbe,'diagnose_after_failure',side_effect=M.NativeApiError(7804,'audit','failed')):
            error=self.reject(h,'Нет проверенного соответствия')
        report='\n'.join(h.t.lines)
        self.assertIn('7804',report);self.assertIn('Локальный Undo новых кривых выполнен',report)
        self.assertNotIsInstance(error,M.HistoricalProbeComplete)

    def test_successful_map_does_not_run_failure_audit(self):
        h=CollectorWorld()
        with patch.object(M.HistoricalReplacementMapProbe,'diagnose_after_failure') as audit:
            with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        audit.assert_not_called();h.w.replace.Commit.assert_not_called()


if __name__=='__main__':unittest.main()
