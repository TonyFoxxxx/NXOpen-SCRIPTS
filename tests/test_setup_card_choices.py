"""Exercise both production dialogs and their pixels on real Windows controls."""
import base64
import ctypes as c
from ctypes import wintypes as w
import importlib.util
import os
from pathlib import Path
import struct
import unittest
from unittest.mock import patch
import zlib

SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Setup_Prototype.py'
SPEC = importlib.util.spec_from_file_location('setup_choices_test', SOURCE)
CARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CARD)


@unittest.skipUnless(os.name == 'nt', 'requires native Windows dialogs')
class NativeChoiceTests(unittest.TestCase):
    def setUp(self):
        self.load = c.WinDLL
        self.u, self.g = self.load('user32'), self.load('gdi32')
        self.theme = self.load('uxtheme')
        self.fonts, self.errors, self.ran = [], [], False
        self.drawers = []
        self.real_drawer = CARD.NativeChoiceRows
        for lib, name, result, args in (
                (self.u, 'SendMessageW', c.c_ssize_t, [w.HWND, w.UINT, w.WPARAM, w.LPARAM]),
                (self.u, 'PostMessageW', w.BOOL, [w.HWND, w.UINT, w.WPARAM, w.LPARAM]),
                (self.u, 'GetDlgItem', w.HWND, [w.HWND, c.c_int]),
                (self.u, 'GetClientRect', w.BOOL, [w.HWND, c.POINTER(w.RECT)]),
                (self.u, 'GetDC', w.HDC, [w.HWND]),
                (self.u, 'ReleaseDC', c.c_int, [w.HWND, w.HDC]),
                (self.u, 'EndDialog', w.BOOL, [w.HWND, c.c_ssize_t]),
                (self.u, 'SetFocus', w.HWND, [w.HWND]),
                (self.u, 'UpdateWindow', w.BOOL, [w.HWND]),
                (self.g, 'CreateCompatibleDC', w.HDC, [w.HDC]),
                (self.g, 'CreateDIBSection', w.HBITMAP,
                 [w.HDC, c.c_void_p, w.UINT, c.POINTER(c.c_void_p), w.HANDLE, w.DWORD]),
                (self.g, 'SelectObject', w.HANDLE, [w.HDC, w.HANDLE]),
                (self.g, 'DeleteObject', w.BOOL, [w.HANDLE]),
                (self.g, 'DeleteDC', w.BOOL, [w.HDC]),
                (self.g, 'GdiFlush', w.BOOL, []),
                (self.g, 'CreateFontW', w.HFONT,
                 [c.c_int] * 5 + [w.DWORD] * 8 + [w.LPCWSTR]),
                (self.theme, 'SetWindowTheme', c.c_long, [w.HWND, w.LPCWSTR, w.LPCWSTR])):
            fn = getattr(lib, name)
            fn.restype, fn.argtypes = result, args

    def tearDown(self):
        for font in self.fonts:
            self.g.DeleteObject(font)

    def dialog(self, launch, check):
        real_dialog = self.u.DialogBoxIndirectParamW

        def modal(instance, template, owner, callback, parameter):
            @type(callback)
            def hook(window, message, wp, lp):
                if message == 0x8001:
                    self.ran = True
                    try:
                        check(window)
                    except BaseException as exc:
                        self.errors.append(exc)
                    finally:
                        self.u.EndDialog(window, 1)
                    return 1
                result = callback(window, message, wp, lp)
                if message == 0x110:
                    self.u.PostMessageW(window, 0x8001, 0, 0)
                return result

            real_dialog.argtypes = [w.HINSTANCE, c.c_void_p, w.HWND, type(callback), w.LPARAM]
            real_dialog.restype = c.c_ssize_t
            return real_dialog(instance, template, None, hook, parameter)

        class Proxy:
            def __getattr__(_, name):
                return modal if name == 'DialogBoxIndirectParamW' else getattr(self.u, name)

        def load(name, **kwargs):
            return Proxy() if name.lower() == 'user32' else self.load(name, **kwargs)

        def drawer(*args):
            result = self.real_drawer(*args)
            self.drawers.append(result)
            return result

        with patch.object(c, 'WinDLL', side_effect=load), patch.object(CARD, 'NativeChoiceRows', side_effect=drawer):
            result = launch()
        self.assertTrue(self.ran, 'Production dialog closed before native tests ran')
        if self.errors:
            raise self.errors[0]
        return result

    def capture(self, window):
        rect = w.RECT()
        self.assertTrue(self.u.GetClientRect(window, c.byref(rect)))
        width, height = rect.right, rect.bottom
        header = c.create_string_buffer(struct.pack('<IiiHHIIiiII', 40, width, -height, 1, 32, 0, 0, 0, 0, 0, 0))
        screen = self.u.GetDC(None)
        dc = self.g.CreateCompatibleDC(screen)
        bits = c.c_void_p()
        bitmap = self.g.CreateDIBSection(screen, header, 0, c.byref(bits), None, 0)
        self.assertTrue(dc and bitmap and bits.value)
        old = self.g.SelectObject(dc, bitmap)
        try:
            self.u.UpdateWindow(window)
            self.u.SendMessageW(window, 0x318, dc, 4 | 8)  # WM_PRINTCLIENT
            self.g.GdiFlush()
            return width, height, c.string_at(bits, width * height * 4)
        finally:
            self.g.SelectObject(dc, old)
            self.g.DeleteObject(bitmap)
            self.g.DeleteDC(dc)
            self.u.ReleaseDC(None, screen)

    def emit(self, image, name):
        width, height, data = image
        rows = b''.join(b'\0' + b''.join(bytes((data[p + 2], data[p + 1], data[p]))
                        for p in range(y * width * 4, (y + 1) * width * 4, 4)) for y in range(height))

        def chunk(kind, value):
            return struct.pack('>I', len(value)) + kind + value + struct.pack('>I', zlib.crc32(kind + value) & 0xffffffff)

        png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0))
        png += chunk(b'IDAT', zlib.compress(rows)) + chunk(b'IEND', b'')
        print('SETUP_CHOICES_PNG_' + name + ':' + base64.b64encode(png).decode(), flush=True)

    def hit(self, window, x, y, tree):
        if tree:
            class Hit(c.Structure):
                _fields_ = [('point', w.POINT), ('flags', w.UINT), ('item', w.HANDLE)]
        else:
            class Hit(c.Structure):
                _fields_ = [('point', w.POINT), ('flags', w.UINT), ('item', c.c_int),
                            ('subitem', c.c_int), ('group', c.c_int)]
        hit = Hit(point=w.POINT(x, y))
        self.u.SendMessageW(window, 0x1111 if tree else 0x1012, 0, c.addressof(hit))
        return hit.flags, hit.item

    def point(self, window, item, tree, flag):
        drawer = self.drawers[0]
        row, label = drawer.rect(window, item), drawer.rect(window, item, True)
        y = (row.top + row.bottom) // 2
        points = [x for x in range(max(0, label.left - 100), label.left)
                  if self.hit(window, x, y, tree)[0] & flag]
        self.assertTrue(points, (item, flag, label.left, y))
        return (points[0] + points[-1]) // 2, y

    def click(self, window, point, double=False):
        pos = (point[1] << 16) | (point[0] & 0xffff)
        self.u.SendMessageW(window, 0x201, 1, pos)
        self.u.SendMessageW(window, 0x202, 0, pos)
        if double:
            self.u.SendMessageW(window, 0x203, 1, pos)
            self.u.SendMessageW(window, 0x202, 0, pos)

    def check_pixels(self, window, items, tree, image=None):
        width, height, data = image or self.capture(window)
        seen = 0
        drawer = self.drawers[0]
        for item in items:
            row, label = drawer.rect(window, item), drawer.rect(window, item, True)
            if row is None or row.top < 0 or row.bottom > height or label.left < 20 or label.left >= width:
                continue
            info = drawer.item_info(item)
            if info is None or info[1]:
                continue
            y = (row.top + row.bottom) // 2
            slot = [x for x in range(max(0, label.left - 40), min(width, label.left))
                    if self.hit(window, x, y, tree)[0] & (0x40 if tree else 8)]
            self.assertTrue(slot, 'Checkbox must retain a native hit target')
            text_rows, box_rows = [], []
            for yy in range(row.top, row.bottom):
                for x in range(max(0, label.left), min(width, label.right)):
                    p = (yy * width + x) * 4
                    blue, green, red = data[p:p + 3]
                    if blue > red + 40 and blue > green + 40:
                        text_rows.append(yy)
                        break
                for x in slot:
                    p = (yy * width + x) * 4
                    if max(data[p:p + 3]) < 200:
                        box_rows.append(yy)
                        break
            self.assertTrue(text_rows, ('Missing text', tree, item, label.left, label.top, label.right, label.bottom))
            self.assertTrue(box_rows, ('Missing checkbox', tree, item))
            text_center, box_center = (min(text_rows) + max(text_rows)) / 2, (min(box_rows) + max(box_rows)) / 2
            self.assertLessEqual(abs(text_center - box_center), 2, (tree, item, text_center, box_center))
            self.assertLessEqual(abs(box_center - y), 1, (tree, item, box_center, y))
            seen += 1
        self.assertGreater(seen, 0, 'No visible rows checked')

    def sizes(self, window, items, tree, theme):
        self.theme.SetWindowTheme(window, 'Explorer' if theme else '', None if theme else '')
        self.u.SendMessageW(window, 0x111E if tree else 0x1024, 0, 160 << 16)
        for size in (12, 18, 24):
            font = self.g.CreateFontW(-size, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, 'Segoe UI')
            self.assertTrue(font)
            self.fonts.append(font)
            self.u.SendMessageW(window, 0x30, font, 1)
            if tree:
                self.u.SendMessageW(window, 0x111B, size + 10, 0)  # TVM_SETITEMHEIGHT
                self.u.SendMessageW(window, 0x110B, 9, 0)
            else:
                self.u.SetFocus(self.u.GetDlgItem(self.parent, 2))
            image = self.capture(window)
            if size == 12 and not theme:
                self.emit(image, 'tree' if tree else 'list')
            self.check_pixels(window, items, tree, image)

    def test_folder_dialog_alignment_and_selection(self):
        rows = [dict(key='root', name='NC_PROGRAM', parent=None, ancestors=(), operation_count=80),
                dict(key='none', name='NONE', parent='root', ancestors=('root',), operation_count=1),
                dict(key='hidden', name='HIDDEN', parent='none', ancestors=('root', 'none'), operation_count=1),
                dict(key='first', name='UST1', parent='root', ancestors=('root',), operation_count=2),
                dict(key='child', name='SUB1', parent='first', ancestors=('root', 'first'), operation_count=2)]
        rows += [dict(key=str(i), name='UST_' + str(i), parent='root', ancestors=('root',), operation_count=2)
                 for i in range(40)]

        def check(parent):
            self.parent = parent
            tree = self.u.GetDlgItem(parent, 101)
            root = self.u.SendMessageW(tree, 0x110A, 0, 0)
            none = self.u.SendMessageW(tree, 0x110A, 4, root)
            first = self.u.SendMessageW(tree, 0x110A, 1, none)
            child = self.u.SendMessageW(tree, 0x110A, 4, first)
            last = self.u.SendMessageW(tree, 0x110A, 10, 0)
            items = [root, none, first, child]

            def state(handle):
                data = self.drawers[0].Item(mask=8, hItem=handle, stateMask=0xffff)
                self.u.SendMessageW(tree, 0x113E, 0, c.addressof(data))
                return data.state

            self.assertFalse(state(none) & 0x20, 'NONE starts collapsed')
            for theme in (False, True):
                self.u.SendMessageW(tree, 0x110B, 5, root)
                self.sizes(tree, items, True, theme)
            self.click(tree, self.point(tree, first, True, 0x40))
            self.assertEqual(state(first) & 0xf000, 0x2000)
            self.click(tree, self.point(tree, child, True, 0x40))
            self.assertEqual(state(child) & 0xf000, 0x1000, 'Parent blocks child')
            self.u.SendMessageW(tree, 0x110B, 9, first)
            self.u.SendMessageW(tree, 0x100, 0x20, 0)
            self.u.SendMessageW(tree, 0x101, 0x20, 0)
            self.assertEqual(state(first) & 0xf000, 0x1000)
            label = self.drawers[0].rect(tree, child, True)
            row = self.drawers[0].rect(tree, child)
            self.click(tree, (label.left + 4, (row.top + row.bottom) // 2), True)
            self.assertEqual(state(child) & 0xf000, 0x2000, 'Double-click selects label')
            self.click(tree, self.point(tree, first, True, 0x40))
            self.assertEqual(state(first) & 0xf000, 0x1000, 'Child blocks parent')
            self.click(tree, self.point(tree, first, True, 0x10))
            self.assertFalse(state(first) & 0x20, 'Blocked parent still collapses')
            self.click(tree, self.point(tree, first, True, 0x10))
            self.assertTrue(state(first) & 0x20)
            self.u.SendMessageW(parent, 0x111, 222, 0)
            self.assertEqual(state(child) & 0xf000, 0x1000)
            self.u.SendMessageW(tree, 0x1114, 0, last)  # TVM_ENSUREVISIBLE
            self.u.SendMessageW(tree, 0x110B, 9, 0)
            self.check_pixels(tree, [last], True)
            self.click(tree, self.point(tree, last, True, 0x40))

        self.assertEqual(self.dialog(lambda: CARD.show_setup_folders(rows), check), ['39'])

    def test_component_dialog_alignment_and_setup_choices(self):
        rows = [dict(key=str(i), path='COMPONENT_' + str(i), ancestors=(), suppressed=(i == 2), visible=(i == 0))
                for i in range(40)]
        reports = [dict(setup_name='UST1'), dict(setup_name='UST2')]
        previews = []

        def check(parent):
            self.parent = parent
            listing, setups = self.u.GetDlgItem(parent, 101), self.u.GetDlgItem(parent, 106)

            def state(index):
                return self.u.SendMessageW(listing, 0x102C, index, 0xf000)

            for theme in (False, True):
                self.sizes(listing, range(40), False, theme)
            self.click(listing, self.point(listing, 0, False, 8))
            self.assertEqual(state(0), 0x2000)
            self.assertEqual(previews[-1], {'0'})
            self.click(listing, self.point(listing, 2, False, 8))
            self.assertEqual(state(2), 0x1000, 'Suppressed component stays unchecked')
            label = self.drawers[0].rect(listing, 1, True)
            row = self.drawers[0].rect(listing, 1)
            self.click(listing, (label.left + 4, (row.top + row.bottom) // 2), True)
            self.assertEqual(state(1), 0x2000)
            self.u.SetFocus(listing)
            self.u.SendMessageW(listing, 0x100, 0x20, 0)
            self.u.SendMessageW(listing, 0x101, 0x20, 0)
            self.assertEqual(state(1), 0x1000)
            other = w.RECT(left=2)
            self.u.SendMessageW(setups, 0x100E, 1, c.addressof(other))
            self.click(setups, (other.left + 5, (other.top + other.bottom) // 2))
            self.assertEqual(previews[-1], set(), 'Next setup hides prior components')
            self.assertEqual(state(0), 0x1000)
            self.u.SendMessageW(parent, 0x111, 223, 0)
            self.assertEqual(previews[-1], {'0'}, 'As in NX restores only initial visibility')
            self.u.SendMessageW(parent, 0x111, 222, 0)
            self.u.SendMessageW(listing, 0x1013, 39, 0)  # LVM_ENSUREVISIBLE
            self.u.SetFocus(self.u.GetDlgItem(parent, 2))
            self.check_pixels(listing, [39], False)
            self.click(listing, self.point(listing, 39, False, 8))

        self.assertEqual(self.dialog(lambda: CARD.show_all_setup_components(rows, reports, previews.append), check),
                         [{'0'}, {'39'}])


if __name__ == '__main__':
    unittest.main()
