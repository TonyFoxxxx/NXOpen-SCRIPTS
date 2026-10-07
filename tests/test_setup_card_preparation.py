"""Preparation uses real production orchestration with a reversible in-memory NX model."""
import importlib.util
from pathlib import Path
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock, patch
from contextlib import ExitStack
import sys

SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Setup_Prototype.py'
SPEC = importlib.util.spec_from_file_location('setup_preparation_test', SOURCE)
CARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CARD)


class Group:
    def __init__(self, tag, name):
        self.Tag, self.Name = tag, name


class Orient(Group):
    pass


class Operation(Group):
    def __init__(self, tag, name, model):
        super().__init__(tag, name)
        self.model, self.has_path, self.status, self.zmin = model, True, 'Complete', -3.125

    def SetName(self, name):
        if name == self.model.refuse:
            raise RuntimeError('injected rename failure')
        if any(obj is not self and obj.Name.casefold() == name.casefold() for obj in self.model.objects):
            raise RuntimeError('occupied name: ' + name)
        self.Name = name
        self.model.writes.append(('name', self.Tag, name))

    def AskPathExists(self):
        return self.has_path

    def GetStatus(self):
        return self.status

    def GetParent(self, view):
        return self.model.mcs


class Tool(Group):
    Types = NS(Mill=1, Drill=2, Barrel=3, Tcutter=4, MillForm=5, Turn=6)

    def __init__(self, tag, name, part, family=1):
        super().__init__(tag, name)
        self.OwningPart, self.family = part, family
        self.general, self.cutter = 'General old', 'Cutter old'
        self.params = {1000: 6., 1038: 2, 1040: 3, 1041: 0}
        self.absent_d, self.fail_cutter, self.fail_d = False, False, False

    def GetTypeAndSubtype(self):
        return self.family, 0


class Groups(list):
    def CreateNcgroupBuilder(self, tool):
        class Builder:
            Description = tool.general
            def Commit(self):
                tool.general = self.Description
            def Destroy(self):
                pass
        return Builder()


