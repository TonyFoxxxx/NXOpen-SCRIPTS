# -*- coding: utf-8 -*-
# NX_Update_Script_Buttons.py
# SCRIPT_VERSION: V1.12
"""
Апдейтер NX / Designcenter для Windows: GitHub manifest или папка обновлений.

GitHub: Проверить загружает только manifest.json; Применить выбранное — только
выбранные исходники. SHA-256, внутренняя версия и постоянное имя проверяются
до записи. Новые скрипты не отмечены автоматически. Местные правки и файлы
без подтверждённого происхождения требуют ручного выбора.

Настройки: NX_Update_Script_Buttons.ini рядом с журналом. Старые [Paths] и
[Options] читаются; новый [Source] задаёт github/folder и постоянный Raw URL.
Рабочие пути, кнопки NX и пользовательские INI сохраняются. Новые INI создаются
из примеров только при первоначальной установке; существующие не заменяются.
Пустой реестр нумератора не устанавливается поверх истории или рядом с уже
существующим INI. Новые кнопки и New User Command не создаются.

Запись использует проверенный промежуточный файл рядом с назначением и замену
целого файла. При обычной ошибке выполняется откат из памяти. При аварийном
завершении промежуточные .pending не исполняются; остатки завершённых процессов
удаляются при следующем запуске. Постоянных копий, отчётов и логов нет.
Сам апдейтер заменяется последним, после закрытия окна и завершения потоков.
Новая редакция работает при следующем запуске. Python/NXOpen из скачанных
исходников во время проверки и установки не запускаются.

Версии: V1, V1.01, ..., V1.09, V1.10. Читаются также старые VERSION/__version__.
Служебная SHA-256 отметка в конце установленного файла сохраняет контроль
местных изменений; в исходники GitHub эта отметка не включается.
"""

import ast
import configparser
import ctypes
import fnmatch
import hashlib
import io
import json
import ntpath
import os
import queue
import re
import threading
import tokenize
import ssl
import stat
import uuid
import urllib.parse
import urllib.request
from collections import defaultdict
from contextlib import ExitStack, contextmanager

SCRIPT_VERSION = "V1.12"
SCRIPT_NAME = "Обновление скриптов NX"
SCRIPT_AUTHOR = bytes(value ^ ((0x5D + index * 11) & 0xFF)
                      for index, value in enumerate((63, 17, 83, 42, 230, 250, 230, 245, 243, 175, 179, 174, 153))).decode('utf-8')
DEFAULT_WORKING_FOLDER = r"C:\ProgramData\NX_SCRIPTS"
DEFAULT_UPDATE_FOLDER = ''
RAW_ROOT = 'https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/'
DEFAULT_MANIFEST_URL = RAW_ROOT + 'manifest.json'
NUMBERING_DATA_FOLDER = r'C:\ProgramData\3_NX_DATA'
MAX_MANIFEST_SIZE = 1024 * 1024
HTTP_TIMEOUT = 20
UPDATER_ID = 'nx_update_script_buttons'
CONFIG_FILENAME = 'NX_Update_Script_Buttons.ini'
MAX_CONFIG_SIZE = 256 * 1024
SCRIPT_EXTENSIONS = {".py", ".cs", ".vb"}
MAX_SCRIPT_SIZE = 16 * 1024 * 1024
MAX_BATCH_SIZE = 128 * 1024 * 1024
VERSION_RE = re.compile(r"^(.*?)[ _.-]v(\d+(?:\.\d+)*)(?:\s*\(\d+\))?$", re.I)
COPY_RE = re.compile(r"\s*\(\d+\)$")
SKIP_FOLDERS = {".git", "__pycache__", "архив", "история", "archive", "history", "backup", "backups"}
MARKER = "NX_UPDATER_INSTALLED_V1 "
SCRIPT_LABELS = {
    'nx_rename_operations_in_selected_folder.cs': '<RENAME>',
    'planarchik.cs': '<Tool_ON>',
    'nx_setup_prototype.py': '<SETUP>',
    'nx_number_program_folders.cs': '<Folder_Numbering>',
    'nx_export_drawing_to_pdf.cs': 'EXPORT .PDF',
    'nx_open_project_folder.cs': '<PROJECT_FOLDER>',
    'nx_open_setup_cards_folder.py': '<SETUP_FOLDER>',
    'nx_open_frez_tsekh_folder.cs': '<FREZ_FOLDER>',
    'nx_eskd_format_gost_a.cs': 'ESKD',
    'nx_export_current_view_to_dxf.cs': 'EXPORT_TO_DXF',
    'nx_tool_d_to_description.cs': '<TOOL_D>',
    'nx_postprocess_to_machine.cs': '<POST>',
    'nx_cam_setup_tools.cs': '<NEW_PROJECT>',
}


def window_title(purpose=''):
    title = SCRIPT_NAME + ' — ' + SCRIPT_VERSION + ' — ' + SCRIPT_AUTHOR
    return title + ' — ' + purpose if purpose else title



def version_key(parts):
    parts = tuple(parts)
    while len(parts) > 1 and parts[-1] == 0:
        parts = parts[:-1]
    return parts

def script_identity(path):
    stem, ext = ntpath.splitext(ntpath.basename(path))
    if ext.lower() not in SCRIPT_EXTENSIONS:
        return None
    match = VERSION_RE.fullmatch(stem)
    if match:
        family, number = match.group(1), match.group(2)
        return (family.casefold(), ext.lower()), version_key(int(x) for x in number.split(".")), number
    return (COPY_RE.sub("", stem).casefold(), ext.lower()), None, "без версии"


def stable_script_name(path):
    """Normalize a release name, without changing the case of the base name."""
    stem, ext = ntpath.splitext(ntpath.basename(path))
    match = VERSION_RE.fullmatch(stem)
    stem = match.group(1) if match else COPY_RE.sub('', stem)
    reserved = stem.split('.')[0].rstrip(' ').upper()
    if (ext.lower() not in SCRIPT_EXTENSIONS or not stem or stem != stem.strip()
            or stem.endswith('.') or any(ord(c) < 32 or c in '<>:"/\\|?*' for c in stem)
            or reserved in {'CON', 'PRN', 'AUX', 'NUL', 'CONIN$', 'CONOUT$'}
            or re.fullmatch(r'(?:COM|LPT)[1-9¹²³]', reserved)):
        raise ValueError('Недопустимое имя рабочего файла Windows: ' + ntpath.basename(path))
    name = stem + ext.lower()
    if script_identity(name)[0] != script_identity(path)[0]:
        raise ValueError('Неоднозначное имя: повторный суффикс версии или копии. Переименуйте источник: ' + ntpath.basename(path))
    return name


def path_key(path):
    return ntpath.normcase(ntpath.normpath(path))

def iter_scripts(folder, recursive, cancelled, include_empty=False):
    pending = [(folder, 0)]
    while pending:
        if cancelled.is_set():
            return
        current, depth = pending.pop()
        try:
            with os.scandir(current) as iterator:
                entries = list(iterator)
        except OSError as exc:
            # Partial searches could silently select an older release.
            raise OSError("Не удалось прочитать папку:\n{}\n{}".format(current, exc))
        for item in sorted(entries, key=lambda x: x.name.casefold()):
            if cancelled.is_set():
                return
            if item.is_symlink():
                continue
            if item.is_dir(follow_symlinks=False):
                if recursive and item.name.casefold() not in SKIP_FOLDERS:
                    attrs = getattr(item.stat(follow_symlinks=False), "st_file_attributes", 0)
                    if attrs & 0x400:  # Do not traverse Windows junctions/reparse points.
                        continue
                    if depth >= 20:
                        raise ValueError("Слишком большая вложенность папок: " + item.path)
                    pending.append((item.path, depth + 1))
            elif item.is_file(follow_symlinks=False) and script_identity(item.name):
                if include_empty or item.stat(follow_symlinks=False).st_size > 0:
                    yield os.path.abspath(item.path)

def choose_candidate(group, candidates, cancelled=None, cache=None):
    family, current_version, _ = group["identity"]
    items = []
    seen = set()
    issues = []
    cache = {} if cache is None else cache
    for path in candidates:
        if cancelled is not None and cancelled.is_set():
            return dict(group, new_path='', status='Отменено', eligible=False, checked=False)
        identity = script_identity(path)
        if not identity or identity[0] != family or path_key(path) in seen:
            continue
        seen.add(path_key(path))
        key = path_key(path)
        if key not in cache:
            try:
                raw = read_script(path)
                info = internal_info(path, raw)
                if info['version'] is None:
                    raise ValueError('Внутри файла нет версии SCRIPT_VERSION / VERSION')
                if info['dirty']:
                    raise ValueError('Код изменён после служебной отметки; подготовьте исходник без устаревшей отметки')
                cache[key] = {'path': path, 'version': info['version'],
                              'label': info['label'], 'source_hash': digest(raw)}
            except Exception as exc:
                cache[key] = {'error': str(exc)}
        record = cache[key]
        if 'error' in record:
            issues.append(path + ': ' + record['error'])
        else:
            items.append(record)
    row = dict(group, new_path="", status="Не найдена", eligible=False, checked=False,
               available_label='—', source_issues=issues)
    if not items:
        if issues:
            row.update(status='Нет источника с читаемой версией; см. подробности', available_label='нет версии')
        return row
    highest = max(item['version'] for item in items)
    best = [item for item in items if item['version'] == highest]
    row['available_label'] = best[0]['label']
    if len(best) != 1:
        row["status"] = "Неоднозначно: {} файла(-ов) одной версии".format(len(best))
        row["alternatives"] = [item['path'] for item in best]
        return row
    candidate = best[0]
    path, new_version = candidate['path'], candidate['version']
    row["new_path"] = path
    row.update(source_hash=candidate['source_hash'], available_version=new_version)
    if current_version is not None and new_version < current_version:
        row["status"] = "В папке более старая версия"
    elif path_key(path) == path_key(group["path"]):
        row["status"] = "Это сам рабочий файл"
    elif current_version is None:
        row.update(status='Нет версии на ПК — можно обновить', eligible=True)
    elif new_version > current_version:
        row.update(status="Новая версия", eligible=True, checked=True)
    else:
        row.update(status="Проверка содержимого той же версии", eligible=True)
    if issues and row['eligible']:
        row.update(checked=False, status='Есть источники без версии / с ошибкой — вручную')
    return row

def resolve_setting_path(value, base_folder):
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] == '"':
        value = value[1:-1]
    value = ntpath.expandvars(os.path.expandvars(value))
    if not value or '\x00' in value or '\n' in value or '\r' in value:
        raise ValueError('В INI указан пустой или недопустимый путь')
    if re.search(r'%[A-Za-z_][A-Za-z0-9_]*%', value):
        raise ValueError('Не найдена переменная окружения в пути: ' + value)
    drive, _ = ntpath.splitdrive(value)
    if drive:
        if not ntpath.isabs(value):
            raise ValueError('Укажите полный путь вместо ' + value)
        return ntpath.normpath(value)
    if os.path.isabs(value):
        return os.path.normpath(value)
    return os.path.abspath(os.path.join(base_folder, value.replace('\\', os.sep)))


def excluded_script(path, patterns):
    identity = script_identity(path)
    if not identity or identity[0] == ('nx_update_script_buttons', '.py'):
        return True
    name = ntpath.basename(path).casefold()
    return any(fnmatch.fnmatchcase(name, pattern.casefold()) for pattern in patterns)


