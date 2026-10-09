# ShopsService: наблюдаемость и восстановление

Срез PR #340, 9 октября 2026 года. Production rollout выполняется отдельно. Диагностика не публикует сырой OCR JSON, фото, JWT, credentials или пользовательские payload. Метрики не содержат ShopId/JobId/envelope IDs; эти идентификаторы допустимы в контролируемых traces/logs.

## Сигналы

При OTEL_EXPORTER_OTLP_ENDPOINT общий exporter подписан на Meter CoffeePeek.Shops и Wolverine*, ActivitySource CoffeePeek.Shops и Wolverine. У установленного Wolverine 6.35 runtime Meter называется Wolverine:{ServiceName}. В этой работе подписка проверена по pinned source; реальный collector/dashboard в доступном deployment не найден.

| Инструмент | Значение |
| --- | --- |
| shops.cache_invalidation.failures | Неуспешная strict invalidation; фиксированные source=profile/check_in. |
| shops.ocr.jobs.completed, shops.ocr.jobs.age | Терминальный outcome=applied/stale/failed после commit и возраст задания. |
| shops.media.claim.failures | Фиксированные kind/reason, отказ claim. |
| shops.write.conflicts | Фиксированные aggregate=profile/menu. |
| Wolverine incoming/outgoing/scheduled gauges | Штатный backlog sampler библиотеки. |
| shops.durability.dead_letters | Provider-reported DLQ count. PostgreSQL counts могут быть estimates для больших таблиц. |
| shops.durability.outbox.observed_age | Возраст backlog, наблюдаемого этим процессом между успешными 30s samples; reset после restart/empty sample. |
| shops.durability.sample_age | Возраст последнего успешного storage sample. Failed poll сохраняет предыдущий sample; до первого успеха значение отсутствует. |

observed_age не является возрастом самого старого envelope. Wolverine 6.35 по умолчанию не хранит enqueue timestamp в outgoing table. Не включать OutboxStaleTime только ради метрики: это также меняет срок хранения сообщений. При failed polls непрерывность backlog неизвестна; всегда оценивать observed_age вместе с sample_age.

Liveness и readiness разделены стандартными service defaults; Shops PG check отмечен readiness. Внешний OCR/Media не останавливает liveness. Политика Solo рассчитана на одну реплику: многорепличный rollout требует отдельной проверки ownership/recovery.

## Cache / broker / неоднозначный commit

1. Найти exception и идентификатор business operation/envelope; восстановить Redis/RabbitMQ, не повторять исходный POST только из-за cache failure.
2. Проверить business row/revision через свежий контекст и штатные storage counts. После commit invalidation должна восстановиться без второго PUT.
3. При потерянном commit acknowledgement сначала перечитать бизнес-данные. Повтор profile command с прежней ExpectedRevision получает 409; HTTP exception mapping проверяется отдельно.
4. Для outbox пользоваться штатным `dotnet CoffeePeek.ShopsService.dll storage counts` с обычной конфигурацией сервиса. SQL/schema имена сперва сверить через information_schema; manual DELETE/COPY envelopes не использовать.

Physical Redis, RabbitMQ/Media restart и PG commit-ack loss выполняются обязательным ShopsFaultRecoveryLiveTests. Selective DLQ replay и production gauge выполнены в ShopProfileOutboxLiveTests; весь contour, включая семь faults и три OCR checkpoints, прошёл в [CI cb386808](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37923822811).

## Dead letters

Сначала исправить зависимость/обработчик и сопоставить тип, envelope ID и business key. Стандартный IDeadLetters API установленного Wolverine поддерживает ID filter:

```csharp
var letters = host.Services.GetRequiredService<IMessageStore>().DeadLetters;
await letters.ReplayAsync(
    new DeadLetterEnvelopeQuery { MessageIds = [verifiedEnvelopeId] }, ct);
```

Это стандартный API для контролируемой operator composition, а не добавленный публичный HTTP endpoint. Live test сохраняет два DLQ сообщения, replay выбирает одно, подтверждает post-commit cache effect и одну ProfileRevision; второе остаётся в DLQ. Повтор выбранного ID не создаёт второй business effect.

Штатный CLI `storage replay --exception-type ...` в 6.35 фильтрует по exception type, а не по ID. Он может выбрать несколько unrelated envelopes: перед batch replay необходима полная сверка этого множества и idempotency handlers. Для точечного восстановления использовать MessageIds API. Broad unfiltered replay не является процедурой безопасного восстановления.

## OCR

Статусы: Pending=0, Running=1, ResultStored=2, Applied=3, Stale=4, Failed=5.

```sql
SELECT "Status", count(*) AS jobs, min("RequestedAtUtc") AS oldest_requested_utc
FROM "MenuOcrJobs" WHERE "Status" IN (0,1,2) GROUP BY "Status";
```

ResultStored redelivery применяет сохранённый ответ без повторного vision call. После ручного изменения revision/photo set старый результат становится Stale целиком. Running после process loss имеет неизвестный provider outcome и автоматически не оплачивается повторно.

Admin закрывает Running ≥15 минут через POST /api/admin/menu-ocr-jobs/{jobId}/recover-unknown-outcome. Locks и повторная проверка актуального input переводят job в Failed/provider-outcome-unknown либо Stale; поздний worker уже не может сохранить результат. Затем владелец может отдельно запросить новое OCR текущих фото. Автоматический повтор paid call не включать. Process gate использует deterministic provider/download adapters; он не доказывает exactly-once billing внешнего сервиса.

## Import и Media

Import Failed snapshot повторяется с теми же SourceKey/SnapshotId/payload: RunId стабилен, hash mismatch и старый collected-at отклоняются. Concurrent snapshot ordering и отказ после SQL flush проверены CoffeeImportRecoveryLiveTests.

Media claim повторяется с прежними attachment ID/Media photo IDs после неоднозначного ответа. Выпуск Media раньше Shops обязателен для Roaster kind=3; старые moderation events сохраняют identity. Брошенные claims разбираются только по [точечной процедуре](runbooks/shops-photo-claims.md), без автоматического освобождения.

Alert candidates: failed readiness >2m, растущий DLQ, backlog вместе со свежестью samples, рост invalidation/claim errors, Running возраст больше принятого SLA. Порог latency/OCR SLA и dashboard/alerts в реальном collector требуют данных deployment; они не объявлены установленными.
