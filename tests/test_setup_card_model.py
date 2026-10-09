"""Exercise mesh tolerances, the fixed budget and saved-card model removal."""
import importlib.util
import base64
import json
import math
from pathlib import Path
import tempfile
from types import SimpleNamespace as NS
import unittest
from unittest.mock import Mock, patch

SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Setup_Prototype.py'
SPEC = importlib.util.spec_from_file_location('setup_model_test', SOURCE)
CARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CARD)


class Body:
    IsBlanked = IsOccurrence = False

    def __init__(self, tag=1, units='Millimeters'):
        self.Tag, self.OwningPart = tag, NS(PartUnits=units)
        self.GetFaces = Mock(return_value=[BodyFace(2, self.OwningPart), BodyFace(3, self.OwningPart)])


class BodyFace:
    def __init__(self, tag, part):
        self.Tag, self.OwningPart = tag, part


class Facets:
    def __init__(self, count=1, fail_body=False):
        self.count, self.fail_body, self.parameters = count, fail_body, []
        self.cycles = 0

    def AskDefaultParameters(self):
        return NS(CurveMaxLength=0.)

    def FacetSolid(self, tag, parameters):
        self.parameters.append((tag, parameters))
        if self.fail_body and tag == 1:
            raise RuntimeError('body tessellation failed')
        return 100 + tag

    def AskNFacetsInModel(self, model):
        return self.count

    def CycleFacets(self, model, previous):
        self.cycles += 1
        return previous + 1

    def AskVerticesOfFacet(self, model, facet):
        return 3, ((0., 0., 0.), (1., 0., 0.), (0., 1., 0.))


def nx_fixture(units='Millimeters', count=1, fail_body=False):
    body, facet = Body(units=units), Facets(count, fail_body)
    session = NS(SetUndoMark=Mock(return_value=10), UndoToMark=Mock(), DeleteUndoMark=Mock())
    nx = NS(Body=Body, UF=NS(UFSession=NS(GetUFSession=lambda: NS(Facet=facet))),
            Session=NS(GetSession=lambda: session, MarkVisibility=NS(Invisible=0)))
    part = NS(ModelingViews=NS(WorkView=NS(AskVisibleObjects=lambda: [body])))
    camera = dict(axes=[(1, 0, 0), (0, 1, 0), (0, 0, 1)])
    return nx, part, camera, body, facet, session


def report(folder='SETUP_1', count=3):
    result = CARD.new_report()
    tools = [dict(tag='100', number=1, name='MILL_D6_H50')]
    result.update(status='ok', document_stem='card', project_name='Test', author='Test',
                  selected_folder_path=['PROGRAM', folder], setup_name=folder,
                  mcs=dict(name=folder), project_tools=tools, tools=tools,
                  operation_count=count, part_units='Millimeters',
                  operation_rows=[dict(kind='operation', depth=0, tag=str(i + 1), name='OP_%02d' % i,
                                       mcs=folder, tool_name=tools[0]['name'], tool_tag='100', tool_number=1,
                                       seconds=30, feed=100, speed=3000, stock=.2, floor_stock=.1, zmin=-5,
                                       has_path=True, path_status='Complete', suppressed=False, tool_change=i == 0)
                                  for i in range(count)])
    return result


def screenshot_bytes():
    return CARD.png_rgb_bytes(40, 40, [bytes((180, 180, 200)) * 40] * 40)


def export(output, mesh, previous=None, folders=('SETUP_1', 'SETUP_2'), project_image=''):
    state = dict(document_stem='card', project_name='Test', project_model=mesh,
                 project_view_image=project_image, setups=[report(folder) for folder in folders],
                 _existing_card=previous)
    return CARD.write_preview(output, state)


