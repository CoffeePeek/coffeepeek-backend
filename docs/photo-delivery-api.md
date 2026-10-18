# Защищённые варианты фото: P3

Реализованы Ready event/outbox, Community projection, совместимые дескрипторы и защищённый GET/HEAD с private revalidation. Кеш ограничений Account до 20 секунд и private freshness до 20 секунд реализованы за отдельным флагом, по умолчанию выключенным. Срок учитывает исходную проверку, задержки и истечение JWT. [Общий план](photo-delivery-improvement-plan.md), [генерация P2](photo-delivery-variants.md), [архивная инвентаризация P5](photo-delivery-inventory.md), [backfill и откат P5](photo-delivery-backfill.md).

## Ответы ленты и деталей

`photos[].url` остаётся URL оригинала. Когда шесть вариантов готовы и включён `CheckInPhotoDeliveryOptions.AdvertiseVariants`, у фотографии появляется необязательный массив `variants`. Для публичного DTO вырезки добавлен `drinkPhotoVariants`; прежний `drinkPhotoUrl` сохранён. В Home DTO вырезка уже представлена объектом: там это `drinkPhoto.variants`. При выключенном флаге или отсутствии готового manifest новые поля отсутствуют в JSON.

Пример одного элемента `variants` (URL сокращён для читаемости):

```json
{
  "purpose": "feed",
  "format": "webp",
  "contentType": "image/webp",
  "width": 640,
  "height": 960,
  "sizeBytes": 84512,
  "sourceVersion": "<64 lowercase hex characters>",
  "recipeVersion": 1,
  "url": "/api/v1/check-ins/<checkInId>/photos/<photoId>/variants/<sourceVersion>/1/feed.webp"
}
```

Профили: `thumb` до 320, `feed` до 960, `detail` до 1600 пикселей по большей стороне, без увеличения исходника. На каждый профиль есть WebP и JPEG либо PNG для alpha. Размеры в дескрипторе — фактические, включая EXIF orientation. URL содержит неизменяемые версии источника и рецепта; он не является подписанной ссылкой MinIO и не открывает доступ сам по себе.

Community запрашивает manifests одним дополнительным SQL batch на уже разрешённую страницу, включая вырезки. Нет HTTP обращения к Media на каждую фотографию в ленте. Metadata появляется при следующем чтении после обработки события; content revision и время публикации чекина не меняются. Модераторские snapshots продолжают использовать прежний отдельный маршрут оригинала.

## HTTP и доступ

`GET` и `HEAD /api/v1/check-ins/{id}/photos/{photoId}/variants/{source}/{recipe}/{purpose}.{format}`:

- Community заново проверяет чекин, актуальное одобрение и скрытие кофейни. Личные блокировки читаются из Account; при включённом флаге для публичных вариантов допустимы факты возрастом менее 20 секунд. Версия источника сверяется с текущей Community photo binding.
- Media проверяет owner/check-in/photo/source binding, Confirmed source, Ready job и отсутствие tombstone. Неготовый, удалённый или отменённый вариант возвращает 404.
- Варианты читаются отдельной IAM identity: GetObject только в private variants bucket, чтение policy/location для readiness. Проверка policy выполняется фоновым monitor каждые 10 секунд с timeout 5 секунд; readiness lease истекает через 30 секунд. Неуспешная проверка закрывает также HEAD/304.
- Ответ содержит фактические `Content-Type`, `Content-Length`, `ETag`, `X-Content-Type-Options: nosniff`. HEAD и 304 не читают объект из MinIO. `If-None-Match` поддерживает weak tags, список и `*`.
- Approved-public variant по умолчанию возвращает `Cache-Control: private, max-age=0, must-revalidate`; при включённом кеше — `private, max-age=N, must-revalidate`, где N от 0 до 20 и `Vary: Authorization, Cookie`, включая anonymous. Shared cache не разрешён. Ошибки, owner-only/private/pending/rejected/hidden данные и оригиналы остаются `private, no-store`. Для owner-only фото conditional header не пересылается в Media, ответ 304 не принимается.
- Каждый 304 следует проверке доступа. Решение ограничено 40 секундами от начала исходного Account lookup, включая его retries и дальнейшие запросы. Cache hit сохраняет исходные observedAt/allowedUntil. Положительная freshness не выходит за этот срок или срок JWT; доли секунды округляются вниз. Это оставляет 20 секунд резерва до согласованных 60 секунд.
- Gateway отбрасывает невалидный/истёкший/отозванный JWT на photo routes до response cache и YARP, возвращая 401/no-store. Он удаляет клиентский `X-User-Token-Expires-At` и передаёт срок из проверенного JWT; downstream принимает его только вместе с доверенной identity. При отсутствии срока у авторизованного пользователя freshness остаётся нулевой. Gateway разрешает web-клиенту читать `ETag`, `Date` и `Age` через CORS на 200 и 304; preflight разрешает Authorization и If-None-Match только для доверенных origins. Range и If-Range не проксируются в Media; отдаётся целый вариант без seek/buffering всего изображения.

