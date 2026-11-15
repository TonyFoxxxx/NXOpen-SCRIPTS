"""Stateful protocol regressions, not evidence of NX kernel/SDK compatibility."""
import copy
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock, patch

from test_mirror_body_history import M, Feature, BodyFeature, Expression, Body, Fixture


class Curve:
    def __init__(self, tag): self.Tag = tag


class Point(Curve): pass


class Edge:
    def __init__(self, tag, body): self.Tag, self.body = tag, body
    def GetBody(self): return self.body


class Face(Edge):
    def GetEdges(self): return self.body.GetEdges()


class SketchFeature(Feature):
    def __init__(self, tag, curve, parents=()):
        super().__init__(tag, 'SKETCH', 'SKETCH_'+str(tag), parents)
        self.Sketch = NS(Tag=tag+700, geometry=[curve], expressions=[])
        self.Sketch.GetAllGeometry = lambda: self.Sketch.geometry[:]
        self.Sketch.GetAllExpressions = lambda: self.Sketch.expressions[:]


class Selection:
    def __init__(self): self.values = []
    def Add(self, values): self.values.extend(values)
    def GetArray(self): return self.values[:]


class ProbeWorld:
    """Simulate successful identity-preserving replacement and injected damage.

    NX is NOT known to behave like this: real replacement may change IDs or
    reject the map. Those are explicit failure cases below, not bypasses.
    """
    def __init__(self):
        t = self.t = object.__new__(M.Trial)
        self.objects, self.boxes, self.references = {}, {}, {}
        self.events, self.after_commit = [], lambda: None
        self.after_extract, self.after_map = lambda: None, lambda: None
        class Features(list):
            pass
        t.part = NS(Tag=5000, Features=Features(), Bodies=[], PartUnits=1)
        self.body = self.make_body(1000)
        self.upstream, self.centerline, self.projected = Curve(601), Curve(602), Curve(603)
        self.sketch0 = SketchFeature(1, self.upstream)
        self.extrude = BodyFeature(10,'EXTRUDE',parents=[self.sketch0],bodies=[self.body])
        self.blend = BodyFeature(11,'BLEND',parents=[self.extrude],bodies=[self.body])
        self.sketch = SketchFeature(22,self.centerline,parents=[self.blend])
        self.projection = Feature(23,'CPROJ',parents=[self.blend])
        self.body.features = [self.extrude,self.blend]
        for feature in (self.sketch0,self.extrude,self.blend,self.sketch,self.projection):
            self.add_feature(feature)
        self.projection.GetEntities = lambda:[self.projected]
        for curve in (self.upstream,self.centerline,self.projected):
            self.register(curve)
            self.boxes[curve.Tag] = (2.,3.,4.,6.,7.,8.)
        for feature in (self.sketch0,self.sketch):
            e = Expression(feature.Tag+800, feature)
            feature.Sketch.expressions = [e]
        self.references[22] = ((1001,b'attachment face'),(11,b'owning feature'))
        self.references[23] = ((1003,b'projected edge'),)
        self.original_features = t.part.Features[:]
        self.original_boxes = copy.deepcopy(self.boxes)
        self.extract_type = NS(ExtractType=NS(Body=1),ParentPartType=NS(WorkPart=2))
        replace_type = NS(MappingMethodType=NS(MapOnlyObjectsWithModelingDependencies=7))
        constants = NS(**{n:i for i,n in enumerate(('COMPARE_NOMATCH','COMPARE_FACES_IDENTICAL',
            'COMPARE_FACES_EQUIVALENT','COMPARE_SURFACES_IDENTICAL','COMPARE_SURFACES_EQUIVALENT',
            'COMPARE_EDGES_IDENTICAL','COMPARE_EDGES_EQUIVALENT','COMPARE_CURVES_IDENTICAL','COMPARE_CURVES_EQUIVALENT'))})
        t.nx = NS(Curve=Curve,Point=Point,Edge=Edge,Face=Face,
            Features=NS(BodyFeature=BodyFeature,SketchFeature=SketchFeature,
                        ExtractFaceBuilder=self.extract_type,ReplaceFeatureBuilder=replace_type),
            UF=NS(UFConstants=constants), BasePart=NS(Units=NS(Millimeters=1,Inches=2)),
            TaggedObjectManager=NS(GetTaggedObject=lambda tag:self.objects[tag]))
        t.uf = NS(Modl=NS())  # Deliberately no .NET-only UF wrapper methods.
        t.part.Features.CreateExtractFaceBuilder = Mock(side_effect=self.extract_builder)
        t.part.Features.CreateReplaceFeatureBuilder = Mock(side_effect=self.replace_builder)
        t.part.ScRuleFactory = NS(CreateRuleBodyDumb=Mock(side_effect=lambda b,s:('body-rule',tuple(o.Tag for o in b),s)))
        t.native = NS(reflection_matrix=Mock(return_value=[0]*16),
            transform_entities=Mock(side_effect=self.reflect),update_model=Mock(),
            sketch_of_geometry=Mock(return_value=self.sketch.Sketch.Tag))
        t.native.prepare_replacement_api = Mock()
        t.native.feature_references = Mock(side_effect=self.ask_references)
        t.native.reference_body = Mock(side_effect=lambda tag,kind:self.objects[tag].body.Tag)
        t.native.delete_candidate_parameters = Mock(side_effect=self.remove_copy_parameters)
        t.native.compare_geometry = Mock(return_value=1)
        t.native.compare_topology = Mock(side_effect=lambda a,b,*args:5 if isinstance(self.objects[a],Edge) and not isinstance(self.objects[a],Face) else 1)
        t.bodies = t.part.Bodies[:]
        t.bounds = dict(self.boxes)
        t.refs, t.frames, t.mcs_definitions, t.stock_features, t.protected_bodies = {},{},{},{},set()
        t.reference = lambda tag: t.refs.setdefault(tag,self.objects[tag])
        t.bounding_box = Mock(side_effect=lambda tag:self.boxes[int(tag)])
        t.lines, t.history_baseline, t.history_previous, t.body_history = [],None,None,{}
        t.plane = M.MirrorPlane([-79,0,0],[-1,0,0])  # boxes are already in this plane's CSYS
        t.sketch_geometry = {602:self.sketch.Sketch.Tag}
        t.boundary_planes, t.current_boundaries = {},{}
        t.current_custom_points, t.custom_points = {(900,0):(),(900,1):None},{}
        t.geometry_labels, t.current_geometry_labels = {900:'R1_CHERN_Z-0.97 [PlanarMilling]'},{}
        self.cam = {(900,0):('read',900,'read',900,(602,),((602,),)),
                    (900,0,'boundary',1):(0,0,2,0,0,0),
                    (900,0,'boundary_app',1,1):(0,None,0,None,None,0,None,None,0,None,1,1)}
        t.geometry_snapshot = Mock(side_effect=lambda **kwargs:copy.deepcopy(self.cam))
        t.operation_snapshot = Mock(return_value=[(900,'R1_CHERN_Z-0.97','PlanarMilling',800)])
        t.setup = NS(DeleteToolPath=Mock())
        t.transfer_mcs_and_stock = Mock()
        self.freeze()

    def register(self, obj):
        obj.OwningPart = self.t.part
        self.objects[obj.Tag] = obj
        return obj

    def make_body(self, tag):
        b = Body(tag)
        b.faces = [Face(tag+1,b),Face(tag+2,b)]
        b.edges = [Edge(tag+3,b)]
        for obj in [b]+b.faces+b.edges:
            self.register(obj)
            # Distinct face bounds prevent an incidental list-order match.
            offset = obj.Tag-tag
            self.boxes[obj.Tag] = (2.,float(offset),0.,6.,float(offset)+1.,1.)
        self.t.part.Bodies.append(b)
        return b

    def add_feature(self, feature):
        self.register(feature)
        feature.GetEntities = lambda:[]
        self.t.part.Features.append(feature)
        self.references[feature.Tag] = ()

    def freeze(self):
        t = self.t
        t.history_baseline = t.history_previous = None
        t.checkpoint_model_history('baseline',check_errors=True)
        self.topology = {b.Tag:(tuple(sorted(f.Tag for f in b.GetFaces())),tuple(sorted(e.Tag for e in b.GetEdges()))) for b in t.part.Bodies}
        t.capture_body_history(self.topology)
        self.probe = M.BodyReplacementProbe(t,self.body,self.topology,copy.deepcopy(self.cam),t.operation_snapshot())

    def ask_references(self, tag):
        return self.references[tag]

    def extract_builder(self, old):
        assert old is None
        def commit():
            self.events.append('extract')
            self.candidate = self.make_body(4000)
            f = BodyFeature(40,'EXTRACT',bodies=[self.candidate])
            self.candidate.features = [f]
            self.add_feature(f)
            self.after_extract()
            return f
        self.extract = NS(ExtractBodyCollector=NS(ReplaceRules=Mock()),Validate=Mock(return_value=True),
                          CommitFeature=Mock(side_effect=commit),Destroy=Mock())
        return self.extract

    def remove_copy_parameters(self, tag):
        self.events.append(('remove-parameters',(tag,)))
        assert tag == 4000
        old = self.candidate.features[0]
        self.t.part.Features.remove(old)
        f = BodyFeature(41,'BREP',bodies=[self.candidate])
        self.candidate.features = [f]
        self.add_feature(f)

    def reflect(self, tags, matrix):
        self.events.append(('reflect',tuple(tags)))
        assert tags == [4000], 'Never raw-transform original history/centerlines/points'
        for obj in [self.candidate]+self.candidate.faces+self.candidate.edges:
            self.boxes[obj.Tag] = M.reflected_box(self.boxes[obj.Tag],0,0.)

    def replace_builder(self):
        self.rows = [NS(ParentEntity=self.objects[tag],MatchedEntity=None,ReverseDirection=False,Validate=lambda:True)
                     for tag in (1001,1003,11)]
        def match(parent, target):
            for row in self.rows:
                if row.ParentEntity.Tag == parent.Tag: row.MatchedEntity = target
        self.replace = NS(SelectFeature=Selection(),ReplacementFeature=Selection(),
            UpdateMap=Mock(side_effect=self.after_map),
            FeatureReferences=NS(GetMatchedReferences=lambda:self.rows,Validate=lambda:True),
            SetMatchingForParent=Mock(side_effect=match),Validate=Mock(return_value=True),
            Commit=Mock(side_effect=self.commit),Destroy=Mock())
        return self.replace

    def commit(self):
        self.events.append('replace')
        t = self.t
        t.part.Features[:] = [f for f in t.part.Features if f not in (self.extrude,self.blend)]
        self.new_brep = BodyFeature(30,'BREP',bodies=[self.body])
        self.body.features = [self.new_brep]
        self.add_feature(self.new_brep)
        for f in (self.sketch,self.projection):
            f.parents = [self.new_brep]
            self.references[f.Tag] = tuple((30 if p in (10,11) else p,n) for p,n in self.references[f.Tag])
        for tag in (1000,1001,1002,1003,602,603):
            self.boxes[tag] = M.reflected_box(self.boxes[tag],0,0.)
        self.after_commit()


