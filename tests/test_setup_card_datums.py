"""XYZ belongs to a setup/MCS pair across saving and fresh NX exports."""
import json
from pathlib import Path
import re
import tempfile
import unittest
from unittest.mock import patch

from test_setup_card_model import CARD, report
from test_setup_card_multi_mcs import fixture


def mcs_report(folder, names):
    result = report(folder)
    result['views'] = []
    for i, name in enumerate(names):
        token = CARD.mcs_datum_key(name)
        for preset, label, *_ in CARD.VIEW_PRESETS:
            result['views'].append(dict(file=token + '_' + preset, preset_file=preset,
                label=label, mcs_name=name, mcs_key='primary' if i == 0 else token,
                mcs_datum_key=token))
    return result


def field_key(name, axis):
    return 'datum_' + axis + '_' + CARD.mcs_datum_key(name)


def fill(markup, values):
    parsed = CARD.CardMarkup(markup)
    edits = []
    for node in parsed.nodes:
        key = node['attrs'].get('data-field')
        if node['tag'] == 'input' and key in values:
            opening = markup[node['start']:node['open_end']]
            opening = re.sub(r'\bvalue="[^"]*"', lambda m: 'value="%s"' % CARD.escaped(values[key]), opening, count=1)
            edits.append((node['start'], node['open_end'], opening))
    return CARD.edit_markup_spans(markup, edits)


def fields(markup):
    parsed = CARD.CardMarkup(markup)
    return {node['attrs']['data-field']: node['attrs'].get('value', '')
            for node in parsed.nodes if CARD.CardMarkup.has_class(node, 'datum-input')}


class McsDatumTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.output = Path(temporary.name)

    def render(self, names, folder='SETUP_1'):
        return CARD.render_setup(self.output, mcs_report(folder, names), folder, [0])

    def test_each_view_sheet_has_independent_xyz_and_the_same_editor(self):
        markup = self.render(['A', 'B', 'C <SIDE> & "UP"'])
        parsed = CARD.CardMarkup(markup)
        tables = [n for n in parsed.nodes if parsed.has_class(n, 'coordinates')]
        self.assertEqual([n['attrs']['data-mcs-name'] for n in tables], ['A', 'B', 'C <SIDE> & "UP"'])
        self.assertEqual(len(fields(markup)), 9)
        self.assertEqual(markup.count('class="datum-toggle"'), 9)
        self.assertEqual(markup.count('class="datum-print field center"'), 9)
        self.assertEqual(markup.count('class="continuation-header mcs-datum-header"'), 2)
        for axis in 'XYZ':
            self.assertIn(field_key('B', axis), fields(markup))

    def test_identity_survives_new_session_tags_and_primary_mcs_change(self):
        groups, _, jobs = fixture()
        context = jobs[0]['context']
        context['capture_mcs'] = list(groups[:2])
        with patch.object(CARD, 'nearest_mcs', side_effect=lambda nx, op: op.mcs):
            before = CARD.setup_view_contexts(None, context)
            groups[0].Tag, groups[1].Tag = 301, 302
            context.update(mcs=groups[1], mcs_groups=list(reversed(groups[:2])),
                           capture_mcs=list(reversed(groups[:2])))
            after = CARD.setup_view_contexts(None, context)
        self.assertEqual({v['mcs'].Name: v['capture_datum_key'] for v in before},
                         {v['mcs'].Name: v['capture_datum_key'] for v in after})
        self.assertEqual(after[0]['capture_mcs_key'], 'primary')
        self.assertNotEqual(before[0]['capture_datum_key'], before[1]['capture_datum_key'])

    def test_reorder_and_new_mcs_keep_values_with_their_owner(self):
        values = {field_key(name, axis): name + '/' + axis + ' <25> & "УПОР"'
                  for name in ('A', 'B') for axis in 'XYZ'}
        values[field_key('B', 'Y')] = ''
        saved = fill(self.render(['A', 'B']), values)
        result = CARD.retain_setup_annotations(self.render(['B', 'C', 'A']), saved)
        actual = fields(result)
        self.assertEqual({k: actual[k] for k in values}, values)
        self.assertTrue(all(actual[field_key('C', axis)] == '' for axis in 'XYZ'))
        parsed = CARD.CardMarkup(result)
        mirrors = [parsed.text(n) for n in parsed.nodes if parsed.has_class(n, 'datum-print')]
        self.assertEqual(mirrors, [actual[field_key(name, axis)] for name in ('B', 'C', 'A') for axis in 'XYZ'])

    def test_temporarily_unchecked_mcs_returns_and_live_empty_overrides_cache(self):
        initial = {field_key('A', 'X'): 'ЦЕНТР', field_key('B', 'Y'): 'ОТ ГУБКИ'}
        old = fill(self.render(['A', 'B']), initial)
        single = CARD.retain_setup_annotations(self.render(['A']), old)
        single = fill(single, {field_key('A', 'X'): ''})
        restored = CARD.retain_setup_annotations(self.render(['B', 'A']), single)
        self.assertEqual(fields(restored)[field_key('B', 'Y')], 'ОТ ГУБКИ')
        self.assertEqual(fields(restored)[field_key('A', 'X')], '')
        self.assertEqual(CARD.retain_setup_annotations(restored, restored), restored)

    def test_v262_cover_uses_actual_photo_mcs_when_first_operation_mcs_was_unchecked(self):
        saved = ('<section class="setup-document" data-nx-setup-identity="%s">'
                 '<article class="page cover-page">%s<figure class="photo" data-image="s01_03_iso.png">'
                 '<span class="mcs-name">B &amp; SIDE</span></figure></article></section>') % (
                     CARD.escaped(json.dumps(dict(mcs='A', folder=['PROGRAM', 'SETUP_1']))),
                     fill(CARD.datum_field('X'), {'datum_X': 'ОТ УПОРА'}))
        result = fields(CARD.retain_setup_annotations(self.render(['A', 'B & SIDE']), saved))
        self.assertEqual(result[field_key('A', 'X')], '')
        self.assertEqual(result[field_key('B & SIDE', 'X')], 'ОТ УПОРА')

    def test_v262_duplicate_name_cover_uses_original_capture_token(self):
        token = CARD.mcs_datum_key('A', 1)
        saved = ('<article class="page cover-page">%s<figure class="photo" '
                 'data-image="s01_mcs_%s_03_iso.png"><span class="mcs-name">A</span></figure></article>') % (
                     fill(CARD.datum_field('Z'), {'datum_Z': 'ВЕРХ ВТОРОЙ MCS'}), token)
        fresh = self.render(['A'])
        extra = CARD.datum_table(dict(mcs_name='A'), dict(mcs_name='A', mcs_datum_key=token))
        result = fields(CARD.retain_setup_annotations(fresh + extra, saved))
        self.assertEqual(result[field_key('A', 'Z')], '')
        self.assertEqual(result['datum_Z_' + token], 'ВЕРХ ВТОРОЙ MCS')

    def test_legacy_select_and_note_formatting_survive(self):
        saved = ('<section class="setup-document" data-nx-setup-identity="%s" '
                 'data-note-heights="{&quot;cover&quot;:30}"><article class="page cover-page">'
                 '<select data-field="datum_x"><option>ЦЕНТР</option><option selected>ЛЕВО</option></select>'
                 '<div data-field="note">Строка 1<br>Строка &amp; 2</div></article>'
                 '<template><input data-field="datum_X" value="ШАБЛОН"></template></section>') % CARD.escaped(
                     json.dumps(dict(mcs='A', folder=['PROGRAM', 'SETUP_1'])))
        result = CARD.retain_setup_annotations(self.render(['A', 'B']), saved)
        self.assertEqual(fields(result)[field_key('A', 'X')], 'ЛЕВО')
        self.assertEqual(fields(result)[field_key('B', 'X')], '')
        self.assertIn('Строка 1<br>Строка &amp; 2', result)
        self.assertIn('&quot;cover&quot;: 30', result)
        self.assertNotIn('ШАБЛОН', result)

    def test_repeated_full_exports_and_partial_setup_update_read_saved_html(self):
        reports = [mcs_report('SETUP_1', ['A', 'B']), mcs_report('SETUP_2', ['A', 'B'])]
        state = dict(document_stem='card', setups=reports)
        filename = CARD.write_preview(self.output, state)
        parsed = CARD.CardMarkup(filename.read_text(encoding='utf8'))
        sections = [n for n in parsed.nodes if parsed.has_class(n, 'setup-document')]
        edits = []
        expected = []
        for index, section in enumerate(sections):
            values = {field_key(name, axis): '%s/%s/%s' % (index, name, axis)
                      for name in ('A', 'B') for axis in 'XYZ'}
            expected.append(values)
            edits.append((section['start'], section['end'], fill(parsed.raw(section), values)))
        filename.write_text(CARD.edit_markup_spans(parsed.source, edits), encoding='utf8')
        fresh = mcs_report('SETUP_2', ['B', 'A', 'C'])
        fresh['operation_rows'][0]['name'] = 'NEW_OPERATION'
        CARD.write_preview(self.output, dict(document_stem='card', setups=[fresh],
                           _existing_card=CARD.read_existing_card(filename)))
        existing = CARD.read_existing_card(filename)
        entries = existing['sections']
        for index, entry in enumerate(entries):
            actual = fields(entry['markup'])
            self.assertEqual({k: actual[k] for k in expected[index]}, expected[index])
        self.assertIn('NEW_OPERATION', entries[1]['markup'])
        # A new export process reconstructs reports and rereads the HTML.
        CARD.write_preview(self.output, dict(document_stem='card', setups=[
            mcs_report('SETUP_2', ['A', 'B']), mcs_report('SETUP_1', ['B', 'A'])],
            _existing_card=CARD.read_existing_card(filename)))
        for index, entry in enumerate(CARD.read_existing_card(filename)['sections']):
            self.assertEqual(fields(entry['markup']), expected[index])


if __name__ == '__main__':
    unittest.main()
