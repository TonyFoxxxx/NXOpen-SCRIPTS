# -*- coding: utf-8 -*-
# Сохранение проекта и ZIP-архива — Siemens NX / Designcenter
# SCRIPT_VERSION: V1
SCRIPT_VERSION = "V1"
SCRIPT_NAME = "Сохранение проекта и архива"

# Штатный Save без смены имени/пути и без закрытия рабочей детали.
# Единственный дополнительный файл: <имя проекта>.zip рядом с .prt.
# В ZIP входит только рабочий .prt; внешние компоненты в него не входят.
# ZIP и резерв прежнего ZIP формируются только в оперативной памяти.
# Нет INI, журналов, временных файлов, обработчиков событий и UndoToMark.
# Без временного файла замена ZIP не атомарна при аварийном завершении.

import sys

_previous_bytecode = sys.dont_write_bytecode
try:
    sys.dont_write_bytecode = True
    import ctypes
    from ctypes import wintypes
    import hashlib
    import io
    import os
    import stat
    import time
    import zipfile
    if os.name == "nt":
        import msvcrt
    else:
        import fcntl
finally:
    sys.dont_write_bytecode = _previous_bytecode
    del _previous_bytecode

_CREDIT = bytes((98, 121, 32, 64, 84, 111, 110, 121, 95, 70, 111, 120, 120, 120, 32, 40, 84, 71, 41)).decode("ascii")
TITLE = SCRIPT_NAME + " — " + SCRIPT_VERSION + " | " + _CREDIT
_ZIP_MARKER = b"NX_Save_Project_And_Archive:1"
_CHUNK = 1024 * 1024


class ArchiveError(RuntimeError):
    """The native save succeeded, but the archive operation did not."""


def _open_locked(path, writable=False):
    """Open without truncation. On Windows deny concurrent writes/deletes.

    No lock files are used. Closing the returned stream releases the lock.
    The POSIX branch supports the portable test suite, not NX deployment.
    """
    created = False
    if os.name == "nt":
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        create = kernel.CreateFileW
        create.argtypes = (wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                           ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD,
                           wintypes.HANDLE)
        create.restype = wintypes.HANDLE
        close = kernel.CloseHandle
        close.argtypes = (wintypes.HANDLE,)
        close.restype = wintypes.BOOL
        access = 0x80000000 | (0x40000000 if writable else 0)
        sharing = 0 if writable else 1  # FILE_SHARE_READ for the source only.
        flags = 0x80 | 0x00200000  # NORMAL | OPEN_REPARSE_POINT
        handle = create(path, access, sharing, None, 3, flags, None)
        if handle == ctypes.c_void_p(-1).value:
            error = ctypes.get_last_error()
            if not writable or error != 2:  # ERROR_FILE_NOT_FOUND
                raise ctypes.WinError(error)
            handle = create(path, access, sharing, None, 1, flags, None)
            if handle == ctypes.c_void_p(-1).value:
                raise ctypes.WinError(ctypes.get_last_error())
            created = True
        try:
            fd = msvcrt.open_osfhandle(
                handle, os.O_BINARY | (os.O_RDWR if writable else os.O_RDONLY))
        except BaseException:
            close(handle)
            if created:
                os.unlink(path)
            raise
    else:
        flags = (os.O_RDWR if writable else os.O_RDONLY) | os.O_NOFOLLOW
        try:
            fd = os.open(path, flags)
        except FileNotFoundError:
            if not writable:
                raise
            fd = os.open(path, flags | os.O_CREAT | os.O_EXCL, 0o600)
            created = True
        try:
            fcntl.flock(fd, (fcntl.LOCK_EX if writable else fcntl.LOCK_SH)
                        | fcntl.LOCK_NB)
        except BaseException:
            os.close(fd)
            if created:
                os.unlink(path)
            raise
    try:
        return os.fdopen(fd, "r+b" if writable else "rb", buffering=0), created
    except BaseException:
        os.close(fd)
        if created:
            os.unlink(path)
        raise


def _regular_file(stream, single_link=False):
    info = os.fstat(stream.fileno())
    if (not stat.S_ISREG(info.st_mode)
            or getattr(info, "st_file_attributes", 0) & 0x400
            or (single_link and info.st_nlink != 1)):
        raise RuntimeError("Ожидался обычный файл, без ссылок и перенаправлений.")
    return info


def _write_all(stream, data):
    view = memoryview(data)
    while view:
        written = stream.write(view)
        if written is None or written <= 0:
            raise OSError("Не удалось полностью записать архив.")
        view = view[written:]


