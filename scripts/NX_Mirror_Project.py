# -*- coding: utf-8 -*-
# Зеркальная копия CAM-проекта — тест без компонентов
# SCRIPT_VERSION: V1
SCRIPT_VERSION = "V1"
SCRIPT_NAME = "Зеркальная копия CAM-проекта — тест"

# Experimental journal: single saved native PRT, no assembly components.
# No Save/SaveAs, toolpath generation, posting or network calls.
import sys
_old_bytecode = sys.dont_write_bytecode
try:
    sys.dont_write_bytecode = True
    import ctypes
    from ctypes import wintypes as W
    import hashlib
    import math
    import os
    import re
    import struct
    import traceback
finally:
    sys.dont_write_bytecode = _old_bytecode
    del _old_bytecode

_CREDIT = bytes((98, 121, 32, 64, 84, 111, 110, 121, 95, 70, 111, 120, 120, 120, 32, 40, 84, 71, 41)).decode("ascii")
TITLE = SCRIPT_NAME + " — " + SCRIPT_VERSION + " | " + _CREDIT
GEOMETRY_KINDS = ("Part", "Blank", "Check", "Trim", "Cut Area", "Wall", "Drive")
MAX_OBJECTS = 20000


def reflect(values, axis, offset=0.0):
    result = [float(x) for x in values]
    result[axis] = 2.0 * offset - result[axis]
    return result


def cross(a, b):
    return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]


def dot(a, b):
    return sum(x*y for x, y in zip(a, b))


def reflected_frame(matrix, axis):
    """Preserve reflected X and Z; reconstruct Y for a right-handed MCS."""
    if len(matrix) != 9 or not all(math.isfinite(x) for x in matrix):
        raise ValueError("Некорректная матрица MCS")
    x, y, z = matrix[:3], matrix[3:6], matrix[6:9]
    if max(abs(dot(v, v)-1.0) for v in (x, y, z)) > 1e-6 or dot(cross(x, y), z) < 0.999999:
        raise ValueError("MCS не является ортонормированной правой системой")
    x, z = reflect(x, axis), reflect(z, axis)
    return x + cross(z, x) + z


def reflected_box(box, axis, offset):
    result = list(box)
    result[axis] = 2.0*offset - box[axis+3]
    result[axis+3] = 2.0*offset - box[axis]
    return result


def close_values(a, b, tolerance=1e-5):
    return len(a) == len(b) and all(math.isfinite(x) and math.isfinite(y) and
        abs(x-y) <= tolerance + 1e-9*max(abs(x), abs(y)) for x, y in zip(a, b))


def digest(path):
    h = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(1024*1024), b""):
            h.update(block)
    return h.hexdigest()


def copy_exclusive(source):
    """Only the requested PRT copy is written; existing files are never replaced."""
    stem, extension = os.path.splitext(source)
    with open(source, "rb") as inp:
        before = os.fstat(inp.fileno())
        for number in range(1, 10000):
            target = stem + "_MIRROR_TEST_" + str(number).zfill(3) + extension
            try:
                out = open(target, "xb")
                break
            except FileExistsError:
                continue
        else:
            raise RuntimeError("Не найдено свободное имя копии")
        try:
            h = hashlib.sha256()
            with out:
                for block in iter(lambda: inp.read(1024*1024), b""):
                    out.write(block)
                    h.update(block)
            after = os.fstat(inp.fileno())
            if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
                raise RuntimeError("Исходный файл изменился во время копирования")
            if digest(target) != h.hexdigest() or digest(source) != h.hexdigest():
                raise RuntimeError("Контрольная сумма копии не совпала с исходником")
            return target, h.hexdigest()
        except Exception:
            # This file was exclusively created by this call and is not open in NX.
            os.remove(target)
            raise


