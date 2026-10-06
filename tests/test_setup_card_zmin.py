"""Zmin uses the standalone journal's tested ToolAxis algorithm, without rename."""
import ast
import importlib.util
import math
from pathlib import Path
from types import SimpleNamespace as NS
import unittest


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'scripts/NX_Setup_Prototype.py'
SPEC = importlib.util.spec_from_file_location('setup_card_zmin_test', SOURCE)
CARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CARD)
IDENTITY = ((1, 0, 0), (0, 1, 0), (0, 0, 1))
NX = NS(CAM=NS(CamPathToolAxisType=NS(Three=3, Five=5),
               CamPathMotionShapeType=NS(Linear=1, Circular=2, Helical=3, Nurbs=4),
               CamPathDir=NS(Clockwise=1, Counterclockwise=2)))


def point(value):
    return NS(X=value[0], Y=value[1], Z=value[2])


class Buffer(NS):
    def Dispose(self):
        assert not self.disposed, 'double disposal'
        self.disposed = True


class Event(Buffer):
    def GetNext(self):
        assert not self.disposed
        return self.owner.event(self.index + 1)


class Path:
    def __init__(self, items, mode=3, subpath=False):
        self.items, self.ToolAxisType, self.subpath = items, mode, subpath
        self.NumberOfToolpathEvents, self.buffers = len(items), []

    def HasSubPath(self):
        return self.subpath

    def event(self, index):
        assert index < len(self.items)
        event = Event(owner=self, index=index, disposed=False)
        self.buffers.append(event)
        return event

    def GetFirstEvent(self):
        return self.event(0)

    def IsToolpathEventAMotion(self, event):
        assert not event.disposed
        item = self.items[event.index]
        return item is not None, 'Cut', item['shape'] if item else 0

    def read(self, event, shape):
        item = self.items[event.index]
        assert not event.disposed and item['shape'] == shape
        if item.get('failure'):
            raise RuntimeError('NX read failure')
        motion = Buffer(EndPoint=point(item['end']), disposed=False)
        if 'tool_axis' in item:
            motion.ToolAxis = point(item['tool_axis'])
        if shape in (2, 3):
            motion.ArcCenter, motion.ArcAxis = point(item['center']), point(item['axis'])
            motion.Direction = item.get('direction', 2)
        if shape == 3:
            motion.NumberOfRevolutions = item['turns']
        self.buffers.append(motion)
        return motion

    def GetLinearMotion(self, event):
        return self.read(event, 1)

    def GetCircularMotion(self, event):
        return self.read(event, 2)

    def GetHelixMotion(self, event):
        return self.read(event, 3)


def line(end, axis=(0, 0, 1), **extra):
    return dict(shape=1, end=end, tool_axis=axis, **extra)


class ZminTests(unittest.TestCase):
    def read(self, items, mode=3, origin=(0, 0, 0), subpath=False):
        path, updates = Path(items, mode, subpath), []
        operation = NS(GetPath=lambda: path)
        try:
            result = CARD.toolpath_zmin(NX, operation, IDENTITY, origin, lambda a, b: updates.append((a, b)))
            return result, updates
        finally:
            self.assertTrue(all(buffer.disposed for buffer in path.buffers))

    def test_functions_match_latest_standalone_exactly(self):
        names = ('dot', 'cross', 'unit', 'xyz', 'read_mcs', 'finite_number', 'enum_name',
                 'path_point', 'release_path_data', 'arc_path_zmin', 'path_axis_mode',
                 'motion_z_axis', 'toolpath_zmin')
        trees = [{node.name: ast.dump(node) for node in ast.parse(path.read_text(encoding='utf-8')).body
                  if isinstance(node, ast.FunctionDef)}
                 for path in (SOURCE, ROOT / 'scripts/NX_Operation_Zmin.py')]
        for name in names:
            with self.subTest(name=name):
                self.assertEqual(trees[0][name], trees[1][name])

    def test_constant_actual_axis_used_for_both_path_formats(self):
        for mode in (3, 5):
            value, _ = self.read([line((16, 15, 200), (2, 0, 0)),
                                  line((8, 30, 210), (2, 0, 0))], mode, (10, 20, 30))
            self.assertAlmostEqual(value, -2)
            self.assertEqual(self.read([line((0, 0, 0))], mode)[0], 0)

    def test_rotations_do_not_create_false_zmin(self):
        origin = (45, 6, -15)
        for mode in (3, 5):
            items = []
            for angle in (-150, -90, -35, 0, 35, 90, 150, 180):
                sine, cosine = math.sin(math.radians(angle)), math.cos(math.radians(angle))
                radial, height = 80, -12.5
                items.append(line((origin[0] + radial * cosine + height * sine,
                                   origin[1], origin[2] - radial * sine + height * cosine),
                                  (sine, 0, cosine)))
            self.assertAlmostEqual(self.read(items, mode, origin)[0], -12.5)

    def test_analytic_arc_and_helix_interior_minimum(self):
        arc = dict(shape=2, end=(-1, 0, 0), center=(0, 0, 0), axis=(0, 1, 0), tool_axis=(0, 0, 1))
        self.assertAlmostEqual(self.read([line((1, 0, 0)), arc])[0], -1)
        helix = dict(shape=3, end=(1, 0, -20), center=(0, 0, 0), axis=(0, 0, 1),
                     tool_axis=(0, 0, 1), turns=3)
        self.assertAlmostEqual(self.read([line((1, 0, 0)), helix], 5)[0], -20)

    def test_invalid_axis_or_curve_is_not_a_partial_minimum(self):
        missing = line((0, 0, -30))
        missing.pop('tool_axis')
        cases = [missing, line((0, 0, -30), (0, 0, 0)), line((0, 0, float('nan'))),
                 line((0, 0, -30), failure=True), dict(shape=4, end=(0, 0, -30)),
                 dict(shape=2, end=(-1, 0, 0), center=(0, 0, 0), axis=(0, 1, 0), tool_axis=(0, 1, 0))]
        for item in cases:
            with self.subTest(item=item), self.assertRaises((ValueError, RuntimeError)):
                self.read([line((1, 0, -10)), item])
        with self.assertRaises(ValueError):
            self.read([line((1, 0, 0))], mode=99)
        with self.assertRaises(ValueError):
            self.read([line((1, 0, 0))], subpath=True)

    def test_empty_path_and_large_linked_traversal(self):
        self.assertIsNone(self.read([])[0])
        self.assertIsNone(self.read([None, None])[0])
        value, updates = self.read([line((0, 0, -index)) for index in range(20000)])
        self.assertEqual(value, -19999)
        self.assertEqual(updates[0], (0, 20000))
        self.assertEqual(updates[-1], (20000, 20000))
        self.assertGreater(len(updates), 75)


if __name__ == '__main__':
    unittest.main()
