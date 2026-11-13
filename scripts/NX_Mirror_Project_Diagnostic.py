# -*- coding: utf-8 -*-
# Диагностика зеркального CAM-проекта — Siemens NX / Designcenter
# SCRIPT_VERSION: V1
SCRIPT_VERSION = "V1"
SCRIPT_NAME = "Диагностика зеркального CAM-проекта"

# Read-only inventory. No builders, updates, cloning, loading, saving or files.
# This journal does NOT mirror a project or certify that it can be mirrored.
import sys

_old_bytecode = sys.dont_write_bytecode
try:
    sys.dont_write_bytecode = True
    import ctypes
    from ctypes import wintypes as W
    import json
    import math
    import re
    import struct
finally:
    sys.dont_write_bytecode = _old_bytecode
    del _old_bytecode

_CREDIT = bytes((98, 121, 32, 64, 84, 111, 110, 121, 95, 70, 111, 120, 120, 120, 32, 40, 84, 71, 41)).decode("ascii")
TITLE = SCRIPT_NAME + " — " + SCRIPT_VERSION + " | " + _CREDIT
MAX_PARTS = 500
MAX_OCCURRENCES = 5000
MAX_CAM_OBJECTS = 3000
MAX_ITEMS = 200
MAX_PARAMETERS = 10000


def field(value, name):
    """UF Python structures can be dictionaries or attribute containers."""
    names = value.keys() if isinstance(value, dict) else dir(value)
    key = name.replace("_", "").casefold()
    matches = [n for n in names if str(n).replace("_", "").casefold() == key]
    if len(matches) != 1:
        raise ValueError("UF structure field unavailable: " + name)
    return value[matches[0]] if isinstance(value, dict) else getattr(value, matches[0])


def counted(value):
    if not isinstance(value, (tuple, list)) or len(value) != 2:
        raise ValueError("Expected UF (count, items) result")
    count, items = value
    items = [] if items is None else list(items)
    if int(count) != len(items) or int(count) < 0:
        raise ValueError("UF count differs from the returned item count")
    return items


def plain(value, depth=0):
    """Serialize UF values without traversing arbitrary NX objects/properties."""
    if value is None or isinstance(value, (bool, int, str)):
        return value
    if isinstance(value, float):
        return value if math.isfinite(value) else str(value)
    if depth > 4:
        return {"not_expanded": type(value).__name__}
    if isinstance(value, dict):
        return {str(k): plain(v, depth + 1) for k, v in value.items()}
    if isinstance(value, (tuple, list)):
        result = [plain(v, depth + 1) for v in value[:MAX_ITEMS]]
        if len(value) > MAX_ITEMS:
            return {"items": result, "total": len(value), "omitted": len(value) - MAX_ITEMS}
        return result
    if all(hasattr(value, x) for x in ("X", "Y", "Z")):
        return [plain(getattr(value, x), depth + 1) for x in ("X", "Y", "Z")]
    axes = ("Xx", "Xy", "Xz", "Yx", "Yy", "Yz", "Zx", "Zy", "Zz")
    if all(hasattr(value, x) for x in axes):
        return [plain(getattr(value, x), depth + 1) for x in axes]
    return str(value)


def class_name(obj):
    return type(obj).__module__ + "." + type(obj).__name__


def enum_name(value, container):
    if container is not None:
        for name in dir(container):
            if name.startswith("_"):
                continue
            try:
                candidate = getattr(container, name)
                if not callable(candidate) and candidate == value:
                    return name
            except Exception:
                pass
    return str(value).rsplit(".", 1)[-1]


def enum_container(module, owner, name):
    return getattr(getattr(module, owner, None), name, None)