class NativeCam:
    """Documented UF C readers; avoids language-specific CAM enum aliases.

    Signatures: Siemens uf_cam.h, uf_camgeom.h, uf_cambnd.h, uf.h.
    UF_CAM_geom_type_t is Part=0, Blank=1, Check=2, Trim=3,
    CutArea=4, Wall=5, Drive=6. tag_t is unsigned 32-bit on Windows.
    Opaque item handles are pointers, never tags; only the returned arrays
    are freed, not the items owned by NX.
    """
    def __init__(self):
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.GetModuleHandleW.argtypes = (W.LPCWSTR,)
        kernel.GetModuleHandleW.restype = W.HMODULE
        self.libs = []
        roots = [os.environ.get("UGII_ROOT_DIR", ""),
                 os.path.join(os.environ.get("UGII_BASE_DIR", ""), "NXBIN")]
        for name in ("libufun.dll", "libufun_cam.dll"):
            handle = kernel.GetModuleHandleW(name)
            if handle:
                self.libs.append(ctypes.CDLL(name, handle=handle))
                continue
            for root in roots:
                path = os.path.join(root, name)
                if root and os.path.isabs(path) and os.path.isfile(path):
                    self.libs.append(ctypes.CDLL(path))
                    break
        u, i, p = ctypes.c_uint32, ctypes.c_int, ctypes.c_void_p
        ip, pp = ctypes.POINTER(i), ctypes.POINTER(p)
        self.free = self.bind("UF_free", (p,), None)
        self.fail_message = self.bind("UF_get_fail_message", (i, p))
        self.items = self.bind("UF_CAMGEOM_ask_items", (u, i, ip, pp))
        self.item_entity = self.bind("UF_CAMGEOM_ask_item_entity", (p, ctypes.POINTER(u)))
        self.collector = self.bind("UF_CAMGEOM_ask_collector_items", (p, ip, pp))
        self.boundaries = self.bind("UF_CAMBND_ask_boundaries", (u, i, ip, pp))
        self.members = self.bind("UF_CAMBND_ask_boundary_items", (p, ip, pp))
        self.member_entity = self.bind("UF_CAMBND_ask_item_entity", (p, ctypes.POINTER(u)))
        self.custom = self.bind("UF_CAMGEOM_ask_custom_points", (u, i, ip, pp))
        dp = ctypes.POINTER(ctypes.c_double)
        self.mirror_matrix = self.bind("UF_MTX4_mirror", (dp, dp, dp))
        self.map_point = self.bind("UF_MTX4_vec3_multiply", (dp, dp, dp), None)

    def bind(self, name, arguments, result=ctypes.c_int):
        for library in self.libs:
            function = getattr(library, name, None)
            if function is not None:
                function.argtypes, function.restype = arguments, result
                return function
        raise RuntimeError("В установленном NX недоступна функция " + name)

    def check(self, code, context):
        if code:
            buffer = ctypes.create_string_buffer(2048)
            self.fail_message(code, buffer)
            message = buffer.value.decode("utf-8", errors="replace")
            raise RuntimeError(context + ": " + str(code) + " — " + message)

    def pointers(self, function, *args):
        count, data = ctypes.c_int(), ctypes.c_void_p()
        try:
            self.check(function(*args, ctypes.byref(count), ctypes.byref(data)), function.__name__)
            if count.value < 0 or count.value > MAX_OBJECTS or (count.value and not data.value):
                raise RuntimeError("Некорректный размер массива CAM: " + str(count.value))
            array = ctypes.cast(data, ctypes.POINTER(ctypes.c_void_p))
            return [array[n] for n in range(count.value)]
        finally:
            if data.value:
                self.free(data)

    def entity(self, function, item, allow_zero=False):
        if not item:
            raise RuntimeError("NX вернул пустой дескриптор геометрии")
        tag = ctypes.c_uint32()
        self.check(function(item, ctypes.byref(tag)), function.__name__)
        if not tag.value and not allow_zero:
            raise RuntimeError("Найдена неассоциативная граница без ссылки на геометрию. V1 её пока не переносит.")
        return tag.value

    def collector_entities(self, item):
        count, data = ctypes.c_int(), ctypes.c_void_p()
        try:
            self.check(self.collector(item, ctypes.byref(count), ctypes.byref(data)), "UF_CAMGEOM_ask_collector_items")
            if count.value < 0 or count.value > MAX_OBJECTS or (count.value and not data.value):
                raise RuntimeError("Некорректный массив элементов коллектора CAM")
            array = ctypes.cast(data, ctypes.POINTER(ctypes.c_uint32))
            tags = [int(array[n]) for n in range(count.value)]
            if any(not tag for tag in tags):
                raise RuntimeError("Пустая ссылка в коллекторе CAM")
            return tags
        finally:
            if data.value:
                self.free(data)

    def custom_count(self, tag, kind):
        count, data = ctypes.c_int(), ctypes.c_void_p()
        try:
            self.check(self.custom(tag, kind, ctypes.byref(count), ctypes.byref(data)), "UF_CAMGEOM_ask_custom_points")
            if count.value < 0 or count.value > MAX_OBJECTS:
                raise RuntimeError("Некорректное количество пользовательских точек")
            return count.value
        finally:
            if data.value:
                # Unlike ask_items, this API allocates every point record too.
                if 0 <= count.value <= MAX_OBJECTS:
                    entries = ctypes.cast(data, ctypes.POINTER(ctypes.c_void_p))
                    for n in range(count.value):
                        if entries[n]:
                            self.free(ctypes.c_void_p(entries[n]))
                self.free(data)

    def reflection_matrix(self, axis, offset):
        origin, normal = (ctypes.c_double * 3)(), (ctypes.c_double * 3)()
        origin[axis], normal[axis] = offset, 1.0
        matrix = (ctypes.c_double * 16)()
        self.check(self.mirror_matrix(origin, normal, matrix), "UF_MTX4_mirror")
        # Verify using NX's own point/matrix convention before touching geometry.
        for point in ([0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]):
            result = (ctypes.c_double * 3)()
            self.map_point((ctypes.c_double * 3)(*point), matrix, result)
            if not close_values(result, reflect(point, axis, offset), 1e-9):
                raise RuntimeError("Матрица NX не прошла проверку отражения контрольных точек")
        return list(matrix)


