"""Cutcom values are read per operation and displayed by project-wide T usage."""
from contextlib import ExitStack
import copy
import importlib.util
from pathlib import Path
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock, patch


SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Setup_Prototype.py'
SPEC = importlib.util.spec_from_file_location('setup_card_cutcom_test', SOURCE)
CARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CARD)
INHERIT = object()
UNAVAILABLE = object()


class Tool:
    def __init__(self, tag, number, register):
        self.Tag, self.Name = tag, 'Tool %s' % tag
        self.number, self.register = number, register


class Operation:
    def __init__(self, tag, tool, register=INHERIT):
        self.Tag, self.Name = tag, 'Operation %s' % tag
        self.tool, self.register = tool, register

    def AskPathExists(self):
        return True

    def GetStatus(self):
        return 'Complete'


class ReadOnlyUF:
    """UF inheritance is resolved on the requested tag, not by the consumer."""
    def __init__(self, tools, operations):
        self.objects = {obj.Tag: obj for obj in tools + operations}
        self.reads = []
        self.Param = NS(AskIntValue=self.integer,
                        AskLogicalValue=lambda tag, index: False,
                        SetIntValue=Mock(side_effect=AssertionError('NX parameter changed')),
                        InheritValue=Mock(side_effect=AssertionError('NX inheritance changed')))
        self.Oper = NS(AskCutterGroup=lambda tag: self.objects[tag].tool.Tag)

    def integer(self, tag, index):
        self.reads.append((tag, index))
        obj = self.objects[tag]
        if index == 1038:
            return (obj.tool if isinstance(obj, Operation) else obj).number
        if index == 1041:
            value = obj.register
            if isinstance(obj, Operation) and value is INHERIT:
                value = obj.tool.register
            if value is UNAVAILABLE:
                raise RuntimeError('Register unavailable')
            return value
        if index == 1:  # Unrelated suppress flag in read_operation_data.
            return 0
        raise AssertionError('Unexpected parameter index %s' % index)


def fixture(tools, operations, selected=None):
    selected = operations if selected is None else selected
    uf = ReadOnlyUF(tools, operations)
    constants = NS(UF_PARAM_TL_NUMBER=1038, UF_PARAM_TL_CUTCOM_REG=1041,
                   UF_PARAM_SUPPRESS_PATH=1, UF_PARAM_OUTPUT_LOAD_TOOL=2)
    nx = NS(UF=NS(UFConstants=constants, UFSession=NS(GetUFSession=lambda: uf)),
            CAM=NS(Tool=Tool))
    part = NS(Name='Part', CAMSetup=NS(CAMOperationCollection=operations,
                                      CAMGroupCollection=tools))
    context = {'operations': selected}
    report = CARD.new_report()
    used = {op.tool.Tag for op in selected}
    report['tools'] = [dict(tag=str(t.Tag), name=t.Name, number=t.number)
                       for t in tools if t.Tag in used]
    report['project_tools'] = [dict(tag=str(t.Tag), name=t.Name, number=t.number) for t in tools]
    report['operation_rows'] = [dict(kind='operation', tag=str(op.Tag), name=op.Name,
                                     tool_tag=str(op.tool.Tag), tool_number=op.tool.number,
                                     tool_change=False) for op in selected]
    return nx, part, context, report, uf


