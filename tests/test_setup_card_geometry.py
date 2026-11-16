"""Geometry tree ownership when a mirrored operation reports its source parent."""
from contextlib import ExitStack
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock, patch

from test_setup_card_preparation import CARD


class Group:
    def __init__(self, tag, name, members=()):
        self.Tag, self.Name, self.members = tag, name, list(members)
        self.GetParent = Mock(side_effect=AssertionError('Do not read inherited Geometry parents'))
        self.reads = 0

    def GetMembers(self):
        self.reads += 1
        return list(self.members)


class Mcs(Group):
    pass


class Feature(Group):
    volume = True


class Operation:
    def __init__(self, tag, name, reported_parent):
        self.Tag, self.Name = tag, name
        self.GetParent = Mock(return_value=reported_parent)

    def AskPathExists(self):
        return False

    def GetStatus(self):
        return 'Complete'


class Groups(list):
    def __init__(self, members):
        super().__init__(members)
        self.builders = []

    def CreateMillGeomBuilder(self, group):
        builder = NS(Destroy=Mock(), Commit=Mock(side_effect=AssertionError('read only')))
        if group.volume:
            builder.PartGeometry, builder.BlankGeometry = object(), object()
        self.builders.append(builder)
        return builder


def fixture(mixed=False):
    source, mirror = Mcs(10, 'MCS_SOURCE'), Mcs(11, 'MCS_MIRROR')
    a, b, c, outside = [Operation(i, name, source) for i, name in
                        ((20, 'SOURCE'), (21, 'MIRROR'), (22, 'SOURCE_2'), (23, 'OUTSIDE'))]
    boundary = Feature(12, 'BOUNDARY', [b, outside])
    boundary.volume = False
    source.members, mirror.members = [a, c], [boundary]
    original, mirrored = Feature(30, 'WORKPIECE', [source]), Feature(31, 'WORKPIECE_COPY', [mirror])
    geometry = Group(1, 'GEOMETRY', [original, mirrored])
    first = Group(40, 'SETUP_A', [a, b, c] if mixed else [a, c])
    second = Group(41, 'SETUP_B', [] if mixed else [b])
    program = Group(2, 'PROGRAM', [first, second, outside])
    groups = Groups([geometry, original, source, mirrored, mirror, boundary, program, first, second])
    roots = {1: geometry, 2: program}
    part = NS(Tag=100, CAMSetup=NS(CAMGroupCollection=groups, CAMOperationCollection=[a, b, c, outside],
                                GetRoot=Mock(side_effect=lambda view: roots[view])))
    nx = NS(CAM=NS(Operation=Operation, NCGroup=Group, OrientGeometry=Mcs, FeatureGeometry=Feature,
                   CAMSetup=NS(View=NS(Geometry=1, ProgramOrder=2))))
    return NS(nx=nx, part=part, groups=groups, geometry=geometry, program=program,
              source=source, mirror=mirror, a=a, b=b, c=c, outside=outside,
              original=original, mirrored=mirrored, boundary=boundary, first=first, second=second)


def context(f, selected):
    return CARD.resolve_context(f.nx, None, f.part, CARD.new_report(), selected=[selected], confirm=False)


