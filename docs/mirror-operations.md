# Проверка охвата CAM-операций — V1.20

**Русский** | [English](en/mirror-operations.md) · [Руководство](mirror-project.md)

Дата проверки: **15.11.2026**. База — опубликованная V1.18, коммит `b7c881ce6850206977b249cb199f58ba01e23966`. На начало работы более новой версии в основной ветке не было. SDK и исполняемого NX / Designcenter 2606 в среде проверки нет. **Совместимость с DC 2606 и полнота охвата его операций не подтверждены.**

## Что означает каталог

Название шаблона в окне создания операции, класс объекта `NXOpen.CAM.Operation` и класс его Builder — разные сущности. Нельзя вывести имя фабрики из названия шаблона или считать один класс операции одним шаблоном. V1.20 выводит имя объекта, его фактический Python-класс, выбранную фабрику и фактический класс Builder. Название пользовательского объекта не выдаётся за название исходного шаблона.

| Уровень | Фактическое состояние |
| --- | --- |
| Общее преобразование проекта | Реализован перенос проверяемой геометрии, поддерживаемых MCS/RCS, параметрических заготовок и заданных плоскостей границ. Ссылки, идентификаторы и отражённое положение проверяются отдельно. |
| Доступные данные операций | Для допускаемых редакторов вызываются `Geometry.InitializeData(True)` и `Validate()`. Через UF читаются доступные каналы Part, Blank, Check, Trim, Cut Area, Wall, Drive, коллекторы и границы. Это общая обработка, без отдельных полных адаптеров семейств. |
| Распознавание редакторов | Сохранены 21 маршрут по классу операции и каталог из 31 Builder. При отсутствии специализированного метода используется проверяемый `CreateBuilder`. Остальные 10 редакторов распознаются только по результату общего вызова. |
| Полное отражение настроек семейства | **Ни для одного семейства не подтверждено.** В частности, наличие `CavityMillingBuilder` не означает переноса всех уровней, направлений и перемещений Cavity Milling. |
| Неизвестные и исключённые семейства | Неизвестный Builder останавливает тест. Каталог не разрешает токарные, WEDM, аддитивные, измерительные и пользовательские движения. |

Чтение Builder не подтверждает сохранение выбранных объектов: это проверяется общим снимком UF-ссылок. Отсутствующее свойство, `None` и неподдерживаемый тип поля не считаются пустым выбором; отчёт перечисляет их как непроверенные через Builder. Если свойство существует, ошибка его чтения или `InitializeData` останавливает тест. Потеря ранее доступного поля или изменение класса Builder также останавливает тест. Доступный UF-канал проверяется независимо от недоступного поля Builder.

## Маршруты, реализованные в скрипте

Это таблица текущего кода, **не результат сверки сигнатур с SDK 2606**. Точное написание методов сохранено. Каждый возвращённый Builder проверяется по каталогу, включая специализированные фабрики. Ошибка вызова фабрики не подавляется повторной попыткой через другой редактор.

| Класс объекта NXOpen | Метод OperationCollection |
| --- | --- |
| `CavityMilling` | `CreateCavityMillingBuilder` |
| `FaceMilling` | `CreateFaceMillingBuilder` |
| `PlanarMilling` | `CreatePlanarMillingBuilder` |
| `Engraving` | `CreateEngravingBuilder` |
| `ZLevelMilling` | `CreateZlevelMillingBuilder` |
| `VariableZLevelMilling` | `CreateVazlMillingBuilder` |
| `FeatureMilling` | `CreateFeatureMillingBuilder` |
| `PlungeMilling` | `CreatePlungeMillingBuilder` |
| `HoleMaking` | `CreateHoleMakingBuilder` |
| `HoleDrilling` | `CreateHoleDrillingBuilder` |
| `ThreadMilling` | `CreateThreadMillingBuilder` |
| `CylinderMilling` | `CreateCylinderMillingBuilder` |
| `VolumeBased25DMillingOperation` | `CreateVolumeBased25dMillingOperationBuilder` |
| `GrooveMilling` | `CreateGrooveMillingBuilder` |
| `ChamferMilling` | `CreateChamferMillingBuilder` |
| `RadialGrooveMilling` | `CreateRadialGrooveMillingBuilder` |
| `GenericFeatureOperation` | `CreateGenericFeatureOperationBuilder` |
| `SurfaceContour` | `CreateSurfaceContourBuilder` |
| `TpMultiAxisDeburring` | `CreateMultiAxisDeburringBuilder` |
| `TpMultiAxisWallFinishing` | `CreateMultiAxisWallFinishingBuilder` |
| `MwBLDeburringOperation` | `CreateMwBldeburringOperationBuilder` |

