"""Batch MCS choices, isolated capture sources, and additional card sheets."""
from contextlib import ExitStack
import copy
import ctypes as c
import os
from pathlib import Path
import tempfile
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock, patch

import test_setup_card_choices as native
CARD = native.CARD
IDENTITY = ((1., 0., 0.), (0., 1., 0.), (0., 0., 1.))
ROTATED = ((0., 1., 0.), (-1., 0., 0.), (0., 0., 1.))


def fixture():
    a, b, d = [NS(Tag=i, Name=n) for i, n in ((1, '1'), (2, '1S'), (3, 'MCS_2'))]
    ops = [NS(Tag=10+i, Name='OP_%d' % i, mcs=g) for i, g in enumerate((a, b, a, d))]
    contexts = [dict(mcs=a, mcs_groups=[a, b], operations=ops[:3], source='selected'),
                dict(mcs=d, mcs_groups=[d], operations=ops[3:], source='selected')]
    jobs = [dict(context=context, report=dict(CARD.new_report(), setup_name='SETUP_%d' % i,
                 selected_folder_path=['PROGRAM', 'SETUP_%d' % i]), output=None)
            for i, context in enumerate(contexts, 1)]
    return (a, b, d), ops, jobs


def tiny_image(path, color=70):
    width = height = 80
    rows = [b''.join(bytes((color, 100, 140)) if 20 <= x < 60 and 20 <= y < 60
                     else b'\xff\xff\xff' for x in range(width)) for y in range(height)]
    path.write_bytes(CARD.png_rgb_bytes(width, height, rows))
    return width, height