def ownerless_support_report_world():
    """The V1.26.02 report's dependency shape, not a reconstruction of its PRT.

    Six readable final entities, two ownerless historical edges, six consumer
    links, and both R1 centerlines with On. Unrelated geometry is synthetic.
    """
    w=ProbeWorld()
    w.body.faces=[w.register(Face(tag,w.body)) for tag in (538463,560022)]
    w.body.edges=[w.register(Edge(tag,w.body)) for tag in (64812,65210,66044,66038)]
    for tag in (64791,73591):
        obj=w.register(Edge(tag,w.body))
        obj.GetBody=Mock(side_effect=AssertionError('No guessed owner for historical support'))
    w.references[22]=((64812,b'csys edge'),(538463,b'csys face'),(65210,b'external edge'))
    w.references[23]=()
    arcs=[w.register(Curve(tag)) for tag in (65349,65353)]
    w.sketch.Name='SKETCH_000';w.sketch.Sketch.geometry=arcs
    for arc in arcs: w.boxes[arc.Tag]=(2.,3.,4.,6.,7.,8.)
    for datum_tag,sketch_tag,edge,index in ((69573,69574,64791,7),
                                          (69576,69578,73591,9),
                                          (69579,69582,73591,10)):
        datum=Feature(datum_tag,'DATUM_CSYS',parents=[w.blend])
        curve=w.register(Curve(sketch_tag+10000));w.boxes[curve.Tag]=(2.,3.,4.,6.,7.,8.)
        sketch=SketchFeature(sketch_tag,curve,parents=[datum])
        if index==7: sketch.Sketch.geometry=[]  # An empty sketch still has a support.
        w.add_feature(datum);w.add_feature(sketch)
        role=('Coordinate system for Datum Coordinate System('+str(index)+')').encode()
        w.references[datum_tag]=((edge,role),(560022,role))
        w.references[sketch_tag]=((edge,role+b' for sketch'),(560022,role+b' for sketch'))
    w.references[69578]+=((66044,b'external edge'),(66038,b'external edge'),(65349,b'external arc'))
    w.references[69582]+=((65349,b'external arc'),)
    w.add_feature(Feature(69595,'FSET','CAM_MAIN'))
    w.cam={}
    names=('R1_CHERN_Z-0.97','R1_CHERN_Z-1.149','R1_CHIST_Z-1','R1_CHIST_Z-1.179')
    for operation,name in enumerate(names,900):
        arc=65349 if operation%2==0 else 65353
        w.t.geometry_labels[operation]=name+' [PlanarMilling]'
        w.cam[(operation,0)]=('read',operation,'read',operation,(arc,),((arc,),))
        w.cam[(operation,0,'boundary_app',1,1)]=(0,None,0,None,None,0,None,None,0,None,1,1)
    w.freeze()
    return w