def config_parser(raw=None):
    parser = configparser.ConfigParser(interpolation=None, delimiters=('=',),
                                       inline_comment_prefixes=None, strict=True)
    parser.optionxform = str
    if raw is not None:
        if len(raw) > MAX_CONFIG_SIZE:
            raise ValueError('INI превышает 256 КБ')
        try:
            text = raw.decode('utf-16') if raw.startswith((b'\xff\xfe', b'\xfe\xff')) else raw.decode('utf-8-sig')
        except UnicodeDecodeError:
            text = raw.decode('cp1251')
        parser.read_string(text)
    return parser


def config_section(parser, name):
    matches = [s for s in parser.sections() if s.casefold() == name.casefold()]
    if len(matches) > 1:
        raise ValueError('Раздел [' + name + '] повторяется')
    result = {}
    for key, value in parser.items(matches[0]) if matches else []:
        folded = key.casefold()
        if folded in result:
            raise ValueError('Параметр ' + key + ' повторяется в [' + name + ']')
        result[folded] = value
    return result


def load_settings(script_path):
    folder = os.path.dirname(os.path.abspath(script_path))
    stable_path = os.path.join(folder, CONFIG_FILENAME)
    matching_path = os.path.splitext(os.path.abspath(script_path))[0] + '.ini'
    config_path = stable_path
    if not os.path.isfile(stable_path) and os.path.isfile(matching_path):
        config_path = matching_path
    settings = {'config_path': config_path, 'working_folder': DEFAULT_WORKING_FOLDER,
                'update_folder': DEFAULT_UPDATE_FOLDER, 'update_recursive': True,
                'source_type': 'github', 'manifest_url': DEFAULT_MANIFEST_URL,
                'working_recursive': False, 'exclude_files': ('NX_Update_Script_Buttons*.py',),
                '_config_bytes': None}
    try:
        with open(config_path, 'rb') as stream:
            raw = stream.read(MAX_CONFIG_SIZE + 1)
    except FileNotFoundError:
        return settings
    try:
        parser = config_parser(raw)
        paths, options = config_section(parser, 'Paths'), config_section(parser, 'Options')
        source = config_section(parser, 'Source')
        for key in ('working_folder',):
            if key not in paths:
                raise ValueError('В разделе [Paths] нужен параметр ' + key)

        def boolean(key, default):
            value = options.get(key, default).strip().casefold()
            if value in ('yes', 'true', '1', 'on', 'да'):
                return True
            if value in ('no', 'false', '0', 'off', 'нет'):
                return False
            raise ValueError('Для ' + key + ' укажите yes или no')

        source_type = source.get('type', 'github').strip().lower()
        if source_type not in ('github', 'folder'):
            raise ValueError('В [Source] type должен быть github или folder')
        manifest_url = source.get('manifest_url', DEFAULT_MANIFEST_URL).strip()
        validate_raw_url(manifest_url, manifest=True)
        update_folder = paths.get('update_folder', '').strip()
        if source_type == 'folder' and not update_folder:
            raise ValueError('Для источника folder укажите update_folder')
        settings.update(working_folder=resolve_setting_path(paths['working_folder'], folder),
                        update_folder=resolve_setting_path(update_folder, folder) if update_folder else '',
                        source_type=source_type, manifest_url=manifest_url,
                        update_recursive=boolean('search_update_subfolders', 'yes'),
                        working_recursive=boolean('search_working_subfolders', 'no'),
                        exclude_files=tuple(p.strip() for p in options.get(
                            'exclude_files', 'NX_Update_Script_Buttons*.py').split(';') if p.strip()),
                        _config_bytes=raw)
        # V1.03 [Scripts] is intentionally ignored: every check discovers the folder afresh.
        return settings
    except (configparser.Error, ValueError) as exc:
        raise ValueError('Ошибка в INI ' + config_path + '\n' + str(exc))


def settings_payload(settings):
    parser = config_parser(settings['_config_bytes'])
    for section in list(parser.sections()):
        if section.casefold() == 'scripts':
            parser.remove_section(section)

    def update_section(name, values):
        existing = next((s for s in parser.sections() if s.casefold() == name.casefold()), None)
        if existing is None:
            parser.add_section(name)
            existing = name
        # Preserve unrelated options and sections, including user-specific settings.
        for key in list(parser[existing]):
            if key.casefold() in values:
                parser.remove_option(existing, key)
        for key, value in values.items():
            parser.set(existing, key, value)

    folder = os.path.dirname(settings['config_path'])
    update_section('Paths', {
        'working_folder': resolve_setting_path(settings['working_folder'], folder),
        'update_folder': resolve_setting_path(settings['update_folder'], folder) if settings['update_folder'] else '',
    })
    update_section('Source', {'type': settings['source_type'], 'manifest_url': settings['manifest_url']})
    update_section('Options', {
        'search_update_subfolders': 'yes' if settings['update_recursive'] else 'no',
        'search_working_subfolders': 'yes' if settings['working_recursive'] else 'no',
        'exclude_files': '; '.join(settings['exclude_files']),
    })
    output = io.StringIO()
    output.write('; NX_Update_Script_Buttons.ini — ' + SCRIPT_VERSION + '\n')
    output.write('; Папки и параметры сохраняются кнопкой «Проверить».\n')
    output.write('; Список скриптов определяется автоматически при каждой проверке.\n\n')
    parser.write(output)
    payload = output.getvalue().replace('\n', '\r\n').encode('utf-8-sig')
    if len(payload) > MAX_CONFIG_SIZE:
        raise ValueError('INI превышает 256 КБ')
    return payload


def save_settings(settings):
    payload = settings_payload(settings)
    before = settings['_config_bytes']
    if payload != before:
        commit_jobs([dict(path=os.path.abspath(settings['config_path']), data=payload,
                          expected=digest(before) if before is not None else None,
                          create=before is None, is_self=False)])
    return dict(settings, _config_bytes=payload)


def configured_buttons(settings, cancelled):
    buttons = []
    for path in iter_scripts(settings['working_folder'], settings['working_recursive'], cancelled, include_empty=True):
        if excluded_script(path, settings['exclude_files']):
            continue
        name = ntpath.basename(path)
        buttons.append({'path': path, 'identity': script_identity(path),
                        'titles': [SCRIPT_LABELS.get(name.casefold(), name)]})
    return sorted(buttons, key=lambda b: path_key(b['path']))


def digest(data):
    return hashlib.sha256(data).hexdigest()


def read_script(path):
    with open(path, 'rb') as stream:
        data = stream.read(MAX_SCRIPT_SIZE + 1)
    if not data:
        raise ValueError('Пустой файл: ' + path)
    if len(data) > MAX_SCRIPT_SIZE:
        raise ValueError('Скрипт превышает 16 МБ: ' + path)
    return data


def marker_codec(data):
    for bom, encoding in ((b'\xff\xfe\x00\x00', 'utf-32-le'), (b'\x00\x00\xfe\xff', 'utf-32-be'),
                          (b'\xff\xfe', 'utf-16-le'), (b'\xfe\xff', 'utf-16-be')):
        if data.startswith(bom):
            return encoding
    # Metadata is ASCII and therefore valid in UTF-8 and Windows code pages.
    return 'latin-1'


def unpack_script(data):
    text = data.decode(marker_codec(data))
    match = re.search(r'(?m)^(?:#|//|\x27) ' + re.escape(MARKER) + r'(\{[^\r\n]+\})\s*\Z', text)
    if not match:
        return data, None, False
    try:
        meta = json.loads(match.group(1))
        size = meta['size']
        version = meta['version']
        if not isinstance(size, int) or not 0 < size <= MAX_SCRIPT_SIZE:
            raise ValueError('Invalid metadata size')
        if version is not None and not re.fullmatch(r'\d+(?:\.\d+)*', version):
            raise ValueError('Invalid metadata version')
        separator = meta['separator']
        if separator not in ('', '\n', '\r\n'):
            raise ValueError('Invalid metadata separator')
        prefix = text[:match.start()]
        if separator:
            if not prefix.endswith(separator):
                raise ValueError('Metadata separator changed')
            prefix = prefix[:-len(separator)]
        payload = prefix.encode(marker_codec(data))
        dirty = len(payload) != size or digest(payload) != meta['sha256']
        return payload, meta, dirty
    except (KeyError, TypeError, ValueError) as exc:
        raise ValueError('Повреждена отметка установленной версии: ' + str(exc))


def parse_version(value):
    if not isinstance(value, str):
        raise ValueError('Версия должна быть строкой, например "V1.02"')
    match = re.fullmatch(r'[vV]?([0-9]+(?:\.[0-9]+)*)', value.strip())
    if not match:
        raise ValueError('Неверная запись версии: ' + value)
    number = match.group(1)
    return version_key(int(part) for part in number.split('.')), 'V' + number


def source_text(payload, ext):
    if payload.startswith((b'\xff\xfe\x00\x00', b'\x00\x00\xfe\xff')):
        return payload.decode('utf-32')
    if payload.startswith((b'\xff\xfe', b'\xfe\xff')):
        return payload.decode('utf-16')
    if ext == '.py':
        encoding, _ = tokenize.detect_encoding(io.BytesIO(payload).readline)
        return payload.decode(encoding)
    try:
        return payload.decode('utf-8-sig')
    except UnicodeDecodeError:
        return payload.decode('cp1251')


def version_declarations(text, ext):
    """Read comments/literal constants as data; never execute a candidate."""
    comments, constants = [], []
    names = {'script_version', 'scriptversion', 'version', '__version__'}
    if ext == '.py':
        try:
            for token in tokenize.generate_tokens(io.StringIO(text).readline):
                if token.start[0] > 50:
                    break
                if token.type == tokenize.COMMENT:
                    comments.append(token.string[1:].strip())
        except (tokenize.TokenError, IndentationError, SyntaxError):
            pass  # An intact header may identify a broken installed script.
        try:
            nodes = ast.parse(text).body
        except SyntaxError:
            nodes = []
        for node in nodes:
            targets = node.targets if isinstance(node, ast.Assign) else [node.target] if isinstance(node, ast.AnnAssign) else []
            for target in targets:
                if not isinstance(target, ast.Name) or target.id.casefold() not in names:
                    continue
                if isinstance(node.value, ast.Constant) and isinstance(node.value.value, str):
                    constants.append((target.id, node.value.value))
                elif target.id.casefold() in {'script_version', 'scriptversion'}:
                    raise ValueError('SCRIPT_VERSION должна быть строковой константой')
    else:
        # Comments and string literals must not become false version declarations.
        in_block = False
        in_verbatim = False
        for line_number, line in enumerate(text.splitlines(), 1):
            if ext == '.vb':
                match = re.match(r"^\s*'\s*(.*)$", line)
                if match:
                    if line_number <= 50:
                        comments.append(match.group(1).strip())
                    continue
                match = re.match(r'(?i)^\s*(?:(?:Public|Private|Friend|Protected|Shared)\s+)*Const\s+(SCRIPT_VERSION|ScriptVersion|VERSION|__version__)\s+As\s+String\s*=\s*"([^"]*)"\s*(?:\x27.*)?$', line)
                if match:
                    constants.append((match.group(1), match.group(2)))
                continue

            # C#: strip comments while retaining literal string tokens. This
            # also prevents matching commented-out const declarations.
            code = []
            i = 0
            while i < len(line):
                if in_block:
                    end = line.find('*/', i)
                    fragment = line[i:] if end < 0 else line[i:end]
                    if line_number <= 50:
                        comments.append(fragment.strip().lstrip('*').strip())
                    if end < 0:
                        break
                    in_block = False
                    code.append(' ')
                    i = end + 2
                elif in_verbatim:
                    if line[i] == '"':
                        if i + 1 < len(line) and line[i + 1] == '"':
                            i += 2
                        else:
                            in_verbatim = False
                            i += 1
                    else:
                        i += 1
                elif line.startswith('//', i):
                    if line_number <= 50:
                        comments.append(line[i + 2:].strip())
                    break
                elif line.startswith('/*', i):
                    in_block = True
                    i += 2
                elif line.startswith('@"', i):
                    # Version constants use ordinary quoted strings.
                    code.append(' <verbatim-string> ')
                    in_verbatim = True
                    i += 2
                elif line[i] in ('"', "'"):
                    start, quote = i, line[i]
                    i += 1
                    while i < len(line):
                        if line[i] == '\\':
                            i += 2
                        elif line[i] == quote:
                            i += 1
                            break
                        else:
                            i += 1
                    code.append(line[start:i])
                else:
                    code.append(line[i])
                    i += 1
            code_line = ''.join(code)
            match = re.match(r'(?i)^\s*(?:(?:public|private|protected|internal|new|static|readonly)\s+)*(?:const\s+)?string\s+(SCRIPT_VERSION|ScriptVersion|VERSION|__version__)\s*=\s*"([^"]*)"\s*;', code_line)
            if match:
                constants.append((match.group(1), match.group(2)))

    headers, old_headers = [], []
    for comment in comments:
        match = re.fullmatch(r'(?i)SCRIPT_VERSION\s*:\s*(.*?)\s*', comment)
        if match:
            headers.append(('SCRIPT_VERSION в шапке', match.group(1)))
        else:
            match = re.fullmatch(r'(?i)(?:version|версия)\s*[:=]\s*([vV]?[0-9]+(?:\.[0-9]+)*)', comment)
            if match:
                old_headers.append(('версия в шапке', match.group(1)))
    declarations = headers + constants
    return declarations if declarations else old_headers