class Model:
    def __init__(self):
        self.part = NS(Tag=500, Leaf='Project.prt', PartUnits='Millimeters')
        self.mcs = Orient(600, '1_UST')
        self.mcs.OwningPart = self.part
        self.ops = [Operation(1, '02_ROUGH_Z-7', self), Operation(2, '01_FINISH', self), Operation(3, 'OUTSIDE', self)]
        self.tools = [Tool(20, 'USED', self.part), Tool(21, 'UNUSED', self.part)]
        self.groups = Groups([self.mcs] + self.tools)
        self.part.CAMSetup = NS(CAMOperationCollection=self.ops, CAMGroupCollection=self.groups)
        self.objects, self.writes, self.refuse = self.ops + self.groups, [], None
        self.selection, self.selection_error, self.general_selection = [], None, []
        self.marks, self.next_mark, self.undone = {}, 0, []
        self.session = NS(Parts=NS(Work=self.part, Display=self.part), SetUndoMark=self.mark,
                          UndoToMark=self.undo, DeleteUndoMark=self.delete,
                          UpdateManager=NS(DoUpdate=Mock(return_value=0)))
        self.uf = NS(UiOnt=NS(AskSelectedNodes=self.selected, Refresh=Mock()),
                     Param=NS(AskIntValue=self.read, AskDoubleValue=self.read, AskParamStatus=self.status,
                              AskStrValue=lambda tag, index: self.by_tag(tag).cutter, SetStrValue=self.write_cutter))
        self.nx = NS(Session=NS(GetSession=lambda: self.session, MarkVisibility=NS(Visible=1, Invisible=0)),
                     UF=NS(UFSession=NS(GetUFSession=lambda: self.uf)),
                     CAM=NS(Operation=Operation, NCGroup=Group, Tool=Tool, OrientGeometry=Orient,
                            CAMObject=NS(Status=None), CAMSetup=NS(View=NS(Geometry=1))))
        self.ui = NS(SelectionManager=NS(GetNumSelectedObjects=lambda: len(self.general_selection),
                                        GetSelectedTaggedObject=lambda i: self.general_selection[i]),
                     NXMessageBox=NS(Show=Mock()))
        self.nx.UI = NS(GetUI=lambda: self.ui)
        self.nx.NXMessageBox = NS(DialogType=NS(Information=1, Warning=2, Error=3))
        self.jobs = [dict(context=dict(operations=self.ops[:2], mcs=self.mcs), report=CARD.new_report(), output=None)]
        self.jobs[0]['report']['setup_index'] = 1

    def by_tag(self, tag):
        return next(obj for obj in self.objects if obj.Tag == tag)

    def read(self, tag, index):
        tool = self.by_tag(tag)
        if index == 1041 and tool.fail_d:
            raise RuntimeError('D read failed')
        return tool.params[index]

    def status(self, tag, index):
        return 'InvalidIndex' if self.by_tag(tag).absent_d else 'Overridden'

    def write_cutter(self, tag, index, value):
        tool = self.by_tag(tag)
        tool.cutter = value
        if tool.fail_cutter:
            raise RuntimeError('injected cutter write failure')

    def selected(self):
        if self.selection_error:
            raise self.selection_error
        return len(self.selection), self.selection[:]

    def mark(self, visibility, name):
        self.next_mark += 1
        self.marks[self.next_mark] = ([(obj, obj.Name) for obj in self.objects],
                                     [(tool, tool.general, tool.cutter, dict(tool.params)) for tool in self.tools])
        return self.next_mark

    def undo(self, mark, name):
        names, tools = self.marks[mark]
        for obj, old in names:
            obj.Name = old
        for tool, general, cutter, params in tools:
            tool.general, tool.cutter, tool.params = general, cutter, dict(params)
        self.undone.append(mark)
        for other in list(self.marks):
            if other > mark:
                del self.marks[other]

    def delete(self, mark, name):
        del self.marks[mark]

    def prepare(self, **options):
        values = CARD.preparation_options()
        values.update(options)
        return CARD.SetupPreparation(self.nx, self.part, self.jobs, values,
                                     CARD.PreparationSelection(self.nx, self.ui, self.part))


