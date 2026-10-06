"""Exercise the actual table handlers and row identity without requiring NX."""
import ast
import copy
import ctypes
import importlib.util
import os
import subprocess
import sys
from pathlib import Path
import queue
from types import SimpleNamespace
import unittest
from unittest.mock import Mock

SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Update_Scripts.py'
SPEC = importlib.util.spec_from_file_location('updater_table', SOURCE)
u = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(u)
WINDOW = next(node for node in ast.parse(SOURCE.read_text(encoding='utf-8')).body
              if isinstance(node, ast.FunctionDef) and node.name == 'run_window')
NODES = {node.name: node for node in WINDOW.body if isinstance(node, (ast.ClassDef, ast.FunctionDef))}


class Point(ctypes.Structure):
    _fields_ = [('x', ctypes.c_int32), ('y', ctypes.c_int32)]


def record(name, installed='V1.09', available='V1.10', selected=False, checked=True, eligible=True):
    return dict(path='C:\\NX_SCRIPTS\\' + name + '.py', titles=[name],
                installed_label=installed, available_label=available,
                status='Новая версия' if eligible else 'Установлена эта версия',
                operation='update', new_path='source/' + name + '.py',
                selected=selected, checked=checked, eligible=eligible)


class NativeTable:
    """Win32 message contract: indices refer to the current native row order."""
    def __init__(self, env):
        self.env = env
        self.items, self.headers, self.text = [], {}, {}
        self.highlight = -1
        self.hit = (-1, -1, 1)
        self.hit_point = None
        self.redraw = []
        self.repaints = 0
        self.last_visible = None
        self.fail_headers = False
        self.delete_notification = False

    def SendMessageW(self, hwnd, msg, wp, lp):
        if msg == 0xB:  # WM_SETREDRAW
            self.redraw.append(wp)
        elif msg == 4108:  # LVM_GETNEXTITEM / LVNI_SELECTED
            return self.highlight
        elif msg == 4153:  # LVM_SUBITEMHITTEST
            hit = self.env['LVHITTESTINFO'].from_address(lp)
            self.hit_point = (hit.pt.x, hit.pt.y)
            hit.iItem, hit.iSubItem, hit.flags = self.hit
            return hit.iItem
        elif msg == 4105:  # LVM_DELETEALLITEMS
            if self.delete_notification:
                # Notifications while rebuilding must not mutate the newly sorted model.
                note = self.env['NMLISTVIEW']()
                note.hdr.hwndFrom, note.hdr.code = 101, -101
                note.iItem, note.uChanged, note.uOldState, note.uNewState = 0, 8, 0x2000, 0x1000
                self.env['window_proc'](100, 0x4E, 0, ctypes.addressof(note))
            self.items, self.highlight = [], -1
        elif msg == 4173:  # LVM_INSERTITEMW
            item = self.env['LVITEM'].from_address(lp)
            self.items.insert(item.iItem, {'values': [item.pszText, '', '', '', ''], 'checked': False})
            return item.iItem
        elif msg == 4212:  # LVM_SETITEMW
            item = self.env['LVITEM'].from_address(lp)
            self.items[item.iItem]['values'][item.iSubItem] = item.pszText
        elif msg == 4139:  # LVM_SETITEMSTATE
            item = self.env['LVITEM'].from_address(lp)
            if item.stateMask & 0xF000:
                self.items[wp]['checked'] = item.state & 0xF000 == 0x2000
            if item.stateMask & 3:
                self.highlight = wp
        elif msg == 4115:  # LVM_ENSUREVISIBLE
            self.last_visible = wp
        elif msg == 4192:  # LVM_SETCOLUMNW / LVCF_TEXT
            if self.fail_headers:
                raise RuntimeError('header failed')
            column = self.env['LVCOLUMN'].from_address(lp)
            if column.mask != 4:
                raise AssertionError('Expected a column text update')
            self.headers[wp] = column.pszText
        else:
            raise AssertionError('Unexpected Win32 message: ' + str(msg))
        return 1

    def SetWindowTextW(self, hwnd, text):
        self.text[hwnd] = text
        return 1

    def RedrawWindow(self, *args):
        self.repaints += 1
        return 1

    def DefWindowProcW(self, *args):
        return 777


