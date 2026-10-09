# Изолированный fault gate Shops/Media

ShopsFaultRecoveryLiveTests содержит семь обязательных родительских cases. Класс
запускается отдельно с -failSkips; positive-total/pass/no-skips TRX guard делает CI
красным при любой failed assertion. Worker запускается дочерним dotnet-процессом и
завершается Process.Kill только после наблюдаемой контрольной точки.

Три исходных physical cases прошли в [CI fd8604bd](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37909817024).
[CI 0295699a](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37921729495)
также выполнил PG ACK-loss и before-store OCR recovery; два остальных OCR cases
прошли saved-offer/provider-once assertions, но упали на незагруженной Photos navigation
в финальном readback fixture. Include исправлен; [CI cb386808](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37923822811)
выполнил все семь cases: 7 passed, 0 failed/skipped/errors/not-run. В том же run
прошли все 29 mandatory PG classes/148 tests, backend regression и legacy PG/MinIO.

Контур использует PostgreSQL 17, RabbitMQ 4.1, Redis 7, MinIO и Toxiproxy 2.12.
Proxy разрывает настоящие TCP connections; polling ограничен 45 секундами, kill
не зависит от случайного sleep. Родитель читает бизнес-данные отдельным DbContext.

| Case | Проверка |
| --- | --- |
| Redis недоступен после profile commit | Одна ревизия в PG, старый настоящий Redis key, production failure counter и durable private envelope. Kill/restart после восстановления TCP удаляет key без второго PUT. |
| Kill после Redis DELETE до consumer ack | Незавершённый envelope восстанавливается; повтор DELETE безопасен, ревизия остаётся одна. |
| Lost Media reply + Rabbit outage + kill Media после commit до ack | Реальный claim сохраняется в Media PG/MinIO; retry сохраняет destination/photo IDs. Shops business row и outgoing envelope сохраняются вместе. Новый sender/receiver завершает доставку; повтор business event не добавляет фото/обжарщика. |
| PG commit acknowledgement loss | После реального SQL flush proxy задерживает PG downstream. Независимое подключение видит committed profile v1 до client ack; TCP разорван и writer убит. Retry ExpectedRevision=0 возвращает ConflictException/409, v1 сохраняется, original private invalidation envelope удаляет настоящий Redis key. |
| OCR kill до ResultStored | Один provider call без SQL transaction; после restart Running не вызывает paid adapter снова. Admin unknown-outcome recovery закрывает job; late result отклонён. |
| OCR kill после ResultStored | JSON уже committed; restart применяет stored offer/фото без повторного provider call. |
| OCR kill после apply SQL flush до commit | Private write/envelope IDs невидимы другому подключению; tracked offer и actual SQL value проверены до kill. Restart применяет stored result один раз, cache callback видит terminal committed job. |

ACK-loss доказывает именно неопределённое подтверждение реального commit. Он проверяет
command ConflictException/409; HTTP envelope mapping проверяется отдельными tests.
Проверки recovery связываются с ID собственных envelopes, а не общим размером inbox
shared fixture.

Redis consumer, profile writer, Media PhotoRepository/claim/finalization/UnitOfWork/
MinIO adapter и OCR MenuModule/job store/menu service — production implementations.
Claim lost reply имитируется lookup adapter после actual server reservation; paid
vision/download adapters детерминированны. Это не доказательство exactly-once billing
внешнего API. Runtime compilation применяется только к test host composition;
release generated adapters проверяются backend/host suite.

Selective DLQ replay и production gauge исполняются отдельно в ShopProfileOutboxLiveTests:
две работы отправляются в настоящий DLQ, dependency восстанавливается, только один
MessageId replayed, второй остаётся в DLQ; repeat replay не создаёт второй business
effect. Import races/SQL rollback/hash recovery — CoffeeImportRecoveryLiveTests.

Запуск разрешён только на disposable loopback контейнерах и guarded cp_live_* DB:
COFFEEPEEK_LIVE_PG с maintenance DB postgres/pgpass, COFFEEPEEK_LIVE_FAULTS=1,
COFFEEPEEK_LIVE_MINIO и proxies shops-redis/shops-rabbit/shops-postgres из CI.
Каждый case использует собственный cp-fault-* bucket. Finally завершает только свои
child processes, восстанавливает proxies, удаляет свои объекты/БД и сохраняет worker
logs в shops-fault-workers artifact. Deployment endpoints здесь не используются.

Этот короткий fault contour не измеряет production SLO, длительный soak или сбой
всего PostgreSQL cluster. Ограничения и recovery procedures — [operations](shops-operations-runbook.md).
