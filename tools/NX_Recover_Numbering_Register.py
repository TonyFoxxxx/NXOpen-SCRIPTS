# -*- coding: utf-8 -*-
# Восстановление реестра нумерации папок — Siemens NX / Designcenter
# SCRIPT_VERSION: V1
# Рабочее имя: NX_Recover_Numbering_Register.py
# Отдельный журнал: восстановление только из атрибутов открытой рабочей детали.
# Поместите рядом с INI; запустите через Tools -> Journal -> Play.
# Папки, атрибуты NX, INI и существующие записи не изменяются.
# Новые файлы, отчёты, .lock, .bak и .tmp не создаются.
SCRIPT_VERSION = "V1"
SCRIPT_NAME = "Восстановление реестра нумерации папок"

import configparser
import contextlib
import datetime
import io
import math
import ntpath
import os
import posixpath
import re
import zipfile
from xml.dom import minidom

SETTINGS_NAME = "NX_Numbering_Settings_v1.0.ini"
ATTR_ID = "NX_NUM_V1_ID"
ATTR_NAME = "NX_NUM_V1_NAME"
ATTR_OWNER = "NX_NUM_V1_OWNER"
NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
REL_NS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
HEADERS = ("Имя папки", "Номер", "Проект", "Полный путь проекта", "Дата и время",
           "Прежнее имя", "Состояние", "ID записи", "Примечание")
STATES = ("Присвоен", "Зарезервирован", "Отменён")
EPOCH = datetime.datetime(1899, 12, 30)


def author_caption():
    data = (63, 17, 83, 62, 221, 251, 241, 211, 234, 134, 164, 174, 153, 148, 215, 42, 89, 95, 10)
    return bytes(value ^ ((0x5D + i * 11) & 0xFF) for i, value in enumerate(data)).decode("utf-8")


def window_title(suffix):
    return SCRIPT_NAME + " — " + SCRIPT_VERSION + " — " + author_caption() + " — " + suffix


def message(text, kind="info"):
    import ctypes
    user32 = ctypes.windll.user32
    user32.GetActiveWindow.restype = ctypes.c_void_p
    user32.MessageBoxW.argtypes = (ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_uint)
    user32.MessageBoxW.restype = ctypes.c_int
    flags = {"info": 0x40, "error": 0x10, "confirm": 0x124}
    suffix = {"info": "Результат", "error": "Ошибка", "confirm": "Проверка перед восстановлением"}
    result = user32.MessageBoxW(user32.GetActiveWindow(), str(text), window_title(suffix[kind]), flags[kind] | 0x10000)
    return result == 6


def same_path(a, b):
    return bool(a and b) and a.replace("/", "\\").lower() == b.replace("/", "\\").lower()


def children(node, name, namespace=NS):
    return [n for n in node.childNodes
            if n.nodeType == n.ELEMENT_NODE and n.localName == name and n.namespaceURI == namespace]


def xml_parse(data):
    # Preserve all original namespace declarations, including mc:Ignorable prefixes.
    if b"<!DOCTYPE" in data.upper() or b"<!ENTITY" in data.upper():
        raise ValueError("Неподдерживаемая структура XML в Excel-реестре.")
    return minidom.parseString(data)


def plain_text(node):
    if node.nodeType in (node.TEXT_NODE, node.CDATA_SECTION_NODE):
        return node.data
    if node.nodeType == node.ELEMENT_NODE and node.localName in ("rPh", "phoneticPr"):
        return ""
    return "".join(plain_text(child) for child in node.childNodes)


def check_id(value):
    if re.fullmatch(r"[0-9a-fA-F]{32}", value or "") is None:
        raise ValueError("Повреждён служебный ID записи. Восстановление остановлено.")


def stored_number(name):
    match = re.fullmatch(r"([A-Z]{1,8})([1-9][0-9]*)", name or "")
    if not match or int(match.group(2)) > 2147483647:
        raise ValueError("Некорректный номер в служебной отметке: «" + str(name) + "».")
    return int(match.group(2))


