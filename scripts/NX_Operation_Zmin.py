# -*- coding: utf-8 -*-
# Минимальный Z в именах операций
# SCRIPT_VERSION: V1.01
# Рабочее имя файла: NX_Operation_Zmin.py
SCRIPT_VERSION = "V1.01"
SCRIPT_NAME = "Минимальный Z в именах операций"

# Запуск: NX / Designcenter -> Журнал -> Воспроизвести.
# Выделены операции: только они. Выделения нет: все операции рабочей детали.
# V1.01: для 4 осей Z считается по текущей оси инструмента в каждой CL-точке.
# Начало СКС операции должно находиться на оси вращения детали.
# Трёхосевая траектория: прежний расчёт Z в СКС операции.
# Единицы CAM-детали; четыре знака после точки без незначащих нулей.
# Дуги/винты с постоянной осью: аналитический минимум между CL-точками.
# Дуги/винты с изменением оси без данных интерполяции: операция пропускается.
# Старый конечный суффикс _Zчисло заменяется. Траектории не пересчитываются.
# Нет файлов настроек, отчётов, журналов, резервных копий или записи в Temp.
# PRT сохраняет пользователь. Переименование можно отменить в NX через Ctrl+Z.

import math
import re

_CREDIT = bytes((98, 121, 32, 64, 84, 111, 110, 121, 95, 70, 111, 120, 120, 120)).decode("ascii")
TITLE = SCRIPT_NAME + " — " + SCRIPT_VERSION + " | " + _CREDIT
_Z_SUFFIX = re.compile(r"(?:_Z[+-]?(?:[0-9]+(?:[.,][0-9]*)?|[.,][0-9]+))+$", re.IGNORECASE)


def message(text, kind="info"):
    """Native Windows window: the actual caption always includes the version."""
    import ctypes
    user32 = ctypes.windll.user32
    user32.GetActiveWindow.restype = ctypes.c_void_p
    user32.MessageBoxW.argtypes = (ctypes.c_void_p, ctypes.c_wchar_p,
                                  ctypes.c_wchar_p, ctypes.c_uint)
    user32.MessageBoxW.restype = ctypes.c_int
    icon = {"info": 0x40, "warning": 0x30, "error": 0x10}[kind]
    suffix = {"info": "Результат", "warning": "Внимание", "error": "Ошибка"}[kind]
    user32.MessageBoxW(user32.GetActiveWindow(), str(text),
                       TITLE + " — " + suffix, icon | 0x10000)


# Векторные функции и расчёт дуг сохранены из карты наладки V2.35.

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
    """NX stores rotary tool vectors in the XYZ+IJK ('Five') path format.

    This storage format does not imply that the machine has five axes.
    Never assume a three-axis path when the format cannot be read.
    """
    axis_type = getattr(nx.CAM, 'CamPathToolAxisType', None)
    try:
        mode = enum_name(path.ToolAxisType, axis_type, ('Three', 'Five'))
    except Exception as exc:
        raise ValueError('Не удалось определить формат оси инструмента: ' + str(exc))
    if mode not in ('Three', 'Five'):
        raise ValueError('Неизвестный формат оси инструмента: ' + str(mode))
    return mode


def motion_z_axis(motion, mode, mcs_z):
    if mode == 'Three':
        # Keep the established 3-axis calculation, including tilted MCS.
        return mcs_z
    try:
        return unit(path_point(motion.ToolAxis))
    except Exception as exc:
        # Falling back to fixed MCS Z here would recreate the +/-150 error.
        raise ValueError('Недоступно направление оси инструмента в траектории: ' + str(exc))


