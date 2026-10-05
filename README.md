# NXOpen-SCRIPTS

Журналы NXOpen для Siemens NX / Designcenter: CAM, карты наладки, оформление чертежей и экспорт. **by @Tony_Foxxx**

GitHub — центральный источник обновлений. Рабочая папка: `C:\ProgramData\2. NX_Scripts`. Основные имена файлов постоянны; версия записана внутри в `SCRIPT_VERSION`. Лицензия исходников — [MIT](LICENSE), сведения о сторонних ресурсах — в [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Совместимость

Проект ведётся для **NX 2506.8100** и **Designcenter 2606 Build 4002** на Windows. Поддержка других сборок не заявляется. Это целевые среды проекта: проверки Python в CI не подтверждают работу каждого журнала в обеих сборках NX.

Апдейтер и manifest проверяются автоматическими тестами на Windows и Linux. [Результаты проверки](https://github.com/TonyFoxxxx/NXOpen-SCRIPTS/actions). Работа журналов и их окон внутри NX проверяется отдельно на ваших проектах: NXOpen и графическое окно NX в CI не запускаются.

## Скрипты

| Файл | Версия | Назначение | Руководство |
| --- | --- | --- | --- |
| [NX_Setup_Prototype.py](scripts/NX_Setup_Prototype.py) | V2.37 | Создание редактируемой карты наладки с видами, инструментами и операциями. | [Все функции](docs/setup-card.md) |
| [NX_Postprocess_To_Machine.cs](scripts/NX_Postprocess_To_Machine.cs) | V1.35 | Вывод управляющих программ и FANUC BIN в папку станка или на носитель. | [Все функции](docs/postprocess.md) |
| [NX_Operation_Zmin.py](scripts/NX_Operation_Zmin.py) | V1.02 | Добавление минимального Z траектории к именам операций. | [Все функции](docs/zmin.md) |
| [NX_Rename_Operations_In_Selected_Folder.cs](scripts/NX_Rename_Operations_In_Selected_Folder.cs) | V1.04 | Переименование операций с нумерацией по порядку дерева CAM. | [Все функции](docs/rename-operations.md) |
| [NX_Number_Program_Folders.cs](scripts/NX_Number_Program_Folders.cs) | V1.03 | Нумерация папок программ по порядку дерева с учётом XLSX-реестра. | [Все функции](docs/number-program-folders.md) |
| [NX_Tool_D_To_Description.cs](scripts/NX_Tool_D_To_Description.cs) | V1.06 | Заполнение описаний инструментов диаметром и номерами T, H, D. | [Все функции](docs/tool-description.md) |
| [NX_ESKD_Format_GOST_A.cs](scripts/NX_ESKD_Format_GOST_A.cs) | V1.35 | Оформление рамки, основной надписи и аннотаций чертежа по ЕСКД. | [Все функции](docs/eskd.md) |
| [NX_Export_Drawing_To_PDF.cs](scripts/NX_Export_Drawing_To_PDF.cs) | V1.11 | Экспорт текущего листа в PDF с настройкой толщин линий. | [Все функции](docs/export-pdf.md) |
| [NX_Export_Current_View_To_DXF.cs](scripts/NX_Export_Current_View_To_DXF.cs) | V1.08 | Экспорт рёбер, внешнего контура или выбранных кривых в DXF. | [Все функции](docs/export-dxf.md) |
| [NX_Rename_Assemblies.cs](scripts/NX_Rename_Assemblies.cs) | V1.05 | Переименование файлов деталей и сборок с обновлением ссылок компонентов. | [Все функции](docs/rename-assemblies.md) |
| [NX_Open_Project_Folder.cs](scripts/NX_Open_Project_Folder.cs) | V1.01 | Открытие папки текущего PRT в Проводнике. | [Все функции](docs/open-project-folder.md) |
| [NX_Open_Setup_Cards_Folder.py](scripts/NX_Open_Setup_Cards_Folder.py) | V1.01 | Открытие папки карт наладки текущего проекта. | [Все функции](docs/open-setup-cards-folder.md) |
| [NX_Update_Script_Buttons.py](scripts/NX_Update_Script_Buttons.py) | V1.11 | Установка и обновление выбранных скриптов из GitHub или папки. | [Все функции](docs/updater.md) |

Каждое руководство содержит возможности, порядок запуска, настройки, результат и ограничения. История редакций находится в [CHANGELOG.md](CHANGELOG.md).

**NEW PROJECT в публичный набор не включён.** C#-журналы сохраняют расширение `.cs`; преобразовывать их в Python не нужно.

### Zmin в именах операций

**Исправление V1.02:** диагностика выявила наклонённую ось инструмента в траекториях формата `Three`. V1.01 ошибочно использовала для них Z системы MCS. Теперь фактический вектор `ToolAxis` учитывается в обоих форматах хранения — `Three` и `Five`. Проверена переносимая логика на диагностических данных; новая версия требует контрольного запуска в NX.

Выделите операции и запустите `NX_Operation_Zmin.py`. Если выделения нет, журнал обрабатывает все операции рабочей детали. Старый конечный суффикс заменяется, например `CONTOUR_Z-1` → `CONTOUR_Z-2.5`. Переименование отменяется через `Ctrl+Z`; сохраняйте PRT обычным способом.

Z определяется проекцией положения CL-точки относительно начала MCS операции на текущее направление инструмента. **Начало MCS должно находиться на оси вращения детали.** При совпадении `ToolAxis` с Z системы MCS результат обычной трёхосевой обработки сохраняется. Операции с недоступной или устаревшей траекторией, конфликтом имён либо неподдерживаемым движением пропускаются с объяснением. Траектории не пересчитываются, отдельный INI не требуется. Подробнее: [docs/zmin.md](docs/zmin.md).

Столбец Zmin карты наладки сохраняет расчёт в MCS операции; исправление V1.02 относится к отдельному журналу переименования.

## Установка

1. Создайте `C:\ProgramData\2. NX_Scripts`, если папки ещё нет. Нужны права записи текущего пользователя.
2. Скачайте [Raw апдейтера V1.11](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/scripts/NX_Update_Script_Buttons.py) как `NX_Update_Script_Buttons.py` в эту папку. При переходе с V1.08 или V1.09 закройте его окно и замените файл один раз вручную. С V1.10 можно обновиться через сам апдейтер.
3. **Существующий `NX_Update_Script_Buttons.ini` сохраните.** V1.11 прочитает старые пути и включит GitHub, если раздела `[Source]` ещё нет. Для новой установки можно скопировать [пример](config/NX_Update_Script_Buttons.example.ini), убрав `.example`, либо создать INI кнопкой «Проверить».
4. Запустите журнал в NX через **Tools → Journal → Play / Инструменты → Журнал → Воспроизвести** (`Alt+F8`, если сочетание назначено в вашей настройке).
5. Проверьте рабочую папку и источник, нажмите «Проверить». Отметьте нужные новые скрипты или обновления и нажмите «Применить выбранное».
6. Перед первым использованием заполните свои INI. Для нумератора назначьте согласованный диапазон; для ЕСКД обеспечьте совместимый внешний шрифт.

Апдейтер не создаёт кнопки NX или New User Command. Существующие кнопки продолжают ссылаться на те же локальные пути. Настройка кнопок и ручная установка — в [docs/installation.md](docs/installation.md).

## INI

В GitHub хранятся только `*.example.ini` с нейтральными значениями. Рабочие INI, история нумерации и последние выборы пользователя остаются на ПК. Обычное обновление скрипта их не заменяет.

| Скрипт | Пример INI | Рабочие настройки |
| --- | --- | --- |
| Карта наладки | [Скачать](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/config/NX_Setup_Prototype.example.ini) | `NX_Setup_Prototype.ini` рядом со скриптом; `[SetupCard] programmer`. |
| Постпроцессирование | [Скачать](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/config/NX_Postprocess_To_Machine.example.ini) | `NX_Postprocess_To_Machine.ini` рядом со скриптом; свои посты и папки станков. |
| ЕСКД | [Скачать](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/config/NX_ESKD_Settings.example.ini) | `NX_ESKD_Settings.ini` рядом со скриптом; надписи и оформление. |
| Нумерация | [Скачать](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/config/NX_Numbering_Settings_v1.0.example.ini) | `C:\ProgramData\3_NX_DATA\NX_Numbering_Settings_v1.0.ini` и реестр из `RegistryFile`. |
| Апдейтер | [Скачать](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/config/NX_Update_Script_Buttons.example.ini) | `NX_Update_Script_Buttons.ini` рядом со скриптом; источник и рабочая папка. |

При ручной установке уберите `.example` из имени INI. При установке нового скрипта апдейтер делает это сам и сохраняет уже существующие настройки.

Подробности: [docs/configuration.md](docs/configuration.md).

## Обновления

При проверке скачивается только небольшой [manifest.json](https://raw.githubusercontent.com/TonyFoxxxx/NXOpen-SCRIPTS/main/manifest.json). Апдейтер читает локальные версии и показывает список. Исходники скачиваются после выбора; перед установкой проверяются SHA-256, внутренние версии и допустимые пути.

Новые скрипты и файлы с местными правками требуют выбора пользователя. Более старая версия не устанавливается поверх новой. Сам апдейтер заменяется после закрытия окна и завершения потоков; новую редакцию нужно запустить повторно. Файлы вне каталога не удаляются.

Установленный скрипт получает служебный комментарий с контрольной суммой в конце. Он позволяет заметить местные изменения; отдельный файл для этого не создаётся.

[Алгоритм апдейтера](docs/updater.md) · [История изменений](CHANGELOG.md) · [Добавление нового скрипта](docs/publishing.md).

## Структура

| Путь | Содержимое |
| --- | --- |
| `scripts/` | Журналы с постоянными именами. |
| `config/` | Публичные примеры INI. |
| `templates/` | Пустой реестр только для первой установки. |
| `manifest.json` | Каталог, версии, пути и SHA-256. |
| `docs/` | Руководства по каждому скрипту, установка, настройки и публикация. |
| `tools/`, `tests/`, `.github/workflows/` | Проверка публикации; устанавливать в NX не нужно. |

## Проверка перед публикацией

Для инструмента проверки и тестов нужен Python 3.11 или новее. Отдельный Python для воспроизведения журналов внутри NX не требуется.

```console
python tools/manifest.py --check
python -m unittest discover -s tests -v
```

Производственные постпроцессоры, модели, личные настройки и история работы в репозиторий не входят. Перед публикацией своих изменений также проверяйте пути и личные данные.
