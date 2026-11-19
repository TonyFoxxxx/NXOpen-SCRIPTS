"""Regenerate whole Program Order folders and edit individual sheets in saved HTML."""
import json
from pathlib import Path
import re
import shutil
import tempfile
import unittest

from test_setup_card_datums import mcs_report
from test_setup_card_model import CARD, report


def setup(path, label=None, operation='OP'):
    item = report(path[-1], count=1)
    item.update(selected_folder_path=path, setup_name=label or path[-1])
    item['operation_rows'][0].update(name=operation, depth=1)
    item['operation_rows'].insert(0, dict(kind='group', name=path[-1], depth=0))
    return item


class SetupUpdateTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.output = Path(temporary.name)
        self.file = self.output / 'card.html'
        self.parent = ['PROGRAM', 'SETUP_A']
        self.first = self.parent + ['ROUGH']
        self.second = self.parent + ['FINISH']
        self.other = ['PROGRAM', 'OTHER']
        self.paths = [self.parent[:1], self.parent, self.first, self.second, self.other]

    def export(self, *setups):
        state = dict(document_stem='card', setups=setups, _program_folder_paths=self.paths,
                     _existing_card=CARD.read_existing_card(self.file))
        CARD.write_preview(self.output, state)
        return CARD.read_existing_card(self.file)['sections']

    def test_short_path_replaced_in_place_after_mcs_change(self):
        old = self.export(setup(['FINISH'], 'G54', 'OLD'), setup(self.other, 'G54', 'KEEP'))
        updated = self.export(setup(self.second, 'G55', 'NEW'))
        self.assertEqual([s['key'] for s in updated], [s['key'] for s in old])
        self.assertEqual(updated[1]['markup'], old[1]['markup'])
        self.assertEqual(updated[0]['identity'], dict(mcs='G55', folder=self.second))
        self.assertIn('NEW', updated[0]['markup'])
        self.assertNotIn('title="OLD"', updated[0]['markup'])

    def test_missing_identity_recovered_from_original_group_after_mcs_change(self):
        old, = self.export(setup(self.second, 'G54'))
        html = re.sub(r' data-nx-setup-identity="[^"]*"', '', self.file.read_text(encoding='utf8'), count=1)
        self.file.write_text(html, encoding='utf8')
        self.assertEqual(CARD.read_existing_card(self.file)['sections'][0]['legacy_folder'], 'FINISH')
        updated, = self.export(setup(self.second, 'G55'))
        self.assertEqual(updated['key'], old['key'])
        self.assertEqual(updated['identity'], dict(mcs='G55', folder=self.second))

    def test_existing_parent_cannot_be_duplicated_by_selecting_subfolder(self):
        self.export(setup(self.parent), setup(self.other))
        before = self.file.read_bytes()
        with self.assertRaisesRegex(RuntimeError, 'выберите в Program Order папку «PROGRAM / SETUP_A»'):
            self.export(setup(self.second))
        self.assertEqual(self.file.read_bytes(), before)

    def test_selecting_parent_consolidates_children_in_their_original_position(self):
        old = self.export(setup(self.first), setup(self.other), setup(self.second))
        updated = self.export(setup(self.parent))
        self.assertEqual([s['key'] for s in updated], [s['key'] for s in old[:2]])
        self.assertEqual(updated[0]['identity']['folder'], self.parent)
        self.assertIn('OTHER', updated[1]['markup'])

    def test_same_mcs_on_distinct_full_paths_remains_two_setups(self):
        first, = self.export(setup(self.first, 'G54'))
        updated = self.export(setup(self.second, 'G54'))
        self.assertEqual([s['identity']['folder'] for s in updated], [self.first, self.second])
        self.assertEqual(updated[0]['key'], first['key'])

    def test_ambiguous_old_path_compared_with_unselected_folders(self):
        self.export(setup(['FINISH'], 'G54'))
        before = self.file.read_bytes()
        self.paths.append(self.other + ['FINISH'])
        with self.assertRaisesRegex(RuntimeError, 'подходит нескольким папкам'):
            self.export(setup(self.second, 'G54'))
        self.assertEqual(self.file.read_bytes(), before)

    def test_missing_old_folder_with_same_mcs_does_not_silently_append(self):
        self.export(setup(['PROGRAM', 'OLD_FOLDER'], 'G54'))
        before = self.file.read_bytes()
        with self.assertRaisesRegex(RuntimeError, 'OLD_FOLDER.*FINISH'):
            self.export(setup(self.second, 'G54'))
        self.assertEqual(self.file.read_bytes(), before)

    def test_proven_duplicate_is_consolidated_unless_saved_annotations_conflict(self):
        old = self.export(setup(self.first, 'G54'), setup(self.other, 'G54'))
        html = self.file.read_text(encoding='utf8')
        wrong = CARD.escaped(json.dumps(old[1]['identity'], ensure_ascii=False))
        right = CARD.escaped(json.dumps(old[0]['identity'], ensure_ascii=False))
        self.assertIn(wrong, html)
        duplicates = html.replace(wrong, right)
        # Distinct HTML keys refer to exactly the same live Program Order path.
        self.file.write_text(duplicates, encoding='utf8')
        updated, = self.export(setup(self.first, 'G55'))
        self.assertEqual(updated['key'], old[0]['key'])
        self.file.write_text(duplicates, encoding='utf8')
        saved = CARD.read_existing_card(self.file)['sections'][1]['markup']
        parsed = CARD.CardMarkup(saved)
        note = next(n for n in parsed.nodes if n['attrs'].get('data-field') == 'note')
        conflicting = CARD.edit_markup_spans(saved, [(note['open_end'], note['content_end'], 'Second note')])
        self.file.write_text(duplicates.replace(saved, conflicting, 1), encoding='utf8')
        before = self.file.read_bytes()
        with self.assertRaisesRegex(RuntimeError, 'различаются сохранённые'):
            self.export(setup(self.first))
        self.assertEqual(self.file.read_bytes(), before)


