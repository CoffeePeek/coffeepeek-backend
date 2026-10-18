# Приватные варианты фотографий: P2

Продолжение [плана доставки фото](photo-delivery-improvement-plan.md). Pipeline работает в production, новые uploads обрабатываются автоматически. После пилота владелец разрешил полный запуск; порядок и фактические проверки описаны в [production runbook](photo-delivery-production.md). Deployment defaults остаются выключенными для новых окружений; в production общие флаги включены явно. Cleanup остаётся отдельной выключенной операцией. Дескрипторы, Ready/outbox, Community projection и защищённый маршрут описаны в [P3](photo-delivery-api.md).

## Триггер и транзакции

`CheckInPhotosAttachedEvent` приходит через существующий Wolverine inbox после коммита Community. Media подтверждает только актуальную owner/check-in binding. В той же EF/outbox транзакции `PhotoVariantQueue` добавляет строку задания. Presign и ранний claim не запускают генерацию. Legacy-фото из публичного бакета пока не генерируются: для них нужен проверенный перенос в P5.

Уникальность задания — `(PhotoId, SourceVersion, RecipeVersion)`. SourceVersion — SHA-256 серверной неизменяемой идентичности источника (photo ID и private key); отдельный SourceDigest вычисляется по реально прочитанным исходным байтам. Клиентский upload URL не позволяет перезаписать private copy. Повтор подтверждения дозаполняет отсутствующее задание, но не дублирует существующее.

Worker опрашивает durable таблицу PostgreSQL. `FOR UPDATE SKIP LOCKED` и атомарный UPDATE/RETURNING выдают lease на 5 минут. Рендеринг идёт вне транзакции; на одну попытку отведены 3 минуты, один worker выполняет одно фото за раз. По умолчанию максимум 5 попыток с задержкой 30/60/120/240 секунд; затем `Failed`. После падения последней попытки expired lease тоже переводится в Failed. Повторный захват получает новый token, старый worker не может зафиксировать результат или изменить состояние нового.

Перед каждой записью S3 фиксируется `PhotoVariantArtifact` с ключом объекта и lease token. Ключ содержит уникальную попытку, поэтому старый worker не перезаписывает результат нового. Каждый ответ renderer проверяется по byte cap, формату, заголовку размеров и полному декодированию; после PUT проверяется размер сохранённого объекта. Ready и шесть дескрипторов коммитятся вместе только при действующей lease, наличии всех write intents, текущей binding и отсутствии tombstone.

Схема additive: `PhotoVariantJobs`, `PhotoVariants`, `PhotoVariantArtifacts`, `CheckInPhotoTombstones`. Миграция `20261018123301_AddPrivatePhotoVariants` не меняет строки Originals/`Photos` или Community `CheckInPhotos`. Она должна быть применена обычным deploy migration step до запуска нового Media.

## Renderer и хранилище

Отдельный `ghcr.io/imgproxy/imgproxy:v4.0.18` подключён только к внутренней сети `checkin-render` с Media и MinIO. В production нет опубликованного порта и маршрута Caddy; публичный старый imgproxy не получает credentials. В dev Media работает на host, поэтому предусмотрен только loopback `127.0.0.1:18080`.

Renderer использует read-only identity оригиналов, ограничение S3 bucket/source prefix и HMAC-SHA256 подпись. Разрешены только presets из `deploy/imgproxy/checkin-renderer.conf`. Внутренний клиент строит URL из подтверждённого private key, запрещает redirects и произвольные пользовательские URL. HTTP-логи клиента и трассировка запроса подавлены, Sentry HTTP handler отключён в Media в пользу фильтруемой OpenTelemetry instrumentation. Docker logging renderer отключён: его ошибки могут содержать подписанные private source URLs. Media пишет только ограниченные failure codes и метрику `photo.stage.duration{stage=variant_render}`.

Рецепт v1: fit 320/960/1600, без увеличения, auto-rotate по EXIF, первый кадр анимации, удаление metadata/ICC/copyright. WebP quality 80; JPEG quality 82 для непрозрачных изображений, PNG fallback для alpha. Вырезка сохраняет прозрачность и не увеличивается сверх исходного размера. Max source 32 MiB / 40 MP, result не более 1600 на сторону / 8 MiB, timeout renderer 20 секунд, 1 renderer worker, очередь 2, 512 MiB и 1 CPU. Изменение presets или версии renderer требует нового RecipeVersion; существующие presets нельзя переписывать.

Media пишет в `coffeepeek.checkin-variants` через отдельную identity. IAM разрешает Get/Put/Delete/AbortMultipart только в этом бакете и чтение его policy/location. Нет права писать оригиналы или менять bucket policy. Provisioning закрывает bucket для anonymous. Worker проверяет private policy перед обработкой; проверка доступа при чтении и постоянный readiness monitor вариантов добавлены в P3. Эти файлы нельзя подключать к публичному imgproxy.

## Удаление и очистка

Оба author delete маршрута (shop/home) и удаление через review report публикуют `CheckInPhotosDeletedEvent` внутри существующей Community outbox транзакции. Media сохраняет необратимый tombstone и отменяет задания, включая Ready. Delete-before-attach не восстанавливает очередь. Удаление исходника в Media также отменяет его варианты. Смена видимости или личная блокировка не удаляет общие файлы: эти решения проверяет Community на каждом protected read.