def counted(value):
    count, items = value
    items = [] if items is None else list(items)
    if int(count) != len(items):
        raise RuntimeError("Неполный массив параметров NX")
    return items


def field(value, name):
    names = value.keys() if isinstance(value, dict) else dir(value)
    matches = [x for x in names if x.replace("_", "").lower() == name.replace("_", "").lower()]
    if len(matches) != 1:
        raise RuntimeError("Недоступно поле UF: " + name)
    return value[matches[0]] if isinstance(value, dict) else getattr(value, matches[0])


def parameter_type(value, nx):
    # The Python Param.Type enum was confirmed by the V1 diagnostic report.
    for name in dir(nx.UF.Param.Type):
        if not name.startswith("_") and getattr(nx.UF.Param.Type, name) == value:
            return re.sub("[^a-z0-9]", "", name.lower()).removeprefix("type")
    return "unknown"


def cam_nodes(setup, nx):
    root = setup.GetRoot(nx.CAM.CAMSetup.View.Geometry)
    nodes, pending, seen = [], [root], set()
    while pending:
        node = pending.pop()
        if int(node.Tag) in seen:
            continue
        seen.add(int(node.Tag))
        nodes.append(node)
        if len(nodes) > MAX_OBJECTS:
            raise RuntimeError("Слишком большое дерево CAM для тестовой версии")
        if isinstance(node, nx.CAM.NCGroup):
            pending.extend(reversed(list(node.GetMembers())))
    return root, nodes


