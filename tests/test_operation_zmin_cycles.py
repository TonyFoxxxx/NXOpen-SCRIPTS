"""Machine-cycle regressions with synthetic geometry; no customer files or NX required."""
import importlib.util
import math
from pathlib import Path
from types import SimpleNamespace as NS
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('operation_zmin_cycles', ROOT / 'scripts/NX_Operation_Zmin.py')
ZMIN = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ZMIN)
IDENTITY = ((1, 0, 0), (0, 1, 0), (0, 0, 1))


class Attribute:
    def __init__(self, value):
        self.value = value

    def GetDoubleValue(self):
        if type(self.value) is not float:
            raise TypeError('Not a Double CAMAttribute')
        return self.value

    def GetIntegerValue(self):
        if type(self.value) is not int:
            raise TypeError('Not an Integer CAMAttribute')
        return self.value


class Feature:
    def __init__(self, point, axis, depth, angle, diameter):
        self.values = dict(DEPTH=float(depth), TIP_ANGLE=float(angle),
                           DIAMETER_1=float(diameter), SUBTYPE=0)
        for c, p, a in zip('XYZ', point, axis):
            self.values[c + '_POSITION'] = float(p)
            self.values[c + '_ORIENTATION_D'] = float(-a)
        self.overrides = set()
        self.Type = 'STEP1HOLE'
        self.operations = []

    def GetAttribute(self, name):
        return Attribute(self.values[name])

    def IsAttributeOverridden(self, name):
        return name in self.overrides

    def GetOperations(self):
        return self.operations


class Buffer(NS):
    def Dispose(self):
        assert not self.disposed, 'double release'
        self.disposed = True


class Event(Buffer):
    def GetNext(self):
        return self.path.event(self.index + 1)


class PathData:
    def __init__(self, points):
        self.points = points
        self.ToolAxisType = 5
        self.NumberOfToolpathEvents = len(points)
        self.buffers = []

    def HasSubPath(self):
        return False

    def event(self, index):
        event = Event(path=self, index=index, disposed=False)
        self.buffers.append(event)
        return event

    def GetFirstEvent(self):
        return self.event(0)

    def IsToolpathEventAMotion(self, event):
        data = self.points[event.index]
        return data is not None, data[2] if data else 0, 1

    def GetLinearMotion(self, event):
        p, a, _ = self.points[event.index]
        motion = Buffer(EndPoint=NS(X=p[0], Y=p[1], Z=p[2]),
                        ToolAxis=NS(X=a[0], Y=a[1], Z=a[2]), disposed=False)
        self.buffers.append(motion)
        return motion


class HoleOperation(NS):
    def GetPath(self):
        return self.path

    def GetStatus(self):
        return self.status

    def GetParent(self, view):
        assert view == 1
        return self.tool

    def AskPathExists(self):
        return True


class Builder(NS):
    def GetFeatureGeometry(self):
        return self.geometry

    def GetToolDrivePoint(self):
        return self.drive

    def Destroy(self):
        self.destroyed += 1

    def Commit(self):
        raise AssertionError('Read-only builder must not Commit')


class UFParameters:
    def __init__(self):
        self.names = {}
        self.values = {}

    def add(self, tag, index, name, value):
        self.names[index] = name
        self.values[tag, index] = value

    def AskParamAttributes(self, index):
        return NS(Name=self.names[index])

    def AskDoubleValue(self, tag, index):
        return self.values[tag, index]

    def AskStrValue(self, tag, index):
        return self.values[tag, index]