После истечения freshness наличие кешированных bytes не даёт права показывать фото offline без успешной перепроверки. Новый клиент должен выбирать вариант по месту показа и формату, сохранять account/session/URL как часть ключа кеша и использовать прежний URL, когда `variants` отсутствует. Существующий клиент, продолжающий использовать `url`, сам по себе не начнёт загружать меньшие файлы.

## Бюджет кеша 20/20

Флаг: `CheckInPhotoDeliveryOptions.CachePublicVariants` (`CHECKIN_VARIANTS_CACHE_ENABLED` в production Compose). Значения 20/20 фиксированы в коде; их нельзя случайно увеличить конфигурацией. Кеш Account выделен отдельно, ограничен 4 MiB по оценке размера записей, разделён по viewer ID и объединяет одновременные lookup через 64 ограничителя. Обычные feed/detail/original/moderation reads продолжают использовать прежнюю свежую проверку; owner-only вариант дополнительно проверяет Account без кеша. Community и Media bindings не кешируются.

Пример: Account наблюдал разрешение в t=0, клиент повторяет запрос в t=19, а Media отвечает в t=37. Ответ 200/HEAD/304 получает максимум 3 секунды freshness, а не 20. Повторный запрос в t=40 обязан получить новое разрешение; если Account недоступен или автор заблокирован, старый ETag не помогает. Account lookup длительностью 20 секунд или больше при включённом кеше отклоняется и не сохраняется. После истечения записи нет stale fallback при сбое.