class ProbeReaderTests(unittest.TestCase):
    def test_reference_diagnostic_decoding_does_not_define_identity(self):
        roles = (b'face', 'опора'.encode('utf-8'), b'\xff', b'\\xff')
        self.assertEqual(M.reference_name(roles[1]), 'опора')
        self.assertEqual(M.reference_name(roles[2]), M.reference_name(roles[3]))
        self.assertNotEqual(roles[2], roles[3])  # Comparison keeps original bytes.

    def test_unique_mapping_is_independent_of_list_order(self):
        self.assertEqual(M.unique_probe_mapping([1,2],[20,10],lambda a,b:b==a*10,'faces'),{1:10,2:20})

    def test_missing_ambiguous_or_many_to_one_mapping_is_rejected(self):
        for predicate in (lambda a,b:False,lambda a,b:True,lambda a,b:b==10):
            with self.assertRaises(RuntimeError): M.unique_probe_mapping([1,2],[10,20],predicate,'faces')


class ReferenceOwnershipTests(unittest.TestCase):
    def assert_before_candidate(self, w):
        self.assertEqual(w.events,[])
        w.t.part.Features.CreateExtractFaceBuilder.assert_not_called()
        w.t.native.delete_candidate_parameters.assert_not_called()
        w.t.native.transform_entities.assert_not_called()

    def test_exact_final_topology_allows_protocol_without_suppressed_get_body(self):
        w=ProbeWorld()
        class Suppressed(RuntimeError): ErrorCode=630003
        for obj in w.body.faces+w.body.edges:
            obj.GetBody=Mock(side_effect=Suppressed('Operation forbidden on suppressed object'))
        with self.assertRaises(M.ReplacementProbeComplete): w.probe.run()
        for obj in w.body.faces+w.body.edges: obj.GetBody.assert_not_called()
        w.t.native.reference_body.assert_not_called()
        self.assertEqual(w.probe.map[1001],4001)
        self.assertEqual(w.probe.map[1003],4003)
        self.assertEqual(w.cam[(900,0)][4],(602,))
        self.assertEqual(w.cam[(900,0,'boundary_app',1,1)][-2:],(1,1))

    def test_exact_owner_index_includes_other_captured_bodies(self):
        w=ProbeWorld(); other=w.make_body(2000)
        w.references[22]=((2001,b'other face'),(2003,b'other edge'))
        w.freeze()
        for obj in other.faces+other.edges: obj.GetBody=Mock(side_effect=AssertionError('redundant query'))
        w.probe.snapshot()
        w.t.native.reference_body.assert_not_called()
        self.assertIn('тело=2000','\n'.join(w.t.lines))
        self.assert_before_candidate(w)

    def test_nonfinal_reference_to_other_known_unchanged_body_keeps_original_tag(self):
        w=ProbeWorld(); other=w.make_body(2000); w.register(Edge(2999,other))
        w.references[22]=((2999,b'historical external edge'),)
        w.freeze(); w.probe.snapshot()
        w.t.native.reference_body.assert_called_once_with(2999,'Edge')
        self.assertEqual(w.probe.initial_refs[22],((2999,b'historical external edge'),))
        self.assertIn('Опора другого тела сохранена','\n'.join(w.t.lines))
        self.assert_before_candidate(w)

    def test_unknown_owner_body_is_rejected_before_extraction(self):
        w=ProbeWorld(); w.register(Face(1999,Body(9999)))
        w.references[22]=((1999,b'unknown body'),)
        with self.assertRaisesRegex(M.UnresolvedProbeReferences,'тело 9999, которого нет'): w.probe.run()
        self.assert_before_candidate(w)

    def test_suppressed_reference_lists_every_consumer_and_queries_each_tag_once(self):
        w=ProbeWorld(); w.register(Face(1999,w.body)); w.register(Edge(1998,w.body))
        w.references[22]=((1999,b'attachment'),(1999,b'orientation'))
        w.references[23]=((1999,b'projection face'),(1998,b'projection edge'))
        failure=M.NativeApiError(630003,'UF_MODL_ask_face_body','Operation forbidden on suppressed object')
        def owner(tag,kind):
            if tag==1999: raise failure
            return 1000
        w.t.native.reference_body.side_effect=owner
        # Last feature/curve-state stage must not be blamed for the bad support.
        group=Feature(90,'FSET','CAM_MAIN'); w.add_feature(group); w.freeze()
        with self.assertRaises(M.UnresolvedProbeReferences) as caught: w.probe.run()
        message=str(caught.exception)
        for role in ('attachment','orientation','projection face','projection edge'):
            self.assertIn(role,message)
        self.assertEqual(w.t.native.reference_body.call_count,2)
        self.assertIn('SKETCH_22 [SKETCH] (элемент 22) / опора tag=1999 / attachment',w.t.stage)
        self.assertNotIn('CAM_MAIN',w.t.stage)
        self.assertIn('неподтверждённых ссылок 4','\n'.join(w.t.lines))
        self.assertIn('опора промежуточной истории тела 1000',message)
        self.assertEqual(caught.exception.code,630003)
        self.assertIs(caught.exception.__cause__,failure)
        self.assert_before_candidate(w)

    def test_owner_error_later_in_scan_remains_chained_after_earlier_map_failure(self):
        w=ProbeWorld(); w.register(Face(1999,w.body)); w.register(Edge(1998,w.body))
        w.references[22]=((1999,b'missing final face'),)
        w.references[23]=((1998,b'unreadable edge'),)
        failure=M.NativeApiError(630003,'UF_MODL_ask_edge_body','suppressed edge')
        w.t.native.reference_body.side_effect=[1000,failure]
        with self.assertRaises(M.UnresolvedProbeReferences) as caught: w.probe.run()
        self.assertIs(caught.exception.__cause__,failure)
        self.assertEqual(caught.exception.code,630003)
        self.assertIn('missing final face',str(caught.exception))
        self.assertIn('unreadable edge',str(caught.exception))
        self.assert_before_candidate(w)

    def test_owner_runtime_error_cannot_be_accepted_as_an_empty_reference(self):
        w=ProbeWorld(); w.register(Face(1999,w.body)); w.references[22]=((1999,b'attachment'),)
        failure=RuntimeError('owner output was NULL')
        w.t.native.reference_body.side_effect=failure
        with self.assertRaises(M.UnresolvedProbeReferences) as caught: w.probe.run()
        self.assertIn('owner output was NULL',str(caught.exception))
        self.assertIs(caught.exception.__cause__,failure)
        self.assert_before_candidate(w)

    def test_duplicate_topology_ownership_or_kind_is_rejected(self):
        for bad in (((1001,),()),((),(1001,))):
            w=ProbeWorld(); w.probe.topology[2000]=bad
            with self.subTest(topology=bad), self.assertRaisesRegex(RuntimeError,'Неоднозначная принадлежность/тип'):
                w.probe.run()
            w.t.native.reference_body.assert_not_called()
            self.assert_before_candidate(w)

    def test_nxopen_type_must_match_exact_topology_kind(self):
        w=ProbeWorld(); w.register(Edge(1001,w.body))
        with self.assertRaisesRegex(M.UnresolvedProbeReferences,'Тип NXOpen Edge.*топологией Face'):
            w.probe.run()
        w.t.native.reference_body.assert_not_called()
        self.assert_before_candidate(w)

    def test_reference_object_access_failure_identifies_consumer_and_role(self):
        w=ProbeWorld(); w.objects[1001].OwningPart=None
        with self.assertRaisesRegex(RuntimeError,'Не подтверждена опора текущей копии: 1001'):
            w.probe.run()
        self.assertIn('Объект опоры / SKETCH_22 [SKETCH] (элемент 22) / tag=1001 / attachment face',w.t.stage)
        self.assert_before_candidate(w)


