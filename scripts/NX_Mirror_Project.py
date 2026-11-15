# -*- coding: utf-8 -*-
# Зеркальная копия CAM-проекта — тест без компонентов
# SCRIPT_VERSION: V1.21
SCRIPT_VERSION = "V1.21"
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
# UF_PARAM_status_t, documented in Siemens uf_param.h.
PARAM_DEFAULT, PARAM_INHERITED, PARAM_OVERRIDDEN, PARAM_INVALID_INDEX = range(4)
PARAM_STATUS_NAMES = ("по умолчанию NX", "наследуется", "собственное значение")
# Only length-valued CAM editor fields use this absolute tolerance, in part
# units. Modes, flags and identifiers must match exactly; no relative tolerance.
CAM_LENGTH_TOLERANCE = 1e-8
# Editor recognition only, not a complete operation-settings mirror adapter.
# See docs/mirror-project.md for provenance and the DC 2606 verification limits.
# Generic dispatch does not authorize turning, additive, probing or user-defined motion.
OPERATION_BUILDERS = {
    'CavityMilling': 'CreateCavityMillingBuilder',
    'FaceMilling': 'CreateFaceMillingBuilder',
    'PlanarMilling': 'CreatePlanarMillingBuilder',
    'Engraving': 'CreateEngravingBuilder',
    'ZLevelMilling': 'CreateZlevelMillingBuilder',
    'VariableZLevelMilling': 'CreateVazlMillingBuilder',
    'FeatureMilling': 'CreateFeatureMillingBuilder',
    'PlungeMilling': 'CreatePlungeMillingBuilder',
    'HoleMaking': 'CreateHoleMakingBuilder',
    'HoleDrilling': 'CreateHoleDrillingBuilder',
    'ThreadMilling': 'CreateThreadMillingBuilder',
    'CylinderMilling': 'CreateCylinderMillingBuilder',
    'VolumeBased25DMillingOperation': 'CreateVolumeBased25dMillingOperationBuilder',
    'GrooveMilling': 'CreateGrooveMillingBuilder',
    'ChamferMilling': 'CreateChamferMillingBuilder',
    'RadialGrooveMilling': 'CreateRadialGrooveMillingBuilder',
    'GenericFeatureOperation': 'CreateGenericFeatureOperationBuilder',
    'SurfaceContour': 'CreateSurfaceContourBuilder',
    'TpMultiAxisDeburring': 'CreateMultiAxisDeburringBuilder',
    'TpMultiAxisWallFinishing': 'CreateMultiAxisWallFinishingBuilder',
    'MwBLDeburringOperation': 'CreateMwBldeburringOperationBuilder',
}
MILLING_HOLE_BUILDERS = frozenset((
    'CavityMillingBuilder', 'ChamferMillingBuilder', 'CylinderMillingBuilder',
    'EdgeChamferBuilder', 'EngravingBuilder', 'FaceMillingBuilder', 'FacingBuilder',
    'FacingZigZagBuilder', 'FeatureMillingBuilder', 'GenericFeatureOperationBuilder',
    'GrooveMillingBuilder', 'HoleDrillingBuilder', 'HoleMakingBuilder',
    'MultiAxisDeburringBuilder', 'MultiAxisRoughingBuilder', 'MultiAxisWallFinishingBuilder',
    'MwBLDeburringOperationBuilder', 'PlanarMillingBuilder', 'PlanarRoughingBuilder',
    'PlungeMillingBuilder', 'PointToPointBuilder', 'RadialGrooveMillingBuilder',
    'RotaryRoughingBuilder', 'SurfaceContourBuilder', 'SwarfBuilder', 'ThreadMillingBuilder',
    'VazlMillingBuilder', 'VolumeBased25DMillingOperationBuilder', 'WallProfilingBuilder',
    'ZLevelMillingBuilder', 'ZlevelUndercutBuilder',
))
FEATURE_DEFINITION_BUILDERS = frozenset((
    'FeatureMillingBuilder', 'GenericFeatureOperationBuilder', 'HoleMakingBuilder',
    'HoleDrillingBuilder', 'ThreadMillingBuilder', 'CylinderMillingBuilder',
))
CAM_GEOMETRY_FIELDS = ('PartGeometry', 'BlankGeometry', 'CheckGeometry',
                       'CutAreaGeometry', 'WallGeometry', 'DriveGeometry', 'TrimGeometry')
CUSTOM_POINT_KINDS = ("Predrill Engage", "Cut Region Start")


def cam_enum_snapshot(value, domain, label):
    # NX Python enum members need not support int(member). Freeze their integer
    # value while the builder is alive, not the Python wrapper's identity.
    number = getattr(value, "value", value)
    if not isinstance(number, int) or isinstance(number, bool):
        raise RuntimeError(label + ": не удалось прочитать код перечисления (" + type(value).__name__ + ")")
    names = []
    for name in dir(domain):
        if name.startswith("_"):
            continue
        member = getattr(domain, name)
        candidate = getattr(member, "value", member)
        if isinstance(candidate, int) and not isinstance(candidate, bool) and candidate == number:
            names.append(name)
    return ("enum", int(number), "/".join(sorted(names)))


def cam_bool_snapshot(value, label):
    if not isinstance(value, bool):
        raise RuntimeError(label + ": ожидалось логическое значение")
    return ("bool", value)


def cam_length_snapshot(value, label):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        raise RuntimeError(label + ": некорректное числовое значение " + repr(value))
    return ("length", float(value))


def cam_values_equal(before, after):
    if before is None or after is None or before[0] != after[0]:
        return False
    if before[0] == "length":
        return (math.isfinite(before[1]) and math.isfinite(after[1])
                and math.isclose(before[1], after[1], rel_tol=0.0, abs_tol=CAM_LENGTH_TOLERANCE))
    return before[1] == after[1]


def cam_value_text(value):
    if value is None:
        return "<отсутствует>"
    if value[0] == "length":
        return format(value[1], ".17g")
    if value[0] == "enum":
        return (value[2] + " " if value[2] else "") + "(код " + str(value[1]) + ")"
    return str(value[1])


def history_feature_label(tag, record):
    if record is None:
        return "элемент " + str(tag)
    return (record["name"] or "Без имени") + " [" + record["type"] + "] (элемент " + str(tag) + ")"


def history_reference_text(values, tags):
    return "; ".join(str(tag) + ": " + values[tag] for tag in sorted(tags)) or "нет"


def model_history_changes(before, after):
    """Compare the same structural invariants as before; descriptions are only
    diagnostic. Reflection can legitimately change coordinate-expression RHS.
    A matching RHS does not make a lost/replaced expression tag acceptable.
    """
    differences = []
    for tag in sorted(set(before) | set(after)):
        old, new = before.get(tag), after.get(tag)
        if old is None or new is None:
            record = new if old is None else old
            differences.append((tag, [
                ("Добавлен: " if old is None else "Удалён: ") + history_feature_label(tag, record),
                "  Родители: " + history_reference_text(record["parents"], record["parents"]),
                "  Выражения: " + history_reference_text(record["expressions"], record["expressions"])]))
            continue
        details = []
        if old["type"] != new["type"]:
            details.append("  Тип: до = " + old["type"] + "; после = " + new["type"])
        for field, caption in (("parents", "Родители"), ("expressions", "Выражения")):
            removed = set(old[field]) - set(new[field])
            added = set(new[field]) - set(old[field])
            if removed or added:
                details.extend([
                    "  " + caption + ": количество " + str(len(old[field])) + " → " + str(len(new[field])),
                    "    Убраны из определения: " + history_reference_text(old[field], removed),
                    "    Добавлены в определение: " + history_reference_text(new[field], added)])
        if details:
            differences.append((tag, ["Изменён: " + history_feature_label(tag, new)] + details))
    return differences


def reflect(values, axis, offset=0.0):
    result = [float(x) for x in values]
    result[axis] = 2.0 * offset - result[axis]
    return result


def cross(a, b):
    return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]


def dot(a, b):
    return sum(x*y for x, y in zip(a, b))


class MirrorPlane:
    def __init__(self, origin, normal):
        self.origin = tuple(float(v) for v in origin)
        vector = tuple(float(v) for v in normal)
        if len(self.origin) != 3 or len(vector) != 3 or not all(math.isfinite(v) for v in self.origin + vector):
            raise ValueError("Некорректная плоскость отражения")
        length = math.sqrt(dot(vector, vector))
        if length < 1e-12:
            raise ValueError("Нормаль плоскости имеет нулевую длину")
        self.normal = tuple(v / length for v in vector)
        seed = [0.0, 0.0, 0.0]
        seed[min(range(3), key=lambda i: abs(self.normal[i]))] = 1.0
        y = cross(seed, self.normal)
        size = math.sqrt(dot(y, y))
        y = [v / size for v in y]
        self.basis = tuple(self.normal) + tuple(y) + tuple(cross(self.normal, y))

    def vector(self, value):
        distance = dot(value, self.normal)
        return [float(value[i]) - 2.0 * distance * self.normal[i] for i in range(3)]

    def point(self, value):
        delta = [float(value[i]) - self.origin[i] for i in range(3)]
        delta = self.vector(delta)
        return [self.origin[i] + delta[i] for i in range(3)]

    def local(self, value):
        delta = [float(value[i]) - self.origin[i] for i in range(3)]
        return [dot(delta, self.basis[i:i+3]) for i in (0, 3, 6)]


def frame_axes(matrix, label):
    if len(matrix) != 9 or not all(math.isfinite(x) for x in matrix):
        raise ValueError("Некорректная матрица " + label)
    x, y, z = list(matrix[:3]), list(matrix[3:6]), list(matrix[6:9])
    if max(abs(dot(v, v)-1.0) for v in (x, y, z)) > 1e-6 or dot(cross(x, y), z) < 0.999999:
        raise ValueError(label + ": требуется ортонормированная правая система")
    return x, y, z


def reflected_frame(matrix, plane):
    """Preserve reflected Y and tool Z; reconstruct X for a right-handed MCS.

    A reflection reverses handedness. X is therefore -reflect(old X), while
    Y/Z follow the geometry without an additional sign reversal. For the
    reported YZ-plane mirror of axis-aligned setups, all axis directions stay
    unchanged and only the origin moves to the reflected location.
    """
    x, y, z = frame_axes(matrix, "MCS")
    y, z = plane.vector(y), plane.vector(z)
    return cross(y, z) + y + z


def reflected_boundary_frame(matrix, plane):
    """Preserve the boundary's 2D coordinates and left/right material side.

    X/Y follow the reflection; Z is reconstructed for a right-handed frame.
    Its Z is -reflect(old Z); MCS separately preserves reflect(its old Z).
    """
    x, y, z = frame_axes(matrix, "плоскости границы")
    x, y = plane.vector(x), plane.vector(y)
    return x + y + cross(x, y)


MATRIX_FIELDS = ("Xx", "Xy", "Xz", "Yx", "Yy", "Yz", "Zx", "Zy", "Zz")


def wcs_values(wcs):
    csys = wcs.CoordinateSystem
    origin, axes = csys.Origin, csys.Orientation.Element
    return [origin.X, origin.Y, origin.Z], [getattr(axes, name) for name in MATRIX_FIELDS]


def set_wcs_values(wcs, nx, origin, axes):
    matrix = nx.Matrix3x3()
    for name, value in zip(MATRIX_FIELDS, axes):
        setattr(matrix, name, value)
    wcs.SetOriginAndMatrix(nx.Point3d(*origin), matrix)
    actual_origin, actual_axes = wcs_values(wcs)
    if not close_values(actual_origin, origin) or not close_values(actual_axes, axes):
        raise RuntimeError("NX не подтвердил положение WCS")


def reflected_box(box, axis, offset):
    result = list(box)
    result[axis] = 2.0*offset - box[axis+3]
    result[axis+3] = 2.0*offset - box[axis]
    return result


def close_values(a, b, tolerance=1e-5):
    return len(a) == len(b) and all(math.isfinite(x) and math.isfinite(y) and
        abs(x-y) <= tolerance + 1e-9*max(abs(x), abs(y)) for x, y in zip(a, b))


def local_box(plane, corner, directions, distances):
    """Convert an exact NX oriented box into the fixed mirror-plane frame."""
    if len(directions) == 9:
        directions = [directions[i:i+3] for i in (0, 3, 6)]
    if len(corner) != 3 or len(directions) != 3 or any(len(row) != 3 for row in directions) or len(distances) != 3:
        raise RuntimeError("Некорректный ориентированный габарит NX")
    corners = []
    for bits in range(8):
        point = [corner[i] + sum(directions[j][i] * distances[j]
                 for j in range(3) if bits & (1 << j)) for i in range(3)]
        corners.append(plane.local(point))
    if not all(math.isfinite(v) for p in corners for v in p):
        raise RuntimeError("Неконечные координаты габарита NX")
    return [min(p[i] for p in corners) for i in range(3)] + [max(p[i] for p in corners) for i in range(3)]


def digest(path):
    h = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(1024*1024), b""):
            h.update(block)
    return h.hexdigest()


def copy_exclusive(source, reserved_paths=()):
    """Only the requested PRT copy is written; existing files are never replaced."""
    stem, extension = os.path.splitext(source)
    folder, name = os.path.split(stem)
    base = re.sub(r"(?:_MIRROR_TEST_[0-9]{3,})+$", "", name, flags=re.IGNORECASE)
    stem = os.path.join(folder, base or name)
    reserved = {os.path.normcase(os.path.abspath(path)) for path in reserved_paths if path}
    with open(source, "rb") as inp:
        before = os.fstat(inp.fileno())
        for number in range(1, 10000):
            target = stem + "_MIRROR_TEST_" + str(number).zfill(3) + extension
            if os.path.normcase(os.path.abspath(target)) in reserved:
                continue
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


class NativeApiError(RuntimeError):
    def __init__(self, code, context, message):
        self.code, self.context = int(code), context
        super().__init__(context + ": " + str(code) + " — " + message)


class _BoundaryData(ctypes.Structure):
    """UF_CAMBND_boundary_data_t from uf_cambnd.h, native C alignment.

    ask_boundary_data writes into caller-owned storage and excludes app_data.
    That pointer is not dereferenced or freed by this reader.
    """
    _fields_ = [
        ("boundary_type", ctypes.c_int),
        ("plane_type", ctypes.c_int),
        ("origin", ctypes.c_double * 3),
        ("matrix", ctypes.c_double * 9),
        ("material_side", ctypes.c_int),
        ("ignore_holes", ctypes.c_int),
        ("ignore_islands", ctypes.c_int),
        ("ignore_chamfers", ctypes.c_int),
        ("app_data", ctypes.c_void_p),
    ]


