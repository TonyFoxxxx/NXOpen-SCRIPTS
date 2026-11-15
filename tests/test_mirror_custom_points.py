"""Custom-point transfer and independent C ABI regressions; no real NX runtime."""
import ctypes
import importlib.util
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock

ROOT = Path(__file__).parents[1]
SPEC = importlib.util.spec_from_file_location('mirror_points',ROOT/'scripts/NX_Mirror_Project.py')
M = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(M)
ZERO = (0.0,0.0,0.0)


class CustomPointAbiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        compiler = shutil.which('cc') or shutil.which('gcc') or shutil.which('clang')
        if not compiler or sys.platform=='win32' or ctypes.sizeof(ctypes.c_void_p)!=8:
            raise unittest.SkipTest('Independent C ABI fixture needs a 64-bit POSIX compiler')
        cls.folder = tempfile.TemporaryDirectory()
        cls.addClassCleanup(cls.folder.cleanup)
        out = Path(cls.folder.name)/'custom_points.so'
        subprocess.run([compiler,'-std=c11','-Wall','-Wextra','-Werror',
            '-dynamiclib' if sys.platform=='darwin' else '-shared','-fPIC',
            str(ROOT/'tests/fixtures/CamCustomPointsAbi.c'),'-o',str(out)],
            check=True,capture_output=True,text=True,timeout=30)
        cls.lib = ctypes.CDLL(str(out))

    def setUp(self):
        self.native = object.__new__(M.NativeCam)
        n = self.native
        n.libs = [self.lib]
        u,i,p = ctypes.c_uint32,ctypes.c_int,ctypes.c_void_p
        n.free = n.bind('UF_free',(p,),None)
        n.custom = n.bind('UF_CAMGEOM_ask_custom_points',(u,i,ctypes.POINTER(i),ctypes.POINTER(p)))
        n.fail_message = lambda code,buffer:setattr(buffer,'value',b'custom point test error')
        self.reset = n.bind('fixture_reset',(i,i,i),None)
        self.stat = n.bind('fixture_stat',(i,))
        self.layout = n.bind('fixture_layout',(i,),ctypes.c_size_t)
        self.target = n.bind('fixture_target',(),u)
        self.record = n.bind('fixture_record',(i,ctypes.POINTER(M._CustomPointData)),None)
        self.reset(0,0,0)

    def tearDown(self):
        self.assertEqual(self.stat(1),0,'No invalid/double frees')
        self.assertEqual(self.stat(2),0,'No outstanding allocated records or arrays')

    def test_layout_and_pointer_array_read_preserve_unsigned_tags_and_used_depth_fields(self):
        self.assertEqual([self.layout(i) for i in range(6)],[56,0,8,32,40,48])
        self.assertEqual(ctypes.sizeof(M._CustomPointData),self.layout(0))
        for i,(name,_) in enumerate(M._CustomPointData._fields_,1):
            self.assertEqual(getattr(M._CustomPointData,name).offset,self.layout(i))
        actual = self.native.read_custom_points(0xf0000080,1,"PlanarMilling")
        self.assertEqual(actual,((0xf0000042,(12.5,-7.25,3.0),(1.5,-2.25,None)),
                                 (0,(-100.0,0.0,8.25),(0.0,0.0,None))))
        self.assertEqual(self.target(),0xf0000080)
        self.assertEqual(self.stat(0),3)

    def test_null_array_and_null_record_are_rejected_before_use(self):
        for mode in (1,2):
            with self.subTest(mode=mode):
                self.reset(mode,0,0)
                with self.assertRaises(RuntimeError): self.native.read_custom_points(100,1,"PlanarMilling")
                self.assertEqual(self.stat(2),0)

    def test_nonfinite_data_rejects_and_frees_every_record(self):
        self.reset(3,0,0)
        with self.assertRaisesRegex(RuntimeError,'координаты/глубины'):
            self.native.read_custom_points(100,1,"PlanarMilling")
        self.assertEqual(self.stat(0),3)

    def test_repeated_record_pointer_is_rejected_without_double_free(self):
        self.reset(4,0,0)
        with self.assertRaisesRegex(RuntimeError,'повторная запись'):
            self.native.read_custom_points(100,1,"PlanarMilling")
        self.assertEqual(self.stat(0),2)

    def test_api_error_preserves_code_and_frees_partial_output(self):
        self.reset(5,0,0)
        with self.assertRaises(M.NativeApiError) as caught:
            self.native.read_custom_points(100,1,"PlanarMilling")
        self.assertEqual(caught.exception.code,1345005)
        self.assertEqual(self.stat(0),3)

    def test_invalid_counts_never_dereference_the_array(self):
        for mode in (6,7):
            with self.subTest(mode=mode):
                self.reset(mode,0,0)
                with self.assertRaisesRegex(RuntimeError,'массив'):
                    self.native.read_custom_points(100,1,"PlanarMilling")
                self.assertEqual(self.stat(0),1)

    def test_available_empty_list_is_a_real_empty_tuple(self):
        self.reset(8,0,0)
        self.assertEqual(self.native.read_custom_points(100,0,"PlanarMilling"),())
        self.assertEqual(self.stat(0),0)

    def test_write_uses_record_pointers_and_preserves_order_tags_and_depths(self):
        points = ((0,(-30.5,2.25,9.0),(0.,0.,None)),(0xf0000042,(1.,2.,3.),(4.,5.,None)))
        self.native.replace_custom_points(0xf0000080,1,points,"PlanarMilling")
        self.assertEqual([self.stat(i) for i in (3,4,5,6)],[1,1,2,1])
        self.assertEqual(self.target(),0xf0000080)
        for index,(tag,point,depths) in enumerate(points):
            value=M._CustomPointData(); self.record(index,ctypes.byref(value))
            self.assertEqual((value.point_tag,tuple(value.point_acs),
                (value.upper_depth,value.lower_depth,value.depth)),(tag,point,depths[:2]+(0.,)))

    def test_invalid_write_data_does_not_delete_existing_list(self):
        for points in ((),((0,(float('nan'),0,0),(0.,0.,None)),),((-1,ZERO,(0.,0.,None)),),((0,(1,2),(0.,0.,None)),)):
            with self.subTest(points=points):
                with self.assertRaises(RuntimeError): self.native.replace_custom_points(100,1,points,"PlanarMilling")
        self.assertEqual(self.stat(3),0)

    def test_both_write_methods_resolve_before_deletion(self):
        bind=self.native.bind
        self.native.bind=lambda name,*a: (_ for _ in ()).throw(RuntimeError('missing append')) if 'append' in name else bind(name,*a)
        with self.assertRaisesRegex(RuntimeError,'missing append'):
            self.native.replace_custom_points(100,1,((0,ZERO,(0.,0.,None)),),"PlanarMilling")
        self.assertEqual(self.stat(3),0)

    def test_delete_or_append_failure_propagates_without_retry(self):
        for de,ae in ((71,0),(0,72)):
            with self.subTest(de=de,ae=ae):
                self.reset(0,de,ae)
                with self.assertRaises(M.NativeApiError) as caught:
                    self.native.replace_custom_points(100,1,((0,ZERO,(0.,0.,None)),),"PlanarMilling")
                self.assertEqual(caught.exception.code,de or ae)
                self.assertEqual([self.stat(i) for i in (3,4)],[1,0 if de else 1])

    def test_cut_region_does_not_interpret_unused_depth_even_if_subnormal_nan_or_infinite(self):
        for family in ('PlanarMilling','CavityMilling'):
            for mode in (12,13,14):
                with self.subTest(family=family,mode=mode):
                    self.reset(mode,0,0)
                    points=self.native.read_custom_points(100,1,family)
                    self.assertEqual(points[0][2],(0.,0.,None))
                    self.assertEqual(self.stat(0),3)
                    self.assertEqual(self.stat(2),0)

    def test_predrill_does_not_load_unused_upper_or_lower_depth(self):
        for family in ('PlanarMilling','CavityMilling'):
            with self.subTest(family=family):
                self.reset(15,0,0)
                self.assertEqual(self.native.read_custom_points(100,0,family)[0][2],(None,None,0.))
                self.assertEqual(self.stat(0),3)

    def test_same_nonfinite_bytes_in_active_fields_are_fatal_and_freed(self):
        for kind,mode in ((0,13),(0,14),(1,15)):
            with self.subTest(kind=kind,mode=mode):
                self.reset(mode,0,0)
                with self.assertRaisesRegex(RuntimeError,'координаты/глубины'):
                    self.native.read_custom_points(100,kind,'PlanarMilling')
                self.assertEqual(self.stat(0),3)
                self.assertEqual(self.stat(2),0)

    def test_face_and_surface_have_no_depth_fields_for_documented_channels(self):
        for family,kind in (('FaceMilling',0),('FaceMilling',1),('SurfaceContour',1)):
            for mode in (14,15):
                with self.subTest(family=family,kind=kind,mode=mode):
                    self.reset(mode,0,0)
                    self.assertEqual(self.native.read_custom_points(100,kind,family)[0][2],(None,None,None))
                    self.assertEqual(self.stat(0),3)

    def test_writer_preserves_active_depth_and_initializes_only_unused_request_storage(self):
        cases=((0,'PlanarMilling',(None,None,6.95090615858665e-310),(0.,0.,6.95090615858665e-310)),
               (1,'CavityMilling',(1.5,-2.25,None),(1.5,-2.25,0.)),
               (1,'SurfaceContour',(None,None,None),ZERO))
        for kind,family,depths,expected in cases:
            with self.subTest(kind=kind,family=family):
                self.reset(0,0,0)
                self.native.replace_custom_points(100,kind,((0,(1.,2.,3.),depths),),family)
                value=M._CustomPointData(); self.record(0,ctypes.byref(value))
                self.assertEqual((value.upper_depth,value.lower_depth,value.depth),expected)

    def test_missing_active_value_and_unknown_semantics_cannot_be_written_as_zero(self):
        cases=((1,'PlanarMilling',(None,0.,None)),(0,'CavityMilling',(None,None,None)),
               (1,'PlanarMilling',(0.,0.,7.)),(1,'UnknownOperation',(None,None,None)),
               (0,'SurfaceContour',(None,None,None)))
        for kind,family,depths in cases:
            with self.subTest(kind=kind,family=family,depths=depths):
                with self.assertRaises(RuntimeError):
                    self.native.replace_custom_points(100,kind,((0,ZERO,depths),),family)
        self.assertEqual(self.stat(3),0)

    def test_reported_inactive_subnormal_is_admitted_and_not_frozen_as_cam_state(self):
        self.reset(12,0,0)
        f=Fixture(self.native.read_custom_points(100,1,'PlanarMilling'))
        f.trial.native.read_custom_points=lambda tag,kind,family: self.native.read_custom_points(tag,kind,family) if kind==1 else ()
        baseline=f.capture()
        self.assertEqual(f.trial.custom_points[(100,1)][0][2],(0.,0.,None))
        self.assertIn('depth=<не используется>','\n'.join(f.trial.lines))
        self.assertNotIn('6.95090615858665e-310','\n'.join(f.trial.lines))
        self.assertEqual(self.stat(2),0)
        self.reset(14,0,0)  # The same unused slot now contains NaN.
        self.assertEqual(f.trial.geometry_snapshot(),baseline)
        f.trial.native.replace_custom_points.assert_not_called()


