"""Call new spline creation through compiled C, not through Siemens/NX."""
import ctypes
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from test_mirror_history_curves import M, rational, line


class SplineCreationAbiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        compiler=shutil.which('cc') or shutil.which('gcc') or shutil.which('clang')
        if not compiler or sys.platform=='win32' or ctypes.sizeof(ctypes.c_void_p)!=8:
            raise unittest.SkipTest('Requires a 64-bit POSIX C compiler, not NX')
        cls.folder=tempfile.TemporaryDirectory();cls.addClassCleanup(cls.folder.cleanup)
        output=Path(cls.folder.name)/'create_spline.so'
        subprocess.run([compiler,'-std=c11','-Wall','-Wextra','-Werror',
            '-dynamiclib' if sys.platform=='darwin' else '-shared','-fPIC',
            str(Path(__file__).parent/'fixtures/HistorySplineCreateAbi.c'),'-lm','-o',str(output)],
            check=True,capture_output=True,text=True,timeout=30)
        cls.library=ctypes.CDLL(str(output))

    def setUp(self):
        n=self.native=object.__new__(M.NativeCam);n.libs=[self.library]
        n.free=n.bind('UF_free',(ctypes.c_void_p,),None)
        n.fail_message=lambda code,buffer:setattr(buffer,'value',b'creation fixture error')
        self.reset=n.bind('fixture_reset',(ctypes.c_int,ctypes.c_int),None)
        self.stat=n.bind('fixture_stat',(ctypes.c_int,))
        self.reset(0,1)
        self.curve=rational()

    def tearDown(self):
        self.assertEqual(self.stat(1),0,'Every states allocation must be freed')
        self.assertEqual(self.stat(3),0,'UF_free must be called exactly once per allocation')

    def test_native_structure_alignment_and_unsigned_returned_tag(self):
        self.assertEqual(ctypes.sizeof(M._CurveSpline),48)
        self.assertEqual(M._CurveSpline.knots.offset,16)
        self.assertEqual(M._CurveSpline.poles.offset,24)
        self.assertEqual(M._CurveSpline.end_param.offset,40)
        self.assertEqual(ctypes.sizeof(M._CurveState),32)
        self.assertEqual(self.native.create_history_spline(self.curve),0xf1000029)
        self.assertEqual(self.stat(0),1)

    def test_nonrational_definition_passes_integer_flag_and_unit_weights(self):
        self.reset(0,0);self.curve['data']['rational']=False
        self.curve['data']['poles']=((1.,0.,0.,1.),(1.,1.,0.,1.),(0.,1.,0.,1.))
        self.assertEqual(self.native.create_history_spline(self.curve),0xf1000029)

    def test_explicit_affine_normalization_precedes_native_call(self):
        self.curve['data']['knots']=(2.,2.,2.,5.,5.,5.);self.curve['limits']=(2.6,4.4)
        with self.assertRaisesRegex(RuntimeError,'явная нормализация'):self.native.create_history_spline(self.curve)
        self.assertEqual(self.stat(0),0)
        planned=M.history_curve_candidate_definition(self.curve)
        self.assertEqual(self.native.create_history_spline(planned),0xf1000029)

    def test_native_error_retains_code_without_claiming_new_curve(self):
        self.reset(1,1)
        with self.assertRaises(M.NativeApiError) as caught:self.native.create_history_spline(self.curve)
        self.assertEqual(caught.exception.code,711);self.assertEqual(self.stat(2),0)

    def test_zero_output_tag_is_rejected(self):
        self.reset(2,1)
        with self.assertRaises(RuntimeError):self.native.create_history_spline(self.curve)

    def test_untouched_or_negative_state_count_is_not_empty_success(self):
        for mode in (3,4):
            with self.subTest(mode=mode):
                self.reset(mode,1)
                with self.assertRaisesRegex(RuntimeError,'массив состояний'):self.native.create_history_spline(self.curve)

    def test_positive_count_with_null_array_is_rejected_without_dereferencing(self):
        self.reset(5,1)
        with self.assertRaisesRegex(RuntimeError,'массив состояний'):self.native.create_history_spline(self.curve)

    def test_all_reported_states_are_rejected_and_aligned_values_logged(self):
        self.reset(6,1)
        with self.assertRaisesRegex(RuntimeError,'принятие запрещено') as caught:self.native.create_history_spline(self.curve)
        self.assertIn('(101, 3, (2.0, 5.0, 0.0))',str(caught.exception))
        self.assertIn('(202, 4, (0.0, 0.0, 0.125))',str(caught.exception))
        self.assertEqual(self.stat(2),1)

    def test_partial_allocation_on_error_is_freed_and_original_code_kept(self):
        self.reset(7,1)
        with self.assertRaises(M.NativeApiError) as caught:self.native.create_history_spline(self.curve)
        self.assertEqual(caught.exception.code,717)
        self.assertIn(str(0xf1000029),str(caught.exception))
        self.assertEqual(self.stat(2),1)

    def test_zero_count_nonnull_array_is_freed_without_reading_unused_storage(self):
        self.reset(8,1)
        self.assertEqual(self.native.create_history_spline(self.curve),0xf1000029)
        self.assertEqual(self.stat(2),1)

    def test_excessive_state_count_is_rejected_before_reading_array(self):
        self.reset(14,1)
        with self.assertRaisesRegex(RuntimeError,'массив состояний'):self.native.create_history_spline(self.curve)
        self.assertEqual(self.stat(2),1)

    def test_mutated_knots_poles_scalars_trim_or_pointers_are_rejected(self):
        for mode in (9,10,11,12,13,15):
            with self.subTest(mode=mode):
                self.reset(mode,1)
                with self.assertRaisesRegex(RuntimeError,'изменил входное'):self.native.create_history_spline(self.curve)
                self.tearDown()

    def test_invalid_weight_never_reaches_c(self):
        self.curve['data']['poles']=((1.,0.,0.,1.),(1.,1.,0.,0.),(0.,1.,0.,1.))
        with self.assertRaisesRegex(RuntimeError,'положительный вес'):self.native.create_history_spline(self.curve)
        self.assertEqual(self.stat(0),0)

    def test_other_curve_kind_does_not_reach_c_spline_api(self):
        with self.assertRaisesRegex(RuntimeError,'определение Spline'):self.native.create_history_spline(line())
        self.assertEqual(self.stat(0),0)

    def test_missing_creation_export_leaves_no_cached_binding(self):
        with patch.object(self.native,'bind',side_effect=RuntimeError('missing export')):
            with self.assertRaisesRegex(RuntimeError,'missing export'):self.native.create_history_spline(self.curve)
        self.assertFalse(hasattr(self.native,'_history_create_spline'))
        self.assertEqual(self.stat(0),0)