def _digest_stream(stream):
    stream.seek(0)
    digest = hashlib.sha256()
    while True:
        chunk = stream.read(_CHUNK)
        if not chunk:
            return digest.digest()
        digest.update(chunk)


def _check_owner(archive, member):
    entries = archive.infolist()
    if (archive.comment != _ZIP_MARKER or len(entries) != 1
            or entries[0].filename != member or entries[0].is_dir()):
        raise RuntimeError("Одноимённый ZIP не является архивом этого проекта, "
                           "созданным данным скриптом. Он не перезаписан.")


def _verify_zip(stream, member, size, digest):
    stream.seek(0)
    with zipfile.ZipFile(stream, "r") as archive:
        _check_owner(archive, member)
        if archive.getinfo(member).file_size != size:
            raise OSError("Размер файла внутри ZIP не совпадает с исходным.")
        actual = hashlib.sha256()
        with archive.open(member, "r") as item:
            while True:
                chunk = item.read(_CHUNK)  # ZipExtFile also checks CRC at EOF.
                if not chunk:
                    break
                actual.update(chunk)
        if actual.digest() != digest:
            raise OSError("Содержимое ZIP не совпадает с сохранённым проектом.")


def _build_zip(source, member):
    before = _regular_file(source)
    if before.st_size == 0:
        raise RuntimeError("Сохранённый .prt пуст. Архив не изменён.")
    stamp = time.localtime(before.st_mtime)[:6]
    if not 1980 <= stamp[0] <= 2107:
        stamp = (1980, 1, 1, 0, 0, 0)
    info = zipfile.ZipInfo(member, stamp)
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = (stat.S_IFREG | 0o644) << 16
    payload = io.BytesIO()
    digest = hashlib.sha256()
    total = 0
    try:
        with zipfile.ZipFile(payload, "w", compression=zipfile.ZIP_DEFLATED,
                             compresslevel=6, allowZip64=True) as archive:
            archive.comment = _ZIP_MARKER
            with archive.open(info, "w", force_zip64=True) as item:
                while True:
                    chunk = source.read(_CHUNK)
                    if not chunk:
                        break
                    item.write(chunk)
                    digest.update(chunk)
                    total += len(chunk)
        after = os.fstat(source.fileno())
        if (total != before.st_size or after.st_size != before.st_size
                or after.st_mtime_ns != before.st_mtime_ns):
            raise RuntimeError("Файл проекта изменился во время чтения. Архив не изменён.")
        checksum = digest.digest()
        _verify_zip(payload, member, total, checksum)
        payload.seek(0)
        return payload, total, checksum
    except BaseException:
        payload.close()
        raise


def _replace_stream(target, payload):
    payload.seek(0)
    target.seek(0)
    while True:
        chunk = payload.read(_CHUNK)
        if not chunk:
            break
        _write_all(target, chunk)
    target.truncate()
    target.flush()
    os.fsync(target.fileno())


def _store_zip(path, payload, member, size, checksum):
    """Overwrite only this journal's ZIP; keep the old ZIP in RAM for rollback.

    This deliberately uses no disk staging file. Power/process loss during
    the overwrite cannot be rolled back; only catchable failures can be.
    """
    target, created = _open_locked(path, writable=True)
    previous = None
    failed_new = False
    try:
        with target:
            identity = _regular_file(target, single_link=True)
            if not created:
                with zipfile.ZipFile(target, "r") as archive:
                    _check_owner(archive, member)
                target.seek(0)
                previous = io.BytesIO(target.read())
            try:
                _replace_stream(target, payload)
                _verify_zip(target, member, size, checksum)
            except BaseException as error:
                if previous is not None:
                    try:
                        _replace_stream(target, previous)
                        if _digest_stream(target) != _digest_stream(previous):
                            raise OSError("Не совпала проверка восстановленного ZIP.")
                    except BaseException as restore_error:
                        raise ArchiveError(
                            "Ошибка записи ZIP; восстановить прежний ZIP не удалось. "
                            "Архив может быть повреждён. Исходный .prt не затронут.\n\n"
                            + str(error) + "\nВосстановление: " + str(restore_error)) from error
                    raise ArchiveError("Ошибка записи ZIP. Прежний архив восстановлен.\n\n"
                                       + str(error)) from error
                failed_new = True
                raise ArchiveError("Создать ZIP не удалось.\n\n" + str(error)) from error
    finally:
        if previous is not None:
            previous.close()
        if failed_new:
            # Remove only the file created by this call, never a replacement
            # installed at the same path by another process after close.
            try:
                current = os.lstat(path)
                if ((current.st_dev, current.st_ino)
                        != (identity.st_dev, identity.st_ino)):
                    raise OSError("Файл ZIP по этому пути уже заменён другим процессом.")
                os.unlink(path)
            except OSError as cleanup_error:
                raise ArchiveError("Не удалось записать и удалить незавершённый ZIP. "
                                   "Проверьте этот архив:\n" + path + "\n\n"
                                   + str(cleanup_error)) from cleanup_error


