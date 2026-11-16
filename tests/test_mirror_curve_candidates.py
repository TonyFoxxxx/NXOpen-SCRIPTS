"""Candidate creation and rollback contracts, not a simulation of NX geometry."""
import copy
import math
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock

from test_mirror_historical_probe import M, Curve, Edge, HistoryWorld
from test_mirror_history_curves import line, arc, rational, with_samples


def report_arcs():
    # Numeric definitions measured in the user's V1.28 NX report. These are
    # historical support edges, not the two central CAM curves in SKETCH_000.
    result={}
    for tag,limits,center,radius in (
        (64791,(.742638574947394,1.68999553347677),
         (-2.149843414312148,-17.95021826017189,-3.054000000000001),.5),
        (73591,(3.022393446907915,3.969750405437263),
         (-17.95021825763835,-2.149843416845711,-3.054000000000001),.5000000000000004)):
        result[tag]=with_samples({'kind':'Arc','limits':limits,'periodic':False,
            'data':{'center':center,'radius':radius,'x_axis':(1.,0.,0.),'y_axis':(0.,-1.,0.),
                    'limits':limits,'periodic':False}})
    return result


class CandidateDefinitionTests(unittest.TestCase):
    def test_normalize_full_spline_domain_preserves_trim_weights_and_shape(self):
        original=rational();original['limits']=(2.6,4.4)
        original['data']['knots']=(2.,2.,2.,5.,5.,5.);with_samples(original)
        before=copy.deepcopy(original);planned=M.history_curve_candidate_definition(original)
        self.assertEqual(original,before)
        self.assertEqual(planned['data']['knots'],(0.,0.,0.,1.,1.,1.))
        self.assertEqual(planned['data']['poles'],original['data']['poles'])
        for a,b in zip(planned['limits'],(.2,.8)): self.assertAlmostEqual(a,b)
        for u in M.history_curve_parameters(planned):
            self.assertTrue(M.close_values(M.history_curve_point(planned,u),M.history_curve_point(original,2+3*u),1e-12))

    def test_line_native_parameter_domain_may_differ_without_reversing_direction(self):
        planned=M.history_curve_candidate_definition(line());actual=line()
        self.assertEqual(planned['limits'],(0.,1.))
        self.assertLess(M.check_created_history_curve(planned,actual),1e-12)
        actual['data']['start'],actual['data']['end']=actual['data']['end'],actual['data']['start'];with_samples(actual)
        with self.assertRaisesRegex(RuntimeError,'start'):M.check_created_history_curve(planned,actual)

    def test_oblique_ellipse_reflects_both_native_directions_and_keeps_radii(self):
        original=arc();original['kind']='Ellipse';del original['data']['radius']
        original['data'].update(major=6.,minor=2.);with_samples(original)
        reflected=M.reflected_history_curve(original,M.MirrorPlane([3,-7,5],[1,2,-3]))
        planned=M.history_curve_candidate_definition(reflected)
        self.assertLess(M.check_created_history_curve(planned,copy.deepcopy(planned)),1e-12)
        self.assertEqual((planned['data']['major'],planned['data']['minor']),(6.,2.))
        for u,p in planned['samples']:
            self.assertTrue(M.close_values(p,M.MirrorPlane([3,-7,5],[1,2,-3]).point(M.history_curve_point(original,u)),1e-12))

    def test_arc_angle_origin_can_shift_one_turn_but_not_change_trim(self):
        expected=arc();actual=copy.deepcopy(expected)
        actual['limits']=actual['data']['limits']=tuple(u+2*math.pi for u in expected['limits']);with_samples(actual)
        self.assertLess(M.check_created_history_curve(expected,actual),1e-12)
        actual['limits']=actual['data']['limits']=(actual['limits'][0],actual['limits'][1]-.01);with_samples(actual)
        with self.assertRaisesRegex(RuntimeError,'обрезка'):M.check_created_history_curve(expected,actual)

    def test_changed_kind_or_periodicity_is_rejected(self):
        expected=arc();actual=copy.deepcopy(expected);actual['periodic']=actual['data']['periodic']=True
        with self.assertRaisesRegex(RuntimeError,'периодичность'):M.check_created_history_curve(expected,actual)
        actual=copy.deepcopy(expected);actual['kind']='Ellipse';del actual['data']['radius'];actual['data'].update(major=2.,minor=2.)
        with self.assertRaisesRegex(RuntimeError,'тип/'):M.check_created_history_curve(expected,actual)

    def test_radius_axes_and_center_change_is_not_hidden_by_sample_points(self):
        expected=arc()
        for key,value in (('radius',2.001),('x_axis',(-1.,0.,0.)),('center',(3.001,-2.,4.))):
            actual=copy.deepcopy(expected);actual['data'][key]=value
            with self.subTest(key=key),self.assertRaises(RuntimeError):M.check_created_history_curve(expected,actual)

    def test_spline_definition_drift_is_rejected_even_with_old_sample_points(self):
        expected=rational()
        for change in ('pole','weight','knot','trim'):
            actual=copy.deepcopy(expected);d=actual['data']
            if change in ('pole','weight'):
                p=list(d['poles']);row=list(p[1]);row[0 if change=='pole' else 3]+=.01;p[1]=tuple(row);d['poles']=tuple(p)
            elif change=='knot':d['knots']=(0.,0.,0.,1.01,1.01,1.01)
            else:actual['limits']=(.21,.8)
            with self.subTest(change=change),self.assertRaisesRegex(RuntimeError,'полюса/веса'):M.check_created_history_curve(expected,actual)

    def test_missing_or_corrupted_native_samples_fail(self):
        expected=arc();actual=copy.deepcopy(expected);actual['samples']=actual['samples'][1:]
        with self.assertRaisesRegex(RuntimeError,'полный контрольный'):M.check_created_history_curve(expected,actual)
        actual=copy.deepcopy(expected);samples=list(actual['samples']);u,p=samples[4];samples[4]=(u,(p[0]+.01,p[1],p[2]));actual['samples']=tuple(samples)
        with self.assertRaisesRegex(RuntimeError,'не совпала'):M.check_created_history_curve(expected,actual)


