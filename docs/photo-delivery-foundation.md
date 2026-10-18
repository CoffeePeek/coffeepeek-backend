# Доставка фотографий: первая часть реализации

Основа: `main` на `50d78de6`. Ветка: `codex/photo-delivery-foundation`.
Это backend-часть P0/P1 [большого плана](photo-delivery-improvement-plan.md), до генерации вариантов и клиентской интеграции.

## Изменение поведения

Защищённый URL оригинала сохраняется. Shops делает один узкий запрос `CheckInPhotoAccessQueries`: автор, ID запрошенного фото, content type, kind, revision. Используются текущие `CheckInEligibility` и Account exclusions. Запрос не загружает галерею, helpful votes, название кофейни или текст чекина. Shop/home и отдельная вырезка напитка проходят через один порт. Moderator route сохраняет проверку текущей public revision.

Media читает файл через отдельный `ICheckInPhotoReader`. В нём нет `BucketExists`, создания бакета и изменения policy. Таким образом из обычного download исключены два `EnsureCheckInBucketPrivateAsync` (четыре управляющих обращения к MinIO). Закреплённый SDK 7 перед GetObject делает bucket/object HEAD и требует лишних IAM прав. Reader подписывает короткий GET локально и передаёт bytes одним HTTP GET без этих HEAD; подписанный URL остаётся внутри Media и исключён из трассировки/ошибок. Операции загрузки, claim и исторического переноса продолжают пользоваться write-service и его проверками.

Бакет создаётся/закрывается provisioning-скриптом и проверяется при запуске Media. `CheckInStorageMonitor` затем проверяет bucket existence/policy раз в 10 секунд, с таймаутом 5 секунд. Успешная проверка действует максимум 30 секунд от начала проверки по монотонным часам. Ошибка, непустая неожиданная policy или истечение срока закрывают чтение и переводят `/health` в unhealthy. Монитор не исправляет policy в фоне: оператор устраняет причину, после успешного аудита сервис возвращается в readiness. Внешнее ошибочное открытие бакета требует немедленного исправления в MinIO; блокирование внутреннего API само по себе не закрывает публичный S3 endpoint.

Доступ к содержимому проверяется заново на каждом запросе. Кеширование ещё не включено: originals и moderation остаются `private, no-store`. Согласованный предел отзыва 60 секунд относится к следующим этапам кеширования; lease инфраструктурной готовности не заменяет бизнес-авторизацию.

## Конфигурация и выпуск

Новые параметры Media: `CheckInPhotoReadOptions__AccessKey` и `CheckInPhotoReadOptions__SecretKey`. Они обязательны. Нельзя использовать ту же identity, что у write-service. `deploy/minio/checkin-reader-policy.json` разрешает только `s3:GetObject` в `coffeepeek.checkins/*` и чтение location этого бакета. Учётная запись предназначена только для этой policy; не прикреплять к ней дополнительные полномочия. При нестандартном имени бакета нужно согласованно изменить provisioning, IAM resource и `MinIOOptions__CheckInBucketName`.

Штатные `deploy.sh`, `ci-deploy.sh` и `update.sh` используют `ensure-secrets.sh`: отсутствующие `MINIO_CHECKIN_READ_USER`/`MINIO_CHECKIN_READ_PASSWORD` создаются один раз в существующем `deploy/.env`; пароль не печатается. Существующие значения сохраняются. `update.sh` повторно выполняет текущий provisioning до замены backend, включая путь с `--no-deps` у import hosts. Новые файлы `deploy/minio/` должны попасть на сервер вместе с compose.

Перед выпуском на стенде:

1. Выполнить provisioning; повторить и убедиться в идемпотентности.
2. Проверить reader credentials: чтение известного объекта разрешено, PUT/DELETE/изменение policy и чтение других бакетов запрещены. Проверить отсутствие лишних IAM policy у reader identity.
3. Запустить Media, дождаться `/health` 200; через gateway проверить anonymous approved, private owner, denied other, cutout и moderator revision.
4. На отдельном тестовом бакете изменить policy и проверить unhealthy/503, затем восстановить. Не проводить этот сценарий с реальными приватными фото.
5. Сравнить одинаковый набор фото до/после. Размер оригиналов на этом этапе не уменьшается; выигрыш ожидается в серверной работе перед передачей байтов, его величина требует замера.

Локально `dev/dev.env` и dev compose содержат согласованные тестовые reader credentials. После обновления запустить backing services, затем `docker compose -f dev/docker-compose.dev.yml run --rm --no-deps minio-init` и обычный `dev/run-services.sh`.

Откат приложения не требует миграций БД. Прежний бинарник работает с оставшимися writer credentials и возвращает старый download path. Reader identity и приватность бакета можно оставить. Существующие branch/stash с прежней локальной работой не затрагивались.

## Измерения

Источник OpenTelemetry: `CoffeePeek.PhotoDelivery` (подключён в Shops и Media). Экспорт включается существующим `OTEL_EXPORTER_OTLP_ENDPOINT`.