class CycleCase:
    def __init__(self, spot=False, positions=((10., 20., 0.), (30., 20., 0.)),
                 axes=None, depths=None, origin=(0., 0., 0.)):
        self.origin = origin
        axes = axes or [(0., 0., 1.)] * len(positions)
        depths = depths or ([1.25] if spot else [9.5]) * len(positions)
        diameter, angle = (6., 90.) if spot else (5., 120.)
        self.locals = [Feature(p, a, d, angle, diameter) for p, a, d in zip(positions, axes, depths)]
        self.masters = [Feature(p, a, 22., 120., 5.) for p, a in zip(positions, axes)]
        self.sets = [NS(CustomPartOffset=False, CustomStock=False, GetFeature=lambda f=f: f)
                     for f in self.locals]
        geometry = NS(UseModelDepth=False, GeometryList=NS(Length=len(self.sets)),
                      GetFeatureSet=lambda i: self.sets[i])
        self.builder = Builder(geometry=geometry, PredefinedDepth=NS(Status=False, Value=500.),
            HoleDepth=NS(Value=0.), IgnoreToolTip=False, drive='SYS_CL_TIP',
            ControlPointOffset=0, HoleDepthType=0, destroyed=0,
            CycleTable=NS(MotionOutput=0, CycleType='Drill,Csink' if spot else 'Drill,Deep',
                          CycleTrackingPoint='None', CsinkDiameter=0.),
            CutParameters=NS(BottomStock=NS(Value=0.), PartStock=NS(Value=0.),
                             BottomOffset=NS(OffsetType=0, Distance=0. if spot else 2.)))
        points = [(tuple(positions[0][i] + 40 * axes[0][i] for i in range(3)), axes[0], 1)]
        for p, a in reversed(list(zip(positions, axes))):
            points.extend((None, (p, a, 2)))  # Stored System / Cycle pairs; reverse geometry order.
        self.op = HoleOperation(Tag=1, Name='HOLES_Z-999', status=0, tool=NS(Tag=2), path=PathData(points))
        for f in self.masters:
            f.operations = [self.op]
        self.op.OwningPart = NS(CAMFeatures=self.masters,
            CAMSetup=NS(CAMOperationCollection=NS(CreateHoleDrillingBuilder=lambda op: self.builder)))
        self.parameters = UFParameters()
        for i, n, v in ((154, 'System Template Subtype', 'SPOT_DRILLING' if spot else 'DRILLING'),
                        (8101, 'Feature Type Name', 'NXHOLECENTER' if spot else 'NXHOLE')):
            self.parameters.add(1, i, n, v)
        for i, n, v in ((1000, 'Tool Diameter', diameter), (1016, 'Tool Point Angle', math.radians(angle)),
                        (1053, 'Tool Point Length', diameter / (2 * math.tan(math.radians(angle / 2)))),
                        (1176, 'Tool Tip Diameter', 0.), (1177, 'Tool Tip Length', 0.),
                        (1181, 'Tool Pilot Diameter', 0.), (1182, 'Tool Pilot Length', 0.)):
            self.parameters.add(2, i, n, v)
        uf = NS(Param=self.parameters)
        self.nx = NS(UF=NS(UFSession=NS(GetUFSession=lambda: uf)), CAM=NS(
            HoleDrilling=HoleOperation, CAMObject=NS(Status=NS(Complete=0, Regen=1, Approved=2)),
            HoleDrillingBuilder=NS(ControlPointOffsetType=NS(NotSet=0)),
            OperationBuilder=NS(HoleDepthTypes=NS(Point=0)),
            Cycle=NS(MotionOutputTypes=NS(MachineCycle=0)),
            VerticalPosition=NS(OffsetTypes=NS(Distance=0)), CAMSetup=NS(View=NS(MachineTool=1)),
            CamPathToolAxisType=NS(Three=3, Five=5), CamPathMotionShapeType=NS(Linear=1),
            CamPathMotionType=NS(Rapid=1, Cycle=2), CamPathDir=NS(Clockwise=1, Counterclockwise=2)))

    def read(self):
        try:
            return ZMIN.toolpath_zmin(self.nx, self.op, IDENTITY, self.origin)
        finally:
            assert all(b.disposed for b in self.op.path.buffers)


