"""Green update cells: model comparison, actual handlers and native Windows pixels."""
import ctypes
import os
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import Mock

import test_updater_table as table

u = table.u


def record(name='Example', installed='V1.09', available='V1.10', **changes):
    row = table.record(name, installed or 'нет версии', available or '—')
    row.update(identity=((name.casefold(), '.py'), u.parse_version(installed)[0] if installed else None, ''),
               available_version=u.parse_version(available)[0] if available else None)
    row.update(changes)
    return row


class DrawingApi:
    def __init__(self, native):
        self.native = native
        self.bounds = (745, 25, 860, 44)
        self.rect_ok = True
        self.fills = []
        self.texts = []
        self.fail_text = False

    def __getattr__(self, name):
        return getattr(self.native, name)

    def SendMessageW(self, hwnd, msg, wp, lp):
        if msg == 4152:  # LVM_GETSUBITEMRECT
            rect = table.Rect.from_address(lp)
            assert (rect.left, rect.top) == (0, 3)
            rect.left, rect.top, rect.right, rect.bottom = self.bounds
            return self.rect_ok
        if msg == 0x31:  # WM_GETFONT
            return 99
        return self.native.SendMessageW(hwnd, msg, wp, lp)

    def FillRect(self, hdc, rect, brush):
        self.fills.append((rect._obj.left, rect._obj.top, rect._obj.right, rect._obj.bottom))
        return 1

    def DrawTextW(self, hdc, text, length, rect, flags):
        if self.fail_text:
            raise RuntimeError('draw failed')
        self.texts.append((text, flags))
        return 16


