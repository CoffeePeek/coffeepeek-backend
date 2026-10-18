# План улучшения доставки фотографий CoffeePeek

Дата: 18 октября 2026. Основа backend: `main`, `50d78de6`.
Статус: реализация начата. Backend-часть P0/P1 подготовлена в `codex/photo-delivery-foundation`: узкий photo access query, отдельный storage reader, provisioning/readiness, телеметрия и HTTP harness. P2 реализован в коде: durable jobs, приватный renderer и варианты, отмена и очистка. PostgreSQL и MinIO/renderer gates пройдены локально, включая IAM и alpha. Доставка через RabbitMQ и замеры на стенде ещё ожидают выполнения. Базовая часть P3 реализована: Ready/outbox, Community projection, variant descriptors, protected GET/HEAD и private revalidation. Бюджет 20/20 реализован за выключенным флагом, включая исходный deadline, срок JWT и запрет anonymous fallback при недействительных credentials. Локальный HTTP gate с JWT/YARP пройден; production proxy/client rollout gates P3 остаются. P4–P7 не начаты. Контракт и включение: [API вариантов](photo-delivery-api.md). Запуск P2: [фоновые варианты](photo-delivery-variants.md). Подробности и запуск: [первая часть реализации](photo-delivery-foundation.md).

Цель — ускорить появление фотографий в мобильном и веб-клиентах, уменьшить трафик и нагрузку на VPS. Начинаем с ленты чекинов, затем распространяем подход на дневник, домашние приготовления, профили, галереи кофеен, обжарщиков, каталог кофе, аватары и меню. Уже загруженные оригиналы и действующие клиенты продолжают работать.

Пользователь подтвердил два условия: проблема заметна в обоих клиентах; после скрытия чекина или личной блокировки автора допустима задержка отзыва доступа **до 60 секунд**. Это верхняя граница всей цепочки кешей, а не отдельный TTL каждого её звена. Ограничение относится к новому получению/повторному использованию изображения приложением; уже показанные или сохранённые пользователем байты отозвать нельзя.

## 1. Наблюдения и исходные ограничения

### Проверено в backend

- Лента возвращает защищённые `photos[].url`, созданные `CheckInMapping`. У `CheckInPhotoDto` нет уменьшенных вариантов. В DTO также есть отдельный `drinkPhotoUrl` для прозрачной вырезки напитка.
- Байты идут через Gateway → Shops → Media → приватный MinIO. Shops вызывает полный `GetUnifiedByIdAsync`, при необходимости `GetHomeByIdAsync`, и только затем обращается в Media.
- Полный запрос загружает все фотографии чекина, название кофейни и, для авторизованного читателя, helpful votes. На каждый авторизованный запрос картинки Account дополнительно возвращает ограничения авторов.
- Оба download-контроллера используют `private, no-store`; условная отдача через ETag не реализована.
- `InternalCheckInPhotosController.Download` и затем `MinIOStorageService.CopyToAsync` вызывают `EnsureCheckInBucketPrivateAsync`. Каждый вызов делает `BucketExistsAsync` и `RemovePolicyAsync`: четыре дополнительных обращения к хранилищу перед чтением одного файла.
- Публичный imgproxy существует для общедоступных бакетов. Он опубликован через Caddy `/img/*`, использует unsigned URL и образ `latest`. Приватные чекины этим путём не обрабатываются.
- `PhotoUploadedEvent`/`PhotosUploadedEvent` отправляются при выдаче presigned upload URL. Такое событие не доказывает, что клиент закончил PUT. Для чекинов проверенный неизменяемый файл появляется в claim/attachment-процессе.
- Права на данные остаются у их владельцев: Community владеет `CheckInPhotos`, Media — объектами и их производными, Account — личными блокировками. Нельзя переносить авторизацию в генератор картинок или дублировать бизнес-статусы в нём.

### Контрольные сетевые запросы

18 октября 2026 выполнена небольшая анонимная выборка с одного соединения:

- JSON первых трёх элементов ленты: 4 751 байт, 125,736 мс.
- Фото 1: JPEG 1440×2560, 442 811 байт; TTFB 167,276 мс, полная загрузка 293,858 мс.
- Фото 2: JPEG 1440×2560, 1 032 087 байт; TTFB 189,419 мс, полная загрузка 305,198 мс.
- Фото 3: JPEG 1440×2560, 522 069 байт; TTFB 158,061 мс, полная загрузка 247,561 мс.
- Повторный обычный запрос второго фото снова передал все 1 032 087 байт. HTTP-заголовки фотографий: `private, no-store`.

