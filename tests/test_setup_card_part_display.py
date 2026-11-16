"""Capture the current Workpiece's real bodies and restore original visibility."""
from pathlib import Path
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock, patch

from test_setup_card_geometry import CARD, Feature, fixture as geometry_fixture, context


class Body:
    def __init__(self, tag, part, blanked=False, occurrence=False):
        self.Tag, self.OwningPart = tag, part
        self.IsBlanked, self.IsOccurrence = blanked, occurrence
        self.Blank = Mock(side_effect=lambda: setattr(self, 'IsBlanked', True))
        self.Unblank = Mock(side_effect=lambda: setattr(self, 'IsBlanked', False))


class Face:
    def __init__(self, body):
        self.GetBody = lambda: body


class Edge(Face):
    pass


def fixture():
    f = geometry_fixture()
    f.nx.Body, f.nx.Face, f.nx.Edge = Body, Face, Edge
    f.part.Views = NS(Refresh=Mock())
    f.nx.UF = NS(UFSession=NS(GetUFSession=lambda: NS(Disp=NS(MakeDisplayUpToDate=Mock()))))
    f.source_body, f.mirror_body = Body(1000, f.part), Body(1001, f.part, blanked=True)
    f.fixture_body, f.blank_body = Body(1002, f.part), Body(1003, f.part, blanked=True)
    f.original.items, f.mirrored.items = [f.source_body], [f.mirror_body]
    f.builders, f.reads = [], []

    def builder(group):
        b = NS(Destroy=Mock(), Commit=Mock(side_effect=AssertionError('read only')))
        f.builders.append(b)
        if group.volume:
            def contents():
                f.reads.append(group.Tag)
                return [NS(GetItems=lambda: list(group.items))]
            b.PartGeometry = NS(GeometryList=NS(GetContents=contents))
            b.BlankGeometry = NS(GeometryList=NS(GetContents=Mock(side_effect=AssertionError('not part geometry'))))
        return b
    f.groups.CreateMillGeomBuilder = builder
    return f


