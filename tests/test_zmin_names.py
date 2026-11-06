"""COPY cleanup, collision allocation and reversible renaming in all consumers."""
import ast
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from test_operation_zmin_cycles import ZMIN, IDENTITY
from test_setup_card_preparation import CARD, Model

ROOT = Path(__file__).resolve().parents[1]
NAME_CASES = [
    ('MILL_R0.5_Z-6_COPY_Z7.125', 7.125, 'MILL_R0.5_Z7.125'),
    ('MILL_R0.5_Z-6_COPY_COPY_Z7.75', 7.75, 'MILL_R0.5_Z7.75'),
    ('COPY_COPY_MILL_Z-6_COPY_2_Z-9_COPY3', -6., 'MILL_Z-6'),
    ('copy_2_MILL_copy1_Z-2,5', -4., 'MILL_Z-4'),
    ('MILL_COPY_COPY', -6., 'MILL_Z-6'),
    ('MILL_Z1_part_Z-2,50_z+.25', -.00001, 'MILL_Z1_part_Z0'),
    ('MILL_COPY_TOOL_Z-3', -6., 'MILL_COPY_TOOL_Z-6'),
    ('COPYSHOP_Z-2', -6., 'COPYSHOP_Z-6'),
    ('01_MILL_R0.5_2_Z-8_COPY', -6., '01_MILL_R0.5_2_Z-6'),
    ('MILL_1_Z-6', -6., 'MILL_1_Z-6'),
    ('01_COPY_COPY_MILL_Z-8_COPY', -6., '01_MILL_Z-6'),
]
ALLOCATION_CASES = [
    (['MILL_Z-6', 'MILL_Z-6'], [], ['MILL_Z-6', 'MILL_1_Z-6']),
    (['MILL_Z-6', 'MILL_Z-6'], ['MILL_Z-6'], ['MILL_1_Z-6', 'MILL_2_Z-6']),
    (['MILL_Z-6', 'MILL_Z-6'], ['mill_z-6', 'mill_1_z-6', 'mill_3_z-6'],
     ['MILL_2_Z-6', 'MILL_4_Z-6']),
    (['MILL_Z-6', 'MILL_Z-6', 'MILL_1_Z-6'], [],
     ['MILL_Z-6', 'MILL_2_Z-6', 'MILL_1_Z-6']),
    (['MILL_Z-6', 'MILL_Z-7'], ['MILL_Z-5'], ['MILL_Z-6', 'MILL_Z-7']),
    (['MILL_Z-6', 'mill_Z-6'], [], ['MILL_Z-6', 'mill_1_Z-6']),
]


