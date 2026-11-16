"""Numerical curve definitions and history protocol; no real NX kernel."""
import copy
import math
import unittest
from unittest.mock import Mock

from test_mirror_historical_probe import M, HistoryWorld


def with_samples(curve):
    curve['samples']=tuple((u,M.history_curve_point(curve,u)) for u in M.history_curve_parameters(curve))
    return curve


def line():
    return with_samples({'kind':'Line','limits':(3.,7.),'periodic':False,
        'data':{'start':(2.,-4.,6.),'end':(-3.,5.,8.)}})


def arc():
    return with_samples({'kind':'Arc','limits':(.1,1.7),'periodic':False,
        'data':{'center':(3.,-2.,4.),'radius':2.,'x_axis':(1.,0.,0.),'y_axis':(0.,1.,0.),
                'limits':(.1,1.7),'periodic':False}})


def rational():
    w=math.sqrt(.5)
    return with_samples({'kind':'Spline','limits':(.2,.8),'periodic':False,
        'data':{'poles':((1.,0.,0.,1.),(w,w,0.,w),(0.,1.,0.,1.)),
                'knots':(0.,0.,0.,1.,1.,1.),'order':3,'rational':True}})


class HistoricalCurveMathTests(unittest.TestCase):
    def test_x_minus79_reflects_line_endpoints_without_renormalizing_parameters(self):
        c=line();r=M.reflected_history_curve(c,M.MirrorPlane([-79,0,0],[-1,0,0]))
        self.assertEqual(r['data']['start'],(-160.,-4.,6.))
        self.assertEqual(r['data']['end'],(-155.,5.,8.))
        self.assertEqual(r['limits'],(3.,7.))
        self.assertEqual(M.history_curve_point(r,5),(-157.5,.5,7.))

    def test_curve_axes_follow_both_reflections_instead_of_mcs_x_rule(self):
        c=arc();r=M.reflected_history_curve(c,M.MirrorPlane([0,0,0],[1,0,0]))
        self.assertEqual(r['data']['x_axis'],(-1.,0.,0.))
        self.assertEqual(r['data']['y_axis'],(0.,1.,0.))
        self.assertEqual(M.cross(r['data']['x_axis'],r['data']['y_axis']),[0.,0.,-1.])
        self.assertEqual(r['data']['limits'],c['data']['limits'])

    def test_oblique_reflection_matches_points_and_second_reflection_restores_curve(self):
        plane=M.MirrorPlane([4,-3,2],[1,2,3])
        for c in (line(),arc(),rational()):
            original=copy.deepcopy(c);r=M.reflected_history_curve(c,plane);back=M.reflected_history_curve(r,plane)
            for i in range(33):
                u=c['limits'][0]+(c['limits'][1]-c['limits'][0])*i/32
                point=M.history_curve_point(c,u)
                self.assertTrue(M.close_values(M.history_curve_point(r,u),plane.point(point),1e-10))
                self.assertTrue(M.close_values(M.history_curve_point(back,u),point,1e-10))
            self.assertEqual(c,original)

    def test_weighted_poles_receive_weighted_translation(self):
        c=rational();r=M.reflected_history_curve(c,M.MirrorPlane([7,0,0],[1,0,0]))
        w=math.sqrt(.5)
        self.assertAlmostEqual(r['data']['poles'][1][0],13*w)
        self.assertEqual(r['data']['poles'][1][3],w)
        self.assertEqual(r['data']['knots'],c['data']['knots'])
        p=M.history_curve_point(r,.5)
        self.assertAlmostEqual(p[0],14-w);self.assertAlmostEqual(p[1],w)

    def test_negative_or_zero_weights_do_not_become_empty_or_unit_weights(self):
        for weight in (0.,-1.,float('nan')):
            c=rational();p=list(c['data']['poles']);p[1]=p[1][:3]+(weight,);c['data']['poles']=tuple(p)
            with self.subTest(weight=weight),self.assertRaises(RuntimeError):M.validate_history_curve(c)

    def test_interior_repeated_knot_and_trim_are_preserved(self):
        # Quadratic pieces meet at (2,1,0) with a tangent discontinuity.
        c={'kind':'Spline','limits':(.1,.9),'periodic':False,
           'data':{'poles':((0,0,0,1),(1,0,0,1),(2,1,0,1),(2,2,0,1),(3,3,0,1)),
                   'knots':(0,0,0,.5,.5,1,1,1),'order':3,'rational':False}}
        M.validate_history_curve(c);with_samples(c)
        self.assertEqual(M.history_curve_point(c,.5),(2.,1.,0.))
        self.assertIn(.2,M.history_curve_parameters(c))
        self.assertIn(.8,M.history_curve_parameters(c))
        r=M.reflected_history_curve(c,M.MirrorPlane([0,0,0],[1,0,0]))
        self.assertEqual(r['data']['knots'],c['data']['knots'])
        self.assertEqual(M.history_curve_point(r,.5),(-2.,1.,0.))

    def test_invalid_knot_multiplicity_and_trim_outside_domain_stop(self):
        for knots,limits in (((0,0,0,0,1,1),(.2,.8)),((0,0,0,1,1,1),(-.1,.8))):
            c=rational();c['data']['knots']=knots;c['limits']=limits
            with self.assertRaises(RuntimeError):M.validate_history_curve(c)

    def test_endpoint_order_can_be_reversed_without_changing_curve_parameter_direction(self):
        c=arc();ends=tuple(M.history_curve_point(c,u) for u in reversed(c['limits']))
        r,_=M.check_history_curve(c,ends,M.MirrorPlane([0,0,0],[1,0,0]))
        self.assertEqual(r['limits'],c['limits'])

    def test_matching_endpoints_cannot_hide_wrong_intermediate_shape(self):
        c=arc();samples=list(c['samples']);u,p=samples[4];samples[4]=(u,(p[0]+.1,p[1],p[2]));c['samples']=tuple(samples)
        ends=tuple(M.history_curve_point(c,u) for u in c['limits'])
        with self.assertRaisesRegex(RuntimeError,'не совпало с UF_EVAL'):
            M.check_history_curve(c,ends,M.MirrorPlane([0,0,0],[1,0,0]))

    def test_missing_sample_is_not_a_successful_shorter_read(self):
        c=line();c['samples']=c['samples'][1:]
        with self.assertRaisesRegex(RuntimeError,'полный набор'):
            M.check_history_curve(c,(c['data']['start'],c['data']['end']),M.MirrorPlane([0,0,0],[1,0,0]))

    def test_wrong_vertices_block_complete_curve_definition(self):
        c=line()
        with self.assertRaisesRegex(RuntimeError,'Edge.GetVertices'):
            M.check_history_curve(c,((0,0,0),(1,1,1)),M.MirrorPlane([0,0,0],[1,0,0]))

    def test_arc_unreadable_axes_or_outside_trim_are_not_normalized_away(self):
        for key,value in (('x_axis',(2,0,0)),('limits',(.4,1.5)),('periodic',1)):
            c=arc();c['data'][key]=value
            with self.subTest(key=key),self.assertRaises(RuntimeError):M.validate_history_curve(c)

    def test_missing_definition_field_is_not_filled_from_samples(self):
        c=rational();del c['data']['knots']
        with self.assertRaises(KeyError):M.validate_history_curve(c)