class GeometryOwnershipTests(unittest.TestCase):
    def test_each_setup_uses_its_actual_mcs_workpiece_and_ipw_operations(self):
        f = fixture()
        with patch.object(CARD, 'ask_setup_folders', return_value=(CARD.cam_tree(f.nx, f.program),
                                                                  [f.first, f.second], {})):
            jobs = CARD.resolve_setup_jobs(f.nx, None, f.part, CARD.new_report())
        self.assertEqual([j['report']['setup_name'] for j in jobs], ['MCS_SOURCE', 'MCS_MIRROR'])
        first, second = [j['context'] for j in jobs]
        self.assertIs(first['geometry_index'], second['geometry_index'])
        self.assertEqual(f.geometry.reads, 1)
        self.assertIs(second['project_first_mcs'], f.source)
        for job, mcs, workpiece, ops in ((jobs[0], f.source, f.original, [f.a, f.c]),
                                         (jobs[1], f.mirror, f.mirrored, [f.b])):
            ctx, report = job['context'], job['report']
            self.assertIs(ctx['mcs'], mcs)
            ipw_source = CARD.resolve_ipw_source(f.nx, f.part, ctx, report)
            self.assertEqual(ipw_source['operations'], ops)
            self.assertIs(ipw_source['geometry_index'], ctx['geometry_index'])
            self.assertEqual(CARD.resolve_workpieces(f.nx, f.part, ipw_source, report), [workpiece])
            self.assertEqual(ctx['view_contexts'][0]['ipw_source']['operations'], ops)
        for obj in f.groups + [f.a, f.b, f.c, f.outside]:
            obj.GetParent.assert_not_called()
        for builder in f.groups.builders:
            builder.Commit.assert_not_called()
            builder.Destroy.assert_called_once()

    def test_multi_mcs_views_split_actual_members_and_keep_program_order(self):
        f = fixture(mixed=True)
        ctx = context(f, f.first)
        self.assertEqual(ctx['operations'], [f.a, f.b, f.c])
        self.assertEqual(ctx['mcs_groups'], [f.source, f.mirror])
        ctx['capture_mcs'] = [f.source, f.mirror]
        views = CARD.setup_view_contexts(f.nx, ctx)
        self.assertEqual([view['operations'] for view in views], [[f.a, f.c], [f.b]])
        for view, workpiece in zip(views, (f.original, f.mirrored)):
            self.assertIs(view['geometry_index'], ctx['geometry_index'])
            ipw_source = CARD.resolve_ipw_source(f.nx, f.part, view, {})
            self.assertEqual(CARD.resolve_workpieces(f.nx, f.part, ipw_source, {}), [workpiece])
            self.assertEqual(ipw_source['operations'], view['operations'])
        self.assertEqual(ctx['operations'], [f.a, f.b, f.c])

    def test_same_mcs_names_do_not_merge_mirror_members_or_workpieces(self):
        f = fixture(mixed=True)
        f.mirror.Name = f.source.Name
        ctx = context(f, f.first)
        ctx['capture_mcs'] = ctx['mcs_groups']
        views = CARD.setup_view_contexts(f.nx, ctx)
        self.assertEqual([view['operations'] for view in views], [[f.a, f.c], [f.b]])
        self.assertNotEqual(views[0]['capture_datum_key'], views[1]['capture_datum_key'])
        self.assertEqual(CARD.resolve_workpieces(f.nx, f.part, views[1], {}), [f.mirrored])

    def test_blank_reader_uses_mirror_workpiece(self):
        f = fixture()
        ctx, report = context(f, f.b), dict(CARD.new_report(), part_units='Millimeters')
        ipw_source = CARD.resolve_ipw_source(f.nx, f.part, ctx, report)
        blank = dict(type='AutoBlock', dimensions=dict(length=210., width=130., height=25.))
        with patch.object(CARD, 'workpiece_blank_values', return_value=blank) as read:
            CARD.read_workpiece_blank(f.nx, f.part, ipw_source, report)
        read.assert_called_once_with(f.nx, f.part, f.mirrored)
        self.assertEqual(report['blank']['status'], 'read')
        self.assertEqual(report['stock_blank'], 'Д×Ш×В 210×130×25 мм')

    def test_operation_table_shows_actual_mcs_for_each_row(self):
        f = fixture(mixed=True)
        ctx, report = context(f, f.first), dict(CARD.new_report(), collect_project_tools=False)
        uf = NS(Param=NS(AskIntValue=lambda *args: 0, AskLogicalValue=lambda *args: False),
                Oper=NS(AskCutterGroup=lambda tag: None))
        f.nx.UF = NS(UFSession=NS(GetUFSession=lambda: uf),
                      UFConstants=NS(UF_PARAM_SUPPRESS_PATH=1, UF_PARAM_OUTPUT_LOAD_TOOL=2))
        f.part.CAMSetup.CreateFeedsBuilder = lambda ops: NS(Destroy=Mock(), FeedsBuilder=NS(
            FeedCutBuilder=NS(Value=100., Unit='PerMinute'), SpindleRpmBuilder=NS(Value=1200.)))
        with ExitStack() as stack:
            for name in ('read_operation_stock', 'annotate_tool_hierarchy', 'read_cutcom_registers'):
                stack.enter_context(patch.object(CARD, name))
            stack.enter_context(patch.object(CARD, 'annotate_operation_timing', return_value=[]))
            stack.enter_context(patch.object(CARD, 'read_toolpath_seconds', return_value=(1., 60.)))
            CARD.read_operation_data(f.nx, f.part, ctx, report)
        self.assertEqual([(row['name'], row['mcs']) for row in report['operation_rows'] if row['kind'] == 'operation'],
                         [('SOURCE', 'MCS_SOURCE'), ('MIRROR', 'MCS_MIRROR'), ('SOURCE_2', 'MCS_SOURCE')])
        self.assertEqual(report.get('data_issues', []), [])

    def test_nested_mcs_wins_and_new_export_rebuilds_ownership(self):
        f = fixture()
        nested = Mcs(15, 'NESTED', [f.b])
        f.boundary.members = [nested, f.outside]
        f.groups.append(nested)
        self.assertIs(context(f, f.b)['mcs'], nested)
        f.boundary.members, f.source.members = [f.outside], [f.a, f.b, f.c]
        self.assertIs(context(f, f.b)['mcs'], f.source)

    def test_detached_s_branch_keeps_unique_workpiece_fallback(self):
        f = fixture()
        f.source.Name, f.mirror.Name = '1', '1S'
        f.geometry.members = [f.original, f.mirror]
        f.groups.remove(f.mirrored)
        ctx, report = context(f, f.b), {}
        self.assertEqual(CARD.resolve_workpieces(f.nx, f.part, ctx, report), [f.original])
        self.assertEqual(report['workpiece_resolution']['operations'][0]['source'], 'unique_matching_mcs')
        extra_mcs = Mcs(16, '1_COPY')
        extra = Feature(32, 'OTHER_WORKPIECE', [extra_mcs])
        f.geometry.members.append(extra)
        f.groups.extend([extra, extra_mcs])
        with self.assertRaisesRegex(ValueError, 'несколько Workpiece'):
            CARD.resolve_workpieces(f.nx, f.part, context(f, f.b), {})

    def test_missing_mirror_does_not_fall_back_to_reported_source_parent(self):
        f = fixture()
        f.boundary.members.remove(f.b)
        with self.assertRaisesRegex(RuntimeError, 'MIRROR.*не найден.*Geometry'):
            context(f, f.b)
        f.b.GetParent.assert_not_called()

    def test_ambiguous_or_cyclic_geometry_is_rejected(self):
        for mode in ('duplicate', 'cycle'):
            with self.subTest(mode=mode):
                f = fixture()
                f.source.members.append(f.b if mode == 'duplicate' else f.geometry)
                with self.assertRaisesRegex(RuntimeError, 'нескольких ветках|цикл'):
                    context(f, f.b)


if __name__ == '__main__':
    unittest.main()
