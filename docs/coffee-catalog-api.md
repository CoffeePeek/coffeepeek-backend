# Каталог кофе: запуск и API

Реализовано 5 октября 2026 года. Каталог хранится в Shops; фотографии — в существующем Media/MinIO. В репозитории нет клиентского приложения. Миграция и серверный таймер подготовлены, в рабочую БД не применены и на VPS не активированы.

## Запуск

1. Применить `AddCoffeeCatalogAndRoasterTags` обычной командой миграции Shops из `CLAUDE.md`. Она создаёт девять таблиц и 30 стран; Республика Конго имеет код `CG`. Существующий `CoffeeBean` не переносится. Дополнительная миграция Media не требуется: используется существующая таблица `Photos`.
2. Заполнить [roaster-mapping.example.json](../dev/coffee-import/roaster-mapping.example.json) реальными GUID существующих обжарщиков. Добавить раздел `CoffeeImportOptions` в конфигурацию Shops (`appsettings.Production.json` либо эквивалентную конфигурацию развёртывания). Сам файл примера автоматически не загружается. Нулевые GUID — заглушки и не принимаются импортом. Бренд сопоставляется точно; неизвестный бренд и отсутствующий обжарщик отклоняются. В магазине Сова нужны два отдельных обжарщика: KOF и Kanga.
3. В Shops и Media задать одинаковый случайный `CoffeeImportAuthOptions__Token` длиной не менее 32 символов. Пустой токен отключает служебные endpoints. Настроить существующие `MinIOOptions` и `MediaPublicUrlOptions`; бакет Shops должен быть доступен для записи из Media и чтения по публичному адресу. `MediaPublicUrlOptions__ImageProxyEndpoint` добавляет URL размеров через imgproxy.
4. Джобе передать тот же токен и внутренние адреса сервисов через [coffee-import.env.example](../dev/coffee-import/coffee-import.env.example). Эти переменные задаются процессу Python отдельно от процессов .NET. Служебные маршруты не проксируются через Gateway.
5. Выполнить `python -X utf8 dev/coffee-import/import_coffees.py --source roast.by --push`, проверить `/api/admin/coffee-import/runs` и черновики. Затем включить ежедневный systemd timer по [README](../dev/coffee-import/README.md). Плановый запуск — 03:00 UTC, пропущенный запуск выполняется после старта сервера.

Новые товары всегда `Draft`. Администратор проверяет страны и характеристики, затем устанавливает `Published`. Последующие успешные импорты обновляют опубликованный товар без повторной публикации; защищённые поля остаются ручными. Теги из предложенного списка не создаются автоматически — справочник заполняется после выбора пользователя.

## Пользовательские маршруты через Gateway

Ответы используют стандартный `Response<T>` проекта; содержимое находится в `data`.

| Метод и маршрут | Результат |
| --- | --- |
| `GET /api/Catalogs/coffee-origin-countries` | `code`, `nameRu`, `nameEn`; код используется клиентом для флагов |
| `GET /api/Catalogs/roaster-tags` | Активные теги со стабильным slug |
| `GET /api/roasters?tags=decaf&page=1&pageSize=20` | Обжарщики, управляемые теги, число доступных опубликованных кофе, время обновления |
| `GET /api/roasters/{slug}` | Существующий профиль с полями `tags`, `availableCoffeeProducts`, `coffeeCatalogUpdatedAtUtc` |
| `GET /api/roasters/{slug}/coffees` | Опубликованные кофе этого обжарщика |
| `GET /api/coffees` | Общий опубликованный каталог |
| `GET /api/coffees/{slug}` | Карточка кофе и варианты покупки |

Фильтры каталога: `roasterSlug`, `search`, повторяемый `countries=ET&countries=CO`, `productKind=roasted_beans|green_beans`, `processing`, `brew`, `weightGrams`, `currency=BYN|RUB`, `minPrice`, `maxPrice`, `availableOnly` (по умолчанию `true`), `page`, `pageSize` (1–100). Несколько стран — OR, несколько тегов обжарщика — AND. Фильтры веса, цены, валюты и наличия проверяются у одного варианта. Для диапазона цен обязательно указать валюту.

Карточка содержит `address`, `roaster`, `content`, `countries`, `photos`, `variants`, `updatedAtUtc`. `photos[]` имеет `fullUrl`, `urls` (размеры существующего `PhotoUrlsDto`), `isPrimary`, `sortIndex`. Это ссылки на сохранённые файлы Media, доступные независимо от исходного сайта. `photos=[]` означает отсутствие фотографии; клиент показывает заглушку.

