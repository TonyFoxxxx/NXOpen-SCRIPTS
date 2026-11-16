"""UF_EVAL calls across compiled C; this cannot validate Siemens binaries."""
import ctypes
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

from test_mirror_project import M


class HistoryCurveAbiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        compiler=shutil.which('cc') or shutil.which('gcc') or shutil.which('clang')
        if not compiler or sys.platform=='win32' or ctypes.sizeof(ctypes.c_void_p)!=8:
            raise unittest.SkipTest('Requires a 64-bit POSIX C compiler, not NX')
        cls.folder=tempfile.TemporaryDirectory();cls.addClassCleanup(cls.folder.cleanup)
        output=Path(cls.folder.name)/'curve.so'
        subprocess.run([compiler,'-std=c11','-Wall','-Wextra','-Werror',
            '-dynamiclib' if sys.platform=='darwin' else '-shared','-fPIC',
            str(Path(__file__).parent/'fixtures/HistoryCurveAbi.c'),'-lm','-o',str(output)],
            check=True,capture_output=True,text=True,timeout=30)
        cls.library=ctypes.CDLL(str(output))

    def setUp(self):
        n=self.native=object.__new__(M.NativeCam);n.libs=[self.library]
        n.free=n.bind('UF_free',(ctypes.c_void_p,),None)
        n.fail_message=lambda code,buffer:setattr(buffer,'value',b'UF_EVAL fixture error')
        self.reset=n.bind('fixture_reset',(ctypes.c_int,ctypes.c_int),None)
        self.stat=n.bind('fixture_stat',(ctypes.c_int,))
        self.tag=n.bind('fixture_tag',(),ctypes.c_uint32)
        self.reset(0,0)

    def tearDown(self):
        self.assertEqual(self.stat(0),0,'Every evaluator and returned array must be released')
        self.assertEqual(self.stat(1),0,'Use the correct free function exactly once')
        self.assertEqual(self.stat(6),0,'Never call the approximating legacy initializer')

    def rejected(self, kind, modes, exception=RuntimeError):
        for mode in modes:
            with self.subTest(mode=mode):
                self.reset(kind,mode)
                with self.assertRaises(exception): self.native.history_curve(19)
                self.tearDown()

    def check_curve(self, kind):
        self.reset(kind,0);curve=self.native.history_curve(0xf0000028)
        ends=tuple(M.history_curve_point(curve,u) for u in curve['limits'])
        reflected,error=M.check_history_curve(curve,ends,M.MirrorPlane([-79,4,6],[1,2,3]))
        self.assertLess(error,1e-10)
        self.assertEqual(curve['limits'],reflected['limits'])
        self.assertEqual(self.tag(),0xf0000028)
        self.assertEqual((self.stat(2),self.stat(3)),(1,1))
        return curve,reflected

    def test_line_non_normalized_parameters_and_unsigned_tag(self):
        curve,_=self.check_curve(0)
        self.assertEqual(curve['data'],{'start':(1.,2.,3.),'end':(4.,8.,9.)})
        self.assertEqual(curve['limits'],(2.,5.))

    def test_arc_native_alignment_absolute_axes_radius_and_trim(self):
        curve,_=self.check_curve(1)
        self.assertEqual(ctypes.sizeof(M._EvalArc),104)
        self.assertEqual(M._EvalArc.y_axis.offset,80)
        self.assertEqual(curve['data']['radius'],3.)
        self.assertEqual(curve['data']['center'],(4.,-2.,5.))
        self.assertEqual(curve['data']['x_axis'],(0.,1.,0.))
        self.assertEqual(curve['limits'],(.2,1.4))

    def test_closed_arc_with_coincident_endpoints_keeps_full_period(self):
        curve,_=self.check_curve(3)
        self.assertTrue(curve['periodic'])
        self.assertTrue(M.close_values(curve['samples'][0][1],curve['samples'][-1][1]))
        self.assertFalse(M.close_values(curve['samples'][0][1],curve['samples'][4][1]))

    def test_rational_spline_weighted_poles_trim_and_separate_frees(self):
        curve,reflected=self.check_curve(2)
        self.assertEqual(curve['data']['order'],3)
        self.assertTrue(curve['data']['rational'])
        self.assertEqual(self.stat(5),2)
        self.assertEqual(curve['data']['knots'],reflected['data']['knots'])
        self.assertEqual([p[3] for p in curve['data']['poles']],[p[3] for p in reflected['data']['poles']])
        for _,p in curve['samples']: self.assertAlmostEqual(p[0]*p[0]+p[1]*p[1],1.)

    def test_nonrational_spline_preserves_unit_weights(self):
        curve,_=self.check_curve(6)
        self.assertFalse(curve['data']['rational'])

    def test_ellipse_minor_major_layout_and_parametric_shape(self):
        curve,_=self.check_curve(7)
        self.assertEqual(ctypes.sizeof(M._EvalEllipse),112)
        self.assertEqual(M._EvalEllipse.minor.offset,24)
        self.assertEqual((curve['data']['major'],curve['data']['minor']),(5.,2.))
        for _,p in curve['samples']:
            self.assertAlmostEqual(((p[1]+2)/5)**2+((p[2]-5)/2)**2,1.)

    def test_ellipse_invalid_radii_are_rejected(self):
        self.rejected(7,(12,13))

    def test_spline_metadata_flags_counts_and_order_are_independently_checked(self):
        self.assertEqual(ctypes.sizeof(M._EvalSpline),16)
        self.assertEqual(M._EvalSpline.order.offset,4)
        self.rejected(2,(33,34,35,36,37))

    def test_missing_initialize2_does_not_fall_back_to_approximating_initializer(self):
        original=self.native.bind
        def bind(name,*args):
            if name=='UF_EVAL_initialize_2':raise RuntimeError('missing exact initializer')
            return original(name,*args)
        with patch.object(self.native,'bind',side_effect=bind):
            with self.assertRaisesRegex(RuntimeError,'missing exact initializer'):self.native.history_curve(19)
        self.assertFalse(hasattr(self.native,'_history_curve_api'))
        self.assertEqual(self.stat(2),0)

    def test_partial_initialize_failure_releases_valid_returned_handle(self):
        self.rejected(0,(1,),M.NativeApiError)
        self.assertEqual((self.stat(2),self.stat(3)),(1,1))

    def test_null_or_failed_initialize_does_not_free_null(self):
        self.rejected(0,(2,3))
        self.assertEqual(self.stat(3),0)

    def test_unwritten_invalid_and_failed_logical_flags_are_not_false(self):
        self.rejected(0,(4,5,6))

    def test_unwritten_inverted_nonfinite_or_failed_limits_stop(self):
        self.rejected(0,(7,8,9,10))

    def test_unknown_or_ambiguous_curve_is_not_fitted_or_skipped(self):
        for kind in (4,5):
            self.reset(kind,0)
            with self.assertRaisesRegex(RuntimeError,'однозначного полного определения'): self.native.history_curve(19)
            self.assertEqual(self.stat(4),0)

    def test_arc_unwritten_fields_bad_radius_axes_and_flag_stop(self):
        self.rejected(1,(11,12,13,14))

    def test_missing_arrays_counts_and_resource_overflow_release_allocations(self):
        self.rejected(2,(15,16,17,29))

    def test_nan_pole_zero_weight_bad_knots_domain_and_order_stop(self):
        self.rejected(2,(18,19,20,21,30))

    def test_array_api_errors_keep_code_and_release_partial_outputs(self):
        for mode,code,freed in ((27,627,2),(28,628,1)):
            self.reset(2,mode)
            with self.assertRaises(M.NativeApiError) as caught: self.native.history_curve(19)
            self.assertEqual(caught.exception.code,code)
            self.assertEqual(self.stat(5),freed)

    def test_evaluation_unwritten_or_api_error_releases_evaluator(self):
        self.rejected(1,(23,24))

    def test_evaluation_error_is_checked_even_when_endpoints_match(self):
        self.reset(0,31);curve=self.native.history_curve(19)
        with self.assertRaisesRegex(RuntimeError,'не совпало с UF_EVAL'):
            M.check_history_curve(curve,((1,2,3),(4,8,9)),M.MirrorPlane([0,0,0],[1,0,0]))

    def test_wrong_arc_evaluation_cannot_pass_on_analytic_parameters_alone(self):
        self.reset(1,22);curve=self.native.history_curve(19)
        ends=tuple(M.history_curve_point(curve,u) for u in curve['limits'])
        with self.assertRaisesRegex(RuntimeError,'не совпало с UF_EVAL'):
            M.check_history_curve(curve,ends,M.MirrorPlane([0,0,0],[1,0,0]))

    def test_free_failure_is_fatal_and_retains_its_code(self):
        self.reset(0,25)
        with self.assertRaises(M.CurveEvaluatorCleanupError) as caught: self.native.history_curve(19)
        self.assertEqual(caught.exception.code,625)

    def test_read_and_free_failures_both_survive_in_exception_chain(self):
        self.reset(1,26)
        with self.assertRaises(M.CurveEvaluatorCleanupError) as caught: self.native.history_curve(19)
        self.assertEqual(caught.exception.code,625)
        self.assertEqual(caught.exception.__cause__.code,624)

    def test_exports_preflight_atomically_before_initialization(self):
        original=self.native.bind
        def bind(name,*args):
            if name=='UF_EVAL_is_periodic':raise RuntimeError('missing periodic reader')
            return original(name,*args)
        with patch.object(self.native,'bind',side_effect=bind):
            with self.assertRaisesRegex(RuntimeError,'missing periodic reader'):self.native.history_curve(19)
        self.assertFalse(hasattr(self.native,'_history_curve_api'))
        self.assertEqual(self.stat(2),0)

    def test_invalid_tag_never_enters_c(self):
        for tag in (0,-1,0x100000000,True,1.0,'8'):
            with self.subTest(tag=tag),self.assertRaises(RuntimeError):self.native.history_curve(tag)
        self.assertEqual(self.stat(2),0)