class Register:
    def __init__(self, original):
        self.original = original
        with zipfile.ZipFile(io.BytesIO(original), "r") as archive:
            self.infos = archive.infolist()
            if len({item.filename for item in self.infos}) != len(self.infos):
                raise ValueError("В Excel-реестре повторяются части ZIP-книги.")
            self.parts = {item.filename: archive.read(item) for item in self.infos}
            self.comment = archive.comment
        workbook = xml_parse(self.parts["xl/workbook.xml"])
        for prop in workbook.getElementsByTagNameNS(NS, "workbookPr"):
            if prop.getAttribute("date1904").lower() in ("1", "true"):
                raise ValueError("Реестр использует неподдерживаемую систему дат 1904.")
        sheets = [s for s in workbook.getElementsByTagNameNS(NS, "sheet") if s.getAttribute("name") == "Реестр"]
        if len(sheets) != 1:
            raise ValueError("В Excel должен быть один лист «Реестр».")
        rid = sheets[0].getAttributeNS(REL_NS, "id")
        relations = xml_parse(self.parts["xl/_rels/workbook.xml.rels"])
        links = [r for r in relations.documentElement.childNodes
                 if r.nodeType == r.ELEMENT_NODE and r.getAttribute("Id") == rid]
        if len(links) != 1 or links[0].getAttribute("TargetMode").lower() == "external":
            raise ValueError("Не найден внутренний лист реестра.")
        target = links[0].getAttribute("Target").replace("\\", "/")
        self.sheet_path = posixpath.normpath(target.lstrip("/") if target.startswith("/") else posixpath.join("xl", target))
        self.doc = xml_parse(self.parts[self.sheet_path])
        data = children(self.doc.documentElement, "sheetData")
        if len(data) != 1:
            raise ValueError("Повреждена структура листа реестра.")
        self.data = data[0]
        self.rows = {}
        for row in children(self.data, "row"):
            index = int(row.getAttribute("r"))
            if not 1 <= index <= 1048576 or index in self.rows:
                raise ValueError("Некорректные номера строк Excel.")
            self.rows[index] = row
        self.shared = []
        if "xl/sharedStrings.xml" in self.parts:
            shared = xml_parse(self.parts["xl/sharedStrings.xml"])
            self.shared = [plain_text(si) for si in children(shared.documentElement, "si")]
        if tuple(self.row_values(1)) != HEADERS:
            raise ValueError("Столбцы Excel не соответствуют формату реестра нумерации.")
        self.styles = [self.cell(2, col).getAttribute("s") if self.cell(2, col) is not None else "" for col in range(9)]
        if not self.styles[4]:
            raise ValueError("В реестре отсутствует формат даты и времени.")
        self.records = []
        self.by_id, self.by_name, self.by_number, self.uuid_case = {}, {}, {}, {}
        self.last_row = 1
        for index in sorted(self.rows):
            if index <= 1:
                continue
            values = self.row_values(index)
            if not any(values):
                continue
            name, number, project, path, stamp, old, state, identity, note = values
            check_id(identity)
            if re.fullmatch(r"[0-9]+", number or "") is None:
                raise ValueError("Неверный номер в строке " + str(index))
            number = int(number)
            if not 1 <= number <= 2147483647 or not name.endswith(str(number)) or len(name) <= len(str(number)):
                raise ValueError("Имя и номер расходятся в строке " + str(index))
            if not project or not path or state not in STATES or not math.isfinite(float(stamp)):
                raise ValueError("Неполная запись в строке " + str(index))
            EPOCH + datetime.timedelta(days=float(stamp))
            record = dict(name=name, number=number, project=project, path=path, id=identity, state=state, row=index)
            self.add_index(record)
            self.last_row = max(self.last_row, index)

    def cell(self, row, col):
        node = self.rows.get(row)
        if node is None:
            return None
        address = chr(65 + col) + str(row)
        found = [c for c in children(node, "c") if c.getAttribute("r") == address]
        if len(found) > 1:
            raise ValueError("Повторяется ячейка " + address)
        return found[0] if found else None

    def row_values(self, index):
        values = []
        for col in range(9):
            cell = self.cell(index, col)
            if cell is None:
                values.append("")
                continue
            if children(cell, "f"):
                raise ValueError("Формулы в записях реестра не поддерживаются.")
            kind = cell.getAttribute("t")
            value_nodes = children(cell, "is" if kind == "inlineStr" else "v")
            text = plain_text(value_nodes[0]) if value_nodes else ""
            if kind == "s":
                string_index = int(text)
                if not 0 <= string_index < len(self.shared):
                    raise ValueError("Повреждена таблица строк Excel.")
                text = self.shared[string_index]
            values.append(text)
        return values

    def add_index(self, record):
        if (record["id"].lower() in self.uuid_case or record["name"].lower() in self.by_name
                or record["number"] in self.by_number):
            raise ValueError("Повторяется номер, имя или ID: " + record["name"])
        self.records.append(record)
        self.by_id[record["id"]] = record
        self.uuid_case[record["id"].lower()] = record["id"]
        self.by_name[record["name"].lower()] = record
        self.by_number[record["number"]] = record

    def element(self, name):
        prefix = self.data.prefix
        return self.doc.createElementNS(NS, (prefix + ":" if prefix else "") + name)

    def append(self, record, now):
        self.last_row += 1
        if self.last_row > 1048576:
            raise ValueError("В реестре закончились строки.")
        note = ("Восстановлено из служебных атрибутов NX " + now.strftime("%d.%m.%Y %H:%M:%S") +
                ". Столбец «Дата и время» содержит дату восстановления; исходная дата выдачи и прежнее имя неизвестны.")
        serial = (now - EPOCH).total_seconds() / 86400.0
        values = (record["name"], str(record["number"]), record["project"], record["path"],
                  format(serial, ".17g"), "", "Присвоен", record["id"], note)
        row = self.rows.get(self.last_row)
        if row is None:
            row = self.element("row")
            row.setAttribute("r", str(self.last_row))
            row.setAttribute("ht", "24")
            row.setAttribute("customHeight", "1")
            following = next((n for n in children(self.data, "row") if int(n.getAttribute("r")) > self.last_row), None)
            self.data.insertBefore(row, following)
            self.rows[self.last_row] = row
        for col, value in enumerate(values):
            old = self.cell(self.last_row, col)
            cell = self.element("c")
            cell.setAttribute("r", chr(65 + col) + str(self.last_row))
            style = old.getAttribute("s") if old is not None else self.styles[col]
            if style:
                cell.setAttribute("s", style)
            if col in (1, 4):
                child = self.element("v")
                child.appendChild(self.doc.createTextNode(value))
                cell.appendChild(child)
            else:
                cell.setAttribute("t", "inlineStr")
                inline = self.element("is")
                text = self.element("t")
                text.setAttribute("xml:space", "preserve")
                text.appendChild(self.doc.createTextNode(value))
                inline.appendChild(text)
                cell.appendChild(inline)
            if old is not None:
                row.replaceChild(cell, old)
            else:
                following = next((n for n in children(row, "c") if column_index(n.getAttribute("r")) > col), None)
                row.insertBefore(cell, following)
        self.add_index(record)

    def encoded(self):
        extent = "A1:I" + str(max(2, self.last_row))
        for tag in ("dimension", "autoFilter"):
            for node in children(self.doc.documentElement, tag):
                node.setAttribute("ref", extent)
        parts = dict(self.parts)
        parts[self.sheet_path] = self.doc.toxml(encoding="utf-8")
        for name, payload in list(parts.items()):
            if not (name.startswith("xl/tables/") and name.endswith(".xml")):
                continue
            table = xml_parse(payload)
            if table.documentElement.getAttribute("name") == "NXNumberingRegister":
                table.documentElement.setAttribute("ref", extent)
                for node in children(table.documentElement, "autoFilter"):
                    node.setAttribute("ref", extent)
                parts[name] = table.toxml(encoding="utf-8")
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            archive.comment = self.comment
            for info in self.infos:
                archive.writestr(info, parts[info.filename])
        result = stream.getvalue()
        checked = Register(result)
        if len(checked.records) != len(self.records):
            raise ValueError("Проверка сформированного реестра не пройдена. Файл не изменён.")
        return result