class ModelTests(unittest.TestCase):
    def test_decimal_input_and_range(self):
        for text, expected in (('1', 1.), ('.5', .5), ('0,5', .5), (' 0.25 ', .25), ('0.0001', .0001)):
            with self.subTest(text=text):
                self.assertEqual(CARD.parse_project_model_accuracy(text), expected)
        for text in ('', '0', '-.5', '1.1', '1.0000000000000000001', 'nan', 'inf', 'word', None, '1e-9999'):
            with self.subTest(text=text), self.assertRaises(ValueError):
                CARD.parse_project_model_accuracy(text)

    def test_triangle_limit_accepts_thousands_separators_and_rejects_invalid_counts(self):
        for text, count in (('500 000', 500000), ('1\u00a0000\u00a0000', 1000000), ('250000', 250000), (1, 1)):
            self.assertEqual(CARD.parse_project_model_triangle_limit(text), count)
        for text in ('0', '-1', '1.5', '', 'nan', '2147483648'):
            with self.subTest(text=text), self.assertRaises(ValueError):
                CARD.parse_project_model_triangle_limit(text)

    def test_accuracy_changes_surface_and_edge_tolerances_in_both_units(self):
        for units, scale in (('Millimeters', 1.), ('Inches', 1. / 25.4)):
            for accuracy in (1., .5, .25, .001):
                with self.subTest(units=units, accuracy=accuracy):
                    nx, part, camera, body, facet, session = nx_fixture(units)
                    mesh = CARD.collect_project_model(nx, part, camera, accuracy)
                    self.assertEqual(mesh['triangles'], 1)
                    params = facet.parameters[0][1]
                    self.assertAlmostEqual(params.SurfaceDistTolerance, .01 / accuracy * scale)
                    self.assertEqual(params.SurfaceDistTolerance, params.CurveDistTolerance)
                    self.assertAlmostEqual(params.SurfaceAngularTolerance, math.radians(min(180., 3. / accuracy)))
                    self.assertEqual(params.SurfaceAngularTolerance, params.CurveAngularTolerance)
                    self.assertTrue(params.SpecifySurfaceTolerance and params.SpecifyCurveTolerance)
                    session.UndoToMark.assert_called_once_with(10, None)
                    session.DeleteUndoMark.assert_called_once_with(10, None)

    def test_invalid_accuracy_never_reaches_nx(self):
        nx, part, camera, body, facet, session = nx_fixture()
        with self.assertRaises(ValueError):
            CARD.project_facet_triangles(nx, body, accuracy=2)
        session.SetUndoMark.assert_not_called()
        self.assertFalse(facet.parameters)

    def test_default_budget_remains_500000_at_every_accuracy(self):
        CARD.check_project_triangle_budget(500000)
        for accuracy in (1., .5, .1):
            with self.subTest(accuracy=accuracy):
                nx, part, camera, body, facet, session = nx_fixture(count=500001)
                with self.assertRaises(CARD.ProjectModelLimitError):
                    CARD.collect_project_model(nx, part, camera, accuracy)
                self.assertEqual(facet.cycles, 0, 'Oversized mesh is rejected before vertices are read')
                body.GetFaces.assert_not_called()
                session.UndoToMark.assert_called_once()
        self.assertEqual(CARD.PROJECT_MODEL_DEFAULT_TRIANGLES, 500000)

    def test_increased_limit_reaches_facet_reader_and_is_embedded_for_viewer(self):
        nx, part, camera, body, facet, session = nx_fixture(count=881450)
        class ReadingStarted(Exception):
            pass
        facet.CycleFacets = Mock(side_effect=ReadingStarted)
        with self.assertRaises(ReadingStarted):
            CARD.read_project_facets(nx, 101, triangle_limit=1000000)
        facet.CycleFacets.assert_called_once()
        nx, part, camera, *_ = nx_fixture()
        self.assertEqual(CARD.collect_project_model(nx, part, camera, .5, 1000000)['triangle_limit'], 1000000)

    def test_custom_budget_is_shared_by_all_bodies(self):
        nx, part, camera, body, facet, session = nx_fixture(count=2)
        part.ModelingViews.WorkView.AskVisibleObjects = lambda: [body, Body(4)]
        with self.assertRaises(CARD.ProjectModelLimitError):
            CARD.collect_project_model(nx, part, camera, .5, 3)
        self.assertEqual(facet.cycles, 2, 'Only the first body fits in the combined limit')
        self.assertEqual(session.UndoToMark.call_count, 2)

    def test_face_fallback_uses_the_same_accuracy_and_cleans_up(self):
        nx, part, camera, body, facet, session = nx_fixture(fail_body=True)
        mesh = CARD.collect_project_model(nx, part, camera, .5)
        self.assertEqual(mesh['triangles'], 2)
        self.assertEqual([tag for tag, params in facet.parameters], [1, 2, 3])
        for tag, params in facet.parameters:
            self.assertAlmostEqual(params.SurfaceDistTolerance, .02)
            self.assertAlmostEqual(params.SurfaceAngularTolerance, math.radians(6))
        self.assertEqual(session.UndoToMark.call_count, 3)
        self.assertEqual(session.DeleteUndoMark.call_count, 3)

    def test_partial_reexport_removes_old_model_and_layout_and_can_enable_again(self):
        nx, part, camera, *_ = nx_fixture()
        mesh = CARD.collect_project_model(nx, part, camera)
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary)
            path = export(output, mesh)
            saved = path.read_text(encoding='utf-8')
            parsed = CARD.CardMarkup(saved)
            first = next(p for p in parsed.pages() if parsed.has_class(p, 'project-tools-page'))
            opening = parsed.source[first['start']:first['open_end']]
            # This is the persistent layout added by the real browser viewer.
            changed = opening.replace('project-tools-page', 'project-tools-page project-model-page')[:-1]
            changed += ' style="--project-tools-y:80mm;--data-scale:1" data-project-tools-bottom="276.5" data-model-clipped="false">'
            path.write_text(CARD.edit_markup_spans(saved, [(first['start'], first['open_end'], changed)]), encoding='utf-8')
            old = CARD.read_existing_card(path)
            picture = 'data:image/png;base64,' + base64.b64encode(screenshot_bytes()).decode('ascii')
            export(output, None, old, folders=('SETUP_2',), project_image=picture)
            off = CARD.read_existing_card(path)
            self.assertIsNone(off['project_model'])
            self.assertEqual(len(off['sections']), 2)
            parsed = CARD.CardMarkup(path.read_text(encoding='utf-8'))
            for node in parsed.nodes:
                self.assertFalse(parsed.has_class(node, 'project-model'))
                self.assertFalse(parsed.has_class(node, 'project-model-page'))
                self.assertNotIn('data-project-tools-bottom', node['attrs'])
            first = next(p for p in parsed.pages() if parsed.has_class(p, 'project-tools-page'))
            self.assertIn('--data-scale:1', first['attrs'].get('style', ''))
            self.assertTrue(any(n['tag'] == 'img' and n['attrs'].get('src') == picture for n in parsed.nodes))
            new = CARD.collect_project_model(nx, part, camera, .5)
            export(output, new, off, folders=('SETUP_2',))
            self.assertEqual(CARD.read_existing_card(path)['project_model']['id'], new['id'])

    def test_screenshot_fits_before_capture_restores_view_and_removes_work_png(self):
        for failure in (None, 'fit', 'capture'):
            with self.subTest(failure=failure), tempfile.TemporaryDirectory() as temporary:
                events = []
                point = lambda x, y, z: NS(X=x, Y=y, Z=z)
                view = NS(Tag=1, Matrix=NS(Xx=1., Xy=0., Xz=0., Yx=0., Yy=1., Yz=0., Zx=0., Zy=0., Zz=1.),
                          Origin=point(0, 0, 0), AbsoluteOrigin=point(0, 0, 0), Scale=.1,
                          LockRotations=False, SyncViews=False, TriadVisibility=True,
                          Regenerate=lambda: events.append('regenerate'))
                view.SetScale = lambda scale: setattr(view, 'Scale', scale)
                view.SetOrigin = lambda origin: setattr(view, 'AbsoluteOrigin', origin)
                def fit(tag, fraction):
                    events.append('fit')
                    self.assertEqual(fraction, CARD.FRAME_FIT_FRACTION)
                    view.SetScale(2.)
                    view.SetOrigin(point(10, 20, 0))
                    if failure == 'fit':
                        raise RuntimeError('fit failed')
                uf = NS(View=NS(FitView=fit, AskPerspective=lambda tag: (1, 0.)))
                nx = NS(UF=NS(UFSession=NS(GetUFSession=lambda: uf)), Point3d=point)
                part = NS(ModelingViews=NS(WorkView=view), WCS=NS(Visibility=True))
                original = CARD.snapshot_view(view)
                original['projection'] = CARD.read_view_projection(nx, view)
                def hide():
                    events.append('hide')
                    view.TriadVisibility = part.WCS.Visibility = False
                def restore():
                    events.append('restore')
                    view.TriadVisibility = part.WCS.Visibility = True
                    return []
                display = NS(hide=hide, hide_after_camera_change=Mock(), restore=restore)
                def capture(nx, part, path):
                    events.append('capture')
                    self.assertEqual(view.Scale, 2.)
                    self.assertEqual(CARD.xyz(view.AbsoluteOrigin), (10., 20., 0.))
                    self.assertEqual(CARD.matrix_rows(view.Matrix), original['matrix'])
                    self.assertLess(events.index('fit'), events.index('capture'))
                    path.write_bytes(screenshot_bytes())
                    if failure == 'capture':
                        raise RuntimeError('capture failed')
                with patch.object(CARD, 'ScreenshotDisplay', return_value=display), patch.object(CARD, 'export_png', side_effect=capture):
                    if failure:
                        with self.assertRaisesRegex(RuntimeError, failure + ' failed'):
                            CARD.capture_project_view(nx, part, Path(temporary), original)
                    else:
                        image = CARD.capture_project_view(nx, part, Path(temporary), original)
                        self.assertTrue(image.startswith('data:image/png;base64,'))
                self.assertEqual(CARD.snapshot_view(view), {k: v for k, v in original.items() if k != 'projection'})
                self.assertTrue(part.WCS.Visibility)
                self.assertIn('restore', events)
                self.assertFalse((Path(temporary) / 'project_view.png').exists())


if __name__ == '__main__':
    unittest.main()
