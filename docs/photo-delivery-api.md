# Защищённые варианты фото: P3

Реализованы Ready event/outbox, Community projection, совместимые дескрипторы и защищённый GET/HEAD с private revalidation. Эта часть вводит private revalidation без положительного TTL. Кеш 20/20 и передача срока JWT следуют отдельным PR. [Общий план](photo-delivery-improvement-plan.md), [генерация P2](photo-delivery-variants.md).

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

- Community заново проверяет чекин, актуальное одобрение, скрытие кофейни и личные блокировки; Account lookup не кешируется. Версия источника сверяется с текущей Community photo binding.
- Media проверяет owner/check-in/photo/source binding, Confirmed source, Ready job и отсутствие tombstone. Неготовый, удалённый или отменённый вариант возвращает 404.
- Варианты читаются отдельной IAM identity: GetObject только в private variants bucket, чтение policy/location для readiness. Проверка policy выполняется фоновым monitor каждые 10 секунд с timeout 5 секунд; readiness lease истекает через 30 секунд. Неуспешная проверка закрывает также HEAD/304.
- Ответ содержит фактические `Content-Type`, `Content-Length`, `ETag`, `X-Content-Type-Options: nosniff`. HEAD и 304 не читают объект из MinIO. `If-None-Match` поддерживает weak tags, список и `*`.
- Approved-public variant возвращает `Cache-Control: private, max-age=0, must-revalidate` и `Vary: Authorization, Cookie`, включая anonymous. Shared cache не разрешён. Ошибки, owner-only/private/pending/rejected/hidden данные и оригиналы остаются `private, no-store`. Для owner-only фото conditional header не пересылается в Media, ответ 304 не принимается.
- Каждый 304 следует новой проверке доступа. Решение ограничено 40 секундами от начала запроса; положительный TTL отсутствует. Это оставляет запас до согласованных 60 секунд.
- Gateway разрешает web-клиенту читать ETag через CORS. Range и If-Range не проксируются в Media; отдаётся целый вариант без seek/buffering всего изображения.

После истечения freshness наличие кешированных bytes не даёт права показывать фото offline без успешной перепроверки. Новый клиент должен выбирать вариант по месту показа и формату, сохранять account/session/URL как часть ключа кеша и использовать прежний URL, когда `variants` отсутствует. Существующий клиент, продолжающий использовать `url`, сам по себе не начнёт загружать меньшие файлы.

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

Для мягкого отката выключить advertisement/generation, сохранив read/serve для уже выданных URL. Для аварийного закрытия вариантов выключить read/serve: клиент получит 404/503 и сможет обратиться к прежнему оригиналу только после обычной проверки доступа. Не удалять private artifacts, originals, tombstones или таблицы при откате приложения. Reconciliation и migration replay не меняют moderation state.

## Проверки и границы готовности

Backend unit/contract checks и PostgreSQL gates покрывают access parity, source binding, projection replay и atomic Ready/outbox. HTTP-тест Kestrel подтверждает HEAD/304 без object GET. Перед включением остаются MinIO/renderer/RabbitMQ, production Caddy и клиентские acceptance checks. Флаги read/serve/advertise выключены по умолчанию.