class Trial:
    def __init__(self, session, nx, uf, part, axis, offset, lines):
        self.session, self.nx, self.uf, self.part = session, nx, uf, part
        self.axis, self.offset, self.lines = axis, offset, lines
        self.stage = "Подготовка копии"
        self.native = NativeCam()
        self.bodies = list(part.Bodies)
        self.setup = part.CAMSetup
        self.operations = list(self.setup.CAMOperationCollection)
        self.root, self.nodes = cam_nodes(self.setup, nx)
        self.refs, self.bounds, self.frames, self.mcs_bindings = {}, {}, {}, []
        self.raw_spatial = []
        self.mcs_expected = {}
        if not self.bodies or not self.operations:
            raise RuntimeError("В копии должны быть тела и CAM-операции")
        if any(op.IsMirror() for op in self.operations):
            raise RuntimeError("В этой версии нужны обычные операции, без существующих зеркальных операций")
        allowed_operations = {"VolumeBased25DMillingOperation", "SurfaceContour", "PlanarMilling", "TpMultiAxisDeburring"}
        for op in self.operations:
            if type(op).__name__ not in allowed_operations:
                raise RuntimeError("В первом тесте пока не поддержан тип операции " + type(op).__name__)
        # TransformEntities also transforms model parents. This first trial is
        # deliberately restricted to simple, local model histories.
        allowed = {"DATUM_CSYS", "DATUM_PLANE", "DATUM_AXIS", "SKETCH", "EXTRUDE",
                   "BLEND", "TOOLING BOX", "FSET", "BREP", "BLOCK", "CYLINDER",
                   "CONE", "SPHERE", "IMPORTED_BODY", "IMPORT"}
        for feature in part.Features:
            name = str(feature.FeatureType).upper()
            if name not in allowed:
                raise RuntimeError("Для первого теста пока не поддержан модельный элемент " + name)
            for parent in feature.GetParents():
                owner = parent.OwningPart
                if owner is not None and int(owner.Tag) != int(part.Tag):
                    raise RuntimeError("Модель содержит внешнего родителя. Тест остановлен до преобразования.")

    def reference(self, tag):
        tag = int(tag)
        if not tag or tag in self.refs:
            return
        obj = self.nx.TaggedObjectManager.GetTaggedObject(tag)
        owner = getattr(obj, "OwningPart", None)
        if owner is not None and int(owner.Tag) != int(self.part.Tag):
            raise RuntimeError("Обнаружена ссылка на другую деталь. V1 работает только с одной деталью.")
        if bool(getattr(obj, "IsOccurrence", False)):
            raise RuntimeError("Обнаружено вхождение компонента")
        self.refs[tag] = obj

    def geometry_snapshot(self, collect=False):
        result = {}
        for node in self.nodes:
            for kind, label in enumerate(GEOMETRY_KINDS):
                context = str(node.Name) + "/" + label
                items = self.native.pointers(self.native.items, int(node.Tag), kind)
                tags = []
                for item in items:
                    tag = self.native.entity(self.native.item_entity, item, allow_zero=True)
                    obj = self.nx.TaggedObjectManager.GetTaggedObject(tag) if tag else None
                    if not tag or isinstance(obj, self.nx.ScCollector):
                        tags.extend(self.native.collector_entities(item))
                    else:
                        tags.append(tag)
                boundaries = []
                for handle in self.native.pointers(self.native.boundaries, int(node.Tag), kind):
                    data = self.uf.Cambnd.AskBoundaryData(handle)
                    # Face-derived boundaries use an automatically evaluated plane.
                    # Preserving a manual plane also requires orientation/material-side handling.
                    if int(field(data, "plane_type")) != 1:
                        raise RuntimeError(context + ": граница с заданной плоскостью пока не поддерживается в V1")
                    members = self.native.pointers(self.native.members, handle)
                    member_tags = [self.native.entity(self.native.member_entity, item) for item in members]
                    boundaries.append(tuple(member_tags))
                    tags.extend(member_tags)
                result[(int(node.Tag), kind)] = (tuple(tags), tuple(boundaries))
                if collect:
                    for tag in tags:
                        self.reference(tag)
            if collect and isinstance(node, self.nx.CAM.Operation):
                kinds = {"PlanarMilling": (0, 1), "VolumeBased25DMillingOperation": (0, 1),
                         "SurfaceContour": (1,), "TpMultiAxisDeburring": ()}[type(node).__name__]
                for kind in kinds:
                    if self.native.custom_count(int(node.Tag), kind):
                        raise RuntimeError(str(node.Name) + ": пользовательские стартовые/предсверлильные точки пока не поддерживаются в V1")
        return result

    def capture_parameters(self):
        for node in self.nodes:
            indices = sorted(set(counted(self.uf.Param.AskRequiredParams(node.Tag))))
            for index in indices:
                attrs = self.uf.Param.AskParamAttributes(index)
                kind = parameter_type(field(attrs, "type"), self.nx)
                if kind == "tag":
                    tag = int(self.uf.Param.AskTagValue(node.Tag, index))
                    if tag:
                        self.reference(tag)
                    if index in (59, 60) and tag:
                        state = int(self.uf.Param.AskParamStatus(node.Tag, index))
                        self.mcs_expected[(int(node.Tag), index)] = tag
                        if tag not in self.frames:
                            matrix, origin = self.uf.Csys.AskCsysInfo(tag)
                            axes = list(self.uf.Csys.AskMatrixValues(matrix))
                            reflected_frame(axes, self.axis)
                            self.frames[tag] = (list(origin), axes)
                        if state == 2 or int(node.Tag) == int(self.root.Tag):
                            self.mcs_bindings.append((node, index, tag))
                elif kind == "vlatag":
                    for tag in counted(self.uf.Param.AskTagVla(node.Tag, index)):
                        self.reference(tag)
                elif kind in ("3d", "3dlength"):
                    if int(self.uf.Param.AskParamStatus(node.Tag, index)) == 2:
                        value = list(self.uf.Param.Ask3dValue(node.Tag, index))
                        if any(abs(x) > 1e-10 for x in value):
                            self.raw_spatial.append(str(node.Name) + ": " + str(field(attrs, "name")) + " = " + str(value))
        if not self.frames:
            raise RuntimeError("Не найдены системы координат MCS")

    def capture_topology(self):
        result = {}
        for body in self.bodies:
            self.reference(body.Tag)
            faces, edges = list(body.GetFaces()), list(body.GetEdges())
            result[int(body.Tag)] = (tuple(sorted(int(f.Tag) for f in faces)),
                                     tuple(sorted(int(e.Tag) for e in edges)))
            for obj in [body] + faces + edges:
                self.bounds[int(obj.Tag)] = list(self.uf.Modl.AskBoundingBox(obj.Tag))
        return result

    def operation_snapshot(self):
        return [(int(op.Tag), str(op.Name), type(op).__name__,
                 int(op.GetParent(self.nx.CAM.CAMSetup.View.Geometry).Tag))
                for op in self.setup.CAMOperationCollection]

    def run(self):
        self.stage = "Чтение геометрических ссылок в копии"
        geometry = self.geometry_snapshot(collect=True)
        self.stage = "Чтение MCS и ссылочных параметров в копии"
        self.capture_parameters()
        self.stage = "Чтение топологии тел"
        topology = self.capture_topology()
        operations = self.operation_snapshot()
        targets = {int(body.Tag): body for body in self.bodies}
        transformable = tuple(getattr(self.nx, name) for name in
            ("Curve", "Point", "Plane", "DatumPlane") if hasattr(self.nx, name))
        for tag, obj in self.refs.items():
            if isinstance(obj, transformable):
                targets[tag] = obj
                if isinstance(obj, (self.nx.Curve, self.nx.Point)):
                    self.bounds[tag] = list(self.uf.Modl.AskBoundingBox(tag))
        self.lines.append("Тела: " + str(len(self.bodies)) + "; операции: " + str(len(self.operations)))
        self.lines.append("Проверяемые тела/грани/рёбра/кривые/точки: " + str(len(self.bounds)))
        self.lines.append("MCS/RCS, включая корневую систему: " + str(len(self.frames)))
        self.stage = "Создание матрицы отражения"
        matrix = self.native.reflection_matrix(self.axis, self.offset)
        self.stage = "Отражение геометрии и её модельных родителей"
        self.uf.Modl.TransformEntities(len(targets), list(targets), matrix)
        self.stage = "Перенос систем координат"
        replacement = {}
        for tag, (old_origin, axes) in self.frames.items():
            origin = reflect(old_origin, self.axis, self.offset)
            axes = reflected_frame(axes, self.axis)
            matrix_tag = self.uf.Csys.CreateMatrix(axes)
            replacement[tag] = self.uf.Csys.CreateCsys(origin, matrix_tag)
        for node, index, old_tag in self.mcs_bindings:
            self.uf.Param.SetTagValue(node.Tag, index, replacement[old_tag])
        self.stage = "Удаление рассчитанных траекторий в копии"
        self.setup.DeleteToolPath(self.operations)
        self.stage = "Обновление и проверка результата"
        self.uf.Modl.Update()
        if self.operation_snapshot() != operations:
            raise RuntimeError("Изменился состав операций или их геометрические родители")
        if any(op.IsMirror() or op.AskPathExists() for op in self.operations):
            raise RuntimeError("В копии остались траектории или зеркальные операции")
        current_bodies = list(self.part.Bodies)
        if set(int(b.Tag) for b in current_bodies) != set(topology):
            raise RuntimeError("При преобразовании изменились идентификаторы тел")
        for body in current_bodies:
            current = (tuple(sorted(int(f.Tag) for f in body.GetFaces())),
                       tuple(sorted(int(e.Tag) for e in body.GetEdges())))
            if current != topology[int(body.Tag)]:
                raise RuntimeError("Изменились идентификаторы граней/рёбер; привязки не подтверждены")
        for tag, box in self.bounds.items():
            actual = list(self.uf.Modl.AskBoundingBox(tag))
            if not close_values(actual, reflected_box(box, self.axis, self.offset)):
                raise RuntimeError("Не подтверждено отражение объекта " + str(tag) +
                                   "\nОжидалось: " + str(reflected_box(box, self.axis, self.offset)) +
                                   "\nПолучено: " + str(actual))
        if self.geometry_snapshot() != geometry:
            raise RuntimeError("Изменились ссылки на выбранную CAM-геометрию/границы")
        for (tag, index), old_tag in self.mcs_expected.items():
            actual = self.uf.Param.AskTagValue(tag, index)
            if int(actual) != int(replacement[old_tag]):
                raise RuntimeError("Не подтверждено наследование MCS/RCS: " + str(tag))
            matrix_tag, origin = self.uf.Csys.AskCsysInfo(actual)
            axes = self.uf.Csys.AskMatrixValues(matrix_tag)
            expected_origin, expected_axes = self.frames[old_tag]
            if not close_values(origin, reflect(expected_origin, self.axis, self.offset)) or not close_values(axes, reflected_frame(expected_axes, self.axis)):
                raise RuntimeError("MCS/RCS не соответствует ожидаемому отражению")
        self.lines.append("ПРОЙДЕНО: отражение проверенных объектов, сохранение их идентификаторов,")
        self.lines.append("ссылок выбранной CAM-геометрии, состава операций и переноса MCS/RCS.")
        self.lines.append("Рассчитанные траектории удалены; зеркальные операции не создавались.")
        self.lines.append("\nГРАНИЦЫ ТЕСТА: это не подтверждение корректности будущей обработки.")
        self.lines.append("Подачи, припуски, стратегии и остальные настройки скрипт не редактирует.")
        self.lines.append("Числовые направления/координаты внутри настроек V1 оставляет как есть:")
        self.lines.extend(self.raw_spatial or ["Локальные ненулевые 3D-параметры не найдены."])
        self.lines.append("Их смысл и соответствие зеркальной детали нужно проверить в операциях.")