def column_index(address):
    letters = re.match(r"[A-Z]+", address)
    if not letters:
        raise ValueError("Некорректный адрес ячейки Excel.")
    value = 0
    for char in letters.group(0):
        value = value * 26 + ord(char) - 64
    return value - 1


def recovery_plan(markers, project_path, registry):
    own, foreign, unmarked = {}, 0, 0
    for marker in markers:
        identity, name, owner = marker["id"], marker["name"], marker["owner"]
        if not any((identity, name, owner)):
            unmarked += 1
            continue
        if not all((identity, name, owner)):
            raise ValueError("Неполная служебная отметка папки «" + marker["current"] + "». Файл не изменён.")
        if not same_path(owner, project_path):
            foreign += 1
            continue
        check_id(identity)
        number = stored_number(name)
        if identity in own and own[identity]["name"] != name:
            raise ValueError("Один ID содержит разные номера в папках NX. Файл не изменён.")
        own[identity] = dict(id=identity, name=name, number=number, path=project_path,
                             project=ntpath.splitext(ntpath.basename(project_path))[0], state="Присвоен")
    missing, existing = [], 0
    planned_names, planned_numbers, planned_ids = set(), set(), set()
    for record in sorted(own.values(), key=lambda r: r["number"]):
        old = registry.by_id.get(record["id"])
        if old is not None:
            if old["name"] != record["name"] or not same_path(old["path"], project_path) or old["state"] == "Отменён":
                raise ValueError("Отметка «" + record["name"] + "» конфликтует с существующей записью. Файл не изменён.")
            existing += 1
            continue
        if (record["id"].lower() in registry.uuid_case or record["name"].lower() in registry.by_name
                or record["number"] in registry.by_number or record["id"].lower() in planned_ids
                or record["name"].lower() in planned_names or record["number"] in planned_numbers):
            raise ValueError("Номер, имя или ID «" + record["name"] + "» уже занят другой записью. Файл не изменён.")
        planned_ids.add(record["id"].lower())
        planned_names.add(record["name"].lower())
        planned_numbers.add(record["number"])
        missing.append(record)
    return dict(missing=missing, existing=existing, foreign=foreign, unmarked=unmarked)


