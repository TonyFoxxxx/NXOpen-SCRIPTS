"""Manual text sizes stay independent of text length and legacy fit state."""
import importlib.util
from pathlib import Path
import unittest


SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Setup_Prototype.py'
SPEC = importlib.util.spec_from_file_location('setup_manual_text_test', SOURCE)
CARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CARD)


class ManualTableTextTests(unittest.TestCase):
    def test_generated_table_fonts_do_not_depend_on_label_length(self):
        fonts = []
        for length in (8, 800):
            name = 'LONG_NAME_' * length
            row = dict(kind='operation', depth=2, name=name, tool_name=name, tool_number=1)
            tool = dict(tag='10', number=1, name=name, cutcom_registers=[1, 99999999])
            markups = (CARD.operations_table([row], dict(operation_rows=[row])),
                       CARD.tool_table([tool]), CARD.project_tool_pages(dict(project_tools=[tool]))[0])
            current = []
            for markup in markups:
                parsed = CARD.CardMarkup(markup)
                table = next(node for node in parsed.nodes if node['tag'] == 'table' and node['attrs'].get('data-fit-family'))
                current.append(table['attrs']['style'])
                self.assertIn('font-size:7.00000pt', table['attrs']['style'])
                self.assertNotIn('data-fitted-font', table['attrs'])
                self.assertTrue(any(name in parsed.text(node) for node in parsed.nodes if node['tag'] == 'td'))
            fonts.append(current)
        self.assertEqual(fonts[0], fonts[1])

    def test_legacy_cleanup_preserves_layout_and_full_text_inside_templates(self):
        table = ('<table class="ops" data-fit-family="operations" data-fitted-font=".001" '
                 'style="font-size:.001px;--operation-name-gap:9px;width:170mm;transform:scale(.8)">'
                 '<thead><tr><th><span class="cell-text" style="font-size:2px;font-weight:bold">'
                 'Припуск<br style="font-size:1px">на стенки</span></th></tr></thead>'
                 '<tbody><tr><td title="Full name"><span class="cell-text" '
                 'style="font-size:.2px;color:red">Full name</span></td></tr></tbody></table>')
        original = '<section><div class="field" style="font-size:12px">Note</div>' + table + '<template>' + table + '</template></section>'
        result = CARD.normalize_manual_table_text(original)
        self.assertEqual(CARD.normalize_manual_table_text(result), result)
        self.assertNotIn('data-fitted-font', result)
        self.assertNotIn('--operation-name-gap', result)
        self.assertNotIn('<br', result)
        self.assertEqual(result.count('Припуск на стенки'), 2)
        self.assertEqual(result.count('width:170mm;transform:scale(.8)'), 2)
        self.assertEqual(result.count('title="Full name"'), 2)
        self.assertEqual(result.count('font-weight:bold'), 2)
        self.assertEqual(result.count('color:red'), 2)
        self.assertIn('<div class="field" style="font-size:12px">Note</div>', result)


if __name__ == '__main__':
    unittest.main()