class Inventory:
    def __init__(self, session, uf, nx, uf_module):
        self.session, self.uf, self.nx, self.uf_module = session, uf, nx, uf_module
        self.parts = {}
        self.part_objects = []
        self.attribute_cache = {}
        self.issue_counts = {}
        self.issues = []
        self.limits = []
        self.reference_cache = {}
        self.param_types = enum_container(uf_module, "Param", "Type")
        self.records = []

    def read(self, context, function):
        try:
            return function()
        except Exception as exc:
            message = type(exc).__name__ + ": " + str(exc)
            self.issue_counts[message] = self.issue_counts.get(message, 0) + 1
            if len(self.issues) < MAX_ITEMS:
                self.issues.append({"context": context, "error": message})
            return {"unavailable": message}

    def limited(self, values, count, context):
        if len(values) > count:
            self.limits.append({"context": context, "total": len(values), "shown": count})
        return values[:count]

    def part_id(self, part):
        if part is None:
            return None
        key = int(part.Tag)
        if key not in self.parts:
            if len(self.parts) >= MAX_PARTS:
                raise ValueError("Part inventory limit reached")
            self.parts[key] = "P" + str(len(self.parts) + 1)
            self.part_objects.append(part)
        return self.parts[key]

    def reference(self, tag):
        key = int(tag)
        if key == 0:
            return {"tag": 0}
        if key in self.reference_cache:
            return self.reference_cache[key]
        result = {"tag": key}
        self.reference_cache[key] = result

        def inspect():
            obj = self.nx.TaggedObjectManager.GetTaggedObject(tag)
            result["class"] = class_name(obj)
            result["name"] = str(getattr(obj, "Name", ""))
            owner = getattr(obj, "OwningPart", None)
            result["owner_part_tag"] = int(owner.Tag) if owner else None
            result["is_occurrence"] = bool(getattr(obj, "IsOccurrence", False))
            comp = getattr(obj, "OwningComponent", None)
            result["component_tag"] = int(comp.Tag) if comp else None
            if "CoordinateSystem" in result["class"]:
                matrix, origin = self.uf.Csys.AskCsysInfo(tag)
                result["origin_acs"] = plain(origin)
                result["axes_acs"] = plain(self.uf.Csys.AskMatrixValues(matrix))
            elif isinstance(obj, getattr(self.nx, "Point", ())):
                result["point_acs"] = plain(obj.Coordinates)
            return True

        status = self.read("reference " + str(key), inspect)
        if status is not True:
            result["inspection"] = status
        return result

    def describe_object(self, obj):
        return {"tag": int(obj.Tag), "name": str(getattr(obj, "Name", "")),
                "class": class_name(obj)}

    def walk_components(self, root):
        nodes, stack, seen = [], [(root, None, ())], set()
        while stack:
            component, parent, ancestry = stack.pop()
            key = int(component.Tag)
            if key in seen:
                continue
            if len(nodes) >= MAX_OCCURRENCES:
                self.limits.append({"context": "component occurrences", "shown": len(nodes),
                                    "remaining_at_least": len(stack) + 1})
                break
            seen.add(key)
            record = self.describe_object(component)
            record["parent_occurrence_tag"] = parent
            record["suppressed"] = self.read(str(key) + " suppression", lambda: component.IsSuppressed)
            prototype = self.read(str(key) + " prototype", lambda: component.Prototype)
            if prototype is None or isinstance(prototype, dict):
                record["prototype"] = "unloaded" if prototype is None else prototype
            else:
                record["prototype_part"] = self.read(str(key) + " part id", lambda: self.part_id(prototype))
            record["position"] = self.read(str(key) + " position", lambda: plain(component.GetPosition()))
            record["position_source"] = "Component.GetPosition; native occurrence context"
            nodes.append(record)
            if key in ancestry:
                record["cycle"] = True
                continue
            children = self.read(str(key) + " children", lambda: list(component.GetChildren()))
            if isinstance(children, list):
                stack.extend((child, key, ancestry + (key,)) for child in reversed(children))
            else:
                record["children"] = children
        return nodes

    def geometry(self, obj):
        result = {}
        kinds = getattr(self.uf_module, "CamGeomType", None)
        for name in ("CamPart", "CamBlank", "CamCheck", "CamCutArea", "CamTrim", "CamWall", "CamDrive"):
            kind = getattr(kinds, name, None)
            if kind is None:
                result[name] = {"unavailable": "CamGeomType member unavailable"}
                continue
            context = str(obj.Tag) + " " + name
            data = {}
            data["provider"] = self.read(context + " provider", lambda: self.reference(
                self.uf.Camgeom.AskGeomProvider(obj.Tag, kind)))
            items = self.read(context + " items", lambda: counted(self.uf.Camgeom.AskItems(obj.Tag, kind)))
            if isinstance(items, list):
                data["item_count"] = len(items)
                data["items"] = [self.read(context + " entity", lambda item=item: self.reference(
                    self.uf.Camgeom.AskItemEntity(item)))
                    for item in self.limited(items, MAX_ITEMS, context + " items")]
            else:
                data["items"] = items
            boundaries = self.read(context + " boundaries", lambda: counted(
                self.uf.Cambnd.AskBoundaries(obj.Tag, kind)))
            if isinstance(boundaries, list):
                data["boundary_count"] = len(boundaries)
                data["boundaries"] = []
                for boundary in self.limited(boundaries, MAX_ITEMS, context + " boundaries"):
                    entry = {"definition": self.read(context + " boundary data", lambda: plain(
                        self.uf.Cambnd.AskBoundaryData(boundary)))}
                    members = self.read(context + " boundary items", lambda: counted(
                        self.uf.Cambnd.AskBoundaryItems(boundary)))
                    if isinstance(members, list):
                        entry["item_count"] = len(members)
                        entry["items"] = [self.read(context + " boundary entity", lambda item=item: self.reference(
                            self.uf.Cambnd.AskItemEntity(item))) for item in
                            self.limited(members, MAX_ITEMS, context + " boundary items")]
                    else:
                        entry["items"] = members
                    data["boundaries"].append(entry)
            else:
                data["boundaries"] = boundaries
            result[name] = data
        return result

    def parameters(self, obj):
        indices = self.read(str(obj.Tag) + " required parameters", lambda: counted(
            self.uf.Param.AskRequiredParams(obj.Tag)))
        if not isinstance(indices, list):
            return indices
        result = {"required_count": len(indices), "type_counts": {}, "spatial_and_reference_values": []}
        for index in self.limited(indices, MAX_PARAMETERS, str(obj.Tag) + " parameter indices"):
            try:
                if index not in self.attribute_cache:
                    self.attribute_cache[index] = self.uf.Param.AskParamAttributes(index)
                attrs = self.attribute_cache[index]
                name = str(field(attrs, "name"))
                kind = enum_name(field(attrs, "type"), self.param_types)
                result["type_counts"][kind] = result["type_counts"].get(kind, 0) + 1
                token = re.sub(r"[^a-z0-9]", "", kind.casefold())
                if token.startswith("type"):
                    token = token[4:]
                getters = {"3d": "Ask3dValue", "3dlength": "Ask3dValue", "2d": "Ask2dValue",
                           "2dlength": "Ask2dValue", "tag": "AskTagValue", "vlatag": "AskTagVla",
                           "vlalength": "AskDoubleVla", "vlareal": "AskDoubleVla",
                           "vlalengthcomposite": "AskDoubleVla"}
                getter = getters.get(token)
                if not getter:
                    # Scalars selected by their documented name are context only;
                    # the diagnostic never interprets them as transformable data.
                    if not re.search(r"point|axis|vector|mcs|csys|angle|plane|origin|clearance|direction", name, re.I):
                        continue
                    getter = {"double": "AskDoubleValue", "doublelength": "AskDoubleValue",
                              "int": "AskIntValue", "logical": "AskLogicalValue"}.get(token)
                entry = {"index": index, "name": name, "type": kind}
                entry["status"] = self.read(name + " status", lambda: str(
                    self.uf.Param.AskParamStatus(obj.Tag, index)))
                if getter:
                    value = self.read(name + " value", lambda: getattr(self.uf.Param, getter)(obj.Tag, index))
                    if isinstance(value, dict) and "unavailable" in value:
                        entry["value"] = value
                    elif token == "tag":
                        entry["value"] = self.read(name + " reference", lambda: self.reference(value))
                    elif token == "vlatag":
                        tags = counted(value)
                        entry["total"] = len(tags)
                        entry["value"] = [self.read(name + " reference", lambda t=t: self.reference(t))
                            for t in self.limited(tags, MAX_ITEMS, name)]
                    else:
                        entry["value"] = plain(value)
                else:
                    entry["value"] = {"unavailable": "No verified reader for the runtime type"}
                result["spatial_and_reference_values"].append(entry)
            except Exception as exc:
                result["spatial_and_reference_values"].append({"index": index, "unavailable": str(exc)})
        return result

    def custom_points(self, obj):
        container = enum_container(self.uf_module, "Camgeom", "CustomPointType")
        result = {}
        for name in ("CutRegionStartType", "PredrillEngageType"):
            kind = getattr(container, name, None)
            if kind is None:
                result[name] = {"unavailable": "CustomPointType member unavailable"}
                continue
            points = self.read(str(obj.Tag) + " " + name, lambda: counted(
                self.uf.Camgeom.AskCustomPoints(obj.Tag, kind)))
            if isinstance(points, list):
                values = []
                for point in self.limited(points, MAX_ITEMS, str(obj.Tag) + " " + name):
                    values.append({key: self.read(name + " " + key, lambda key=key: plain(field(point, key)))
                                   for key in ("point_acs", "point_tag", "depth", "lower_depth", "upper_depth")})
                result[name] = {"count": len(points), "values": values}
            else:
                result[name] = points
        return result

    def cam(self, part):
        setup = self.read("CAM setup " + str(part.Tag), lambda: part.CAMSetup)
        if setup is None or isinstance(setup, dict):
            return {"setup": "none" if setup is None else setup}
        operations = self.read("operations", lambda: list(setup.CAMOperationCollection))
        groups = self.read("groups", lambda: list(setup.CAMGroupCollection))
        if not isinstance(operations, list) or not isinstance(groups, list):
            return {"operations": operations if isinstance(operations, dict) else len(operations),
                    "groups": groups if isinstance(groups, dict) else len(groups)}
        result = {"operation_count": len(operations), "group_count": len(groups), "objects": []}
        geometry_tags = set()
        root = self.read("geometry root", lambda: setup.GetRoot(self.nx.CAM.CAMSetup.View.Geometry))
        pending = [root] if root is not None and not isinstance(root, dict) else []
        if not pending:
            result["geometry_root"] = root
        while pending:
            group = pending.pop()
            key = int(group.Tag)
            if key in geometry_tags:
                continue
            geometry_tags.add(key)
            children = self.read(str(key) + " CAM children", lambda: list(group.GetMembers()))
            if isinstance(children, list):
                pending.extend(x for x in children if isinstance(x, self.nx.CAM.NCGroup))
        candidates = operations + [g for g in groups if int(g.Tag) in geometry_tags]
        for obj in self.limited(candidates, MAX_CAM_OBJECTS, "CAM objects " + str(part.Tag)):
            entry = self.describe_object(obj)
            is_operation = isinstance(obj, self.nx.CAM.Operation)
            if is_operation:
                entry["has_toolpath"] = self.read(str(obj.Tag) + " path", obj.AskPathExists)
                entry["is_mirror"] = self.read(str(obj.Tag) + " mirror", obj.IsMirror)
                entry["geometry_parent"] = self.read(str(obj.Tag) + " parent", lambda: self.reference(
                    obj.GetParent(self.nx.CAM.CAMSetup.View.Geometry).Tag))
            entry["parameters"] = self.parameters(obj)
            entry["geometry"] = self.geometry(obj)
            entry["custom_points"] = self.custom_points(obj)
            result["objects"].append(entry)
        return result

    def inspect_part(self, part):
        result = {"id": self.parts[int(part.Tag)], "tag": int(part.Tag),
                  "leaf": str(getattr(part, "Leaf", "")),
                  "fully_loaded": self.read("part loading", lambda: part.IsFullyLoaded),
                  "modified_before": self.read("modified before", lambda: part.IsModified)}
        if result["fully_loaded"] is not True:
            result["content"] = "Not inspected: part is not fully loaded. No automatic load was requested."
            return result
        result["units"] = self.read("units", lambda: str(part.PartUnits))
        bodies = self.read("bodies", lambda: list(part.Bodies))
        result["body_count"] = len(bodies) if isinstance(bodies, list) else bodies
        features = self.read("features", lambda: list(part.Features))
        if isinstance(features, list):
            counts = {}
            for feature in features:
                kind = self.read("feature type", lambda: feature.FeatureType)
                key = kind if isinstance(kind, str) else "<unavailable>"
                counts[key] = counts.get(key, 0) + 1
            result["feature_types"] = counts
        else:
            result["feature_types"] = features
        result["cam"] = self.cam(part)
        result["modified_after"] = self.read("modified after", lambda: part.IsModified)
        return result

    def collect(self):
        display = self.session.Parts.Display
        if display is None:
            raise ValueError("Откройте проект, который требуется зеркалить.")
        self.part_id(display)
        work = self.session.Parts.Work
        root = display.ComponentAssembly.RootComponent
        nodes = self.walk_components(root) if root is not None else []
        # Snapshot the list: references to external objects do not cause loading.
        self.records = [self.inspect_part(part) for part in list(self.part_objects)]
        return {"diagnostic": SCRIPT_NAME, "version": SCRIPT_VERSION,
                "purpose": "Inventory for an independent mirrored project; NOT a mirroring operation or compatibility certificate",
                "nx_version": self.read("NX version", lambda: self.session.GetEnvironmentVariableValue("UGII_VERSION")),
                "display_part": self.parts[int(display.Tag)],
                "work_part_tag": int(work.Tag) if work else None,
                "scope": "Displayed project and already loaded component prototypes; no automatic loading",
                "coverage": "Required UF parameters, UF CAM geometry/boundaries/custom points. Not a complete serialization of all CAM settings, WAVE links, expressions, constraints or FBM data.",
                "parts": self.records, "component_occurrences": nodes,
                "limits": self.limits, "read_issue_counts": self.issue_counts,
                "read_issue_examples": self.issues}


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
        report = Inventory(NXOpen.Session.GetSession(), NXOpen.UF.UFSession.GetUFSession(),
                           NXOpen, NXOpen.UF).collect()
        text = ("Диагностика завершена. Это сбор данных, зеркальная копия не создавалась.\r\n"
                "Нажмите «Копировать всё» и передайте текст разработчику.\r\n"
                "Недоступные данные и ограничения перечислены в конце.\r\n\r\n"
                + json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False).replace("\n", "\r\n"))
        show_result(text)
    except Exception as exc:
        NXOpen.UI.GetUI().NXMessageBox.Show(TITLE + " — Ошибка",
            NXOpen.NXMessageBox.DialogType.Error, type(exc).__name__ + ": " + str(exc))


if __name__ == "__main__":
    main()
