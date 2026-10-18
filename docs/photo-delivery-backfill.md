# P5: управляемый backfill вариантов фотографий

PR 16 [общего плана](photo-delivery-improvement-plan.md) добавил возобновляемый обход архива с checkpoint в Media PostgreSQL. Production-инвентаризация проверила 45 originals; пилот обработал пять фото, все Ready. Владелец разрешил завершить весь архив и включить варианты всем; актуальный порядок — в [production runbook](photo-delivery-production.md). Ниже описаны durable producer и его низкоуровневый CLI.

## Подготовка

1. Проверить отчёт инвентаризации: доступность immutable originals, `needs_transfer` / `provenance_unverified`, missing/corrupt объекты, metadata mismatches, Failed jobs и свободное место. Перенос публичных originals и исправление данных выполняются отдельной процедурой; этот backfill их не делает. Прогноз storage строится по измеренным полным Ready manifests, время — по реальному пилоту.
2. До запуска нового Media применить additive migration `20261018132514_AddPhotoBackfillRuns` штатным migration step (`make up-media` из корня репозитория с конфигурацией нужной Media DB). Она добавляет `PhotoBackfillRuns`, nullable `BackfillRunId`, приоритет и ограничения очереди. Существующие jobs получают priority 0; оригиналы, их ключи и Ready manifests сохраняются. Fresh install и populated upgrade должны пройти до выпуска.
3. Проверить приватность buckets, read-only original identity, variant writer/reader, readiness и внутренний renderer согласно [P2 runbook](photo-delivery-variants.md). Media должен обращаться к внутреннему Community candidates endpoint через настроенный service HttpClient. Service secret Media и Shops должен совпадать. Не направлять его на публичный origin или браузер.
4. Установить `PHOTO_BACKFILL_SERVICE_SECRET` через менеджер секретов. Это доверенный service secret, не пользовательский bearer token. CLI требует Python 3.12, использует stdlib и закрытый сетевой доступ к Media. Не записывать секрет в аргументы или историю команд. Внутренние run endpoints отсутствуют в публичном Gateway routing и отвечают с `no-store`.

Новые архивные jobs исполняются существующим worker только при включённом `PhotoVariantOptions.Enabled` (`CHECKIN_VARIANTS_ENABLED` в production Compose). Создание или возобновление run не меняет feature flags, доступ к фотографиям или клиентскую аудиторию. По умолчанию rollout-флаги выключены.

## Создание и ручное продвижение

Каждая команда делает один запрос. Планировщика архивного обхода, автоматического `resume` и цикла `advance` нет. После рестарта Media checkpoint сохраняется; последующее продвижение требует явной команды. Адреса ниже заменить на доступный приватный origin. Значение `RUN_ID` взять из ответа `create`.

```sh
python dev/photo-variants/photo_backfill_runs.py create \
  --media-url http://localhost:5025 --page-size 10 --interval-seconds 5

python dev/photo-variants/photo_backfill_runs.py status \
  --media-url http://localhost:5025 --run-id "$RUN_ID"

python dev/photo-variants/photo_backfill_runs.py resume \
  --media-url http://localhost:5025 --run-id "$RUN_ID"

python dev/photo-variants/photo_backfill_runs.py advance \
  --media-url http://localhost:5025 --run-id "$RUN_ID"

python dev/photo-variants/photo_backfill_runs.py pause \
  --media-url http://localhost:5025 --run-id "$RUN_ID"
```

`create` всегда сохраняет **paused** run. Значения по умолчанию: 10 фотографий за страницу, интервал 5 секунд и recent window 30 дней. Допустимы batch 1–100 и интервал 5–3600 секунд. `--before` задаёт фиксированный UTC ISO timestamp; без него берётся время создания команды. `BeforeUtc`, `RecentSinceUtc`, recipe, batch и интервал сохраняются на сервере и не переопределяются командами `resume` / `advance`.

`resume` разрешает последующие `advance` и claim уже поставленных архивных jobs. Он сам не читает следующую страницу. Каждый успешный `advance` проверяет одну страницу Community, повторно проверяет current Media binding/tombstones и атомарно фиксирует enqueue с checkpoint. При ошибке страницы транзакция не продвигается. Повтор после сбоя безопасен благодаря checkpoint и уникальности `(PhotoId, SourceVersion, RecipeVersion)`.