class ProbeRoutingTests(unittest.TestCase):
    def test_dependency_graph_not_feature_names_selects_one_body(self):
        f = Fixture(); t = f.workflow(legacy_branch=False)
        f.sketch.parents = [f.blends[-1]]
        f.sketch.FeatureType = 'UNKNOWN_ASSOCIATIVE_FEATURE_2606'
        f.before()
        self.assertIs(t.choose_replacement_body(),f.body)

    def test_stock_only_consumers_keep_existing_transfer_branch(self):
        f = Fixture(); f.before()
        self.assertIsNone(f.trial.choose_replacement_body())

    def test_probe_route_never_reaches_old_transform_mcs_or_toolpath_deletion(self):
        for outcome in (M.ReplacementProbeComplete('done'),RuntimeError('mapping failed'),None):
            f = Fixture(); t = f.workflow(legacy_branch=False)
            f.sketch.parents = [f.blends[-1]]
            with patch.object(M,'BodyReplacementProbe') as cls:
                cls.return_value.run.side_effect = outcome
                with self.assertRaises(Exception): t.run_in_absolute_wcs()
                self.assertIs(cls.call_args.args[1],f.body)
            t.native.transform_entities.assert_not_called()
            t.transfer_mcs.assert_not_called()
            t.update_stock.assert_not_called()
            t.setup.DeleteToolPath.assert_not_called()


