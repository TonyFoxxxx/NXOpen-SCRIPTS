"""Abort on the real V1.30.08 error code; fixtures do not model NX resources."""
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock, patch

import test_mirror_project as project
from test_mirror_historical_probe import M, HistoryWorld
from test_mirror_map_inputs import CollectorWorld
from test_mirror_replacement_probe import Edge


def exhausted():
    return M.NativeApiError(821049, 'UF_MODL_ask_bounding_box_exact', 'Tag limit would be exceeded')


class ResourceRecognitionTests(unittest.TestCase):
    def test_native_and_nxopen_codes_are_recognized(self):
        class NxError(RuntimeError): ErrorCode = 821049
        for error in (exhausted(), NxError('localized message')):
            with self.subTest(error=error):
                self.assertIs(M.nx_resource_failure(error), error)

    def test_messages_strings_and_other_error_codes_are_not_resource_evidence(self):
        for code in (True, '821049', 821048, None):
            error = RuntimeError('821049 Tag limit would be exceeded')
            error.code = code
            self.assertIsNone(M.nx_resource_failure(error))

    def test_chained_resource_error_survives_cleanup_error_and_cycles(self):
        failure = exhausted(); cleanup = RuntimeError('cleanup failed')
        cleanup.__context__ = failure; failure.__context__ = cleanup
        self.assertIs(M.nx_resource_failure(cleanup), failure)
        a, b = RuntimeError('a'), RuntimeError('b')
        a.__cause__ = b; b.__cause__ = a
        self.assertIsNone(M.nx_resource_failure(a))

    def test_unreadable_code_does_not_mask_original_exception(self):
        class NxError(RuntimeError):
            @property
            def ErrorCode(self): raise RuntimeError('descriptor unavailable')
        error = NxError('failure'); error.code = 821049
        self.assertIs(M.nx_resource_failure(error), error)

    def test_batch_aborts_and_latches_before_another_read_or_guard(self):
        t = NS(lines=[], stage='before'); guard = Mock(); error = exhausted()
        batch = M.ProbeChecks(t, guard, 'audit')
        with self.assertRaises(M.NativeApiError) as caught:
            batch.run('face', Mock(side_effect=error))
        self.assertIs(caught.exception, error)
        later = Mock()
        with self.assertRaises(M.NativeApiError): batch.run('next', later)
        later.assert_not_called(); self.assertEqual(guard.call_count, 1)
        self.assertEqual(batch.passed, 0); self.assertEqual(len(batch.errors), 1)
        self.assertIn('821049', '\n'.join(t.lines)); self.assertIn('Traceback', '\n'.join(t.lines))


class ResourceHistoryTests(unittest.TestCase):
    def test_resource_failure_before_first_context_creates_no_local_mark(self):
        h = HistoryWorld(); h.t.bounding_box.side_effect = exhausted()
        with self.assertRaises(M.NativeApiError): h.p.run()
        self.assertEqual(h.events, [])
        h.t.session.UndoToMark.assert_not_called()
        h.assert_no_geometry_edits(self)

    def test_three_contexts_keep_both_post_mutation_checks_without_duplicate_prechecks(self):
        h = HistoryWorld(); phases = []; verify = M.HistoricalReferenceProbe.verify_restored
        def observed(probe, phase):
            phases.append(phase)
            return verify(probe, phase)
        with patch.object(M.HistoricalReferenceProbe, 'verify_restored', observed), \
                patch.object(M.HistoricalCurveCandidateProbe, 'run', side_effect=RuntimeError('history boundary')):
            with self.assertRaisesRegex(RuntimeError, 'history boundary'): h.p.run()
        self.assertEqual(len(phases), 7)
        self.assertEqual(phases[0], 'перед первым контекстом')
        self.assertEqual(sum(p.startswith('Stop / ') for p in phases), 3)
        self.assertEqual(sum(p.startswith('локальный Undo / ') for p in phases), 3)
        self.assertEqual(h.marks, {})
        h.assert_no_geometry_edits(self)

    def test_resource_read_stops_before_other_contexts_but_attempts_owned_cleanup(self):
        h = HistoryWorld(); error = exhausted()
        h.t.native.history_bounding_box.side_effect = error
        with self.assertRaises(M.NativeApiError) as caught: h.p.run()
        self.assertIs(caught.exception, error)
        self.assertEqual([e[0] for e in h.events], ['mark','start','update','stop','destroy','undo'])
        h.t.native.history_bounding_box.assert_called_once()
        h.t.session.DeleteUndoMark.assert_not_called()
        self.assertEqual(set(h.marks), {100})
        self.assertIn('восстановление модели не подтверждено', '\n'.join(h.t.lines))
        self.assertNotIn('Локальный Undo выполнен; возврат', '\n'.join(h.t.lines))
        h.assert_no_geometry_edits(self)

    def test_resource_error_in_manager_stop_prevents_post_undo_geometry_reads(self):
        h = HistoryWorld(); h.fail['stop'] = exhausted()
        with patch.object(M.HistoricalReferenceProbe, 'verify_restored') as verify:
            # Only the initial check may be entered. Restoration checks after
            # the resource error are blocked before querying the model.
            with self.assertRaises(M.NativeApiError): h.p.run()
        self.assertEqual(verify.call_count, 1)
        self.assertEqual([e[0] for e in h.events], ['mark','start','update','stop','destroy','undo'])
        h.assert_no_geometry_edits(self)

    def test_resource_error_in_post_stop_box_keeps_all_other_restoration_guards(self):
        h = HistoryWorld(); saved = h.t.bounding_box.side_effect; reads_after_error = []
        error = exhausted(); failed = False
        def box(tag):
            nonlocal failed
            if failed: reads_after_error.append(tag)
            if h.events and h.events[-1][0] == 'destroy':
                failed = True
                raise error
            return saved(tag)
        h.t.bounding_box.side_effect = box
        with self.assertRaises(M.NativeApiError) as caught: h.p.run()
        self.assertIs(caught.exception, error); self.assertEqual(reads_after_error, [])
        self.assertEqual(sum(e[0] == 'start' for e in h.events), 1)
        self.assertIn('точный габарит', h.t.stage)
        self.assertEqual(sum(e[0] == 'undo' for e in h.events), 1)

    def test_resource_error_with_lost_copy_never_operates_on_foreign_part(self):
        h = HistoryWorld()
        def read(tag):
            h.t.session.Parts.Work = NS(Tag=123)
            raise exhausted()
        h.t.native.history_bounding_box.side_effect = read
        with self.assertRaises(M.NativeApiError): h.p.run()
        self.assertEqual([e[0] for e in h.events], ['mark','start'])
        h.t.session.UndoToMark.assert_not_called()
        h.assert_no_geometry_edits(self)