class HistoricalCurveProtocolTests(unittest.TestCase):
    def external_sketches(self):
        h=HistoryWorld()
        for datum,sketch in ((69573,69574),(69576,69578),(69579,69582)):
            h.w.objects[datum].IsInternal=True;h.w.objects[sketch].IsInternal=False
        return h

    def test_v12702_external_sketch_contexts_read_owned_data_before_stop(self):
        # V1.27.02 establishes these roles and three successful reads; the new
        # curve definitions here remain synthetic, not measured NX geometry.
        h=self.external_sketches();before=h.snapshot();read=[]
        def curve(tag):
            read.append((h.current,tag,h.events[-1][0]));return h.curve(tag)
        h.t.native.history_curve.side_effect=curve
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertEqual(read[:3],[(69574,64791,'start'),(69578,73591,'start'),(69582,73591,'start')])
        self.assertEqual(read[3:5],[(69595,900001,'mark'),(69595,900002,'mark')])
        self.assertEqual(read[5:],[(41,900001,'mark'),(41,900002,'mark')]*3)
        self.assertEqual(h.snapshot(),before)
        self.assertEqual(len([e for e in h.events if e[0]=='undo']),4)
        self.assertIn('контекстов/рёбер 3; уникальных рёбер 2','\n'.join(h.t.lines))
        h.assert_probe_candidates_only(self)

    def test_full_definition_exports_missing_stop_before_first_visible_mark(self):
        h=HistoryWorld();h.t.native.prepare_history_curve_api.side_effect=RuntimeError('missing UF_EVAL export')
        with self.assertRaisesRegex(RuntimeError,'missing UF_EVAL export'):h.p.run()
        self.assertEqual(h.events,[])
        h.assert_no_geometry_edits(self)

    def test_curve_read_errors_are_not_counted_as_full_success(self):
        h=HistoryWorld();h.t.native.history_curve.side_effect=M.NativeApiError(6123,'UF_EVAL','read failure')
        with self.assertRaises(M.UnresolvedProbeReferences) as caught:h.p.run()
        self.assertEqual(caught.exception.code,6123)
        self.assertIn('успешно 0; ошибок 3','\n'.join(h.t.lines))
        self.assertEqual(len([e for e in h.events if e[0]=='undo']),3)
        h.assert_no_geometry_edits(self)

    def test_evaluator_cleanup_failure_restores_context_and_forbids_next_context(self):
        h=HistoryWorld();before=h.snapshot()
        h.t.native.history_curve.side_effect=M.CurveEvaluatorCleanupError(M.NativeApiError(625,'UF_EVAL_free','failed'))
        with self.assertRaises(M.CurveEvaluatorCleanupError):h.p.run()
        self.assertEqual([e[0] for e in h.events],['mark','start','update','stop','destroy','undo','delete-mark'])
        self.assertTrue(h.events[2][2])
        self.assertEqual(h.snapshot(),before)
        h.assert_no_geometry_edits(self)

    def test_same_tag_different_definition_is_not_silently_merged(self):
        h=HistoryWorld()
        def curve(tag):
            c=h.curve(tag)
            if h.current==69579:
                c['limits']=(4.,8.);with_samples(c)
            return c
        h.t.native.history_curve.side_effect=curve
        with self.assertRaisesRegex(M.UnresolvedProbeReferences,'разные определения'):h.p.run()
        self.assertIn('успешно 2; ошибок 1','\n'.join(h.t.lines))
        self.assertEqual(len([e for e in h.events if e[0]=='undo']),3)

    def test_numerical_rejection_retains_context_label_and_runs_all_restore_checks(self):
        h=self.external_sketches();before=h.snapshot()
        def curve(tag):
            c=h.curve(tag);s=list(c['samples']);u,p=s[3];s[3]=(u,(p[0]+.1,p[1],p[2]));c['samples']=tuple(s);return c
        h.t.native.history_curve.side_effect=curve
        with self.assertRaisesRegex(M.UnresolvedProbeReferences,'не совпало с UF_EVAL'):h.p.run()
        report='\n'.join(h.t.lines)
        self.assertIn('полное определение Edge 64791',report)
        self.assertIn('Traceback',report)
        self.assertEqual(h.snapshot(),before)
        h.assert_no_geometry_edits(self)