class CandidateProtocolTests(unittest.TestCase):
    def install_definitions(self, h, definitions):
        for tag,c in definitions.items():
            h.w.objects[tag].GetVertices.return_value=tuple(NS(X=p[0],Y=p[1],Z=p[2])
                for p in (M.history_curve_point(c,u) for u in c['limits']))
        def read(tag):
            if tag in definitions:
                h.owner(tag,'Edge');return copy.deepcopy(definitions[tag])
            return h.curve(tag)
        h.t.native.history_curve.side_effect=read

    def test_v128_measured_arcs_materialize_once_per_edge_after_all_contexts_close(self):
        h=HistoryWorld();definitions=report_arcs();created=[]
        self.install_definitions(h,definitions)
        h.after_create=lambda obj:created.append(copy.deepcopy(h.candidates[obj.Tag]))
        before=h.snapshot()
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertEqual(len(created),2)
        for original,new in zip(definitions.values(),created):
            self.assertEqual(new['kind'],'Arc')
            self.assertEqual(new['data']['x_axis'],(-1.,0.,0.))
            self.assertEqual(new['data']['y_axis'],(0.,-1.,0.))
            self.assertEqual(new['limits'],original['limits'])
            self.assertEqual(new['data']['radius'],original['data']['radius'])
            self.assertAlmostEqual(new['data']['center'][0],-158-original['data']['center'][0])
            for u,p in new['samples']:
                q=M.history_curve_point(original,u)
                self.assertTrue(M.close_values(p,(-158-q[0],q[1],q[2]),1e-12))
        self.assertEqual(h.candidate_events,[(69595,'Arc',22),(69595,'Arc',22)])
        self.assertEqual(h.snapshot(),before)
        self.assertEqual(h.events[-3:],[('mark',103,1),('undo',103),('delete-mark',103)])
        h.assert_no_geometry_edits(self)

    def test_ellipse_constructor_receives_radii_and_reflected_axes_in_order(self):
        h=HistoryWorld();c=arc();c['kind']='Ellipse';del c['data']['radius']
        c['data'].update(major=5.,minor=2.);with_samples(c)
        self.install_definitions(h,{64791:c,73591:c});created=[]
        h.after_create=lambda obj:created.append(copy.deepcopy(h.candidates[obj.Tag]))
        before=h.snapshot()
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        for actual in created:
            self.assertEqual((actual['data']['major'],actual['data']['minor']),(5.,2.))
            self.assertEqual(actual['data']['x_axis'],(-1.,0.,0.))
            self.assertEqual(actual['data']['y_axis'],(0.,1.,0.))
        self.assertEqual(h.snapshot(),before)

    def test_spline_phase_resolves_only_new_tag_and_verifies_native_readback(self):
        h=HistoryWorld();c=rational();self.install_definitions(h,{64791:c,73591:c})
        h.t.native.prepare_history_spline_creation_api=Mock()
        h.t.native.create_history_spline=Mock(side_effect=lambda definition:h.create_curve(copy.deepcopy(definition)).Tag)
        before=h.snapshot()
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        h.t.native.prepare_history_spline_creation_api.assert_called_once_with()
        self.assertEqual(h.t.native.create_history_spline.call_count,2)
        self.assertEqual(h.candidate_events,[(69595,'Spline',22),(69595,'Spline',22)])
        self.assertEqual(h.snapshot(),before)

    def test_history_read_failure_does_not_start_candidate_phase(self):
        h=HistoryWorld();h.t.native.history_curve.side_effect=RuntimeError('bad history curve')
        with self.assertRaises(M.UnresolvedProbeReferences):h.p.run()
        self.assertEqual(h.candidate_events,[])
        self.assertEqual(len([e for e in h.events if e[0]=='mark']),3)

    def test_missing_creation_api_fails_before_candidate_mark(self):
        h=HistoryWorld();h.t.part.Curves.CreateLine=None
        with self.assertRaisesRegex(RuntimeError,'API создания'):h.p.run()
        self.assertEqual(len([e for e in h.events if e[0]=='mark']),3)
        self.assertEqual(h.candidate_events,[])

    def test_partial_creation_native_error_restores_objects_and_keeps_code(self):
        h=HistoryWorld();before=h.snapshot()
        def fail(obj):raise M.NativeApiError(711,'CreateLine','failure after allocation')
        h.after_create=fail
        with self.assertRaises(M.NativeApiError) as caught:h.p.run()
        self.assertEqual(caught.exception.code,711)
        self.assertEqual(h.snapshot(),before);self.assertEqual(h.marks,{})
        self.assertIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))

    def test_null_or_reused_or_wrong_type_output_never_becomes_verified_candidate(self):
        for kind in ('null','existing','edge'):
            h=HistoryWorld();before=h.snapshot();create=h.t.part.Curves.CreateLine
            def malformed(*args):
                obj=create(*args)
                return None if kind=='null' else h.w.objects[601] if kind=='existing' else Edge(obj.Tag,h.w.body)
            h.t.part.Curves.CreateLine=malformed
            with self.subTest(kind=kind),self.assertRaises(RuntimeError):h.p.run()
            self.assertEqual(h.snapshot(),before)
            self.assertNotIn('Кривая-кандидат проверена','\n'.join(h.t.lines))

    def test_foreign_owner_is_rejected_and_removed_by_own_undo(self):
        h=HistoryWorld();before=h.snapshot();h.after_create=lambda obj:setattr(obj,'OwningPart',NS(Tag=999))
        with self.assertRaises(RuntimeError):h.p.run()
        self.assertEqual(h.snapshot(),before)

    def test_unexpected_extra_curve_or_removed_original_is_rejected(self):
        for extra in (True,False):
            h=HistoryWorld();before=h.snapshot()
            def damage(obj):
                if extra:h.t.part.Curves.append(h.w.register(Curve(800000)))
                else:h.t.part.Curves.remove(h.w.objects[601])
            h.after_create=damage
            with self.subTest(extra=extra),self.assertRaisesRegex(RuntimeError,'вне явного списка'):h.p.run()
            self.assertEqual(h.snapshot(),before)

    def test_existing_curve_box_change_is_rejected(self):
        h=HistoryWorld();original=h.t.native.history_bounding_box.return_value
        h.t.native.history_bounding_box.side_effect=lambda tag:(0,0,0,9,9,9) if h.candidates and tag==601 else original
        before=h.snapshot()
        with self.assertRaisesRegex(RuntimeError,'исходная кривая 601'):h.p.run()
        self.assertEqual(h.snapshot(),before)

    def test_new_curve_readback_failure_is_not_swallowed(self):
        h=HistoryWorld();before=h.snapshot()
        def read(tag):
            if tag in h.candidates:raise M.NativeApiError(712,'UF_EVAL','new read failed')
            return h.curve(tag)
        h.t.native.history_curve.side_effect=read
        with self.assertRaises(M.NativeApiError) as caught:h.p.run()
        self.assertEqual(caught.exception.code,712);self.assertEqual(h.snapshot(),before)

    def test_cam_change_during_creation_is_detected_and_undone(self):
        h=HistoryWorld();before=h.snapshot()
        h.after_create=lambda obj:h.w.cam.clear()
        with self.assertRaises(RuntimeError):h.p.run()
        self.assertEqual(h.snapshot(),before)
        h.assert_no_geometry_edits(self)

    def test_failed_candidate_undo_retains_its_mark_and_reports_secondary_code(self):
        h=HistoryWorld()
        def fail(obj):
            h.fail['undo']=M.NativeApiError(714,'Undo','undo failed')
            raise M.NativeApiError(713,'CreateLine','create failed')
        h.after_create=fail
        with self.assertRaises(M.NativeApiError) as caught:h.p.run()
        self.assertEqual(caught.exception.code,713)
        self.assertEqual(set(h.marks),{103})
        report='\n'.join(h.t.lines)
        self.assertIn('714',report);self.assertIn('Traceback',report)
        self.assertNotIn('Локальный Undo новых кривых выполнен',report)

    def test_lost_active_copy_does_not_undo_another_part(self):
        h=HistoryWorld()
        h.after_create=lambda obj:setattr(h.t.session.Parts,'Work',NS(Tag=999))
        with self.assertRaises(RuntimeError):h.p.run()
        self.assertEqual([e[1] for e in h.events if e[0]=='undo'],[100,101,102])
        self.assertEqual(set(h.marks),{103})

    def test_surviving_curve_after_undo_forbids_success_and_mark_removal(self):
        h=HistoryWorld()
        def damage():
            if h.events[-1]==('undo',103):h.t.part.Curves.append(h.w.register(Curve(800000)))
        h.after_undo=damage
        with self.assertRaisesRegex(RuntimeError,'вне явного списка'):h.p.run()
        self.assertEqual(set(h.marks),{103})
        self.assertNotIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))

    def test_candidate_spline_binding_is_not_required_for_line_only_project(self):
        h=HistoryWorld()
        h.t.native.prepare_history_spline_creation_api=Mock(side_effect=RuntimeError('unavailable'))
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        h.t.native.prepare_history_spline_creation_api.assert_not_called()
