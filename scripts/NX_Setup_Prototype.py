# -*- coding: utf-8 -*-
# Карта наладки
# SCRIPT_VERSION: V2.49
# Рабочее имя файла: NX_Setup_Prototype.py
"""Карта наладки — виды MCS и операции.

Запускать внутри NX: Журнал / Воспроизвести (Journal / Play).
После запуска отметьте папки установов в дереве Program Order.
Для выбора установов выделение в NX не используется. Родитель и потомок взаимоисключаются.
В стартовом меню можно включить нумерацию операций, Description инструментов и Zmin в именах.
Все три опции по умолчанию выключены. Zmin использует выделение до открытия меню
или все операции проекта, если выделение пустое, как отдельный журнал Zmin.
Каждый ракурс вписывается по геометрии IPW и видимой оснастки и сразу снимается.
Одна выбранная папка — один установ со всеми её операциями и вложенными папками.
Имя, ракурсы и IPW — от MCS первой операции внутри выбранной папки.
Таблицы карты содержат выбранные операции, независимо от источника IPW.
Zmin — минимум рассчитанной траектории по фактической оси ToolAxis каждого движения,
относительно начала СКС операции, в единицах CAM-детали.
При поворотной обработке начало СКС должно лежать на оси вращения детали.
Для отсутствующей, устаревшей или недоступной траектории поле Zmin пустое.
Свободное поле — не менее 5% ширины/высоты окна с каждой стороны.
Пробных снимков и пересъёмок нет: один снимок с IPW и один без IPW на ракурс.
Проверка полей и обрезка используют одно декодирование каждого PNG.
Вспомогательная геометрия IPW скрыта на снимках и удаляется после съёмки.
Ракурсы без IPW и с IPW всегда используют одинаковую камеру.
Оси на детали привязаны к началу MCS; их размер меняется ползунком в HTML.
Проекция снимков переключается автоматически.
Одно окно содержит все выбранные установы и отдельный набор компонентов для каждого.
Для каждого установа отметьте видимые компоненты детали и оснастки в списке.
Отметки сразу показывают выбранные компоненты активного установа в NX.
Исходная видимость восстанавливается после окна выбора и каждого установа.

Требования: интерактивная сессия NX CAM с NXOpen Python.
Отдельный Python и пакеты устанавливать не надо.
Геометрия и параметры обработки не редактируются; .prt автоматически не сохраняется.
Только явно включённые опции меняют имена операций и Description инструментов в NX.
При отмене или ошибке до записи HTML эти изменения отменяются; после записи доступны через Ctrl+Z в NX.
HTML лежит в Карты Наладки / имя текущего .prt без расширения.
Название установа — полное имя СКС первой операции. Все установы в одном HTML.
Повторный экспорт заменяет только выбранные установы в сохранённом HTML.
При обновлении папки прежние отдельные записи её подпапок объединяются в неё.
Остальные установы и сохранённые ручные правки переносятся без пересоздания.
«Инструменты проекта» — всегда один первый лист, при необходимости в две колонки.
Если двух колонок недостаточно, таблица равномерно уменьшается до размеров листа.
Фасетная 3D-модель первого листа включается галочкой в стартовом меню.
По умолчанию галочка снята; после включения доступны коэффициент 0 < k ≤ 1 и лимит треугольников.
Без галочки текущий ракурс вписывается в окно NX и снимается для первого листа.
При повторном выводе снимок заменяет прежнюю модель, а модель — прежний снимок.
Мышь вращает модель; Shift + мышь сдвигает; колесо меняет масштаб.
Сетка, выбранный ракурс и изображение для печати хранятся в самом HTML.
При k=1 допуски равны 0.01 мм и 3°; при уменьшении k допуски делятся на k.
Угловой допуск ограничен 180°; предел всей модели задаётся в меню, по умолчанию 500 000.
Модель перемещается по первому листу; таблицы адаптируются к её габаритам.
Масштаб 1–10000%; на печати границей остаётся формат A4.
Порядок и сквозная нумерация сохраняются при сохранении и повторном экспорте.
Результат — один автономный HTML для редактирования и печати.
Карта открывается через встроенный локальный помощник и сохраняет правки автоматически.
Помощник работает отдельно от NX, без NXOpen и служебных файлов.
Если отдельного python.exe нет, используется уже загруженная DLL Python NX
в отдельном процессе штатного Windows PowerShell, без установки Python.
До открытия браузера проверяется его ответ; при недоступности открывается обычный HTML.
При открытии HTML напрямую остаётся прежнее сохранение средствами браузера.
Во время создания карты отображаются текущий этап и полоса прогресса 0–100%.
При повторном экспорте готовый HTML заменяет предыдущий файл без копий.
Отчёты и диагностические файлы не создаются; снимки встраиваются в HTML.
Временно меняется ракурс; восстановление выполняется в finally.
"""

import base64
import builtins
import configparser
from contextlib import contextmanager
import copy
import datetime
from decimal import Decimal, InvalidOperation, ROUND_HALF_UP
import hashlib
import html
import http.client
from html.parser import HTMLParser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import math
import ntpath
import os
import shutil
from pathlib import Path
import queue
import re
import struct
import subprocess
import sys
import time
import tempfile
import threading
import uuid
import zlib


SCRIPT_VERSION = "V2.49"
SCRIPT_NAME = "Карта наладки"
SCRIPT_AUTHOR = bytes(value ^ ((0x5D + index * 11) & 0xFF)
                      for index, value in enumerate((63, 17, 83, 62, 221, 251, 241, 211, 234, 134, 164, 174, 153, 148, 215, 42, 89, 95, 10))).decode("utf-8")
TITLE = SCRIPT_NAME + " — " + SCRIPT_VERSION + " — " + SCRIPT_AUTHOR
DEFAULT_PROGRAMMER = ''
SETTINGS_FILENAME = 'NX_Setup_Prototype.ini'

# Installed only in memory by the separate diagnostic launcher. Normal runs
# never open a diagnostic file or start a watchdog.
DIAGNOSTIC_CALLBACK = None
EXPORT_PROGRESS = None


def display_file_name(path):
    """Keep Win32 namespace prefixes out of UI, clipboard and shell paths."""
    value = os.fspath(path)
    if value[:8].upper() == '\\\\?\\UNC\\':
        return '\\\\' + value[8:]
    if value.startswith('\\\\?\\') and re.match(r'[A-Za-z]:\\', value[4:]):
        return value[4:]
    return value


def windows_io_name(path):
    """An absolute DOS/UNC path in the extended Unicode namespace; no OS changes."""
    value = ntpath.normpath(display_file_name(path))
    drive, tail = ntpath.splitdrive(value)
    if value.startswith(('\\\\.\\', '\\\\?\\')):
        raise ValueError('Путь к устройству Windows не является путём к файлу проекта.')
    share = drive[2:].split('\\') if drive.startswith('\\\\') else []
    if len(share) == 2 and all(share):
        result = '\\\\?\\UNC\\' + value[2:]
    elif re.fullmatch(r'[A-Za-z]:', drive) and tail.startswith('\\'):
        result = '\\\\?\\' + value
    else:
        raise ValueError('Требуется абсолютный путь к файлу проекта или UNC-путь.')
    if len(result.encode('utf-16-le')) // 2 >= 32760:
        raise ValueError('Путь превышает предел расширенного пути Windows.')
    return result


def io_path(path):
    if os.name != 'nt':
        return Path(path)
    return Path(windows_io_name(os.path.abspath(display_file_name(path))))


def windows_short_name(path):
    """Read existing 8.3 aliases only; never enable them or create a mapping."""
    if os.name != 'nt':
        return display_file_name(path)
    import ctypes
    from ctypes import wintypes
    function = ctypes.WinDLL('kernel32', use_last_error=True).GetShortPathNameW
    function.argtypes = [wintypes.LPCWSTR, wintypes.LPWSTR, wintypes.DWORD]
    function.restype = wintypes.DWORD
    source = str(io_path(path))
    size = function(source, None, 0)
    if not size:
        raise ctypes.WinError(ctypes.get_last_error())
    for attempt in range(2):
        buffer = ctypes.create_unicode_buffer(size)
        length = function(source, buffer, size)
        if not length:
            raise ctypes.WinError(ctypes.get_last_error())
        if length < size:
            return display_file_name(buffer.value)
        size = length + 1
    raise OSError('Не удалось получить короткий путь Windows.')


def nx_image_file_name(path):
    """NX receives an ordinary short path, never an unverified namespace form."""
    path = io_path(path)
    value = display_file_name(path)
    if os.name != 'nt' or len(value.encode('utf-16-le')) // 2 < 260:
        return value
    try:
        value = ntpath.join(windows_short_name(path.parent), path.name)
    except OSError:
        pass
    if len(value.encode('utf-16-le')) // 2 >= 260:
        raise RuntimeError('Папка самого проекта слишком глубоко вложена для безопасного экспорта '
                           'изображений NX; короткий путь Windows недоступен.\n'
                           'Сохраните проект средствами NX в папке с более коротким путём и повторите запуск.\n'
                           'Имена проекта и готовой карты скрипт не меняет.\n' + display_file_name(path))
    return value


def local_card_runtime():
    """Keep one helper across Journal / Play repeats, without files or NX callbacks."""
    key = '_nx_setup_card_local_runtime_v1'
    runtime = getattr(builtins, key, None)
    if runtime is None:
        runtime = {'lock': threading.RLock(), 'helper': None}
        setattr(builtins, key, runtime)
    return runtime


@contextmanager
def local_card_guard():
    """Share export/read exclusion with the independent helper, without a lock file."""
    runtime = local_card_runtime()
    with runtime['lock']:
        helper = runtime['helper']
        remote = getattr(helper, 'external_protocol', None) == 1
        if remote and helper.process.poll() is not None:
            runtime['helper'] = helper = None
            remote = False
        try:
            if remote:
                helper.request('lock')
            yield
        finally:
            if remote:
                # Also enqueue unlock after a timeout: a late lock reply must
                # not leave the helper permanently blocked.
                try:
                    helper.request('unlock')
                except Exception:
                    if helper.process.poll() is None:
                        raise
                    # A terminated process cannot race with the published file.
                    runtime['helper'] = None


def local_card_identity(source):
    """Read only the small metadata prefix, not the possibly large 3D model."""
    class Metadata(HTMLParser):
        def __init__(self):
            super().__init__(convert_charrefs=True)
            self.values = {}

        def handle_starttag(self, tag, attrs):
            values = dict(attrs)
            name = values.get('name', '')
            if tag == 'meta' and name in ('nx-card-id', 'nx-card-revision', 'nx-card-format'):
                if name in self.values:
                    raise ValueError('Повторяется служебное поле HTML.')
                self.values[name] = values.get('content', '')

    parser = Metadata()
    parser.feed(source[:8192])
    values = parser.values
    ident, revision = values.get('nx-card-id', ''), values.get('nx-card-revision', '')
    if (not re.fullmatch(r'[A-Za-z0-9_-]{1,128}', ident) or
            not re.fullmatch(r'[a-f0-9]{32}', revision) or
            values.get('nx-card-format') != 'setup-sections-v1'):
        raise ValueError('Не найдены идентификатор и редакция карты наладки.')
    return ident, revision


def replace_local_card_contents(path, payload, previous):
    """Keep the same file; roll back write errors from memory, without sidecars.

    The caller holds the shared export/save lock. Unlike the export's existing
    staging/replace operation, this in-place write is not power-failure atomic.
    """
    with io_path(path).open('r+b') as stream:
        try:
            if stream.write(payload) != len(payload):
                raise OSError('Неполная запись HTML.')
            stream.truncate()
            stream.flush()
            os.fsync(stream.fileno())
        except Exception:
            stream.seek(0)
            stream.write(previous)
            stream.truncate()
            stream.flush()
            os.fsync(stream.fileno())
            raise


class LocalCardServer(ThreadingHTTPServer):
    daemon_threads = True
    block_on_close = False
    allow_reuse_address = False
    request_queue_size = 8

    def __init__(self, helper):
        self.helper = helper
        self.slots = threading.BoundedSemaphore(8)
        super().__init__(('127.0.0.1', 0), LocalCardRequest)

    def get_request(self):
        connection, address = super().get_request()
        connection.settimeout(30)
        return connection, address

    def verify_request(self, request, address):
        return address[0] == '127.0.0.1'

    def process_request(self, request, address):
        if not self.slots.acquire(blocking=False):
            self.shutdown_request(request)
            return
        try:
            super().process_request(request, address)
        except Exception:
            self.slots.release()
            raise

    def process_request_thread(self, request, address):
        try:
            super().process_request_thread(request, address)
        finally:
            self.slots.release()

    def handle_error(self, request, address):
        pass  # No console tracebacks or diagnostic files in normal operation.


class LocalCardRequest(BaseHTTPRequestHandler):
    """Same-origin loopback only; the client cannot supply a destination path."""
    def log_message(self, *args):
        pass

    def reply(self, status, payload=b'', content_type='application/json; charset=utf-8'):
        if not isinstance(payload, bytes):
            payload = json.dumps(payload, ensure_ascii=False).encode('utf-8')
        self.send_response(status)
        self.send_header('Content-Type', content_type)
        self.send_header('Content-Length', str(len(payload)))
        self.send_header('Cache-Control', 'no-store')
        self.send_header('Referrer-Policy', 'no-referrer')
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.send_header('X-Frame-Options', 'DENY')
        self.send_header('Content-Security-Policy', "frame-ancestors 'none'")
        self.send_header('Connection', 'close')
        self.end_headers()
        self.close_connection = True
        if self.command != 'HEAD':
            self.wfile.write(payload)

    def send_error(self, code, message=None, explain=None):
        self.reply(code, {'error': 'Запрос отклонён.'})

    def entry(self, saving=False):
        helper = self.server.helper
        if (self.headers.get_all('Host', []) != [helper.authority] or
                self.headers.get('Sec-Fetch-Site') == 'cross-site'):
            self.send_error(403)
            return None
        route = self.path[:-4] if saving and self.path.endswith('save') else self.path
        with helper.lock:
            entry = helper.entries.get(route)
        if entry is None or (saving and self.path != route + 'save'):
            self.send_error(404)
            return None
        if saving and (self.headers.get_all('Origin', []) != [helper.origin] or
                       self.headers.get_all('X-NX-Card-Key', []) != [entry['key']]):
            self.send_error(403)
            return None
        return entry

    def do_GET(self):
        entry = self.entry()
        if entry is None:
            return
        try:
            with self.server.helper.lock:
                source = io_path(entry['path']).read_text(encoding='utf-8-sig')
            if local_card_identity(source)[0] != entry['id']:
                self.send_error(409)
                return
            session = json.dumps({'protocol': 1, 'id': entry['id'], 'key': entry['key'],
                                  'save': entry['route'] + 'save'}, separators=(',', ':'))
            session = '<script id="nx-local-session" type="application/json">' + session + '</script>'
            source, count = re.subn(r'(<body\b[^>]*>)', lambda match: match[0] + session,
                                   source, count=1, flags=re.I)
            if count != 1:
                raise ValueError('Не найдено содержимое HTML.')
            self.reply(200, source.encode('utf-8'), 'text/html; charset=utf-8')
        except FileNotFoundError:
            self.send_error(404)
        except (OSError, ValueError):
            self.send_error(500)

    do_HEAD = do_GET

    def do_POST(self):
        entry = self.entry(saving=True)
        if entry is None:
            return
        try:
            lengths = self.headers.get_all('Content-Length', [])
            if (len(lengths) != 1 or not lengths[0].isdigit() or
                    self.headers.get('Transfer-Encoding') is not None or
                    self.headers.get('Content-Type') != 'text/html; charset=utf-8'):
                self.send_error(400)
                return
            length = int(lengths[0])
            if not 0 < length <= 256 * 1024 * 1024:
                self.send_error(413)
                return
            previous = self.headers.get('X-NX-Card-Revision', '')
            following = self.headers.get('X-NX-Card-Next', '')
            if (not re.fullmatch(r'[a-f0-9]{32}', previous) or
                    not re.fullmatch(r'[a-f0-9]{32}', following) or previous == following):
                self.send_error(400)
                return
            payload = self.rfile.read(length)
            if len(payload) != length:
                self.send_error(400)
                return
            source = payload.decode('utf-8')
            if (local_card_identity(source) != (entry['id'], following) or
                    not source.lstrip().lower().startswith('<!doctype html>') or
                    not source.rstrip().lower().endswith('</html>') or
                    re.search(r'<script\b[^>]*\bid=[\'"]nx-local-session[\'"]', source, re.I)):
                self.send_error(422)
                return
            with self.server.helper.lock:
                if not entry.get('active', True):
                    self.send_error(409)
                    return
                current = io_path(entry['path']).read_bytes()
                ident, revision = local_card_identity(current.decode('utf-8-sig'))
                # Retrying the same bytes after a lost response is safe. Never
                # retry an old snapshot over a different, newer export/save.
                if ident != entry['id'] or (revision != previous and current != payload):
                    self.send_error(409)
                    return
                if current != payload:
                    replace_local_card_contents(entry['path'], payload, current)
            self.reply(200, {'id': entry['id'], 'revision': following})
        except FileNotFoundError:
            self.send_error(404)
        except (UnicodeError, ValueError):
            self.send_error(422)
        except OSError:
            self.send_error(500)


class LocalCardHelper:
    """File IO only on daemon threads. Never call NXOpen outside its UI thread."""
    def __init__(self, lock):
        self.lock, self.entries, self.paths = lock, {}, {}
        self.server = LocalCardServer(self)
        self.authority = '127.0.0.1:%d' % self.server.server_address[1]
        self.origin = 'http://' + self.authority
        self.thread = threading.Thread(target=self.server.serve_forever,
                                       name='NX setup card save', daemon=True)
        try:
            self.thread.start()
        except Exception:
            self.server.server_close()
            raise

    def register(self, path):
        path = io_path(io_path(path).resolve(strict=True))
        if path.suffix.lower() not in ('.html', '.htm') or not path.is_file():
            raise ValueError('Не найден готовый HTML карты.')
        with self.lock:
            ident, _ = local_card_identity(path.read_text(encoding='utf-8-sig'))
            key = os.path.normcase(display_file_name(path))
            entry = self.paths.get(key)
            if entry is None or entry['id'] != ident:
                if entry is not None:
                    self.entries.pop(entry['route'], None)
                route = '/card/' + uuid.uuid4().hex + '/'
                entry = {'path': path, 'id': ident, 'route': route,
                         'key': uuid.uuid4().hex + uuid.uuid4().hex}
                self.paths[key], self.entries[route] = entry, entry
            return self.origin + entry['route']


def helper_python_candidates():
    """Use an existing runtime only; never install Python or launch ugraf.exe."""
    candidates, folders = [], []
    if re.fullmatch(r'python(?:[0-9.]+)?(?:\.exe)?', Path(sys.executable).name, re.I):
        candidates.append(Path(sys.executable))
    for value in (sys.prefix, sys.base_prefix, os.environ.get('UGII_PYTHON_LIBRARY_DIR')):
        if value:
            folders.append(Path(value.strip('"')))
    module_file = getattr(os, '__file__', '')
    if module_file:
        folders.append(Path(module_file).parent.parent)
    for variable in ('UGII_BASE_DIR', 'UGII_ROOT_DIR'):
        value = os.environ.get(variable)
        if value:
            base = Path(value.strip('"'))
            folders.extend((base, base / 'python', base / 'nxbin' / 'python', base / 'UGII' / 'python'))
    for folder in folders:
        candidates.extend(folder / name for name in ('python.exe', 'python3.exe'))
    seen = set()
    for path in candidates:
        key = os.path.normcase(os.path.abspath(path))
        if key not in seen:
            seen.add(key)
            try:
                if io_path(path).is_file():
                    yield path
            except OSError:
                continue


EMBEDDED_PYTHON_BOOTSTRAP = r'''
$ErrorActionPreference = 'Stop'
try {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $encoded = [Environment]::GetEnvironmentVariable('NX_SETUP_CARD_BOOT', 'Process')
    [Environment]::SetEnvironmentVariable('NX_SETUP_CARD_BOOT', $null, 'Process')
    $data = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded)).Split([char]0)
    if ($data.Length -ne 3) { throw 'Invalid Python helper configuration.' }
    $assembly = [AppDomain]::CurrentDomain.DefineDynamicAssembly(
        [Reflection.AssemblyName]::new('NxCardPython'), [Reflection.Emit.AssemblyBuilderAccess]::Run)
    $module = $assembly.DefineDynamicModule('Runtime', $false)
    $type = $module.DefineType('NxCardPython', [Reflection.TypeAttributes]'Public,Sealed,Abstract')
    $flags = [Reflection.MethodAttributes]'Public,Static,PinvokeImpl'
    $standard = [Reflection.CallingConventions]::Standard
    $cdecl = [Runtime.InteropServices.CallingConvention]::Cdecl
    $unicode = [Runtime.InteropServices.CharSet]::Unicode
    $method = $type.DefinePInvokeMethod('SetPath', $data[0], 'Py_SetPath', $flags,
        $standard, [void], [Type[]]@([IntPtr]), $cdecl, $unicode)
    $method.SetImplementationFlags([Reflection.MethodImplAttributes]::PreserveSig)
    $method = $type.DefinePInvokeMethod('Main', $data[0], 'Py_Main', $flags,
        $standard, [int], [Type[]]@([int], [IntPtr]), $cdecl, $unicode)
    $method.SetImplementationFlags([Reflection.MethodImplAttributes]::PreserveSig)
    $native = $type.CreateType()
    $arguments = @('nx-card-helper', '-I', '-S', '-B', '-u', '-X', 'utf8', $data[2], '--nx-card-helper')
    $allocations = [Collections.Generic.List[IntPtr]]::new()
    try {
        $path = [Runtime.InteropServices.Marshal]::StringToHGlobalUni($data[1])
        $allocations.Add($path)
        $argv = [Runtime.InteropServices.Marshal]::AllocHGlobal([IntPtr]::Size * ($arguments.Count + 1))
        $allocations.Add($argv)
        for ($i = 0; $i -lt $arguments.Count; $i++) {
            $value = [Runtime.InteropServices.Marshal]::StringToHGlobalUni($arguments[$i])
            $allocations.Add($value)
            [Runtime.InteropServices.Marshal]::WriteIntPtr($argv, $i * [IntPtr]::Size, $value)
        }
        [Runtime.InteropServices.Marshal]::WriteIntPtr($argv, $arguments.Count * [IntPtr]::Size, [IntPtr]::Zero)
        $null = $native.GetMethod('SetPath').Invoke($null, [object[]]@($path))
        $result = $native.GetMethod('Main').Invoke($null, [object[]]@([int]$arguments.Count, $argv))
    }
    finally {
        foreach ($allocation in $allocations) { [Runtime.InteropServices.Marshal]::FreeHGlobal($allocation) }
    }
    exit ([int]$result)
}
catch {
    $error64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($_.Exception.ToString()))
    [Console]::WriteLine('{"error64":"' + $error64 + '"}')
    exit 1
}
'''


def embedded_python_runtime():
    """Locate the already loaded interpreter DLL; never initialize Python in NX."""
    if os.name != 'nt':
        raise RuntimeError('Запуск встроенного Python без python.exe доступен только в Windows.')
    import ctypes
    from ctypes import wintypes
    for name in ('Py_Main', 'Py_SetPath'):
        if not hasattr(ctypes.pythonapi, name):
            raise RuntimeError('Встроенный Python NX не предоставляет ' + name)
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.GetModuleFileNameW.argtypes = [wintypes.HMODULE, wintypes.LPWSTR, wintypes.DWORD]
    kernel.GetModuleFileNameW.restype = wintypes.DWORD
    kernel.GetSystemDirectoryW.argtypes = [wintypes.LPWSTR, wintypes.UINT]
    kernel.GetSystemDirectoryW.restype = wintypes.UINT
    buffer = ctypes.create_unicode_buffer(32768)
    count = kernel.GetModuleFileNameW(ctypes.pythonapi._handle, buffer, len(buffer))
    if not 0 < count < len(buffer):
        raise RuntimeError('Не удалось определить DLL встроенного Python NX.')
    dll = io_path(buffer.value)
    if dll.suffix.lower() != '.dll' or not dll.is_file():
        raise RuntimeError('DLL встроенного Python NX недоступна: ' + display_file_name(dll))
    count = kernel.GetSystemDirectoryW(buffer, len(buffer))
    if not 0 < count < len(buffer):
        raise RuntimeError('Не удалось определить системную папку Windows.')
    powershell = io_path(buffer.value) / 'WindowsPowerShell' / 'v1.0' / 'powershell.exe'
    if not powershell.is_file():
        raise RuntimeError('Штатный Windows PowerShell недоступен.')
    # Start with verified locations of stdlib modules. Keep absolute NX runtime
    # paths, including zip libraries and extension modules, without adding cwd.
    import encodings
    paths, seen = [], set()
    def add(value):
        if not isinstance(value, str) or not value or not Path(value).is_absolute():
            return
        value = display_file_name(io_path(value))
        if ';' in value or '\0' in value:
            raise RuntimeError('Недопустимый путь библиотеки встроенного Python.')
        key = os.path.normcase(value)
        if key not in seen:
            seen.add(key)
            paths.append(value)
    for module, depth in ((encodings, 2), (json, 2), (os, 1), (zlib, 1), (math, 1)):
        location = getattr(module, '__file__', '')
        if location:
            path = Path(location)
            for _ in range(depth):
                path = path.parent
            add(str(path))
    excluded = {os.path.normcase(display_file_name(io_path(os.getcwd()))),
                os.path.normcase(display_file_name(io_path(__file__).parent))}
    for entry in sys.path:
        if isinstance(entry, str) and entry and Path(entry).is_absolute():
            if os.path.normcase(display_file_name(io_path(entry))) not in excluded:
                add(entry)
    if not paths:
        raise RuntimeError('Не найдены библиотеки встроенного Python NX.')
    return powershell, dll, paths


def embedded_python_launch():
    """An in-memory host for the same worker, without PS1/EXE/DLL output files."""
    powershell, dll, paths = embedded_python_runtime()
    # Data is passed separately from PowerShell code: paths are never commands.
    payload = '\0'.join((display_file_name(dll), ';'.join(paths), str(io_path(__file__))))
    encoded = base64.b64encode(payload.encode('utf-8')).decode('ascii')
    if len(encoded) > 30000:
        raise RuntimeError('Слишком длинная конфигурация библиотек Python NX для запуска помощника.')
    environment = os.environ.copy()
    environment['NX_SETUP_CARD_BOOT'] = encoded
    command = [display_file_name(powershell), '-NoLogo', '-NoProfile', '-NonInteractive',
               '-Command', EMBEDDED_PYTHON_BOOTSTRAP]
    return command, environment


class RemoteCardHelper:
    """Control the file-only server through private pipes, not NX Python threads."""
    # Journal replay creates new Python classes; isinstance is not stable then.
    external_protocol = 1

    def __init__(self, executable=None):
        self.serial, self.responses, self.startup_errors = 0, queue.Queue(), ''
        self.control_lock = threading.RLock()
        if executable is None:
            command, environment = embedded_python_launch()
        else:
            command = [str(executable), '-I', '-S', '-B', str(io_path(__file__)), '--nx-card-helper']
            environment = None
        self.process = subprocess.Popen(
            command, env=environment,
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            encoding='utf-8', errors='replace', bufsize=1, close_fds=True,
            creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))

        def receive():
            try:
                for line in self.process.stdout:
                    self.responses.put(json.loads(line))
            except Exception:
                pass
            finally:
                self.responses.put({'error': 'Локальный помощник завершил работу.'})
        self.reader = threading.Thread(target=receive, name='NX card helper replies', daemon=True)
        def receive_errors():
            try:
                for line in self.process.stderr:
                    self.startup_errors = (self.startup_errors + line)[-4000:]
            except Exception:
                pass
        self.error_reader = threading.Thread(target=receive_errors, name='NX card helper startup', daemon=True)
        try:
            self.reader.start()
            self.error_reader.start()
            hello = self.responses.get(timeout=10 if executable is None else 5)
            if hello.get('error64'):
                detail = base64.b64decode(hello['error64']).decode('utf-8', errors='replace')
                raise RuntimeError('Не запустился встроенный Python NX: ' + detail[:1600])
            if hello.get('ready') != SCRIPT_VERSION:
                raise RuntimeError('Не запустился совместимый Python-помощник.')
        except Exception as exc:
            self.process.stdin.close()  # No cards registered: startup can be stopped safely.
            try:
                self.process.wait(timeout=2)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=2)
            if self.reader.ident is not None:
                self.reader.join(timeout=2)
            if self.error_reader.ident is not None:
                self.error_reader.join(timeout=2)
            self.process.stdout.close()
            self.process.stderr.close()
            detail = self.startup_errors.strip()[-1600:]
            raise RuntimeError((str(exc) or 'Помощник не подтвердил запуск вовремя.') +
                               ('\n' + detail if detail else '')) from exc

    def request(self, action, **data):
        with self.control_lock:
            if self.process.poll() is not None:
                raise RuntimeError('Локальный помощник завершил работу.')
            self.serial += 1
            ident = self.serial
            self.process.stdin.write(json.dumps(dict(data, action=action, request=ident)) + '\n')
            self.process.stdin.flush()
            deadline = time.monotonic() + 15
            while True:
                left = deadline - time.monotonic()
                if left <= 0:
                    raise RuntimeError('Локальный помощник не ответил вовремя.')
                try:
                    reply = self.responses.get(timeout=left)
                except queue.Empty:
                    raise RuntimeError('Локальный помощник не ответил вовремя.')
                if reply.get('error') and 'request' not in reply:
                    raise RuntimeError(reply['error'])
                if reply.get('request') != ident:
                    continue
                if reply.get('error'):
                    raise RuntimeError(reply['error'])
                return reply

    def register(self, path):
        url = self.request('register', path=display_file_name(io_path(path)))['url']
        match = re.fullmatch(r'http://(127\.0\.0\.1:[0-9]+)/card/[a-f0-9]{32}/', url)
        if not match:
            raise RuntimeError('Помощник вернул неверный адрес карты.')
        # A real HTTP response, not just a listening socket or a running thread.
        connection = http.client.HTTPConnection(match[1], timeout=8)
        try:
            connection.request('HEAD', url.split(match[1], 1)[1])
            response = connection.getresponse()
            if response.status != 200 or not response.getheader('Content-Type', '').startswith('text/html'):
                raise RuntimeError('Локальный помощник не подтвердил готовность HTML.')
        finally:
            connection.close()
        return url


def local_card_worker():
    """Standalone stdlib mode. EOF on the parent's pipe ends the helper safely."""
    helper = LocalCardHelper(threading.RLock())
    held = 0
    print(json.dumps({'ready': SCRIPT_VERSION}), flush=True)
    try:
        for line in sys.stdin:
            command = json.loads(line)
            reply = {'request': command.get('request')}
            try:
                action = command.get('action')
                if action == 'register':
                    reply['url'] = helper.register(Path(command['path']))
                elif action == 'lock':
                    helper.lock.acquire()
                    held += 1
                elif action == 'unlock':
                    if held:
                        helper.lock.release()
                        held -= 1
                else:
                    raise ValueError('Неизвестная команда помощника.')
            except Exception as exc:
                reply['error'] = str(exc)
            print(json.dumps(reply), flush=True)
    finally:
        while held:
            helper.lock.release()
            held -= 1
        with helper.lock:
            # Drain any in-progress disk write; pending requests cannot start
            # another write once the NX process closes its control pipe.
            for entry in helper.entries.values():
                entry['active'] = False
        helper.server.shutdown()
        helper.server.server_close()


def local_card_url(path):
    runtime = local_card_runtime()
    with runtime['lock']:
        helper = runtime['helper']
        if helper is not None and getattr(helper, 'external_protocol', None) != 1:
            # Retire an in-process helper left by V2.38. Revoke entries under
            # its shared lock so a pending old request cannot overwrite a file.
            for entry in helper.entries.values():
                entry['id'] = uuid.uuid4().hex
            helper.server.shutdown()
            helper.server.server_close()
            runtime['helper'] = helper = None
        if helper is not None and helper.process.poll() is not None:
            runtime['helper'] = helper = None
        if helper is None:
            failures = []
            deadline = time.monotonic() + 15
            for executable in helper_python_candidates():
                if time.monotonic() >= deadline:
                    break
                try:
                    helper = RemoteCardHelper(executable)
                    break
                except Exception as exc:
                    failures.append(str(exc))
            if helper is None and os.name == 'nt':
                try:
                    helper = RemoteCardHelper()
                except Exception as exc:
                    failures.append(str(exc))
            if helper is None:
                raise RuntimeError('Не удалось запустить локальный помощник автосохранения '
                                   'через доступный Python или встроенный Python NX. '
                                   'Готовая карта сохранена.' +
                                   ('\n' + failures[-1] if failures else ''))
            runtime['helper'] = helper
        return helper.register(path)


def diagnostic_event(event, **details):
    if EXPORT_PROGRESS is not None:
        EXPORT_PROGRESS.event(event, details)
    if DIAGNOSTIC_CALLBACK is not None:
        DIAGNOSTIC_CALLBACK(event, details)


def progress_dialog_template():
    """A modeless progress window defined in memory; no resource files."""
    def text(value):
        return (value + '\0').encode('utf-16-le')
    controls = [
        (0x82, 101, 'Подготовка к созданию карты', 10, 10, 390, 20, 0),
        ('msctls_progress32', 102, '', 10, 34, 340, 15, 1),
        (0x82, 103, '0%', 356, 32, 44, 20, 1),
        (0x82, 104, '', 10, 58, 390, 28, 0),
    ]
    # WS_POPUP | WS_CAPTION | DS_MODALFRAME | DS_SETFONT | DS_CENTER.
    # No close/cancel button: closing the indicator must not interrupt NX undo.
    # WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE keeps the focus on NX.
    data = bytearray(struct.pack('<IIHhhhh', 0x80C008C0, 0x08000080,
                                 len(controls), 0, 0, 410, 94))
    data += struct.pack('<HH', 0, 0) + text(TITLE + ' — Создание карты')
    data += struct.pack('<H', 9) + text('Segoe UI')
    for cls, ident, label, x, y, width, height, style in controls:
        data += b'\0' * (-len(data) % 4)
        data += struct.pack('<IIhhhhH', 0x50000000 | style, 0, x, y, width, height, ident)
        data += struct.pack('<HH', 0xFFFF, cls) if isinstance(cls, int) else text(cls)
        data += text(label) + struct.pack('<H', 0)
    return bytes(data)


class NativeProgressWindow:
    """Paint on the journal thread, without dispatching queued NX commands."""
    def __init__(self):
        import ctypes
        from ctypes import wintypes as w
        self.ctypes, self.window = ctypes, None

        class INITCOMMONCONTROLSEX(ctypes.Structure):
            _fields_ = [('dwSize', w.DWORD), ('dwICC', w.DWORD)]

        class MSG(ctypes.Structure):
            _fields_ = [('hwnd', w.HWND), ('message', w.UINT), ('wParam', w.WPARAM),
                        ('lParam', w.LPARAM), ('time', w.DWORD), ('pt', w.POINT),
                        ('lPrivate', w.DWORD)]

        self.message_type = MSG
        user = self.user = ctypes.WinDLL('user32', use_last_error=True)
        common = ctypes.WinDLL('comctl32', use_last_error=True)
        callback_type = ctypes.WINFUNCTYPE(ctypes.c_ssize_t, w.HWND, w.UINT, w.WPARAM, w.LPARAM)
        signatures = {
            'GetActiveWindow': ([], w.HWND), 'GetForegroundWindow': ([], w.HWND),
            'GetDlgItem': ([w.HWND, ctypes.c_int], w.HWND),
            'CreateDialogIndirectParamW': ([w.HINSTANCE, ctypes.c_void_p, w.HWND, callback_type, w.LPARAM], w.HWND),
            'ShowWindow': ([w.HWND, ctypes.c_int], w.BOOL),
            'SetWindowTextW': ([w.HWND, w.LPCWSTR], w.BOOL),
            'SendMessageW': ([w.HWND, w.UINT, w.WPARAM, w.LPARAM], ctypes.c_ssize_t),
            'PeekMessageW': ([ctypes.POINTER(MSG), w.HWND, w.UINT, w.UINT, w.UINT], w.BOOL),
            'IsDialogMessageW': ([w.HWND, ctypes.POINTER(MSG)], w.BOOL),
            'TranslateMessage': ([ctypes.POINTER(MSG)], w.BOOL),
            'DispatchMessageW': ([ctypes.POINTER(MSG)], ctypes.c_ssize_t),
            'PostQuitMessage': ([ctypes.c_int], None),
            'RedrawWindow': ([w.HWND, ctypes.c_void_p, w.HANDLE, w.UINT], w.BOOL),
            'DestroyWindow': ([w.HWND], w.BOOL),
        }
        for name, (arguments, result) in signatures.items():
            function = getattr(user, name)
            function.argtypes, function.restype = arguments, result
        common.InitCommonControlsEx.argtypes = [ctypes.POINTER(INITCOMMONCONTROLSEX)]
        common.InitCommonControlsEx.restype = w.BOOL
        init = INITCOMMONCONTROLSEX(ctypes.sizeof(INITCOMMONCONTROLSEX), 0x20)
        if not common.InitCommonControlsEx(ctypes.byref(init)):
            raise RuntimeError('Не удалось подготовить полосу прогресса Windows.')

        @callback_type
        def callback(window, message, wparam, lparam):
            # No NX calls from the window procedure. Ignore Escape/Enter/Close.
            return 1 if message in (0x0010, 0x0111) else 0

        self.callback = callback  # Keep the native callback alive until DestroyWindow.
        template = ctypes.create_string_buffer(progress_dialog_template())
        owner = user.GetActiveWindow() or user.GetForegroundWindow()
        self.window = user.CreateDialogIndirectParamW(
            None, ctypes.cast(template, ctypes.c_void_p), owner, callback, 0)
        if not self.window:
            raise RuntimeError('Не удалось открыть окно создания карты: ' +
                               str(ctypes.WinError(ctypes.get_last_error())))
        try:
            self.controls = {ident: user.GetDlgItem(self.window, ident) for ident in (101, 102, 103, 104)}
            if not all(self.controls.values()):
                raise RuntimeError('Не удалось создать элементы полосы прогресса.')
            user.SendMessageW(self.controls[102], 0x0406, 0, 100)  # PBM_SETRANGE32.
            user.ShowWindow(self.window, 4)  # SW_SHOWNOACTIVATE.
            self.update(0, 'Подготовка к созданию карты', '')
        except Exception:
            self.close()
            raise

    def update(self, percent, stage, detail):
        if not self.window:
            return
        for ident, text in ((101, stage), (103, str(percent) + '%'), (104, detail)):
            self.user.SetWindowTextW(self.controls[ident], text)
        self.user.SendMessageW(self.controls[102], 0x0402, percent, 0)  # PBM_SETPOS.
        # Only this window and its child controls; never pump the whole NX queue.
        message = self.message_type()
        for _ in range(32):
            if not self.user.PeekMessageW(self.ctypes.byref(message), self.window, 0, 0, 1):
                break
            if message.message == 0x0012:  # Preserve the host's WM_QUIT.
                self.user.PostQuitMessage(int(message.wParam))
                break
            if not self.user.IsDialogMessageW(self.window, self.ctypes.byref(message)):
                self.user.TranslateMessage(self.ctypes.byref(message))
                self.user.DispatchMessageW(self.ctypes.byref(message))
        self.user.RedrawWindow(self.window, None, None, 0x0181)  # Invalidate, children, update now.

    def hide_for_capture(self, hidden):
        if self.window:
            self.user.ShowWindow(self.window, 0 if hidden else 4)
            if not hidden:
                self.user.RedrawWindow(self.window, None, None, 0x0181)

    def close(self):
        if self.window:
            window, self.window = self.window, None
            self.user.DestroyWindow(window)


class ExportProgress:
    """Weighted completed-work progress, independent of diagnostic logging."""
    def __init__(self, setups):
        self.window = NativeProgressWindow()
        self.setups = max(1, int(setups))
        self.percent, self.last_paint, self.last_text = 0., 0., None
        self.body_index, self.body_total = 1, 1
        self.face_index, self.face_total = 1, 1
        self.setup_index, self.setup_name = 0, ''
        self.phase = 'prepare'

    def update(self, percent=None, stage='', detail='', complete=False):
        if percent is not None:
            self.percent = max(self.percent, min(100. if complete else 99., max(0., percent)))
        now = time.monotonic()
        text = (int(self.percent), stage, detail)
        # Limit repainting, not computation; long loops supply real item counts.
        if text == self.last_text and now - self.last_paint < .2:
            return
        self.last_text, self.last_paint = text, now
        if self.window is not None:
            try:
                self.window.update(*text)
            except Exception:
                # A broken indicator must never bypass NX geometry/display cleanup.
                self.close()

    def mesh(self):
        self.phase = 'mesh'
        self.update(2, 'Подготовка 3D-модели', 'Получение отображаемых тел NX')

    def body_progress(self, fraction, stage, detail=''):
        value = 2 + 50 * (self.body_index - 1 + fraction) / self.body_total
        self.update(value, stage, 'Тело %d из %d%s' % (
            self.body_index, self.body_total, ' · ' + detail if detail else ''))

    def setup(self, index, name):
        self.phase, self.setup_index, self.setup_name = 'setup', index, name
        self.setup_progress(0, 'Подготовка установа')

    def setup_progress(self, fraction, stage, detail=''):
        value = 55 + 37 * (self.setup_index - 1 + fraction) / self.setups
        self.update(value, 'Установ %d из %d · %s' % (
            self.setup_index, self.setups, self.setup_name), stage + ('\n' + detail if detail else ''))

    def event(self, event, data):
        if self.phase == 'mesh':
            if event == 'mesh.bodies':
                self.body_total = max(1, int(data['count']))
            elif event == 'mesh.body.begin':
                self.body_index = int(data['index'])
                self.face_index, self.face_total = 1, 1
                self.body_progress(0, 'Подготовка 3D-модели')
            elif event == 'UF.Facet.FacetSolid.begin':
                self.body_progress(0, 'Создание фасетной сетки NX')
            elif event == 'mesh.face.begin':
                self.face_index, self.face_total = int(data['index']), max(1, int(data['count']))
                self.body_progress(.85 * (self.face_index - 1) / self.face_total,
                                   'Подготовка граней тела', 'Грань %d из %d' % (self.face_index, self.face_total))
            elif event in ('mesh.facets.progress', 'mesh.facets.end'):
                ratio = min(1., max(0., data['completed'] / max(1, data['count'])))
                detail = 'Фасеты: %d / %d' % (data['completed'], data['count'])
                if self.face_total > 1:
                    detail = 'Грань %d/%d · %s' % (self.face_index, self.face_total, detail)
                self.body_progress(.85 * (self.face_index - 1 + ratio) / self.face_total,
                                   'Чтение фасетной сетки', detail)
            elif event == 'mesh.transform.begin':
                self.body_progress(.85, 'Подготовка координат 3D-модели')
            elif event == 'mesh.transform.progress':
                self.body_progress(.85 + .15 * data['completed'] / max(1, data['count']),
                                   'Подготовка координат 3D-модели')
            elif event == 'mesh.body.end':
                self.body_progress(1, 'Подготовка 3D-модели')
            elif event == 'mesh.pack.begin':
                self.update(52, 'Упаковка 3D-модели для карты')
            elif event == 'mesh.pack.end':
                self.update(55, '3D-модель подготовлена')
        elif self.phase == 'setup':
            if event == 'stage':
                if data['stage'] == 'Чтение таблицы операций':
                    self.setup_progress(.94, data['stage'])
                else:
                    self.update(stage='Установ %d из %d · %s' % (
                        self.setup_index, self.setups, self.setup_name), detail=data['stage'])
            elif event in ('capture.view.begin', 'capture.view.end'):
                count = max(1, data['count'])
                fraction = (data['index'] - (event.endswith('.begin'))) / count
                self.setup_progress(.05 + .85 * fraction, 'Создание видов',
                                    'Вид %d из %d · %s' % (data['index'], count, data['label']))
            elif event == 'operations.progress':
                self.setup_progress(.94 + .04 * data['completed'] / max(1, data['count']),
                                    'Чтение таблицы операций', '%d / %d' % (data['completed'], data['count']))
            elif event == 'operations.zmin.progress':
                fraction = data['completed'] / max(1, data['count'])
                self.setup_progress(.94 + .04 * (data['operation_index'] + fraction) / max(1, data['operations']),
                                    'Расчёт Zmin · ' + data['operation'],
                                    'Движения траектории: %d / %d' % (data['completed'], data['count']))

    def hide_for_capture(self, hidden):
        if self.window is not None:
            try:
                self.window.hide_for_capture(hidden)
            except Exception:
                self.close()

    def close(self):
        if self.window is not None:
            window, self.window = self.window, None
            try:
                window.close()
            except Exception:
                pass


def read_card_settings(settings_path=None):
    """Read the journal's adjacent INI, independently of NX's current directory."""
    info = {'source': 'default'}
    result = {'author': DEFAULT_PROGRAMMER, 'author_settings': info}
    try:
        path = io_path(settings_path) if settings_path is not None else io_path(__file__).resolve().with_name(SETTINGS_FILENAME)
        info['file'] = display_file_name(path)
        raw = path.read_bytes()
        if raw.startswith((b'\xff\xfe', b'\xfe\xff')):
            text = raw.decode('utf-16')
        else:
            try:
                text = raw.decode('utf-8-sig')
            except UnicodeDecodeError:
                text = raw.decode('cp1251')
        config = configparser.ConfigParser(interpolation=None)
        config.read_string(text)
        name = config.get('SetupCard', 'programmer', fallback='').strip()
        if name:
            result['author'] = name
            info['source'] = 'ini'
    except FileNotFoundError:
        pass
    except Exception as exc:
        info['error'] = str(exc)
        result['settings_warning'] = ('Не удалось прочитать ' + SETTINGS_FILENAME +
            '. Заполните поле «Разработчик» в карте или исправьте programmer в INI.')
    return result


FRAME_MARGIN = 0.05
FRAME_FIT_FRACTION = 1.0 - 2.0 * FRAME_MARGIN
# NX 2606 пользователя возвращает минуты: подтверждено сравнением
# GetToolpathTime и TIME в Operation Navigator, а не старой C++ справкой.
NX_TOOLPATH_TIME_UNIT = "minutes"

# Положение наблюдателя относительно MCS; второй вектор — вверх на экране.
VIEW_PRESETS = (
    ("01_top.png", "Сверху · со стороны +Z", (0., 0., 1.), (0., 1., 0.)),
    ("02_side.png", "Сбоку · со стороны −Y", (0., -1., 0.), (0., 0., 1.)),
    ("03_iso.png", "Изометрия · со стороны +X, −Y, +Z", (1., -1., 1.), (0., 0., 1.)),
)


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def cross(a, b):
    return (a[1]*b[2] - a[2]*b[1],
            a[2]*b[0] - a[0]*b[2],
            a[0]*b[1] - a[1]*b[0])


def unit(v):
    v = tuple(float(x) for x in v)
    if len(v) != 3 or not all(math.isfinite(x) for x in v):
        raise ValueError("Некорректный вектор системы координат.")
    length = math.sqrt(dot(v, v))
    if length < 1e-10:
        raise ValueError("Нулевой вектор системы координат.")
    return tuple(x / length for x in v)


def xyz(v):
    return (float(v.X), float(v.Y), float(v.Z))


def checked_basis(x_axis, y_axis):
    x_axis, y_axis = unit(x_axis), unit(y_axis)
    if abs(dot(x_axis, y_axis)) > 1e-6:
        raise ValueError("Оси выбранной MCS не перпендикулярны.")
    z_axis = unit(cross(x_axis, y_axis))
    return (x_axis, unit(cross(z_axis, x_axis)), z_axis)


def local_to_absolute(vector, mcs_basis):
    return tuple(sum(vector[j] * mcs_basis[j][i] for j in range(3))
                 for i in range(3))


def camera_axes(mcs_basis, eye_local, up_local):
    # Положительная ось Z вида направлена от модели к наблюдателю.
    back = unit(local_to_absolute(eye_local, mcs_basis))
    up_hint = unit(local_to_absolute(up_local, mcs_basis))
    right = unit(cross(up_hint, back))
    up = unit(cross(back, right))
    return (right, up, back)


def matrix_rows(matrix):
    return tuple(tuple(float(getattr(matrix, a+b)) for b in "xyz")
                 for a in "XYZ")


def nx_matrix(nx, axes, transpose=False):
    matrix = nx.Matrix3x3()
    for i, a in enumerate("XYZ"):
        for j, b in enumerate("xyz"):
            setattr(matrix, a+b, axes[j][i] if transpose else axes[i][j])
    return matrix


def axes_equal(a, b, tolerance=1e-6):
    return all(abs(a[i][j]-b[i][j]) <= tolerance
               for i in range(3) for j in range(3))


def orient_and_verify(nx, view, expected):
    # Проверяем фактические оси рабочего вида, а не только переданную матрицу.
    # Это обнаруживает несовпадение соглашений о строках/столбцах матрицы.
    current = tuple(xyz(view.GetAxis(axis)) for axis in
                    (nx.XYZAxis.XAxis, nx.XYZAxis.YAxis, nx.XYZAxis.ZAxis))
    rows = matrix_rows(view.Matrix)
    columns = tuple(zip(*rows))
    # Infer the convention from the current view without turning the model.
    # Symmetric matrices are ambiguous; only then may verification need the
    # other convention. Never repeat a rotation already at the requested view.
    prefer_columns = axes_equal(current, columns) and not axes_equal(current, rows)
    if axes_equal(current, expected):
        return 'columns' if prefer_columns else 'rows'
    for transpose in ((True, False) if prefer_columns else (False, True)):
        view.Orient(nx_matrix(nx, expected, transpose))
        actual = tuple(xyz(view.GetAxis(axis)) for axis in
                       (nx.XYZAxis.XAxis, nx.XYZAxis.YAxis, nx.XYZAxis.ZAxis))
        if axes_equal(actual, expected):
            return "columns" if transpose else "rows"
    raise RuntimeError("Ориентация вида NX не совпала с рассчитанными осями MCS. "
                       "Изображение не сохранено. Проверьте ориентацию MCS в NX.")


def nearest_mcs(nx, obj):
    seen = set()
    while obj is not None:
        key = str(obj.Tag)
        if key in seen:
            raise RuntimeError("Обнаружен цикл в дереве геометрии CAM.")
        seen.add(key)
        if isinstance(obj, nx.CAM.OrientGeometry):
            return obj
        if isinstance(obj, nx.CAM.Operation):
            obj = obj.GetParent(nx.CAM.CAMSetup.View.Geometry)
        elif isinstance(obj, nx.CAM.NCGroup):
            obj = obj.GetParent()
        else:
            return None
    return None


def cam_selection(nx, ui, part, report):
    # Выделение в навигаторе CAM хранится отдельно от общего выделения NX.
    try:
        count, tags = nx.UF.UFSession.GetUFSession().UiOnt.AskSelectedNodes()
    except Exception as exc:
        report["navigator_selection_warning"] = str(exc)
        count, tags = 0, []
    if count:
        wanted = {str(tag) for tag in tags}
        objects = [obj for collection in (part.CAMSetup.CAMGroupCollection,
                                          part.CAMSetup.CAMOperationCollection)
                   for obj in collection if str(obj.Tag) in wanted]
        if len(objects) != len(wanted):
            raise RuntimeError("Не удалось прочитать выбранные узлы CAM. "
                               "Выдели папку операций и повтори запуск.")
        report["selection_source"] = "operation_navigator"
        return objects
    manager = ui.SelectionManager
    report["selection_source"] = "selection_manager"
    return [manager.GetSelectedTaggedObject(i)
            for i in range(manager.GetNumSelectedObjects())]


def resolve_mcs(nx, ui, part, report):
    selected = cam_selection(nx, ui, part, report)
    matches = {}
    for obj in selected:
        group = nearest_mcs(nx, obj)
        if group is not None:
            matches[str(group.Tag)] = group
    if len(matches) == 1:
        return next(iter(matches.values())), "selected"
    if len(matches) > 1:
        raise RuntimeError("Выбраны разные СКС: " + ", ".join(g.Name for g in matches.values())
                           + ". Выдели одну MCS или операции одного установа.")
    if selected:
        raise RuntimeError("В выделении не найдена MCS. Выдели строку MCS "
                           "в дереве геометрии CAM или одну операцию этого установа.")
    groups = [g for g in part.CAMSetup.CAMGroupCollection
              if isinstance(g, nx.CAM.OrientGeometry)]
    if len(groups) == 1:
        return groups[0], "only_mcs_in_part"
    names = ", ".join(g.Name for g in groups[:12]) or "не найдены"
    raise RuntimeError("Сначала выдели одну MCS в навигаторе операций, "
                       "затем снова запусти журнал. Доступные MCS: " + names)


def read_mcs(part, group):
    if str(group.OwningPart.Tag) != str(part.Tag):
        raise RuntimeError("MCS принадлежит другой детали. Сделай CAM-файл "
                           "рабочей и отображаемой деталью и повтори запуск.")
    builder = part.CAMSetup.CAMGroupCollection.CreateMillOrientGeomBuilder(group)
    try:
        csys = builder.Mcs
        if csys is None:
            raise RuntimeError("У выбранной группы не определена MCS.")
        # GetDirections возвращает физические направления осей, что исключает
        # неоднозначность порядка элементов NXMatrix при чтении MCS.
        x_axis, y_axis = csys.GetDirections()
        basis = checked_basis(xyz(x_axis), xyz(y_axis))
        origin = xyz(csys.Origin)
        return basis, origin
    finally:
        # Не вызываем Commit: CAM-параметры только читаются.
        builder.Destroy()


def activate_first_mcs(nx, part, context, report):
    """Выбрать первую CAM-операцию и включить её штатные XM/YM/ZM.

    WCS — другая система: SetCoordinateSystem / Unblank здесь недопустимы.
    Численные оси карты читаются из геометрической ветки операции; результат
    UI-запроса не используется как источник ориентации камеры или карты.
    """
    group = context['mcs']
    operation = context['operations'][0]
    report['current_stage'] = 'Отображение СКС первой операции'
    diagnostic_event('stage', stage=report['current_stage'])
    basis, origin = read_mcs(part, group)
    report['mcs_name'] = str(group.Name)
    report['mcs_axes_absolute'], report['mcs_origin_absolute'] = basis, origin
    info = report['mcs_activation'] = {
        'status': 'pending', 'mcs_name': str(group.Name), 'mcs_tag': object_key(group),
        'operation_name': str(operation.Name), 'operation_tag': object_key(operation),
        'api': 'Selection.RequestSelections; CAMSession.SetMcsDisplay',
        'axes_source': 'first_operation_geometry_parent.OrientGeomBuilder.Mcs',
        'wcs_repositioned': False}
    changed = []
    view = part.ModelingViews.WorkView
    original_view = snapshot_view(view)

    def keep_camera():
        # В NX может быть включена автоматическая ориентация при выделении.
        if snapshot_view(view) != original_view:
            errors = restore_view(nx, view, original_view)
            if errors:
                raise RuntimeError('Не удалось сохранить выбранный ракурс: ' + '; '.join(errors))

    try:
        session = nx.Session.GetSession()
        cam = session.CAMSession
        uf = nx.UF.UFSession.GetUFSession()
        manager = nx.UI.GetUI().SelectionManager
        # Исходная папка уже прочитана resolve_context. Переключение выделения
        # не должно сократить состав экспорта до одной операции.
        selected = list(context.get('selected_objects', ()))
        selected.extend(manager.GetSelectedTaggedObject(i)
                        for i in range(manager.GetNumSelectedObjects()))
        selected = list({object_key(obj): obj for obj in selected}.values())
        before_display = bool(cam.GetMcsDisplay())
        before_wcs = bool(part.WCS.Visibility)
        changed.append(lambda: cam.SetMcsDisplay(before_display))
        cam.SetMcsDisplay(False)
        changed.append(lambda: setattr(part.WCS, 'Visibility', before_wcs))
        part.WCS.Visibility = False
        changed.append(lambda: manager.RequestSelections(selected) if selected else None)
        if selected:
            manager.RequestDeselections(selected)
        changed.append(lambda: manager.RequestDeselections([operation]))
        manager.RequestSelections([operation])
        uf.UiOnt.Refresh()
        # Перед собственным Win32-диалогом завершить буферизованное рисование.
        # Без этого обновление может стать видно только после конца журнала.
        uf.Disp.MakeDisplayUpToDate()
        keep_camera()
        count, tags = uf.UiOnt.AskSelectedNodes()
        actual_tags = [str(tag) for tag in tags]
        info['selected_operation_tags'] = actual_tags
        if count != 1 or actual_tags != [object_key(operation)]:
            raise RuntimeError('NX не подтвердил выделение первой операции «%s» '
                               'в Operation Navigator.' % operation.Name)
        cam.SetMcsDisplay(True)
        if not cam.GetMcsDisplay():
            raise RuntimeError('NX не включил отображение CAM СКС.')
        part.Views.Refresh()
        uf.Disp.MakeDisplayUpToDate()
        info['status'] = 'selection_confirmed_display_enabled'
    except Exception as exc:
        # Численные данные MCS уже прочитаны. Карта может быть построена
        # правильно даже при недоступном программном выделении в интерфейсе.
        info.update(status='warning', message=str(exc))
        for action in reversed(changed):
            try:
                action()
            except Exception as restore_exc:
                info.setdefault('restore_errors', []).append(str(restore_exc))
        try:
            keep_camera()
        except Exception as restore_exc:
            info.setdefault('restore_errors', []).append(str(restore_exc))
            # Ошибка UI допустима; потеря выбранного ракурса — нет.
            raise
        report.setdefault('data_issues', []).append({
            'object': str(group.Name), 'field': 'Отображение СКС в NX',
            'optional': True, 'message': str(exc)})


def object_key(obj):
    return str(obj.Tag)


def mcs_number_key(name):
    """1 / 1S, MCS_1 / MCS_1S и 1_описание имеют номер 1."""
    value = str(name).strip().upper()
    match = re.fullmatch(r"(?:MCS[ _-]*)?([0-9]+)S?(?:[ _-].*)?", value)
    return ("number", str(int(match.group(1)))) if match else ("name", value)


def setup_name_from_context(context):
    """Полное имя СКС первой операции, без извлечения номера и суффиксов."""
    if not context["operations"]:
        raise RuntimeError("В выбранной папке нет операций. Невозможно определить установ.")
    name = str(context["mcs"].Name)
    if not name.strip():
        raise RuntimeError("У СКС первой операции пустое имя. Задайте ей название в NX.")
    return name


def setup_label(report):
    return str(report.get('setup_name', report.get('setup_number', '')))


def is_first_setup(report):
    # Совместимость прежних данных; новые содержат явный признак.
    return bool(report['is_first_setup']) if 'is_first_setup' in report else (
        mcs_number_key(setup_label(report)) == ('number', '1'))


def record_setup_identity(context, report):
    name = setup_name_from_context(context)
    report['setup_name'] = name
    # Ключ сохранён для совместимости, но его значение теперь текстовое.
    report['setup_number'] = name
    report['setup_number_source'] = {
        'method': 'first_operation_mcs_name', 'mcs_name': name,
        'operation_name': str(context['operations'][0].Name)}
    kind, number = mcs_number_key(name)
    if kind == 'number':
        first, source = number == '1', 'numeric_mcs_alias'
    else:
        first_mcs = context.get('project_first_mcs')
        first = first_mcs is not None and mcs_number_key(first_mcs.Name) == mcs_number_key(name)
        source = 'first_operation_in_project_program_order'
    report['is_first_setup'] = first
    report['first_setup_source'] = source
    return name


def export_is_complete(report):
    """Предупреждение о UI/восстановлении вида не делает готовые файлы ошибочными."""
    return report.get('status') in ('ok', 'warning') and not report.get('error')


def safe_file_component(name, reserve_internal=False):
    """Сохраняет обычные имена точно; небезопасные имена путей не смешивает."""
    original = str(name)
    result = re.sub(r'[<>:"/\\|?*\x00-\x1f]', '_', original).rstrip(' .')
    if not result or result in ('.', '..'):
        result = '_'
    if re.fullmatch(r'CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³]', result.split('.')[0], re.I):
        result = '_' + result
    if reserve_internal and (result.casefold() in ('история', 'ошибки') or result.casefold().startswith('.nx_')):
        result = '_' + result
    # Запас для расширения в пределах 255 UTF-16 единиц Windows.
    encoded = result.encode('utf-16-le')
    if len(encoded) > 460:
        result = encoded[:440].decode('utf-16-le', errors='ignore').rstrip(' .')
    if result != original:
        result += '~' + hashlib.sha256(original.encode('utf-8')).hexdigest()[:10]
    return result


def cam_tree(nx, obj, trail=()):
    key = object_key(obj)
    if key in trail:
        raise RuntimeError("Обнаружен цикл в дереве операций CAM.")
    if isinstance(obj, nx.CAM.Operation):
        return {"object": obj, "children": None}
    if not isinstance(obj, nx.CAM.NCGroup):
        raise RuntimeError("Выдели папку операций, MCS или операции в Operation Navigator.")
    return {"object": obj,
            "children": [cam_tree(nx, member, trail + (key,)) for member in obj.GetMembers()]}


def tree_nodes(tree):
    yield tree
    for child in tree["children"] or ():
        yield from tree_nodes(child)


class ExportCancelled(Exception):
    """An explicit user cancellation, not a failed card export."""


def confirm_mixed_mcs(nx, groups, reference, report):
    """Confirm mixed setups before changing the view or creating any files."""
    different = len({mcs_number_key(group.Name) for group in groups}) > 1
    decision = report['mixed_mcs'] = {
        'present': different,
        'names': [str(group.Name) for group in groups],
        'reference_mcs': str(reference.Name),
        'reference_source': 'first_operation_in_program_order',
        'decision': 'not_needed',
    }
    if not different:
        return
    answer = ask_warning(nx,
        'В установе присутствуют разные MCS. Продолжить создание карты наладки?')
    decision['decision'] = 'yes' if answer is True else ('no' if answer is False else 'closed')
    if answer is not True:
        raise ExportCancelled('Создание карты наладки отменено пользователем.')


def resolve_context(nx, ui, part, report, selected=None):
    """Члены папки берутся рекурсивно, порядок — из ветки ProgramOrder."""
    selected = cam_selection(nx, ui, part, report) if selected is None else list(selected)
    source = "selected"
    if not selected:
        groups = [g for g in part.CAMSetup.CAMGroupCollection
                  if isinstance(g, nx.CAM.OrientGeometry)]
        if len(groups) != 1:
            raise RuntimeError("Выдели папку нужного установа в Operation Navigator и запусти журнал снова.")
        selected, source = groups, "only_mcs_in_part"
    wanted, selected_keys = {}, {object_key(obj) for obj in selected}
    selected_mcs = []
    for obj in selected:
        if isinstance(obj, nx.CAM.OrientGeometry):
            selected_mcs.append(obj)
        for node in tree_nodes(cam_tree(nx, obj)):
            wanted[object_key(node["object"])] = node
    wanted_ops = {key for key, node in wanted.items() if node["children"] is None}
    program = cam_tree(nx, part.CAMSetup.GetRoot(nx.CAM.CAMSetup.View.ProgramOrder))

    def prune(node):
        key = object_key(node["object"])
        if node["children"] is None:
            return node if key in wanted_ops else None
        children = [result for child in node["children"] for result in [prune(child)] if result is not None]
        if children or key in wanted:
            return {"object": node["object"], "children": children}
        return None

    tree = prune(program)
    if tree is not None:
        # Убираем внешние папки NC_PROGRAM / MAIN, сохраняя выбранный узел.
        while (object_key(tree["object"]) not in selected_keys
               and tree["children"] is not None and len(tree["children"]) == 1):
            tree = tree["children"][0]
        operations = [n["object"] for n in tree_nodes(tree) if n["children"] is None]
    else:
        operations = []
    if {object_key(op) for op in operations} != wanted_ops:
        raise RuntimeError("Не все выделенные операции найдены в ветке Program Order. Проверь текущий CAM-проект.")
    if not operations:
        if len(selected) == 1 and len(selected_mcs) == 1:
            group = selected_mcs[0]
            groups = {object_key(group): group}
        else:
            raise RuntimeError("В выбранной папке нет операций.")
    else:
        groups = {}
        for operation in operations:
            group = nearest_mcs(nx, operation)
            if group is None:
                raise RuntimeError("У операции «%s» не найдена СКС." % operation.Name)
            groups[object_key(group)] = group
        group = nearest_mcs(nx, operations[0])
    report["selected_nodes"] = [obj.Name for obj in selected]
    report["mcs_names"] = [g.Name for g in groups.values()]
    report["operation_count"] = len(operations)
    confirm_mixed_mcs(nx, list(groups.values()), group, report)
    first_project_op = next((n['object'] for n in tree_nodes(program) if n['children'] is None), None)
    first_project_mcs = nearest_mcs(nx, first_project_op) if first_project_op is not None else None
    return {"mcs": group, "source": source, "tree": tree, "operations": operations,
            "selected_objects": selected, "project_first_mcs": first_project_mcs, "program_tree": program}


def navigator_folder_path(tree, target, prefix=()):
    """A name path survives NX sessions; runtime object tags do not."""
    if tree['children'] is None:
        return None
    path = prefix + (str(tree['object'].Name),)
    if object_key(tree['object']) == object_key(target):
        return list(path)
    for child in tree['children']:
        found = navigator_folder_path(child, target, path)
        if found is not None:
            return found
    return None


def resolve_ipw_source(nx, part, context, report):
    """The selected folder is one setup; only its first MCS supplies IPW.

    resolve_context already orders ALL selected operations by ProgramOrder.
    Filter that list by the actual MCS object, never by numeric/S name aliases
    or by operations outside the selected folder. Keep all rows in the card.
    """
    source = context.get('ipw_source')
    if source is None:
        operations = context['operations']
        if not operations:
            raise RuntimeError('В выбранной папке нет операций для определения первой MCS.')
        source_mcs = nearest_mcs(nx, operations[0])
        if source_mcs is None or object_key(source_mcs) != object_key(context['mcs']):
            raise RuntimeError('Первая MCS выбранной папки не совпала с MCS первой операции.')
        source_operations = []
        for operation in operations:
            owner = nearest_mcs(nx, operation)
            if owner is not None and object_key(owner) == object_key(source_mcs):
                source_operations.append(operation)
        source = {'mcs': source_mcs,
                  'operations': source_operations,
                  'operation_scope': 'selected_first_mcs_operations'}
        context['ipw_source'] = source
    report['ipw_source'] = {
        'method': 'first_operation_mcs_in_selected_folder',
        'reference_mcs_name': str(context['mcs'].Name),
        'mcs_name': str(source['mcs'].Name), 'mcs_tag': object_key(source['mcs']),
        'operation_scope': source['operation_scope'],
        'operations': [str(op.Name) for op in source['operations']]}
    return source


def setup_folder_inventory(program):
    """All Program Order folders in navigator order; operations are not choices."""
    rows, seen = [], set()

    def walk(node, ancestors=(), names=()):
        if node['children'] is None:
            return 1
        obj = node['object']
        key = object_key(obj)
        if key in seen:
            raise RuntimeError('Папка повторяется в дереве Program Order: ' + str(obj.Name))
        seen.add(key)
        row = {'key': key, 'object': obj, 'name': str(obj.Name),
               'ancestors': ancestors, 'parent': ancestors[-1] if ancestors else None,
               'path': names + (str(obj.Name),), 'operation_count': 0}
        rows.append(row)
        row['operation_count'] = sum(walk(child, ancestors + (key,), row['path'])
                                     for child in node['children'])
        return row['operation_count']

    walk(program)
    return rows


class SetupFolderChoices:
    """Checked folders are an antichain: no selected ancestor or descendant."""
    def __init__(self, rows):
        self.rows = rows
        self.by_key = {row['key']: row for row in rows}
        if len(self.by_key) != len(rows):
            raise ValueError('Повторяется идентификатор папки.')
        self.selected = set()

    def disabled(self, key):
        row = self.by_key[key]
        return not row['operation_count'] or any(
            other in row['ancestors'] or key in self.by_key[other]['ancestors']
            for other in self.selected if other != key)

    def toggle(self, key, checked):
        if key not in self.by_key:
            raise ValueError('Неизвестная папка установа.')
        if checked:
            if self.disabled(key):
                return False
            self.selected.add(key)
        else:
            self.selected.discard(key)
        return True

    def values(self):
        return [row['key'] for row in self.rows if row['key'] in self.selected]


class NativeChoiceRows:
    """Paint native checkboxes and labels around one actual row center.

    The controls still own selection, check states, scrolling and hit testing.
    Like the postprocessor tree, geometry comes from Win32, never font offsets.
    """
    def __init__(self, tree, item_info):
        import ctypes
        from ctypes import wintypes as w

        class NMHDR(ctypes.Structure):
            _fields_ = [('hwndFrom', w.HWND), ('idFrom', ctypes.c_size_t), ('code', w.UINT)]

        class NMCUSTOMDRAW(ctypes.Structure):
            _fields_ = [('hdr', NMHDR), ('dwDrawStage', w.DWORD), ('hdc', w.HDC),
                        ('rc', w.RECT), ('dwItemSpec', ctypes.c_size_t),
                        ('uItemState', w.UINT), ('lItemlParam', w.LPARAM)]

        class TVITEMW(ctypes.Structure):
            _fields_ = [('mask', w.UINT), ('hItem', w.HANDLE), ('state', w.UINT),
                        ('stateMask', w.UINT), ('pszText', w.LPWSTR), ('cchTextMax', ctypes.c_int),
                        ('iImage', ctypes.c_int), ('iSelectedImage', ctypes.c_int),
                        ('cChildren', ctypes.c_int), ('lParam', w.LPARAM)]

        class TEXTMETRICW(ctypes.Structure):
            _fields_ = [(name, w.LONG) for name in (
                'height', 'ascent', 'descent', 'internal_leading', 'external_leading',
                'average_width', 'maximum_width', 'weight', 'overhang', 'aspect_x', 'aspect_y')]
            _fields_ += [(name, w.WCHAR) for name in ('first', 'last', 'default', 'break_char')]
            _fields_ += [(name, w.BYTE) for name in ('italic', 'underlined', 'struck_out', 'pitch', 'charset')]

        self.ctypes, self.w = ctypes, w
        self.Draw, self.Item, self.Metrics = NMCUSTOMDRAW, TVITEMW, TEXTMETRICW
        self.tree, self.item_info = tree, item_info
        self.user = ctypes.WinDLL('user32', use_last_error=True)
        self.gdi = ctypes.WinDLL('gdi32', use_last_error=True)
        self.common = ctypes.WinDLL('comctl32', use_last_error=True)
        for library, name, result, arguments in (
                (self.user, 'SendMessageW', ctypes.c_ssize_t, [w.HWND, w.UINT, w.WPARAM, w.LPARAM]),
                (self.user, 'GetClientRect', w.BOOL, [w.HWND, ctypes.POINTER(w.RECT)]),
                (self.user, 'GetWindowLongW', w.LONG, [w.HWND, ctypes.c_int]),
                (self.user, 'GetFocus', w.HWND, []),
                (self.user, 'IsWindowEnabled', w.BOOL, [w.HWND]),
                (self.user, 'GetSysColor', w.DWORD, [ctypes.c_int]),
                (self.user, 'FillRect', ctypes.c_int, [w.HDC, ctypes.POINTER(w.RECT), w.HBRUSH]),
                (self.user, 'DrawTextW', ctypes.c_int,
                 [w.HDC, w.LPCWSTR, ctypes.c_int, ctypes.POINTER(w.RECT), w.UINT]),
                (self.user, 'DrawFrameControl', w.BOOL, [w.HDC, ctypes.POINTER(w.RECT), w.UINT, w.UINT]),
                (self.user, 'DrawFocusRect', w.BOOL, [w.HDC, ctypes.POINTER(w.RECT)]),
                (self.gdi, 'SaveDC', ctypes.c_int, [w.HDC]),
                (self.gdi, 'RestoreDC', w.BOOL, [w.HDC, ctypes.c_int]),
                (self.gdi, 'IntersectClipRect', ctypes.c_int,
                 [w.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int]),
                (self.gdi, 'SelectObject', w.HANDLE, [w.HDC, w.HANDLE]),
                (self.gdi, 'GetStockObject', w.HANDLE, [ctypes.c_int]),
                (self.gdi, 'SetDCBrushColor', w.DWORD, [w.HDC, w.DWORD]),
                (self.gdi, 'SetTextColor', w.DWORD, [w.HDC, w.DWORD]),
                (self.gdi, 'SetBkMode', ctypes.c_int, [w.HDC, ctypes.c_int]),
                (self.gdi, 'GetDeviceCaps', ctypes.c_int, [w.HDC, ctypes.c_int]),
                (self.gdi, 'GetTextMetricsW', w.BOOL, [w.HDC, ctypes.POINTER(TEXTMETRICW)]),
                (self.gdi, 'CreatePen', w.HANDLE, [ctypes.c_int, ctypes.c_int, w.DWORD]),
                (self.gdi, 'MoveToEx', w.BOOL, [w.HDC, ctypes.c_int, ctypes.c_int, ctypes.POINTER(w.POINT)]),
                (self.gdi, 'LineTo', w.BOOL, [w.HDC, ctypes.c_int, ctypes.c_int]),
                (self.gdi, 'Rectangle', w.BOOL,
                 [w.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int]),
                (self.gdi, 'DeleteObject', w.BOOL, [w.HANDLE]),
                (self.common, 'ImageList_GetIconSize', w.BOOL,
                 [w.HANDLE, ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int)])):
            function = getattr(library, name)
            function.restype, function.argtypes = result, arguments

    def rect(self, window, item, label=False):
        rectangle = self.w.RECT()
        if self.tree:
            self.ctypes.c_void_p.from_buffer(rectangle).value = item
            ok = self.user.SendMessageW(window, 0x1104, int(label), self.ctypes.addressof(rectangle))
        else:
            rectangle.left = 2 if label else 0
            ok = self.user.SendMessageW(window, 0x100E, item, self.ctypes.addressof(rectangle))
        return rectangle if ok else None

    def fill(self, dc, rectangle, color):
        self.gdi.SetDCBrushColor(dc, color)
        self.user.FillRect(dc, self.ctypes.byref(rectangle), self.gdi.GetStockObject(18))  # DC_BRUSH

    def branches(self, window, item, state, children, dc, row, label, slot, background):
        user, gdi, w = self.user, self.gdi, self.w
        style = user.GetWindowLongW(window, -16)
        indent = max(1, user.SendMessageW(window, 0x1106, 0, 0))
        center = row.top + (row.bottom - row.top) // 2
        x, right = label.left - slot - indent // 2, label.left - slot
        parent = user.SendMessageW(window, 0x110A, 3, item)
        root_lines = bool(style & 4)
        pen = gdi.CreatePen(2, 1, user.GetSysColor(16))  # PS_DOT / COLOR_3DSHADOW
        old = gdi.SelectObject(dc, pen)

        def line(x1, y1, x2, y2):
            gdi.MoveToEx(dc, x1, y1, None)
            gdi.LineTo(dc, x2, y2)

        try:
            if style & 2:  # TVS_HASLINES
                if parent or root_lines:
                    top = center if not parent and not user.SendMessageW(window, 0x110A, 2, item) else row.top
                    bottom = row.bottom if user.SendMessageW(window, 0x110A, 1, item) else center
                    line(x, top, x, bottom)
                    line(x, center, right, center)
                ancestor, ancestor_x = parent, x - indent
                while ancestor:
                    ancestor_parent = user.SendMessageW(window, 0x110A, 3, ancestor)
                    if user.SendMessageW(window, 0x110A, 1, ancestor) and (root_lines or ancestor_parent):
                        line(ancestor_x, row.top, ancestor_x, row.bottom)
                    ancestor, ancestor_x = ancestor_parent, ancestor_x - indent
        finally:
            gdi.SelectObject(dc, old)
            gdi.DeleteObject(pen)
        if style & 1 and children and (parent or root_lines):
            size = max(7, round(9 * gdi.GetDeviceCaps(dc, 88) / 96)) | 1
            size = min(size, max(7, indent - 4) | 1)
            half = size // 2
            box = w.RECT(x - half, center - half, x + half + 1, center + half + 1)
            self.fill(dc, box, background)
            pen = gdi.CreatePen(0, 1, user.GetSysColor(16))
            old = gdi.SelectObject(dc, pen)
            brush = gdi.SelectObject(dc, gdi.GetStockObject(5))  # NULL_BRUSH
            try:
                gdi.Rectangle(dc, box.left, box.top, box.right, box.bottom)
                line(x - half + 2, center, x + half - 1, center)
                if not state & 0x20:  # TVIS_EXPANDED
                    line(x, center - half + 2, x, center + half - 1)
            finally:
                gdi.SelectObject(dc, brush)
                gdi.SelectObject(dc, old)
                gdi.DeleteObject(pen)

    def paint(self, notification):
        c, w, user, gdi = self.ctypes, self.w, self.user, self.gdi
        draw = self.Draw.from_address(notification)
        if draw.dwDrawStage == 1:  # CDDS_PREPAINT
            return 0x20  # CDRF_NOTIFYITEMDRAW
        if draw.dwDrawStage != 0x10001:  # CDDS_ITEMPREPAINT
            return 0
        window, item, dc = draw.hdr.hwndFrom, draw.dwItemSpec, draw.hdc
        info = self.item_info(item)
        if info is None:
            return 0
        text, disabled = info
        row, label = self.rect(window, item), self.rect(window, item, True)
        client = w.RECT()
        if row is None or label is None or row.bottom <= row.top or not user.GetClientRect(window, c.byref(client)):
            return 0
        if self.tree:
            data = self.Item(mask=0x48, hItem=item, stateMask=0xFFFF)
            if not user.SendMessageW(window, 0x113E, 0, c.addressof(data)):
                return 0
            state, children = data.state, data.cChildren
        else:
            state = user.SendMessageW(window, 0x102C, item, 0xF003)
            children = 0
        images = user.SendMessageW(window, 0x1108 if self.tree else 0x1002, 2, 0)
        image_width, image_height = c.c_int(), c.c_int()
        slot = image_width.value if images and self.common.ImageList_GetIconSize(
            images, c.byref(image_width), c.byref(image_height)) else 16
        slot = max(1, slot)
        saved = gdi.SaveDC(dc)
        if not saved:
            return 0
        try:
            gdi.IntersectClipRect(dc, client.left, max(client.top, row.top), client.right, min(client.bottom, row.bottom))
            font = user.SendMessageW(window, 0x31, 0, 0)
            if font:
                gdi.SelectObject(dc, font)
            background = user.SendMessageW(window, 0x111F if self.tree else 0x1000, 0, 0) & 0xFFFFFFFF
            foreground = user.SendMessageW(window, 0x1120 if self.tree else 0x1023, 0, 0) & 0xFFFFFFFF
            if background == 0xFFFFFFFF:
                background = user.GetSysColor(5)
            if foreground == 0xFFFFFFFF:
                foreground = user.GetSysColor(8)
            full = w.RECT(client.left, row.top, client.right, row.bottom)
            self.fill(dc, full, background)
            focused = user.GetFocus() == window
            style = user.GetWindowLongW(window, -16)
            selected = bool(state & 2) and (focused or bool(style & (0x20 if self.tree else 8)))
            label = w.RECT(label.left, row.top, label.right, row.bottom)
            selected_box = label if self.tree else w.RECT(max(client.left, row.left), row.top, min(client.right, row.right), row.bottom)
            if selected:
                self.fill(dc, selected_box, user.GetSysColor(13 if focused else 15))
                foreground = user.GetSysColor(14 if focused else 8)
            disabled = disabled or not user.IsWindowEnabled(window)
            if disabled:
                foreground = user.GetSysColor(17)
            if self.tree:
                self.branches(window, item, state, children, dc, row, label, slot, background)
            center = row.top + (row.bottom - row.top) // 2
            size = max(9, min(slot - 2, row.bottom - row.top - 2, round(13 * gdi.GetDeviceCaps(dc, 88) / 96)))
            x = label.left - slot // 2 - size // 2
            box = w.RECT(x, center - size // 2, x + size, center - size // 2 + size)
            user.DrawFrameControl(dc, c.byref(box), 4,
                                  (0x400 if state & 0xF000 == 0x2000 else 0) | (0x100 if disabled else 0))
            gdi.SetTextColor(dc, foreground)
            gdi.SetBkMode(dc, 1)
            metrics = self.Metrics()
            # Center the visible letter body, accounting for excess leading in
            # fonts such as Segoe UI. Keep the selection/hit rectangles intact.
            shift = max(0, (metrics.internal_leading - metrics.descent) // 2) if gdi.GetTextMetricsW(dc, c.byref(metrics)) else 0
            text_box = w.RECT(label.left, label.top - shift, label.right, label.bottom - shift)
            user.DrawTextW(dc, text, -1, c.byref(text_box), 0x824)  # DT_VCENTER | SINGLELINE | NOPREFIX
            if selected and focused and not user.SendMessageW(window, 0x129, 0, 0) & 1:
                user.DrawFocusRect(dc, c.byref(selected_box))
            return 4  # CDRF_SKIPDEFAULT: glyphs and label must not be painted twice.
        finally:
            gdi.RestoreDC(dc, saved)


def preparation_options():
    return dict(number_operations=False, update_descriptions=False,
                include_tool_numbers=False, add_zmin=False,
                create_project_model=False, project_model_accuracy=1.,
                project_model_triangle_limit=PROJECT_MODEL_DEFAULT_TRIANGLES)


def parse_project_model_accuracy(value):
    message = 'Коэффициент точности должен быть больше 0 и не больше 1.'
    try:
        number = Decimal(str(value).strip().replace(',', '.'))
    except (InvalidOperation, ValueError):
        raise ValueError(message) from None
    if not number.is_finite() or not 0 < number <= 1:
        raise ValueError(message)
    accuracy = float(number)
    if accuracy <= 0 or not math.isfinite(.01 / accuracy):
        raise ValueError(message)
    return accuracy


def parse_project_model_triangle_limit(value):
    text = ''.join(str(value).split())
    if not re.fullmatch(r'[0-9]+', text) or not 1 <= int(text) <= 2147483647:
        raise ValueError('Лимит треугольников: целое число от 1 до 2 147 483 647.')
    return int(text)


def setup_folders_dialog_template():
    def text(value):
        return (value + '\0').encode('utf-16-le')
    controls = [
        (0x82, 100, 'Отметьте папки установов. Каждая галочка — отдельный установ. '
         'Родительскую папку и её подпапки нельзя выбрать одновременно.', 10, 9, 480, 29, 0),
        (0x80, 230, 'Нумеровать операции', 10, 41, 480, 14, 0x14003),
        (0x80, 231, 'Добавить параметры инструмента в описание (description)', 10, 62, 480, 14, 0x14003),
        (0x80, 232, 'Только диаметр — ⌀6', 26, 83, 464, 14, 0x34009),
        (0x80, 233, 'Диаметр и параметры T, H, D — ⌀6_T2_H3_D4', 26, 104, 464, 14, 0x14009),
        (0x80, 234, 'Добавить Zmin к именам операций', 10, 83, 480, 14, 0x34003),
        (0x80, 235, 'Создать фасетную 3D-модель на первом листе', 10, 104, 310, 14, 0x14003),
        (0x82, 236, 'Коэффициент (до 1):', 326, 105, 108, 14, 0),
        (0x81, 237, '1', 440, 102, 50, 18, 0x810081),
        (0x82, 238, 'Максимальное количество треугольников:', 26, 126, 340, 14, 0),
        (0x81, 239, format(PROJECT_MODEL_DEFAULT_TRIANGLES, ',').replace(',', ' '), 374, 123, 116, 18, 0x810081),
        ('SysTreeView32', 101, '', 10, 126, 480, 196, 0x810127),
        (0x82, 102, 'Выбрано установов: 0', 10, 329, 480, 14, 0),
        (0x80, 222, 'Снять все', 10, 352, 74, 22, 0x10000),
        (0x80, 1, 'Далее', 326, 352, 78, 22, 0x30001),
        (0x80, 2, 'Отмена', 412, 352, 78, 22, 0x10000),
    ]
    data = bytearray(struct.pack('<IIHhhhh', 0x80C808C0, 0, len(controls), 0, 0, 500, 385))
    data += struct.pack('<HH', 0, 0) + text(TITLE + ' — Выбор установов')
    data += struct.pack('<H', 9) + text('Segoe UI')
    for cls, ident, label, x, y, width, height, style in controls:
        data += b'\0' * (-len(data) % 4)
        data += struct.pack('<IIhhhhH', 0x50000000 | style, 0, x, y, width, height, ident)
        data += struct.pack('<HH', 0xFFFF, cls) if isinstance(cls, int) else text(cls)
        data += text(label) + struct.pack('<H', 0)
    return bytes(data)


def show_setup_folders(rows, options=None):
    """Native modal tree, built in memory. No resource/configuration files."""
    import ctypes
    from ctypes import wintypes as w

    class INITCOMMONCONTROLSEX(ctypes.Structure):
        _fields_ = [('dwSize', w.DWORD), ('dwICC', w.DWORD)]

    class TVITEMW(ctypes.Structure):
        _fields_ = [('mask', w.UINT), ('hItem', w.HANDLE), ('state', w.UINT),
                    ('stateMask', w.UINT), ('pszText', w.LPWSTR), ('cchTextMax', ctypes.c_int),
                    ('iImage', ctypes.c_int), ('iSelectedImage', ctypes.c_int),
                    ('cChildren', ctypes.c_int), ('lParam', w.LPARAM)]

    class TVINSERTSTRUCTW(ctypes.Structure):
        _fields_ = [('hParent', w.HANDLE), ('hInsertAfter', w.HANDLE), ('item', TVITEMW)]

    class TVHITTESTINFO(ctypes.Structure):
        _fields_ = [('pt', w.POINT), ('flags', w.UINT), ('hItem', w.HANDLE)]

    class NMHDR(ctypes.Structure):
        _fields_ = [('hwndFrom', w.HWND), ('idFrom', ctypes.c_size_t), ('code', w.UINT)]

    class NMTVITEMCHANGE(ctypes.Structure):
        _fields_ = [('hdr', NMHDR), ('uChanged', w.UINT), ('hItem', w.HANDLE),
                    ('uStateNew', w.UINT), ('uStateOld', w.UINT), ('lParam', w.LPARAM)]

    user = ctypes.WinDLL('user32', use_last_error=True)
    common = ctypes.WinDLL('comctl32', use_last_error=True)
    callback_type = ctypes.WINFUNCTYPE(ctypes.c_ssize_t, w.HWND, w.UINT, w.WPARAM, w.LPARAM)
    subclass_type = ctypes.WINFUNCTYPE(ctypes.c_ssize_t, w.HWND, w.UINT, w.WPARAM, w.LPARAM,
                                     ctypes.c_size_t, ctypes.c_size_t)
    signatures = {
        'GetActiveWindow': ([], w.HWND), 'GetForegroundWindow': ([], w.HWND),
        'GetDlgItem': ([w.HWND, ctypes.c_int], w.HWND), 'SetFocus': ([w.HWND], w.HWND),
        'EnableWindow': ([w.HWND, w.BOOL], w.BOOL), 'GetSysColor': ([ctypes.c_int], w.DWORD),
        'SetWindowTextW': ([w.HWND, w.LPCWSTR], w.BOOL),
        'GetWindowTextW': ([w.HWND, w.LPWSTR, ctypes.c_int], ctypes.c_int),
        'ShowWindow': ([w.HWND, ctypes.c_int], w.BOOL),
        'MapDialogRect': ([w.HWND, ctypes.POINTER(w.RECT)], w.BOOL),
        'MoveWindow': ([w.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, w.BOOL], w.BOOL),
        'SetWindowLongPtrW': ([w.HWND, ctypes.c_int, ctypes.c_ssize_t], ctypes.c_ssize_t),
        'SendMessageW': ([w.HWND, w.UINT, w.WPARAM, w.LPARAM], ctypes.c_ssize_t),
        'InvalidateRect': ([w.HWND, ctypes.POINTER(w.RECT), w.BOOL], w.BOOL),
        'EndDialog': ([w.HWND, ctypes.c_ssize_t], w.BOOL),
        'DialogBoxIndirectParamW': ([w.HINSTANCE, ctypes.c_void_p, w.HWND, callback_type, w.LPARAM], ctypes.c_ssize_t),
    }
    for name, (arguments, result) in signatures.items():
        function = getattr(user, name)
        function.argtypes, function.restype = arguments, result
    common.InitCommonControlsEx.argtypes = [ctypes.POINTER(INITCOMMONCONTROLSEX)]
    common.InitCommonControlsEx.restype = w.BOOL
    common.SetWindowSubclass.argtypes = [w.HWND, subclass_type, ctypes.c_size_t, ctypes.c_size_t]
    common.SetWindowSubclass.restype = w.BOOL
    common.RemoveWindowSubclass.argtypes = [w.HWND, subclass_type, ctypes.c_size_t]
    common.RemoveWindowSubclass.restype = w.BOOL
    common.DefSubclassProc.argtypes = [w.HWND, w.UINT, w.WPARAM, w.LPARAM]
    common.DefSubclassProc.restype = ctypes.c_ssize_t
    common.ImageList_Destroy.argtypes, common.ImageList_Destroy.restype = [w.HANDLE], w.BOOL
    init = INITCOMMONCONTROLSEX(ctypes.sizeof(INITCOMMONCONTROLSEX), 0x2)
    if not common.InitCommonControlsEx(ctypes.byref(init)):
        raise ctypes.WinError(ctypes.get_last_error())
    model = SetupFolderChoices(rows)
    state = {'window': None, 'tree': None, 'updating': False, 'error': None,
             'handles': {}, 'keys': {}, 'images': None, 'options': preparation_options(), 'double_click': set()}
    labels = {row['key']: row['name'] + (' — нет операций' if not row['operation_count'] else '') for row in rows}

    def item_info(handle):
        key = state['keys'].get(handle)
        return (labels[key], model.disabled(key)) if key is not None else None

    choice_rows = NativeChoiceRows(True, item_info)

    def description_formats():
        shown = user.SendMessageW(user.GetDlgItem(state['window'], 231), 0xF0, 0, 0) == 1
        for ident in (232, 233):
            user.ShowWindow(user.GetDlgItem(state['window'], ident), 5 if shown else 0)
        mesh_shown = user.SendMessageW(user.GetDlgItem(state['window'], 235), 0xF0, 0, 0) == 1
        for ident in (236, 237, 238, 239):
            user.ShowWindow(user.GetDlgItem(state['window'], ident), 5 if mesh_shown else 0)
        offset = 42 if shown else 0
        for ident, rect in ((234, (10, 83 + offset, 490, 97 + offset)),
                            (235, (10, 104 + offset, 320, 118 + offset)),
                            (236, (326, 105 + offset, 434, 119 + offset)),
                            (237, (440, 102 + offset, 490, 120 + offset)),
                            (238, (26, 126 + offset, 366, 140 + offset)),
                            (239, (374, 123 + offset, 490, 141 + offset)),
                            (101, (10, 126 + offset + (21 if mesh_shown else 0), 490, 322))):
            box = w.RECT(*rect)
            user.MapDialogRect(state['window'], ctypes.byref(box))
            user.MoveWindow(user.GetDlgItem(state['window'], ident), box.left, box.top,
                            box.right - box.left, box.bottom - box.top, True)
        user.InvalidateRect(state['window'], None, True)

    def model_accuracy():
        value = ctypes.create_unicode_buffer(64)
        user.GetWindowTextW(user.GetDlgItem(state['window'], 237), value, len(value))
        return parse_project_model_accuracy(value.value)

    def model_triangle_limit():
        value = ctypes.create_unicode_buffer(64)
        user.GetWindowTextW(user.GetDlgItem(state['window'], 239), value, len(value))
        return parse_project_model_triangle_limit(value.value)

    def option_feedback():
        enabled = user.SendMessageW(user.GetDlgItem(state['window'], 235), 0xF0, 0, 0) == 1
        error = ''
        if enabled:
            try:
                model_accuracy()
                model_triangle_limit()
            except ValueError as exc:
                error = str(exc)
        text = 'Выбрано установов: %d' % len(model.selected)
        user.SetWindowTextW(user.GetDlgItem(state['window'], 102), text + (' · ' + error if error else ''))
        user.EnableWindow(user.GetDlgItem(state['window'], 1), bool(model.selected) and not error)
        return not error

    @subclass_type
    def option_proc(window, message, wparam, lparam, subclass_id, ref_data):
        # The first click already toggles the native checkbox (including its
        # caption). A double click must not immediately toggle it back.
        if message == 0x0203:
            state['double_click'].add(window)
            return 0
        if message == 0x0202 and window in state['double_click']:
            state['double_click'].discard(window)
            return 0
        if message == 0x0082:
            state['double_click'].discard(window)
            common.RemoveWindowSubclass(window, option_proc, 2)
        return common.DefSubclassProc(window, message, wparam, lparam)

    def sync():
        state['updating'] = True
        try:
            for row in rows:
                key = row['key']
                item = TVITEMW()
                item.mask, item.hItem, item.stateMask = 0x8, state['handles'][key], 0xF004
                item.state = (0x2000 if key in model.selected else 0x1000) | (4 if model.disabled(key) else 0)
                if not user.SendMessageW(state['tree'], 0x113F, 0, ctypes.addressof(item)):
                    raise RuntimeError('Не удалось обновить папку: ' + row['name'])
            option_feedback()
            user.InvalidateRect(state['tree'], None, False)
        finally:
            state['updating'] = False

    def toggle(handle):
        key = state['keys'].get(handle)
        if key is not None and model.toggle(key, key not in model.selected):
            sync()

    @subclass_type
    def tree_proc(window, message, wparam, lparam, subclass_id, ref_data):
        try:
            # Handle checkboxes ourselves on both old and themed common controls.
            # Labels/arrows remain usable for expanding blocked ancestor folders.
            if message in (0x0201, 0x0203):
                hit = TVHITTESTINFO()
                hit.pt = w.POINT(ctypes.c_short(lparam & 0xFFFF).value,
                                 ctypes.c_short((lparam >> 16) & 0xFFFF).value)
                user.SendMessageW(window, 0x1111, 0, ctypes.addressof(hit))
                if hit.hItem and hit.flags & 0x40:
                    user.SetFocus(window)
                    user.SendMessageW(window, 0x110B, 9, hit.hItem)
                    if message == 0x0201:
                        toggle(hit.hItem)
                    return 0
                if message == 0x0203 and hit.hItem and hit.flags & 0x6:
                    # Double-click the folder label/icon: check once; arrows
                    # still expand/collapse, disabled relatives remain blocked.
                    key = state['keys'].get(hit.hItem)
                    if key is not None and not model.disabled(key):
                        user.SetFocus(window)
                        user.SendMessageW(window, 0x110B, 9, hit.hItem)
                        if model.toggle(key, True):
                            sync()
                        return 0
            if message == 0x0100 and wparam == 0x20:
                if not lparam & (1 << 30):
                    toggle(user.SendMessageW(window, 0x110A, 9, 0))
                return 0
            if message in (0x0101, 0x0102) and wparam == 0x20:
                return 0
            if message == 0x0082:
                common.RemoveWindowSubclass(window, tree_proc, 1)
        except Exception as exc:
            state['error'] = exc
            user.EndDialog(state['window'], 2)
            return 0
        return common.DefSubclassProc(window, message, wparam, lparam)

    def notify_result(window, value):
        # A dialog must return notification results through DWLP_MSGRESULT.
        user.SetWindowLongPtrW(window, 0, value)
        return 1

    @callback_type
    def callback(window, message, wparam, lparam):
        try:
            if message == 0x0110:
                state['window'], state['tree'] = window, user.GetDlgItem(window, 101)
                for ident in (230, 231, 232, 233, 234, 235):
                    control = user.GetDlgItem(window, ident)
                    user.SendMessageW(control, 0xF1, int(ident == 232), 0)
                    if not common.SetWindowSubclass(control, option_proc, 2, 0):
                        raise RuntimeError('Не удалось подключить опции подготовки карты.')
                user.SendMessageW(user.GetDlgItem(window, 237), 0x00C5, 24, 0)  # EM_SETLIMITTEXT
                user.SendMessageW(user.GetDlgItem(window, 239), 0x00C5, 24, 0)
                description_formats()
                if not state['tree']:
                    raise RuntimeError('Не удалось создать дерево папок.')
                state['updating'] = True
                user.SendMessageW(state['tree'], 0x2005, 1, 0)  # Unicode notifications.
                for row in rows:
                    label = labels[row['key']]
                    buffer = ctypes.create_unicode_buffer(label)
                    insert = TVINSERTSTRUCTW()
                    insert.hParent = state['handles'].get(row['parent'], ctypes.c_void_p(-0x10000).value)
                    insert.hInsertAfter = ctypes.c_void_p(-0x0FFFE).value  # TVI_LAST, never sort.
                    insert.item.mask, insert.item.stateMask, insert.item.state = 0x9, 0xF000, 0x1000
                    insert.item.pszText = ctypes.cast(buffer, w.LPWSTR)
                    handle = user.SendMessageW(state['tree'], 0x1132, 0, ctypes.addressof(insert))
                    if not handle:
                        raise RuntimeError('Не удалось добавить папку: ' + row['name'])
                    state['handles'][row['key']], state['keys'][handle] = handle, row['key']
                state['images'] = user.SendMessageW(state['tree'], 0x1108, 2, 0)
                for row in rows:
                    if row['name'].strip().upper() != 'NONE':
                        user.SendMessageW(state['tree'], 0x1102, 2, state['handles'][row['key']])
                if not common.SetWindowSubclass(state['tree'], tree_proc, 1, 0):
                    raise RuntimeError('Не удалось подключить выбор папок.')
                sync()
                if rows:
                    user.SendMessageW(state['tree'], 0x110B, 9, state['handles'][rows[0]['key']])
                user.SetFocus(state['tree'])
                return 0
            if message == 0x004E and lparam:
                header = ctypes.cast(lparam, ctypes.POINTER(NMHDR)).contents
                if header.idFrom == 101:
                    code = ctypes.c_int(header.code).value
                    if code == -12:  # NM_CUSTOMDRAW
                        return notify_result(window, choice_rows.paint(lparam))
                    if not state['updating'] and code in (-416, -417, -418, -419):
                        notice = ctypes.cast(lparam, ctypes.POINTER(NMTVITEMCHANGE)).contents
                        key = state['keys'].get(notice.hItem)
                        if key is not None and (notice.uStateOld ^ notice.uStateNew) & 0xF000:
                            checked = (notice.uStateNew & 0xF000) == 0x2000
                            if code in (-416, -417):
                                return notify_result(window, int(checked and model.disabled(key)))
                            model.toggle(key, checked)
                            sync()
                return 0
            if message == 0x0010:
                user.EndDialog(window, 2)
                return 1
            if message == 0x0111:
                ident = wparam & 0xFFFF
                if ident in (231, 235):
                    description_formats()
                    option_feedback()
                    return 1
                if ident in (237, 239) and wparam >> 16 == 0x0300 and state['window']:  # EN_CHANGE
                    option_feedback()
                    return 1
                if ident == 2 or (ident == 1 and model.selected):
                    if ident == 1:
                        if not option_feedback():
                            try:
                                model_accuracy()
                                invalid = 239
                            except ValueError:
                                invalid = 237
                            user.SetFocus(user.GetDlgItem(window, invalid))
                            return 1
                        for key, control in (('number_operations', 230), ('update_descriptions', 231),
                                             ('include_tool_numbers', 233), ('add_zmin', 234),
                                             ('create_project_model', 235)):
                            state['options'][key] = user.SendMessageW(user.GetDlgItem(window, control), 0xF0, 0, 0) == 1
                        if state['options']['create_project_model']:
                            state['options']['project_model_accuracy'] = model_accuracy()
                            state['options']['project_model_triangle_limit'] = model_triangle_limit()
                    user.EndDialog(window, ident)
                    return 1
                if ident == 222:
                    model.selected.clear()
                    sync()
                    return 1
        except Exception as exc:
            state['error'] = exc
            user.EndDialog(window, 2)
            return 1
        return 0

    template = ctypes.create_string_buffer(setup_folders_dialog_template())
    try:
        owner = user.GetActiveWindow() or user.GetForegroundWindow()
        result = user.DialogBoxIndirectParamW(None, ctypes.cast(template, ctypes.c_void_p), owner, callback, 0)
    finally:
        # Tree-view checkbox image lists are owned by their application.
        if state['images']:
            common.ImageList_Destroy(state['images'])
    if state['error'] is not None:
        raise state['error']
    if result not in (1, 2):
        raise RuntimeError('Не удалось открыть выбор установов: ' + str(ctypes.WinError(ctypes.get_last_error())))
    if result == 1 and options is not None:
        options.update(state['options'])
    return model.values() if result == 1 else None


def ask_setup_folders(nx, part):
    program = cam_tree(nx, part.CAMSetup.GetRoot(nx.CAM.CAMSetup.View.ProgramOrder))
    rows = setup_folder_inventory(program)
    if not any(row['operation_count'] for row in rows):
        raise RuntimeError('В Program Order нет папок с операциями для карты наладки.')
    uf_ui = nx.UF.UFSession.GetUFSession().Ui
    lock_source = nx.UF.UFConstants.UF_UI_FROM_CUSTOM
    uf_ui.LockUgAccess(lock_source)
    try:
        options = preparation_options()
        choices = show_setup_folders(rows, options)
    finally:
        uf_ui.UnlockUgAccess(lock_source)
    if choices is None:
        raise ExportCancelled()
    model = SetupFolderChoices(rows)
    for key in choices:
        if key in model.selected or not model.toggle(key, True):
            raise RuntimeError('Одновременно выбраны родительская папка и её подпапка.')
    if not model.selected:
        raise ExportCancelled()
    return program, [row['object'] for row in rows if row['key'] in model.selected], options


def resolve_setup_jobs(nx, ui, part, report):
    """Only the startup folder dialog defines jobs; NX preselection is ignored."""
    program, folders, options = ask_setup_folders(nx, part)
    report['_preparation_options'] = options
    selections = [[folder] for folder in folders]
    report['selection_source'] = 'startup_folder_tree'
    jobs = []
    for selection in selections:
        item = new_report()
        item['selection_source'] = report.get('selection_source')
        try:
            context = resolve_context(nx, ui, part, item, selected=selection)
            name = record_setup_identity(context, item)
            resolve_ipw_source(nx, part, context, item)
        except ExportCancelled:
            raise
        except Exception as exc:
            label = ', '.join(str(obj.Name) for obj in selection) or 'текущий установ'
            raise RuntimeError('Не удалось подготовить «%s»: %s' % (label, exc)) from exc
        item['setup_total'] = len(selections)
        item['setup_index'] = len(jobs) + 1
        item['selected_folder'] = str(selection[0].Name)
        item['selected_folder_tag'] = object_key(selection[0])
        item['selected_folder_path'] = navigator_folder_path(program, selection[0]) or []
        jobs.append({'context': context, 'report': item, 'output': None})
    return jobs


class PreparationSelection:
    """Zmin scope frozen before modal dialogs or component previews change NX selection."""
    def __init__(self, nx, ui, part):
        self.part_tag, self.operations, self.scope, self.error = object_key(part), [], '', None
        try:
            all_operations = list(part.CAMSetup.CAMOperationCollection)
            known = {object_key(obj) for obj in all_operations + list(part.CAMSetup.CAMGroupCollection)}
            operation_tags = {object_key(obj) for obj in all_operations}
            selected, navigator_error = None, None
            try:
                count, tags = nx.UF.UFSession.GetUFSession().UiOnt.AskSelectedNodes()
                tags = list(tags) if tags is not None else []
                if int(count) < 0 or int(count) != len(tags):
                    raise RuntimeError('NX вернул неполный список выбранных узлов.')
                if count:
                    selected = {str(tag) for tag in tags}
                    if not selected <= known:
                        raise RuntimeError('В выделении есть узлы другого CAM-проекта.')
            except Exception as exc:
                navigator_error, selected = exc, None
            if selected is None:
                manager = ui.SelectionManager
                objects = [manager.GetSelectedTaggedObject(i) for i in range(manager.GetNumSelectedObjects())]
                selected = {object_key(obj) for obj in objects}
                if any(isinstance(obj, nx.CAM.Operation) and object_key(obj) not in operation_tags for obj in objects):
                    raise RuntimeError('Выделена операция другой детали. Откройте её CAM-проект.')
                if navigator_error is not None and not selected & operation_tags:
                    raise RuntimeError('Не удалось надёжно прочитать выделение в навигаторе.\n'
                                       'Выделите нужные операции и повторите запуск.\n\n' + str(navigator_error))
            if not selected:
                self.operations, self.scope = all_operations, 'Все операции проекта'
            else:
                self.operations = [obj for obj in all_operations if object_key(obj) in selected]
                if not self.operations:
                    raise RuntimeError('Выделены папки или другие объекты, но не операции.\n'
                                       'Выделите операции либо полностью снимите выделение для всего проекта.')
                self.scope = 'Выделенные операции'
        except Exception as exc:
            # A disabled Zmin option must not block ordinary card generation.
            self.error = exc

    def validate(self, session, part):
        if self.error is not None:
            raise RuntimeError('Zmin: ' + str(self.error)) from self.error
        if (object_key(part) != self.part_tag or session.Parts.Work is None or session.Parts.Display is None
                or object_key(session.Parts.Work) != self.part_tag or object_key(session.Parts.Display) != self.part_tag):
            raise RuntimeError('Zmin: CAM-проект должен оставаться рабочей и отображаемой деталью.')


def operation_numbering_plan(jobs):
    """Restart numbering in each chosen program folder, preserving its NX order."""
    planned, seen = [], set()
    for job in jobs:
        operations = job['context']['operations']
        width = 2 if len(operations) <= 99 else 3
        for index, operation in enumerate(operations, 1):
            key, old = object_key(operation), str(operation.Name)
            if key in seen:
                raise RuntimeError('Операция попала в несколько выводимых установов: ' + old)
            seen.add(key)
            body = re.sub(r'\A(?:[0-9]+_)+', '', old)
            if not body:
                match = re.search(r'([0-9]+)_$', old)
                body = match.group(1) if match else 'Операция'
            new = str(index).zfill(width) + '_' + body
            if new != old:
                planned.append(dict(op=operation, tag=key, old=old, new=new))
    return planned


def zmin_operation_name(name, value):
    body = re.sub(r'(?:_Z[+-]?(?:[0-9]+(?:[.,][0-9]*)?|[.,][0-9]+))+$', '', str(name), flags=re.I)
    if not body:
        raise ValueError('После удаления старого суффикса Z имя операции пустое.')
    number = ('%.4f' % finite_number(value)).rstrip('0').rstrip('.')
    return body + '_Z' + ('0' if number == '-0' else number)


def preparation_progress(stage, detail=''):
    if EXPORT_PROGRESS is not None:
        EXPORT_PROGRESS.update(stage=stage, detail=detail)


def zmin_rename_plan(nx, part, operations):
    planned, skipped, unchanged, values, frames, frame_errors = [], [], 0, {}, {}, {}
    status_type = getattr(getattr(nx.CAM, 'CAMObject', None), 'Status', None)
    for index, operation in enumerate(operations, 1):
        old = str(operation.Name)
        try:
            preparation_progress('Zmin · ' + old, 'Операция %d из %d' % (index, len(operations)))
            if not operation.AskPathExists():
                raise ValueError('Нет рассчитанной траектории.')
            if enum_name(operation.GetStatus(), status_type, ('Complete', 'Approved', 'Regen', 'Repost')) == 'Regen':
                raise ValueError('Траектория устарела: требуется пересчёт в NX.')
            group = nearest_mcs(nx, operation)
            if group is None:
                raise ValueError('Не найдена СКС операции.')
            key = object_key(group)
            if key in frame_errors:
                raise ValueError(frame_errors[key])
            if key not in frames:
                try:
                    frames[key] = read_mcs(part, group)
                except Exception as exc:
                    frame_errors[key] = 'Не удалось прочитать СКС: ' + str(exc)
                    raise ValueError(frame_errors[key]) from exc
            basis, origin = frames[key]
            def progress(completed, count):
                preparation_progress('Zmin · ' + old, 'Операция %d/%d · Движения: %d/%d' %
                                     (index, len(operations), completed, count))
            value = toolpath_zmin(nx, operation, basis, origin, progress)
            if value is None:
                raise ValueError('В траектории нет доступных перемещений.')
            values[object_key(operation)] = value
            new = zmin_operation_name(old, value)
            if new == old:
                unchanged += 1
            else:
                planned.append(dict(op=operation, tag=object_key(operation), old=old, new=new))
        except Exception as exc:
            skipped.append((old, str(exc)))
    return planned, skipped, unchanged, values


def remove_rename_conflicts(planned, objects):
    """Same collision rules as Zmin: skip ambiguous names, including blocking chains."""
    targets = {}
    for item in planned:
        targets.setdefault(item['new'].casefold(), []).append(item)
    remaining, skipped = [], []
    for item in planned:
        if len(targets[item['new'].casefold()]) > 1:
            skipped.append((item['old'], 'Несколько операций получат имя «' + item['new'] + '».'))
        else:
            remaining.append(item)
    while remaining:
        moving = {item['tag'] for item in remaining}
        occupied = {str(obj.Name).casefold() for obj in objects if object_key(obj) not in moving}
        blocked = [item for item in remaining if item['new'].casefold() in occupied]
        if not blocked:
            break
        blocked_tags = {item['tag'] for item in blocked}
        for item in blocked:
            skipped.append((item['old'], 'Имя «' + item['new'] + '» уже занято в CAM-проекте.'))
        remaining = [item for item in remaining if item['tag'] not in blocked_tags]
    return remaining, skipped


def apply_operation_names(planned, objects):
    """Use temporary NX names for swaps; the enclosing preparation owns rollback."""
    reserved = {str(obj.Name).casefold() for obj in objects} | {item['new'].casefold() for item in planned}
    temporary, number = [], 1
    for item in planned:
        name = 'NXPREP_' + str(number)
        # Case-insensitive comparison must include existing mixed-case names.
        while name.casefold() in reserved:
            number += 1
            name = 'NXPREP_' + str(number)
        reserved.add(name.casefold())
        temporary.append(name)
        number += 1
    for names in (temporary, [item['new'] for item in planned]):
        for item, name in zip(planned, names):
            item['op'].SetName(name)
            actual = str(item['op'].Name)
            if actual != name:
                raise RuntimeError('NX изменил запрошенное имя «%s» на «%s».' % (name, actual))


def tool_description_numbers(uf, tool):
    t = uf.Param.AskIntValue(tool.Tag, 1038)  # UF_PARAM_TL_NUMBER
    h = uf.Param.AskIntValue(tool.Tag, 1040)  # UF_PARAM_TL_ADJ_REG
    try:
        status = str(uf.Param.AskParamStatus(tool.Tag, 1041)).rsplit('.', 1)[-1].lower().replace('_', '')
        d = None if status in ('3', 'invalidindex', 'ufparaminvalidindex') else uf.Param.AskIntValue(tool.Tag, 1041)
    except Exception as exc:
        if getattr(exc, 'ErrorCode', None) != 1345036:  # Explicit UF_CAM_ERROR_INVALID_INDEX only.
            raise
        d = None
    return t, h, d


def tool_general_description(part, tool, value=None):
    builder = part.CAMSetup.CAMGroupCollection.CreateNcgroupBuilder(tool)
    try:
        if value is not None:
            builder.Description = value
            builder.Commit()
        return builder.Description or ''
    finally:
        builder.Destroy()


def apply_tool_description(nx, part, uf, tool, include_numbers):
    family, subtype = tool.GetTypeAndSubtype()
    if enum_name(family, nx.CAM.Tool.Types, ('Mill', 'Drill', 'Barrel', 'Tcutter', 'MillForm')) not in (
            'Mill', 'Drill', 'Barrel', 'Tcutter', 'MillForm'):
        return False
    diameter = float(uf.Param.AskDoubleValue(tool.Tag, 1000))  # UF_PARAM_TL_DIAMETER
    if not math.isfinite(diameter) or diameter <= 0:
        return False
    rounded = Decimal(str(diameter)).quantize(Decimal('0.000001'), rounding=ROUND_HALF_UP)
    if rounded <= 0:
        return False
    value = '⌀' + format(rounded, 'f').rstrip('0').rstrip('.')
    numbers = tool_description_numbers(uf, tool) if include_numbers else None
    if numbers is not None:
        value += '_T%d_H%d' % numbers[:2]
        if numbers[2] is not None:
            value += '_D%d' % numbers[2]
    general = tool_general_description(part, tool)
    cutter = uf.Param.AskStrValue(tool.Tag, 1068) or ''  # UF_PARAM_TL_DESCRIPTION
    if general == value and cutter == value:
        return False
    if general != value:
        tool_general_description(part, tool, value)
    if (uf.Param.AskStrValue(tool.Tag, 1068) or '') != value:
        uf.Param.SetStrValue(tool.Tag, 1068, value)
    if tool_general_description(part, tool) != value or uf.Param.AskStrValue(tool.Tag, 1068) != value:
        raise RuntimeError('NX не подтвердил записанный текст описания.')
    after = finite_number(uf.Param.AskDoubleValue(tool.Tag, 1000))
    if abs(after - diameter) > max(1e-9, abs(diameter) * 1e-12):
        raise RuntimeError('Контроль диаметра после записи не пройден.')
    if numbers is not None and tool_description_numbers(uf, tool) != numbers:
        raise RuntimeError('Контроль номеров T/H/D после записи не пройден.')
    return True


def update_tool_descriptions(nx, part, session, uf, batch_mark, objects, include_numbers):
    tools = [obj for obj in objects if isinstance(obj, nx.CAM.Tool) and obj.OwningPart is not None
             and object_key(obj.OwningPart) == object_key(part)]
    changed, skipped = 0, []
    for index, tool in enumerate(tools, 1):
        preparation_progress('Description инструментов', '%d / %d · %s' % (index, len(tools), tool.Name))
        mark = session.SetUndoMark(nx.Session.MarkVisibility.Invisible, TITLE + ' — Description')
        try:
            if apply_tool_description(nx, part, uf, tool, include_numbers):
                changed += 1
        except Exception as exc:
            try:
                session.UndoToMark(mark, None)
            except Exception as restore:
                raise RuntimeError('Не удалось отменить изменение инструмента «%s»: %s. Исходная ошибка: %s' %
                                   (tool.Name, restore, exc)) from restore
            skipped.append((str(tool.Name), str(exc)))
        finally:
            session.DeleteUndoMark(mark, None)
    if changed:
        errors = session.UpdateManager.DoUpdate(batch_mark)
        if errors:
            raise RuntimeError('NX сообщил об ошибках обновления Description: ' + str(errors))
    return changed, skipped


class SetupPreparation:
    """Keep preparation only after HTML publication; no files and no part save."""
    def __init__(self, nx, part, jobs, options, selection):
        self.nx, self.part, self.jobs, self.options, self.selection = nx, part, jobs, options, selection
        self.session, self.mark = nx.Session.GetSession(), None
        self.messages, self.warnings, self.original_names = [], [], []

    def apply(self):
        options = self.options
        if not any(options.get(key) for key in ('number_operations', 'update_descriptions', 'add_zmin')):
            return
        if options.get('add_zmin'):
            self.selection.validate(self.session, self.part)
        uf = self.nx.UF.UFSession.GetUFSession()
        objects = list(self.part.CAMSetup.CAMOperationCollection) + list(self.part.CAMSetup.CAMGroupCollection)
        self.original_names = [(obj, str(obj.Name)) for obj in objects if isinstance(obj, self.nx.CAM.Operation)]
        self.mark = self.session.SetUndoMark(self.nx.Session.MarkVisibility.Visible, TITLE + ' — Подготовка карты')
        if options.get('update_descriptions'):
            changed, skipped = update_tool_descriptions(self.nx, self.part, self.session, uf, self.mark,
                                                       objects, options.get('include_tool_numbers', False))
            self.messages.append('Description: обновлено инструментов — %d; ошибок — %d.' % (changed, len(skipped)))
            self.warnings.extend('%s: %s' % item for item in skipped)
        if options.get('number_operations'):
            preparation_progress('Нумерация операций')
            planned = operation_numbering_plan(self.jobs)
            allowed, conflicts = remove_rename_conflicts(planned, objects)
            if conflicts:
                raise RuntimeError('Нумерация: ' + '\n'.join('%s: %s' % item for item in conflicts[:8]))
            apply_operation_names(allowed, objects)
            self.messages.append('Нумерация: переименовано операций — %d.' % len(allowed))
        if options.get('add_zmin'):
            planned, skipped, unchanged, values = zmin_rename_plan(self.nx, self.part, self.selection.operations)
            planned, conflicts = remove_rename_conflicts(planned, objects)
            skipped.extend(conflicts)
            apply_operation_names(planned, objects)
            self.messages.append('Zmin: %s. Переименовано — %d; уже актуальны — %d; пропущено — %d.' %
                                 (self.selection.scope, len(planned), unchanged, len(skipped)))
            self.warnings.extend('%s: %s' % item for item in skipped)
            for job in self.jobs:
                job['context']['prepared_zmin'] = values
        for job in self.jobs:
            record_setup_identity(job['context'], job['report'])
        self.refresh()

    def refresh(self):
        try:
            self.nx.UF.UFSession.GetUFSession().UiOnt.Refresh()
        except Exception as exc:
            self.warnings.append('Не удалось обновить навигатор NX: ' + str(exc))

    def finish(self, published):
        if self.mark is None:
            return
        if not published:
            try:
                self.session.UndoToMark(self.mark, None)
                if any(str(obj.Name) != name for obj, name in self.original_names):
                    raise RuntimeError('NX не восстановил исходные имена операций.')
                self.session.DeleteUndoMark(self.mark, None)
                self.mark = None
            except Exception as exc:
                raise RuntimeError('Не удалось полностью отменить подготовку карты. Проверьте имена операций '
                                   'и Description инструментов; выполните отмену в NX.\n' + str(exc)) from exc
            self.refresh()

    def result(self):
        lines = self.messages[:]
        if self.warnings:
            lines.append('Пропуски и замечания:\n' + '\n'.join(self.warnings[:8]))
            if len(self.warnings) > 8:
                lines.append('И ещё %d.' % (len(self.warnings) - 8))
        if self.mark is not None:
            lines.append('Отмена подготовки в NX: Ctrl+Z. Файл .prt автоматически не сохранён.')
        return '\n'.join(lines)


def component_inventory(part):
    """Read assembly occurrences, never change prototypes, arrangements or refsets."""
    root = part.ComponentAssembly.RootComponent
    if root is None:
        return []
    rows, seen = [], set()

    def walk(parent, ancestors=(), names=(), parent_visible=True):
        for component in parent.GetChildren():
            key = object_key(component)
            if key in seen:
                raise RuntimeError('Компонент повторно встретился в дереве сборки: ' + key)
            seen.add(key)
            name = str(component.DisplayName or component.Name or key)
            instance_name = str(component.Name or '')
            if instance_name and instance_name != name:
                name += ' [' + instance_name + ']'
            path = ' / '.join(names + (name,))
            suppressed = bool(component.IsSuppressed)
            blanked = None if suppressed else bool(component.IsBlanked)
            visible = parent_visible and not suppressed and not blanked
            rows.append({'object': component, 'key': key, 'name': name, 'path': path,
                         'ancestors': ancestors, 'suppressed': suppressed,
                         'blanked': blanked, 'visible': visible})
            # Suppressed branches must stay suppressed; do not load them to enumerate.
            if not suppressed:
                walk(component, ancestors + (key,), names + (name,), visible)

    walk(root)
    return rows


def normalized_component_choice(rows, selected):
    """A visible child needs visible ancestors, not visible siblings."""
    by_key = {row['key']: row for row in rows if not row['suppressed']}
    result = set(selected)
    unknown = result - set(by_key)
    if unknown:
        raise ValueError('Выбран недоступный компонент сборки: ' + ', '.join(sorted(unknown)))
    for key in tuple(result):
        result.update(by_key[key]['ancestors'])
    return result


def toggle_component_choice(rows, selected, key, checked):
    by_key = {row['key']: row for row in rows}
    row = by_key[key]
    if row['suppressed']:
        return set(selected)
    branch = {r['key'] for r in rows
              if not r['suppressed'] and (r['key'] == key or key in r['ancestors'])}
    result = set(selected)
    if checked:
        result.update(branch)
    else:
        result.difference_update(branch)
    return normalized_component_choice(rows, result)


class AssemblyComponentDisplay:
    """Per-export visibility guard; the saved snapshot is shared by all setups."""
    def __init__(self, nx, part):
        self.nx, self.part = nx, part
        self.rows = component_inventory(part)
        self.dirty = False

    def refresh(self):
        self.part.Views.Refresh()
        self.nx.UF.UFSession.GetUFSession().Disp.MakeDisplayUpToDate()

    def apply(self, selected):
        # An empty selection (including the global No answer) hides all occurrences.
        selected = normalized_component_choice(self.rows, selected)
        self.dirty = True  # A failing NX call may have changed the display already.
        changed = False
        for row in self.rows:
            if row['key'] in selected and row['object'].IsBlanked:
                diagnostic_event('component.show.begin', component=row['path'])
                row['object'].Unblank()
                diagnostic_event('component.show.end', component=row['path'])
                changed = True
        # Descendants first: hide every unchecked occurrence, including siblings.
        for row in reversed(self.rows):
            if not row['suppressed'] and row['key'] not in selected and not row['object'].IsBlanked:
                diagnostic_event('component.hide.begin', component=row['path'])
                row['object'].Blank()
                diagnostic_event('component.hide.end', component=row['path'])
                changed = True
        for row in self.rows:
            if not row['suppressed'] and bool(row['object'].IsBlanked) != (row['key'] not in selected):
                raise RuntimeError('NX не применил видимость компонента «%s».' % row['path'])
        self.refresh()
        return changed

    def restore(self):
        if not self.dirty:
            return []
        errors = []
        # Restore every recorded flag, not just the components last selected.
        # Show parents before children; hide children before their parents.
        ordered = ([row for row in self.rows if row['blanked'] is False] +
                   [row for row in reversed(self.rows) if row['blanked'] is True])
        for row in ordered:
            try:
                if bool(row['object'].IsBlanked) != row['blanked']:
                    diagnostic_event('component.restore.begin', component=row['path'], blanked=row['blanked'])
                    (row['object'].Blank if row['blanked'] else row['object'].Unblank)()
                    diagnostic_event('component.restore.end', component=row['path'])
            except Exception as exc:
                errors.append('«%s»: %s' % (row['path'], exc))
        for row in ordered:
            try:
                if bool(row['object'].IsBlanked) != row['blanked']:
                    errors.append('«%s»: исходная видимость не восстановлена.' % row['path'])
            except Exception as exc:
                errors.append('Проверка «%s»: %s' % (row['path'], exc))
        try:
            self.refresh()
        except Exception as exc:
            errors.append('Обновление сборки: ' + str(exc))
        self.dirty = bool(errors)
        return errors


class SetupComponentChoices:
    """Independent in-memory choices; changing the active row never edits NX."""
    def __init__(self, rows, count):
        if count < 1:
            raise ValueError('Не выбраны установы.')
        self.rows, self.active = rows, 0
        self.initial = normalized_component_choice(rows, {r['key'] for r in rows if r['visible']})
        self.selected = [set() for _ in range(count)]

    def select_setup(self, index):
        if not 0 <= index < len(self.selected):
            raise ValueError('Неизвестный установ.')
        self.active = index

    def toggle(self, key, checked):
        self.selected[self.active] = toggle_component_choice(
            self.rows, self.selected[self.active], key, checked)

    def reset(self, action):
        choices = {'all': {r['key'] for r in self.rows if not r['suppressed']},
                   'none': set(), 'nx': self.initial}
        self.selected[self.active] = set(choices[action])

    def values(self):
        return [normalized_component_choice(self.rows, choice) for choice in self.selected]


def setups_components_dialog_template():
    def text(value):
        return (value + '\0').encode('utf-16-le')
    controls = [
        (0x82, 100, 'Выберите установ слева и настройте его компоненты справа. '
         'Настройки каждого установа независимы.', 10, 9, 620, 23, 0),
        ('SysListView32', 106, '', 10, 37, 174, 260, 0x81000D),
        (0x81, 103, '', 194, 37, 436, 32, 0x200844),
        (0x82, 105, 'Будут видны только отмеченные компоненты. Без отметок все компоненты скрыты. '
         'Выбор сборки меняет всю её ветку; вложенные компоненты можно настроить отдельно.',
         194, 75, 436, 37, 0),
        ('SysListView32', 101, '', 194, 117, 436, 153, 0x83000D),
        (0x82, 102, '', 194, 276, 436, 23, 0),
        (0x80, 221, 'Все', 194, 307, 48, 22, 0x30000),
        (0x80, 222, 'Снять все', 248, 307, 64, 22, 0x10000),
        (0x80, 223, 'Как в NX', 318, 307, 64, 22, 0x10000),
        (0x80, 1, 'Создать карту', 450, 307, 100, 22, 0x30001),
        (0x80, 2, 'Отмена', 558, 307, 72, 22, 0x10000),
    ]
    # The resource is built in memory: no .dlx, .tcl or settings files.
    data = bytearray(struct.pack('<IIHhhhh', 0x80C808C0, 0, len(controls), 0, 0, 640, 340))
    data += struct.pack('<HH', 0, 0) + text(TITLE + ' — Установы и компоненты')
    data += struct.pack('<H', 9) + text('Segoe UI')
    for cls, ident, label, x, y, width, height, style in controls:
        data += b'\0' * (-len(data) % 4)
        data += struct.pack('<IIhhhhH', 0x50000000 | style, 0, x, y, width, height, ident)
        data += struct.pack('<HH', 0xFFFF, cls) if isinstance(cls, int) else text(cls)
        data += text(label) + struct.pack('<H', 0)
    return bytes(data)


def show_all_setup_components(rows, reports, preview=None):
    """One modal dialog for the complete batch; Cancel discards every choice."""
    import ctypes
    from ctypes import wintypes as w

    class INITCOMMONCONTROLSEX(ctypes.Structure):
        _fields_ = [('dwSize', w.DWORD), ('dwICC', w.DWORD)]

    class LVITEMW(ctypes.Structure):
        _fields_ = [('mask', w.UINT), ('iItem', ctypes.c_int), ('iSubItem', ctypes.c_int),
                    ('state', w.UINT), ('stateMask', w.UINT), ('pszText', w.LPWSTR),
                    ('cchTextMax', ctypes.c_int), ('iImage', ctypes.c_int), ('lParam', w.LPARAM),
                    ('iIndent', ctypes.c_int), ('iGroupId', ctypes.c_int), ('cColumns', w.UINT),
                    ('puColumns', ctypes.POINTER(w.UINT)), ('piColFmt', ctypes.POINTER(ctypes.c_int)),
                    ('iGroup', ctypes.c_int)]

    class LVCOLUMNW(ctypes.Structure):
        _fields_ = [('mask', w.UINT), ('fmt', ctypes.c_int), ('cx', ctypes.c_int),
                    ('pszText', w.LPWSTR), ('cchTextMax', ctypes.c_int), ('iSubItem', ctypes.c_int),
                    ('iImage', ctypes.c_int), ('iOrder', ctypes.c_int), ('cxMin', ctypes.c_int),
                    ('cxDefault', ctypes.c_int), ('cxIdeal', ctypes.c_int)]

    class NMHDR(ctypes.Structure):
        _fields_ = [('hwndFrom', w.HWND), ('idFrom', ctypes.c_size_t), ('code', w.UINT)]

    class NMLISTVIEW(ctypes.Structure):
        _fields_ = [('hdr', NMHDR), ('iItem', ctypes.c_int), ('iSubItem', ctypes.c_int),
                    ('uNewState', w.UINT), ('uOldState', w.UINT), ('uChanged', w.UINT),
                    ('ptAction', w.POINT), ('lParam', w.LPARAM)]

    class NMITEMACTIVATE(ctypes.Structure):
        _fields_ = NMLISTVIEW._fields_ + [('uKeyFlags', w.UINT)]

    class LVHITTESTINFO(ctypes.Structure):
        _fields_ = [('pt', w.POINT), ('flags', w.UINT), ('iItem', ctypes.c_int),
                    ('iSubItem', ctypes.c_int), ('iGroup', ctypes.c_int)]

    user = ctypes.WinDLL('user32', use_last_error=True)
    common = ctypes.WinDLL('comctl32', use_last_error=True)
    callback_type = ctypes.WINFUNCTYPE(ctypes.c_ssize_t, w.HWND, w.UINT, w.WPARAM, w.LPARAM)
    signatures = {
        'GetActiveWindow': ([], w.HWND), 'GetForegroundWindow': ([], w.HWND),
        'GetDlgItem': ([w.HWND, ctypes.c_int], w.HWND),
        'SetFocus': ([w.HWND], w.HWND), 'EndDialog': ([w.HWND, ctypes.c_ssize_t], w.BOOL),
        'SetWindowTextW': ([w.HWND, w.LPCWSTR], w.BOOL),
        'SendMessageW': ([w.HWND, w.UINT, w.WPARAM, w.LPARAM], ctypes.c_ssize_t),
        'GetClientRect': ([w.HWND, ctypes.POINTER(w.RECT)], w.BOOL),
        'SetWindowLongPtrW': ([w.HWND, ctypes.c_int, ctypes.c_ssize_t], ctypes.c_ssize_t),
        'DialogBoxIndirectParamW': ([w.HINSTANCE, ctypes.c_void_p, w.HWND, callback_type, w.LPARAM], ctypes.c_ssize_t),
    }
    for name, (arguments, result) in signatures.items():
        function = getattr(user, name)
        function.argtypes, function.restype = arguments, result
    common.InitCommonControlsEx.argtypes = [ctypes.POINTER(INITCOMMONCONTROLSEX)]
    common.InitCommonControlsEx.restype = w.BOOL
    init = INITCOMMONCONTROLSEX(ctypes.sizeof(INITCOMMONCONTROLSEX), 1)
    if not common.InitCommonControlsEx(ctypes.byref(init)):
        raise ctypes.WinError(ctypes.get_last_error())
    model = SetupComponentChoices(rows, len(reports))
    state = {'updating': False, 'error': None, 'list': None, 'setups': None, 'preview': None}
    labels = [r['path'] + (' — ПОДАВЛЕН' if r['suppressed'] else '') for r in rows]
    choice_rows = NativeChoiceRows(False, lambda index: (labels[index], rows[index]['suppressed'])
                                   if 0 <= index < len(rows) else None)

    def fill_list(listing, heading, labels, checkboxes=False):
        if not listing:
            raise RuntimeError('Не удалось создать список: ' + heading)
        user.SendMessageW(listing, 0x1036, 0, 0x10000 | 0x4000 | 0x20 | (0x4 if checkboxes else 0))
        rect = w.RECT()
        if not user.GetClientRect(listing, ctypes.byref(rect)):
            raise ctypes.WinError(ctypes.get_last_error())
        header = ctypes.create_unicode_buffer(heading)
        column = LVCOLUMNW()
        column.mask, column.cx = 0x6, max(100, rect.right - rect.left - 24)
        column.pszText = ctypes.cast(header, w.LPWSTR)
        if user.SendMessageW(listing, 0x1061, 0, ctypes.addressof(column)) < 0:
            raise RuntimeError('Не удалось создать столбец: ' + heading)
        for i, label in enumerate(labels):
            buffer = ctypes.create_unicode_buffer(label)
            item = LVITEMW()
            item.mask, item.iItem = 0x1, i
            item.pszText = ctypes.cast(buffer, w.LPWSTR)
            if user.SendMessageW(listing, 0x104D, 0, ctypes.addressof(item)) != i:
                raise RuntimeError('Не удалось добавить строку: ' + label)
        user.SendMessageW(listing, 0x101E, 0, -1)
        width = user.SendMessageW(listing, 0x101D, 0, 0)
        user.SendMessageW(listing, 0x101E, 0, max(width + 24, column.cx))

    def sync(window):
        state['updating'] = True
        try:
            active = model.active
            selected = model.selected[active]
            report = reports[active]
            label = 'Установ «%s» (%d/%d)' % (setup_label(report), active + 1, len(reports))
            if report.get('selected_folder'):
                label += '\r\nПапка: ' + report['selected_folder']
            user.SetWindowTextW(user.GetDlgItem(window, 103), label)
            for i, row in enumerate(rows):
                item = LVITEMW()
                item.stateMask = 0xF000
                item.state = 0x2000 if row['key'] in selected else 0x1000
                if not user.SendMessageW(state['list'], 0x102B, i, ctypes.addressof(item)):
                    raise RuntimeError('Не удалось установить отметку: ' + row['path'])
            suppressed = sum(r['suppressed'] for r in rows)
            status = 'Отмечено: %d из %d. Подавлено: %d (недоступны).' % (
                len(selected), len(rows) - suppressed, suppressed)
            user.SetWindowTextW(user.GetDlgItem(window, 102), status)
            snapshot = (active, frozenset(selected))
            if preview is not None and snapshot != state['preview']:
                preview(set(selected))
                state['preview'] = snapshot
        finally:
            state['updating'] = False

    @callback_type
    def callback(window, message, wparam, lparam):
        try:
            if message == 0x0110:
                state['updating'] = True
                state['list'], state['setups'] = user.GetDlgItem(window, 101), user.GetDlgItem(window, 106)
                fill_list(state['setups'], 'Установы', ['%d. %s%s' % (i, setup_label(r),
                    ' — ' + r['selected_folder'] if r.get('selected_folder') else '')
                    for i, r in enumerate(reports, 1)])
                fill_list(state['list'], 'Компоненты детали и оснастки',
                          labels, True)
                item = LVITEMW()
                item.stateMask, item.state = 3, 3  # Focused and selected.
                if not user.SendMessageW(state['setups'], 0x102B, 0, ctypes.addressof(item)):
                    raise RuntimeError('Не удалось выбрать первый установ.')
                sync(window)
                user.SetFocus(state['setups'])
                return 0
            if message == 0x004E and lparam:
                header = ctypes.cast(lparam, ctypes.POINTER(NMHDR)).contents
                code = ctypes.c_int(header.code).value
                if header.idFrom == 101 and code == -12:  # Paint even while selection is being synchronized.
                    user.SetWindowLongPtrW(window, 0, choice_rows.paint(lparam))
                    return 1
                if state['updating']:
                    return 0
                if header.idFrom == 101 and code == -3:  # NM_DBLCLK.
                    notice = ctypes.cast(lparam, ctypes.POINTER(NMITEMACTIVATE)).contents
                    hit = LVHITTESTINFO()
                    hit.pt, hit.iItem = notice.ptAction, -1
                    index = int(user.SendMessageW(state['list'], 0x1039, 0, ctypes.addressof(hit)))
                    # Checkbox clicks already use the native state-change handler.
                    if 0 <= index < len(rows) and not (hit.flags & 0x8) and not rows[index]['suppressed']:
                        model.toggle(rows[index]['key'], True)
                        sync(window)
                elif code == -101:
                    notice = ctypes.cast(lparam, ctypes.POINTER(NMLISTVIEW)).contents
                    if header.idFrom == 106 and 0 <= notice.iItem < len(reports):
                        if (notice.uNewState & 2) and not (notice.uOldState & 2):
                            model.select_setup(notice.iItem)
                            sync(window)
                    elif header.idFrom == 101 and 0 <= notice.iItem < len(rows):
                        if (notice.uOldState ^ notice.uNewState) & 0xF000:
                            model.toggle(rows[notice.iItem]['key'], (notice.uNewState & 0xF000) == 0x2000)
                            sync(window)
                return 0
            if message == 0x0010:
                user.EndDialog(window, 2)
                return 1
            if message == 0x0111:
                ident = wparam & 0xFFFF
                if ident in (1, 2):
                    user.EndDialog(window, ident)
                    return 1
                if ident in (221, 222, 223):
                    model.reset({221: 'all', 222: 'none', 223: 'nx'}[ident])
                    sync(window)
                    return 1
        except Exception as exc:
            state['error'] = exc
            user.EndDialog(window, 2)
            return 1
        return 0

    template = ctypes.create_string_buffer(setups_components_dialog_template())
    owner = user.GetActiveWindow() or user.GetForegroundWindow()
    result = user.DialogBoxIndirectParamW(None, ctypes.cast(template, ctypes.c_void_p), owner, callback, 0)
    if state['error'] is not None:
        raise state['error']
    if result not in (1, 2):
        raise RuntimeError('Не удалось открыть настройки установов: ' + str(ctypes.WinError(ctypes.get_last_error())))
    return model.values() if result == 1 else None


def ask_all_setup_components(nx, display, jobs):
    if not any(not row['suppressed'] for row in display.rows):
        return [set() for _ in jobs]
    answer = ask_warning(nx, 'Добавить компоненты сборки для отображения?')
    if answer is None:
        raise ExportCancelled()
    if answer is False:
        return [set() for _ in jobs]
    uf_ui = nx.UF.UFSession.GetUFSession().Ui
    lock_source = nx.UF.UFConstants.UF_UI_FROM_CUSTOM
    uf_ui.LockUgAccess(lock_source)
    try:
        choices = show_all_setup_components(display.rows, [job['report'] for job in jobs], display.apply)
    finally:
        try:
            restore_errors = display.restore()
            if restore_errors:
                raise RuntimeError('Не удалось восстановить видимость после выбора компонентов:\n' + '\n'.join(restore_errors))
        finally:
            uf_ui.UnlockUgAccess(lock_source)
    if choices is None:
        raise ExportCancelled()
    if len(choices) != len(jobs):
        raise RuntimeError('Не получены настройки всех установов.')
    return [normalized_component_choice(display.rows, choice) for choice in choices]


def finite_number(value):
    number = float(value)
    if not math.isfinite(number):
        raise ValueError("NX вернул нечисловое значение.")
    return number


def enum_name(value, enum_type, names):
    # Python-обёртки NX разных версий по-разному реализуют str(Enum).
    for name in names:
        if enum_type is not None and hasattr(enum_type, name) and value == getattr(enum_type, name):
            return name
    return str(value).rsplit(".", 1)[-1]


def try_read(report, obj, field, action, optional=False):
    try:
        return action()
    except Exception as exc:
        report.setdefault("data_issues", []).append({"object": str(obj.Name), "field": field,
                                                    "optional": optional, "message": str(exc)})
        return None


def read_toolpath_seconds(operation, report):
    raw = try_read(report, operation, "TIME (мин)",
                   lambda: finite_number(operation.GetToolpathTime()))
    if raw is not None and raw < 0:
        report.setdefault("data_issues", []).append({"object": str(operation.Name),
            "field": "TIME", "optional": False, "message": "NX вернул отрицательное время."})
        return raw, None
    return raw, (raw * 60.0 if raw is not None else None)


def path_point(value):
    point = xyz(value)
    if not all(math.isfinite(c) for c in point):
        raise ValueError('В траектории обнаружена некорректная координата.')
    return point


def release_path_data(value):
    # Transient wrappers own their read buffers, not the CAM path itself.
    # Python bindings also release these buffers when the wrapper is discarded.
    dispose = getattr(value, 'Dispose', None)
    if dispose is not None:
        dispose()


def arc_path_zmin(start, end, center, axis, clockwise, z_axis, origin, revolutions=None):
    """Analytic minimum of a circular/helical CL motion in the operation MCS.

    End points alone miss the bottom of a tilted arc. Helices are evaluated at
    their stationary points as well, without discretizing or allocating points.
    Only the first/last occurrence of each stationary phase is needed, even
    when the helix contains many revolutions.
    """
    if start is None:
        raise ValueError('У дугового движения нет начальной точки.')
    axis = unit(axis)
    delta0 = tuple(start[i] - center[i] for i in range(3))
    delta1 = tuple(end[i] - center[i] for i in range(3))
    h0, h1 = dot(delta0, axis), dot(delta1, axis)
    radial = tuple(delta0[i] - h0 * axis[i] for i in range(3))
    last_radial = tuple(delta1[i] - h1 * axis[i] for i in range(3))
    radius = math.sqrt(dot(radial, radial))
    tolerance = max(1e-7, radius * 1e-6)
    if radius <= 1e-10 or abs(math.sqrt(dot(last_radial, last_radial)) - radius) > tolerance:
        raise ValueError('Не удалось проверить радиус дуги траектории.')
    direction = -1.0 if clockwise else 1.0
    tangent = tuple(direction * c for c in cross(axis, radial))
    phase = math.atan2(dot(last_radial, tangent), dot(last_radial, radial)) % (2 * math.pi)
    rise = h1 - h0
    if revolutions is None:
        if abs(rise) > tolerance:
            raise ValueError('Концы круговой дуги лежат в разных плоскостях.')
        sweep = phase if phase > 1e-10 else 2 * math.pi
        rise = 0.0
    else:
        sweep = abs(finite_number(revolutions)) * 2 * math.pi
        if sweep <= 1e-10:
            raise ValueError('Не определено число витков винтового движения.')
        phase_error = math.remainder(sweep - phase, 2 * math.pi)
        if abs(phase_error) * radius > tolerance:
            raise ValueError('Число витков не соответствует концам винтового движения.')
    z0 = dot(tuple(start[i] - origin[i] for i in range(3)), z_axis)
    z1 = dot(tuple(end[i] - origin[i] for i in range(3)), z_axis)
    a, b = dot(radial, z_axis), dot(tangent, z_axis)
    drift = dot(axis, z_axis) * rise / sweep
    base = z0 - a
    minimum = min(z0, z1)
    amplitude = math.hypot(a, b)
    if amplitude > 1e-12 and abs(drift) <= amplitude:
        angle = math.acos(max(-1., min(1., -drift / amplitude)))
        offset = math.atan2(a, b)
        period = 2 * math.pi
        for phase in (angle - offset, -angle - offset):
            first = math.ceil(-phase / period)
            last = math.floor((sweep - phase) / period)
            if first > last:
                continue
            for cycle in (first, last):
                theta = phase + cycle * period
                value = base + a * math.cos(phase) + b * math.sin(phase) + drift * theta
                minimum = min(minimum, value)
    return finite_number(minimum)


def path_axis_mode(nx, path):
    """Validate the supported NX path storage formats.

    The supplied NX diagnostic demonstrates that 'Three' also contains a
    meaningful, fixed ToolAxis tilted relative to the operation MCS. Neither
    format determines the physical tool direction or the machine axis count.
    """
    axis_type = getattr(nx.CAM, 'CamPathToolAxisType', None)
    try:
        mode = enum_name(path.ToolAxisType, axis_type, ('Three', 'Five'))
    except Exception as exc:
        raise ValueError('Не удалось определить формат оси инструмента: ' + str(exc))
    if mode not in ('Three', 'Five'):
        raise ValueError('Неизвестный формат оси инструмента: ' + str(mode))
    return mode


def motion_z_axis(motion):
    # Read the actual CL vector for BOTH Three and Five, including indexed cuts.
    # Using MCS Z for Three caused wrong Z values on 16 operations in the
    # supplied diagnostic. Stored CAM parameter vectors can also differ from
    # this motion's vector after a toolpath transformation: use the path itself.
    try:
        return unit(path_point(motion.ToolAxis))
    except Exception as exc:
        # Falling back to fixed MCS Z here would recreate the +/-150 error.
        raise ValueError('Недоступно направление оси инструмента в траектории: ' + str(exc))


def toolpath_zmin(nx, operation, basis, origin, progress=None):
    """Read the existing CL path; never generate, post or modify it.

    EndPoint and ToolAxis are used together for Three and Five paths. With MCS origin
    O on the rotary axis, Z at each stored position is dot(P - O, unit(IJK)).
    Under the same rigid rotation of P - O and IJK their dot product is
    invariant. No abs(), radius substitution or guessed A/B angle is used.

    For variable-axis linear CL output evaluate each stored position with its
    own vector. Do not interpolate a Cartesian chord between rotated CL points
    and project it onto an invented intermediate vector: this would falsely
    reduce Z even for constant-height rotation. This is a CL-coordinate result,
    not a postprocessed machine-axis or controller-smoothing simulation.

    Arcs/helices with a constant axis retain the analytic interior extrema.
    Without orientation interpolation data, changing-axis arcs are rejected
    rather than reporting a partial or guessed minimum for the operation.
    """
    path = operation.GetPath()
    if path is None:
        return None
    if path.HasSubPath():
        raise ValueError('Zmin недоступен для составной траектории с подчинёнными путями.')
    count = int(path.NumberOfToolpathEvents)
    if count <= 0:
        return None
    path_axis_mode(nx, path)
    shapes = nx.CAM.CamPathMotionShapeType
    directions = nx.CAM.CamPathDir
    if not all(math.isfinite(c) for c in origin):
        raise ValueError('Некорректное начало СКС операции.')
    minimum, previous, previous_axis, event = None, None, None, None
    if progress is not None:
        progress(0, count)
    try:
        event = path.GetFirstEvent()
        # Linked traversal avoids repeated searches from the path's beginning
        # on large operations. At most two transient buffers are held at once.
        for index in range(count):
            if event is None:
                raise ValueError('Траектория закончилась до последнего события.')
            is_motion, motion_type, shape = path.IsToolpathEventAMotion(event)
            if is_motion:
                motion = None
                try:
                    if shape == shapes.Linear:
                        motion = path.GetLinearMotion(event)
                    elif shape == shapes.Circular:
                        motion = path.GetCircularMotion(event)
                    elif shape == shapes.Helical:
                        motion = path.GetHelixMotion(event)
                    else:
                        raise ValueError('Zmin: неподдерживаемая форма движения ' + str(shape))
                    end = path_point(motion.EndPoint)
                    z_axis = motion_z_axis(motion)
                    value = dot(tuple(end[i] - origin[i] for i in range(3)), z_axis)
                    if shape != shapes.Linear:
                        if previous_axis is not None:
                            difference = tuple(z_axis[i] - previous_axis[i] for i in range(3))
                            if dot(difference, difference) > 1e-16:
                                raise ValueError('Дуга или винтовое движение с изменением оси инструмента: '
                                                 'недостаточно данных для пересчёта Z между точками.')
                        direction = motion.Direction
                        if direction not in (directions.Clockwise, directions.Counterclockwise):
                            raise ValueError('Не определено направление дуги траектории.')
                        value = arc_path_zmin(previous, end, path_point(motion.ArcCenter),
                                              path_point(motion.ArcAxis), direction == directions.Clockwise,
                                              z_axis, origin,
                                              motion.NumberOfRevolutions if shape == shapes.Helical else None)
                    minimum = value if minimum is None else min(minimum, value)
                    previous = end
                    previous_axis = z_axis
                finally:
                    release_path_data(motion)
            if (index + 1) % 256 == 0 and progress is not None:
                progress(index + 1, count)
            following = event.GetNext() if index + 1 < count else None
            current, event = event, following
            release_path_data(current)
        if progress is not None:
            progress(count, count)
    finally:
        release_path_data(event)
    return finite_number(minimum) if minimum is not None else None


def parent_workpiece(nx, part, obj, cache=None, trace=None):
    """Find a volume geometry owner, continuing past boundary-only groups.

    FeatureGeometry is shared by WORKPIECE and boundary geometry. Inspect the
    actual builder instead of assuming that every FeatureGeometry is a body
    container. Builders are read without Commit and always destroyed.
    """
    cache = {} if cache is None else cache
    seen = set()
    while obj is not None:
        key = object_key(obj)
        if key in seen:
            raise RuntimeError('Обнаружен цикл в дереве геометрии CAM.')
        seen.add(key)
        if isinstance(obj, nx.CAM.FeatureGeometry):
            if key not in cache:
                item = {'tag': key, 'name': str(obj.Name), 'status': 'pending'}
                if trace is not None:
                    trace.append(item)
                builder = None
                try:
                    builder = part.CAMSetup.CAMGroupCollection.CreateMillGeomBuilder(obj)
                    item['builder_type'] = type(builder).__name__
                    has_part = getattr(builder, 'PartGeometry', None) is not None
                    has_blank = getattr(builder, 'BlankGeometry', None) is not None
                    item.update(has_part_geometry=has_part, has_blank_geometry=has_blank,
                                status='workpiece' if has_part and has_blank else 'skip_non_volume_geometry')
                    cache[key] = has_part and has_blank
                except Exception as exc:
                    item.update(status='error', message=str(exc))
                    raise RuntimeError('Не удалось проверить геометрию «%s»: %s' % (obj.Name, exc)) from exc
                finally:
                    if builder is not None:
                        builder.Destroy()
            if cache[key]:
                return obj
        if isinstance(obj, nx.CAM.Operation):
            obj = obj.GetParent(nx.CAM.CAMSetup.View.Geometry)
        elif isinstance(obj, nx.CAM.NCGroup):
            obj = obj.GetParent()
        else:
            return None
    return None


def resolve_workpieces(nx, part, context, report):
    """Use the same verified owners for blank dimensions of the selected setups.

    A detached 1S branch may use the unique Workpiece of its matching 1 MCS.
    Resolve this per operation even when other operations already found their
    parent Workpiece. Never silently select a different setup or merge several
    ambiguous fallback candidates.
    """
    cached = context.get('_resolved_workpiece_groups')
    if cached is not None:
        return cached
    result = report['workpiece_resolution'] = {
        'status': 'pending', 'checked_groups': [], 'operations': [], 'workpieces': []}
    cache, groups, bindings, missing, fallback_cache = {}, {}, [], [], {}

    def parent(obj):
        return parent_workpiece(nx, part, obj, cache, result['checked_groups'])

    def bind(operation, mcs, group, source):
        groups[object_key(group)] = group
        bindings.append({'operation_tag': object_key(operation), 'operation_name': str(operation.Name),
                         'mcs_tag': object_key(mcs) if mcs is not None else None,
                         'mcs_name': str(mcs.Name) if mcs is not None else '',
                         'workpiece_tag': object_key(group), 'workpiece_name': str(group.Name),
                         'source': source})

    try:
        for operation in context['operations']:
            mcs, group = nearest_mcs(nx, operation), parent(operation)
            if group is None:
                missing.append((operation, mcs))
            else:
                bind(operation, mcs, group, 'operation_ancestors')

        for operation, mcs in missing:
            if mcs is None:
                raise ValueError('У операции «%s» не найдена СКС для поиска Workpiece.' % operation.Name)
            family = mcs_number_key(mcs.Name)
            if family not in fallback_cache:
                candidates = {}
                for node in part.CAMSetup.CAMGroupCollection:
                    if isinstance(node, nx.CAM.OrientGeometry):
                        matching = mcs_number_key(node.Name) == family
                    elif isinstance(node, nx.CAM.FeatureGeometry):
                        owner = nearest_mcs(nx, node)
                        matching = owner is not None and mcs_number_key(owner.Name) == family
                    else:
                        matching = False
                    if matching:
                        candidate = parent(node)
                        if candidate is not None:
                            candidates[object_key(candidate)] = candidate
                fallback_cache[family] = candidates
            candidates = fallback_cache[family]
            if not candidates:
                raise ValueError('Для операции «%s» и СКС «%s» не найден Workpiece с Part Geometry и Blank Geometry.'
                                 % (operation.Name, mcs.Name))
            if len(candidates) != 1:
                names = ', '.join('«%s» [%s]' % (g.Name, object_key(g)) for g in candidates.values())
                raise ValueError('Для операции «%s» в связанных СКС найдено несколько Workpiece: %s. '
                                 'Нужно однозначно задать родительскую геометрию операции в NX.'
                                 % (operation.Name, names))
            bind(operation, mcs, next(iter(candidates.values())), 'unique_matching_mcs')
        if not groups:
            raise ValueError('В выбранных операциях не найден Workpiece с Part Geometry и Blank Geometry.')
        order = {object_key(op): i for i, op in enumerate(context['operations'])}
        bindings.sort(key=lambda item: order[item['operation_tag']])
        result.update(status='resolved', operations=bindings,
                      workpieces=[{'tag': object_key(g), 'name': str(g.Name)} for g in groups.values()])
        context['_resolved_workpiece_groups'] = list(groups.values())
        return context['_resolved_workpiece_groups']
    except Exception as exc:
        result.update(status='error', message=str(exc), operations=bindings,
                      workpieces=[{'tag': object_key(g), 'name': str(g.Name)} for g in groups.values()])
        raise


def workpiece_blank_values(nx, part, group):
    """Read the actual Workpiece parameters, without editing or measuring bodies."""
    builder = part.CAMSetup.CAMGroupCollection.CreateMillGeomBuilder(group)
    try:
        blank = getattr(builder, 'BlankGeometry', None)
        if blank is None:
            raise ValueError('Группа «%s» не содержит Blank Geometry (%s).'
                             % (group.Name, type(builder).__name__))
        kind = enum_name(blank.BlankDefinitionType, nx.CAM.GeometryGroup.BlankDefinitionTypes,
                         ("AutoBlock", "BoundingCylinder", "FromGeometry", "Ipw", "OffsetFromPart",
                          "PartConvexHull", "PartOutline"))
        data = {"workpiece_tag": object_key(group), "workpiece_name": str(group.Name), "type": kind}
        fields = {"AutoBlock": (("length", "BlockLength"), ("width", "BlockWidth"), ("height", "BlockHeight")),
                  "BoundingCylinder": (("diameter", "CylinderDiameter"), ("height", "CylinderHeight"))}
        if kind not in fields:
            return data
        dimensions = {name: finite_number(getattr(blank, attribute)) for name, attribute in fields[kind]}
        if any(value <= 0 for value in dimensions.values()):
            raise RuntimeError("В Workpiece «%s» NX вернул нулевой или отрицательный размер заготовки." % group.Name)
        # Это готовые размеры из полей Workpiece. Припуски второй раз не прибавляем.
        data["dimensions"] = dimensions
        return data
    finally:
        builder.Destroy()


def blank_size_text(data, units):
    def dimension(value):
        text = ('%.6f' % value).rstrip('0').rstrip('.')
        return (text if text != '0' else format(value, '.8g')).replace('.', ',')
    dims = data["dimensions"]
    if data["type"] == "AutoBlock":
        text = "Д×Ш×В " + "×".join(dimension(dims[name]) for name in ("length", "width", "height"))
    else:
        text = "Ø" + dimension(dims["diameter"]) + "×" + dimension(dims["height"])
    return text + (" дюйм" if units == "Inches" else " мм")


def read_workpiece_blank(nx, part, context, report):
    """Read each selected setup's Workpiece; names and numeric aliases never suppress dimensions."""
    report["stock_blank"] = ""
    result = report["blank"] = {"status": "manual", "source": "operation_ancestors", "workpieces": []}
    try:
        groups = resolve_workpieces(nx, part, context, report)
        if any(item['source'] == 'unique_matching_mcs'
               for item in report['workpiece_resolution']['operations']):
            result['source'] = 'operation_ancestors_and_matching_mcs'
        for group in groups:
            result['workpieces'].append(workpiece_blank_values(nx, part, group))
        if any("dimensions" not in data for data in result["workpieces"]):
            result.update(status="unsupported", message="Заготовка Workpiece задана не блоком или цилиндром. Заполните размеры вручную.")
            return
        first = result["workpieces"][0]
        for data in result["workpieces"][1:]:
            if data["type"] != first["type"] or any(not math.isclose(value, first["dimensions"][name], rel_tol=1e-9, abs_tol=1e-9)
                                                  for name, value in data["dimensions"].items()):
                result.update(status="ambiguous", message="В выбранных операциях разные заготовки Workpiece. Укажите размеры вручную.")
                return
        units = report.get("part_units")
        if units not in ("Millimeters", "Inches"):
            raise RuntimeError("Не удалось определить единицы размеров Workpiece.")
        text = blank_size_text(first, units)
        result.update(status="read", units=units, text=text)
        report["stock_blank"] = text
    except Exception as exc:
        result.update(status="error", message="Не удалось прочитать размеры заготовки Workpiece. Заполните поле вручную. " + str(exc))


def mark_tool_changes(rows):
    """Follow continuous Program Order; an ordinary folder is not a tool change."""
    previous, have_previous, previous_known = None, False, True
    for row in rows:
        if row['kind'] != 'operation':
            continue
        if row.get('suppressed') is True or row.get('has_path') is False:
            row.update(tool_change=False, tool_change_reason='Операция не выводится')
            continue
        tool = row.get('tool_tag')
        if not tool or row.get('suppressed') is None or row.get('has_path') is None:
            row.update(tool_change=None, tool_change_reason='Последовательность инструментов не прочитана')
            previous_known, have_previous = False, True
            continue
        forced = row.get('output_load_tool') is True
        first = not have_previous
        changed = have_previous and previous_known and previous != tool
        change = (first or changed or forced) if previous_known or forced else None
        row.update(tool_change=change, tool_change_source='continuous_project_program_order',
                   tool_change_reason=('Первый инструмент' if first else
                                       'Смена назначенного инструмента' if changed else
                                       'Явный LOAD/TOOL' if forced else
                                       'Предыдущий инструмент неизвестен' if change is None else ''))
        previous, have_previous, previous_known = tool, True, True


def read_final_floor_switch(uf, operation, report):
    """Read the newer logical switch, validating its name and type in this NX.

    Index/name/type are from the supplied NX parameter catalogue. Unlike
    integer modes, logical values must be read with AskLogicalValue. The
    IndexAttribute.IntDefault field is NOT a logical default value.
    """
    result = {'index': 17022, 'name': 'Use Final Floor Same As Part', 'supported': False}
    try:
        count, indices = uf.Param.AskRequiredParams(operation.Tag)
        if int(count) != len(indices):
            raise ValueError('Неполный список параметров операции.')
        if result['index'] not in indices:
            return result
        result['supported'] = True
        attrs = uf.Param.AskParamAttributes(result['index'])
        name = str(uf_struct_value(attrs, 'name')).strip()
        kind = str(uf_struct_value(attrs, 'type')).rsplit('.', 1)[-1].lower()
        if name.casefold() != result['name'].casefold() or kind not in ('0', 'logical', 'bool', 'boolean'):
            raise ValueError('Название или тип переключателя FLOOR STOCK не совпадает с каталогом NX.')
        result['value'] = bool(uf.Param.AskLogicalValue(operation.Tag, result['index']))
        result['status'] = str(uf.Param.AskParamStatus(operation.Tag, result['index']))
        result['definer_tag'] = str(uf.Param.AskParamDefiner(operation.Tag, result['index']))
    except Exception as exc:
        result['error'] = str(exc)
        report.setdefault('data_issues', []).append({'object': str(operation.Name), 'field': result['name'],
            'message': str(exc), 'optional': not result['supported']})
    return result


def stock_parameter_origin(uf, operation, index):
    """Read provenance only; a default zero is distinct from an assigned zero."""
    result = {'index': index}
    try:
        raw = uf.Param.AskParamStatus(operation.Tag, index)
        text = str(raw).rsplit('.', 1)[-1].lower()
        if text.startswith('uf_param_'):
            text = text[len('uf_param_'):]
        status = {'0': 'default', '1': 'inherited', '2': 'overridden',
                  '3': 'invalid_index'}.get(text, text)
        if status not in ('default', 'inherited', 'overridden', 'invalid_index'):
            raise ValueError('Неизвестный статус наследования параметра: ' + str(raw))
        result.update(status=status, raw_status=str(raw),
                      definer_tag=str(uf.Param.AskParamDefiner(operation.Tag, index)))
    except Exception as exc:
        result['error'] = str(exc)
    return result


def surface_floor_uses_part_stock(uf, operation, switch, record):
    """Resolve the SurfaceContour default-stock case seen in the live NX v2 audit.

    SurfaceContour also covers operations with a separate floor allowance.
    Class alone is insufficient: an inherited/overridden zero remains a real
    separate floor value. Only the all-default case uses the uniform part stock.
    UF_PARAM status order is DEFAULT, INHERITED, OVERRIDDEN, INVALID_INDEX.
    """
    if (not any(cls.__name__ == 'SurfaceContour' for cls in type(operation).__mro__)
            or uf is None or not switch.get('supported') or switch.get('value') is not False):
        return False
    origins = {name: stock_parameter_origin(uf, operation, index)
               for name, index in (('floor', 264), ('stock_part_use', 346), ('final_floor_switch', 17022))}
    record['floor_stock_origins'] = origins
    # An explicitly assigned or inherited zero must never become part stock.
    if any(item.get('status') in ('inherited', 'overridden') for item in origins.values()):
        return False
    if all(item.get('status') == 'default' and item.get('definer_tag') == '0'
           and 'error' not in item for item in origins.values()):
        return True
    # An unreadable origin is not evidence for either a default or an override.
    return None


def read_operation_stock(operation, record, param, uf=None, report=None):
    """Read active allowances, retaining inactive values and mode provenance."""
    side = param(operation, 'UF_PARAM_STOCK_PART', 'Double', True)
    mode = param(operation, 'UF_PARAM_STOCK_PART_USE', 'Int', True)
    floor = param(operation, 'UF_PARAM_STOCK_FLOOR', 'Double', True)
    switch = read_final_floor_switch(uf, operation, report) if uf is not None else {'supported': False}
    record.update(stock=side, stock_raw=side, floor_stock_raw=floor, stock_part_use=mode,
                  stock_source='UF_PARAM_STOCK_PART', final_floor_same_as_part=switch,
                  operation_class=type(operation).__name__)
    # A drilling operation can carry a generic unused floor default; ONT has no floor column value for it.
    drilling = any(cls.__name__ == 'HoleDrilling' for cls in type(operation).__mro__)
    if drilling:
        record.update(floor_stock=None, floor_stock_mode='not_applicable', floor_stock_source='HoleDrilling')
    elif switch.get('supported') and 'value' not in switch:
        record.update(floor_stock=None, floor_stock_mode='unavailable', floor_stock_source='Floor mode could not be read')
    elif mode == 1 or switch.get('value') is True:
        source = 'UF_PARAM_STOCK_PART_USE=1' if mode == 1 else 'Use Final Floor Same As Part=true'
        record.update(floor_stock=side, floor_stock_mode='same_as_side', floor_stock_source='UF_PARAM_STOCK_PART (' + source + ')')
    elif mode in (0, None):
        use_part = surface_floor_uses_part_stock(uf, operation, switch, record) if mode == 0 else False
        if use_part is True:
            record.update(floor_stock=side, floor_stock_mode='uniform_part_stock' if side is not None else 'unavailable',
                          floor_stock_source='UF_PARAM_STOCK_PART (SurfaceContour; floor and floor modes are NX defaults)')
        elif use_part is None:
            record.update(floor_stock=None, floor_stock_mode='unavailable',
                          floor_stock_source='SurfaceContour floor inheritance could not be read')
            if report is not None:
                report.setdefault('data_issues', []).append({'object': str(operation.Name), 'field': 'FLOOR STOCK',
                    'message': 'Не удалось определить источник припуска на дно у SurfaceContour.', 'optional': False})
        else:
            # CylinderMilling / VolumeBased25DMilling do not have index 346;
            # their explicitly assigned floor allowance is nevertheless valid.
            record.update(floor_stock=floor, floor_stock_mode='separate' if floor is not None else 'not_applicable',
                          floor_stock_source='UF_PARAM_STOCK_FLOOR')
    else:
        record.update(floor_stock=None, floor_stock_mode='unavailable', floor_stock_source='Unknown stock-use mode')


def read_machine_change_seconds(nx, part, operation, report, cache):
    """ToolChangeTime.Value is seconds (12.0 in the supplied live NX data)."""
    parent, seen = operation.GetParent(nx.CAM.CAMSetup.View.MachineTool), set()
    while parent is not None and isinstance(parent, nx.CAM.NCGroup):
        key = object_key(parent)
        if key in seen:
            break
        seen.add(key)
        if not isinstance(parent, nx.CAM.Tool):
            if key not in cache:
                item, builder = {'tag': key, 'name': str(parent.Name), 'unit': 'seconds'}, None
                try:
                    builder = part.CAMSetup.CAMGroupCollection.CreateMachineGroupBuilder(parent)
                    if builder is None:
                        raise ValueError('Узел не предоставляет MachineGroupBuilder.')
                    seconds = finite_number(builder.ToolChangeTime.Value)
                    if seconds < 0:
                        raise ValueError('Отрицательное время смены инструмента.')
                    item.update(seconds=seconds, source='MachineGroupBuilder.ToolChangeTime.Value')
                except Exception as exc:
                    item['error'] = str(exc)
                finally:
                    if builder is not None:
                        builder.Destroy()
                cache[key] = item
            if 'seconds' in cache[key]:
                return cache[key]
        parent = parent.GetParent()
    return {'seconds': None, 'source': 'unavailable'}


def annotate_operation_timing(nx, part, context, records, param, report):
    """Use full project order even when the user selected just O35 or one op."""
    uf = nx.UF.UFSession.GetUFSession()
    program = context.get('program_tree')
    if program is None:
        program = cam_tree(nx, part.CAMSetup.GetRoot(nx.CAM.CAMSetup.View.ProgramOrder))
    sequence = []
    for node in tree_nodes(program):
        if node['children'] is not None:
            continue
        operation = node['object']
        key = object_key(operation)
        if key in records:
            sequence.append(records[key])
            continue
        # Only tool ordering is read outside the selection, never feeds or geometry.
        state = param(operation, 'UF_PARAM_SUPPRESS_PATH', 'Int', True)
        sequence.append({'kind': 'operation', 'tag': key, 'name': str(operation.Name),
            'tool_tag': try_read(report, operation, 'Tool sequence', lambda: str(uf.Oper.AskCutterGroup(operation.Tag)), True),
            'suppressed': state != 0 if state is not None else None,
            'has_path': try_read(report, operation, 'Path sequence', operation.AskPathExists, True),
            'output_load_tool': param(operation, 'UF_PARAM_OUTPUT_LOAD_TOOL', 'Logical', True)})
    if not set(records).issubset({r['tag'] for r in sequence}):
        raise ValueError('Не все выбранные операции найдены в полной последовательности инструментов.')
    mark_tool_changes(sequence)
    cache = {}
    for operation in context['operations']:
        record = records[object_key(operation)]
        record['tool_change_seconds'] = 0.0 if record.get('tool_change') is False else None
        if record.get('tool_change') is True:
            try:
                setting = read_machine_change_seconds(nx, part, operation, report, cache)
                record.update(tool_change_seconds=setting.get('seconds'), machine_change_time=setting)
            except Exception as exc:
                record['machine_change_time'] = {'error': str(exc)}
        active = record.get('suppressed') is False and record.get('has_path') is not False
        if record.get('suppressed') is None:
            record['group_seconds'] = None
        elif not active:
            record['group_seconds'] = 0.0
        elif record['seconds'] is None or record['tool_change_seconds'] is None:
            record['group_seconds'] = None
        else:
            record['group_seconds'] = record['seconds'] + record['tool_change_seconds']
    report['machine_change_times'] = list(cache.values())
    report['tool_change_source'] = 'continuous_project_program_order_and_explicit_load'
    report['group_time_source'] = 'active_toolpaths_plus_machine_tool_changes'
    return sequence





def tools_with_active_paths(rows, report, scope):
    """Include a tool once when any operation in this scope has an active path."""
    included, unverified = set(), []
    for row in rows:
        if row.get('kind') != 'operation':
            continue
        if row.get('has_path') is False or row.get('suppressed') is True:
            continue
        tag = row.get('tool_tag')
        if row.get('has_path') is True and row.get('suppressed') is False and tag not in (None, '', '0'):
            included.add(str(tag))
        else:
            unverified.append({'tag': row.get('tag'), 'name': row.get('name')})
    audit = report.setdefault('tool_filter', {'rule': 'has_path=true AND suppressed=false; match tool object tag'})
    audit[scope] = {'included_tool_tags': sorted(included), 'unverified_operations': unverified}
    if unverified:
        report.setdefault('data_issues', []).append({'object': scope, 'field': 'Список инструментов',
            'message': 'Не удалось проверить %d операций; список инструментов может быть неполным.' % len(unverified),
            'optional': False})
    return included


def annotate_tool_hierarchy(nx, part, groups, report):
    """Read actual MachineTool parents; names and U-prefixed tools are opaque.

    Carrier is an NCGroup in the machine-tool tree. Keeping its real ancestor
    path also supports arbitrary names and nested carriers/pockets, without
    guessing a setup from a tool's name or number. Only included tools are read.
    """
    audit = {'source': 'CAMSetup.GetRoot(MachineTool); NCGroup.GetParent/GetMembers',
             'errors': [], 'order_errors': []}
    report['tool_hierarchy'] = audit
    included = {str(t['tag']): t for field in ('tools', 'project_tools')
                for t in report.get(field, []) if t.get('tag') is not None}
    if not included:
        return
    root = None
    try:
        root = part.CAMSetup.GetRoot(nx.CAM.CAMSetup.View.MachineTool)
        audit['root'] = {'tag': object_key(root), 'name': str(root.Name)}
    except Exception as exc:
        audit['errors'].append({'field': 'MachineTool root', 'message': str(exc)})
    root_key = audit.get('root', {}).get('tag')
    orders, cache = {}, {}

    def sibling_order(parent, child_key):
        if parent is None:
            return None
        key = object_key(parent)
        if key not in orders:
            try:
                orders[key] = {object_key(obj): i for i, obj in enumerate(parent.GetMembers())}
            except Exception as exc:
                orders[key] = {}
                audit['order_errors'].append({'tag': key, 'message': str(exc)})
        return orders[key].get(child_key)

    for tag, item in included.items():
        path, seen, complete = [], {tag}, False
        tool = groups.get(tag)
        try:
            if tool is None:
                raise ValueError('Объект инструмента недоступен в CAMGroupCollection.')
            current = tool.GetParent()
            for _ in range(128):
                if current is None or object_key(current) == '0':
                    if root_key is None:
                        # The terminal group is the root if GetRoot was unavailable.
                        if path:
                            path.pop()
                        complete = True
                        break
                    raise ValueError('Цепочка инструмента не достигла корня MachineTool.')
                key = object_key(current)
                if key == root_key:
                    complete = True
                    break
                if key in seen:
                    raise ValueError('Цикл в родительских группах инструмента.')
                seen.add(key)
                if not isinstance(current, nx.CAM.NCGroup):
                    raise ValueError('Родитель инструмента не является NCGroup.')
                # Keep a known group even if reading the next parent fails.
                entry = {'tag': key, 'name': str(current.Name), 'order': None}
                path.append(entry)
                parent = current.GetParent()
                entry['order'] = sibling_order(parent, key)
                current = parent
            else:
                raise ValueError('Превышена глубина дерева инструментов.')
        except Exception as exc:
            audit['errors'].append({'tag': tag, 'name': item['name'], 'message': str(exc)})
            report.setdefault('data_issues', []).append({
                'object': item['name'], 'field': 'Carrier', 'optional': False,
                'message': 'Не удалось полностью прочитать группы инструмента; '
                           'известная часть сохранена. ' + str(exc)})
        cache[tag] = {'machine_path': list(reversed(path)),
                      'machine_path_status': 'complete' if complete else 'partial'}
    for field in ('tools', 'project_tools'):
        for item in report.get(field, []):
            if str(item.get('tag')) in cache:
                item.update(cache[str(item['tag'])])
    for row in report.get('operation_rows', []):
        data = cache.get(str(row.get('tool_tag')))
        if row.get('kind') == 'operation' and data is not None:
            row['tool_machine_path'] = data['machine_path']


def tool_display_rows(tools):
    """Expand only occupied groups; keep separate tool objects even with equal T.

    Root tools remain outside carriers. Tools are sorted by T within each
    group; group order follows the machine-tool tree whenever NX supplied it.
    Legacy reports without machine_path retain the previous flat list.
    """
    root = {'children': {}, 'tools': []}
    for index, tool in enumerate(tools):
        node, path, seen = root, [], set()
        for group in tool.get('machine_path') or []:
            if not isinstance(group, dict) or group.get('tag') is None:
                continue
            key = str(group['tag'])
            if key in seen:
                break
            seen.add(key)
            path.append(key)
            if key not in node['children']:
                node['children'][key] = {'children': {}, 'tools': [],
                    'group': dict(group), 'path': list(path), 'first': index}
            node = node['children'][key]
        node['tools'].append(tool)
    rows = []

    def emit(node, depth):
        for tool in sorted_tools(node['tools']):
            rows.append(dict(tool, kind='tool', depth=depth))
        def order(child):
            value = child['group'].get('order')
            return (not isinstance(value, (int, float)),
                    value if isinstance(value, (int, float)) else child['first'],
                    child['first'])
        for child in sorted(node['children'].values(), key=order):
            group = child['group']
            rows.append({'kind': 'group', 'tag': str(group['tag']),
                         'name': str(group.get('name', '')), 'number': None,
                         'depth': depth, 'group_path': child['path']})
            emit(child, depth + 1)
    emit(root, 0)
    return rows


def tool_row_html(row, name_first=True):
    group = row.get('kind') == 'group'
    name = str(row['name'])
    depth = max(0, min(12, int(row.get('depth', 0))))
    if group:
        label = name + (' · продолжение' if row.get('continued') else '')
        icon = ('<span class="carrier-icon" aria-hidden="true">'
                '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="6.5"/>'
                '<circle cx="8" cy="4.5" r="1.1"/><circle cx="11.5" cy="8" r="1.1"/>'
                '<circle cx="8" cy="11.5" r="1.1"/><circle cx="4.5" cy="8" r="1.1"/>'
                '</svg></span>')
        text = '<span class="carrier-expanded" aria-hidden="true">▾</span>' + icon + '<span class="tool-group-label">' + escaped(label) + '</span>'
        number = ''
        attrs = ' class="tool-group-row" data-tool-group="%s"' % escaped(str(row.get('tag', '')))
        if row.get('continued'):
            attrs += ' data-repeat="true"'
    else:
        label = name
        text = tool_name_html(name)
        number = number_text(row.get('number'))
        attrs = ' data-tool-tag="%s"' % escaped(str(row.get('tag', '')))
    content = ('<span class="tool-tree-cell"><span class="tool-tree-indent" '
               'aria-hidden="true" style="width:%.2fem"></span>%s</span>') % (depth * 1.3, clipped_cell(text, label))
    name_cell = '<td class="tool-name-cell" title="%s">%s</td>' % (escaped(label), content)
    number_cell = '<td>%s</td>' % clipped_cell(
        '' if group else tool_number_html(row.get('number'), row.get('duplicate_number', False)), number)
    return '<tr data-kind="%s" data-depth="%d"%s>%s</tr>' % (
        'group' if group else 'tool', depth, attrs,
        name_cell + number_cell if name_first else number_cell + name_cell)


def tool_number_key(value):
    """Only known integral T values can establish a duplicate, including T0."""
    if value is None or isinstance(value, bool):
        return None
    try:
        number = finite_number(value)
        return int(number) if number == int(number) else None
    except (TypeError, ValueError, OverflowError):
        return None


def duplicate_tool_number_groups(tools):
    """Count distinct NX tool objects, never repeated references or operations."""
    numbers, seen = {}, set()
    for tool in tools:
        tag = tool.get('tag')
        if tag in (None, '', '0'):
            continue
        tag = str(tag)
        if tag in seen:
            continue
        seen.add(tag)
        number = tool_number_key(tool.get('number'))
        if number is not None:
            numbers.setdefault(number, []).append({'tag': tag, 'name': str(tool.get('name', ''))})
    return [{'number': number, 'tools': members}
            for number, members in sorted(numbers.items()) if len(members) > 1]


def inspect_project_tool_numbers(nx, part, report):
    """Inspect all CAM tools, also unused tools and tools in other Carriers."""
    uf = nx.UF.UFSession.GetUFSession()
    symbol = nx.UF.UFConstants.UF_PARAM_TL_NUMBER
    tools, errors, seen = [], [], set()
    for tool in part.CAMSetup.CAMGroupCollection:
        if not isinstance(tool, nx.CAM.Tool):
            continue
        tag = object_key(tool)
        if tag in seen:
            continue
        seen.add(tag)
        record = {'tag': tag, 'name': str(tool.Name), 'number': None}
        try:
            record['number'] = tool_number_key(uf.Param.AskIntValue(tool.Tag, symbol))
            if record['number'] is None:
                raise ValueError('Не прочитан целый номер инструмента.')
        except Exception as exc:
            errors.append({'tag': tag, 'name': record['name'], 'message': str(exc)})
            report.setdefault('data_issues', []).append({
                'object': record['name'], 'field': 'Проверка повторяющихся T',
                'message': str(exc), 'optional': False})
        tools.append(record)
    groups = duplicate_tool_number_groups(tools)
    check = {'scope': 'all_cam_tool_objects_in_project', 'tool_count': len(tools),
             'complete': not errors, 'errors': errors, 'duplicate_groups': groups,
             'numbers': [group['number'] for group in groups], 'decision': 'not_needed'}
    report['tool_number_check'] = check
    return check


def duplicate_tool_warning_details(groups):
    """Keep every distinct tool in the warning, grouped by its duplicate T."""
    blocks = []
    for group in groups:
        lines = ['T%s' % group['number']]
        for tool in group['tools']:
            name = str(tool.get('name') or '(без названия)')
            # Preserve arbitrary names; display control characters literally.
            name = name.replace('\0', r'\0').replace('\r', r'\r').replace('\n', r'\n')
            lines.append('  • ' + name)
        blocks.append('\r\n'.join(lines))
    return '\r\n\r\n'.join(blocks)


def confirm_duplicate_tool_numbers(nx, part, report):
    check = inspect_project_tool_numbers(nx, part, report)
    if not check['numbers']:
        return
    answer = ask_warning(nx, 'В проекте присутствуют повторяющиеся номера инструмента! Продолжить?',
                         duplicate_tool_warning_details(check['duplicate_groups']))
    check['decision'] = 'yes' if answer is True else ('no' if answer is False else 'closed')
    if answer is not True:
        raise ExportCancelled('Создание карты наладки отменено пользователем.')


def mark_duplicate_tool_numbers(report):
    check = report.get('tool_number_check')
    if check is None:
        # Offline previews and older JSON reports have only exported tool lists.
        tools = list(report.get('tools', [])) + list(report.get('project_tools', []))
        groups = duplicate_tool_number_groups(tools)
        check = {'scope': 'available_report_tools', 'duplicate_groups': groups,
                 'numbers': [group['number'] for group in groups], 'decision': 'not_run_offline'}
        report['tool_number_check'] = check
    numbers = {tool_number_key(value) for value in check.get('numbers', [])}
    numbers.discard(None)
    for field in ('tools', 'project_tools'):
        for tool in report.get(field, []):
            tool['duplicate_number'] = tool_number_key(tool.get('number')) in numbers
    for row in report.get('operation_rows', []):
        if row.get('kind') == 'operation':
            row['duplicate_tool_number'] = tool_number_key(row.get('tool_number')) in numbers


def tool_number_html(value, duplicate=False):
    text = escaped(number_text(value))
    if duplicate:
        return '<strong class="duplicate-tool-number" title="Повторяющийся номер инструмента в проекте">%s</strong>' % text
    return text


def read_operation_data(nx, part, context, report):
    uf = nx.UF.UFSession.GetUFSession()
    constants = nx.UF.UFConstants
    groups = {object_key(g): g for g in part.CAMSetup.CAMGroupCollection}
    records, tools, mcs_frames = {}, {}, {}

    def param(obj, symbol, kind, optional=False):
        def read():
            value = getattr(uf.Param, "Ask" + kind + "Value")(obj.Tag, getattr(constants, symbol))
            return finite_number(value) if kind == "Double" else value
        return try_read(report, obj, symbol, read, optional)

    for operation_index, operation in enumerate(context["operations"]):
        if operation_index % 8 == 0:
            diagnostic_event('operations.progress', completed=operation_index, count=len(context['operations']))
        group = nearest_mcs(nx, operation)
        record = {"kind": "operation", "tag": object_key(operation), "name": str(operation.Name),
                  "mcs": str(group.Name), "tool_name": None, "tool_number": None,
                  "feed": None, "feed_unit": None, "speed": None}
        record["raw_toolpath_time"], record["seconds"] = read_toolpath_seconds(operation, report)
        record["has_path"] = try_read(report, operation, "Path", lambda: operation.AskPathExists())
        status_type = getattr(getattr(nx.CAM, "CAMObject", None), "Status", None)
        record["path_status"] = try_read(report, operation, "Path status", lambda: enum_name(
            operation.GetStatus(), status_type, ("Complete", "Approved", "Regen", "Repost")))
        record['zmin'] = None
        prepared = context.get('prepared_zmin', {})
        if record['has_path'] and record['path_status'] != 'Regen' and object_key(operation) in prepared:
            record['zmin'] = prepared[object_key(operation)]
        elif record['has_path'] and record['path_status'] != 'Regen':
            frame_key = object_key(group)
            if frame_key not in mcs_frames:
                mcs_frames[frame_key] = try_read(report, group, 'Zmin: СКС', lambda: read_mcs(part, group), True)
            frame = mcs_frames[frame_key]
            if frame is not None:
                def zmin_progress(completed, count):
                    diagnostic_event('operations.zmin.progress', operation=str(operation.Name),
                                     operation_index=operation_index, operations=len(context['operations']),
                                     completed=completed, count=count)
                record['zmin'] = try_read(report, operation, 'Zmin',
                                         lambda: toolpath_zmin(nx, operation, frame[0], frame[1], zmin_progress), True)
        record["suppress_state"] = param(operation, "UF_PARAM_SUPPRESS_PATH", "Int")
        # UF_PARAM_suppress_path_t: none=0, all other states suppress the output.
        record["suppressed"] = (record["suppress_state"] != 0) if record["suppress_state"] is not None else None
        # Это флаг принудительного LOAD/TOOL, а не значок смены инструмента ONT.
        record["output_load_tool"] = param(operation, "UF_PARAM_OUTPUT_LOAD_TOOL", "Logical", True)
        record["tool_tag"] = None
        read_operation_stock(operation, record, param, uf, report)
        tag = try_read(report, operation, "Tool", lambda: uf.Oper.AskCutterGroup(operation.Tag))
        tool = groups.get(str(tag))
        if tool is not None:
            key = object_key(tool)
            record["tool_tag"] = key
            if key not in tools:
                tools[key] = {"tag": key, "name": str(tool.Name),
                              "number": param(tool, "UF_PARAM_TL_NUMBER", "Int")}
            record["tool_name"], record["tool_number"] = tools[key]["name"], tools[key]["number"]
        elif tag is not None:
            report.setdefault("data_issues", []).append({"object": str(operation.Name), "field": "Tool",
                                                        "message": "Инструмент не назначен или недоступен.", "optional": False})
        builder = None
        try:
            builder = part.CAMSetup.CreateFeedsBuilder([operation])
            feed_builder = builder.FeedsBuilder
            record["feed"] = try_read(report, operation, "FEED", lambda: finite_number(feed_builder.FeedCutBuilder.Value))
            record["feed_unit"] = try_read(report, operation, "FEED unit", lambda: enum_name(
                feed_builder.FeedCutBuilder.Unit, getattr(nx.CAM, "FeedRateUnit", None),
                ("PerMinute", "PerRevolution", "CutPercent", "Rapid", "None")))
            record["speed"] = try_read(report, operation, "SPEED", lambda: finite_number(feed_builder.SpindleRpmBuilder.Value))
        except Exception as exc:
            report.setdefault("data_issues", []).append({"object": str(operation.Name), "field": "FeedsBuilder",
                                                        "message": str(exc), "optional": False})
        finally:
            if builder is not None:
                builder.Destroy()
        if record["speed"] is None:
            record["speed"] = param(operation, "UF_PARAM_SPINDLE_RPM", "Double")
        records[object_key(operation)] = record

    project_sequence = annotate_operation_timing(nx, part, context, records, param, report)
    rows = []

    def collect(node, depth, program=None):
        if node["children"] is None:
            rows.append(dict(records[object_key(node["object"])], depth=depth,
                             program_tag=program or 'selected_operations'))
            return
        descendants = [records[object_key(n["object"])] for n in tree_nodes(node) if n["children"] is None]
        active = [r for r in descendants if r["suppressed"] is not True]
        known = all(r["seconds"] is not None and r["suppressed"] is not None for r in active)
        seconds = sum(r["seconds"] for r in active) if known else None
        complete = all(r.get('group_seconds') is not None for r in descendants)
        rows.append({"kind": "group", "tag": object_key(node['object']),
                     "name": str(node["object"].Name), "depth": depth,
                     "seconds": sum(r['group_seconds'] for r in descendants) if complete else seconds,
                     "toolpath_seconds": seconds,
                     "tool_change_seconds": sum(r.get('tool_change_seconds') or 0 for r in active) if complete else None,
                     "time_source": "active_toolpaths_plus_machine_tool_changes" if complete else "sum_of_active_toolpaths"})
        for child in node["children"]:
            collect(child, depth + 1, object_key(node['object']))

    if context["tree"] is not None:
        collect(context["tree"], 0)
    report["operation_rows"] = rows
    setup_tool_tags = tools_with_active_paths(records.values(), report, 'setup')
    report["tools"] = [tool for key, tool in tools.items() if key in setup_tool_tags]
    report['tool_count'] = len(report['tools'])
    report['tool_filter']['setup']['excluded_tool_tags'] = sorted(set(tools) - setup_tool_tags)
    report["time_unit"] = "seconds"
    report["nx_toolpath_time_unit"] = NX_TOOLPATH_TIME_UNIT
    if report.get("collect_project_tools", True):
        # Whole project, but only tools used by at least one unsuppressed path.
        project_tool_tags = tools_with_active_paths(project_sequence, report, 'project')
        project_tools = []
        for key, tool in groups.items():
            if not isinstance(tool, nx.CAM.Tool) or key not in project_tool_tags:
                continue
            number = tools[key]["number"] if key in tools else param(tool, "UF_PARAM_TL_NUMBER", "Int")
            project_tools.append({"tag": key, "name": str(tool.Name), "number": number})
        report['tool_filter']['project']['excluded_tool_tags'] = sorted(
            key for key, tool in groups.items() if isinstance(tool, nx.CAM.Tool) and key not in project_tool_tags)
        # Разные используемые инструменты с одинаковым T остаются отдельными строками.
        project_tools.sort(key=lambda t: (t["number"] is None, t["number"] or 0, t["name"].casefold(), t["tag"]))
        report["project_tools"] = project_tools
        report["project_tool_count"] = len(project_tools)
    annotate_tool_hierarchy(nx, part, groups, report)


def ensure_view_triad(nx, part, report, phase='finish'):
    """Enable both documented display gates; does not move WCS or CAM MCS.

    PartVisualizationScreen.TriadVisibility is separate from View.TriadVisibility.
    Reading back a true view flag alone cannot confirm the part-wide gate.
    This command intentionally enables the triad, including a previously hidden one.
    """
    record = {'phase': phase, 'before': {}, 'after': {}, 'errors': []}
    report.setdefault('view_triad', []).append(record)
    errors = record['errors']
    if part is None:
        errors.append('Нет отображаемой детали для включения триады.')
        return errors
    view = part.ModelingViews.WorkView
    try:
        screen = part.Preferences.ScreenVisualization
        before = bool(screen.TriadVisibility)
        on_value = True
        record['scope'] = 'Part.Preferences.ScreenVisualization'
    except Exception as exc:
        # Older NX bindings: the session property was superseded in NX 12.
        record['part_api_unavailable'] = str(exc)
        try:
            screen = nx.Session.GetSession().Preferences.ScreenVisualization
            before = bool(screen.TriadVisibility)
            on_value = 1
            record['scope'] = 'Session.Preferences.ScreenVisualization (legacy)'
        except Exception as fallback:
            errors.append('Настройка триады недоступна: ' + str(fallback))
            return errors
    record['before']['screen'] = before
    try:
        record['before']['view'] = bool(view.TriadVisibility)
    except Exception as exc:
        errors.append('Чтение триады вида: ' + str(exc))

    def attempt(label, action):
        try:
            action()
        except Exception as exc:
            errors.append(label + ': ' + str(exc))

    # An explicit off/on transition also invalidates stale triad graphics.
    attempt('Сброс триады вида', lambda: setattr(view, 'TriadVisibility', False))
    attempt('Сброс настройки триады', lambda: setattr(screen, 'TriadVisibility', type(on_value)(0)))
    attempt('Обновление экрана', lambda: nx.UF.UFSession.GetUFSession().Disp.MakeDisplayUpToDate())
    attempt('Включение настройки триады', lambda: setattr(screen, 'TriadVisibility', on_value))
    attempt('Включение триады вида', lambda: setattr(view, 'TriadVisibility', True))
    attempt('Перерисовка вида', view.Regenerate)
    attempt('Обновление рабочего окна', part.Views.Refresh)
    # Refresh can replace/reinitialize the current WorkView. Reacquire it.
    attempt('Настройка триады после обновления', lambda: setattr(screen, 'TriadVisibility', on_value))
    attempt('Триада текущего рабочего вида', lambda: setattr(part.ModelingViews.WorkView, 'TriadVisibility', True))
    attempt('Завершение перерисовки', lambda: nx.UF.UFSession.GetUFSession().Disp.MakeDisplayUpToDate())
    for key, read in (
            ('screen', lambda: bool(screen.TriadVisibility)),
            ('view', lambda: bool(part.ModelingViews.WorkView.TriadVisibility))):
        try:
            value = read()
            record['after'][key] = value
            if not value:
                errors.append('NX не включил флаг видимости триады: ' + key)
        except Exception as exc:
            errors.append('Проверка триады ' + key + ': ' + str(exc))
    record['flags_enabled'] = record['after'] == {'screen': True, 'view': True}
    return errors


def snapshot_view(view):
    # Origin is a NEGATED translation in view space. SetOrigin requires an
    # absolute position, so it must only receive AbsoluteOrigin on restoration.
    return {"matrix": matrix_rows(view.Matrix), "origin": xyz(view.Origin),
            "absolute_origin": xyz(view.AbsoluteOrigin),
            "scale": float(view.Scale), "lock_rotations": bool(view.LockRotations),
            "sync_views": bool(view.SyncViews), "triad_visible": bool(view.TriadVisibility)}


def verify_capture_camera(view, expected):
    """Read only: IPW must remain visible while checking the shared camera."""
    actual = snapshot_view(view)
    changes = []
    if not axes_equal(actual['matrix'], expected['matrix'], 1e-8):
        changes.append('ракурс')
    if not all(math.isclose(a, b, rel_tol=1e-9, abs_tol=1e-8)
               for a, b in zip(actual['absolute_origin'], expected['absolute_origin'])):
        changes.append('центр %s → %s' % (expected['absolute_origin'], actual['absolute_origin']))
    if not math.isclose(actual['scale'], expected['scale'], rel_tol=1e-9, abs_tol=1e-8):
        changes.append('масштаб %.9g → %.9g' % (expected['scale'], actual['scale']))
    if changes:
        raise RuntimeError('NX изменил камеру: ' + '; '.join(changes) +
                           '. Пара снимков не сохранена с разными камерами.')
    return actual


def read_view_projection(nx, view):
    """UF projection: 1 = parallel, 2 = perspective; distance only applies to 2."""
    mode, distance = nx.UF.UFSession.GetUFSession().View.AskPerspective(view.Tag)
    mode = int(mode)
    if mode not in (1, 2):
        raise RuntimeError('NX вернул неизвестный тип проекции вида: %s.' % mode)
    distance = float(distance) if mode == 2 else 0.0
    if not math.isfinite(distance) or (mode == 2 and distance <= 0):
        raise RuntimeError('NX вернул некорректное расстояние перспективы.')
    return {'type': mode, 'distance': distance}


def set_view_projection(nx, view, target):
    """Set and read back the projection; never called between Display IPW and PNG.

    UF_VIEW_set_perspective options 1/2 ignore the eye-point argument. NXOpen
    wrappers expose that pointer as either a scalar or a 3-element sequence.
    Only a binding TypeError permits retry with the alternate argument shape.
    """
    current = read_view_projection(nx, view)
    mode, distance = target['type'], target['distance']
    if current['type'] == mode and (mode == 1 or math.isclose(
            current['distance'], distance, rel_tol=1e-9, abs_tol=1e-9)):
        return False
    method = nx.UF.UFSession.GetUFSession().View.SetPerspective
    try:
        method(view.Tag, mode, distance, 0.0)
    except TypeError:
        method(view.Tag, mode, distance, [0.0, 0.0, 0.0])
    actual = read_view_projection(nx, view)
    if actual['type'] != mode or (mode == 2 and not math.isclose(
            actual['distance'], distance, rel_tol=1e-8, abs_tol=1e-8)):
        raise RuntimeError('NX не подтвердил переключение проекции вида: %s.' % actual)
    return True


def ensure_capture_projection(nx, view, report):
    """Use an orthographic image so the axes and their origin match its pixels."""
    info = report['capture_projection']
    if set_view_projection(nx, view, {'type': 1, 'distance': 0.0}):
        info['switch_count'] += 1
        view.Regenerate()
    info['status'] = 'parallel_confirmed'
    report['projection'] = 'parallel_for_capture'


def restore_view(nx, view, saved):
    errors = []
    steps = (
        ("разблокировка", lambda: setattr(view, "LockRotations", False)),
        ("синхронизация", lambda: setattr(view, "SyncViews", False)),
        ("ракурс", lambda: view.Orient(nx_matrix(nx, saved["matrix"]))
         if not axes_equal(matrix_rows(view.Matrix), saved['matrix'], 1e-9) else None),
        ("проекция", lambda: set_view_projection(nx, view, saved['projection'])
         if 'projection' in saved else None),
        ("центр", lambda: view.SetOrigin(nx.Point3d(*saved["absolute_origin"]))),
        ("масштаб", lambda: view.SetScale(saved["scale"])),
        ("блокировка", lambda: setattr(view, "LockRotations", saved["lock_rotations"])),
        ("синхронизация", lambda: setattr(view, "SyncViews", saved["sync_views"])),
        ("обновление", view.Regenerate),
        ("триада вида", lambda: setattr(view, "TriadVisibility", saved["triad_visible"])),
    )
    for label, action in steps:
        try:
            action()
        except Exception as exc:
            errors.append(label + ": " + str(exc))
    try:
        verify_capture_camera(view, saved)
    except Exception as exc:
        errors.append('проверка восстановления исходного вида: ' + str(exc))
    return errors


def png_size(path):
    with open(path, "rb") as handle:
        header = handle.read(24)
    if len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n" or header[12:16] != b"IHDR":
        raise RuntimeError("NX не создал корректный PNG: " + str(path.name))
    width, height = struct.unpack(">II", header[16:24])
    if width < 32 or height < 32:
        raise RuntimeError("Слишком маленькое изображение: " + str(path.name))
    return width, height


def export_png(nx, part, path):
    path = io_path(path)
    filename = nx_image_file_name(path)
    progress = EXPORT_PROGRESS
    if progress is not None:
        progress.hide_for_capture(True)
    try:
        builder = part.Views.CreateImageExportBuilder()
        try:
            builder.FileFormat = nx.Gateway.ImageExportBuilder.FileFormats.Png
            builder.FileName = filename
            builder.RegionMode = False
            builder.BackgroundOption = nx.Gateway.ImageExportBuilder.BackgroundOptions.CustomColor
            builder.SetCustomBackgroundColor([1.0, 1.0, 1.0])
            builder.EnhanceEdges = False
            builder.Commit()
        finally:
            builder.Destroy()
    finally:
        if progress is not None:
            progress.hide_for_capture(False)
    return png_size(path)


def capture_project_view(nx, part, output, camera):
    """Fit and capture the startup orientation; restore the camera and display."""
    view = part.ModelingViews.WorkView
    def verify(expected_camera):
        verify_capture_camera(view, expected_camera)
        actual, expected = read_view_projection(nx, view), expected_camera['projection']
        if actual['type'] != expected['type'] or not math.isclose(
                actual['distance'], expected['distance'], rel_tol=1e-8, abs_tol=1e-8):
            raise RuntimeError('Изменилась проекция текущего вида.')
    try:
        verify(camera)
    except Exception:
        errors = restore_view(nx, view, camera)
        if errors:
            raise RuntimeError('Не удалось восстановить текущий вид для первого листа: ' + '; '.join(errors))
    path = output / 'project_view.png'
    display = ScreenshotDisplay(nx, part, view, {})
    capture_error = None
    try:
        try:
            # Reuse the paired-view display guard: corner triad, WCS, CAM MCS,
            # saved coordinate systems and paths only. Visible bodies stay as is.
            display.hide()
            view.Regenerate()
            display.hide_after_camera_change()
            verify(camera)
            fit_visible_view(nx, view)
            fitted = snapshot_view(view)
            fitted['projection'] = read_view_projection(nx, view)
            if not axes_equal(fitted['matrix'], camera['matrix'], 1e-8):
                raise RuntimeError('NX изменил текущий ракурс при вписывании первого листа.')
            if fitted['projection']['type'] != camera['projection']['type']:
                raise RuntimeError('NX изменил тип проекции при вписывании первого листа.')
            display.hide_after_camera_change()
            if bool(view.TriadVisibility) or bool(part.WCS.Visibility):
                raise RuntimeError('NX не скрыл системы координат перед снимком первого листа.')
            verify(fitted)
            export_png(nx, part, path)
            verify(fitted)
        except Exception as exc:
            capture_error = exc
            raise
        finally:
            # Fit must not change the camera inherited by later setup captures.
            # Restore on success and on failures, before processing the PNG.
            errors = restore_view(nx, view, camera)
            errors.extend(display.restore())
            if errors:
                message = 'Не удалось восстановить отображение NX после снимка первого листа: ' + '; '.join(errors)
                if capture_error is not None:
                    message = str(capture_error) + '\n' + message
                raise RuntimeError(message) from capture_error
        return 'data:image/png;base64,' + base64.b64encode(project_view_png(path)).decode('ascii')
    finally:
        if path.exists():
            path.unlink()


PROJECT_MODEL_DEFAULT_TRIANGLES = 500000


class ProjectModelLimitError(RuntimeError):
    """A resource limit must never trigger per-face refaceting."""
    pass


def check_project_triangle_budget(count, limit=PROJECT_MODEL_DEFAULT_TRIANGLES):
    if count > limit:
        raise ProjectModelLimitError(
            'Превышен лимит треугольников. Уменьшите коэффициент точности или увеличьте лимит.')


class ProjectFacetCleanupError(RuntimeError):
    pass


def project_facet_triangles(nx, entity, normal_rows=None, triangle_limit=PROJECT_MODEL_DEFAULT_TRIANGLES,
                            accuracy=1.):
    """Read a temporary UF facet model; roll back even a failed FacetSolid call.

    AskDefaultParameters supplies the wrapper's actual structure/version. Do
    not construct an uninitialised struct or enable zero curve tolerances.
    NXOpen Python has FacetSolid on the supported NX releases; TessellateFace
    is not present in all Python wrappers.
    """
    accuracy = parse_project_model_accuracy(accuracy)
    uf = nx.UF.UFSession.GetUFSession()
    session = nx.Session.GetSession()
    mark = session.SetUndoMark(nx.Session.MarkVisibility.Invisible, 'Setup card mesh')
    failure = None
    try:
        parameters = uf.Facet.AskDefaultParameters()
        parameters.MaxFacetEdges = 3
        parameters.SpecifyConvexFacets = True
        units = enum_name(entity.OwningPart.PartUnits,
                          getattr(getattr(nx, 'BasePart', None), 'Units', None),
                          ('Millimeters', 'Inches'))
        millimeter = 1. / 25.4 if units == 'Inches' else 1.
        # Lower accuracy loosens geometry tolerances, not the triangle budget.
        # Normals/tangents cannot differ by more than a half turn; UF uses radians.
        distance = .01 / accuracy * millimeter
        angle = math.radians(min(180., 3. / accuracy))
        parameters.SpecifySurfaceTolerance = True
        parameters.SurfaceDistTolerance = distance
        parameters.SurfaceAngularTolerance = angle
        parameters.SpecifyCurveTolerance = True
        parameters.CurveDistTolerance = distance
        parameters.CurveAngularTolerance = angle
        parameters.NumberStorageType = 1  # UF_FACET_TYPE_DOUBLE
        parameters.SpecifyMaxFacetSize = False
        parameters.SpecifyViewDirection = False
        parameters.SpecifyParameters = False
        parameters.StoreFaceTags = False
        # Positive also when curve tolerances are disabled: some NX builds
        # validate this field unconditionally (zero failed in earlier exports).
        if not math.isfinite(parameters.CurveMaxLength) or parameters.CurveMaxLength <= 0:
            parameters.CurveMaxLength = 1000.0
        diagnostic_event('UF.Facet.FacetSolid.begin', entity=entity)
        model = uf.Facet.FacetSolid(entity.Tag, parameters)
        diagnostic_event('UF.Facet.FacetSolid.end', model=model)
        if not model:
            raise RuntimeError('NX не создал фасетную сетку.')
        return read_project_facets(nx, model, normal_rows, triangle_limit)
    except Exception as exc:
        failure = exc
        raise
    finally:
        errors = []
        try:
            diagnostic_event('mesh.UndoToMark.begin', mark=mark)
            session.UndoToMark(mark, None)
            diagnostic_event('mesh.UndoToMark.end', mark=mark)
        except Exception as exc:
            errors.append(str(exc))
        if not errors:
            try:
                session.DeleteUndoMark(mark, None)
            except Exception as exc:
                errors.append(str(exc))
        if errors:
            message = 'Не удалось удалить временную сетку NX: ' + '; '.join(errors)
            if failure is not None:
                message = str(failure) + '\n' + message
            raise ProjectFacetCleanupError(message) from failure


def read_project_facets(nx, model, normal_rows=None, triangle_limit=PROJECT_MODEL_DEFAULT_TRIANGLES):
    facet = nx.UF.UFSession.GetUFSession().Facet
    diagnostic_event('UF.Facet.AskNFacetsInModel.begin', model=model)
    count = int(facet.AskNFacetsInModel(model))
    diagnostic_event('UF.Facet.AskNFacetsInModel.end', count=count, limit=triangle_limit)
    check_project_triangle_budget(count, triangle_limit)
    if count < 1:
        raise RuntimeError('Недопустимый размер фасетной сетки: %d граней.' % count)
    triangles, visited, collected_normals = [], set(), []
    ask_normals = getattr(facet, 'AskNormalsOfFacet', None) if normal_rows is not None else None
    # UF_FACET_NULL_FACET_ID is unsigned ~0U in C and signed -1 in the
    # Python wrapper's int argument. Facet zero is a valid facet, not a sentinel.
    ident = -1
    for index in range(count):
        if index % 1024 == 0:
            diagnostic_event('mesh.facets.progress', completed=index, count=count, model=model)
        ident = int(facet.CycleFacets(model, ident))
        if ident in (-1, 4294967295) or ident in visited:
            raise RuntimeError('NX вернул неполный список фасет.')
        visited.add(ident)
        size, vertices = facet.AskVerticesOfFacet(model, ident)
        size = int(size)
        if size != 3:
            raise RuntimeError('NX вернул нетреугольную фасету (%d вершин).' % size)
        # NXOpen Python returns (count, tuple-of-coordinate-tuples).
        triangle = tuple(tuple(float(v) for v in vertices[i]) for i in range(size))
        if any(len(p) != 3 or not all(math.isfinite(v) for v in p) for p in triangle):
            raise RuntimeError('NX вернул некорректные координаты фасеты.')
        a, b, c = triangle
        if sum(v*v for v in cross(tuple(b[i]-a[i] for i in range(3)),
                                  tuple(c[i]-a[i] for i in range(3)))) > 0:
            triangles.append(triangle)
            normals = None
            if ask_normals is not None:
                try:
                    n, values = ask_normals(model, ident)
                    if int(n) != 3:
                        raise ValueError('Invalid normal count')
                    normals = tuple(unit(tuple(float(v) for v in values[i])) for i in range(3))
                    if any(len(v) != 3 or not all(math.isfinite(x) for x in v) for v in normals):
                        raise ValueError('Invalid normal vector')
                except MemoryError:
                    raise
                except Exception:
                    # Normal data is visual only. Keep the valid geometry and
                    # use crease-aware smoothing in the viewer if unavailable.
                    normals = None
                    ask_normals = None
            collected_normals.append(normals)
    if not triangles:
        raise RuntimeError('NX вернул пустую фасетную сетку.')
    if normal_rows is not None:
        normal_rows.extend(collected_normals)
    diagnostic_event('mesh.facets.end', completed=count, count=count, model=model)
    return triangles


def collect_project_model(nx, part, camera, accuracy=1., triangle_limit=PROJECT_MODEL_DEFAULT_TRIANGLES):
    """Embed only displayed bodies, with the startup NX axes, entirely in memory.

    AskVisibleObjects honours hidden objects, layers, assembly reference sets
    and occurrence visibility. Curves, coordinate systems and toolpaths cannot
    enter the mesh. Occurrence coordinates are transformed exactly once.
    """
    accuracy = parse_project_model_accuracy(accuracy)
    triangle_limit = parse_project_model_triangle_limit(triangle_limit)
    uf = nx.UF.UFSession.GetUFSession()
    view = part.ModelingViews.WorkView
    bodies, seen = [], set()
    facet_type = getattr(getattr(nx, 'Facet', None), 'FacetedBody', ())
    diagnostic_event('view.AskVisibleObjects.begin')
    visible_objects = view.AskVisibleObjects()
    diagnostic_event('view.AskVisibleObjects.end')
    for obj in visible_objects:
        if not (isinstance(obj, nx.Body) or (facet_type and isinstance(obj, facet_type))):
            continue
        if obj.IsBlanked or object_key(obj) in seen:
            continue
        seen.add(object_key(obj))
        bodies.append(obj)
    if not bodies:
        raise RuntimeError('Для первого листа не найдено отображаемых тел NX.')
    axes = camera['axes']
    triangles, normal_rows, cached = [], [], {}
    diagnostic_event('mesh.bodies', count=len(bodies))
    for body_index, body in enumerate(bodies, 1):
        diagnostic_event('mesh.body.begin', index=body_index, total=len(bodies), entity=body)
        remaining = triangle_limit - len(triangles)
        check_project_triangle_budget(1, remaining)
        prototype = body.Prototype if body.IsOccurrence else body
        key = object_key(prototype)
        if key not in cached:
            source_normals = []
            if facet_type and isinstance(prototype, facet_type):
                source_triangles = read_project_facets(nx, prototype.Tag, source_normals, remaining)
            else:
                try:
                    source_triangles = project_facet_triangles(nx, prototype, source_normals, remaining, accuracy)
                except (ProjectFacetCleanupError, ProjectModelLimitError, MemoryError):
                    raise
                except Exception as body_error:
                    # Non-manifold bodies can fail as a whole. Facet every face
                    # with the same valid parameters; never silently omit a face.
                    diagnostic_event('body.GetFaces.begin', entity=prototype, error=str(body_error))
                    faces = list(prototype.GetFaces())
                    diagnostic_event('body.GetFaces.end', count=len(faces))
                    if not faces:
                        raise RuntimeError('Не удалось получить сетку тела: ' + str(body_error)) from body_error
                    face_triangles, source_normals = [], []
                    for index, face in enumerate(faces, 1):
                        diagnostic_event('mesh.face.begin', index=index, count=len(faces))
                        try:
                            face_triangles.extend(project_facet_triangles(
                                nx, face, source_normals, remaining - len(face_triangles), accuracy))
                        except (ProjectFacetCleanupError, ProjectModelLimitError, MemoryError):
                            raise
                        except Exception as exc:
                            raise RuntimeError('Не удалось получить сетку тела, грань %d из %d: %s'
                                               % (index, len(faces), exc)) from exc
                    source_triangles = face_triangles
            cached[key] = source_triangles, source_normals
        # Repeated assembly occurrences also consume the document budget.
        # Stop before copying/transformation, not after an oversized body.
        check_project_triangle_budget(len(cached[key][0]), remaining)
        diagnostic_event('mesh.transform.begin', entity=body, triangles=len(cached[key][0]))
        transform = uf.Assem.AskTransformOfOcc(body.Tag) if body.IsOccurrence else None
        normal_matrix = None
        if transform is not None:
            rows = [tuple(float(transform[i][j]) for j in range(3)) for i in range(3)]
            normal_matrix = [cross(rows[1], rows[2]), cross(rows[2], rows[0]), cross(rows[0], rows[1])]
            determinant = dot(rows[0], normal_matrix[0])
            if abs(determinant) < 1e-30:
                raise RuntimeError('Некорректное преобразование компонента для 3D-модели.')
            normal_matrix = [tuple(v/determinant for v in row) for row in normal_matrix]
        for triangle_index, (triangle, normals) in enumerate(zip(*cached[key])):
            if triangle_index % 4096 == 0:
                diagnostic_event('mesh.transform.progress', completed=triangle_index, count=len(cached[key][0]))
            points = []
            for point in triangle:
                if transform is not None:
                    point = tuple(sum(float(transform[i][j])*point[j] for j in range(3)) +
                                  float(transform[i][3]) for i in range(3))
                points.append(tuple(dot(point, axis) for axis in axes))
            triangles.append(points)
            if normals is not None:
                transformed = []
                for normal in normals:
                    if normal_matrix is not None:
                        normal = tuple(dot(row, normal) for row in normal_matrix)
                    transformed.append(unit(tuple(dot(normal, axis) for axis in axes)))
                normals = transformed
            normal_rows.append(normals)
        diagnostic_event('mesh.transform.end', triangles=len(triangles))
        diagnostic_event('mesh.body.end', index=body_index, triangles=len(triangles))
    diagnostic_event('mesh.pack.begin', triangles=len(triangles))
    # Center before float32 packing: large absolute part origins retain detail.
    lower = [min(p[i] for t in triangles for p in t) for i in range(3)]
    upper = [max(p[i] for t in triangles for p in t) for i in range(3)]
    center = [(lower[i]+upper[i])/2 for i in range(3)]
    span = max(upper[i]-lower[i] for i in range(3))
    if not math.isfinite(span) or span <= 0:
        raise RuntimeError('Пустые или некорректные границы 3D-модели.')
    packed = bytearray()
    for triangle in triangles:
        for point in triangle:
            packed.extend(struct.pack('<fff', *((point[i]-center[i])/span for i in range(3))))
    packed_normals = bytearray()
    if all(normals is not None for normals in normal_rows):
        for normals in normal_rows:
            for normal in normals:
                packed_normals.extend(struct.pack('<hhh', *(round(max(-1., min(1., v))*32767) for v in normal)))
    projection = None
    if camera.get('projection', {}).get('type') == 2:
        origin = camera['absolute_origin']
        eye = [(dot(origin, axes[i])-center[i])/span for i in range(3)]
        eye[2] += camera['projection']['distance']/span
        # Keep all vertices in front of the eye during subsequent rotation.
        eye[2] = max(eye[2], 2.)
        projection = eye
    diagnostic_event('mesh.pack.end', vertices_bytes=len(packed), normals_bytes=len(packed_normals))
    return {'schema': 1, 'id': uuid.uuid4().hex, 'triangles': len(triangles), 'triangle_limit': triangle_limit,
            'vertices': base64.b64encode(packed).decode('ascii'),
            'normals': base64.b64encode(packed_normals).decode('ascii'),
            'normal_format': 'snorm16',
            'aspect': max(.12, min(8., (upper[0]-lower[0])/max(upper[1]-lower[1], span*1e-6))),
            'projection': projection,
            'view': {'rotation': [0, 0, 0, 1], 'pan': [0, 0], 'zoom': 1}}


def read_png_rgb(path):
    """PNG NX (8 bit, non-interlaced), без Pillow и сторонних пакетов."""
    data = path.read_bytes()
    if data[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError('Некорректный PNG: ' + path.name)
    offset, chunks, palette, transparency, header = 8, [], None, None, None
    while offset + 12 <= len(data):
        size = struct.unpack_from('>I', data, offset)[0]
        kind, payload = data[offset + 4:offset + 8], data[offset + 8:offset + 8 + size]
        if offset + size + 12 > len(data):
            raise ValueError('Неполный PNG: ' + path.name)
        if zlib.crc32(kind + payload) & 0xffffffff != struct.unpack_from('>I', data, offset + 8 + size)[0]:
            raise ValueError('Повреждённый PNG: ' + path.name)
        if kind == b'IHDR':
            header = struct.unpack('>IIBBBBB', payload)
        elif kind == b'IDAT':
            chunks.append(payload)
        elif kind == b'PLTE':
            palette = payload
        elif kind == b'tRNS':
            transparency = payload
        elif kind == b'IEND':
            break
        offset += size + 12
    if header is None:
        raise ValueError('Нет заголовка PNG.')
    width, height, bits, color, compression, filtering, interlace = header
    if bits != 8 or color not in (0, 2, 3, 4, 6) or compression or filtering or interlace:
        raise ValueError('Не поддержан формат PNG для обрезки: ' + str(header))
    if not 0 < width * height <= 64000000:
        raise ValueError('Слишком большой PNG для обрезки.')
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[color]
    stride = width * channels
    expected = (stride + 1) * height
    decoder = zlib.decompressobj()
    raw = decoder.decompress(b''.join(chunks), expected + 1)
    if len(raw) != expected or not decoder.eof:
        raise ValueError('Неверная длина данных PNG.')
    previous, rows = bytearray(stride), []
    for y in range(height):
        start = y * (stride + 1)
        mode, row = raw[start], bytearray(raw[start + 1:start + stride + 1])
        if mode == 1:
            for x in range(channels, stride):
                row[x] = (row[x] + row[x - channels]) & 255
        elif mode == 2:
            row = bytearray((a + b) & 255 for a, b in zip(row, previous))
        elif mode in (3, 4):
            for x in range(stride):
                left, up = row[x - channels] if x >= channels else 0, previous[x]
                if mode == 3:
                    prediction = (left + up) // 2
                else:
                    corner = previous[x - channels] if x >= channels else 0
                    value = left + up - corner
                    dl, du, dc = abs(value - left), abs(value - up), abs(value - corner)
                    prediction = left if dl <= du and dl <= dc else (up if du <= dc else corner)
                row[x] = (row[x] + prediction) & 255
        elif mode != 0:
            raise ValueError('Неизвестный фильтр PNG.')
        previous = row
        if color == 2 and not transparency:
            rows.append(bytes(row))
            continue
        rgb = bytearray()
        for x in range(0, stride, channels):
            alpha = 255
            if color == 3:
                index = row[x]
                if not palette or index * 3 + 3 > len(palette):
                    raise ValueError('Некорректная палитра PNG.')
                values = palette[index * 3:index * 3 + 3]
                alpha = transparency[index] if transparency and index < len(transparency) else 255
            elif color in (0, 4):
                values = (row[x],) * 3
                alpha = row[x + 1] if color == 4 else 255
                if color == 0 and transparency and row[x] == struct.unpack('>H', transparency)[0]:
                    alpha = 0
            else:
                values = row[x:x + 3]
                alpha = row[x + 3] if color == 6 else 255
                if color == 2 and transparency and tuple(values) == struct.unpack('>HHH', transparency):
                    alpha = 0
            rgb.extend((v * alpha + 255 * (255 - alpha) + 127) // 255 for v in values)
        rows.append(bytes(rgb))
    return width, height, rows


def png_rgb_bytes(width, height, rows):
    def chunk(kind, payload):
        return struct.pack('>I', len(payload)) + kind + payload + struct.pack('>I', zlib.crc32(kind + payload) & 0xffffffff)
    raw = b''.join(b'\0' + row for row in rows)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b''))


def content_bounds(width, height, rows):
    # Белый фон задан самим ImageExportBuilder. Не удаляем отдельные
    # объекты/линии: все небелые пиксели входят в рамку, включая оснастку.
    ink_table = bytes(1 if v < 251 else 0 for v in range(256))
    left, top, right, bottom = width, height, 0, 0
    for y, row in enumerate(rows):
        mask = row.translate(ink_table)
        first = mask.find(b'\1')
        if first >= 0:
            left, right = min(left, first // 3), max(right, mask.rfind(b'\1') // 3 + 1)
            top, bottom = min(top, y), y + 1
    return (left, top, right, bottom) if right > left else None


def project_view_png(path):
    """Trim the white viewport in memory; retain all visible geometry and 2% margins."""
    width, height, rows = read_png_rgb(path)
    bounds = content_bounds(width, height, rows)
    if bounds is None:
        raise RuntimeError('На снимке первого листа не найдено видимое тело. Проверьте текущий вид NX.')
    x0, y0, x1, y1 = bounds
    content_width, content_height = x1 - x0, y1 - y0
    # Independent margins keep narrow/tall bodies tightly framed as well.
    margin = .02
    pad_x = max(2, math.ceil(content_width * margin / (1 - 2 * margin)))
    pad_y = max(2, math.ceil(content_height * margin / (1 - 2 * margin)))
    target_width, target_height = content_width + 2 * pad_x, content_height + 2 * pad_y
    empty = b'\xff' * (target_width * 3)
    side = b'\xff' * (pad_x * 3)
    cropped = ([empty] * pad_y +
               [side + row[x0 * 3:x1 * 3] + side for row in rows[y0:y1]] +
               [empty] * pad_y)
    return png_rgb_bytes(target_width, target_height, cropped)


def capture_frame_bounds(output, item, images=None, bounds=None):
    """Validate the final pair once; this never changes the camera or recaptures."""
    names = ('file', 'no_ipw_file') if item.get('no_ipw_file') else ('file',)
    if images is None:
        images = [read_png_rgb(output / item[name]) for name in names]
    width, height = images[0][:2]
    if any(image[:2] != (width, height) for image in images):
        raise RuntimeError('Размеры пары снимков различаются. Не меняйте размер окна NX во время экспорта.')
    boxes = bounds if bounds is not None else [content_bounds(*image) for image in images]
    nonempty = [box for box in boxes if box is not None]
    box = (min(b[0] for b in nonempty), min(b[1] for b in nonempty),
           max(b[2] for b in nonempty), max(b[3] for b in nonempty)) if nonempty else None
    # The no-IPW image may be empty when every assembly component is hidden.
    # The IPW image must be nonempty and all visible objects must leave a margin.
    # Allow one pixel for rasterization of edges at the 5% boundary.
    margin_x, margin_y = max(1, math.floor(width * FRAME_MARGIN) - 1), max(1, math.floor(height * FRAME_MARGIN) - 1)
    safe = bool(boxes[0] is not None and box is not None and
                box[0] >= margin_x and box[1] >= margin_y and
                box[2] <= width - margin_x and box[3] <= height - margin_y)
    return {'source_size': [width, height], 'content_box': box,
            'ipw_box': boxes[0], 'safe': safe, 'margin': FRAME_MARGIN}


def uf_output_vector(method, tag, count):
    """NXOpen Python returns output arrays; some bindings fill a supplied array."""
    try:
        result = method(tag)
    except TypeError:
        result = [float('nan')] * count
        returned = method(tag, result)
        if returned is not None:
            result = returned
    values = tuple(float(v) for v in result)
    if len(values) != count or not all(math.isfinite(v) for v in values):
        raise ValueError('NX вернул некорректные координаты вида.')
    return values


def project_mcs_origin(origin, center, axes, clip, width, height):
    """Orthographic ABS -> current view plane -> uncropped PNG (top-left origin).

    UF_VIEW_ask_current_xy_clip: xmin,xmax,ymin,ymax in current View Space,
    in model units, before pixel conversion. No assumptions about DPI or Scale.
    Reference: https://www.ugapi.com/doc/ufun/uf_view/global.html
    """
    values = list(origin) + list(center) + list(clip) + [width, height]
    if not all(math.isfinite(float(v)) for v in values):
        raise ValueError('Некорректные координаты СКС или границы вида.')
    xmin, xmax, ymin, ymax = clip
    if xmax <= xmin or ymax <= ymin or width <= 0 or height <= 0:
        raise ValueError('Пустые границы вида NX.')
    ratio = (width / float(height)) / ((xmax - xmin) / (ymax - ymin))
    if abs(ratio - 1.) > .015:
        raise ValueError('Границы рабочего вида не совпадают с кадром. Разверните один вид NX на всю область моделирования.')
    frame = tuple(unit(axis) for axis in axes)
    if len(frame) != 3 or any(abs(dot(frame[i], frame[j])) > 1e-5 for i in range(3) for j in range(i)):
        raise ValueError('Некорректные оси камеры NX.')
    delta = tuple(float(a) - float(b) for a, b in zip(origin, center))
    x, y = dot(delta, frame[0]), dot(delta, frame[1])
    return [(x - xmin) * width / (xmax - xmin),
            (ymax - y) * height / (ymax - ymin)]


def capture_mcs_anchor(nx, view, axes, width, height, report, label, variant):
    """Read-only: safe between Display IPW and its removal; never redraws NX."""
    data = {'status': 'unavailable', 'source_size': [width, height],
            'method': 'current_xy_clip_absolute_origin'}
    try:
        origin = report.get('mcs_origin_absolute')
        if origin is None or len(origin) != 3:
            raise ValueError('Не прочитано начало СКС первой операции.')
        uf_view = nx.UF.UFSession.GetUFSession().View
        projection, distance = uf_view.AskPerspective(view.Tag)
        data['projection_type'] = int(projection)
        if int(projection) != 1:
            raise ValueError('NX изменил проекцию вида во время снимка: требуется параллельная проекция. '
                             'Оси этого снимка не построены.')
        clip = uf_output_vector(uf_view.AskCurrentXyClip, view.Tag, 4)
        # View.Origin is a translation in rotated View Space, NOT an ABS point.
        try:
            center = xyz(view.AbsoluteOrigin)
        except AttributeError:
            center = uf_output_vector(uf_view.AskCenter, view.Tag, 3)
        point = project_mcs_origin(origin, center, axes, clip, width, height)
        data.update(origin_absolute=list(origin), view_center_absolute=list(center),
                    current_xy_clip=list(clip), source_pixel=point)
        if not (0 <= point[0] < width and 0 <= point[1] < height):
            raise ValueError('Начало СКС находится за границей снимка. Включите его в кадр NX.')
        data['status'] = 'projected'
    except Exception as exc:
        data['message'] = str(exc)
        report.setdefault('mcs_anchor_issues', []).append('%s · %s: %s' % (label, variant.upper(), exc))
    return data


def finish_mcs_anchors(item):
    """Apply the exact shared crop and padding, retaining a diagnostic source point."""
    crop = item.get('image_fit', {})
    for anchor in item.get('mcs_anchors', {}).values():
        if anchor.get('status') != 'projected':
            continue
        x, y = anchor['source_pixel']
        if crop.get('status') == 'cropped':
            x += crop['padding'] - crop['content_box'][0]
            y += crop['padding'] - crop['content_box'][1]
        width, height = item['width'], item['height']
        anchor.update(image_pixel=[x, y], image_size=[width, height],
                      normalized=[x / width, y / height])


def fit_view_images(output, item, report, images=None, bounds=None):
    """Общая рамка IPW/NO IPW сохраняет их взаимный масштаб и положение."""
    filenames = [item['file']] + ([item['no_ipw_file']] if item.get('no_ipw_file') else [])
    try:
        if images is None:
            images = [read_png_rgb(output / name) for name in filenames]
        width, height = images[0][:2]
        if any(image[:2] != (width, height) for image in images):
            raise ValueError('Размеры пары IPW / NO IPW различаются.')
        if bounds is None:
            bounds = [content_bounds(*image) for image in images]
        if any(box is None for box in bounds):
            raise ValueError('Один из снимков пустой; обрезка не выполнена.')
        x0, y0 = min(b[0] for b in bounds), min(b[1] for b in bounds)
        x1, y1 = max(b[2] for b in bounds), max(b[3] for b in bounds)
        # Keep the true datum in the image, even if it lies outside visible material.
        for anchor in item.get('mcs_anchors', {}).values():
            if anchor.get('status') == 'projected':
                x, y = anchor['source_pixel']
                x0, y0 = min(x0, int(math.floor(x))), min(y0, int(math.floor(y)))
                x1, y1 = max(x1, int(math.floor(x)) + 1), max(y1, int(math.floor(y)) + 1)
        # Keep at least 5% on each side in the embedded image as well.
        padding = max(2, math.ceil(max(x1 - x0, y1 - y0) * FRAME_MARGIN / FRAME_FIT_FRACTION))
        # Добавляем одинаковый белый отступ, в том числе у края окна.
        target_width, target_height = x1 - x0 + padding * 2, y1 - y0 + padding * 2
        encoded = []
        for _, _, rows in images:
            empty = b'\xff' * (target_width * 3)
            cropped = [empty] * padding
            cropped += [b'\xff' * (padding * 3) + row[x0 * 3:x1 * 3] + b'\xff' * (padding * 3) for row in rows[y0:y1]]
            cropped += [empty] * padding
            encoded.append(png_rgb_bytes(target_width, target_height, cropped))
    except Exception as exc:
        item['image_fit'] = {'status': 'unchanged', 'message': str(exc)}
        report.setdefault('image_fit_issues', []).append(item['label'] + ': ' + str(exc))
        finish_mcs_anchors(item)
        return
    # Запись после успешного расчёта всей пары. Ошибки диска не скрываем.
    for name, data in zip(filenames, encoded):
        (output / name).write_bytes(data)
    item.update(width=target_width, height=target_height,
                image_fit={'status': 'cropped', 'source_size': [width, height],
                           'content_box': [x0, y0, x1, y1], 'padding': padding,
                           'shared_pair': len(images) == 2})
    finish_mcs_anchors(item)


class ScreenshotDisplay:
    """Только временная видимость; не меняет СКС и CAM-параметры."""
    def __init__(self, nx, part, view, report, context=None):
        self.nx, self.part, self.view, self.report = nx, part, view, report
        self.context = context
        self.restore_actions = []
        self.cam = None
        self.original_triad = None

    def hide(self):
        info = self.report['screenshot_display'] = {'hidden': [], 'restored': False}
        def switch(obj, attr, value, label):
            original = getattr(obj, attr)
            self.restore_actions.append((label, lambda: setattr(obj, attr, original)))
            setattr(obj, attr, value)
            if getattr(obj, attr) != value:
                raise RuntimeError('NX не отключил ' + label)
            info['hidden'].append(label)
        session = self.nx.Session.GetSession()
        # Hide only the captured view. The session/part preference also affects
        # other views and may change this view's flag before it can be saved.
        self.original_triad = bool(self.view.TriadVisibility)
        info['triad'] = {'before': self.original_triad, 'scope': 'work_view_only'}
        switch(self.view, 'TriadVisibility', False, 'триада рабочего вида')
        switch(self.part.WCS, 'Visibility', False, 'WCS')
        # Штатные XM/YM/ZM — временная CAM-графика, а не объект CSYS.
        # Blank() и WCS.Visibility её не скрывают (ошибка предыдущих версий).
        self.cam = session.CAMSession
        original_mcs_display = bool(self.cam.GetMcsDisplay())
        self.restore_actions.append(('CAM СКС',
                                     lambda: self.cam.SetMcsDisplay(original_mcs_display)))
        self.cam.SetMcsDisplay(False)
        self.check_cam_hidden()
        info['hidden'].append('CAM СКС XM/YM/ZM')
        # Выбор первой операции может включить её траекторию в навигаторе.
        switch(self.cam.PathDisplay, 'DisplayToolPath', False, 'траектории CAM')
        # Сюда входят сохранённые CartesianCoordinateSystem, в т. ч. MCS.
        # Ранее скрытые СКС оставляем скрытыми и при восстановлении.
        systems = list(self.part.CoordinateSystems)
        if self.context and self.context.get('mcs') is not None:
            builder = self.part.CAMSetup.CAMGroupCollection.CreateMillOrientGeomBuilder(self.context['mcs'])
            try:
                if builder.Mcs is not None:
                    systems.append(builder.Mcs)
            finally:
                builder.Destroy()
        seen = set()
        for csys in systems:
            key = object_key(csys)
            if key in seen:
                continue
            seen.add(key)
            if not csys.IsBlanked:
                label = 'СКС ' + str(getattr(csys, 'Name', key))
                self.restore_actions.append((label, csys.Unblank))
                csys.Blank()
                info['hidden'].append(label)

    def hide_after_camera_change(self):
        """Clear regenerated CAM arrows before NO IPW, never after Display IPW.

        The v14 user's PNGs contain XM/YM/ZM after Orient/Fit even though the
        display flag is False. Force a real state transition instead of merely
        reading that cached flag. This changes visibility only, not the MCS.
        """
        self.cam.SetMcsDisplay(True)
        self.cam.SetMcsDisplay(False)
        self.part.WCS.Visibility = False
        self.view.TriadVisibility = False
        self.check_cam_hidden()
        self.nx.UF.UFSession.GetUFSession().Disp.MakeDisplayUpToDate()
        info = self.report['screenshot_display']
        info['mcs_visibility_reapplied'] = info.get('mcs_visibility_reapplied', 0) + 1

    def check_cam_hidden(self):
        # Только чтение: особенно важно не перерисовывать вид после Display IPW.
        if self.cam is None or self.cam.GetMcsDisplay():
            raise RuntimeError('NX не скрыл штатную CAM СКС перед снимком. '
                               'Экспорт остановлен, чтобы чужие оси не попали в карту.')

    def restore(self):
        errors = []
        for label, action in reversed(self.restore_actions):
            try:
                action()
            except Exception as exc:
                errors.append('восстановление ' + label + ': ' + str(exc))
        if self.original_triad is not None:
            # Repaint after restoring CSYS/CAM graphics and the camera, then
            # reapply the saved view flag if NX changed it while repainting.
            try:
                self.part.Views.Refresh()
            except Exception as exc:
                errors.append('обновление рабочего окна: ' + str(exc))
            try:
                if bool(self.view.TriadVisibility) != self.original_triad:
                    self.view.TriadVisibility = self.original_triad
                self.nx.UF.UFSession.GetUFSession().Disp.MakeDisplayUpToDate()
            except Exception as exc:
                errors.append('отображение триады вида: ' + str(exc))
            try:
                actual = bool(self.view.TriadVisibility)
                self.report['screenshot_display']['triad']['after'] = actual
                if actual != self.original_triad:
                    errors.append('NX не восстановил исходную видимость триады вида.')
            except Exception as exc:
                errors.append('проверка триады вида: ' + str(exc))
        self.report.setdefault('screenshot_display', {})['restored'] = not errors
        return errors


def display_ipw(part, context, report):
    """Штатное отображение IPW, без генерации траекторий и сохранения .prt."""
    report["current_stage"] = "Display IPW"
    diagnostic_event('stage', stage=report['current_stage'])
    source = context.get('ipw_source')
    if source is None:
        raise RuntimeError('Источник IPW первой MCS установа не определён до съёмки.')
    operations = list(source['operations'])
    source_name = str(source['mcs'].Name)
    info = report.setdefault("ipw", {})
    info.update(status="pending", operations=[str(op.Name) for op in operations],
                source_mcs_name=source_name,
                api="CAMSetup.DisplayIpwWithPickOnPath")
    try:
        if not operations:
            raise RuntimeError("В выбранном установе нет операций для Display IPW.")
        part.CAMSetup.DisplayIpwWithPickOnPath(operations)
        # Refresh / Regenerate, Orient / Fit can remove the transient graphics.
        # The final PNG therefore immediately follows its own Display IPW.
        info.update(status="displayed", activation_count=info.get("activation_count", 0) + 1)
    except Exception as exc:
        info.update(status="error", message=str(exc))
        raise RuntimeError('Не удалось выполнить Display IPW от первой MCS «%s» установа. '
                           'Пара снимков не завершена. %s' % (source_name, exc)) from exc


def fit_visible_view(nx, view):
    """Fit actual visible geometry into 90% of the viewport, centered by NX.

    UF_VIEW_fit_view accepts the fraction directly, independent of the user's
    fit preference. Transient IPW alone is not sufficient: its temporary facet
    body must be visible when this is called for the capture cameras.
    """
    nx.UF.UFSession.GetUFSession().View.FitView(view.Tag, FRAME_FIT_FRACTION)
    view.Regenerate()
    if not math.isfinite(float(view.Scale)) or float(view.Scale) <= 0:
        raise RuntimeError('NX вернул некорректный масштаб после вписывания.')


def capture_views(nx, part, view, basis, output, report, ipw_context=None):
    saved = snapshot_view(view)
    report["original_view"] = saved
    projection_info = report['capture_projection'] = {
        'status': 'pending', 'switch_count': 0, 'restored': False,
        'api': 'UF.View.AskPerspective / SetPerspective',
        'restore_errors': []}
    ipw_requested = ipw_context is not None
    ipw_cleared = False
    fit_mark, fit_facet = None, None
    display = ScreenshotDisplay(nx, part, view, report, ipw_context)
    if ipw_requested:
        report["ipw"] = {"status": "pending", "sequence": "per_view_fit_then_no_ipw_then_ipw",
                         "activation_count": 0, "fit_count": 0, "pairs": 0, "events": []}

    def clear_ipw():
        nonlocal ipw_cleared
        report["current_stage"] = "Отключение IPW"
        diagnostic_event('stage', stage=report['current_stage'])
        ipw_cleared = False
        diagnostic_event('CAM.DeleteIpwWithPickOnPath.begin')
        part.CAMSetup.DeleteIpwWithPickOnPath()
        diagnostic_event('CAM.DeleteIpwWithPickOnPath.end')
        ipw_cleared = True
        report["ipw"]["events"].append({"action": "hide_ipw"})

    def prepare_capture_display():
        part.Views.Refresh()
        # Refresh/Orient can reapply saved view settings. Finish this before IPW.
        ensure_capture_projection(nx, view, report)
        display.hide_after_camera_change()

    def capture_once(item, fitted_camera):
        nonlocal ipw_cleared
        prepare_capture_display()
        camera = verify_capture_camera(view, fitted_camera)
        if fit_facet is not None and not fit_facet.IsBlanked:
            raise RuntimeError('Вспомогательная геометрия IPW видна перед итоговым снимком.')
        if not ipw_requested:
            width, height = export_png(nx, part, output / item["file"])
            item.update(width=width, height=height)
            actual = tuple(xyz(view.GetAxis(axis)) for axis in
                           (nx.XYZAxis.XAxis, nx.XYZAxis.YAxis, nx.XYZAxis.ZAxis))
            item['camera_axes_absolute'] = actual
            item['mcs_anchors'] = {'ipw': capture_mcs_anchor(nx, view, actual, width, height, report, item['label'], 'ipw')}
            return
        filename = Path(item["file"]).stem + "_no_ipw.png"
        report["current_stage"] = "NO IPW · " + item["label"]
        diagnostic_event('stage', stage=report['current_stage'])
        width, height = export_png(nx, part, output / filename)
        verify_capture_camera(view, camera)
        no_ipw_axes = tuple(xyz(view.GetAxis(axis)) for axis in
                            (nx.XYZAxis.XAxis, nx.XYZAxis.YAxis, nx.XYZAxis.ZAxis))
        no_ipw_anchor = capture_mcs_anchor(nx, view, no_ipw_axes, width, height, report, item['label'], 'no-ipw')
        report["ipw"]["events"].append({"action": "capture_no_ipw", "file": filename})
        ipw_cleared = False  # В том числе если команда завершится частично.
        display_ipw(part, ipw_context, report)
        report["ipw"]["events"].append({"action": "show_ipw", "view": item["file"]})
        report["current_stage"] = "IPW · " + item["label"]
        diagnostic_event('stage', stage=report['current_stage'])
        # Между включением IPW и этим снимком нет команд изменения вида.
        display.check_cam_hidden()
        if not fit_facet.IsBlanked:
            raise RuntimeError('NX включил вспомогательную геометрию при Display IPW.')
        verify_capture_camera(view, camera)
        ipw_width, ipw_height = export_png(nx, part, output / item["file"])
        report["ipw"]["events"].append({"action": "capture_ipw", "file": item["file"]})
        ipw_camera = verify_capture_camera(view, camera)
        ipw_axes = tuple(xyz(view.GetAxis(axis)) for axis in
                        (nx.XYZAxis.XAxis, nx.XYZAxis.YAxis, nx.XYZAxis.ZAxis))
        ipw_anchor = capture_mcs_anchor(nx, view, ipw_axes, ipw_width, ipw_height, report, item['label'], 'ipw')
        clear_ipw()
        if (width, height) != (ipw_width, ipw_height):
            raise RuntimeError("Размеры снимков IPW и NO IPW различаются. Не меняйте размер окна NX во время экспорта.")
        item.update(width=width, height=height, no_ipw_file=filename, default_variant="ipw",
                    no_ipw_camera=camera, ipw_camera=ipw_camera,
                    no_ipw_camera_axes_absolute=no_ipw_axes, ipw_camera_axes_absolute=ipw_axes)
        item['mcs_anchors'] = {'ipw': ipw_anchor, 'no-ipw': no_ipw_anchor}

    def create_fit_geometry():
        """Create the IPW fit helper once, without changing the camera.

        It is unblanked only for Fit and blanked for BOTH final images. Undo
        is deferred to the final cleanup, never used between fitting/capture.
        """
        nonlocal ipw_cleared, fit_mark, fit_facet
        create = getattr(part.CAMSetup, 'CreateFacetBodyForIpwDisplay', None)
        if not callable(create):
            raise RuntimeError('В этой версии NX недоступно получение геометрии Display IPW '
                               'для вписывания без пробных снимков.')
        prepare_capture_display()
        session = nx.Session.GetSession()
        fit_mark = session.SetUndoMark(nx.Session.MarkVisibility.Invisible, 'Setup card IPW fit')
        ipw_cleared = False
        display_ipw(part, ipw_context, report)
        report['current_stage'] = 'Геометрия IPW для вписывания'
        diagnostic_event('stage', stage=report['current_stage'])
        diagnostic_event('CAM.CreateFacetBodyForIpwDisplay.begin')
        fit_facet = create()
        diagnostic_event('CAM.CreateFacetBodyForIpwDisplay.end')
        if fit_facet is None or not fit_facet.Tag:
            raise RuntimeError('NX не вернул геометрию отображаемого IPW.')
        fit_facet.Layer = fit_facet.OwningPart.Layers.WorkLayer
        fit_facet.Unblank()
        report['ipw']['fit_geometry'] = 'CAMSetup.CreateFacetBodyForIpwDisplay'

    def capture(item, fitted_camera):
        capture_once(item, fitted_camera)
        # NX exports are decoded once per pair. Reuse the same immutable RGB
        # rows and bounds for validation/cropping, then release them before the
        # next view. This avoids a second slow Python PNG filter pass, without
        # changing any NX display commands, pixels, image size or compression.
        filenames = [item['file']] + ([item['no_ipw_file']] if item.get('no_ipw_file') else [])
        images = [read_png_rgb(output / name) for name in filenames]
        bounds = [content_bounds(*image) for image in images]
        measured = capture_frame_bounds(output, item, images=images, bounds=bounds)
        item['capture_framing'] = dict(measured, attempts=1, trial_images=0,
                                       method='geometry_fit_5_percent')
        if not measured['safe']:
            raise RuntimeError('На итоговом кадре «%s» NX не оставил поле 5%%. '
                               'Пересъёмка не выполнялась; прежняя HTML-карта сохранена.' % item['label'])
        fit_view_images(output, item, report, images=images, bounds=bounds)
        report['views'].append(item)
        if ipw_requested:
            report['ipw']['pairs'] += 1

    try:
        report['current_stage'] = 'Подготовка параллельной проекции для снимков'
        diagnostic_event('stage', stage=report['current_stage'])
        saved['projection'] = read_view_projection(nx, view)
        projection_info['original'] = dict(saved['projection'])
        # Do not propagate this temporary display change to synchronized views.
        view.SyncViews = False
        view.LockRotations = False
        ensure_capture_projection(nx, view, report)
        report['current_stage'] = 'Скрытие штатных осей NX'
        diagnostic_event('stage', stage=report['current_stage'])
        display.hide()
        if ipw_requested:
            # Обеспечиваем NO IPW даже при уже включённом отображении до запуска.
            clear_ipw()
            create_fit_geometry()
        # Same one-pass view order as v2.05. Never replay a saved fit camera.
        for view_index, (filename, label, eye, up) in enumerate(VIEW_PRESETS, 1):
            diagnostic_event('capture.view.begin', index=view_index, count=len(VIEW_PRESETS), label=label)
            report['current_stage'] = label
            diagnostic_event('stage', stage=report['current_stage'])
            expected = camera_axes(basis, eye, up)
            layout = orient_and_verify(nx, view, expected)
            prepare_capture_display()
            if fit_facet is not None:
                fit_facet.Unblank()
            report['current_stage'] = 'Вписывание с полями 5% · ' + label
            diagnostic_event('stage', stage=report['current_stage'])
            fit_visible_view(nx, view)
            fitted_camera = snapshot_view(view)
            if ipw_requested:
                fit_facet.Blank()
                if not fit_facet.IsBlanked:
                    raise RuntimeError('NX не скрыл вспомогательную геометрию перед съёмкой.')
                clear_ipw()
                report['ipw']['fit_count'] += 1
                report['ipw']['events'].append({'action': 'fit_geometry', 'view': filename,
                                              'margin': FRAME_MARGIN})
            capture({'file': filename, 'label': label,
                     'eye_direction_mcs': eye, 'up_hint_mcs': up,
                     'camera_axes_absolute': expected, 'nx_matrix_layout': layout}, fitted_camera)
            diagnostic_event('capture.view.end', index=view_index, count=len(VIEW_PRESETS), label=label)
        if ipw_requested:
            report["ipw"]["status"] = "pairs_created"
    except Exception as exc:
        report['capture_failed'] = True
        if ipw_requested:
            report["ipw"].update(status="error", message=str(exc))
        raise
    finally:
        errors = []
        geometry_error = None
        if ipw_requested and not ipw_cleared:
            try:
                clear_ipw()
            except Exception as exc:
                errors.append("снятие отображения IPW: " + str(exc))
        if fit_mark is not None:
            try:
                session = nx.Session.GetSession()
                session.UndoToMark(fit_mark, None)
                report['ipw']['fit_geometry_removed'] = True
                session.DeleteUndoMark(fit_mark, None)
            except Exception as exc:
                geometry_error = 'Не удалось завершить удаление временной геометрии IPW: ' + str(exc)
                errors.append(geometry_error)
        errors.extend(restore_view(nx, view, saved))
        errors.extend(display.restore())
        if 'projection' in saved:
            try:
                actual = read_view_projection(nx, view)
                projection_info['after_restore'] = actual
                expected = saved['projection']
                projection_info['restored'] = actual['type'] == expected['type'] and (
                    actual['type'] == 1 or math.isclose(actual['distance'], expected['distance'],
                                                      rel_tol=1e-8, abs_tol=1e-8))
                if not projection_info['restored']:
                    raise RuntimeError('исходная проекция не восстановлена')
            except Exception as exc:
                projection_info['restore_errors'].append(str(exc))
                errors.append('проверка исходной проекции: ' + str(exc))
        report["restore_errors"] = errors
        report["view_restored"] = not errors
        if geometry_error:
            report['capture_failed'] = True
            report['ipw'].update(status='error', message=geometry_error)
            raise RuntimeError(geometry_error)


def project_file_from_part(part):
    """Путь к текущему CAM-файлу, а не к журналу или рабочей папке NX."""
    raw_path = str(getattr(part, "FullPath", "") or "")
    path = Path(raw_path)
    if not raw_path or not path.is_absolute() or path.suffix.lower() != ".prt":
        raise RuntimeError("Сначала сохрани текущий CAM-проект в файл .prt "
                           "и повтори запуск журнала.")
    path = io_path(path)
    if not path.is_file():
        raise RuntimeError("Файл проекта не найден или недоступен:\n%s\n"
                           "Сохрани проект и проверь доступ к его папке." % display_file_name(path))
    return path


def project_name_from_part(part):
    # Удаляем только последнее .prt: точки в обозначении детали сохраняются.
    raw_name = getattr(part, "FullPath", "") or part.Leaf
    name = str(raw_name).replace("\\", "/").rsplit("/", 1)[-1]
    if name.lower().endswith(".prt"):
        name = name[:-4]
    name = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "_", name).rstrip(" .")
    if not name or name in (".", ".."):
        raise RuntimeError("Не удалось определить имя проекта. Сохрани CAM-файл с именем и повтори запуск.")
    if re.fullmatch(r"CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9]", name.split(".")[0], re.I):
        name = "_" + name
    return name


def warning_dialog_template(question, include_details=False):
    """A native modal dialog with our own red title band, independent of theme."""
    def text(value):
        return (value + '\0').encode('utf-16-le')
    # WS_POPUP | WS_SYSMENU | DS_MODALFRAME | DS_SETFONT | DS_CENTER.
    # No WS_CAPTION: Windows' accent colour cannot replace the red band.
    data = bytearray(struct.pack('<IIHhhhh', 0x800808C0, 0, 6 if include_details else 5, 0, 0, 360, 160))
    data += struct.pack('<HH', 0, 0) + text(TITLE + ' — Предупреждение')
    data += struct.pack('<H', 9) + text('Segoe UI')
    controls = [
        (0x82, 104, '  ' + TITLE + ' — Предупреждение', 0, 0, 360, 24, 0x280),
        (0x82, 101, question, 12, 40, 336, 72, 0x80),
    ]
    if include_details:
        # EDIT: border, vertical scroll, tab stop, multiline, auto vertical scroll,
        # read-only. Without ES_AUTOHSCROLL, full names wrap instead of clipping.
        controls.append((0x81, 105, '', 12, 90, 336, 30, 0xA10844))
    controls.extend((
        (0x80, 1, 'Да', 218, 128, 60, 20, 0x10000),
        (0x80, 2, 'Нет', 286, 128, 60, 20, 0x10001),
        (0x80, 3, '×', 336, 3, 20, 18, 0x18000),
    ))
    for class_id, control_id, label, x, y, width, height, style in controls:
        data += b'\0' * (-len(data) % 4)
        data += struct.pack('<IIhhhhH', 0x50000000 | style, 0, x, y, width, height, control_id)
        data += struct.pack('<HH', 0xFFFF, class_id) + text(label) + struct.pack('<H', 0)
    return bytes(data)


def show_warning_dialog(question, details=''):
    """Win32 only; body uses exactly 3x the dialog font height and FW_BOLD."""
    import ctypes
    from ctypes import wintypes as w

    class LOGFONTW(ctypes.Structure):
        _fields_ = [('lfHeight', w.LONG), ('lfWidth', w.LONG), ('lfEscapement', w.LONG),
                    ('lfOrientation', w.LONG), ('lfWeight', w.LONG),
                    ('lfItalic', w.BYTE), ('lfUnderline', w.BYTE), ('lfStrikeOut', w.BYTE),
                    ('lfCharSet', w.BYTE), ('lfOutPrecision', w.BYTE), ('lfClipPrecision', w.BYTE),
                    ('lfQuality', w.BYTE), ('lfPitchAndFamily', w.BYTE), ('lfFaceName', w.WCHAR * 32)]

    class MONITORINFO(ctypes.Structure):
        _fields_ = [('cbSize', w.DWORD), ('rcMonitor', w.RECT), ('rcWork', w.RECT), ('dwFlags', w.DWORD)]

    user = ctypes.WinDLL('user32', use_last_error=True)
    gdi = ctypes.WinDLL('gdi32', use_last_error=True)
    callback_type = ctypes.WINFUNCTYPE(ctypes.c_ssize_t, w.HWND, w.UINT, w.WPARAM, w.LPARAM)
    user_signatures = {
        'GetActiveWindow': ([], w.HWND), 'GetForegroundWindow': ([], w.HWND),
        'GetDlgItem': ([w.HWND, ctypes.c_int], w.HWND),
        'SetWindowTextW': ([w.HWND, w.LPCWSTR], w.BOOL),
        'SetFocus': ([w.HWND], w.HWND), 'EndDialog': ([w.HWND, ctypes.c_ssize_t], w.BOOL),
        'SendMessageW': ([w.HWND, w.UINT, w.WPARAM, w.LPARAM], ctypes.c_ssize_t),
        'GetDC': ([w.HWND], w.HDC), 'ReleaseDC': ([w.HWND, w.HDC], ctypes.c_int),
        'GetWindowRect': ([w.HWND, ctypes.POINTER(w.RECT)], w.BOOL),
        'GetClientRect': ([w.HWND, ctypes.POINTER(w.RECT)], w.BOOL),
        'MoveWindow': ([w.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, w.BOOL], w.BOOL),
        'MonitorFromWindow': ([w.HWND, w.DWORD], w.HANDLE),
        'GetMonitorInfoW': ([w.HANDLE, ctypes.POINTER(MONITORINFO)], w.BOOL),
        'SystemParametersInfoW': ([w.UINT, w.UINT, ctypes.c_void_p, w.UINT], w.BOOL),
        'DrawTextW': ([w.HDC, w.LPCWSTR, ctypes.c_int, ctypes.POINTER(w.RECT), w.UINT], ctypes.c_int),
        'ReleaseCapture': ([], w.BOOL),
        'DialogBoxIndirectParamW': ([w.HINSTANCE, ctypes.c_void_p, w.HWND, callback_type, w.LPARAM], ctypes.c_ssize_t),
    }
    gdi_signatures = {
        'GetObjectW': ([w.HANDLE, ctypes.c_int, ctypes.c_void_p], ctypes.c_int),
        'CreateFontIndirectW': ([ctypes.POINTER(LOGFONTW)], w.HANDLE),
        'CreateSolidBrush': ([w.DWORD], w.HBRUSH),
        'DeleteObject': ([w.HANDLE], w.BOOL), 'SelectObject': ([w.HDC, w.HANDLE], w.HANDLE),
        'SetTextColor': ([w.HDC, w.DWORD], w.DWORD),
        'SetBkColor': ([w.HDC, w.DWORD], w.DWORD), 'SetBkMode': ([w.HDC, ctypes.c_int], ctypes.c_int),
    }
    for dll, signatures in ((user, user_signatures), (gdi, gdi_signatures)):
        for name, (args, result) in signatures.items():
            function = getattr(dll, name)
            function.argtypes, function.restype = args, result
    state = {'error': None, 'header_height': 0}
    resources = []
    red, white = 0x000000C4, 0x00FFFFFF  # COLORREF = 0x00bbggrr.
    owner = user.GetActiveWindow() or user.GetForegroundWindow()

    def checked(value):
        if not value:
            raise ctypes.WinError(ctypes.get_last_error())
        return value

    def init_layout(window):
        label = user.GetDlgItem(window, 101)
        font_info = LOGFONTW()
        original_font = checked(user.SendMessageW(label, 0x0031, 0, 0))  # WM_GETFONT.
        checked(gdi.GetObjectW(original_font, ctypes.sizeof(font_info), ctypes.byref(font_info)))
        if font_info.lfHeight == 0:
            raise RuntimeError('Не удалось определить исходный размер текста предупреждения.')
        scale = abs(font_info.lfHeight) / 12.0  # 9 pt at 96 DPI; no process-wide DPI change.
        font_info.lfHeight *= 3
        font_info.lfWidth, font_info.lfWeight = 0, 700
        big_font = checked(gdi.CreateFontIndirectW(ctypes.byref(font_info)))
        resources.append(big_font)
        user.SendMessageW(label, 0x0030, big_font, 1)  # WM_SETFONT.
        if details:
            detail_control = checked(user.GetDlgItem(window, 105))
            user.SendMessageW(detail_control, 0x0030, big_font, 1)
            # WM_SETTEXT (used by SetWindowTextW) is not subject to the edit
            # control's default typing limit. Do not embed the list in DLGTEMPLATE.
            checked(user.SetWindowTextW(detail_control, details))

        monitor = MONITORINFO()
        monitor.cbSize = ctypes.sizeof(monitor)
        handle = user.MonitorFromWindow(owner or window, 2)
        if not handle or not user.GetMonitorInfoW(handle, ctypes.byref(monitor)):
            checked(user.SystemParametersInfoW(0x30, 0, ctypes.byref(monitor.rcWork), 0))
        area = monitor.rcWork
        gap = max(12, round(20 * scale))
        header = max(30, round(36 * scale))
        button_h, button_w = max(30, round(36 * scale)), max(80, round(110 * scale))
        outer, inner = w.RECT(), w.RECT()
        checked(user.GetWindowRect(window, ctypes.byref(outer)))
        checked(user.GetClientRect(window, ctypes.byref(inner)))
        frame_w = outer.right - outer.left - (inner.right - inner.left)
        frame_h = outer.bottom - outer.top - (inner.bottom - inner.top)
        width = min(round(980 * scale), area.right - area.left - 2 * gap - frame_w)
        if width <= 2 * gap:
            raise RuntimeError('Недостаточно места на экране для предупреждения.')
        dc = checked(user.GetDC(label))
        previous = gdi.SelectObject(dc, big_font)
        try:
            bounds = w.RECT(0, 0, width - 2 * gap, 0)
            checked(user.DrawTextW(dc, question, -1, ctypes.byref(bounds), 0xC10))
            text_h = bounds.bottom - bounds.top + max(4, round(4 * scale))
        finally:
            if previous:
                gdi.SelectObject(dc, previous)
            user.ReleaseDC(label, dc)
        height = header + 3 * gap + text_h + button_h
        max_height = area.bottom - area.top - 2 * gap - frame_h
        if details:
            # The list scrolls inside the window; more tools never push buttons
            # below the monitor's work area or shrink the requested 3x font.
            detail_h = min(round(380 * scale), max_height - height - gap)
            if detail_h < abs(font_info.lfHeight) + round(12 * scale):
                raise RuntimeError('Недостаточно места на экране для списка повторяющихся инструментов.')
            height += gap + detail_h
        if height > max_height:
            raise RuntimeError('Предупреждение с тройным размером текста не помещается на экран.')
        x = area.left + (area.right - area.left - width - frame_w) // 2
        y = area.top + (area.bottom - area.top - height - frame_h) // 2
        checked(user.MoveWindow(window, x, y, width + frame_w, height + frame_h, True))
        controls = {
            104: (0, 0, width, header),
            101: (gap, header + gap, width - 2 * gap, text_h),
            1: (width - 2 * gap - 2 * button_w, height - gap - button_h, button_w, button_h),
            2: (width - gap - button_w, height - gap - button_h, button_w, button_h),
            3: (width - header, 3, header - 6, header - 6),
        }
        if details:
            controls[105] = (gap, header + 2 * gap + text_h, width - 2 * gap, detail_h)
        for control_id, rect in controls.items():
            checked(user.MoveWindow(user.GetDlgItem(window, control_id), *rect, True))
        state['header_height'] = header
        user.SetFocus(user.GetDlgItem(window, 2))

    @callback_type
    def callback(window, message, wparam, lparam):
        try:
            if message == 0x0110:  # WM_INITDIALOG.
                init_layout(window)
                return 0
            if message == 0x0138:  # WM_CTLCOLORSTATIC.
                is_header = lparam == user.GetDlgItem(window, 104)
                gdi.SetTextColor(wparam, white if is_header else red)
                gdi.SetBkColor(wparam, red if is_header else white)
                gdi.SetBkMode(wparam, 2)  # OPAQUE.
                return brushes['red' if is_header else 'white']
            if message == 0x0136:  # WM_CTLCOLORDLG.
                return brushes['white']
            if message == 0x0201 and 0 <= ctypes.c_short((lparam >> 16) & 0xFFFF).value < state['header_height']:
                # The title STATIC has no SS_NOTIFY and passes pointer hits through.
                user.ReleaseCapture()
                user.SendMessageW(window, 0x00A1, 2, 0)  # WM_NCLBUTTONDOWN / HTCAPTION.
                return 1
            if message == 0x0010:  # WM_CLOSE / Alt+F4.
                user.EndDialog(window, 3)
                return 1
            if message == 0x0111 and (wparam & 0xFFFF) in (1, 2, 3):
                user.EndDialog(window, wparam & 0xFFFF)
                return 1
        except Exception as exc:
            state['error'] = exc
            user.EndDialog(window, 2)
            return 1
        return 0

    try:
        brushes = {}
        for name, color in (('white', white), ('red', red)):
            brush = checked(gdi.CreateSolidBrush(color))
            resources.append(brush)
            brushes[name] = brush
        template = ctypes.create_string_buffer(warning_dialog_template(question, bool(details)))
        result = user.DialogBoxIndirectParamW(None, ctypes.cast(template, ctypes.c_void_p), owner, callback, 0)
        if state['error'] is not None:
            raise state['error']
        if result not in (1, 2, 3):
            raise RuntimeError('Не удалось открыть предупреждение: ' + str(ctypes.WinError(ctypes.get_last_error())))
        return result == 1 if result in (1, 2) else None
    finally:
        for resource in reversed(resources):
            gdi.DeleteObject(resource)


def ask_warning(nx, question, details=''):
    uf_ui = nx.UF.UFSession.GetUFSession().Ui
    lock_source = nx.UF.UFConstants.UF_UI_FROM_CUSTOM
    uf_ui.LockUgAccess(lock_source)
    try:
        return show_warning_dialog(question, details) if details else show_warning_dialog(question)
    finally:
        uf_ui.UnlockUgAccess(lock_source)


def make_output_folder(project_folder, project_name, setup_name=None):
    """Reuse one short staging folder beside the PRT, on the target volume."""
    project_folder = io_path(project_folder)
    project = project_folder / 'Карты Наладки' / safe_file_component(project_name, True)
    project.mkdir(parents=True, exist_ok=True)
    # A junction may put the result on another volume. Keep atomic replacement
    # there rather than falling back to copying over the previous card.
    parent = project_folder if project_folder.stat().st_dev == project.stat().st_dev else project
    return io_path(tempfile.mkdtemp(prefix='.nx_', dir=str(parent)))


def prepare_capture_folders(staging, jobs, project_view=False):
    """Check every NX image path before costly meshing or changes to NX views."""
    if project_view:
        nx_image_file_name(io_path(staging) / 'project_view.png')
    for index, job in enumerate(jobs, 1):
        output = io_path(staging) / str(index)
        output.mkdir()
        job['output'] = output
        for filename, _label, _eye, _up in VIEW_PRESETS:
            nx_image_file_name(output / filename)
            nx_image_file_name(output / (Path(filename).stem + '_no_ipw.png'))


def axes_svg(axes, basis):
    # Легенда показывает направления MCS в плоскости конкретного изображения.
    # Это векторная схема; она не меняет исходный PNG NX.
    shapes = []
    for vec, label, color in zip(basis, ("X", "Y", "Z"), ("#cf3a30", "#187c48", "#225ec2")):
        dx, dy = 34*dot(vec, axes[0]), -34*dot(vec, axes[1])
        x, y = 52+dx, 46+dy
        if math.hypot(dx, dy) < 1:
            shapes.append('<circle cx="52" cy="46" r="5" fill="white" stroke="%s"/>' % color)
            shapes.append('<text x="58" y="58" fill="%s">%s</text>' % (color, label))
        else:
            shapes.append('<line x1="52" y1="46" x2="%.2f" y2="%.2f" stroke="%s" stroke-width="2"/>' % (x, y, color))
            shapes.append('<circle cx="%.2f" cy="%.2f" r="2.5" fill="%s"/>' % (x, y, color))
            shapes.append('<text x="%.2f" y="%.2f" fill="%s">%s</text>' % (x+3, y-4, color, label))
    return '<svg viewBox="0 0 108 92" width="108" height="92" aria-label="Оси MCS">' + ''.join(shapes) + '</svg>'


def uf_struct_field(obj, name):
    """Resolve an existing UF field across Python/C# casing, never invent one.

    Python's generated *_Struct wrappers can expose PascalCase properties
    where the UF/.NET reference lists snake_case. Compare their names before
    assigning so a permissive Python object cannot hide a binding mismatch.
    """
    tokens = name.strip('_').split('_')
    pascal = ''.join(token[:1].upper() + token[1:] for token in tokens)
    candidates = (name, pascal, pascal[:1].lower() + pascal[1:])
    if isinstance(obj, dict):
        names = obj.keys()
        candidates = [candidate for candidate in candidates if candidate in obj]
    else:
        names = dir(obj)
        candidates = [candidate for candidate in candidates if hasattr(obj, candidate)]
    if candidates:
        return candidates[0]
    key = re.sub(r'[^a-z0-9]', '', name.casefold())
    matches = [field for field in names
               if re.sub(r'[^a-z0-9]', '', str(field).casefold()) == key]
    return matches[0] if len(matches) == 1 else None



def uf_struct_value(obj, name):
    field = uf_struct_field(obj, name)
    if field is None:
        return None
    return obj[field] if isinstance(obj, dict) else getattr(obj, field)



SETUP_TEMPLATE = r"""
<div class="toolbar"><span class="brand">Установ __SETUP_LABEL__</span><label class="axes-control" for="mcs-size">Стрелки СКС на детали <input id="mcs-size" type="range" min="50" max="180" step="5" value="100"><output id="mcs-size-value" for="mcs-size">100%</output></label><label class="axes-control" for="mcs-outline-color">Контур <select id="mcs-outline-color"><option value="white" selected>Белый</option><option value="black">Чёрный</option></select></label><label class="axes-control" for="mcs-outline-width">Толщина контура <input id="mcs-outline-width" type="range" min="50" max="250" step="10" value="100"><output id="mcs-outline-width-value" for="mcs-outline-width">100%</output></label><div class="actions"><button id="gallery-reset" type="button" title="Выровнять размеры ячеек и вернуть доступную высоту блока, сохранив содержимое">Выровнять ячейки</button><button id="columns-auto" type="button" disabled title="Вернуть всем таблицам ширину столбцов по содержимому">Автоширина</button></div>
<div class="layout-toolbar"><button id="cell-add" type="button" aria-haspopup="dialog" aria-controls="cell-grid-picker" aria-expanded="false" title="Выберите место, число столбцов и строк">+ Ячейки изображений ▾</button><button id="cell-undo" type="button" disabled title="Отменить последнее добавление, удаление или перемещение ячеек">Отменить действие</button><output id="cell-selection-status" class="cell-selection-status" aria-live="polite"></output><button id="cell-clear-selection" type="button" hidden>Снять выделение</button><button id="operations-to-cover" type="button" disabled>На первый лист ↑</button><button id="operations-to-next" type="button" disabled>На следующие листы ↓</button><output id="operations-status" aria-live="polite"></output></div></div>
<div hidden id="editor-service"><output id="gallery-size-status"></output><output id="column-size-status"></output></div>

<div id="pages" class="setup-pages">__BODY__</div>
<template id="operation-source">__OPERATION_SOURCE__</template>
<template id="continuation-template">__CONTINUATION_TEMPLATE__</template>

<script id="column-widths" type="application/json">{}</script>
"""

CARD_TEMPLATE = r"""<!doctype html>
<html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'; base-uri 'none'; form-action 'none'">
<meta name="nx-operation-options" content="__OPERATION_OPTIONS__">
<meta name="nx-card-id" content="__CARD_ID__">
<meta name="nx-card-revision" content="__CARD_REVISION__">
<meta name="nx-card-format" content="setup-sections-v1">
<meta name="nx-project-tools-position" content="__PROJECT_TOOLS_POSITION__">
<meta name="nx-project-image-scale" content="__PROJECT_IMAGE_SCALE__">
<title>__DOCUMENT_TITLE__</title>
<script id="nx-project-model" type="application/json">__PROJECT_MODEL__</script>
<style>
*{box-sizing:border-box}template,[hidden]{display:none!important}body{margin:0;background:#e9eef2;color:#171d21;font-family:Arial,sans-serif}button{font:inherit;cursor:pointer}button:focus-visible,.photo:focus-visible{outline:2px solid #078778;outline-offset:3px}.toolbar{position:sticky;top:0;z-index:10;background:#fff;border-bottom:1px solid #ced8dd;padding:12px 24px;display:flex;align-items:center;gap:12px;flex-wrap:wrap}.brand{font-weight:700;font-size:17px}.summary{color:#526872;font-size:12px}.actions{display:flex;gap:8px;margin-left:auto}.actions button{border:1px solid #a9c2c8;background:#fff;border-radius:6px;padding:9px 16px;font-size:14px}.actions .primary{background:#087d72;border-color:#087d72;color:#fff}.page{background:white;width:210mm;height:297mm;padding:8mm;margin:18px auto;box-shadow:0 2px 12px #223b4320}.page-content{position:relative;height:281mm;padding-bottom:4.5mm;display:flex;flex-direction:column}.card-header{height:49mm;flex:none;display:grid;grid-template-columns:57.52% 40.48%;gap:2%;margin-bottom:2mm}.card-header>div{min-width:0}.header-right{display:flex;flex-direction:column}.header-right>.locked-spacer{flex:1}.metadata,.coordinates,.legends,.tools,.ops{width:100%;border-collapse:collapse;table-layout:fixed}.metadata{height:49mm;font-size:10.5pt}.metadata th,.metadata td,.coordinates th,.coordinates td,.legends th,.legends td{border:.25mm solid #363e43;padding:0 .8mm;font-weight:400}.metadata th{background:#d8d8d8;text-align:center;white-space:normal}.metadata th.white{background:#fff}.metadata th.white .field{font-size:9.3pt}.metadata .note-row{height:14.2mm}.field{min-width:0;line-height:1.1;white-space:pre-wrap;overflow-wrap:anywhere;outline:0;cursor:text}.metadata td>.field,.metadata th>.field{height:6.45mm;padding:.6mm 0}.metadata .note-row .field{height:13.1mm;display:flex;justify-content:center;align-items:center;white-space:pre-wrap}.field:empty:before{content:attr(data-placeholder);color:#929ea3;font-size:8pt;font-weight:400}.field:hover{background:#f0fbf8}.field:focus{outline:.5mm solid #078778;outline-offset:-.5mm;background:#f3fcfa}.field.overflowing{outline:.4mm solid #cc5b12}.bold{font-weight:700}.center{text-align:center}.red{color:#e50000}.setup-values{display:grid;grid-template-columns:max-content max-content max-content 1fr;column-gap:1.5mm;height:6.45mm;align-items:center}.setup-values>.field{height:6.45mm;padding:.6mm 0}.setup-values>.field:empty:before{content:''}.setup-values>span{text-align:center}.setup-values,.setup-values.named-setup{display:flex;gap:1.5mm;justify-content:flex-start}.setup-values [data-field="setup"]{flex:0 1 auto;min-width:1ch;max-width:calc(100% - 14mm);text-align:left;white-space:nowrap}.setup-values [data-field="setup_total"]{flex:0 0 auto;min-width:1.2ch;text-align:left}.setup-values>.locked-spacer{display:none}.coordinates{height:20.85mm;font-size:14pt}.coordinates th{background:#d3e3d1;text-align:center}.coordinates .field{height:6.2mm;padding:.5mm 0}.legends{height:20.85mm}.legends th{background:#bdc5cb;font-size:14pt;text-align:center}.legends td{font-size:7.1pt}.legends .field{height:6.2mm;padding:.7mm 0;display:flex;align-items:center;justify-content:center}.locked-spacer{cursor:default}.gallery{position:relative;height:225.5mm;flex:none}.gallery-grid{height:225.5mm;display:grid;grid-template-columns:58.75% 41.25%;grid-template-rows:47% 53%;border:0}.gallery-frame{position:absolute;top:0;left:0;width:194mm;height:225.5mm;z-index:2;pointer-events:none;fill:none;stroke:#363e43;stroke-width:.25mm}.photo{position:relative;min-width:0;min-height:0;margin:0;background:white;display:flex;flex-direction:column;overflow:hidden;cursor:default}.photo[role="button"]{cursor:pointer}.photo-stage{position:absolute;left:2mm;right:2mm;top:18mm;bottom:6mm}.photo img{position:absolute;inset:0;display:block;width:100%;height:100%;object-fit:contain;object-position:50% 50%;padding:0}.mcs-onpart{position:absolute;transform:translate(-50%,-50%);width:36mm;height:36mm;z-index:1;pointer-events:none;overflow:visible}.axes-control{display:flex;align-items:center;gap:8px;font-size:13px;color:#344f58;white-space:nowrap}.axes-control input{width:130px;accent-color:#087d72}.axes-control output{min-width:42px;font-variant-numeric:tabular-nums}.axes-control input:focus-visible{outline:2px solid #078778;outline-offset:3px}.photo figcaption{position:absolute;bottom:0;left:0;right:0;order:2;min-height:4mm;flex:none;font-size:6.3pt;color:#515a60;text-align:center;padding:.4mm;line-height:1.2}.mcs-axes{position:absolute;left:1mm;top:5mm;width:24mm;height:24mm;z-index:1;pointer-events:none;overflow:visible}.mcs-name{position:absolute;left:2mm;top:1mm;max-width:calc(100% - 4mm);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:7pt;font-weight:700;color:#42494e;z-index:1;background:#fffffff2;padding:.3mm .5mm;pointer-events:none}.iso-photo{grid-row:1;grid-column:1}.top-photo{grid-column:1;grid-row:2}.side-photo{grid-column:2;grid-row:2}.right-panel{grid-column:2;grid-row:1;min-width:0;min-height:0;display:flex;flex-direction:column}.tool-panel{min-height:0;padding:1.5mm;flex:1;display:flex;justify-content:flex-start;flex-direction:column}.tools{font-size:8pt}.tools td,.tools th{border:.18mm solid #a8aeb2;padding:.55mm .8mm;line-height:1.2;overflow-wrap:anywhere}.tools th{background:#f0f2f3;text-align:left;font-size:7pt;font-weight:400}.tools td:last-child,.tools th:last-child{text-align:center}.empty-tools{font-size:7pt;margin:1.5mm 0;text-align:center}.empty-image{margin:auto;font-size:8pt;color:#7b858b;text-align:center}.rows-area{flex:1;min-height:0;border:.25mm solid #363e43}.ops{border:0;font-size:7pt;line-height:1.2}.ops th,.ops td{border-right:.16mm solid #acb3b7;border-bottom:.16mm solid #b9c0c3;vertical-align:middle;overflow-wrap:anywhere;padding:.65mm .6mm}.ops td:first-child,.ops th:first-child{border-left:0}.ops td:last-child,.ops th:last-child{border-right:0}.ops th{height:6mm;font-size:6pt;font-weight:400;background:#e7edf1;text-align:left;line-height:1.1}.ops th:nth-child(3),.ops th:nth-child(4){text-align:center}.ops .numeric{font-size:6.2pt;white-space:nowrap;text-align:center;padding-left:.35mm;padding-right:.35mm}.ops .name-cell{white-space:normal}.tree-name{display:block;position:relative;overflow-wrap:anywhere}.folder-icon{display:inline-block;vertical-align:baseline;width:2.3mm;height:1.6mm;background:#e4d7a7;border:.15mm solid #9c894d;margin-right:1mm}.group-row{background:#f0f3f5;font-weight:700}.group-row td{padding-top:.65mm;padding-bottom:.65mm}.suppressed-row{color:#79858a;background:#f7f7f7}.tool-change{display:inline-block;width:2mm;height:3mm;color:#454c51}.ops svg{display:block;width:2mm;height:3mm;margin:auto}.path{font-size:10pt;font-weight:700;line-height:1;display:inline-block}.path.ready{color:#27843b}.path.stale{color:#b36213}.path.suppressed,.path.missing{color:#727b81}.page-footer{position:absolute;bottom:0;left:0;width:100%;height:4.5mm;flex:none;text-align:right;font-size:11pt;line-height:4.5mm;white-space:nowrap;border:0}.page-footer span{display:inline-block;min-width:7mm;text-align:center}.continuation-header{flex:none;height:28mm;margin-bottom:3mm}.continuation-header .metadata{height:28mm}.continuation-header th{width:15.5%}.tools-title{font-size:12pt;font-weight:400;margin:3mm 0}.tools-page .tools{font-size:10pt}.tools-page .page-footer{margin-top:auto}.tools-page .tools td{padding:1.5mm}.empty-operations{font-size:8pt;margin:2mm 0}
.time-footnote{position:absolute;left:0;top:0;font-size:6.5pt;color:#53626a;font-weight:400}
.catalog-title{font-size:14pt;margin:3mm 0 1.5mm;font-weight:700}.catalog-subtitle{font-size:9pt;margin:0 0 4mm;color:#52626a}.catalog-table{border-collapse:collapse;table-layout:fixed;width:100%;font-size:9.5pt}.catalog-table td,.catalog-table th{border:.2mm solid #687279;padding:1.2mm 1.4mm;overflow-wrap:anywhere;line-height:1.25;text-align:center}.catalog-table th{background:#e7edf1;font-size:8.5pt}.catalog-table td:nth-child(2),.catalog-table th:nth-child(2){text-align:left}.catalog-table tr{break-inside:avoid;page-break-inside:avoid}.catalog-table thead{display:table-header-group}@media(max-width:840px){.toolbar{padding:10px}.page{margin:12px 8px}.summary{display:none}.actions button{padding:8px}.brand{font-size:14px}}

@page{size:A4 portrait;margin:0}
@media print{html,body{margin:0;background:#fff}.toolbar{display:none!important}.page{margin:0;box-shadow:none;break-after:page;page-break-after:always}.page:last-child{break-after:auto;page-break-after:auto}.field{outline:none!important;background:transparent!important}.field:empty:before{content:none}.field.overflowing{outline:.4mm solid #d00!important}.photo figcaption{color:#353a3e}.ops thead{display:table-header-group}.ops tr,.tools tr{break-inside:avoid;page-break-inside:avoid}*{-webkit-print-color-adjust:exact;print-color-adjust:exact}}
.tool-dimension{font-weight:700;color:#d00000;-webkit-print-color-adjust:exact;print-color-adjust:exact}

/* One-line data tables; the header fields keep their existing multiline layout. */
table[data-fit-family]{table-layout:fixed;width:100%;line-height:1.15}
table[data-fit-family] tr,table[data-fit-family] th,table[data-fit-family] td{height:5.4mm}
table[data-fit-family] th,table[data-fit-family] td{white-space:nowrap;overflow-wrap:normal;word-break:normal;padding:.4mm .6mm;vertical-align:middle}
table[data-fit-family] td,table[data-fit-family] .numeric{font-size:inherit}
table[data-fit-family] th{font-size:.85714286em;line-height:1.15}
table[data-fit-family] .tree-name,table[data-fit-family] .name-cell{white-space:nowrap;overflow-wrap:normal;word-break:normal}

/* A narrow column truncates only its presentation, including during printing. */
.rows-area{min-width:0;width:100%}
table[data-fit-family] th,table[data-fit-family] td{max-width:0}
table[data-fit-family] td{overflow:hidden}
table[data-fit-family] .cell-text{display:block;min-width:0;max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;overflow-wrap:normal;word-break:normal}
table[data-fit-family] .tree-cell{display:flex;min-width:0;max-width:100%}
table[data-fit-family] .tree-indent{display:block;flex:0 0 auto;min-width:0}
table[data-fit-family] .tree-cell>.tree-name{position:static;flex:1 1 0%;min-width:0}
table.column-probe .cell-text{overflow:visible;max-width:none;text-overflow:clip}
table.column-probe th,table.column-probe td{max-width:none}
table.column-probe .tree-cell{display:block}
table.column-probe .tree-indent{display:inline-block}
table.column-probe .tree-name{display:inline}

/* The complete cover list scales uniformly with its panel. */
.tool-panel{overflow:hidden;position:relative}
.tool-panel>table{flex:none}
table[data-fit-family="cover-tools"] tr,table[data-fit-family="cover-tools"] th,table[data-fit-family="cover-tools"] td{height:var(--cover-row,4.2mm)}
table[data-fit-family="cover-tools"] th,table[data-fit-family="cover-tools"] td{line-height:1.15;padding:var(--cover-pady,.35mm) var(--cover-padx,.6mm);border-width:var(--cover-border,.18mm)}

.tools-page table[data-fit-family] td{padding:.4mm .6mm}
table[data-fit-family] th{position:relative}
.column-grip{position:absolute;right:-4px;top:0;bottom:0;width:8px;z-index:3;cursor:col-resize;touch-action:none;user-select:none}
.column-grip:after{content:'';position:absolute;left:3px;top:0;bottom:0;width:2px;background:transparent}
.column-grip:hover:after,.column-grip:focus:after,.column-grip.dragging:after{background:#078778}
.column-grip:focus-visible{outline:1px solid #078778;outline-offset:1px}
.setup-document.columns-dragging,.setup-document.columns-dragging *{cursor:col-resize!important;user-select:none!important}
.actions button:disabled{opacity:.45;cursor:default}
[data-editor-id="column-size-status"]{color:#08766b;font-variant-numeric:tabular-nums}
@media print{.column-grip{display:none!important}}
.photo img{transform-origin:50% 50%}
.photo-toggle{position:absolute;inset:0;z-index:3;border:0;padding:0;background:transparent;cursor:pointer}
.photo-toggle:focus-visible{outline:2px solid #078778;outline-offset:-3px}
.photo figcaption{z-index:2;background:#fffffff2;pointer-events:none}
/* Compact reset buttons; scale itself is controlled by the wheel. */
.photo-view-tools{position:absolute;right:1mm;bottom:6mm;z-index:4;display:flex;align-items:center;gap:2px;padding:2px;background:#ffffffe6;border:1px solid #d6e3e1;border-radius:3mm;cursor:default}
.photo-view-tools button{width:auto;margin:0;min-width:7mm;border:0;border-radius:2mm;padding:3px 5px;font-size:9px;line-height:1.3;background:transparent;color:#08766b;font-variant-numeric:tabular-nums;white-space:nowrap}
.photo-view-tools button:hover{background:#e4f5f1}
@media print{.photo-view-tools,.photo-toggle{display:none!important}}
.axes-control select{font:inherit;border:1px solid #a9c2c8;border-radius:5px;padding:5px;background:white;color:#243f47}
.axes-control input[type=range]{width:100px}.axes-control select:focus-visible{outline:2px solid #078778;outline-offset:2px}

.gallery{isolation:isolate}
/* Keep cell content below the shared frame; resize grips remain above it. */
.gallery-grid{position:relative;z-index:0}
.gallery-grip{position:absolute;z-index:8;display:block;box-sizing:border-box;background:transparent;border:0;padding:0;margin:0;touch-action:none;user-select:none;outline:none}
.gallery-grip[data-gallery-axis=column]{top:0;height:100%;width:8px;transform:translateX(-50%);cursor:col-resize}
.gallery-grip[data-gallery-axis=row]{height:8px;transform:translateY(-50%);cursor:row-resize}
.gallery-grip:hover,.gallery-grip:focus-visible,.gallery-grip.dragging{background:rgba(8,125,114,.25);outline:1px solid #087d72;outline-offset:-3px}
.gallery-grip:after{content:"";position:absolute;border-radius:3px;background:#087d72;opacity:0;pointer-events:none}
.gallery-grip:hover:after,.gallery-grip:focus-visible:after,.gallery-grip.dragging:after{opacity:1}
.gallery-grip[data-gallery-axis=column]:after{width:3px;height:24px;top:50%;left:2.5px;transform:translateY(-50%)}
.gallery-grip[data-gallery-axis=row]:after:after{height:3px;width:24px;left:50%;top:2.5px;transform:translateX(-50%)}
.setup-document.gallery-sizing{user-select:none}
.setup-document.gallery-sizing-column,.setup-document.gallery-sizing-column *{cursor:col-resize!important}
.setup-document.gallery-sizing-row,.setup-document.gallery-sizing-row *{cursor:row-resize!important}
[data-editor-id="gallery-size-status"]{color:#08766b;font-variant-numeric:tabular-nums}
@media print{.gallery-grip,[data-editor-id="gallery-size-status"]{display:none!important}}

.photo[data-image],.photo[data-image] .photo-toggle{cursor:grab;touch-action:none;user-select:none}
.photo[data-image] img{-webkit-user-drag:none;user-select:none}
.setup-document.photo-panning,.setup-document.photo-panning *{cursor:grabbing!important;user-select:none!important}

/* Shared proportions, based on a compact NX-style table. */
:root{--data-scale:1}
table[data-fit-family]{font-size:calc(7pt * var(--data-scale));line-height:1.1}
table[data-fit-family] tr,table[data-fit-family] th,table[data-fit-family] td{height:calc(3.8mm * var(--data-scale))}
table[data-fit-family] th,table[data-fit-family] td,.tools-page table[data-fit-family] td{padding:calc(.2mm * var(--data-scale)) calc(.6mm * var(--data-scale));border-width:calc(.16mm * var(--data-scale));line-height:1.1;font-size:inherit}
table[data-fit-family] .path{font-size:1.2em;line-height:1}
table[data-fit-family] .tool-change,table[data-fit-family] svg{height:1.2em;width:.85em}
table[data-fit-family] .folder-icon{width:.9em;height:.65em;margin-right:.4em;border-width:.06em}
table[data-fit-family] .cell-text{height:calc(3.4mm * var(--data-scale) - 1px);line-height:calc(3.4mm * var(--data-scale) - 1px)}
table[data-fit-family] .path,table[data-fit-family] .tool-change{vertical-align:middle}
table[data-fit-family] .cell-text>svg{display:inline-block;vertical-align:middle}
table[data-fit-family="cover-tools"] tr,table[data-fit-family="cover-tools"] th,table[data-fit-family="cover-tools"] td{height:var(--cover-row,3.8mm)}
table[data-fit-family="cover-tools"] th,table[data-fit-family="cover-tools"] td{padding:var(--cover-pady,.2mm) var(--cover-padx,.6mm);border-width:var(--cover-border,.16mm);line-height:1.1;font-size:inherit}
table[data-fit-family="cover-tools"] .cell-text{height:calc(var(--cover-row,3.8mm) - 2 * var(--cover-pady,.2mm) - 1px);line-height:calc(var(--cover-row,3.8mm) - 2 * var(--cover-pady,.2mm) - 1px)}
.layout-toolbar{width:100%;display:flex;gap:8px;align-items:center;flex-wrap:wrap;border-top:1px solid #e1e8eb;padding-top:8px}
.layout-toolbar button{border:1px solid #a9c2c8;border-radius:5px;background:#fff;color:#204e55;padding:6px 10px;font-size:12px}
.layout-toolbar button:disabled{opacity:.45;cursor:default}
.layout-toolbar .drop-active{outline:3px solid #078778;background:#dbf7ec}
.layout-toolbar .axes-control{margin-left:auto}
[data-editor-id="operations-status"]{font-size:12px;color:#53656e;flex-basis:100%}
.cover-operations{flex:none;min-height:0;margin-top:2mm;overflow:hidden}
.cover-operations[hidden]{display:none!important}
.operation-grip{float:left;padding:0;margin:0 .4mm 0 0;width:2.8mm;height:1.1em;line-height:1;border:0;background:transparent;color:#5b797f;font-size:inherit;cursor:grab;touch-action:none}
.ops td:first-child{position:relative}
.operation-selected td{background:#dcf2ff}
.operation-drag-ghost{position:fixed;z-index:100;pointer-events:none;padding:8px 12px;color:white;background:#174d48;border-radius:5px;font-size:12px;max-width:260px;box-shadow:0 3px 10px #0003}
.setup-document.operations-dragging,.setup-document.operations-dragging *{user-select:none!important;cursor:grabbing!important}
.setup-document{overflow-anchor:none}
.cell-remove{position:absolute;z-index:7;right:1mm;top:1mm;border:1px solid #cdd7da;background:#fff;color:#526970;border-radius:50%;font:16px/18px Arial;width:20px;height:20px;padding:0;cursor:pointer;opacity:.25}
.photo:hover>.cell-remove,.cell-remove:focus-visible{opacity:1;color:#a82525}
.cell-remove:focus-visible{outline:2px solid #078778}
.user-photo .photo-stage{top:3mm;bottom:6mm}
.gallery .photo-stage{top:min(18mm,16%);bottom:min(6mm,10%)}
.gallery .user-photo .photo-stage{top:3mm;bottom:6mm}
.gallery .mcs-axes{width:min(24mm,25%);height:min(24mm,38%)}
.user-photo .paste-hint{position:absolute;inset:8mm;display:flex;align-items:center;justify-content:center;text-align:center;font-size:10px;color:#60807e;pointer-events:none;line-height:1.4}
.user-photo.cell-selected{outline:2px solid #078778;outline-offset:-3px}
.gallery-frame{overflow:visible}
@media print{
 .cell-remove,.operation-grip,.operation-drag-ghost,.paste-hint{display:none!important}
 .user-photo.cell-selected{outline:none}
 .operation-selected td{background:transparent}
 .operation-selected.group-row td{background:#f0f3f5}
 .gallery,.cover-operations,.tool-panel{break-inside:avoid;page-break-inside:avoid}
}

/* Moving a whole cell has its own handle, leaving image pan intact. */
.gallery{z-index:1}
.cell-move{position:absolute;z-index:7;right:7.5mm;top:1mm;width:22px;height:22px;padding:0;border:1px solid #a9c2c8;border-radius:4px;background:#fffffff0;color:#396167;font:20px/20px Arial;cursor:grab;touch-action:none;opacity:.65}
.photo:hover>.cell-move,.cell-move:focus-visible{opacity:1}
.cell-move:focus-visible{outline:2px solid #078778;outline-offset:1px}
.cell-swap-source:after,.cell-swap-target:after{content:'';position:absolute;inset:2px;z-index:6;pointer-events:none;border:2px dashed #078778;border-radius:2px}
.cell-swap-target:after{border-style:solid;background:#07877812;box-shadow:inset 0 0 0 2px white}
.setup-document.cells-swapping,.setup-document.cells-swapping *{user-select:none!important;cursor:grabbing!important}
.gallery-height-grip{position:absolute;display:block;left:0;top:100%;width:100%;height:10px;transform:translateY(-50%);z-index:9;cursor:row-resize;touch-action:none;outline:none}
.gallery-height-grip:after{content:'';position:absolute;left:50%;top:3px;transform:translateX(-50%);width:40px;height:4px;border:1px solid #087d72;border-radius:3px;background:white;opacity:.65}
.gallery-height-grip:hover,.gallery-height-grip:focus-visible,.gallery-height-grip.dragging{background:#087d7240;outline:1px solid #087d72;outline-offset:-3px}
.gallery-height-grip:hover:after,.gallery-height-grip:focus-visible:after,.gallery-height-grip.dragging:after{background:#087d72;opacity:1}
.operation-drop-active{outline:3px solid #078778;outline-offset:-4px}
.operation-drop-hint{margin:20mm 8mm;padding:10mm 5mm;border:2px dashed #078778;border-radius:5px;color:#08766b;text-align:center;font-size:13px;line-height:1.5}
.ops tbody tr{touch-action:none}
@media print{
 .cell-move,.gallery-height-grip,.operation-drop-page{display:none!important}
 .cell-swap-source:after,.cell-swap-target:after{display:none!important}
 .operation-drop-active{outline:none!important}
}

/* Paper contains manufacturing data; editor guidance lives in control tooltips. */
[data-editor-id="operations-status"]:empty{display:none}

.gallery .photo-stage,.gallery .user-photo .photo-stage{top:8mm;bottom:8mm}
.project-tools-page .continuation-header.no-note{height:var(--project-header-height,39mm);display:grid;grid-template-columns:minmax(0,1fr) var(--project-image-width,44mm);grid-template-rows:var(--project-meta-height,14mm) var(--project-legend-height,20.85mm);gap:2mm 4mm;align-items:start}
.project-tools-page .legends{grid-column:1;grid-row:2;height:var(--project-legend-height,20.85mm)}.project-tools-page .legends col:first-child{width:12mm!important}.project-tools-page .project-isometry{grid-column:2;grid-row:1 / span 2}
.project-tools-page .continuation-header.no-note .metadata{height:var(--project-meta-height,14mm)}
.project-tools-page .metadata col:first-child{width:var(--project-label-width,30mm)!important}
.project-tools-page .metadata th{overflow-wrap:anywhere}
.project-tools-page .metadata td>.field{height:calc(var(--project-meta-height,14mm) / 2 - .55mm);display:flex;align-items:center}
.project-tools-page .legends .field{height:calc(var(--project-legend-height,20.85mm) / 3 - .75mm)}
.project-tools-page .legends td{font-size:11pt}
.project-tools-page .legend-text{cursor:default}.project-tools-page .legend-text:hover{background:transparent}
.project-isometry{width:var(--project-image-width,44mm);height:var(--project-image-height,32mm);display:flex;align-items:center;justify-content:center;overflow:hidden}
.project-isometry img{display:block;width:100%;height:100%;object-fit:contain}
.project-isometry .empty-image{font-size:7pt}
.project-model{position:relative;background:#fff;touch-action:none;user-select:none;-webkit-user-select:none}
.project-model canvas{width:100%;height:100%;display:block;cursor:grab;touch-action:none;outline-offset:-2px}
.project-model canvas.model-dragging{cursor:grabbing}.project-model canvas:focus-visible{outline:1px solid #078778}
.project-model.model-ready>.project-model-print{display:none}.project-model-actions{display:flex;gap:6px}
.project-model-actions button,.page-controls>[data-page-reset]{font-size:11px;padding:3px 7px;border:1px solid #bacbd1;border-radius:4px;background:#fff;color:#23505a}
@media print{.project-model canvas{display:none!important}.project-model.model-ready>.project-model-print{display:block!important}}
.catalog-heading{display:flex;align-items:center;justify-content:space-between;gap:3mm}
.catalog-heading .catalog-title{min-width:0}
.project-tool-plane{position:relative;overflow:hidden}
.rows-area,.cover-operations,.catalog-scroll,.tool-table-scroll{overflow:hidden}
.catalog-scroll{flex:none;min-width:0;width:100%}
.tool-table-plane{position:relative;overflow:hidden}
@media print{
 
 .rows-area,.cover-operations,.catalog-scroll,.tool-table-scroll{overflow:visible!important;scrollbar-width:none}
 table[data-fit-family]:not(.column-probe){width:var(--print-source-width,100%)!important}
 .ops[data-fit-family]:not(.column-probe){width:var(--print-source-width,100%)!important;zoom:var(--print-table-scale,1)}
 .tool-table-plane{width:100%!important}
}

/* Selection and insertion controls stay outside the printed card. */
.gallery .photo.cell-selected{outline:2px solid #078778;outline-offset:-3px}
.cell-selection-status{color:#08766b;font-size:12px;white-space:nowrap}
.cell-drop-preview{position:absolute;z-index:8;pointer-events:none;display:flex;align-items:center;justify-content:center;text-align:center;padding:6px;border:3px solid #087d72;background:#d5f3eade;color:#154e45;font:600 12px/1.3 Arial;box-shadow:inset 0 0 0 2px #fff}
.cell-grid-picker{position:fixed;z-index:40;width:292px;max-width:calc(100vw - 16px);max-height:calc(100vh - 16px);overflow:auto;padding:12px;border:1px solid #a9c2c8;border-radius:7px;background:#fff;box-shadow:0 5px 25px #233e4535;color:#203b43;font:13px/1.4 Arial}
.cell-picker-heading{display:flex;justify-content:space-between;align-items:center;margin-bottom:10px}
.cell-picker-close{border:0;background:transparent;color:#49626a;font:22px/1 Arial;padding:0 4px}
.cell-grid-picker label{display:flex;align-items:center;gap:8px;justify-content:space-between;margin:8px 0}
.cell-grid-picker select{font:12px Arial;max-width:178px;min-width:130px;padding:5px 2px;border:1px solid #a9c2c8;border-radius:4px;background:white;color:#203b43}
.cell-grid-picker output{display:block;margin:12px 0 7px;font-weight:700;color:#08766b}
.cell-picker-grid{width:max-content;margin:0 auto;touch-action:manipulation}
.cell-picker-grid>[role=row]{display:flex;gap:4px;margin-bottom:4px}
.cell-picker-grid button{display:block;width:30px;height:25px;padding:0;border:1px solid #93abb3;background:#f6f8f9;border-radius:2px}
.cell-picker-grid button.active{background:#d6f3e9;border:2px solid #078778}
.cell-picker-grid button:focus-visible{outline:2px solid #234d61;outline-offset:1px}
@media print{
 .cell-grid-picker,.cell-drop-preview{display:none!important}
 .gallery .photo.cell-selected{outline:none!important}
}

/* Carrier rows share the tool list's row/font scale and column resizing. */
.tool-group-row{background:#edf1f3;color:#253d48;font-weight:700}
.tool-tree-cell{display:flex;align-items:center;min-width:0;width:100%}
.tool-tree-cell>.tool-tree-indent{display:block;flex:none}
.tool-tree-cell>.cell-text{display:block;flex:1;min-width:0;width:0}
.carrier-expanded{display:inline-block;width:.9em;color:#647b85;font-weight:400}
.carrier-icon{display:inline-block;width:1.1em;margin-right:.35em;vertical-align:middle;color:#647b85}
table[data-fit-family] .carrier-icon svg{display:block;width:1em;height:1em;fill:none;stroke:currentColor;stroke-width:1.1}
.catalog-table .tool-name-cell{text-align:left}
/* Natural width measurement must include indentation and the full group name. */
table.column-probe .tool-tree-cell>.cell-text{width:auto;flex:none}

/* Numeric D codes are green; H/HL/L and the exact R0 token keep their red. */
.tool-diameter{font-weight:700;color:#006400;-webkit-print-color-adjust:exact;print-color-adjust:exact}

/* A long project list reads down the left column, then down the right. */
.catalog-columns{display:grid;grid-template-columns:minmax(0,1fr);gap:4mm;align-items:start;flex:none;min-width:0;width:100%}
.catalog-columns[data-columns="2"]{grid-template-columns:repeat(2,minmax(0,1fr))}
.catalog-columns>.catalog-scroll{min-width:0;align-self:start}

/* Apply fill to cells so it also survives table selection and print layout. */
table[data-fit-family] tbody>tr{--table-row-fill:#fff}
table[data-fit-family] tbody>tr:nth-child(even){--table-row-fill:#ededed}
table[data-fit-family] tbody>tr:is(.group-row,.tool-group-row){--table-row-fill:#dce1e5}
table[data-fit-family] tbody>tr>td{background:var(--table-row-fill)}
table[data-fit-family] tbody>tr.operation-selected>td{background:#dcf2ff}
@media print{
 table[data-fit-family] tbody>tr.operation-selected>td{background:var(--table-row-fill)}
 .catalog-columns,.catalog-columns>.catalog-scroll{break-inside:avoid;page-break-inside:avoid}
}

.duplicate-tool-number{color:#d00000;font-weight:700;-webkit-print-color-adjust:exact;print-color-adjust:exact}

.datum-editor{display:flex;align-items:stretch;width:100%;height:6.2mm}
.datum-editor:focus-within{outline:1px solid #078778;background:#f3fcfa}
.datum-input{display:block;flex:1;min-width:0;width:0;border:0;border-radius:0;outline:0;padding:0 2px;background:transparent;color:#171d21;text-align:center;text-transform:uppercase;font:8pt Arial,sans-serif}
.datum-input::placeholder{color:#929ea3;opacity:1}
.datum-toggle{flex:none;width:18px;padding:0;border:0;background:transparent;color:#48616a;font:12px Arial,sans-serif}
.datum-toggle:hover{background:#e4f3ee}
.datum-suggestions{position:fixed;z-index:50;overflow:auto;max-height:224px;border:1px solid #8fa9ae;border-radius:3px;background:white;box-shadow:0 4px 16px #203b4330;color:#171d21;font:12px/1.35 Arial,sans-serif}
.datum-option{padding:6px 9px;cursor:pointer;white-space:nowrap}
.datum-option[aria-selected="true"]{background:#087d72;color:white}
.datum-print{display:none;font-size:8pt}
@media print{.datum-editor,.datum-suggestions{display:none!important}.datum-print{display:block!important}}

.project-toolbar{position:sticky;top:0;z-index:60;display:flex;align-items:center;gap:12px;flex-wrap:wrap;background:#fff;border-bottom:1px solid #ced8dd;padding:9px 24px}
.project-toolbar select{max-width:300px;padding:6px;border:1px solid #a9c2c8;border-radius:4px;background:white;font:13px Arial}
.project-toolbar label{display:flex;gap:8px;align-items:center;font:13px Arial}
.setup-document{--data-scale:1;position:relative}
.setup-document>.toolbar{top:var(--project-bar-height,52px);z-index:12;padding-top:8px;padding-bottom:8px}
.setup-document>.toolbar .brand{font-size:13px}.setup-document>.toolbar .actions button{font-size:12px;padding:6px 9px}
@media print{.project-toolbar{display:none!important}.setup-document{display:block;break-after:page;page-break-after:always}.setup-document:last-child{break-after:auto;page-break-after:auto}.setup-pages{display:block}.setup-pages>.page{break-after:page;page-break-after:always}.setup-pages>.page:last-child{break-after:auto;page-break-after:auto}}

/* Per-sheet controls are outside the paper and never print. */
.page{position:relative;margin-top:48px}
.page-controls{position:absolute;right:0;bottom:100%;display:flex;flex-wrap:wrap;max-width:100%;justify-content:flex-end;align-items:center;gap:6px 16px;color:#294d54;background:#fff;border:1px solid #b4cbd0;border-radius:5px;padding:4px 8px;font:12px Arial;white-space:nowrap}
.page-controls label{display:flex;align-items:center;gap:8px}
.page-controls.setup-panel{left:0;right:auto;width:100%;border-radius:5px 5px 0 0;padding:10px 12px;z-index:12;justify-content:flex-start;white-space:normal}
.setup-panel>.toolbar{position:static;flex-basis:100%;width:100%;padding:0 0 6px;margin:0;border:0;background:transparent;gap:8px 12px}
.setup-panel>.toolbar .brand{font-size:13px}
.setup-panel .actions button{padding:6px 9px;font-size:12px}
.setup-panel .layout-toolbar{border:0;padding:0;margin:0}
.setup-panel>label{white-space:nowrap}
.slider-label{-webkit-user-select:none;user-select:none}
#save-toast{position:fixed;z-index:10000;display:flex;align-items:center;gap:8px;max-width:calc(100vw - 16px);padding:11px 16px;border:1px solid #86d6a3;border-radius:9px;background:#e2f7e9;color:#17613b;box-shadow:0 5px 18px #164e2d20;font:600 14px/1.35 'Segoe UI',Arial,sans-serif;pointer-events:none}
#save-toast:before{content:'✓';font-size:18px;line-height:1}
@media print{#save-toast{display:none!important}}
.slider-percent{position:relative;cursor:text;border-radius:3px;font-variant-numeric:tabular-nums}
.slider-percent:hover{background:#e8f6f3}.slider-percent:focus-visible{outline:2px solid #087d72;outline-offset:2px}
.slider-percent.percent-editing{color:transparent}
.slider-percent input.slider-percent-input{position:absolute;z-index:1;left:-3px;top:-3px;width:calc(100% + 6px);height:calc(100% + 6px);min-width:0;padding:1px 1.1em 1px 2px;border:1px solid #087d72;border-radius:3px;outline:none;background:#fff;color:#294d54;text-align:right;font:inherit;-webkit-user-select:text;user-select:text}
.slider-percent.percent-editing:after{content:'%';position:absolute;z-index:2;right:0;top:0;color:#294d54;pointer-events:none}
.page-controls input[type=range]{width:115px;accent-color:#087d72}.page-controls input[type=checkbox]{accent-color:#087d72}.page.has-operation-controls,.project-tools-page{margin-top:80px}.page-controls output{min-width:34px;text-align:right}
.page-controls output.slider-percent{min-width:52px}
#operation-options{position:relative;font:13px Arial}#operation-options summary{cursor:pointer;padding:7px;border:1px solid #a9c2c8;border-radius:4px}
.operation-options-panel{position:absolute;top:100%;left:0;z-index:90;width:320px;background:white;border:1px solid #a9c2c8;border-radius:6px;box-shadow:0 5px 18px #173c4930;padding:14px}
.operation-options-panel strong{display:block;margin-bottom:10px;line-height:1.4}.operation-options-panel label{display:flex;align-items:center;gap:8px;padding:5px 0}.operation-options-panel .wrap-option{border-top:1px solid #d4dfe2;margin-top:7px;padding-top:12px;line-height:1.4}
.ops th .cell-text{height:auto!important;min-height:calc(3.4mm * var(--data-scale) - 1px);line-height:1.12!important;white-space:normal!important;overflow:visible;text-overflow:clip}
.ops th{white-space:normal!important;text-align:center}.ops th[data-column-id="name"]{text-align:left}
.ops tbody td.numeric,.ops tbody td.numeric .cell-text{text-align:center}
.ops.wrap-operation-names tbody td[data-column-id="name"] .cell-text{height:auto;min-height:calc(3.4mm * var(--data-scale) - 1px);white-space:normal;overflow-wrap:anywhere;word-break:normal;line-height:1.2;text-overflow:clip}
.ops.wrap-operation-names tbody td[data-column-id="name"] .tree-cell{align-items:center}
.ops tbody td[data-column-id="name"] .tree-cell{width:100%}
.ops .operation-grip{position:absolute;left:.3mm;top:50%;transform:translateY(-50%);float:none}
.ops tbody td[data-column-id="name"]{padding-left:calc(3.8mm * var(--data-scale))!important}
.ops col[hidden],.ops td[hidden],.ops th[hidden]{display:none!important}
@media print{.page,.page.has-operation-controls{margin:0}.page-controls,#operation-options{display:none!important}}


/* The mesh uses the paper as its viewport. Only printed paper edges clip it;
   metadata and tools occupy the available rectangles around the model. */
.project-model-page{margin-top:112px}
.project-model-page .page-content{isolation:isolate}
.project-model-page .continuation-header.no-note{display:block;height:var(--project-header-height,39mm)}
.project-model-page .continuation-header>.metadata{position:absolute;left:var(--project-meta-x);top:var(--project-meta-y);width:var(--project-meta-width);height:var(--project-meta-height);z-index:1;background:#fff}
.project-model-page .continuation-header>.legends{position:absolute;left:var(--project-meta-x);top:var(--project-legend-y);width:var(--project-meta-width);height:var(--project-legend-height);z-index:1;background:#fff}
.project-model-page .project-model{position:absolute;left:0;top:0;width:194mm;height:276.5mm;border:0;outline:0;background:transparent;z-index:0;pointer-events:none;overflow:hidden}
.project-model-page .project-model canvas{pointer-events:none;outline:0!important}
.project-model-page .catalog-heading{position:absolute;left:var(--project-tools-x);top:var(--project-tools-y);width:var(--project-tools-width);height:10mm;z-index:1;background:#fff}
.project-model-page .catalog-columns,.project-model-page .page-content>.catalog-table{position:absolute;left:var(--project-tools-x);top:calc(var(--project-tools-y) + 10mm);width:var(--project-tools-width);z-index:1;background:#fff}
.project-model-page .page-footer{z-index:1;background:#fff}

@media print{.project-model-page{margin:0}.project-model-page .project-model-print{object-fit:fill}}

/* The editor is a left-to-right paper strip; printed A4 pages stay unchanged. */
@media screen{
 body{width:max-content;min-width:100%}
 .project-toolbar{left:0;width:100vw}
 
 #setup-documents{display:flex;align-items:stretch;gap:24px;width:max-content;min-width:100%;padding:0 18px 18px}
 .setup-document{display:flex;flex-direction:column;align-items:flex-start;flex:0 0 auto}
 
 .setup-pages{display:flex;align-items:flex-start;gap:18px;margin-top:auto;padding-top:var(--setup-panel-space,180px)}
 .setup-pages>.page{flex:0 0 210mm;margin:0;zoom:var(--paper-screen-scale,1)}
 
 
 
}

/* Notes retain their font and grow with their content. The row, not the
   editable text, owns the user's minimum height. */
.card-header{height:auto;min-height:49mm;position:relative}
.continuation-header:not(.no-note){height:auto;min-height:28mm;position:relative}
.metadata .note-row{height:14.2mm}
.metadata .note-row th,.metadata .note-row td{vertical-align:middle}
.metadata .note-row .field{height:auto;min-height:1.1em;display:block;padding:.6mm 0;white-space:pre-wrap;overflow-wrap:anywhere}
.note-height-grip{position:absolute;display:block;height:10px;transform:translateY(-50%);z-index:11;cursor:row-resize;touch-action:none;user-select:none;outline:none}
.note-height-grip:after{content:'';position:absolute;left:50%;top:3px;transform:translateX(-50%);width:36px;height:4px;border:1px solid #087d72;border-radius:3px;background:white;opacity:.65}
.note-height-grip:hover,.note-height-grip:focus-visible,.note-height-grip.dragging{background:#087d7240;outline:1px solid #087d72;outline-offset:-3px}
.note-height-grip:hover:after,.note-height-grip:focus-visible:after,.note-height-grip.dragging:after{background:#087d72;opacity:1}
.setup-document.note-sizing,.setup-document.note-sizing *{cursor:row-resize!important;user-select:none!important}
@media print{.note-height-grip{display:none!important}}
</style></head><body>

<div class="project-toolbar"><span class="brand">Карта наладки</span><details id="operation-options"><summary>Столбцы</summary><div class="operation-options-panel"><strong id="operation-options-title"></strong><div id="operation-column-list"></div></div></details><label>Установ <select id="setup-jump" aria-label="Перейти к установу">__SETUP_OPTIONS__</select></label><div class="actions"><button id="undo" type="button" disabled title="Отменить последнее действие (Ctrl+Z)" aria-keyshortcuts="Control+Z Meta+Z">Отменить</button><button id="save" type="button">Сохранить карту</button><button id="save-copy" type="button" hidden>Скачать копию</button><button id="print" class="primary" type="button">Печать</button></div></div>
<main id="setup-documents">__SETUPS__</main>
<script>
'use strict';
const DOC = __DOCUMENT_CONTEXT__;
const SLIDER_RESET_HINT = "Нажмите дважды для возврата к исходному масштабу";
// Edit the displayed percentage, including the model's logarithmic range.
// An unfinished entry is UI only; saved HTML contains the committed value.
const PercentInput=(()=>{
 let editing=null;const pending=new WeakMap();
 function rangeFor(output){const range=output?.previousElementSibling;return range?.matches('input[type="range"]')?range:null;}
 function update(range,value){
  const output=range.nextElementSibling;if(output?.tagName!=='OUTPUT')return;
  const text=value+'%';range.setAttribute('aria-valuetext',text);output.dataset.percentValue=String(value);
  output.classList.add('slider-percent');output.tabIndex=0;output.setAttribute('role','button');
  output.title='Нажмите, чтобы ввести процент с клавиатуры';
  output.setAttribute('aria-label',(range.getAttribute('aria-label')||range.closest('label')?.firstChild?.textContent?.trim()||'Масштаб')+': '+text+'. Изменить процент');
  if(editing?.output!==output)output.textContent=text;
 }
 function finish(apply=true,focus=false){
  const item=editing;if(!item)return;editing=null;
  const raw=item.input.value.trim().replace(/%$/,'').trim().replace(',','.');
  item.output.classList.remove('percent-editing');item.output.textContent=item.output.dataset.percentValue+'%';
  if(apply&&item.range.isConnected&&/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/.test(raw)){
   const n=Number(raw),log=item.range.dataset.logScale==='true',min=log?1:Number(item.range.min),max=log?10000:Number(item.range.max);
   if(Number.isFinite(n)){
    const value=Math.max(min,Math.min(max,Math.round(n)));
    if(value!==Number(item.output.dataset.percentValue)){
     item.range.value=log?100*Math.log10(value):value;pending.set(item.range,value);
     try{item.range.dispatchEvent(new Event('input',{bubbles:true}));item.range.dispatchEvent(new Event('change',{bubbles:true}));}finally{pending.delete(item.range);}
    }
   }
  }
  if(focus&&item.output.isConnected)item.output.focus({preventScroll:true});
 }
 function begin(output){
  const range=rangeFor(output);if(!range||range.disabled||editing?.output===output)return;
  finish();const input=document.createElement('input');input.type='text';input.inputMode='decimal';input.className='slider-percent-input';
  input.value=output.dataset.percentValue;input.setAttribute('aria-label',output.getAttribute('aria-label'));input.autocomplete='off';input.spellcheck=false;
  editing={range,output,input};output.classList.add('percent-editing');output.append(input);input.focus({preventScroll:true});input.select();
 }
 document.addEventListener('click',event=>{const output=event.target.closest?.('output.slider-percent');if(!output||event.target.closest('input'))return;event.preventDefault();begin(output);});
 document.addEventListener('keydown',event=>{
  if(event.target===editing?.input){
   if(event.isComposing)return;
   if(event.key==='Enter'||event.key==='Escape'){event.preventDefault();event.stopPropagation();finish(event.key==='Enter',true);}
   else if((event.ctrlKey||event.metaKey)&&['s','p'].includes(event.key.toLowerCase()))finish();
  }else if(event.target.matches?.('output.slider-percent')&&['Enter',' '].includes(event.key)){event.preventDefault();begin(event.target);}
 });
 document.addEventListener('focusout',event=>{if(event.target===editing?.input)finish();});
 return{update,cancel:()=>finish(false),manual:range=>pending.has(range),
  value:range=>pending.has(range)?pending.get(range):range.dataset.logScale==='true'?Math.pow(10,Number(range.value)/100):Number(range.value),
  cleanClone:copy=>copy.querySelectorAll('output.slider-percent').forEach(output=>{output.classList.remove('percent-editing');output.textContent=output.dataset.percentValue+'%';})};
})();
// Only the NX loopback server injects this session. It is never stored in the
// saved/shared HTML, and neither the browser nor the request chooses a path.
const LOCAL_SESSION=(()=>{
 try{
  const value=JSON.parse(document.getElementById('nx-local-session')?.textContent||'null');
  if(location.protocol!=='http:'||location.hostname!=='127.0.0.1'||!/^\/card\/[a-f0-9]{32}\/$/.test(location.pathname))return null;
  if(value?.protocol!==1||value.id!==DOC.id||! /^[a-f0-9]{64}$/.test(value.key)||value.save!==location.pathname+'save')return null;
  return value;
 }catch(e){return null;}
})();
let changed=false, saving=false, editRevision=0,CardHistory=null;
let fileHandle=null, expectedRevision=document.querySelector('meta[name="nx-card-revision"]').content;
const fileKey=LOCAL_SESSION?'nx-card-file:local:'+DOC.id:'nx-card-file:'+location.href.split('#')[0];
let autosaveTimer=0,autosaveBlocked=false,autosaveFailures=0,pendingLocalSave=null,composing=false;
const activePointers=new Set();
// A manual save is acknowledged only after the requested edits are written.
// This overlay never participates in layout, serialization or autosave notices.
const SaveNotice=(()=>{
 let node=null,timer=0,requested=null;
 function hide(){clearTimeout(timer);node?.remove();node=null;}
 function position(){
  if(!node)return;const button=document.getElementById('save').getBoundingClientRect(),box=node.getBoundingClientRect();
  node.style.left=Math.max(8,Math.min(button.left,innerWidth-box.width-8))+'px';
  node.style.top=Math.max(8,Math.min(button.bottom+8,innerHeight-box.height-8))+'px';
 }
 function saved(revision){
  if(requested===null||revision<requested)return;requested=null;hide();
  node=document.createElement('div');node.id='save-toast';node.setAttribute('role','status');node.setAttribute('aria-live','polite');node.setAttribute('aria-atomic','true');
  node.textContent='Карта сохранена';document.body.append(node);position();timer=setTimeout(hide,3000);
 }
 window.addEventListener('resize',position);window.addEventListener('scroll',position,{passive:true});
 return{request:revision=>{hide();requested=revision;},saved,cancel:()=>{requested=null;}};
})();
function revisionDraftKey(revision){return 'nx-card-v2.05:'+DOC.id+'|'+revision+'|'+fileKey;}
let draftKey=revisionDraftKey(expectedRevision);
function acceptSavedRevision(revision){
 const previousKey=draftKey;expectedRevision=revision;draftKey=revisionDraftKey(revision);
 document.querySelector('meta[name="nx-card-revision"]').content=revision;
 try{localStorage.removeItem(previousKey);}catch(e){}
}
function handleStore(action,handle){return new Promise(resolve=>{
 let request,done=false;
 const finish=value=>{if(!done){done=true;resolve(value||null);}};
 try{
  if(!window.indexedDB){finish();return;}
  request=indexedDB.open('nx-setup-card-files',1);
  request.onupgradeneeded=()=>{if(!request.result.objectStoreNames.contains('handles'))request.result.createObjectStore('handles');};
  request.onerror=request.onblocked=()=>finish();
  request.onsuccess=()=>{
   const db=request.result;if(done){db.close();return;}
   try{
    const tx=db.transaction('handles',action==='get'?'readonly':'readwrite'),store=tx.objectStore('handles');
    const result=action==='get'?store.get(fileKey):(action==='put'?store.put(handle,fileKey):store.delete(fileKey));
    tx.oncomplete=()=>{db.close();finish(action==='get'?result.result:null);};
    tx.onerror=tx.onabort=()=>{db.close();finish();};
   }catch(e){db.close();finish();}
  };
 }catch(e){finish();}
});}
handleStore('get').then(handle=>{if(handle&&handle.kind==='file'&&!saving&&!fileHandle)fileHandle=handle;});
function fieldValue(el){const value=el.matches('input,select')?el.value:el.textContent;return el.matches('.datum-input')?value.toUpperCase():value;}
function setFieldValue(el,value){
 value=String(value??'');const datum=el.matches('.datum-input');if(datum)value=value.toUpperCase();
 if(el.matches('input')){
  if(el.value!==value){
   const before=el.value,start=el.selectionStart,end=el.selectionEnd,direction=el.selectionDirection;el.value=value;
   // Keep the caret/selection when uppercasing an edit, including Unicode expansions.
   if(datum&&document.activeElement===el&&start!==null&&end!==null)el.setSelectionRange(before.slice(0,start).toUpperCase().length,before.slice(0,end).toUpperCase().length,direction||'none');
  }
  el.setAttribute('value',value);const printed=el.closest('td')?.querySelector('.datum-print');if(printed)printed.textContent=value;
 }
 else if(el.matches('select')){el.value=value;[...el.options].forEach(o=>o.toggleAttribute('selected',o.value===el.value));}
 else el.textContent=value;
}

const OP_COLUMNS=[['name','Операции'],['mcs','СКС'],['change','Смена инструмента'],['tool','Инструмент'],['number','T.'],['time','Время'],['feed','Подача'],['speed','Обороты'],['stock','Припуск на стенки'],['floor_stock','Припуск на пол'],['zmin','Zmin']];
const ColumnOptions=(()=>{
 const meta=document.querySelector('meta[name="nx-operation-options"]'),list=document.getElementById('operation-column-list');
 let hidden=new Set(),wrapped=false;
 function state(){return{hidden:[...hidden],wrapNames:wrapped};}
 function set(data){hidden=new Set(Array.isArray(data?.hidden)?data.hidden.filter(k=>OP_COLUMNS.some(c=>c[0]===k)):[]);wrapped=data?.wrapNames===true;sync();}
 function sync(){meta.content=JSON.stringify(state());list.querySelectorAll('input').forEach(c=>c.checked=!hidden.has(c.value));}
 function apply(table){table.hidden=hidden.size===OP_COLUMNS.length;table.querySelectorAll('[data-column-id]').forEach(c=>c.hidden=hidden.has(c.dataset.columnId));}
 function change(){sync();editors.forEach(e=>e.paginate());markChanged();}
 document.getElementById('operation-options-title').textContent=DOC.title+' — Столбцы';
 list.replaceChildren();
 OP_COLUMNS.forEach(([id,text])=>{const label=document.createElement('label'),input=document.createElement('input');input.type='checkbox';input.value=id;input.checked=true;input.addEventListener('change',()=>{if(input.checked)hidden.delete(id);else hidden.add(id);change();});label.append(input,document.createTextNode(text));list.append(label);});
 try{set(JSON.parse(meta.content));}catch(e){set({});}
 return{state,set,apply,shown:id=>!hidden.has(id),any:()=>hidden.size<OP_COLUMNS.length,wrap:()=>wrapped};
})();
function restoreCellText(root){root.querySelectorAll('[data-full-html]').forEach(n=>{n.innerHTML=n.dataset.fullHtml;delete n.dataset.fullHtml;});}
const operationTableSchemas=new WeakMap();
function migrateOperationTables(root){
 root.querySelectorAll('table.ops').forEach(table=>{
  restoreCellText(table);
  const head=table.tHead?.rows[0];if(!head)return;
  const hasZmin=[...head.cells].some(c=>c.dataset.columnId==='zmin'),legacy=!hasZmin&&table.dataset.opsSchema!=='3'&&head.cells.length===11;
  operationTableSchemas.set(table,legacy?1:(hasZmin?3:2));
  if(legacy){
   for(const row of table.rows)row.cells[3]?.remove();table.querySelectorAll('colgroup>col')[3]?.remove();
   const floor=table.dataset.minMm?.split(',');if(floor?.length===11){floor.splice(3,1);table.dataset.minMm=floor.join(',');}table.dataset.flexCols='0';
  }
  if(!hasZmin&&head.cells.length===10){
   const col=document.createElement('col');col.dataset.columnId='zmin';table.querySelector('colgroup').append(col);
   for(const row of table.rows){const cell=document.createElement(row.parentElement===table.tHead?'th':'td');cell.dataset.columnId='zmin';if(cell.tagName==='TD')cell.className='numeric';const span=document.createElement('span');span.className='cell-text';cell.append(span);row.append(cell);}
   const floor=table.dataset.minMm.split(',');floor.push('7');table.dataset.minMm=floor.join(',');
  }
  [...table.querySelectorAll('colgroup>col')].forEach((col,i)=>col.dataset.columnId=OP_COLUMNS[i][0]);
  [...table.rows].forEach(row=>[...row.cells].forEach((cell,i)=>cell.dataset.columnId=OP_COLUMNS[i][0]));
  [...table.tHead.rows[0].cells].forEach((cell,i)=>{
   const [id,label]=OP_COLUMNS[i],text=cell.querySelector('.cell-text');cell.title=label;cell.setAttribute('aria-label',label);
   if(id!=='change'){text.replaceChildren();label.split(' ').forEach((word,j)=>{if(j)text.append(document.createElement('br'));text.append(document.createTextNode(word));});}text.title=label;
  });
  for(const row of table.tBodies[0].rows){
   for(const key of ['stock','floor_stock','feed','speed']){
    const cell=row.querySelector('[data-column-id="'+key+'"]'),text=cell?.querySelector('.cell-text');if(!text)continue;
    let value=text.textContent.trim();
    if(['stock','floor_stock'].includes(key)&&/^[+-]?\d+(?:[.,]\d+)?$/.test(value)){
     value=value.replace(',','.');if(Number(value)===0)value='';else if(value.includes('.'))value=value.replace(/0+$/,'').replace(/\.$/,'');
    }
    if(key==='feed')value=value.replace(/mmpm/g,'мм/мин').replace(/mm\/rev/g,'мм/об').replace(/in\/rev/g,'дюйм/об').replace(/ipm/g,'дюйм/мин').replace(/RAPID/g,'Быстрый ход');
    if(key==='speed')value=value.replace(/rpm/g,'об/мин');
    text.textContent=value;text.title=value;cell.title=value;
   }
  }
  table.dataset.opsSchema='3';table.dataset.flexCols='0';ColumnOptions.apply(table);
 });
 root.querySelectorAll('template').forEach(t=>migrateOperationTables(t.content));
}
migrateOperationTables(document);

// One embedded mesh and one renderer for the project's first sheet.
// No network, workers, libraries, STL files or NX display objects are required.
const ProjectModel=(()=>{
 const dataNode=document.getElementById('nx-project-model');
 let data=null;try{data=JSON.parse(dataNode?.textContent||'null');}catch(e){}
 const identity=()=>({rotation:[0,0,0,1],pan:[0,0],zoom:1});
 let view=identity(),positions=null,normals=null,radius=1,base=1,offset=[0,0],node=null,canvas=null,drag=null,frame=0,needsLayout=false;
 let modelBounds=null;
 let meshEdges=[],faceNormals=null,faceCenters=null,edgePoints=null,edgeKey='',edgeCount=0,edgeRevision=0;
 const sheetWidth=194,sheetHeight=276.5;
 let backend=null,lastURL='',lastKey='';
 function normalize(value){
  const result=identity(),q=value?.rotation,p=value?.pan;
  if(Array.isArray(q)&&q.length===4&&q.every(Number.isFinite)){const n=Math.hypot(...q);if(n>1e-8)result.rotation=q.map(x=>x/n);}
  if(Array.isArray(p)&&p.length===2&&p.every(Number.isFinite))result.pan=p.map(x=>Math.max(-100000,Math.min(100000,x)));
  if(Number.isFinite(value?.zoom))result.zoom=Math.max(.0001,Math.min(10000,value.zoom));
  return result;
 }
 function state(){return{id:data?.id,view:JSON.parse(JSON.stringify(view))};}
 function restore(saved){if(data&&saved?.id===data.id){view=normalize(saved.view);adoptScale();invalidate();sync();queue(true);}}
 function sync(){if(data){data.view=view;dataNode.textContent=JSON.stringify(data);}}
 function matrix(){const [x,y,z,w]=view.rotation;return [1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w),2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w),2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)];}
 function rotate(dx,dy){
  const length=Math.hypot(dx,dy);if(!length)return;
  const before=geometry();
  const angle=length*Math.PI,half=Math.sin(angle/2),a=[dy/length*half,dx/length*half,0,Math.cos(angle/2)],b=view.rotation;
  view.rotation=normalize({rotation:[a[3]*b[0]+a[0]*b[3]+a[1]*b[2]-a[2]*b[1],a[3]*b[1]-a[0]*b[2]+a[1]*b[3]+a[2]*b[0],a[3]*b[2]+a[0]*b[1]-a[1]*b[0]+a[2]*b[3],a[3]*b[3]-a[0]*b[0]-a[1]*b[1]-a[2]*b[2]]}).rotation;
  // Mesh coordinates are centered on the 3D body's bounds when exported.
  // Keep that fixed origin at the same point on paper while its projected
  // bounds change. Re-centering / anchoring each new silhouette made the
  // part appear to orbit an offset axis, especially after magnification.
  invalidate();const after=geometry();
  view.pan[0]+=before.cx-after.cx+(after.bounds.center[0]-before.bounds.center[0])*before.unit;
  view.pan[1]+=before.cy-after.cy+(before.bounds.center[1]-after.bounds.center[1])*before.unit;
 }
 function project(x,y,z){const eye=data.projection;if(!eye)return [x,y,z];const k=eye[2]/Math.max(.01,eye[2]-z);return[(x-eye[0])*k,(y-eye[1])*k,z];}
 function bounds(){
  const m=matrix(),lo=[Infinity,Infinity],hi=[-Infinity,-Infinity];
  for(let i=0;i<positions.length;i+=3){const x=positions[i],y=positions[i+1],z=positions[i+2],p=project(m[0]*x+m[1]*y+m[2]*z,m[3]*x+m[4]*y+m[5]*z,m[6]*x+m[7]*y+m[8]*z);for(let j=0;j<2;j++){lo[j]=Math.min(lo[j],p[j]);hi[j]=Math.max(hi[j],p[j]);}}
  return{width:Math.max(1e-6,hi[0]-lo[0]),height:Math.max(1e-6,hi[1]-lo[1]),center:lo.map((x,i)=>(x+hi[i])/2)};
 }
 function vertexNormals(flat){
  if(data.normals&&data.normal_format==='snorm16'){
   try{const bytes=Uint8Array.from(atob(data.normals),c=>c.charCodeAt(0));if(bytes.length!==positions.length*2)throw Error('Normal count');
    const dv=new DataView(bytes.buffer),result=new Float32Array(positions.length);
    for(let i=0;i<result.length;i+=3){const x=dv.getInt16(i*2,true),y=dv.getInt16((i+1)*2,true),z=dv.getInt16((i+2)*2,true),length=Math.hypot(x,y,z);if(length<1)throw Error('Zero normal');result.set([x/length,y/length,z/length],i);}return result;
   }catch(e){}
  }
  // Older cards / NX builds without vertex normals: angle-weighted smoothing
  // across coincident vertices, with a 30-degree crease limit. Caps, corners
  // and ordinary chamfers retain their separate surface normals.
  const key=i=>[0,1,2].map(j=>Math.round(positions[i+j]*1e6)).join(','),adjacent=new Map(),result=new Float32Array(flat.length),limit=Math.cos(Math.PI/6);
  for(let i=0;i<positions.length;i+=9)for(let j=0;j<3;j++){
   const a=i+j*3,b=i+(j+1)%3*3,c=i+(j+2)%3*3;
   const u=[0,1,2].map(k=>positions[b+k]-positions[a+k]),v=[0,1,2].map(k=>positions[c+k]-positions[a+k]);
   const angle=Math.atan2(Math.hypot(u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]),u[0]*v[0]+u[1]*v[1]+u[2]*v[2]),k=key(a);
   if(!adjacent.has(k))adjacent.set(k,[]);adjacent.get(k).push(i,angle);
  }
  for(let i=0;i<positions.length;i+=3){const list=adjacent.get(key(i));let x=0,y=0,z=0;
   for(let j=0;j<list.length;j+=2){const k=list[j],weight=list[j+1];if(flat[i]*flat[k]+flat[i+1]*flat[k+1]+flat[i+2]*flat[k+2]>=limit){x+=flat[k]*weight;y+=flat[k+1]*weight;z+=flat[k+2]*weight;}}
   const length=Math.hypot(x,y,z);result.set(length>1e-12?[x/length,y/length,z/length]:flat.subarray(i,i+3),i);
  }
  return result;
 }
 function initialize(){
  if(!data||data.schema!==1||!data.vertices)return false;
  try{
   const limit=data.triangle_limit??500000;
   if(!Number.isSafeInteger(limit)||limit<1||!Number.isSafeInteger(data.triangles)||data.triangles<1||data.triangles>limit)throw Error('Некорректная сетка');
   const bytes=Uint8Array.from(atob(data.vertices),c=>c.charCodeAt(0));
   if(bytes.length!==data.triangles*36||!bytes.length)throw Error('Некорректная сетка');
   const values=new DataView(bytes.buffer);positions=new Float32Array(bytes.length/4);
   for(let i=0;i<positions.length;i++){positions[i]=values.getFloat32(i*4,true);if(!Number.isFinite(positions[i]))throw Error('Некорректные координаты');}
   normals=new Float32Array(positions.length);radius=0;
   for(let i=0;i<positions.length;i+=9){
    const ax=positions[i+3]-positions[i],ay=positions[i+4]-positions[i+1],az=positions[i+5]-positions[i+2],bx=positions[i+6]-positions[i],by=positions[i+7]-positions[i+1],bz=positions[i+8]-positions[i+2];
    const n=[ay*bz-az*by,az*bx-ax*bz,ax*by-ay*bx],size=Math.hypot(...n)||1;
    for(let j=0;j<9;j+=3){normals.set(n.map(x=>x/size),i+j);radius=Math.max(radius,Math.hypot(positions[i+j],positions[i+j+1],positions[i+j+2]));}
   }
   const flat=normals;normals=vertexNormals(flat);buildEdges(flat);
   const b=bounds();data.aspect=b.width/b.height;base=.96*Math.min(44/b.width,32/b.height);offset=b.center;
   view=normalize(data.view);
   if(data.view_units!=='mm'){const oldScale=Number(document.querySelector('meta[name="nx-project-image-scale"]')?.content)||100;view.pan=[view.pan[0]*b.width*base*oldScale/100,view.pan[1]*b.height*base*oldScale/100];}
   data.view_units='mm';sync();return true;
  }catch(e){data=null;positions=null;return false;}
 }
 // Join coincident triangle vertices. Only creases, open boundaries and the
 // current silhouette are drawn; coplanar triangulation never becomes a grid.
 function buildEdges(flat){
  const vertices=new Map(),ids=new Uint32Array(positions.length/3),pairs=new Map();
  faceNormals=new Float32Array(positions.length/3);faceCenters=new Float32Array(positions.length/3);
  for(let i=0;i<positions.length;i+=3){const key=[0,1,2].map(j=>Math.round(positions[i+j]*1e6)).join(',');if(!vertices.has(key))vertices.set(key,vertices.size);ids[i/3]=vertices.get(key);}
  const smoothLimit=Math.cos(Math.PI/30),creaseLimit=Math.cos(Math.PI/9);
  const dotAt=(a,b,values)=>values[a]*values[b]+values[a+1]*values[b+1]+values[a+2]*values[b+2];
  for(let i=0;i<positions.length;i+=9){
   const face=i/9;faceNormals.set(flat.subarray(i,i+3),face*3);
   for(let k=0;k<3;k++)faceCenters[face*3+k]=(positions[i+k]+positions[i+3+k]+positions[i+6+k])/3;
   for(let j=0;j<3;j++){
    let a=i+j*3,b=i+(j+1)%3*3,va=ids[a/3],vb=ids[b/3];if(va===vb)continue;
    if(va>vb){[va,vb]=[vb,va];[a,b]=[b,a];}const key=va+':'+vb,edge=pairs.get(key);
    if(!edge)pairs.set(key,[a,b,face,-1,false]);
    else{edge[4]=edge[4]||edge[3]!==-1||dotAt(edge[0],a,normals)<smoothLimit||dotAt(edge[1],b,normals)<smoothLimit||dotAt(edge[2]*9,i,flat)<creaseLimit;edge[3]=face;}
   }
  }
  meshEdges=[...pairs.values()];edgePoints=new Float32Array(meshEdges.length*6);
 }
 function edgeLines(m){
  const key=view.rotation.join(',');if(edgeKey===key)return edgePoints.subarray(0,edgeCount);
  const facing=new Float32Array(faceNormals.length/3),eye=data.projection;
  for(let i=0;i<faceNormals.length;i+=3){
   const x=faceNormals[i],y=faceNormals[i+1],z=faceNormals[i+2],nz=m[6]*x+m[7]*y+m[8]*z;
   if(!eye){facing[i/3]=nz;continue;}
   const nx=m[0]*x+m[1]*y+m[2]*z,ny=m[3]*x+m[4]*y+m[5]*z,cx=faceCenters[i],cy=faceCenters[i+1],cz=faceCenters[i+2];
   facing[i/3]=nx*(eye[0]-m[0]*cx-m[1]*cy-m[2]*cz)+ny*(eye[1]-m[3]*cx-m[4]*cy-m[5]*cz)+nz*(eye[2]-m[6]*cx-m[7]*cy-m[8]*cz);
  }
  edgeCount=0;
  for(const [a,b,f0,f1,crease] of meshEdges){
   if(!crease&&f1!==-1&&!(facing[f0]*facing[f1]<-1e-12))continue;
   edgePoints.set(positions.subarray(a,a+3),edgeCount);edgePoints.set(positions.subarray(b,b+3),edgeCount+3);edgeCount+=6;
  }
  edgeKey=key;edgeRevision++;return edgePoints.subarray(0,edgeCount);
 }
 const active=initialize();
 function invalidate(){modelBounds=null;lastKey='';}
 function adoptScale(){if(active&&view.zoom!==1){ProjectImage.set(ProjectImage.value()*view.zoom);view.zoom=1;sync();}}
 function geometry(){
  if(!modelBounds)modelBounds=bounds();const b=modelBounds,unit=base*ProjectImage.value()/100,width=b.width*unit,height=b.height*unit;
  const x=sheetWidth-width-1+view.pan[0],y=.5+view.pan[1];return{x,y,width,height,right:x+width,bottom:y+height,cx:x+width/2,cy:y+height/2,unit,bounds:b};
 }
 function layout(page){
  if(!active||!page.querySelector('.project-model'))return false;
  page.classList.add('project-model-page');const header=page.querySelector('.continuation-header');
  if(header)for(const name of Array.from(header.style))if(name.startsWith('--project-'))header.style.removeProperty(name);
  const g=geometry(),gap=3;
  const left=Math.max(0,Math.min(sheetWidth,g.x-gap)),right=Math.max(0,Math.min(sheetWidth,sheetWidth-g.right-gap));
  const visible=g.right>0&&g.x<sheetWidth&&g.bottom>0&&g.y<sheetHeight;
  let x=0,y=0,width=sheetWidth,fallback=false;
  if(visible&&g.y<40){
   if(Math.max(left,right)>=72){width=Math.max(left,right);x=left>=right?0:sheetWidth-width;}
   else if(g.bottom+gap+37<210){y=Math.max(0,g.bottom+gap);}
   else{width=72;x=left>=right?0:sheetWidth-width;fallback=true;}
  }
  const narrow=Math.max(0,140-width),metaHeight=14+narrow*.4,legendHeight=20.85+narrow*.28,metaBottom=y+metaHeight+2+legendHeight;
  const candidates=[],add=(tx,ty,tw,bottom)=>{if(tw>=60&&bottom-ty>=18)candidates.push({x:tx,y:ty,width:tw,bottom,area:tw*(bottom-ty-9)});};
  add(0,Math.max(metaBottom,visible?Math.min(sheetHeight,g.bottom):0)+gap,sheetWidth,sheetHeight);
  if(visible){
   if(left>=72)add(0,Math.max(metaBottom,g.y<0?0:metaBottom)+gap,left,sheetHeight);
   if(right>=72)add(sheetWidth-right,metaBottom+gap,right,sheetHeight);
   if(g.y>metaBottom+24)add(0,metaBottom+gap,sheetWidth,Math.min(sheetHeight,g.y-gap));
  }
  candidates.sort((a,b)=>b.area-a.area);let tools=candidates[0];
  if(!tools){tools={x,y:metaBottom+gap,width,bottom:sheetHeight};fallback=true;}
  const vars={'meta-x':x,'meta-y':y,'meta-width':width,'meta-height':metaHeight,'legend-y':y+metaHeight+2,'legend-height':legendHeight,
   'tools-x':tools.x,'tools-y':tools.y,'tools-width':tools.width,'tools-bottom':tools.bottom,'header-height':metaBottom,'label-width':Math.min(30,width*.35)};
  for(const [key,value] of Object.entries(vars))page.style.setProperty('--project-'+key,value+'mm');
  page.dataset.projectToolsBottom=String(tools.bottom);
  const outside=g.x<0||g.y<0||g.right>sheetWidth||g.bottom>sheetHeight;
  page.dataset.modelClipped=String(outside||fallback);
  return true;
 }
 function webgl(){
  const surface=document.createElement('canvas'),gl=surface.getContext('webgl',{alpha:true,antialias:true,preserveDrawingBuffer:true,premultipliedAlpha:true});
  if(!gl)return null;
  const shaders=[],buffers=[],programs=[];let lineRevision=-1,lineVertices=0;
  try{
   const projection=`uniform mat3 rotation;uniform vec2 scale;uniform vec2 pan;uniform vec2 center;uniform vec3 eye;uniform float perspective;uniform float depth;
    vec4 projectPoint(vec3 p){vec3 v=rotation*p;float w=mix(1.0,max(.01,1.0-v.z/eye.z),perspective);vec2 xy=v.xy-perspective*eye.xy;return vec4((xy-center*w)*scale+pan*w,-v.z/depth*w,w);}`;
   function program(vertex,fragment){
    const result=gl.createProgram();programs.push(result);
    [vertex,fragment].forEach((source,i)=>{const shader=gl.createShader(i?gl.FRAGMENT_SHADER:gl.VERTEX_SHADER);shaders.push(shader);gl.shaderSource(shader,source);gl.compileShader(shader);if(!gl.getShaderParameter(shader,gl.COMPILE_STATUS))throw Error(gl.getShaderInfoLog(shader));gl.attachShader(result,shader);});
    gl.linkProgram(result);if(!gl.getProgramParameter(result,gl.LINK_STATUS))throw Error(gl.getProgramInfoLog(result));
    return{program:result,uniforms:Object.fromEntries(['rotation','scale','pan','center','eye','perspective','depth','viewport','stroke'].map(name=>[name,gl.getUniformLocation(result,name)]))};
   }
   const solid=program('attribute vec3 p;attribute vec3 n;varying vec3 normal;'+projection+'void main(){gl_Position=projectPoint(p);normal=rotation*n;}',
    'precision mediump float;varying vec3 normal;void main(){vec3 n=normalize(normal);if(n.z<0.0)n=-n;float light=.42+.58*max(0.0,dot(n,normalize(vec3(-.35,.5,1.0))));gl_FragColor=vec4(vec3(.72,.78,.81)*light,1.0);}');
   // Screen-space ribbons retain a thin 0.18 mm stroke at every model zoom
   // and print resolution, even on WebGL devices limited to 1-pixel lines.
   const lines=program('attribute vec3 p;attribute vec3 other;attribute float side;uniform vec2 viewport;uniform mediump float stroke;varying mediump float distance;'+projection+
    'void main(){vec4 a=projectPoint(p),b=projectPoint(other);vec2 d=(b.xy/b.w-a.xy/a.w)*viewport;vec2 n=vec2(-d.y,d.x)/max(.00001,length(d));distance=side*(stroke+1.0)*.5;a.xy+=n*side*(stroke+1.0)/viewport*a.w;a.z-=.000002*a.w;gl_Position=a;}',
    'precision mediump float;uniform mediump float stroke;varying mediump float distance;void main(){float alpha=clamp((stroke+1.0)*.5-abs(distance),0.0,1.0);if(alpha<=0.0)discard;gl_FragColor=vec4(.16,.20,.22,alpha);}');
   function buffer(array){const b=gl.createBuffer();buffers.push(b);gl.bindBuffer(gl.ARRAY_BUFFER,b);if(array)gl.bufferData(gl.ARRAY_BUFFER,array,gl.STATIC_DRAW);return b;}
   const solidP=buffer(positions),solidN=buffer(normals),lineBuffer=buffer(null);
   function attribute(target,name,b,size,stride=0,offset=0){const at=gl.getAttribLocation(target.program,name);gl.bindBuffer(gl.ARRAY_BUFFER,b);gl.enableVertexAttribArray(at);gl.vertexAttribPointer(at,size,gl.FLOAT,false,stride,offset);}
   function use(target,w,h,m,s,placement){
    gl.useProgram(target.program);const u=target.uniforms;
    gl.uniformMatrix3fv(u.rotation,false,new Float32Array([m[0],m[3],m[6],m[1],m[4],m[7],m[2],m[5],m[8]]));
    gl.uniform2f(u.scale,2*s/w,2*s/h);gl.uniform2f(u.pan,2*placement.cx/sheetWidth-1,1-2*placement.cy/sheetHeight);gl.uniform2f(u.center,placement.bounds.center[0],placement.bounds.center[1]);
    gl.uniform3fv(u.eye,new Float32Array(data.projection||[0,0,2]));gl.uniform1f(u.perspective,data.projection?1:0);gl.uniform1f(u.depth,Math.max(1,radius*2));
   }
   gl.enable(gl.DEPTH_TEST);gl.disable(gl.CULL_FACE);gl.clearColor(0,0,0,0);
   surface.addEventListener('webglcontextlost',event=>{event.preventDefault();backend=null;queue();});
   return{surface,draw(w,h,m,s,placement){
    if(gl.isContextLost())throw Error('WebGL context lost');surface.width=w;surface.height=h;gl.viewport(0,0,w,h);gl.depthMask(true);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);
    const stroke=.18*w/sheetWidth;
    gl.disable(gl.BLEND);gl.enable(gl.POLYGON_OFFSET_FILL);gl.polygonOffset(stroke*.5+.75,1);
    use(solid,w,h,m,s,placement);attribute(solid,'p',solidP,3);attribute(solid,'n',solidN,3);gl.drawArrays(gl.TRIANGLES,0,positions.length/3);gl.disable(gl.POLYGON_OFFSET_FILL);
    const edges=edgeLines(m);
    if(lineRevision!==edgeRevision){
     const vertices=new Float32Array(edges.length*7);let at=0;
     for(let i=0;i<edges.length;i+=6)for(const [a,b,side] of [[0,3,1],[0,3,-1],[3,0,-1],[3,0,-1],[0,3,-1],[3,0,1]]){
      vertices.set(edges.subarray(i+a,i+a+3),at);vertices.set(edges.subarray(i+b,i+b+3),at+3);vertices[at+6]=side;at+=7;
     }
     gl.bindBuffer(gl.ARRAY_BUFFER,lineBuffer);gl.bufferData(gl.ARRAY_BUFFER,vertices,gl.DYNAMIC_DRAW);lineVertices=vertices.length/7;lineRevision=edgeRevision;
    }
    use(lines,w,h,m,s,placement);gl.uniform2f(lines.uniforms.viewport,w,h);gl.uniform1f(lines.uniforms.stroke,stroke);
    attribute(lines,'p',lineBuffer,3,28);attribute(lines,'other',lineBuffer,3,28,12);attribute(lines,'side',lineBuffer,1,28,24);
    gl.depthMask(false);gl.depthFunc(gl.LEQUAL);gl.enable(gl.BLEND);gl.blendFuncSeparate(gl.SRC_ALPHA,gl.ONE_MINUS_SRC_ALPHA,gl.ONE,gl.ONE_MINUS_SRC_ALPHA);gl.drawArrays(gl.TRIANGLES,0,lineVertices);gl.depthMask(true);gl.depthFunc(gl.LESS);
   }};
  }catch(e){buffers.forEach(b=>gl.deleteBuffer(b));shaders.forEach(s=>gl.deleteShader(s));programs.forEach(p=>gl.deleteProgram(p));return null;}
 }
 // Depth-buffer fallback for browsers with disabled/unavailable WebGL. It uses
 // the same triangles, camera and lighting, including holes and hidden faces.
 function software(ctx,w,h,m,s,placement){
  const result=ctx.createImageData(w,h),pixels=result.data,zbuffer=new Float32Array(w*h),zslopes=new Float32Array(w*h);zbuffer.fill(-Infinity);
  const lightLength=Math.hypot(.35,.5,1);
  for(let i=0;i<positions.length;i+=9){
   const v=[];
   for(let j=0;j<9;j+=3){const x=positions[i+j],y=positions[i+j+1],z=positions[i+j+2],p=project(m[0]*x+m[1]*y+m[2]*z,m[3]*x+m[4]*y+m[5]*z,m[6]*x+m[7]*y+m[8]*z);v.push([(p[0]-placement.bounds.center[0])*s+w*placement.cx/sheetWidth,-(p[1]-placement.bounds.center[1])*s+h*placement.cy/sheetHeight,p[2]]);}
   const [a,b,c]=v,den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1]);if(Math.abs(den)<1e-8)continue;
   const x0=Math.max(0,Math.floor(Math.min(a[0],b[0],c[0]))),x1=Math.min(w-1,Math.ceil(Math.max(a[0],b[0],c[0]))),y0=Math.max(0,Math.floor(Math.min(a[1],b[1],c[1]))),y1=Math.min(h-1,Math.ceil(Math.max(a[1],b[1],c[1])));
   if(x0>x1||y0>y1)continue;
   const lights=[];for(let j=0;j<9;j+=3){const nx=normals[i+j],ny=normals[i+j+1],nz=normals[i+j+2],n=[m[0]*nx+m[1]*ny+m[2]*nz,m[3]*nx+m[4]*ny+m[5]*nz,m[6]*nx+m[7]*ny+m[8]*nz],sign=n[2]<0?-1:1;lights.push(.42+.58*Math.max(0,sign*(-.35*n[0]+.5*n[1]+n[2])/lightLength));}
   const da=(b[1]-c[1])/den,db=(c[1]-a[1])/den,ea=(c[0]-b[0])/den,eb=(a[0]-c[0])/den;
   const slope=Math.max(Math.abs(da*(a[2]-c[2])+db*(b[2]-c[2])),Math.abs(ea*(a[2]-c[2])+eb*(b[2]-c[2])));
   for(let y=y0;y<=y1;y++){
    let wa=((b[1]-c[1])*(x0+.5-c[0])+(c[0]-b[0])*(y+.5-c[1]))/den,wb=((c[1]-a[1])*(x0+.5-c[0])+(a[0]-c[0])*(y+.5-c[1]))/den;
    for(let x=x0;x<=x1;x++,wa+=da,wb+=db){const wc=1-wa-wb;if(wa< -1e-7||wb< -1e-7||wc< -1e-7)continue;
     let z=wa*a[2]+wb*b[2]+wc*c[2],fa=wa,fb=wb,fc=wc;if(data.projection){const d=data.projection[2];fa=wa/(d-a[2]);fb=wb/(d-b[2]);fc=wc/(d-c[2]);const sum=fa+fb+fc;z=d-1/sum;fa/=sum;fb/=sum;fc/=sum;}
     const at=y*w+x;if(z<=zbuffer[at])continue;zbuffer[at]=z;zslopes[at]=slope;const light=fa*lights[0]+fb*lights[1]+fc*lights[2];pixels[at*4]=Math.round(183.6*light);pixels[at*4+1]=Math.round(198.9*light);pixels[at*4+2]=Math.round(206.55*light);pixels[at*4+3]=255;
    }
   }
  }
  const edges=edgeLines(m),half=.09*w/sheetWidth,reach=half+.5;
  for(let i=0;i<edges.length;i+=6){
   const v=[];
   for(let j=0;j<6;j+=3){const x=edges[i+j],y=edges[i+j+1],z=edges[i+j+2],p=project(m[0]*x+m[1]*y+m[2]*z,m[3]*x+m[4]*y+m[5]*z,m[6]*x+m[7]*y+m[8]*z);v.push([(p[0]-placement.bounds.center[0])*s+w*placement.cx/sheetWidth,-(p[1]-placement.bounds.center[1])*s+h*placement.cy/sheetHeight,p[2]]);}
   const [a,b]=v,dx=b[0]-a[0],dy=b[1]-a[1],length=dx*dx+dy*dy;if(length<1e-10)continue;
   const major=Math.abs(dy)>Math.abs(dx)?1:0,minor=1-major,delta=b[major]-a[major],ratio=(b[minor]-a[minor])/delta,pad=reach*Math.sqrt(1+ratio*ratio);
   const begin=Math.max(0,Math.floor(Math.min(a[major],b[major])-reach)),end=Math.min((major?h:w)-1,Math.ceil(Math.max(a[major],b[major])+reach));
   for(let u=begin;u<=end;u++){
    const middle=a[minor]+(u+.5-a[major])*ratio,lo=Math.max(0,Math.floor(middle-pad)),hi=Math.min((major?w:h)-1,Math.ceil(middle+pad));
    for(let k=lo;k<=hi;k++){
     const x=major?k:u,y=major?u:k,t=Math.max(0,Math.min(1,((x+.5-a[0])*dx+(y+.5-a[1])*dy)/length));
     const distance=Math.hypot(x+.5-a[0]-t*dx,y+.5-a[1]-t*dy),alpha=Math.max(0,Math.min(1,reach-distance));if(!alpha)continue;
     let z=a[2]+t*(b[2]-a[2]);if(data.projection){const d=data.projection[2];z=d-1/((1-t)/(d-a[2])+t/(d-b[2]));}
     const at=y*w+x;if(z+zslopes[at]*(half+.75)+.000003<zbuffer[at])continue;
     const old=pixels[at*4+3]/255,total=alpha+old*(1-alpha);
     for(let c=0;c<3;c++)pixels[at*4+c]=([40.8,51,56.1][c]*alpha+pixels[at*4+c]*old*(1-alpha))/total;
     pixels[at*4+3]=total*255;
    }
   }
  }
  ctx.putImageData(result,0,0);
 }
 function draw(high=false){
  if(!active||!canvas?.isConnected)return;
  const r=node.getBoundingClientRect();if(r.width<=0||r.height<=0)return;
  const quality=high?3.125:drag?1:Math.max(2,Math.min(devicePixelRatio||1,3)),limit=high?3300:drag?1100:2200;
  const screenScale=high?screenPaperScale(node):1,rw=r.width/screenScale,rh=r.height/screenScale;
  const factor=Math.min(quality,limit/Math.max(rw,rh)),w=Math.max(1,Math.round(rw*factor)),h=Math.max(1,Math.round(rh*factor));
  if(canvas.width!==w)canvas.width=w;if(canvas.height!==h)canvas.height=h;
  const ctx=canvas.getContext('2d'),m=matrix(),placement=geometry(),s=placement.unit*w/sheetWidth;ctx.clearRect(0,0,w,h);
  if(backend===null)backend=webgl()||false;
  if(backend){try{backend.draw(w,h,m,s,placement);ctx.drawImage(backend.surface,0,0);}catch(e){backend=false;software(ctx,w,h,m,s,placement);}}
  else software(ctx,w,h,m,s,placement);
  node.dataset.renderer=backend?'webgl':'software';
 }
 function queue(layout=false){needsLayout=needsLayout||layout;if(frame)return;frame=requestAnimationFrame(()=>{frame=-1;try{const refresh=needsLayout,focused=canvas&&document.activeElement===canvas;needsLayout=false;if(refresh)editors.forEach(e=>e.flow.refreshCatalog());mount();if(focused&&canvas)canvas.focus({preventScroll:true});draw();}finally{frame=0;}});}
 function mount(){
  const next=document.querySelector('.setup-pages>.project-tools-page .project-model');
  if(!active){if(next)next.replaceChildren();return;}
  if(next===node&&canvas?.isConnected)return;
  node=next;canvas=null;if(!node)return;
  node.querySelectorAll('canvas').forEach(c=>c.remove());canvas=document.createElement('canvas');canvas.tabIndex=0;
  canvas.setAttribute('aria-label','3D-модель детали: перетаскивание — вращение; Shift и перетаскивание — сдвиг; колесо — масштаб.');canvas.title='Перетаскивание — вращение · Shift + перетаскивание — сдвиг · Колесо — масштаб';node.append(canvas);node.classList.add('model-ready');draw();
 }
 function commit(){sync();lastKey='';markChanged();}
 function finish(cancel=false){
  if(!drag)return;
  const item=drag;drag=null;if(cancel)view=item.start;
  try{item.host.releasePointerCapture(item.id);}catch(e){}item.host.style.cursor='';invalidate();queue(true);if(!cancel&&item.moved)commit();
 }
 function prepare(){
  finish();if(frame>0)cancelAnimationFrame(frame);frame=0;needsLayout=false;editors.forEach(e=>e.flow.refreshCatalog());mount();if(!canvas)return;sync();draw(true);
  const key=JSON.stringify([view,ProjectImage.value(),canvas.width,canvas.height]);if(lastKey!==key){lastURL=canvas.toDataURL('image/png');lastKey=key;}
  let image=node.querySelector('.project-model-print');if(!image){image=document.createElement('img');image.className='project-model-print';image.alt='3D-модель детали — выбранный вид';node.prepend(image);}image.src=lastURL;
 }
 function fit(){finish();const b=bounds();ProjectImage.set(100*Math.min(110/(base*b.width),160/(base*b.height)));view.pan=[0,0];invalidate();commit();queue(true);}
 function captureInitial(){if(data&&!data.initialView){data.initialView=normalize(view);data.initialScale=ProjectImage.value();sync();}}
 function reset(dirty=true){finish();view=normalize(data?.initialView);ProjectImage.set(data?.initialScale??ProjectImage.initial());invalidate();sync();lastKey='';if(dirty)markChanged();queue(true);}
 function hit(event){
  const page=event.target.closest?.('.project-model-page');if(!page||event.target.closest('table,.catalog-heading,.page-footer,.page-controls,button,input,select,label,a'))return null;
  const r=page.querySelector('.page-content').getBoundingClientRect(),x=(event.clientX-r.left)*sheetWidth/r.width,y=(event.clientY-r.top)*sheetWidth/r.width,g=geometry();
  return x>=0&&x<=sheetWidth&&y>=0&&y<=sheetHeight&&x>=g.x&&x<=g.right&&y>=g.y&&y<=g.bottom?page:null;
 }
 document.addEventListener('pointerdown',event=>{
  const host=active&&hit(event);if(!host||![0,1,2].includes(event.button))return;event.preventDefault();event.stopPropagation();finish();
  canvas.focus({preventScroll:true});drag={id:event.pointerId,host,start:normalize(view),x:event.clientX,y:event.clientY,pan:event.shiftKey||event.button!==0,moved:false};host.setPointerCapture(event.pointerId);host.style.cursor='grabbing';
 });
 document.addEventListener('pointermove',event=>{
  if(!drag){const host=active&&hit(event);if(node){const page=node.closest('.page');page.style.cursor=host?'grab':'';page.title=host?canvas.title:'';}return;}
  if(event.pointerId!==drag.id)return;event.preventDefault();const r=node.getBoundingClientRect(),dx=event.clientX-drag.x,dy=event.clientY-drag.y;
  if(!dx&&!dy)return;drag.moved=true;drag.x=event.clientX;drag.y=event.clientY;
  if(drag.pan){view.pan[0]+=dx*sheetWidth/r.width;view.pan[1]+=dy*sheetHeight/r.height;invalidate();}else rotate(dx/220,dy/220);queue(true);
 });
 document.addEventListener('pointerup',event=>{if(drag&&event.pointerId===drag.id){event.preventDefault();finish();}});
 document.addEventListener('pointercancel',event=>{if(drag&&event.pointerId===drag.id)finish(true);});
 document.addEventListener('lostpointercapture',event=>{if(drag&&event.pointerId===drag.id)finish(true);});
 document.addEventListener('contextmenu',event=>{if(active&&hit(event))event.preventDefault();});
 document.addEventListener('wheel',event=>{
  if(!active||!hit(event))return;event.preventDefault();finish();const delta=event.deltaY*(event.deltaMode===1?16:event.deltaMode===2?400:1);
  ProjectImage.set(ProjectImage.value()*Math.exp(-Math.max(-300,Math.min(300,delta))*.0015));commit();queue(true);
 },{passive:false});
 document.addEventListener('keydown',event=>{
  if(event.key==='Escape'&&drag){event.preventDefault();finish(true);return;}if(event.target!==canvas)return;
  const arrows={ArrowLeft:[-.06,0],ArrowRight:[.06,0],ArrowUp:[0,-.06],ArrowDown:[0,.06]};
  if(arrows[event.key]){event.preventDefault();const [x,y]=arrows[event.key];if(event.shiftKey){view.pan[0]+=x*50;view.pan[1]+=y*50;invalidate();}else rotate(x,y);commit();queue(true);}
  else if(['+','=','-','Home'].includes(event.key)){event.preventDefault();if(event.key==='Home')reset();else{ProjectImage.set(ProjectImage.value()*(event.key==='-'?1/1.1:1.1));commit();queue(true);}}
 });
 document.addEventListener('click',event=>{const button=event.target.closest('[data-project-model-action]');if(button){if(button.dataset.projectModelAction==='fit')fit();else reset();}});
 window.addEventListener('blur',()=>finish(true));window.addEventListener('resize',()=>queue());
 return{active,state,restore,prepare,mount,queue,layout,geometry,adoptScale,captureInitial,reset,cancel:()=>finish(true),aspect:()=>data?.aspect||44/32};
})();
// The first-sheet view belongs to the project, even if its owning setup changes.
const ProjectImage=(()=>{
 const meta=document.querySelector('meta[name="nx-project-image-scale"]');
 let scale=100;
 function set(value){const n=Number(value);scale=Number.isFinite(n)?(ProjectModel.active?Math.max(1,Math.min(10000,Math.round(n))):Math.max(60,Math.min(300,Math.round(n)))):100;if(meta)meta.content=String(scale);}
 function apply(page){
  if(ProjectModel.layout(page))return;
  const header=page.querySelector('.continuation-header.no-note');if(!header)return;
  const img=header.querySelector('.project-isometry img');
  const model=ProjectModel.active&&header.querySelector('.project-model');
  const sourceWidth=model?ProjectModel.aspect():Number(img?.getAttribute('width'))||img?.naturalWidth||44,
   sourceHeight=model?1:Number(img?.getAttribute('height'))||img?.naturalHeight||32;
  // A tight rectangle with the actual picture ratio, within the same page budget.
  const factor=scale/100*Math.min(44/sourceWidth,32/sourceHeight),
   width=sourceWidth*factor,height=sourceHeight*factor;
  // Preserve the left tables' height budget when a wide, shallow picture
  // narrows their columns; its own rectangle still follows its exact ratio.
  const extra=Math.max(0,32*scale/100-39);
  const sizes={'image-width':width,'image-height':height,'header-height':39+extra,
   'meta-height':14+extra*.4,'legend-height':20.85+extra*.6,'label-width':Math.min(30,Math.max(28,(194-width-4)*.4))};
  for(const [name,value] of Object.entries(sizes))header.style.setProperty('--project-'+name,value+'mm');
 }
 set(meta?.content||100);const initial=Number(meta?.dataset.initialScale)||scale;if(meta)meta.dataset.initialScale=String(initial);
 return{set,apply,value:()=>scale,initial:()=>initial};
})();

ProjectModel.adoptScale();
ProjectModel.captureInitial();
let editors=[];
let paperScreenScale=1;
document.documentElement.style.setProperty('--paper-screen-scale','1');
function screenPaperScale(node){
 const page=node?.closest?.('.page');
 return !window.matchMedia('print').matches&&page?.parentElement?.classList.contains('setup-pages')?paperScreenScale:1;
}
function fitScreenSheets(){
 if(window.matchMedia('print').matches)return;
 const strip=document.getElementById('setup-documents'),panels=[...strip.querySelectorAll('.setup-pages>.page>.page-controls')];
 const panelHeight=Math.max(0,...panels.map(p=>p.getBoundingClientRect().height/paperScreenScale));
 const top=strip.getBoundingClientRect().top+window.scrollY,paperHeight=297*96/25.4;
 const before=paperScreenScale;
 const available=Math.max(40,innerHeight-top-38);
 paperScreenScale=Math.max(.03,available/(paperHeight+panelHeight));
 document.documentElement.style.setProperty('--paper-screen-scale',String(paperScreenScale));
 if(Math.abs(before-paperScreenScale)>.0001)ProjectModel.queue();
}
// A double-click always enables the checkbox, including when it started checked.
// Keep the native single-click and keyboard behavior of every HTML label.
document.addEventListener('dblclick',event=>{
 const label=event.target.closest?.('label'),input=label?.control||label?.querySelector('input[type="checkbox"]');
 if(!input?.matches('input[type="checkbox"]')||input.disabled||event.target===input)return;
 event.preventDefault();if(!input.checked){input.checked=true;input.dispatchEvent(new Event('change',{bubbles:true}));}
});

function placeProjectTools(){
 const owners=editors.filter(e=>e.flow.hasCatalog());
 if(owners.length!==1||!editors.length)return;
 const target=editors[0],owner=owners[0];
 if(owner!==target){target.flow.setCatalog(owner.flow.catalogContent());owner.flow.setCatalog();}
 document.querySelector('meta[name="nx-project-tools-position"]').content='first';
}
function saveDraft(){try{localStorage.setItem(draftKey,JSON.stringify({options:ColumnOptions.state(),projectImageScale:ProjectImage.value(),projectModel:ProjectModel.state(),setups:Object.fromEntries(editors.map(e=>[e.id,e.state()]))}));}catch(e){}}
function markChanged(){if(CardHistory?.restoring())return;CardHistory?.record();changed=true;editRevision++;saveDraft();queueLocalSave();}

// Session-only history stores editable state, not whole documents or mesh data.
// Immutable image strings are shared across steps; deleted original views remain
// in their setup's cell pool until the document closes.
function createCardHistory(){
 const steps=[],limit=100;let current=null,busy=false,group=null,pointer=null,key=null,typing=null,wheel=null,wheelTimer=0;
 const clone=value=>Array.isArray(value)?value.map(clone):value&&typeof value==='object'?Object.fromEntries(Object.entries(value).map(([k,v])=>[k,clone(v)])):value;
 function same(a,b){
  if(a===b)return true;if(!a||!b||typeof a!=='object'||typeof b!=='object'||Array.isArray(a)!==Array.isArray(b))return false;
  const keys=Object.keys(a);return keys.length===Object.keys(b).length&&keys.every(k=>Object.prototype.hasOwnProperty.call(b,k)&&same(a[k],b[k]));
 }
 function capture(){return clone({options:ColumnOptions.state(),projectImageScale:ProjectImage.value(),projectModel:ProjectModel.state(),setups:Object.fromEntries(editors.map(e=>[e.id,e.state()]))});}
 function sync(){document.querySelectorAll('#undo,[data-editor-id="cell-undo"]').forEach(button=>{button.disabled=busy||!steps.length;button.title='Отменить последнее действие (Ctrl+Z)';});}
 function endGroup(){group=pointer=key=typing=wheel=null;clearTimeout(wheelTimer);}
 function editable(target){return target instanceof Element&&(target.isContentEditable||!!target.closest('textarea,input:not([type]),input[type="text"],input[type="search"],input[type="number"]'));}
 function targetKey(target){
  const setup=target.closest?.('.setup-document')?.dataset.setupId||'project',page=target.closest?.('.page')?.dataset.pageKey||'';
  return [setup,page,target.dataset?.field||target.dataset?.scaleTarget||target.dataset?.editorId||target.closest?.('[data-image]')?.dataset.image||target.tagName].join('|');
 }
 function record(){
  if(busy)return;
  const next=capture();if(current===null){current=next;return;}if(same(current,next))return;
  const previous=steps.at(-1);
  if(group&&previous?.group===group){previous.after=next;if(same(previous.before,next))steps.pop();}
  else{steps.push({before:current,after:next,group});if(steps.length>limit)steps.shift();}
  current=next;sync();
 }
 function undo(){
  if(busy||!steps.length||composing)return;
  const entry=steps.pop(),scroll=[window.scrollX,window.scrollY];busy=true;endGroup();
  try{
   PercentInput.cancel();ProjectModel.cancel();editors.forEach(e=>e.cancel());
   ColumnOptions.set(entry.before.options);ProjectImage.set(entry.before.projectImageScale);ProjectModel.restore(entry.before.projectModel);
   editors.forEach(e=>e.restore(clone(entry.before.setups[e.id]),true));paginate();ProjectModel.queue(true);
   current=capture();
  }finally{busy=false;sync();}
  // Undo is a new edit for autosave/revision checks, but not a new undo step.
  changed=true;editRevision++;saveDraft();queueLocalSave();window.scrollTo(...scroll);
 }
 document.addEventListener('pointerdown',event=>{endGroup();current=capture();pointer=event.pointerId;group={};},true);
 for(const type of ['pointerup','pointercancel'])document.addEventListener(type,event=>{
  if(pointer!==event.pointerId)return;const token=group;queueMicrotask(()=>{if(group===token)endGroup();});
 },true);
 document.addEventListener('beforeinput',event=>{
  if(!editable(event.target))return;const next=targetKey(event.target);
  if(typing!==next){endGroup();current=capture();typing=next;group={};}
 },true);
 document.addEventListener('focusout',()=>{if(typing)endGroup();},true);
 document.addEventListener('keydown',event=>{
  if((event.ctrlKey||event.metaKey)&&!event.shiftKey&&!event.altKey&&(event.code==='KeyZ'||event.key.toLowerCase()==='z')){
   if(editable(event.target)||event.isComposing)return;
   event.preventDefault();event.stopImmediatePropagation();undo();return;
  }
  if(editable(event.target))return;
  const next=targetKey(event.target)+'|'+event.code;
  if(!event.repeat||key!==next){endGroup();current=capture();key=next;group={};}
 },true);
 document.addEventListener('keyup',()=>{if(key)endGroup();},true);
 document.addEventListener('wheel',event=>{
  const next=targetKey(event.target);if(wheel!==next){endGroup();current=capture();wheel=next;group={};}
  clearTimeout(wheelTimer);wheelTimer=setTimeout(endGroup,350);
 },{capture:true,passive:true});
 document.addEventListener('click',event=>{if(event.target.closest?.('#undo'))undo();});
 window.addEventListener('blur',endGroup);
 current=capture();sync();return{record,undo,sync,restoring:()=>busy,refresh:()=>{if(!steps.length&&!group)current=capture();}};
}
function queueLocalSave(delay=900){
 if(!LOCAL_SESSION||!changed||autosaveBlocked)return;
 clearTimeout(autosaveTimer);autosaveTimer=setTimeout(()=>saveLocalCard(true),delay);
}
// An autosave must not commit/cancel a drag preview or interrupt IME entry.
document.addEventListener('pointerdown',event=>activePointers.add(event.pointerId),true);
for(const name of ['pointerup','pointercancel'])document.addEventListener(name,event=>{activePointers.delete(event.pointerId);queueLocalSave();},true);
document.addEventListener('compositionstart',()=>{composing=true;});
document.addEventListener('compositionend',()=>{composing=false;queueLocalSave();});
window.addEventListener('blur',()=>{activePointers.clear();composing=false;queueLocalSave();});
document.addEventListener('visibilitychange',()=>{if(document.hidden&&LOCAL_SESSION&&changed&&!autosaveBlocked)saveLocalCard(true);});
function renumber(){
 const pages=[...document.querySelectorAll('.setup-pages>.page')];pages.forEach((p,i)=>{p.querySelector('.page-number').textContent=i+1;p.querySelector('.page-count').textContent=pages.length;});
 // Controls travel with their own paper. Reserve enough shared space above
 // the strip so panels never overlap the project toolbar or stagger sheets.
 if(!window.matchMedia('print').matches){
  fitScreenSheets();
  const height=Math.max(0,...[...document.querySelectorAll('.setup-pages>.page>.page-controls')].map(p=>p.getBoundingClientRect().height));
  document.getElementById('setup-documents').style.setProperty('--setup-panel-space',Math.ceil(height+12)+'px');
 }
 if(!window.matchMedia('print').matches)document.querySelectorAll('.setup-document').forEach(scope=>{
  const row=scope.querySelector('.setup-pages'),cover=row?.querySelector(':scope>.cover-page');
  const offset=cover?Math.max(0,cover.getBoundingClientRect().left-row.getBoundingClientRect().left):0;
  scope.style.setProperty('--setup-start-offset',offset+'px');
 });
}
window.addEventListener('resize',renumber);
function paginate(){editors.forEach(e=>e.paginate());renumber();}
function validFields(){editors.forEach(e=>e.fitFields());const field=document.querySelector('.field.overflowing');if(field){field.focus();return false;}return true;}
async function printCard(){if(!validFields())return;paginate();try{editors.forEach(e=>e.prepare());ProjectModel.prepare();await Promise.all(editors.map(e=>e.waitImages()));window.print();}catch(e){}}
function serializedCard(revision){
 editors.forEach(e=>e.prepare());ProjectModel.prepare();
 const copy=document.documentElement.cloneNode(true),sections=[...copy.querySelectorAll('.setup-document')];
 editors.forEach((editor,i)=>sections[i].replaceWith(editor.serialize()));
 copy.querySelectorAll('.project-model canvas').forEach(c=>c.remove());
 copy.querySelectorAll('.project-model').forEach(n=>n.classList.remove('model-ready'));
 copy.querySelector('meta[name="nx-card-revision"]').content=revision;copy.querySelector('#nx-local-session')?.remove();
 copy.querySelector('#save-toast')?.remove();
 copy.querySelector('#save').disabled=false;copy.querySelector('#save').textContent='Сохранить карту';copy.querySelector('#save-copy').hidden=true;
 copy.querySelector('#undo').disabled=true;
 copy.querySelectorAll('#setup-jump option').forEach(o=>o.removeAttribute('selected'));
 return '<!doctype html>\n'+copy.outerHTML;
}
function newRevision(){return [...crypto.getRandomValues(new Uint32Array(4))].map(n=>n.toString(16).padStart(8,'0')).join('');}
function downloadCopy(){
 paginate();const blob=new Blob([serializedCard(expectedRevision)],{type:'text/html;charset=utf-8'}),url=URL.createObjectURL(blob);
 const link=document.createElement('a');link.href=url;link.download=DOC.filename;document.body.append(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),30000);
}
async function selectSaveFile(){
 if(fileHandle){
  if(await fileHandle.queryPermission({mode:'readwrite'})!=='granted'&&await fileHandle.requestPermission({mode:'readwrite'})!=='granted')throw new DOMException('Нет разрешения на запись.','NotAllowedError');
  return fileHandle;
 }
 const [handle]=await window.showOpenFilePicker({id:'nx-setup-card',multiple:false,types:[{description:'Исходная карта наладки HTML',accept:{'text/html':['.html','.htm']}}],excludeAcceptAllOption:true});
 if(!handle)throw new DOMException('Файл не выбран.','AbortError');
 return handle;
}
async function saveLocalCard(automatic=false){
 if(!LOCAL_SESSION||saving||(!changed&&!pendingLocalSave&&automatic))return;
 if(automatic&&(activePointers.size||composing||window.matchMedia('print').matches||
    [...document.querySelectorAll('.cell-grid-picker')].some(node=>node.getClientRects().length))){queueLocalSave();return;}
 if(!automatic)SaveNotice.request(editRevision);
 clearTimeout(autosaveTimer);saving=true;
 const button=document.getElementById('save');button.disabled=true;button.textContent='Сохранение…';
 const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),30000);
 try{
  if(!pendingLocalSave){
   if(!automatic)paginate();
   const next=newRevision(),content=serializedCard(next);
   pendingLocalSave={previous:expectedRevision,next,content,edit:editRevision};
  }
  const pending=pendingLocalSave;
  const response=await fetch(LOCAL_SESSION.save,{method:'POST',mode:'same-origin',credentials:'omit',cache:'no-store',redirect:'error',signal:controller.signal,
   headers:{'Content-Type':'text/html; charset=utf-8','X-NX-Card-Key':LOCAL_SESSION.key,'X-NX-Card-Revision':pending.previous,'X-NX-Card-Next':pending.next},body:pending.content});
  if(!response.ok){const error=new Error('Не удалось сохранить HTML.');error.status=response.status;throw error;}
  const result=await response.json();
  if(result.id!==DOC.id||result.revision!==pending.next)throw new Error('Не подтверждена запись HTML.');
  acceptSavedRevision(pending.next);pendingLocalSave=null;autosaveBlocked=false;autosaveFailures=0;
  if(editRevision===pending.edit){changed=false;try{localStorage.removeItem(draftKey);}catch(e){}}
  else saveDraft();
  document.getElementById('save-copy').hidden=true;button.textContent='Сохранить карту';
  SaveNotice.saved(pending.edit);
 }catch(e){
  SaveNotice.cancel();
  saveDraft();autosaveFailures++;autosaveBlocked=[400,403,404,409,413,422].includes(e.status);
  // No popups or warning banners. A failed write keeps the draft and exposes
  // the existing explicit download action; it never falls back to a picker.
  document.getElementById('save-copy').hidden=false;button.textContent='Повторить сохранение';
 }finally{
  clearTimeout(timeout);saving=false;button.disabled=false;
  if(changed||pendingLocalSave)queueLocalSave(autosaveFailures?Math.min(30000,5000*autosaveFailures):900);
 }
}
async function saveCard(){
 if(LOCAL_SESSION){await saveLocalCard();return;}
 if(saving)return;
 SaveNotice.request(editRevision);
 saving=true;const button=document.getElementById('save');button.disabled=true;let writable=null;
 try{
  if(typeof window.showOpenFilePicker!=='function'){
   paginate();const blob=new Blob([serializedCard(expectedRevision)],{type:'text/html;charset=utf-8'}),url=URL.createObjectURL(blob);
   const link=document.createElement('a');link.href=url;link.download=DOC.filename;document.body.append(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),30000);
   SaveNotice.cancel(); // A download request cannot confirm completion on disk.
   return;
  }
  // Выбор файла запускается сразу по нажатию, пока действует разрешение жеста пользователя.
  const handle=await selectSaveFile();
  writable=await handle.createWritable({mode:'exclusive'});
  const current=await handle.getFile();
  if(!current.size)throw new Error('Выберите существующий HTML этой карты; пустой файл не подходит.');
  if(current.size){
   const existing=new DOMParser().parseFromString(await current.text(),'text/html');
   if(existing.querySelector('meta[name="nx-card-id"]')?.content!==DOC.id){
    fileHandle=null;handleStore('delete');throw new Error('Выбран другой файл или результат нового экспорта NX. Выберите исходный HTML этой карты.');
   }
   if(existing.querySelector('meta[name="nx-card-revision"]')?.content!==expectedRevision)throw new Error('Файл изменился после открытия карты. Проверьте другую вкладку. Правки остались в этой вкладке.');
  }
  paginate();const revision=editRevision,nextRevision=newRevision(),content=serializedCard(nextRevision);
  await writable.write(content);await writable.close();writable=null;
  fileHandle=handle;acceptSavedRevision(nextRevision);
  if(editRevision===revision){changed=false;try{localStorage.removeItem(draftKey);}catch(e){}}
  else{saveDraft();}
  document.getElementById('save-copy').hidden=true;handleStore('put',handle);
  SaveNotice.saved(revision);
 }catch(e){
  SaveNotice.cancel();
  if(writable){try{await writable.abort();}catch(ignore){}}
  document.getElementById('save-copy').hidden=false;
  if(e.name==='NotFoundError'){fileHandle=null;handleStore('delete');}
 }finally{saving=false;button.disabled=false;}
}

function createSetupEditor(scope){
 const prefix=scope.dataset.setupId+'-',local=id=>scope.querySelector('[data-editor-id="'+id+'"]');
 let initialView=null,initialNode=local('initial-view');
 try{const saved=JSON.parse(initialNode?.textContent||'null');if(saved?.schema===1&&saved.state)initialView=saved;}catch(e){}
 function writeInitial(node,deletedCells={}){node.textContent=JSON.stringify({...initialView,deletedCells}).replace(/</g,'\\u003c');}
 function captureInitial(){
  if(initialView)return;
  initialView={schema:1,state:state()};
  if(!initialNode){initialNode=document.createElement('script');initialNode.type='application/json';initialNode.dataset.editorId='initial-view';scope.append(initialNode);}
  writeInitial(initialNode);
 }
 local('cell-undo').textContent='Отменить действие';
 local('operations-to-cover').textContent='На первый лист ←';local('operations-to-next').textContent='На следующие листы →';
 scope.querySelectorAll('input[type=range]').forEach(input=>{if(input.dataset.logScale!=='true')input.step='1';const label=input.closest('label');if(label){label.title=SLIDER_RESET_HINT;label.classList.add('slider-label');}});
// Delegate to the setup so newly created/reopened sheet controls also reset.
// Use the normal input handlers: update the view, pagination and saved state.
scope.addEventListener('dblclick',event=>{
 const label=event.target.closest('label'),input=label?.querySelector('input[type="range"]');
 if(!input||input.disabled||event.target.closest('input,select,button,output,a,[contenteditable]'))return;
 event.preventDefault();input.value=input.dataset.logScale==='true'?'200':'100';
 input.dispatchEvent(new Event('input',{bubbles:true}));
 input.dispatchEvent(new Event('change',{bubbles:true}));
});
function values(){const result={};[...scope.querySelectorAll('.cover-page [data-field]'),...scope.querySelectorAll('[data-field]')].forEach(el=>{if(!(el.dataset.field in result))result[el.dataset.field]=fieldValue(el);});return result;}
function applyValues(data){scope.querySelectorAll('[data-field]').forEach(el=>{if(Object.prototype.hasOwnProperty.call(data,el.dataset.field)&&fieldValue(el)!==String(data[el.dataset.field]??''))setFieldValue(el,data[el.dataset.field]);});}
function fitFields(){const resized=NoteLayout.refresh();scope.querySelectorAll('.field').forEach(el=>{el.style.fontSize='';el.classList.remove('overflowing');if(el.closest('.note-row')||!el.getClientRects().length)return;let size=parseFloat(getComputedStyle(el).fontSize);while((el.scrollHeight>el.clientHeight+1||el.scrollWidth>el.clientWidth+1)&&size>9){size-=.5;el.style.fontSize=size+'px';}if(el.scrollHeight>el.clientHeight+1||el.scrollWidth>el.clientWidth+1)el.classList.add('overflowing');});return resized;}
// Manual note heights belong to physical sheets; automatic height is always
// recomputed from text at its normal font size, including in saved older cards.
const NoteLayout=(()=>{
 const mm=96/25.4,base=14.2,measured=new WeakMap();let heights=Object.create(null),drag=null,frame=0;
 function restore(data){
  heights=Object.create(null);
  if(data&&typeof data==='object'&&!Array.isArray(data))for(const [key,value] of Object.entries(data))
   if(/^(cover|operations-[1-9][0-9]*)$/.test(key)&&typeof value==='number'&&Number.isFinite(value))heights[key]=Math.max(base,Math.min(270,value));
 }
 try{restore(JSON.parse(scope.dataset.noteHeights||'{}'));}catch(e){}
 function value(key){return heights[key]??base;}
 function apply(page,controls=true){
  const row=page.querySelector('.note-row');if(!row||!page.isConnected)return null;
  const header=row.closest('header'),scale=screenPaperScale(page),key=page.dataset.pageKey||'cover';
  row.style.height=base+'mm';
  row.querySelectorAll('.field').forEach(field=>{field.style.fontSize='';field.style.removeProperty('height');field.classList.remove('overflowing');});
  const minimum=Math.max(base,...[...row.querySelectorAll('.field')].map(n=>n.getBoundingClientRect().height/scale/mm+.25));
  const before=row.getBoundingClientRect(),head=header.getBoundingClientRect(),footer=page.querySelector('.page-footer').getBoundingClientRect();
  const reserve=page.classList.contains('cover-page')?GalleryLayout.minimumHeight():50;
  const free=(footer.top-head.bottom)/scale/mm-(parseFloat(getComputedStyle(header).marginBottom)||0)/mm;
  const maximum=Math.max(minimum,before.height/scale/mm+free-reserve);
  row.style.height=Math.max(minimum,Math.min(value(key),maximum))+'mm';
  const rect=row.getBoundingClientRect(),height=rect.height/scale/mm;
  if(controls){
   let grip=header.querySelector(':scope>.note-height-grip');
   if(!grip){grip=document.createElement('span');grip.className='note-height-grip';grip.tabIndex=0;grip.setAttribute('role','separator');grip.setAttribute('aria-orientation','horizontal');grip.setAttribute('aria-label','Изменить высоту примечания');header.append(grip);}
   Object.assign(grip.style,{left:(rect.left-head.left)/scale+'px',top:(rect.bottom-head.top)/scale+'px',width:rect.width/scale+'px'});
   grip.setAttribute('aria-valuemin',minimum.toFixed(1));grip.setAttribute('aria-valuemax',maximum.toFixed(1));grip.setAttribute('aria-valuenow',height.toFixed(1));grip.setAttribute('aria-valuetext',height.toFixed(1)+' мм');
   grip.title='Потяните нижнюю границу примечания. Стрелки — 1 мм; Shift — 5 мм. Высота не меньше текста.';
  }
  const headerHeight=header.getBoundingClientRect().height/scale,previous=measured.get(page);measured.set(page,headerHeight);
  return{minimum,maximum,height,changed:previous!==undefined&&Math.abs(previous-headerHeight)>.25};
 }
 function refresh(){let changed=false;scope.querySelectorAll('.setup-pages>.page').forEach(page=>{if(apply(page)?.changed)changed=true;});return changed;}
 function reflow(){
  cancelAnimationFrame(frame);frame=0;
  const active=document.activeElement,key=active?.closest('.page')?.dataset.pageKey,name=active?.dataset.field;
  const selection=getSelection(),range=selection?.rangeCount?selection.getRangeAt(0):null;
  let offset=null;
  if(active?.closest('.note-row')&&range&&active.contains(range.endContainer)){const before=range.cloneRange();before.selectNodeContents(active);before.setEnd(range.endContainer,range.endOffset);offset=before.toString().length;}
  if(drag)CardFlow.restore(drag.operations,drag.placement,drag.limit);
  CardFlow.reflow();
  // Most pages are retained. If the edited continuation disappeared after
  // deleting text, keep typing in the surviving copy of the same note.
  if(offset!==null&&!active.isConnected){
   const page=scope.querySelector('.setup-pages>[data-page-key="'+key+'"]')||scope.querySelector('.cover-page'),field=page?.querySelector('[data-field="'+name+'"]');
   if(field){field.focus({preventScroll:true});const walker=document.createTreeWalker(field,NodeFilter.SHOW_TEXT);let node;
    while((node=walker.nextNode())){if(offset<=node.length)break;offset-=node.length;}
    const caret=document.createRange();if(node)caret.setStart(node,offset);else{caret.selectNodeContents(field);caret.collapse(false);}caret.collapse(true);selection.removeAllRanges();selection.addRange(caret);
   }
  }
 }
 function queue(){if(!frame)frame=requestAnimationFrame(reflow);}
 function finish(cancel=false){
  const old=drag;if(!old)return;drag=null;scope.classList.remove('note-sizing');old.grip.classList.remove('dragging');
  try{scope.releasePointerCapture(old.id);}catch(e){}
  if(cancel)heights=old.before;
  if(old.moved){CardFlow.restore(old.operations,old.placement,old.limit);reflow();if(!cancel)markChanged();}
 }
 scope.addEventListener('pointerdown',event=>{
  const grip=event.target.closest('.note-height-grip');if(!grip||event.button!==0||event.isPrimary===false)return;
  event.preventDefault();PhotoPan.finish();GalleryLayout.closePicker();GalleryLayout.finish(true);finishColumnDrag();CardFlow.finish(true);finish(true);
  const page=grip.closest('.page'),size=apply(page);grip.focus({preventScroll:true});
  drag={id:event.pointerId,grip,key:page.dataset.pageKey,y:event.clientY,scale:screenPaperScale(page),size,before:{...heights},operations:CardFlow.values(),placement:CardFlow.mode(),limit:CardFlow.limit(),moved:false};
  scope.classList.add('note-sizing');grip.classList.add('dragging');try{scope.setPointerCapture(event.pointerId);}catch(e){}
 });
 scope.addEventListener('pointermove',event=>{
  if(drag?.id!==event.pointerId)return;event.preventDefault();
  const delta=(event.clientY-drag.y)/drag.scale/mm;if(Math.abs(delta)<.1&&!drag.moved)return;
  heights[drag.key]=Math.max(drag.size.minimum,Math.min(drag.size.maximum,drag.size.height+delta));drag.moved=true;queue();
 },{passive:false});
 scope.addEventListener('pointerup',event=>{if(drag?.id===event.pointerId)finish();});
 scope.addEventListener('pointercancel',event=>{if(drag?.id===event.pointerId)finish(true);});
 scope.addEventListener('lostpointercapture',event=>{if(drag?.id===event.pointerId)finish(true);});
 scope.addEventListener('keydown',event=>{
  if(event.key==='Escape'&&drag){event.preventDefault();finish(true);return;}
  const grip=event.target.closest('.note-height-grip');if(!grip||!['ArrowUp','ArrowDown'].includes(event.key))return;
  event.preventDefault();finish();const page=grip.closest('.page'),size=apply(page);
  heights[page.dataset.pageKey]=Math.max(size.minimum,Math.min(size.maximum,size.height+(event.key==='ArrowDown'?1:-1)*(event.shiftKey?5:1)));reflow();markChanged();
 });
 window.addEventListener('blur',()=>finish(true));
 return{apply,refresh,reflow,queue,finish,value,restore,reset:(key,saved)=>{delete heights[key];if(Number.isFinite(saved?.noteHeights?.[key]))heights[key]=saved.noteHeights[key];},state:()=>({...drag?.before||heights}),
  save:copy=>{copy.dataset.noteHeights=JSON.stringify(heights);copy.classList.remove('note-sizing');copy.querySelectorAll('.note-height-grip').forEach(n=>n.classList.remove('dragging'));}};
})();
// Editable datums: suggestions never overwrite unfinished or custom text.
const DatumEditor=(()=>{
 let input=null,popup=null,items=[],active=-1;
 const fold=value=>value.trim().toLocaleLowerCase('ru-RU');
 function close(){
  if(input){input.setAttribute('aria-expanded','false');input.removeAttribute('aria-activedescendant');input.removeAttribute('aria-controls');}
  popup?.remove();input=null;popup=null;items=[];active=-1;
 }
 function place(){
  if(!popup||!input?.isConnected){close();return;}
  const r=input.closest('.datum-editor').getBoundingClientRect(),gap=8;
  const width=Math.min(Math.max(200,r.width),window.innerWidth-2*gap);
  popup.style.width=width+'px';popup.style.left=Math.max(gap,Math.min(r.left,window.innerWidth-width-gap))+'px';
  const below=window.innerHeight-r.bottom-gap,above=r.top-gap;
  const up=below<Math.min(popup.scrollHeight,224)&&above>below;
  popup.style.maxHeight=Math.max(28,Math.min(224,up?above:below))+'px';
  popup.style.top=(up?Math.max(gap,r.top-popup.offsetHeight):r.bottom)+'px';
 }
 function select(index){
  active=index;
  [...popup.children].forEach((node,i)=>node.setAttribute('aria-selected',String(i===active)));
  const option=popup.children[active];
  if(option){input.setAttribute('aria-activedescendant',option.id);option.scrollIntoView({block:'nearest'});}
 }
 function open(field,filtered){
  close();const query=fold(field.value),choices=JSON.parse(field.closest('.datum-editor').dataset.choices).map(value=>value.toUpperCase());
  const matches=filtered?choices.filter(value=>fold(value).startsWith(query)):choices;
  if(!matches.length||(filtered&&!query))return;
  input=field;items=matches;popup=document.createElement('div');popup.id=prefix+'datum-suggestions';popup.dataset.editorId='datum-suggestions';popup.className='datum-suggestions';
  popup.setAttribute('role','listbox');popup.setAttribute('aria-label',field.getAttribute('aria-label'));
  items.forEach((value,i)=>{const option=document.createElement('div');option.id=prefix+'datum-option-'+i;option.className='datum-option';option.setAttribute('role','option');option.dataset.index=String(i);option.textContent=value;popup.append(option);});
  scope.append(popup);input.setAttribute('aria-expanded','true');input.setAttribute('aria-controls',popup.id);
  place();select(Math.max(0,items.findIndex(value=>fold(value)===query)));
 }
 function accept(){
  if(!input||active<0)return;const field=input,value=items[active];close();
  setFieldValue(field,value);field.dispatchEvent(new Event('input',{bubbles:true}));close();
  field.focus({preventScroll:true});field.setSelectionRange(value.length,value.length);
 }
 scope.addEventListener('input',event=>{if(event.target.matches('.datum-input')){if(event.isComposing)close();else open(event.target,true);}});
 scope.addEventListener('compositionstart',event=>{if(event.target.matches('.datum-input'))close();});
 scope.addEventListener('compositionend',event=>{if(event.target.matches('.datum-input')){syncField(event);open(event.target,true);}});
 scope.addEventListener('keydown',event=>{
  const field=event.target;if(!field.matches('.datum-input')||event.isComposing||event.ctrlKey||event.metaKey)return;
  if(event.key==='ArrowDown'||event.key==='ArrowUp'){
   event.preventDefault();event.stopImmediatePropagation();
   if(input!==field||!popup)open(field,false);else select((active+(event.key==='ArrowDown'?1:-1)+items.length)%items.length);
  }else if(event.key==='Enter'&&input===field&&popup){event.preventDefault();event.stopImmediatePropagation();accept();}
  else if(event.key==='Escape'&&popup){event.preventDefault();event.stopImmediatePropagation();close();}
  else if(event.key==='Tab')close();
 });
 scope.addEventListener('pointerdown',event=>{
  const toggle=event.target.closest('.datum-toggle'),option=event.target.closest('.datum-option');
  if(toggle){event.preventDefault();const field=toggle.closest('.datum-editor').querySelector('.datum-input'),wasOpen=input===field&&popup;field.focus({preventScroll:true});if(wasOpen)close();else open(field,false);}
  else if(option&&popup?.contains(option)){event.preventDefault();select(Number(option.dataset.index));accept();}
  else if(!event.target.closest('.datum-editor,.datum-suggestions'))close();
 });
 scope.addEventListener('pointermove',event=>{const option=event.target.closest('.datum-option');if(option&&popup?.contains(option)&&Number(option.dataset.index)!==active)select(Number(option.dataset.index));});
 scope.addEventListener('focusout',event=>{if(event.target===input)close();});
 window.addEventListener('resize',close);window.addEventListener('blur',close);
 scope.addEventListener('scroll',event=>{if(popup&&!popup.contains(event.target))close();},true);
 window.addEventListener('beforeprint',close);
 return{close,position:place,cleanClone:copy=>{copy.querySelector('[data-editor-id="datum-suggestions"]')?.remove();copy.querySelectorAll('.datum-input').forEach(field=>{field.setAttribute('aria-expanded','false');field.removeAttribute('aria-activedescendant');field.removeAttribute('aria-controls');});}};
})();
function repeatGroup(row){
 const copy=row.cloneNode(true);copy.dataset.repeat='true';copy.querySelector('.tree-name').append(document.createTextNode(' · продолжение'));
 const name=copy.cells[0].title+' · продолжение';copy.cells[0].title=name;copy.cells[0].querySelector('.cell-text').title=name;
 const cell=copy.querySelector('[data-column-id="time"]'),time=cell.querySelector('.cell-text');time.textContent='—';time.title='';cell.title='';return copy;
}
function operationPagePlan(rows,heights,repeatHeights,firstBudget,continuationBudget){
 const count=rows.length,ends=rows.map((_,i)=>i+1),parents=[],stack=[],sums=[0];
 rows.forEach((row,i)=>{const depth=Number(row.dataset.depth||0);while(stack.length&&Number(rows[stack.at(-1)].dataset.depth||0)>=depth)ends[stack.pop()]=i;parents.push([...stack]);if(row.dataset.kind==='group')stack.push(i);sums.push(sums.at(-1)+heights[i]);});stack.forEach(i=>ends[i]=count);
 const pages=[];let current=[],used=0,budget=firstBudget,i=0;
 while(i<count){const contextHeight=parents[i].reduce((sum,j)=>sum+repeatHeights[j],0);let end=i+1;
  if(rows[i].dataset.kind==='group'){let j=i;while(j<ends[i]&&rows[j].dataset.kind==='group'){const nested=rows.slice(j+1,ends[j]).some(row=>row.dataset.kind==='group');if(!nested&&contextHeight+sums[ends[j]]-sums[i]<=continuationBudget){end=ends[j];break;}j++;end=Math.min(j+1,ends[i]);}}
  const height=sums[end]-sums[i];if(used+height>budget&&(!pages.length||current.some(item=>!item[1]))){pages.push(current);current=parents[i].map(j=>[j,true]);used=contextHeight;budget=continuationBudget;}
  for(let j=i;j<end;j++)current.push([j,false]);used+=height;i=end;
 }if(current.length||!pages.length)pages.push(current);return pages;
}
function projectToolPlan(rows,capacity){
 const sizes=rows.map(()=>1),make=n=>operationPagePlan(rows,sizes,sizes,n,n).filter(c=>c.length);
 const plan=make(Math.max(2,Math.floor(capacity)));if(plan.length<=2)return plan.length?plan:[[]];
 // Balance an oversized list before shrinking; repeat Carrier context on the right.
 const stack=[];let best=null;
 rows.forEach((row,i)=>{
  const depth=Number(row.dataset.depth||0);while(stack.length&&Number(rows[stack.at(-1)].dataset.depth||0)>=depth)stack.pop();
  if(i&&rows[i-1].dataset.kind!=='group'){
   const score=Math.max(i,rows.length-i+stack.length);
   if(!best||score<best.score)best={score,index:i,parents:[...stack]};
  }
  if(row.dataset.kind==='group')stack.push(i);
 });
 if(!best)return[rows.map((_,i)=>[i,false])];
 return[rows.slice(0,best.index).map((_,i)=>[i,false]),[...best.parents.map(i=>[i,true]),...rows.slice(best.index).map((_,i)=>[i+best.index,false])]];
}
// Independent logical pixel widths: resizing a column moves every column to its right.
function readColumnWidths(data){
 const result=Object.create(null);
 if(!data||typeof data!=='object')return result;
 scope.querySelectorAll('table[data-fit-family]').forEach(table=>{
  const family=table.dataset.fitFamily,value=data[family],count=table.querySelectorAll('colgroup>col').length;let widths=Array.isArray(value)?value:value?.widths;
  if(family==='operations'&&Array.isArray(widths)){
   const schema=operationTableSchemas.get(table)||Number(table.dataset.opsSchema)||2;
   // Old status-column tables and new Zmin tables both contain 11 columns.
   // Use their source schema (or saved keys), never the count alone.
   if(!Array.isArray(value?.keys)&&schema===1&&widths.length===11)widths=widths.filter((_,i)=>i!==3);
   if(widths.length===10&&count===11){
    const ratio=Array.isArray(value)||value?.unit==='ratio',extra=ratio?7/194:7*96/25.4;
    const total=widths.reduce((a,b)=>a+b,0);widths=ratio?widths.map(n=>n/total*(1-extra)).concat(extra):widths.concat(extra);
   }
  }
  if(Array.isArray(widths)&&widths.length===count&&widths.every(n=>typeof n==='number'&&Number.isFinite(n)&&n>0)){
   const total=widths.reduce((a,b)=>a+b,0);
   if(Number.isFinite(total)){result[family]=Array.isArray(value)?{unit:'ratio',widths:widths.map(n=>n/total)}:{unit:value.unit==='ratio'?'ratio':'px',widths:widths.slice()};if(family==='operations')result[family].keys=OP_COLUMNS.map(c=>c[0]);}
  }
 });
 return result;
}
let columnWidths=Object.create(null),columnDrag=null;
try{columnWidths=readColumnWidths(JSON.parse(local('column-widths').textContent));}catch(e){}
const columnMetrics=new Map();
function columnLabel(table,index){return table.tHead.rows[0].cells[index].textContent.trim()||(index===2&&table.dataset.fitFamily==='operations'?'Смена инструмента':'Столбец '+(index+1));}
function ensureColumnHandles(table){
 const cells=[...table.tHead.rows[0].cells];
 cells.forEach((cell,index)=>{
  let grip=cell.querySelector('.column-grip');
  if(!grip){grip=document.createElement('span');grip.className='column-grip';grip.tabIndex=0;grip.setAttribute('role','separator');grip.setAttribute('aria-orientation','vertical');cell.append(grip);}
  grip.dataset.column=String(index);
  grip.setAttribute('aria-label','Ширина столбца «'+columnLabel(table,index)+'»');
 });
}
function syncColumnControls(){local('columns-auto').disabled=Object.keys(columnWidths).length===0;}
function safeManualWidths(metrics,saved){
 if(!saved||!Array.isArray(saved.widths)||saved.widths.length!==metrics.floor.length)return null;
 // A later font/row scale change must not rewrite a user's column widths.
 return saved.widths.map(n=>Math.max(1,Math.min(4000,n*(saved.unit==='ratio'?metrics.target:1))));
}
function tableViewport(table){
 const family=table.dataset.fitFamily;
 if(family==='operations')return table.closest('.rows-area');
 const cls=family==='cover-tools'?'tool-table-scroll':'catalog-scroll';
 let viewport=table.closest('.'+cls);
 if(!viewport){viewport=document.createElement('div');viewport.className=cls;table.before(viewport);viewport.append(table);}
 if(family==='cover-tools'||family==='project-tools'){
  const planeClass=family==='cover-tools'?'tool-table-plane':'project-tool-plane';
  if(!table.parentElement.classList.contains(planeClass)){const plane=document.createElement('div');plane.className=planeClass;table.before(plane);plane.append(table);}
 }
 return viewport;
}
function applyColumnLayout(family,metrics,widths){
 const font=metrics.base,visible=widths.map((w,i)=>metrics.shown[i]?w:0),total=visible.reduce((a,b)=>a+b,0);metrics.widths=widths.slice();metrics.font=font;
 const fit=Math.min(1,metrics.target/Math.max(1,total));metrics.widthScale=fit;
 metrics.layoutScale=(metrics.renderScale||1)*fit;
 const apply=table=>{
  const viewport=tableViewport(table);if(!viewport)return;
  table.style.width=total+'px';table.style.setProperty('--print-source-width',total+'px');
  // At extreme font scales or manual widths fit the entire table, keeping
  // complete values and the same layout on screen and on paper.
  if(family==='operations'){table.style.zoom=String(fit);table.style.setProperty('--print-table-scale',fit);}
  table.style.fontSize=font+'pt';table.dataset.widthMode=columnWidths[family]?'manual':'auto';
  [...table.querySelectorAll('colgroup>col')].forEach((col,i)=>{col.style.width=(total?visible[i]/total*100:0)+'%';});
  if(family==='cover-tools'||family==='project-tools'){
   const plane=viewport.querySelector(family==='cover-tools'?'.tool-table-plane':'.project-tool-plane'),scale=metrics.layoutScale;
   table.style.transform='scale('+scale+')';
   plane.style.width=total*scale+'px';plane.style.height=(table.offsetHeight*scale+1)+'px';
  }
  ensureColumnHandles(table);restoreCellText(table);alignNumericText(table);
  table.querySelectorAll('.column-grip').forEach(grip=>{
   const index=Number(grip.dataset.column),unit=25.4/96*metrics.layoutScale,mm=widths[index]*unit;
   grip.setAttribute('aria-valuenow',mm.toFixed(1));grip.setAttribute('aria-valuemin',(metrics.floor[index]*unit).toFixed(1));
   grip.setAttribute('aria-valuemax',(4000*unit).toFixed(1));
   grip.setAttribute('aria-valuetext',mm.toFixed(1)+' мм');
   grip.title=columnLabel(table,index)+': '+mm.toFixed(1)+' мм. Перетащите границу или используйте ← / →. Двойной щелчок — автоширина этой таблицы.';
  });
 };
 metrics.tables.forEach(apply);
 syncColumnControls();
}
function moveColumnBoundary(table,index,initial,delta){
 const family=table.dataset.fitFamily,metrics=columnMetrics.get(table);if(!metrics)return false;
 const minimum=metrics.floor[index],maximum=4000;
 if(maximum<minimum)return false;
 const next=initial.slice();next[index]=Math.max(minimum,Math.min(maximum,initial[index]+delta));
 if(Math.abs(next[index]-metrics.widths[index])<.001)return false;
 columnWidths[family]={unit:'px',widths:next.slice()};
 if(family==='operations')columnWidths[family].keys=OP_COLUMNS.map(c=>c[0]);
 for(const m of new Set(columnMetrics.values()))if(m.tables[0].dataset.fitFamily===family)applyColumnLayout(family,m,next);
 return true;
}
function showColumnSize(table,index){
 const metrics=columnMetrics.get(table);if(!metrics)return;
 local('column-size-status').textContent=columnLabel(table,index)+': '+(metrics.widths[index]*25.4/96*(metrics.layoutScale||1)).toFixed(1)+' мм';
}
function finishColumnDrag(cancel=false){
 const drag=columnDrag;if(!drag)return;
 columnDrag=null;scope.classList.remove('columns-dragging');drag.grip.classList.remove('dragging');
 try{drag.grip.releasePointerCapture(drag.pointerId);}catch(e){}
 if(cancel){
  if(drag.previous)columnWidths[drag.family]=drag.previous;else delete columnWidths[drag.family];
  fitDataTables();local('column-size-status').textContent='Изменение ширины отменено.';
 }else if(drag.moved){CardFlow.reflow();markChanged();}
}
function resetColumnWidths(family){
 finishColumnDrag();
 if(family){if(!columnWidths[family])return;delete columnWidths[family];}
 else{if(!Object.keys(columnWidths).length)return;columnWidths=Object.create(null);}
 CardFlow.reflow();markChanged();local('column-size-status').textContent=family?'Для этой таблицы восстановлена автоширина.':'Для всех таблиц восстановлена автоширина.';
}
scope.addEventListener('pointerdown',event=>{
 const grip=event.target.closest('.column-grip');if(!grip||event.button!==0||event.isPrimary===false||columnDrag)return;
 const table=grip.closest('table'),family=table.dataset.fitFamily;
 fitDataTables();const metrics=columnMetrics.get(table),rect=table.getBoundingClientRect();if(!metrics||!(rect.width>0))return;
 event.preventDefault();grip.focus({preventScroll:true});
 const viewport=tableViewport(table);
 columnDrag={grip,table,family,viewport,index:Number(grip.dataset.column),pointerId:event.pointerId,start:event.clientX,scroll:viewport?.scrollLeft||0,screenScale:screenPaperScale(table),scale:1/(metrics.layoutScale||1),initial:metrics.widths.slice(),previous:columnWidths[family]?JSON.parse(JSON.stringify(columnWidths[family])):null,moved:false};
 scope.classList.add('columns-dragging');grip.classList.add('dragging');
 try{grip.setPointerCapture(event.pointerId);}catch(e){}
 showColumnSize(table,columnDrag.index);
});
scope.addEventListener('pointermove',event=>{
 const drag=columnDrag;if(!drag||event.pointerId!==drag.pointerId)return;
 event.preventDefault();
 const delta=(event.clientX-drag.start)/drag.screenScale+(drag.viewport?.scrollLeft||0)-drag.scroll;
 if(moveColumnBoundary(drag.table,drag.index,drag.initial,delta*drag.scale)){drag.moved=true;showColumnSize(drag.table,drag.index);}
},{passive:false});
scope.addEventListener('pointerup',event=>{if(columnDrag?.pointerId===event.pointerId)finishColumnDrag();});
scope.addEventListener('pointercancel',event=>{if(columnDrag?.pointerId===event.pointerId)finishColumnDrag(true);});
window.addEventListener('blur',()=>finishColumnDrag());
scope.addEventListener('dblclick',event=>{const grip=event.target.closest('.column-grip');if(grip){event.preventDefault();resetColumnWidths(grip.closest('table').dataset.fitFamily);}});
scope.addEventListener('keydown',event=>{
 if(event.key==='Escape'&&columnDrag){event.preventDefault();finishColumnDrag(true);return;}
 const grip=event.target.closest('.column-grip');if(!grip||!['ArrowLeft','ArrowRight'].includes(event.key))return;
 event.preventDefault();finishColumnDrag();
 const table=grip.closest('table'),family=table.dataset.fitFamily,index=Number(grip.dataset.column);
 if(!columnMetrics.has(table))fitDataTables();const metrics=columnMetrics.get(table);if(!metrics)return;
 const delta=(event.key==='ArrowRight'?1:-1)*(event.shiftKey?5:1)*96/25.4/(metrics.layoutScale||1);
 if(moveColumnBoundary(table,index,metrics.widths,delta)){CardFlow.reflow();markChanged();showColumnSize(table,index);}
});
local('columns-auto').addEventListener('click',()=>resetColumnWidths());

function autoColumnWidths(natural,floor,flex,target){
 const widths=natural.map((n,i)=>Math.max(n,floor[i]));
 // All other columns keep their measured content width. The flexible name
 // column receives the remainder and may use ellipsis; never squeeze values.
 flex.forEach(i=>widths[i]=floor[i]);
 const slack=Math.max(0,target-widths.reduce((a,b)=>a+b,0)),recipients=flex.length?flex:widths.map((_,i)=>i).filter(i=>floor[i]>0||natural[i]>0);
 const weight=recipients.reduce((s,i)=>s+Math.max(natural[i],floor[i]),0);
 if(weight>0)recipients.forEach(i=>{widths[i]+=slack*Math.max(natural[i],floor[i])/weight;});
 return widths;
}

function fitAutoColumns(family,metrics,flex,hiddenWidths){
 const natural=metrics.natural.slice(),range=document.createRange();
 for(let pass=0;pass<4;pass++){
  const widths=autoColumnWidths(natural,metrics.floor,flex,metrics.target).map((n,i)=>metrics.shown[i]?n:Math.max(1,hiddenWidths[i]));
  applyColumnLayout(family,metrics,widths);
  if(pass===3)break;
  // At very small screen zooms the browser still paints a border at least
  // one pixel wide. Measure the final cells as well as the unscaled probe.
  const extra=natural.map(()=>0);
  for(const table of metrics.tables){
   if(table.hidden)continue;
   const scale=screenPaperScale(table)*metrics.layoutScale;
   for(const node of table.querySelectorAll('th .cell-text,td .cell-text')){
    const cell=node.closest('th,td'),i=cell.cellIndex;if(!metrics.shown[i]||flex.includes(i)||!node.textContent)continue;
    const bounds=node.getBoundingClientRect();if(!(bounds.width>0))continue;
    range.selectNodeContents(node);
    const overflow=Math.max(node.scrollWidth-node.clientWidth,(range.getBoundingClientRect().width-bounds.width)/scale);
    if(overflow>.01)extra[i]=Math.max(extra[i],overflow+1/scale);
   }
  }
  if(!extra.some(n=>n>0))break;
  extra.forEach((n,i)=>natural[i]+=n);
 }
}

const PageScale=(()=>{
 const old=local('row-scale');let fallback=old?Number(old.value)||100:100,scales=Object.create(null),wraps=Object.create(null),toolScale=100;
 function normalized(value){const n=Number(value);return Number.isFinite(n)?Math.max(60,Math.min(300,Math.round(n))):100;}
 function restore(data,legacy,tools,wrapData){
  if(legacy!=null)fallback=normalized(legacy);
  if(data&&typeof data==='object'&&!Array.isArray(data)){scales=Object.create(null);for(const [key,value] of Object.entries(data))if(/^(cover|catalog|operations-[1-9][0-9]*)$/.test(key))scales[key]=normalized(value);}
  // Older cards used the cover scale for both tables. Preserve its appearance
  // once, then keep the setup's tool list independent of the operations.
  toolScale=normalized(tools??value('cover'));
  if(wrapData&&typeof wrapData==='object'&&!Array.isArray(wrapData)){wraps=Object.create(null);for(const [key,value] of Object.entries(wrapData))if(/^(cover|operations-[1-9][0-9]*)$/.test(key)&&typeof value==='boolean')wraps[key]=value;}
 }
 try{restore(JSON.parse(scope.dataset.pageScales||'{}'),null,scope.dataset.coverToolScale,JSON.parse(scope.dataset.pageWraps||'{}'));}catch(e){toolScale=normalized(scope.dataset.coverToolScale??value('cover'));}
 if(old){old.closest('label')?.remove();local('row-scale-value')?.remove();scope.style.removeProperty('--data-scale');}
 function value(key){return scales[key]??fallback;}
 function wrapValue(key){return wraps[key]??ColumnOptions.wrap();}
 function applyWrap(table){table.classList.toggle('wrap-operation-names',wrapValue(table.closest('.page')?.dataset.pageKey||'cover'));}
 function factor(node){
  if(typeof node!=='string'&&node?.dataset.fitFamily==='cover-tools')return toolScale/100;
  return value(typeof node==='string'?node:(node?.closest('.page')?.dataset.pageKey||'cover'))/100;
 }
 function control(bar,target,label,value){
  let input=bar.querySelector('input[data-scale-target="'+target+'"]');
  if(!input){const group=document.createElement('label');group.append(document.createTextNode(label));input=document.createElement('input');input.type='range';input.min='60';input.max='300';input.step='1';input.dataset.scaleTarget=target;input.setAttribute('aria-label',label);group.append(input,document.createElement('output'));bar.append(group);}
  else{const caption=input.closest('label')?.firstChild;if(caption?.nodeType===3)caption.textContent=label;input.setAttribute('aria-label',label);}
  const logarithmic=target==='project-image'&&ProjectModel.active;input.dataset.logScale=String(logarithmic);
  if(logarithmic){input.min='0';input.max='400';input.step='1';}
  input.closest('label').title=SLIDER_RESET_HINT;input.closest('label').classList.add('slider-label');input.value=logarithmic?100*Math.log10(value):value;PercentInput.update(input,value);
 }
 function apply(page,key,controls=false){
  page.dataset.pageKey=key;page.style.setProperty('--data-scale',value(key)/100);
  if(key==='catalog')ProjectImage.apply(page);
  page.querySelectorAll('table[data-fit-family]').forEach(t=>t.style.fontSize=7*factor(t)+'pt');
  page.querySelectorAll('table.ops').forEach(applyWrap);
  if(!controls)return;
  let bar=page.querySelector(':scope>.page-controls');
  if(bar?.tagName==='LABEL'){bar.remove();bar=null;}
  if(!bar){bar=document.createElement('div');bar.className='page-controls';page.append(bar);}
  // Migrate the old model-only command to one reset button per physical sheet.
  bar.querySelectorAll('[data-project-model-action="reset"]').forEach(button=>button.remove());
  if(key==='cover'){bar.classList.add('setup-panel');const toolbar=scope.querySelector('.toolbar');if(toolbar&&toolbar.parentElement!==bar)bar.prepend(toolbar);}
  control(bar,'page',key==='cover'?'Операции: строки и текст':'Строки и текст этого листа',value(key));
  if(key==='cover'&&page.querySelector('table[data-fit-family="cover-tools"]'))control(bar,'tools','Инструменты: текст',toolScale);
  if(key==='catalog'){
   if(page.querySelector('.project-isometry img,.project-model'))control(bar,'project-image',ProjectModel.active?'Масштаб модели детали':'Масштаб изображения детали',ProjectImage.value());
   else bar.querySelector('input[data-scale-target="project-image"]')?.closest('label')?.remove();
   if(!ProjectModel.active)bar.querySelectorAll('.project-model-actions').forEach(n=>n.remove());
   if(ProjectModel.active&&!bar.querySelector('.project-model-actions')){
    const actions=document.createElement('span');actions.className='project-model-actions';
    const b=document.createElement('button');b.type='button';b.dataset.projectModelAction='fit';b.textContent='Вписать';actions.append(b);bar.append(actions);
   }
   if(ProjectModel.active)ProjectModel.layout(page);
  }
  page.classList.toggle('has-operation-controls',key!=='catalog');
  if(key!=='catalog'){
   let input=bar.querySelector('[data-page-wrap]');
   if(!input){const group=document.createElement('label');group.className='page-wrap-control';input=document.createElement('input');input.type='checkbox';input.dataset.pageWrap='true';group.append(input,document.createTextNode('Переносить названия операций'));bar.append(group);}
   input.checked=wrapValue(key);
  }
  let reset=bar.querySelector('[data-page-reset]');
  if(!reset){reset=document.createElement('button');reset.type='button';reset.dataset.pageReset='true';reset.textContent='Исходный вид';bar.append(reset);}
  reset.title='Восстановить исходные элементы, компоновку и оформление этого листа';
 }
 function refresh(){
  let index=0;scope.querySelectorAll('.setup-pages>.page').forEach(page=>apply(page,page.classList.contains('project-tools-page')?'catalog':page.classList.contains('cover-page')?'cover':'operations-'+(++index),true));
  scope.dataset.pageScales=JSON.stringify({...scales});scope.dataset.coverToolScale=String(toolScale);scope.dataset.pageWraps=JSON.stringify(wrapState());
 }
 scope.addEventListener('input',event=>{
  if(!event.target.matches('.page-controls input[type=range]'))return;
  const target=event.target.dataset.scaleTarget;
  // The shared panel also contains MCS controls; only handle our scale targets.
  if(!['page','tools','project-image'].includes(target))return;
  const page=event.target.closest('.page'),key=page.dataset.pageKey;
  const percent=PercentInput.value(event.target);
  if(target==='project-image')ProjectImage.set(percent);else if(target==='tools')toolScale=normalized(percent);else scales[key]=normalized(percent);
  // Operation pages are rebuilt; keep the active slider in the same screen position.
  const rect=page.getBoundingClientRect();
  CardFlow.reflow();markChanged();const next=scope.querySelector('.setup-pages>[data-page-key="'+key+'"]');
  if(next){const after=next.getBoundingClientRect();window.scrollBy(after.left-rect.left,after.top-rect.top);if(!PercentInput.manual(event.target))next.querySelector('.page-controls input[data-scale-target="'+target+'"]')?.focus({preventScroll:true});}
 });
 scope.addEventListener('change',event=>{
  if(!event.target.matches('.page-controls [data-page-wrap]'))return;
  const page=event.target.closest('.page'),key=page.dataset.pageKey,rect=page.getBoundingClientRect();wraps[key]=event.target.checked;
  CardFlow.reflow();markChanged();const next=scope.querySelector('.setup-pages>[data-page-key="'+key+'"]');
  if(next){const after=next.getBoundingClientRect();window.scrollBy(after.left-rect.left,after.top-rect.top);next.querySelector('[data-page-wrap]')?.focus({preventScroll:true});}
 });
 function state(){const result={...scales};scope.querySelectorAll('.setup-pages>.page').forEach(p=>result[p.dataset.pageKey]=value(p.dataset.pageKey));return result;}
 function wrapState(){const result={...wraps};scope.querySelectorAll('.setup-pages>.page:not(.project-tools-page)').forEach(p=>result[p.dataset.pageKey]=wrapValue(p.dataset.pageKey));return result;}
 function reset(key,saved){scales[key]=normalized(saved?.pageScales?.[key]??100);if(key==='cover')toolScale=normalized(saved?.coverToolScale??100);if(key!=='catalog')wraps[key]=saved?.pageWraps?.[key]??false;}
 function save(copy){copy.dataset.pageScales=JSON.stringify(state());copy.dataset.pageWraps=JSON.stringify(wrapState());copy.dataset.coverToolScale=String(toolScale);copy.querySelectorAll('.page-controls input[type=range]').forEach(input=>input.setAttribute('value',input.value));copy.querySelectorAll('.page-controls [data-page-wrap]').forEach(input=>input.toggleAttribute('checked',input.checked));}
 return{apply,refresh,factor,value,restore,state,save,reset,applyWrap,wrapValue,wrapState,toolsValue:()=>toolScale};
})();

// Uniform scaling includes row spacing, text, padding and borders.
const CoverTools=(()=>{
 const mm=96/25.4;
 function fit(){
  const panel=scope.querySelector('.gallery .tool-panel'),table=panel?.querySelector('table[data-fit-family="cover-tools"]');if(!table)return;
  const style=getComputedStyle(panel),number=k=>parseFloat(style[k])||0;
  const width=(number('width')||Number(panel.dataset.cellWidth)*mm)-number('paddingLeft')-number('paddingRight');
  const height=(number('height')||Number(panel.dataset.cellHeight)*mm)-number('paddingTop')-number('paddingBottom');
  if(!(width>0&&height>0))return;
  const viewport=tableViewport(table);
  Object.assign(viewport.style,{position:'absolute',left:number('paddingLeft')+'px',top:number('paddingTop')+'px',width:width+'px',height:height+'px'});
  const s=PageScale.factor(table);table.dataset.coverScale=String(s);table.dataset.coverRenderScale='1';
  table.style.fontSize=7*s+'pt';table.style.width=width+'px';
  Object.assign(table.style,{position:'absolute',left:'0',top:'0',transform:'none',transformOrigin:'0 0'});
  for(const [name,value] of Object.entries({row:3.8,pady:.2,padx:.6,border:.16}))table.style.setProperty('--cover-'+name,value*s+'mm');
  const natural=table.getBoundingClientRect().height/screenPaperScale(table)||((table.rows.length*3.8+.16)*s*mm);
  // Width and height both reduce the list; expanding restores the chosen scale.
  const render=Math.max(.001,Math.min(1,width/(77.025*mm),Math.max(.01,height-12-.3*mm)/natural));
  table.dataset.coverRenderScale=String(render);table.style.width=width/render+'px';table.style.transform='scale('+render+')';
  const plane=viewport.querySelector('.tool-table-plane');plane.style.width=width+'px';plane.style.height=(natural*render+1)+'px';
 }
 return{fit};
})();


// A project catalog always occupies one page. Fit both columns uniformly.
const ProjectTools=(()=>{
 function fit(){
  scope.querySelectorAll('.setup-pages>.project-tools-page').forEach(page=>{
   const columns=page.querySelector('.catalog-columns'),tables=[...page.querySelectorAll('.catalog-table')];
   if(!columns||!tables.length)return;
   const screenScale=screenPaperScale(page);
   const bottom=page.dataset.projectToolsBottom?page.querySelector('.page-content').getBoundingClientRect().top+Number(page.dataset.projectToolsBottom)*96/25.4*screenScale:page.querySelector('.page-footer').getBoundingClientRect().top;
   const available=Math.max(1,(bottom-columns.getBoundingClientRect().top)/screenScale-14);
   const data=tables.map(table=>{
    const viewport=tableViewport(table),width=viewport.clientWidth;
    table.style.fontSize=7*PageScale.factor(page)+'pt';table.style.width=width+'px';
    Object.assign(table.style,{position:'absolute',left:'0',top:'0',transform:'none',transformOrigin:'0 0'});
    return{table,viewport,width,height:table.getBoundingClientRect().height/screenScale};
   });
   const scale=Math.max(.001,Math.min(1,available/Math.max(1,...data.map(d=>d.height))));
   data.forEach(({table,viewport,width,height})=>{
    table.dataset.projectRenderScale=String(scale);table.style.width=width/scale+'px';table.style.transform='scale('+scale+')';
    const plane=viewport.querySelector('.project-tool-plane');plane.style.width=width+'px';plane.style.height=(height*scale+1)+'px';
   });
  });
 }
 return{fit};
})();

function fitDataTables(only=null){
 if(!only){CoverTools.fit();ProjectTools.fit();columnMetrics.clear();}
 const families=new Map();
 const selected=only||[...scope.querySelectorAll('table[data-fit-family]')];
 selected.forEach(table=>{
  restoreCellText(table);const family=table.dataset.fitFamily;if(family==='operations'){ColumnOptions.apply(table);PageScale.applyWrap(table);}
  const scale=PageScale.factor(table),key=family+':'+scale;
  if(!families.has(key))families.set(key,[]);families.get(key).push(table);
 });
 for(const tables of families.values()){
  const first=tables.find(table=>!table.hidden&&table.getBoundingClientRect().width>0);if(!first)continue;
  const family=first.dataset.fitFamily;
  const renderScale=family==='cover-tools'?Number(first.dataset.coverRenderScale)||1:(family==='project-tools'?Number(first.dataset.projectRenderScale)||1:1);
  const viewport=tableViewport(first),target=viewport?.clientWidth/renderScale-1.5;if(!(target>0))continue;
  const scale=family==='cover-tools'?Number(first.dataset.coverScale)||1:PageScale.factor(first);
  const base=Number(first.dataset.basePt)*scale,allFloor=first.dataset.minMm.split(',').map(x=>Number(x)*96/25.4*scale);
  const shown=allFloor.map((_,i)=>family!=='operations'||ColumnOptions.shown(OP_COLUMNS[i][0]));
  const floor=allFloor.map((x,i)=>shown[i]?x:0),flex=(family==='operations'?[0]:first.dataset.flexCols.split(',').map(Number)).filter(i=>shown[i]);
  const holder=document.createElement('div');holder.style.cssText='position:absolute;left:-30000px;top:0;visibility:hidden;width:max-content;pointer-events:none;';holder.style.setProperty('--data-scale',scale);
  const probe=first.cloneNode(false);probe.classList.add('column-probe');probe.classList.remove('wrap-operation-names');probe.style.width='max-content';probe.style.tableLayout='auto';probe.style.fontSize=base+'pt';for(const property of ['transform','transform-origin','position','left','top','zoom'])probe.style.removeProperty(property);
  probe.append(first.tHead.cloneNode(true));const body=document.createElement('tbody');probe.append(body);
  const sample=family==='operations'?[...local('operation-source').content.querySelectorAll('tbody>tr')]:tables.flatMap(t=>[...t.tBodies[0].rows]);
  sample.forEach(row=>{const clone=row.cloneNode(true);restoreCellText(clone);body.append(clone);if(family==='operations'&&row.dataset.kind==='group'&&!row.dataset.repeat)body.append(repeatGroup(clone));});
  if(family==='operations')ColumnOptions.apply(probe);probe.classList.remove('wrap-operation-names');
  probe.querySelectorAll('.column-grip,.operation-grip').forEach(grip=>grip.remove());holder.append(probe);scope.append(holder);
  const headerCells=[...probe.tHead.rows[0].cells];
  if(family==='operations'&&ellipsisContext)headerCells.forEach((cell,i)=>{
   if(!shown[i]||OP_COLUMNS[i][0]==='change')return;
   const style=getComputedStyle(cell);ellipsisContext.font=[style.fontStyle,style.fontWeight,style.fontSize,style.fontFamily].join(' ');
   const word=Math.max(...OP_COLUMNS[i][1].split(' ').map(w=>ellipsisContext.measureText(w).width));
   floor[i]=Math.max(floor[i],word+(parseFloat(style.paddingLeft)||0)+(parseFloat(style.paddingRight)||0)+3);
  });
  if(family==='operations'&&shown[0])for(const row of body.rows){
   // The remaining column still needs room after the tree indentation and
   // drag handle, including when long operation names wrap onto more lines.
   const cell=row.cells[0],style=getComputedStyle(cell),indent=cell.querySelector('.tree-indent');
   floor[0]=Math.max(floor[0],(indent?.getBoundingClientRect().width||0)+
    (parseFloat(style.paddingLeft)||0)+(parseFloat(style.paddingRight)||0)+4*(parseFloat(style.fontSize)||base*96/72)+3);
  }
  const natural=headerCells.map((cell,i)=>shown[i]?cell.getBoundingClientRect().width+2:0);holder.remove();
  if(natural.some((x,i)=>shown[i]&&!(x>.35)))continue;
  const metrics={target,base,natural,floor,renderScale,tables,shown};tables.forEach(t=>columnMetrics.set(t,metrics));
  const manual=columnWidths[family]&&safeManualWidths(metrics,columnWidths[family]);
  if(manual){applyColumnLayout(family,metrics,manual);continue;}
  fitAutoColumns(family,metrics,flex,allFloor);
 }
}

// Center the visible text even when ellipsis is needed. Full values/markup stay
// in the HTML, tooltips and source table; browser text-overflow cannot shift them.
const ellipsisCanvas=document.createElement('canvas'),ellipsisContext=ellipsisCanvas.getContext('2d');
function alignNumericText(table){
 // Auto width already reserves the complete rendered text. Canvas rounding
 // must not replace a value that actually fits with an artificial ellipsis.
 if(!ellipsisContext||table.dataset.widthMode==='auto')return;
 table.querySelectorAll('td.numeric:not([hidden]) .cell-text').forEach(node=>{
  if(node.querySelector('svg'))return;
  const full=node.textContent;if(!full)return;
  const strong=node.querySelector('strong'),style=getComputedStyle(strong||node),width=node.clientWidth;
  if(!(width>0))return;ellipsisContext.font=[style.fontStyle,style.fontWeight,style.fontSize,style.fontFamily].join(' ');
  if(ellipsisContext.measureText(full).width<=width-.5)return;
  node.dataset.fullHtml=node.innerHTML;const chars=Array.from(full);let lo=0,hi=chars.length;
  while(lo<hi){const mid=Math.ceil((lo+hi)/2);if(ellipsisContext.measureText(chars.slice(0,mid).join('')+'…').width<=width-.5)lo=mid;else hi=mid-1;}
  const short=chars.slice(0,lo).join('')+'…';if(strong){const copy=strong.cloneNode(false);copy.textContent=short;node.replaceChildren(copy);}else node.textContent=short;
 });
}


function paginate(){
 NoteLayout.finish(true);PhotoPan.finish();GalleryLayout.closePicker();GalleryLayout.finish(true);finishColumnDrag();CardFlow.finish(true);CardFlow.reflow();
}

function normalizedPhotoPan(value){
 const number=Number(value);return Number.isFinite(number)?Math.max(-50,Math.min(50,number)):0;
}
function photoPan(photo){return{x:normalizedPhotoPan(photo.dataset.panX??0),y:normalizedPhotoPan(photo.dataset.panY??0)};}
function photoPanValues(){
 const result=Object.create(null);scope.querySelectorAll('.photo[data-image]:not([data-cell-preview])').forEach(photo=>{result[photo.dataset.image]=photoPan(photo);});return result;
}
function applyPhotoPanValues(values){
 if(!values||typeof values!=='object')return;
 scope.querySelectorAll('.photo[data-image]').forEach(photo=>{
  if(!Object.prototype.hasOwnProperty.call(values,photo.dataset.image))return;
  const value=values[photo.dataset.image];if(value&&typeof value==='object')setPhotoPan(photo,value.x,value.y);
 });
}
function photoImageTransform(photo){
 const pan=photoPan(photo),scale=normalizedPhotoZoom(photo.dataset.zoom??100)/100;
 // translate is in stage percentages and precedes scale, so zoom does not
 // multiply the chosen pan. The matching datum adds exactly the same shift.
 // Clipboard images have no IPW variant; transform every raster in the cell.
 photo.querySelectorAll('.photo-stage img').forEach(img=>{
  img.style.transform='translate('+pan.x+'%, '+pan.y+'%) scale('+scale+')';
 });
}
function setPhotoPan(photo,x,y,dirty=false){
 const before=photoPan(photo),pan={x:normalizedPhotoPan(x),y:normalizedPhotoPan(y)};
 photo.dataset.panX=String(pan.x);photo.dataset.panY=String(pan.y);
 photoImageTransform(photo);positionMcsAnchors(photo);
 if(dirty&&(before.x!==pan.x||before.y!==pan.y))markChanged();
}
const PhotoPan=(()=>{
 let drag=null;const suppressedClicks=new WeakMap();
 const controls='.photo-view-tools,.gallery-grip,.gallery-height-grip,.column-grip,.cell-remove,.cell-move';
 function photoAt(target){return target instanceof Element?target.closest('.photo[data-image]'):null;}
 function finish(cancel=false){
  const previous=drag;drag=null;scope.classList.remove('photo-panning');
  if(!previous)return;
  previous.photo.classList.remove('photo-pan-active');
  if(previous.moved){
   suppressedClicks.set(previous.photo,Date.now()+1000);
   if(cancel)setPhotoPan(previous.photo,previous.initial.x,previous.initial.y);
   else{const now=photoPan(previous.photo);if(now.x!==previous.initial.x||now.y!==previous.initial.y)markChanged();}
  }
  try{previous.capture.releasePointerCapture(previous.id);}catch(error){}
 }
 scope.addEventListener('pointerdown',event=>{
  const photo=photoAt(event.target);
  if(!photo||event.target.closest(controls)||event.button!==0||event.isPrimary===false||drag)return;
  const stage=photo.querySelector('.photo-stage');if(!stage?.querySelector('img'))return;
  const rect=stage.getBoundingClientRect();if(!(rect.width>0&&rect.height>0))return;
  suppressedClicks.delete(photo);
  const capture=photo.querySelector('.photo-toggle')||photo;
  capture.focus({preventScroll:true});
  drag={photo,capture,id:event.pointerId,x:event.clientX,y:event.clientY,rect,initial:photoPan(photo),moved:false};
  try{capture.setPointerCapture(event.pointerId);}catch(error){}
 });
 scope.addEventListener('pointermove',event=>{
  if(!drag||event.pointerId!==drag.id)return;
  const dx=event.clientX-drag.x,dy=event.clientY-drag.y;
  if(!drag.moved&&Math.hypot(dx,dy)<4)return;
  event.preventDefault();drag.moved=true;
  scope.classList.add('photo-panning');drag.photo.classList.add('photo-pan-active');
  setPhotoPan(drag.photo,drag.initial.x+dx/drag.rect.width*100,drag.initial.y+dy/drag.rect.height*100);
 },{passive:false});
 scope.addEventListener('pointerup',event=>{if(drag?.id===event.pointerId)finish();});
 scope.addEventListener('pointercancel',event=>{if(drag?.id===event.pointerId)finish(true);});
 scope.addEventListener('lostpointercapture',event=>{if(drag?.id===event.pointerId)finish(true);});
 window.addEventListener('blur',()=>finish(true));
 // Consume only the click generated by a completed drag, never the next
 // pointer gesture, an IPW keyboard click, a scale reset or the centre button.
 scope.addEventListener('click',event=>{
  const photo=photoAt(event.target);if(!photo||event.target.closest(controls))return;
  const until=suppressedClicks.get(photo);if(event.detail!==0&&until&&Date.now()<=until){
   suppressedClicks.delete(photo);event.preventDefault();event.stopImmediatePropagation();
  }
 },true);
 scope.addEventListener('click',event=>{
  const reset=event.target instanceof Element?event.target.closest('.photo-pan-reset'):null;
  if(reset){finish();setPhotoPan(reset.closest('.photo'),0,0,true);}
 });
 scope.addEventListener('keydown',event=>{
  if(event.key==='Escape'&&drag){event.preventDefault();finish(true);return;}
  const photo=photoAt(event.target);if(!photo||event.target.closest(controls)||event.ctrlKey||event.metaKey||event.altKey)return;
  if(event.key==='Home'){event.preventDefault();finish();setPhotoPan(photo,0,0,true);return;}
  const deltas={ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]},delta=deltas[event.key];
  if(!delta)return;event.preventDefault();finish();const step=event.shiftKey?5:1,pan=photoPan(photo);
  setPhotoPan(photo,pan.x+delta[0]*step,pan.y+delta[1]*step,true);
 });
 scope.addEventListener('dragstart',event=>{if(photoAt(event.target))event.preventDefault();});
 scope.querySelectorAll('.photo[data-image]').forEach(photo=>{
  if(!photo.querySelector('.photo-toggle')){photo.tabIndex=0;photo.setAttribute('aria-label',photo.dataset.label+'; тяните мышью для сдвига, стрелки — сдвиг, Home — центр; колесо или + / − — масштаб');}
  photo.querySelectorAll('.photo-stage img').forEach(img=>{img.draggable=false;});
 });
 return{finish,cleanClone:copy=>{
  copy.classList.remove('photo-panning');copy.querySelectorAll('.photo-pan-active').forEach(photo=>photo.classList.remove('photo-pan-active'));
 }};
})();

function normalizedPhotoZoom(value){
 const n=Number(value);
 return Number.isFinite(n)?Math.max(50,Math.min(200,Math.round(n/5)*5)):100;
}
function photoZoomValues(){
 const result=Object.create(null);
 scope.querySelectorAll('.photo[data-image]:not([data-cell-preview])').forEach(photo=>{result[photo.dataset.image]=normalizedPhotoZoom(photo.dataset.zoom??100);});
 return result;
}
function applyPhotoZoomValues(values){
 if(!values||typeof values!=='object')return;
 scope.querySelectorAll('.photo[data-image]').forEach(photo=>{
  if(Object.prototype.hasOwnProperty.call(values,photo.dataset.image))setPhotoZoom(photo,values[photo.dataset.image]);
 });
}
function setPhotoZoom(photo,value,dirty=false){
 const before=normalizedPhotoZoom(photo.dataset.zoom??100),zoom=normalizedPhotoZoom(value);
 photo.dataset.zoom=String(zoom);
 const reset=photo.querySelector('.photo-zoom-reset');
 if(reset){reset.textContent=zoom+'%';reset.title='Масштаб '+zoom+'%. Нажмите, чтобы вернуть 100%.';reset.setAttribute('aria-label',reset.title);}
 // Apply the same transform to both variants and the image-space MCS origin.
 photoImageTransform(photo);positionMcsAnchors(photo);
 if(dirty&&before!==zoom)markChanged();
}

function wheelZoomDirection(event){
 // Preserve browser zoom and horizontal scrolling; a zero delta is not a zoom-out.
 if(event.defaultPrevented||event.ctrlKey||event.metaKey||event.altKey||!Number.isFinite(event.deltaY)||event.deltaY===0||Math.abs(event.deltaX)>Math.abs(event.deltaY))return 0;
 return event.deltaY<0?1:-1;
}

scope.addEventListener('wheel',event=>{
 const photo=event.target.closest('.photo[data-image]');if(!photo||event.target.closest('.photo-view-tools,.gallery-grip,.gallery-height-grip,.column-grip,.cell-remove,.cell-move')||!photo.querySelector('.photo-stage img'))return;
 const direction=wheelZoomDirection(event);if(!direction)return;event.preventDefault();PhotoPan.finish();setPhotoZoom(photo,normalizedPhotoZoom(photo.dataset.zoom??100)+direction*5,true);
},{passive:false});
scope.addEventListener('keydown',event=>{
 const photo=event.target.closest('.photo[data-image]');if(!photo||event.ctrlKey||event.metaKey||event.altKey||event.target.closest('.photo-view-tools,.cell-remove,.cell-move'))return;
 const direction=event.key==='+'||event.key==='='?1:event.key==='-'?-1:0;if(!direction)return;event.preventDefault();PhotoPan.finish();setPhotoZoom(photo,normalizedPhotoZoom(photo.dataset.zoom??100)+direction*5,true);
});

function mcsAnchorInStage(w,h,iw,ih,u,v,zoom,panX=0,panY=0){
 // object-fit:contain at 100%, then image scale() about the stage centre.
 // Letterboxing and an off-centre datum must both take part in the mapping.
 const scale=Math.min(w/iw,h/ih)*normalizedPhotoZoom(zoom)/100;
 return {left:50+(u-.5)*iw*scale*100/w+normalizedPhotoPan(panX),top:50+(v-.5)*ih*scale*100/h+normalizedPhotoPan(panY)};
}
scope.addEventListener('click',event=>{
 const reset=event.target.closest('.photo-zoom-reset');if(reset){PhotoPan.finish();setPhotoZoom(reset.closest('.photo'),100,true);}
});

function setVariant(photo,noIpw,dirty=false){
 if(!photo.querySelector('img[data-variant="no-ipw"]'))return;
 const variant=noIpw?'no-ipw':'ipw',label=noIpw?'NO IPW':'IPW';photo.dataset.variant=variant;
 photo.querySelectorAll('img[data-variant],.mcs-axes[data-variant],.mcs-onpart[data-variant]').forEach(el=>{el.toggleAttribute('hidden',el.dataset.variant!==variant);});
 const toggle=photo.querySelector('.photo-toggle');
 if(toggle){toggle.setAttribute('aria-pressed',String(!noIpw));toggle.setAttribute('aria-label',photo.dataset.label+' · '+label+'; нажмите для '+(noIpw?'IPW':'NO IPW')+'; колесо или + / − — масштаб');}
 const mode=photo.querySelector('.image-mode');if(mode)mode.textContent=' · '+label;
 positionMcsAnchors(photo);
 if(dirty)markChanged();
}
function positionMcsAnchors(root=scope){
 root.querySelectorAll('.photo-stage').forEach(stage=>{
  // Computed CSS lengths avoid mixing CSS pixels with browser zoom or page transforms.
  const style=getComputedStyle(stage),w=parseFloat(style.width)||stage.clientWidth,h=parseFloat(style.height)||stage.clientHeight;
  const photo=stage.closest('.photo'),zoom=photo?.dataset.zoom??100,pan=photoPan(photo);
  if(!(w>0&&h>0))return;
  stage.querySelectorAll('.mcs-onpart').forEach(svg=>{
   const iw=Number(svg.dataset.imageWidth),ih=Number(svg.dataset.imageHeight),u=Number(svg.dataset.anchorX),v=Number(svg.dataset.anchorY);
   if(!(iw>0&&ih>0)||![u,v].every(Number.isFinite))return;
   const point=mcsAnchorInStage(w,h,iw,ih,u,v,zoom,pan.x,pan.y);
   svg.style.left=point.left+'%';svg.style.top=point.top+'%';
  });
 });
}
function setMcsSize(value,dirty=false){
 const input=local('mcs-size'),n=Number(value);
 const size=Number.isFinite(n)?Math.max(50,Math.min(180,Math.round(n))):100;
 input.value=String(size);input.setAttribute('value',String(size));input.setAttribute('aria-valuetext',size+'%');
 PercentInput.update(input,size);
 scope.querySelectorAll('.mcs-onpart').forEach(svg=>{svg.style.width=(36*size/100)+'mm';svg.style.height=(36*size/100)+'mm';});
 if(dirty)markChanged();
}
function setMcsOutline(mode,width,dirty=false){
 const chosen=['white','black'].includes(mode)?mode:'white',n=Number(width);
 const value=Number.isFinite(n)?Math.max(50,Math.min(250,Math.round(n))):100;
 const select=local('mcs-outline-color'),input=local('mcs-outline-width');
 select.value=chosen;[...select.options].forEach(option=>option.toggleAttribute('selected',option.value===chosen));
 input.value=String(value);input.setAttribute('value',String(value));input.setAttribute('aria-valuetext',value+'%');
 PercentInput.update(input,value);
 scope.querySelectorAll('.mcs-onpart').forEach(svg=>svg.querySelectorAll('[data-outline-axis]').forEach(node=>{
  const color=chosen==='white'?'#ffffff':'#111111';
  node.setAttribute('stroke',color);
  node.setAttribute('stroke-width',String(Number(node.dataset.outlineFixed)+Number(node.dataset.outlineBase)*value/100));
  if(node.dataset.outlineFill==='true')node.setAttribute('fill',color);
 }));
 if(dirty)markChanged();
}
// A split tree tiles the cover without gaps. Removing a leaf promotes its sibling.
const GalleryLayout=(()=>{
 const root=scope.querySelector('.gallery'),grid=root.querySelector('.gallery-grid'),W=194;
 const cells=new Map(),rects=new Map(),splits=new Map(),selection=new Set(),copy=x=>JSON.parse(JSON.stringify(x));
 let tree=null,height=225.5,preferredHeight=225.5,availableHeight=225.5,selected=null,drag=null,queued=false,swapFrom=null,swapScroll=0;
 let picker=null,pickerTarget='tools',pickerSide='bottom',pickerCols=1,pickerRows=1,suppressPickClick=false,gridPreview=null;
 const dropPreview=document.createElement('div');dropPreview.className='cell-drop-preview';dropPreview.hidden=true;root.append(dropPreview);
 const clamp=(x,a,b)=>Math.max(a,Math.min(b,x));
 const leaf=id=>({id}),split=(axis,ratio,a,b)=>({axis,ratio,a,b});
 grid.querySelectorAll('.photo,.tool-panel').forEach((node,index)=>{
  const id=node.dataset.cellId||(node.matches('.tool-panel')?'tools':node.dataset.image||'view-'+index);
  node.dataset.cellId=id;cells.set(id,node);
 });
 const originalCells=new Map([...cells].filter(([,node])=>!node.matches('.user-photo')));
 // Only deleted original cells need an archive in saved HTML. Existing images
 // remain in their normal cells, without a second copy of every image payload.
 for(const [id,markup] of Object.entries(initialView?.deletedCells||{})){
  if(originalCells.has(id)||typeof markup!=='string')continue;
  const template=document.createElement('template');template.innerHTML=markup;const node=template.content.firstElementChild;
  if(node?.matches('.photo:not(.user-photo),.tool-panel')&&node.dataset.cellId===id)originalCells.set(id,node);
 }
 const original={iso:[...cells].find(([,n])=>n.matches('.iso-photo'))?.[0],
  top:[...cells].find(([,n])=>n.matches('.top-photo'))?.[0],
  side:[...cells].find(([,n])=>n.matches('.side-photo'))?.[0]};
 // Flatten the fixed grid while retaining the existing cell nodes.
 cells.forEach(node=>grid.append(node));grid.querySelectorAll('.right-panel').forEach(n=>n.remove());
 function balanced(ids,axis='x'){
  if(ids.length===1)return leaf(ids[0]);const middle=Math.ceil(ids.length/2);
  return split(axis,middle/ids.length,balanced(ids.slice(0,middle),axis==='x'?'y':'x'),balanced(ids.slice(middle),axis==='x'?'y':'x'));
 }
 function defaults(){
  const {iso,top,side}=original;
  if(iso&&top&&side&&cells.has(iso)&&cells.has(top)&&cells.has(side)){
   const rightTop=leaf('tools');
   return split('x',.5875,split('y',.47,leaf(iso),leaf(top)),split('y',.47,rightTop,leaf(side)));
  }
  return balanced([...cells.keys()]);
 }
 function ids(node=tree){return node.id?[node.id]:[...ids(node.a),...ids(node.b)];}
 function valid(node,seen=new Set(),depth=0){
  if(!node||depth>64)return null;
  if(typeof node.id==='string'){
   if(!cells.has(node.id)||seen.has(node.id))return null;seen.add(node.id);return leaf(node.id);
  }
  if(!['x','y'].includes(node.axis)||!Number.isFinite(node.ratio))return null;
  const a=valid(node.a,seen,depth+1),b=valid(node.b,seen,depth+1);
  return a&&b?split(node.axis,clamp(node.ratio,.03,.97),a,b):null;
 }
 function mutate(node,id,replacement){
  if(node.id)return node.id===id?replacement:node;
  const a=mutate(node.a,id,replacement),b=mutate(node.b,id,replacement);
  return a&&b?split(node.axis,node.ratio,a,b):a||b;
 }
 function at(path){let n=tree;for(const k of path)n=n[k];return n;}
 function minSpan(node,axis){
  if(node.id)return node.id==='tools'?(axis==='x'?18:12):(axis==='x'?24:25);
  const a=minSpan(node.a,axis),b=minSpan(node.b,axis);return node.axis===axis?a+b:Math.max(a,b);
 }
 function ratio(node,box,value){
  const span=node.axis==='x'?box.w*W:box.h*height,a=minSpan(node.a,node.axis),b=minSpan(node.b,node.axis);
  return span>a+b?clamp(value,a/span,1-b/span):a/(a+b);
 }
 function walk(node,box,path=''){
  if(node.id){rects.set(node.id,box);return;}
  node.ratio=ratio(node,box,node.ratio);splits.set(path,{node,box});const r=node.ratio;
  if(node.axis==='x'){
   walk(node.a,{x:box.x,y:box.y,w:box.w*r,h:box.h},path+'a');
   walk(node.b,{x:box.x+box.w*r,y:box.y,w:box.w*(1-r),h:box.h},path+'b');
  }else{
   walk(node.a,{x:box.x,y:box.y,w:box.w,h:box.h*r},path+'a');
   walk(node.b,{x:box.x,y:box.y+box.h*r,w:box.w,h:box.h*(1-r)},path+'b');
  }
 }
 function refresh(){CoverTools.fit();fitDataTables([...root.querySelectorAll('table[data-fit-family]')]);positionMcsAnchors();}
 function queue(){if(queued)return;queued=true;requestAnimationFrame(()=>{queued=false;refresh();});}
 function controls(node){
  if(node.matches('.tool-panel'))return;
  if(!node.querySelector(':scope>.cell-move')){
   const button=document.createElement('button');button.type='button';button.className='cell-move';
   button.textContent='⠿';button.title='Перетащите ячейку или выделенную группу. У края — вставить рядом, в центре другой картинки — обменять. Ctrl + щелчок — выделить несколько.';
   button.setAttribute('aria-label','Переместить ячейку '+(node.dataset.label||'изображения'));node.append(button);
  }
  if(!node.querySelector(':scope>.cell-remove')){
   const button=document.createElement('button');button.type='button';button.className='cell-remove';
   button.textContent='×';button.title='Удалить ячейку';button.setAttribute('aria-label','Удалить ячейку '+(node.dataset.label||'изображения'));node.append(button);
  }
  if(node.matches('.user-photo')){node.tabIndex=0;node.setAttribute('aria-label','Своё изображение. Ctrl+V — вставить; тяните мышью для сдвига; колесо или + / − — масштаб; Home — центр.');}
 }
 function paintSelection(){
  cells.forEach((n,key)=>{n.classList.toggle('cell-selected',selection.has(key));n.querySelector('.cell-move')?.setAttribute('aria-pressed',String(selection.has(key)));});
  const status=local('cell-selection-status');status.textContent=selection.size>1?'Выбрано ячеек: '+selection.size:'';
  local('cell-clear-selection').hidden=selection.size<2;
 }
 function select(id,focus=false,toggle=false,preserve=false){
  selected=cells.has(id)?id:null;
  if(toggle&&imageCell(id)){if(selection.has(id))selection.delete(id);else selection.add(id);}
  else if(!preserve){selection.clear();if(imageCell(id))selection.add(id);}
  paintSelection();
  if(focus&&selected)cells.get(selected).focus({preventScroll:true});
 }
 function selectMany(list){selection.clear();list.filter(imageCell).forEach(id=>selection.add(id));selected=[...selection].at(-1)||null;paintSelection();}
 function apply({refreshNow=false,scheduleRefresh=true}={}){
  root.style.height=height+'mm';grid.style.height='100%';grid.style.display='block';
  rects.clear();splits.clear();walk(tree,{x:0,y:0,w:1,h:1});
  const path=[];
  cells.forEach((node,id)=>{
   const b=rects.get(id);if(!b)return;controls(node);
   Object.assign(node.style,{position:'absolute',left:b.x*100+'%',top:b.y*100+'%',width:b.w*100+'%',height:b.h*100+'%'});
   node.dataset.cellWidth=String(b.w*W);node.dataset.cellHeight=String(b.h*height);
   const hint=node.querySelector('.paste-hint');if(hint)hint.textContent=b.w*W<48||b.h*height<40?'Ctrl+V':'Нажмите здесь и вставьте изображение: Ctrl+V';
   path.push('M'+(b.x*1000)+' '+(b.y*1000)+'h'+(b.w*1000)+'v'+(b.h*1000)+'h'+(-b.w*1000)+'Z');
  });
  const frame=root.querySelector('.gallery-frame');frame.style.width='100%';frame.style.height='100%';frame.querySelector('path').setAttribute('d',path.join(' '));
  const existing=new Map([...root.querySelectorAll('.gallery-grip')].map(n=>[n.dataset.splitPath,n]));
  splits.forEach(({node,box},key)=>{
   let grip=existing.get(key);existing.delete(key);
   if(!grip){grip=document.createElement('span');grip.className='gallery-grip';grip.dataset.splitPath=key;grip.tabIndex=0;grip.setAttribute('role','separator');root.append(grip);}
   const horizontal=node.axis==='y';grip.dataset.galleryAxis=horizontal?'row':'column';
   Object.assign(grip.style,{left:(box.x+(horizontal?0:box.w*node.ratio))*100+'%',top:(box.y+(horizontal?box.h*node.ratio:0))*100+'%',
    width:horizontal?box.w*100+'%':'8px',height:horizontal?'8px':box.h*100+'%'});
   grip.setAttribute('aria-orientation',horizontal?'horizontal':'vertical');grip.setAttribute('aria-label','Изменить размер соседних ячеек');
   grip.setAttribute('aria-valuemin','3');grip.setAttribute('aria-valuemax','97');grip.setAttribute('aria-valuenow',(node.ratio*100).toFixed(1));
   grip.title='Перетащите границу. Стрелки — шаг 1 мм; Shift — 5 мм; двойной щелчок — поровну.';
  });
  existing.forEach(g=>g.remove());
  let edge=root.querySelector('.gallery-height-grip');
  if(!edge){edge=document.createElement('span');edge.className='gallery-height-grip';edge.tabIndex=0;edge.setAttribute('role','separator');edge.setAttribute('aria-orientation','horizontal');root.append(edge);}
  edge.setAttribute('aria-label','Изменить общую высоту блока изображений');
  edge.setAttribute('aria-valuemin',String(minimumHeight()));edge.setAttribute('aria-valuemax',availableHeight.toFixed(1));edge.setAttribute('aria-valuenow',height.toFixed(1));
  edge.setAttribute('aria-valuetext',height.toFixed(1)+' мм');
  edge.title='Тяните нижнюю границу вверх или вниз. Стрелки — 1 мм; Shift — 5 мм; двойной щелчок — доступная высота.';
  root.dataset.galleryLayout=JSON.stringify({tree,preferredHeight});
  CardHistory?.sync();
 if(refreshNow)refresh();else if(scheduleRefresh)queue();
 }
 function restore(data,deferred=false){
  preferredHeight=Number.isFinite(data?.preferredHeight)?clamp(data.preferredHeight,50,225.5):225.5;
  originalCells.forEach((node,id)=>{if(!cells.has(id)){cells.set(id,node);grid.append(node);}});
  if(data?.users){
   for(const [id,node] of cells)if(node.matches('.user-photo')){node.remove();cells.delete(id);}
   for(const item of data.users){if(typeof item.id==='string'&&!cells.has(item.id))createUser(item.id,item.src,item.label);}
  }
  const candidate=valid(data?.tree);
  if(candidate&&ids(candidate).includes('tools')){
   tree=candidate;const retained=new Set(ids());
   for(const [id,node] of cells)if(!retained.has(id)){node.remove();cells.delete(id);}
  }else if(!tree)tree=defaults();
  height=clamp(Math.min(preferredHeight,availableHeight),minimumHeight(),225.5);select(null);if(!deferred)apply();
 }
 function values(){
  const saved=gridPreview?.base||(drag?.kind==='height'?drag:null);
  return{tree:copy(saved?.tree||tree),preferredHeight:saved?.preferredHeight??preferredHeight,users:[...(gridPreview?.base.cells||cells)].filter(([,n])=>n.matches('.user-photo')).map(([id,n])=>({id,src:n.querySelector('img')?.getAttribute('src')||'',label:n.dataset.label}))};
 }
 function minimumHeight(){return Math.min(225.5,Math.max(50,minSpan(tree,'y')));}
 function setHeight(value,refreshNow=true){availableHeight=clamp(value,minimumHeight(),225.5);height=clamp(Math.min(preferredHeight,availableHeight),minimumHeight(),availableHeight);apply({refreshNow,scheduleRefresh:false});}
 function resizeHeight(value,flow=null){
  preferredHeight=clamp(value,minimumHeight(),flow?CardFlow.availableHeight():availableHeight);height=preferredHeight;apply();
  const count=flow?CardFlow.previewHeight(height,flow):null;
  local('gallery-size-status').textContent='Высота блока: '+height.toFixed(1)+' мм'+(count===null?'':' · операций на первом листе: '+count);
 }
 function restoreGeometry(saved,render=true){
  tree=copy(saved.tree);preferredHeight=saved.preferredHeight;
  height=clamp(Math.min(preferredHeight,availableHeight),minimumHeight(),225.5);if(render)apply();
 }
 function clearSwap(){
  swapFrom=null;dropPreview.hidden=true;cells.forEach(n=>n.classList.remove('cell-swap-source','cell-swap-target'));paintSelection();
 }
 function imageCell(id){return id!=='tools'&&cells.get(id)?.matches('.photo');}
 function snapshot(){return{tree:copy(tree),cells:new Map(cells),height,preferredHeight,availableHeight,selected:[...selection],active:selected};}
 function useSnapshot(saved){
  cells.forEach((n,id)=>{if(!saved.cells.has(id))n.remove();});cells.clear();
  saved.cells.forEach((n,id)=>{cells.set(id,n);if(n.parentNode!==grid)grid.append(n);});
  tree=copy(saved.tree);height=saved.height;preferredHeight=saved.preferredHeight;availableHeight=saved.availableHeight;
  selectMany(saved.selected);selected=saved.active;apply({scheduleRefresh:false});
 }
 function subset(node,keep){
  if(node.id)return keep.has(node.id)?copy(node):null;
  const a=subset(node.a,keep),b=subset(node.b,keep);
  return a&&b?split(node.axis,node.ratio,a,b):a||b;
 }
 function fits(candidate){return minSpan(candidate,'x')<=W+.01&&minSpan(candidate,'y')<=CardFlow.availableHeight()+.01;}
 function beside(target,block,side){
  const axis=['left','right'].includes(side)?'x':'y',before=['left','top'].includes(side);
  return split(axis,.5,before?block:leaf(target),before?leaf(target):block);
 }
 function redistribute(candidate,newSizes=new Map()){
  // Insertion increases the space needed by the target branch. Share that
  // demand with its ancestors instead of squeezing the tools into 12 mm.
  function size(node,axis){
   if(node.id){const b=newSizes.get(node.id)||rects.get(node.id);return b?(axis==='x'?b.w*W:b.h*height):minSpan(node,axis);}
   const a=size(node.a,axis),b=size(node.b,axis);return node.axis===axis?a+b:Math.max(a,b);
  }
  function visit(node){if(node.id)return;const a=size(node.a,node.axis),b=size(node.b,node.axis);node.ratio=a/(a+b);visit(node.a);visit(node.b);}
  visit(candidate);return candidate;
 }
 function changedLayout(){
  height=Math.max(height,minimumHeight());preferredHeight=Math.max(preferredHeight,minimumHeight());
  apply();CardFlow.reflow();markChanged();
 }
 function swap(a,b){
  if(a===b||!imageCell(a)||!imageCell(b))return false;
  function exchange(n){if(n.id){if(n.id===a)n.id=b;else if(n.id===b)n.id=a;}else{exchange(n.a);exchange(n.b);}}
  exchange(tree);clearSwap();select(a);apply({refreshNow:true});markChanged();return true;
 }
 function moveGroup(list,target,side='bottom'){
  const moving=new Set(list.filter(imageCell));
  if(!moving.size||moving.has(target)||!cells.has(target)||!['left','right','top','bottom'].includes(side))return false;
  const block=subset(tree,moving),remainder=subset(tree,new Set(ids().filter(id=>!moving.has(id))));
  const candidate=mutate(remainder,target,beside(target,block,side));
  if(!fits(candidate))return false;
  tree=redistribute(candidate);clearSwap();selectMany([...moving]);changedLayout();return true;
 }
 function swapTarget(x,y){
  const node=document.elementFromPoint(x,y)?.closest('.gallery-grid>[data-cell-id]'),id=node?.dataset.cellId;
  if(!cells.has(id)||drag?.sources.includes(id))return null;
  if(!root.contains(node))return null;
  const b=node.getBoundingClientRect(),nx=(x-b.left)/b.width,ny=(y-b.top)/b.height;
  const edges=[['left',nx],['right',1-nx],['top',ny],['bottom',1-ny]].sort((a,b)=>a[1]-b[1]);
  const side=edges[0][1]<.23?edges[0][0]:(drag?.sources.length===1&&id!=='tools'?'swap':'bottom');
  return{id,side};
 }
 function showSwapTarget(){
  if(drag?.kind!=='swap'||!drag.moved)return;
  drag.target=swapTarget(drag.x,drag.y);
  dropPreview.hidden=!drag.target;if(!drag.target)return;
  const b=rects.get(drag.target.id),side=drag.target.side,box={...b};
  if(side==='left'||side==='right'){box.w/=2;if(side==='right')box.x+=box.w;}
  if(side==='top'||side==='bottom'){box.h/=2;if(side==='bottom')box.y+=box.h;}
  Object.assign(dropPreview.style,{left:box.x*100+'%',top:box.y*100+'%',width:box.w*100+'%',height:box.h*100+'%'});
  dropPreview.textContent=side==='swap'?'Поменять местами':'Переместить '+drag.sources.length+' '+({top:'сверху',bottom:'снизу',left:'слева',right:'справа'}[side]);
 }
 function scrollSwap(){
  if(drag?.kind!=='swap'||!drag.moved)return;
  const top=scope.querySelector('.toolbar').getBoundingClientRect().bottom;
  const delta=drag.y<top+24?-10:drag.y>innerHeight-40?10:0;
  const dx=drag.x<36?-10:drag.x>innerWidth-36?10:0;
  if(dx||delta){window.scrollBy(dx,delta);showSwapTarget();}swapScroll=requestAnimationFrame(scrollSwap);
 }
 function remove(id){
  if(id==='tools'||!cells.has(id))return;
  closePicker();PhotoPan.finish();finish(true);clearSwap();
  cells.get(id).remove();cells.delete(id);tree=mutate(tree,id,null);select(null);changedLayout();
 }
 function undoRemove(){
  CardHistory?.undo();
 }
 function imageSource(src){return typeof src==='string'&&/^data:image\/(?:png|jpeg|webp|gif|bmp);base64,[A-Za-z0-9+/=\r\n]+$/.test(src);}
 function createUser(id,src='',label='Своё изображение'){
  const node=document.createElement('figure');node.className='photo user-photo';node.dataset.cellId=id;node.dataset.image=id;
  node.dataset.label=label;node.dataset.zoom='100';node.dataset.panX='0';node.dataset.panY='0';
  const stage=document.createElement('div');stage.className='photo-stage';node.append(stage);
  const hint=document.createElement('div');hint.className='paste-hint';hint.textContent='Нажмите здесь и вставьте изображение: Ctrl+V';node.append(hint);
  const bar=document.createElement('div');bar.className='photo-view-tools';
  const zoom=document.createElement('button');zoom.type='button';zoom.className='photo-zoom-reset';zoom.textContent='100%';zoom.title='Вернуть масштаб 100%';
  const pan=document.createElement('button');pan.type='button';pan.className='photo-pan-reset';pan.textContent='Центр';pan.title='Вернуть изображение в центр ячейки без изменения масштаба';bar.append(zoom,pan);node.append(bar);
  cells.set(id,node);grid.append(node);controls(node);if(imageSource(src))putImage(node,src);return node;
 }
 function putImage(node,src){
  if(!imageSource(src))throw Error('Поддерживается изображение PNG, JPEG, WebP, GIF или BMP.');
  const img=document.createElement('img');img.src=src;img.alt=node.dataset.label;img.draggable=false;
  node.querySelector('.photo-stage').replaceChildren(img);node.querySelector('.paste-hint').hidden=true;
  setPhotoZoom(node,100);setPhotoPan(node,0,0);
 }
 function rowOf(list,axis){
  if(list.length===1)return list[0];const n=Math.ceil(list.length/2);
  return split(axis,n/list.length,rowOf(list.slice(0,n),axis),rowOf(list.slice(n),axis));
 }
 function gridCandidate(cols,rows,target,side){
  cols=Number(cols);rows=Number(rows);
  if(!Number.isInteger(cols)||!Number.isInteger(rows)||cols<1||cols>6||rows<1||rows>6||!cells.has(target)||!['left','right','top','bottom'].includes(side))return null;
  const prefix='user-'+Date.now().toString(36)+'-'+Math.random().toString(36).slice(2,8)+'-',created=Array.from({length:cols*rows},(_,i)=>prefix+i);
  const rowsTree=Array.from({length:rows},(_,r)=>rowOf(created.slice(r*cols,(r+1)*cols).map(leaf),'x'));
  const block=rowOf(rowsTree,'y'),candidate=mutate(tree,target,beside(target,block,side));
  if(!fits(candidate))return null;
  const box=rects.get(target),sizes=new Map(created.map(id=>[id,{w:Math.max(24/W,box.w/cols),h:Math.max(25/height,Math.min(40/height,box.h/rows))}]));
  return{tree:redistribute(candidate,sizes),created};
 }
 function cancelGridPreview(){
  if(!gridPreview)return;const saved=gridPreview.base;gridPreview=null;useSnapshot(saved);
  CardFlow.reflow(new Set(CardFlow.values()));
 }
 function previewGrid(cols,rows){
  const signature=[cols,rows,pickerTarget,pickerSide].join('|');
  if(gridPreview?.signature===signature){if(picker)picker.querySelector('output').textContent=cols+' × '+rows+(gridPreview.created?'':' — не помещается');return !!gridPreview.created;}
  const base=gridPreview?.base||snapshot(),measured=gridPreview?.measured||CardFlow.layoutMetrics();useSnapshot(base);
  gridPreview={base,measured,signature,created:null};
  const candidate=gridCandidate(cols,rows,pickerTarget,pickerSide);
  if(candidate){
   tree=candidate.tree;gridPreview.created=candidate.created;
   candidate.created.forEach(id=>{createUser(id).dataset.cellPreview='true';});
   height=Math.max(height,minimumHeight());preferredHeight=Math.max(preferredHeight,minimumHeight());apply({scheduleRefresh:false});CardFlow.previewLayout(measured);
  }else CardFlow.reflow(new Set(CardFlow.values()));
  if(picker)picker.querySelector('output').textContent=cols+' × '+rows+(candidate?'':' — не помещается');
  return !!candidate;
 }
 function addGrid(cols,rows,target='tools',side='bottom'){
  finish(true);clearSwap();const signature=[cols,rows,target,side].join('|');
  let created;
  if(gridPreview?.signature===signature&&gridPreview.created){
   created=gridPreview.created;gridPreview=null;
   created.forEach(id=>delete cells.get(id).dataset.cellPreview);
  }else{
   cancelGridPreview();const candidate=gridCandidate(cols,rows,target,side);
   if(!candidate)return false;
   tree=candidate.tree;created=candidate.created;created.forEach(id=>createUser(id));
  }
  closePicker();selectMany(created);
  changedLayout();return created;
 }
 function cellLabel(id){
  if(id==='tools')return 'Инструменты установа';
  const node=cells.get(id),list=ids().filter(imageCell),label=node?.dataset.label||'Изображение';
  return (list.indexOf(id)+1)+'. '+label;
 }
 function positionPicker(){
  if(!picker||picker.hidden)return;const b=local('cell-add').getBoundingClientRect();
  picker.style.left=Math.max(8,Math.min(innerWidth-picker.offsetWidth-8,b.left))+'px';
  picker.style.top=Math.max(8,Math.min(innerHeight-picker.offsetHeight-8,b.bottom+6))+'px';
 }
 function closePicker(focus=false){
  if(picker)picker.hidden=true;local('cell-add').setAttribute('aria-expanded','false');
  cancelGridPreview();
  if(focus)local('cell-add').focus({preventScroll:true});
 }
 function highlightGrid(cols,rows,focus=false){
  pickerCols=cols;pickerRows=rows;picker.querySelector('output').textContent=cols+' × '+rows;
  picker.querySelectorAll('[data-grid-col]').forEach(b=>{
   const inside=Number(b.dataset.gridCol)<=cols&&Number(b.dataset.gridRow)<=rows;
   b.classList.toggle('active',inside);b.setAttribute('aria-selected',String(inside));
   const current=Number(b.dataset.gridCol)===cols&&Number(b.dataset.gridRow)===rows;b.tabIndex=current?0:-1;if(current&&focus)b.focus();
  });
  previewGrid(cols,rows);
 }
 function makePicker(){
  picker=document.createElement('div');picker.id=prefix+'cell-grid-picker';picker.dataset.editorId='cell-grid-picker';picker.className='cell-grid-picker';picker.hidden=true;picker.setAttribute('role','dialog');picker.setAttribute('aria-label',DOC.title+' — Сетка изображений');
  picker.innerHTML='<div class="cell-picker-heading"><strong>Сетка изображений</strong><button type="button" class="cell-picker-close" aria-label="Закрыть">×</button></div><label>Рядом с <select class="cell-picker-target" aria-label="Блок для вставки"></select></label><label>Расположение <select class="cell-picker-side" aria-label="Расположение новых ячеек"><option value="bottom">Снизу</option><option value="top">Сверху</option><option value="right">Справа</option><option value="left">Слева</option></select></label><output aria-live="polite">1 × 1</output><div class="cell-picker-grid" role="grid" aria-label="Число столбцов и строк" aria-rowcount="6" aria-colcount="6"></div>';
  picker.querySelector('.cell-picker-heading strong').textContent=DOC.title+' — Сетка изображений';
  const area=picker.querySelector('.cell-picker-grid');
  for(let row=1;row<=6;row++){
   const line=document.createElement('div');line.setAttribute('role','row');
   for(let col=1;col<=6;col++){
    const b=document.createElement('button');b.type='button';b.dataset.gridCol=col;b.dataset.gridRow=row;b.setAttribute('role','gridcell');b.setAttribute('aria-label',col+' столбцов, '+row+' строк');line.append(b);
   }area.append(line);
  }
  picker.querySelector('.cell-picker-close').addEventListener('click',()=>closePicker(true));
  picker.querySelector('.cell-picker-target').addEventListener('change',e=>{pickerTarget=e.target.value;previewGrid(pickerCols,pickerRows);});
  picker.querySelector('.cell-picker-side').addEventListener('change',e=>{pickerSide=e.target.value;previewGrid(pickerCols,pickerRows);});
  area.addEventListener('pointerover',e=>{const b=e.target.closest('[data-grid-col]');if(b)highlightGrid(Number(b.dataset.gridCol),Number(b.dataset.gridRow));});
  area.addEventListener('click',e=>{const b=e.target.closest('[data-grid-col]');if(b)addGrid(Number(b.dataset.gridCol),Number(b.dataset.gridRow),pickerTarget,pickerSide);});
  area.addEventListener('keydown',e=>{
   const delta={ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]}[e.key];if(!delta)return;
   e.preventDefault();highlightGrid(clamp(pickerCols+delta[0],1,6),clamp(pickerRows+delta[1],1,6),true);
  });scope.append(picker);
 }
 function add(){
  finish(true);clearSwap();if(!picker)makePicker();else if(!picker.hidden){closePicker();return;}
  pickerTarget=cells.has(selected)?selected:'tools';pickerSide='bottom';
  const choices=picker.querySelector('.cell-picker-target');choices.replaceChildren();
  for(const id of ['tools',...ids().filter(imageCell)]){const option=document.createElement('option');option.value=id;option.textContent=cellLabel(id);choices.append(option);}
  choices.value=pickerTarget;picker.querySelector('.cell-picker-side').value=pickerSide;
  picker.hidden=false;highlightGrid(1,1);local('cell-add').setAttribute('aria-expanded','true');positionPicker();choices.focus({preventScroll:true});
 }
 function finish(cancel=false){
  const old=drag;drag=null;if(!old)return;
  if(old.kind==='swap'){
   cancelAnimationFrame(swapScroll);scope.classList.remove('cells-swapping');clearSwap();
   try{old.grip.releasePointerCapture(old.id);}catch(e){}
   if(!old.moved&&!cancel&&old.armed&&cells.has(old.armed)){swapFrom=old.armed;cells.get(old.armed).classList.add('cell-swap-source');}
   if(old.moved&&!cancel&&old.target){
    if(old.target.side==='swap')swap(old.source,old.target.id);else moveGroup(old.sources,old.target.id,old.target.side);
   }
   local('gallery-size-status').textContent='';return;
  }
  scope.classList.remove('gallery-sizing','gallery-sizing-column','gallery-sizing-row');old.grip.classList.remove('dragging');
  try{old.grip.releasePointerCapture(old.id);}catch(e){}
  if(cancel){tree=old.tree;preferredHeight=old.preferredHeight;height=old.height;}
  if(old.kind==='height')CardFlow.finishHeight(old.flow,cancel||!old.moved);
  apply({refreshNow:true});if(!cancel&&old.moved)markChanged();
 }
 root.addEventListener('pointerdown',e=>{
  if(e.button!==0||e.isPrimary===false)return;
  const cell=e.target.closest('[data-cell-id]'),id=cell?.dataset.cellId;
  if(cell&&(e.ctrlKey||e.metaKey)){
   e.preventDefault();e.stopImmediatePropagation();PhotoPan.finish();finish(true);clearSwap();select(id,false,true);suppressPickClick=true;return;
  }
  if(cell&&picker&&!picker.hidden){
   e.preventDefault();e.stopImmediatePropagation();suppressPickClick=true;
   if(gridPreview?.base.cells.has(id)){pickerTarget=id;picker.querySelector('.cell-picker-target').value=id;previewGrid(pickerCols,pickerRows);}return;
  }
  suppressPickClick=false;
  if(cell)select(id,false,false,!!e.target.closest('.cell-move')&&selection.has(id));
  const grip=e.target.closest('.gallery-grip,.gallery-height-grip,.cell-move');if(!grip||e.button!==0||e.isPrimary===false)return;
  e.preventDefault();e.stopPropagation();closePicker();PhotoPan.finish();finish(true);finishColumnDrag();grip.focus({preventScroll:true});
  if(grip.matches('.cell-move')){
   const source=cell.dataset.cellId,armed=swapFrom;suppressMoveClick=false;clearSwap();
   drag={kind:'swap',id:e.pointerId,grip,source,sources:selection.has(source)?[...selection]:[source],armed,target:null,startX:e.clientX,startY:e.clientY,x:e.clientX,y:e.clientY,moved:false};
   try{grip.setPointerCapture(e.pointerId);}catch(error){}return;
  }
  clearSwap();
  if(grip.matches('.gallery-height-grip')){
   drag={kind:'height',id:e.pointerId,grip,tree:copy(tree),height,preferredHeight,flow:CardFlow.heightState(),rect:root.getBoundingClientRect(),x:e.clientX,y:e.clientY,moved:false};
   scope.classList.add('gallery-sizing','gallery-sizing-row');grip.classList.add('dragging');
   try{grip.setPointerCapture(e.pointerId);}catch(error){}return;
  }
  const {node,box}=splits.get(grip.dataset.splitPath),rect=root.getBoundingClientRect();
  drag={kind:'split',id:e.pointerId,grip,key:grip.dataset.splitPath,axis:node.axis,ratio:node.ratio,tree:copy(tree),height,preferredHeight,box,rect,x:e.clientX,y:e.clientY,moved:false};
  scope.classList.add('gallery-sizing',node.axis==='x'?'gallery-sizing-column':'gallery-sizing-row');grip.classList.add('dragging');
  try{grip.setPointerCapture(e.pointerId);}catch(error){}
 },true);
 scope.addEventListener('pointermove',e=>{
  if(!drag||drag.id!==e.pointerId)return;e.preventDefault();
  if(drag.kind==='swap'){
   drag.x=e.clientX;drag.y=e.clientY;
   if(!drag.moved&&Math.hypot(e.clientX-drag.startX,e.clientY-drag.startY)<5)return;
   if(!drag.moved){drag.moved=true;scope.classList.add('cells-swapping');drag.sources.forEach(id=>cells.get(id).classList.add('cell-swap-source'));scrollSwap();}
   showSwapTarget();return;
  }
  if(drag.kind==='height'){
   const delta=(e.clientY-drag.y)*drag.height/drag.rect.height;
   if(Math.abs(delta)<.05&&!drag.moved)return;tree=copy(drag.tree);drag.moved=true;resizeHeight(drag.height+delta,drag.flow);return;
  }
  const delta=drag.axis==='x'?(e.clientX-drag.x)/(drag.rect.width*drag.box.w):(e.clientY-drag.y)/(drag.rect.height*drag.box.h);
  if(!Number.isFinite(delta)||Math.abs(delta)<.0001)return;const node=at(drag.key);node.ratio=ratio(node,drag.box,drag.ratio+delta);drag.moved=true;apply();
 },{passive:false});
 let suppressMoveClick=false;
 scope.addEventListener('pointerup',e=>{
  if(drag?.id!==e.pointerId)return;
  if(drag.kind==='swap'){
   suppressMoveClick=drag.moved;
   if(drag.moved)drag.target=swapTarget(e.clientX,e.clientY);
  }
  finish();
 });
 scope.addEventListener('pointercancel',e=>{if(drag?.id===e.pointerId)finish(true);});
 root.addEventListener('lostpointercapture',e=>{if(drag?.id===e.pointerId)finish(true);});
 root.addEventListener('click',e=>{
  if(suppressPickClick||((e.ctrlKey||e.metaKey)&&e.target.closest('[data-cell-id]'))){suppressPickClick=false;e.preventDefault();e.stopImmediatePropagation();return;}
  if(e.target.closest('.cell-move')){
   e.preventDefault();e.stopPropagation();if(suppressMoveClick){suppressMoveClick=false;return;}
   const id=e.target.closest('[data-cell-id]').dataset.cellId;
   if(selection.size>1){clearSwap();return;}
   if(swapFrom&&swapFrom!==id){swap(swapFrom,id);return;}
   const previous=swapFrom;clearSwap();if(previous===id)return;
   swapFrom=id;cells.get(id).classList.add('cell-swap-source');e.target.closest('.cell-move').setAttribute('aria-pressed','true');
   return;
  }
  if(e.target.closest('.cell-remove')){e.preventDefault();e.stopPropagation();remove(e.target.closest('[data-cell-id]').dataset.cellId);return;}
  const cell=e.target.closest('.user-photo');if(cell&&!e.target.closest('button')){select(cell.dataset.cellId,true);}
 },true);
 root.addEventListener('dblclick',e=>{
  const grip=e.target.closest('.gallery-grip,.gallery-height-grip');if(!grip)return;e.preventDefault();finish();
  if(grip.matches('.gallery-height-grip')){preferredHeight=225.5;setHeight(availableHeight);}else{at(grip.dataset.splitPath).ratio=.5;apply();}markChanged();
 });
 scope.addEventListener('keydown',e=>{
  if(e.key==='Escape'&&picker&&!picker.hidden){e.preventDefault();closePicker(true);return;}
  if(e.key==='Escape'&&(drag||swapFrom)){e.preventDefault();suppressMoveClick=drag?.kind==='swap';finish(true);clearSwap();return;}
  if(e.key==='Escape'&&selection.size){select(null);return;}
  if(e.target.closest('.gallery-height-grip')){
   if(['ArrowUp','ArrowDown','Home','End'].includes(e.key)){
    e.preventDefault();const flow=CardFlow.heightState();resizeHeight(e.key==='Home'?minimumHeight():e.key==='End'?CardFlow.availableHeight():height+(e.key==='ArrowDown'?1:-1)*(e.shiftKey?5:1),flow);CardFlow.finishHeight(flow,false);markChanged();
   }return;
  }
  const grip=e.target.closest('.gallery-grip');if(!grip)return;const {node,box}=splits.get(grip.dataset.splitPath);
  const keys=node.axis==='x'?['ArrowLeft','ArrowRight']:['ArrowUp','ArrowDown'];
  if(keys.includes(e.key)){e.preventDefault();node.ratio=ratio(node,box,node.ratio+(e.key===keys[1]?1:-1)*(e.shiftKey?5:1)/(node.axis==='x'?W*box.w:height*box.h));apply();markChanged();}
 });
 root.addEventListener('keydown',e=>{
  const id=e.target.closest('[data-cell-id]')?.dataset.cellId;
  if((e.ctrlKey||e.metaKey)&&e.code==='Space'&&imageCell(id)){e.preventDefault();e.stopImmediatePropagation();select(id,false,true);}
 },true);
 scope.addEventListener('paste',e=>{
  if(e.target.closest('[contenteditable],input,textarea'))return;
  const node=e.target.closest('.user-photo')||cells.get(selected);if(!node?.matches('.user-photo'))return;
  const file=[...e.clipboardData?.items||[]].find(item=>item.type.startsWith('image/'))?.getAsFile();
  if(!file)return;
  e.preventDefault();const reader=new FileReader();
  reader.onload=()=>{if(!node.isConnected)return;try{putImage(node,reader.result);markChanged();}catch(error){}};
  reader.readAsDataURL(file);
 });
 local('cell-add').addEventListener('click',add);
 local('cell-undo').addEventListener('click',undoRemove);
 local('cell-clear-selection').addEventListener('click',()=>{clearSwap();select(null);});
 document.addEventListener('pointerdown',e=>{
  if(picker&&!picker.hidden&&!picker.contains(e.target)&&!local('cell-add').contains(e.target)&&!root.contains(e.target))closePicker();
 },true);
 document.addEventListener('keydown',e=>{
  if(e.key!=='Escape')return;
  if(picker&&!picker.hidden){e.preventDefault();closePicker(true);}
  if(drag||swapFrom){e.preventDefault();finish(true);clearSwap();}
 });
 window.addEventListener('resize',positionPicker);
 window.addEventListener('scroll',positionPicker,{passive:true});
 local('gallery-reset').addEventListener('click',()=>{
  finish(true);closePicker();clearSwap();preferredHeight=225.5;
  // Reset proportions, preserving every user-added cell and every deletion.
  function reset(n){if(!n.id){n.ratio=.5;reset(n.a);reset(n.b);}}
  reset(tree);setHeight(availableHeight);markChanged();
 });
 window.addEventListener('blur',()=>{closePicker();finish(true);});
 let stored={};try{stored=JSON.parse(root.dataset.galleryLayout||'{}');}catch(e){}
 restore(stored);
 return{values,restore,finish,closePicker,add,addGrid,moveGroup,remove,undoRemove,putImage,imageSource,
  minimumHeight,setHeight,swap,restoreGeometry,deletedOriginalCells:()=>Object.fromEntries([...originalCells].filter(([id])=>!cells.has(id)).map(([id,node])=>[id,node.outerHTML])),
  cleanClone:clone=>{clone.classList.remove('gallery-sizing','gallery-sizing-column','gallery-sizing-row','cells-swapping');clone.querySelectorAll('.gallery-grip,.gallery-height-grip,.cell-selected,.cell-swap-source,.cell-swap-target').forEach(n=>n.classList.remove('dragging','cell-selected','cell-swap-source','cell-swap-target'));clone.querySelectorAll('.cell-move').forEach(n=>n.setAttribute('aria-pressed','false'));clone.querySelectorAll('[data-editor-id="cell-grid-picker"],.cell-drop-preview').forEach(n=>n.remove());clone.querySelector('[data-editor-id="cell-add"]').setAttribute('aria-expanded','false');clone.querySelector('[data-editor-id="cell-selection-status"]').textContent='';clone.querySelector('[data-editor-id="cell-clear-selection"]').hidden=true;clone.querySelector('[data-editor-id="cell-undo"]').disabled=true;clone.querySelector('[data-editor-id="gallery-size-status"]').textContent='';},
  geometry:()=>({height,preferredHeight,availableHeight,tree:copy(tree),rects:[...rects].map(([id,b])=>({id,...b}))})};
})();

const CardFlow=(()=>{
 const pages=local('pages'),cover=pages.querySelector('.cover-page'),content=cover.querySelector('.page-content'),mm=96/25.4;
 const source=local('operation-source'),master=[...source.content.querySelectorAll('tbody>tr')];
 master.forEach((row,i)=>row.dataset.rowId=String(i));
 const opIds=master.filter(r=>r.dataset.kind==='operation').map(r=>r.dataset.rowId),opSet=new Set(opIds),children=new Map();
 master.forEach((r,i)=>{
  if(r.dataset.kind!=='group')return;const list=[];
  for(let j=i+1;j<master.length&&Number(master[j].dataset.depth)>Number(r.dataset.depth);j++)if(master[j].dataset.kind==='operation')list.push(master[j].dataset.rowId);
  children.set(r.dataset.rowId,list);
 });
 const template=local('continuation-template');
 let area=content.querySelector('.cover-operations');
 if(!area){area=template.content.querySelector('.rows-area').cloneNode(true);area.classList.add('cover-operations');content.querySelector('.page-footer').before(area);}
 let catalog=local('catalog-source');
 if(!catalog){
  catalog=document.createElement('template');catalog.id=prefix+'catalog-source';catalog.dataset.editorId='catalog-source';const old=[...pages.querySelectorAll('.project-tools-page')];
  if(old.length){
   const page=old[0].cloneNode(true),table=page.querySelector('.catalog-table');
   const rows=old.flatMap(p=>[...p.querySelectorAll('.catalog-table tbody>tr:not([data-repeat])')]);
   table.remove();page.querySelectorAll('.catalog-columns,.catalog-scroll,.catalog-table').forEach(n=>n.remove());
   table.tBodies[0].replaceChildren(...rows.map(r=>r.cloneNode(true)));
   page.querySelector('.page-footer').before(table);catalog.content.append(page);
  }
  template.after(catalog);
 }
 // Older saved HTML may still contain a placement button or scaled columns.
 catalog.content.querySelectorAll('.catalog-first').forEach(button=>button.remove());
 catalog.content.querySelectorAll('.catalog-table').forEach(table=>{
  for(const property of ['position','left','top','transform','transform-origin','width'])table.style.removeProperty(property);
 });
 let committed=new Set(),selection=new Set(),anchor=null,drag=null,ghost=null,busy=false;
 const status=local('operations-status');
 let placement=cover.dataset.coverPlacement||(cover.hasAttribute('data-cover-operations')?'manual':'auto');
 function validIds(values){return new Set(Array.isArray(values)?values.filter(id=>typeof id==='string'&&opSet.has(id)):[]);}
 try{committed=validIds(JSON.parse(cover.dataset.coverOperations||'[]'));}catch(e){}
 function markSelection(){
  pages.querySelectorAll('.ops tbody tr').forEach(row=>{const ids=children.get(row.dataset.rowId)||[row.dataset.rowId];const on=ids.length>0&&ids.every(id=>selection.has(id));row.classList.toggle('operation-selected',on);row.setAttribute('aria-selected',String(on));});
  local('operations-to-cover').disabled=!selection.size;local('operations-to-next').disabled=!selection.size;
  status.textContent=selection.size?'Выбрано операций: '+selection.size:'';
 }
 function subset(selected){
  const output=[];
  for(const row of master){
   const id=row.dataset.rowId,group=row.dataset.kind==='group',desc=children.get(id)||[],count=desc.filter(k=>selected.has(k)).length;
   if(group?!count:!selected.has(id))continue;
   const clone=row.cloneNode(true);
   if(group&&count<desc.length){
    clone.dataset.partial='true';const name=clone.querySelector('.tree-name');name.append(document.createTextNode(' · часть'));
    const time=clone.querySelector('[data-column-id="time"] .cell-text');if(time){time.textContent='—';time.title='Часть папки; полное время папки смотрите в NX';}
   }
   output.push(clone);
  }
  return output;
 }
 function decorate(root){
  root.querySelectorAll('.ops tbody tr').forEach(row=>{
   const cell=row.cells[0];if(!cell.querySelector('.operation-grip')){
    const handle=document.createElement('button');handle.type='button';handle.className='operation-grip';handle.textContent='⋮⋮';
    handle.title='Перетащить на первый лист или обратно. Enter — выбрать строку.';handle.setAttribute('aria-label','Перенести '+(cell.title||'операцию'));cell.prepend(handle);
   }
  });
 }
 function measurePage(page,key){
  page.style.cssText='position:absolute;left:-20000px;top:0;visibility:hidden;';page.setAttribute('aria-hidden','true');PageScale.apply(page,key);scope.append(page);applyValues(values());NoteLayout.apply(page,false);return page;
 }
 function metrics(rows=master,key='cover'){
  const p=measurePage(template.content.firstElementChild.cloneNode(true),key),table=p.querySelector('.ops'),tbody=table.tBodies[0];
  // Measure with the same screen zoom: subpixel table borders otherwise round
  // differently on the hidden page and can push its last row below the sheet.
  const screen=window.matchMedia('print').matches?1:paperScreenScale;p.style.zoom=String(screen);
  const repeated=rows.map(r=>r.dataset.kind==='group'?repeatGroup(r):r.cloneNode(true));
  tbody.replaceChildren(...rows.map(r=>r.cloneNode(true)),...repeated.map(r=>r.cloneNode(true)));decorate(p);ColumnOptions.apply(table);fitDataTables([table]);
  const minimum=3.8*PageScale.factor(key)*mm*(columnMetrics.get(table)?.widthScale||1),heights=[...tbody.rows].map(r=>Math.max(minimum,r.getBoundingClientRect().height/screen));
  const head=table.tHead.getBoundingClientRect(),end=p.querySelector('.rows-area').getBoundingClientRect();
  const header=head.height/screen||minimum,budget=(end.bottom-head.bottom)/screen-14;
  p.remove();return{heights:heights.slice(0,rows.length),repeatHeights:heights.slice(rows.length),header,budget:Math.max(minimum,budget),rows};
 }
 function coverHeight(ids,m){
  if(!ids.size||!ColumnOptions.any())return 0;
  const heights=new Map(m.rows.map((r,i)=>[r.dataset.rowId,r.dataset.kind==='group'?Math.max(m.heights[i],m.repeatHeights[i]):m.heights[i]]));
  return(subset(ids).reduce((sum,r)=>sum+(heights.get(r.dataset.rowId)||3.8*PageScale.factor('cover')*mm),0)+m.header+14)/mm;
 }
 const COVER_OPERATION_LIMIT=25;
 let limitEnabled=cover.dataset.operationLimit!=='false';
 PageScale.apply(cover,'cover',true);
 let limitControl=cover.querySelector('[data-operation-limit]');
 if(!limitControl){const label=document.createElement('label');label.className='operation-limit-control';limitControl=document.createElement('input');limitControl.type='checkbox';limitControl.dataset.operationLimit='true';label.append(limitControl,document.createTextNode('Лимит на 25 операций'));cover.querySelector(':scope>.page-controls').append(label);}
 function operationLimit(){return limitEnabled?COVER_OPERATION_LIMIT:Infinity;}
 function syncLimit(){cover.dataset.operationLimit=String(limitEnabled);limitControl.checked=limitEnabled;limitControl.toggleAttribute('checked',limitEnabled);}
 syncLimit();
 limitControl.addEventListener('change',()=>{limitEnabled=limitControl.checked;syncLimit();reflow();markChanged();});
 const sameIds=(a,b)=>a.size===b.size&&[...a].every(id=>b.has(id));
 function availableHeight(){
  const header=content.querySelector('.card-header'),footer=content.querySelector('.page-footer'),scale=screenPaperScale(cover);
  return Math.max(0,(footer.getBoundingClientRect().top-header.getBoundingClientRect().bottom)/scale/mm-(parseFloat(getComputedStyle(header).marginBottom)||0)/mm);
 }
 function fits(ids,m=metrics()){return !ids.size||ids.size<=operationLimit()&&coverHeight(ids,m)<=availableHeight()-GalleryLayout.minimumHeight()-2+.01;}
 function fitted(ids,m=metrics()){
  const chosen=new Set(ids);
  for(const id of [...opIds].reverse()){if(fits(chosen,m))break;chosen.delete(id);}return chosen;
 }
 function autoCover(m){
  if(!ColumnOptions.any())return new Set();
  // Count operations, not folder headers; a folder may continue on the next sheet.
  return fitted(new Set(limitEnabled?opIds.slice(0,COVER_OPERATION_LIMIT):opIds),m);
 }
 function operationSheets(rows,shared=null){
  if(!rows.length||!ColumnOptions.any())return[];
  const count=rows.length,ends=rows.map((_,i)=>i+1),parents=[],stack=[],cache=new Map();
  rows.forEach((r,i)=>{const depth=Number(r.dataset.depth||0);while(stack.length&&Number(rows[stack.at(-1)].dataset.depth||0)>=depth)ends[stack.pop()]=i;parents.push([...stack]);if(r.dataset.kind==='group')stack.push(i);});stack.forEach(i=>ends[i]=count);
  function measure(index){
   const key='operations-'+index,wrap=PageScale.wrapValue(key),scale=PageScale.value(key)+'|'+wrap+'|'+NoteLayout.value(key);
   if(!cache.has(scale)){
    let m;
    // During a gesture, unchanged unwrapped row heights can be reused by ID.
    if(shared&&!wrap){
     if(!shared.has(scale))shared.set(scale,metrics(master,key));
     const full=shared.get(scale),positions=new Map(full.rows.map((r,i)=>[r.dataset.rowId,i]));
     m={...full,rows,heights:rows.map(r=>full.heights[positions.get(r.dataset.rowId)]),repeatHeights:rows.map(r=>full.repeatHeights[positions.get(r.dataset.rowId)])};
    }else m=metrics(rows,key);
    m.sums=[0];m.heights.forEach(h=>m.sums.push(m.sums.at(-1)+h));cache.set(scale,m);
   }return cache.get(scale);
  }
  const result=[];let pageIndex=1,m=measure(pageIndex),current=[],used=0,i=0;
  while(i<count){
   const contextHeight=parents[i].reduce((n,j)=>n+m.repeatHeights[j],0);let end=i+1;
   if(rows[i].dataset.kind==='group'){
    let j=i;while(j<ends[i]&&rows[j].dataset.kind==='group'){
     const nested=rows.slice(j+1,ends[j]).some(r=>r.dataset.kind==='group');
     if(!nested&&contextHeight+m.sums[ends[j]]-m.sums[i]<=m.budget){end=ends[j];break;}j++;end=Math.min(j+1,ends[i]);
    }
   }
   const height=m.sums[end]-m.sums[i];
   if(used+height>m.budget&&current.some(([,repeat])=>!repeat)){
    result.push({key:'operations-'+pageIndex,items:current});pageIndex++;m=measure(pageIndex);
    current=parents[i].map(j=>[j,true]);used=parents[i].reduce((n,j)=>n+m.repeatHeights[j],0);continue;
   }
   for(let j=i;j<end;j++)current.push([j,false]);used+=height;i=end;
  }
  if(current.length)result.push({key:'operations-'+pageIndex,items:current});return result;
 }
 function catalogPages(data){
  const saved=catalog.content.firstElementChild;if(!saved){pages.querySelectorAll('.project-tools-page').forEach(p=>p.remove());return;}
  const rows=[...saved.querySelectorAll('.catalog-table tbody>tr:not([data-repeat])')],probe=measurePage(saved.cloneNode(true),'catalog');applyValues(data);
  const bottom=probe.dataset.projectToolsBottom?probe.querySelector('.page-content').getBoundingClientRect().top+Number(probe.dataset.projectToolsBottom)*mm:probe.querySelector('.page-content').getBoundingClientRect().bottom-4.5*mm;
  const top=probe.querySelector('.catalog-table thead').getBoundingClientRect().bottom;
  const row=Math.max(3.8*PageScale.factor('catalog')*mm,...[...probe.querySelectorAll('.catalog-table tbody>tr')].slice(0,10).map(r=>r.getBoundingClientRect().height));
  const budget=(bottom>top?bottom-top:221*mm)-12;probe.remove();
  // Use two columns before scaling; never create continuation catalog pages.
  const plan=projectToolPlan(rows,budget/row),count=plan.length;
  {
   const page=saved.cloneNode(true),table=page.querySelector('.catalog-table');
   table.remove();table.tBodies[0].replaceChildren();
   const columns=document.createElement('div');columns.className='catalog-columns';columns.dataset.columns=String(count);
   for(const chunk of plan){
    const column=table.cloneNode(true),viewport=document.createElement('div');viewport.className='catalog-scroll';
    column.tBodies[0].replaceChildren(...chunk.map(([i,repeat])=>{
     const r=rows[i].cloneNode(true);
     if(repeat){
      r.dataset.repeat='true';const label=r.querySelector('.tool-group-label'),cell=r.querySelector('.tool-name-cell');
      if(label)label.append(document.createTextNode(' · продолжение'));
      if(cell){cell.title+=' · продолжение';cell.querySelector('.cell-text').title=cell.title;}
     }return r;
    }));
    viewport.append(column);columns.append(viewport);
   }
   page.querySelector('.page-footer').before(columns);
   page.querySelector('.catalog-title').textContent='Инструменты проекта';
   const live=pages.querySelector('.project-tools-page');
   if(live){live.querySelector('.page-content').replaceWith(page.querySelector('.page-content'));PageScale.apply(live,'catalog');if(cover.previousElementSibling!==live)cover.before(live);}
   else{PageScale.apply(page,'catalog');cover.before(page);} 
   ProjectModel.queue();
  }
 }
 function dropSheet(){
  if(!drag?.moved||pages.querySelector('.operation-page'))return;
  const page=template.content.firstElementChild.cloneNode(true);page.classList.add('operation-drop-page');
  page.querySelector('.ops tbody').replaceChildren();const hint=document.createElement('p');hint.className='operation-drop-hint';
  hint.textContent='Отпустите операции на этом листе, чтобы убрать их с первого листа.';page.querySelector('.rows-area').append(hint);cover.after(page);
 }
 function highlightDrop(target,valid){
  pages.querySelectorAll('.operation-drop-active').forEach(p=>p.classList.remove('operation-drop-active'));
  if(valid){if(target==='cover')cover.classList.add('operation-drop-active');else pages.querySelectorAll('.operation-page').forEach(p=>p.classList.add('operation-drop-active'));}
  local('operations-to-cover').classList.toggle('drop-active',target==='cover'&&valid);
  local('operations-to-next').classList.toggle('drop-active',target==='next'&&valid);
 }
 function reflow(preview=null,measured=null){
  if(busy)return;busy=true;
  try{
   const data=values();PageScale.apply(cover,'cover');NoteLayout.apply(cover);
   // Scales belong to physical sheet positions, independently of total page count.
   let legacyIndex=0;pages.querySelectorAll('.operation-page').forEach(p=>{if(!p.dataset.pageKey)p.dataset.pageKey='operations-'+(++legacyIndex);});
   const oldPages=new Map([...pages.querySelectorAll('.operation-page')].map(p=>[p.dataset.pageKey,p]));
   const m=measured||metrics();if(preview===null&&placement==='auto')committed=autoCover(m);
   let chosen=preview===null?new Set(committed):new Set(preview);
   if(preview===null&&!fits(chosen,m)){
    for(const id of [...opIds].reverse()){if(fits(chosen,m))break;chosen.delete(id);}
    committed=chosen;
   }
   const height=coverHeight(chosen,m);area.hidden=!height;area.style.height=height+'mm';area.querySelector('.ops tbody').replaceChildren(...subset(chosen));
   const rows=subset(new Set(opIds.filter(id=>!chosen.has(id)))),repeated=rows.map(r=>r.dataset.kind==='group'?repeatGroup(r):r.cloneNode(true));
   let previous=cover;
   for(const {key,items} of operationSheets(rows,measured?.pageMetrics)){
    const page=oldPages.get(key)||template.content.firstElementChild.cloneNode(true);oldPages.delete(key);page.classList.remove('operation-drop-page');page.querySelector('.operation-drop-hint')?.remove();
    PageScale.apply(page,key);page.querySelector('.ops tbody').replaceChildren(...items.map(([i,repeat])=>(repeat?repeated[i]:rows[i]).cloneNode(true)));
    if(previous.nextElementSibling!==page)previous.after(page);previous=page;
   }
   oldPages.forEach(page=>page.remove());if(preview===null)catalogPages(data);dropSheet();applyValues(data);
   NoteLayout.refresh();GalleryLayout.setHeight(availableHeight()-(height?height+2:0),false);
   PageScale.refresh();decorate(pages);fitDataTables();positionMcsAnchors();fitFields();renumber();markSelection();
   cover.dataset.coverOperations=JSON.stringify(opIds.filter(id=>committed.has(id)));cover.dataset.coverPlacement=placement;
   cover.classList.toggle('has-cover-operations',!!height);
  }finally{busy=false;}
 }
 function candidate(ids,target){const next=new Set(committed);ids.forEach(id=>target==='cover'?next.add(id):next.delete(id));return next;}
 function move(ids,target){
  const next=candidate(ids,target);if(!fits(next))return false;
  committed=next;placement='manual';reflow();markChanged();return true;
 }
 function layoutMetrics(){const m=metrics();m.pageMetrics=new Map([[PageScale.value('cover')+'|'+PageScale.wrapValue('cover')+'|'+NoteLayout.value('cover'),m]]);return m;}
 function heightState(){return{ids:new Set(committed),m:layoutMetrics(),preview:new Set(committed)};}
 function previewHeight(value,state){
  const next=new Set(state.ids),budget=availableHeight()-value-2;
  for(const id of [...opIds].reverse()){if(next.size<=operationLimit()&&coverHeight(next,state.m)<=budget+.01)break;next.delete(id);}
  for(const id of opIds){
   if(next.has(id))continue;if(next.size>=operationLimit())break;
   next.add(id);if(coverHeight(next,state.m)>budget+.01){next.delete(id);break;}
  }
  if(!sameIds(next,state.preview)){state.preview=next;reflow(next,state.m);}
  return next.size;
 }
 function finishHeight(state,cancel){
  if(!cancel){committed=new Set(state.preview);placement='manual';}
  reflow(cancel?new Set(committed):null,state.m);
 }
 function selectRow(row,event){
  const keys=children.get(row.dataset.rowId)||[row.dataset.rowId];
  if(event.shiftKey&&anchor&&opSet.has(row.dataset.rowId)){
   const a=opIds.indexOf(anchor),b=opIds.indexOf(row.dataset.rowId);if(!event.ctrlKey&&!event.metaKey)selection.clear();
   opIds.slice(Math.min(a,b),Math.max(a,b)+1).forEach(id=>selection.add(id));
  }else if(event.ctrlKey||event.metaKey){const remove=keys.every(id=>selection.has(id));keys.forEach(id=>remove?selection.delete(id):selection.add(id));}
  else selection=new Set(keys);
  if(opSet.has(row.dataset.rowId))anchor=row.dataset.rowId;markSelection();
 }
 pages.addEventListener('click',e=>{
  if(e.target.closest('.column-grip'))return;const row=e.target.closest('.ops tbody tr');if(!row)return;
  if(Date.now()<suppressClickUntil){e.preventDefault();return;}selectRow(row,e);
 });
 let suppressClickUntil=0,scrollFrame=0;
 function targetAt(x,y){
  const hit=document.elementFromPoint?.(x,y);
  if(hit&&!scope.contains(hit))return null;
  if(hit?.closest('[data-editor-id="operations-to-cover"]'))return'cover';
  if(hit?.closest('[data-editor-id="operations-to-next"],.operation-page'))return'next';
  if(hit?.closest('.cover-page'))return'cover';
  // Page gaps and a temporary continuation sheet remain valid on the way back.
  // The whole following paper area is a target, even when it has no rows yet.
  if(hit?.closest('.toolbar'))return null;
  const paper=cover.getBoundingClientRect(),end=pages.getBoundingClientRect();
  if(x>=paper.right&&x<=end.right&&y>=paper.top&&y<=paper.bottom)return'next';return null;
 }
 function previewTarget(target){
  if(!drag)return;
  const hit=document.elementFromPoint(drag.x,drag.y),button=hit?.closest('[data-editor-id="operations-to-cover"],[data-editor-id="operations-to-next"]');
  const dx=drag.x+window.scrollX-drag.startX-drag.startScrollX,dy=drag.y+window.scrollY-drag.startY-drag.startScroll;
  const delta=Math.abs(dx)>Math.abs(dy)?dx:dy;
  if(target&&!button)target=delta>0?'next':delta<0?'cover':null;
  const next=new Set(committed);let moved=0,eligible=[];
  if(target){
   eligible=opIds.filter(id=>drag.ids.includes(id)&&(target==='cover'?!committed.has(id):committed.has(id)));
   if(target==='next')eligible.reverse();
   const count=button?eligible.length:Math.min(eligible.length,Math.floor((Math.abs(delta)+drag.step*.25)/drag.step));
   for(const id of eligible.slice(0,count)){
    if(target==='cover'){next.add(id);if(!fits(next,drag.m)){next.delete(id);break;}}
    else next.delete(id);moved++;
   }
  }
  drag.target=target;drag.valid=!!target;
  if(!sameIds(next,drag.preview)){
   drag.preview=next;GalleryLayout.restoreGeometry(drag.gallery,false);reflow(next,drag.m);
  }
  highlightDrop(target,drag.valid);
  if(ghost)ghost.textContent=!target?'Наведите на лист или кнопку переноса':(target==='cover'?'На первый лист: ':'На следующие листы: ')+moved+' из '+eligible.length+' · отпустите для применения';
 }
 function autoScroll(){
  if(!drag?.moved)return;
  // Scroll along the paper strip as well as vertically within a tall A4 sheet.
  const toolbar=scope.querySelector('.toolbar').getBoundingClientRect().bottom;
  const delta=drag.y<toolbar+25?-12:drag.y>innerHeight-50?12:0;
  const dx=drag.x<40?-12:drag.x>innerWidth-40?12:0;
  if(dx||delta){window.scrollBy(dx,delta);previewTarget(targetAt(drag.x,drag.y));}
  scrollFrame=requestAnimationFrame(autoScroll);
 }
 pages.addEventListener('pointerdown',e=>{
  // A fresh gesture must not be mistaken for the click following the last drag.
  suppressClickUntil=0;
  const row=e.target.closest('.ops tbody tr');
  if(!row||e.target.closest('.column-grip,input,textarea,[contenteditable="true"]')||e.button!==0||e.isPrimary===false)return;e.preventDefault();
  PhotoPan.finish();GalleryLayout.closePicker();GalleryLayout.finish(true);finishColumnDrag();
  // Resolve selection only when movement starts, so Ctrl-click still toggles once.
  drag={id:e.pointerId,row,keys:children.get(row.dataset.rowId)||[row.dataset.rowId],modifiers:{ctrlKey:e.ctrlKey,metaKey:e.metaKey,shiftKey:e.shiftKey},ids:[],startX:e.clientX,startY:e.clientY,startScroll:window.scrollY,startScrollX:window.scrollX,x:e.clientX,y:e.clientY,moved:false,target:null,valid:false,preview:new Set(committed),gallery:GalleryLayout.values(),step:Math.max(12,row.getBoundingClientRect().height)};
  try{scope.setPointerCapture(e.pointerId);}catch(error){}
 });
 scope.addEventListener('pointermove',e=>{
  if(!drag||drag.id!==e.pointerId)return;drag.x=e.clientX;drag.y=e.clientY;
  if(!drag.moved&&Math.hypot(e.clientX-drag.startX,e.clientY-drag.startY)<5)return;e.preventDefault();
  if(!drag.moved){
   if(!drag.keys.every(id=>selection.has(id)))selectRow(drag.row,drag.modifiers);drag.ids=[...selection];
   drag.moved=true;drag.m=layoutMetrics();ghost=document.createElement('div');ghost.className='operation-drag-ghost';ghost.textContent=drag.ids.length+' операций';scope.append(ghost);scope.classList.add('operations-dragging');
   dropSheet();renumber();autoScroll();
  }
  ghost.style.left=Math.min(innerWidth-250,e.clientX+14)+'px';ghost.style.top=e.clientY+14+'px';previewTarget(targetAt(e.clientX,e.clientY));
 },{passive:false});
 function finish(cancel=false){
  const was=drag;if(!was)return;drag=null;cancelAnimationFrame(scrollFrame);ghost?.remove();ghost=null;scope.classList.remove('operations-dragging');
  highlightDrop(null,false);
  try{scope.releasePointerCapture(was.id);}catch(e){}
  if(was.moved){
   suppressClickUntil=Date.now()+400;
   if(!cancel&&was.valid&&!sameIds(was.preview,committed)){committed=new Set(was.preview);placement='manual';reflow();markChanged();}
   else{GalleryLayout.restoreGeometry(was.gallery,false);reflow(new Set(committed));}
  }
 }
 scope.addEventListener('pointerup',e=>{
  if(drag?.id!==e.pointerId)return;
  if(!drag.moved){selectRow(drag.row,drag.modifiers);suppressClickUntil=Date.now()+400;}
  finish();
 });
 scope.addEventListener('pointercancel',e=>{if(drag?.id===e.pointerId)finish(true);});
 scope.addEventListener('lostpointercapture',e=>{if(drag?.id===e.pointerId)finish(true);});
 document.addEventListener('keydown',e=>{if(e.key==='Escape'&&drag){e.preventDefault();finish(true);}});
 window.addEventListener('blur',()=>finish(true));
 local('operations-to-cover').addEventListener('click',()=>move([...selection],'cover'));
 local('operations-to-next').addEventListener('click',()=>move([...selection],'next'));
 function refreshCatalog(){if(busy||!catalog.content.firstElementChild)return;busy=true;try{const data=values();catalogPages(data);applyValues(data);PageScale.refresh();fitDataTables();fitFields();renumber();}finally{busy=false;}}
 return{reflow,refreshCatalog,finish,move,availableHeight,layoutMetrics,heightState,previewHeight,finishHeight,previewLayout:m=>reflow(fitted(committed,m),m),galleryValues:()=>drag?.moved?drag.gallery:GalleryLayout.values(),hasCatalog:()=>!!catalog.content.firstElementChild,
  catalogContent:()=>catalog.content.cloneNode(true),setCatalog:content=>{catalog.content.replaceChildren(...(content?[content]:[]));pages.querySelectorAll('.project-tools-page').forEach(page=>page.remove());},
  values:()=>opIds.filter(id=>committed.has(id)),mode:()=>placement,limit:()=>limitEnabled,restore:(value,mode,limit)=>{committed=validIds(value);placement=mode==='auto'?'auto':'manual';if(typeof limit==='boolean')limitEnabled=limit;syncLimit();},
  cleanClone:clone=>{clone.querySelectorAll('.operation-selected').forEach(n=>{n.classList.remove('operation-selected');n.setAttribute('aria-selected','false');});clone.querySelectorAll('.drop-active,.operations-dragging,.operation-drop-active').forEach(n=>n.classList.remove('drop-active','operations-dragging','operation-drop-active'));clone.querySelectorAll('.operation-drop-page,.operation-drag-ghost').forEach(n=>n.remove());clone.querySelector('[data-editor-id="operations-status"]').textContent='';clone.querySelector('[data-editor-id="operations-to-cover"]').disabled=true;clone.querySelector('[data-editor-id="operations-to-next"]').disabled=true;},
  masterIds:()=>[...opIds],selection:()=>[...selection],fits};
})();

local('mcs-outline-color').addEventListener('change',event=>setMcsOutline(event.target.value,local('mcs-outline-width').value,true));
local('mcs-outline-width').addEventListener('input',event=>setMcsOutline(local('mcs-outline-color').value,event.target.value,true));
setMcsOutline(local('mcs-outline-color').value,local('mcs-outline-width').value);
local('mcs-size').addEventListener('input',event=>setMcsSize(event.target.value,true));
setMcsSize(local('mcs-size').value);
scope.addEventListener('click',event=>{
 const button=event.target.closest('[data-page-reset]');if(!button)return;
 const page=button.closest('.page'),key=page.dataset.pageKey,rect=page.getBoundingClientRect();
 cancel();captureInitial();const saved=initialView.state;PageScale.reset(key,saved);NoteLayout.reset(key,saved);
 if(key==='catalog'){ProjectModel.cancel();ProjectModel.reset(false);}
 else if(key==='cover'){
  GalleryLayout.restore(saved.galleryLayout,true);
  setMcsSize(saved.axesSize);setMcsOutline(saved.outlineColor,saved.outlineWidth);
  applyPhotoZoomValues(saved.photoZooms);applyPhotoPanValues(saved.photoPans);
  page.querySelectorAll('.photo[data-paired]').forEach(photo=>setVariant(photo,saved.variants?.[photo.dataset.image]==='no-ipw'));
  CardFlow.restore(saved.coverOperations,saved.coverPlacement,saved.operationLimit);
 }
 // Shared project data and other sheets' settings survive. Reflow accounts
 // for the restored cells and the original placement of cover operations.
 CardFlow.reflow();markChanged();
 const next=scope.querySelector('.setup-pages>[data-page-key="'+key+'"]');
 if(next){const after=next.getBoundingClientRect();window.scrollBy(after.left-rect.left,after.top-rect.top);next.querySelector('[data-page-reset]')?.focus({preventScroll:true});}
});
window.addEventListener('resize',()=>{fitDataTables();positionMcsAnchors();NoteLayout.queue();});
window.addEventListener('afterprint',()=>{positionMcsAnchors();NoteLayout.queue();});
if(window.ResizeObserver){const observer=new ResizeObserver(()=>positionMcsAnchors());scope.querySelectorAll('.photo-stage').forEach(stage=>observer.observe(stage));}
function togglePhoto(photo){setVariant(photo,photo.dataset.variant!=='no-ipw',true);}
function syncField(event){const field=event.target.closest('[data-field]');if(!field||(event.isComposing&&field.matches('.datum-input')))return;const value=fieldValue(field);scope.querySelectorAll('[data-field]').forEach(el=>{if(el.dataset.field===field.dataset.field&&(el!==field||el.matches('input,select')))setFieldValue(el,value);});const resized=fitFields();if(resized&&field.closest('.note-row'))NoteLayout.queue();markChanged();DatumEditor.position();}
scope.addEventListener('input',syncField);
scope.addEventListener('change',event=>{if(event.target.matches('input[data-field],select[data-field]'))syncField(event);});
scope.addEventListener('paste',event=>{const field=event.target.closest('[data-field][contenteditable]');if(field){event.preventDefault();document.execCommand('insertText',false,event.clipboardData.getData('text/plain'));}});
scope.addEventListener('click',event=>{if(event.target.closest('.photo-view-tools,.cell-remove,.cell-move,.gallery-grip,.gallery-height-grip'))return;const photo=event.target.closest('.photo[data-paired]');if(photo)togglePhoto(photo);});

function state(){const variants={};scope.querySelectorAll('.photo[data-image]:not([data-cell-preview])').forEach(photo=>{variants[photo.dataset.image]=photo.dataset.variant;});return{fields:values(),variants,axesSize:Number(local('mcs-size').value),outlineColor:local('mcs-outline-color').value,outlineWidth:Number(local('mcs-outline-width').value),columnWidths,photoZooms:photoZoomValues(),photoPans:photoPanValues(),galleryLayout:CardFlow.galleryValues(),pageScales:PageScale.state(),pageWraps:PageScale.wrapState(),coverToolScale:PageScale.toolsValue(),coverOperations:CardFlow.values(),coverPlacement:CardFlow.mode(),operationLimit:CardFlow.limit(),noteHeights:NoteLayout.state()};}
function restoreState(draft,deferred=false){
 if(!draft?.fields)return;applyValues(draft.fields);if(draft.noteHeights)NoteLayout.restore(draft.noteHeights);
 if(draft.galleryLayout)GalleryLayout.restore(draft.galleryLayout,deferred);PageScale.restore(draft.pageScales,draft.rowScale,draft.coverToolScale,draft.pageWraps);if(draft.coverOperations)CardFlow.restore(draft.coverOperations,draft.coverPlacement,draft.operationLimit);
 if(draft.photoZooms)applyPhotoZoomValues(draft.photoZooms);if(draft.photoPans)applyPhotoPanValues(draft.photoPans);if(draft.columnWidths)columnWidths=readColumnWidths(draft.columnWidths);
 if(draft.axesSize!=null)setMcsSize(draft.axesSize);if(draft.outlineColor!=null||draft.outlineWidth!=null)setMcsOutline(draft.outlineColor,draft.outlineWidth??100);
 scope.querySelectorAll('.photo[data-paired]').forEach(photo=>{const variant=draft.variants?.[photo.dataset.image];if(variant==='ipw'||variant==='no-ipw')setVariant(photo,variant==='no-ipw');});
}
function prepare(){NoteLayout.finish(true);PhotoPan.finish();GalleryLayout.closePicker();GalleryLayout.finish(true);finishColumnDrag();CardFlow.finish(true);positionMcsAnchors();}
function cancel(){NoteLayout.finish(true);PhotoPan.finish(true);GalleryLayout.closePicker();GalleryLayout.finish(true);finishColumnDrag(true);CardFlow.finish(true);DatumEditor.close();}
function serialize(){
 const copy=scope.cloneNode(true),fields=values();
 copy.querySelectorAll('input[data-field],select[data-field]').forEach(el=>setFieldValue(el,fields[el.dataset.field]));
 GalleryLayout.cleanClone(copy);PhotoPan.cleanClone(copy);CardFlow.cleanClone(copy);DatumEditor.cleanClone(copy);PercentInput.cleanClone(copy);
 if(initialView)writeInitial(copy.querySelector('[data-editor-id="initial-view"]'),GalleryLayout.deletedOriginalCells());
 const get=id=>copy.querySelector('[data-editor-id="'+id+'"]');
 PageScale.save(copy);NoteLayout.save(copy);get('column-widths').textContent=JSON.stringify(columnWidths);get('column-size-status').textContent='';copy.classList.remove('columns-dragging');
 copy.querySelectorAll('.column-grip').forEach(g=>g.classList.remove('dragging'));
 get('mcs-size').setAttribute('value',local('mcs-size').value);get('mcs-outline-width').setAttribute('value',local('mcs-outline-width').value);
 [...get('mcs-outline-color').options].forEach(o=>o.toggleAttribute('selected',o.value===local('mcs-outline-color').value));
 return copy;
}
scope.querySelectorAll('.datum-input').forEach(field=>setFieldValue(field,field.value));
scope.querySelectorAll('.photo[data-image]').forEach(photo=>{setPhotoZoom(photo,photo.dataset.zoom??100);setVariant(photo,photo.dataset.variant==='no-ipw');});
window.addEventListener('scroll',event=>{if(!event.target.closest?.('.datum-suggestions'))DatumEditor.close();},true);
return{id:scope.dataset.setupId,state,restore:restoreState,serialize,prepare,cancel,fitFields,paginate,captureInitial,waitImages:()=>Promise.all([...scope.querySelectorAll('.photo-stage img[src],.project-isometry img[src]')].map(img=>img.decode?img.decode():Promise.resolve())),beforePrint:()=>{paginate();fitFields();positionMcsAnchors();},flow:CardFlow,gallery:GalleryLayout};

}

document.querySelectorAll('.setup-document').forEach(scope=>editors.push(createSetupEditor(scope)));
placeProjectTools();paginate();editors.forEach(e=>e.captureInitial());
try{const draft=JSON.parse(localStorage.getItem(draftKey)||'null');if(draft?.setups){if(draft.options)ColumnOptions.set(draft.options);if(draft.projectImageScale!=null)ProjectImage.set(draft.projectImageScale);if(draft.projectModel)ProjectModel.restore(draft.projectModel);editors.forEach(e=>e.restore(draft.setups[e.id]));changed=true;editRevision++;}}catch(e){}
placeProjectTools();ProjectModel.queue();CardHistory=createCardHistory();
document.addEventListener('keydown',event=>{if((event.ctrlKey||event.metaKey)&&(event.key.toLowerCase()==='s'||event.code==='KeyS')){event.preventDefault();saveCard();}else if((event.ctrlKey||event.metaKey)&&(event.key.toLowerCase()==='p'||event.code==='KeyP')){event.preventDefault();printCard();}});
document.getElementById('save').addEventListener('click',saveCard);document.getElementById('print').addEventListener('click',printCard);document.getElementById('save-copy').addEventListener('click',downloadCopy);
window.addEventListener('beforeprint',()=>{editors.forEach(e=>e.beforePrint());renumber();ProjectModel.prepare();});
const projectBar=document.querySelector('.project-toolbar');
function barHeight(){document.documentElement.style.setProperty('--project-bar-height',projectBar.getBoundingClientRect().height+'px');}
// Wheel navigation follows the horizontal paper strip. Existing image/model
// zoom handlers run first; scrollable menus and Ctrl+wheel retain their behavior.
document.addEventListener('wheel',event=>{
 if(event.defaultPrevented||event.ctrlKey||event.metaKey||window.matchMedia('print').matches)return;
 if(event.target.closest?.('select,textarea,input,[contenteditable="true"],.datum-popup,.cell-grid-picker,.operation-options-panel'))return;
 const delta=(Math.abs(event.deltaX)>Math.abs(event.deltaY)?event.deltaX:event.deltaY)*(event.deltaMode===1?16:event.deltaMode===2?innerWidth*.9:1);
 if(!delta)return;event.preventDefault();window.scrollBy(delta,0);
},{passive:false});
if(window.ResizeObserver)new ResizeObserver(barHeight).observe(projectBar);barHeight();
document.getElementById('setup-jump').addEventListener('change',event=>{
 const scope=document.querySelector('[data-setup-id="'+event.target.value+'"]');if(!scope)return;
 const rect=scope.getBoundingClientRect(),cover=scope.querySelector('.setup-pages>.cover-page');
 window.scrollTo({left:window.scrollX+(cover||scope).getBoundingClientRect().left-18,top:window.scrollY+rect.top-projectBar.getBoundingClientRect().height,behavior:'smooth'});
});
(document.fonts?document.fonts.ready:Promise.resolve()).then(()=>{paginate();editors.forEach(e=>e.fitFields());CardHistory.refresh();queueLocalSave();});
</script></body></html>
"""

# Renderer embedded in this journal; no external template at runtime.
TABLE_ROW_MM = 3.8
COVER_TOOL_ROW_MM = 3.8
COVER_TOOL_WIDTH_MM = 77.025
OP_KEYS = ['name', 'mcs', 'change', 'tool', 'number', 'time', 'feed', 'speed', 'stock', 'floor_stock', 'zmin']
OP_HEADERS = ['Операции', 'СКС', '', 'Инструмент', 'T.', 'Время', 'Подача', 'Обороты', 'Припуск на стенки', 'Припуск на пол', 'Zmin']
TOOL_ICON = '<svg viewBox="0 0 12 18" aria-hidden="true"><rect x="3" y="1" width="6" height="15" rx="1" fill="none" stroke="currentColor"/><path d="M3 8L9 3M3 12L9 7M3 16L9 11" fill="none" stroke="currentColor"/></svg>'


def escaped(value):
    return html.escape(str(value), quote=True)


def tool_name_html(value, include_lengths=True):
    """Highlight tool dimensions while preserving names and HTML escaping."""
    text = str(value)
    parts, start = [], 0
    # Codes start at a delimiter, so BALL10 and SOLID10 remain ordinary names.
    lengths = r'(?:HL|H|L)[0-9]+(?:[.,][0-9]+)?|' if include_lengths else ''
    # Letters may follow the numeric diameter: D10ST -> highlight only D10.
    diameter = r'D[0-9]+(?:[.,][0-9]+)?(?![0-9]|[.,][0-9])|' if include_lengths else ''
    pattern = r'(?<![^\W_])(?:' + diameter + lengths + r'R0(?![^\W_]|[.,][0-9]))'
    for match in re.finditer(pattern, text, re.IGNORECASE):
        parts.append(escaped(text[start:match.start()]))
        css_class = 'tool-diameter' if match.group()[0].upper() == 'D' else 'tool-dimension'
        parts.append('<strong class="%s">%s</strong>' % (css_class, escaped(match.group())))
        start = match.end()
    parts.append(escaped(text[start:]))
    return ''.join(parts)



def text_width_em(value, bold=False):
    """Conservative, dependency-free fallback; HTML refines it with actual fonts."""
    widths = {' ': .278, '_': .556, '.': .278, ',': .278, ':': .278,
              '-': .334, '/': .278, '+': .584, 'I': .278, 'J': .5,
              'M': .834, 'W': .944, 'i': .223, 'l': .223, 'j': .223,
              'f': .278, 'r': .334, 't': .278, 'm': .834, 'w': .723}
    total = sum(widths.get(c, .556 if c.isdigit() else .69 if c.isupper()
                           else 1.0 if ord(c) > 0x2e80 else .59)
                for c in ' '.join(str(value).split()))
    return total * (1.24 if bold else 1.20)


def fitted_columns(samples, width_mm, base_pt, minima, flexible):
    """Reserve complete values first; flexible names receive the remaining width."""
    needed = [max([minimum] + [em * base_pt * 25.4 / 72 + extra for em, extra in column])
              for column, minimum in zip(samples, minima)]
    if width_mm <= 0:
        raise ValueError('Недостаточная ширина таблицы.')
    natural = needed[:]
    for i in flexible:
        needed[i] = minima[i]
    slack = max(0., width_mm - sum(needed))
    recipients = flexible or list(range(len(needed)))
    weight = sum(natural[i] for i in recipients)
    for i in recipients:
        needed[i] += slack * natural[i] / weight
    # Initial static HTML also stays inside the sheet. JavaScript refines this
    # using the actual font and scales the whole table only when necessary.
    fit = min(1.0, width_mm / sum(needed))
    return base_pt * fit, [value * fit for value in needed]


def clipped_cell(content, full_text, indent_mm=None):
    """Keep all markup/data; ellipsis is visual and the tooltip has the full value."""
    css = 'cell-text tree-name' if indent_mm is not None else 'cell-text'
    text = '<span class="%s" title="%s">%s</span>' % (css, escaped(full_text), content)
    if indent_mm is not None:
        text = '<span class="tree-cell"><span class="tree-indent" aria-hidden="true" style="width:%.2fmm"></span>%s</span>' % (indent_mm, text)
    return text


def layout_attributes(family, font, widths, base_pt, minima, flexible):
    attrs = (' data-fit-family="%s" data-base-pt="%s" data-min-mm="%s" '
             'data-flex-cols="%s" style="font-size:%.5fpt"') % (
                 family, base_pt, ','.join(map(str, minima)), ','.join(map(str, flexible)), font)
    cols = '<colgroup>' + ''.join('<col style="width:%.8f%%">' % (w / sum(widths) * 100)
                                  for w in widths) + '</colgroup>'
    return attrs, cols


def operation_layout(report):
    samples = [[(max((text_width_em(word) for word in h.split()), default=0) * 6 / 7, 1.8)] for h in OP_HEADERS]
    for row in report.get('operation_rows', []):
        values = operation_cells(row, report)
        for i, value in enumerate(values):
            if i == 2:
                continue
            extra = 1.8
            if i == 0:
                extra += min(int(row.get('depth', 0)), 7) * 2.5
                if row['kind'] == 'group':
                    extra += 4.5
                    value = str(value) + ' · продолжение'
            samples[i].append((text_width_em(value, row['kind'] == 'group' or i == 3 or (i == 0 and bool(re.search(r'(?<![^\W_])R0(?![^\W_]|[.,][0-9])', str(value), re.IGNORECASE)))), extra))
    minima = (8, 5, 4, 8, 4, 7, 7, 7, 7, 7, 7)
    font, widths = fitted_columns(samples, 193.4, 7, minima, (0,))
    attrs, cols = layout_attributes('operations', font, widths, 7, minima, (0,))
    for key in OP_KEYS:
        cols = cols.replace('<col style=', '<col data-column-id="%s" style=' % key, 1)
    return attrs + ' data-ops-schema="3"', cols


def tool_layout(tools, width_mm, base_pt, family, name_first=True, name_header=None, fit_scale=1.0):
    names = [(text_width_em(name_header if name_header is not None else ('Инструмент' if name_first else 'Наименование инструмента'), True), 2.0)]
    numbers = [(text_width_em('T'), 2.0)]
    for row in tool_display_rows(tools):
        extra_em = min(12, row['depth']) * 1.3 + (2.1 if row['kind'] == 'group' else 0)
        names.append((text_width_em(row['name'], True) + extra_em, 2.0))
        if row['kind'] != 'group':
            numbers.append((text_width_em(number_text(row.get('number'))), 2.0))
    minima, flexible = ((8, 6), (0,)) if name_first else ((6, 8), (1,))
    samples = [names, numbers] if name_first else [numbers, names]
    font, widths = fitted_columns(samples, width_mm - .6, base_pt * fit_scale,
                                  tuple(value * fit_scale for value in minima), flexible)
    return layout_attributes(family, font, widths, base_pt, minima, flexible)


def number_text(value, digits=None):
    if value is None:
        return '—'
    try:
        number = finite_number(value)
        return (('%.' + str(digits) + 'f') % number) if digits is not None else ('%.4f' % number).rstrip('0').rstrip('.')
    except (ValueError, TypeError):
        return '—'


def time_text(seconds):
    if seconds is None:
        return '—'
    n = max(0, int(float(seconds) + 0.5))
    return '%02d:%02d:%02d' % (n // 3600, n // 60 % 60, n % 60)


def feed_text(record, report):
    if record.get('feed') is None:
        return '—'
    unit = (record.get('feed_unit') or '').rsplit('.', 1)[-1]
    metric = 'inch' not in report.get('part_units', '').lower()
    names = {'PerMinute': 'мм/мин' if metric else 'дюйм/мин',
             'PerRevolution': 'мм/об' if metric else 'дюйм/об',
             'PerTooth': 'мм/зуб' if metric else 'дюйм/зуб',
             'CutPercent': '%', 'Rapid': 'Быстрый ход'}
    if unit == 'Rapid':
        return 'Быстрый ход'
    if unit not in names:
        return number_text(record['feed']) + ' [?]'
    return number_text(record['feed']) + ' ' + names[unit]


def stock_text(value):
    text = number_text(value)
    return '' if text in ('0', '-0') else text


def zmin_text(value):
    if value is None:
        return ''
    text = number_text(value)
    return '' if text == '—' else ('0' if text == '-0' else text)


def operation_cells(row, report):
    if row['kind'] == 'group':
        return [row['name'], '', '', '', '', time_text(row.get('seconds')), '', '', '', '', '']
    return [row['name'], row.get('mcs', ''), '', row.get('tool_name') or '—',
            number_text(row.get('tool_number')), time_text(row.get('seconds')),
            feed_text(row, report), number_text(row.get('speed')) + (' об/мин' if row.get('speed') is not None else ''),
            stock_text(row.get('stock')), '' if row.get('floor_stock_mode') == 'not_applicable' else stock_text(row.get('floor_stock')),
            zmin_text(row.get('zmin'))]


def path_symbol(row):
    if row.get('suppressed') is True:
        return '<span class="path suppressed" title="Операция подавлена: не выводится постпроцессором">×</span>'
    if row.get('has_path') is False:
        return '<span class="path missing" title="Траектория отсутствует">○</span>'
    status = (row.get('path_status') or '').rsplit('.', 1)[-1]
    if row.get('has_path') and status in ('Complete', 'Approved', 'Repost'):
        label = 'Траектория рассчитана' + ('; требуется постпроцессирование' if status == 'Repost' else '')
        return '<span class="path ready" title="%s">✓</span>' % label
    if status == 'Regen':
        return '<span class="path stale" title="Требуется пересчёт траектории">!</span>'
    return '<span class="path missing" title="Не удалось прочитать статус траектории">?</span>'


def operation_row_html(row, report):
    values = operation_cells(row, report)
    group = row['kind'] == 'group'
    classes = 'group-row' if group else ('suppressed-row' if row.get('suppressed') else '')
    cells = []
    for index, value in enumerate(values):
        css = 'name-cell' if index in (0, 1, 3) else 'numeric'
        content = escaped(value)
        if index == 0:
            if not group:
                content = tool_name_html(value, include_lengths=False)
            indent = min(int(row.get('depth', 0)), 7) * 2.5
            prefix = '<span class="folder-icon" aria-hidden="true"></span>' if group else ''
            if group and row.get('time_source') == 'sum_of_active_toolpaths':
                prefix += '<span title="Сумма времени активных траекторий; без дополнительных затрат NX">Σ </span>'
            content = prefix + content
        elif index == 2 and not group:
            change = row.get('tool_change')
            title = 'Смена инструмента: ' + (row.get('tool_change_reason') or 'Смена инструмента')
            content = ('<span class="tool-change" title="%s" aria-label="Смена инструмента">' % escaped(title) + TOOL_ICON + '</span>') if change else ('?' if change is None else '')
        elif index == 3 and not group:
            content = tool_name_html(value)
        elif index == 4 and not group:
            content = tool_number_html(row.get('tool_number'), row.get('duplicate_tool_number', False))
        cells.append('<td data-column-id="%s" class="%s" title="%s">%s</td>' % (OP_KEYS[index], css, escaped(value), clipped_cell(content, value, indent if index == 0 else None)))
    return '<tr class="%s" data-kind="%s" data-depth="%d"%s>%s</tr>' % (
        classes, row['kind'], int(row.get('depth', 0)),
        ' data-repeat="true"' if row.get('continued') else '', ''.join(cells))


def operations_table(rows, report):
    headers = [TOOL_ICON if key == 'change' else '<br>'.join(escaped(w) for w in label.split())
               for key, label in zip(OP_KEYS, OP_HEADERS)]
    titles = [label or 'Смена инструмента' for label in OP_HEADERS]
    attrs, cols = operation_layout(report)
    head = ''.join('<th data-column-id="%s" title="%s" aria-label="%s">%s</th>' %
                   (key, escaped(label), escaped(label), clipped_cell(content, label))
                   for key, label, content in zip(OP_KEYS, titles, headers))
    body = ''.join(operation_row_html(row, report) for row in rows)
    return '<table class="ops"%s>%s<thead><tr>%s</tr></thead><tbody>%s</tbody></table>' % (attrs, cols, head, body)


def field(name, value='', placeholder='', css=''):
    # Только именованные заполненные ячейки исходного Excel, не пустые промежутки.
    return '<div class="field %s" contenteditable="plaintext-only" role="textbox" data-field="%s" data-placeholder="%s" aria-label="%s" spellcheck="false">%s</div>' % (
        css, escaped(name), escaped(placeholder), escaped(placeholder or name), escaped(value))


DATUM_CHOICES = {
    'X': ('ЦЕНТР', 'ЦЕНТР ОТВЕРСТИЯ', 'ЛЕВО', 'ПРАВО', 'ОТ УПОРА СЛЕВА', 'ОТ УПОРА СПРАВА'),
    'Y': ('ЦЕНТР', 'ЦЕНТР ОТВЕРСТИЯ', 'ДАЛЬНИЙ', 'БЛИЖНИЙ', 'ОТ НЕПОДВИЖНОЙ ГУБКИ', 'ОТ ПОДВИЖНОЙ ГУБКИ'),
    'Z': ('ВЕРХ', 'НИЗ', 'ЦЕНТР', 'ОТ ВЕРХА ЗАГОТОВКИ', 'ОТ НИЗА ЗАГОТОВКИ', 'ОТ ПЛОСКОСТИ ОСНАСТКИ'),
}


def datum_field(axis):
    return ('<div class="datum-editor" data-choices="%s">'
            '<input class="datum-input" type="text" data-field="datum_%s" value="" '
            'placeholder="Привязка %s" aria-label="Привязка %s" role="combobox" '
            'aria-autocomplete="list" aria-haspopup="listbox" aria-expanded="false" '
            'autocomplete="off" spellcheck="false">'
            '<button class="datum-toggle" type="button" tabindex="-1" '
            'aria-label="Варианты привязки %s" title="Выбрать привязку">▾</button></div>'
            '<div class="datum-print field center" aria-hidden="true"></div>') % (
                escaped(json.dumps(DATUM_CHOICES[axis], ensure_ascii=False)), axis, axis, axis, axis)


def tool_legend_table(locked=False):
    legends = '<table class="legends"><colgroup><col style="width:22.15%"><col></colgroup>'
    for label, text_value in [('H', 'Вылет инструмента из оправки, мм'), ('L', 'Длина режущей части, мм'), ('HL', 'Длина обниженной части, мм')]:
        description = ('<div class="field center legend-text" contenteditable="false">%s</div>' % escaped(text_value)
                       if locked else field('legend_' + label, text_value, '', 'center'))
        legends += '<tr><th>%s</th><td>%s</td></tr>' % (label, description)
    legends += '</table>'
    return legends


def card_header(report, compact=False, note=True):
    part = field('part', report.get('project_name', report.get('part_name', '')), 'Деталь', 'bold')
    label = setup_label(report)
    named = ' named-setup' if not re.fullmatch(r'[0-9]{1,3}', label) else ''
    setup = '<div class="setup-values%s">%s<span>/</span>%s<span class="locked-spacer"></span></div>' % (
        named, field('setup', label, 'Установ', 'bold center'), field('setup_total', report.get('setup_total', 1), 'Всего', 'center'))
    if compact:
        note_row = ('<tr class="note-row"><th class="white">%s</th><td>%s</td></tr>' % (
            field('note_label', 'Примечание:', '', 'bold center'),
            field('note', '', 'Особенности закрепления и обработки', 'red bold center'))) if note else ''
        return ('<header class="continuation-header%s"><table class="metadata">'
                '<colgroup><col style="width:15.5%%"><col></colgroup>'
                '<tr><th>Деталь:</th><td>%s</td></tr><tr><th>Установ:</th><td>%s</td></tr>'
                '%s</table></header>') % ('' if note else ' no-note', part, setup, note_row)
    # new_report freezes the local Windows clock; retain its wall time, not UTC or browser time.
    created_at = report.get('created_at') or datetime.datetime.now().astimezone().isoformat()
    date = datetime.datetime.fromisoformat(created_at).strftime('%d.%m.%Y %H:%M')
    left = '<table class="metadata"><colgroup><col style="width:25.23%"><col></colgroup>'
    left += '<tr><th>Разработчик:</th><td>%s</td></tr>' % field('author', report.get('author', DEFAULT_PROGRAMMER), 'Фамилия И. О.', 'bold')
    left += '<tr><th>Деталь:</th><td>%s</td></tr>' % part
    left += '<tr><th>Установ:</th><td>%s</td></tr>' % setup
    left += '<tr><th>Дата:</th><td>%s</td></tr>' % field('date', date, 'Дата и время', 'bold')
    blank_text = report.get('stock_blank', '')
    left += '<tr><th>ЗАГОТОВКА</th><td>%s</td></tr>' % field('stock_blank', blank_text, 'Размеры заготовки', 'red bold center')
    left += '<tr class="note-row"><th class="white">%s</th><td>%s</td></tr></table>' % (
        field('note_label', 'Примечание:', '', 'bold center'), field('note', '', 'Особенности закрепления и обработки', 'red bold center'))
    axes = '<table class="coordinates"><colgroup><col style="width:22.15%"><col></colgroup>'
    for axis in 'XYZ':
        axes += '<tr><th>%s</th><td>%s</td></tr>' % (axis, datum_field(axis))
    axes += '</table>'
    legends = tool_legend_table()
    return '<header class="card-header"><div>%s</div><div class="header-right">%s<div class="locked-spacer"></div>%s</div></header>' % (left, axes, legends)


def projected_mcs_axes(item, report, variant='ipw'):
    """Оси именно MCS в проекции камеры: +X экрана вправо, +Y экрана вниз."""
    basis = report.get('mcs_axes_absolute')
    camera = item.get(variant.replace('-', '_') + '_camera_axes_absolute', item.get('camera_axes_absolute'))
    if basis is None or camera is None:
        return None  # Не подменяем неизвестную ориентацию глобальными осями.
    frames = []
    for frame in (basis, camera):
        if len(frame) != 3:
            raise ValueError('Для указателя СКС нужны три оси.')
        vectors = tuple(unit(axis) for axis in frame)
        if any(abs(dot(vectors[i], vectors[j])) > 1e-5 for i in range(3) for j in range(i)):
            raise ValueError('Оси указателя СКС не перпендикулярны.')
        frames.append(vectors)
    basis, camera = frames
    return [(dot(axis, camera[0]), -dot(axis, camera[1]), dot(axis, camera[2])) for axis in basis]


def mcs_axes_svg(item, report, variant='ipw', on_part=False):
    directions = projected_mcs_axes(item, report, variant)
    if directions is None:
        return ''
    title = ('Начало СКС на детали' if on_part else 'Оси СКС') + ' %s · %s' % (report.get('mcs_name', ''), item['label'])
    parts = ['<svg class="%s" data-variant="%s" viewBox="0 0 100 100" '
             'xmlns="http://www.w3.org/2000/svg" role="img" aria-label="%s"%s><title>%s</title>' % (
                 'mcs-onpart' if on_part else 'mcs-axes', variant, escaped(title), ' hidden' if variant == 'no-ipw' else '', escaped(title)),
             '<circle cx="50" cy="50" r="1.35" fill="#202428"/>']
    labels, positions, bounds = [], [], [(45., 45.), (55., 55.)]
    axes = list(zip('XYZ', ('#e64b43', '#34a853', '#3478e5'), directions))
    def contour(axis, base, fixed=0., fill=False):
        color = '#ffffff' if on_part else '#111111'
        attrs = 'stroke="%s" stroke-width="%.4g"' % (color, base + fixed)
        if fill:
            attrs += ' fill="%s"' % color
        if on_part:
            attrs += (' data-outline-axis="%s" data-outline-base="%.4g" data-outline-fixed="%.4g"%s'
                      % (axis, base, fixed, ' data-outline-fill="true"' if fill else ''))
        return attrs
    rim = .7 if on_part else .4
    for name, color, (dx, dy, depth) in sorted(axes, key=lambda axis: math.hypot(*axis[2][:2]) < 1e-6):
        length = math.hypot(dx, dy)
        parts.append('<g data-axis="%s" data-dx="%.8f" data-dy="%.8f" data-depth="%.8f" fill="%s" stroke="%s">' % (
            name, dx, dy, depth, color, color))
        if length < 1e-6:
            # Точка: положительная ось к наблюдателю; крест: от наблюдателя.
            # Transparent centre; two nested strokes make a fine black rim.
            parts.append('<circle cx="50" cy="50" r="3.8" fill="none" %s/>' % contour(name, 2 * rim if on_part else .75, .9))
            parts.append('<circle cx="50" cy="50" r="3.8" fill="none" stroke-width=".9"/>')
            if depth > 0:
                parts.append('<circle cx="50" cy="50" r="1.25" %s/>' % contour(name, rim))
            else:
                for attrs in (contour(name, 2 * rim if on_part else .75, .85), 'stroke="%s" stroke-width=".85"' % color):
                    parts.append('<path d="M47.8 47.8L52.2 52.2M47.8 52.2L52.2 47.8" fill="none" %s stroke-linecap="round"/>' % attrs)
            lx, ly = 36., 66.
        else:
            ux, uy = dx / length, dy / length
            arm = 28. * length if on_part else max(12., 28. * length)
            ex, ey = 50. + ux * arm, 50. + uy * arm
            bounds.extend([(ex - 5., ey - 5.), (ex + 5., ey + 5.)])
            head = min(5.5, arm * .48)
            half_shaft, half_head = min(.8, arm * .12), min(2.7, arm * .27)
            bx, by = ex - ux * head, ey - uy * head
            def offset(x, y, amount):
                return x - uy * amount, y + ux * amount
            outline = [offset(50., 50., half_shaft), offset(bx, by, half_shaft),
                       offset(bx, by, half_head), (ex, ey), offset(bx, by, -half_head),
                       offset(bx, by, -half_shaft), offset(50., 50., -half_shaft)]
            path = 'M' + 'L'.join('%.3f %.3f' % point for point in outline) + 'Z'
            # One filled silhouette: the neck and arrowhead have no visible seam.
            parts.append('<path d="%s" %s stroke-linejoin="round"/>' % (path, contour(name, rim)))
            lx, ly = ex + ux * 11., ey + uy * 11. + 4.
        parts.append('</g>')
        if any(math.hypot(lx - x, ly - y) < 14 for x, y in positions):
            ly = max(12., min(94., ly + (14. if ly < 65. else -14.)))
        positions.append((lx, ly))
        bounds.extend([(lx - 12., ly - 14.), (lx + 12., ly + 3.)])
        # The on-part default is white; corner indicators keep their black outline.
        text_style = 'x="%.3f" y="%.3f" text-anchor="middle" font-family="Arial,sans-serif" font-weight="700" font-size="%s"' % (lx, ly, '11' if on_part else '14')
        labels.append('<text %s %s stroke-linejoin="round" aria-hidden="true">%sM</text>' % (
            text_style, contour(name, .75 if on_part else .55, fill=True), name))
        labels.append('<text %s fill="%s" stroke="none">%sM</text>' % (text_style, color, name))
    if on_part:
        # Exactly (50,50): never apply the corner indicator's bounding-box shift.
        return ''.join(parts + labels) + '</svg>'
    # Прижимаем сам рисунок к углу, убирая пустой отступ абстрактного куба.
    # Длина стрелок и размер шрифта сохраняются для всех ракурсов.
    shift_x = 5. - min(x for x, y in bounds)
    shift_y = 5. - min(y for x, y in bounds)
    return parts[0] + '<g transform="translate(%.3f %.3f)">' % (shift_x, shift_y) + ''.join(parts[1:] + labels) + '</g></svg>'


def on_part_axes_svg(item, report, css, variant='ipw'):
    anchor = item.get('mcs_anchors', {}).get(variant, {})
    if anchor.get('status') != 'projected' or not anchor.get('normalized'):
        return ''
    u, v = anchor['normalized']
    width, height = anchor['image_size']
    if not all(math.isfinite(float(n)) for n in (u, v, width, height)) or not (0 <= u <= 1 and 0 <= v <= 1) or min(width, height) <= 0:
        return ''
    # Static placement also works without JS. Sizes match the A4 gallery CSS;
    # JS additionally measures the actual stage after load/resize/before print.
    cells = {'iso-photo': (194 * .5875, 225.5 * .47),
             'top-photo': (194 * .5875, 225.5 * .53),
             'side-photo': (194 * .4125, 225.5 * .53)}
    cw, ch = cells[css]
    sw, sh = cw - 4., ch - 16.
    scale = min(sw / width, sh / height)
    left = 50. + (u - .5) * width * scale * 100. / sw
    top = 50. + (v - .5) * height * scale * 100. / sh
    svg = mcs_axes_svg(item, report, variant, on_part=True)
    attributes = (' data-anchor-x="%.12g" data-anchor-y="%.12g" data-image-width="%s" '
                  'data-image-height="%s" style="left:%.8f%%;top:%.8f%%;width:36mm;height:36mm"') % (u, v, width, height, left, top)
    return svg.replace(' viewBox=', attributes + ' viewBox=', 1)


def image_slot(item, output, css, report):
    if not item or not (output / item['file']).is_file():
        return '<figure class="photo %s"></figure>' % css
    encoded = base64.b64encode((output / item['file']).read_bytes()).decode('ascii')
    images = '<img data-variant="ipw" alt="%s" src="data:image/png;base64,%s">' % (escaped(item['label']), encoded)
    on_part = on_part_axes_svg(item, report, css)
    paired = False
    if item.get('no_ipw_file') and (output / item['no_ipw_file']).is_file():
        second = base64.b64encode((output / item['no_ipw_file']).read_bytes()).decode('ascii')
        images += '<img data-variant="no-ipw" alt="%s · NO IPW" src="data:image/png;base64,%s" hidden>' % (escaped(item['label']), second)
        on_part += on_part_axes_svg(item, report, css, 'no-ipw')
        paired = True
    # Reset controls are siblings of the image toggle, so they never change IPW.
    control = ' data-paired="true"' if paired else ''
    toggle = ('<button class="photo-toggle" type="button" aria-pressed="true" '
              'title="Щелчок: IPW / NO IPW. Тяните мышью для сдвига. Колесо или + / − — масштаб; Home — центр." '
              'aria-label="%s · IPW; нажмите для NO IPW; колесо или + / − — масштаб"></button>' % escaped(item['label'])) if paired else ''
    zoom = ('<div class="photo-view-tools" role="group" aria-label="Настройки изображения: %s">'
            '<button class="photo-zoom-reset" type="button" title="Вернуть масштаб изображения 100%%" '
            'aria-label="Вернуть масштаб изображения 100%%">100%%</button>'
            '<button class="photo-pan-reset" type="button" title="Вернуть изображение в центр ячейки без изменения масштаба" '
            'aria-label="Вернуть изображение в центр ячейки">Центр</button></div>') % escaped(item['label'])
    return ('<figure class="photo %s" data-image="%s" data-variant="ipw" data-label="%s" data-zoom="100" data-pan-x="0" data-pan-y="0"%s>'
            '%s</figure>') % (
        css, escaped(item['file']), escaped(item['label']), control,
        '<div class="photo-stage">' + images + on_part + '</div>' + toggle + zoom)


def cover_tools_scale(count, width_mm, height_mm):
    """Keep every setup tool on the cover, with a printable initial fit."""
    natural = (count + 1) * COVER_TOOL_ROW_MM + .16
    return min(1.0, width_mm / COVER_TOOL_WIDTH_MM, max(.01, height_mm - .3) / natural)


def tool_table(tools, width_mm=77.025, base_pt=7, family='cover-tools', all_tools=None, name_header='Инструмент', height_mm=None):
    display_rows = tool_display_rows(tools)
    scale = cover_tools_scale(len(display_rows), width_mm, height_mm) if family == 'cover-tools' and height_mm is not None else 1.0
    attrs, cols = tool_layout(tools if all_tools is None else all_tools, width_mm, base_pt, family,
                              name_header=name_header, fit_scale=scale)
    if family == 'cover-tools':
        sizes = {'row': COVER_TOOL_ROW_MM, 'pady': .2, 'padx': .6, 'border': .16}
        properties = ''.join('--cover-%s:%.8fmm;' % (key, value * scale) for key, value in sizes.items())
        attrs = attrs.replace(' style="', ' data-cover-scale="%.10f" style="%s' % (scale, properties), 1)
    rows = ''.join(tool_row_html(row) for row in display_rows)
    return '<table class="tools"%s>%s<thead><tr><th>%s</th><th>%s</th></tr></thead><tbody>%s</tbody></table>' % (attrs, cols, clipped_cell(escaped(name_header), name_header), clipped_cell('T', 'T'), rows)


def sorted_tools(tools):
    def key(tool):
        try:
            number = finite_number(tool.get('number'))
        except (ValueError, TypeError):
            number = float('inf')
        return number, str(tool.get('name', '')).casefold()
    return sorted(tools, key=key)


def gallery_html(output, report):
    views = {v.get('logical_file', v['file']): v for v in report.get('views', [])}
    # Every setup tool stays on the cover. Row and font sizing follows its panel.
    setup_tools = sorted_tools(report.get('tools', []))
    height_mm = 225.5 * .47 - 3
    tools = (tool_table(setup_tools, name_header='Инструмент для установа #%s' % setup_label(report),
                        height_mm=height_mm) if setup_tools else
             '')
    panel = '<section class="tool-panel">' + tools + '</section>'
    content = (image_slot(views.get('03_iso.png'), output, 'iso-photo', report)
               + image_slot(views.get('01_top.png'), output, 'top-photo', report)
               + image_slot(views.get('02_side.png'), output, 'side-photo', report)
               + '<div class="right-panel">%s</div>' % panel)
    frame = '<svg class="gallery-frame" viewBox="0 0 1000 1000" preserveAspectRatio="none" aria-hidden="true"><path fill="none" stroke="#363e43" stroke-width="1.2" vector-effect="non-scaling-stroke" d="M.5 .5H999.5V999.5H.5Z M587.5 0V1000 M0 470H1000"/></svg>'
    return '<section class="gallery"><div class="gallery-grid">%s</div>%s</section>' % (content, frame)


def estimate_row_mm(row, report):
    return TABLE_ROW_MM


def continued_group(row):
    return dict(row, name=row['name'] + ' · продолжение', continued=True,
                seconds=None, time_source=None)


def operation_page_plan(rows, heights, repeat_heights, first_budget, continuation_budget):
    """План (index, repeat). Малые папки целиком; большие — с контекстом."""
    count = len(rows)
    ends, parents, stack = [i + 1 for i in range(count)], [], []
    sums = [0.]
    for i, row in enumerate(rows):
        depth = int(row.get('depth', 0))
        while stack and int(rows[stack[-1]].get('depth', 0)) >= depth:
            ends[stack.pop()] = i
        parents.append(list(stack))
        if row['kind'] == 'group':
            stack.append(i)
        sums.append(sums[-1] + heights[i])
    for i in stack:
        ends[i] = count
    pages, current, used, budget, i = [], [], 0., first_budget, 0
    while i < count:
        context_height = sum(repeat_heights[j] for j in parents[i])
        end = i + 1
        # Выбираем самую большую целую папку, помещающуюся на листе.
        # Для слишком большой папки удерживаем цепочку заголовков
        # вместе хотя бы с первой операцией / первым малым подпунктом.
        if rows[i]['kind'] == 'group':
            j = i
            while j < ends[i] and rows[j]['kind'] == 'group':
                nested = any(r['kind'] == 'group' for r in rows[j + 1:ends[j]])
                if not nested and context_height + sums[ends[j]] - sums[i] <= continuation_budget:
                    end = ends[j]
                    break
                j += 1
                end = min(j + 1, ends[i])
        height = sums[end] - sums[i]
        if used + height > budget and (not pages or any(not repeat for _, repeat in current)):
            pages.append(current)
            current = [(j, True) for j in parents[i]]
            used, budget = context_height, continuation_budget
        current.extend((j, False) for j in range(i, end))
        used += height
        i = end
    if current or not pages:
        pages.append(current)
    return pages


def paginate_operations(report, first_budget=234, continuation_budget=234):
    rows = report.get('operation_rows', [])
    repeated = [continued_group(r) if r['kind'] == 'group' else r for r in rows]
    plan = operation_page_plan(rows, [estimate_row_mm(r, report) for r in rows],
                               [estimate_row_mm(r, report) for r in repeated],
                               first_budget, continuation_budget)
    return [[repeated[i] if repeat else rows[i] for i, repeat in page] for page in plan]


def project_tool_plan(rows, capacity):
    """At most two columns, including repeated Carrier context, without loss."""
    sizes = [1.] * len(rows)

    def plan(limit):
        return [chunk for chunk in operation_page_plan(rows, sizes, sizes, limit, limit) if chunk]

    result = plan(max(2, int(capacity)))
    if len(result) <= 2:
        return result or [[]]
    # Balance an oversized list before reducing it. Never strand a group title
    # at the bottom of the left column; repeat its context above the right one.
    stack, best = [], None
    for i, row in enumerate(rows):
        depth = int(row.get('depth', 0))
        while stack and int(rows[stack[-1]].get('depth', 0)) >= depth:
            stack.pop()
        if i and rows[i - 1]['kind'] != 'group':
            score = max(i, len(rows) - i + len(stack))
            if best is None or score < best[0]:
                best = (score, i, list(stack))
        if row['kind'] == 'group':
            stack.append(i)
    if best is None:
        return [[(i, False) for i in range(len(rows))]]
    _, split, parents = best
    return [[(i, False) for i in range(split)],
            [(i, True) for i in parents] + [(i, False) for i in range(split, len(rows))]]


def project_tool_pages(report):
    """One source page; final layout and first-setup image follow the merge."""
    if not report.get('include_project_tools', True):
        return []
    tools = report.get('project_tools', [])
    rows = tool_display_rows(tools)
    attrs, cols = tool_layout(tools, 194, 7, 'project-tools', name_first=False)
    table_head = ('<table class="catalog-table"%s>%s'
                  '<thead><tr><th>%s</th><th>%s</th></tr></thead><tbody>') % (
                      attrs, cols, clipped_cell('T', 'T'),
                      clipped_cell('Наименование инструмента', 'Наименование инструмента'))
    body = ''.join(tool_row_html(row, name_first=False) for row in rows)
    head = '<div class="catalog-heading"><h2 class="catalog-title">Инструменты проекта</h2></div>'
    return ['<article class="page tools-page project-tools-page"><div class="page-content">'
            + project_card_header(report) + head + table_head + body + '</tbody></table>'
            + '<footer class="page-footer"><span class="page-number"></span> / '
            '<span class="page-count"></span></footer></div></article>']



class CardMarkup(HTMLParser):
    """Read exact element spans without reserializing saved user markup.

    Only the standard library is needed in NX. Templates and SVG are parsed,
    not executed; their pages are never counted as printed pages.
    """
    VOID = {'area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link',
            'meta', 'param', 'source', 'track', 'wbr'}

    def __init__(self, source):
        super().__init__(convert_charrefs=True)
        self.source, self.nodes, self.stack = source, [], []
        self.lines = [0] + [m.end() for m in re.finditer('\n', source)]
        self.feed(source)
        self.close()
        if self.stack:
            raise ValueError('HTML оборван: не закрыт элемент ' + self.stack[-1]['tag'])

    def position(self):
        line, column = self.getpos()
        return self.lines[line - 1] + column

    def start(self, tag, attrs, closed=False):
        start = self.position()
        end = start + len(self.get_starttag_text())
        node = {'tag': tag, 'attrs': dict(attrs), 'start': start, 'open_end': end,
                'content_end': end, 'end': end, 'parent': self.stack[-1] if self.stack else None}
        self.nodes.append(node)
        if not closed and tag not in self.VOID:
            self.stack.append(node)

    def handle_starttag(self, tag, attrs):
        self.start(tag, attrs)

    def handle_startendtag(self, tag, attrs):
        self.start(tag, attrs, True)

    def handle_endtag(self, tag):
        if tag in self.VOID:
            return
        if not self.stack or self.stack[-1]['tag'] != tag:
            raise ValueError('Повреждена структура HTML возле </%s>.' % tag)
        node = self.stack.pop()
        node['content_end'] = self.position()
        end = self.source.find('>', node['content_end'])
        if end < 0:
            raise ValueError('HTML оборван.')
        node['end'] = end + 1

    @staticmethod
    def has_class(node, name):
        return name in (node['attrs'].get('class') or '').split()

    @staticmethod
    def in_template(node):
        while node is not None:
            if node['tag'] == 'template':
                return True
            node = node['parent']
        return False

    def raw(self, node):
        return self.source[node['start']:node['end']]

    def text(self, node):
        return html.unescape(re.sub('<[^>]*>', '', self.source[node['open_end']:node['content_end']])).strip()

    def pages(self):
        return [n for n in self.nodes if self.has_class(n, 'page') and
                n['parent'] is not None and self.has_class(n['parent'], 'setup-pages') and not self.in_template(n)]


def edit_markup_spans(source, edits):
    """Replace only requested, non-overlapping spans; preserve all other bytes."""
    boundary = len(source)
    for start, end, value in sorted(edits, reverse=True):
        if not 0 <= start <= end <= boundary:
            raise ValueError('Пересекаются изменения HTML.')
        source = source[:start] + value + source[end:]
        boundary = start
    return source


def setup_identity(report):
    path = report.get('selected_folder_path')
    if path is None:
        path = [str(report['selected_folder'])] if report.get('selected_folder') else []
    return {'mcs': setup_label(report), 'folder': list(path)}


def identity_key(identity):
    if (not isinstance(identity, dict) or not isinstance(identity.get('mcs'), str) or
            not identity['mcs'].strip() or not isinstance(identity.get('folder'), list) or
            any(not isinstance(p, str) for p in identity['folder'])):
        raise ValueError('Повреждён идентификатор установа в HTML.')
    return identity['mcs'], tuple(identity['folder'])


def read_existing_card(path):
    """Read the saved HTML once. Invalid input must never become a fresh export."""
    try:
        path = io_path(path)
        with local_card_guard():
            if not path.exists():
                return None
            raw = path.read_bytes()
        markup = CardMarkup(raw.decode('utf-8-sig'))
        meta = {n['attrs'].get('name'): n['attrs'].get('content') for n in markup.nodes if n['tag'] == 'meta'}
        card_id = meta.get('nx-card-id', '')
        if not re.fullmatch('[A-Za-z0-9_-]{1,128}', card_id):
            raise ValueError('Не найден идентификатор карты наладки.')
        format_name = meta.get('nx-card-format')
        if format_name not in (None, 'setup-sections-v1'):
            raise ValueError('Неизвестный формат карты; требуется совместимая версия скрипта.')
        containers = [n for n in markup.nodes if n['attrs'].get('id') == 'setup-documents']
        if len(containers) != 1:
            raise ValueError('Не найден единый контейнер установов.')
        container = containers[0]
        nodes = [n for n in markup.nodes if markup.has_class(n, 'setup-document')]
        if not nodes or any(n['parent'] is not container for n in nodes):
            raise ValueError('Нарушена структура установов.')
        options = {n['attrs'].get('value'): markup.text(n) for n in markup.nodes
                   if n['tag'] == 'option' and n['parent'] is not None and
                   n['parent']['attrs'].get('id') == 'setup-jump'}
        sections, keys, identities = [], set(), set()
        for node in nodes:
            key = node['attrs'].get('data-setup-id', '')
            if not re.fullmatch('[A-Za-z][A-Za-z0-9_-]*', key) or key in keys:
                raise ValueError('Повторяется или повреждён HTML-ключ установа.')
            keys.add(key)
            value = node['attrs'].get('data-nx-setup-identity')
            identity = json.loads(value) if value else None
            if identity is not None:
                token = identity_key(identity)
                if token in identities:
                    raise ValueError('В HTML повторяется установ «%s».' % identity['mcs'])
                identities.add(token)
            label = identity['mcs'] if identity else options.get(key, '')
            if not label:
                raise ValueError('Не удалось определить исходное имя установа ' + key)
            section = markup.raw(node)
            parsed = CardMarkup(section)
            if len([n for n in parsed.nodes if parsed.has_class(n, 'setup-pages')]) != 1 or not parsed.pages():
                raise ValueError('Повреждены листы установа «%s».' % label)
            for required in ('operation-source', 'continuation-template'):
                if not any(n['attrs'].get('data-editor-id') == required for n in parsed.nodes):
                    raise ValueError('Не найден редактор установа «%s». Обновите его полностью.' % label)
            has_catalog = any(parsed.has_class(n, 'project-tools-page') for n in parsed.nodes)
            legacy_cells = any(parsed.has_class(n, 'photo') and not any(parsed.has_class(n, cls)
                for cls in ('iso-photo', 'top-photo', 'side-photo', 'user-photo')) for n in parsed.nodes)
            sections.append({'key': key, 'label': label, 'identity': identity,
                             'markup': section, 'catalog': has_catalog, 'legacy_cells': legacy_cells})
        return {'sections': sections, 'card_id': card_id, 'digest': hashlib.sha256(raw).hexdigest(),
                'project_tools_first': meta.get('nx-project-tools-position') == 'first',
                'project_image_scale': meta.get('nx-project-image-scale') or '100',
                'project_model': next((json.loads(markup.source[n['open_end']:n['content_end']])
                    for n in markup.nodes if n['attrs'].get('id') == 'nx-project-model'), None),
                'operation_options': json.loads(meta.get('nx-operation-options') or '{}')}
    except Exception as exc:
        raise RuntimeError('Существующая HTML-карта не изменена. Не удалось безопасно прочитать её:\n%s\n%s'
                           % (display_file_name(path), exc)) from exc


def plan_setup_update(existing, reports):
    """A folder path identifies a setup; MCS is its current display name.

    Updating a parent replaces its saved section and absorbs former sections
    of its descendants. Other folders remain independent even with equal MCS
    names. Legacy sections without a folder can only match by a unique name.
    Resolve the complete batch before changing or publishing any section.
    """
    saved = existing['sections'] if existing else []
    old_count, incoming, consumed = len(saved), set(), set()
    resolved, paths = [], []

    def folder(entry):
        return tuple(entry['identity']['folder']) if entry['identity'] is not None else ()

    for report in reports:
        identity = setup_identity(report)
        label, path = identity_key(identity)
        token = ('folder', path) if path else ('mcs', label)
        if token in incoming:
            raise RuntimeError('Повторно выбрана одна и та же папка или установ «%s».' % label)
        if path and any(path[:len(previous)] == previous or previous[:len(path)] == path
                        for previous in paths):
            raise RuntimeError('Одновременно выбраны родительская папка и её подпапка. '
                               'Выберите родительскую папку один раз; она включает все вложенные операции.')
        if path:
            paths.append(path)
        incoming.add(token)
        exact = [i for i, entry in enumerate(saved) if path and folder(entry) == path]
        descendants = [i for i, entry in enumerate(saved) if path and
                       len(folder(entry)) > len(path) and folder(entry)[:len(path)] == path]
        candidates = exact + descendants
        if not candidates:
            candidates = [i for i, entry in enumerate(saved)
                          if not folder(entry) and entry['label'] == label]
            if len(candidates) > 1:
                raise RuntimeError('Нельзя однозначно сопоставить старые листы без пути папки для установа «%s». '
                                   'Существующая карта не изменена.' % label)
        if any(i in consumed for i in candidates):
            raise RuntimeError('Нельзя однозначно сопоставить установ «%s» с листами существующей карты. '
                               'Карта не изменена. Проверьте повторяющиеся имена СКС и выбор папок установов.'
                               % label)
        anchors = exact or sorted(candidates)
        index = anchors[0] if anchors else None
        consumed.update(candidates)
        resolved.append((report, identity, index, candidates))
    replacements = {index: (report, identity, candidates)
                    for report, identity, index, candidates in resolved if index is not None}
    entries = []
    used_keys = {entry['key'] for entry in saved}
    for index, saved_entry in enumerate(saved):
        if index in consumed and index not in replacements:
            continue
        entry = dict(saved_entry)
        if index in replacements:
            report, identity, candidates = replacements[index]
            # Only the same folder (or an unambiguous legacy MCS) owns these
            # annotations. A new parent must not inherit an arbitrary child's.
            same_setup = (bool(identity['folder']) and folder(saved_entry) == tuple(identity['folder'])
                          or not folder(saved_entry) and saved_entry['label'] == identity['mcs'])
            entry.update(label=identity['mcs'], identity=identity, report=report,
                         catalog=any(saved[i]['catalog'] for i in candidates),
                         annotations_markup=saved_entry['markup'] if same_setup else None)
        entries.append(entry)
    for report, identity, index, candidates in resolved:
        if index is not None:
            continue
        key = 'setup-' + uuid.uuid4().hex
        while key in used_keys:
            key = 'setup-' + uuid.uuid4().hex
        used_keys.add(key)
        entry = {'key': key, 'catalog': False}
        entry.update(label=identity['mcs'], identity=identity, report=report)
        entries.append(entry)
    if not entries:
        raise RuntimeError('Не выбраны установы для карты.')
    for entry in entries:
        if 'report' not in entry and entry.get('legacy_cells'):
            raise RuntimeError('В невыбранном установе «%s» есть дополнительная ячейка прежней версии. '
                               'Для перехода на %s один раз выведите этот установ вместе с остальными. '
                               'Существующая карта не изменена.' % (entry['label'], SCRIPT_VERSION))
    return entries, {'updated': len(replacements), 'added': len(resolved) - len(replacements),
                     'preserved': old_count - len(consumed), 'total': len(entries),
                     'consolidated': len(consumed) - len(replacements)}


def renumber_saved_sections(sections, setup_count):
    parsed = [CardMarkup(section) for section in sections]
    total = sum(len(markup.pages()) for markup in parsed)
    result, index = [], 0
    for markup in parsed:
        edits = []
        for page in markup.pages():
            index += 1
            for name, value in (('page-number', index), ('page-count', total)):
                fields = [n for n in markup.nodes if page['open_end'] <= n['start'] < page['content_end']
                          and markup.has_class(n, name)]
                if len(fields) != 1:
                    raise RuntimeError('Не удалось обновить нумерацию листов; старая карта сохранена.')
                edits.append((fields[0]['open_end'], fields[0]['content_end'], str(value)))
        for node in markup.nodes:
            if node['attrs'].get('data-field') == 'setup_total' and node['tag'] not in ('input', 'select'):
                edits.append((node['open_end'], node['content_end'], str(setup_count)))
        result.append(edit_markup_spans(markup.source, edits))
    return result


def move_shared_catalog(sections, source_index, first=False):
    """Keep the shared catalog first or last when setups are refreshed/appended.

    Its saved pages and browser source template are moved, never regenerated
    from the unselected setup, so manual catalog edits survive as well.
    """
    target_index = 0 if first else len(sections) - 1
    old = CardMarkup(sections[source_index])
    pages = [n for n in old.pages() if old.has_class(n, 'project-tools-page')]
    templates = [n for n in old.nodes if n['attrs'].get('data-editor-id') == 'catalog-source']
    if len(templates) > 1:
        raise RuntimeError('Повторяется общий список инструментов; старая карта сохранена.')
    content = ''.join(old.raw(n) for n in pages)
    if source_index == target_index:
        containers = [n for n in old.nodes if old.has_class(n, 'setup-pages')]
        if len(containers) != 1:
            raise RuntimeError('Не найдено место для общего списка инструментов.')
        position = containers[0]['open_end'] if first else containers[0]['content_end']
        sections[source_index] = edit_markup_spans(old.source,
            [(n['start'], n['end'], '') for n in pages] + [(position, position, content)])
        return sections
    new = CardMarkup(sections[target_index])
    removals = [(n['start'], n['end'], '') for n in pages + templates]
    sections[source_index] = edit_markup_spans(old.source, removals)
    containers = [n for n in new.nodes if new.has_class(n, 'setup-pages')]
    if len(containers) != 1:
        raise RuntimeError('Не найдено место для общего списка инструментов.')
    position = containers[0]['open_end'] if first else containers[0]['content_end']
    edits = [(position, position, content)]
    # Saved editors have an empty catalog template even without catalog pages.
    # Remove it so it cannot shadow the transferred source on the next load.
    for node in new.nodes:
        if node['attrs'].get('data-editor-id') == 'catalog-source':
            edits.append((node['start'], node['end'], ''))
    if templates:
        template = old.raw(templates[0])
        key = new.nodes[0]['attrs']['data-setup-id']
        template = re.sub(r'(?<=\s)id=("[^"]*"|\x27[^\x27]*\x27)',
                          'id="' + key + '-catalog-source"', template, count=1)
        root = new.nodes[0]
        edits.append((root['content_end'], root['content_end'], template))
    sections[target_index] = edit_markup_spans(new.source, edits)
    return sections


def normalize_project_catalog(sections, project_view_image=None, project_model=None):
    """Migrate only the shared catalog; keep every setup's own pages intact.

    The saved master rows, fields and tool hierarchy survive re-export. The
    corner picture is refreshed from the current export, even in a partial merge.
    A separate source template prevents scaled/repeated rows becoming data.
    """
    parsed = CardMarkup(sections[0])
    root = next(n for n in parsed.nodes if parsed.has_class(n, 'setup-document'))
    key = root['attrs']['data-setup-id']
    pages = [n for n in parsed.pages() if parsed.has_class(n, 'project-tools-page')]
    templates = [n for n in parsed.nodes if n['attrs'].get('data-editor-id') == 'catalog-source']
    if len(templates) > 1 or not pages:
        raise RuntimeError('Не найден единый список инструментов проекта. Прежняя карта сохранена.')

    def inside(node, parent):
        return parent['open_end'] <= node['start'] < parent['content_end']

    source = None
    if templates:
        candidate = CardMarkup(parsed.raw(templates[0]))
        if any(candidate.has_class(n, 'catalog-table') for n in candidate.nodes):
            source = candidate
    if source is None:
        source = CardMarkup(''.join(parsed.raw(n) for n in pages))
    tables = [n for n in source.nodes if source.has_class(n, 'catalog-table')]
    row_nodes = [n for n in source.nodes if n['tag'] == 'tr' and n['parent'] is not None
                 and n['parent']['tag'] == 'tbody' and n['parent']['parent'] in tables
                 and n['attrs'].get('data-repeat') != 'true']
    rows = [source.raw(n) for n in row_nodes]
    descriptions = [{'kind': n['attrs'].get('data-kind', 'tool'),
                     'depth': int(n['attrs'].get('data-depth', 0))} for n in row_nodes]
    master = CardMarkup(source.raw(tables[0]))
    tbody = next(n for n in master.nodes if n['tag'] == 'tbody')
    table = edit_markup_spans(master.source, [(tbody['open_end'], tbody['content_end'], ''.join(rows))])
    opening = table[:table.index('>') + 1]
    clean_opening = re.sub(r'\sstyle=("[^"]*"|\x27[^\x27]*\x27)', '', opening)
    clean_opening = clean_opening[:-1] + ' style="font-size:7pt">'
    table = clean_opening + table[len(opening):]

    # Reopening/saving keeps the embedded picture. Every NX export supplies a
    # fresh startup view; never substitute an automatically oriented setup view.
    preview = next((n for n in parsed.nodes if parsed.has_class(n, 'project-isometry')
                    and not parsed.in_template(n)), None)
    images = [n for n in parsed.nodes if n['tag'] == 'img' and preview is not None and inside(n, preview)]
    src = project_view_image if project_view_image is not None else (images[0]['attrs'].get('src', '') if images else '')
    if src and not re.match(r'^data:image/(?:png|jpeg|webp|gif);base64,', src, re.I):
        raise RuntimeError('Некорректный снимок текущего вида для первого листа.')
    dimensions = ''
    if src.lower().startswith('data:image/png;base64,'):
        # The size is available before image decoding and on detached clones,
        # so the first layout, saved HTML and print all use the same rectangle.
        header_bytes = base64.b64decode(src.split(',', 1)[1][:32], validate=True)
        if (len(header_bytes) != 24 or header_bytes[:8] != b'\x89PNG\r\n\x1a\n' or
                header_bytes[12:16] != b'IHDR'):
            raise RuntimeError('Некорректный PNG текущего вида для первого листа.')
        image_width, image_height = struct.unpack('>II', header_bytes[16:24])
        if not image_width or not image_height:
            raise RuntimeError('Некорректные размеры снимка первого листа.')
        dimensions = ' width="%d" height="%d"' % (image_width, image_height)
    picture = ('<img alt="Текущий вид NX на момент запуска"%s src="%s">' % (dimensions, escaped(src))
               if src else '')
    preview = '<div class="project-isometry" data-source-view="current">%s</div>' % picture
    if project_model:
        preview = '<div class="project-isometry project-model" data-source-view="current"></div>'

    # Live header fields contain the user's latest edits; the source template
    # supplies only the complete, non-repeated tool rows.
    page = CardMarkup(parsed.raw(pages[0]))
    footer = next(n for n in page.nodes if page.has_class(n, 'page-footer'))
    header = next(n for n in page.nodes if n['tag'] == 'header')
    removals = {}
    for node in page.nodes:
        if page.has_class(node, 'catalog-table'):
            outer = node
            while outer['parent'] is not None and not page.has_class(outer['parent'], 'page-content'):
                outer = outer['parent']
            removals[outer['start']] = outer
        elif page.has_class(node, 'catalog-first') or page.has_class(node, 'project-isometry'):
            removals[node['start']] = node
    edits = [(n['start'], n['end'], '') for n in removals.values()]
    if not project_model:
        # A browser-saved mesh page retains absolute layout coordinates. Remove
        # those when the new export explicitly disables its model.
        page_root = next(n for n in page.nodes if page.has_class(n, 'project-tools-page'))
        attrs = dict(page_root['attrs'])
        attrs['class'] = ' '.join(c for c in attrs.get('class', '').split() if c != 'project-model-page')
        attrs.pop('data-project-tools-bottom', None)
        attrs.pop('data-model-clipped', None)
        style = ';'.join(p for p in attrs.get('style', '').split(';')
                         if p.strip() and not p.strip().startswith('--project-'))
        if style:
            attrs['style'] = style
        else:
            attrs.pop('style', None)
        opening = '<' + page_root['tag'] + ''.join(' ' + k + ('="%s"' % escaped(v) if v is not None else '')
                                                  for k, v in attrs.items()) + '>'
        edits.append((page_root['start'], page_root['open_end'], opening))
    for node in page.nodes:
        if page.has_class(node, 'catalog-title'):
            edits.append((node['open_end'], node['content_end'], 'Инструменты проекта'))
    legend_tables = [n for n in page.nodes if page.has_class(n, 'legends') and inside(n, header)]
    # Keep the saved wording, but remove editable field bindings and old fitted
    # font sizes from the first-sheet legend, including cards from older versions.
    for node in page.nodes:
        if page.has_class(node, 'field') and any(inside(node, table) for table in legend_tables):
            edits.append((node['start'], node['open_end'],
                          '<div class="field center legend-text" contenteditable="false">'))
    legend = '' if legend_tables else tool_legend_table(locked=True)
    edits += [(header['content_end'], header['content_end'], legend + preview),
              (footer['start'], footer['start'], table)]
    canonical = edit_markup_spans(page.source, edits)

    plan = project_tool_plan(descriptions, 214 / TABLE_ROW_MM)
    count = len(plan)
    width = (194. - 4. * (count - 1)) / count
    scale = min(1., 218. / ((max(len(chunk) for chunk in plan) + 1) * TABLE_ROW_MM + .16))
    master = CardMarkup(table)
    tbody = next(n for n in master.nodes if n['tag'] == 'tbody')

    def repeated(markup):
        parsed_row = CardMarkup(markup)
        labels = [n for n in parsed_row.nodes if parsed_row.has_class(n, 'tool-group-label')]
        result = edit_markup_spans(markup, [(n['content_end'], n['content_end'], ' · продолжение') for n in labels])
        return result.replace('<tr ', '<tr data-repeat="true" ', 1)

    columns = []
    for chunk in plan:
        body = ''.join(repeated(rows[i]) if repeat else rows[i] for i, repeat in chunk)
        column = edit_markup_spans(table, [(tbody['open_end'], tbody['content_end'], body)])
        column = column.replace('style="font-size:7pt"',
            'style="font-size:7pt;position:absolute;left:0;top:0;width:%.8fmm;'
            'transform:scale(%.10f);transform-origin:0 0"' % (width / scale, scale), 1)
        height = ((len(chunk) + 1) * TABLE_ROW_MM + .16) * scale + .3
        columns.append('<div class="catalog-scroll"><div class="project-tool-plane" '
                       'style="width:%.8fmm;height:%.8fmm">%s</div></div>' % (width, height, column))
    layout = '<div class="catalog-columns" data-columns="%d">%s</div>' % (count, ''.join(columns))
    live = canonical.replace(table, layout, 1)
    template = '<template id="%s-catalog-source" data-editor-id="catalog-source">%s</template>' % (escaped(key), canonical)
    container = next(n for n in parsed.nodes if parsed.has_class(n, 'setup-pages'))
    sections[0] = edit_markup_spans(parsed.source,
        [(n['start'], n['end'], '') for n in pages + templates]
        + [(container['open_end'], container['open_end'], live),
           (root['content_end'], root['content_end'], template)])
    return sections


def remove_html_notifications(markup):
    """Strip retired messages from saved setups while preserving all card data."""
    parsed = CardMarkup(markup)
    classes = {'notice', 'export-notices', 'export-error', 'toast', 'help', 'project-model-hint',
               'empty-image', 'empty-tools', 'empty-operations'}
    spans = sorted((node['start'], node['end']) for node in parsed.nodes
                   if node['tag'] == 'noscript'
                   or node['attrs'].get('id') in ('status', 'save-hint')
                   or classes.intersection(node['attrs'].get('class', '').split()))
    legacy_tool_messages = {
        'В CAM-проекте нет инструментов с используемыми траекториями.',
        'Не удалось прочитать список инструмента проекта.'}
    for node in parsed.nodes:
        parent = node['parent']
        table = parent['parent'] if parent is not None and parent['tag'] == 'tbody' else parent
        if (node['tag'] == 'tr' and table is not None and parsed.has_class(table, 'catalog-table')
                and parsed.text(node).strip() in legacy_tool_messages):
            spans.append((node['start'], node['end']))
    edits = []
    for start, end in sorted(spans):
        if not edits or start >= edits[-1][1]:
            edits.append((start, end, ''))
    return edit_markup_spans(markup, edits)


def retain_setup_annotations(markup, saved_markup):
    """Carry saved notes, their manual heights and X/Y/Z datums into fresh headers."""
    if not saved_markup:
        return markup
    saved, current = CardMarkup(saved_markup), CardMarkup(markup)

    def field_name(node):
        name = node['attrs'].get('data-field', '')
        if name in ('note', 'note_label'):
            return name
        return 'datum_' + name[-1].upper() if re.fullmatch(r'datum_[XYZxyz]', name) else None

    def in_cover(node):
        while node is not None:
            if saved.has_class(node, 'cover-page'):
                return True
            node = node['parent']
        return False

    # Live cover fields take precedence over continuation pages. Templates may
    # still contain defaults; an intentionally cleared live value is authoritative.
    sources = sorted((node for node in saved.nodes
                      if field_name(node) and not saved.in_template(node)),
                     key=lambda node: (not in_cover(node), node['start']))
    values = {}
    for node in sources:
        name = field_name(node)
        if name in values:
            continue
        if node['tag'] == 'input':
            value = node['attrs'].get('value', '')
        elif node['tag'] == 'select':
            options = [option for option in saved.nodes if option['tag'] == 'option'
                       and node['open_end'] <= option['start'] < node['content_end']]
            chosen = next((option for option in options if 'selected' in option['attrs']),
                          options[0] if options else None)
            value = (chosen['attrs'].get('value', saved.text(chosen)) if chosen else '')
        else:
            value = saved.text(node)
        if name in ('note', 'note_label'):
            # Keep existing line breaks and inline formatting without trimming
            # the comment or decoding/re-encoding its HTML entities.
            values[name] = (escaped(value) if node['tag'] in ('input', 'select')
                            else saved_markup[node['open_end']:node['content_end']])
        else:
            values[name] = value

    edits = []
    saved_scope = next((node for node in saved.nodes if saved.has_class(node, 'setup-document')), None)
    current_scope = next((node for node in current.nodes if current.has_class(node, 'setup-document')), None)
    if saved_scope is not None and current_scope is not None:
        try:
            stored = json.loads(saved_scope['attrs'].get('data-note-heights', '{}'))
            heights = {key: max(14.2, min(270, value)) for key, value in stored.items()
                       if re.fullmatch(r'cover|operations-[1-9][0-9]*', key)
                       and type(value) in (int, float) and math.isfinite(value)} if isinstance(stored, dict) else {}
        except (ValueError, TypeError, OverflowError):
            heights = {}
        if heights:
            opening = markup[current_scope['start']:current_scope['open_end']]
            opening = opening[:-1] + ' data-note-heights="%s">' % escaped(json.dumps(heights))
            edits.append((current_scope['start'], current_scope['open_end'], opening))
    for node in current.nodes:
        name = field_name(node)
        if name not in values:
            continue
        value = values[name]
        if name in ('note', 'note_label'):
            edits.append((node['open_end'], node['content_end'], value))
        else:
            opening = markup[node['start']:node['open_end']]
            opening, count = re.subn(r'\bvalue="[^"]*"',
                                    lambda match: 'value="%s"' % escaped(value), opening, count=1)
            if count != 1:
                raise RuntimeError('Не удалось восстановить поле привязки ' + name)
            edits.append((node['start'], node['open_end'], opening))
            cell = node['parent']['parent']
            for mirror in current.nodes:
                if mirror['parent'] is cell and current.has_class(mirror, 'datum-print'):
                    edits.append((mirror['open_end'], mirror['content_end'], escaped(value)))
    return edit_markup_spans(markup, edits)


def write_preview(output, report):
    """Render selected setups and carry saved, unselected sections forward."""
    output = io_path(output)
    existing = report.get('_existing_card')
    setups = report.get('setups', [report])
    entries, report['merge_result'] = plan_setup_update(existing, setups)
    report['card_id'] = existing['card_id'] if existing else report.get('card_id', uuid.uuid4().hex)
    report['html_revision'] = uuid.uuid4().hex
    catalog = next((r for r in setups if 'project_tools' in r), None)
    owners = [i for i, entry in enumerate(entries) if entry.get('catalog')]
    if len(owners) > 1:
        raise RuntimeError('В существующей карте несколько общих списков инструментов. Карта не изменена.')
    owner = owners[0] if owners else 0
    sections, options, counter = [], [], [0]
    for index, entry in enumerate(entries):
        key = entry['key']
        item = entry.get('report')
        if item is None:
            sections.append(remove_html_notifications(entry['markup']))
        else:
            item['setup_total'] = len(entries)
            item['include_project_tools'] = index == owner
            item['project_catalog'] = True
            if item['include_project_tools'] and catalog is not None:
                item['project_tools'] = copy.deepcopy(catalog['project_tools'])
                item['project_tool_count'] = len(item['project_tools'])
            item['project_tools_available'] = catalog is not None
            markup = render_setup(output, item, key, counter)
            sections.append(retain_setup_annotations(markup, entry.get('annotations_markup')))
        options.append('<option value="%s">%s</option>' % (key, escaped(entry['label'])))
    sections = move_shared_catalog(sections, owner, first=True)
    # Explicit None removes a previous mesh; absent means a legacy caller that
    # did not request any change to the shared model.
    project_model = report.get('project_model', existing.get('project_model') if existing else None)
    sections = normalize_project_catalog(sections, report.get('project_view_image'), project_model)
    body = ''.join(renumber_saved_sections(sections, len(entries)))
    context = {'filename': report['document_stem'] + '.html', 'id': report['card_id'], 'key': report['card_id'],
               'title': TITLE}
    page = CARD_TEMPLATE.replace('__DOCUMENT_TITLE__', escaped(TITLE + ' — ' + report['document_stem']))
    page = page.replace('__CARD_ID__', report['card_id']).replace('__CARD_REVISION__', report['html_revision'])
    page = page.replace('__PROJECT_TOOLS_POSITION__', 'first')
    page = page.replace('__PROJECT_MODEL__', json.dumps(project_model, ensure_ascii=False,
        separators=(',', ':'), allow_nan=False).replace('<', '\\u003c').replace('&', '\\u0026'))
    page = page.replace('__PROJECT_IMAGE_SCALE__', escaped(existing.get('project_image_scale', 100) if existing else 100))
    page = page.replace('__OPERATION_OPTIONS__', escaped(json.dumps(
        existing.get('operation_options', {}) if existing else {}, ensure_ascii=False)))
    page = page.replace('__SETUPS__', body).replace('__SETUP_OPTIONS__', ''.join(options))
    page = page.replace('__DOCUMENT_CONTEXT__', json.dumps(context, ensure_ascii=False).replace('<', '\\u003c').replace('&', '\\u0026'))
    path = output / (report['document_stem'] + '.html')
    path.write_text(page, encoding='utf-8')
    return path


def copy_windows_text(text):
    """Copy Unicode text with the Windows API; no shell or extra process."""
    if os.name != 'nt':
        raise OSError('Автоматическое копирование пути доступно в Windows.')
    import ctypes
    from ctypes import wintypes
    user = ctypes.WinDLL('user32', use_last_error=True)
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    user.GetActiveWindow.argtypes, user.GetActiveWindow.restype = [], wintypes.HWND
    user.GetForegroundWindow.argtypes, user.GetForegroundWindow.restype = [], wintypes.HWND
    user.OpenClipboard.argtypes, user.OpenClipboard.restype = [wintypes.HWND], wintypes.BOOL
    user.EmptyClipboard.argtypes, user.EmptyClipboard.restype = [], wintypes.BOOL
    user.CloseClipboard.argtypes, user.CloseClipboard.restype = [], wintypes.BOOL
    user.SetClipboardData.argtypes = [wintypes.UINT, wintypes.HANDLE]
    user.SetClipboardData.restype = wintypes.HANDLE
    kernel.GlobalAlloc.argtypes = [wintypes.UINT, ctypes.c_size_t]
    kernel.GlobalAlloc.restype = wintypes.HGLOBAL
    kernel.GlobalLock.argtypes, kernel.GlobalLock.restype = [wintypes.HGLOBAL], ctypes.c_void_p
    kernel.GlobalUnlock.argtypes, kernel.GlobalUnlock.restype = [wintypes.HGLOBAL], wintypes.BOOL
    kernel.GlobalFree.argtypes, kernel.GlobalFree.restype = [wintypes.HGLOBAL], wintypes.HGLOBAL
    owner = user.GetActiveWindow() or user.GetForegroundWindow()
    if not owner:
        raise OSError('Не найдено окно для доступа к буферу обмена.')
    payload = str(text).encode('utf-16-le') + b'\0\0'
    handle = kernel.GlobalAlloc(0x0002, len(payload))  # GMEM_MOVEABLE
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    opened, transferred = False, False
    try:
        pointer = kernel.GlobalLock(handle)
        if not pointer:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            ctypes.memmove(pointer, payload, len(payload))
        finally:
            kernel.GlobalUnlock(handle)
        for attempt in range(6):
            if user.OpenClipboard(owner):
                opened = True
                break
            if attempt < 5:
                time.sleep(.05)
        if not opened:
            raise ctypes.WinError(ctypes.get_last_error())
        if not user.EmptyClipboard():
            raise ctypes.WinError(ctypes.get_last_error())
        if not user.SetClipboardData(13, handle):  # CF_UNICODETEXT
            raise ctypes.WinError(ctypes.get_last_error())
        transferred = True  # Windows now owns the allocation; never free it.
    finally:
        if opened:
            user.CloseClipboard()
        if not transferred:
            kernel.GlobalFree(handle)


def copy_card_path(card, report):
    if card is None or not export_is_complete(report):
        return ''
    if not card.is_file():
        return '\n\nПуть не скопирован: HTML-файл не найден.'
    try:
        copy_windows_text(display_file_name(card.parent.resolve()))
        return '\n\nПуть к папке с картой наладки скопирован в буфер обмена Windows.'
    except Exception as exc:
        return '\n\nКарта сохранена. Не удалось скопировать путь к папке в буфер обмена: %s' % exc


def open_output_folder(card, report, open_folder=True):
    """Open only a responding helper, otherwise open the finished HTML directly."""
    if card is None or not export_is_complete(report) or os.name != 'nt':
        return []
    errors = []
    try:
        editor = local_card_url(card)
    except Exception as exc:
        editor = card
        errors.append('Карта сохранена, но локальное автосохранение не запущено: %s\n'
                      'HTML откроется с обычным сохранением через браузер.' % exc)
    targets = [(editor, 'карту')]
    if open_folder:
        targets.append((card.parent, 'папку результата'))
    for target, label in targets:
        try:
            value = display_file_name(target)
            if not value.startswith('http://') and len(value.encode('utf-16-le')) // 2 >= 260:
                try:
                    value = windows_short_name(target)
                except OSError:
                    pass
            os.startfile(value)
        except OSError as exc:
            errors.append('Не удалось открыть %s: %s\n%s' % (label, target, exc))
    return errors


def publish_output(card, destination, expected_digest):
    """Replace one complete HTML atomically, without moving the previous file."""
    card, destination = io_path(card), io_path(destination)
    with local_card_guard():
        actual_digest = hashlib.sha256(destination.read_bytes()).hexdigest() if destination.exists() else None
        if actual_digest != expected_digest:
            raise RuntimeError('HTML-карта изменилась во время экспорта. Чтобы не потерять правки, '
                               'запись отменена. Сохраните изменения и повторите запуск.')
        # Staging was created on the destination volume for atomic replacement.
        # If replacement fails, the previous HTML remains at its original path.
        card.replace(destination)
    return destination


def finalize_output(output, report, destination=None):
    # A failed batch must not publish a partial card or a diagnostic bundle.
    if not export_is_complete(report):
        return None
    output = io_path(output)
    project = io_path(destination) if destination is not None else output.parent
    if '_existing_card' not in report:
        report['_existing_card'] = read_existing_card(project / (report['document_stem'] + '.html'))
    card = write_preview(output, report)
    report['html_file'] = card.name
    existing = report['_existing_card']
    return publish_output(card, project / card.name, existing['digest'] if existing else None)


def cleanup_work_files(staging, created_directories):
    """Remove only this run's capture files and newly created empty folders."""
    errors = []
    staging = io_path(staging) if staging is not None else None
    if staging is not None and staging.exists():
        try:
            shutil.rmtree(staging)
        except OSError as exc:
            errors.append('Не удалось удалить временные файлы снимков: %s\n%s' % (staging, exc))
    for path in reversed(created_directories):
        try:
            io_path(path).rmdir()
        except OSError:
            pass  # The result or another process may now use the folder.
    return errors


def run(nx, ui, output, report, context=None):
    session = nx.Session.GetSession()
    part, display_part = session.Parts.Work, session.Parts.Display
    if part is None or display_part is None:
        raise RuntimeError("Открой CAM-файл в NX и повтори запуск.")
    if str(part.Tag) != str(display_part.Tag):
        raise RuntimeError("CAM-файл должен быть одновременно рабочей "
                           "и отображаемой деталью. Открой его в отдельном окне NX.")
    report["part_name"] = part.Leaf
    report["part_units"] = enum_name(part.PartUnits, getattr(getattr(nx, "BasePart", None), "Units", None),
                                     ("Millimeters", "Inches"))
    view = display_part.ModelingViews.WorkView
    context = context if context is not None else resolve_context(nx, ui, part, report)
    group = context["mcs"]
    report["mcs_name"], report["mcs_selection"] = group.Name, context["source"]
    if 'mcs_activation' not in report:
        activate_first_mcs(nx, part, context, report)
    basis, origin = read_mcs(part, group)
    report["mcs_axes_absolute"], report["mcs_origin_absolute"] = basis, origin
    ipw_source = resolve_ipw_source(nx, part, context, report)
    report["current_stage"] = "Чтение заготовки Workpiece"
    diagnostic_event('stage', stage=report['current_stage'])
    read_workpiece_blank(nx, part, ipw_source, report)
    capture_views(nx, display_part, view, basis, output, report, ipw_context=context)
    report["current_stage"] = "Чтение таблицы операций"
    diagnostic_event('stage', stage=report['current_stage'])
    read_operation_data(nx, part, context, report)
    report["status"] = ("warning" if report["restore_errors"] or report.get('mcs_anchor_issues') or
                        report.get('mcs_activation', {}).get('status') == 'warning' or
                        report.get('tool_number_check', {}).get('complete') is False or report.get('blank', {}).get('message') else "ok")
    report["current_stage"] = "Завершено"
    diagnostic_event('stage', stage=report['current_stage'])


def new_report():
    return {'format': 'nx-setup-view-prototype', 'version': SCRIPT_VERSION,
            'created_at': datetime.datetime.now().astimezone().isoformat(),
            'status': 'error', 'views': [], 'restore_errors': [],
            'current_stage': 'Запуск', 'projection': 'unchanged_from_NX',
            'scope': 'paired_ipw_no_ipw', 'native_image_resolution': True,
            'data_issues': [], 'operation_rows': [], 'tools': [],
            'note': 'Парные виды IPW / NO IPW; операции выбранного установа в порядке Program Order.'}


def fit_final_view(nx, part, report):
    """Fit only after all captures have restored the original camera/display."""
    record = report['final_view_fit'] = {'status': 'pending', 'method': 'UF.View.FitView', 'margin': FRAME_MARGIN}
    try:
        fit_visible_view(nx, part.ModelingViews.WorkView)
        part.Views.Refresh()
        record['status'] = 'done'
        return []
    except Exception as exc:
        record.update(status='error', message=str(exc))
        return ['Вписывание детали в рабочее поле: ' + str(exc)]


def main():
    global EXPORT_PROGRESS
    previous_progress, progress = EXPORT_PROGRESS, None
    ui, nx, part, staging, card = None, None, None, None, None
    components, preparation = None, None
    jobs, state = [], new_report()
    processing, cancelled = False, False
    created_directories = []
    try:
        try:
            import NXOpen
            import NXOpen.Assemblies
            import NXOpen.CAM
            import NXOpen.Facet
            import NXOpen.Gateway
            import NXOpen.UF
            nx = NXOpen
            ui = nx.UI.GetUI()
            session = nx.Session.GetSession()
            part, display = session.Parts.Work, session.Parts.Display
            if part is None:
                raise RuntimeError('Открой CAM-файл в NX и повтори запуск.')
            if display is None or str(part.Tag) != str(display.Tag):
                raise RuntimeError('CAM-файл должен быть рабочей и отображаемой деталью.')
            preparation_selection = PreparationSelection(nx, ui, part)
            project_camera = snapshot_view(display.ModelingViews.WorkView)
            project_camera['projection'] = read_view_projection(nx, display.ModelingViews.WorkView)
            project_camera['axes'] = [xyz(display.ModelingViews.WorkView.GetAxis(axis)) for axis in
                                      (nx.XYZAxis.XAxis, nx.XYZAxis.YAxis, nx.XYZAxis.ZAxis)]
            project_name, project_file = project_name_from_part(part), project_file_from_part(part)
            state.update(project_name=project_name, project_file=display_file_name(project_file),
                         document_stem=safe_file_component(project_name), format='nx-setup-document')
            jobs = resolve_setup_jobs(nx, ui, part, state)
            confirm_duplicate_tool_numbers(nx, part, state)
            components = AssemblyComponentDisplay(nx, part)
            for job in jobs:
                report = job['report']
                report['tool_number_check'] = copy.deepcopy(state['tool_number_check'])
                report['data_issues'].extend(copy.deepcopy(state['data_issues']))
                report.update(project_name=project_name, project_file=display_file_name(project_file),
                              document_stem=state['document_stem'], html_file=state['document_stem'] + '.html')
            choices = ask_all_setup_components(nx, components, jobs)
            for job, selected in zip(jobs, choices):
                job['context']['visible_component_keys'] = selected
            # Preview visibility is restored before meshing the startup view.
            # No output files until every confirmation has been answered.
            destination = project_file.parent / 'Карты Наладки' / safe_file_component(project_name, True)
            state['_existing_card'] = read_existing_card(destination / (state['document_stem'] + '.html'))
            plan_setup_update(state['_existing_card'], [job['report'] for job in jobs])
            progress = ExportProgress(len(jobs))
            EXPORT_PROGRESS = progress
            preparation = SetupPreparation(nx, part, jobs, state['_preparation_options'], preparation_selection)
            preparation.apply()
            created_directories.extend(path for path in (destination.parent, destination) if not path.exists())
            staging = make_output_folder(project_file.parent, project_name)
            model_enabled = state['_preparation_options'].get('create_project_model', False)
            prepare_capture_folders(staging, jobs, project_view=not model_enabled)
            processing = True
            state['project_model'], state['project_view_image'] = None, ''
            if model_enabled:
                progress.mesh()
                state['project_model'] = collect_project_model(
                    nx, display, project_camera,
                    accuracy=state['_preparation_options'].get('project_model_accuracy', 1.),
                    triangle_limit=state['_preparation_options'].get('project_model_triangle_limit', PROJECT_MODEL_DEFAULT_TRIANGLES))
            else:
                progress.update(2, 'Изображение первого листа', 'Вписывание текущего вида NX')
                state['project_view_image'] = capture_project_view(nx, display, staging, project_camera)
                progress.update(55, 'Изображение первого листа готово')
            ensure_view_triad(nx, display, state, 'start')
            for index, job in enumerate(jobs, 1):
                report, context = job['report'], job['context']
                progress.setup(index, setup_label(report))
                try:
                    activate_first_mcs(nx, part, context, report)
                    try:
                        components.apply(context['visible_component_keys'])
                        run(nx, ui, job['output'], report, context)
                        progress.setup_progress(.99, 'Восстановление видимости компонентов')
                    finally:
                        restore_errors = components.restore()
                        if restore_errors:
                            report['restore_errors'].extend(restore_errors)
                            raise RuntimeError('Не удалось восстановить видимость сборки:\n' + '\n'.join(restore_errors))
                    progress.setup_progress(1, 'Установ подготовлен')
                except Exception as exc:
                    report.update(status='error', error=str(exc))
                    if components.dirty or report.get('capture_failed'):
                        # A capture failure must not repeat the same failed
                        # camera cycle for every remaining setup in the batch.
                        for remaining in jobs[index:]:
                            remaining['report'].update(status='not_run', current_stage='Не выполнялся после ошибки съёмки')
                        state['stopped_after_setup_error'] = True
                        break
        except ExportCancelled:
            cancelled = True
        except Exception as exc:
            state.update(status='error', error=str(exc), model_limit_exceeded=isinstance(exc, ProjectModelLimitError))
        finally:
            if progress is not None:
                progress.phase = 'finish'
                progress.update(stage='Восстановление вида NX')
            if components is not None:
                restore_errors = components.restore()
                if restore_errors:
                    state.update(status='error', error='Не удалось восстановить исходную видимость компонентов:\n' + '\n'.join(restore_errors))
            if processing:
                errors = []
                try:
                    errors.extend('Триада: ' + text for text in ensure_view_triad(nx, part, state, 'finish'))
                except Exception as exc:
                    errors.append('Включение триады: ' + str(exc))
                errors.extend(fit_final_view(nx, part, state))
                for job in jobs:
                    report = job['report']
                    report['final_view_fit'] = copy.deepcopy(state['final_view_fit'])
                    report['view_triad_finish'] = copy.deepcopy(state.get('view_triad', []))
                    report['restore_errors'].extend(errors)
                    if errors and report['status'] != 'error':
                        report['status'] = 'warning'
        if not cancelled and staging is not None:
            state['setups'] = [j['report'] for j in jobs]
            state['setup_total'] = len(jobs)
            state['setups_completed'] = sum(export_is_complete(j['report']) for j in jobs)
            state['status'] = ('error' if state.get('error') or state['setups_completed'] != len(jobs)
                               else 'warning' if any(j['report']['status'] == 'warning' for j in jobs) else 'ok')
            if state['status'] == 'error' and not state.get('error'):
                state['error'] = 'Не все установы созданы. Новая карта не сохранена; предыдущая готовая карта сохранена.'
            try:
                if export_is_complete(state):
                    progress.update(94, 'Подготовка файла карты', 'Объединение видов всех установов')
                    collect_document_assets(staging, jobs)
                    progress.update(96, 'Сохранение карты', 'Формирование и запись HTML')
                    card = finalize_output(staging, state, destination)
            except Exception as exc:
                state.update(status='error', error=state.get('error', '') + '\nНе удалось сохранить общий документ: ' + str(exc))
    finally:
        try:
            if preparation is not None:
                try:
                    preparation.finish(card is not None)
                except Exception as exc:
                    state.update(status='error', error=(state.get('error', '') + '\n' + str(exc)).strip())
                if card is not None and preparation.warnings and state['status'] == 'ok':
                    state['status'] = 'warning'
            if progress is not None:
                progress.phase = 'finish'
                progress.update(99 if card is not None else None, 'Завершение создания карты')
            state['cleanup_errors'] = cleanup_work_files(staging, created_directories)
            if state['cleanup_errors'] and state['status'] != 'error':
                state['status'] = 'warning'
            if progress is not None and card is not None and export_is_complete(state):
                progress.update(100, 'Карта наладки создана', 'HTML сохранён', complete=True)
        finally:
            EXPORT_PROGRESS = previous_progress
            if progress is not None:
                progress.close()
    if cancelled and not state.get('error'):
        diagnostic_event('export.cancelled')
        return
    diagnostic_event('export.result', status=state['status'], error=state.get('error'),
                     setups=[{'name': setup_label(j['report']), 'status': j['report']['status'],
                              'error': j['report'].get('error')} for j in jobs])
    message = '' if state.get('model_limit_exceeded') else TITLE + '\n\n'
    if jobs and not state.get('model_limit_exceeded'):
        message += 'Создано установов: %d из %d.\n' % (state.get('setups_completed', 0), len(jobs))
        message += '\n'.join('%d. %s: %s' % (j['report']['setup_index'], setup_label(j['report']),
                              'готово' if export_is_complete(j['report']) else
                              'не выполнялся' if j['report']['status'] == 'not_run' else 'ошибка') for j in jobs)
        if state.get('stopped_after_setup_error'):
            message += '\nОставшиеся установы не выполнялись после ошибки; лишние циклы съёмки остановлены.'
    if card is not None:
        message += '\n\nHTML:\n%s' % display_file_name(card)
        merged = state.get('merge_result', {})
        if merged:
            message += '\n\nВ общей карте: %d установов. Обновлено: %d; добавлено: %d; сохранено без обновления: %d.' % (
                merged['total'], merged['updated'], merged['added'], merged['preserved'])
            if merged.get('consolidated'):
                message += '\nОбъединено прежних отдельных записей выбранных папок: %d.' % merged['consolidated']
    if state.get('error'):
        message += ('\n\n' if message else '') + state['error']
        failures = ['Установ «%s»: %s' % (setup_label(j['report']), j['report']['error'])
                    for j in jobs if j['report'].get('error')]
        if failures:
            message += '\n\n' + '\n'.join(failures)
    if state.get('cleanup_errors'):
        message += '\n\n' + '\n'.join(state['cleanup_errors'])
    if card is not None and preparation is not None and preparation.result():
        message += '\n\n' + preparation.result()
    message += copy_card_path(card, state)
    if ui is None:
        print('Это журнал NX. Запускайте его внутри NX: Журнал / Воспроизвести.\n' + message)
        return
    try:
        kind_name = {'ok': 'Information', 'warning': 'Warning'}.get(state['status'], 'Error')
        ui.NXMessageBox.Show(TITLE, getattr(nx.NXMessageBox.DialogType, kind_name), message)
    except Exception:
        print(message)
    opening_errors = open_output_folder(card, state)
    if opening_errors:
        try:
            ui.NXMessageBox.Show(TITLE, nx.NXMessageBox.DialogType.Warning, '\n\n'.join(opening_errors))
        except Exception:
            print('\n'.join(opening_errors))



def namespace_editor_markup(markup, key):
    """Keep HTML/ARIA identifiers unique while retaining local selector keys."""
    identifiers = set(re.findall(r'(?<=\s)id="([A-Za-z][A-Za-z0-9_-]*)"', markup))
    markup = re.sub(r'(?<=\s)id="([A-Za-z][A-Za-z0-9_-]*)"',
                    lambda m: 'id="%s-%s" data-editor-id="%s"' % (key, m[1], m[1]), markup)
    for name in identifiers:
        markup = markup.replace('for="%s"' % name, 'for="%s-%s"' % (key, name))
        markup = markup.replace('aria-controls="%s"' % name, 'aria-controls="%s-%s"' % (key, name))
        markup = markup.replace('href="#%s"' % name, 'href="#%s-%s"' % (key, name))
        markup = markup.replace('url(#%s)' % name, 'url(#%s-%s)' % (key, name))
    # The grid chooser is created by JavaScript after loading.
    markup = markup.replace('aria-controls="cell-grid-picker"', 'aria-controls="%s-cell-grid-picker"' % key)
    return markup


def collect_document_assets(staging, jobs):
    """Flatten captured files with stable per-setup prefixes, for all selected setups."""
    staging = io_path(staging)
    for index, job in enumerate(jobs, 1):
        output, report = job['output'], job['report']
        mapping = {}
        if output is not None:
            output = io_path(output)
            for path in sorted(output.iterdir()):
                if path.is_file():
                    name = 's%02d_%s' % (index, path.name)
                    shutil.copyfile(path, staging / name)
                    mapping[path.name] = name
            # Only remove this run's known per-setup folder after copying assets.
            if output.parent == staging and output.name == str(index):
                shutil.rmtree(output)
        for view in report.get('views', []):
            view.setdefault('logical_file', view['file'])
            for field_name in ('file', 'no_ipw_file'):
                if view.get(field_name) in mapping:
                    view[field_name] = mapping[view[field_name]]
        report['assets'] = mapping


def render_setup(output, report, key, counter):
    mark_duplicate_tool_numbers(report)
    if 'author' not in report:
        report.update(read_card_settings())
    gallery = gallery_html(output, report)
    # Первый лист содержит только шапку, виды и инструменты.
    cover = '<article class="page cover-page"><div class="page-content">' + card_header(report) + gallery
    cover += '<footer class="page-footer"><span class="page-number"></span> / <span class="page-count"></span></footer></div></article>'
    body, chunks = [cover], paginate_operations(report)
    for index, rows in enumerate(chunks):
        content = card_header(report, compact=True)
        content += '<div class="rows-area">%s</div>' % operations_table(rows, report)
        body.append('<article class="page operation-page continuation"><div class="page-content">%s<footer class="page-footer"><span class="page-number"></span> / <span class="page-count"></span></footer></div></article>' % content)
    body.extend(project_tool_pages(report))
    count = len(body)
    body = [page.replace('<span class="page-number"></span>', '<span class="page-number">%d</span>' % (counter[0]+i+1))
            .replace('<span class="page-count"></span>', '<span class="page-count">__PROJECT_PAGE_COUNT__</span>') for i, page in enumerate(body)]
    counter[0] += count
    markup = SETUP_TEMPLATE.replace('__BODY__', ''.join(body))
    continuation = '<article class="page operation-page continuation"><div class="page-content">' + card_header(report, compact=True) + '<div class="rows-area">' + operations_table([], report) + '</div><footer class="page-footer"><span class="page-number"></span> / <span class="page-count"></span></footer></div></article>'
    markup = markup.replace('__CONTINUATION_TEMPLATE__', continuation)
    markup = markup.replace('__OPERATION_SOURCE__', operations_table(report.get('operation_rows', []), report))
    markup = markup.replace('__SETUP_LABEL__', escaped(setup_label(report)))
    identity = escaped(json.dumps(setup_identity(report), ensure_ascii=False))
    return '<section class="setup-document" data-setup-id="%s" data-nx-setup-identity="%s">%s</section>' % (
        key, identity, namespace_editor_markup(markup, key))


def project_card_header(report):
    return ('<header class="continuation-header no-note"><table class="metadata">'
            '<colgroup><col style="width:15.5%%"><col></colgroup><tr><th>Деталь:</th><td>%s</td></tr>'
            '<tr><th>Разработчик:</th><td>%s</td></tr></table>%s</header>') % (
                field('part', report.get('project_name', ''), 'Деталь', 'bold'),
                field('author', report.get('author', DEFAULT_PROGRAMMER), 'Фамилия И. О.', 'bold'), tool_legend_table(locked=True))


if __name__ == '__main__':
    if sys.argv[1:] == ['--nx-card-helper']:
        local_card_worker()
    else:
        main()