class CycleDepthTests(unittest.TestCase):
    def test_spot_uses_local_tip_depth_not_master_depth_or_tool_cone(self):
        case = CycleCase(spot=True)
        self.assertAlmostEqual(case.read(), -1.25)
        self.assertEqual(case.builder.destroyed, 1)

    def test_drill_matches_nc_rounding_and_replaces_stale_suffix(self):
        case = CycleCase()
        result = case.read()
        # Synthetic G83: 9.5 mm feature + 2 mm overtravel + 120-degree D5 tip.
        self.assertEqual('%.3f' % result, '-12.943')
        self.assertEqual(ZMIN.name_with_zmin(case.op.Name, result), 'HOLES_Z-12.9434')
        self.assertEqual(case.builder.destroyed, 1)

    def test_minimum_uses_each_holes_height_depth_and_actual_order(self):
        case = CycleCase(spot=True, positions=((10., 0., 5.), (30., 0., 2.)), depths=[10., 3.])
        self.assertEqual(case.read(), -5.)

    def test_rotary_projection_is_invariant_for_both_storage_modes(self):
        origin = (45., 10., -7.)
        for spot in (True, False):
            for mode in (3, 5):
                for angle in (-150, -90, -35, 0, 35, 90, 150, 180):
                    with self.subTest(spot=spot, mode=mode, angle=angle):
                        sine, cosine = math.sin(math.radians(angle)), math.cos(math.radians(angle))
                        axis = (sine, 0., cosine)
                        point = (origin[0] + 80*cosine + 6*sine, origin[1],
                                 origin[2] - 80*sine + 6*cosine)
                        case = CycleCase(spot, [point], [axis], origin=origin)
                        case.op.path.ToolAxisType = mode
                        self.assertAlmostEqual(case.read(), 4.75 if spot else -6.943375672974064)

    def test_missing_or_nonfinite_depth_rejects_whole_operation(self):
        for value in (None, float('nan'), float('inf'), 0., -1.):
            case = CycleCase(spot=True)
            if value is None:
                del case.locals[0].values['DEPTH']
            else:
                case.locals[0].values['DEPTH'] = value
            with self.subTest(value=value), self.assertRaises((ValueError, KeyError)):
                case.read()
            self.assertEqual(case.builder.destroyed, 1)

    def test_unmatched_transformed_or_ambiguous_entry_rejected(self):
        for bad_point, bad_axis in (((999., 0., 0.), (0., 0., 1.)),
                                    ((10., 20., 0.), (0., 0., -1.))):
            case = CycleCase(spot=True)
            case.op.path.points[-1] = (bad_point, bad_axis, 2)
            with self.assertRaisesRegex(ValueError, 'сопоставить'):
                case.read()
        case = CycleCase(spot=True, positions=((10., 0., 0.), (10., 0., 0.)))
        with self.assertRaisesRegex(ValueError, 'однозначно'):
            case.read()

    def test_blind_or_unlinked_master_cannot_use_through_formula(self):
        for kind in ('STEP1POCKET', 'SIMPLE_HOLE', 'STEP2HOLE'):
            case = CycleCase()
            case.masters[0].Type = kind
            with self.subTest(kind=kind), self.assertRaisesRegex(ValueError, 'сквозное'):
                case.read()
        case = CycleCase()
        case.masters[0].operations = []
        with self.assertRaisesRegex(ValueError, 'сквозное'):
            case.read()

    def test_unsupported_depth_modes_destroy_builder_and_do_not_return_zero(self):
        mutations = [lambda c: setattr(c.builder.geometry, 'UseModelDepth', True),
                     lambda c: setattr(c.builder.PredefinedDepth, 'Status', True),
                     lambda c: setattr(c.builder, 'ControlPointOffset', 1),
                     lambda c: setattr(c.builder, 'IgnoreToolTip', True),
                     lambda c: setattr(c.builder, 'drive', 'SYS_CL_SHOULDER'),
                     lambda c: setattr(c.builder, 'HoleDepthType', 1),
                     lambda c: setattr(c.builder.CycleTable, 'CycleTrackingPoint', 'R2'),
                     lambda c: setattr(c.builder.CycleTable, 'CycleType', 'Tap'),
                     lambda c: setattr(c.builder.CycleTable, 'MotionOutput', 1),
                     lambda c: setattr(c.builder.CycleTable, 'CsinkDiameter', 7.),
                     lambda c: setattr(c.builder.CutParameters.BottomStock, 'Value', 1.),
                     lambda c: setattr(c.builder.CutParameters.PartStock, 'Value', 1.),
                     lambda c: setattr(c.builder.CutParameters.BottomOffset, 'OffsetType', 1),
                     lambda c: setattr(c.builder.CutParameters.BottomOffset, 'Distance', -1.),
                     lambda c: setattr(c.sets[0], 'CustomStock', True),
                     lambda c: setattr(c.sets[0], 'CustomPartOffset', True)]
        for mutation in mutations:
            case = CycleCase()
            mutation(case)
            with self.subTest(mutation=mutations.index(mutation)), self.assertRaises(ValueError):
                case.read()
            self.assertEqual(case.builder.destroyed, 1)

    def test_changed_local_subtype_and_tool_geometry_are_rejected(self):
        for mutate in (lambda c: c.locals[0].overrides.add('SUBTYPE'),
                       lambda c: c.locals[0].values.update(SUBTYPE=1),
                       lambda c: c.locals[0].values.update(TIP_ANGLE=90.),
                       lambda c: c.locals[0].values.update(DIAMETER_1=6.),
                       lambda c: c.parameters.values.update({(2, 1053): 0.}),
                       lambda c: c.parameters.values.update({(2, 1181): 2.})):
            case = CycleCase()
            mutate(case)
            with self.assertRaises(ValueError):
                case.read()

    def test_unknown_cycle_class_status_or_uf_descriptor_are_rejected(self):
        case = CycleCase()
        case.nx.CAM.HoleDrilling = type('DifferentOperation', (), {})
        with self.assertRaisesRegex(ValueError, 'типа цикла'):
            case.read()
        for status in (1, 99):
            case = CycleCase()
            case.op.status = status
            with self.assertRaisesRegex(ValueError, 'актуальная'):
                case.read()
        case = CycleCase()
        case.parameters.names[1053] = 'Unrelated dimension'
        with self.assertRaisesRegex(ValueError, 'описание параметра'):
            case.read()

    def test_unsupported_cycle_is_reported_and_its_old_name_is_preserved(self):
        case = CycleCase()
        case.locals[0].values['DEPTH'] = float('nan')
        with patch.object(ZMIN, 'nearest_mcs', return_value=NS(Tag=3)), \
             patch.object(ZMIN, 'read_mcs', return_value=(IDENTITY, case.origin)):
            planned, skipped, unchanged = ZMIN.prepare_names(case.nx, case.op.OwningPart, [case.op])
        self.assertEqual(planned, [])
        self.assertEqual(unchanged, 0)
        self.assertEqual(skipped[0][0], 'HOLES_Z-999')
        self.assertEqual(case.op.Name, 'HOLES_Z-999')


if __name__ == '__main__':
    unittest.main()