Возвращается время текущего ответа `Date` и остаток срока в `max-age`; HTTP age включает сетевую задержку. 304 не сбрасывает исходное наблюдение policy. Сервер дополнительно использует монотонные часы, поэтому перевод системных часов назад не продлевает факт. Срок JWT — абсолютный UTC, часы Gateway/Shops должны быть синхронизированы. Shared cache запрещён и для anonymous. Эти правила используют [HTTP freshness и расчёт возраста RFC 9111](https://www.rfc-editor.org/rfc/rfc9111.html#section-4.2.3).

Локальные bitmap/disk caches клиентов должны учитывать `Date`, `Age`, `max-age`, время передачи, account/session/variant и принудительно очищать разрешение при logout/смене аккаунта/local hide/block. Положительный TTL включается после проверки реальных клиентов; один серверный заголовок не ограничивает произвольный offline image cache.

## Транзакции и восстановление проекции

Media сохраняет все шесть дескрипторов и `CheckInPhotoVariantsReadyEvent` в одной транзакции с Wolverine outbox, затем отправляет событие. Consumer Community принимает только полный manifest с текущими photo/source/check-in/owner. Уникальный ключ `(PhotoId, SourceVersion, RecipeVersion)` делает повтор безопасным. Старый рецепт не замещает новый, late event после уже выполненного удаления не создаёт проекцию.

Для восстановления уже готовых фото предусмотрен сервисный endpoint Media:

```text
POST /internal/check-in-photo-variants/replay?limit=100
POST /internal/check-in-photo-variants/replay?limit=100&after=<nextCursor>
X-Gateway-Auth: <shared service secret>
```

Ответ: `{ "published": 100, "nextCursor": "<uuid>" }`. Это число событий, переданных в durable outbox; получение Community может произойти позже. Максимум 100 jobs / 600 descriptors за запрос. Вызывается непосредственно в приватной сети или через Media loopback-port, не через публичный gateway. При `nextCursor: null` проход завершён. Следующий полный проход начинают без cursor: job, завершившийся позади cursor, будет найден тогда. Это восстановление проекции, не генерация архива P5. Частично выполненный проход можно безопасно повторять. Неготовые/отменённые/tombstoned jobs не публикуются.

## Включение и откат

1. Применить additive Media migration P2 и Shops `20261018130514_AddCheckInPhotoVariantProjection` до запуска новых сервисов. Миграция добавляет только проекцию и не меняет существующие фото.
2. Выполнить `ensure-secrets.sh` и `minio-init`. Новые credentials: `MINIO_CHECKIN_VARIANT_READ_USER/PASSWORD`; не переиспользовать root, original-reader или variant-writer. В Compose все `PhotoVariantOptions__*` находятся у **Media**, `CheckInPhotoDeliveryOptions__*` — у **Shops**.
3. После storage/renderer gates включить генератор P2. Затем включить `CHECKIN_VARIANTS_READ_ENABLED=true` у Media и убедиться в `/health` 200.
4. Включить `CHECKIN_VARIANTS_SERVE_ENABLED=true` у Shops. Проверить GET/HEAD/304 через реальный gateway, затем hide/block/delete, owner-only, смену аккаунта и истёкшие credentials.
5. Выполнить replay готовых jobs, если они появились до нового consumer или проекция потеряна. Дождаться обработки outbox/inbox.
6. После проверки старых и новых клиентов включить `CHECKIN_VARIANTS_ADVERTISE_ENABLED=true`. Shops не стартует, если advertisement включён, а serving выключен. Все rollout-флаги в репозитории по умолчанию false.
7. После проверки Date/Age/freshness через production Caddy и кеши обоих клиентов отдельно включить `CHECKIN_VARIANTS_CACHE_ENABLED=true`. Сначала проверить cold/warm, повторные 304, блокировку непосредственно после заполнения кеша, Account outage, смену аккаунта и истечение JWT.

Для отката кеширования выключить `CHECKIN_VARIANTS_CACHE_ENABLED`: новые ответы потребуют revalidation, ранее выданная freshness истечёт максимум через 20 секунд. Для мягкого отката выключить advertisement/generation, сохранив read/serve для уже выданных URL. Для аварийного закрытия вариантов выключить read/serve: клиент получит 404/503 и сможет обратиться к прежнему оригиналу только после обычной проверки доступа. Не удалять private artifacts, originals, tombstones или таблицы при откате приложения. Reconciliation и migration replay не меняют moderation state.

## Проверки и границы готовности

Финальный локальный backend gate: **2 436 passed / 210 skipped**, 0 failed; Release build: **0 errors** (739 analyzer/compiler warnings). Audit зависимостей — без найденных уязвимостей. Первоначальный P2/P3-прогон фото с PostgreSQL: **49 passed / 2 skipped** (MinIO/renderer). Новый целевой прогон кеша и доступа с PostgreSQL: **7 passed / 0 skipped**; HTTP JWT/YARP gate: **1 passed**. HTTP harness: 4 passed; deployment helpers: 8 passed; `git diff --check` проходит.

Локально выполнены PostgreSQL gates для additive populated upgrade, duplicate/out-of-order/late события, source/owner binding, восстановления проекции, постоянного числа SQL batches, atomic Ready + Wolverine envelope rollback и доставки через durable local queue после commit. HTTP-тест с настоящим Kestrel подтверждает заголовки и пустое тело HEAD/304. Контроллерные проверки покрывают скрытие/блокировку, private no-store, недоступность Account/Media и истечение бюджета решения. Добавлены проверки границы 20 секунд, отсутствия sliding TTL/stale fallback, coalescing, разделения viewers, возврата часов назад, общего deadline 200/HEAD/304 и округления freshness до истечения JWT. Настоящий Kestrel + JWT validation + YARP + downstream header auth подтверждает удаление поддельного expiry, 401 для malformed/expired/wrong-signature/revoked токенов и отсутствие shared cache hit; Date/Age сохраняются при GET/HEAD/304 через прокси. Проверяются публичный DTO вырезки, OpenAPI и CORS.

MinIO + pinned renderer gate дополнен настоящим variant reader и проверками запрета DELETE и чтения originals. Для него нужны `COFFEEPEEK_LIVE_VARIANT_READER_USER/PASSWORD` наряду с переменными P2; CI их задаёт. При подготовке PR Docker запущен в rootless-режиме. Дополнительный прогон на Docker PostgreSQL 17: 12 passed / 0 skipped (queue/outbox, populated migration/projection, access SQL). MinIO/renderer gate выполнен: 41 passed / 0 skipped, включая JPEG/alpha, подпись, IAM deny, чтение всех вариантов и dry-run/actual cleanup с сохранением оригинала. Временный MinIO image собран из закреплённого официального исходника, поскольку прежние публичные образы и dl.min.io недоступны. Установлен постоянный rootless Docker с user service и CLI context. Доставка через RabbitMQ, production gateway/Caddy, целевые web/mobile клиенты, warm-cache latency и p95 на стенде остаются acceptance checks. Успех durable local queue теста не означает проверки RabbitMQ outage/restart.

Метрики `photo.stage.duration` используют stage `variant_download`, а `policy_cache` показывает hit/miss для ограничений Account. `photo.responses` различает `no_store`, `private_revalidate`, `private_fresh` и учитывает 304. Метрики не содержат URLs, object keys или идентификаторы пользователей.

Внешняя зависимость выпуска: [MinIO OSS архивирован](https://github.com/minio/minio), официальный download возвращает HTTP 410. Клиент mc теперь собирается из официального закреплённого GitHub release через `deploy/minio/mc.Dockerfile`, с проверкой SHA256 для amd64/arm64. CI и minio-init не зависят от недоступного quay.io/minio/mc. Для свежего production deploy по-прежнему нужно подтвердить доступность выбранного MinIO server image; локальная тестовая source-сборка не подменяет его.

Live-проверка выявила и устранила два дефекта: unsupported `/a:0` в presets imgproxy 4.0.18 и скрытые bucket/object HEAD в Minio .NET 7 GetObjectAsync. Reader использует локальную server-only подпись и один потоковый GET без расширения IAM; worker дополнительно сверяет длину и SHA256 реально сохранённого варианта через scoped GET.

Сборка нового mc image проверена настоящим Docker; `mc --version`, Compose validation и 8 deployment helper tests проходят. Исправление устраняет подтверждённое падение GitHub CI на pull `quay.io/minio/mc:latest`.