def internal_info(path, raw):
    identity = script_identity(path)
    if not identity:
        raise ValueError('Неподдерживаемое расширение: ' + path)
    payload, meta, dirty = unpack_script(raw)
    declarations = version_declarations(source_text(payload, identity[0][1]), identity[0][1])
    versions = [(where,) + parse_version(value) for where, value in declarations]
    if len({item[1] for item in versions}) > 1:
        raise ValueError('Разные версии внутри файла: ' + '; '.join(where + ' = ' + label for where, _, label in versions))
    if versions:
        _, version, label = versions[0]
        origin = 'внутри файла'
        if meta and meta['version'] and not dirty and parse_version(meta['version'])[0] != version:
            raise ValueError('Версия в коде не совпадает со служебной отметкой обновлятора')
    elif meta and meta['version']:
        version, label = parse_version(meta['version'])
        origin = 'отметка прежнего обновлятора'
    else:
        version, label, origin = None, 'нет версии', 'версия внутри не найдена'
    return dict(family=identity[0], version=version, label=label, payload=payload,
                meta=meta, dirty=dirty, origin=origin)


def pack_script(source, source_path):
    info = internal_info(source_path, source)
    payload, meta, dirty = info['payload'], info['meta'], info['dirty']
    if info['version'] is None:
        raise ValueError('Внутри источника нет версии: ' + source_path)
    if dirty:
        raise ValueError('В источнике устаревшая служебная отметка: ' + source_path)
    version = info['label'][1:]
    ext = info['family'][1]
    if ext == '.py':
        # Syntax only: do not execute code or import NX modules from the update.
        compile(payload, source_path, 'exec')
    codec = marker_codec(payload)
    text = payload.decode(codec)
    newline = '\r\n' if '\r\n' in text else '\n'
    prefix = {'.py': '#', '.cs': '//', '.vb': "'"}[ext]
    separator = '' if text.endswith('\n') else newline
    record = {'version': version, 'source': ntpath.basename(source_path), 'size': len(payload), 'sha256': digest(payload), 'separator': separator}
    comment = separator + prefix + ' ' + MARKER + json.dumps(record, ensure_ascii=True, separators=(',', ':')) + newline
    return payload + comment.encode(codec)


def installed_info(path, raw):
    info = internal_info(path, raw)
    identity = info['family'], info['version'], info['label']
    label = info['label']
    if info['dirty']:
        label += ' *'
    return identity, label, info['payload'], info['dirty']


def select_in_batch(row, action):
    """Bulk selection is deliberate, but local edits/source warnings stay manual."""
    if not row.get('eligible'):
        return False
    if action == 'installs':
        return row.get('operation') == 'install' and not row.get('source_issues')
    if action == 'updates' and row.get('operation') == 'update':
        unknown_local_version = row['identity'][1] is None
        return bool(row.get('checked') or (unknown_local_version and
                    not row.get('local_dirty') and not row.get('source_issues')))
    return False


def select_row(row, value):
    """Keep user intent independent of the native checkbox image/state."""
    if value and not row.get('eligible'):
        raise ValueError('Этот файл нельзя отметить.\n' + row['status'])
    row['selected'] = bool(value)


def displayed_status(row):
    return ('Выбрано — ' if row.get('selected') else '') + row['status']


def folder_identity(folder):
    info = os.stat(folder)
    if not os.path.isdir(folder):
        raise ValueError('Рабочая папка не найдена: ' + folder)
    return path_key(os.path.realpath(folder)), info.st_dev, info.st_ino


def installation_conflict(path):
    # Also detect case-insensitive collisions in portable tests / mixed shares.
    name = os.path.basename(path).casefold()
    with os.scandir(os.path.dirname(path)) as entries:
        return any(entry.name.casefold() == name for entry in entries)


def scan_settings(settings, cancelled):
    if settings.get('source_type', 'github') == 'github':
        return scan_github(settings, cancelled)
    buttons = configured_buttons(settings, cancelled)
    if cancelled.is_set():
        return []
    return scan(buttons, settings['update_folder'], settings['update_recursive'], cancelled,
                working_folder=settings['working_folder'], exclude_files=settings['exclude_files'])


def scan(buttons, folder, recursive, cancelled, working_folder=None, exclude_files=()):
    index = defaultdict(list)
    for source in iter_scripts(folder, recursive, cancelled, include_empty=True):
        if excluded_script(source, exclude_files):
            continue
        try:
            if excluded_script(stable_script_name(source), exclude_files):
                continue
        except ValueError:
            pass  # Show invalid Windows names as disabled rows, rather than hiding them.
        index[script_identity(source)[0]].append(source)
    result = []
    cache = {}
    self_path = path_key(os.path.abspath(__file__)) if '__file__' in globals() else ''
    for button in buttons:
        if cancelled.is_set():
            return []
        base = dict(button, identity=script_identity(button['path']), operation='update')
        row = dict(base, new_path='', eligible=False, checked=False, status='', installed_label='—', available_label='—')
        try:
            if path_key(os.path.abspath(base['path'])) == self_path:
                raise ValueError('Сам обновлятор сейчас запущен — пропущен')
            raw = read_script(base['path'])
            identity, label, current_payload, dirty = installed_info(base['path'], raw)
            base['identity'] = identity
            row = choose_candidate(base, index[identity[0]], cancelled, cache)
            row.update(installed_label=label, target_hash=digest(raw), local_dirty=dirty)
            if not row['new_path']:
                result.append(row)
                continue
            if row['eligible']:
                source = read_script(row['new_path'])
                if digest(source) != row['source_hash']:
                    raise ValueError('Источник изменился во время проверки. Нажмите «Проверить» заново')
                ready = pack_script(source, row['new_path'])
                source_payload = unpack_script(ready)[0]
                same_version = row['available_version'] == identity[1]
                if source_payload == current_payload and same_version:
                    row.update(status='Установлена эта версия', eligible=False, checked=False)
                elif same_version:
                    row.update(status='Та же версия, другой код — вручную', checked=False)
                elif dirty:
                    row.update(status='Новая версия; местные правки — вручную', checked=False)
        except OSError as exc:
            row.update(status='Файл недоступен: ' + str(exc), eligible=False, checked=False)
        except Exception as exc:
            row.update(status=str(exc), eligible=False, checked=False)
        result.append(row)
    if not working_folder or cancelled.is_set():
        return [] if cancelled.is_set() else result

    working_folder = os.path.abspath(working_folder)
    root_identity = folder_identity(working_folder)
    # A broken/unreadable local script still counts as present. Never create a duplicate.
    present = {script_identity(button['path'])[0] for button in buttons}
    additions = []
    for family in sorted(set(index) - present):
        if cancelled.is_set():
            return []
        group = dict(path=working_folder, identity=(family, None, 'нет версии'),
                     titles=[ntpath.basename(index[family][0])], operation='install')
        row = choose_candidate(group, index[family], cancelled, cache)
        row.update(installed_label='Не установлен', checked=False, root_identity=root_identity)
        try:
            name = stable_script_name(row['new_path'] or index[family][0])
            row.update(path=os.path.join(working_folder, name), titles=[SCRIPT_LABELS.get(name.casefold(), name)])
            if installation_conflict(row['path']):
                raise ValueError('Имя занято файлом или папкой; установка заблокирована')
            if row['eligible']:
                source = read_script(row['new_path'])
                if digest(source) != row['source_hash']:
                    raise ValueError('Источник изменился во время проверки. Нажмите «Проверить» заново')
                pack_script(source, row['new_path'])
                row['status'] = ('Есть источники с ошибкой — выбор вручную'
                                 if row.get('source_issues') else 'установка')
        except Exception as exc:
            row.update(status=str(exc), eligible=False)
        row['status'] = 'Новый скрипт — ' + row['status']
        additions.append(row)
    return [] if cancelled.is_set() else additions + result


def write_checked(stream, data):
    stream.seek(0)
    remaining = memoryview(data)
    while remaining:
        count = stream.write(remaining)
        if not count:
            raise OSError('Не удалось записать файл полностью.')
        remaining = remaining[count:]
    stream.truncate(len(data))
    stream.flush()
    os.fsync(stream.fileno())
    stream.seek(0)
    if stream.read(len(data) + 1) != data:
        raise OSError('Проверка записанного файла не пройдена.')


def validate_raw_url(url, manifest=False):
    """Only this public repository; a manifest cannot change the trusted origin."""
    if not isinstance(url, str) or any(ord(c) < 32 for c in url):
        raise ValueError('Недопустимый адрес GitHub')
    parts = urllib.parse.urlsplit(url)
    if (parts.scheme != 'https' or parts.netloc != 'raw.githubusercontent.com'
            or parts.query or parts.fragment or not url.startswith(RAW_ROOT)):
        raise ValueError('Разрешён только Raw HTTPS этого репозитория: ' + RAW_ROOT)
    relative = url[len(RAW_ROOT):]
    if manifest:
        if relative != 'manifest.json':
            raise ValueError('Укажите постоянный адрес manifest.json')
    else:
        validate_remote_path(relative)
    return url


def validate_remote_path(path, category=None):
    if not isinstance(path, str) or len(path) > 240:
        raise ValueError('Недопустимый путь в manifest')
    parts = path.split('/')
    if (len(parts) != 2 or parts[0] not in ('scripts', 'config', 'templates')
            or any(not p or p in ('.', '..') for p in parts)
            or any(ord(c) < 32 or c in '\\:%?#' for c in path)):
        raise ValueError('Недопустимый путь в manifest: ' + path)
    if category is not None and parts[0] != category:
        raise ValueError('Неверный каталог в manifest: ' + path)
    validate_leaf(parts[1])
    return path


def validate_leaf(name):
    if (not isinstance(name, str) or not name or len(name) > 180
            or name != name.strip() or name.endswith('.')
            or name in ('.', '..') or any(ord(c) < 32 or c in '<>:"/\\|?*' for c in name)):
        raise ValueError('Недопустимое имя файла')
    base = name.split('.')[0].upper()
    if base in {'CON', 'PRN', 'AUX', 'NUL', 'CONIN$', 'CONOUT$'} or re.fullmatch(r'(COM|LPT)[1-9¹²³]', base):
        raise ValueError('Зарезервированное имя Windows: ' + name)
    return name