В общей ветке дополнительно распознаются `EdgeChamferBuilder`, `FacingBuilder`, `FacingZigZagBuilder`, `MultiAxisRoughingBuilder`, `PlanarRoughingBuilder`, `PointToPointBuilder`, `RotaryRoughingBuilder`, `SwarfBuilder`, `WallProfilingBuilder`, `ZlevelUndercutBuilder`. Соответствие их конкретным классам объектов и шаблонам DC 2606 ещё нужно установить. После допуска к ним применяются те же общие проверки, а не только проверка имени.

## Данные, которым ещё нужны преобразование или проверка

| Семейство | Что уже делает общий механизм | Что не подтверждено |
| --- | --- | --- |
| Cavity / Planar / Face / Z-level / Plunge и родственные | Открытие редактора, доступная геометрия, границы, MCS/RCS; оба канала пользовательских точек для новых семейств | Ручные уровни/плоскости резания, числовые направления, точки, заходы/отходы и переходы; семантика настроек после отражения |
| Surface Contour, многокоординатные, Swarf, Rotary, Deburring | Тот же общий механизм и доступные Drive/Wall/Check-ссылки | Полное определение оси инструмента, ориентации/наклоны, ведущая геометрия вне прочитанных каналов, локальные направления и перемещения |
| HoleMaking / HoleDrilling / Thread / Cylinder / FeatureMilling / GenericFeature | Общие ссылки и редакторы; полное определение отверстий/FBM явно отмечается как непроверенное, в том числе при общем вызове без `GetFeatureGeometry` | Список/порядок features и отверстий, оси и глубины, локальные CSYS, управляющие точки, атрибуты и данные технологических элементов |
| Все 31 Builder | Обнаруженные пользовательские стартовые/предсверлильные точки останавливают тест; недоступность канала с кодом 1345005 отмечается отдельно | Остальные числовые пространственные настройки не отражаются. Перечень локальных ненулевых 3D-параметров не охватывает все координаты, унаследованные значения и настройки семейств |

Сохранены прежние особенности опроса пользовательских точек: для `SurfaceContour` читается канал 1, для `TpMultiAxisDeburring` этот UF-запрос не выполняется, для остальных операций — каналы 0 и 1. Это ограничение охвата, а не доказательство отсутствия иных точек у этих двух классов; применимость каналов требует проверки в SDK 2606.

## Что сверить по SDK DC 2606