@contextlib.contextmanager
def exclusive_file(path):
    # OPEN_EXISTING / O_RDWR: never create even a lock file.
    if os.name == "nt":
        import ctypes
        import msvcrt
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.CreateFileW.argtypes = (ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p,
                                       ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p)
        kernel.CreateFileW.restype = ctypes.c_void_p
        kernel.CloseHandle.argtypes = (ctypes.c_void_p,)
        handle = kernel.CreateFileW(path, 0xC0000000, 0, None, 3, 0x80, None)
        if handle == ctypes.c_void_p(-1).value:
            raise OSError("Файл недоступен: закройте Excel и другие запуски нумератора.\n" + path +
                          "\nКод Windows: " + str(ctypes.get_last_error()))
        try:
            fd = msvcrt.open_osfhandle(handle, os.O_RDWR | os.O_BINARY)
        except BaseException:
            kernel.CloseHandle(handle)
            raise
    else:
        import fcntl
        fd = os.open(path, os.O_RDWR)
        try:
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BaseException:
            os.close(fd)
            raise
    with os.fdopen(fd, "r+b", buffering=0) as stream:
        yield stream


def write_bytes(stream, data):
    stream.seek(0)
    view = memoryview(data)
    while view:
        written = stream.write(view)
        if not written:
            raise OSError("Не удалось полностью записать Excel-реестр.")
        view = view[written:]
    stream.truncate(len(data))
    stream.flush()
    os.fsync(stream.fileno())


def save_existing(stream, original, updated):
    stream.seek(0)
    if stream.read() != original:
        raise OSError("Реестр изменился во время проверки. Восстановление отменено.")
    # No disk backup/temp. Ordinary I/O failures roll back from memory.
    # An abrupt process/OS termination cannot be rolled back by this function.
    try:
        write_bytes(stream, updated)
        stream.seek(0)
        if stream.read() != updated:
            raise OSError("Записанный файл не прошёл контроль содержимого.")
    except BaseException as error:
        try:
            write_bytes(stream, original)
            stream.seek(0)
            if stream.read() != original:
                raise OSError("Не совпали исходные байты.")
        except BaseException as rollback:
            raise OSError("Не удалось записать и вернуть исходный Excel. Не запускайте нумерацию.\n" +
                          str(error) + "\n" + str(rollback)) from error
        raise OSError("Ошибка записи. Исходное содержимое Excel восстановлено.\n" + str(error)) from error


def recover_file(path, markers, project_path, confirm, settings=None):
    with contextlib.ExitStack() as stack:
        # Respect an existing gate used by the numbering journal, without creating it.
        if os.path.exists(path + ".lock"):
            stack.enter_context(exclusive_file(path + ".lock"))
        stream = stack.enter_context(exclusive_file(path))
        original = stream.read()
        registry = Register(original)
        plan = recovery_plan(markers, project_path, registry)
        missing = plan["missing"]
        if not missing:
            return ("Недостающих записей для открытого проекта не найдено.\n" +
                    "Совпадающих записей: " + str(plan["existing"]) + ".\n" +
                    "Папок с отметками другого проекта: " + str(plan["foreign"]) + ".\n" +
                    "Папок без отметок: " + str(plan["unmarked"]) + ".\n\nРеестр:\n" + path +
                    "\n\nФайл и проект не изменены.")
        before_count = len(registry.records)
        recovered_at = datetime.datetime.now()
        for record in missing:
            registry.append(record, recovered_at)
        updated = registry.encoded()
        numbers = ", ".join(r["name"] for r in missing[:12])
        if len(missing) > 12:
            numbers += " … (ещё " + str(len(missing) - 12) + ")"
        warning = ""
        if settings and any(not settings["start"] <= r["number"] <= settings["end"]
                            or r["name"][:-len(str(r["number"]))] != settings["prefix"] for r in missing):
            warning = ("\n\nВосстанавливаемые номера выходят за текущие настройки INI. "
                       "Восстановление сохранит их; перед обычной нумерацией проверьте свой диапазон.")
        prompt = ("Проект: " + ntpath.basename(project_path) + "\n\nРеестр:\n" + path +
                  "\n\nДобавить записей: " + str(len(missing)) + "\n" + numbers +
                  "\nСуществующих записей сохраняется: " + str(before_count) +
                  "\n\nНомера взяты из служебных отметок. Имена и атрибуты папок NX не изменятся." +
                  "\nДата в новых строках — время восстановления. Исходная дата выдачи неизвестна." +
                  "\n\nВосстанавливается только открытый проект. Другие проекты нужно проверить отдельно." +
                  warning + "\n\nДобавить эти записи в Excel?")
        if not confirm(prompt):
            return "Восстановление отменено. Excel и проект не изменены."
        save_existing(stream, original, updated)
        return ("Восстановлено записей: " + str(len(missing)) + ".\n" + numbers +
                "\nПрежних записей сохранено: " + str(before_count) + ".\n\nРеестр:\n" + path +
                "\n\nПапки и служебные отметки NX не изменены. "
                "Восстановите остальные проекты с потерянными записями перед выдачей новых номеров." + warning)