class PartDisplayTests(unittest.TestCase):
    def guard(self, f, selected):
        return CARD.SetupPartDisplay(f.nx, f.part, context(f, selected), CARD.new_report())

    def test_hidden_mirror_body_is_shown_and_original_is_restored_afterwards(self):
        f = fixture()
        guard = self.guard(f, f.second)
        guard.apply()
        self.assertTrue(f.source_body.IsBlanked)
        self.assertFalse(f.mirror_body.IsBlanked)
        self.assertFalse(f.fixture_body.IsBlanked)
        self.assertTrue(f.blank_body.IsBlanked)
        self.assertEqual(guard.restore(), [])
        self.assertFalse(f.source_body.IsBlanked)
        self.assertTrue(f.mirror_body.IsBlanked)
        for untouched in (f.fixture_body, f.blank_body):
            untouched.Blank.assert_not_called()
            untouched.Unblank.assert_not_called()
        for builder in f.builders:
            builder.Destroy.assert_called_once()
            builder.Commit.assert_not_called()

    def test_shared_part_body_is_not_hidden_when_another_workpiece_also_uses_it(self):
        f = fixture()
        f.mirrored.items = [f.source_body]
        guard = self.guard(f, f.second)
        guard.apply()
        self.assertFalse(f.source_body.IsBlanked)
        f.source_body.Blank.assert_not_called()
        self.assertEqual(guard.restore(), [])

    def test_face_and_edge_selections_resolve_all_bodies_without_duplicate_changes(self):
        f = fixture()
        extra = Body(1004, f.part, blanked=True)
        f.mirrored.items = [Face(f.mirror_body), Edge(f.mirror_body), f.mirror_body, Face(extra)]
        guard = self.guard(f, f.second)
        guard.apply()
        self.assertFalse(extra.IsBlanked)
        f.mirror_body.Unblank.assert_called_once()
        self.assertEqual(guard.restore(), [])
        self.assertTrue(extra.IsBlanked)

    def test_empty_local_part_geometry_inherits_from_enclosing_workpiece(self):
        f = fixture()
        outer = Feature(60, 'OUTER', [f.mirrored])
        outer.items, f.mirrored.items = [f.mirror_body], []
        f.geometry.members = [f.original, outer]
        f.groups.append(outer)
        guard = self.guard(f, f.second)
        guard.apply()
        self.assertFalse(f.mirror_body.IsBlanked)
        self.assertTrue(f.source_body.IsBlanked)
        self.assertEqual(guard.restore(), [])

    def test_missing_part_geometry_stops_before_changing_visibility(self):
        f = fixture()
        f.mirrored.items = []
        guard = self.guard(f, f.second)
        with self.assertRaisesRegex(RuntimeError, 'WORKPIECE_COPY.*Part Geometry'):
            guard.apply()
        self.assertEqual(guard.restore(), [])
        f.source_body.Blank.assert_not_called()
        f.mirror_body.Unblank.assert_not_called()

    def test_foreign_prototype_and_unknown_selections_are_rejected_before_changes(self):
        for item in (Body(1005, NS(Tag=999)), object()):
            with self.subTest(item=type(item).__name__):
                f = fixture()
                f.mirrored.items = [item]
                guard = self.guard(f, f.second)
                with self.assertRaisesRegex(RuntimeError, 'Part Geometry'):
                    guard.apply()
                self.assertEqual(guard.restore(), [])
                f.source_body.Blank.assert_not_called()
                for builder in f.builders:
                    builder.Destroy.assert_called_once()

    def test_occurrence_visibility_changes_do_not_modify_prototype_or_component(self):
        f = fixture()
        prototype = Body(1005, NS(Tag=999), blanked=True)
        occurrence = Body(1006, prototype.OwningPart, blanked=True, occurrence=True)
        occurrence.Prototype = prototype
        occurrence.OwningComponent = NS(IsBlanked=True, Blank=Mock(), Unblank=Mock())
        f.mirrored.items = [occurrence]
        guard = self.guard(f, f.second)
        guard.apply()
        self.assertFalse(occurrence.IsBlanked)
        self.assertTrue(occurrence.OwningComponent.IsBlanked)
        occurrence.OwningComponent.Unblank.assert_not_called()
        prototype.Unblank.assert_not_called()
        self.assertEqual(guard.restore(), [])
        self.assertTrue(occurrence.IsBlanked)

    def test_partial_visibility_failure_restores_even_the_failing_body(self):
        f = fixture()
        def partial_failure():
            f.mirror_body.IsBlanked = False
            raise RuntimeError('injected partial unblank')
        f.mirror_body.Unblank.side_effect = partial_failure
        guard = self.guard(f, f.second)
        try:
            with self.assertRaisesRegex(RuntimeError, 'injected partial'):
                guard.apply()
        finally:
            self.assertEqual(guard.restore(), [])
        self.assertFalse(f.source_body.IsBlanked)
        self.assertTrue(f.mirror_body.IsBlanked)

    def test_cache_reuses_membership_but_visibility_is_recorded_again_for_each_setup(self):
        f = fixture()
        contexts = [context(f, f.first), context(f, f.second)]
        shared = {}
        for ctx in contexts:
            ctx['_part_geometry_cache'] = shared
            guard = CARD.SetupPartDisplay(f.nx, f.part, ctx, CARD.new_report())
            guard.apply()
            self.assertEqual(guard.restore(), [])
        self.assertEqual(f.reads, [f.original.Tag, f.mirrored.Tag])
        self.assertFalse(f.source_body.IsBlanked)
        self.assertTrue(f.mirror_body.IsBlanked)
        f.source_body.IsBlanked = True
        guard = self.guard(f, f.first)
        guard.apply()
        self.assertEqual(guard.restore(), [])
        self.assertTrue(f.source_body.IsBlanked)
        self.assertEqual(len(f.reads), 4)

    def test_capture_loop_switches_bodies_per_mcs_and_restores_on_capture_failure(self):
        for failure in (False, True):
            with self.subTest(failure=failure):
                f = fixture()
                ctx = context(f, f.program)
                ctx['capture_mcs'] = ctx['mcs_groups']
                ctx['_part_geometry_cache'] = {}
                seen = []
                def capture(nx, part, view, basis, output, report, ipw_context):
                    seen.append((ipw_context['mcs'].Name, f.source_body.IsBlanked, f.mirror_body.IsBlanked))
                    if failure and ipw_context['mcs'] is f.mirror:
                        raise RuntimeError('injected screenshot failure')
                report = CARD.new_report()
                with patch.object(CARD, 'activate_first_mcs'), \
                        patch.object(CARD, 'read_mcs', return_value=(((1, 0, 0), (0, 1, 0), (0, 0, 1)), (0, 0, 0))), \
                        patch.object(CARD, 'capture_views', side_effect=capture):
                    if failure:
                        with self.assertRaisesRegex(RuntimeError, 'injected screenshot failure'):
                            CARD.capture_setup_views(f.nx, f.part, None, Path('.'), report, ctx)
                        self.assertTrue(report['capture_failed'])
                    else:
                        CARD.capture_setup_views(f.nx, f.part, None, Path('.'), report, ctx)
                self.assertEqual(seen, [('MCS_SOURCE', False, True), ('MCS_MIRROR', True, False)])
                self.assertFalse(f.source_body.IsBlanked)
                self.assertTrue(f.mirror_body.IsBlanked)
                self.assertTrue(all(r['part_geometry_display']['restored'] for r in report['mcs_captures']))

    def test_restore_failure_stops_further_captures_and_retains_original_error(self):
        f = fixture()
        ctx = context(f, f.program)
        ctx['capture_mcs'] = [f.mirror, f.source]
        def capture(*args, **kwargs):
            f.source_body.Unblank.side_effect = RuntimeError('injected restoration failure')
            raise RuntimeError('injected screenshot failure')
        report = CARD.new_report()
        with patch.object(CARD, 'activate_first_mcs'), \
                patch.object(CARD, 'read_mcs', return_value=(((1, 0, 0), (0, 1, 0), (0, 0, 1)), (0, 0, 0))), \
                patch.object(CARD, 'capture_views', side_effect=capture) as save:
            with self.assertRaisesRegex(RuntimeError, r'screenshot failure[\s\S]*restoration failure'):
                CARD.capture_setup_views(f.nx, f.part, None, Path('.'), report, ctx)
        self.assertEqual(save.call_count, 1)
        self.assertTrue(report['capture_failed'])
        self.assertTrue(report['restore_errors'])
        self.assertFalse(report['mcs_captures'][0]['part_geometry_display']['restored'])


if __name__ == '__main__':
    unittest.main()
