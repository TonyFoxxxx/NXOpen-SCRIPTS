"""Cycle depths reach both setup-card consumers through the production reader."""
from contextlib import ExitStack
import importlib.util
from pathlib import Path
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock, patch

from test_operation_zmin_cycles import CycleCase, IDENTITY


SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Setup_Prototype.py'
SPEC = importlib.util.spec_from_file_location('setup_card_cycles_test', SOURCE)
CARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CARD)


class SetupCardCycleTests(unittest.TestCase):
    def frame(self, stack, case):
        mcs = NS(Tag=77, Name='MCS')
        stack.enter_context(patch.object(CARD, 'nearest_mcs', return_value=mcs))
        stack.enter_context(patch.object(CARD, 'read_mcs', return_value=(IDENTITY, case.origin)))

    def rename_plan(self, case):
        with ExitStack() as stack:
            self.frame(stack, case)
            return CARD.zmin_rename_plan(case.nx, case.op.OwningPart, [case.op])

    def table(self, case, prepared=None):
        """Supply unrelated metadata; preserve Zmin orchestration and its real NX reader."""
        part, uf = case.op.OwningPart, case.nx.UF.UFSession.GetUFSession()
        case.op.tool.Name = 'CUTTER'
        part.CAMSetup.CAMGroupCollection = [case.op.tool]
        case.nx.UF.UFConstants = NS(UF_PARAM_SUPPRESS_PATH=1,
                                   UF_PARAM_OUTPUT_LOAD_TOOL=2, UF_PARAM_TL_NUMBER=3)
        uf.Param.AskIntValue = lambda tag, index: 0 if index == 1 else 7
        uf.Param.AskLogicalValue = lambda tag, index: False
        uf.Oper = NS(AskCutterGroup=lambda tag: case.op.tool.Tag)
        feeds = NS(FeedCutBuilder=NS(Value=100., Unit='PerMinute'),
                   SpindleRpmBuilder=NS(Value=1200.))
        part.CAMSetup.CreateFeedsBuilder = lambda ops: NS(FeedsBuilder=feeds, Destroy=lambda: None)
        report = CARD.new_report()
        report['collect_project_tools'] = False
        context = dict(operations=[case.op], tree=dict(object=case.op, children=None))
        if prepared is not None:
            context['prepared_zmin'] = prepared
        with ExitStack() as stack:
            self.frame(stack, case)
            stack.enter_context(patch.object(CARD, 'read_toolpath_seconds', return_value=(1., 1.)))
            stack.enter_context(patch.object(CARD, 'read_operation_stock'))
            stack.enter_context(patch.object(CARD, 'annotate_operation_timing', return_value=[]))
            stack.enter_context(patch.object(CARD, 'annotate_tool_hierarchy'))
            CARD.read_operation_data(case.nx, part, context, report)
        self.assertEqual(len(report['operation_rows']), 1)
        return report['operation_rows'][0], report

    def assert_released(self, case, builders=1):
        self.assertEqual(case.builder.destroyed, builders)
        self.assertTrue(case.op.path.buffers)
        self.assertTrue(all(buffer.disposed for buffer in case.op.path.buffers))

    def test_rename_plan_gets_tip_depth_and_caches_it_for_both_cycles(self):
        for spot, suffix, expected in ((True, '-1.25', -1.25),
                                       (False, '-12.9434', -12.943375672974064)):
            with self.subTest(spot=spot):
                case = CycleCase(spot=spot)
                old = case.op.Name
                plan, skipped, unchanged, values = self.rename_plan(case)
                self.assertEqual((skipped, unchanged), ([], 0))
                self.assertEqual(len(plan), 1)
                self.assertEqual((plan[0]['old'], plan[0]['new']), (old, 'HOLES_Z' + suffix))
                self.assertAlmostEqual(values[CARD.object_key(case.op)], expected)
                self.assertEqual(case.op.Name, old)
                self.assert_released(case)

    def test_unsupported_cycle_is_skipped_without_name_or_cache_change(self):
        case = CycleCase()
        case.builder.geometry.UseModelDepth = True
        old = case.op.Name
        plan, skipped, unchanged, values = self.rename_plan(case)
        self.assertEqual((plan, unchanged, values), ([], 0, {}))
        self.assertEqual(len(skipped), 1)
        self.assertEqual(skipped[0][0], old)
        self.assertTrue(skipped[0][1])
        self.assertEqual(case.op.Name, old)
        self.assert_released(case)

    def test_table_uses_cycle_bottom_without_enabling_renaming(self):
        for spot, expected in ((True, -1.25), (False, -12.943375672974064)):
            for rotated in (False, True):
                with self.subTest(spot=spot, rotated=rotated):
                    if rotated:
                        case = CycleCase(spot=spot, positions=((45., 30., 10.),),
                                         axes=((-1., 0., 0.),), origin=(45., 0., 0.))
                    else:
                        case = CycleCase(spot=spot)
                    old = case.op.Name
                    case.op.SetName = Mock(side_effect=AssertionError('Read-only table renamed operation'))
                    row, report = self.table(case)
                    self.assertAlmostEqual(row['zmin'], expected)
                    self.assertEqual(CARD.operation_cells(row, report)[-1], CARD.zmin_text(expected))
                    self.assertNotEqual(CARD.zmin_text(row['zmin']), '0')
                    self.assertEqual(row['name'], old)
                    self.assertEqual(case.op.Name, old)
                    self.assertEqual(report['data_issues'], [])
                    case.op.SetName.assert_not_called()
                    self.assert_released(case)

    def test_table_reuses_the_same_successful_preparation_value(self):
        case = CycleCase()
        plan, skipped, _, values = self.rename_plan(case)
        self.assertFalse(skipped)
        with patch.object(CARD, 'toolpath_zmin', side_effect=AssertionError('Cached path reread')) as reader:
            row, report = self.table(case, prepared=values)
        reader.assert_not_called()
        self.assertEqual(row['zmin'], values[CARD.object_key(case.op)])
        self.assertEqual(CARD.zmin_operation_name(row['name'], row['zmin']), plan[0]['new'])
        self.assertEqual(report['data_issues'], [])
        self.assert_released(case)

    def test_unsupported_table_depth_is_blank_and_has_a_diagnostic(self):
        case = CycleCase(spot=True)
        del case.locals[0].values['DEPTH']
        old = case.op.Name
        row, report = self.table(case)
        self.assertIsNone(row['zmin'])
        self.assertEqual(CARD.operation_cells(row, report)[-1], '')
        self.assertEqual(case.op.Name, old)
        issues = [issue for issue in report['data_issues'] if issue['field'] == 'Zmin']
        self.assertEqual(len(issues), 1)
        self.assertEqual(issues[0]['object'], old)
        self.assertTrue(issues[0]['message'])
        self.assert_released(case)

    def test_outdated_path_does_not_display_a_prepared_value(self):
        case = CycleCase(spot=True)
        case.op.status = 1  # Regen: the old path cannot supply a current depth.
        with patch.object(CARD, 'toolpath_zmin', side_effect=AssertionError('Stale path read')) as reader:
            row, _ = self.table(case, prepared={CARD.object_key(case.op): -1.25})
        reader.assert_not_called()
        self.assertIsNone(row['zmin'])
        self.assertEqual(case.builder.destroyed, 0)


if __name__ == '__main__':
    unittest.main()
