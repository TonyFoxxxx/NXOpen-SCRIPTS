"""File preservation, native-save boundary and failure recovery scenarios."""
import hashlib
import importlib.util
import io
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock
import zipfile

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    "save_project_archive", ROOT / "scripts/NX_Save_Project_And_Archive.py")
journal = importlib.util.module_from_spec(spec)
spec.loader.exec_module(journal)
NX = SimpleNamespace(BasePart=SimpleNamespace(
    SaveComponents=SimpleNamespace(TrueValue=object()),
    CloseAfterSave=SimpleNamespace(FalseValue=object())))


class SaveStatus:
    NumberUnsavedParts = 0
    NumberUnsavedObjects = 0

    def __init__(self):
        self.disposed = False

    def Dispose(self):
        self.disposed = True

    def GetPart(self, index):
        return SimpleNamespace(FullPath="failed-component.prt")

    def GetStatus(self, index):
        return 123

    def GetObjectStatus(self, index):
        return 456


class WorkPart:
    IsReadOnly = False
    Tag = 1042

    def __init__(self, path):
        self.FullPath = str(path)
        self.IsModified = True
        self.content = b"saved native project version 1"
        self.status = SaveStatus()
        self.save_calls = []

    def Save(self, components, close):
        self.save_calls.append((components, close))
        Path(self.FullPath).write_bytes(self.content)
        self.IsModified = False
        return self.status


class SaveArchiveTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.source = self.folder / "Деталь 305.10.prt"
        self.archive = self.source.with_suffix(".zip")
        self.source.write_bytes(b"old native file")
        self.part = WorkPart(self.source)
        self.session = SimpleNamespace(Parts=SimpleNamespace(Work=self.part))

    def run_save(self):
        return journal.save_project(NX, self.session)

    def assert_archive(self, expected):
        with zipfile.ZipFile(self.archive) as archive:
            self.assertEqual(archive.namelist(), [self.source.name])
            self.assertEqual(archive.read(self.source.name), expected)
            self.assertIsNone(archive.testzip())

    def assert_only_outputs(self):
        self.assertEqual(set(self.folder.iterdir()), {self.source, self.archive})

    def test_save_archive_plain_save_then_save_archive(self):
        result = self.run_save()
        self.assertEqual(result, (str(self.source), str(self.archive)))
        self.assertEqual(self.part.save_calls, [(NX.BasePart.SaveComponents.TrueValue,
                                              NX.BasePart.CloseAfterSave.FalseValue)])
        self.assertTrue(self.part.status.disposed)
        self.assertFalse(self.part.IsModified)
        self.assert_archive(self.part.content)
        self.assert_only_outputs()
        # A subsequent ordinary save can overwrite the same original: there
        # are no remaining source locks, changed paths or archive redirection.
        old_zip = self.archive.read_bytes()
        self.part.content = b"normal save, new model state"
        self.part.Save(NX.BasePart.SaveComponents.TrueValue,
                       NX.BasePart.CloseAfterSave.FalseValue)
        self.assertEqual(self.source.read_bytes(), self.part.content)
        self.assertEqual(self.archive.read_bytes(), old_zip)
        self.part.content = b"new script save, final model state"
        self.run_save()
        self.assert_archive(self.part.content)
        self.assert_only_outputs()
        self.assertEqual(self.part.FullPath, str(self.source))
        self.assertIs(self.session.Parts.Work, self.part)

    def test_smaller_archive_replaces_and_truncates(self):
        self.part.content = os.urandom(2 * journal._CHUNK + 33)
        self.run_save()
        old_size = self.archive.stat().st_size
        self.part.content = b"small"
        self.run_save()
        self.assertLess(self.archive.stat().st_size, old_size)
        self.assert_archive(b"small")
        self.assert_only_outputs()

    def test_save_exception_preserves_existing_archive(self):
        self.run_save()
        before = self.archive.read_bytes()
        with mock.patch.object(self.part, "Save", side_effect=RuntimeError("NX save failure")):
            with self.assertRaisesRegex(RuntimeError, "NX save failure"):
                self.run_save()
        self.assertEqual(self.archive.read_bytes(), before)

    def test_failed_parts_or_objects_prevent_archive_and_dispose_status(self):
        for field in ("NumberUnsavedParts", "NumberUnsavedObjects"):
            with self.subTest(field=field):
                self.part.status = SaveStatus()
                setattr(self.part.status, field, 1)
                with self.assertRaisesRegex(RuntimeError, "NX"):
                    self.run_save()
                self.assertFalse(self.archive.exists())
                self.assertTrue(self.part.status.disposed)

    def test_status_read_failure_still_disposes(self):
        class BrokenStatus(SaveStatus):
            @property
            def NumberUnsavedParts(self):
                raise RuntimeError("cannot read save status")
        self.part.status = BrokenStatus()
        with self.assertRaisesRegex(RuntimeError, "cannot read"):
            self.run_save()
        self.assertTrue(self.part.status.disposed)
        self.assertFalse(self.archive.exists())

    def test_missing_save_status_is_not_success(self):
        self.part.status = None
        with self.assertRaises(RuntimeError):
            self.run_save()
        self.assertFalse(self.archive.exists())

    def test_still_modified_is_not_archived(self):
        with mock.patch.object(self.part, "Save", return_value=self.part.status):
            with self.assertRaisesRegex(RuntimeError, "изменённый"):
                self.run_save()
        self.assertTrue(self.part.status.disposed)
        self.assertFalse(self.archive.exists())

    def test_no_part_unnamed_unsaved_readonly_managed_paths(self):
        self.session.Parts.Work = None
        with self.assertRaises(RuntimeError):
            self.run_save()
        self.session.Parts.Work = self.part
        for path in ("", "Untitled.prt", "@DB/part/A", str(self.folder / "missing.prt")):
            with self.subTest(path=path):
                self.part.FullPath = path
                with self.assertRaises(RuntimeError):
                    self.run_save()
        self.part.FullPath = str(self.source)
        self.part.IsReadOnly = True
        with self.assertRaises(RuntimeError):
            self.run_save()
        self.assertEqual(self.part.save_calls, [])
        self.assertEqual(set(self.folder.iterdir()), {self.source})

    def test_path_changed_by_external_save_handler_is_rejected(self):
        def changed_save(*args):
            self.part.FullPath = str(self.folder / "different.prt")
            self.part.IsModified = False
            return self.part.status
        with mock.patch.object(self.part, "Save", side_effect=changed_save):
            with self.assertRaisesRegex(RuntimeError, "путь"):
                self.run_save()
        self.assertFalse(self.archive.exists())

    def test_empty_saved_prt_not_archived(self):
        self.part.content = b""
        with self.assertRaises(journal.ArchiveError):
            self.run_save()
        self.assertFalse(self.archive.exists())

    def test_external_component_is_saved_by_nx_but_not_in_archive(self):
        child = self.folder / "component.prt"
        child.write_bytes(b"component")
        self.run_save()
        self.assert_archive(self.part.content)
        self.assertEqual(child.read_bytes(), b"component")

    def test_unrelated_or_corrupt_zip_is_not_overwritten(self):
        with zipfile.ZipFile(self.archive, "w") as archive:
            archive.writestr("customer.txt", b"must keep")
        for before in (self.archive.read_bytes(), b"not a valid zip"):
            self.archive.write_bytes(before)
            with self.assertRaises(journal.ArchiveError):
                self.run_save()
            self.assertEqual(self.archive.read_bytes(), before)
            self.assertEqual(self.source.read_bytes(), self.part.content)

    def test_owned_zip_with_extra_entries_is_not_overwritten(self):
        self.run_save()
        with zipfile.ZipFile(self.archive, "a") as archive:
            archive.writestr("another-file.prt", b"other")
        before = self.archive.read_bytes()
        with self.assertRaises(journal.ArchiveError):
            self.run_save()
        self.assertEqual(self.archive.read_bytes(), before)

    def test_memory_exhaustion_preserves_old_archive(self):
        self.run_save()
        before = self.archive.read_bytes()
        with mock.patch.object(journal, "_build_zip", side_effect=MemoryError("RAM")):
            with self.assertRaises(journal.ArchiveError):
                self.run_save()
        self.assertEqual(self.archive.read_bytes(), before)

    def test_partial_write_restores_old_zip(self):
        self.run_save()
        before = self.archive.read_bytes()
        real_write = journal._write_all
        failed = False
        def write_once_failure(stream, data):
            nonlocal failed
            if not failed:
                failed = True
                stream.write(bytes(data[:16]))
                raise OSError("disk write failure")
            real_write(stream, data)
        self.part.content = b"new native save must survive archive failure"
        with mock.patch.object(journal, "_write_all", side_effect=write_once_failure):
            with self.assertRaisesRegex(journal.ArchiveError, "восстановлен"):
                self.run_save()
        self.assertEqual(self.archive.read_bytes(), before)
        self.assertEqual(self.source.read_bytes(), self.part.content)
        self.assert_only_outputs()
        self.run_save()
        self.assert_archive(self.part.content)

    def test_first_write_failure_leaves_no_zip_or_extra_file(self):
        with mock.patch.object(journal, "_write_all", side_effect=OSError("disk full")):
            with self.assertRaises(journal.ArchiveError):
                self.run_save()
        self.assertEqual(set(self.folder.iterdir()), {self.source})
        self.assertEqual(self.source.read_bytes(), self.part.content)

    def test_verification_failure_restores_previous_archive(self):
        self.run_save()
        before = self.archive.read_bytes()
        real_verify = journal._verify_zip
        def reject_disk(stream, *args):
            if not isinstance(stream, io.BytesIO):
                raise OSError("readback mismatch")
            return real_verify(stream, *args)
        with mock.patch.object(journal, "_verify_zip", side_effect=reject_disk):
            with self.assertRaisesRegex(journal.ArchiveError, "восстановлен"):
                self.run_save()
        self.assertEqual(self.archive.read_bytes(), before)

    def test_failed_restore_is_reported_and_never_touches_prt(self):
        self.run_save()
        self.part.content = b"latest model"
        with mock.patch.object(journal, "_replace_stream", side_effect=OSError("offline disk")):
            with self.assertRaisesRegex(journal.ArchiveError, "восстановить прежний ZIP не удалось"):
                self.run_save()
        self.assertEqual(self.source.read_bytes(), self.part.content)
        self.assert_only_outputs()

    def test_locked_archive_is_not_changed_and_handles_release(self):
        self.run_save()
        before = self.archive.read_bytes()
        locked, created = journal._open_locked(str(self.archive), writable=True)
        self.assertFalse(created)
        with locked:
            with self.assertRaises(journal.ArchiveError):
                self.run_save()
        self.assertEqual(self.archive.read_bytes(), before)
        self.run_save()
        self.assert_archive(self.part.content)

    def test_archive_hardlink_cannot_overwrite_original_or_other_file(self):
        other = self.folder / "other.txt"
        other.write_bytes(b"important unrelated file")
        try:
            os.link(other, self.archive)
        except OSError as error:
            self.skipTest(str(error))
        with self.assertRaises(journal.ArchiveError):
            self.run_save()
        self.assertEqual(other.read_bytes(), b"important unrelated file")

    def test_content_hash_detects_valid_zip_with_wrong_prt_data(self):
        self.run_save()
        with self.archive.open("rb") as archive:
            with self.assertRaises(OSError):
                journal._verify_zip(archive, self.source.name, len(self.part.content),
                                    hashlib.sha256(b"other model").digest())

    def test_author_caption_and_no_persistent_settings(self):
        self.assertEqual(journal._CREDIT, "by @Tony_Foxxx (TG)")
        source = (ROOT / "scripts/NX_Save_Project_And_Archive.py").read_text("utf-8")
        self.assertNotIn(journal._CREDIT, source)
        before = sys.dont_write_bytecode
        spec.loader.exec_module(journal)
        self.assertEqual(sys.dont_write_bytecode, before)


if __name__ == "__main__":
    unittest.main()
