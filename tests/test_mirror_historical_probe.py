"""Stateful history-access/rollback protocol, not an NX kernel simulation.

The dependency shape comes from the V1.26.02 report. Historical visibility is
deliberately supplied by this fixture; only a real NX run can establish it.
"""
import copy
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock

from test_mirror_replacement_probe import M, Body, Curve, Edge, Face, ownerless_support_report_world


class VisibleUndoMarkRequired(RuntimeError):
    """Contract observed in the user's DC 2606 V1.27.01 report."""
    ErrorCode=690037


class HistoryWorld:
    def __init__(self):
        self.w = ownerless_support_report_world()
        self.t, self.p = self.w.t, self.w.probe
        self.events, self.marks, self.fail = [], {}, {}
        self.mark_visibility = {}
        self.after_start = self.after_stop = self.after_undo = lambda: None
        self.current = 69595
        self.next_mark = 100
        self.next_curve = 900000
        self.candidates, self.candidate_events = {}, []
        self.after_create = lambda obj: None
        self.t.nx.Point3d = self.t.nx.Vector3d = lambda x,y,z:NS(X=x,Y=y,Z=z)
        self.t.part.Curves = CurveCollection(self)
        for index, feature in enumerate(self.t.part.Features):
            feature.Timestamp, feature.Suppressed, feature.IsInternal = index, False, False
        for datum, sketch, stamp in ((69573,69574,7),(69576,69578,9),(69579,69582,10)):
            self.w.objects[datum].Timestamp = self.w.objects[sketch].Timestamp = stamp
            self.w.objects[sketch].IsInternal = True
        self.w.objects[69595].Timestamp = 100
        self.t.part.Expressions = list({e.Tag:e for f in self.t.part.Features for e in f.expressions}.values())
        self.t.nx.Body = Body
        self.t.nx.Session = NS(MarkVisibility=NS(Invisible=0,Visible=1))
        self.t.nx.Features.EditWithRollbackManager = HistoryManager
        self.t.part.Features.StartEditWithRollbackManager = Mock(side_effect=self.start)
        self.t.session = NS(Parts=NS(Work=self.t.part, Display=self.t.part),
            SetUndoMark=Mock(side_effect=self.mark), UndoToMark=Mock(side_effect=self.undo),
            DeleteUndoMark=Mock(side_effect=self.delete_mark))
        self.t.native.prepare_history_probe_api = Mock()
        self.t.native.prepare_history_curve_api = Mock()
        self.t.native.history_curve = Mock(side_effect=self.curve)
        self.t.native.current_feature = Mock(side_effect=lambda part:self.current)
        self.t.native.reference_body = Mock(side_effect=self.owner)
        self.t.native.history_bounding_box = Mock(return_value=(-4.,2.,3.,5.,6.,7.))
        for tag in (64791,73591):
            self.w.objects[tag].GetVertices = Mock(return_value=(NS(X=-4.,Y=2.,Z=3.),NS(X=5.,Y=6.,Z=7.)))

    def curve(self, tag):
        if tag in self.candidates:
            return copy.deepcopy(self.candidates[tag])
        # Synthetic straight edges only; the V1.27.02 report does not establish
        # the actual curve type or parameters of the user's two edges.
        self.owner(tag,'Edge')
        result={'kind':'Line','limits':(2.,5.),'periodic':False,
                'data':{'start':(-4.,2.,3.),'end':(5.,6.,7.)}}
        result['samples']=tuple((u,M.history_curve_point(result,u)) for u in M.history_curve_parameters(result))
        return result

    def create_curve(self, definition):
        # A protocol fixture: native curve creation is established only by NX.
        assert self.current==69595
        assert sum(e[0]=='start' for e in self.events)==sum(e[0]=='destroy' for e in self.events)
        self.candidate_events.append((self.current,definition['kind'],len(self.events)))
        definition['samples']=tuple((u,M.history_curve_point(definition,u))
                                    for u in M.history_curve_parameters(definition))
        self.next_curve+=1
        obj=self.w.register(Curve(self.next_curve))
        self.t.part.Curves.append(obj)
        self.candidates[obj.Tag]=definition
        self.after_create(obj)
        return obj

    def snapshot(self):
        return dict(current=self.current, objects=dict(self.w.objects), edges=self.w.body.edges[:], faces=self.w.body.faces[:],
            features=self.t.part.Features[:], expressions=self.t.part.Expressions[:],
            feature_data={f.Tag:(f.Timestamp,f.Suppressed,f.IsInternal,f.warnings[:],f.parents[:])
                          for f in self.t.part.Features},
            expression_data={e.Tag:(e.Name,e.Type,e.RightHandSide,e.Units.Name) for e in self.t.part.Expressions},
            sketches={f.Tag:(f.Sketch.geometry[:],f.Sketch.expressions[:]) for f in self.t.part.Features if hasattr(f,'Sketch')},
            cam=copy.deepcopy(self.w.cam), references=copy.deepcopy(self.w.references), boxes=dict(self.w.boxes),
            curves=self.t.part.Curves[:],candidates=copy.deepcopy(self.candidates))

    def restore(self, saved):
        self.current = saved['current']
        self.w.objects = dict(saved['objects'])
        self.w.body.edges, self.w.body.faces = saved['edges'][:], saved['faces'][:]
        self.t.part.Features[:] = saved['features']
        self.t.part.Expressions[:] = saved['expressions']
        for f in self.t.part.Features:
            stamp,suppressed,internal,warnings,parents = saved['feature_data'][f.Tag]
            f.Timestamp,f.Suppressed,f.IsInternal = stamp,suppressed,internal
            f.warnings,f.parents = warnings[:],parents[:]
        for e in self.t.part.Expressions:
            e.Name,e.Type,e.RightHandSide,e.Units.Name = saved['expression_data'][e.Tag]
        for tag,(geometry,expressions) in saved['sketches'].items():
            sketch = self.w.objects[tag].Sketch
            sketch.geometry,sketch.expressions = geometry[:],expressions[:]
        self.w.cam = copy.deepcopy(saved['cam'])
        self.w.references = copy.deepcopy(saved['references'])
        self.w.boxes = dict(saved['boxes'])
        self.t.part.Curves[:] = saved['curves']
        self.candidates = copy.deepcopy(saved['candidates'])

    def failure(self, name):
        if name in self.fail:
            raise self.fail[name]

    def mark(self, visibility, name):
        self.failure('mark')
        mark = self.next_mark
        self.next_mark += 1
        self.events.append(('mark',mark,visibility))
        self.marks[mark] = self.snapshot()
        self.mark_visibility[mark] = visibility
        return mark

    def start(self, feature, mark):
        self.events.append(('start',feature.Tag,mark))
        if mark not in self.marks or self.mark_visibility[mark] != self.t.nx.Session.MarkVisibility.Visible:
            raise VisibleUndoMarkRequired('Cannot edit with rollback a feature. A visible undo mark needs to be provided to the edit with rollback manager.')
        self.current = feature.Tag
        edge = 64791 if feature.Tag in (69573,69574) else 73591
        self.w.body.edges.append(self.w.objects[edge])
        for other in self.t.part.Features:
            other.Suppressed = other.Timestamp > feature.Timestamp
        self.after_start()
        self.failure('start')  # May fail after entering history without returning a handle.
        return HistoryManager(self,mark)

    def owner(self, tag, kind):
        entities = self.w.body.faces if kind == 'Face' else self.w.body.edges
        if tag not in {obj.Tag for obj in entities}:
            raise RuntimeError('UF: no readable owner for '+str(tag))
        return self.w.body.Tag

    def undo(self, mark, name):
        self.events.append(('undo',mark))
        self.failure('undo')
        self.restore(self.marks[mark])
        self.after_undo()

    def delete_mark(self, mark, name):
        self.events.append(('delete-mark',mark))
        self.failure('delete-mark')
        del self.marks[mark]
        del self.mark_visibility[mark]

    def assert_no_geometry_edits(self, test):
        test.assertEqual(self.w.events,[])
        self.t.part.Features.CreateExtractFaceBuilder.assert_not_called()
        self.t.part.Features.CreateReplaceFeatureBuilder.assert_not_called()
        self.t.native.delete_candidate_parameters.assert_not_called()
        self.t.native.transform_entities.assert_not_called()
        self.t.transfer_mcs_and_stock.assert_not_called()
        self.t.setup.DeleteToolPath.assert_not_called()


