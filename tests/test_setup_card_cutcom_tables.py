"""Cutcom output keeps one row per tool and a separate D cell per operation."""
import importlib.util
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Setup_Prototype.py'
SPEC = importlib.util.spec_from_file_location('setup_cutcom_tables_test', SOURCE)
CARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CARD)


class CutcomTableTests(unittest.TestCase):
    def test_tool_registers_are_distinct_sorted_and_never_guessed_from_t(self):
        self.assertEqual(CARD.tool_cutcom_text(dict(number=5, cutcom_registers=[12, 5, 12, 0])), 'D0 / D5 / D12')
        self.assertEqual(CARD.tool_cutcom_text(dict(number=5, cutcom_register=12)), 'D12')
        self.assertEqual(CARD.tool_cutcom_text(dict(number=5)), '—')
        self.assertEqual(CARD.tool_cutcom_text(dict(number=5, cutcom_registers=[True, None, 2.5])), '—')

    def test_operation_cell_uses_global_visibility_flag_including_d_equals_t(self):
        row = dict(kind='operation', name='MILL', tool_name='T5', tool_number=5,
                   cutcom_register=5, show_cutcom=True, tool_change=False)
        self.assertEqual(CARD.operation_cells(row, {})[5], 'D5')
        row['cutcom_register'] = 12
        self.assertEqual(CARD.operation_cells(row, {})[5], 'D12')
        row['show_cutcom'] = False
        self.assertEqual(CARD.operation_cells(row, {})[5], '')
        row.update(show_cutcom=True, cutcom_register=None)
        self.assertEqual(CARD.operation_cells(row, {})[5], '—')
        self.assertEqual(len(CARD.operation_cells(dict(kind='group', name='GROUP'), {})), 12)

    def test_tools_keep_carriers_and_duplicate_t_objects_in_three_columns(self):
        path = [dict(tag='99', name='CARRIER', order=0)]
        tools = [dict(tag='1', name='MILL', number=5, machine_path=path, cutcom_registers=[5, 12]),
                 dict(tag='2', name='DRILL', number=5, machine_path=path, cutcom_registers=[5, 12])]
        for catalog in (False, True):
            if catalog:
                markup = CARD.project_tool_pages(dict(project_tools=tools))[0]
            else:
                markup = CARD.tool_table(tools)
            parsed = CARD.CardMarkup(markup)
            rows = [n for n in parsed.nodes if n['tag'] == 'tr' and n['parent']['tag'] == 'tbody']
            self.assertEqual(len(rows), 3)
            self.assertEqual([n['attrs'].get('data-kind') for n in rows], ['group', 'tool', 'tool'])
            for row in rows:
                cells = [n for n in parsed.nodes if n['parent'] is row and n['tag'] == 'td']
                self.assertEqual(len(cells), 3)
                d = next(n for n in cells if n['attrs']['data-tool-column-id'] == 'cutcom')
                self.assertEqual(parsed.text(d), '' if row['attrs']['data-kind'] == 'group' else 'D5 / D12')

    def test_operation_columns_and_width_metadata_remain_aligned(self):
        row = dict(kind='operation', tag='op-77', name='MILL', tool_name='MILL_D10', tool_number=5,
                   cutcom_register=12, show_cutcom=True)
        markup = CARD.operations_table([row], dict(operation_rows=[row]))
        parsed = CARD.CardMarkup(markup)
        table = next(n for n in parsed.nodes if n['tag'] == 'table')
        self.assertEqual(table['attrs']['data-ops-schema'], '4')
        operation = next(n for n in parsed.nodes if n['attrs'].get('data-kind') == 'operation')
        self.assertEqual(operation['attrs']['data-operation-tag'], 'op-77')
        self.assertEqual(len(table['attrs']['data-min-mm'].split(',')), 12)
        for kind in ('col', 'th', 'td'):
            cells = [n for n in parsed.nodes if n['tag'] == kind]
            self.assertEqual([n['attrs']['data-column-id'] for n in cells], CARD.OP_KEYS)
        self.assertEqual(CARD.OP_KEYS[4:6], ['number', 'cutcom'])


if __name__ == '__main__':
    unittest.main()