class NamingTests(unittest.TestCase):
    def test_shared_python_helpers_are_identical(self):
        trees = [ast.parse((ROOT / 'scripts' / f).read_text(encoding='utf-8'))
                 for f in ('NX_Operation_Zmin.py', 'NX_Setup_Prototype.py')]
        for name in ('zmin_base_name', 'unique_zmin_names'):
            funcs = [next(n for n in t.body if isinstance(n, ast.FunctionDef) and n.name == name) for t in trees]
            self.assertEqual(ast.dump(funcs[0]), ast.dump(funcs[1]))

    def test_copy_chains_keep_the_core_name_and_one_current_z(self):
        for module, formatter in ((ZMIN, ZMIN.name_with_zmin), (CARD, CARD.zmin_operation_name)):
            for old, depth, expected in NAME_CASES:
                with self.subTest(module=module.__name__, name=old):
                    self.assertEqual(formatter(old, depth), expected)
                    self.assertEqual(formatter(expected, depth), expected)
            with self.assertRaises(ValueError):
                formatter('_Z-6_COPY_Z-7', -6.)

    def test_names_are_allocated_against_fixed_and_natural_targets(self):
        for module in (ZMIN, CARD):
            for desired, occupied, expected in ALLOCATION_CASES:
                with self.subTest(module=module.__name__, desired=desired, occupied=occupied):
                    self.assertEqual(module.unique_zmin_names(desired, occupied), expected)

    def standalone(self, model, selected):
        with patch.object(ZMIN, 'nearest_mcs', return_value=model.mcs), \
             patch.object(ZMIN, 'read_mcs', return_value=(IDENTITY, (0., 0., 0.))), \
             patch.object(ZMIN, 'toolpath_zmin', side_effect=lambda nx, op, *args: op.zmin):
            plan, skipped, unchanged = ZMIN.prepare_names(model.nx, model.part, selected)
        plan, conflicts = ZMIN.remove_name_conflicts(plan, model.ops, model.groups)
        self.assertEqual(conflicts, [])
        ZMIN.rename_operations(model.nx, model.session, plan, model.ops, model.groups)
        return plan, skipped, unchanged

    def test_standalone_selection_skips_bad_paths_and_rerun_is_stable(self):
        m = Model()
        for op, name in zip(m.ops, ('MILL_Z-6_COPY', 'MILL_Z-6_COPY_COPY', 'MILL_Z-6')):
            op.Name, op.zmin = name, -6.
        plan, skipped, _ = self.standalone(m, m.ops[:2])
        self.assertEqual(skipped, [])
        self.assertEqual([op.Name for op in m.ops], ['MILL_1_Z-6', 'MILL_2_Z-6', 'MILL_Z-6'])
        self.assertEqual(len(plan), 2)
        m.writes.clear()
        plan, skipped, unchanged = self.standalone(m, m.ops)
        self.assertEqual((plan, skipped, unchanged), ([], [], 3))
        self.assertFalse(m.writes)
        m.ops[0].Name, m.ops[0].has_path = 'MILL_COPY_Z-99', False
        _, skipped, _ = self.standalone(m, m.ops)
        self.assertEqual(len(skipped), 1)
        self.assertEqual(m.ops[0].Name, 'MILL_COPY_Z-99')

    def test_standalone_released_names_allow_swaps_and_failed_write_rolls_back(self):
        m = Model()
        m.ops[0].Name, m.ops[1].Name = 'MILL_Z-6', 'MILL_Z-7'
        m.ops[0].zmin, m.ops[1].zmin = -7., -6.
        self.standalone(m, m.ops[:2])
        self.assertEqual([op.Name for op in m.ops[:2]], ['MILL_Z-7', 'MILL_Z-6'])
        m.ops[0].Name, m.ops[1].Name = 'MILL_COPY', 'MILL_COPY_COPY'
        m.ops[0].zmin = m.ops[1].zmin = -6.
        original = [op.Name for op in m.ops]
        m.refuse = 'MILL_1_Z-6'
        with self.assertRaisesRegex(RuntimeError, 'восстановлены'):
            self.standalone(m, m.ops[:2])
        self.assertEqual([op.Name for op in m.ops], original)

    def test_setup_preparation_uses_all_project_names_and_can_undo(self):
        m = Model()
        for op, name in zip(m.ops, ('MILL_Z-6_COPY', 'MILL_Z-6_COPY_COPY', 'MILL_Z-6')):
            op.Name, op.zmin = name, -6.
        m.selection = [1, 2]
        m.groups[0].Name = 'MILL_1_Z-6'  # Groups outside selection also occupy names.
        original = [op.Name for op in m.ops]
        with patch.object(CARD, 'read_mcs', return_value=(IDENTITY, (0., 0., 0.))), \
             patch.object(CARD, 'toolpath_zmin', side_effect=lambda nx, op, *args: op.zmin):
            preparation = m.prepare(add_zmin=True)
            preparation.apply()
            self.assertEqual([op.Name for op in m.ops], ['MILL_2_Z-6', 'MILL_3_Z-6', 'MILL_Z-6'])
            self.assertFalse(preparation.warnings)
            self.assertEqual(m.jobs[0]['context']['prepared_zmin'], {'1': -6., '2': -6.})
            m.writes.clear()
            again = m.prepare(add_zmin=True)
            again.apply()
            self.assertFalse(m.writes)
            preparation.finish(False)
        self.assertEqual([op.Name for op in m.ops], original)

    def test_csharp_naming_and_real_collision_application(self):
        source = (ROOT / 'scripts/NX_Postprocess_To_Machine.cs').read_text(encoding='ascii')
        names = 'internal static class ZminNames' + source.split('internal static class ZminNames', 1)[1].split(
            '// Same supported cycle profiles', 1)[0]
        prefix = 'internal sealed class OperationZmin' + source.split('internal sealed class OperationZmin', 1)[1].split(
            '    internal static OperationZmin Prepare(', 1)[0]
        tail = '    private void RemoveConflicts()' + source.split('    private void RemoveConflicts()', 1)[1].split(
            '// Same naming rules', 1)[0]
        fixture = (ROOT / 'tests/fixtures/PostprocessZminNames.cs').read_text(encoding='ascii')
        helper = fixture.split('// INJECTED_METHODS', 1)[1]
        fixture = fixture.split('// INJECTED_METHODS', 1)[0]
        checks = []
        for old, depth, expected in NAME_CASES:
            checks.append('Check(OperationZmin.NameWithZmin(%s, %s) == %s, "name cleanup");' %
                          (json.dumps(old), repr(depth), json.dumps(expected)))
        for desired, occupied, expected in ALLOCATION_CASES:
            lists = ['new List<string> { ' + ', '.join(json.dumps(x) for x in items) + ' }'
                     for items in (desired, occupied, expected)]
            checks.append('Equal(ZminNames.Unique(%s, %s), %s);' % tuple(lists))
        fixture = fixture.replace('// GENERATED_CASES', '\n'.join(checks))
        code = fixture + names + prefix + helper + tail
        with tempfile.TemporaryDirectory(prefix='nx-zmin-name-test-') as directory:
            path = Path(directory)
            (path / 'Checks.cs').write_text(code, encoding='ascii')
            if os.name == 'nt':
                compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
                exe = path / 'Checks.exe'
                command = [str(compiler), '/nologo', '/out:' + str(exe), str(path / 'Checks.cs')]
                run = [str(exe)]
            else:
                dotnet = shutil.which('dotnet')
                self.assertIsNotNone(dotnet, '.NET SDK required for naming checks')
                (path / 'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                    '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                    '</PropertyGroup></Project>', encoding='ascii')
                command = [dotnet, 'build', str(path / 'Checks.csproj'), '--nologo', '-v:q']
                run = [dotnet, str(path / 'bin/Debug/net8.0/Checks.dll')]
            compiled = subprocess.run(command, capture_output=True, text=True, timeout=120)
            self.assertEqual(compiled.returncode, 0, compiled.stdout + compiled.stderr)
            result = subprocess.run(run, capture_output=True, text=True, timeout=60)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            print(result.stdout, flush=True)


if __name__ == '__main__':
    unittest.main()