class _CustomPointData(ctypes.Structure):
    # uf_camgeom.h: point_acs uses ABS coordinates in part units. The returned
    # list contains separately allocated record pointers, not inline records.
    _fields_ = [("point_tag", ctypes.c_uint32),
                ("point_acs", ctypes.c_double * 3),
                ("upper_depth", ctypes.c_double),
                ("lower_depth", ctypes.c_double),
                ("depth", ctypes.c_double)]


class NativeCam:
    """Documented UF C calls, including methods absent from Python UF.Modl.

    Signatures: Siemens uf_cam.h, uf_camgeom.h, uf_cambnd.h, uf.h,
    uf_modl.h, uf_modl_utilities.h, uf_trns.h and uf_param.h.
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
        self.provider = self.bind("UF_CAMGEOM_ask_geom_provider", (u, i, ctypes.POINTER(u)))
        self.item_entity = self.bind("UF_CAMGEOM_ask_item_entity", (p, ctypes.POINTER(u)))
        self.collector = self.bind("UF_CAMGEOM_ask_collector_items", (p, ip, pp))
        self.boundaries = self.bind("UF_CAMBND_ask_boundaries", (u, i, ip, pp))
        self.boundary_data = self.bind("UF_CAMBND_ask_boundary_data", (p, ctypes.POINTER(_BoundaryData)))
        self.members = self.bind("UF_CAMBND_ask_boundary_items", (p, ip, pp))
        self.member_entity = self.bind("UF_CAMBND_ask_item_entity", (p, ctypes.POINTER(u)))
        self.custom = self.bind("UF_CAMGEOM_ask_custom_points", (u, i, ip, pp))
        self.param_status = self.bind("UF_PARAM_ask_param_status", (u, i, ip))
        self.param_definer = self.bind("UF_PARAM_ask_param_definer", (u, i, ctypes.POINTER(u)))
        # uf_defs.h: logical is bool / unsigned char, NOT a C int.
        self.so_test = self.bind("UF_SO_is_so", (u, ctypes.POINTER(ctypes.c_ubyte)))
        self.so_parents = self.bind("UF_SO_ask_parents", (u, i, ip, pp))
        self.so_error = self.bind("UF_SO_ask_update_error_code", (u, ip))
        dp = ctypes.POINTER(ctypes.c_double)
        self.xform_point = self.bind("UF_SO_set_point_of_xform", (u, dp))
        self.xform_axes = self.bind("UF_SO_set_xy_direction_of_xform", (u, dp, dp))
        self.set_boundary = self.bind("UF_CAMBND_set_boundary_plane", (p, dp, dp))
        self.mirror_matrix = self.bind("UF_MTX4_mirror", (dp, dp, dp))
        self.map_point = self.bind("UF_MTX4_vec3_multiply", (dp, dp, dp), None)
        self.face_data = self.bind("UF_MODL_ask_face_data", (u, ip, dp, dp, dp, dp, dp, ip))
        # double directions[3][3] is a pointer to rows of three doubles.
        self.exact_box = self.bind("UF_MODL_ask_bounding_box_exact",
                                   (u, u, dp, ctypes.POINTER(ctypes.c_double * 3), dp))
        self.transform = self.bind("UF_MODL_transform_entities", (i, ctypes.POINTER(u), dp))
        self.model_update = self.bind("UF_MODL_update", ())

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
            raise NativeApiError(code, context, message)

    def face_plane(self, tag):
        face_type, sense = ctypes.c_int(), ctypes.c_int()
        point, normal = (ctypes.c_double * 3)(), (ctypes.c_double * 3)()
        box = (ctypes.c_double * 6)()
        radius, radial = ctypes.c_double(), ctypes.c_double()
        self.check(self.face_data(int(tag), ctypes.byref(face_type), point, normal,
                                 box, ctypes.byref(radius), ctypes.byref(radial),
                                 ctypes.byref(sense)), "UF_MODL_ask_face_data")
        if face_type.value != 22:
            raise ValueError("Нужна плоская грань, а не цилиндрическая или криволинейная поверхность")
        # Reflection is independent of the normal's sign / face sense.
        return MirrorPlane(point, normal)

    def smart_parents(self, tag):
        """None means non-parametric; () is a parameterized object with no parents."""
        smart = ctypes.c_ubyte(255)
        self.check(self.so_test(int(tag), ctypes.byref(smart)), "UF_SO_is_so")
        if smart.value not in (0, 1):
            raise RuntimeError("Некорректный ответ UF_SO_is_so")
        if not smart.value:
            return None
        error = ctypes.c_int(-1)
        self.check(self.so_error(int(tag), ctypes.byref(error)), "UF_SO_ask_update_error_code")
        if error.value:
            raise RuntimeError("Ошибка обновления ассоциативного объекта " + str(tag) + ": " + str(error.value))
        count, data = ctypes.c_int(), ctypes.c_void_p()
        try:
            # UF_SO_ASK_ALL_PARENTS = 1 << 3. Immediate parents, not a flattened
            # recursive list: preserve the three-plane construction graph.
            self.check(self.so_parents(int(tag), 8, ctypes.byref(count), ctypes.byref(data)), "UF_SO_ask_parents")
            if not 0 <= count.value <= MAX_OBJECTS or (count.value and not data.value):
                raise RuntimeError("Некорректный массив родителей ассоциативного объекта")
            values = ctypes.cast(data, ctypes.POINTER(ctypes.c_uint32))
            result = tuple(int(values[i]) for i in range(count.value))
            if any(not item for item in result):
                raise RuntimeError("Пустой родитель ассоциативного объекта")
            return result
        finally:
            if data.value:
                self.free(data)

    def bounding_box_exact(self, tag, csys):
        corner, distances = (ctypes.c_double * 3)(), (ctypes.c_double * 3)()
        directions = ((ctypes.c_double * 3) * 3)()
        self.check(self.exact_box(int(tag), int(csys), corner, directions, distances),
                   "UF_MODL_ask_bounding_box_exact")
        return list(corner), [list(row) for row in directions], list(distances)

    def set_fixed_xform(self, tag, origin, axes):
        # uf_so.h setters accept only a dumb transform. A smart CSYS wrapper
        # referencing it remains smart: neither object is replaced or detached.
        if not 0 < int(tag) <= 0xffffffff:
            raise RuntimeError("Некорректный идентификатор Xform")
        if self.smart_parents(tag) is not None:
            raise RuntimeError("Числовое редактирование ассоциативного Xform запрещено")
        if len(origin) != 3 or not all(math.isfinite(v) for v in origin):
            raise RuntimeError("Некорректное начало Xform")
        x, y, _ = frame_axes(axes, "Xform")
        values = ctypes.c_double * 3
        self.check(self.xform_point(int(tag), values(*origin)), "UF_SO_set_point_of_xform")
        self.check(self.xform_axes(int(tag), values(*x), values(*y)), "UF_SO_set_xy_direction_of_xform")

    def transform_entities(self, tags, matrix):
        tags = [int(tag) for tag in tags]
        if not 0 < len(tags) <= MAX_OBJECTS or any(not 0 < tag <= 0xffffffff for tag in tags):
            raise ValueError("Некорректный список объектов для отражения")
        if len(matrix) != 16 or not all(math.isfinite(v) for v in matrix):
            raise ValueError("Некорректная матрица отражения")
        entities = (ctypes.c_uint32 * len(tags))(*tags)
        values = (ctypes.c_double * 16)(*matrix)
        self.check(self.transform(len(tags), entities, values), "UF_MODL_transform_entities")

    def update_model(self):
        self.check(self.model_update(), "UF_MODL_update")

    def parameter_status(self, tag, index):
        # Python returns ParamStatusMemberType, which need not support int().
        # Read the documented C enum into caller-owned int storage instead.
        state = ctypes.c_int(-1)
        self.check(self.param_status(int(tag), index, ctypes.byref(state)), "UF_PARAM_ask_param_status")
        if state.value == PARAM_INVALID_INDEX:
            raise RuntimeError("Параметр " + str(index) + " недопустим для объекта " + str(tag))
        if state.value not in (PARAM_DEFAULT, PARAM_INHERITED, PARAM_OVERRIDDEN):
            raise RuntimeError("Неизвестный статус параметра " + str(index) +
                               " объекта " + str(tag) + ": " + str(state.value))
        return state.value

    def parameter_definer(self, tag, index):
        source = ctypes.c_uint32()
        self.check(self.param_definer(int(tag), index, ctypes.byref(source)), "UF_PARAM_ask_param_definer")
        return source.value

    def read_boundary(self, handle):
        # Keep the opaque handle pointer-sized from ask_boundaries to this call;
        # do not pass a C pointer through the Python Cambnd wrapper.
        if not handle:
            raise RuntimeError("NX вернул пустой дескриптор границы")
        data = _BoundaryData()
        self.check(self.boundary_data(handle, ctypes.byref(data)), "UF_CAMBND_ask_boundary_data")
        if data.plane_type not in (0, 1):
            raise RuntimeError("UF_CAMBND_ask_boundary_data: неизвестный тип плоскости " + str(data.plane_type))
        return data

    def boundary_plane_type(self, handle):
        return int(self.read_boundary(handle).plane_type)

    def set_boundary_plane(self, handle, origin, matrix):
        if not handle or len(origin) != 3 or not all(math.isfinite(v) for v in origin):
            raise ValueError("Некорректная плоскость границы")
        frame_axes(matrix, "плоскости границы")
        self.check(self.set_boundary(handle, (ctypes.c_double * 3)(*origin),
                                     (ctypes.c_double * 9)(*matrix)), "UF_CAMBND_set_boundary_plane")

    def geometry_provider(self, tag, kind):
        provider = ctypes.c_uint32()
        self.check(self.provider(tag, kind, ctypes.byref(provider)), "UF_CAMGEOM_ask_geom_provider")
        return int(provider.value)

    def channel(self, function, node_tag, kind):
        """Wrong-type means unavailable, never an empty validated selection.

        Query the geometry provider when a container cannot own this channel.
        All other errors remain fatal; the caller records unavailable coverage.
        """
        try:
            return ("read", node_tag, self.pointers(function, node_tag, kind))
        except NativeApiError as exc:
            if exc.code != 1345005:
                raise
        try:
            provider = self.geometry_provider(node_tag, kind)
        except NativeApiError as exc:
            if exc.code != 1345005:
                raise
            return ("unavailable", 0, [])
        if provider and provider != node_tag:
            try:
                return ("provider", provider, self.pointers(function, provider, kind))
            except NativeApiError as exc:
                if exc.code != 1345005:
                    raise
        return ("unavailable", provider, [])

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
            raise RuntimeError("Найдена неассоциативная граница без ссылки на геометрию. Тестовая версия её пока не переносит.")
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

    def read_custom_points(self, tag, kind):
        count, data = ctypes.c_int(), ctypes.c_void_p()
        try:
            self.check(self.custom(tag, kind, ctypes.byref(count), ctypes.byref(data)), "UF_CAMGEOM_ask_custom_points")
            if count.value < 0 or count.value > MAX_OBJECTS or (count.value and not data.value):
                raise RuntimeError("Некорректный массив пользовательских точек")
            entries = ctypes.cast(data, ctypes.POINTER(ctypes.c_void_p))
            result, seen = [], set()
            for n in range(count.value):
                address = entries[n]
                if not address or address in seen:
                    raise RuntimeError("Пустая или повторная запись пользовательской точки: " + str(n + 1))
                seen.add(address)
                record = ctypes.cast(address, ctypes.POINTER(_CustomPointData)).contents
                point = tuple(record.point_acs)
                depths = (record.upper_depth, record.lower_depth, record.depth)
                if not all(math.isfinite(v) for v in point + depths):
                    raise RuntimeError("Некорректные координаты/глубины пользовательской точки: " + str(n + 1))
                result.append((int(record.point_tag), point, depths))
            return tuple(result)
        finally:
            if data.value:
                # Unlike ask_items, this API allocates every point record too.
                if 0 <= count.value <= MAX_OBJECTS:
                    entries = ctypes.cast(data, ctypes.POINTER(ctypes.c_void_p))
                    freed = set()
                    for n in range(count.value):
                        if entries[n] and entries[n] not in freed:
                            freed.add(entries[n])
                            self.free(ctypes.c_void_p(entries[n]))
                self.free(data)

    def replace_custom_points(self, tag, kind, points):
        # Resolve BOTH entry points and validate the entire list before deleting
        # anything. A failed append propagates to the copy's existing Undo path.
        if kind not in (0, 1) or not points or len(points) > MAX_OBJECTS:
            raise RuntimeError("Некорректный список для переноса пользовательских точек")
        records = []
        for point_tag, point, depths in points:
            if (not isinstance(point_tag, int) or not 0 <= point_tag <= 0xffffffff or
                    len(point) != 3 or len(depths) != 3 or
                    not all(math.isfinite(v) for v in tuple(point) + tuple(depths))):
                raise RuntimeError("Некорректная запись для переноса пользовательской точки")
            records.append(_CustomPointData(point_tag, (ctypes.c_double * 3)(*point), *depths))
        pointers = (ctypes.POINTER(_CustomPointData) * len(records))(*(ctypes.pointer(r) for r in records))
        u, i = ctypes.c_uint32, ctypes.c_int
        delete = self.bind("UF_CAMGEOM_delete_custom_points", (u, i))
        append = self.bind("UF_CAMGEOM_append_custom_points", (u, i, i, ctypes.POINTER(ctypes.POINTER(_CustomPointData))))
        self.check(delete(tag, kind), "UF_CAMGEOM_delete_custom_points")
        self.check(append(tag, kind, len(records), pointers), "UF_CAMGEOM_append_custom_points")

    def reflection_matrix(self, plane):
        origin = (ctypes.c_double * 3)(*plane.origin)
        normal = (ctypes.c_double * 3)(*plane.normal)
        matrix = (ctypes.c_double * 16)()
        self.check(self.mirror_matrix(origin, normal, matrix), "UF_MTX4_mirror")
        # Verify using NX's own point/matrix convention before touching geometry.
        for point in ([0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]):
            result = (ctypes.c_double * 3)()
            self.map_point((ctypes.c_double * 3)(*point), matrix, result)
            if not close_values(result, plane.point(point), 1e-9):
                raise RuntimeError("Матрица NX не прошла проверку отражения контрольных точек")
        return list(matrix)


def xyz(value):
    return [float(value.X), float(value.Y), float(value.Z)]


def graph_branch(graph, root):
    result, pending = {}, [root]
    while pending:
        tag = pending.pop()
        if tag in result:
            continue
        result[tag] = graph[tag]
        pending.extend(graph[tag] or ())
    return result


def parent_order(graph):
    """Order a captured dependency graph without recursive NX Evaluate calls."""
    pending, result = set(graph), []
    while pending:
        ready = sorted(tag for tag in pending if not (set(graph[tag] or ()) & pending))
        if not ready:
            raise RuntimeError("Цикл зависимостей: " + str(sorted(pending)))
        for tag in ready:
            if any(parent not in graph for parent in (graph[tag] or ())):
                raise RuntimeError("Неполный граф зависимостей: " + str(tag))
        result.extend(ready)
        pending.difference_update(ready)
    return result


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
    # The collection is authoritative for operation coverage. A detached or
    # differently represented operation must not escape editor/UF checks just
    # because it was absent from the traversed geometry branch.
    for operation in setup.CAMOperationCollection:
        if int(operation.Tag) not in seen:
            nodes.append(operation)
            seen.add(int(operation.Tag))
            if len(nodes) > MAX_OBJECTS:
                raise RuntimeError("Слишком большое дерево CAM для тестовой версии")
    return root, nodes


class Trial:
    def __init__(self, session, nx, uf, part, plane, lines, native=None):
        self.session, self.nx, self.uf, self.part = session, nx, uf, part
        self.plane, self.lines = plane, lines
        self.stage = "Подготовка копии"
        self.native = native if native is not None else NativeCam()
        self.bodies = list(part.Bodies)
        self.setup = part.CAMSetup
        self.operations = list(self.setup.CAMOperationCollection)
        self.root, self.nodes = cam_nodes(self.setup, nx)
        self.refs, self.bounds, self.frames, self.mcs_bindings = {}, {}, {}, []
        self.raw_spatial = []
        self.unavailable = set()
        self.box_csys = None
        self.mcs_expected = {}
        self.mcs_sources = {}
        self.mcs_links = {}
        self.mcs_definitions, self.mcs_verified_definitions = {}, {}
        self.mcs_fixed_xforms = {}
        self.mcs_rebuilt = {}
        self.mcs_supports = {}
        self.mcs_plane_selections = {}
        self.stock_features, self.protected_bodies = {}, set()
        self.cam_builder_state, self.cam_builder_labels = None, {}
        self.history_baseline, self.history_previous = None, None
        self.body_history, self.body_conversions = {}, []
        self.boundary_planes, self.current_boundaries = {}, {}
        self.custom_points, self.current_custom_points, self.custom_point_labels = {}, {}, {}
        self.moved_boundary_count = 0
        if not self.bodies or not self.operations:
            raise RuntimeError("В копии должны быть тела и CAM-операции")
        if any(op.IsMirror() for op in self.operations):
            raise RuntimeError("В этой версии нужны обычные операции, без существующих зеркальных операций")
        # Inspect every feature, regardless of its FeatureType label. Permission
        # to replace history is established later from its NX class, body
        # ownership and dependencies; it does not cover arbitrary features.
        for feature in part.Features:
            for parent in feature.GetParents():
                owner = parent.OwningPart
                if owner is not None and int(owner.Tag) != int(part.Tag):
                    raise RuntimeError("Модель содержит внешнего родителя у " +
                        history_feature_label(int(feature.Tag), {"name": str(feature.Name),
                            "type": str(feature.FeatureType)}) + ". Тест остановлен до преобразования.")

    def reference(self, tag):
        tag = int(tag)
        if not tag or tag in self.refs:
            return
        obj = self.nx.TaggedObjectManager.GetTaggedObject(tag)
        owner = getattr(obj, "OwningPart", None)
        if owner is not None and int(owner.Tag) != int(self.part.Tag):
            raise RuntimeError("Обнаружена ссылка на другую деталь. Тестовая версия работает только с одной деталью.")
        if bool(getattr(obj, "IsOccurrence", False)):
            raise RuntimeError("Обнаружено вхождение компонента")
        self.refs[tag] = obj

    def smart_graph(self, tag):
        result, pending = {}, [int(tag)]
        while pending:
            current = pending.pop()
            if current in result:
                continue
            if len(result) >= MAX_OBJECTS:
                raise RuntimeError("Слишком много родителей системы координат")
            self.reference(current)
            parents = self.native.smart_parents(current)
            result[current] = parents
            if parents is not None:
                pending.extend(parents)
        return result

    def plane_data(self, obj):
        if isinstance(obj, self.nx.Face):
            return self.native.face_plane(obj.Tag)
        if isinstance(obj, (self.nx.Plane, self.nx.DatumPlane)):
            return MirrorPlane(xyz(obj.Origin), xyz(obj.Normal))
        raise RuntimeError("Не поддержана опорная плоскость MCS: " + type(obj).__name__)

    def capture_csys_definition(self, tag, origin, axes):
        graph = self.smart_graph(tag)
        planes = None
        # Recognize the actual NX Three Planes graph, not a display name or a
        # flattened set of arbitrary ancestors. Reject ambiguous constructions.
        parents = graph[tag]
        if parents is not None:
            xforms = [self.refs[p] for p in parents if isinstance(self.refs[p], self.nx.Xform)]
            if len(xforms) == 1 and len(parents) == 1:
                xform = xforms[0]
                xform_tag = int(xform.Tag)
                inputs = graph[xform_tag]
                if inputs is None and close_values([float(xform.Scale)], [1.0]):
                    # UF_SO_is_so refers to the CSYS itself, not its transform.
                    # A CSYS -> fixed Xform chain has a numerical definition,
                    # even though UF calls the CSYS wrapper parameterized.
                    xform_axes = [float(getattr(xform.Orientation, field)) for field in MATRIX_FIELDS]
                    if close_values(xyz(xform.Origin), origin) and close_values(xform_axes, axes):
                        self.mcs_fixed_xforms[tag] = xform_tag
                if inputs is not None:
                    planar = [self.refs[p] for p in inputs
                              if isinstance(self.refs[p], (self.nx.Face, self.nx.Plane, self.nx.DatumPlane))]
                    others = [self.refs[p] for p in inputs if self.refs[p] not in planar]
                    if (len(planar) == 3 and all(isinstance(o, self.nx.Scalar) for o in others)
                            and close_values([float(xforms[0].Scale)], [1.0])):
                        data = [(obj, self.plane_data(obj)) for obj in planar]
                        ordered = []
                        for axis in (axes[:3], axes[3:6], axes[6:]):
                            matching = [obj for obj, plane in data
                                        if abs(dot(plane.normal, axis)) > 1.0 - 1e-7
                                        and abs(dot(plane.normal, [a-b for a,b in zip(origin, plane.origin)])) < 1e-5]
                            if len(matching) != 1:
                                break
                            ordered.append(int(matching[0].Tag))
                        if len(set(ordered)) == 3:
                            planes = tuple(ordered)
                            for obj, plane in data:
                                self.mcs_supports[int(obj.Tag)] = plane
                                if isinstance(obj, self.nx.Plane):
                                    self.mcs_plane_selections[int(obj.Tag)] = self.plane_selection(obj)
        self.mcs_definitions[tag] = (graph, planes)
        self.mcs_verified_definitions[tag] = graph
        fixed = self.mcs_fixed_xforms.get(tag)
        description = ("Three Planes" if planes else "CSYS с числовым Xform " + str(fixed) if fixed
                       else "ассоциативная СК: способ задания не распознан" if parents is not None else "числовая СК")
        self.lines.append("Определение СК " + str(tag) + ": " + description +
                          ("; плоскости " + str(planes) if planes else ""))
        uses = [(self.mcs_sources[key], key) for key, value in self.mcs_expected.items() if value == tag]
        inherited = sum(state == PARAM_INHERITED for (state, _, _), _ in uses)
        for (state, definer, label), key in uses:
            if state != PARAM_INHERITED:
                self.lines.append("  Источник: " + label + "; узел " + str(key[0]) +
                                  "; статус " + PARAM_STATUS_NAMES[state] + "; источник параметра " + str(definer))
        self.lines.append("  Наследующих параметров: " + str(inherited) + "; начало ABS: " + str(origin))
        self.lines.append("  Исходные оси ABS: X=" + str(axes[:3]) + "; Y=" + str(axes[3:6]) + "; Z=" + str(axes[6:]))
        if parents is not None and not planes and not fixed:
            self.lines.extend(self.csys_definition_details(tag, graph))

    def csys_definition_details(self, tag, graph):
        lines = ["  Структура определения СК " + str(tag) + ":"]
        for number, (obj_tag, parents) in enumerate(graph.items()):
            if number >= 32:
                lines.append("    Показаны первые 32 объекта из " + str(len(graph)))
                break
            label = type(self.refs[obj_tag]).__name__ + " " + str(obj_tag)
            lines.append("    " + label + (": числовой объект" if parents is None else ": родители " + str(parents)))
        return lines

    def csys_frame(self, tag):
        matrix, origin = self.uf.Csys.AskCsysInfo(tag)
        return list(origin), list(self.uf.Csys.AskMatrixValues(matrix))

    def plane_selection(self, plane):
        # A transitive UF parent path to a face does not prove that the native
        # plane editor still selects that face. Freeze its DIRECT selection and
        # construction method separately, before any geometry is transformed.
        method = cam_enum_snapshot(plane.Method, self.nx.PlaneTypes.MethodType,
                                   "Способ задания плоскости " + str(plane.Tag))
        return (method, tuple(int(obj.Tag) for obj in plane.GetGeometry()))

    def require_plane_selection(self, plane, expected, context):
        actual = self.plane_selection(plane)
        if actual != expected:
            raise RuntimeError("Изменился прямой выбор Three Planes / " + context +
                "\nСпособ ожидался: " + cam_value_text(expected[0]) +
                "; фактически: " + cam_value_text(actual[0]) +
                "\nВыбранные объекты ожидались: " + str(expected[1]) +
                "; фактически: " + str(actual[1]))

    def require_source_plane_selection(self, source):
        if source in self.mcs_plane_selections:
            self.require_plane_selection(self.refs[source], self.mcs_plane_selections[source],
                                         "исходная опора " + str(source))

    def verify_three_plane_selections(self, tag):
        _, sources = self.mcs_definitions[tag]
        if sources is None:
            return
        data = self.mcs_rebuilt.get(tag)
        # After ReplaceParameters, NX may delete the old, now unreferenced
        # Plane objects during update. Only the installed CSYS graph is live;
        # the original selection/support snapshots remain immutable evidence.
        graph = self.smart_graph(tag)
        if graph != self.mcs_verified_definitions[tag]:
            raise RuntimeError("Изменилось определение Three Planes: " + str(tag) +
                               "; переход на Dynamic не разрешён")
        active_tags = data["plane_tags"] if data is not None else sources
        active = []
        for axis, source, current in zip("XYZ", sources, active_tags):
            obj = self.current_mcs_object(current, (self.nx.Plane, self.nx.Face, self.nx.DatumPlane),
                                          "СК " + str(tag) + " / " + axis)
            expected = (data["selections"][len(active)] if data is not None
                        else self.mcs_plane_selections.get(source))
            if expected is not None:
                if not isinstance(obj, self.nx.Plane):
                    raise RuntimeError("Изменился тип действующей плоскости Three Planes: " + str(current))
                context = ("СК " + str(tag) + " / " + axis if data is not None
                           else "исходная опора " + str(source))
                self.require_plane_selection(obj, expected, context)
            self.verify_support_plane(source, obj)
            active.append(obj)
        if data is not None:
            data["planes"] = tuple(active)
            data["xform"] = self.current_mcs_object(data["xform_tag"], (self.nx.Xform,), "СК " + str(tag))
        self.current_mcs_object(tag, (self.nx.CartesianCoordinateSystem,), "Three Planes")

    def current_mcs_object(self, tag, types, context):
        try:
            obj = self.nx.TaggedObjectManager.GetTaggedObject(int(tag))
            if (not isinstance(obj, types) or int(obj.Tag) != int(tag)
                    or obj.OwningPart is None or int(obj.OwningPart.Tag) != int(self.part.Tag)
                    or bool(getattr(obj, "IsOccurrence", False))):
                raise RuntimeError("Неверный тип, идентификатор или владелец объекта")
        except Exception as exc:
            raise RuntimeError("Недоступен действующий объект Three Planes " + str(tag) +
                               " / " + context + ": " + str(exc)) from exc
        self.refs[int(tag)] = obj
        return obj

    def verify_support_plane(self, source, current=None):
        old = self.mcs_supports[source]
        expected = MirrorPlane(self.plane.point(old.origin), self.plane.vector(old.normal))
        obj = self.refs[source] if current is None else current
        actual = self.plane_data(obj)
        distance = dot(expected.normal, [a-b for a,b in zip(actual.origin, expected.origin)])
        if abs(dot(expected.normal, actual.normal)) < 1.0 - 1e-7 or abs(distance) > 1e-5:
            details = ["Опорная плоскость MCS ещё не соответствует отражённой геометрии: " + str(source),
                "Исходная точка: " + str(list(old.origin)),
                "Ожидаемая точка: " + str(list(expected.origin)) + "; нормаль: " + str(list(expected.normal)),
                "Фактическая точка: " + str(list(actual.origin)) + "; нормаль: " + str(list(actual.normal)),
                "Расстояние до ожидаемой плоскости: " + str(distance)]
            details.append("Проверяемая действующая опора: " + str(obj.Tag))
            details.extend(self.csys_definition_details(int(obj.Tag), self.smart_graph(int(obj.Tag))))
            raise RuntimeError("\n".join(details))

    def refresh_mcs_supports(self, tag, graph, planes):
        self.stage = "Обновление опорных плоскостей MCS: " + str(tag)
        branches = {}
        for source in planes:
            self.require_source_plane_selection(source)
            branch = graph_branch(graph, source)
            if self.smart_graph(source) != branch:
                raise RuntimeError("Повреждено определение опорной плоскости MCS " + str(source))
            branches.update(branch)
        # Original support planes can themselves be smart objects. Evaluating
        # just the new coincident wrappers leaves those original parents stale.
        for source in parent_order(branches):
            obj = self.refs[source]
            evaluate = getattr(obj, "Evaluate", None)
            if branches[source] is not None and callable(evaluate):
                evaluate()
        for source in planes:
            self.require_source_plane_selection(source)
            if self.smart_graph(source) != graph_branch(graph, source):
                raise RuntimeError("Изменилось определение опоры при обновлении MCS: " + str(source))
            self.stage = "Проверка положения опоры MCS " + str(tag) + " / " + str(source)
            self.verify_support_plane(source)
        self.lines.append("MCS " + str(tag) + ": проверено отражение трёх опор до построения СК.")

    def require_csys_frame(self, tag, origin, axes, context):
        self.stage = "Проверка Three Planes: " + str(tag) + " / " + context
        actual_origin, actual_axes = self.csys_frame(tag)
        if not (close_values(actual_origin, origin) and close_values(actual_axes, axes)):
            raise RuntimeError("Не подтверждена ориентация Three Planes " + str(tag) + " / " + context +
                "\nНачало ожидаемое: " + str(list(origin)) + "; фактическое: " + str(actual_origin) +
                "\nОси ожидаемые: " + str(list(axes)) + "; фактические: " + str(actual_axes))

    def evaluate_three_planes(self, csys, data):
        # SmartObject.Evaluate is not recursive: explicitly evaluate parents
        # before their Xform, and the Xform before the CSYS which consumes it.
        for plane, source, selection in zip(data["planes"], data["sources"], data["selections"]):
            plane.Evaluate()
            self.verify_support_plane(source, plane)
            self.require_plane_selection(plane, selection, "опора " + str(source))
        data["xform"].Evaluate()
        csys.Evaluate()
        for source, graph in data["selection_graphs"].items():
            if self.smart_graph(source) != graph:
                raise RuntimeError("Изменилось определение исходной опоры Three Planes: " + str(source))

    def orient_three_planes(self, csys, data, context):
        # Plane.Normal alone is not proof of the resulting CSYS orientation.
        # Try only the eight normal-sign combinations of the SAME three linked
        # planes, never a numerical CSYS or a different support/order.
        initial = tuple(bool(p.Flip) for p in data["planes"])
        attempts = []
        for mask in range(8):
            self.stage = "Ориентация Three Planes: " + str(csys.Tag) + " / " + context
            flips = tuple(value ^ bool(mask & (1 << i)) for i,value in enumerate(initial))
            for plane, flip in zip(data["planes"], flips):
                if bool(plane.Flip) != flip:
                    plane.SetFlip(flip)
            self.evaluate_three_planes(csys, data)
            origin, axes = self.csys_frame(csys.Tag)
            attempts.append("  Flip=" + str(flips) + "; начало=" + str(origin) + "; оси=" + str(axes))
            if not close_values(origin, data["origin"]):
                raise RuntimeError("Начало кандидата Three Planes не совпадает с пересечением отражённых опор.\n" +
                    "Ожидалось: " + str(list(data["origin"])) + "\n" + attempts[-1])
            if close_values(axes, data["axes"]):
                self.lines.append("Three Planes: ориентация проверена / " + context +
                    "; вариантов: " + str(mask+1) + "; Flip=" + str(flips) + "; оси=" + str(axes))
                return
        raise RuntimeError("Не удалось получить ожидаемые оси Three Planes без изменения опор.\n" +
            "Ожидалось: " + str(list(data["axes"])) + "\n" + "\n".join(attempts))

    def rebuild_three_planes(self, tag, planes, expected_origin, expected_axes):
        nx = self.nx
        oriented, selections = [], []
        for index, source in enumerate(planes):
            obj = self.refs[source]
            direction = expected_axes[index*3:index*3+3]
            if isinstance(obj, nx.Plane):
                # Copy the native definition, not Coincident([oldPlane]). The
                # extra wrapper in V1.14 retained a transitive face dependency
                # but changed what the Three Planes editor directly selected.
                self.require_source_plane_selection(source)
                selection = self.mcs_plane_selections[source]
                plane = obj.CopyPlane()
                if (plane is None or not isinstance(plane, nx.Plane)
                        or not int(plane.Tag) or int(plane.Tag) in self.refs
                        or plane.OwningPart is None or int(plane.OwningPart.Tag) != int(self.part.Tag)):
                    raise RuntimeError("NX не создал отдельную копию определения плоскости " + str(source))
            else:
                # A directly selected Face/DatumPlane has no Plane definition
                # to copy. One coincident Plane retains that direct selection.
                plane = self.part.Planes.CreatePlane(nx.Point3d(*expected_origin),
                    nx.Vector3d(*direction), nx.SmartObject.UpdateOption.AfterModeling)
                plane.SetMethod(nx.PlaneTypes.MethodType.Coincident)
                plane.SetGeometry([obj])
                method = cam_enum_snapshot(nx.PlaneTypes.MethodType.Coincident,
                    nx.PlaneTypes.MethodType, "Three Planes / совпадающая плоскость")
                selection = (method, (source,))
            self.require_plane_selection(plane, selection, "создание опоры " + str(source))
            plane.Evaluate()
            if dot(xyz(plane.Normal), direction) < 0.0:
                plane.SetFlip(not plane.Flip)
                plane.Evaluate()
            if not close_values(xyz(plane.Normal), direction):
                raise RuntimeError("Не подтверждено направление плоскости Three Planes: " + str(source))
            self.require_plane_selection(plane, selection, "ориентация опоры " + str(source))
            plane.SetVisibility(nx.SmartObject.VisibilityOption.Invisible)
            oriented.append(plane)
            selections.append(selection)
        xform = self.part.Xforms.CreateXform(oriented[0], oriented[1], oriented[2],
                                            nx.SmartObject.UpdateOption.AfterModeling, 1.0)
        replacement = self.part.CoordinateSystems.CreateCoordinateSystem(xform, nx.SmartObject.UpdateOption.AfterModeling)
        original_graph = self.mcs_definitions[tag][0]
        selection_graphs = {}
        for _, selected in selections:
            for selected_tag in selected:
                if selected_tag not in original_graph:
                    raise RuntimeError("Нет исходного определения выбранного объекта Three Planes " + str(selected_tag))
                selection_graphs[selected_tag] = graph_branch(original_graph, selected_tag)
        data = {"planes": tuple(oriented), "plane_tags": tuple(int(p.Tag) for p in oriented),
                "sources": tuple(planes), "xform": xform, "xform_tag": int(xform.Tag),
                "selections": tuple(selections),
                "origin": tuple(expected_origin), "axes": tuple(expected_axes),
                "selection_graphs": selection_graphs}
        # A candidate must have the requested frame before touching the existing
        # CSYS. ReplaceParameters destroys the temporary candidate afterwards.
        self.orient_three_planes(replacement, data, "кандидат для СК " + str(tag))
        for source in planes:
            self.require_source_plane_selection(source)
            self.verify_support_plane(source)
            if self.smart_graph(source) != graph_branch(original_graph, source):
                raise RuntimeError("Изменилось определение исходной опоры Three Planes: " + str(source))
        self.refs[tag].ReplaceParameters(replacement)
        self.stage = "Проверка Three Planes: " + str(tag) + " / после ReplaceParameters"
        self.evaluate_three_planes(self.refs[tag], data)
        self.require_csys_frame(tag, expected_origin, expected_axes, "после ReplaceParameters")
        # Verify actual stored construction, not merely the temporary objects.
        graph = self.smart_graph(tag)
        inputs = set(graph.get(int(xform.Tag)) or ())
        plane_tags = {int(p.Tag) for p in oriented}
        if (graph[tag] != (int(xform.Tag),) or not plane_tags.issubset(inputs)
                or any(not isinstance(self.refs[p], nx.Scalar) for p in inputs - plane_tags)
                or not close_values([float(xform.Scale)], [1.0])):
            raise RuntimeError("NX не сохранил определение Three Planes")
        for p, selection in zip(oriented, selections):
            if not set(selection[1]).issubset(graph[int(p.Tag)] or ()):
                raise RuntimeError("NX не сохранил прямые ссылки Three Planes на выбранные объекты")
        self.mcs_verified_definitions[tag] = graph
        self.mcs_rebuilt[tag] = data
        self.verify_three_plane_selections(tag)
        self.lines.append("Three Planes " + str(tag) + ": проверяются плоскости действующего определения " +
                          str(data["plane_tags"]) + "; исходные опоры " + str(tuple(planes)) +
                          " проверяются по сохранённым данным выбора и положения.")

    def stabilize_three_planes_after_update(self):
        repaired = False
        for tag, (graph, planes) in self.mcs_definitions.items():
            if planes is None:
                continue
            origin, axes = self.frames[tag]
            origin, axes = self.plane.point(origin), reflected_frame(axes, self.plane)
            self.stage = "Проверка Three Planes после обновления модели: " + str(tag)
            if self.smart_graph(tag) != self.mcs_verified_definitions[tag]:
                raise RuntimeError("Изменилось определение Three Planes после обновления модели: " + str(tag))
            self.verify_three_plane_selections(tag)
            actual_origin, actual_axes = self.csys_frame(tag)
            if close_values(actual_origin, origin) and close_values(actual_axes, axes):
                continue
            self.lines.append("Three Planes " + str(tag) + ": после обновления модели получены оси " +
                              str(actual_axes) + "; ожидаются " + str(axes))
            data = self.mcs_rebuilt.get(tag)
            if data is None:
                for source in planes:
                    if self.smart_graph(source) != graph_branch(graph, source):
                        raise RuntimeError("Повреждено определение опорной плоскости MCS " + str(source))
                self.rebuild_three_planes(tag, planes, origin, axes)
            else:
                self.orient_three_planes(self.refs[tag], data, "после обновления модели")
            repaired = True
        if not repaired:
            return
        # A correction must survive a normal NX update. Do not repair again at
        # this checkpoint or in final verification: an unstable result fails.
        self.stage = "Проверка устойчивости Three Planes при обновлении модели"
        self.native.update_model()
        for tag, (_, planes) in self.mcs_definitions.items():
            if planes is None:
                continue
            if self.smart_graph(tag) != self.mcs_verified_definitions[tag]:
                raise RuntimeError("Изменилось определение Three Planes при контрольном обновлении: " + str(tag))
            self.verify_three_plane_selections(tag)
            origin, axes = self.frames[tag]
            self.require_csys_frame(tag, self.plane.point(origin), reflected_frame(axes, self.plane),
                                    "контрольное обновление модели")

    def model_history(self, context="исходное состояние", check_errors=True):
        result = {}
        for feature in self.part.Features:
            tag = int(feature.Tag)
            record = {"name": str(feature.Name), "type": str(feature.FeatureType)}
            self.stage = "Чтение истории модели / " + context + " / " + history_feature_label(tag, record)
            # Intermediate snapshots do not introduce an extra model update or
            # reject a temporary error before MCS/stock repair has completed.
            # Validate errors at the original initial/final checkpoints only.
            errors = list(feature.GetFeatureErrorMessages()) if check_errors else []
            if errors:
                raise RuntimeError("Ошибка модельного элемента " + history_feature_label(tag, record) +
                                   " после этапа «" + context + "»: " + "; ".join(str(e) for e in errors))
            record["parents"] = {int(p.Tag): str(p.Name) + " [" + str(p.FeatureType) + "]"
                                 for p in feature.GetParents()}
            expressions = {}
            for expression in feature.GetExpressions():
                expression_tag = int(expression.Tag)
                # These descriptions must be frozen while the object is alive:
                # a later transform may remove the expression. They do not
                # authorize changing or recreating any parameter.
                try:
                    kind = str(expression.Type)
                    text = str(expression.Name) + " [" + kind + "] = " + str(expression.RightHandSide)
                    # NX Expression.Units is only valid for Number expressions.
                    if kind.lower() == "number":
                        units = expression.Units
                        text += "; единицы: " + (str(units.Name) if units is not None else "безразмерное")
                except Exception as error:
                    text = "описание недоступно: " + type(error).__name__ + ": " + str(error)
                expressions[expression_tag] = text
            record["expressions"] = expressions
            result[tag] = record
        return result

    def checkpoint_model_history(self, context, check_errors=False):
        current = self.model_history(context, check_errors)
        if self.history_baseline is None:
            self.history_baseline = current
        else:
            changes = model_history_changes(self.history_previous, current)
            if changes:
                self.lines.append("\nИзменения истории зафиксированы после этапа: " + context)
                for _, details in changes:
                    self.lines.extend(details)
        self.history_previous = current
        self.stage = "История модели: " + context
        return current

    def verify_model_history(self):
        changes = model_history_changes(self.history_baseline, self.history_previous)
        if not changes:
            return
        allowed = self.accepted_body_history_changes()
        changes = [(tag, lines) for tag, lines in changes if tag not in allowed]
        if not changes:
            return
        tag = changes[0][0]
        record = self.history_previous.get(tag, self.history_baseline.get(tag))
        self.stage = "Сравнение истории модели: " + history_feature_label(tag, record)
        details = ["В истории остались неразрешённые изменения элементов, родителей или состава выражений; элементов: " + str(len(changes))]
        for _, lines in changes:
            details.extend(lines)
        details.append("Этапы появления различий приведены выше. Эти изменения выходят за разрешённый переход тела в BREP.")
        raise RuntimeError("\n".join(details))

    def capture_body_history(self, topology):
        """Freeze ownership and expression users before NX can remove features.
        FeatureType is diagnostic, never a whitelist of modeling commands.
        Only BodyFeature instances may qualify; all other features must remain.
        """
        self.stage = "Чтение класса NXOpen.Features.BodyFeature для проверки истории тел"
        body_feature_class = getattr(getattr(self.nx, "Features", None), "BodyFeature", None)
        if body_feature_class is None:
            raise RuntimeError("Недоступен NXOpen.Features.BodyFeature; принадлежность истории телам не подтверждена")
        self.body_history, self.body_conversions = {}, []
        for body in self.bodies:
            body_tag = int(body.Tag)
            if body_tag in self.protected_bodies:
                continue
            self.stage = "Чтение истории тела и зависимостей выражений: " + str(body_tag)
            features, owners, dependencies, retained = {}, {}, {}, {}
            for feature in body.GetFeatures():
                tag = int(feature.Tag)
                if tag not in self.history_baseline:
                    raise RuntimeError("История тела изменилась ещё до отражения: " + str(tag))
                record = self.history_baseline[tag]
                self.stage = "Чтение истории тела " + str(body_tag) + " / " + history_feature_label(tag, record)
                features[tag] = record["type"]
                if tag in self.stock_features or not isinstance(feature, body_feature_class):
                    retained[tag] = "защищённая заготовка или элемент вне NXOpen.Features.BodyFeature"
                    continue
                # Do not infer ownership from membership in Body.GetFeatures,
                # or treat an unreadable GetBodies/dependency query as empty.
                owners[tag] = tuple(sorted(int(b.Tag) for b in feature.GetBodies()))
                expressions = list(feature.GetExpressions())
                if {int(e.Tag) for e in expressions} != set(record["expressions"]):
                    raise RuntimeError("Состав выражений изменился ещё до отражения: " + history_feature_label(tag, record))
                for expression in expressions:
                    expression_tag = int(expression.Tag)
                    dependencies[expression_tag] = (
                        tuple(sorted(int(f.Tag) for f in expression.GetUsingFeatures())),
                        tuple(sorted(int(e.Tag) for e in expression.GetReferencingExpressions())))
            self.body_history[body_tag] = {"features": features, "owners": owners,
                "dependencies": dependencies, "retained": retained, "topology": topology[body_tag]}

    def accepted_body_history_changes(self):
        """Accept only a verified one-body history -> one plain BREP conversion.
        All other history differences remain failures. CAM, stock settings,
        reflected geometry and MCS are still checked by the normal workflow.
        """
        before, after = self.history_baseline, self.history_previous
        saved_bodies = getattr(self, "body_history", {})
        if not saved_bodies:
            return set()
        current_bodies = {int(b.Tag): b for b in self.part.Bodies}
        replacements, new_features, conversions = {}, set(), []
        for body_tag, saved in sorted(saved_bodies.items()):
            old_features = set(saved["features"])
            removed = old_features - set(after)
            if not removed:
                continue
            self.stage = "Проверка перехода истории тела в BREP: " + str(body_tag)
            if body_tag in self.protected_bodies:
                raise RuntimeError("Параметрическая заготовка не может переходить в BREP")
            if removed != old_features:
                raise RuntimeError("Не подтверждена полная замена истории одного модельного тела: " + str(body_tag))
            unowned = {tag for tag in old_features if saved["owners"].get(tag) != (body_tag,)}
            if unowned:
                details = [history_feature_label(tag, before[tag]) + ": " +
                    saved["retained"].get(tag, "тела по GetBodies: " + str(saved["owners"].get(tag, "не прочитаны")))
                    for tag in sorted(unowned)]
                raise RuntimeError("Не подтверждена принадлежность истории только одному телу " +
                                   str(body_tag) + "; замена запрещена:\n" + "\n".join(details))
            if set(replacements).intersection(old_features):
                raise RuntimeError("Один модельный элемент относится к нескольким преобразуемым телам")
            body = current_bodies.get(body_tag)
            if body is None:
                raise RuntimeError("При переходе в BREP изменился идентификатор тела: " + str(body_tag))
            actual_topology = (tuple(sorted(int(f.Tag) for f in body.GetFaces())),
                               tuple(sorted(int(e.Tag) for e in body.GetEdges())))
            if actual_topology != saved["topology"]:
                raise RuntimeError("При переходе в BREP изменились идентификаторы граней/рёбер тела: " + str(body_tag))
            features = list(body.GetFeatures())
            if len(features) != 1:
                raise RuntimeError("Вместо истории тела ожидался один элемент BREP: " + str(body_tag))
            feature = features[0]
            new_tag = int(feature.Tag)
            record = after.get(new_tag)
            if (new_tag in before or new_tag in new_features or record is None or
                    record["type"].upper() != "BREP" or record["parents"] or record["expressions"] or
                    tuple(sorted(int(b.Tag) for b in feature.GetBodies())) != (body_tag,)):
                raise RuntimeError("Не подтверждён новый BREP того же модельного тела: " + str(body_tag))
            replacements.update((tag, new_tag) for tag in old_features)
            new_features.add(new_tag)
            conversions.append((body_tag, tuple(sorted(old_features)), new_tag))

        removed_features = set(replacements)
        removed_expressions = {e for tag in removed_features for e in before[tag]["expressions"]}
        for body_tag, old_features, _ in conversions:
            self.stage = "Проверка зависимостей удаляемых параметров тела: " + str(body_tag)
            dependencies = saved_bodies[body_tag]["dependencies"]
            for tag in old_features:
                for expression_tag in before[tag]["expressions"]:
                    if expression_tag not in dependencies:
                        raise RuntimeError("Не прочитаны зависимости выражения: " + str(expression_tag))
                    users, references = dependencies[expression_tag]
                    external_users = set(users) - removed_features
                    external_expressions = set(references) - removed_expressions
                    if external_users or external_expressions:
                        raise RuntimeError("Параметр удаляемого построения используется вне заменяемой истории: " +
                            before[tag]["expressions"][expression_tag] +
                            "\nЭлементы: " + str(sorted(external_users)) +
                            "; выражения: " + str(sorted(external_expressions)))

        allowed = removed_features | new_features
        for tag in getattr(self, "stock_features", {}):
            old, new = before.get(tag), after.get(tag)
            if old is None or new is None or not (set(old["parents"]) & removed_features):
                continue
            expected_parents = {replacements.get(p, p) for p in old["parents"]}
            # Only the already protected Tooling Box may replace model-feature
            # parents with that same body's BREP. Its parameters stay protected.
            if (old["type"].upper() == "TOOLING BOX" and old["type"] == new["type"] and
                    set(old["expressions"]) == set(new["expressions"]) and
                    set(new["parents"]) == expected_parents):
                allowed.add(tag)
        self.body_conversions = conversions
        for body_tag, old_features, new_tag in conversions:
            self.lines.append("Разрешён переход истории модельного тела " + str(body_tag) +
                " в BREP " + str(new_tag) + "; заменены элементы " + str(old_features) +
                ". Идентификаторы тела, граней и рёбер сохранены.")
        return allowed

    def tooling_builder(self, feature):
        return self.part.Features.ToolingFeatureCollection.CreateToolingBoxBuilder(feature)

    def mcs_stock_order(self, topology):
        """Use the pre-transform references, including faces of protected stock."""
        self.stage = "Проверка порядка зависимостей заготовок и MCS"
        owners = {}
        for tag, (_, bodies, _, _) in self.stock_features.items():
            task = ("stock", tag)
            owners[tag] = task
            for body in bodies:
                faces, edges = topology[body]
                for entity in (body,) + tuple(faces) + tuple(edges):
                    if entity in owners and owners[entity] != task:
                        raise RuntimeError("Общая геометрия у нескольких параметрических заготовок")
                    owners[entity] = task
        owners.update({tag: ("mcs", tag) for tag in self.frames})
        dependencies = {}
        for tag in self.frames:
            graph, _ = self.mcs_definitions[tag]
            parent_order(graph)  # Detect cycles even inside an individual CSYS.
            dependencies[("mcs", tag)] = {owners[p] for p in graph if p != tag and p in owners}
        for tag, (feature, _, state, _) in self.stock_features.items():
            parents = set(int(p.Tag) for p in feature.GetParents()) | set(state["BoundedObject"])
            for key in ("Csys", "AxisPoint", "AxisVector"):
                source = state.get(key, 0)
                if source:
                    graph = self.smart_graph(source)
                    parent_order(graph)
                    parents.update(graph)
            dependencies[("stock", tag)] = {owners[p] for p in parents if p in owners}
        # No model object is changed before this plan is known to be acyclic.
        order = parent_order(dependencies)
        labels = [("Заготовка " + str(self.stock_features[tag][0].Name)) if kind == "stock"
                  else "MCS/RCS " + str(tag) for kind, tag in order]
        self.lines.append("Порядок обновления опор: " + " → ".join(labels))
        return order

    def transfer_mcs_and_stock(self, order):
        replacement = {}
        for kind, tag in order:
            if kind == "stock":
                self.update_stock((tag,))
                label = "Обновление параметрических заготовок: " + str(self.stock_features[tag][0].Name)
            else:
                replacement.update(self.transfer_mcs((tag,)))
                label = "Перенос определений MCS/RCS: " + str(tag)
            self.checkpoint_model_history(label)
        return replacement

    def stock_state(self, builder):
        flags = ("Type", "ReferenceCsysType", "CsysAssociative", "NonAlignedMinimumBox", "SingleOffset")
        offsets = ("Clearance", "OffsetNegativeX", "OffsetNegativeY", "OffsetNegativeZ",
                   "OffsetPositiveX", "OffsetPositiveY", "OffsetPositiveZ", "RadialOffset")
        values = {name: getattr(builder, name) for name in flags}
        # Some inactive fields are temporary builder expressions. Compare their
        # definitions/values, not transient expression tags across editor opens.
        values.update({name: (str(getattr(builder, name).RightHandSide), float(getattr(builder, name).Value))
                       for name in offsets})
        values["BoundedObject"] = tuple(sorted(int(o.Tag) for o in builder.BoundedObject.GetObjects()))
        selected = self.nx.Features.ToolingBoxBuilder.RefCsysType.SelectedCsys
        csys = builder.CsysSelection.Value if builder.ReferenceCsysType == selected else None
        values["Csys"] = int(csys.Tag) if csys is not None else 0
        types = self.nx.Features.ToolingBoxBuilder.Types
        if builder.Type == types.BoundedCylinder:
            for name in ("AxisPoint", "AxisVector"):
                obj = getattr(builder, name)
                values[name] = int(obj.Tag) if obj is not None else 0
        elif builder.Type == types.CenterAndLengths:
            for name in ("XValue", "YValue", "ZValue"):
                expression = getattr(builder, name)
                values[name] = (str(expression.RightHandSide), float(expression.Value))
        return values

    def capture_stock(self):
        for feature in self.part.Features:
            if str(feature.FeatureType).upper() != "TOOLING BOX":
                continue
            self.stage = "Чтение параметрической заготовки: " + str(feature.Name)
            builder = self.tooling_builder(feature)
            try:
                state = self.stock_state(builder)
                if not builder.Validate():
                    raise RuntimeError("Некорректные параметры исходной заготовки")
                bodies = tuple(int(b.Tag) for b in feature.GetBodies())
                if not bodies:
                    raise RuntimeError("У параметрической заготовки отсутствует тело")
                self.protected_bodies.update(bodies)
                self.stock_features[int(feature.Tag)] = (feature, bodies, state, xyz(builder.BoxPosition))
                self.lines.append("Защищённая заготовка: " + str(feature.Name) + "; " + str(state["Type"]))
            finally:
                builder.Destroy()

    def update_stock(self, tags=None):
        for tag, (feature, bodies, state, position) in self.stock_features.items():
            if tags is not None and tag not in tags:
                continue
            self.stage = "Обновление параметрической заготовки: " + str(feature.Name)
            builder = self.tooling_builder(feature)
            try:
                if self.stock_state(builder) != state:
                    raise RuntimeError("Изменились параметры заготовки до обновления: " + str(feature.Name))
                # Often an associative Tooling Box has already followed its part.
                # Otherwise edit its existing feature, never transform its solid
                # directly or replace the CAM blank with From Geometry.
                if not all(close_values(self.bounding_box(b), reflected_box(self.bounds[b], 0, 0.0)) for b in bodies):
                    if builder.Type == self.nx.Features.ToolingBoxBuilder.Types.CenterAndLengths:
                        builder.BoxPosition = self.nx.Point3d(*self.plane.point(position))
                    else:
                        builder.CalculateBoxSize()
                    if not builder.Validate():
                        raise RuntimeError("Некорректное обновление параметрической заготовки")
                    committed = builder.Commit()
                    if committed is None or int(committed.Tag) != int(feature.Tag):
                        raise RuntimeError("NX заменил параметрический элемент заготовки")
                if self.stock_state(builder) != state:
                    raise RuntimeError("Изменились тип, припуски или ссылки параметрической заготовки")
            finally:
                builder.Destroy()
        self.native.update_model()

    def operation_editor(self, node):
        collection = self.setup.CAMOperationCollection
        kind = type(node).__name__
        factory = OPERATION_BUILDERS.get(kind)
        method = getattr(collection, factory, None) if factory else None
        if not callable(method):
            factory = "CreateBuilder"
            method = getattr(collection, factory, None)
        if not callable(method):
            raise RuntimeError("Нет доступного редактора фрезерной/сверлильной операции: " + str(node.Name) + " [" + kind + "]")
        self.stage = "Открытие редактора CAM: " + str(node.Name) + " [" + kind + "] / " + factory
        # A factory error is a real failure; do not retry it with a different
        # editor. Check the result of BOTH specialized and generic factories.
        builder = method(node)
        if type(builder).__name__ not in MILLING_HOLE_BUILDERS:
            actual = type(builder).__name__
            try:
                raise RuntimeError("Редактор операции вне поддерживаемых фрезерных/сверлильных семейств: " +
                                   str(node.Name) + " [" + kind + "]; " + factory + " → " + actual)
            finally:
                if builder is not None:
                    builder.Destroy()
        self.lines.append("Распознан редактор: " + str(node.Name) + " [" + kind + "]; " +
                          factory + " → " + type(builder).__name__ + ". Полнота настроек не подтверждена.")
        return builder

    def check_cam_builders(self):
        if not hasattr(self, "cam_extra_unverified"):
            self.cam_extra_unverified = set()
        factories = {"FeatureGeometry": (self.setup.CAMGroupCollection, "CreateMillGeomBuilder"),
                     "OrientGeometry": (self.setup.CAMGroupCollection, "CreateMillOrientGeomBuilder")}
        result, labels = {}, {}
        for node in self.nodes:
            kind = type(node).__name__
            is_operation = kind in OPERATION_BUILDERS or isinstance(node, getattr(self.nx.CAM, "Operation", ()))
            if kind not in factories and not is_operation:
                continue
            tag = int(node.Tag)
            label = str(node.Name) + " [" + kind + "]"
            labels[tag] = label
            self.stage = "Проверка открытия CAM: " + label
            if is_operation:
                builder = self.operation_editor(node)
            else:
                collection, factory = factories[kind]
                builder = getattr(collection, factory)(node)
            try:
                state = {}
                # FBM/CI definitions are not represented completely by UF channels.
                # Opening an editor is not proof that all hole/feature data moved.
                if is_operation:
                    state["Operation.Class"] = ("text", kind)
                    state["Operation.Builder"] = ("text", type(builder).__name__)
                    if (type(builder).__name__ in FEATURE_DEFINITION_BUILDERS or
                            callable(getattr(builder, "GetFeatureGeometry", None))):
                        self.cam_extra_unverified.add(label + " / полное определение отверстий/FBM")
                geometry_fields = []
                for name in CAM_GEOMETRY_FIELDS:
                    self.stage = "Чтение CAM через редактор: " + label + " / " + name
                    # Do not let getattr(..., None) swallow AttributeError from
                    # an existing property getter. Missing, null and unreadable
                    # fields are distinct; none proves an empty selection.
                    try:
                        geometry, present = getattr(builder, name), True
                    except AttributeError:
                        if name in dir(builder) or any(name in cls.__dict__ for cls in type(builder).__mro__):
                            raise
                        geometry, present = None, False
                    access = type(geometry).__name__ if present else "нет свойства"
                    state[name + ".API"] = ("text", access)
                    if isinstance(geometry, self.nx.CAM.Geometry):
                        geometry_fields.append(name)
                        geometry.InitializeData(True)
                        if name == "BlankGeometry" and isinstance(geometry, self.nx.CAM.GeometryGroup):
                            domains = (("BlankDefinitionType", "BlankDefinitionTypes"),
                                       ("BlankCreationType", "BlankCreationTypes"),
                                       ("OrientationType", "OrientationTypes"), ("DirectionType", "DirectionTypes"))
                            for field, domain in domains:
                                key = name + "." + field
                                self.stage = "Чтение параметра CAM: " + label + " / " + key
                                state[key] = cam_enum_snapshot(getattr(geometry, field),
                                    getattr(self.nx.CAM.GeometryGroup, domain), label + " / " + key)
                            key = name + ".BlankToggleValue"
                            self.stage = "Чтение параметра CAM: " + label + " / " + key
                            state[key] = cam_bool_snapshot(geometry.BlankToggleValue, label + " / " + key)
                            fields = ("OffsetFromPart", "OffsetNegativeZ", "OffsetPositiveZ",
                                      "AutoBlockOffsetNegativeX", "AutoBlockOffsetPositiveX", "AutoBlockOffsetNegativeY",
                                      "AutoBlockOffsetPositiveY", "AutoBlockOffsetNegativeZ", "AutoBlockOffsetPositiveZ")
                            for field in fields:
                                key = name + "." + field
                                self.stage = "Чтение параметра CAM: " + label + " / " + key
                                state[key] = cam_length_snapshot(getattr(geometry, field), label + " / " + key)
                    else:
                        self.cam_extra_unverified.add(label + " / " + name + " через Builder: " + access)
                if kind == "OrientGeometry":
                    self.stage = "Чтение параметра CAM: " + label + " / MCS.McsLocationMode"
                    state["MCS.McsLocationMode"] = cam_enum_snapshot(builder.McsLocationMode,
                        self.nx.CAM.OrientGeomBuilder.McsLocationModes, label + " / MCS.McsLocationMode")
                    self.stage = "Чтение параметра CAM: " + label + " / MCS.LinkRcsToMcs"
                    state["MCS.LinkRcsToMcs"] = cam_bool_snapshot(builder.LinkRcsToMcs, label + " / MCS.LinkRcsToMcs")
                self.stage = "Проверка редактора CAM: " + str(node.Name)
                if not builder.Validate():
                    raise RuntimeError("Редактор CAM не прошёл проверку: " + str(node.Name))
                if is_operation:
                    self.lines.append("  Прочитаны поля Geometry: " + (", ".join(geometry_fields) or "нет") +
                                      "; Validate пройден. UF-ссылки проверяются отдельно.")
                result[tag] = state
            finally:
                # Validation only: no CAM Commit, generation, or parameter edits.
                builder.Destroy()
        self.compare_cam_builder_states(result, labels)

    def compare_cam_builder_states(self, result, labels):
        if self.cam_builder_state is None:
            self.cam_builder_state, self.cam_builder_labels = result, labels
            return
        differences = []
        for tag in sorted(set(self.cam_builder_state) | set(result)):
            before, after = self.cam_builder_state.get(tag), result.get(tag)
            if before is None or after is None:
                differences.append((tag, "Редактор CAM", None if before is None else ("bool", True),
                                    None if after is None else ("bool", True)))
                continue
            for field in sorted(set(before) | set(after)):
                old, new = before.get(field), after.get(field)
                if not cam_values_equal(old, new):
                    differences.append((tag, field, old, new))
        if not differences:
            return  # Keep the original baseline; do not accumulate tolerated drift.
        def node_label(tag):
            return labels.get(tag, self.cam_builder_labels.get(tag, "Узел " + str(tag)))
        self.stage = "Сравнение параметров CAM: " + node_label(differences[0][0])
        lines = ["Изменились параметры редакторов CAM; различий: " + str(len(differences))]
        for tag, field, before, after in differences:
            detail = (node_label(tag) + " (узел " + str(tag) + ") / " + field +
                      ": до = " + cam_value_text(before) + "; после = " + cam_value_text(after))
            if before is not None and after is not None and before[0] == after[0] == "length":
                detail += "; разница = " + format(after[1] - before[1], ".17g")
            lines.append(detail)
        lines.append("Режимы и флаги сравниваются точно; абсолютный допуск размеров: " +
                     str(CAM_LENGTH_TOLERANCE) + " единиц детали.")
        raise RuntimeError("\n".join(lines))

    def geometry_snapshot(self, collect=False):
        result = {}
        self.current_boundaries = {}
        self.current_custom_points = {}
        if collect:
            self.boundary_planes = {}
            self.custom_points, self.custom_point_labels = {}, {}
        for node in self.nodes:
            for kind, label in enumerate(GEOMETRY_KINDS):
                context = str(node.Name) + " [" + type(node).__name__ + "]/" + label
                self.stage = "Чтение геометрии: " + context + " / элементы"
                item_state, item_owner, items = self.native.channel(self.native.items, int(node.Tag), kind)
                if collect and item_state == "unavailable":
                    self.unavailable.add(context + " / элементы")
                tags = []
                for item in items:
                    tag = self.native.entity(self.native.item_entity, item, allow_zero=True)
                    obj = self.nx.TaggedObjectManager.GetTaggedObject(tag) if tag else None
                    if not tag or isinstance(obj, self.nx.ScCollector):
                        tags.extend(self.native.collector_entities(item))
                    else:
                        tags.append(tag)
                boundaries = []
                self.stage = "Чтение геометрии: " + context + " / границы"
                bnd_state, bnd_owner, handles = self.native.channel(self.native.boundaries, int(node.Tag), kind)
                if collect and bnd_state == "unavailable":
                    self.unavailable.add(context + " / границы")
                for number, handle in enumerate(handles, 1):
                    boundary_context = context + " / граница " + str(number) + " из " + str(len(handles))
                    self.stage = "Чтение геометрии: " + boundary_context + " / параметры"
                    data = self.native.read_boundary(handle)
                    key = (int(node.Tag), kind, number)
                    self.current_boundaries[key] = (handle, data)
                    result[(int(node.Tag), kind, "boundary", number)] = tuple(int(getattr(data, name)) for name in
                        ("plane_type", "boundary_type", "material_side", "ignore_holes", "ignore_islands", "ignore_chamfers"))
                    if collect and data.plane_type == 0:
                        origin, axes = list(data.origin), list(data.matrix)
                        if not all(math.isfinite(v) for v in origin):
                            raise RuntimeError(boundary_context + ": некорректная точка плоскости")
                        reflected_boundary_frame(axes, self.plane)
                        self.boundary_planes[key] = (origin, axes)
                    self.stage = "Чтение геометрии: " + boundary_context + " / элементы"
                    members = self.native.pointers(self.native.members, handle)
                    member_tags = [self.native.entity(self.native.member_entity, item) for item in members]
                    boundaries.append(tuple(member_tags))
                    tags.extend(member_tags)
                result[(int(node.Tag), kind)] = (item_state, item_owner, bnd_state, bnd_owner,
                                               tuple(tags), tuple(boundaries))
                if collect:
                    for tag in tags:
                        self.reference(tag)
            if isinstance(node, self.nx.CAM.Operation):
                # Query both channels for newly accepted families; an unsupported
                # UF channel is recorded as unverified, never assumed empty.
                kinds = {"SurfaceContour": (1,), "TpMultiAxisDeburring": ()}.get(type(node).__name__, (0, 1))
                for kind in kinds:
                    context = str(node.Name) + " / " + CUSTOM_POINT_KINDS[kind]
                    self.stage = "Чтение пользовательских точек: " + context
                    try:
                        points = self.native.read_custom_points(int(node.Tag), kind)
                    except NativeApiError as exc:
                        if exc.code != 1345005:
                            raise
                        points = None
                        if collect:
                            self.unavailable.add(context + " / пользовательские точки")
                    key = (int(node.Tag), kind)
                    self.current_custom_points[key] = points
                    # Coordinates are checked against the frozen reflection,
                    # separately from ordered references and depth values.
                    result[(int(node.Tag), "custom", kind)] = ("unavailable" if points is None else
                        tuple((point_tag, depths) for point_tag, _, depths in points))
                    if collect and points:
                        self.capture_custom_points(node, kind, points, context)
        if collect and not self.refs:
            raise RuntimeError("Не удалось получить ни одной CAM-ссылки на геометрию. Недоступные каналы: " + "; ".join(sorted(self.unavailable)[:12]))
        return result

    def capture_custom_points(self, node, kind, points, context):
        self.lines.append("Пользовательские точки: " + context + "; количество " + str(len(points)))
        for number, (tag, point, depths) in enumerate(points, 1):
            self.lines.append("  Точка " + str(number) + ": tag=" + str(tag) + "; ABS=" + str(point) +
                              "; upper_depth/lower_depth/depth=" + str(depths))
        # The old public UF contract describes these four operation classes.
        # Nonzero depth semantics have not been verified against SDK 2606;
        # do not silently preserve or zero them as a substitute for reflection.
        if type(node).__name__ not in ("PlanarMilling", "CavityMilling", "FaceMilling", "SurfaceContour"):
            raise RuntimeError(context + ": перенос пользовательских точек этого класса операции не подтверждён")
        if any(any(v != 0.0 for v in depths) for _, _, depths in points):
            raise RuntimeError(context + ": ненулевые глубины пользовательских точек требуют проверки по SDK 2606; значения приведены выше")
        # A zero depth may still be a level rather than an inactive field.
        # Until its reference frame is verified, Planar/Cavity points are only
        # admitted when reflection preserves ABS Z as well as local MCS Z.
        if type(node).__name__ in ("PlanarMilling", "CavityMilling") and self.plane.normal[2] != 0.0:
            raise RuntimeError(context + ": плоскость меняет ABS Z; смысл даже нулевых глубин Planar/Cavity требует проверки по SDK 2606")
        for tag, point, _ in points:
            if tag:
                self.reference(tag)
                obj = self.refs[tag]
                if not isinstance(obj, self.nx.Point) or not close_values(xyz(obj.Coordinates), point):
                    raise RuntimeError(context + ": не подтверждена ссылка на Point и его координаты: " + str(tag))
        key = (int(node.Tag), kind)
        self.custom_points[key], self.custom_point_labels[key] = points, context

    def transfer_custom_points(self, geometry):
        if not self.custom_points:
            return
        self.stage = "Повторное чтение пользовательских точек после геометрии и MCS"
        if self.geometry_snapshot() != geometry:
            raise RuntimeError("Изменились CAM-ссылки или определения пользовательских точек до переноса")
        updates = []
        for key, original in self.custom_points.items():
            context = self.custom_point_labels[key]
            self.stage = "Подготовка переноса пользовательских точек: " + context
            current = self.current_custom_points[key]
            expected, needs_write = [], False
            for number, ((tag, point, depths), (_, actual, _)) in enumerate(zip(original, current), 1):
                reflected = tuple(self.plane.point(point))
                if not close_values(actual, reflected):
                    if tag or not close_values(actual, point):
                        raise RuntimeError(context + ": неожиданное положение точки " + str(number) +
                                           "; ожидалось " + str(reflected) + "; получено " + str(actual))
                    needs_write = True
                expected.append((tag, reflected, depths))
            if needs_write:
                updates.append((key, tuple(expected)))
        for (tag, kind), points in updates:
            self.stage = "Перенос числовых пользовательских точек: " + self.custom_point_labels[(tag, kind)]
            self.native.replace_custom_points(tag, kind, points)
        if updates:
            self.native.update_model()
        if self.geometry_snapshot() != geometry:
            raise RuntimeError("Изменились CAM-ссылки, порядок точек или глубины после переноса")
        self.verify_custom_points()

    def verify_custom_points(self):
        for key, original in self.custom_points.items():
            context = self.custom_point_labels[key]
            self.stage = "Проверка пользовательских точек: " + context
            current = self.current_custom_points.get(key)
            if current is None or len(current) != len(original):
                raise RuntimeError(context + ": потерян список пользовательских точек")
            for number, (old, new) in enumerate(zip(original, current), 1):
                tag, point, depths = old
                if (new[0] != tag or new[2] != depths or
                        not close_values(new[1], self.plane.point(point))):
                    raise RuntimeError(context + ": не подтверждены отражение, ссылка или глубины точки " + str(number))

    def transfer_boundary_planes(self, geometry):
        if not self.boundary_planes:
            return
        # Reacquire handles after modelling/MCS update; never reuse old pointers.
        self.stage = "Повторное чтение границ перед переносом плоскостей"
        if self.geometry_snapshot() != geometry:
            raise RuntimeError("Изменились CAM-ссылки или настройки границ до переноса плоскостей")
        updates = {}
        for key, (origin, axes) in self.boundary_planes.items():
            handle, data = self.current_boundaries[key]
            expected = (self.plane.point(origin), reflected_boundary_frame(axes, self.plane))
            if handle in updates:
                if not all(close_values(a, b) for a, b in zip(updates[handle], expected)):
                    raise RuntimeError("Противоречивое наследование плоскости границы")
            else:
                updates[handle] = expected
        for number, (handle, (origin, axes)) in enumerate(updates.items(), 1):
            self.stage = "Перенос заданной плоскости границы " + str(number) + " из " + str(len(updates))
            self.native.set_boundary_plane(handle, origin, axes)
        self.moved_boundary_count = len(updates)
        self.native.update_model()

    def verify_boundary_planes(self):
        for key, (origin, axes) in self.boundary_planes.items():
            if key not in self.current_boundaries:
                raise RuntimeError("Не найдена перенесённая граница " + str(key))
            handle, data = self.current_boundaries[key]
            if (data.plane_type != 0 or not close_values(list(data.origin), self.plane.point(origin)) or
                    not close_values(list(data.matrix), reflected_boundary_frame(axes, self.plane))):
                raise RuntimeError("Не подтверждено отражение плоскости границы " + str(key))

    def capture_parameters(self):
        for node in self.nodes:
            context = "Чтение MCS и параметров: " + str(node.Name) + " [" + type(node).__name__ + "]"
            self.stage = context
            indices = sorted(set(counted(self.uf.Param.AskRequiredParams(node.Tag))))
            for index in indices:
                self.stage = context + " / параметр " + str(index)
                attrs = self.uf.Param.AskParamAttributes(index)
                kind = parameter_type(field(attrs, "type"), self.nx)
                if index == 41 and kind == "logical":
                    # UF_PARAM_RCS_LINKED_TO_MCS: preserve this relationship.
                    self.mcs_links[int(node.Tag)] = bool(self.uf.Param.AskLogicalValue(node.Tag, index))
                elif kind == "tag":
                    tag = int(self.uf.Param.AskTagValue(node.Tag, index))
                    if tag:
                        self.reference(tag)
                    if index in (59, 60) and tag:
                        self.capture_mcs(node, index, tag)
                elif kind == "vlatag":
                    for tag in counted(self.uf.Param.AskTagVla(node.Tag, index)):
                        self.reference(tag)
                elif kind in ("3d", "3dlength"):
                    if self.native.parameter_status(node.Tag, index) == PARAM_OVERRIDDEN:
                        value = list(self.uf.Param.Ask3dValue(node.Tag, index))
                        if any(abs(x) > 1e-10 for x in value):
                            self.raw_spatial.append(str(node.Name) + ": " + str(field(attrs, "name")) + " = " + str(value))
        if not self.frames:
            raise RuntimeError("Не найдены системы координат MCS")
        self.resolve_mcs_sources()

    def capture_mcs(self, node, index, tag):
        key = (int(node.Tag), index)
        if key in self.mcs_expected:
            return
        label = str(node.Name) + " [" + type(node).__name__ + "] / " + ("MCS" if index == 59 else "RCS")
        self.stage = "Чтение MCS и параметров: " + label + " / параметр " + str(index)
        state = self.native.parameter_status(node.Tag, index)
        definer = (self.native.parameter_definer(node.Tag, index) if state == PARAM_INHERITED
                   else int(node.Tag) if state == PARAM_OVERRIDDEN else 0)
        self.mcs_expected[key] = tag
        self.mcs_sources[key] = (state, definer, label)
        if tag not in self.frames:
            matrix, origin = self.uf.Csys.AskCsysInfo(tag)
            axes = list(self.uf.Csys.AskMatrixValues(matrix))
            reflected_frame(axes, self.plane)
            self.frames[tag] = (list(origin), axes)
        # NX defaults are not inheritance from another CAM object. Materialize
        # their reflected value in this copy; never override an inherited child.
        if state != PARAM_INHERITED:
            self.mcs_bindings.append((node, index, tag))

    def resolve_mcs_sources(self):
        pending = list(self.mcs_expected)
        for key in pending:
            state, definer, label = self.mcs_sources[key]
            if state != PARAM_INHERITED:
                continue
            self.stage = "Чтение источника наследования: " + label
            if not definer or definer == key[0]:
                raise RuntimeError("Не определён источник наследования " + label + ": " + str(definer))
            source_key = (definer, key[1])
            if source_key not in self.mcs_expected:
                if len(pending) >= MAX_OBJECTS:
                    raise RuntimeError("Слишком много источников MCS/RCS")
                # Follow the actual UF provider, not an assumed geometry parent.
                self.reference(definer)
                source_node = self.nx.TaggedObjectManager.GetTaggedObject(definer)
                source_tag = int(self.uf.Param.AskTagValue(definer, key[1]))
                if not source_tag:
                    raise RuntimeError("У источника наследования отсутствует система координат: " + label)
                self.reference(source_tag)
                self.capture_mcs(source_node, key[1], source_tag)
                pending.append(source_key)
            value = self.frames[self.mcs_expected[key]]
            source_value = self.frames[self.mcs_expected[source_key]]
            if not all(close_values(a, b) for a, b in zip(value, source_value)):
                raise RuntimeError("Различаются система координат и её источник наследования: " + label)
        for first in pending:
            seen, key = set(), first
            while self.mcs_sources[key][0] == PARAM_INHERITED:
                if key in seen:
                    raise RuntimeError("Цикл наследования MCS/RCS: " + self.mcs_sources[first][2])
                seen.add(key)
                key = (self.mcs_sources[key][1], key[1])

    def transfer_mcs(self, tags=None):
        replacement = {}
        for tag, (old_origin, axes) in self.frames.items():
            if tags is not None and tag not in tags:
                continue
            origin = self.plane.point(old_origin)
            axes = reflected_frame(axes, self.plane)
            self.stage = "Перенос определения MCS/RCS: " + str(tag)
            graph, planes = self.mcs_definitions[tag]
            if planes is not None:
                self.refresh_mcs_supports(tag, graph, planes)
            is_default = any(old == tag and self.mcs_sources[(int(node.Tag), index)][0] == PARAM_DEFAULT
                             for node, index, old in self.mcs_bindings)
            if is_default:
                # The root's implicit default is not a user's Three Planes
                # definition. Do not modify an NX default shared elsewhere.
                if planes or any(old == tag and self.mcs_sources[(int(node.Tag), index)][0] == PARAM_OVERRIDDEN
                                 for node, index, old in self.mcs_bindings):
                    raise RuntimeError("Значение MCS по умолчанию разделяется с собственным определением")
                matrix_tag = self.uf.Csys.CreateMatrix(axes)
                replacement[tag] = self.uf.Csys.CreateCsys(origin, matrix_tag)
                continue
            replacement[tag] = tag
            if graph[tag] is not None:
                obj = self.refs[tag]
                obj.Evaluate()
                matrix_tag, actual_origin = self.uf.Csys.AskCsysInfo(tag)
                actual_axes = self.uf.Csys.AskMatrixValues(matrix_tag)
                if not (close_values(actual_origin, origin) and close_values(actual_axes, axes)):
                    fixed_xform = self.mcs_fixed_xforms.get(tag)
                    if fixed_xform is not None:
                        if self.smart_graph(tag) != graph:
                            raise RuntimeError("Изменилось определение числового Xform системы " + str(tag))
                        self.native.set_fixed_xform(fixed_xform, origin, axes)
                        obj.Evaluate()
                        matrix_tag, actual_origin = self.uf.Csys.AskCsysInfo(tag)
                        actual_axes = self.uf.Csys.AskMatrixValues(matrix_tag)
                        if (self.smart_graph(tag) != graph or not close_values(actual_origin, origin)
                                or not close_values(actual_axes, axes)):
                            raise RuntimeError("Не подтверждён перенос CSYS с числовым Xform " + str(tag))
                        continue
                    if planes is None:
                        labels = [self.mcs_sources[key][2] for key, value in self.mcs_expected.items()
                                  if value == tag and self.mcs_sources[key][0] != PARAM_INHERITED]
                        raise RuntimeError("Нельзя сохранить способ задания ассоциативной СК " + str(tag) +
                                           " (" + "; ".join(labels) + "). Требуется поддержка её определения; замена на Dynamic запрещена.\n" +
                                           "\n".join(self.csys_definition_details(tag, graph)))
                    # Preserve every original support-plane definition. Only the
                    # CSYS/xform construction is replaced, retaining the CSYS tag.
                    for source in planes:
                        if self.smart_graph(source) != graph_branch(graph, source):
                            raise RuntimeError("Повреждено определение опорной плоскости MCS " + str(source))
                    self.rebuild_three_planes(tag, planes, origin, axes)
            else:
                matrix_tag = self.uf.Csys.CreateMatrix(axes)
                self.uf.Csys.SetOrigin(tag, origin)
                self.uf.Csys.EditMatrixOfObject(tag, matrix_tag)
        for node, index, old_tag in self.mcs_bindings:
            if old_tag in replacement and replacement[old_tag] != old_tag:
                self.stage = "Перенос систем координат: " + str(node.Name) + " / параметр " + str(index)
                self.uf.Param.SetTagValue(node.Tag, index, replacement[old_tag])
        return replacement

    def verify_mcs(self, replacement):
        verified_frames = {}
        for tag in self.mcs_definitions:
            self.stage = "Итоговая проверка прямого выбора Three Planes: " + str(tag)
            self.verify_three_plane_selections(tag)
        for (tag, index), old_tag in self.mcs_expected.items():
            old_state, old_definer, label = self.mcs_sources[(tag, index)]
            self.stage = "Проверка MCS/RCS: " + label + " / параметр " + str(index)
            actual = int(self.uf.Param.AskTagValue(tag, index))
            state = self.native.parameter_status(tag, index)
            definer = (self.native.parameter_definer(tag, index) if state == PARAM_INHERITED
                       else tag if state == PARAM_OVERRIDDEN else 0)
            expected_origin, expected_axes = self.frames[old_tag]
            expected_origin = self.plane.point(expected_origin)
            expected_axes = reflected_frame(expected_axes, self.plane)
            origin, axes = [], []
            if actual:
                self.reference(actual)
                matrix_tag, origin = self.uf.Csys.AskCsysInfo(actual)
                axes = self.uf.Csys.AskMatrixValues(matrix_tag)
            inherited = old_state == PARAM_INHERITED
            source_ok = (state == PARAM_INHERITED and definer == old_definer) if inherited else state == PARAM_OVERRIDDEN
            frame_ok = close_values(origin, expected_origin) and close_values(axes, expected_axes)
            if replacement[old_tag] == old_tag and self.mcs_definitions[old_tag][0][old_tag] is not None:
                if actual != old_tag or self.smart_graph(actual) != self.mcs_verified_definitions[old_tag]:
                    raise RuntimeError("Изменилось ассоциативное определение " + label + "; переход на Dynamic не разрешён")
            # NX may supply an equivalent CSYS object. A matching tag alone is
            # neither necessary nor sufficient: verify frame AND inheritance.
            if not source_ok or not frame_ok:
                details = ["Не подтверждён перенос " + label + " (узел " + str(tag) + ")",
                           "Причина: " + ("изменилось наследование" if not source_ok else "не совпадают начало или оси"),
                           "Система: исходная=" + str(old_tag) + "; созданная=" + str(replacement[old_tag]) + "; фактическая=" + str(actual),
                           "Статус: " + PARAM_STATUS_NAMES[old_state] + " → " + PARAM_STATUS_NAMES[state],
                           "Источник наследования: " + str(old_definer) + " → " + str(definer),
                           "Начало ожидаемое: " + str(expected_origin) + "; фактическое: " + str(list(origin)),
                           "Оси ожидаемые: " + str(expected_axes) + "; фактические: " + str(list(axes))]
                raise RuntimeError("\n".join(details))
            verified_frames[old_tag] = (actual, list(origin), list(axes))
        for tag, linked in self.mcs_links.items():
            label = self.mcs_sources.get((tag, 59), (None, None, str(tag)))[2]
            self.stage = "Проверка связи RCS с MCS: " + label
            if bool(self.uf.Param.AskLogicalValue(tag, 41)) != linked:
                raise RuntimeError("Изменился параметр связи RCS с MCS: " + label)
        for tag, (_, sources) in self.mcs_definitions.items():
            if tag in verified_frames:
                actual, origin, axes = verified_frames[tag]
                self.lines.append("Проверены оси MCS/RCS " + str(tag) + " (текущая СК " + str(actual) +
                                  ") / ABS: X=" + str(axes[:3]) + "; Y=" + str(axes[3:6]) +
                                  "; Z=" + str(axes[6:]) + "; начало=" + str(origin))
            if sources is None:
                continue
            data = self.mcs_rebuilt.get(tag)
            selections = (data["selections"] if data is not None else
                tuple(self.mcs_plane_selections.get(source, (None, (source,))) for source in sources))
            for axis, (_, selected) in zip("XYZ", selections):
                labels = [type(self.nx.TaggedObjectManager.GetTaggedObject(value)).__name__ + " " + str(value)
                          for value in selected]
                self.lines.append("Прямой выбор Three Planes " + str(tag) + " / " + axis + ": " +
                                  (", ".join(labels) if labels else "нет объектов в исходном определении"))

    def capture_topology(self):
        # The Python UF wrapper requires List[float]; the stored plane is immutable.
        matrix_tag = self.uf.Csys.CreateMatrix(list(self.plane.basis))
        self.box_csys = self.uf.Csys.CreateTempCsys(list(self.plane.origin), matrix_tag)
        result = {}
        for body in self.bodies:
            self.reference(body.Tag)
            faces, edges = list(body.GetFaces()), list(body.GetEdges())
            result[int(body.Tag)] = (tuple(sorted(int(f.Tag) for f in faces)),
                                     tuple(sorted(int(e.Tag) for e in edges)))
            for obj in [body] + faces + edges:
                # A regenerated bounding solid may rename its internal faces.
                # Its body and any explicitly selected faces/edges still must
                # reflect exactly; no selected reference is exempted.
                if int(body.Tag) not in self.protected_bodies or int(obj.Tag) in self.refs:
                    self.bounds[int(obj.Tag)] = self.bounding_box(obj.Tag)
        return result

    def bounding_box(self, tag):
        obj = self.refs.get(int(tag))
        if obj is not None and isinstance(obj, self.nx.Point):
            value = obj.Coordinates
            point = self.plane.local((value.X, value.Y, value.Z))
            return point + point
        corner, directions, distances = self.native.bounding_box_exact(tag, self.box_csys)
        return local_box(self.plane, corner, directions, distances)

    def operation_snapshot(self):
        return [(int(op.Tag), str(op.Name), type(op).__name__,
                 int(op.GetParent(self.nx.CAM.CAMSetup.View.Geometry).Tag))
                for op in self.setup.CAMOperationCollection]

    def run(self):
        # set_boundary_plane uses WCS coordinates. Normalize only the copy so
        # getters/setters use ABS consistently, then restore the user's WCS.
        self.stage = "Подготовка WCS в копии"
        wcs = self.part.WCS
        origin, axes = wcs_values(wcs)
        try:
            set_wcs_values(wcs, self.nx, [0.0, 0.0, 0.0], [1,0,0,0,1,0,0,0,1])
            self.run_in_absolute_wcs()
        finally:
            set_wcs_values(wcs, self.nx, origin, axes)

    def run_in_absolute_wcs(self):
        self.lines.append("Режим: геометрия и CAM. В копии допускается замена истории модельных тел на BREP; заготовки и MCS защищены отдельно.")
        self.lines.append("Правило MCS/RCS: Y и Z следуют отражению без дополнительного разворота; X = Y × Z для правой системы координат.")
        self.check_cam_builders()
        self.history_baseline, self.history_previous = None, None
        self.checkpoint_model_history("Исходное состояние после первичного чтения CAM", check_errors=True)
        self.capture_stock()
        self.stage = "Чтение геометрических ссылок в копии"
        geometry = self.geometry_snapshot(collect=True)
        self.stage = "Чтение MCS и ссылочных параметров в копии"
        self.capture_parameters()
        for tag, (origin, axes) in self.frames.items():
            self.capture_csys_definition(tag, origin, axes)
        self.stage = "Чтение топологии тел"
        topology = self.capture_topology()
        self.capture_body_history(topology)
        transfer_order = self.mcs_stock_order(topology)
        operations = self.operation_snapshot()
        targets = {int(body.Tag): body for body in self.bodies if int(body.Tag) not in self.protected_bodies}
        transformable = tuple(getattr(self.nx, name) for name in
            ("Curve", "Point", "Plane", "DatumPlane") if hasattr(self.nx, name))
        for tag, obj in list(self.refs.items()):
            if isinstance(obj, transformable):
                # Smart objects follow their defining geometry. Transforming a
                # smart plane explicitly would also transform its parent stock.
                if self.native.smart_parents(tag) is not None:
                    continue
                targets[tag] = obj
                if isinstance(obj, (self.nx.Curve, self.nx.Point)):
                    self.bounds[tag] = self.bounding_box(tag)
        self.lines.append("Тела: " + str(len(self.bodies)) + "; операции: " + str(len(self.operations)))
        self.lines.append("Проверяемые тела/грани/рёбра/кривые/точки: " + str(len(self.bounds)))
        self.lines.append("MCS/RCS, включая корневую систему: " + str(len(self.frames)))
        self.checkpoint_model_history("Подготовка геометрии, MCS и заготовок до отражения")
        self.stage = "Создание матрицы отражения"
        matrix = self.native.reflection_matrix(self.plane)
        self.stage = "Отражение геометрии и её модельных родителей"
        self.native.transform_entities(list(targets), matrix)
        self.checkpoint_model_history("Отражение геометрии и её модельных родителей")
        self.stage = "Обновление заготовок и MCS по зависимостям"
        replacement = self.transfer_mcs_and_stock(transfer_order)
        self.stage = "Удаление рассчитанных траекторий в копии"
        self.setup.DeleteToolPath(self.operations)
        self.stage = "Обновление и проверка результата"
        self.native.update_model()
        self.stabilize_three_planes_after_update()
        self.checkpoint_model_history("Удаление траекторий и обновление модели")
        self.transfer_custom_points(geometry)
        self.checkpoint_model_history("Перенос пользовательских точек")
        self.stage = "Перенос заданных плоскостей границ"
        self.transfer_boundary_planes(geometry)
        self.checkpoint_model_history("Перенос заданных плоскостей границ")
        self.check_cam_builders()
        self.checkpoint_model_history("Повторное чтение редакторов CAM", check_errors=True)
        self.verify_model_history()
        self.stage = "Проверка отражённой геометрии, границ и MCS"
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
            if int(body.Tag) not in self.protected_bodies and current != topology[int(body.Tag)]:
                raise RuntimeError("Изменились идентификаторы граней/рёбер; привязки не подтверждены")
        for tag, box in self.bounds.items():
            actual = self.bounding_box(tag)
            if not close_values(actual, reflected_box(box, 0, 0.0)):
                raise RuntimeError("Не подтверждено отражение объекта " + str(tag) +
                                   "\nОжидалось в СК плоскости: " + str(reflected_box(box, 0, 0.0)) +
                                   "\nПолучено: " + str(actual))
        if self.geometry_snapshot() != geometry:
            raise RuntimeError("Изменились ссылки на выбранную CAM-геометрию/границы")
        self.verify_custom_points()
        self.stage = "Проверка плоскостей границ"
        self.verify_boundary_planes()
        self.verify_mcs(replacement)
        self.stage = "Удаление временной системы проверки"
        self.uf.Obj.DeleteObject(self.box_csys)
        self.box_csys = None
        self.lines.append("ПРОЙДЕНО: отражение проверенных объектов, сохранение их идентификаторов,")
        self.lines.append("доступных ссылок CAM-геометрии, состава операций и переноса MCS/RCS.")
        self.lines.append("Рассчитанные траектории удалены; зеркальные операции не создавались.")
        self.lines.append("Перенесены и проверены заданные плоскости границ: " + str(self.moved_boundary_count))
        self.lines.append("Проверены пользовательские точки начала/предсверления: " +
                          str(sum(len(points) for points in self.custom_points.values())))
        self.lines.append("Проверены данные редакторов Workpiece/MCS/операций: " + str(len(self.cam_builder_state)))
        self.lines.append("Параметрические заготовки сохранены: " + str(len(self.stock_features)))
        self.lines.append("Модельные тела с заменой истории построения на BREP: " + str(len(self.body_conversions)))
        if self.body_conversions:
            self.lines.append("В этой копии эти тела редактируются без исходных Extrude/Blend; история исходного проекта не изменялась.")
        self.lines.append("Сохранены определения Three Planes: " + str(sum(bool(p) for _, p in self.mcs_definitions.values())))
        self.lines.append("Распознаны CSYS с числовым Xform: " + str(len(self.mcs_fixed_xforms)))
        if getattr(self, "cam_extra_unverified", None):
            self.lines.append("\nНЕ ПРОВЕРЕНЫ полностью дополнительные данные CAM / недоступные поля Builder:")
            self.lines.extend(sorted(self.cam_extra_unverified))
        if self.unavailable:
            self.lines.append("\nНЕ ПРОВЕРЕНЫ через UF (код 1345005): " + str(len(self.unavailable)) + " разделов.")
            self.lines.extend(sorted(self.unavailable))
            self.lines.append("Недоступный раздел не считался пустой геометрией. Его содержимое не подтверждено.")
        self.lines.append("\nГРАНИЦЫ ТЕСТА: это не подтверждение корректности будущей обработки.")
        self.lines.append("Подачи, припуски, стратегии и остальные настройки скрипт не редактирует.")
        self.lines.append("Прочие числовые направления/координаты внутри настроек скрипт оставляет как есть:")
        self.lines.extend(self.raw_spatial or ["Локальные ненулевые 3D-параметры не найдены."])
        self.lines.append("Их смысл и соответствие зеркальной детали нужно проверить в операциях.")


def preflight_source(session):
    part = session.Parts.Display
    if part is None or session.Parts.Work is None or int(session.Parts.Work.Tag) != int(part.Tag):
        raise RuntimeError("Откройте тестовую деталь и сделайте её рабочей и отображаемой")
    root = part.ComponentAssembly.RootComponent
    if root is not None and list(root.GetChildren()):
        raise RuntimeError("Тестовая версия работает без компонентов сборки")
    if not part.IsFullyLoaded:
        raise RuntimeError("Сначала полностью загрузите тестовую деталь")
    source = str(part.FullPath)
    if not os.path.isfile(source) or os.path.splitext(source)[1].lower() != ".prt":
        raise RuntimeError("Сначала сохраните тестовую деталь в обычный файл .prt")
    if part.IsModified:
        if re.search(r"_MIRROR_TEST_\d+$", os.path.splitext(source)[0], re.IGNORECASE):
            raise RuntimeError("Открыта изменённая тестовая копия. Для повторного теста активируйте исправный исходный проект. Копию с ошибками не используйте как исходник.")
        raise RuntimeError("В исходнике есть несохранённые изменения. Сохраните его обычной командой NX и запустите скрипт снова. Скрипт сам исходник не сохраняет.")
    return part, source


def execute(session, nx, uf, plane, native=None):
    lines = [TITLE, "Выбранная плоскость ABS: точка " + str(plane.origin) + "; нормаль " + str(plane.normal)]
    source_part, source = preflight_source(session)
    target, source_hash = copy_exclusive(source, [str(part.FullPath) for part in session.Parts])
    lines.append("Исходный проект: " + source)
    lines.append("Файл тестовой копии: " + target)
    part, trial, mark, stage = None, None, None, "Открытие копии"
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
        trial = Trial(session, nx, uf, part, plane, lines, native)
        trial.run()
        if digest(source) != source_hash:
            raise RuntimeError("Исходный файл изменился во время выполнения; результат теста не подтверждён")
        if source_part.IsModified:
            raise RuntimeError("NX выставил исходной детали признак изменения. Исходник не сохранялся; нужно выяснить причину.")
        lines.append("\nИсходный файл на диске: контрольная сумма не изменилась.")
        lines.append("\nРЕЗУЛЬТАТ ОТКРЫТ В NX, НО ЕЩЁ НЕ СОХРАНЁН.")
        lines.append("На диске пока лежит исходная копия PRT. Проверьте тела, MCS и геометрию операций;")
        lines.append("после проверки сохраните открытую копию штатной командой NX.")
        lines.append("Затем рассчитайте операции заново. Исходный проект скрипт не сохранял.")
    except Exception as exc:
        failure_traceback = traceback.format_exc()
        lines.append("\nТЕСТ НЕ ПРОЙДЕН. Этап: " + (trial.stage if trial else stage))
        lines.append(type(exc).__name__ + ": " + str(exc))
        for attribute in ("ErrorCode", "code"):
            try:
                code = getattr(exc, attribute, None)
            except Exception:
                continue
            if isinstance(code, int) and not isinstance(code, bool):
                lines.append("Код NX/API (" + attribute + "): " + str(code))
        if mark is not None:
            rolled_back = False
            try:
                session.UndoToMark(mark, None)
                rolled_back = True
                session.DeleteUndoMark(mark, None)
                lines.append("Изменения теста в копии отменены через Undo.")
            except Exception as rollback:
                if rolled_back:
                    lines.append("Изменения отменены; не удалось удалить метку Undo: " + str(rollback))
                else:
                    lines.append("Откат через Undo не выполнен: " + str(rollback))
                    try:
                        discard_failed_copy(session, nx, part, source_part, target)
                        lines.append("Созданная тестовая копия закрыта без сохранения; это не успешный Undo.")
                        lines.append("Снова активен исходный проект: " + source)
                    except Exception as discard:
                        lines.append("Не удалось подтвердить закрытие тестовой копии: " + str(discard))
                        lines.append("Закройте тестовую копию без сохранения: " + target)
            if rolled_back:
                try:
                    restore_source_display(session, source_part)
                    lines.append("Снова активен исходный проект: " + source)
                except Exception as restore:
                    lines.append("Не удалось вернуть исходный проект на экран: " + str(restore))
        lines.append("Новая копия на диске не перезаписывалась; исходник не сохранялся.")
        # A failed diagnostic must not replace the original error/traceback or
        # imply that the source is unchanged. Check disk AND in-session state.
        try:
            if digest(source) != source_hash:
                lines.append("ВНИМАНИЕ: контрольная сумма исходника изменилась во время теста.")
            else:
                lines.append("Исходный файл на диске: контрольная сумма не изменилась.")
        except Exception as source_error:
            lines.append("Не удалось проверить контрольную сумму исходника: " + str(source_error))
        try:
            if source_part.IsModified:
                lines.append("ВНИМАНИЕ: NX выставил исходной детали признак изменения; исходник не сохранялся.")
            else:
                lines.append("Исходная деталь в NX: признак изменения не установлен.")
        except Exception as source_error:
            lines.append("Не удалось проверить признак изменения исходной детали: " + str(source_error))
        for attribute in ("cam_extra_unverified", "unavailable"):
            unverified = getattr(trial, attribute, ())
            if unverified:
                lines.append("Данные CAM, оставшиеся непроверенными до остановки:")
                lines.extend(sorted(unverified))
        lines.append("\n" + failure_traceback)
    lines.append("\nНажмите «Копировать всё» и пришлите результат теста.")
    return "\r\n".join(lines)


def discard_failed_copy(session, nx, part, source_part, target):
    # This fallback may discard ONLY the new standalone copy created by this
    # invocation. Preserve the user's other open parts and their undo marks.
    if part is None or int(part.Tag) == int(source_part.Tag):
        raise RuntimeError("Не подтверждена отдельная тестовая копия")
    if os.path.normcase(os.path.abspath(str(part.FullPath))) != os.path.normcase(os.path.abspath(target)):
        raise RuntimeError("Путь открытой детали не совпадает с созданной копией")
    root = part.ComponentAssembly.RootComponent
    if root is not None and list(root.GetChildren()):
        raise RuntimeError("Тестовая копия содержит компоненты")
    close = getattr(part, "CloseRetainUndoMarks", None)
    if not callable(close):
        raise RuntimeError("В этом NX недоступно закрытие с сохранением остальных меток Undo")
    tag = int(part.Tag)
    restore_source_display(session, source_part)
    close(nx.BasePart.CloseWholeTree.False_, nx.BasePart.CloseModified.CloseModified, None)
    if any(int(open_part.Tag) == tag for open_part in session.Parts):
        raise RuntimeError("NX всё ещё считает тестовую копию открытой")
    if (int(session.Parts.Work.Tag) != int(source_part.Tag) or
            int(session.Parts.Display.Tag) != int(source_part.Tag)):
        raise RuntimeError("После закрытия NX не подтвердил активный исходный проект")


def restore_source_display(session, source_part):
    # SetDisplay returns (SdpsStatus, PartLoadStatus) in NX Python.
    result = session.Parts.SetDisplay(source_part, False, False)
    values = result if isinstance(result, tuple) else (result,)
    try:
        session.Parts.SetWork(source_part)
        if (session.Parts.Display is None or session.Parts.Work is None or
                int(session.Parts.Display.Tag) != int(source_part.Tag) or
                int(session.Parts.Work.Tag) != int(source_part.Tag)):
            raise RuntimeError("NX не подтвердил рабочую и отображаемую деталь")
    finally:
        for value in values:
            if hasattr(value, "Dispose"):
                value.Dispose()


def plane_from_object(obj, part, nx, native):
    if bool(getattr(obj, "IsOccurrence", False)) or int(obj.OwningPart.Tag) != int(part.Tag):
        raise ValueError("Выберите плоскость текущей детали, без компонентов")
    if isinstance(obj, nx.Face):
        return native.face_plane(obj.Tag)
    if isinstance(obj, nx.DatumPlane):
        origin, normal = obj.Origin, obj.Normal
        return MirrorPlane((origin.X, origin.Y, origin.Z), (normal.X, normal.Y, normal.Z))
    raise ValueError("Выберите плоскую грань или базовую плоскость")


def select_mirror_plane(nx, native, part):
    ui = nx.UI.GetUI()
    masks = []
    for object_type, solid_type in (
        (nx.UF.UFConstants.UF_solid_type, nx.UF.UFConstants.UF_UI_SEL_FEATURE_PLANAR_FACE),
        (nx.UF.UFConstants.UF_datum_plane_type, 0),
    ):
        mask = nx.Selection.MaskTriple()
        mask.Type, mask.Subtype, mask.SolidBodySubtype = object_type, 0, solid_type
        masks.append(mask)
    while True:
        response, objects = ui.SelectionManager.SelectTaggedObjects(
            "Щёлкните одну плоскую грань или базовую плоскость, затем нажмите OK",
            TITLE + " — Выберите одну плоскость",
            nx.Selection.SelectionScope.WorkPart,
            nx.Selection.SelectionAction.ClearAndEnableSpecific,
            False, False, masks)
        if response in (nx.Selection.Response.Cancel, nx.Selection.Response.Back):
            return None
        objects = list(objects or [])
        if len(objects) != 1:
            ui.NXMessageBox.Show(TITLE + " — Выбор плоскости", nx.NXMessageBox.DialogType.Information,
                "Выбрано объектов: " + str(len(objects)) + ". Выберите одну плоскую грань или базовую плоскость и нажмите OK.")
            continue
        try:
            # Only numerical plane data cross into the copied part. Cursor
            # position is not used as the plane origin and no source tag is reused.
            return plane_from_object(objects[0], part, nx, native)
        except ValueError as exc:
            ui.NXMessageBox.Show(TITLE + " — Выбор плоскости", nx.NXMessageBox.DialogType.Information, str(exc))


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
        import NXOpen.Features
        import NXOpen.UF
    finally:
        sys.dont_write_bytecode = previous
    try:
        session = NXOpen.Session.GetSession()
        part, source = preflight_source(session)
        uf = NXOpen.UF.UFSession.GetUFSession()
        # Resolve the native entry points before selection or creating any copy.
        native = NativeCam()
        plane = select_mirror_plane(NXOpen, native, part)
        if plane is None:
            return
        # Recheck after the dialog; no CAM access or mutations on the source.
        preflight_source(session)
        report = execute(session, NXOpen, uf, plane, native)
        show_result(report)
    except Exception:
        show_result(TITLE + "\r\n\r\n" + traceback.format_exc().replace("\n", "\r\n"))


if __name__ == "__main__":
    main()