class Point:
    def __init__(self,tag,point,part):
        self.Tag,self.OwningPart,self.IsOccurrence=tag,part,False
        self.Coordinates=NS(X=point[0],Y=point[1],Z=point[2])


class Fixture:
    def __init__(self,points=None,kind='PlanarMilling',channel=1):
        class Operation: pass
        self.node=type(kind,(Operation,),{'Tag':100,'Name':'PERIMETER_FINISH'})()
        self.records={(100,channel):points if points is not None else ((0,(60.,-25.,16.),ZERO),)}
        self.part=NS(Tag=1)
        self.objects={9000:NS(Tag=9000,OwningPart=self.part)}
        for values in self.records.values():
            for tag,point,_ in values:
                if tag: self.objects[tag]=Point(tag,point,self.part)
        t=self.trial=object.__new__(M.Trial)
        t.part,t.nodes,t.refs,t.lines,t.unavailable=self.part,[self.node],{},[],set()
        t.plane=M.MirrorPlane((-79.,0.,0.),(1.,0.,0.))
        t.custom_points,t.current_custom_points,t.custom_point_labels={},{},{}
        t.boundary_planes,t.current_boundaries={},{}
        t.nx=NS(Point=Point,ScCollector=type('Collector',(),{}),
            CAM=NS(Operation=Operation),TaggedObjectManager=NS(GetTaggedObject=self.objects.__getitem__))
        items,boundaries=object(),object()
        def channel_read(fn,tag,channel):
            return ('local',tag,[9000] if fn is items and channel==0 else [])
        def read(tag,channel,operation_class):
            # Fake UF decoder exposes meaningful fields only, as the real
            # decoder is independently exercised through the C fixture above.
            active = ((2,) if channel==0 else (0,1)) if operation_class in ('PlanarMilling','CavityMilling') else ()
            return tuple((point_tag,point,tuple(v if i in active else None for i,v in enumerate(depths)))
                         for point_tag,point,depths in self.records.get((tag,channel),()))
        def replace(tag,channel,points,operation_class):
            self.records[(tag,channel)]=tuple((point_tag,point,tuple(0.0 if v is None else v for v in depths))
                                            for point_tag,point,depths in points)
        t.native=NS(items=items,boundaries=boundaries,item_entity=object(),
            channel=channel_read,entity=lambda *a,**kw:9000,
            read_custom_points=Mock(side_effect=read),
            replace_custom_points=Mock(side_effect=replace),update_model=Mock(),smart_parents=lambda tag:None)

    def capture(self): return self.trial.geometry_snapshot(collect=True)

    def follow_geometry(self):
        # Simulate associative Point entities following the model once.
        moved=set()
        for key,points in self.records.items():
            self.records[key]=tuple((tag,tuple(self.trial.plane.point(point)) if tag else point,depths)
                                    for tag,point,depths in points)
            for tag,point,_ in self.records[key]:
                if tag and tag not in moved:
                    self.objects[tag].Coordinates=NS(X=point[0],Y=point[1],Z=point[2]); moved.add(tag)