try:
    from playwright.sync_api import sync_playwright, expect
except ImportError:
    sync_playwright = None


@unittest.skipUnless(sync_playwright and (shutil.which('chromium') or shutil.which('google-chrome')),
                     'requires Playwright and Chromium')
class BrowserSheetTests(unittest.TestCase):
    def test_delete_undo_download_print_and_regenerate(self):
        with tempfile.TemporaryDirectory() as directory, sync_playwright() as playwright:
            output = Path(directory)
            first = mcs_report('A', ['A1', 'A2'])
            first.update(operation_rows=report('A', 120)['operation_rows'], operation_count=120)
            other = report('B')
            path = CARD.write_preview(output, dict(document_stem='card', setups=[first, other]))
            browser = playwright.chromium.launch(executable_path=shutil.which('chromium') or shutil.which('google-chrome'))
            errors = []

            def open_card():
                context = browser.new_context()
                context.route('http://127.0.0.1/card.html', lambda route: route.fulfill(
                    path=path, content_type='text/html; charset=utf-8'))
                context.add_init_script('window.showOpenFilePicker=undefined')
                page = context.new_page()
                page.on('pageerror', lambda e: errors.append(str(e)))
                page.goto('http://127.0.0.1/card.html')
                page.locator('.page-remove:visible').first.wait_for()
                return page

            page = open_card()
            papers = '.setup-pages > .page:not([data-page-deleted])'
            operations = '.ops tbody tr[data-kind="operation"]'
            group = page.locator('.setup-document').first
            pages = page.locator(papers)
            count = pages.count()
            target = group.locator('.operation-page').first
            removed = target.locator(operations).evaluate_all('(rows)=>rows.map(r=>r.dataset.rowId)')
            self.assertGreater(len(removed), 0)
            self.assertEqual(pages.locator(':scope > .page-remove').count(), count)
            target.locator('.page-remove').click()
            dialog = page.get_by_role('dialog')
            expect(dialog).to_have_text(re.compile('Вы уверены, что ходите удалить лист'))
            expect(dialog.get_by_text('Нет', exact=True)).to_be_focused()
            dialog.get_by_text('Нет', exact=True).click()
            expect(pages).to_have_count(count)
            expect(page.locator('#undo')).to_be_disabled()

            def remove(sheet):
                sheet.locator(':scope > .page-remove').click()
                page.get_by_role('dialog').get_by_text('Да', exact=True).click()

            remove(target)
            expect(pages).to_have_count(count - 1)
            visible_rows = group.locator(papers).locator(operations)
            self.assertTrue(set(removed).isdisjoint(visible_rows.evaluate_all('(rs)=>rs.map(r=>r.dataset.rowId)')))
            expect(visible_rows).to_have_count(120 - len(removed))
            page.keyboard.press('Control+z')
            expect(pages).to_have_count(count)
            expect(visible_rows).to_have_count(120)
            remove(target)
            remove(group.locator('.mcs-view-page'))
            remove(group.locator('.project-tools-page'))
            expect(pages).to_have_count(count - 3)
            page.emulate_media(media='print')
            expect(pages.locator('.page-remove').first).to_be_hidden()
            pdf = page.pdf(prefer_css_page_size=True)
            self.assertEqual(len(re.findall(rb'/Type /Page\b', pdf)), pages.count())
            page.emulate_media(media='screen')
            with page.expect_download() as download:
                page.locator('#save').click()
            download.value.save_as(path)
            page.context.close()

            page = open_card()
            expect(page.locator(papers)).to_have_count(count - 3)
            remaining = page.locator('.setup-document').first.locator(papers).locator(operations)
            expect(remaining).to_have_count(120 - len(removed))
            expect(page.locator('#undo')).to_be_disabled()
            page.context.close()
            for selected, expected in ((other, count - 3), (first, count - 1)):
                CARD.write_preview(output, dict(document_stem='card', setups=[selected],
                                   _existing_card=CARD.read_existing_card(path)))
                page = open_card()
                expect(page.locator(papers)).to_have_count(expected)
                page.context.close()
            page = open_card()
            for expected in range(page.locator(papers).count() - 1, -1, -1):
                remove(page.locator(papers).first)
                expect(page.locator(papers)).to_have_count(expected)
            expect(page.locator('#empty-card')).to_be_visible()
            expect(page.locator('#print')).to_be_disabled()
            page.locator('#undo').click()
            expect(page.locator(papers)).to_have_count(1)
            self.assertFalse(errors, errors)
            browser.close()