def load_settings(path):
    parser = configparser.ConfigParser(interpolation=None, strict=True)
    with open(path, "r", encoding="utf-8-sig") as source:
        parser.read_file(source)
    section = parser["Numbering"]
    filename = section["RegistryFile"].strip()
    if not filename or os.path.splitext(filename)[1].lower() != ".xlsx":
        raise ValueError("RegistryFile в INI должен указывать на заполненный файл .xlsx.")
    registry_path = filename if os.path.isabs(filename) else os.path.join(os.path.dirname(path), filename)
    settings = dict(start=int(section["StartNumber"]), end=int(section["EndNumber"]), prefix=section["Prefix"].strip())
    if settings["start"] < 1 or settings["end"] < settings["start"] or not re.fullmatch(r"[A-Z]{1,8}", settings["prefix"]):
        raise ValueError("Проверьте диапазон и Prefix в INI.")
    return os.path.abspath(registry_path), settings


def collect_markers(nx, root):
    markers, visited = [], set()

    def attr(group, title):
        if group.HasUserAttribute(title, nx.NXObject.AttributeType.String, -1):
            return group.GetStringUserAttribute(title, -1) or ""
        return ""

    def walk(parent):
        tag = str(parent.Tag)
        if tag in visited:
            raise ValueError("Повторный узел в дереве программ.")
        visited.add(tag)
        for group in parent.GetMembers():
            if not isinstance(group, nx.CAM.NCGroup):
                continue
            markers.append(dict(current=group.Name or "", id=attr(group, ATTR_ID),
                                name=attr(group, ATTR_NAME), owner=attr(group, ATTR_OWNER)))
            walk(group)

    walk(root)
    return markers


def main():
    try:
        import NXOpen
        import NXOpen.CAM
        session = NXOpen.Session.GetSession()
        part = session.Parts.Work
        if part is None or part.CAMSetup is None:
            raise ValueError("Откройте сохранённый CAM-проект с потерянными записями реестра.")
        project_path = part.FullPath
        if not project_path or not os.path.isabs(project_path) or not os.path.isfile(project_path):
            raise ValueError("Сначала откройте сохранённый CAM-проект (.prt).")
        project_path = os.path.abspath(project_path)
        journal = session.ExecutingJournal
        if not journal or not os.path.isabs(journal):
            raise ValueError("NX не сообщил путь к журналу. Запустите файл .py через Tools → Journal → Play.")
        settings_path = os.path.join(os.path.dirname(os.path.abspath(journal)), SETTINGS_NAME)
        if not os.path.isfile(settings_path):
            raise ValueError("Не найден INI:\n" + settings_path + "\n\nПоместите этот скрипт рядом с действующим INI нумератора.")
        path, settings = load_settings(settings_path)
        if not os.path.isfile(path):
            raise ValueError("Не найден заполненный Excel-реестр:\n" + path + "\nВосстановление не создаёт пустую книгу.")
        root = part.CAMSetup.GetRoot(NXOpen.CAM.CAMSetup.View.ProgramOrder)
        if root is None:
            raise ValueError("Не найдено дерево «Порядок программ».")
        markers = collect_markers(NXOpen, root)
        result = recover_file(path, markers, project_path, lambda text: message(text, "confirm"), settings)
        message(result)
    except Exception as error:
        message(str(error), "error")


if __name__ == "__main__":
    main()