class ReplacementProtocolTests(unittest.TestCase):
    def test_complete_protocol_only_transforms_candidate_and_requests_mandatory_undo(self):
        w = ProbeWorld()
        self.assertFalse(hasattr(w.t.uf.Modl,'AskReferencesOfFeatures'))
        self.assertFalse(hasattr(w.t.uf.Modl,'DeleteObjectParms'))
        self.assertFalse(hasattr(w.t.uf.Modl,'Compare'))
        self.assertFalse(hasattr(w.t.uf.Modl,'CompareTopology'))
        with self.assertRaises(M.ReplacementProbeComplete): w.probe.run()
        self.assertEqual(w.events,['extract',('remove-parameters',(4000,)),('reflect',(4000,)),'replace'])
        self.assertEqual(w.probe.map,{1003:4003,1001:4001,1002:4002,1000:4000})
        self.assertFalse(w.replace.DoAutomaticGeomMatch)
        self.assertTrue(w.replace.CopyReplacementFeature)
        self.assertEqual(w.sketch.Sketch.GetAllGeometry()[0].Tag,602)
        self.assertEqual(w.cam[(900,0,'boundary_app',1,1)][-2:],(1,1))
        self.assertEqual(w.boxes[601],w.original_boxes[601])
        self.assertEqual(w.boxes[602],M.reflected_box(w.original_boxes[602],0,0.))
        w.t.setup.DeleteToolPath.assert_not_called()
        w.t.transfer_mcs_and_stock.assert_not_called()
        w.extract.Destroy.assert_called_once(); w.replace.Destroy.assert_called_once()

    def test_missing_reference_api_stops_before_creating_body(self):
        w = ProbeWorld(); del w.t.native.feature_references
        with self.assertRaises(AttributeError): w.probe.run()
        self.assertEqual(w.events,[])

    def test_missing_native_export_is_detected_before_reading_or_mutating_model(self):
        w = ProbeWorld()
        w.t.native.prepare_replacement_api.side_effect = RuntimeError('missing UF_MODL_compare')
        with self.assertRaisesRegex(RuntimeError,'UF_MODL_compare'): w.probe.run()
        self.assertEqual(w.events,[])
        w.t.native.feature_references.assert_not_called()
        w.t.part.Features.CreateExtractFaceBuilder.assert_not_called()

    def test_native_reference_error_is_not_empty_and_stops_before_candidate(self):
        w = ProbeWorld()
        failure = M.NativeApiError(777,'UF_MODL_ask_references_of_features','reference failed')
        w.t.native.feature_references.side_effect = failure
        with self.assertRaises(M.NativeApiError) as caught: w.probe.run()
        self.assertIs(caught.exception,failure)
        self.assertIn('UF_MODL_ask_references_of_features',w.t.stage)
        self.assertEqual(w.events,[])

    def test_external_parameter_consumer_stops_before_extract(self):
        w = ProbeWorld(); expression = Expression(950,w.extrude); expression.users.append(w.sketch)
        w.freeze()
        with self.assertRaisesRegex(RuntimeError,'внешних потребителей'): w.probe.run()
        self.assertEqual(w.events,[])

    def test_intermediate_history_face_requires_exact_support_not_coplanar_substitute(self):
        w = ProbeWorld(); w.register(Face(1999,w.body)); w.references[22] = ((1999,b'old attachment'),)
        with self.assertRaises(M.UnresolvedProbeReferences) as caught:
            w.probe.run()
        self.assertIn('опора промежуточной истории тела 1000',str(caught.exception))
        self.assertIn('SKETCH_22 [SKETCH] (элемент 22) / опора tag=1999 / old attachment',str(caught.exception))
        w.t.native.reference_body.assert_called_once_with(1999,'Face')
        self.assertEqual(w.events,[])

    def test_candidate_preparation_cannot_damage_original_sketch(self):
        w = ProbeWorld(); w.after_extract = lambda:w.sketch.parents.clear()
        with self.assertRaisesRegex(RuntimeError,'исходную историю'): w.probe.run()
        w.t.native.delete_candidate_parameters.assert_not_called()
        w.t.native.transform_entities.assert_not_called()
        w.extract.Destroy.assert_called_once()

    def test_candidate_must_be_independent_before_removing_parameters(self):
        w = ProbeWorld(); w.after_extract = lambda:setattr(w.candidate.features[0],'parents',[w.extrude])
        with self.assertRaisesRegex(RuntimeError,'независимым телом'): w.probe.run()
        w.t.native.delete_candidate_parameters.assert_not_called()

    def test_unreadable_brep_parents_and_expressions_do_not_mean_independent(self):
        for method in ('GetParents','GetExpressions','GetBodies'):
            w=ProbeWorld()
            feature=BodyFeature(30,'BREP',bodies=[w.body])
            w.body.features=[feature]
            setattr(feature,method,lambda:None)
            with self.subTest(method=method), self.assertRaises(RuntimeError):
                w.probe.plain_brep(w.body)

    def test_copy_reflection_cannot_move_originals_or_change_candidate_topology(self):
        for kind in ('original','topology'):
            w=ProbeWorld()
            def damage(tags,matrix):
                w.reflect(tags,matrix)
                if kind=='original': w.boxes[602]=(-6.,3.,4.,-2.,7.,8.)
                else: w.candidate.faces[0].Tag=9999
            w.t.native.transform_entities.side_effect=damage
            with self.subTest(kind=kind), self.assertRaises(RuntimeError): w.probe.run()
            w.t.part.Features.CreateReplaceFeatureBuilder.assert_not_called()

    def test_protected_stock_settings_and_body_identity_are_still_checked(self):
        for kind in ('mode','body'):
            w=ProbeWorld()
            feature=NS(GetBodies=lambda:[NS(Tag=2000)])
            w.t.stock_features={80:(feature,(2000,),{'mode':'Box','allowance':3.},None)}
            builder=NS(Validate=lambda:True,Destroy=Mock())
            w.t.tooling_builder=Mock(return_value=builder)
            w.t.stock_state=Mock(return_value={'mode':'Box','allowance':3.})
            if kind=='mode': w.t.stock_state.return_value={'mode':'Body','allowance':0.}
            else: feature.GetBodies=lambda:[NS(Tag=9999)]
            with self.subTest(kind=kind), self.assertRaisesRegex(RuntimeError,'защищённой заготовки'):
                w.probe.run()
            builder.Destroy.assert_called_once()
            w.t.native.transform_entities.assert_not_called()

    def test_geometry_comparison_failure_or_unknown_status_blocks_replacement(self):
        for equivalent,status in ((0,5),(1,99),(True,5),(1,6)):
            w = ProbeWorld(); w.t.native.compare_geometry.side_effect = None; w.t.native.compare_geometry.return_value = equivalent
            w.t.native.compare_topology.side_effect = None; w.t.native.compare_topology.return_value = status
            with self.subTest(equivalent=equivalent,status=status), self.assertRaises(RuntimeError): w.probe.run()
            w.t.part.Features.CreateReplaceFeatureBuilder.assert_not_called()
            w.t.native.transform_entities.assert_not_called()

    def test_partial_or_unknown_builder_map_never_commits(self):
        for alteration in ('missing','unknown','reverse','wrong-match','selection'):
            w = ProbeWorld()
            def alter():
                if alteration == 'missing': w.rows[:] = w.rows[1:]
                if alteration == 'unknown': w.rows[0].ParentEntity = w.centerline
                if alteration == 'reverse': w.rows[0].ReverseDirection = None
                if alteration == 'wrong-match': w.replace.SetMatchingForParent.side_effect = lambda *args:None
                if alteration == 'selection': w.replace.SelectFeature.values.pop()
            w.after_map = alter
            with self.subTest(alteration=alteration), self.assertRaises(RuntimeError): w.probe.run()
            w.replace.Commit.assert_not_called(); w.replace.Destroy.assert_called_once()

    def test_changed_original_body_face_or_edge_id_is_rejected(self):
        for kind in ('body','face','edge'):
            w = ProbeWorld()
            def damage():
                obj = w.body if kind=='body' else w.body.faces[0] if kind=='face' else w.body.edges[0]
                obj.Tag += 10000
            w.after_commit = damage
            with self.subTest(kind=kind), self.assertRaisesRegex(RuntimeError,'идентификаторы'): w.probe.run()

    def test_retained_sketch_parent_expression_membership_or_position_damage_stops(self):
        for kind in ('parent','expression','membership','position','warning','reference'):
            w = ProbeWorld()
            def damage():
                if kind=='parent': w.sketch.parents=[]
                if kind=='expression': w.sketch.expressions=[]
                if kind=='membership': w.sketch.Sketch.geometry=[w.projected]
                if kind=='position': w.boxes[602]=w.original_boxes[602]
                if kind=='warning': w.sketch.warnings=['Parent point modeled away']
                if kind=='reference': w.references[22]=((1002,b'attachment face'),(30,b'owning feature'))
            w.after_commit = damage
            with self.subTest(kind=kind), self.assertRaises(RuntimeError): w.probe.run()
            w.t.setup.DeleteToolPath.assert_not_called()

    def test_centerline_cannot_be_replaced_by_body_edge_or_on_changed_to_tanto(self):
        for kind in ('selection','position'):
            w = ProbeWorld()
            def damage():
                key=(900,0) if kind=='selection' else (900,0,'boundary_app',1,1)
                value=list(w.cam[key])
                if kind=='selection': value[-2],value[-1]=(1003,),((1003,),)
                else: value[-1]=2
                w.cam[key]=tuple(value)
            w.after_commit=damage
            with self.subTest(kind=kind), self.assertRaisesRegex(RuntimeError,'R1_CHERN_Z-0.97'):
                w.probe.run()

    def test_original_three_plane_selection_is_checked_during_candidate_preparation(self):
        w=ProbeWorld(); w.t.mcs_definitions={91:None}
        w.t.verify_three_plane_selections=Mock(side_effect=RuntimeError('different selected face'))
        with self.assertRaisesRegex(RuntimeError,'different selected face'): w.probe.run()
        w.t.native.transform_entities.assert_not_called()

    def test_explicit_boundary_plane_and_custom_points_are_not_exempted(self):
        for kind in ('plane','point'):
            w=ProbeWorld()
            if kind=='plane':
                w.t.boundary_planes={(900,0,1):([0.,0.,0.],[1,0,0,0,1,0,0,0,1])}
                w.t.current_boundaries={(900,0,1):(1,NS(origin=[0.,0.,0.],matrix=[1,0,0,0,1,0,0,0,1]))}
            def damage():
                if kind=='plane': w.t.current_boundaries[(900,0,1)][1].origin[0]=99
                else: w.t.current_custom_points[(900,0)]=((0,(99,0,0),(None,None,0)),)
            w.after_commit=damage
            with self.subTest(kind=kind), self.assertRaisesRegex(RuntimeError,'плоскость CAM|точки CAM'):
                w.probe.run()


if __name__ == '__main__': unittest.main()