def preflight_source(session):
    part = session.Parts.Display
    if part is None or session.Parts.Work is None or int(session.Parts.Work.Tag) != int(part.Tag):
        raise RuntimeError("Откройте тестовую деталь и сделайте её рабочей и отображаемой")
    root = part.ComponentAssembly.RootComponent
    if root is not None and list(root.GetChildren()):
        raise RuntimeError("V1 работает без компонентов сборки")
    if not part.IsFullyLoaded:
        raise RuntimeError("Сначала полностью загрузите тестовую деталь")
    source = str(part.FullPath)
    if not os.path.isfile(source) or os.path.splitext(source)[1].lower() != ".prt":
        raise RuntimeError("Сначала сохраните тестовую деталь в обычный файл .prt")
    if part.IsModified:
        raise RuntimeError("В исходнике есть несохранённые изменения. Сохраните его обычной командой NX и запустите скрипт снова. Скрипт сам исходник не сохраняет.")
    return part, source


def execute(session, nx, uf, axis, offset):
    lines = [TITLE, "Плоскость ABS: " + "XYZ"[axis] + " = " + str(offset)]
    source_part, source = preflight_source(session)
    target, source_hash = copy_exclusive(source)
    lines.append("Файл тестовой копии: " + target)
    trial, mark, stage = None, None, "Открытие копии"
    try:
        part, status = session.Parts.OpenBaseDisplay(target)
        try:
            if status.NumberUnloadedParts:
                raise RuntimeError("NX не смог полностью открыть копию")
        finally:
            status.Dispose()
        if int(part.Tag) == int(source_part.Tag) or os.path.normcase(os.path.abspath(str(part.FullPath))) != os.path.normcase(os.path.abspath(target)):
            raise RuntimeError("Не удалось отделить копию от исходной детали")
        session.Parts.SetWork(part)
        if int(session.Parts.Display.Tag) != int(part.Tag):
            raise RuntimeError("Копия не стала отображаемой деталью")
        mark = session.SetUndoMark(nx.Session.MarkVisibility.Visible, TITLE)
        stage = "Инициализация CAM в копии"
        trial = Trial(session, nx, uf, part, axis, offset, lines)
        trial.run()
        if digest(source) != source_hash:
            raise RuntimeError("Исходный файл изменился во время выполнения; результат теста не подтверждён")
        if source_part.IsModified:
            raise RuntimeError("NX выставил исходной детали признак изменения. Исходник не сохранялся; нужно выяснить причину.")
        lines.append("\nИсходный файл на диске: контрольная сумма не изменилась.")
        lines.append("\nРЕЗУЛЬТАТ ОТКРЫТ В NX, НО ЕЩЁ НЕ СОХРАНЁН.")
        lines.append("На диске пока лежит исходная копия PRT. Проверьте тела, обе MCS и геометрию операций;")
        lines.append("после проверки сохраните открытую копию штатной командой NX.")
        lines.append("Затем рассчитайте операции заново. Исходный проект скрипт не сохранял.")
    except Exception as exc:
        lines.append("\nТЕСТ НЕ ПРОЙДЕН. Этап: " + (trial.stage if trial else stage))
        lines.append(type(exc).__name__ + ": " + str(exc))
        if mark is not None:
            try:
                session.UndoToMark(mark, None)
                session.DeleteUndoMark(mark, None)
                lines.append("Изменения теста в копии отменены через Undo.")
            except Exception as rollback:
                lines.append("Откат не выполнен: " + str(rollback) + ". Закройте тестовую копию без сохранения.")
        lines.append("Новая копия на диске не перезаписывалась; исходник не сохранялся.")
        if digest(source) != source_hash:
            lines.append("ВНИМАНИЕ: контрольная сумма исходника изменилась во время теста.")
        lines.append("\n" + traceback.format_exc())
    lines.append("\nНажмите «Копировать всё» и пришлите результат теста.")
    return "\r\n".join(lines)