Это проверка фактического пути и объёма, а не p95, нагрузочный тест или измерение пользовательского экрана. Многосекундная задержка этими запросами не воспроизведена. Три фотографии суммарно занимают 1 996 967 байт; снижение до суммарных 450 000 байт означало бы примерно 77,5% экономии. Это расчёт целевого сценария, а не уже полученный результат сжатия.

### Проверено в локальном мобильном клиенте

В `/home/arseny/StudioProjects/CoffeePeek-Android` прочитаны текущие файлы, без изменений:

- `CheckInMapper` заполняет `photoUrls` из `photo.url`, а `photoThumbnailUrls` по умолчанию равен `photoUrls`. Компонент ленты уже принимает `thumbnailUrls` отдельно.
- Android fetcher сначала скачивает `ByteArray`, затем `BitmapUtil.prepareForDisplay` декодирует, уменьшает и заново кодирует изображение перед передачей в Kamel. Этот путь требует профилирования и упрощения после появления серверных вариантов.
- iOS-декодер ограничивает размер, но сериализует чтение потока и декодирование одним mutex; это может создавать очередь и требует отдельного замера.
- У `ImageBitmapMemoryCache` есть ограничение 48 MiB/100 элементов, но в самом классе нет TTL и привязки к аккаунту. HTTP-заголовков недостаточно для ограничения жизни готового bitmap.

Веб-репозиторий по прежнему локальному пути отсутствует. Веб-часть ниже — конкретные требования к реализации и проверке; текущий web image loader, Service Worker и способ передачи авторизации ещё нужно исследовать. Коммит и рабочее состояние мобильного репозитория также следует зафиксировать перед его изменением.

## 2. Предлагаемая архитектура

### Неизменяемые варианты создаются заранее

После подтверждения неизменяемого источника Media надёжно ставит задания в существующий Wolverine/outbox. Отдельный внутренний imgproxy рендерит заданные размеры. Media сохраняет результат в **приватное** хранилище вариантов и публикует только готовые дескрипторы.

Цепочка подготовки: проверенный immutable original → durable job → внутренний renderer → private variants + metadata.

Цепочка чтения: клиент → проверка доступа/HTTP-кеш → узкий Shops access query → Media → готовый вариант. Трансформация и запись bucket policy отсутствуют в обычном GET. На первом этапе байты по-прежнему проходят через Shops и Media: уменьшение количества hop — отдельное решение после измерения их цены.

