"""MCS preservation before/after history access; synthetic NX objects only."""
from types import SimpleNamespace as NS
import unittest

from test_mirror_historical_probe import M, HistoryWorld
from test_mirror_replacement_probe import ProbeWorld
from test_mirror_semantics import SmartWorld, xyz


def report_mcs_world(mirror=None):
    """The reported x=0 support and x=-79 mirror, including ordered selection."""
    world=SmartWorld()
    for face in world.faces:
        face.Origin=xyz(1.4210854715202004e-14,2.1316282072803006e-14,-12.)
    second=world.Face(89000,[0,0,-12],[1,0,0])
    planes=[]
    for index,face in enumerate(world.faces):
        plane=world.Plane(face.Origin,face.Normal,2)
        plane.SetGeometry([face,second] if index==0 else [face])
        plane.Evaluate()
        planes.append(plane)
    world.xform=world.Xform(*planes)
    world.csys=world.Csys(world.xform)
    trial=world.trial(mirror or M.MirrorPlane([-79,0,0],[-1,0,0]))
    return world,trial,planes


def mcs_probe(trial):
    probe=object.__new__(M.BodyReplacementProbe)
    probe.t=trial
    probe.body_tag=1000
    probe.frame_graphs={tag:trial.smart_graph(tag) for tag in trial.frames}
    return probe


def attach_mcs(target, trial):
    # Compose the real MCS validators with the history fixture. No validator is
    # replaced by a no-op; the two fixtures keep separate synthetic tag spaces.
    target.frames=trial.frames
    target.mcs_definitions=trial.mcs_definitions
    for name in ('smart_graph','require_csys_frame','verify_three_plane_selections'):
        setattr(target,name,getattr(trial,name))