При сетевом timeout исход запроса может быть неизвестен: сначала выполнить `status`, затем решить, нужен ли следующий `advance`. CLI не повторяет writes автоматически. Сервер выставляет `NextBatchAtUtc` после обработки страницы; время чтения Community не расходует интервал между batch. Предусмотрен общий для текущего recipe предел **200 outstanding архивных jobs** (`Pending`, `RetryScheduled`, `Processing`). Размер следующей страницы уменьшается до свободного остатка этого бюджета.

## Приоритет, pause и завершение

Новые uploads имеют priority **0**, архив — **1**. При следующем claim worker выбирает готовые к исполнению новые uploads раньше всего архивного backlog. Одна уже начатая архивная попытка не прерывается ради upload или `pause`: её штатный timeout — 3 минуты. Lease длится 5 минут и обеспечивает восстановление после аварии. Это не обещание нулевого ожидания для нового фото; очередь архива не должна добавлять ожидание за текущей попыткой.

`pause` прекращает продвижение checkpoint и новые claims этого архива. Уже выполняющаяся попытка может завершиться; новые пользовательские uploads продолжают обрабатываться. Для остановки всех преобразований выключается generation flag с пересозданием Media, как описано в P2. Отдельного динамического переключателя worker этот API не добавляет.

`status` возвращает параметры и counters run, число его jobs по каждому статусу и `generationDrained`. Значения трактуются так:

- `scannedCount` — наблюдения кандидатов; переходы между живыми фазами могут дать повторное наблюдение одного фото. Это не число уникальных готовых фотографий.
- `enqueuedCount` — новые durable jobs; `existingCount` включает уже существующие Pending, Ready, Failed или Cancelled jobs и не доказывает coverage.
- `blockedCount` — неподтверждённые, изменившиеся, удалённые или неподходящие привязки; оригиналы автоматически не исправляются.
- `scanComplete=true` означает завершение обхода фаз. Run остаётся активным, чтобы поставленная очередь могла обработаться.
- `generationDrained=true` означает завершённый обход и отсутствие Pending/RetryScheduled/Processing jobs, всё ещё привязанных к этому run. Failed/Cancelled могут оставаться. Jobs, повышенные до приоритета upload, больше не входят в эту группу. Этот флаг не означает 100% Ready coverage.

После `scanComplete` ждать обработки очереди и проверить все job statuses. Затем поставить run на pause. Не ставить завершённый обход на pause раньше времени, если его jobs должны продолжать исполняться.

| Результат | Действие оператора |
| --- | --- |
| `paused` | Проверить причины остановки; при готовности явно вызвать `resume`. |
| `rate_limited` | Дождаться `NextBatchAtUtc`; не повышать частоту повторов. |
| `queue_full` | Дождаться worker; проверить errors, retries и renderer/storage. |
| `another_run_active` | Разобраться с единственным активным run, затем завершить или поставить его на pause. |
| `previous_archive_backlog` | Возобновить и обработать старый run того же recipe; новый не присваивает его jobs. |
| `recipe_changed` | Не переписывать recipe существующего run. Поставить его на pause и создать новый проход для recipe текущего бинарника. |
| `scan_complete` | Проверить `status` и обработку очереди; новые `advance` этот обход не перезапускают. |

Включение второго run ограничено PostgreSQL lock и уникальным индексом. При смене recipe paused jobs неподдерживаемого recipe сохраняются и не занимают бюджет текущего worker; готовые варианты сохраняют собственную версию. Их дальнейшее восстановление — отдельная операторская процедура. Не менять recipe/source/key существующей job и не выполнять массовый reset attempts или Failed/Cancelled. Повторный backfill сохраняет terminal status; расследование причины и согласованное исправление конкретных jobs проводятся отдельно с актуальной проверкой binding/tombstones.

Таблица описывает `outcome` внутреннего API. Для конфликтов `resume` сервер возвращает HTTP 409, а CLI намеренно не печатает response body: он сообщает об ошибке и предлагает проверить status. Не включать вывод произвольных service error bodies ради диагностики.

## Фазы и reconciliation

Порядок: последние approved-public shop photos (`RecentPublicShop`) → остальные shop check-ins, включая private/pending/hidden (`OtherShop`) → home, включая drink cutouts (`Home`) → старый public shop archive (`PublicShopArchive`). Public eligibility использует текущую approved revision и видимость кофейни. Cutoffs заморожены; keyset идёт по `(SortAtUtc, CheckInId, PhotoId)` по убыванию. Это живой проход, а не распределённый snapshot.