def check_cancelled(cancelled):
    if cancelled is not None and cancelled.is_set():
        raise RuntimeError('Проверка отменена')


class _RepositoryRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, url):
        validate_raw_url(url, manifest=url == DEFAULT_MANIFEST_URL)
        return super().redirect_request(request, fp, code, message, headers, url)


def download_bytes(url, limit, cancelled=None):
    validate_raw_url(url, manifest=url == DEFAULT_MANIFEST_URL)
    check_cancelled(cancelled)
    request = urllib.request.Request(url, headers={
        'User-Agent': 'NXOpen-Scripts-Updater/' + SCRIPT_VERSION,
        'Accept': 'application/octet-stream', 'Cache-Control': 'no-cache',
    })
    opener = urllib.request.build_opener(
        _RepositoryRedirect(), urllib.request.HTTPSHandler(context=ssl.create_default_context()))
    try:
        with opener.open(request, timeout=HTTP_TIMEOUT) as response:
            validate_raw_url(response.geturl(), manifest=url == DEFAULT_MANIFEST_URL)
            if response.status != 200:
                raise ValueError('HTTP ' + str(response.status))
            declared = response.headers.get('Content-Length')
            if declared is not None and (not declared.isdecimal() or int(declared) > limit):
                raise ValueError('Файл превышает разрешённый размер')
            chunks, size = [], 0
            while True:
                check_cancelled(cancelled)
                chunk = response.read(min(65536, limit + 1 - size))
                if not chunk:
                    break
                chunks.append(chunk)
                size += len(chunk)
                if size > limit:
                    raise ValueError('Файл превышает разрешённый размер')
            data = b''.join(chunks)
            if declared is not None and len(data) != int(declared):
                raise ValueError('Загрузка получена не полностью')
            if not data:
                raise ValueError('Получен пустой файл')
            return data
    except Exception as exc:
        raise OSError('Не удалось загрузить ' + url + '\n' + str(exc)) from exc


def _unique_json_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Повторный ключ JSON: ' + key)
        result[key] = value
    return result


def _manifest_digest(value):
    if not isinstance(value, str) or not re.fullmatch('[0-9a-f]{64}', value):
        raise ValueError('В manifest нужен SHA-256 из 64 строчных шестнадцатеричных знаков')
    return value


def _release_version(value):
    if not isinstance(value, str) or not re.fullmatch(r'V[1-9][0-9]*(?:\.[0-9]{2,})?', value):
        raise ValueError('Версия manifest должна иметь вид V1, V1.01, V1.10')
    return parse_version(value)[0]


def validate_manifest(data):
    if not isinstance(data, bytes) or len(data) > MAX_MANIFEST_SIZE:
        raise ValueError('Недопустимый размер manifest')
    manifest = json.loads(data.decode('utf-8-sig'), object_pairs_hook=_unique_json_object,
                          parse_constant=lambda value: (_ for _ in ()).throw(ValueError('Недопустимый JSON: ' + value)))
    if not isinstance(manifest, dict) or type(manifest.get('schema_version')) is not int or manifest['schema_version'] != 1:
        raise ValueError('Этот формат manifest не поддерживается. Обновите апдейтер вручную по README.')
    _release_version(manifest.get('min_updater_version'))
    scripts = manifest.get('scripts')
    if not isinstance(scripts, list) or not 1 <= len(scripts) <= 200:
        raise ValueError('В manifest нужен список от 1 до 200 скриптов')
    identities, targets = set(), set()
    for entry in scripts:
        if not isinstance(entry, dict):
            raise ValueError('Неверная запись скрипта в manifest')
        filename = validate_leaf(entry.get('file'))
        if stable_script_name(filename) != filename:
            raise ValueError('Основной файл должен иметь постоянное имя без версии: ' + filename)
        identity = filename.rsplit('.', 1)[0]
        if entry.get('id') != identity or identity.casefold() in identities:
            raise ValueError('Повторный или неверный id в manifest: ' + identity)
        identities.add(identity.casefold())
        for key in ('name', 'description'):
            value = entry.get(key)
            if not isinstance(value, str) or not value.strip() or len(value) > 500 or any(ord(c) < 32 for c in value):
                raise ValueError('В manifest нужно короткое текстовое поле ' + key)
        _release_version(entry.get('version'))
        validate_remote_path(entry.get('path'), 'scripts')
        if entry['path'] != 'scripts/' + filename:
            raise ValueError('Путь скрипта не соответствует его имени: ' + filename)
        _manifest_digest(entry.get('sha256'))
        companions = []
        config = entry.get('config')
        if config is not None:
            if not isinstance(config, dict):
                raise ValueError('Неверное описание INI')
            validate_remote_path(config.get('path'), 'config')
            validate_leaf(config.get('file'))
            if not config['file'].endswith('.ini') or not config['path'].endswith('.example.ini'):
                raise ValueError('Для INI требуется отдельный .example.ini')
            companions.append(config)
        additions = entry.get('install_files', [])
        if not isinstance(additions, list) or len(additions) > 1:
            raise ValueError('Неверный список начальных файлов')
        for extra in additions:
            if (identity != 'NX_Number_Program_Folders' or not isinstance(extra, dict)
                    or extra.get('file') != 'NX_Numbering_Register_v1.0.xlsx'
                    or extra.get('path') != 'templates/NX_Numbering_Register_v1.0.xlsx'
                    or extra.get('location') != 'numbering_data' or config is None):
                raise ValueError('Недопустимый шаблон начальных данных')
            companions.append(extra)
        for companion in companions:
            location = companion.get('location')
            if location not in ('script', 'numbering_data'):
                raise ValueError('Недопустимая папка назначения в manifest')
            if location == 'numbering_data' and identity != 'NX_Number_Program_Folders':
                raise ValueError('Папка реестра разрешена только для нумератора')
            _manifest_digest(companion.get('sha256'))
            target = (location, companion['file'].casefold())
            if target in targets:
                raise ValueError('Несколько записей пытаются установить один файл настроек')
            targets.add(target)
    return manifest


def scan_github(settings, cancelled, fetcher=None, self_path=None):
    fetcher = fetcher or download_bytes
    raw = fetcher(validate_raw_url(settings['manifest_url'], manifest=True), MAX_MANIFEST_SIZE, cancelled)
    manifest = validate_manifest(raw)
    current_updater = parse_version(SCRIPT_VERSION)[0]
    need_updater = parse_version(manifest['min_updater_version'])[0] > current_updater
    root = os.path.abspath(settings['working_folder'])
    root_id = folder_identity(root)
    self_path = os.path.abspath(self_path or __file__)
    local, result, additions = defaultdict(list), [], []
    for path in iter_scripts(root, settings['working_recursive'], cancelled, include_empty=True):
        family = script_identity(path)[0]
        if family[0] != UPDATER_ID and excluded_script(path, settings['exclude_files']):
            continue
        local[family].append(path)
    seen_families = set()
    for entry in manifest['scripts']:
        check_cancelled(cancelled)
        family = script_identity(entry['file'])[0]
        if family[0] != UPDATER_ID and excluded_script(entry['file'], settings['exclude_files']):
            continue
        seen_families.add(family)
        paths = local.get(family, [])
        path = paths[0] if paths else os.path.join(root, entry['file'])
        row = dict(path=path, new_path=RAW_ROOT + entry['path'], remote=entry,
                   operation='update' if paths else 'install', origin='github',
                   identity=(family, None, 'нет версии'), titles=[entry['name']],
                   installed_label='Не установлен', available_label=entry['version'],
                   available_version=parse_version(entry['version'])[0], source_hash=entry['sha256'],
                   status='', eligible=True, checked=False, source_issues=[], root_identity=root_id,
                   is_self=path_key(path) == path_key(self_path))
        try:
            if len(paths) > 1:
                raise ValueError('Несколько рабочих файлов одного скрипта; оставьте один рабочий путь: ' + ' | '.join(paths))
            if paths:
                before = read_script(path)
                info = internal_info(path, before)
                row.update(identity=(family, info['version'], info['label']),
                           installed_label=info['label'] + (' *' if info['dirty'] else ''),
                           target_hash=digest(before), local_dirty=info['dirty'])
                current, available = info['version'], row['available_version']
                if current is None:
                    row['status'] = 'Нет версии на ПК — выбор вручную'
                elif current > available:
                    row.update(status='На ПК более новая версия', eligible=False)
                elif current == available:
                    if digest(info['payload']) == entry['sha256']:
                        row.update(status='Установлена эта версия', eligible=False)
                    else:
                        row['status'] = 'Та же версия, другой код — вручную'
                elif info['dirty']:
                    row['status'] = 'Новая версия; местные правки — вручную'
                elif info['meta'] is None:
                    row['status'] = 'Новая версия; происхождение локального кода не подтверждено — вручную'
                else:
                    row.update(status='Новая версия', checked=not row['is_self'])
            else:
                if installation_conflict(path):
                    raise ValueError('Имя занято файлом или папкой')
                row['status'] = 'Новый скрипт — установка'
            if need_updater and family[0] != UPDATER_ID:
                row.update(status='Сначала обновите апдейтер до ' + manifest['min_updater_version'],
                           eligible=False, checked=False)
            if row['is_self'] and row['eligible']:
                row['status'] += '; апдейтер заменяется после закрытия окна'
        except Exception as exc:
            row.update(status=str(exc), eligible=False, checked=False)
        (result if paths else additions).append(row)
    for family, paths in local.items():
        if family in seen_families:
            continue
        for path in paths:
            result.append(dict(path=path, new_path='', identity=script_identity(path), operation='update',
                               titles=[ntpath.basename(path)], installed_label='—', available_label='—',
                               status='Нет в каталоге GitHub; файл сохранён', eligible=False, checked=False))
    return additions + result


def _verified_download(record, limit, fetcher):
    raw = fetcher(RAW_ROOT + record['path'], limit, None)
    if not isinstance(raw, bytes) or not raw or len(raw) > limit or digest(raw) != record['sha256']:
        raise ValueError('SHA-256 или размер не совпадает с manifest: ' + record['path'] +
                         '\nРабочие файлы не изменены. Выполните проверку заново.')
    return raw


def _companion_target(record, script_path):
    folder = os.path.dirname(script_path) if record['location'] == 'script' else NUMBERING_DATA_FOLDER
    return os.path.abspath(os.path.join(folder, record['file']))


