# -*- coding: utf-8 -*-
# NX_Open_Setup_Cards_Folder — Открыть папку карт наладки.
# SCRIPT_VERSION: V1.01
r"""Открыть карты текущего проекта — NX / Designcenter, Windows.

Запуск: Журнал / Воспроизвести (Journal / Play).
Открывает: <папка .prt>\Карты Наладки\<имя проекта без .prt>.
Совместим со структурой генератора карты наладки начиная с v1.41.
Папки и файлы не создаёт; модель не сохраняет и не изменяет.
"""

import hashlib
import os
from pathlib import Path
import re

SCRIPT_NAME = 'Открыть папку карт наладки'
SCRIPT_VERSION = 'V1.01'
SCRIPT_TITLE = SCRIPT_NAME + ' — ' + SCRIPT_VERSION


def project_name_from_part(part):
    # Точно те же правила имени, что в генераторе карты наладки v1.42.
    raw_name = getattr(part, 'FullPath', '') or part.Leaf
    name = str(raw_name).replace('\\', '/').rsplit('/', 1)[-1]
    if name.lower().endswith('.prt'):
        name = name[:-4]
    name = re.sub(r'[<>:"/\\|?*\x00-\x1f]', '_', name).rstrip(' .')
    if not name or name in ('.', '..'):
        raise RuntimeError('Не удалось определить имя проекта. Сохраните файл .prt.')
    if re.fullmatch(r'CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9]', name.split('.')[0], re.I):
        name = '_' + name
    return name


def project_folder_name(name):
    # Совпадает с safe_file_component(name, reserve_internal=True) генератора.
    original = str(name)
    result = re.sub(r'[<>:"/\\|?*\x00-\x1f]', '_', original).rstrip(' .')
    if not result or result in ('.', '..'):
        result = '_'
    if re.fullmatch(r'CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³]', result.split('.')[0], re.I):
        result = '_' + result
    if result.casefold() in ('история', 'ошибки') or result.casefold().startswith('.nx_'):
        result = '_' + result
    encoded = result.encode('utf-16-le')
    if len(encoded) > 460:
        result = encoded[:440].decode('utf-16-le', errors='ignore').rstrip(' .')
    if result != original:
        result += '~' + hashlib.sha256(original.encode('utf-8')).hexdigest()[:10]
    return result


def main():
    try:
        import NXOpen
    except ImportError:
        print(SCRIPT_TITLE + '\nЗапускайте этот файл внутри NX: Журнал / Воспроизвести.')
        return
    try:
        if os.name != 'nt':
            raise RuntimeError('Этот журнал предназначен для NX в Windows.')
        part = NXOpen.Session.GetSession().Parts.Work
        if part is None:
            raise RuntimeError('Сначала откройте проект в NX.')
        raw_path = str(getattr(part, 'FullPath', '') or '')
        project_file = Path(raw_path)
        if not raw_path or not project_file.is_absolute() or project_file.suffix.lower() != '.prt':
            raise RuntimeError('Сначала сохраните текущий проект в файл .prt.')
        folder = (project_file.parent / 'Карты Наладки'
                  / project_folder_name(project_name_from_part(part)))
        if not folder.is_dir():
            NXOpen.UI.GetUI().NXMessageBox.Show(
                SCRIPT_TITLE, NXOpen.NXMessageBox.DialogType.Information,
                'Папка карт этого проекта ещё не создана или недоступна:\n\n'
                + str(folder) + '\n\nСначала создайте карту наладки и проверьте доступ к папке.')
            return
        os.startfile(str(folder))
    except Exception as exc:
        NXOpen.UI.GetUI().NXMessageBox.Show(
            SCRIPT_TITLE, NXOpen.NXMessageBox.DialogType.Error, str(exc))


if __name__ == '__main__':
    main()