class SetupCardCutcomTests(unittest.TestCase):
    def collect(self, tools, operations, selected=None):
        nx, part, context, report, uf = fixture(tools, operations, selected)
        before = [(obj.Tag, obj.Name, obj.register) for obj in tools + operations]
        CARD.read_cutcom_registers(nx, part, context, report,
                                  {str(tool.Tag): tool for tool in tools})
        self.assertEqual([(obj.Tag, obj.Name, obj.register) for obj in tools + operations], before)
        uf.Param.SetIntValue.assert_not_called()
        uf.Param.InheritValue.assert_not_called()
        self.assertTrue(all(index in (1038, 1041) for _, index in uf.reads))
        return report, uf

    def test_two_and_three_registers_show_every_operation_including_equal_t(self):
        for values in ([5, 12, 5], [5, 12, 23, 12]):
            with self.subTest(values=values):
                tool = Tool(100, 5, 5)
                ops = [Operation(i + 1, tool, d) for i, d in enumerate(values)]
                report, _ = self.collect([tool], ops)
                self.assertEqual(report['tools'][0]['cutcom_registers'], sorted(set(values)))
                for row, d in zip(report['operation_rows'], values):
                    self.assertTrue(row['show_cutcom'])
                    self.assertFalse(row['tool_change'])
                    self.assertEqual(CARD.operation_cells(row, report)[CARD.OP_KEYS.index('cutcom')], 'D%d' % d)
                expected = ' / '.join('D%d' % d for d in sorted(set(values)))
                self.assertEqual(CARD.tool_cutcom_text(report['tools'][0]), expected)

    def test_single_register_different_from_t_stays_blank_in_operations(self):
        tool = Tool(100, 5, 12)
        report, uf = self.collect([tool], [Operation(1, tool), Operation(2, tool)])
        self.assertEqual(report['tools'][0]['cutcom_registers'], [12])
        self.assertEqual(CARD.tool_cutcom_text(report['tools'][0]), 'D12')
        for row in report['operation_rows']:
            self.assertEqual(row['cutcom_register'], 12)
            self.assertFalse(row['show_cutcom'])
            self.assertEqual(CARD.operation_cells(row, report)[CARD.OP_KEYS.index('cutcom')], '')
        self.assertIn((1, 1041), uf.reads)
        self.assertIn((2, 1041), uf.reads)

    def test_unselected_project_operation_controls_visibility_and_both_lists(self):
        tool = Tool(100, 5, 5)
        selected, outside = Operation(1, tool), Operation(2, tool, 12)
        report, uf = self.collect([tool], [selected, outside], [selected])
        self.assertEqual(len(report['operation_rows']), 1)
        self.assertEqual(report['operation_rows'][0]['cutcom_register'], 5)
        self.assertTrue(report['operation_rows'][0]['show_cutcom'])
        self.assertIn((outside.Tag, 1041), uf.reads)
        for field in ('tools', 'project_tools'):
            self.assertEqual(report[field][0]['cutcom_registers'], [5, 12])

    def test_unused_tool_default_does_not_create_phantom_second_register(self):
        tool = Tool(100, 5, 5)
        report, _ = self.collect([tool], [Operation(1, tool, 12), Operation(2, tool, 12)])
        self.assertEqual(report['tools'][0]['cutcom_register'], 5)
        self.assertEqual(report['tools'][0]['cutcom_registers'], [12])
        self.assertTrue(all(not row['show_cutcom'] for row in report['operation_rows']))

    def test_zero_is_a_real_register_and_failed_read_never_becomes_zero(self):
        tool = Tool(100, 5, 99)
        ops = [Operation(1, tool, 0), Operation(2, tool, 12), Operation(3, tool, UNAVAILABLE)]
        report, _ = self.collect([tool], ops)
        self.assertEqual(report['tools'][0]['cutcom_registers'], [0, 12])
        self.assertEqual(CARD.tool_cutcom_text(report['tools'][0]), 'D0 / D12')
        zero, known, missing = report['operation_rows']
        self.assertEqual(zero['cutcom_register'], 0)
        self.assertEqual(CARD.operation_cells(zero, report)[CARD.OP_KEYS.index('cutcom')], 'D0')
        self.assertEqual(known['cutcom_register'], 12)
        self.assertIsNone(missing['cutcom_register'])
        self.assertNotEqual(CARD.operation_cells(missing, report)[CARD.OP_KEYS.index('cutcom')], 'D0')
        self.assertTrue(any(issue['object'] == ops[2].Name and 'Cutcom' in issue['field']
                            for issue in report['data_issues']))

    def test_same_t_across_distinct_tool_objects_aggregates_without_merging_rows(self):
        a, b = Tool(100, 5, 5), Tool(200, 5, 12)
        report, _ = self.collect([a, b], [Operation(1, a), Operation(2, b)])
        for field in ('tools', 'project_tools'):
            self.assertEqual([tool['tag'] for tool in report[field]], ['100', '200'])
            self.assertTrue(all(tool['cutcom_registers'] == [5, 12] for tool in report[field]))
        self.assertEqual([row['cutcom_register'] for row in report['operation_rows']], [5, 12])
        self.assertTrue(all(row['show_cutcom'] for row in report['operation_rows']))

    def test_unknown_t_does_not_merge_unrelated_tools_and_no_ops_uses_tool_default(self):
        report = {'tools': [dict(tag='a', number=None), dict(tag='b', number=None),
                            dict(tag='c', number=7)],
                  'operation_rows': [dict(kind='operation', tag='1', tool_tag='a', tool_number=None),
                                     dict(kind='operation', tag='2', tool_tag='b', tool_number=None)]}
        operations = [dict(tag='1', tool_tag='a', tool_number=None, cutcom_register=5),
                      dict(tag='2', tool_tag='b', tool_number=None, cutcom_register=12)]
        unchanged = copy.deepcopy(operations)
        CARD.apply_cutcom_registers(report, operations, {'a': 5, 'b': 12, 'c': 17})
        self.assertEqual(operations, unchanged)
        self.assertEqual([tool['cutcom_registers'] for tool in report['tools']], [[5], [12], [17]])
        self.assertTrue(all(not row['show_cutcom'] for row in report['operation_rows']))

    def test_real_operation_data_pipeline_runs_cutcom_reader(self):
        tool = Tool(100, 5, 5)
        op, outside = Operation(1, tool), Operation(2, tool, 12)
        nx, part, context, report, uf = fixture([tool], [op, outside], [op])
        context.update(tree=dict(object=op, children=None), prepared_zmin={'1': -3.0})
        report['collect_project_tools'] = False
        feeds = NS(FeedCutBuilder=NS(Value=100, Unit='PerMinute'), SpindleRpmBuilder=NS(Value=1200))
        builder = NS(FeedsBuilder=feeds, Destroy=Mock())
        part.CAMSetup.CreateFeedsBuilder = lambda operations: builder
        with ExitStack() as stack:
            stack.enter_context(patch.object(CARD, 'nearest_mcs', return_value=NS(Tag=9, Name='MCS')))
            stack.enter_context(patch.object(CARD, 'read_toolpath_seconds', return_value=(1, 60)))
            stack.enter_context(patch.object(CARD, 'read_operation_stock'))
            stack.enter_context(patch.object(CARD, 'annotate_operation_timing', return_value=[]))
            stack.enter_context(patch.object(CARD, 'annotate_tool_hierarchy'))
            read = stack.enter_context(patch.object(CARD, 'read_cutcom_registers', wraps=CARD.read_cutcom_registers))
            CARD.read_operation_data(nx, part, context, report)
        read.assert_called_once()
        builder.Destroy.assert_called_once()
        self.assertEqual(report['tools'][0]['cutcom_registers'], [5, 12])
        row = report['operation_rows'][0]
        self.assertEqual(row['cutcom_register'], 5)
        self.assertTrue(row['show_cutcom'])
        self.assertIn((outside.Tag, 1041), uf.reads)
        uf.Param.SetIntValue.assert_not_called()


if __name__ == '__main__':
    unittest.main()
