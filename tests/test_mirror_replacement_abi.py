"""UF prototype calls across separately compiled C, without Siemens binaries."""
import ctypes
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import Mock

from test_mirror_replacement_probe import M, ProbeWorld


class ReplacementAbiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        compiler=shutil.which('cc') or shutil.which('gcc') or shutil.which('clang')
        if not compiler or sys.platform=='win32' or ctypes.sizeof(ctypes.c_void_p)!=8:
            raise unittest.SkipTest('Requires a 64-bit POSIX C compiler, not NX')
        cls.folder=tempfile.TemporaryDirectory();cls.addClassCleanup(cls.folder.cleanup)
        output=Path(cls.folder.name)/'model.so'
        subprocess.run([compiler,'-std=c11','-Wall','-Wextra','-Werror',
            '-dynamiclib' if sys.platform=='darwin' else '-shared','-fPIC',
            str(Path(__file__).parent/'fixtures/ModelReferencesAbi.c'),'-o',str(output)],
            check=True,capture_output=True,text=True,timeout=30)
        cls.library=ctypes.CDLL(str(output))

    def setUp(self):
        n=self.native=object.__new__(M.NativeCam);n.libs=[self.library]
        n.free=n.bind('UF_free',(ctypes.c_void_p,),None)
        n.fail_message=lambda code,buffer:setattr(buffer,'value',b'UF model fixture error')
        self.reset=n.bind('fixture_reset',(ctypes.c_int,),None)
        self.stat=n.bind('fixture_stat',(ctypes.c_int,))
        self.tag=n.bind('fixture_tag',(ctypes.c_int,),ctypes.c_uint32)
        self.tolerance=n.bind('fixture_tolerance',(ctypes.c_int,),ctypes.c_double)
        self.reset(0)

    def tearDown(self):
        self.assertEqual(self.stat(0),0,'Every valid returned allocation must be released')
        self.assertEqual(self.stat(1),0,'Correct allocator, pointer level and exactly one free')

    def test_reference_tags_order_duplicates_and_names_cross_c(self):
        self.assertEqual(self.native.feature_references(0xf0000042),
                         ((0xf0000001,b'face'),(29,b'start'),(29,b'end')))
        self.assertEqual(self.tag(0),0xf0000042)
        self.assertEqual((self.stat(2),self.stat(3)),(1,3))

    def test_successful_empty_references_are_explicit(self):
        self.reset(1)
        self.assertEqual(self.native.feature_references(8),())
        self.assertEqual(self.stat(2),0)

    def test_missing_count_or_inconsistent_arrays_are_not_empty(self):
        for mode in (3,4,5,6,7):
            self.reset(mode)
            with self.subTest(mode=mode), self.assertRaisesRegex(RuntimeError,'размер/массив'):
                self.native.feature_references(8)
            self.assertEqual(self.stat(0),0);self.assertEqual(self.stat(1),0)

    def test_invalid_parent_and_null_name_free_both_arrays(self):
        for mode in (8,9):
            self.reset(mode)
            with self.subTest(mode=mode), self.assertRaises(RuntimeError): self.native.feature_references(8)
            self.assertEqual((self.stat(2),self.stat(3)),(1,3))
            self.assertEqual(self.stat(0),0)

    def test_reference_errors_preserve_api_code_and_free_partial_output(self):
        for mode,code in ((2,777),(10,778)):
            self.reset(mode)
            with self.subTest(mode=mode), self.assertRaises(M.NativeApiError) as caught:
                self.native.feature_references(8)
            self.assertEqual(caught.exception.code,code)
            self.assertEqual(caught.exception.context,'UF_MODL_ask_references_of_features')
            self.assertEqual(self.stat(0),0)

    def test_raw_name_bytes_are_preserved_across_diagnostic_decoding(self):
        self.reset(11)
        refs=self.native.feature_references(8)
        self.assertEqual(refs[1],(29,b'\xff'));self.assertEqual(refs[2],(29,b'\\xff'))
        self.assertNotEqual(refs[1],refs[2])

    def test_invalid_tag_does_not_call_native_reference_query(self):
        for tag in (0,-1,0x100000000,True,1.0,'8'):
            with self.subTest(tag=tag), self.assertRaises(RuntimeError): self.native.feature_references(tag)
        self.assertEqual(self.tag(0),0)

    def test_bind_preflight_checks_every_export_before_any_call(self):
        self.native.bind=Mock(wraps=self.native.bind)
        self.native.prepare_replacement_api()
        self.assertEqual(self.native.bind.call_count,10)
        self.native.prepare_replacement_api()
        self.assertEqual(self.native.bind.call_count,10)
        self.assertEqual([self.stat(i) for i in (4,5,6,7)],[0,0,0,0])

    def test_missing_export_does_not_complete_preflight_or_start_prototype(self):
        original=self.native.bind
        def bind(name,*args):
            if name=='UF_MODL_compare_topology': raise RuntimeError('missing '+name)
            return original(name,*args)
        self.native.bind=bind
        w=ProbeWorld();w.t.native=self.native
        with self.assertRaisesRegex(RuntimeError,'UF_MODL_compare_topology'): w.probe.run()
        self.assertFalse(hasattr(self.native,'_replacement_api'))
        self.assertEqual(w.events,[])
        self.assertEqual(self.tag(0),0)

    def test_reported_missing_python_method_now_reaches_native_reference_read(self):
        w=ProbeWorld();self.reset(1)
        self.assertFalse(hasattr(w.t.uf.Modl,'AskReferencesOfFeatures'))
        w.t.native.feature_references=self.native.feature_references
        w.probe.snapshot()
        self.assertEqual(self.tag(0),23)  # All original features were read.
        self.assertEqual(w.events,[])
        self.assertTrue(all(v==() for v in w.probe.initial_refs.values()))

    def test_geometry_compare_preserves_unsigned_tags_and_relation(self):
        self.assertEqual(self.native.compare_geometry(0xf0000001,0xe0000002),1)
        self.assertEqual((self.tag(1),self.tag(2)),(0xf0000001,0xe0000002))
        self.reset(15);self.assertEqual(self.native.compare_geometry(5,6),0)

    def test_geometry_compare_missing_output_invalid_value_and_error_fail(self):
        for mode in (12,13,14):
            self.reset(mode)
            with self.subTest(mode=mode), self.assertRaises(RuntimeError): self.native.compare_geometry(5,6)

    def test_topology_compare_passes_both_double_tolerances(self):
        self.assertEqual(self.native.compare_topology(0xf0000001,0xe0000002,0.01,0.0025),5)
        self.assertEqual((self.tag(1),self.tag(2)),(0xf0000001,0xe0000002))
        self.assertEqual((self.tolerance(0),self.tolerance(1)),(0.01,0.0025))

    def test_topology_read_errors_and_invalid_input_never_mean_match(self):
        for mode in (16,17):
            self.reset(mode)
            with self.subTest(mode=mode), self.assertRaises(RuntimeError): self.native.compare_topology(5,6,.01,.01)
        self.reset(0)
        for a,b in ((0,.01),(float('nan'),.01),(.01,-1),(True,.01)):
            with self.subTest(a=a,b=b), self.assertRaises(ValueError): self.native.compare_topology(5,6,a,b)
        self.assertEqual(self.stat(5),0)

    def test_unknown_topology_status_is_rejected_by_mapping_not_silently_matched(self):
        w=ProbeWorld();self.reset(18)
        w.t.native.compare_topology=self.native.compare_topology
        with self.assertRaisesRegex(RuntimeError,'не прочитан статус'): w.probe.run()
        w.t.native.transform_entities.assert_not_called()

    def test_parameter_removal_uses_one_unsigned_candidate_in_a_linked_list(self):
        self.native.delete_candidate_parameters(0xf0000042)
        self.assertEqual(self.tag(3),0xf0000042)
        self.assertEqual((self.stat(6),self.stat(7)),(1,1))

    def test_parameter_list_errors_do_not_reach_destructive_call(self):
        for mode in range(19,28):
            self.reset(mode)
            with self.subTest(mode=mode), self.assertRaises(RuntimeError): self.native.delete_candidate_parameters(0xf0000042)
            self.assertEqual(self.stat(7),0)
            self.assertEqual(self.stat(6),0 if mode==19 else 1)
            self.assertEqual(self.stat(0),0)

    def test_parameter_removal_error_and_cleanup_error_keep_primary_context(self):
        for mode,code,context in ((28,785,'UF_MODL_delete_object_parms'),
                                  (29,786,'UF_MODL_delete_list'),(30,785,'UF_MODL_delete_object_parms')):
            self.reset(mode)
            with self.subTest(mode=mode), self.assertRaises(M.NativeApiError) as caught:
                self.native.delete_candidate_parameters(0xf0000042)
            self.assertEqual(caught.exception.code,code);self.assertEqual(caught.exception.context,context)
            if mode==30: self.assertIn('UF_MODL_delete_list',str(caught.exception))
            self.assertEqual((self.stat(6),self.stat(7)),(1,1));self.assertEqual(self.stat(0),0)

    def test_invalid_candidate_tag_is_rejected_before_list_allocation(self):
        for tag in (0,-1,0x100000000,True,1.0,'8'):
            with self.subTest(tag=tag), self.assertRaises(RuntimeError): self.native.delete_candidate_parameters(tag)
        self.assertEqual((self.stat(6),self.stat(7)),(0,0))


if __name__=='__main__': unittest.main()
