"""Menu choices survive NX sessions without mixing users, parts or setups."""
from concurrent.futures import ThreadPoolExecutor
import copy
import ctypes
import importlib.util
import json
from pathlib import Path
import tempfile
import threading
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock, patch


SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Setup_Prototype.py'
SPEC = importlib.util.spec_from_file_location('setup_card_preferences_test', SOURCE)
CARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CARD)


def setup_rows(offset=0):
    """Names survive reopening, while the NX tags deliberately change."""
    rows = []
    for key, name, ancestors, path, count in (
            (1, 'PROGRAM', (), ('PROGRAM',), 3),
            (2, 'Установ 1', (1,), ('PROGRAM', 'Установ 1'), 1),
            (3, 'Установ 2', (1,), ('PROGRAM', 'Установ 2'), 2),
            (4, 'Подпапка', (1, 3), ('PROGRAM', 'Установ 2', 'Подпапка'), 1),
            (5, 'Пустая', (1,), ('PROGRAM', 'Пустая'), 0)):
        keys = tuple(str(value + offset) for value in ancestors)
        rows.append(dict(key=str(key + offset), name=name, ancestors=keys,
                         parent=keys[-1] if keys else None, path=path,
                         operation_count=count, object=NS(Tag=key + offset, Name=name)))
    return rows


def component_rows(offset=0):
    fixture = ('FIXTURE_1', 'Оснастка', 'fixture.prt')
    clamp = ('CLAMP_1', 'Прижим', 'clamp.prt')
    spare = ('CLAMP_2', 'Прижим', 'clamp.prt')
    stock = ('STOCK_1', 'Заготовка', 'stock.prt')
    rows = []
    for key, name, ancestors, memory_path in (
            (10, 'Оснастка [FIXTURE_1]', (), (fixture,)),
            (11, 'Прижим [CLAMP_1]', (10,), (fixture, clamp)),
            (12, 'Прижим [CLAMP_2]', (10,), (fixture, spare)),
            (13, 'Заготовка [STOCK_1]', (), (stock,))):
        rows.append(dict(key=str(key + offset), name=name, path=name,
                         memory_path=memory_path,
                         ancestors=tuple(str(value + offset) for value in ancestors),
                         suppressed=False, visible=True, blanked=False))
    return rows


def jobs(rows=None):
    rows = rows or setup_rows()
    return [dict(report=dict(setup_name=row['name'], selected_folder_path=list(row['path'])),
                 context={}) for row in rows[1:3]]


class FakeNativeMenu:
    """Only the Win32 boundary is fake; production initialization/callbacks run."""
    def __init__(self, action):
        self.action, self.ended, self.next_item = action, None, 1000
        self.checks, self.text, self.enabled = {}, {}, {}
        self.user = Mock()
        self.common = Mock()
        self.common.InitCommonControlsEx.return_value = 1
        self.common.SetWindowSubclass.return_value = 1
        self.user.GetActiveWindow.return_value = 1
        self.user.GetDlgItem.side_effect = lambda window, ident: ident
        self.user.SetWindowTextW.side_effect = lambda handle, value: self.text.update({handle: value}) or 1
        self.user.GetWindowTextW.side_effect = self.get_text
        self.user.EnableWindow.side_effect = lambda handle, value: self.enabled.update({handle: bool(value)}) or 1
        self.user.SendMessageW.side_effect = self.send
        self.user.EndDialog.side_effect = self.end
        self.user.DialogBoxIndirectParamW.side_effect = self.modal

    def send(self, handle, message, wp, lp):
        if message == 0xF1:  # BM_SETCHECK
            self.checks[handle] = wp
        if message == 0xF0:  # BM_GETCHECK
            return self.checks.get(handle, 0)
        if message == 0x1132:  # TVM_INSERTITEMW
            self.next_item += 1
            return self.next_item
        return 1

    def get_text(self, handle, buffer, capacity):
        buffer.value = self.text.get(handle, '')[:capacity - 1]
        return len(buffer.value)

    def end(self, window, value):
        self.ended = value
        return 1

    def modal(self, instance, template, owner, callback, parameter):
        callback(1, 0x110, 0, 0)
        if self.ended is None:
            self.action(self, callback)
        return self.ended

    def run(self, options, selected):
        def library(name, **kwargs):
            return self.user if name == 'user32' else self.common

        with patch.object(ctypes, 'WinDLL', side_effect=library, create=True), \
                patch.object(ctypes, 'WINFUNCTYPE', ctypes.CFUNCTYPE, create=True), \
                patch.object(CARD, 'NativeChoiceRows', return_value=NS(paint=lambda pointer: 0)):
            return CARD.show_setup_folders(setup_rows(), options, selected)


class MenuMemoryTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.project = self.root / 'Деталь.prt'
        self.project.write_bytes(b'NX part is never changed by these tests')
        self.preferences = self.root / 'user-preferences.ini'

    def memory(self, user='domain\\alice', project=None, preferences=None):
        return CARD.MenuMemory(project or self.project, user_key=user,
                               preferences_path=preferences or self.preferences)

    def test_reading_new_project_has_no_filesystem_side_effects(self):
        before = {path.name: path.read_bytes() for path in self.root.iterdir()}
        memory = self.memory()
        self.assertEqual(memory.read_options(), CARD.preparation_options())
        self.assertEqual(memory.setup_choices(setup_rows()), [])
        self.assertEqual(memory.component_choices(component_rows(), jobs()), [set(), set()])
        self.assertEqual({path.name: path.read_bytes() for path in self.root.iterdir()}, before)
        self.assertFalse(memory.warnings)

    def test_setup_paths_restore_new_tags_and_drop_deleted_or_unavailable_folders(self):
        memory = self.memory()
        self.assertTrue(memory.save_setup_choices(setup_rows(), ['2', '4']))
        reopened = setup_rows(100)
        self.assertEqual(self.memory().setup_choices(reopened), ['102', '104'])
        reopened[3]['operation_count'] = 0
        self.assertEqual(self.memory().setup_choices(reopened), ['102'])
        self.assertEqual(self.memory().setup_choices(reopened[0:1] + reopened[2:]), [])

    def test_duplicate_setup_paths_are_not_restored_to_an_arbitrary_folder(self):
        memory = self.memory()
        self.assertTrue(memory.save_setup_choices(setup_rows(), ['2']))
        reopened = setup_rows(100)
        duplicate = dict(reopened[1], key='902', object=NS(Tag=902, Name='Установ 1'))
        self.assertEqual(self.memory().setup_choices(reopened + [duplicate]), [])
        self.assertTrue(memory.save_setup_choices(reopened + [duplicate], ['902']))
        self.assertEqual(self.memory().setup_choices(setup_rows()), [])

    def test_component_occurrences_restore_new_tags_and_preserve_other_setup(self):
        memory = self.memory()
        self.assertTrue(memory.save_component_choices(component_rows(), jobs(), [{'10', '11'}, {'13'}]))
        self.assertEqual(self.memory().component_choices(component_rows(200), jobs(setup_rows(100))),
                         [{'210', '211'}, {'213'}])
        # An accepted empty choice is meaningful; only the selected setup changes.
        self.assertTrue(memory.save_component_choices(component_rows(200), jobs()[0:1], [set()]))
        self.assertEqual(self.memory().component_choices(component_rows(), jobs()), [set(), {'13'}])

    def test_unavailable_components_do_not_select_their_sibling_or_revert_to_nx_visibility(self):
        memory = self.memory()
        self.assertTrue(memory.save_component_choices(component_rows(), jobs(), [{'10', '11'}, {'13'}]))
        reopened = component_rows(200)
        reopened[1]['suppressed'] = True
        self.assertEqual(self.memory().component_choices(reopened, jobs()), [{'210'}, {'213'}])
        reopened = [row for row in reopened if row['key'] != '211']
        self.assertEqual(self.memory().component_choices(reopened, jobs()), [{'210'}, {'213'}])

    def test_duplicate_component_occurrence_path_is_ambiguous_but_other_siblings_survive(self):
        memory = self.memory()
        self.assertTrue(memory.save_component_choices(component_rows(), jobs(), [{'10', '11', '12'}, set()]))
        reopened = component_rows(200)
        duplicate = dict(reopened[1], key='999')
        self.assertEqual(self.memory().component_choices(reopened + [duplicate], jobs()),
                         [{'210', '212'}, set()])

    def test_duplicate_ancestor_prevents_binding_even_when_child_path_looks_unique(self):
        memory = self.memory()
        self.assertTrue(memory.save_setup_choices(setup_rows(), ['4']))
        self.assertTrue(memory.save_component_choices(component_rows(), jobs(), [{'10', '11'}, {'13'}]))
        reopened = setup_rows(100)
        self.assertEqual(self.memory().setup_choices(reopened + [dict(reopened[2], key='999')]), [])
        components = component_rows(200)
        self.assertEqual(self.memory().component_choices(components + [dict(components[0], key='999')], jobs()),
                         [set(), {'213'}])

    def test_real_component_inventory_survives_project_move_and_tags_without_loading_suppressed(self):
        def assembly(folder, offset):
            def component(tag, name, display, prototype, children=(), suppressed=False):
                return NS(Tag=tag + offset, Name=name, DisplayName=display,
                          Prototype=NS(FullPath=folder + '\\' + prototype),
                          GetChildren=Mock(return_value=list(children)), IsSuppressed=suppressed, IsBlanked=False)
            # Delimiters inside raw names must not turn into path separators.
            left = component(11, 'CLAMP / LEFT', 'Прижим [A]', 'parts\\clamp.prt')
            right = component(12, 'CLAMP / RIGHT', 'Прижим [A]', 'parts\\clamp.prt')
            parent = component(10, 'FIXTURE', 'Оснастка / 1', 'fixture.prt', [left, right])
            suppressed = component(13, 'HIDDEN', 'Не загружен', 'hidden.prt', suppressed=True)
            suppressed.GetChildren.side_effect = AssertionError('must not load suppressed children')
            part = NS(FullPath=folder + '\\part.prt',
                      ComponentAssembly=NS(RootComponent=NS(GetChildren=lambda: [parent, suppressed])))
            return CARD.component_inventory(part), suppressed

        original, suppressed = assembly('C:\\work\\project', 0)
        self.assertTrue(self.memory().save_component_choices(original, jobs(), [{'10', '11'}, set()]))
        moved, moved_suppressed = assembly('D:\\moved\\project', 500)
        self.assertEqual(self.memory().component_choices(moved, jobs()), [{'510', '511'}, set()])
        self.assertNotEqual(moved[1]['memory_path'], moved[2]['memory_path'])
        self.assertEqual(moved[1]['memory_path'][-1][0], 'CLAMP / LEFT')
        self.assertTrue(moved[1]['memory_path'][-1][2].startswith('project:'))
        suppressed.GetChildren.assert_not_called()
        moved_suppressed.GetChildren.assert_not_called()

    def test_real_native_startup_callback_restores_controls_and_keeps_disabled_last_model_values(self):
        values = dict(CARD.preparation_options(), number_operations=True, update_descriptions=True,
                      include_tool_numbers=True, add_zmin=True, create_project_model=True,
                      project_model_accuracy=.05, project_model_triangle_limit=1200000)

        def action(fake, callback):
            self.assertEqual([fake.checks[index] for index in (230, 231, 232, 233, 234, 235)],
                             [1, 1, 0, 1, 1, 1])
            self.assertEqual(CARD.parse_project_model_accuracy(fake.text[237]), .05)
            self.assertEqual(CARD.parse_project_model_triangle_limit(fake.text[239]), 1200000)
            self.assertIn('1', fake.text[102])
            fake.checks[235] = 0
            fake.text.update({237: 'invalid hidden edit', 239: '-1'})
            callback(1, 0x111, 235, 0)
            callback(1, 0x111, 1, 0)

        self.assertEqual(FakeNativeMenu(action).run(values, ['2']), ['2'])
        self.assertFalse(values['create_project_model'])
        self.assertEqual(values['project_model_accuracy'], .05)
        self.assertEqual(values['project_model_triangle_limit'], 1200000)

        before = copy.deepcopy(values)
        def cancel(fake, callback):
            fake.checks.update({230: 0, 235: 1})
            fake.text[237] = '.5'
            callback(1, 0x111, 2, 0)
        self.assertIsNone(FakeNativeMenu(cancel).run(values, ['3']))
        self.assertEqual(values, before)

    def test_users_and_parts_are_isolated_and_unselected_scopes_are_preserved(self):
        other = self.root / 'Другая.prt'
        other.write_bytes(b'another part')
        alice, bob = self.memory(), self.memory('domain\\bob')
        other_part = self.memory(project=other)
        self.assertTrue(alice.save_setup_choices(setup_rows(), ['2']))
        self.assertTrue(alice.save_component_choices(component_rows(), jobs(), [{'10', '11'}, {'13'}]))
        self.assertTrue(bob.save_setup_choices(setup_rows(), ['3']))
        self.assertTrue(bob.save_component_choices(component_rows(), jobs(), [{'12'}, set()]))
        self.assertTrue(other_part.save_setup_choices(setup_rows(), ['4']))
        self.assertEqual(self.memory().setup_choices(setup_rows()), ['2'])
        self.assertEqual(self.memory('domain\\bob').setup_choices(setup_rows()), ['3'])
        self.assertEqual(self.memory(project=other).setup_choices(setup_rows()), ['4'])
        self.assertEqual(self.memory().component_choices(component_rows(), jobs()), [{'10', '11'}, {'13'}])
        self.assertEqual(self.memory('domain\\bob').component_choices(component_rows(), jobs()), [{'10', '12'}, set()])
        self.assertEqual(self.memory(project=other).component_choices(component_rows(), jobs()), [set(), set()])

    def test_options_follow_user_across_projects_and_retain_valid_disabled_model_values(self):
        alice = self.memory()
        bob_path = self.root / 'bob-preferences.ini'
        bob = self.memory('domain\\bob', preferences=bob_path)
        values = dict(CARD.preparation_options(), number_operations=True,
                      update_descriptions=True, include_tool_numbers=True, add_zmin=True,
                      create_project_model=True, project_model_accuracy=.05,
                      project_model_triangle_limit=1200000)
        self.assertTrue(alice.save_options(values))
        values['create_project_model'] = False
        self.assertTrue(alice.save_options(values))
        other = self.root / 'Другая.prt'
        self.assertEqual(self.memory(project=other).read_options(), values)
        self.assertEqual(bob.read_options(), CARD.preparation_options())
        self.assertTrue(bob.save_options(dict(CARD.preparation_options(), add_zmin=True)))
        self.assertEqual(self.memory().read_options(), values)

    def test_invalid_ini_fields_fall_back_individually_without_losing_unknown_sections(self):
        self.preferences.write_text(
            '[Unrelated]\ntext = keep % literal\n\n[Preparation]\n'
            'number_operations = true\nupdate_descriptions = definitely-not-a-bool\n'
            'include_tool_numbers = false\nadd_zmin = true\ncreate_project_model = false\n'
            'project_model_accuracy = NaN\nproject_model_triangle_limit = -7\n', encoding='utf-8')
        memory = self.memory()
        values = memory.read_options()
        self.assertTrue(values['number_operations'])
        self.assertTrue(values['add_zmin'])
        self.assertFalse(values['update_descriptions'])
        self.assertEqual(values['project_model_accuracy'], 1.)
        self.assertEqual(values['project_model_triangle_limit'], CARD.PROJECT_MODEL_DEFAULT_TRIANGLES)
        values.update(project_model_accuracy=.1, project_model_triangle_limit=987654)
        self.assertTrue(memory.save_options(values))
        parser = CARD.configparser.ConfigParser(interpolation=None)
        parser.read(self.preferences, encoding='utf-8-sig')
        self.assertEqual(parser['Unrelated']['text'], 'keep % literal')
        self.assertEqual(self.memory().read_options(), values)

    def test_malformed_ini_and_unknown_project_format_are_never_overwritten(self):
        memory = self.memory()
        bad_ini = b'not an INI section\nproject_model_accuracy = 0.05\n'
        self.preferences.write_bytes(bad_ini)
        self.assertEqual(memory.read_options(), CARD.preparation_options())
        self.assertFalse(memory.save_options(dict(CARD.preparation_options(), add_zmin=True)))
        self.assertEqual(self.preferences.read_bytes(), bad_ini)
        self.assertTrue(memory.warnings)
        for payload in (b'{broken', json.dumps({'format': 'another-app', 'version': 1, 'users': {}}).encode(),
                        json.dumps({'format': 'nx-setup-menu-state', 'version': 999, 'users': {}}).encode(),
                        json.dumps({'format': 'nx-setup-menu-state', 'version': 1, 'users': []}).encode()):
            with self.subTest(payload=payload):
                memory.project_path.write_bytes(payload)
                other = self.memory()
                self.assertEqual(other.setup_choices(setup_rows()), [])
                self.assertFalse(other.save_setup_choices(setup_rows(), ['2']))
                self.assertFalse(other.save_component_choices(component_rows(), jobs(), [set(), set()]))
                self.assertEqual(memory.project_path.read_bytes(), payload)
                self.assertTrue(other.warnings)

    def test_stale_instances_merge_concurrent_users_without_sidecar_files(self):
        users = ['domain\\writer-%d' % index for index in range(6)]
        memories = [self.memory(user) for user in users]
        barrier = threading.Barrier(len(memories))

        def write(index):
            memory = memories[index]
            # All instances have observed the same initially empty project.
            self.assertEqual(memory.setup_choices(setup_rows()), [])
            barrier.wait(timeout=5)
            return memory.save_component_choices(component_rows(), jobs(), [{'10', '11'}, {'13'}])

        with ThreadPoolExecutor(max_workers=len(memories)) as pool:
            self.assertEqual(list(pool.map(write, range(len(memories)))), [True] * len(memories))
        for user in users:
            self.assertEqual(self.memory(user).component_choices(component_rows(), jobs()),
                             [{'10', '11'}, {'13'}])
        self.assertEqual({path.name for path in self.root.iterdir()},
                         {self.project.name, memories[0].project_path.name})
        self.assertEqual(self.project.read_bytes(), b'NX part is never changed by these tests')

    def test_write_failure_keeps_previous_choices_and_leaves_no_sidecars(self):
        memory = self.memory()
        self.assertTrue(memory.save_setup_choices(setup_rows(), ['2']))
        original = memory.project_path.read_bytes()
        before = {path.name for path in self.root.iterdir()}
        with patch.object(CARD.os, 'fsync', side_effect=[OSError('injected disk failure'), None]):
            self.assertFalse(memory.save_setup_choices(setup_rows(), ['3']))
        self.assertEqual(memory.project_path.read_bytes(), original)
        self.assertEqual({path.name for path in self.root.iterdir()}, before)
        self.assertEqual(self.memory().setup_choices(setup_rows()), ['2'])
        self.assertTrue(memory.warnings)

    def test_read_only_settings_still_restore_choices_and_failed_saves_are_nonfatal(self):
        memory = self.memory()
        options = dict(CARD.preparation_options(), add_zmin=True)
        self.assertTrue(memory.save_options(options))
        self.assertTrue(memory.save_setup_choices(setup_rows(), ['2']))
        before = {path.name: path.read_bytes() for path in self.root.iterdir()}
        # Unlike chmod under a privileged test runner, denial at the write-open
        # boundary reliably models a shared read-only project/settings file.
        with patch.object(CARD.os, 'open', side_effect=PermissionError('read-only settings')):
            reopened = self.memory()
            self.assertEqual(reopened.read_options(), options)
            self.assertEqual(reopened.setup_choices(setup_rows()), ['2'])
            self.assertFalse(reopened.save_options(CARD.preparation_options()))
            self.assertFalse(reopened.save_setup_choices(setup_rows(), ['3']))
        self.assertEqual({path.name: path.read_bytes() for path in self.root.iterdir()}, before)
        self.assertTrue(reopened.warnings)

    def fake_nx(self):
        ui = NS(LockUgAccess=Mock(), UnlockUgAccess=Mock())
        nx = NS(UF=NS(UFSession=NS(GetUFSession=lambda: NS(Ui=ui)),
                      UFConstants=NS(UF_UI_FROM_CUSTOM=77)),
                CAM=NS(CAMSetup=NS(View=NS(ProgramOrder=1))))
        part = NS(CAMSetup=NS(GetRoot=lambda view: 'program-root'))
        return nx, part, ui

    def test_setup_dialog_restores_values_and_commits_only_accepted_choices(self):
        memory = self.memory()
        values = dict(CARD.preparation_options(), add_zmin=True, project_model_accuracy=.05)
        self.assertTrue(memory.save_options(values))
        self.assertTrue(memory.save_setup_choices(setup_rows(), ['2']))
        before = {path.name: path.read_bytes() for path in self.root.iterdir()}
        nx, part, ui = self.fake_nx()
        observed = []

        def dialog(rows, options=None, selected=None):
            observed.append((copy.deepcopy(options), list(selected)))
            options.update(add_zmin=False, number_operations=True)
            return None

        with patch.object(CARD, 'cam_tree', return_value='program'), \
                patch.object(CARD, 'setup_folder_inventory', return_value=setup_rows(100)), \
                patch.object(CARD, 'show_setup_folders', side_effect=dialog):
            with self.assertRaises(CARD.ExportCancelled):
                CARD.ask_setup_folders(nx, part, memory)
        self.assertEqual(observed, [(values, ['102'])])
        self.assertEqual({path.name: path.read_bytes() for path in self.root.iterdir()}, before)
        ui.UnlockUgAccess.assert_called_once_with(77)

        def accepted(rows, options=None, selected=None):
            options.update(add_zmin=False, number_operations=True)
            return ['103']

        with patch.object(CARD, 'cam_tree', return_value='program'), \
                patch.object(CARD, 'setup_folder_inventory', return_value=setup_rows(100)), \
                patch.object(CARD, 'show_setup_folders', side_effect=accepted):
            program, selected, options = CARD.ask_setup_folders(nx, part, memory)
        self.assertEqual(program, 'program')
        self.assertEqual([obj.Tag for obj in selected], [103])
        self.assertTrue(self.memory().read_options()['number_operations'])
        self.assertFalse(self.memory().read_options()['add_zmin'])
        self.assertEqual(self.memory().setup_choices(setup_rows()), ['3'])

    def test_component_dialog_cancel_and_no_do_not_replace_previously_confirmed_choices(self):
        memory = self.memory()
        self.assertTrue(memory.save_component_choices(component_rows(), jobs(), [{'10', '11'}, {'13'}]))
        before = memory.project_path.read_bytes()
        nx, _, ui = self.fake_nx()
        display = NS(rows=component_rows(100), apply=Mock(), restore=Mock(return_value=[]))
        with patch.object(CARD, 'ask_warning', return_value=False), \
                patch.object(CARD, 'show_all_setup_components') as show:
            self.assertEqual(CARD.ask_all_setup_components(nx, display, jobs(), memory), [set(), set()])
            show.assert_not_called()
        self.assertEqual(memory.project_path.read_bytes(), before)
        with patch.object(CARD, 'ask_warning', return_value=True), \
                patch.object(CARD, 'show_all_setup_components', return_value=None) as show:
            with self.assertRaises(CARD.ExportCancelled):
                CARD.ask_all_setup_components(nx, display, jobs(), memory)
        self.assertEqual(show.call_args.kwargs['selected'], [{'110', '111'}, {'113'}])
        display.restore.assert_called_once()
        ui.UnlockUgAccess.assert_called_once_with(77)
        self.assertEqual(memory.project_path.read_bytes(), before)

    def test_component_dialog_accepts_explicit_empty_and_restoration_failure_does_not_commit(self):
        memory = self.memory()
        self.assertTrue(memory.save_component_choices(component_rows(), jobs(), [{'10', '11'}, {'13'}]))
        nx, _, _ = self.fake_nx()
        display = NS(rows=component_rows(), apply=Mock(), restore=Mock(return_value=[]))
        with patch.object(CARD, 'ask_warning', return_value=True), \
                patch.object(CARD, 'show_all_setup_components', return_value=[set(), {'13'}]):
            self.assertEqual(CARD.ask_all_setup_components(nx, display, jobs(), memory), [set(), {'13'}])
        self.assertEqual(self.memory().component_choices(component_rows(), jobs()), [set(), {'13'}])
        before = memory.project_path.read_bytes()
        display.restore.return_value = ['injected NX visibility failure']
        with patch.object(CARD, 'ask_warning', return_value=True), \
                patch.object(CARD, 'show_all_setup_components', return_value=[{'10'}, set()]):
            with self.assertRaisesRegex(RuntimeError, 'visibility failure'):
                CARD.ask_all_setup_components(nx, display, jobs(), memory)
        self.assertEqual(memory.project_path.read_bytes(), before)


if __name__ == '__main__':
    unittest.main()
