"""Standalone constant-Z arcs: radius roundoff must not hide a known depth."""
import math
from types import SimpleNamespace as NS
import unittest

from test_setup_card_zmin import IDENTITY, NX, Path, ZMIN, line


class StandaloneArcTests(unittest.TestCase):
    def read(self, start, end, center=(0, 0, -5.1), arc_axis=(0, 0, 1),
             tool_axis=(0, 0, 1), origin=(0, 0, 0), mode=3, direction=2,
             turns=None):
        arc = dict(shape=2 if turns is None else 3, end=end, center=center,
                   axis=arc_axis, tool_axis=tool_axis, direction=direction,
                   turns=turns)
        path = Path([line(start, tool_axis), None, arc], mode)
        try:
            return ZMIN.toolpath_zmin(NX, NS(GetPath=lambda: path), IDENTITY, origin)
        finally:
            self.assertTrue(all(buffer.disposed for buffer in path.buffers))

    def test_flat_radius_roundoff_does_not_skip_operation(self):
        for mode in (3, 5):
            for direction in (1, 2):
                with self.subTest(mode=mode, direction=direction):
                    value = self.read((9, 0, -5.1), (0, 9.000025, -5.1),
                                      mode=mode, direction=direction)
                    self.assertAlmostEqual(value, -5.1)
                    self.assertEqual(ZMIN.name_with_zmin('POCKET_COPY_Z0', value),
                                     'POCKET_Z-5.1')

    def test_constant_projection_survives_rotation_translation_and_reversed_normal(self):
        # An orthonormal frame with no axis aligned to the global coordinates.
        radial = ZMIN.unit((1, 2, 0))
        normal = ZMIN.unit((2, -1, 3))
        tangent = ZMIN.cross(normal, radial)
        origin = (54, -80, 17)
        center = tuple(origin[i] - 6.25 * normal[i] for i in range(3))
        start = tuple(center[i] + 9 * radial[i] for i in range(3))
        end = tuple(center[i] + 9.000025 * tangent[i] for i in range(3))
        for mode in (3, 5):
            for sign in (-1, 1):
                for direction in (1, 2):
                    with self.subTest(mode=mode, sign=sign, direction=direction):
                        value = self.read(start, end, center,
                                          tuple(sign * 3 * x for x in normal),
                                          tuple(2 * x for x in normal), origin,
                                          mode, direction)
                        self.assertAlmostEqual(value, -6.25)

    def test_equal_endpoint_z_does_not_hide_tilted_arc_minimum(self):
        value = self.read((2, 0, -5.1), (-2, 0, -5.1), arc_axis=(0, 1, 0))
        self.assertAlmostEqual(value, -7.1)
        with self.assertRaisesRegex(ValueError, 'радиус'):
            self.read((2, 0, -5.1), (-2.000025, 0, -5.1), arc_axis=(0, 1, 0))

    def test_small_tilt_with_large_radius_keeps_real_interior_minimum(self):
        tilt, radius = 1e-8, 1e8
        normal = (0, math.sin(tilt), math.cos(tilt))
        value = self.read((radius, 0, -5.1), (-radius, 0, -5.1), arc_axis=normal)
        self.assertAlmostEqual(value, -5.1 - radius * math.sin(tilt))
        with self.assertRaisesRegex(ValueError, 'радиус'):
            self.read((radius, 0, -5.1), (-radius - 200, 0, -5.1), arc_axis=normal)

    def test_nonplanar_circular_motion_is_still_rejected(self):
        with self.assertRaises(ValueError):
            self.read((9, 0, -5.1), (0, 9.000025, -5.2))
        with self.assertRaisesRegex(ValueError, 'плоскостях'):
            self.read((9, 0, -5.1), (0, 9, -5.2))

    def test_zero_radius_and_missing_start_are_still_rejected(self):
        for start, end in (((0, 0, -5.1), (0, 9, -5.1)),
                           ((9, 0, -5.1), (0, 0, -5.1))):
            with self.subTest(start=start), self.assertRaisesRegex(ValueError, 'радиус'):
                self.read(start, end)
        with self.assertRaisesRegex(ValueError, 'начальной точки'):
            ZMIN.arc_path_zmin(None, (0, 9, -5.1), (0, 0, -5.1),
                               (0, 0, 1), False, (0, 0, 1), (0, 0, 0))

    def test_helix_radius_and_revolution_checks_are_retained(self):
        with self.assertRaisesRegex(ValueError, 'радиус'):
            self.read((9, 0, -5.1), (0, 9.000025, -5.1), turns=0.25)
        with self.assertRaisesRegex(ValueError, 'витков'):
            self.read((9, 0, -5.1), (0, 9, -5.1), turns=0.5)
        self.assertAlmostEqual(self.read((9, 0, -5.1), (0, 9, -7.1), turns=1.25), -7.1)


if __name__ == '__main__':
    unittest.main()
