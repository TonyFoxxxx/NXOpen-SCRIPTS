"""Carrier identities survive tool filtering and selected-setup re-export."""
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace as NS
import unittest

SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Setup_Prototype.py'
SPEC = importlib.util.spec_from_file_location('setup_carriers_test', SOURCE)
CARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CARD)


class Group:
    def __init__(self, tag, name, parent=None):
        self.Tag, self.Name, self.parent, self.members = tag, name, parent, []
        if parent is not None:
            parent.members.append(self)
    def GetMembers(self):
        return list(self.members)
    def GetParent(self):
        return self.parent


class CarrierTests(unittest.TestCase):
    def test_key_uses_all_siblings_even_when_first_carrier_is_unused(self):
        root = Group(1, 'ROOT')
        first, second = Group(2, 'Carrier', root), Group(3, 'Carrier', root)
        tool = Group(4, 'MILL', second)
        nx = NS(CAM=NS(NCGroup=Group, CAMSetup=NS(View=NS(MachineTool=1))))
        part = NS(CAMSetup=NS(GetRoot=lambda view: root))
        report = dict(tools=[dict(tag='4', name='MILL', number=1)])
        CARD.annotate_tool_hierarchy(nx, part, {'4': tool}, report)
        self.assertEqual(report['tools'][0]['machine_path'][0]['name_index'], 1)
        row = CARD.tool_display_rows(report['tools'])[0]
        self.assertEqual(json.loads(row['carrier_key']), [['Carrier', 1]])
        first_tool = dict(tag='5', name='FIRST', number=2,
                          machine_path=[dict(tag='2', name='Carrier', name_index=0, order=0)])
        rows = CARD.tool_display_rows([first_tool] + report['tools'])
        self.assertEqual(next(r['carrier_key'] for r in rows if r.get('tag') == '3'), row['carrier_key'])

    def test_legacy_same_name_groups_never_receive_same_key(self):
        tools = [dict(tag=str(n + 10), name='TOOL', number=n,
                      machine_path=[dict(tag=str(n), name='Carrier')]) for n in (1, 2)]
        rows = [row for row in CARD.tool_display_rows(tools) if row['kind'] == 'group']
        self.assertEqual(len({row['carrier_key'] for row in rows}), 2)
        self.assertTrue(all('tag:' in row['carrier_key'] for row in rows))

    def test_group_button_is_accessible_and_preserves_carrier_identity(self):
        row = dict(kind='group', name='A & B', tag='2', depth=0, carrier_key='[["A & B",0]]')
        markup = CARD.tool_row_html(row)
        parsed = CARD.CardMarkup(markup)
        button = next(n for n in parsed.nodes if n['tag'] == 'button')
        self.assertEqual(button['attrs']['type'], 'button')
        self.assertEqual(button['attrs']['aria-expanded'], 'true')
        self.assertIn('A & B', button['attrs']['aria-label'])
        tr = next(n for n in parsed.nodes if n['tag'] == 'tr')
        self.assertEqual(tr['attrs']['data-carrier-key'], row['carrier_key'])

    def test_deep_descendants_keep_logical_depth_but_cap_indent(self):
        markup = CARD.tool_row_html(dict(kind='tool', tag='7', name='TOOL', number=1, depth=13))
        self.assertIn('data-depth="13"', markup)
        self.assertIn('width:15.60em', markup)

    def test_catalog_source_state_survives_selected_owner_export(self):
        saved = ('<section class="setup-document"><table data-fit-family="project-tools" '
                 'data-collapsed-carriers="[&quot;stale&quot;]"></table>'
                 '<template data-editor-id="catalog-source"><table data-fit-family="project-tools" '
                 'data-collapsed-carriers="[&quot;canonical&quot;]"></table></template></section>')
        fresh = '<section class="setup-document"><table data-fit-family="project-tools"></table></section>'
        parsed = CARD.CardMarkup(CARD.retain_setup_annotations(fresh, saved))
        table = next(n for n in parsed.nodes if n['tag'] == 'table')
        self.assertEqual(json.loads(table['attrs']['data-collapsed-carriers']), ['canonical'])

    def test_unreadable_sibling_name_does_not_discard_tree_order(self):
        class Unreadable(Group):
            def __init__(self, parent):
                self.Tag, self.parent = 8, parent
                parent.members.append(self)
            @property
            def Name(self):
                raise RuntimeError('name unavailable')
        root = Group(1, 'ROOT')
        Unreadable(root)
        carrier = Group(3, 'Carrier', root)
        tool = Group(4, 'MILL', carrier)
        nx = NS(CAM=NS(NCGroup=Group, CAMSetup=NS(View=NS(MachineTool=1))))
        part = NS(CAMSetup=NS(GetRoot=lambda view: root))
        report = dict(tools=[dict(tag='4', name='MILL', number=1)])
        CARD.annotate_tool_hierarchy(nx, part, {'4': tool}, report)
        group = report['tools'][0]['machine_path'][0]
        self.assertEqual(group['order'], 1)
        self.assertIsNone(group['name_index'])

    def test_selected_export_keeps_only_cover_carrier_state(self):
        key = '[["Carrier",0]]'
        collapse = CARD.escaped(json.dumps([key]))
        old = ('<section class="setup-document"><table data-fit-family="cover-tools" '
               'data-collapsed-carriers="%s"><tbody></tbody></table>'
               '<table data-fit-family="project-tools" data-collapsed-carriers="[&quot;other&quot;]"></table></section>') % collapse
        new = '<section class="setup-document"><table data-fit-family="cover-tools"><tbody></tbody></table></section>'
        result = CARD.retain_setup_annotations(new, old)
        parsed = CARD.CardMarkup(result)
        table = next(n for n in parsed.nodes if n['tag'] == 'table')
        self.assertEqual(json.loads(table['attrs']['data-collapsed-carriers']), [key])
        self.assertNotIn('other', result)


if __name__ == '__main__':
    unittest.main()