1. Все 21 фабрику из таблицы и `OperationCollection.CreateBuilder(CAMObject)`: наличие в Python, аргументы, фактические возвращаемые классы и правила выбора редактора для разных шаблонов одного класса операции. Наличие в старом C++ или .NET API не доказывает наличие в Python 2606.
2. Все 31 класса из [каталога](mirror-project.md#каталог-редакторов-фрезерных-и-сверлильных-операций), их наследование и дополнительные редакторы 2606. Сопоставить с установленными шаблонами отдельно; автоматически добавлять найденные имена в разрешённый список нельзя.
3. `Geometry.InitializeData(True)`, `Builder.Validate()`, `Builder.Destroy()` и наличие свойств/дескрипторов, включая поведение `dir()`. Проверить типы/доступность семи полей `*Geometry`, поведение пустых и неактивных полей, а также `GeometryGroup` и её режимы/припуски. Например, `TrimBoundary` — самостоятельное определение, его нельзя считать синонимом отсутствующего `TrimGeometry`.
4. `CavityMillingBuilder.CutLevel`, связанные уровни/плоскости и перемещения; современные определения оси инструмента и ведущей геометрии поверхностных/многокоординатных редакторов. Старый `SurfaceContourBuilder.ToolAxis` встречается в списке устаревших API; перенос по догадке не реализован.
5. `GetFeatureGeometry()` и `NXOpen.CAM.FBM.FeatureGeometry`, определения отверстий, резьб и технологических элементов: способы перечисления, ссылки, точки/оси, системы координат и единицы. Вызов метода сейчас используется только как признак дополнительного непроверенного определения, его содержимое не читается.
6. Объявления и применимость `UF_CAMGEOM_ask_items`, `UF_CAMGEOM_ask_geom_provider`, `UF_CAMGEOM_ask_collector_items`, `UF_CAMGEOM_ask_custom_points`; `UF_CAMBND_ask_boundaries`, `UF_CAMBND_ask_boundary_data`, `UF_CAMBND_ask_boundary_items`, `UF_CAMBND_set_boundary_plane`; структуры, размеры, владение памятью и код 1345005 в заголовках 2606. Отдельно проверить каналы пользовательских точек для SurfaceContour и TpMultiAxisDeburring.
7. `UF_PARAM_ask_required_params` / Python `AskRequiredParams`, `AskParamAttributes`, типы 3D/3DLength/tag/VLA-tag, `UF_PARAM_ask_param_status`, `UF_PARAM_ask_param_definer`, значения параметров MCS/RCS 59/60 и связи 41. Нужен смысл и система отсчёта каждого переносимого параметра, а не только его тип.
8. Для общего допуска модельной истории V1.20: Python `NXOpen.Features.BodyFeature`, фактические классы из `Body.GetFeatures()`, `BodyFeature.GetBodies()`, `Feature.GetParents()`, `GetExpressions()`, `GetFeatureErrorMessages()`, `Expression.GetUsingFeatures()` и `GetReferencingExpressions()`. Проверить полноту данных для булевых, многотельных, подавленных и пользовательских построений и поведение `UF_MODL_transform_entities` при сохранении/замене истории. [Правила и ограничения](mirror-project.md#обработка-модельных-элементов).

Эта сверка не заменяет запуск в NX. Для нового семейства нужен тестовый PRT с непустыми специфическими настройками, проверка выбора и осей, ручное сохранение и повторное открытие копии, затем штатный пересчёт и проверка траекторий. Исходник должен оставаться неизменным; автоматического сохранения и дополнительных файлов скрипт не создаёт.

## Источники и пределы проверки

- [Siemens NXOpen OperationCollection, воспроизведение справочника 2023 года на Tech PLM](https://docs.tech-plm.ru/documentation/external/PL20230425447184281/en-US/custom_api/nx_open_c_plus_plus_ref/a50101.html) — источник каталога V1.18; это не документация 2606.
- [CavityMillingBuilder](https://docs.tech-plm.ru/documentation/external/PL20230425447184281/en-US/custom_api/nx_open_c_plus_plus_ref/a48265.html) и [HoleDrillingBuilder](https://docs.tech-plm.ru/documentation/external/PL20230425447184281/en-US/custom_api/nx_open_c_plus_plus_ref/a49073.html) из того же справочника показывают самостоятельные определения CutLevel и FBM. В текущей проверке доступны поисковые выдержки; полные страницы Tech PLM возвращали 502.
- [Siemens SurfaceContourBuilder, воспроизведение старого справочника на ugapi](https://www.ugapi.com/doc/nxopen/a02024.html) и [список устаревших API](https://www.ugapi.com/doc/NXOpen/a10908.html) — дополнительное основание не переносить старые сигнатуры механически в 2606.
- [Официальный портал документации Siemens NX](https://docs.sw.siemens.com/en-US/product/209349590) не удалось прочитать используемым инструментом. Доступных исходников SDK 2606 для точной сверки нет.

V1.20: **244 регрессионные проверки вне NX**, в том числе 32 с отдельно скомпилированной C-библиотекой. Сохранены проверки каталога V1.19; 16 новых сценариев относятся к общему допуску модельной истории и защитным отказам. Каталог операций остаётся 21 маршрутом / 31 Builder. C-библиотека — тестовая реализация контракта, не библиотека Siemens. Эти результаты не подтверждают совместимость с NX или корректность обработки.
