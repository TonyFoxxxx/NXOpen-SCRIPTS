"""Operation columns retain full values and use wrapped header minima."""
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import unittest
from unittest.mock import patch

SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Setup_Prototype.py'
SPEC = importlib.util.spec_from_file_location('setup_columns_test', SOURCE)
CARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CARD)
NODE = shutil.which('node')


class OperationColumnWidthTests(unittest.TestCase):
    def layout(self, rows):
        original = CARD.column_layout_widths
        with patch.object(CARD, 'column_layout_widths', wraps=original) as calculate:
            CARD.operation_layout(dict(operation_rows=rows))
        return calculate.call_args, calculate.call_args.args

    def test_empty_stock_columns_use_the_longest_header_word(self):
        call, (samples, target, font, minima, flexible) = self.layout([])
        widths = CARD.column_layout_widths(*call.args, **call.kwargs)
        for index in (9, 10):
            header_width = max(CARD.text_width_em(word) for word in CARD.OP_HEADERS[index].split())
            self.assertAlmostEqual(widths[index], header_width * font * 25.4 / 72 + 1.8)
            self.assertLess(widths[index], CARD.text_width_em(CARD.OP_HEADERS[index]) * font * 25.4 / 72 + 1.8)
        self.assertAlmostEqual(sum(widths), target)

    def test_all_rows_contribute_and_only_operation_name_gets_spare_space(self):
        short = dict(kind='operation', depth=0, name='OP', tool_name='MILL_D6', tool_number=1)
        long = dict(short, name='LONG_OPERATION_' * 30, tool_name='MILL_D10_Z4_STEEL_H51L10_DUBL')
        call, (_, target, font, _, _) = self.layout([short])
        first = CARD.column_layout_widths(*call.args, **call.kwargs)
        call, _ = self.layout([short] * 25 + [long])
        last = CARD.column_layout_widths(*call.args, **call.kwargs)
        expected = CARD.text_width_em(long['tool_name'], True) * font * 25.4 / 72 + 1.8
        self.assertAlmostEqual(last[3], expected)
        self.assertGreater(last[3], first[3])
        self.assertLess(last[0], first[0])
        for index in range(1, len(first)):
            if index != 3:
                self.assertAlmostEqual(last[index], first[index])
        self.assertAlmostEqual(sum(last), target)

    @unittest.skipUnless(NODE, 'Node.js is required to check the HTML column calculation')
    def test_html_widths_hidden_name_and_oversized_content(self):
        program = r"""
const assert=require('node:assert/strict'),vm=require('node:vm'),fs=require('node:fs');
const src=fs.readFileSync(SOURCE,'utf8'),start=src.indexOf('function autoColumnWidths('),end=src.indexOf('const PageScale=',start);
const context={};vm.createContext(context);vm.runInContext(src.slice(start,end)+';globalThis.fit=autoColumnWidths;',context);
const fit=(...args)=>Array.from(context.fit(...args));
assert.deepEqual(fit([400,18,120,25,35],[40,0,0,0,0],[0],500,true),[302,18,120,25,35]);
assert.deepEqual(fit([0,18,120,25,35],[0,0,0,0,0],[],500,true),[0,18,120,25,35]);
assert.deepEqual(fit([400,18,120,25,35],[40,0,0,0,0],[0],150,true),[40,18,120,25,35]);
assert.deepEqual(fit([400,0,120,0,35],[40,0,0,0,0],[0],500,true),[345,0,120,0,35]);
assert.deepEqual(fit([400,18,120],[40,0,0],[0],178,true),[40,18,120]);
// Other table families retain their existing bounded distribution.
assert.deepEqual(fit([120,80],[20,20],[0],70),[20,50]);
"""
        result = subprocess.run([NODE, '-e', 'const SOURCE=' + json.dumps(str(SOURCE)) + ';\n' + program],
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == '__main__':
    unittest.main()