def configuration():
    """Small in-memory Win32 dialog; no .dlx, temp files or preferences."""
    def word(value):
        return (value + "\0").encode("utf-16le")
    data = bytearray(struct.pack("<LLHhhhh", 0x90C80040, 0, 8, 0, 0, 380, 153))
    data += b"\0\0\0\0" + word(TITLE) + struct.pack("<H", 9) + word("Segoe UI")
    controls = (
        (0x50000000, 10, 9, 360, 31, 110, 0x82, "Тест без компонентов. Создаётся отдельный PRT рядом с исходником.\nГотовые траектории в копии будут удалены для нового расчёта."),
        (0x50000000, 10, 46, 205, 12, 111, 0x82, "Плоскость абсолютной системы координат:"),
        (0x50210003, 220, 43, 150, 75, 101, 0x85, ""),
        (0x50000000, 10, 72, 205, 12, 112, 0x82, "Координата плоскости (в единицах детали):"),
        (0x50810080, 220, 69, 150, 16, 102, 0x81, "0"),
        (0x50000000, 10, 96, 360, 22, 113, 0x82, "MCS: оси X и Z отражаются; Y достраивается для правой системы.\nРезультат сначала остаётся в NX без сохранения."),
        (0x50010001, 174, 126, 96, 18, 1, 0x80, "Создать копию"),
        (0x50010000, 278, 126, 92, 18, 2, 0x80, "Отмена"),
    )
    for style, x, y, width, height, ident, atom, caption in controls:
        data += b"\0" * ((-len(data)) % 4)
        data += struct.pack("<LLhhhhH", style, 0, x, y, width, height, ident)
        data += struct.pack("<HH", 0xffff, atom) + word(caption) + b"\0\0"
    user = ctypes.WinDLL("user32", use_last_error=True)
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    result_type = ctypes.c_ssize_t
    callback_type = ctypes.WINFUNCTYPE(result_type, W.HWND, W.UINT, W.WPARAM, W.LPARAM)
    user.DialogBoxIndirectParamW.argtypes = (W.HINSTANCE, ctypes.c_void_p, W.HWND, callback_type, W.LPARAM)
    user.DialogBoxIndirectParamW.restype = result_type
    user.GetDlgItem.argtypes, user.GetDlgItem.restype = (W.HWND, ctypes.c_int), W.HWND
    user.SendMessageW.argtypes = (W.HWND, W.UINT, W.WPARAM, W.LPARAM)
    user.SendMessageW.restype = result_type
    user.GetDlgItemTextW.argtypes = (W.HWND, ctypes.c_int, W.LPWSTR, ctypes.c_int)
    user.GetDlgItemTextW.restype = W.UINT
    user.EndDialog.argtypes = (W.HWND, result_type)
    user.EndDialog.restype = W.BOOL
    user.MessageBoxW.argtypes = (W.HWND, W.LPCWSTR, W.LPCWSTR, W.UINT)
    user.MessageBoxW.restype = ctypes.c_int
    user.GetActiveWindow.restype = W.HWND
    kernel.GetModuleHandleW.argtypes, kernel.GetModuleHandleW.restype = (W.LPCWSTR,), W.HMODULE
    answer, errors = [], []

    @callback_type
    def procedure(hwnd, message, wparam, lparam):
        try:
            if message == 0x110:
                combo = user.GetDlgItem(hwnd, 101)
                for text in ("YZ: X = координата", "XZ: Y = координата", "XY: Z = координата"):
                    buffer = ctypes.create_unicode_buffer(text)
                    user.SendMessageW(combo, 0x143, 0, ctypes.addressof(buffer))
                user.SendMessageW(combo, 0x14e, 0, 0)
                return 1
            if message == 0x111:
                ident = wparam & 0xffff
                if ident == 1:
                    buffer = ctypes.create_unicode_buffer(100)
                    user.GetDlgItemTextW(hwnd, 102, buffer, len(buffer))
                    try:
                        offset = float(buffer.value.strip().replace(",", "."))
                        if not math.isfinite(offset):
                            raise ValueError()
                    except ValueError:
                        user.MessageBoxW(hwnd, "Введите конечное число, например 0 или 25,5.", TITLE + " — Координата", 0x30)
                        return 1
                    axis = int(user.SendMessageW(user.GetDlgItem(hwnd, 101), 0x147, 0, 0))
                    if axis not in (0, 1, 2):
                        raise RuntimeError("Не выбрана плоскость")
                    answer.append((axis, offset))
                    user.EndDialog(hwnd, 1)
                    return 1
                if ident == 2:
                    user.EndDialog(hwnd, 0)
                    return 1
            if message == 0x10:
                user.EndDialog(hwnd, 0)
                return 1
        except Exception as exc:
            errors.append(exc)
            user.EndDialog(hwnd, -2)
            return 1
        return 0

    template = ctypes.create_string_buffer(bytes(data))
    result = user.DialogBoxIndirectParamW(kernel.GetModuleHandleW(None), template,
                                        user.GetActiveWindow(), procedure, 0)
    if errors:
        raise errors[0]
    if result == -1:
        raise ctypes.WinError(ctypes.get_last_error())
    return answer[0] if answer else None