class MultiMcsTests(unittest.TestCase):
    def test_independent_checks_and_every_setup_required(self):
        groups, _, jobs = fixture()
        rows, selected = CARD.setup_mcs_rows(jobs)
        model = CARD.SetupMcsChoices(rows, selected)
        self.assertEqual(model.values(), [[groups[0]], [groups[2]]])
        self.assertTrue(model.complete())
        model.toggle('setup-1:2', True)
        model.toggle('setup-1:1', False)
        self.assertEqual(model.values(), [[groups[1]], [groups[2]]])
        self.assertFalse(model.toggle('setup-1', True))
        model.toggle('setup-2:3', False)
        self.assertFalse(model.complete())
        self.assertNotIn('setup-1', model.selected)

    def test_same_number_s_branches_offer_multi_choice_and_cancel_safely(self):
        groups, _, _ = fixture()
        for answer, expected in ((True, None), ('multiple', 'multiple')):
            with self.subTest(answer=answer), patch.object(CARD, 'ask_warning', return_value=answer) as ask:
                self.assertEqual(CARD.confirm_mixed_mcs(None, groups[:2], groups[0], {}), expected)
                self.assertTrue(ask.call_args.kwargs['allow_multiple_mcs'])
        for answer in (False, None):
            with patch.object(CARD, 'ask_warning', return_value=answer):
                with self.assertRaises(CARD.ExportCancelled):
                    CARD.confirm_mixed_mcs(None, groups[:2], groups[0], {})

    def test_batch_dialog_includes_single_mcs_setups_and_can_exclude_primary(self):
        groups, ops, jobs = fixture()
        with patch.object(CARD, 'ask_warning', return_value='multiple'), \
                patch.object(CARD, 'ask_setup_mcs', return_value=[[groups[1]], [groups[2]]]) as choose, \
                patch.object(CARD, 'nearest_mcs', side_effect=lambda nx, op: op.mcs):
            CARD.choose_setup_mcs(None, jobs)
        self.assertIs(choose.call_args.args[1], jobs)
        self.assertEqual(jobs[0]['context']['capture_mcs'], [groups[1]])
        self.assertEqual(jobs[0]['context']['view_contexts'][0]['operations'], [ops[1]])
        self.assertEqual(jobs[0]['context']['operations'], ops[:3])
        self.assertEqual(jobs[0]['report']['setup_name'], 'SETUP_1')

    def test_yes_keeps_first_mcs_and_only_its_selected_operations(self):
        groups, ops, jobs = fixture()
        with patch.object(CARD, 'ask_warning', return_value=True), \
                patch.object(CARD, 'ask_setup_mcs') as chooser, \
                patch.object(CARD, 'nearest_mcs', side_effect=lambda nx, op: op.mcs):
            CARD.choose_setup_mcs(None, jobs)
        chooser.assert_not_called()
        source = jobs[0]['context']['view_contexts'][0]['ipw_source']
        self.assertEqual(source['operations'], [ops[0], ops[2]])
        self.assertEqual(jobs[0]['context']['view_contexts'][0]['capture_file_prefix'], '')

    def test_cancel_choice_unlocks_nx_and_never_applies_partial_selection(self):
        _, _, jobs = fixture()
        ui = NS(LockUgAccess=Mock(), UnlockUgAccess=Mock())
        nx = NS(UF=NS(UFSession=NS(GetUFSession=lambda: NS(Ui=ui)),
                      UFConstants=NS(UF_UI_FROM_CUSTOM=7)))
        with patch.object(CARD, 'show_setup_folders', return_value=None):
            with self.assertRaises(CARD.ExportCancelled):
                CARD.ask_setup_mcs(nx, jobs)
        ui.LockUgAccess.assert_called_once_with(7)
        ui.UnlockUgAccess.assert_called_once_with(7)
        self.assertNotIn('capture_mcs', jobs[0]['context'])

    def test_duplicate_names_remain_separate_and_filenames_survive_new_session_tags(self):
        groups, ops, jobs = fixture()
        a, b, _ = groups
        b.Name = a.Name
        context = jobs[0]['context']
        context['capture_mcs'] = [a, b]
        with patch.object(CARD, 'nearest_mcs', side_effect=lambda nx, op: op.mcs):
            first = CARD.setup_view_contexts(None, context)
            a.Tag, b.Tag = 501, 502
            second = CARD.setup_view_contexts(None, context)
        self.assertEqual([v['capture_file_prefix'] for v in first], [v['capture_file_prefix'] for v in second])
        self.assertEqual(len({v['capture_mcs_key'] for v in second}), 2)
        rows, _ = CARD.setup_mcs_rows(jobs)
        self.assertIn('[ID 501]', rows[1]['name'])
        self.assertIn('[ID 502]', rows[2]['name'])

    def test_actual_capture_loop_keeps_pairs_sources_orientation_and_unique_assets(self):
        groups, ops, jobs = fixture()
        context = jobs[0]['context']
        context['capture_mcs'] = groups[:2]
        facet = NS(Tag=40, IsBlanked=False, OwningPart=NS(Layers=NS(WorkLayer=1)))
        facet.Blank = lambda: setattr(facet, 'IsBlanked', True)
        facet.Unblank = lambda: setattr(facet, 'IsBlanked', False)
        cam = NS(DisplayIpwWithPickOnPath=Mock(), DeleteIpwWithPickOnPath=Mock(),
                 CreateFacetBodyForIpwDisplay=lambda: facet)
        part = NS(CAMSetup=cam, Views=NS(Refresh=Mock()))
        session = NS(SetUndoMark=Mock(return_value=9), UndoToMark=Mock(), DeleteUndoMark=Mock())
        nx = NS(Session=NS(GetSession=lambda: session, MarkVisibility=NS(Invisible=0)),
                XYZAxis=NS(XAxis=0, YAxis=1, ZAxis=2))
        view = NS(SyncViews=True, LockRotations=True, GetAxis=lambda i: NS(X=IDENTITY[i][0], Y=IDENTITY[i][1], Z=IDENTITY[i][2]))
        report = CARD.new_report()
        display = NS(hide=Mock(), hide_after_camera_change=Mock(), check_cam_hidden=Mock(), restore=Mock(return_value=[]))
        with tempfile.TemporaryDirectory() as folder, ExitStack() as stack:
            output = Path(folder)
            patches = dict(nearest_mcs=lambda nx, op: op.mcs,
                activate_first_mcs=Mock(),
                read_mcs=lambda part, group: (IDENTITY if group is groups[0] else ROTATED, (group.Tag, 0., 0.)),
                ScreenshotDisplay=Mock(return_value=display),
                snapshot_view=lambda view: dict(scale=1.),
                read_view_projection=lambda nx, view: dict(type=1),
                ensure_capture_projection=Mock(), verify_capture_camera=lambda view, expected: expected,
                orient_and_verify=Mock(return_value='rows'), fit_visible_view=Mock(),
                restore_view=Mock(return_value=[]), capture_mcs_anchor=Mock(return_value=dict(status='unavailable')),
                export_png=lambda nx, part, path: tiny_image(path))
            for name, value in patches.items():
                stack.enter_context(patch.object(CARD, name, value))
            CARD.capture_setup_views(nx, part, view, output, report, context)
            self.assertEqual(len(report['views']), 6)
            self.assertEqual(len(list(output.glob('*.png'))), 12)
            self.assertEqual(len({v['file'] for v in report['views']}), 6)
            self.assertEqual([v['mcs_name'] for v in report['views']], ['1'] * 3 + ['1S'] * 3)
            self.assertEqual(report['views'][3]['mcs_axes_absolute'], ROTATED)
            actual = [call.args[0] for call in cam.DisplayIpwWithPickOnPath.call_args_list]
            self.assertEqual(actual, [[ops[0], ops[2]]] * 4 + [[ops[1]]] * 4)
            self.assertEqual(session.UndoToMark.call_count, 2)
            self.assertEqual(display.restore.call_count, 2)
            orientations = patches['orient_and_verify'].call_args_list
            self.assertEqual(orientations[3].args[2], CARD.camera_axes(ROTATED, *CARD.VIEW_PRESETS[0][2:]))
            for item in report['views']:
                self.assertTrue((output / item['file']).is_file())
                self.assertTrue((output / item['no_ipw_file']).is_file())
            before = [(v['file'], v['no_ipw_file']) for v in report['views']]
            html = CARD.gallery_html(output, report) + ''.join(CARD.additional_mcs_pages(output, report))
            self.assertEqual(html.count('class="mcs-name"'), 6)
            self.assertEqual(html.count('mcs-view-page'), 1)
            self.assertEqual(html.count('data-paired="true"'), 6)
            self.assertEqual(before, [(v['file'], v['no_ipw_file']) for v in report['views']])

    def test_capture_failure_stops_remaining_mcs_and_marks_setup_failed(self):
        groups, _, jobs = fixture()
        context = jobs[0]['context']
        context['capture_mcs'] = groups[:2]
        report = CARD.new_report()
        with patch.object(CARD, 'nearest_mcs', side_effect=lambda nx, op: op.mcs), \
                patch.object(CARD, 'activate_first_mcs'), \
                patch.object(CARD, 'read_mcs', return_value=(IDENTITY, (0, 0, 0))), \
                patch.object(CARD, 'capture_views', side_effect=RuntimeError('capture test')) as capture:
            with self.assertRaisesRegex(RuntimeError, 'MCS «1»'):
                CARD.capture_setup_views(None, None, None, Path('.'), report, context)
        self.assertTrue(report['capture_failed'])
        self.assertEqual(capture.call_count, 1)
        self.assertEqual(report['views'], [])

    def test_per_image_axes_override_setup_axes_and_names_are_escaped(self):
        item = dict(label='Вид', mcs_axes_absolute=ROTATED, camera_axes_absolute=IDENTITY)
        self.assertEqual(CARD.projected_mcs_axes(item, dict(mcs_axes_absolute=IDENTITY)),
                         [(0., -1., 0.), (-1., 0., 0.), (0., 0., 1.)])
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder)
            tiny_image(output / 'test.png')
            item.update(file='test.png', mcs_name='MCS <A> & "B"')
            markup = CARD.image_slot(item, output, 'top-photo', {})
            self.assertIn('MCS &lt;A&gt; &amp; &quot;B&quot;', markup)
            self.assertNotIn('MCS <A>', markup)

    @unittest.skipUnless(os.name == 'nt', 'requires native Windows dialog')
    def test_native_mcs_tree_initial_checks_toggle_all_and_validation(self):
        harness = native.NativeChoiceTests()
        harness.setUp()
        groups, _, jobs = fixture()
        rows, selected = CARD.setup_mcs_rows(jobs)
        try:
            def check(parent):
                u = harness.u
                tree = u.GetDlgItem(parent, 101)
                u.ShowWindow(parent, 5)
                u.UpdateWindow(parent)
                first_setup = u.SendMessageW(tree, 0x110A, 0, 0)
                first = u.SendMessageW(tree, 0x110A, 4, first_setup)
                second = u.SendMessageW(tree, 0x110A, 1, first)
                second_setup = u.SendMessageW(tree, 0x110A, 1, first_setup)
                third = u.SendMessageW(tree, 0x110A, 4, second_setup)
                def state(handle):
                    item = harness.drawers[0].Item(mask=8, hItem=handle, stateMask=0xf000)
                    self.assertTrue(u.SendMessageW(tree, 0x113E, 0, c.addressof(item)))
                    return item.state & 0xf000
                self.assertEqual([state(h) for h in (first_setup, first, second, second_setup, third)],
                                 [0, 0x2000, 0x1000, 0, 0x2000])
                self.assertTrue(u.IsWindowEnabled(u.GetDlgItem(parent, 1)))
                harness.click(tree, harness.point(tree, first, True, 0x40))
                self.assertFalse(u.IsWindowEnabled(u.GetDlgItem(parent, 1)))
                harness.click(tree, harness.point(tree, second, True, 0x40))
                self.assertTrue(u.IsWindowEnabled(u.GetDlgItem(parent, 1)))
                u.SendMessageW(parent, 0x111, 222, 0)
                self.assertFalse(u.IsWindowEnabled(u.GetDlgItem(parent, 1)))
                u.SendMessageW(parent, 0x111, 221, 0)
                self.assertTrue(u.IsWindowEnabled(u.GetDlgItem(parent, 1)))
                self.assertEqual([state(h) for h in (first, second, third)], [0x2000] * 3)
            result = harness.dialog(lambda: CARD.show_setup_folders(rows, selected=selected, mcs_mode=True), check)
            self.assertEqual(result, [list(groups[:2]), [groups[2]]])
        finally:
            harness.tearDown()

    @unittest.skipUnless(os.name == 'nt', 'requires native Windows dialog')
    def test_native_warning_extra_button_returns_multiple(self):
        harness = native.NativeChoiceTests()
        harness.setUp()
        try:
            def check(parent):
                self.assertTrue(harness.u.GetDlgItem(parent, 4))
                self.assertTrue(harness.u.IsWindowEnabled(harness.u.GetDlgItem(parent, 4)))
            result = harness.dialog(lambda: CARD.show_warning_dialog(
                'В установе присутствует несколько MCS.', allow_multiple_mcs=True), check, result_code=4)
            self.assertEqual(result, 'multiple')
        finally:
            harness.tearDown()


if __name__ == '__main__':
    unittest.main()