Отдельный renderer обязателен: нельзя выдавать приватные S3 credentials существующему публичному unsigned imgproxy. Внутренний экземпляр не имеет публичного маршрута, получает только read-доступ к разрешённым источникам, ограниченный набор presets, лимиты размеров/пикселей/времени/параллелизма и закреплённую версию контейнера. Запросы формирует сервер из доверенных metadata, а не из пользовательского URL. Media имеет отдельные права записи в хранилище вариантов. S3-совместимые источники и endpoint MinIO поддерживаются imgproxy; это позволяет использовать существующую технологию без новой .NET image-библиотеки. [Документация imgproxy](https://docs.imgproxy.net/image_sources/amazon_s3).

Не полагаться на `IMGPROXY_TTL` как на наличие сохранённых производных: cache headers и постоянное хранилище готовых файлов решают разные задачи. Изменения проверяются на выбранной закреплённой версии imgproxy.

### Начальный набор вариантов

- `thumb`: вписать в 320×320, без увеличения; календарь и маленькие плитки.
- `feed`: вписать в 960×960, без увеличения; карточка ленты с учётом DPR 2 при ширине около 480 CSS px.
- `detail`: вписать в 1600×1600, без увеличения; просмотр по нажатию.
- `original`: существующий исходный файл, для явного открытия/скачивания; не подменять его уменьшенным изображением.
- Для обычных фото начать с WebP и JPEG fallback. Утвердить качество на реальном наборе портретов, тёмных кадров, чашек и текста. AVIF добавлять после измерения стоимости кодирования и поддержки клиентов.
- `drinkPhoto`: отдельные alpha-preserving PNG/WebP профили без потери прозрачности. Не смешивать вырезку с исходной галереей.
- Меню и упаковки кофе требуют собственного профиля читаемости текста; уменьшение по правилам ленты к ним автоматически не применять.

Новые профили `thumb/feed/detail` версионируются отдельно от существующих публичных `thumb/card/detail/full`. Вписывание выбрано, чтобы не обрезать портретный оригинал; визуальный crop остаётся решением UI. Размеры и quality — стартовые параметры, окончательные значения фиксируются после P0.

### Контракт без поломки старых клиентов

Сохранить `photos[].url`, `storageKey`, ID, sort order и существующий `drinkPhotoUrl`. Добавить необязательные размеры и список только **готовых** вариантов. Например:

```json
{
  "id": "photo-id",
  "url": "/api/v1/check-ins/check-in-id/photos/photo-id",
  "width": 1440,
  "height": 2560,
  "variants": [
    {
      "purpose": "feed",
      "contentType": "image/webp",
      "width": 540,
      "height": 960,
      "sizeBytes": 120000,
      "url": "/api/v1/check-ins/check-in-id/photos/photo-id/variants/v1/feed/webp"
    }
  ]
}
```

Числа варианта в примере иллюстративные, не результат текущей обработки. Версия URL однозначно определяет рецепт и неизменяемый источник. Вариант не становится публичным из-за наличия version/hash в пути: бизнес-доступ проверяется и на этом маршруте. Для вырезки добавить необязательный дескриптор рядом с прежним `drinkPhotoUrl`.

Пустой список не означает сломанную фотографию. Старый клиент использует прежний URL. Новый клиент показывает локальный placeholder и делает ограниченный повтор получения metadata; не скачивает автоматически все оригиналы при задержке worker. Возможность открыть исходник явно сохраняется. Pending/error/timeout generation отражаются в технической телеметрии, а не превращаются в новый пользовательский workflow.

### Состояние и идемпотентность обработки

Media хранит `PhotoVariants` с уникальностью `(PhotoId, SourceVersion, RecipeVersion, Purpose, Format)`, размерами, длиной, digest/ETag и ключом объекта. Отдельная запись задания хранит lease, попытки и результат; схема не меняет ownership и moderation state Community.

Состояния задания: Pending → Processing → Ready либо RetryScheduled/Failed. После истечения lease Processing возвращается в обработку. Сначала записать и проверить immutable object, затем пометить metadata Ready; сбой между этими шагами безопасен при повторе. Объект с тем же детерминированным ключом повторно проверяется, а не бесконечно создаётся заново.

### Как готовые варианты попадают в JSON ленты

В одной транзакции с Ready Media через outbox публикует `PhotoVariantsReady` с ID фотографии, неизменяемой версией источника, версией рецепта и дескрипторами. Community ведёт локальную read projection этих metadata с additive Shops migration. Consumer сверяет действующую привязку к чекину/источнику, принимает дубликаты идемпотентно и отклоняет устаревшие версии/удалённые binding. Он не изменяет `ContentRevision`, moderation state и время публикации чекина.

Feed/detail читают готовые дескрипторы одним ограниченным SQL batch по ID фотографий страницы или SQL projection; HTTP-вызова Media на каждое фото нет. Media остаётся источником истины о файлах. После пропущенных событий read projection восстанавливается внутренней cursor-based batch manifest/reconciliation процедурой с service auth, без доступа клиентов к storage keys вариантов. Доставка read model может отставать: отсутствие дескриптора временно обрабатывается прежним контрактом/placeholder. Её lag измеряется отдельно от готовности renderer.

Не запускать generation от события выдачи upload URL. Для новых чекинов использовать verified immutable source после события **закоммиченного attachment** и outbox-транзакцию Media. Один предварительный claim до commit Shops не является достаточным триггером. Статус Confirmed сам по себе также недостаточен для поверхностей со старым upload flow: факт наличия и неизменяемость источника нужно доказать. Для аватаров и публичных загрузок добавить verify/complete flow и reconcile для старых клиентов. Незавершённые и неприкреплённые uploads не должны расходовать очередь генерации.

Presigned PUT остаётся действительным 600 секунд. Поэтому complete/HEAD/tag не превращают тот же ключ в immutable source. При финализации аватара/публичного upload нужно создать snapshot в новом server-owned ключе, на который клиент никогда не получал PUT-подпись, либо закрепить конкретный object version. Проверка формата/digest и renderer читают именно snapshot/version. Для существующих чекинов сохранить уже реализованное приватное копирование; хранить идентичность проверенного источника в Media. Повторная запись по старой upload-ссылке не меняет принятый snapshot и его производные.

Detach/delete/replacement Community передаются Media отдельным durable outbox-событием с ID binding, source version и монотонной generation/tombstone. Перед Ready commit worker проверяет, что binding и generation ещё действуют. Consumer Ready также проверяет эти значения перед обновлением read projection. Hide/reject не считаются командой физического удаления: действующие правила хранения оригиналов и moderation snapshots сохраняются.

GC работает сначала в report/dry-run режиме; затем удаляет только доказанные orphan **производные** после grace period, превышающего worker lease и поддерживаемое окно клиентских URL. Active lease, ещё не закоммиченный Ready после object write и запоздалые сообщения учитываются при сверке. Хешированные импортные/public originals могут иметь несколько ссылок: нельзя удалять их по одному detachment без проверки актуальных ссылок и отдельного решения владельца о retention. Этот план не вводит автоматическое удаление старых оригиналов.

## 3. Политика кеширования и отзыва доступа

### Базовый бюджет

Рабочая цель — максимум **40 секунд** устаревшего разрешения, оставшиеся 20 секунд — резерв до согласованной границы 60. Начальное разбиение: разрешающие policy facts не старше 20 секунд; последующее повторное использование ответа — не дольше 20 секунд. При вложенных кешах нельзя заново выдавать полный TTL на старые факты.

Каждое разрешение имеет `observedAt` и абсолютный `allowedUntil`. Если Shops использует ограничения из Account, возраст Account-данных учитывается в том же сроке. Внешняя свежесть ограничивается оставшимся временем. `Date`/`Age` не сбрасываются произвольно между слоями. Запрещены `stale-while-revalidate`, `stale-if-error`, offline reuse и любое другое продление разрешения после deadline. TTL корректируется на оставшийся срок credentials. Эта архитектура и значения требуют проверок на границе срока, а не только unit-теста заголовка.

Правила freshness, `Age`, shared/private cache и revalidation описывает [RFC 9111](https://www.rfc-editor.org/rfc/rfc9111.html). Здесь 20/20/60 секунд — продуктовые и архитектурные решения CoffeePeek, не требования RFC.

### Категории ответов

- **Личные фото, pending/rejected/hidden, оригиналы и moderation snapshots:** на первом этапе сохранить `private, no-store`. Никакого shared cache. Внутренние готовые bytes можно переиспользовать только после актуального решения о доступе. Moderation URL продолжает проверять revision.
- **Approved-public вариант при авторизованном запросе:** сначала `private, max-age=0, must-revalidate` + ETag. После проверки клиентского кеша допускается `private, max-age=20, must-revalidate` в пределах `allowedUntil`. Общий HTTP-кеш обходится при наличии любой используемой приложением идентификации. При смене пользователя/выходе локальные записи и bitmap scope сбрасываются.
- **Approved-public вариант при анонимном запросе:** сначала тот же безопасный revalidation flow. Позже разрешить `public, max-age=20, s-maxage=20, must-revalidate` на доказанно анонимной ветке с сохранением возраста ответа и общего бюджета.
- **Аватары/галереи/каталог:** долгий `immutable` допустим только для действительно публичных immutable объектов, для которых не обещается отзыв по прежнему URL. Если к поверхности применяется отзыв ≤60 секунд, использовать защищённую доставку с тем же бюджетом. Это отдельное решение по каждой поверхности, а не общий годовой TTL.
- **401/403/404/5xx:** `no-store`, без кеширования ошибок доступа. При инфраструктурном сбое после истечения разрешения не отдавать старые байты.

Анонимный и авторизованный запросы сейчас используют один URL. Перед включением shared cache нужен явный bypass как чтения, так и записи кеша при `Authorization`, используемых session cookies или иной доверенной идентификации; `Vary` должен отражать реально используемые credentials. Не помещать токены в URL. Одного наличия `Vary` без проверенных правил proxy/CDN недостаточно. Личные блокировки применяются в контексте предъявленной личности, как в текущем API; анонимный запрос не может определить, какой пользователь его сделал.

### Условные запросы

ETag вычисляется из неизменяемого содержимого варианта/рецепта и хранится в metadata. Порядок: проверить credentials и бизнес-доступ → разрешённый вариант → обработать `If-None-Match` → вернуть 304 либо поток bytes. После скрытия, удаления, смены revision или блокировки совпавший ETag не даёт права на 304. Для валидного 304 чтение объекта MinIO не требуется. `FileResult` поддерживает conditional requests, но не заменяет предшествующую бизнес-авторизацию. [Документация ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/responses?view=aspnetcore-10.0#file).

Клиентский memory/disk cache тоже хранит deadline, аккаунт и идентичность варианта. HTTP-cache и decoded-bitmap cache должны разделять байты и разрешение на показ. Смена аккаунта, локальный hide/block, logout и новая сессия не должны повторно использовать разрешение предыдущего пользователя. В P0 отдельно определить поведение административной блокировки аккаунта: действующий photo access код не следует считать доказательством, что она уже запрещает байты автора.

## 4. Измеримые цели

Значения ниже — начальные критерии приёмки, а не результаты уже выполненной оптимизации. P0 утверждает воспроизводимый набор данных, устройства, нагрузку и сетевые профили.

- Для репрезентативного набора обычных фотографий: feed-вариант p95 ≤150 000 байт; уменьшение общего image traffic первого экрана минимум на 70% относительно тех же оригиналов. Отдельно публиковать результаты для PNG с alpha и меню.
- Первый экран из трёх обычных feed-фото: суммарно ≤450 000 байт при целевом качестве. Не скачивать невидимые галереи и исходники всей страницы.
- Сетевой профиль mobile/web: 5 Mbit/s downstream, RTT 100 мс, потерь нет, холодный клиентский кеш. Цель p75 времени от появления первой карточки до отрисованного фото ≤1,2 с. Собирать также p95 и разложение download/decode/render.
- Backend ready-variant TTFB p95 ≤250 мс при 50 параллельных запросах из той же тестовой области; выборка минимум 1 000 запросов, anonymous/authenticated раздельно. Отдельно учитывать queue miss и исходные файлы.
- Повторный разрешённый conditional GET: 304 без object GET; ready GET не пишет bucket policy и не вызывает renderer. Успешный origin GET использует один GET готового объекта; дополнительные HEAD только при обоснованном fallback.
- Новые verified attachments: feed/thumb готовы p95 ≤30 с при согласованном потоке; деградация worker не увеличивает задержку создания чекина. Retry не создаёт дубликаты и не держит HTTP download открытым.
- Все слои соблюдают отзыв доступа ≤60 секунд, в том числе при сбоях, вложенных кешах, повторных 304 и смене аккаунта. Нет успешного доступа чужого пользователя к изначально приватной фотографии.
- На целевых маломощных устройствах нет OOM на длинной прокрутке; decoded-memory budget задаётся отдельно, не выводится из сжатого размера JPEG.

## 5. Этапы и небольшие PR

### P0. Базовая диагностика и окончательные решения

Результат: карта всех media surfaces, сценарии и baseline, по которым можно сравнить каждый следующий этап.

1. PR 01: instrumentation в Shops/Media: access-query, Account lookup, MinIO GET/policy, stream, bytes, status, cache outcome; без photoId/userId/URL/token в labels. Сквозной trace с семплированием; RUM download/decode/render в обоих клиентах.
2. PR 02: воспроизводимый performance harness и набор изображений — portrait/landscape, маленькие, 4K, alpha, EXIF orientation, animation, текст; 20/100 элементов ленты, cold/warm, 1/10/50 concurrent, slow network. Нагрузку выполнять на стенде; прод — только ограниченные read probes.
3. Зафиксировать клиентские версии, размеры UI/DPR, целевую географию, доступные CPU/RAM/disk, тип авторизации web и требования к отзывам аватаров/галерей. Утвердить presets/quality и семантику admin account block.

Приёмка: raw results и методика сохранены; baseline не выдаёт три ручных запроса за процентиль; privacy/cache matrix согласована. Зависимостей нет.

### P1. Быстро убрать лишнюю работу из GET

1. PR 03: перенести создание и политику private buckets в idempotent provision/deploy/startup readiness. Проверять дрейф отдельным health/audit процессом. Runtime-reader получает read-only права; инфраструктурная ошибка закрывает доступ. Убрать обе policy mutation из hot path, сохранив нужные проверки write/claim.
2. PR 04: отдельный `ICheckInPhotoAccessQueries`/access DTO с `ownerId`, requested photo, kind, revision и минимальными facts доступа. SQL использует общие правила `CheckInEligibility`, но не тянет все photos/shop names/votes. Сначала Account lookup остаётся актуальным, без нового кеша. Добавить narrow pair/batch-API Account только если P0 подтвердил цену списка блокировок.

Приёмка: parity tests для anonymous/author/other/moderator, shop/home, approved/private/hidden/deleted и обеих сторон личной блокировки; тот же внешний URL и статус отказа. На ready download нет BucketExists/RemovePolicy; SQL plan/indexes проверены на PostgreSQL. Зависит от P0.

### P2. Надёжное создание приватных вариантов

1. PR 05: additive Media migration `PhotoVariants` + durable job/source metadata, уникальность и индексы. Репетиция fresh install и populated upgrade. Не менять существующие originals и CheckInPhotos.
2. PR 06: закреплённый внутренний renderer, отдельные минимальные credentials и network isolation. Профили и лимиты декодирования, таймауты и memory/CPU concurrency budget. Начать с 1 worker operation одновременно до нагрузочного замера.
3. PR 07: verified-source event, outbox enqueue, идемпотентный worker, lease/retry/dead-letter/reconcile, приоритет новых пользовательских фото над backfill. Генерация вне транзакции создания чекина; durable запись задания внутри owning Media transaction. Событие presign не является триггером.
4. PR 08: durable detach/delete/replacement contract Community → Media; orphan variants cleanup с active-lease/grace/reference проверками и dry-run; tombstone/generation fence при Ready commit и применении Ready event. Crash recovery между object write и DB Ready. Запоздалое задание не восстанавливает удалённый binding. Для rollback artifacts можно оставить приватно, не удаляя originals.

Приёмка: restart worker, duplicate delivery, удаление во время обработки, partial upload, corrupt/огромный raster, EXIF, alpha и неподдерживаемый format покрыты реальными MinIO/PostgreSQL/broker сценариями. Проверено отсутствие публичного маршрута к private renderer и вариантам. Зависит от P0/P1; schema/renderer можно готовить параллельно P1.

### P3. Совместимый API и контролируемый кеш

1. PR 09: Ready event/outbox и idempotent Community read projection с additive Shops migration; descriptors в `CheckInPhotoDto` и DTO вырезки; protected variant route, стабильные source/recipe versions, готовые размеры/длины. Feed/detail загружают manifest одним bounded SQL batch; восстановление выполняется внутренним batch reconcile. Старый `url` продолжает отдавать оригинал. Не использовать публичный `PhotoUrlsDto.Create` для private check-ins.
2. PR 10: ETag/If-None-Match, корректные Content-Type/Content-Length, HEAD parity и `nosniff`. Conditional path после authorization; нет загрузки файла ради вычисления ETag. Range поддерживать отдельным PR только если viewer действительно требует его; не буферизовать весь файл ради seek.
3. PR 11: начать с private revalidation, затем ввести 20/20 budget для approved-public variants за feature flag. Модерация/private/original остаются no-store. Account cache facts, если он нужен, имеют исходный deadline и invalidation events только как ускорение, а не единственную гарантию TTL.
4. Обновить OpenAPI, примеры/генераторы контрактов и pre-generated Wolverine handlers для новых/изменённых обработчиков. Добавления неизвестных полей проверяются на реальных старых клиентах; несовместимый контракт получает новую версию API.

Приёмка: old-client contract tests; Ready после уже созданного чекина появляется в следующих feed/detail без новой ревизии; duplicate/late event и rebuild projection безопасны; число запросов manifest не растёт с количеством фотографий. 304 запрещён после потери доступа; повторные 304 на старых Account facts не продлевают `allowedUntil` и передают только оставшуюся freshness. Credentials bypass не позволяет авторизованному запросу получить anonymous cache hit; cache headers и Age проверены сквозь реальный gateway/proxy. Зависит от P1/P2.

### P4. Мобильный и веб-клиент

1. PR 12, mobile contracts/UI: маппинг `variants` в существующие `photoThumbnailUrls`, feed/thumb для списка, detail по нажатию, original по явному действию. Учитывать размеры и DPR, сохранять исходный порядок/навигацию галереи. Маленькая картинка не занимает cache key полноэкранной версии.
2. PR 13, mobile pipeline/cache: убрать лишнее decode→re-encode→decode у уже готовых вариантов; EXIF/alpha/legacy uploads сохранить. Профилировать iOS mutex и сетевое ожидание, менять параллелизм только с контролем пиков памяти. Кеш учитывает account/session/variant/deadline; 304 восстанавливает разрешённые сохранённые bytes, не произвольный старый bitmap.
3. PR 14, web: сначала найти и исследовать актуальный репозиторий. Реализовать `srcset/sizes` или выбор явного variant URL, width/height для стабильной раскладки, eager только для первой видимой картинки, lazy для остальных, ограниченный prefetch следующего экрана. При bearer-защите исследовать fetch/blob путь и HTTP cache: Cache API/Service Worker не должны обходить expiry; object URL освобождается при смене изображения/аккаунта.
4. Ограничить одновременные загрузки на устройство по P0; отменять ушедшие с экрана запросы, объединять дубликаты, не префетчить все пять фото каждого невидимого чекина. Собирать ошибки и время декодирования отдельно от сервера.

Приёмка: cold/warm screenshot/performance сценарии на целевых Android/iOS и двух web browsers; возврат в ленту, scroll, viewer, offline после deadline, logout/login другим аккаунтом, hide/block без перезапуска, прозрачная вырезка и fallback старого API. Зависит от согласованного P3-контракта; skeleton mapping можно делать параллельно P2/P3. Изменения нужны в клиентских репозиториях, не в этом backend checkout.

### P5. Старые фотографии и постепенный запуск ленты

1. PR 15: dry-run инвентаризация, количество/объём eligible immutable originals, missing/corrupt objects, прогноз дополнительного storage и времени. Расчёт: число фото × средний фактический суммарный размер вариантов; throughput определяется пилотной партией. Не назначать стоимость/срок по непроверенному числу фотографий.
2. PR 16: возобновляемый keyset backfill с checkpoint, bounded batches и rate limit. Сначала последние approved-public фото ленты, затем diary/home и архив. Generation не меняет visibility/revision/publication time и не трогает оригинальные keys.
3. Включение: canary accounts/staging → 5% новых клиентов → 25% → 100%. Критерии роста: 24 часа без нарушения privacy gate, error/latency не хуже baseline и ready coverage ≥99% недавних eligible фотографий. Сравнивать одинаковые источники и клиентские версии.

Приёмка: повтор backfill ничего не дублирует; новые uploads не ждут очередь архива; есть pause/resume и reconciliation. Rollback flags прекращают новые generation/advertisement и возвращают legacy descriptors, но уже выданные versioned variant URL продолжают работать с проверкой доступа в течение явно заданного client/cache support window. Нельзя подставлять JPEG/original по URL ранее выданного WebP-рецепта. Приватные производные остаются безопасными, additive tables не откатываются через destructive Down. Старые/неготовые фото не оставляют пользователей с бесконечным placeholder: ограниченный повтор и явное открытие оригинала. Зависит от P2–P4.

### P6. Остальные media surfaces

1. PR 17: avatars + author feed cards — варианты фактического размера, verified completion/reconcile, стабильные версионированные ключи, очистка замены. Сначала проверить действующий `UserPhotoUploadedHandler`, который реагирует на выдачу presign.
2. PR 18: галереи кофеен/обжарщиков и coffee product photos — общий подход к descriptors, форматам и измерению, с сохранением существующих `FullUrl`/`Urls` и каждого ownership boundary. Публичные sources не копируются в приватный бакет без причины.
3. PR 19: меню и изображения с текстом — отдельные quality/size profiles и visual acceptance на мелком шрифте; исходник доступен для увеличения/OCR. Не ухудшить существующий OCR download flow и не передавать ему preview вместо original.

Приёмка: на каждой поверхности проверены реальные URL/headers, cache policy, обратная совместимость, качество, replacement/delete и отсутствие лишних повторных преобразований. Зависит от результатов P5; эти изменения не задерживают ускорение ленты.

### P7. Shared cache или CDN по результатам измерений

1. PR 20/ADR: сравнить географический RTT, traffic, CPU, стоимость VPS и cacheable anonymous share. Выбрать внешний CDN либо отдельный self-hosted response cache только при доказанной пользе. Обычный Caddy `reverse_proxy` не становится файловым кешем от добавления Cache-Control.
2. PR 21: cache только анонимной approved-public ветки с явным bypass credentials; ключ включает полный normalized variant URL/recipe/format. Не кешировать private/moderation/original/error, Set-Cookie и неожиданные query/preset values. Подтвердить, что provider минимальный TTL/serve-stale не нарушает бюджет.
3. Сохранять `Date/Age` и effective deadline; проверить многоуровневую цепочку с Age=19/20/39/40 и отказом origin. Purge от hide/delete может ускорить отзыв, но его отказ не должен расширять 60 секунд. Проверить DNS/TLS/CORS, bearer leakage и поведение нескольких точек присутствия.

Приёмка: measurable reduction origin bytes/requests и p95 в целевых регионах, shared-hit не обходит персональные ограничения, worst-case revoke ≤60 секунд. При отсутствии эффекта закончить ADR решением «не подключать сейчас». Откат отключает cache route; origin protected API продолжает работать. Зависит от P3/P5; это последняя оптимизация, не условие появления превью.

## 6. Проверки перед выпуском

- Access matrix: anonymous/author/other/moderator; shop/home; private/pending/approved/rejected/hidden/deleted; текущая и stale revision; личные блокировки в обе стороны, снятие блокировки, истечение credentials, logout и смена аккаунта.
- Cache matrix: cold 200, fresh reuse, expired conditional 304, denied conditional 404/401, HEAD, неверный preset/format, счётчик Age, два proxy слоя и offline после срока. До и после TTL нет перехода private → public из-за записи кеша другого читателя.
- Worker: presign без PUT, partial/corrupt source, immutable verification, duplicate message, process crash, lease expiry, DB/object-store outage, replacement/delete во время генерации, orphan cleanup, воспроизводимый deterministic output/version change.
- Content: портрет/альбом, EXIF orientation, alpha, GIF/animation, tiny/oversized images, текст меню, WebP/JPEG fallback. Не поддерживаемую анимацию явно трактовать как первый кадр для preview, сохраняя оригинал; согласовать это поведение на P0.
- Delivery: потоковая передача без полного server-side buffer, cancellation при закрытии клиента, медленный читатель и лимиты соединений; original/variant не смешиваются из-за общего ETag/cache key.
- Rollout: fresh schema и populated upgrade, смешанные версии бинарников при additive migration, старые клиенты, worker rollback и повторный запуск, separate renderer недоступен снаружи.
- Команды: релевантные unit/contract тесты, затем live PostgreSQL/MinIO/Wolverine tests, build/analyzers, vulnerability audit и codegen write затронутых сервисов. Полный CI после интеграции; smoke проходит через gateway, а не только напрямую в Media.

## 7. Наблюдаемость, ёмкость и управление запуском

Dashboard показывает image bytes/viewport, download/decode/render p50/p75/p95, backend access/Account/MinIO этапы, 200/304/404/503, generation queue age, ready coverage, retries/dead letters, renderer CPU/RSS, storage growth и cache hit ratio по anonymous/authenticated и purpose. ID/URL не становятся metric labels; приватные ключи/credentials редактируются в logs/traces.

Feature flags разделены: `VariantsGeneration`, `VariantsApi`, `VariantsClient`, `PhotoConditionalGet`, `PhotoPolicyCache`, `AnonymousPhotoCache`. Метрика privacy/revoke выходит за 60 с — немедленно отключить внешнее кеширование, а не только generation. По error rate/queue lag останавливать backfill раньше новых uploads.

Порядок раскатки: provision private storage/credentials → additive Media migration → renderer/worker → protected API → новые клиенты → backfill и rollout → остальные surfaces → решение о CDN. Первые пользовательские улучшения доступны после P1/P3/P4, до полного backfill и CDN.

Разбивка — 21 ориентировочный небольшой PR, а не обещание фиксированного количества. Роли: backend Media; backend Community/Account/Gateway; mobile; web; release/performance. P1 и схема/renderer P2 частично параллельны; mobile/web идут параллельно после заморозки additive контракта. Оценку календарного срока выполнять после P0: ещё неизвестны объём архива, производительность renderer и актуальный web loader.

## 8. Карта основных мест изменения

Backend hot path: `CoffeePeek.ShopsService/Controllers/CheckInPhotosController.cs`, `CoffeePeek.Shops.Persistance/Queries/CheckInQueries.cs`, `CheckInEligibility.cs`, `CheckInProjection.cs`, `CoffeePeek.Shops.Infrastructure/Media/MediaCheckInPhotoClient.cs`, `CoffeePeek.Shops.Infrastructure/Account/AccountAuthorVisibilityLookup.cs`.

Media lifecycle: `CoffeePeek.MediaService/Controllers/InternalCheckInPhotosController.cs`, `Handlers/ClaimCheckInPhotosHandler.cs`, `Consumers/CheckInPhotosAttachedHandler.cs`, `Services/MinIOStorageService.cs`, `Repositories/PhotoRepository.cs`, `Data/PhotoMetadata.cs`, `Services/DrinkPhotoValidation.cs`; новые job/variant entities, EF migration, worker и renderer client.

Контракты/проекции: `CoffeePeek.Contract/Dtos/CheckIns/CheckInPhotoDto.cs`, `PublicAddresses/PublicCheckInDto.cs`, `CoffeePeek.Shops.Application/Features/CheckIn/CheckInMapping.cs`, `PublicAddresses/PublicCheckInProjector.cs`, `CoffeePeek.Account.Application/Mapper/MapsterConfiguration.cs`; `PhotoUrlsDto` остаётся совместимым для публичных surfaces.

Инфраструктура/документация: `deploy/docker-compose.yml`, `deploy/Caddyfile`, `deploy/.env.example`, provisioning/migration scripts, `docs/api/photos-client.md`, `photos-admin.md`, `feed.md`, примеры и DTO generators. Секреты renderer/S3 в репозиторий не включать.

Мобильная интеграция: `modules/data/.../mapper/CheckInMapper.kt`, DTO `CheckInDto`, domain `Catalog.kt`, `composeApp/.../feature/checkin/ui/CheckInTimelineContent.kt`, платформенные `utils/Fetcher.*.kt`, Android `BitmapUtil.kt`, `utils/image/ImageBitmapMemoryCache.kt`, iOS `IosImageBitmapDecoder.kt`. Существующую модульную архитектуру клиента сохранять. Web file map определяется в P0 после нахождения актуального репозитория.

## 9. Граница этого плана

План не требует нового облачного сервиса, перехода с MinIO, замены .NET или image loader и удаления старых оригиналов. Доступные технологии используются повторно; CDN и дальнейшее сокращение hops принимаются по измерениям. Критические решения приняты для ленты, а детали quality/resource budgets и поведение остальных media surfaces имеют явные шаги проверки в P0/P6.

Начать реализацию следует с PR 01–04: воспроизводимые измерения, provisioning и узкая проверка доступа. Затем провести один полный сценарий нового verified photo → ready feed variant → новый mobile/web UI → cache expiry/revoke. После доказанного вертикального сценария расширять охват и запускать архив.