def _dialog_template():
    # In-memory standard DLGTEMPLATE. No .dlx or temporary resources.
    def word(text):
        return (text + "\0").encode("utf-16le")

    style = 0x90CF0040  # visible popup, caption, resize, system/min/max, font
    data = bytearray(struct.pack("<LLHhhhh", style, 0, 3, 0, 0, 520, 340))
    data += b"\0\0\0\0" + word(TITLE) + struct.pack("<H", 9) + word("Segoe UI")
    for control_style, x, y, w, h, control_id, atom, caption in (
        (0x50B108C4, 8, 8, 504, 300, 101, 0x81, ""),
        (0x50010000, 310, 316, 100, 16, 102, 0x80, "Копировать всё"),
        (0x50010001, 418, 316, 94, 16, 2, 0x80, "Закрыть"),
    ):
        data += b"\0" * ((-len(data)) % 4)
        data += struct.pack("<LLhhhhH", control_style, 0, x, y, w, h, control_id)
        data += struct.pack("<HH", 0xffff, atom) + word(caption) + b"\0\0"
    return bytes(data)


def show_result(text):
    """Resizable native dialog; copying occurs only on the user's button click."""
    user = ctypes.WinDLL("user32", use_last_error=True)
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    result_type = ctypes.c_ssize_t
    callback_type = ctypes.WINFUNCTYPE(result_type, W.HWND, W.UINT, W.WPARAM, W.LPARAM)
    user.DialogBoxIndirectParamW.argtypes = (W.HINSTANCE, ctypes.c_void_p, W.HWND, callback_type, W.LPARAM)
    user.DialogBoxIndirectParamW.restype = result_type
    user.GetDlgItem.argtypes = (W.HWND, ctypes.c_int)
    user.GetDlgItem.restype = W.HWND
    user.SendMessageW.argtypes = (W.HWND, W.UINT, W.WPARAM, W.LPARAM)
    user.SendMessageW.restype = result_type
    user.SetDlgItemTextW.argtypes = (W.HWND, ctypes.c_int, W.LPCWSTR)
    user.SetDlgItemTextW.restype = W.BOOL
    user.EndDialog.argtypes = (W.HWND, result_type)
    user.EndDialog.restype = W.BOOL
    user.MoveWindow.argtypes = (W.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, W.BOOL)
    user.MoveWindow.restype = W.BOOL
    user.GetClientRect.argtypes = (W.HWND, ctypes.POINTER(W.RECT))
    user.GetClientRect.restype = W.BOOL
    user.GetActiveWindow.restype = W.HWND
    kernel.GetModuleHandleW.argtypes = (W.LPCWSTR,)
    kernel.GetModuleHandleW.restype = W.HMODULE
    callback_error = []

    def layout(hwnd):
        rect = W.RECT()
        user.GetClientRect(hwnd, ctypes.byref(rect))
        scale = 1.0
        dpi = getattr(user, "GetDpiForWindow", None)
        if dpi:
            dpi.argtypes, dpi.restype = (W.HWND,), W.UINT
            scale = (dpi(hwnd) or 96) / 96.0
        pad, height, gap = int(12 * scale), int(30 * scale), int(8 * scale)
        width = max(1, rect.right - 2 * pad)
        edit_height = max(1, rect.bottom - 3 * pad - height)
        user.MoveWindow(user.GetDlgItem(hwnd, 101), pad, pad, width, edit_height, True)
        button_width = int(150 * scale)
        y = rect.bottom - pad - height
        user.MoveWindow(user.GetDlgItem(hwnd, 102), max(pad, rect.right - pad - 2 * button_width - gap),
                        y, button_width, height, True)
        user.MoveWindow(user.GetDlgItem(hwnd, 2), max(pad, rect.right - pad - button_width),
                        y, button_width, height, True)

    @callback_type
    def procedure(hwnd, message, wparam, lparam):
        try:
            if message == 0x110:  # WM_INITDIALOG
                edit = user.GetDlgItem(hwnd, 101)
                user.SendMessageW(edit, 0xC5, 0x7ffffffe, 0)  # EM_SETLIMITTEXT
                if not user.SetDlgItemTextW(hwnd, 101, text):
                    raise ctypes.WinError(ctypes.get_last_error())
                layout(hwnd)
                return 1
            if message == 5:  # WM_SIZE
                layout(hwnd)
                return 1
            if message == 0x111:
                control = wparam & 0xffff
                if control == 102:
                    edit = user.GetDlgItem(hwnd, 101)
                    user.SendMessageW(edit, 0xB1, 0, -1)  # EM_SETSEL
                    user.SendMessageW(edit, 0x301, 0, 0)  # WM_COPY
                    return 1
                if control in (1, 2):
                    user.EndDialog(hwnd, 0)
                    return 1
            if message == 0x10:
                user.EndDialog(hwnd, 0)
                return 1
        except Exception as exc:
            callback_error.append(exc)
            user.EndDialog(hwnd, -2)
            return 1
        return 0

    template = ctypes.create_string_buffer(_dialog_template())
    result = user.DialogBoxIndirectParamW(kernel.GetModuleHandleW(None), template,
                                          user.GetActiveWindow(), procedure, 0)
    if callback_error:
        raise callback_error[0]
    if result == -1:
        raise ctypes.WinError(ctypes.get_last_error())



def main():
    previous = sys.dont_write_bytecode
    try:
        sys.dont_write_bytecode = True
        import NXOpen
        import NXOpen.CAM
        import NXOpen.UF
    finally:
        sys.dont_write_bytecode = previous
    try:
        session = NXOpen.Session.GetSession()
        preflight_source(session)
        options = configuration()
        if options is None:
            return
        # Recheck after the dialog; no CAM access or mutations on the source.
        preflight_source(session)
        report = execute(session, NXOpen, NXOpen.UF.UFSession.GetUFSession(), *options)
        show_result(report)
    except Exception:
        show_result(TITLE + "\r\n\r\n" + traceback.format_exc().replace("\n", "\r\n"))


if __name__ == "__main__":
    main()