class ResourceMapTests(unittest.TestCase):
    def unknown(self, h, tag):
        return h.row(h.w.register(Edge(tag, h.w.body)), h.t.nx.Features.MatchedReferenceBuilder.Types.Curve)

    def test_fatal_owner_read_blocks_later_rows_audit_and_second_builder(self):
        h = CollectorWorld(); rows = [self.unknown(h, 81001), self.unknown(h, 81002)]
        h.before_inputs = lambda: h.rows.extend(rows)
        owner = h.t.native.reference_body.side_effect
        def read(tag, kind):
            if tag == 81001: raise exhausted()
            return owner(tag, kind)
        h.t.native.reference_body.side_effect = read
        with patch.object(M.HistoricalReplacementMapProbe, 'diagnose_after_failure') as audit:
            with self.assertRaises(M.NativeApiError): h.p.run()
        audit.assert_not_called(); self.assertEqual(len(h.map_builders), 1)
        self.assertNotIn('Исходная строка карты: tag=81002', '\n'.join(h.t.lines))
        h.w.replace.SetMatchingForParent.assert_not_called(); h.w.replace.Commit.assert_not_called()
        self.assertEqual(sum(e[0] == 'undo' for e in h.events), 4)
        self.assertEqual(set(h.marks), {103})

    def test_fatal_audit_stops_later_checks_and_provenance_but_preserves_first_map_error(self):
        h = CollectorWorld(); row = self.unknown(h, 81001)
        h.before_inputs = lambda: h.rows.append(row)
        before = h.t.bounding_box.side_effect; later = []
        def box(tag):
            if 'Независимая диагностика' in h.t.stage: raise exhausted()
            if getattr(h.t, 'resource_failure', None) is not None: later.append(tag)
            return before(tag)
        h.t.bounding_box.side_effect = box
        with patch.object(M.ReferenceProvenanceProbe, 'run') as provenance:
            with self.assertRaises(RuntimeError): h.p.run()
        provenance.assert_not_called(); self.assertEqual(later, [])
        self.assertEqual(len(h.map_builders), 1)
        report = '\n'.join(h.t.lines)
        self.assertIn('821049', report); self.assertIn('Edge 81001', report)
        self.assertNotIn('CAM-ссылки, границы и точки', report)
        self.assertNotIn('Локальный Undo новых кривых выполнен', report)
        h.w.replace.Commit.assert_not_called()

    def test_new_diagnostic_editor_cannot_open_in_latched_session(self):
        h = CollectorWorld(); h.t.resource_failure = exhausted()
        probe = object.__new__(M.HistoricalReplacementMapProbe); probe.t = h.t
        with self.assertRaises(M.NativeApiError): probe.inspect_unique_view(None)
        h.t.part.Features.CreateReplaceFeatureBuilder.assert_not_called()

    def test_resource_failure_in_separate_view_prevents_post_view_audit(self):
        h = CollectorWorld(); row = self.unknown(h, 81001)
        h.before_inputs = lambda: h.rows.append(row)
        h.after_unique = Mock(side_effect=exhausted())
        calls = []; original = M.HistoricalReplacementMapProbe.diagnose_after_failure
        def audit(probe):
            calls.append(len(h.map_builders))
            return original(probe)
        with patch.object(M.HistoricalReplacementMapProbe, 'diagnose_after_failure', audit):
            with self.assertRaises(RuntimeError): h.p.run()
        self.assertEqual(calls, [1,1]); self.assertEqual(len(h.map_builders), 2)
        for builder in h.map_builders:
            builder.Destroy.assert_called_once(); builder.Commit.assert_not_called()
        self.assertIn('Проверки после отдельного просмотра остановлены из-за 821049', '\n'.join(h.t.lines))
        self.assertNotIn('Локальный Undo новых кривых выполнен', '\n'.join(h.t.lines))

    def test_resource_failure_destroying_map_prevents_audit_and_verified_undo_claim(self):
        h = CollectorWorld(); row = self.unknown(h, 81001)
        h.before_inputs = lambda: h.rows.append(row)
        h.after_destroy_map = Mock(side_effect=exhausted())
        with patch.object(M.HistoricalReplacementMapProbe, 'diagnose_after_failure') as audit:
            with self.assertRaises(RuntimeError): h.p.run()
        audit.assert_not_called(); self.assertEqual(len(h.map_builders), 1)
        self.assertEqual(sum(e[0] == 'undo' for e in h.events), 4)
        self.assertNotIn('Локальный Undo новых кривых выполнен', '\n'.join(h.t.lines))