class PreparationTests(unittest.TestCase):
    def setUp(self):
        self.m = Model()
        self.stack = ExitStack()
        self.stack.enter_context(patch.object(CARD, 'read_mcs', return_value=(((1, 0, 0), (0, 1, 0), (0, 0, 1)), (0, 0, 0))))
        self.path = self.stack.enter_context(patch.object(CARD, 'toolpath_zmin', side_effect=lambda nx, op, *args: op.zmin))
        self.addCleanup(self.stack.close)

    def test_all_off_does_not_read_paths_or_change_nx_even_if_selection_failed(self):
        self.m.selection_error = RuntimeError('navigator unavailable')
        preparation = self.m.prepare()
        preparation.apply()
        preparation.finish(True)
        self.assertFalse(self.m.marks or self.m.writes)
        self.path.assert_not_called()
        self.assertEqual(self.m.tools[0].general, 'General old')

    def test_selection_is_frozen_and_empty_selection_means_whole_project(self):
        for tags, expected in (([], [1, 2, 3]), ([3, 1, 600], [1, 3])):
            self.m.selection = tags
            snapshot = CARD.PreparationSelection(self.m.nx, self.m.ui, self.m.part)
            self.m.selection = [2]
            snapshot.validate(self.m.session, self.m.part)
            self.assertEqual([op.Tag for op in snapshot.operations], expected)

    def test_bad_selection_never_becomes_all_operations(self):
        for selected, error in (([600], None), ([], RuntimeError('read failed')), ([9999], None)):
            self.m.selection, self.m.selection_error = selected, error
            preparation = self.m.prepare(add_zmin=True)
            with self.assertRaisesRegex(RuntimeError, 'Zmin:'):
                preparation.apply()
            self.assertFalse(self.m.marks or self.m.writes)
        self.m.selection_error = RuntimeError('navigator unavailable')
        self.m.general_selection = [self.m.ops[1]]
        snapshot = CARD.PreparationSelection(self.m.nx, self.m.ui, self.m.part)
        snapshot.validate(self.m.session, self.m.part)
        self.assertEqual(snapshot.operations, [self.m.ops[1]])

    def test_numbering_restarts_in_each_folder_and_changes_to_three_digits(self):
        many = [Operation(i + 100, '9_008_OP' + str(i), self.m) for i in range(100)]
        jobs = [dict(context=dict(operations=many)), self.m.jobs[0]]
        plan = CARD.operation_numbering_plan(jobs)
        self.assertEqual((plan[0]['new'], plan[99]['new'], plan[100]['new']),
                         ('001_OP0', '100_OP99', '01_ROUGH_Z-7'))
        self.assertEqual(CARD.operation_numbering_plan([dict(context=dict(operations=many[:99]))])[0]['new'], '01_OP0')
        with self.assertRaisesRegex(RuntimeError, 'несколько'):
            CARD.operation_numbering_plan([self.m.jobs[0], self.m.jobs[0]])

    def test_swapped_numbered_names_and_case_insensitive_temporary_names(self):
        self.m.ops[0].Name, self.m.ops[1].Name, self.m.ops[2].Name = '02_ROUGH', '01_ROUGH', 'nxprep_1'
        preparation = self.m.prepare(number_operations=True)
        preparation.apply()
        self.assertEqual([op.Name for op in self.m.ops], ['01_ROUGH', '02_ROUGH', 'nxprep_1'])
        self.assertFalse(CARD.operation_numbering_plan(self.m.jobs))
        preparation.finish(False)
        self.assertEqual([op.Name for op in self.m.ops], ['02_ROUGH', '01_ROUGH', 'nxprep_1'])

    def test_description_updates_all_tools_both_fields_without_changing_t_h_d(self):
        self.m.tools[1].absent_d = True
        before = [dict(tool.params) for tool in self.m.tools]
        preparation = self.m.prepare(update_descriptions=True, include_tool_numbers=True)
        preparation.apply()
        self.assertEqual([tool.general for tool in self.m.tools], ['⌀6_T2_H3_D0', '⌀6_T2_H3'])
        self.assertEqual([tool.cutter for tool in self.m.tools], [tool.general for tool in self.m.tools])
        self.assertEqual([tool.params for tool in self.m.tools], before)
        preparation.finish(False)
        self.assertEqual([tool.general for tool in self.m.tools], ['General old'] * 2)
        self.assertEqual([tool.cutter for tool in self.m.tools], ['Cutter old'] * 2)

    def test_diameter_only_does_not_read_t_h_d_and_repeated_run_is_idempotent(self):
        self.m.tools[0].params = {1000: 6.0000015}
        self.m.tools[1].params = {1000: 0.0000004}
        preparation = self.m.prepare(update_descriptions=True)
        preparation.apply()
        self.assertEqual(self.m.tools[0].general, '⌀6.000002')
        self.assertEqual(self.m.tools[1].general, 'General old')
        self.assertFalse(CARD.apply_tool_description(self.m.nx, self.m.part, self.m.uf, self.m.tools[0], False))

    def test_partial_description_write_rolls_back_one_tool_and_bad_d_is_not_omitted(self):
        self.m.tools[0].fail_cutter = True
        self.m.tools[1].fail_d = True
        preparation = self.m.prepare(update_descriptions=True, include_tool_numbers=True)
        preparation.apply()
        self.assertEqual([tool.general for tool in self.m.tools], ['General old'] * 2)
        self.assertEqual([tool.cutter for tool in self.m.tools], ['Cutter old'] * 2)
        self.assertEqual(len(preparation.warnings), 2)
        self.assertIn('D read failed', preparation.result())

    def test_unsupported_tool_and_invalid_diameter_are_unchanged(self):
        self.m.tools[0].family, self.m.tools[1].params[1000] = Tool.Types.Turn, float('nan')
        preparation = self.m.prepare(update_descriptions=True)
        preparation.apply()
        self.assertFalse(preparation.warnings)
        self.assertEqual([tool.general for tool in self.m.tools], ['General old'] * 2)

    def test_combined_options_replace_suffix_and_do_not_renumber_outside_chosen_folder(self):
        preparation = self.m.prepare(number_operations=True, update_descriptions=True, add_zmin=True)
        preparation.apply()
        self.assertEqual([op.Name for op in self.m.ops], ['01_ROUGH_Z-3.125', '02_FINISH_Z-3.125', 'OUTSIDE_Z-3.125'])
        self.assertEqual(self.m.jobs[0]['context']['prepared_zmin'], {'1': -3.125, '2': -3.125, '3': -3.125})
        preparation.finish(True)
        self.assertIn(preparation.mark, self.m.marks, 'Successful changes retain one visible NX undo mark')
        again = self.m.prepare(number_operations=True, update_descriptions=True, add_zmin=True)
        self.m.writes.clear()
        again.apply()
        self.assertFalse(self.m.writes)
        self.assertIn('уже актуальны — 3', again.result())

    def test_zmin_keeps_invalid_paths_and_removes_only_terminal_numeric_suffixes(self):
        self.m.ops[0].has_path, self.m.ops[1].status = False, 'Regen'
        preparation = self.m.prepare(add_zmin=True)
        preparation.apply()
        self.assertEqual([op.Name for op in self.m.ops], ['02_ROUGH_Z-7', '01_FINISH', 'OUTSIDE_Z-3.125'])
        self.assertEqual(len(preparation.warnings), 2)
        self.assertEqual(CARD.zmin_operation_name('MILL_Z1_part_Z-2,50_z+.25', -0.00001), 'MILL_Z1_part_Z0')

    def test_conflicts_skip_zmin_but_abort_numbering_and_undo_other_options(self):
        self.m.ops[2].Name = '01_ROUGH_Z-7'
        original = [op.Name for op in self.m.ops]
        preparation = self.m.prepare(number_operations=True, update_descriptions=True)
        with self.assertRaisesRegex(RuntimeError, 'Нумерация:'):
            preparation.apply()
        preparation.finish(False)
        self.assertEqual([op.Name for op in self.m.ops], original)
        self.assertEqual(self.m.tools[0].general, 'General old')
        self.m.ops[2].Name = '02_ROUGH_Z-3.125'
        self.m.selection = [1]
        preparation = self.m.prepare(add_zmin=True)
        preparation.apply()
        self.assertEqual(self.m.ops[0].Name, original[0])
        self.assertTrue(preparation.warnings)

    def test_mid_rename_failure_restores_names_and_descriptions(self):
        self.m.refuse = '02_FINISH'
        original = [op.Name for op in self.m.ops]
        preparation = self.m.prepare(number_operations=True, update_descriptions=True)
        with self.assertRaisesRegex(RuntimeError, 'injected rename'):
            preparation.apply()
        preparation.finish(False)
        self.assertEqual([op.Name for op in self.m.ops], original)
        self.assertEqual(self.m.tools[0].general, 'General old')
        self.assertFalse(self.m.marks)

    def test_model_update_failure_restores_all_descriptions(self):
        self.m.session.UpdateManager.DoUpdate.return_value = 2
        preparation = self.m.prepare(update_descriptions=True)
        with self.assertRaisesRegex(RuntimeError, 'ошибках обновления'):
            preparation.apply()
        preparation.finish(False)
        self.assertEqual([tool.general for tool in self.m.tools], ['General old'] * 2)

    def run_main(self, publish=True, cancel=False):
        m = self.m
        self.m.selection = [1]
        m.part.ModelingViews = NS(WorkView=NS(GetAxis=lambda axis: NS(X=1, Y=0, Z=0)))
        m.nx.XYZAxis = NS(XAxis=1, YAxis=2, ZAxis=3)
        m.nx.Assemblies = m.nx.Facet = m.nx.Gateway = NS()
        components = NS(restore=lambda: [], apply=lambda keys: None, dirty=False)
        def jobs(nx, ui, part, state):
            state['_preparation_options'] = dict(number_operations=True, update_descriptions=True, add_zmin=True)
            m.selection = [3]  # Startup modal changes navigator selection after snapshot.
            return m.jobs
        def confirm(nx, part, state):
            state['tool_number_check'] = {}
        observed = []
        def render(nx, ui, output, report, context):
            observed.append(([op.Name for op in m.ops], m.tools[0].general))
            report.update(status='ok', final_view_fit={})
        def fit(nx, part, state):
            state['final_view_fit'] = {}
            return []
        mocks = dict(snapshot_view=Mock(return_value={}), read_view_projection=Mock(return_value='Orthographic'),
                     project_name_from_part=Mock(return_value='Project'), project_file_from_part=Mock(return_value=Path('/project/Project.prt')),
                     resolve_setup_jobs=Mock(side_effect=jobs), confirm_duplicate_tool_numbers=Mock(side_effect=confirm),
                     AssemblyComponentDisplay=Mock(return_value=components),
                     ask_all_setup_components=Mock(side_effect=CARD.ExportCancelled() if cancel else None, return_value=[set()]),
                     read_existing_card=Mock(return_value=None), plan_setup_update=Mock(), ExportProgress=Mock(),
                     make_output_folder=Mock(return_value=Path('/project/output')), prepare_capture_folders=Mock(),
                     collect_project_model=Mock(return_value={}), ensure_view_triad=Mock(return_value=[]),
                     activate_first_mcs=Mock(), run=Mock(side_effect=render), fit_final_view=Mock(side_effect=fit),
                     collect_document_assets=Mock(),
                     finalize_output=Mock(return_value=Path('/project/card.html'), side_effect=None if publish else RuntimeError('write failed')),
                     cleanup_work_files=Mock(return_value=[]), copy_card_path=Mock(return_value=''), open_output_folder=Mock(return_value=[]))
        modules = {'NXOpen': m.nx, **{'NXOpen.' + name: getattr(m.nx, name) for name in ('CAM', 'UF', 'Assemblies', 'Facet', 'Gateway')}}
        with patch.dict(sys.modules, modules), patch.multiple(CARD, **mocks):
            CARD.main()
        return observed, mocks

    def test_main_prepares_before_html_and_retains_edits_only_after_publish(self):
        observed, mocks = self.run_main()
        self.assertEqual(observed, [(['01_ROUGH_Z-3.125', '02_FINISH', 'OUTSIDE'], '⌀6')])
        mocks['finalize_output'].assert_called_once()
        self.assertFalse(self.m.undone)
        self.assertIn('Zmin:', self.m.ui.NXMessageBox.Show.call_args.args[2])

    def test_main_html_write_failure_rolls_back_preparation(self):
        original = [op.Name for op in self.m.ops]
        observed, mocks = self.run_main(publish=False)
        self.assertTrue(observed)
        self.assertEqual([op.Name for op in self.m.ops], original)
        self.assertEqual(self.m.tools[0].general, 'General old')
        self.assertFalse(self.m.marks)
        self.assertIn('write failed', self.m.ui.NXMessageBox.Show.call_args.args[2])

    def test_main_cancel_in_component_picker_never_prepares_or_creates_output(self):
        observed, mocks = self.run_main(cancel=True)
        self.assertFalse(observed or self.m.marks or self.m.writes)
        mocks['make_output_folder'].assert_not_called()
        mocks['finalize_output'].assert_not_called()
        self.m.ui.NXMessageBox.Show.assert_not_called()


if __name__ == '__main__':
    unittest.main()