Поздняя транзакция или изменение публикации/видимости могут переместить кандидата за пройденный cursor. Для reconciliation выполнить новую read-only инвентаризацию с новым checkpoint и затем создать новый backfill run с начала. Старый cursor не используется как постоянный watermark. Уникальный job key исключает повторную генерацию уже поставленного source/recipe. Для утраченной Community Ready projection дополнительно применяется [bounded replay P3](photo-delivery-api.md), а не повторный рендер всех фотографий.

Backfill не меняет visibility, content/approved revision, publication time, originals и их storage keys. Community остаётся владельцем бизнес-доступа. Generation и Ready event не публикуют фотографию и не обходят protected API; current binding, tombstone и обычная проверка читателя продолжают действовать.

## Пилот и rollout

Переход к реальному архиву требует отдельного контролируемого запуска. Сначала staging и canary accounts с малой партией. Измерить фактические Ready bytes, throughput, renderer CPU/RSS, очередь новых uploads, ошибки, p95 и storage headroom; сравнивать одинаковые источники и версии клиентов. Включение клиентов выполняется средствами выпуска, этот CLI не задаёт процент аудитории.

После production-пилота владелец разрешил полный запуск без промежуточных процентов и ожидания между ними. Ограничение advertisement на canary accounts удалено; варианты доступны всем читателям с обычной проверкой доступа. Полный архив завершается через `rollout-*` [production workflow](photo-delivery-production.md). Denominator и cohort подтверждаются свежей инвентаризацией и фактической доступностью Ready projection, а не `existingCount` или одним `generationDrained`. При ухудшении очереди/ошибок остановить архив раньше новых uploads; при нарушении отзыва доступа отключить соответствующее кеширование и расследовать причину.

## Откат и поддержка выданных URL

Для мягкого отката поставить архив на pause, выключить `CHECKIN_VARIANTS_ENABLED` и `CHECKIN_VARIANTS_ADVERTISE_ENABLED`, сохранив `CHECKIN_VARIANTS_READ_ENABLED` и `CHECKIN_VARIANTS_SERVE_ENABLED`. Дескрипторы возвращаются к legacy original URL, а уже выданный versioned variant URL продолжает отдавать свой точный формат и bytes после обычной проверки доступа. При необходимости отдельно выключить `CHECKIN_VARIANTS_CACHE_ENABLED` согласно бюджету P3/P4. Перед откатом бинарника остановить generation workers: старая версия может не учитывать архивный priority/pause.

Текущая политика совместимости: **поддержка уже выданных versioned URL сохраняется бессрочно, до отдельной согласованной retirement policy**. Автоматического удаления через 30 дней или при смене recipe нет. Политика не продлевает бизнес-доступ: delete, tombstone, replacement и отказ авторизации по-прежнему закрывают фотографию; безопасная очистка удалённых/неиспользуемых artifacts подчиняется P2 fences. По URL WebP-рецепта нельзя возвращать JPEG, новый recipe или original.

Не удалять originals, Ready artifacts, manifests, tombstones и checkpoints ради отката. `Down` этой additive migration намеренно запрещён; изменения схемы выполняются forward migration. Для аварийного закрытия вариантов можно выключить read/serve, получив 404/503; это отдельный аварийный режим, а не сохранение совместимости. Оригинал открывается только своим защищённым маршрутом после проверки доступа.

## Проверки и границы готовности

Тесты PostgreSQL покрывают populated upgrade, restart/checkpoint, rollback между enqueue и checkpoint, конкурентные advances, приоритет uploads, pause, queue cap, сохранение Failed и смену recipe. CI должен обязательно выполнять `PhotoBackfillRunLiveTests` и `CheckInPhotoBackfillCandidatesLiveTests` без skips; Python tests проверяют operator CLI, ошибки и synthetic HTTP, MinIO/renderer corpus gates проверяют приватность и pipeline P2. Наличие теста или его включение в CI не означает успешный прогон текущего commit.

Production-пилот пяти фото выполнен с PostgreSQL/MinIO/RabbitMQ/renderer: Ready без Failed jobs, p95 queue-to-Ready 5479,7 мс; feed WebP для тех же пяти originals занимает на 96,6% меньше байт. Это измерение пилота, не клиентская latency и не гарантия скорости всего архива. Отдельные нагрузочные и device/browser проверки не подменяются этими числами. Свежая готовность полного архива подтверждается отдельным production отчётом.