def _save_part(nx, part):
    status = part.Save(nx.BasePart.SaveComponents.TrueValue,
                       nx.BasePart.CloseAfterSave.FalseValue)
    if status is None:
        raise RuntimeError("NX не вернул результат сохранения. Архив не изменён.")
    try:
        part_count = int(status.NumberUnsavedParts)
        object_count = int(status.NumberUnsavedObjects)
        if part_count or object_count:
            lines = ["NX не сохранил все данные. Архив не изменён."]
            for index in range(min(part_count, 10)):
                item = status.GetPart(index)
                name = str(item.FullPath) if item is not None else "Деталь"
                lines.append(name + ": код " + str(status.GetStatus(index)))
            for index in range(min(object_count, 10)):
                lines.append("Объект: код " + str(status.GetObjectStatus(index)))
            raise RuntimeError("\n".join(lines))
    finally:
        status.Dispose()
    if part.IsModified:
        raise RuntimeError("После сохранения NX всё ещё отмечает проект как изменённый. "
                           "Архив не изменён.")


def save_project(nx, session=None):
    """Save the existing work part, then archive its saved on-disk bytes."""
    if session is None:
        session = nx.Session.GetSession()
    part = session.Parts.Work
    if part is None:
        raise RuntimeError("Сначала откройте проект в NX.")
    path = str(part.FullPath or "")
    if (not path or not os.path.isabs(path)
            or os.path.splitext(path)[1].lower() != ".prt"
            or not os.path.isfile(path)):
        raise RuntimeError("Сначала сохраните проект штатной командой NX в файл .prt. "
                           "Этот скрипт работает с уже сохранённым файлом.")
    if part.IsReadOnly:
        raise RuntimeError("Проект открыт только для чтения. Сохранение не выполнено.")
    part_tag = part.Tag
    _save_part(nx, part)
    if (str(part.FullPath) != path or session.Parts.Work is None
            or session.Parts.Work.Tag != part_tag):
        raise RuntimeError("Во время сохранения изменился путь или рабочая деталь. "
                           "Архив не изменён.")
    archive_path = os.path.splitext(path)[0] + ".zip"
    try:
        source, _ = _open_locked(path)
        with source:
            payload, size, checksum = _build_zip(source, os.path.basename(path))
            with payload:
                _store_zip(archive_path, payload, os.path.basename(path), size, checksum)
    except ArchiveError:
        raise
    except Exception as error:
        raise ArchiveError("Архив не обновлён.\n\n" + str(error)) from error
    return path, archive_path


def _message(text, kind="info"):
    # Native caption includes the version. No log or report file is created.
    user32 = ctypes.windll.user32
    user32.GetActiveWindow.restype = ctypes.c_void_p
    user32.MessageBoxW.argtypes = (ctypes.c_void_p, ctypes.c_wchar_p,
                                  ctypes.c_wchar_p, ctypes.c_uint)
    user32.MessageBoxW.restype = ctypes.c_int
    icon = {"info": 0x40, "warning": 0x30, "error": 0x10}[kind]
    user32.MessageBoxW(user32.GetActiveWindow(), str(text), TITLE, icon | 0x10000)


def main():
    if os.name != "nt":
        print("Запускайте журнал внутри Siemens NX / Designcenter на Windows.")
        return
    try:
        before = sys.dont_write_bytecode
        try:
            sys.dont_write_bytecode = True
            import NXOpen
        finally:
            sys.dont_write_bytecode = before
        path, archive_path = save_project(NXOpen)
    except ArchiveError as error:
        _message("Проект сохранён штатной командой NX.\n\n" + str(error), "warning")
    except Exception as error:
        _message("Сохранение не подтверждено. Архив не изменён.\n\n" + str(error), "error")
    else:
        _message("Проект сохранён:\n" + path + "\n\nАрхив обновлён:\n" + archive_path)


if __name__ == "__main__":
    main()