class ColorTests(unittest.TestCase):
    def handlers(self, rows):
        state, native, env = table.TableTests().handlers(rows)
        api = DrawingApi(native)
        gdi = Mock()
        gdi.SaveDC.return_value = 2
        gdi.IntersectClipRect.return_value = 2
        gdi.GetStockObject.return_value = 888
        env.update(U=api, G=gdi, unit=lambda value: value, font=99)
        return state, api, gdi, env

    def draw(self, env, stage=0x30001, index=0, column=3, item_state=0, sender=101):
        note = env['NMLVCUSTOMDRAW']()
        note.nmcd.hdr.hwndFrom, note.nmcd.hdr.code = sender, -12
        note.nmcd.dwDrawStage, note.nmcd.dwItemSpec = stage, index
        note.nmcd.hdc, note.nmcd.uItemState, note.iSubItem = 123, item_state, column
        return env['window_proc'](100, 0x4E, 0, ctypes.addressof(note))

    def test_comparison_uses_numeric_internal_versions_not_status_or_selection(self):
        for installed, available, expected in (
                ('V1.09', 'V1.10', True), ('V1.10', 'V1.100', True),
                ('V2.44', 'V2.44', False), ('V2', 'V1.99', False),
                ('V1', 'V1.00', False), ('V1.10', 'V1.09', False),
                (None, 'V1.10', False), ('V1.09', None, False)):
            with self.subTest(installed=installed, available=available):
                row = record(installed=installed, available=available, status='arbitrary', selected=False)
                self.assertEqual(u.has_newer_version(row), expected)
                row.update(selected=True, checked=False, eligible=False)
                self.assertEqual(u.has_newer_version(row), expected)
        self.assertFalse(u.has_newer_version(record(operation='install')))
        self.assertFalse(u.has_newer_version({}))

    def test_only_available_cells_of_newer_installed_scripts_are_colored(self):
        rows = [record(), record('same', available='V1.09'),
                record('older', available='V1.08'), record('unknown', installed=None),
                record('new', installed=None, operation='install')]
        state, api, gdi, env = self.handlers(rows)
        self.assertEqual(self.draw(env, stage=1), 0x20)
        for index in range(len(rows)):
            self.assertEqual(self.draw(env, stage=0x10001, index=index), 0x20 if index == 0 else 0)
            for column in range(5):
                self.assertEqual(self.draw(env, index=index, column=column),
                                 4 if (index, column) == (0, 3) else 0)
        self.assertEqual(len(api.fills), 1)
        self.assertEqual(api.texts[0][0], 'V1.10')
        self.assertEqual(u.UPDATE_CELL_COLOR, 0 | (205 << 8) | (134 << 16))
        gdi.SetDCBrushColor.assert_called_once_with(123, u.UPDATE_CELL_COLOR)
        gdi.SetTextColor.assert_called_once_with(123, 0)
        gdi.RestoreDC.assert_called_once_with(123, 2)

    def test_focus_checkboxes_and_rebuild_do_not_hide_green(self):
        state, api, gdi, env = self.handlers([record()])
        for selected in (False, True):
            state['rows'][0]['selected'] = selected
            for item_state in (0, 1, 16, 17, 64):
                for populating in (False, True):
                    state['populating'] = populating
                    self.assertEqual(self.draw(env, item_state=item_state), 4)
        env['message'].assert_not_called()

    def test_sort_and_rescan_bind_color_to_the_correct_script(self):
        newer, same = record('Zulu'), record('Alpha', available='V1.09')
        state, api, gdi, env = self.handlers([newer, same])
        env['sort_by_column'](0)
        self.assertEqual(self.draw(env, index=0), 0)
        self.assertEqual(self.draw(env, index=1), 4)
        fresh = [record('Zulu', installed='V1.10'), record('Alpha', available='V1.09')]
        env['result_queue'].put((fresh, None, ('inputs',)))
        env['render_results']()
        self.assertEqual(self.draw(env, index=0), 0)
        self.assertEqual(self.draw(env, index=1), 0)

    def test_invalid_notifications_and_failed_drawing_fall_back_safely(self):
        state, api, gdi, env = self.handlers([record()])
        for index in (-1, 1, 2**40):
            self.assertEqual(self.draw(env, index=index), 0)
        self.assertEqual(self.draw(env, stage=0x10002), 0)
        self.assertEqual(self.draw(env, sender=999), 777)
        self.assertFalse(api.fills)
        api.rect_ok = False
        self.assertEqual(self.draw(env), 0)
        gdi.SaveDC.assert_not_called()
        api.rect_ok = True
        gdi.SaveDC.return_value = 0
        self.assertEqual(self.draw(env), 0)
        gdi.RestoreDC.assert_not_called()

    def test_grid_clip_font_and_dc_restore(self):
        state, api, gdi, env = self.handlers([record()])
        self.assertEqual(self.draw(env), 4)
        self.assertEqual(api.fills, [(745, 25, 859, 43)])
        gdi.IntersectClipRect.assert_called_once_with(123, 745, 25, 859, 43)
        gdi.SelectObject.assert_called_once_with(123, 99)
        self.assertEqual(api.texts[0][1], 0x4 | 0x20 | 0x800 | 0x8000)
        api.fail_text = True
        note = env['NMLVCUSTOMDRAW']()
        note.nmcd.hdc = 123
        with self.assertRaisesRegex(RuntimeError, 'draw failed'):
            env['draw_update_cell'](note, state['rows'][0])
        self.assertEqual(gdi.RestoreDC.call_count, 2)

    def test_structures_follow_windows_pointer_and_dword_layout(self):
        _, _, _, env = self.handlers([])
        if ctypes.sizeof(ctypes.c_void_p) == 8:
            self.assertEqual(ctypes.sizeof(env['NMCUSTOMDRAW']), 80)
            self.assertEqual(ctypes.sizeof(env['NMLVCUSTOMDRAW']), 136)
            self.assertEqual(env['NMLVCUSTOMDRAW'].iSubItem.offset, 88)
        else:
            self.assertEqual(ctypes.sizeof(env['NMCUSTOMDRAW']), 48)
            self.assertEqual(ctypes.sizeof(env['NMLVCUSTOMDRAW']), 104)
            self.assertEqual(env['NMLVCUSTOMDRAW'].iSubItem.offset, 56)

    @unittest.skipUnless(os.name == 'nt', 'Requires native Windows list-view rendering')
    def test_real_windows_pixels_selection_sort_scroll_and_rescan(self):
        if os.environ.get('NX_UPDATER_COLOR_PROBE') != '1':
            result = subprocess.run(
                [sys.executable, '-B', str(Path(__file__).resolve()),
                 'ColorTests.test_real_windows_pixels_selection_sort_scroll_and_rescan', '-v'],
                env=dict(os.environ, NX_UPDATER_COLOR_PROBE='1'),
                capture_output=True, text=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            return
        import faulthandler
        from ctypes import wintypes as W
        faulthandler.dump_traceback_later(20, exit=True)
        self.addCleanup(faulthandler.cancel_dump_traceback_later)
        state, _, _, env = self.handlers([record('Zulu'), record('Alpha', available='V1.09')])
        api, gdi, kernel, common = (ctypes.WinDLL(name, use_last_error=True)
                                    for name in ('user32', 'gdi32', 'kernel32', 'comctl32'))
        env.update(U=api, G=gdi, W=W)
        callback_type = ctypes.WINFUNCTYPE(ctypes.c_ssize_t, W.HWND, W.UINT,
                                          ctypes.c_size_t, ctypes.c_ssize_t)
        class WindowClass(ctypes.Structure):
            _fields_ = [('style', W.UINT), ('proc', callback_type),
                        ('clsExtra', ctypes.c_int), ('wndExtra', ctypes.c_int),
                        ('instance', W.HINSTANCE), ('icon', W.HICON), ('cursor', W.HANDLE),
                        ('background', W.HBRUSH), ('menu', W.LPCWSTR), ('name', W.LPCWSTR)]

        # Use the production signatures for all rendering functions.
        import ast
        calls = [node for node in table.WINDOW.body
                 if isinstance(node, ast.Expr) and isinstance(node.value, ast.Call)
                 and isinstance(node.value.func, ast.Name) and node.value.func.id == 'signature'
                 and node.value.args[0].id in ('U', 'G', 'K')
                 and node.value.args[1].value not in ('RegisterClassW',)]
        env.update(K=kernel, WNDCLASS=WindowClass, LPARAM=ctypes.c_ssize_t,
                   WPARAM=ctypes.c_size_t, LRESULT=ctypes.c_ssize_t)
        exec(compile(ast.Module(body=[table.NODES['signature']] + calls, type_ignores=[]),
                     str(table.SOURCE), 'exec'), env)
        signature = env['signature']
        signature(api, 'RegisterClassW', W.ATOM, ctypes.POINTER(WindowClass))
        signature(api, 'GetDC', W.HDC, W.HWND)
        signature(api, 'ReleaseDC', ctypes.c_int, W.HWND, W.HDC)
        signature(gdi, 'CreateCompatibleDC', W.HDC, W.HDC)
        signature(gdi, 'CreateCompatibleBitmap', W.HBITMAP, W.HDC, ctypes.c_int, ctypes.c_int)
        signature(gdi, 'DeleteDC', W.BOOL, W.HDC)
        signature(gdi, 'DeleteObject', W.BOOL, W.HANDLE)
        signature(gdi, 'GetPixel', W.DWORD, W.HDC, ctypes.c_int, ctypes.c_int)
        signature(common, 'InitCommonControlsEx', W.BOOL, ctypes.c_void_p)
        self.assertTrue(common.InitCommonControlsEx(ctypes.byref((ctypes.c_uint32 * 2)(8, 1))))
        errors, stages = [], []

        @callback_type
        def proc(hwnd, msg, wp, lp):
            try:
                if msg == 0x4E and lp and 'list' in env['controls']:
                    header = env['NMHDR'].from_address(lp)
                    if header.hwndFrom == env['controls']['list']:
                        if header.code == -12:
                            stages.append(env['NMLVCUSTOMDRAW'].from_address(lp).nmcd.dwDrawStage)
                        return env['window_proc'](hwnd, msg, wp, lp)
                return api.DefWindowProcW(hwnd, msg, wp, lp)
            except Exception as exc:
                errors.append(str(exc))
                return 0

        instance = kernel.GetModuleHandleW(None)
        class_name = 'NXUpdaterColorTest_' + str(os.getpid())
        window_class = WindowClass(proc=proc, instance=instance, name=class_name)
        self.assertTrue(api.RegisterClassW(ctypes.byref(window_class)))
        parent = screen = dc = bitmap = old_bitmap = None
        try:
            parent = api.CreateWindowExW(0, class_name, '', 0x80000000, 0, 0, 1000, 240,
                                         None, None, instance, None)
            self.assertTrue(parent)
            handle = api.CreateWindowExW(0, 'SysListView32', '', 0x50000000 | 1 | 4 | 8,
                                         0, 0, 1000, 220, parent, 101, instance, None)
            self.assertTrue(handle)
            env['controls'] = {'list': handle, 'details': parent, 'status': parent}
            env['font'] = gdi.GetStockObject(17)
            api.SendMessageW(handle, 0x30, env['font'], 0)
            api.SendMessageW(handle, 4150, 0, 1 | 4 | 0x20 | 0x10000)
            for index, (label, width) in enumerate(u.RESULT_COLUMNS):
                buffer = ctypes.create_unicode_buffer(label)
                column = env['LVCOLUMN'](mask=7, cx=width, pszText=ctypes.cast(buffer, W.LPWSTR))
                api.SendMessageW(handle, 4193, index, ctypes.addressof(column))
            env['rebuild_list']()
            screen = api.GetDC(None)
            dc = gdi.CreateCompatibleDC(screen)
            bitmap = gdi.CreateCompatibleBitmap(screen, 1000, 240)
            self.assertTrue(dc and bitmap)
            old_bitmap = gdi.SelectObject(dc, bitmap)

            def rect_for(index, column):
                rect = W.RECT(0, column, 0, 0)
                self.assertTrue(api.SendMessageW(handle, 4152, index, ctypes.addressof(rect)))
                return rect

            def verify():
                api.FillRect(dc, ctypes.byref(W.RECT(0, 0, 1000, 240)), gdi.GetStockObject(0))
                api.SendMessageW(handle, 0x318, dc, 4 | 8)  # WM_PRINTCLIENT / CLIENT | ERASEBKGND
                self.assertFalse(errors, errors)
                env['message'].assert_not_called()
                self.assertIn(0x30001, stages, 'Native control must request subitem painting')
                for index, row in enumerate(state['rows']):
                    for column in (1, 2, 3):
                        rect = rect_for(index, column)
                        pixel = gdi.GetPixel(dc, rect.right - 8, (rect.top + rect.bottom) // 2)
                        expected = column == 3 and u.has_newer_version(row)
                        self.assertEqual(pixel == u.UPDATE_CELL_COLOR, expected,
                                         (index, column, hex(pixel), expected))
                    if u.has_newer_version(row):
                        rect = rect_for(index, 3)
                        self.assertTrue(any(gdi.GetPixel(dc, x, y) == 0
                                            for x in range(rect.left + 6, min(rect.left + 60, rect.right - 4))
                                            for y in range(rect.top + 2, rect.bottom - 2)),
                                        'Version text must remain visible in black')

            verify()
            item = env['LVITEM'](mask=8, state=3, stateMask=3)
            api.SendMessageW(handle, 4139, 0, ctypes.addressof(item))
            env['set_checked'](0, True)
            verify()
            env['sort_by_column'](0)
            verify()
            api.SendMessageW(handle, 4116, 150, 0)  # LVM_SCROLL
            verify()
            self.assertTrue(api.SetWindowPos(handle, None, 0, 0, 930, 220, 0x14))
            verify()
            fresh = [record('Alpha', available='V1.09'), record('Zulu', installed='V1.10')]
            env['result_queue'].put((fresh, None, ('inputs',)))
            env['render_results']()
            verify()
        finally:
            if old_bitmap:
                gdi.SelectObject(dc, old_bitmap)
            if bitmap:
                gdi.DeleteObject(bitmap)
            if dc:
                gdi.DeleteDC(dc)
            if screen:
                api.ReleaseDC(None, screen)
            if parent:
                api.DestroyWindow(parent)
            api.UnregisterClassW(class_name, instance)


if __name__ == '__main__':
    unittest.main()
