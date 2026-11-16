"""UF database cycling across C; does not test NX 2606 enumeration semantics."""
import ctypes
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

from test_mirror_project import M


class CollectorInventoryAbiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        compiler=shutil.which('cc') or shutil.which('gcc') or shutil.which('clang')
        if not compiler or sys.platform=='win32' or ctypes.sizeof(ctypes.c_void_p)!=8:
            raise unittest.SkipTest('Requires a 64-bit POSIX C compiler, not NX')
        cls.folder=tempfile.TemporaryDirectory();cls.addClassCleanup(cls.folder.cleanup)
        output=Path(cls.folder.name)/'inventory.so'
        subprocess.run([compiler,'-std=c11','-Wall','-Wextra','-Werror',
            '-dynamiclib' if sys.platform=='darwin' else '-shared','-fPIC',
            str(Path(__file__).parent/'fixtures/ObjectInventoryAbi.c'),'-o',str(output)],
            check=True,capture_output=True,text=True,timeout=30)
        cls.library=ctypes.CDLL(str(output))

    def setUp(self):
        self.native=object.__new__(M.NativeCam);self.native.libs=[self.library]
        self.native.fail_message=lambda code,buffer:setattr(buffer,'value',b'UF inventory fixture error')
        self.reset=self.native.bind('fixture_reset',(ctypes.c_int,),None)
        self.stat=self.native.bind('fixture_stat',(ctypes.c_int,))
        self.tag=self.native.bind('fixture_tag',(ctypes.c_int,),ctypes.c_uint32)
        self.reset(0)

    def test_unsigned_tag_return_and_complete_cycle_include_internal_containers(self):
        all_tags,containers=self.native.object_inventory(0xf0000042,113)
        self.assertEqual(all_tags,{0xf0000011,0xe0000017,0xf0000055,0xd0000009})
        self.assertEqual(containers,{0xe0000017,0xf0000055,0xd0000009})
        self.assertEqual((self.tag(0),self.tag(1),self.tag(2)),(0xf0000042,0xd0000009,0xd0000009))
        self.assertEqual((self.stat(0),self.stat(1)),(5,4))

    def test_installed_container_type_is_used_instead_of_a_hardcoded_number(self):
        all_tags,containers=self.native.object_inventory(8,1)
        self.assertEqual(containers,{0xf0000011});self.assertEqual(len(all_tags),4)

    def test_null_termination_is_not_passed_to_type_reader(self):
        self.reset(1)
        self.assertEqual(self.native.object_inventory(8,113),(frozenset(),frozenset()))
        self.assertEqual((self.stat(0),self.stat(1)),(1,0))

    def test_repeated_tag_is_rejected_without_infinite_loop_or_second_type_read(self):
        self.reset(2)
        with self.assertRaisesRegex(RuntimeError,'повтор tag'):self.native.object_inventory(8,113)
        self.assertEqual((self.stat(0),self.stat(1)),(2,1))

    def test_database_bound_is_checked_without_mutating_any_object(self):
        with patch.object(M,'MAX_PART_OBJECTS',2):
            with self.assertRaisesRegex(RuntimeError,'предел обхода'):self.native.object_inventory(8,113)
        self.assertEqual((self.stat(0),self.stat(1)),(3,2))

    def test_type_error_preserves_code_and_tag_and_stops_cycling(self):
        self.reset(3)
        with self.assertRaises(M.NativeApiError) as caught:self.native.object_inventory(8,113)
        self.assertEqual(caught.exception.code,7408)
        self.assertIn(str(0xf0000011),caught.exception.context)
        self.assertEqual((self.stat(0),self.stat(1)),(1,1))

    def test_unwritten_type_or_subtype_is_never_treated_as_another_object(self):
        for mode in (4,5):
            self.reset(mode)
            with self.subTest(mode=mode),self.assertRaisesRegex(RuntimeError,'не прочитан тип/подтип'):
                self.native.object_inventory(8,113)
            self.assertEqual((self.stat(0),self.stat(1)),(1,1))

    def test_invalid_arguments_are_rejected_before_native_calls(self):
        for part in (0,-1,0x100000000,True,1.0,'8'):
            with self.subTest(part=part),self.assertRaises(RuntimeError):self.native.object_inventory(part,113)
        for kind in (None,True,0,-1,1.0,'113',0x80000000):
            with self.subTest(kind=kind),self.assertRaises(RuntimeError):self.native.object_inventory(8,kind)
        self.assertEqual((self.stat(0),self.stat(1)),(0,0))

    def test_all_exports_bind_before_first_cycle_and_no_partial_api_is_cached(self):
        original=self.native.bind
        def bind(name,*args):
            if name=='UF_OBJ_ask_type_and_subtype':raise RuntimeError('missing type reader')
            return original(name,*args)
        with patch.object(self.native,'bind',side_effect=bind):
            with self.assertRaisesRegex(RuntimeError,'missing type reader'):self.native.object_inventory(8,113)
        self.assertFalse(hasattr(self.native,'_object_inventory_api'))
        self.assertEqual((self.stat(0),self.stat(1)),(0,0))

    def test_repeated_inventory_is_fresh_but_exports_bind_once(self):
        self.native.bind=Mock(wraps=self.native.bind)
        self.native.object_inventory(8,113);self.reset(1)
        self.assertEqual(self.native.object_inventory(8,113),(frozenset(),frozenset()))
        self.assertEqual(self.native.bind.call_count,2)


if __name__=='__main__':unittest.main()