def prepare_jobs(rows, fetcher=None):
    if not rows or any(not row.get('eligible') for row in rows):
        raise ValueError('Отметьте доступные скрипты')
    fetcher, jobs, seen, total = fetcher or download_bytes, [], set(), 0
    for row in rows:
        if row.get('operation') not in ('install', 'update'):
            raise ValueError('Неизвестное действие')
        if row.get('origin') == 'github':
            entry = row['remote']
            raw = _verified_download(entry, MAX_SCRIPT_SIZE, fetcher)
            info = internal_info(entry['file'], raw)
            declarations = version_declarations(source_text(raw, ntpath.splitext(entry['file'])[1]),
                                                ntpath.splitext(entry['file'])[1])
            if (info['label'] != entry['version'] or info['meta'] is not None
                    or not any(k == 'SCRIPT_VERSION в шапке' for k, _ in declarations)
                    or not any(k.casefold() == 'script_version' for k, _ in declarations)):
                raise ValueError('Версия/шапка исходника не соответствует manifest: ' + entry['file'])
            payload = pack_script(raw, entry['file'])
        else:
            raw = read_script(row['new_path'])
            if digest(raw) != row['source_hash']:
                raise ValueError('Источник изменился. Выполните проверку заново')
            payload = pack_script(raw, row['new_path'])
        jobs.append(dict(path=row['path'], data=payload, expected=row.get('target_hash'),
                         create=row['operation'] == 'install', is_self=row.get('is_self', False),
                         root_identity=row.get('root_identity') if row['operation'] == 'install' else None))
        if row.get('origin') == 'github' and row['operation'] == 'install':
            config = row['remote'].get('config')
            config_existed = config is not None and os.path.lexists(_companion_target(config, row['path']))
            companions = [] if config is None or config_existed else [config]
            # Never rebuild a lost registry for an existing INI. Custom RegistryFile is user data.
            if config is not None and not config_existed:
                companions += row['remote'].get('install_files', [])
            for record in companions:
                target = _companion_target(record, row['path'])
                if os.path.lexists(target):
                    continue
                raw = _verified_download(record, MAX_CONFIG_SIZE if record['file'].endswith('.ini') else MAX_SCRIPT_SIZE, fetcher)
                if record['file'].endswith('.ini'):
                    config_parser(raw)
                jobs.append(dict(path=target, data=raw, expected=None, create=True, is_self=False,
                                 allow_directory=record['location'] == 'numbering_data'))
    for job in jobs:
        key = path_key(os.path.realpath(job['path']))
        if key in seen:
            raise ValueError('Файл выбран несколько раз: ' + job['path'])
        seen.add(key)
        total += len(job['data'])
        if total > MAX_BATCH_SIZE:
            raise ValueError('Общий объём выбранных файлов превышает 128 МБ')
    return jobs


def _reject_link(path):
    if os.path.lexists(path):
        info = os.lstat(path)
        if stat.S_ISLNK(info.st_mode) or getattr(info, 'st_file_attributes', 0) & 0x400:
            raise ValueError('Ссылка или reparse point не обновляется: ' + path)
        if not stat.S_ISREG(info.st_mode):
            raise ValueError('Ожидался обычный файл: ' + path)


def open_replace_guard(path):
    """Guard preflight reads on Windows; close before replacing this file."""
    if os.name != 'nt':
        return open(path, 'rb')
    import msvcrt
    from ctypes import wintypes as W
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.CreateFileW.argtypes = [W.LPCWSTR, W.DWORD, W.DWORD, ctypes.c_void_p, W.DWORD, W.DWORD, W.HANDLE]
    kernel.CreateFileW.restype = W.HANDLE
    kernel.CloseHandle.argtypes = [W.HANDLE]
    # FILE_SHARE_READ only: other readers are allowed during preflight,
    # but writers and renames must wait until this guard is closed.
    handle = kernel.CreateFileW(path, 0x80000000, 1, None, 3, 0x80, None)
    if handle == ctypes.c_void_p(-1).value:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        descriptor = msvcrt.open_osfhandle(handle, os.O_RDONLY | os.O_BINARY)
    except Exception:
        kernel.CloseHandle(handle)
        raise
    return os.fdopen(descriptor, 'rb')


def _stage_bytes(path, data):
    stage = os.path.join(os.path.dirname(path), '.nxupdater-' + os.path.basename(path) + '.' + str(os.getpid()) + '.' + uuid.uuid4().hex + '.pending')
    try:
        with open(stage, 'x+b', buffering=0) as stream:
            write_checked(stream, data)
        if os.path.isfile(path):
            os.chmod(stage, stat.S_IMODE(os.stat(path).st_mode))
        return stage
    except Exception:
        if os.path.isfile(stage):
            os.unlink(stage)
        raise


def _install_staged(stage, target):
    if os.name == 'nt':
        os.rename(stage, target)  # Windows rename fails if target already exists.
    else:
        os.link(stage, target)  # Exclusive creation; stage cleanup belongs to the caller.


def commit_jobs(jobs, replace=None):
    """Preflight the entire selection; use same-directory stages and RAM rollback."""
    replace = replace or os.replace
    staged, committed, created_dirs = [], [], []
    guards = {}
    try:
        with ExitStack() as locks:
            for job in jobs:
                path = os.path.abspath(job['path'])
                if path != job['path']:
                    raise ValueError('Нужен полный путь рабочего файла')
                parent = os.path.dirname(path)
                if not os.path.isdir(parent):
                    if not job.get('allow_directory') or path_key(parent) != path_key(os.path.abspath(NUMBERING_DATA_FOLDER)):
                        raise ValueError('Папка назначения отсутствует: ' + parent)
                    os.makedirs(parent, exist_ok=True)
                    created_dirs.append(parent)
                if job.get('root_identity') and folder_identity(parent) != job['root_identity']:
                    raise ValueError('Рабочая папка изменилась после проверки')
                job['parent_identity'] = folder_identity(parent)
                _reject_link(path)
                if job['create']:
                    if installation_conflict(path):
                        raise FileExistsError('Файл уже существует и не будет заменён: ' + path)
                    job['before'] = None
                else:
                    stream = locks.enter_context(open_replace_guard(path))
                    guards[path] = stream
                    before = stream.read(MAX_SCRIPT_SIZE + 4097)
                    if digest(before) != job['expected']:
                        raise ValueError('Рабочий файл изменился после проверки: ' + path)
                    job['before'] = before
            for job in jobs:
                job['stage'] = _stage_bytes(job['path'], job['data'])
                staged.append(job['stage'])
            try:
                for job in jobs:
                    path = job['path']
                    if folder_identity(os.path.dirname(path)) != job['parent_identity']:
                        raise ValueError('Папка назначения изменилась')
                    _reject_link(path)
                    if job['create']:
                        if installation_conflict(path):
                            raise FileExistsError('Имя уже занято: ' + path)
                        _install_staged(job['stage'], path)
                    else:
                        with open(path, 'rb') as source:
                            if digest(source.read(MAX_SCRIPT_SIZE + 4097)) != job['expected']:
                                raise ValueError('Рабочий файл изменился перед заменой: ' + path)
                        # Windows cannot replace the destination while our own
                        # guard is open. Release only this file immediately
                        # before the atomic rename; other jobs remain guarded.
                        guards.pop(path).close()
                        replace(job['stage'], path)
                    committed.append(job)
                    with open(path, 'rb') as check:
                        if check.read(len(job['data']) + 1) != job['data']:
                            raise OSError('Проверка установленного файла не пройдена: ' + path)
            except Exception as exc:
                errors = []
                for job in reversed(committed):
                    try:
                        _reject_link(job['path'])
                        with open(job['path'], 'rb') as current:
                            if digest(current.read(MAX_SCRIPT_SIZE + 4097)) != digest(job['data']):
                                raise ValueError('Файл изменён другим процессом; автоматический откат запрещён')
                        if job['before'] is None:
                            os.unlink(job['path'])
                        else:
                            rollback = _stage_bytes(job['path'], job['before'])
                            staged.append(rollback)
                            os.replace(rollback, job['path'])
                    except Exception as restore_error:
                        errors.append(job['path'] + ': ' + str(restore_error))
                if errors:
                    raise RuntimeError(str(exc) + '\nНе удалось завершить откат:\n' + '\n'.join(errors)) from exc
                raise RuntimeError('Запись отменена; прежние файлы восстановлены.\n' + str(exc)) from exc
    finally:
        for path in staged:
            if os.path.isfile(path):
                os.unlink(path)
        for directory in reversed(created_dirs):
            try:
                os.rmdir(directory)
            except OSError:
                pass
    return len(jobs)


def apply_updates(rows, fetcher=None, replace=None, defer_self=False):
    jobs = prepare_jobs(rows, fetcher)
    pending = [job for job in jobs if job['is_self']]
    if len(pending) > 1:
        raise ValueError('Самообновление выбрано несколько раз')
    if pending and not defer_self:
        raise ValueError('Самообновление выполняется только после закрытия окна')
    regular = [job for job in jobs if not job['is_self']]
    commit_jobs(regular, replace)
    return pending[0] if pending else None


def configured_source(settings):
    return settings['manifest_url'] if settings.get('source_type', 'github') == 'github' else settings['update_folder']


@contextmanager
def single_instance():
    if os.name != 'nt':
        yield
        return
    from ctypes import wintypes as W
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.CreateMutexW.argtypes = [ctypes.c_void_p, W.BOOL, W.LPCWSTR]
    kernel.CreateMutexW.restype = W.HANDLE
    kernel.WaitForSingleObject.argtypes = [W.HANDLE, W.DWORD]
    kernel.WaitForSingleObject.restype = W.DWORD
    kernel.ReleaseMutex.argtypes = [W.HANDLE]
    kernel.CloseHandle.argtypes = [W.HANDLE]
    handle = kernel.CreateMutexW(None, False, 'Local\\NXOpenScriptsUpdater_V1')
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    owned = False
    try:
        result = kernel.WaitForSingleObject(handle, 0)
        if result not in (0, 0x80):
            raise RuntimeError('Апдейтер уже открыт в этом сеансе Windows. Закройте другое окно.')
        owned = True
        yield
    finally:
        if owned:
            kernel.ReleaseMutex(handle)
        kernel.CloseHandle(handle)


def _process_running(pid):
    if pid <= 0 or pid == os.getpid():
        return True
    if os.name != 'nt':
        try:
            os.kill(pid, 0)
            return True
        except ProcessLookupError:
            return False
        except PermissionError:
            return True
    from ctypes import wintypes as W
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = [W.DWORD, W.BOOL, W.DWORD]
    kernel.OpenProcess.restype = W.HANDLE
    kernel.WaitForSingleObject.argtypes = [W.HANDLE, W.DWORD]
    kernel.WaitForSingleObject.restype = W.DWORD
    kernel.CloseHandle.argtypes = [W.HANDLE]
    handle = kernel.OpenProcess(0x100000, False, pid)
    if not handle:
        return ctypes.get_last_error() != 87  # Access denied is not proof of exit.
    try:
        return kernel.WaitForSingleObject(handle, 0) != 0
    finally:
        kernel.CloseHandle(handle)


def cleanup_stages(settings):
    # Delete only our strict filenames from processes that no longer exist.
    folders = {os.path.abspath(settings['working_folder']), os.path.abspath(NUMBERING_DATA_FOLDER),
               os.path.dirname(os.path.abspath(settings['config_path']))}
    if settings['working_recursive'] and os.path.isdir(settings['working_folder']):
        folders.update(os.path.dirname(path) for path in iter_scripts(
            settings['working_folder'], True, threading.Event(), include_empty=True))
    pattern = re.compile(r'^\.nxupdater-(.+)\.([1-9][0-9]*)\.([0-9a-f]{32})\.pending$')
    for folder in folders:
        if not os.path.isdir(folder):
            continue
        with os.scandir(folder) as entries:
            for entry in entries:
                match = pattern.fullmatch(entry.name)
                if (not match or entry.is_symlink() or not entry.is_file(follow_symlinks=False)
                        or ntpath.splitext(match[1])[1].lower() not in SCRIPT_EXTENSIONS | {'.ini', '.xlsx'}):
                    continue
                validate_leaf(match[1])
                if not _process_running(int(match[2])):
                    os.unlink(entry.path)