class CurveCollection(list):
    def __init__(self, history):
        self.h=history
        super().__init__(obj for obj in history.w.objects.values() if type(obj) is Curve)

    def CreateLine(self, start, end):
        return self.h.create_curve({'kind':'Line','limits':(0.,1.),'periodic':False,
            'data':{'start':tuple(M.xyz(start)),'end':tuple(M.xyz(end))}})

    def conic(self, kind, center, x, y, radii, lo, hi):
        periodic=abs(hi-lo-2*M.math.pi)<1e-10
        return self.h.create_curve({'kind':kind,'limits':(lo,hi),'periodic':periodic,
            'data':dict(center=tuple(M.xyz(center)),x_axis=tuple(M.xyz(x)),y_axis=tuple(M.xyz(y)),
                        limits=(lo,hi),periodic=periodic,**radii)})

    def CreateArc(self, center, x, y, radius, lo, hi):
        return self.conic('Arc',center,x,y,{'radius':radius},lo,hi)

    def CreateEllipse(self, center, x, y, major, minor, lo, hi):
        return self.conic('Ellipse',center,x,y,{'major':major,'minor':minor},lo,hi)


class HistoryManager:
    def __init__(self, world, mark):
        self.h,self.mark = world,mark

    def UpdateFeature(self, error):
        self.h.events.append(('update',self.mark,error))
        self.h.failure('update')

    def Stop(self):
        self.h.events.append(('stop',self.mark))
        self.h.failure('stop')
        self.h.restore(self.h.marks[self.mark])
        self.h.after_stop()

    def Destroy(self):
        self.h.events.append(('destroy',self.mark))
        self.h.failure('destroy')