class CustomPointTransferTests(unittest.TestCase):
    def test_reported_start_point_and_predrill_coordinates_reflect_about_offset_plane(self):
        for channel in (0,1):
            with self.subTest(channel=channel):
                f=Fixture(channel=channel); baseline=f.capture()
                f.trial.transfer_custom_points(baseline)
                self.assertEqual(f.records[(100,channel)],((0,(-218.,-25.,16.),ZERO),))
                f.trial.verify_custom_points()
                f.trial.native.replace_custom_points.assert_called_once()

    def test_oblique_plane_reflects_numeric_coordinates_and_preserves_order(self):
        points=((0,(7.,8.,9.),ZERO),(0,(-5.,4.,2.),ZERO))
        f=Fixture(points,kind="FaceMilling"); f.trial.plane=M.MirrorPlane((4.,-3.,2.),(1.,2.,3.))
        baseline=f.capture(); f.trial.transfer_custom_points(baseline)
        self.assertEqual(f.records[(100,1)],tuple((0,tuple(f.trial.plane.point(p)),ZERO) for _,p,_ in points))

    def test_tagged_point_keeps_reference_and_needs_no_list_replacement(self):
        f=Fixture(((700,(60.,-25.,16.),ZERO),)); baseline=f.capture()
        self.assertIs(f.trial.refs[700],f.objects[700])
        f.follow_geometry(); f.trial.transfer_custom_points(baseline)
        f.trial.native.replace_custom_points.assert_not_called()
        self.assertEqual(f.records[(100,1)][0][0],700)

    def test_zero_depth_is_not_assumed_inactive_when_planar_or_cavity_reflection_changes_abs_z(self):
        for kind in ('PlanarMilling','CavityMilling'):
            with self.subTest(kind=kind):
                f=Fixture(kind=kind); f.trial.plane=M.MirrorPlane((4.,-3.,2.),(1.,2.,3.))
                with self.assertRaisesRegex(RuntimeError,'смысл даже нулевых глубин'): f.capture()
                f.trial.native.replace_custom_points.assert_not_called()

    def test_mixed_list_retains_tagged_selection_while_numeric_entry_is_replaced(self):
        f=Fixture(((700,(60.,-25.,16.),ZERO),(0,(12.,3.,4.),ZERO))); baseline=f.capture()
        f.follow_geometry(); f.trial.transfer_custom_points(baseline)
        self.assertEqual(f.records[(100,1)],((700,(-218.,-25.,16.),ZERO),(0,(-170.,3.,4.),ZERO)))

    def test_numeric_point_already_moved_by_nx_is_not_mirrored_twice(self):
        f=Fixture(); baseline=f.capture()
        f.records[(100,1)]=((0,(-218.,-25.,16.),ZERO),)
        f.trial.transfer_custom_points(baseline)
        f.trial.native.replace_custom_points.assert_not_called()

    def test_unmoved_tagged_point_is_not_replaced_by_numeric_coordinates(self):
        f=Fixture(((700,(60.,-25.,16.),ZERO),)); baseline=f.capture()
        with self.assertRaisesRegex(RuntimeError,'неожиданное положение'):
            f.trial.transfer_custom_points(baseline)
        f.trial.native.replace_custom_points.assert_not_called()

    def test_unexpected_numeric_motion_is_not_overwritten(self):
        f=Fixture(); baseline=f.capture(); f.records[(100,1)]=((0,(123.,0.,0.),ZERO),)
        with self.assertRaisesRegex(RuntimeError,'неожиданное положение'):
            f.trial.transfer_custom_points(baseline)
        f.trial.native.replace_custom_points.assert_not_called()

    def test_nonzero_depths_stop_with_values_before_any_write(self):
        for depths in ((1.,0.,0.),(0.,-20.,0.),(0.,0.,2.)):
            with self.subTest(depths=depths):
                f=Fixture(((0,(2.,3.,4.),depths),),channel=0 if depths[2] else 1)
                with self.assertRaisesRegex(RuntimeError,'ненулевые глубины'): f.capture()
                self.assertIn('depth=', '\n'.join(f.trial.lines))
                f.trial.native.replace_custom_points.assert_not_called()

    def test_tiny_but_active_depths_are_not_rounded_or_treated_as_unused(self):
        tiny=6.95090615858665e-310
        for channel,depths in ((1,(tiny,0.,99.)),(1,(0.,tiny,99.)),(0,(99.,99.,tiny))):
            with self.subTest(channel=channel,depths=depths):
                f=Fixture(((0,(2.,3.,4.),depths),),channel=channel)
                with self.assertRaisesRegex(RuntimeError,'ненулевые глубины'): f.capture()
                self.assertIn(str(tiny),'\n'.join(f.trial.lines))
                f.trial.native.replace_custom_points.assert_not_called()

    def test_unused_depth_changes_do_not_block_transfer_of_verified_point_data(self):
        f=Fixture(((0,(60.,-25.,16.),(0.,0.,float('nan'))),)); baseline=f.capture()
        f.records[(100,1)]=((0,(60.,-25.,16.),(0.,0.,123.)),)
        f.trial.transfer_custom_points(baseline)
        self.assertEqual(f.records[(100,1)][0][1],(-218.,-25.,16.))
        self.assertEqual(f.trial.current_custom_points[(100,1)][0][2],(0.,0.,None))

    def test_missing_active_depth_is_not_confused_with_unused_storage(self):
        f=Fixture(((0,(2.,3.,4.),(None,0.,99.)),))
        with self.assertRaisesRegex(RuntimeError,'Не прочитано используемое поле.*upper_depth'): f.capture()
        f.trial.native.replace_custom_points.assert_not_called()

    def test_nonempty_unverified_operation_family_is_not_silently_admitted(self):
        f=Fixture(kind='HoleDrilling')
        with self.assertRaisesRegex(RuntimeError,'класса операции не подтверждён'): f.capture()
        self.assertIn('<не проверено>','\n'.join(f.trial.lines))
        self.assertNotIn('<не используется>','\n'.join(f.trial.lines))

    def test_tagged_non_point_external_or_occurrence_reference_is_rejected(self):
        for reason in ('type','external','occurrence','coordinates'):
            with self.subTest(reason=reason):
                f=Fixture(((700,(2.,3.,4.),ZERO),))
                if reason=='type': f.objects[700]=NS(Tag=700,OwningPart=f.part)
                elif reason=='external': f.objects[700].OwningPart=NS(Tag=99)
                elif reason=='occurrence': f.objects[700].IsOccurrence=True
                else: f.objects[700].Coordinates=NS(X=999.,Y=0.,Z=0.)
                with self.assertRaises(RuntimeError): f.capture()

    def test_unavailable_channel_is_distinct_from_available_empty(self):
        f=Fixture(points=())
        def read(tag,kind,operation_class):
            if kind==1: raise M.NativeApiError(1345005,'UF_CAMGEOM_ask_custom_points','unavailable')
            return ()
        f.trial.native.read_custom_points.side_effect=read
        snapshot=f.capture()
        self.assertEqual(snapshot[(100,'custom',0)],())
        self.assertEqual(snapshot[(100,'custom',1)],'unavailable')
        self.assertTrue(f.trial.unavailable)

    def test_other_api_errors_are_not_treated_as_unavailable(self):
        f=Fixture(); error=M.NativeApiError(999,'UF_CAMGEOM_ask_custom_points','bad data')
        f.trial.native.read_custom_points.side_effect=error
        with self.assertRaises(M.NativeApiError) as caught: f.capture()
        self.assertIs(caught.exception,error)

    def test_changed_count_tags_or_depths_are_rejected_before_replacement(self):
        for points in ((),((999,(60.,-25.,16.),ZERO),),((0,(60.,-25.,16.),(1.,0.,0.)),)):
            with self.subTest(points=points):
                f=Fixture(); baseline=f.capture(); f.records[(100,1)]=points
                with self.assertRaisesRegex(RuntimeError,'определения пользовательских точек'):
                    f.trial.transfer_custom_points(baseline)
                f.trial.native.replace_custom_points.assert_not_called()

    def test_write_that_does_not_move_point_is_detected_after_update(self):
        f=Fixture(); baseline=f.capture()
        f.trial.native.replace_custom_points.side_effect=None
        with self.assertRaisesRegex(RuntimeError,'не подтверждены отражение'):
            f.trial.transfer_custom_points(baseline)

    def test_reordered_numeric_entries_are_detected_after_write(self):
        f=Fixture(((0,(2.,3.,4.),ZERO),(0,(7.,8.,9.),ZERO))); baseline=f.capture()
        f.trial.native.replace_custom_points.side_effect=lambda tag,kind,points,operation_class:f.records.update({(tag,kind):points[::-1]})
        with self.assertRaisesRegex(RuntimeError,'не подтверждены отражение'):
            f.trial.transfer_custom_points(baseline)

    def test_late_move_after_update_is_detected_without_repair(self):
        f=Fixture(); baseline=f.capture(); f.trial.transfer_custom_points(baseline)
        f.records[(100,1)]=((0,(60.,-25.,16.),ZERO),)
        f.trial.geometry_snapshot()
        with self.assertRaisesRegex(RuntimeError,'не подтверждены отражение'): f.trial.verify_custom_points()
        self.assertEqual(f.trial.native.replace_custom_points.call_count,1)

    def workflow(self):
        import test_mirror_body_history as history
        body=history.Fixture(); t=body.workflow()
        f=Fixture(((700,(60.,-25.,16.),ZERO),(0,(12.,3.,4.),ZERO)))
        f.records[(100,0)]=((700,(60.,-25.,16.),ZERO),)
        f.part.Tag=t.part.Tag
        t.plane=f.trial.plane
        t.nodes=f.trial.nodes
        for name in ('Point','ScCollector','CAM','TaggedObjectManager'): setattr(t.nx,name,getattr(f.trial.nx,name))
        for name in ('items','boundaries','item_entity','channel','entity','read_custom_points','replace_custom_points','smart_parents'):
            setattr(t.native,name,getattr(f.trial.native,name))
        t.native.transform_entities.side_effect=lambda *args:(body.convert(),f.follow_geometry())
        del t.geometry_snapshot
        return t,f

    def test_full_body_cam_workflow_transfers_both_point_forms_and_keeps_guards(self):
        t,f=self.workflow()
        t.run_in_absolute_wcs()
        t.verify_mcs.assert_called_once(); t.verify_boundary_planes.assert_called_once()
        t.setup.DeleteToolPath.assert_called_once()
        self.assertEqual(t.check_cam_builders.call_count,2)
        self.assertEqual(len(t.body_conversions),1)
        self.assertEqual(t.native.transform_entities.call_args.args[0].count(700),1)
        self.assertIn('точки начала/предсверления: 3','\n'.join(t.lines))

    def test_full_workflow_rechecks_points_after_final_cam_editor_read(self):
        t,f=self.workflow()
        calls=[]
        def editor_read():
            calls.append(1)
            if len(calls)==2:
                first=f.records[(100,1)][0]
                f.records[(100,1)]=(first,(0,(12.,3.,4.),ZERO))
        t.check_cam_builders.side_effect=editor_read
        with self.assertRaisesRegex(RuntimeError,'не подтверждены отражение'):
            t.run_in_absolute_wcs()
        self.assertNotIn('ПРОЙДЕНО','\n'.join(t.lines))
        t.native.replace_custom_points.assert_called_once()


if __name__=='__main__': unittest.main()