| Метрика | Назначение | Измерения |
| --- | --- | --- |
| `photo.stage.duration`, s | access, Account, download, MinIO GET/stream, policy write/audit | stage, reader, outcome |
| `photo.stream.bytes`, By | успешно записанные downstream bytes, включая неполную передачу | stage, outcome |
| `photo.responses` | результат Shops download, включая отказ/503/cancel | reader, status, cache=no_store |

`account` измеряет общий author-visibility adapter, в том числе вызовы других API; для анализа именно фото использовать родительский `photo.download` trace. `minio_get` включает время потока, а не только TTFB. Автоматические HTTP traces подписанных S3-запросов отключены: специальные photo spans не содержат object key, URL, token, photoId или userId. Bytes на Media и Shops отражают два участка одного потока; не суммировать их как клиентский трафик.

Скрипт `dev/performance/photo_delivery.py` требует Python 3 и curl. Пример для локального стенда:

```sh
python3 dev/performance/photo_delivery.py --base-url http://localhost:8080 \
  --environment local --page-size 20 --photo-limit 20 --repeats 50 \
  --concurrency 10 --output /tmp/photo-local.json
```

Для стенда использовать `--environment staging`; по умолчанию действует probe: не более 10 фото-запросов и concurrency=1. Load mode для известного production host отклоняется. Повторять с `--page-size 100`, concurrency 1/10/50. Сохранять версию backend, стенд, выбранные данные и характеристики машины рядом с результатом. Для авторизованного сценария передать JWT через переменную `COFFEEPEEK_PHOTO_TOKEN`, не через аргумент командной строки. Отчёт содержит непрозрачные hashes путей и выбранные заголовки, без URL и credentials. Скрипт не следует redirects.

Каждый проход делает реальные сетевые GET; curl не моделирует browser/bitmap cache. TTFB/total/bytes и sample p95 — сетевые измерения, без decode/render. `--limit-rate 625K` ограничивает отдельный поток, а не суммарный канал и не RTT. Целевой профиль 5 Mbit/s + 100 ms RTT нужно задавать на тестовом устройстве/браузере/сетевом стенде. Эти результаты не подменяют RUM обоих клиентов.

`generate_photo_corpus.py /tmp/photo-corpus` создаёт 8 синтетических fixtures (требуется Pillow): portrait, landscape, 4K, tiny JPEG, alpha PNG, EXIF rotation, animated GIF, текст меню. Manifest содержит размеры, SHA-256, seed и версию Pillow. Это набор проверки обработки форматов, не репрезентативные фото для утверждения качества и 70% экономии. Реальные обезличенные фото добавляются на стенде перед P2; бинарные fixtures в git не включаются.

## Проверки и оставшаяся работа

Добавлены parity tests narrow/detail для shop/home, private/pending/rejected/approved, скрытой кофейни, owner/anonymous/other, excluded author, удаления, drink cutout и moderator current/stale revision. Отдельный тест проверяет PostgreSQL SQL translation, без сервера. Контроллерные тесты продолжают проверять protected URL, MIME/attachment/nosniff, отсутствие обращения к Media при отказе.

Media tests проверяют readiness lease/expiry/failure/recovery, отсутствие управляющих MinIO вызовов у reader, отсутствие policy и AccessDenied через реальный MinIO 7 SDK с тестовым HTTP transport. Тесты harness проверяют credential/redirection boundary и измерение локального HTTP-сервера. Для интеграции добавлены `CheckInPhotoAccessLiveTests` (один SQL query, EXPLAIN, актуальные ограничения) и `CheckInPhotoStorageLiveTests` (реальный MinIO, поток, policy drift). Оба подключены в существующий CI; это не означает, что CI уже запускался на этой ветке.

Для MinIO live gate нужны `COFFEEPEEK_LIVE_MINIO`, `COFFEEPEEK_LIVE_MINIO_ACCESS_KEY`, `COFFEEPEEK_LIVE_MINIO_SECRET_KEY` тестового сервера. Gate создаёт собственный временный бакет и удаляет его после проверки. Он проверяет S3 data path, а реальную IAM конфигурацию отдельного reader нужно проверить при provisioning стенда. Для PostgreSQL используется существующий `COFFEEPEEK_LIVE_PG` и pgpass flow.

Локальный полный backend test run: 2 379 passed / 183 skipped до добавления MinIO live gate; новый opt-in gate без сервера добавляет ещё один skip. На следующем этапе `CheckInPhotoAccessLiveTests` успешно выполнен на изолированном PostgreSQL 18.6 (query/EXPLAIN/access parity). MinIO и замер ускорения текущей ветки пока не выполнены: локальный Docker daemon выключен, `sudo -n dockerd` требует пароль. Генерация превью реализуется в [P2](photo-delivery-variants.md); manifests, ETag/304, клиентские кеши и UI, backfill и CDN остаются следующими этапами. P0 также требует клиентские RUM, реальную выборку качества, размеры UI/DPR и ресурсы стенда.

Обновление при публикации PR: постоянный rootless Docker установлен; PostgreSQL/MinIO/imgproxy проверки выполнены. Актуальные результаты и ограничение доступности upstream MinIO images — в [P3 API и проверках](photo-delivery-api.md).