class ResourceReportTests(unittest.TestCase):
    def test_exact_box_failure_identifies_object_csys_and_call_number_without_retry(self):
        native = project.NativeModellingTests().reader(); native.exact_box = Mock(return_value=821049)
        t = object.__new__(M.Trial)
        t.refs, t.plane, t.box_csys, t.native = {}, project.axis_plane(), 99, native
        t.lines, t.stage, t.exact_box_calls = [], 'restoration', 6
        with self.assertRaises(project.M.NativeApiError) as caught: t.bounding_box(1234)
        self.assertEqual(caught.exception.code, 821049)
        self.assertEqual(caught.exception.context, 'UF_MODL_ask_bounding_box_exact')
        for text in ('restoration','объект=1234','CSYS=99','вызов в этом запуске=7'):
            self.assertIn(text, t.stage)
        native.exact_box.assert_called_once()
        self.assertIs(t.resource_failure, caught.exception)
        with self.assertRaises(project.M.NativeApiError): t.bounding_box(5678)
        native.exact_box.assert_called_once()

    def test_wcs_cleanup_failure_does_not_hide_original_resource_error(self):
        t, _, _ = project.WcsTests().trial(False); t.lines = []
        error = exhausted(); t.run_in_absolute_wcs = Mock(side_effect=error)
        with patch.object(project.M, 'set_wcs_values', side_effect=[None, M.NativeApiError(999, 'WCS', 'restore failed')]):
            with self.assertRaises(M.NativeApiError) as caught: t.run()
        self.assertIs(caught.exception, error)
        report = '\n'.join(t.lines)
        self.assertIn('999', report); self.assertIn('restore failed', report); self.assertIn('Traceback', report)

    def test_successful_undo_api_after_tag_limit_does_not_claim_verified_recovery(self):
        events, report = project.TransactionTests().run_with_trial(exhausted())
        self.assertIn(('undo',55), events)
        self.assertIn('Команда общего Undo вернулась без ошибки', report)
        self.assertNotIn('Изменения теста в копии отменены через Undo', report)
        self.assertIn('не повторяйте тест в этом сеансе', report)
        self.assertIn('контрольная сумма не изменилась', report)

    def test_failed_undo_and_close_preserve_all_failures_and_source_disk_check(self):
        events, report = project.TransactionTests().run_with_trial(exhausted(), rollback_fails=True, close_fails=True)
        for text in ('821049','undo failed','close failed','не удалось','контрольная сумма не изменилась'):
            self.assertIn(text.lower(), report.lower())
        self.assertIn('не повторяйте тест в этом сеансе', report)
        self.assertNotIn('Созданная тестовая копия закрыта без сохранения', report)
        self.assertIn(('undo',55), events)
        self.assertEqual(sum(isinstance(e,tuple) and e[0]=='close' for e in events), 1)


if __name__ == '__main__':
    unittest.main()