Вариант содержит `weightGrams` (может быть `null`), `price`, `currency`, `grind`, `roastPurpose`, `availability`, `availabilityScope`, `sellerName`, `sourceUrl`, `checkedAtUtc`. Наличие: `InStock`, `OutOfStock`, `Unknown`; область: `online` или `in_store`. River даёт наличие в кофейне. Оформление заказа происходит по `sourceUrl` на сайте продавца. Изменения каталога читаются из БД со следующего запроса, ответы не кешируются.

## Административные маршруты

Требуются существующие роли Moderator/Admin. GUID используются в административных операциях.

| Метод и маршрут | Действие |
| --- | --- |
| `GET /api/admin/coffees?status=Draft` | Черновики, предупреждения, фотографии и версия карточки |
| `GET /api/admin/coffees/{id}` | Полная карточка для редактирования |
| `POST /api/admin/coffees` | Создание ручного черновика: `roasterId`, `content`, `countryCodes` |
| `PATCH /api/admin/coffees/{id}` | Полные `content`, `countryCodes`, `protectedFields`, `status`, последняя `version` |
| `DELETE /api/admin/coffees/{id}` | Архивирование без удаления источников и фото |
| `GET/POST /api/admin/roaster-tags` | Справочник / создание: `slug`, `name`, `description`, `sortOrder` |
| `PATCH /api/admin/roaster-tags/{id}` | `name`, `description`, `sortOrder`, `isActive` |
| `DELETE /api/admin/roaster-tags/{id}` | Деактивация; назначения сохраняются, публично скрыты |
| `GET /api/admin/roasters/{id}/tags` | Назначенные теги, включая неактивные |
| `PUT /api/admin/roasters/{id}/tags` | Замена назначений: `{ "tagIds": [...] }`; допускаются активные теги |
| `GET /api/admin/coffee-import/runs?limit=30` | Применённые/неуспешные снимки и ошибки |

`protectedFields` использует имена: `Name`, `Description`, `ProductKind`, `ProductForm`, `CompositionKind`, `Processing`, `RoastLevel`, `Acidity`, `Body`, `QGraderScoreRaw`, `TasteDescriptors`, `BrewRecommendations`, `GrindOptions`, `Features`, `Countries`, `Photos`. Защита `Photos` сохраняет текущую галерею при следующем импорте. Цены и наличие остаются данными источника. Несвежая `version` при сохранении возвращает 409: клиент перечитывает карточку.

Администратор с ролью Admin может менять slug через существующий `/api/admin/coffees/public-addresses/{id}` по контракту публичных адресов проекта; прежние ссылки разрешаются через aliases.

## Внутренний обмен

Требуется заголовок `X-Coffee-Import-Token`. Доступ — по внутренней сети напрямую к Media/Shops.

| Endpoint | Контракт |
| --- | --- |
| `POST /internal/coffee-photos` в Media | Multipart `file`, растровое фото до 8 MiB → `id`, `storageKey`, `fileName`, `contentType`, `sizeBytes` |
| `POST /internal/coffee-import/snapshots` в Shops | `{ "sourceKey": "roast.by", "snapshotId": "<sha256>", "snapshot": {...} }`, до 16 MiB |
| `POST /internal/coffee-import/failures` в Shops | `sourceKey`, `snapshotId`, `collectedAtUtc`, `stage=collect|photos|apply`, `error` |

Сначала фотографии сохраняются в MinIO, затем их подтверждённые метаданные добавляются к снимку в `storedPhotos`. Повторное содержимое использует ключ `coffees/<sha256>.<ext>`. Применение одного источника транзакционное; повтор идентичного снимка возвращает прежний результат. Новый устаревший снимок отклоняется. Старый уже применённый идентификатор остаётся идемпотентным. Изменённое содержимое под прежним идентификатором отклоняется.

Страны, извлечённые только из названия, требуют подтверждения и остаются предупреждениями. Неизвестные коды не пополняют справочник автоматически. Отсутствие поля/фото в очередном снимке не очищает прежнее значение. Для сайтов без независимого счётчика исчезнувшая карточка не снимается автоматически; явно недоступные карточки переводятся в `OutOfStock`, проверенное исчезновение — в `Unknown`.

Проверки: импорт 15 сохранённых каталогов (447 карточек, 886 вариантов), повтор снимка, сохранение ручных полей/тегов/фото, публикация/архивирование, проверка денег и фильтров; Media сохраняет исходные байты и не загружает повторный объект. HTTP smoke-тест проверяет каталог и карточки через production-маршруты Gateway. Скачаны образцы фото со всех 14 источников с изображениями; River фото не предоставляет. Backend CI: 1537 пройдено, 23 инфраструктурных теста пропущены; Media: 19 пройдено; Python: 19 пройдено. Аудит NuGet не обнаружил уязвимых пакетов.

Полный живой цикл с PostgreSQL/MinIO и активацию VPS нужно выполнить при развёртывании: Docker Engine в текущем окружении недоступен.