class ProbeMcsTests(unittest.TestCase):
    def test_report_original_plane_is_not_required_to_move_minus_158_before_history(self):
        world,trial,planes=report_mcs_world()
        before=trial.csys_frame(world.csys.Tag)
        mcs_probe(trial).check_stock_and_frames(True)
        self.assertEqual(trial.csys_frame(world.csys.Tag),before)
        self.assertAlmostEqual(planes[0].Origin.X,0.)
        self.assertAlmostEqual(trial.plane.point(M.xyz(planes[0].Origin))[0],-158.)

    def test_original_state_also_passes_with_oblique_mirror(self):
        _,trial,_=report_mcs_world(M.MirrorPlane([4,-3,2],[1,2,3]))
        mcs_probe(trial).check_stock_and_frames(True)

    def test_direct_face_supports_use_the_same_original_and_reflected_phases(self):
        world=SmartWorld();trial=world.trial(M.MirrorPlane([-79,0,0],[-1,0,0]));probe=mcs_probe(trial)
        probe.check_stock_and_frames(True)
        with self.assertRaisesRegex(RuntimeError,'отражённой геометрии'):
            probe.check_stock_and_frames(False)
        world.reflect_faces(trial.plane)
        probe.check_stock_and_frames(False)

    def test_numerical_frame_drift_is_not_hidden_by_original_plane_validation(self):
        world,trial,_=report_mcs_world()
        fixed=world.Xform();csys=world.Csys(fixed)
        world.add_csys(trial,csys,2,'numerical root')
        probe=mcs_probe(trial);probe.check_stock_and_frames(True)
        fixed.fixed=([100,8,9],fixed.fixed[1])
        with self.assertRaisesRegex(RuntimeError,'начало и оси СК '+str(csys.Tag)):
            probe.check_stock_and_frames(True)

    def test_post_replacement_still_requires_reflected_support_planes(self):
        for mirror in (M.MirrorPlane([-79,0,0],[-1,0,0]),M.MirrorPlane([4,-3,2],[1,2,3])):
            with self.subTest(normal=mirror.normal):
                _,trial,planes=report_mcs_world(mirror);probe=mcs_probe(trial)
                with self.assertRaisesRegex(RuntimeError,'отражённой геометрии'):
                    probe.check_stock_and_frames(False)
                for plane in planes:
                    plane.Origin=xyz(*mirror.point(M.xyz(plane.Origin)))
                    plane.Normal=xyz(*mirror.vector(M.xyz(plane.Normal)))
                probe.check_stock_and_frames(False)

    def test_default_final_validator_rejects_unreflected_support(self):
        world,trial,_=report_mcs_world()
        with self.assertRaisesRegex(RuntimeError,'отражённой геометрии'):
            trial.verify_three_plane_selections(world.csys.Tag)

    def test_original_plane_translation_and_rotation_fail_even_with_unchanged_csys_frame(self):
        for kind in ('translation','rotation','reflected'):
            with self.subTest(kind=kind):
                world,trial,planes=report_mcs_world();probe=mcs_probe(trial)
                world.xform.fixed=trial.csys_frame(world.csys.Tag)
                if kind=='translation': planes[0].Origin=xyz(2,0,-12)
                elif kind=='rotation': planes[0].Normal=xyz(0,1,0)
                else:
                    planes[0].Origin=xyz(-158,0,-12)
                    planes[0].Normal=xyz(-1,0,0)
                with self.assertRaisesRegex(RuntimeError,'исходной геометрии'):
                    probe.check_stock_and_frames(True)

    def test_exact_ordered_face_choices_and_plane_method_are_still_checked(self):
        for kind in ('reorder','substitute','method'):
            with self.subTest(kind=kind):
                world,trial,planes=report_mcs_world();probe=mcs_probe(trial)
                if kind=='reorder': planes[0].sources.reverse()
                elif kind=='substitute': planes[0].sources[0]=world.Face(89001,[0,0,-12],[1,0,0])
                else: planes[0].Method=2
                with self.assertRaisesRegex(RuntimeError,'прямой выбор'):
                    probe.check_stock_and_frames(True)

    def test_changed_definition_and_unavailable_live_plane_remain_errors(self):
        for kind in ('graph','missing','foreign'):
            with self.subTest(kind=kind):
                world,trial,planes=report_mcs_world();probe=mcs_probe(trial)
                if kind=='graph': world.parents[world.csys.Tag]=None
                elif kind=='missing': del world.objects[planes[0].Tag]
                else: planes[0].OwningPart=NS(Tag=9999)
                with self.assertRaises(RuntimeError): probe.check_stock_and_frames(True)

    def test_error_stage_names_actual_csys_axis_and_plane_instead_of_previous_frame(self):
        world,trial,planes=report_mcs_world();probe=mcs_probe(trial)
        world.xform.fixed=trial.csys_frame(world.csys.Tag)
        planes[0].Origin=xyz(3,0,-12)
        trial.stage='previous unrelated frame'
        with self.assertRaises(RuntimeError): probe.check_stock_and_frames(True)
        self.assertIn(str(world.csys.Tag),trial.stage)
        self.assertIn('/ X /',trial.stage)
        self.assertIn(str(planes[0].Tag),trial.stage)
        self.assertIn('исходное',trial.stage)

    def test_candidate_preparation_uses_original_mcs_supports_before_and_after_reflecting_only_candidate(self):
        w=ProbeWorld();world,trial,_=report_mcs_world();attach_mcs(w.t,trial)
        w.probe.snapshot()
        w.probe.independent_copy()
        w.probe.match_topology()
        w.probe.reflect_candidate()
        self.assertEqual(trial.csys_frame(world.csys.Tag),trial.frames[world.csys.Tag])
        w.t.part.Features.CreateReplaceFeatureBuilder.assert_not_called()
        self.assertEqual([e for e in w.events if isinstance(e,tuple) and e[0]=='reflect'],[('reflect',(4000,))])

    def historical_world(self):
        h=HistoryWorld();world,trial,planes=report_mcs_world();attach_mcs(h.t,trial)
        # V1.27's real report: Datum CSYS are internal, sketches are external.
        for datum,sketch in ((69573,69574),(69576,69578),(69579,69582)):
            h.w.objects[datum].IsInternal=True
            h.w.objects[sketch].IsInternal=False
        return h,world,trial,planes

    def test_report_contexts_reach_external_sketches_and_restore_with_real_mcs_checks(self):
        h,world,trial,_=self.historical_world();before=trial.csys_frame(world.csys.Tag)
        with self.assertRaises(M.HistoricalProbeComplete): h.p.run()
        self.assertEqual([e[1] for e in h.events if e[0]=='start'],[69574,69578,69582])
        self.assertEqual(len([e for e in h.events if e[0]=='undo']),4)
        self.assertEqual(trial.csys_frame(world.csys.Tag),before)
        h.assert_probe_candidates_only(self)

    def test_damaged_mcs_before_history_still_prevents_manager_start(self):
        h,world,trial,planes=self.historical_world()
        world.xform.fixed=trial.csys_frame(world.csys.Tag)
        planes[0].Origin=xyz(1,0,-12)
        with self.assertRaisesRegex(RuntimeError,'исходной геометрии'): h.p.run()
        h.t.part.Features.StartEditWithRollbackManager.assert_not_called()
        h.assert_no_geometry_edits(self)

    def test_mcs_damage_after_stop_is_rejected_even_when_local_undo_repairs_it(self):
        h,world,trial,planes=self.historical_world()
        world.xform.fixed=trial.csys_frame(world.csys.Tag)
        original=planes[0].Origin
        h.after_stop=lambda:setattr(planes[0],'Origin',xyz(1,0,-12))
        h.after_undo=lambda:setattr(planes[0],'Origin',original)
        with self.assertRaisesRegex(RuntimeError,'исходной геометрии'): h.p.run()
        self.assertEqual([e[1] for e in h.events if e[0]=='start'],[69574])
        self.assertIn('Локальный Undo выполнен','\n'.join(h.t.lines))
        h.assert_no_geometry_edits(self)

    def test_mcs_selection_damage_after_undo_prevents_next_context_and_success_claim(self):
        h,_,_,planes=self.historical_world()
        h.after_undo=lambda:planes[0].sources.reverse()
        with self.assertRaisesRegex(RuntimeError,'прямой выбор'): h.p.run()
        self.assertEqual([e[1] for e in h.events if e[0]=='start'],[69574])
        self.assertNotIn('Локальный Undo выполнен','\n'.join(h.t.lines))
        h.t.session.DeleteUndoMark.assert_not_called()
        h.assert_no_geometry_edits(self)


if __name__=='__main__':
    unittest.main()