class HistoricalProtocolTests(unittest.TestCase):
    def test_two_report_edges_six_links_three_external_contexts_restore_each(self):
        h=HistoryWorld(); before=h.snapshot()
        with self.assertRaises(M.HistoricalProbeComplete): h.p.run()
        self.assertEqual([event[1] for event in h.events if event[0]=='start'],[69573,69576,69579])
        for offset,consumer in enumerate((69573,69576,69579)):
            mark=100+offset
            self.assertEqual(h.events[offset*7:(offset+1)*7],[('mark',mark,1),('start',consumer,mark),
                ('update',mark,False),('stop',mark),('destroy',mark),('undo',mark),('delete-mark',mark)])
        self.assertEqual(h.snapshot(),before)
        self.assertEqual(h.marks,{})
        report='\n'.join(h.t.lines)
        self.assertIn('неподтверждённых ссылок 6',report)
        self.assertIn('уникальных опор 2; внешних контекстов 3',report)
        self.assertIn('успешно 3; ошибок 0',report)
        for tag in (69574,69578,69582): self.assertIn('элемент '+str(tag),report)
        for tag in (64791,73591): h.w.objects[tag].GetBody.assert_not_called()
        h.assert_no_geometry_edits(self)

    def test_invisible_mark_reproduces_690037_and_local_return_without_reading_supports(self):
        h=HistoryWorld();before=h.snapshot()
        with self.assertRaises(M.UnresolvedProbeReferences) as rejected: h.p.snapshot()
        probe=M.HistoricalReferenceProbe(h.p,rejected.exception.supports)
        probe.preflight()
        probe.visibility=h.t.nx.Session.MarkVisibility.Invisible
        consumer,supports=probe.contexts[0]
        with self.assertRaises(VisibleUndoMarkRequired) as caught: probe.inspect_context(consumer,supports)
        self.assertEqual(caught.exception.ErrorCode,690037)
        self.assertEqual(h.snapshot(),before)
        self.assertEqual(h.events,[('mark',100,0),('start',consumer,100),('undo',100),('delete-mark',100)])
        self.assertEqual(h.marks,{})
        h.t.native.history_bounding_box.assert_not_called()
        self.assertIn('Локальный Undo выполнен','\n'.join(h.t.lines))
        h.assert_no_geometry_edits(self)

    def test_missing_visible_enum_stops_before_creating_mark_or_starting_manager(self):
        h=HistoryWorld();del h.t.nx.Session.MarkVisibility.Visible
        with self.assertRaises(AttributeError): h.p.run()
        h.t.session.SetUndoMark.assert_not_called()
        h.t.part.Features.StartEditWithRollbackManager.assert_not_called()
        self.assertEqual(h.events,[])

    def test_visible_value_is_taken_from_nx_enum_instead_of_a_numeric_guess(self):
        h=HistoryWorld();h.t.nx.Session.MarkVisibility.Visible=17
        with self.assertRaises(M.HistoricalProbeComplete): h.p.run()
        self.assertEqual([e[2] for e in h.events if e[0]=='mark'],[17,17,17,17])
        self.assertEqual(h.marks,{})
        self.assertEqual(h.mark_visibility,{})

    def test_each_context_uses_its_own_visible_mark_and_keeps_existing_marks(self):
        h=HistoryWorld();h.marks[55]=h.snapshot();h.mark_visibility[55]=1
        with self.assertRaises(M.HistoricalProbeComplete): h.p.run()
        created=[e[1] for e in h.events if e[0]=='mark']
        self.assertEqual(created,[100,101,102,103])
        self.assertEqual([e[2] for e in h.events if e[0]=='start'],created[:3])
        self.assertEqual([e[1] for e in h.events if e[0]=='undo'],created)
        self.assertEqual([e[1] for e in h.events if e[0]=='delete-mark'],created)
        self.assertEqual(set(h.marks),{55})
        self.assertEqual(h.mark_visibility,{55:1})
        h.assert_no_geometry_edits(self)

    def test_external_sketch_consumers_get_their_own_contexts(self):
        h=HistoryWorld()
        for tag in (69574,69578,69582): h.w.objects[tag].IsInternal=False
        with self.assertRaises(M.HistoricalProbeComplete): h.p.run()
        self.assertEqual([e[1] for e in h.events if e[0]=='start'],[69573,69574,69576,69578,69579,69582])
        h.assert_no_geometry_edits(self)

    def test_historical_face_requires_exact_membership_without_edge_vertices(self):
        h=HistoryWorld(); face=h.w.register(Face(64791,h.w.body))
        face.GetVertices=Mock(side_effect=AssertionError('Face is not an Edge'))
        def activate_face():
            if h.current==69573:
                h.w.body.edges.remove(face)
                h.w.body.faces.append(face)
        h.after_start=activate_face
        with self.assertRaisesRegex(M.UnresolvedProbeReferences,'поверхности ещё не реализовано'): h.p.run()
        self.assertIn('64791; Face; тело=1000','\n'.join(h.t.lines))
        face.GetVertices.assert_not_called()
        h.assert_no_geometry_edits(self)

    def test_protected_stock_cannot_be_used_as_history_edit_context(self):
        h=HistoryWorld()
        with self.assertRaises(M.UnresolvedProbeReferences) as rejected: h.p.snapshot()
        h.t.stock_features[69573]=None
        probe=M.HistoricalReferenceProbe(h.p,rejected.exception.supports)
        with self.assertRaisesRegex(RuntimeError,'защищённой заготовке'): probe.run()
        self.assertEqual(h.events,[])

    def test_missing_start_method_stops_before_undo_mark(self):
        h=HistoryWorld(); h.t.part.Features.StartEditWithRollbackManager=None
        with self.assertRaisesRegex(RuntimeError,'API EditWithRollbackManager'): h.p.run()
        self.assertEqual(h.events,[])

    def test_undo_mark_failure_never_starts_history_manager(self):
        h=HistoryWorld(); h.fail['mark']=RuntimeError('cannot create local mark')
        with self.assertRaisesRegex(RuntimeError,'cannot create local mark'): h.p.run()
        h.t.part.Features.StartEditWithRollbackManager.assert_not_called()
        self.assertEqual(h.events,[])

    def test_incomplete_manager_api_stops_before_mark_or_transition(self):
        for missing in ('UpdateFeature','Stop','Destroy'):
            with self.subTest(missing=missing):
                h=HistoryWorld()
                h.t.nx.Features.EditWithRollbackManager=NS(**{name:lambda:None for name in ('UpdateFeature','Stop','Destroy') if name!=missing})
                with self.assertRaises(AttributeError): h.p.run()
                self.assertEqual(h.events,[])
                h.assert_no_geometry_edits(self)

    def test_missing_native_history_exports_stop_before_transition(self):
        h=HistoryWorld(); h.t.native.prepare_history_probe_api.side_effect=RuntimeError('missing UF history reader')
        with self.assertRaisesRegex(RuntimeError,'missing UF history reader'): h.p.run()
        self.assertEqual(h.events,[])

    def test_unreadable_feature_states_are_not_defaulted(self):
        for attr,value in (('Timestamp',None),('Timestamp',True),('Suppressed',None),('IsInternal',0)):
            with self.subTest(attr=attr,value=value):
                h=HistoryWorld(); setattr(h.w.objects[69573],attr,value)
                with self.assertRaisesRegex(RuntimeError,'Timestamp/Suppressed/IsInternal'): h.p.run()
                self.assertEqual(h.events,[])

    def test_missing_expression_rhs_stops_before_transition(self):
        h=HistoryWorld(); del h.t.part.Expressions[0].RightHandSide
        with self.assertRaises(AttributeError): h.p.run()
        self.assertEqual(h.events,[])

    def test_unknown_current_feature_stops_before_transition(self):
        h=HistoryWorld(); h.current=99999
        with self.assertRaisesRegex(RuntimeError,'Текущий элемент не найден'): h.p.run()
        self.assertEqual(h.events,[])

    def test_support_without_external_direct_context_is_rejected(self):
        h=HistoryWorld(); h.w.objects[69573].IsInternal=True
        with self.assertRaisesRegex(RuntimeError,'нет внешнего прямого потребителя.*64791'): h.p.run()
        self.assertEqual(h.events,[])

    def test_suppressed_external_consumer_is_rejected(self):
        h=HistoryWorld(); h.w.objects[69573].Suppressed=True
        with self.assertRaisesRegex(RuntimeError,'Контекст подавлен'): h.p.run()
        self.assertEqual(h.events,[])

    def test_topology_type_mismatch_never_enters_history(self):
        h=HistoryWorld(); h.w.body.edges.append(h.w.body.faces.pop()); h.w.freeze(); h.p=h.w.probe
        with self.assertRaises(M.UnresolvedProbeReferences): h.p.run()
        self.assertEqual(h.events,[])
        h.assert_no_geometry_edits(self)

    def test_exact_tag_required_even_if_other_edge_has_same_bounds(self):
        h=HistoryWorld()
        def lose_edge():
            h.w.body.edges=[e for e in h.w.body.edges if e.Tag not in (64791,73591)]
            h.t.native.reference_body.side_effect=lambda tag,kind:h.w.body.Tag
        h.after_start=lose_edge
        with self.assertRaisesRegex(M.UnresolvedProbeReferences,'Точный tag.*64791'): h.p.run()
        self.assertEqual(len([e for e in h.events if e[0]=='undo']),3)
        self.assertTrue(all(e[2] for e in h.events if e[0]=='update'))
        h.t.native.history_bounding_box.assert_not_called()
        h.assert_no_geometry_edits(self)

    def test_read_errors_preserve_first_native_code_and_try_next_restored_context(self):
        h=HistoryWorld()
        first=M.NativeApiError(9123,'historic box','cannot read curve')
        h.t.native.history_bounding_box.side_effect=first
        with self.assertRaises(M.UnresolvedProbeReferences) as raised: h.p.run()
        self.assertEqual(raised.exception.code,9123)
        self.assertIs(raised.exception.__cause__,first)
        self.assertIn('успешно 0; ошибок 3','\n'.join(h.t.lines))
        self.assertEqual(len([e for e in h.events if e[0]=='delete-mark']),3)
        self.assertTrue(all(e[2] for e in h.events if e[0]=='update'))
        h.assert_no_geometry_edits(self)

    def test_secondary_read_error_keeps_its_code_and_traceback(self):
        h=HistoryWorld()
        class NxError(RuntimeError): ErrorCode=9345
        h.t.native.history_bounding_box.side_effect=[RuntimeError('first read'),NxError('second read'),(-1.,0.,0.,1.,2.,3.)]
        with self.assertRaises(M.UnresolvedProbeReferences): h.p.run()
        report='\n'.join(h.t.lines)
        self.assertIn('Код NX/API (ErrorCode): 9345',report)
        self.assertIn('NxError: second read',report)
        self.assertIn('Traceback',report)

    def test_unreadable_or_nonfinite_edge_vertices_are_rejected(self):
        for vertices in ((),(NS(X=1,Y=2,Z=3),),(NS(X=float('nan'),Y=0,Z=0),NS(X=1,Y=2,Z=3))):
            with self.subTest(vertices=vertices):
                h=HistoryWorld(); h.w.objects[64791].GetVertices.return_value=vertices
                with self.assertRaisesRegex(M.UnresolvedProbeReferences,'GetVertices'): h.p.run()
                self.assertIn('успешно 2; ошибок 1','\n'.join(h.t.lines))

    def test_wrong_object_type_and_foreign_owner_are_rejected(self):
        for case in ('type','owner'):
            with self.subTest(case=case):
                h=HistoryWorld()
                def damage():
                    if case=='type': h.w.objects[64791]=NS(Tag=64791,OwningPart=h.t.part)
                    else: h.t.native.reference_body.side_effect=lambda tag,kind:69573
                h.after_start=damage
                with self.assertRaises(M.UnresolvedProbeReferences): h.p.run()
                h.assert_no_geometry_edits(self)

    def test_start_failure_after_transition_still_undoes_without_manager(self):
        h=HistoryWorld(); before=h.snapshot(); h.fail['start']=RuntimeError('start failed after moving history')
        with self.assertRaisesRegex(RuntimeError,'start failed'): h.p.run()
        self.assertEqual(h.events,[('mark',100,1),('start',69573,100),('undo',100),('delete-mark',100)])
        self.assertEqual(h.snapshot(),before)

    def test_all_manager_cleanup_steps_and_local_undo_are_attempted_on_failure(self):
        for method in ('update','stop','destroy'):
            with self.subTest(method=method):
                h=HistoryWorld(); before=h.snapshot(); h.fail[method]=RuntimeError(method+' failure')
                with self.assertRaisesRegex(RuntimeError,method+' failure'): h.p.run()
                self.assertEqual([e[0] for e in h.events],['mark','start','update','stop','destroy','undo','delete-mark'])
                self.assertEqual(h.snapshot(),before)
                h.assert_no_geometry_edits(self)

    def test_local_undo_failure_keeps_mark_and_stops_before_next_context(self):
        h=HistoryWorld(); h.fail['undo']=RuntimeError('local undo failed')
        with self.assertRaisesRegex(RuntimeError,'local undo failed'): h.p.run()
        self.assertEqual([e[1] for e in h.events if e[0]=='start'],[69573])
        self.assertIn(100,h.marks)
        h.t.session.DeleteUndoMark.assert_not_called()
        self.assertNotIn('Локальный Undo выполнен','\n'.join(h.t.lines))

    def test_delete_mark_failure_stops_after_verified_return(self):
        h=HistoryWorld(); h.fail['delete-mark']=RuntimeError('delete mark failed')
        with self.assertRaisesRegex(RuntimeError,'delete mark failed'): h.p.run()
        self.assertEqual([e[1] for e in h.events if e[0]=='start'],[69573])
        self.assertIn('Локальный Undo выполнен','\n'.join(h.t.lines))

    def test_lost_active_copy_never_undoes_or_updates_another_part(self):
        h=HistoryWorld()
        h.after_start=lambda:setattr(h.t.session.Parts,'Work',NS(Tag=123))
        with self.assertRaisesRegex(RuntimeError,'копия должна оставаться'): h.p.run()
        self.assertEqual([e[0] for e in h.events],['mark','start'])
        h.t.session.UndoToMark.assert_not_called()
        h.assert_no_geometry_edits(self)

    def test_original_start_failure_survives_failed_local_undo(self):
        h=HistoryWorld(); h.fail={'start':RuntimeError('primary start failure'),'undo':RuntimeError('secondary undo failure')}
        with self.assertRaisesRegex(RuntimeError,'primary start failure'): h.p.run()
        self.assertIn('secondary undo failure','\n'.join(h.t.lines))
        h.t.session.DeleteUndoMark.assert_not_called()

    def test_secondary_cleanup_failure_keeps_code_and_traceback(self):
        h=HistoryWorld()
        h.fail={'start':RuntimeError('primary'),'undo':M.NativeApiError(9567,'undo','secondary')}
        with self.assertRaisesRegex(RuntimeError,'primary'): h.p.run()
        report='\n'.join(h.t.lines)
        self.assertIn('Код NX/API (code): 9567',report)
        self.assertIn('Traceback',report)

    def test_damage_after_stop_is_rejected_even_when_undo_repairs_it(self):
        for damage in ('current','state','expression','warning','sketch','cam','reference','bounds'):
            with self.subTest(damage=damage):
                h=HistoryWorld(); before=h.snapshot()
                h.after_stop=lambda:self.damage(h,damage)
                with self.assertRaises(RuntimeError): h.p.run()
                self.assertEqual([e[1] for e in h.events if e[0]=='start'],[69573])
                self.assertEqual(h.snapshot(),before)
                self.assertIn('Локальный Undo выполнен','\n'.join(h.t.lines))
                h.assert_no_geometry_edits(self)

    def test_damage_after_undo_keeps_mark_and_prevents_next_context(self):
        for damage in ('current','state','expression','warning','sketch','cam','reference','bounds'):
            with self.subTest(damage=damage):
                h=HistoryWorld(); h.after_undo=lambda:self.damage(h,damage)
                with self.assertRaises(RuntimeError): h.p.run()
                self.assertEqual([e[1] for e in h.events if e[0]=='start'],[69573])
                h.t.session.DeleteUndoMark.assert_not_called()
                self.assertNotIn('Локальный Undo выполнен','\n'.join(h.t.lines))

    @staticmethod
    def damage(h,kind):
        if kind=='current': h.current=69573
        elif kind=='state': h.w.objects[69573].Suppressed=True
        elif kind=='expression': h.t.part.Expressions[0].RightHandSide='16+1'
        elif kind=='warning': h.w.objects[69574].warnings.append('Parent modeled away')
        elif kind=='sketch': h.w.sketch.Sketch.geometry.pop()
        elif kind=='cam': h.w.cam[(900,0,'boundary_app',1,1)]=(0,None,0,None,None,0,None,None,0,None,1,2)
        elif kind=='reference': h.w.references[69574]=((73591,b'substituted coplanar edge'),)
        elif kind=='bounds': h.w.boxes[65349]=(0.,0.,0.,1.,1.,1.)


if __name__ == '__main__':
    unittest.main()