class TableTests(unittest.TestCase):
    def handlers(self, rows):
        state = dict(rows=rows, busy=False, closed=False, applying=False, populating=False,
                     syncing_checks=False, sort_column=None, sort_reverse=False,
                     settings={'working_folder': 'C:\\NX_SCRIPTS', 'config_path': 'updater.ini'})
        env = vars(u).copy()
        # Model the Windows LLP64 layout even when these tests run on Linux.
        env.update(W=SimpleNamespace(HWND=ctypes.c_void_p, UINT=ctypes.c_uint32,
                                     LPWSTR=ctypes.c_wchar_p, POINT=Point),
                   LPARAM=ctypes.c_ssize_t, WPARAM=ctypes.c_size_t,
                   state=state, controls={'list': 101, 'details': 102, 'status': 103},
                   message=Mock(), result_queue=queue.Queue(),
                   busy=lambda value: state.update(busy=value))
        names = ('NMHDR', 'NMLISTVIEW', 'LVHITTESTINFO', 'LVITEM', 'LVCOLUMN',
                 'checked', 'paint_check', 'set_checked', 'highlighted_row', 'show_row_details',
                 'toggle_row', 'handle_list_key', 'handle_list_mouse', 'update_sort_headers',
                 'rebuild_list', 'sort_by_column', 'selection_status', 'render_results', 'window_proc')
        nodes = [copy.deepcopy(NODES[name]) for name in names]
        for node in nodes:
            node.decorator_list = []
        exec(compile(ast.Module(body=nodes, type_ignores=[]), str(SOURCE), 'exec'), env)
        native = NativeTable(env)
        env['U'] = native
        env['rebuild_list']()
        return state, native, env

    def notify(self, env, code, item=-1, column=0, old=0, new=0, sender=101):
        note = env['NMLISTVIEW']()
        note.hdr.hwndFrom, note.hdr.code = sender, code
        note.iItem, note.iSubItem = item, column
        note.uChanged, note.uOldState, note.uNewState = 8, old, new
        return env['window_proc'](100, 0x4E, 0, ctypes.addressof(note))

    def mouse(self, env, x=123, y=45, hwnd=101, msg=0x203):
        return env['handle_list_mouse'](SimpleNamespace(hWnd=hwnd, message=msg,
                                                       lParam=((y & 0xFFFF) << 16) | (x & 0xFFFF)))

    def test_version_columns_sort_numerically_in_both_directions(self):
        versions = ['V1.100', 'V1.10', 'V1.09 *', 'V2', 'V1']
        for column in (2, 3):
            with self.subTest(column=column):
                state, native, env = self.handlers([record(str(i), v, v) for i, v in enumerate(versions)])
                self.notify(env, -108, column=column)
                self.assertEqual([u.result_values(row)[column] for row in state['rows']],
                                 ['V1', 'V1.09 *', 'V1.10', 'V1.100', 'V2'])
                self.assertTrue(native.headers[column].endswith(' ↑'))
                self.notify(env, -108, column=column)
                self.assertEqual([u.result_values(row)[column] for row in state['rows']],
                                 ['V2', 'V1.100', 'V1.10', 'V1.09 *', 'V1'])
                self.assertTrue(native.headers[column].endswith(' ↓'))

    def test_sort_preserves_user_choices_focus_and_native_row_mapping(self):
        rows = [record('Zulu', selected=True, checked=False), record('alpha', selected=False, checked=True),
                record('Бета', selected=True, checked=False)]
        state, native, env = self.handlers(rows)
        focused = rows[0]
        native.highlight, native.delete_notification = 0, True
        for column in (0, 0, 1, 2, 3, 4):
            self.notify(env, -108, column=column)
            self.assertTrue(focused['selected'])
            self.assertEqual([row['selected'] for row in rows], [True, False, True])
            self.assertIs(state['rows'][native.highlight], focused)
            self.assertEqual(native.last_visible, native.highlight)
            self.assertEqual([item['checked'] for item in native.items],
                             [row['selected'] for row in state['rows']])
            self.assertEqual([item['values'] for item in native.items],
                             [list(u.result_values(row)) for row in state['rows']])
            self.assertIn(focused['path'], native.text[102])
        env['message'].assert_not_called()

    def test_text_sort_is_case_insensitive_and_new_column_starts_ascending(self):
        rows = [record('zulu'), record('ALPHA'), record('beta')]
        state, native, env = self.handlers(rows)
        self.notify(env, -108, column=0)
        self.assertEqual([row['titles'][0] for row in state['rows']], ['ALPHA', 'beta', 'zulu'])
        self.notify(env, -108, column=0)
        self.notify(env, -108, column=1)
        self.assertFalse(state['sort_reverse'])
        self.assertEqual(native.headers[0], 'Скрипт')
        self.assertEqual(native.headers[1], 'Рабочий файл ↑')

    def test_missing_versions_and_equal_keys_sort_without_resetting_choices(self):
        rows = [record('a', 'нет версии'), record('b', 'Не установлен'), record('c', '—'),
                record('d', 'V1.01'), record('e', 'V1.01', selected=True)]
        state, native, env = self.handlers(rows)
        self.notify(env, -108, column=2)
        self.assertEqual(state['rows'][:2], rows[3:])
        self.notify(env, -108, column=2)
        self.assertEqual(state['rows'][-2:], rows[3:])
        self.assertTrue(rows[-1]['selected'])
        a, b = record('a', selected=True), record('b')
        self.assertEqual(u.result_sort_key(a, 4), u.result_sort_key(b, 4))

    def test_new_scan_keeps_sort_order_but_initializes_fresh_selection(self):
        state, native, env = self.handlers([record('before')])
        self.notify(env, -108, column=1)
        self.notify(env, -108, column=1)
        fresh = [record('Alpha', selected=True, checked=False), record('Zulu', selected=False, checked=True)]
        env['result_queue'].put((fresh, None, ('inputs',)))
        env['render_results']()
        self.assertEqual([row['titles'][0] for row in state['rows']], ['Zulu', 'Alpha'])
        self.assertEqual([item['checked'] for item in native.items], [True, False])
        self.assertEqual(state['inputs'], ('inputs',))

    def test_double_click_any_column_toggles_only_the_hit_row(self):
        for column in range(5):
            with self.subTest(column=column):
                rows = [record('a'), record('b')]
                state, native, env = self.handlers(rows)
                native.highlight, native.hit = 0, (1, column, 4)
                self.assertTrue(self.mouse(env))
                self.assertEqual([row['selected'] for row in rows], [False, True])
                self.assertTrue(self.mouse(env))
                self.assertEqual([row['selected'] for row in rows], [False, False])
                env['message'].assert_not_called()

    def test_checkbox_first_click_remains_and_double_click_does_not_toggle_again(self):
        for selected in (False, True):
            with self.subTest(selected=selected):
                row = record('a', selected=selected)
                state, native, env = self.handlers([row])
                old, new = (0x2000, 0x1000) if selected else (0x1000, 0x2000)
                self.assertEqual(self.notify(env, -100, item=0, old=old, new=new), 0)
                self.notify(env, -101, item=0, old=old, new=new)
                native.hit = (0, 0, 8)  # LVHT_ONITEMSTATEICON
                self.assertTrue(self.mouse(env))
                self.assertEqual(row['selected'], not selected)

    def test_blank_space_other_controls_single_click_and_signed_coordinates(self):
        row = record('a', selected=True)
        state, native, env = self.handlers([row])
        native.highlight = 0
        self.assertTrue(self.mouse(env, x=-2, y=-3))
        self.assertEqual(native.hit_point, (-2, -3))
        self.assertFalse(self.mouse(env, hwnd=999))
        self.assertFalse(self.mouse(env, msg=0x201))
        self.assertTrue(row['selected'])
        env['message'].assert_not_called()

    def test_busy_populating_closed_and_ineligible_rows_are_protected(self):
        for guard in ('busy', 'populating', 'closed'):
            state, native, env = self.handlers([record('b'), record('a')])
            native.hit, state[guard] = (0, 1, 4), True
            self.notify(env, -108, column=0)
            self.assertTrue(self.mouse(env))
            self.assertIsNone(state['sort_column'])
            self.assertFalse(any(row['selected'] for row in state['rows']))
        row = record('locked', eligible=False)
        state, native, env = self.handlers([row])
        native.hit = (0, 2, 4)
        self.mouse(env)
        self.assertFalse(row['selected'])
        env['message'].assert_called_once()

    def test_column_sender_and_bounds_are_validated(self):
        state, native, env = self.handlers([record('b'), record('a')])
        self.assertEqual(self.notify(env, -108, column=0, sender=999), 777)
        for column in (-1, 5, 1000):
            self.notify(env, -108, column=column)
        self.assertIsNone(state['sort_column'])

    def test_redraw_and_guard_restored_on_render_failure(self):
        state, native, env = self.handlers([record('a', selected=True)])
        native.fail_headers = True
        with self.assertRaisesRegex(RuntimeError, 'header failed'):
            env['rebuild_list']()
        self.assertFalse(state['populating'])
        self.assertEqual(native.redraw[-2:], [0, 1])
        self.assertTrue(state['rows'][0]['selected'])

    @unittest.skipUnless(os.name == 'nt', 'Requires native Windows list-view controls')
    def test_real_windows_double_click_text_and_checkbox_each_toggle_once(self):
        """Verify native hit flags and the remaining mouse-up after consuming a double-click."""
        if os.environ.get('NX_UPDATER_TABLE_PROBE') != '1':
            probe_env = dict(os.environ, NX_UPDATER_TABLE_PROBE='1')
            result = subprocess.run([sys.executable, '-B', str(Path(__file__).resolve()),
                                     'TableTests.test_real_windows_double_click_text_and_checkbox_each_toggle_once', '-v'],
                                    env=probe_env, capture_output=True, text=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            return
        import faulthandler
        faulthandler.dump_traceback_later(20, exit=True)
        self.addCleanup(faulthandler.cancel_dump_traceback_later)
        from ctypes import wintypes as W
        state, _, env = self.handlers([record('Example')])
        api = ctypes.WinDLL('user32', use_last_error=True)
        common = ctypes.WinDLL('comctl32', use_last_error=True)
        common.InitCommonControlsEx.argtypes = [ctypes.c_void_p]
        common.InitCommonControlsEx.restype = W.BOOL
        self.assertTrue(common.InitCommonControlsEx(ctypes.byref((ctypes.c_uint32 * 2)(8, 1))))
        signatures = {
            'CreateWindowExW': (W.HWND, [W.DWORD, W.LPCWSTR, W.LPCWSTR, W.DWORD,
                                        ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
                                        W.HWND, W.HMENU, W.HINSTANCE, ctypes.c_void_p]),
            'SendMessageW': (ctypes.c_ssize_t, [W.HWND, W.UINT, ctypes.c_size_t, ctypes.c_ssize_t]),
            'PostMessageW': (W.BOOL, [W.HWND, W.UINT, ctypes.c_size_t, ctypes.c_ssize_t]),
            'PeekMessageW': (W.BOOL, [ctypes.POINTER(W.MSG), W.HWND, W.UINT, W.UINT, W.UINT]),
            'TranslateMessage': (W.BOOL, [ctypes.POINTER(W.MSG)]),
            'DispatchMessageW': (ctypes.c_ssize_t, [ctypes.POINTER(W.MSG)]),
            'SetWindowTextW': (W.BOOL, [W.HWND, W.LPCWSTR]),
            'RedrawWindow': (W.BOOL, [W.HWND, ctypes.c_void_p, W.HANDLE, W.UINT]),
            'DestroyWindow': (W.BOOL, [W.HWND]),
        }
        for name, (result, arguments) in signatures.items():
            getattr(api, name).restype, getattr(api, name).argtypes = result, arguments
        parent = api.CreateWindowExW(0, 'STATIC', '', 0x80000000, 0, 0, 900, 260,
                                     None, None, None, None)
        self.assertTrue(parent)
        try:
            handle = api.CreateWindowExW(0, 'SysListView32', '', 0x50000000 | 1 | 4 | 8,
                                         0, 0, 900, 220, parent, 101, None, None)
            self.assertTrue(handle)
            env['U'] = api
            env['controls'] = {'list': handle, 'details': parent, 'status': parent}
            api.SendMessageW(handle, 4150, 0, 1 | 4 | 0x20 | 0x10000)
            for index, (label, width) in enumerate(u.RESULT_COLUMNS):
                buffer = ctypes.create_unicode_buffer(label)
                column = env['LVCOLUMN'](mask=7, cx=width, pszText=ctypes.cast(buffer, ctypes.c_wchar_p))
                api.SendMessageW(handle, 4193, index, ctypes.addressof(column))
            env['rebuild_list']()
            rect = W.RECT()
            self.assertTrue(api.SendMessageW(handle, 4110, 0, ctypes.addressof(rect)))
            y = (rect.top + rect.bottom) // 2
            checkbox_x = None
            for x in range(40):
                hit = env['LVHITTESTINFO'](pt=Point(x, y))
                index = api.SendMessageW(handle, 4153, 0, ctypes.addressof(hit))
                if index == 0 and hit.iSubItem == 0 and hit.flags & 8:
                    checkbox_x = x
                    break
            self.assertIsNotNone(checkbox_x, 'Native hit test must identify the checkbox')

            def native_checked():
                return api.SendMessageW(handle, 4140, 0, 0xF000) & 0xF000 == 0x2000

            def mouse_sequence(messages, position):
                # Queue releases in advance: native drag detection may pump messages
                # inside WM_LBUTTONDOWN and must not wait for a synchronous caller.
                for message in messages:
                    self.assertTrue(api.PostMessageW(handle, message, 0 if message == 0x202 else 1, position))
                queued = W.MSG()
                while api.PeekMessageW(ctypes.byref(queued), None, 0, 0, 1):
                    if env['handle_list_mouse'](queued):
                        continue
                    api.TranslateMessage(ctypes.byref(queued))
                    api.DispatchMessageW(ctypes.byref(queued))

            for x, checkbox in ((u.RESULT_COLUMNS[0][1] + 20, False), (checkbox_x, True)):
                with self.subTest(checkbox=checkbox):
                    env['set_checked'](0, False)
                    position = (y << 16) | x
                    mouse_sequence((0x201, 0x202), position)
                    self.assertEqual(native_checked(), checkbox)
                    if checkbox:
                        # The STATIC test parent has no notification handler; replay its actual change.
                        self.notify(env, -101, item=0, old=0x1000, new=0x2000, sender=handle)
                    mouse_sequence((0x203, 0x202), position)
                    self.assertTrue(native_checked())
                    self.assertTrue(state['rows'][0]['selected'])
        finally:
            api.DestroyWindow(parent)


if __name__ == '__main__':
    unittest.main()