def toolpath_zmin(nx, operation, basis, origin, progress=None):
    """Read the existing CL path; never generate, post or modify it.

    Three-axis paths keep their original MCS calculation. For XYZ+IJK paths,
    EndPoint and ToolAxis are read in the same work-part frame. With MCS origin
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
    mode = path_axis_mode(nx, path)
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
                    z_axis = motion_z_axis(motion, mode, basis[2])
                    value = dot(tuple(end[i] - origin[i] for i in range(3)), z_axis)
                    if shape != shapes.Linear:
                        if mode == 'Five' and previous_axis is not None:
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


def select_operations(nx, ui, uf, all_operations, all_groups):
    """An unreadable selection must never become permission to rename all."""
    operations_by_tag = {str(op.Tag): op for op in all_operations}
    known_tags = set(operations_by_tag) | {str(group.Tag) for group in all_groups}
    navigator_error = None
    selected_tags = None
    try:
        count, tags = uf.UiOnt.AskSelectedNodes()
        count = int(count)
        tags = list(tags) if tags is not None else []
        if count < 0 or count != len(tags):
            raise RuntimeError("NX вернул неполный список выбранных узлов.")
        if count:
            selected_tags = {str(tag) for tag in tags}
            if not selected_tags <= known_tags:
                raise RuntimeError("В выделении есть узлы другого CAM-проекта.")
    except Exception as exc:
        navigator_error = exc
        selected_tags = None

    if selected_tags is None:
        manager = ui.SelectionManager
        selected = [manager.GetSelectedTaggedObject(index)
                    for index in range(manager.GetNumSelectedObjects())]
        selected_tags = {str(obj.Tag) for obj in selected}
        for obj in selected:
            if isinstance(obj, nx.CAM.Operation) and str(obj.Tag) not in operations_by_tag:
                raise RuntimeError("Выделена операция другой детали. Откройте её CAM-проект.")
        if navigator_error is not None and not (selected_tags & set(operations_by_tag)):
            raise RuntimeError("Не удалось надёжно прочитать выделение в навигаторе.\n"
                               "Выделите нужные операции и повторите запуск.\n\n" + str(navigator_error))

    if not selected_tags:
        return list(all_operations), "Все операции проекта"
    chosen = [op for op in all_operations if str(op.Tag) in selected_tags]
    if not chosen:
        raise RuntimeError("Выделены папки или другие объекты, но не операции.\n\n"
                           "Выделите операции либо полностью снимите выделение, "
                           "чтобы обработать весь проект.")
    return chosen, "Выделенные операции"


def zmin_text(value):
    # Exactly the setup-card display precision; suppress negative zero.
    text = ("%.4f" % finite_number(value)).rstrip("0").rstrip(".")
    return "0" if text == "-0" else text


def name_with_zmin(name, value):
    base = _Z_SUFFIX.sub("", str(name))
    if not base:
        raise ValueError("После удаления старого суффикса Z имя операции пустое.")
    return base + "_Z" + zmin_text(value)


def prepare_names(nx, part, operations):
    """Read every path before changing names; one bad path skips one operation."""
    planned, skipped, unchanged = [], [], 0
    frames = {}
    frame_errors = {}
    status_type = getattr(getattr(nx.CAM, "CAMObject", None), "Status", None)
    for operation in operations:
        old_name = str(operation.Name)
        try:
            if not operation.AskPathExists():
                raise ValueError("Нет рассчитанной траектории.")
            status = enum_name(operation.GetStatus(), status_type,
                               ("Complete", "Approved", "Regen", "Repost"))
            if status == "Regen":
                raise ValueError("Траектория устарела: требуется пересчёт в NX.")
            group = nearest_mcs(nx, operation)
            if group is None:
                raise ValueError("Не найдена СКС операции.")
            key = str(group.Tag)
            if key in frame_errors:
                raise ValueError(frame_errors[key])
            if key not in frames:
                try:
                    frames[key] = read_mcs(part, group)
                except Exception as exc:
                    frame_errors[key] = "Не удалось прочитать СКС: " + str(exc)
                    raise ValueError(frame_errors[key])
            basis, origin = frames[key]
            value = toolpath_zmin(nx, operation, basis, origin)
            if value is None:
                raise ValueError("В траектории нет доступных перемещений.")
            new_name = name_with_zmin(old_name, value)
            if new_name == old_name:
                unchanged += 1
            else:
                planned.append({"op": operation, "tag": str(operation.Tag),
                                "old": old_name, "new": new_name})
        except Exception as exc:
            skipped.append((old_name, str(exc)))
    return planned, skipped, unchanged


def remove_name_conflicts(planned, all_operations, all_groups):
    """Skip ambiguous names without adding numbering or altering the base name."""
    targets = {}
    for item in planned:
        targets.setdefault(item["new"].casefold(), []).append(item)
    skipped, remaining = [], []
    for item in planned:
        if len(targets[item["new"].casefold()]) > 1:
            skipped.append((item["old"], "Несколько операций получат имя «" + item["new"] + "»."))
        else:
            remaining.append(item)

    # Removing a blocked rename may make its old name block another rename.
    while remaining:
        moving = {item["tag"] for item in remaining}
        occupied = {str(op.Name).casefold() for op in all_operations if str(op.Tag) not in moving}
        occupied.update(str(group.Name).casefold() for group in all_groups)
        blocked = [item for item in remaining if item["new"].casefold() in occupied]
        if not blocked:
            break
        blocked_tags = {item["tag"] for item in blocked}
        for item in blocked:
            skipped.append((item["old"], "Имя «" + item["new"] + "» уже занято в CAM-проекте."))
        remaining = [item for item in remaining if item["tag"] not in blocked_tags]
    return remaining, skipped


def set_operation_name(operation, name):
    operation.SetName(name)
    actual = str(operation.Name)
    if actual != name:
        raise RuntimeError("NX изменил запрошенное имя «" + name + "» на «" + actual + "».")


def rename_operations(nx, session, planned, all_operations, all_groups):
    if not planned:
        return
    # Reserve every current and final name before the first mutation.
    reserved = {str(obj.Name).casefold() for obj in list(all_operations) + list(all_groups)}
    reserved.update(item["new"].casefold() for item in planned)
    temporary = []
    for index in range(len(planned)):
        number = index + 1
        while True:
            name = "ZMIN_TMP_" + str(number)
            if name.casefold() not in reserved:
                break
            number += 1
        reserved.add(name.casefold())
        temporary.append(name)

    mark = session.SetUndoMark(nx.Session.MarkVisibility.Visible, TITLE)
    try:
        # Temporary object names only: no temporary files or directories.
        for item, name in zip(planned, temporary):
            set_operation_name(item["op"], name)
        for item in planned:
            set_operation_name(item["op"], item["new"])
    except Exception as exc:
        try:
            session.UndoToMark(mark, TITLE)
            unrestored = [item["old"] for item in planned if str(item["op"].Name) != item["old"]]
            if unrestored:
                raise RuntimeError("Не восстановлены имена: " + ", ".join(unrestored))
        except Exception as rollback_exc:
            raise RuntimeError("Переименование прервано. Автоматическая отмена не завершена.\n"
                               "Проверьте имена операций и выполните отмену в NX.\n\n"
                               + str(exc) + "\n\nОшибка отмены: " + str(rollback_exc))
        raise RuntimeError("Переименование прервано. Имена операций восстановлены.\n\n" + str(exc))


def show_result(scope, total, renamed, unchanged, skipped, refresh_error=None):
    lines = [scope + ".", "Обработано: " + str(total) + ".",
             "Переименовано: " + str(renamed) + ".",
             "Уже актуальны: " + str(unchanged) + ".",
             "Пропущено: " + str(len(skipped)) + "."]
    if renamed:
        lines.extend(("", "Отмена переименования: Ctrl+Z."))
    if refresh_error:
        lines.extend(("", "Имена изменены, но обновить навигатор не удалось: " + str(refresh_error)))
    if skipped:
        lines.extend(("", "Пропущенные операции:"))
        for name, reason in skipped[:8]:
            lines.append("• " + name + " — " + reason)
        if len(skipped) > 8:
            lines.append("И ещё " + str(len(skipped) - 8) + " операций.")
    message("\n".join(lines), "warning" if skipped or refresh_error else "info")


def main():
    try:
        import NXOpen
        import NXOpen.CAM
        import NXOpen.UF
        nx = NXOpen
        session = nx.Session.GetSession()
        ui = nx.UI.GetUI()
        uf = nx.UF.UFSession.GetUFSession()
        part = session.Parts.Work
        if part is None:
            raise RuntimeError("Откройте CAM-проект в NX и повторите запуск.")
        display = session.Parts.Display
        if display is None or str(part.Tag) != str(display.Tag):
            raise RuntimeError("CAM-проект должен быть рабочей и отображаемой деталью.\n"
                               "Откройте его в отдельном окне NX и повторите запуск.")
        setup = part.CAMSetup
        if setup is None:
            raise RuntimeError("В рабочей детали нет CAM-проекта.")
        all_operations = list(setup.CAMOperationCollection)
        all_groups = list(setup.CAMGroupCollection)
        operations, scope = select_operations(nx, ui, uf, all_operations, all_groups)
        if not operations:
            message("В текущем CAM-проекте нет операций.")
            return
        planned, skipped, unchanged = prepare_names(nx, part, operations)
        planned, conflicts = remove_name_conflicts(planned, all_operations, all_groups)
        skipped.extend(conflicts)
        rename_operations(nx, session, planned, all_operations, all_groups)
        refresh_error = None
        if planned:
            try:
                uf.UiOnt.Refresh()
            except Exception as exc:
                refresh_error = exc
        show_result(scope, len(operations), len(planned), unchanged, skipped, refresh_error)
    except Exception as exc:
        message(str(exc), "error")


if __name__ == "__main__":
    main()
