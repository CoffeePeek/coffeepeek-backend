# Импорт кофе: жизненный цикл снимка (P21)

`CoffeeImportRecoveryLiveTests` добавляет настоящую PG barrier по `pg_locks`: новый и
старый снимки конкурируют в обоих порядках commit. Итоговые offers принадлежат новому
снимку, имя/страна/classification protection и ручной roaster tag остаются прежними.
Третий сценарий бросает ошибку после реального SQL flush, проверяет старое состояние
через независимое подключение, `Failed` с закреплённым payload hash и успешное
продолжение тем же payload без дубликатов. Класс обязателен в CI; новые сценарии
пока только собраны и ожидают выполнения PostgreSQL gate.

Один `CoffeeSnapshotImportRequest` применяется как единая транзакция PostgreSQL. В этой же транзакции меняются продукты/источники/предложения и `CoffeeImportRun` получает статус `Applied`. Операции сериализованы существующей транзакционной advisory-блокировкой в `PublicAddressTransaction`; перед записью проверяются `(SourceKey, SnapshotId)`, хеш payload и время последнего применённого снимка того же источника. Повтор уже применённого снимка возвращает тот же run без новых продуктов. Более старый снимок и повтор ID с другим содержимым получают 409.

Состояние `CoffeeImportRun` меняется только через `MarkFailed`/`MarkApplied`; публичных setters нет. `Pending` существует внутри ещё не сохранённой операции, сохранённый run получает `Failed` или `Applied`. Конструктор проверяет hash identity, операции — UTC timestamp и counters. Повтор уже применённого payload сохраняет первоначальные counters и время применения; поздняя ошибка не может заменить подтверждённый успех.

Прикладной `CoffeeImportService` определяет identity/order, coverage, manual priority, классификацию и применение снимка через `ICoffeeImportStorage`. Persistence adapter загружает tracked aggregate graph, добавляет/удаляет перечисленные дочерние записи и выполняет транзакцию; бизнес-решений о ручных полях и lifecycle в нём нет. После rollback tracker очищается до записи failed run. Concrete `CoffeeCatalogStore.ImportAsync` остаётся совместимым делегатом; production DI направляет `ICoffeeImportWriter` в Application service.

`POST /internal/coffee-import/failures` может прийти до появления полного payload. Такой run имеет статус `Failed`, а его `PayloadHash` временно равен `SnapshotId`. При первом полном применении с тем же `(SourceKey, SnapshotId)` `CoffeeImportRun` распознаёт report по префиксу `collect:`, `photos:` или `apply:` и закрепляет вычисленный хеш payload. Следующие повторы требуют точного совпадения хеша. Failed run, созданный самим применением после отката транзакции, уже содержит фактический хеш и не разрешает другой payload.

Повторные failure reports идемпотентны даже при одновременной вставке: уникальность `(SourceKey, SnapshotId)` сохраняет PostgreSQL, а проигравший запрос обрабатывает только конфликт этого конкретного индекса и удаляет неудачную запись из EF tracker.

Checkpoint здесь — целый снимок: после сбоя его можно отправить повторно с тем же ID и тем же содержимым. Промежуточного batch checkpoint нет. Неполный снимок (`Complete = false`) отвергается до записи; пустой снимок требует явных excluded IDs. Массовое снятие отсутствующих предложений включается только при подтверждённой coverage (`ExpectedProducts == DiscoveredProducts` для каждой записи) и ограничении на падение числа продуктов относительно прошлого применённого снимка. Если coverage не знает ожидаемого числа, отсутствующие предложения автоматически не снимаются. Защищённые ручные поля продукта сохраняются доменными методами импорта.

Проверки: `CoffeeCatalogTests.ReportedPreApplyFailureCanResumeWithSameSnapshotIdentity` воспроизводит прежний 409, подтверждает успешный повтор и запрет повторного использования ID с другим содержимым; `MissingOffersAreRetiredOnlyWhenCoverageIsConfirmed` различает неполную и подтверждённую coverage. Остальные `CoffeeCatalogTests` покрывают импорт, старый снимок, защиту ручных полей и неполный/пустой снимок. PostgreSQL сценарии `DiscoveryQueryLiveTests.ReportedFailureResumesAtomicallyAndPinsPayloadHash_ExecuteInPostgres` и `ConcurrentFailureReportsKeepOneRun_ExecuteInPostgres` выполняются в live CI gate; локально без `COFFEEPEEK_LIVE_PG` они пропускаются.