Текущие API редактирования чекина не заменяют его фотографии. Если появятся detach/replacement endpoints, их обязательно подключить к такому же outbox/fence контракту до выпуска; нельзя просто поменять source key под Ready manifest. Раннее освобождение Pending claim не создаёт variant job.

Cleanup выбирает максимум 50 объектов раз в 10 минут. Он проверяет отсутствие active lease, возраст intent не менее 24 часов, отсутствие Ready reference либо Cancelled job и повторяет проверку перед удалением. Удаляются только объекты из dedicated variants bucket, затем metadata/intents. Оригиналы и tombstones остаются. Падение после object PUT и до Ready оставляет intent для последующей уборки; падение после DELETE до очистки БД безопасно повторяется. При выключенном cleanup объекты просто остаются private.

## Запуск и rollback

1. Применить миграцию Media. `ensure-secrets.sh` создаёт и сохраняет отсутствующие `MINIO_CHECKIN_VARIANT_USER/PASSWORD`, `CHECKIN_RENDERER_KEY/SALT`; действующие значения не перезаписываются.
2. Выполнить текущий `minio-init`; проверить private bucket и права reader/writer. `update.sh` делает provisioning и запускает private renderer перед Media.
3. Выполнить live gates ниже и проверить реальные EXIF/HEIC/анимацию/повреждённые источники на корпусе P0. Подтвердить память и время обработки при имеющихся лимитах.
4. Включить `CHECKIN_VARIANTS_ENABLED=true` и пересоздать Media. Уже подтверждённые после обновления фото находятся в durable backlog. Старый архив автоматически не сканируется.
5. Сначала включить `CHECKIN_VARIANTS_CLEANUP_ENABLED=true` с `CHECKIN_VARIANTS_CLEANUP_DRY_RUN=true`; после проверки кандидатов разрешить удаление, установив dry-run=false.

Rollback: выключить generation и cleanup, пересоздать Media. Pending jobs и private artifacts сохраняются, originals продолжают работать. Не выполнять destructive migration Down. После устранения причины Failed-задания можно вернуть в Pending отдельной операторской процедурой с выборкой конкретных IDs, сбросом attempts и проверкой binding/tombstones; массовый бесконечный retry не предусмотрен.

Для наблюдения доступны bounded metrics и SQL без source keys/URL:

```sql
SELECT "Status", count(*), min("CreatedAtUtc") AS oldest
FROM "PhotoVariantJobs" GROUP BY "Status";
SELECT "FailureCode", count(*) FROM "PhotoVariantJobs"
WHERE "Status" = 'Failed' GROUP BY "FailureCode";
```

## Проверки

Локальный прогон на момент завершения P2: 2 391 passed / 192 skipped для backend solution; Release build — 0 errors. Отдельно Media с включённым PostgreSQL gate: 128 passed / 3 skipped (три MinIO/renderer сценария требуют Docker). P1 access query gate — ещё 1 passed. Deployment tests: 8 passed; compose config, YAML, shell syntax и `git diff --check` проходят. Числа skipped не означают выполненную интеграционную проверку.

Изолированные .NET tests покрывают подпись/источник/presets, corrupt/неверный формат/большие размеры, alpha, полный decode, ограничение ответа без Content-Length, byte cap исходника, неполный manifest, cleanup reference/lease/grace и публикацию события удаления всеми текущими Community маршрутами.

`PhotoVariantQueueLiveTests` реально выполнены локально на временном PostgreSQL 18.6: fresh schema, populated upgrade, rollback enqueue, duplicate, lease expiry/reclaim, retry exhaustion, delete-before-attach и DELETE, удерживающий row lock против concurrent Ready. Также выполнен P1 `CheckInPhotoAccessLiveTests`. CI использует PostgreSQL 17 и существующий guarded `COFFEEPEEK_LIVE_PG`/pgpass flow.

`PhotoVariantPipelineLiveTests` добавлены в CI: provisioning IAM, pinned imgproxy + MinIO, шесть результатов, отсутствие upscale, alpha fallback, anonymous denial, подпись, запрет reader DELETE/writer original GET, dry-run и фактическая уборка без удаления оригинала. Их локальный запуск ещё не выполнен: Docker daemon недоступен. Подключение gate в workflow не означает успешный прогон CI.

Для ручного запуска на изолированном стенде задать `COFFEEPEEK_LIVE_PG`, `COFFEEPEEK_LIVE_MINIO`, `COFFEEPEEK_LIVE_MINIO_ACCESS_KEY/SECRET_KEY`, `COFFEEPEEK_LIVE_RENDERER`, `COFFEEPEEK_LIVE_RENDERER_KEY/SALT`, `COFFEEPEEK_LIVE_READER_USER/PASSWORD`, `COFFEEPEEK_LIVE_VARIANT_USER/PASSWORD`, `COFFEEPEEK_LIVE_VARIANT_READER_USER/PASSWORD`; создать private buckets/policies тем же provisioning-скриптом. Затем:

```bash
dotnet test CoffeePeek.MediaService.Tests --filter 'FullyQualifiedName~PhotoVariantQueueLiveTests|FullyQualifiedName~PhotoVariantPipelineLiveTests'
```

Перед production enable остаются настоящий broker restart/redelivery, fault injection S3/DB/renderer, полный quality corpus (включая EXIF, HEIC и анимацию), замеры p95/памяти и backlog. P2 не меняет фактическое время загрузки у пользователей до подключения P3/P4.