def run_window():
    from ctypes import wintypes as W

    if os.name != "nt":
        raise RuntimeError("Этот журнал предназначен для NX на Windows.")
    settings = load_settings(__file__)
    U = ctypes.WinDLL("user32", use_last_error=True)
    K = ctypes.WinDLL("kernel32", use_last_error=True)
    G = ctypes.WinDLL("gdi32", use_last_error=True)
    C = ctypes.WinDLL("comctl32", use_last_error=True)
    S = ctypes.WinDLL("shell32", use_last_error=True)
    O = ctypes.WinDLL("ole32", use_last_error=True)
    LPARAM, WPARAM, LRESULT = ctypes.c_ssize_t, ctypes.c_size_t, ctypes.c_ssize_t
    WNDPROC = ctypes.WINFUNCTYPE(LRESULT, W.HWND, W.UINT, WPARAM, LPARAM)
    BFFCALLBACK = ctypes.WINFUNCTYPE(ctypes.c_int, W.HWND, W.UINT, LPARAM, LPARAM)

    class WNDCLASS(ctypes.Structure):
        _fields_ = [("style", W.UINT), ("proc", WNDPROC), ("cbClsExtra", ctypes.c_int), ("cbWndExtra", ctypes.c_int),
                    ("instance", W.HINSTANCE), ("icon", W.HICON), ("cursor", W.HANDLE), ("background", W.HBRUSH),
                    ("menu", W.LPCWSTR), ("name", W.LPCWSTR)]

    class NMHDR(ctypes.Structure):
        _fields_ = [("hwndFrom", W.HWND), ("idFrom", WPARAM), ("code", ctypes.c_int)]

    class NMLISTVIEW(ctypes.Structure):
        _fields_ = [("hdr", NMHDR), ("iItem", ctypes.c_int), ("iSubItem", ctypes.c_int),
                    ("uNewState", W.UINT), ("uOldState", W.UINT), ("uChanged", W.UINT), ("ptAction", W.POINT), ("lParam", LPARAM)]

    class LVITEM(ctypes.Structure):
        _fields_ = [("mask", W.UINT), ("iItem", ctypes.c_int), ("iSubItem", ctypes.c_int), ("state", W.UINT),
                    ("stateMask", W.UINT), ("pszText", W.LPWSTR), ("cchTextMax", ctypes.c_int), ("iImage", ctypes.c_int),
                    ("lParam", LPARAM), ("iIndent", ctypes.c_int), ("iGroupId", ctypes.c_int), ("cColumns", W.UINT),
                    ("puColumns", ctypes.c_void_p), ("piColFmt", ctypes.c_void_p), ("iGroup", ctypes.c_int)]

    class LVCOLUMN(ctypes.Structure):
        _fields_ = [("mask", W.UINT), ("fmt", ctypes.c_int), ("cx", ctypes.c_int), ("pszText", W.LPWSTR),
                    ("cchTextMax", ctypes.c_int), ("iSubItem", ctypes.c_int), ("iImage", ctypes.c_int), ("iOrder", ctypes.c_int),
                    ("cxMin", ctypes.c_int), ("cxDefault", ctypes.c_int), ("cxIdeal", ctypes.c_int)]

    class BROWSEINFO(ctypes.Structure):
        _fields_ = [("hwndOwner", W.HWND), ("pidlRoot", ctypes.c_void_p),
                    ("pszDisplayName", W.LPWSTR), ("lpszTitle", W.LPCWSTR), ("ulFlags", W.UINT),
                    ("lpfn", BFFCALLBACK), ("lParam", LPARAM), ("iImage", ctypes.c_int)]

    class INITCOMMON(ctypes.Structure):
        _fields_ = [("dwSize", W.DWORD), ("dwICC", W.DWORD)]

    def signature(dll, name, result, *args):
        function = getattr(dll, name)
        function.restype, function.argtypes = result, list(args)
        return function

    signature(K, "GetModuleHandleW", W.HMODULE, W.LPCWSTR)
    signature(K, "GetModuleFileNameW", W.DWORD, W.HMODULE, W.LPWSTR, W.DWORD)
    signature(U, "CreateWindowExW", W.HWND, W.DWORD, W.LPCWSTR, W.LPCWSTR, W.DWORD,
              ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, W.HWND, W.HMENU, W.HINSTANCE, ctypes.c_void_p)
    signature(U, "DefWindowProcW", LRESULT, W.HWND, W.UINT, WPARAM, LPARAM)
    signature(U, "SendMessageW", LRESULT, W.HWND, W.UINT, WPARAM, LPARAM)
    signature(U, "PostMessageW", W.BOOL, W.HWND, W.UINT, WPARAM, LPARAM)
    signature(U, "RegisterClassW", W.ATOM, ctypes.POINTER(WNDCLASS))
    signature(U, "UnregisterClassW", W.BOOL, W.LPCWSTR, W.HINSTANCE)
    signature(U, "LoadCursorW", W.HANDLE, W.HINSTANCE, ctypes.c_void_p)
    signature(U, "GetMessageW", ctypes.c_int, ctypes.POINTER(W.MSG), W.HWND, W.UINT, W.UINT)
    signature(U, "TranslateMessage", W.BOOL, ctypes.POINTER(W.MSG))
    signature(U, "DispatchMessageW", LRESULT, ctypes.POINTER(W.MSG))
    signature(U, "IsDialogMessageW", W.BOOL, W.HWND, ctypes.POINTER(W.MSG))
    signature(U, "GetForegroundWindow", W.HWND)
    signature(U, "EnableWindow", W.BOOL, W.HWND, W.BOOL)
    signature(U, "DestroyWindow", W.BOOL, W.HWND)
    signature(U, "ShowWindow", W.BOOL, W.HWND, ctypes.c_int)
    signature(U, "SetForegroundWindow", W.BOOL, W.HWND)
    signature(U, "GetWindowTextLengthW", ctypes.c_int, W.HWND)
    signature(U, "GetWindowTextW", ctypes.c_int, W.HWND, W.LPWSTR, ctypes.c_int)
    signature(U, "SetWindowTextW", W.BOOL, W.HWND, W.LPCWSTR)
    signature(U, "MessageBoxW", ctypes.c_int, W.HWND, W.LPCWSTR, W.LPCWSTR, W.UINT)
    signature(U, "BeginDeferWindowPos", W.HANDLE, ctypes.c_int)
    signature(U, "DeferWindowPos", W.HANDLE, W.HANDLE, W.HWND, W.HWND,
              ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, W.UINT)
    signature(U, "EndDeferWindowPos", W.BOOL, W.HANDLE)
    signature(U, "SetWindowPos", W.BOOL, W.HWND, W.HWND,
              ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, W.UINT)
    signature(U, "RedrawWindow", W.BOOL, W.HWND, ctypes.POINTER(W.RECT), W.HANDLE, W.UINT)
    signature(U, "GetClientRect", W.BOOL, W.HWND, ctypes.POINTER(W.RECT))
    signature(U, "GetWindowThreadProcessId", W.DWORD, W.HWND, ctypes.POINTER(W.DWORD))
    if hasattr(U, "GetDpiForWindow"):
        signature(U, "GetDpiForWindow", W.UINT, W.HWND)
    if hasattr(U, "GetDpiForSystem"):
        signature(U, "GetDpiForSystem", W.UINT)
    signature(G, "GetStockObject", W.HANDLE, ctypes.c_int)
    signature(C, "InitCommonControlsEx", W.BOOL, ctypes.POINTER(INITCOMMON))
    signature(S, "SHBrowseForFolderW", ctypes.c_void_p, ctypes.POINTER(BROWSEINFO))
    signature(S, "SHGetPathFromIDListEx", W.BOOL, ctypes.c_void_p, W.LPWSTR, W.DWORD, W.UINT)
    signature(O, "CoInitializeEx", ctypes.c_long, ctypes.c_void_p, W.DWORD)
    signature(O, "CoUninitialize", None)
    signature(O, "CoTaskMemFree", None, ctypes.c_void_p)
    C.InitCommonControlsEx(ctypes.byref(INITCOMMON(ctypes.sizeof(INITCOMMON), 1)))
    instance = K.GetModuleHandleW(None)
    font = G.GetStockObject(17)
    title = window_title()
    owner = U.GetForegroundWindow()
    owner_pid = W.DWORD()
    U.GetWindowThreadProcessId(owner, ctypes.byref(owner_pid))
    if owner_pid.value != os.getpid():
        owner = None

    try:
        dpi = U.GetDpiForWindow(owner) if owner else U.GetDpiForSystem()
        scale = max(1.0, dpi / 96.0)
    except AttributeError:
        scale = 1.0
    unit = lambda n: int(n * scale)
    result_queue = queue.Queue()
    cancelled = threading.Event()
    state = {"rows": [], "busy": False, "closed": False, "applying": False, "populating": False,
             "syncing_checks": False, "laying_out": False,
             "inputs": None, "settings": settings, "pending_self": None, "threads": []}
    controls = {}
    hwnd_holder = {"value": None}

    def message(text, error=False):
        U.MessageBoxW(hwnd_holder["value"], str(text), window_title('Ошибка') if error else title, 0x10 if error else 0x40)

    def get_text(handle):
        buffer = ctypes.create_unicode_buffer(U.GetWindowTextLengthW(handle) + 1)
        U.GetWindowTextW(handle, buffer, len(buffer))
        return buffer.value.strip().strip('"')

    def checked(index):
        return bool(state['rows'][index].get('selected'))

    def paint_check(index):
        row = state['rows'][index]
        previous = state['syncing_checks']
        state['syncing_checks'] = True
        try:
            item = LVITEM(mask=8, state=0x2000 if checked(index) else 0x1000, stateMask=0xF000)
            U.SendMessageW(controls['list'], 0x1000 + 43, index, ctypes.addressof(item))
            # A textual indicator remains usable even if NX's checkbox rendering fails.
            buffer = ctypes.create_unicode_buffer(displayed_status(row))
            item = LVITEM(mask=1, iItem=index, iSubItem=4, pszText=ctypes.cast(buffer, W.LPWSTR))
            U.SendMessageW(controls['list'], 0x1000 + 116, index, ctypes.addressof(item))
        finally:
            state['syncing_checks'] = previous

    def set_checked(index, value):
        select_row(state['rows'][index], value)
        paint_check(index)

    def highlighted_row():
        return int(U.SendMessageW(controls['list'], 0x1000 + 12, -1, 2))  # LVNI_SELECTED

    def show_row_details(index):
        row = state['rows'][index]
        target_label = '\r\nУстановка в: ' if row['operation'] == 'install' else '\r\nРабочий файл: '
        detail = ('Состояние: ' + displayed_status(row) + target_label + row['path'] +
                  '\r\nИсточник нового кода: ' + (row['new_path'] or 'не найден'))
        if row.get('remote'):
            detail += '\r\n' + row['remote']['description']
        if row.get('alternatives'):
            detail += '\r\nСовпадения: ' + ' | '.join(row['alternatives'])
        if row.get('source_issues'):
            detail += '\r\nПропущенные источники:\r\n' + '\r\n'.join(row['source_issues'])
        if not row['eligible']:
            detail += '\r\nВыбор недоступен. Причина указана в состоянии и подробностях выше.'
        U.SetWindowTextW(controls['details'], detail)

    def toggle_row():
        if state['busy']:
            return
        index = highlighted_row()
        if not 0 <= index < len(state['rows']):
            message('Сначала выделите нужную строку в списке.')
            return
        row = state['rows'][index]
        if not row['eligible']:
            show_row_details(index)
            reason = 'Этот файл нельзя отметить.\n\n' + row['status']
            if row.get('source_issues'):
                reason += '\n\n' + '\n'.join(row['source_issues'])
            if row.get('alternatives'):
                reason += '\n\n' + '\n'.join(row['alternatives'])
            message(reason, True)
            return
        set_checked(index, not checked(index))
        selection_status()
        show_row_details(index)

    def handle_list_key(msg):
        # Consume Space before native/dialog processing; otherwise it can toggle twice.
        if msg.hWnd != controls['list'] or msg.wParam != 0x20 or msg.message not in (0x100, 0x101, 0x102):
            return False
        if msg.message == 0x100 and not msg.lParam & (1 << 30):
            toggle_row()
        return True

    def inputs():
        return (path_key(get_text(controls['working'])), get_text(controls['folder']),
                U.SendMessageW(controls['working_recursive'], 0xF0, 0, 0) == 1,
                U.SendMessageW(controls['recursive'], 0xF0, 0, 0) == 1,
                state['settings']['exclude_files'])

    def settings_from_controls():
        cfg = dict(state['settings'])
        cfg['working_folder'] = resolve_setting_path(get_text(controls['working']), os.path.dirname(cfg['config_path']))
        source = get_text(controls['folder'])
        if source.startswith('https://'):
            cfg['manifest_url'] = validate_raw_url(source, manifest=True)
            cfg['source_type'] = 'github'
        else:
            cfg['update_folder'] = resolve_setting_path(source, os.path.dirname(cfg['config_path']))
            cfg['source_type'] = 'folder'
        cfg['working_recursive'] = U.SendMessageW(controls['working_recursive'], 0xF0, 0, 0) == 1
        cfg['update_recursive'] = U.SendMessageW(controls['recursive'], 0xF0, 0, 0) == 1
        return cfg

    def busy(value):
        state['busy'] = value
        for key in ('scan', 'working', 'browse_working', 'browse_update', 'reload', 'folder',
                    'working_recursive', 'recursive', 'select', 'select_installs', 'clear', 'apply'):
            U.EnableWindow(controls[key], not value)

    def selection_status():
        if state['busy'] or state['populating'] or not state['rows']:
            return
        rows = state['rows']
        selected = [row for i, row in enumerate(rows) if checked(i)]
        U.SetWindowTextW(controls['status'],
                         'Новых скриптов: {}. Доступно обновлений: {}. Выбрано: установить {}, обновить {}.'.format(
                             sum(row['operation'] == 'install' for row in rows),
                             sum(row['operation'] == 'update' and row['eligible'] for row in rows),
                             sum(row['operation'] == 'install' for row in selected),
                             sum(row['operation'] == 'update' for row in selected)))

    def start_scan(persist=True):
        if state['busy']:
            return
        state['inputs'] = None
        cfg = settings_from_controls()
        if persist:
            cfg = save_settings(cfg)
        state.update(settings=cfg, rows=[], inputs=None)
        U.SetWindowTextW(controls['working'], cfg['working_folder'])
        U.SetWindowTextW(controls['folder'], configured_source(cfg))
        snapshot = inputs()
        U.SendMessageW(controls['list'], 0x1000 + 9, 0, 0)
        U.SetWindowTextW(controls['details'], 'INI: ' + cfg['config_path'] +
                         ('\r\nINI ещё не создан. Нажмите «Проверить», чтобы сохранить настройки.'
                          if cfg['_config_bytes'] is None else ''))
        U.SetWindowTextW(controls['status'], 'Поиск рабочих скриптов и чтение версий... Окно можно закрыть для отмены.')
        busy(True)

        def work():
            try:
                found = scan_settings(cfg, cancelled)
                result_queue.put((found, None, snapshot))
            except Exception as exc:
                result_queue.put((None, str(exc), None))
            if not state['closed']:
                U.PostMessageW(hwnd_holder['value'], 0x8001, 0, 0)

        thread = threading.Thread(target=work, daemon=True)
        state['threads'].append(thread)
        thread.start()

    def render_results():
        found, error, snapshot = result_queue.get_nowait()
        busy(False)
        if error:
            U.SetWindowTextW(controls["status"], "Проверка не завершена.")
            message(error, True)
            return
        state.update(rows=found, inputs=snapshot)
        if not found:
            U.SetWindowTextW(controls['status'], 'Скрипты .py, .cs и .vb не найдены. Проверьте обе папки, поиск в подпапках и исключения INI.')
            U.SetWindowTextW(controls['details'], 'INI: ' + state['settings']['config_path'])
            return
        state['populating'] = True
        try:
            for i, row in enumerate(found):
                values = [", ".join(row["titles"]), ntpath.basename(row["path"]),
                          row.get("installed_label", "—"), row.get("available_label", "—"), row["status"]]
                for j, value in enumerate(values):
                    buffer = ctypes.create_unicode_buffer(value)
                    item = LVITEM(mask=1, iItem=i, iSubItem=j, pszText=ctypes.cast(buffer, W.LPWSTR))
                    U.SendMessageW(controls["list"], 0x1000 + (77 if j == 0 else 116), 0 if j == 0 else i, ctypes.addressof(item))
                set_checked(i, row["checked"])
        finally:
            state['populating'] = False
        selection_status()
        U.SetWindowTextW(controls['details'], 'Выберите строку для просмотра полного пути и причины состояния.\r\n'
                         'Новые скрипты устанавливаются в: ' + state['settings']['working_folder'])

    def browse_folder(control, purpose):
        if state['busy']:
            return
        # NX may already have initialized COM. Never change its apartment or uninitialize it.
        hr = O.CoInitializeEx(None, 2)  # COINIT_APARTMENTTHREADED
        owns_com = hr >= 0
        if not owns_com and (hr & 0xFFFFFFFF) != 0x80010106:  # RPC_E_CHANGED_MODE
            raise OSError('Не удалось открыть выбор папки. HRESULT 0x{:08X}'.format(hr & 0xFFFFFFFF))
        initial = ctypes.create_unicode_buffer(get_text(controls[control]))
        display_name = ctypes.create_unicode_buffer(260)
        folder_path = ctypes.create_unicode_buffer(32768)
        callback_errors = []
        pidl = None

        @BFFCALLBACK
        def folder_callback(dialog_hwnd, msg, lparam, data):
            try:
                if msg == 1:  # BFFM_INITIALIZED: lpszTitle is body text, set the actual title too.
                    U.SetWindowTextW(dialog_hwnd, window_title(purpose))
                    if initial.value:
                        U.SendMessageW(dialog_hwnd, 0x467, 1, ctypes.addressof(initial))  # BFFM_SETSELECTIONW
                elif msg == 2:  # BFFM_SELCHANGED; a network server alone is not a directory.
                    valid = bool(S.SHGetPathFromIDListEx(lparam, folder_path, len(folder_path), 0))
                    U.SendMessageW(dialog_hwnd, 0x465, 0, int(valid))  # BFFM_ENABLEOK
            except Exception as exc:
                callback_errors.append(str(exc))
            return 0

        try:
            flags = 0x1 | 0x10 | 0x200  # filesystem folders, edit box, no create-folder button
            if owns_com:
                flags |= 0x40  # new dialog style requires an STA apartment
            dialog = BROWSEINFO(hwndOwner=hwnd_holder['value'],
                                pszDisplayName=ctypes.cast(display_name, W.LPWSTR),
                                lpszTitle=purpose, ulFlags=flags, lpfn=folder_callback)
            pidl = S.SHBrowseForFolderW(ctypes.byref(dialog))
            if callback_errors:
                raise RuntimeError(callback_errors[0])
            if not pidl:
                return
            if not S.SHGetPathFromIDListEx(pidl, folder_path, len(folder_path), 0):
                raise ValueError('Выберите папку на диске или сетевую папку, а не имя компьютера.')
            U.SetWindowTextW(controls[control], folder_path.value)
            state['inputs'] = None
            U.SetWindowTextW(controls['status'], 'Папка выбрана. Нажмите «Проверить», чтобы сохранить пути и обновить список.')
        finally:
            if pidl:
                O.CoTaskMemFree(pidl)
            if owns_com:
                O.CoUninitialize()

    def reload_settings():
        cfg = load_settings(__file__)
        state.update(settings=cfg, inputs=None)
        U.SetWindowTextW(controls['working'], cfg['working_folder'])
        U.SetWindowTextW(controls['folder'], configured_source(cfg))
        U.SendMessageW(controls['working_recursive'], 0xF1, int(cfg['working_recursive']), 0)
        U.SendMessageW(controls['recursive'], 0xF1, int(cfg['update_recursive']), 0)
        start_scan(persist=False)

    def apply():
        if state['busy']:
            return
        if state["inputs"] != inputs():
            message("Параметры изменились. Сначала нажмите «Проверить».", True)
            return
        selected = [row for i, row in enumerate(state["rows"]) if checked(i)]
        if not selected:
            message("Отметьте хотя бы один доступный скрипт.", True)
            return
        installs = sum(row['operation'] == 'install' for row in selected)
        updates = len(selected) - installs
        unknown_versions = sum(row['operation'] == 'update' and row['identity'][1] is None for row in selected)
        question = ('Установить новых скриптов: {}.\nОбновить существующих: {}.\n\nРабочая папка:\n{}\n\n'
                    'Новые файлы получат имена без версии. Кнопки и команды NX не создаются.\n'
                    'Существующие INI и реестры сохраняются. При новой установке создаются отсутствующие настройки.\n'
                    'Новый нумератор с отсутствующим INI может получить пустой реестр; это только для первого запуска.\n'
                    'Самообновление закроет окно; новая редакция заработает при следующем запуске.\n\nПродолжить?').format(
                        installs, updates, state['settings']['working_folder'])
        if unknown_versions:
            question = ('Среди выбранных: {} рабочих файлов без внутренней версии.\n'
                        'Их код будет заменён выбранными версиями источника.\n\n').format(unknown_versions) + question
        if U.MessageBoxW(hwnd_holder['value'], question, window_title('Подтверждение'), 0x124) != 6:
            return  # MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2
        state["applying"] = True
        busy(True)
        U.EnableWindow(controls["close"], False)
        U.SetWindowTextW(controls["status"], "Установка и обновление выбранных скриптов... Дождитесь завершения.")

        def work():
            try:
                pending = apply_updates(selected, defer_self=True)
                result_queue.put((installs, updates, None, pending))
            except Exception as exc:
                result_queue.put((0, 0, str(exc), None))
            U.PostMessageW(hwnd_holder["value"], 0x8002, 0, 0)

        thread = threading.Thread(target=work, daemon=True)
        state['threads'].append(thread)
        thread.start()

    def render_update():
        installs, updates, error, pending = result_queue.get_nowait()
        state["applying"] = False
        busy(False)
        U.EnableWindow(controls["close"], True)
        if error:
            U.SetWindowTextW(controls["status"], "Запись не завершена. Выполните проверку заново.")
            state["inputs"] = None
            message(error, True)
        else:
            if pending is not None:
                state['pending_self'] = pending
                message('Выбранные файлы подготовлены. Окно закроется для замены апдейтера.\n'
                        'После закрытия запустите апдейтер снова и проверьте версию в заголовке.')
                U.DestroyWindow(hwnd_holder['value'])
            else:
                message(('Установлено новых скриптов: {}.\nОбновлено существующих: {}.\n\n'
                         'Новые скрипты запускаются через Alt+F8; кнопки настраиваются вручную.\n'
                         'Перед первым использованием проверьте INI.').format(installs, updates))
                start_scan(persist=False)

    def redraw_window():
        hwnd = hwnd_holder['value']
        if hwnd and not state['closed']:
            # Invalidate old child positions, erase their borders, and repaint
            # every child (including nonclient borders) before returning.
            U.RedrawWindow(hwnd, None, None, 0x1 | 0x4 | 0x80 | 0x100 | 0x400)
            # RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW | RDW_FRAME

    def layout():
        # WM_SIZE can arrive while controls are being created or while minimized.
        if 'close' not in controls or state['laying_out'] or state['closed']:
            return
        rect = W.RECT()
        U.GetClientRect(hwnd_holder["value"], ctypes.byref(rect))
        width, height = rect.right, rect.bottom
        if width <= 0 or height <= 0:
            return
        positions = (
            ("working", unit(150), unit(14), width-unit(272), unit(26)),
            ("browse_working", width-unit(112), unit(13), unit(96), unit(28)),
            ("folder", unit(150), unit(50), width-unit(272), unit(26)),
            ("browse_update", width-unit(112), unit(49), unit(96), unit(28)),
            ("hint", unit(16), unit(122), width-unit(32), unit(24)),
            ("scan", width-unit(166), unit(84), unit(150), unit(30)),
            ("list", unit(16), unit(152), width-unit(32), max(unit(80), height-unit(317))),
            ("details", unit(16), height-unit(155), width-unit(32), unit(72)),
            ("status", unit(16), height-unit(78), width-unit(32), unit(22)),
            ("select", unit(16), height-unit(43), unit(180), unit(30)),
            ("select_installs", unit(206), height-unit(43), unit(205), unit(30)),
            ("clear", unit(421), height-unit(43), unit(105), unit(30)),
            ("apply", width-unit(350), height-unit(43), unit(225), unit(30)),
            ("close", width-unit(112), height-unit(43), unit(96), unit(30)))
        state['laying_out'] = True
        try:
            # Position all controls first. Copying old pixels or repainting each
            # control separately can leave trails during rapid resizing.
            flags = 0x4 | 0x8 | 0x10 | 0x100
            # SWP_NOZORDER | SWP_NOREDRAW | SWP_NOACTIVATE | SWP_NOCOPYBITS
            batch = U.BeginDeferWindowPos(len(positions))
            if batch:
                for key, x, y, w, h in positions:
                    batch = U.DeferWindowPos(batch, controls[key], None, x, y, w, h, flags)
                    if not batch:
                        break  # A failed DeferWindowPos invalidates the batch.
            if not batch or not U.EndDeferWindowPos(batch):
                # Keep the window usable if Windows cannot allocate a batch.
                for key, x, y, w, h in positions:
                    if not U.SetWindowPos(controls[key], None, x, y, w, h, flags):
                        raise ctypes.WinError(ctypes.get_last_error())
            col_widths = [unit(145), max(unit(260), width-unit(725)), unit(140), unit(115), unit(285)]
            for i, w in enumerate(col_widths):
                U.SendMessageW(controls["list"], 0x1000 + 30, i, max(unit(90), w))
        finally:
            try:
                redraw_window()
            finally:
                state['laying_out'] = False

    @WNDPROC
    def window_proc(hwnd, msg, wparam, lparam):
        try:
            if msg == 0x111:  # WM_COMMAND
                command = wparam & 0xFFFF
                if command == 101: start_scan()
                elif command == 102: browse_folder('working', 'Папка рабочих скриптов')
                elif command == 109: browse_folder('folder', 'Папка новых версий')
                elif command in (103, 104, 111) and not state['busy']:
                    state['populating'] = True
                    try:
                        for i, row in enumerate(state['rows']):
                            if command == 104:
                                set_checked(i, False)
                            elif command == 103 and select_in_batch(row, 'updates'):
                                set_checked(i, True)
                            elif command == 111 and select_in_batch(row, 'installs'):
                                set_checked(i, True)
                    finally:
                        state['populating'] = False
                    selection_status()
                elif command == 105: apply()
                elif command == 106 and not state["applying"]: U.DestroyWindow(hwnd)
                elif command == 108: reload_settings()
                return 0
            if msg == 0x4E and lparam and "list" in controls:
                header = ctypes.cast(lparam, ctypes.POINTER(NMHDR)).contents
                if header.hwndFrom != controls["list"] or header.code not in (-100, -101):
                    return U.DefWindowProcW(hwnd, msg, wparam, lparam)
                note = ctypes.cast(lparam, ctypes.POINTER(NMLISTVIEW)).contents
                if 0 <= note.iItem < len(state["rows"]):
                    row = state["rows"][note.iItem]
                    checkbox_changed = bool(note.uChanged & 8 and (note.uNewState ^ note.uOldState) & 0xF000)
                    if note.hdr.code == -100 and checkbox_changed and not state['syncing_checks']:
                        if state['busy']:
                            return 1
                        if (note.uNewState & 0xF000) == 0x2000 and not row['eligible']:
                            show_row_details(note.iItem)
                            U.SetWindowTextW(controls['status'], 'Нельзя отметить: ' + row['status'])
                            return 1
                    if note.hdr.code == -101 and not state['syncing_checks']:
                        if checkbox_changed:
                            # Accept only validated rows, including local files without a version.
                            select_row(row, row['eligible'] and (note.uNewState & 0xF000) == 0x2000)
                            paint_check(note.iItem)
                        if note.uNewState & 2 and not state['populating']:
                            show_row_details(note.iItem)
                        if note.uChanged & 8:
                            selection_status()
                return 0
            if msg == 0x8003:
                start_scan(persist=False)
                return 0
            if msg == 0x8001:
                render_results()
                return 0
            if msg == 0x8002:
                render_update()
                return 0
            if msg == 5:  # WM_SIZE
                if wparam != 1:  # SIZE_MINIMIZED has no useful client area.
                    layout()
                return 0
            if msg == 0x232:  # WM_EXITSIZEMOVE: finish with a clean complete frame.
                redraw_window()
                return 0
            if msg == 0x24:  # WM_GETMINMAXINFO
                values = ctypes.cast(lparam, ctypes.POINTER(W.POINT))
                values[3].x, values[3].y = unit(980), unit(520)
                return 0
            if msg == 0x10:
                if state["applying"]:
                    message("Дождитесь окончания записи выбранных скриптов.")
                    return 0
                U.DestroyWindow(hwnd)
                return 0
            if msg == 2:
                state["closed"] = True
                cancelled.set()
                return 0  # Never PostQuitMessage into NX's GUI thread.
        except Exception as exc:
            message(str(exc), True)
            return 0
        return U.DefWindowProcW(hwnd, msg, wparam, lparam)

    class_name = "NXScriptUpdater_{}_{}".format(os.getpid(), id(window_proc))
    window_class = WNDCLASS(proc=window_proc, instance=instance, cursor=U.LoadCursorW(None, ctypes.c_void_p(32512)),
                            background=ctypes.c_void_p(16), name=class_name)
    if not U.RegisterClassW(ctypes.byref(window_class)):
        raise ctypes.WinError(ctypes.get_last_error())
    hwnd = None
    try:
        screen_w, screen_h = U.GetSystemMetrics(0), U.GetSystemMetrics(1)
        width, height = min(unit(1380), screen_w-unit(40)), min(unit(740), screen_h-unit(80))
        # WS_CLIPCHILDREN keeps the parent background out of child controls.
        hwnd = U.CreateWindowExW(0x100, class_name, title, 0x00CF0000 | 0x02000000,
                                 max(0, (screen_w-width)//2), max(0, (screen_h-height)//2), width, height,
                                 owner, None, instance, None)
        if not hwnd:
            raise ctypes.WinError(ctypes.get_last_error())
        hwnd_holder["value"] = hwnd

        def control(key, cls, text, x, y, w, h, command=0, style=0, ex=0):
            # WS_CLIPSIBLINGS prevents one control painting over its neighbours.
            handle = U.CreateWindowExW(ex, cls, text, 0x50000000 | 0x04000000 | style, unit(x), unit(y), unit(w), unit(h), hwnd, command, instance, None)
            if not handle:
                raise ctypes.WinError(ctypes.get_last_error())
            controls[key] = handle
            U.SendMessageW(handle, 0x30, font, 1)
            return handle

        control("p_label", "STATIC", "Рабочая папка:", 16, 18, 132, 24)
        control("working", "EDIT", settings['working_folder'], 150, 14, 1014, 26, style=0x10000 | 0x80, ex=0x200)
        control("browse_working", "BUTTON", "Обзор…", 1174, 13, 96, 28, 102, 0x10000)
        control("f_label", "STATIC", "Источник:", 16, 54, 132, 24)
        control("folder", "EDIT", configured_source(settings), 150, 50, 1014, 26, style=0x10000 | 0x80, ex=0x200)
        control("browse_update", "BUTTON", "Обзор…", 1174, 49, 96, 28, 109, 0x10000)
        control("working_recursive", "BUTTON", "Скрипты в подпапках", 16, 87, 255, 26, 110, 3 | 0x10000)
        U.SendMessageW(controls['working_recursive'], 0xF1, int(settings['working_recursive']), 0)
        control("recursive", "BUTTON", "Обновления в подпапках", 282, 87, 260, 26, 107, 3 | 0x10000)
        U.SendMessageW(controls["recursive"], 0xF1, int(settings['update_recursive']), 0)
        control("reload", "BUTTON", "Перечитать INI", 553, 84, 156, 30, 108, 0x10000)
        control("scan", "BUTTON", "Проверить", 1120, 84, 150, 30, 101, 0x10000)
        control("hint", "STATIC", "GitHub: при проверке загружается только manifest.json. Для папки используйте «Обзор…».", 16, 122, 1254, 24)
        control("list", "SysListView32", "", 16, 152, 1270, 356, style=1 | 4 | 8 | 0x10000 | 0x100000 | 0x200000, ex=0x200)
        U.SendMessageW(controls["list"], 0x1000+54, 0, 1 | 4 | 0x20 | 0x10000)
        for i, (label, column_width) in enumerate((("Скрипт", 145), ("Рабочий файл", 460), ("Установлено", 140), ("Доступно", 115), ("Состояние", 285))):
            buffer = ctypes.create_unicode_buffer(label)
            column = LVCOLUMN(mask=1 | 2 | 4, cx=unit(column_width), pszText=ctypes.cast(buffer, W.LPWSTR))
            U.SendMessageW(controls["list"], 0x1000+97, i, ctypes.addressof(column))
        control("details", "EDIT", 'INI: ' + settings['config_path'], 16, 510, 1270, 72, style=4 | 0x800 | 0x40 | 0x200000, ex=0x200)
        control("status", "STATIC", "Подготовка автоматической проверки...", 16, 590, 1270, 24)
        control("select", "BUTTON", "Выбрать обновления", 16, 630, 180, 30, 103, 0x10000)
        control("select_installs", "BUTTON", "Выбрать новые скрипты", 206, 630, 205, 30, 111, 0x10000)
        control("clear", "BUTTON", "Снять все", 421, 630, 105, 30, 104, 0x10000)
        control("apply", "BUTTON", "Применить выбранное", 936, 630, 225, 30, 105, 0x10000)
        control("close", "BUTTON", "Закрыть", 1190, 630, 96, 30, 106, 0x10000)
        layout()
        if owner:
            U.EnableWindow(owner, False)
        U.ShowWindow(hwnd, 5)
        U.SetForegroundWindow(hwnd)
        U.PostMessageW(hwnd, 0x8003, 0, 0)
        msg = W.MSG()
        while not state["closed"]:
            code = U.GetMessageW(ctypes.byref(msg), None, 0, 0)
            if code <= 0:
                if code == 0:
                    U.PostQuitMessage(msg.wParam)
                break
            if handle_list_key(msg):
                continue
            if not U.IsDialogMessageW(hwnd, ctypes.byref(msg)):
                U.TranslateMessage(ctypes.byref(msg))
                U.DispatchMessageW(ctypes.byref(msg))
    finally:
        cancelled.set()
        if owner:
            U.EnableWindow(owner, True)
        if hwnd and not state["closed"]:
            U.DestroyWindow(hwnd)
        U.UnregisterClassW(class_name, instance)
        for thread in state['threads']:
            thread.join(timeout=HTTP_TIMEOUT + 5)
        if any(thread.is_alive() for thread in state['threads']):
            raise RuntimeError('Проверка ещё завершается; самообновление не выполнено.')
    return state['pending_self']

def main():
    try:
        settings = load_settings(__file__)
        with single_instance():
            cleanup_stages(settings)
            pending = run_window()
            if pending is not None:
                commit_jobs([pending])
    except Exception as exc:
        if os.name == "nt":
            ctypes.windll.user32.MessageBoxW(None, str(exc), window_title('Ошибка'), 0x10)
        else:
            raise

if __name__ == "__main__":
    main()
