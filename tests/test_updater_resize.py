"""Exercise the actual resize handlers; native pixel rendering still needs NX."""
import ast
import copy
import ctypes
from ctypes import wintypes
from pathlib import Path
import unittest


SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Update_Script_Buttons.py'
WINDOW = next(node for node in ast.parse(SOURCE.read_text(encoding='utf-8')).body
              if isinstance(node, ast.FunctionDef) and node.name == 'run_window')
FUNCTIONS = {node.name: node for node in WINDOW.body if isinstance(node, ast.FunctionDef)}
CONTROL_NAMES = {node.args[0].value for node in ast.walk(WINDOW)
                 if isinstance(node, ast.Call) and isinstance(node.func, ast.Name)
                 and node.func.id == 'control' and node.args}


class NativeCalls:
    """Record documented Win32 call ordering, including changing HDWP handles."""
    def __init__(self, failure=None):
        self.failure = failure
        self.size = (1364, 700)
        self.calls = []
        self.handle = 0x123456780000
        self.deferred = []
        self.rects = {}

    def GetClientRect(self, hwnd, rect):
        rect._obj.right, rect._obj.bottom = self.size
        return True

    def BeginDeferWindowPos(self, count):
        self.calls.append(('begin', count))
        self.deferred = []
        return None if self.failure == 'begin' else self.handle

    def DeferWindowPos(self, batch, hwnd, after, x, y, width, height, flags):
        assert batch == self.handle, 'Must use the HDWP returned by the last call'
        self.calls.append(('defer', hwnd, flags))
        if self.failure == 'defer' and len(self.deferred) == 4:
            self.deferred = []
            return None
        self.deferred.append((hwnd, (x, y, width, height)))
        self.handle += 1
        return self.handle

    def EndDeferWindowPos(self, batch):
        assert batch == self.handle
        self.calls.append(('end',))
        if self.failure == 'end':
            return False
        self.rects.update(self.deferred)
        return True

    def SetWindowPos(self, hwnd, after, x, y, width, height, flags):
        self.calls.append(('set', hwnd, flags))
        self.rects[hwnd] = (x, y, width, height)
        return True

    def SendMessageW(self, *args):
        self.calls.append(('column',))
        if self.failure == 'column':
            raise RuntimeError('column update failed')
        return 1

    def RedrawWindow(self, hwnd, rect, region, flags):
        self.calls.append(('redraw', hwnd, rect, region, flags))
        return True


class ResizeTests(unittest.TestCase):
    def make_handlers(self, failure=None, scale=1):
        native = NativeCalls(failure)
        state = {'closed': False, 'laying_out': False}
        controls = {name: name for name in CONTROL_NAMES}
        env = dict(U=native, W=wintypes, ctypes=ctypes, state=state,
                   controls=controls, hwnd_holder={'value': 123},
                   unit=lambda n: int(n * scale))
        nodes = [copy.deepcopy(FUNCTIONS[name]) for name in ('redraw_window', 'layout', 'window_proc')]
        for node in nodes:
            node.decorator_list = []
        exec(compile(ast.Module(body=nodes, type_ignores=[]), str(SOURCE), 'exec'), env)
        return native, state, controls, env

    def assert_full_repaint(self, native):
        call = native.calls[-1]
        self.assertEqual(call[:4], ('redraw', 123, None, None))
        # Entire parent + children + borders must be invalidated, erased and painted.
        for flag in (0x1, 0x4, 0x80, 0x100, 0x400):
            self.assertTrue(call[4] & flag)

    def test_resize_moves_every_control_before_one_full_repaint(self):
        native, state, controls, env = self.make_handlers()
        env['layout']()
        self.assertEqual(len(native.rects), native.calls[0][1])
        self.assertEqual([c[0] for c in native.calls].count('end'), 1)
        self.assertEqual([c[0] for c in native.calls].count('redraw'), 1)
        self.assertFalse(any(c[0] == 'set' for c in native.calls))
        for call in native.calls:
            if call[0] == 'defer':
                self.assertEqual(call[2] & 0x11C, 0x11C)
        self.assert_full_repaint(native)
        self.assertFalse(state['laying_out'])

    def test_batch_failures_still_position_every_control_and_repaint(self):
        for failure in ('begin', 'defer', 'end'):
            with self.subTest(failure=failure):
                native, state, controls, env = self.make_handlers(failure)
                env['layout']()
                self.assertEqual(len(native.rects), native.calls[0][1])
                self.assertEqual(sum(c[0] == 'set' for c in native.calls), native.calls[0][1])
                if failure != 'end':
                    self.assertFalse(any(c[0] == 'end' for c in native.calls))
                self.assert_full_repaint(native)
                self.assertFalse(state['laying_out'])

    def test_repaint_and_guard_recovery_when_column_update_fails(self):
        native, state, controls, env = self.make_handlers('column')
        with self.assertRaisesRegex(RuntimeError, 'column update failed'):
            env['layout']()
        self.assert_full_repaint(native)
        self.assertFalse(state['laying_out'])

    def test_minimized_incomplete_closed_and_nested_layout_are_skipped(self):
        for reason in ('minimized', 'incomplete', 'closed', 'nested'):
            with self.subTest(reason=reason):
                native, state, controls, env = self.make_handlers()
                if reason == 'minimized':
                    native.size = (0, 0)
                elif reason == 'incomplete':
                    controls.pop('close')
                elif reason == 'closed':
                    state['closed'] = True
                else:
                    state['laying_out'] = True
                env['layout']()
                self.assertEqual(native.calls, [])

    def test_rapid_grow_shrink_at_different_dpi_preserves_geometry(self):
        for scale in (1, 1.25, 1.5, 2):
            native, state, controls, env = self.make_handlers(scale=scale)
            for width, height in ((964, 480), (1800, 980), (1200, 600), (964, 480)) * 20:
                native.size = (int(width * scale), int(height * scale))
                env['layout']()
                close = native.rects['close']
                self.assertEqual(close[0] + close[2], native.size[0] - int(112 * scale) + int(96 * scale))
                script_list, details = native.rects['list'], native.rects['details']
                self.assertLessEqual(script_list[1] + script_list[3], details[1])
                self.assertTrue(all(w > 0 and h > 0 for x, y, w, h in native.rects.values()))
                self.assert_full_repaint(native)

    def test_size_messages_and_drag_end_use_current_handlers(self):
        native, state, controls, env = self.make_handlers()
        env['window_proc'](123, 5, 1, 0)  # WM_SIZE / SIZE_MINIMIZED
        self.assertEqual(native.calls, [])
        env['window_proc'](123, 5, 0, 0)
        self.assert_full_repaint(native)
        native.calls.clear()
        env['window_proc'](123, 0x232, 0, 0)  # WM_EXITSIZEMOVE
        self.assertEqual(len(native.calls), 1)
        self.assert_full_repaint(native)

    def test_missing_or_destroyed_parent_never_redraws_the_desktop(self):
        for reason in ('missing', 'closed'):
            native, state, controls, env = self.make_handlers()
            if reason == 'missing':
                env['hwnd_holder']['value'] = None
            else:
                state['closed'] = True
            env['redraw_window']()
            self.assertEqual(native.calls, [])


if __name__ == '__main__':
    unittest.main()
