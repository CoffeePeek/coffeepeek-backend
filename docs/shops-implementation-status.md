# ShopsService: статус P01–P35

Срез PR #340, 9 октября 2026 года. Доступная repo/CI часть P01–P35 завершена.
«Подтверждено» означает выполненную проверку в её указанной области. Production
acceptance требует отдельного доступа/данных и перечислена в [release packet](shops-release-readiness.md).

[CI cb386808](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37923822811)
полностью SUCCESS: backend build/regression, 29 mandatory PostgreSQL классов
(148 tests, ноль skips/failures), семь physical/process fault cases, три legacy
PostgreSQL/MinIO transfer/cleanup tests и 43 Python checks. Dependency audit не
сообщил vulnerable packages. Тестируемый PR merge SHA: be4c4a61fcc4bf36ec3e6abd8f23ab4b8fa994f0,
source head cb3868081d9f86e4c2aaa647580e000cd9a31ee1.

Предыдущие два OCR failures были ошибкой fixture: final query не загружал Photos.
Saved offer сохранялся; Include исправлен, все три OCR checkpoints теперь passed.
Production item-loss гипотеза снята. История review/fixes сохранена отдельно.

| Карточка | Доступная часть | Результат / предел |
| --- | --- | --- |
| P01 | Подтверждена | Актуальный [реестр HTTP/messages/SQL](shops-write-inventory.md) и P28 references. |
| P02 | Выполнена | Утверждённые ADR01–08 реализованы; неутверждённые продуктовые предложения не введены как новые требования. |
| P03 | Подтверждена | Owner modules/ports, единый snapshot, пустой cross-module write baseline. |
| P04 | Подтверждена live CI | Isolated PG, настоящие Redis/RabbitMQ/Media PG/MinIO faults; skips не заменяют gate. |
| P05 | Подтверждена | Обе architecture baselines пусты, проверки обязательны. |
| P06 | Подтверждена | Command transaction, validate-before-mutate, rollback/409, durable post-commit effects. |
| P07 | Подтверждена | Early flush под outer transaction; short import/OCR owner scopes, private-envelope/crash recovery. |
| P08 | Подтверждена | Шесть catalog readers читают DB; 12 Redis removals только в трёх durable consumers. |
| P09 | Подтверждена PG | City Restrict/409, preflight и concurrent insert/delete. |
| P10 | Подтверждена domain/PG | Private state, whole-profile validation, общая ProfileRevision/If-Match, concurrency→409. |
| P11 | Подтверждена PG/upgrade | Per-shop/category primary; ambiguous legacy flag не переносится выдуманным выбором. |
| P12 | Подтверждена domain/PG | Polygon/radius/version, nested schedules/gallery и копирование входных коллекций. |
| P13 | Подтверждена | Whole manual packet validation, Present positive price/volume, incomplete parsed offers пропускаются. |
| P14 | Подтверждена PG | Upsert/tracking reload, CHECK/uniqueness, concurrency/rollback. |
| P15 | Подтверждена PG/process | Job/version/photo snapshot, ResultStored, terminal state; три kill checkpoints. |
| P16 | Подтверждена PG/process | Provider вне transaction; unknown Running recovery/late rejection, stored apply после restart без повторного provider call. |
| P17 | Подтверждена physical gate | Stable claims/finalization/lost replies; Media-first, abandoned claims operator-only. |
| P18 | Подтверждена PG/legacy | Community CheckInPhotos, IDs/contracts, moderation/limits/votes races, additive archive transition. |
| P19 | Подтверждена domain/PG | Private catalog children, owner enforcement, manual/import concurrency. |
| P20 | Подтверждена architecture/PG | Application coordinators и narrow SQL/tracking Storage. |
| P21 | Подтверждена PG | Snapshot ordering в обоих порядках, SQL-flush rollback, Failed resume/same hash, mismatch409. |
| P22 | Подтверждена PG/experiment | Полные admin/owner photos/projections, page1/50 SQL counts постоянны. |
| P23 | Подтверждена PG | Filters, same-offer matching, privacy/VisitedOnly; optimized facet self-exclusion/legacy duplicate arrays. |
| P24 | Подтверждена PG/experiment | 20001 shops, 20000 clustered points/sentinel, full zone counts/stable primary markers. |
| P25 | Подтверждена | Modular DI/composition, host regression и пустой HTTP bypass baseline. |
| P26 | Подтверждена PG/restart | AppDistribution отдельно, read-only GET, active release/redirect после нового host; durable/idempotent analytics. Отдельный forced-kill analytics case не выполнялся. |
| P27 | Repo contracts подтверждены | Stable CLR/routes/auth; nullable isOpen и required published revision объявлены явно. Deployed legacy topology/envelopes — rollout prerequisite. |
| P28 | Audit подтверждён | Все HTTP bypass закрыты, 45 direct commit/rollback cases и enrolled OCR outbox; independent source/runtime audit. |
| P29 | Synthetic upgrade подтверждён | Populated old schema, double idempotent script, old SQL writer window, invalid preflight/no model drift. Production backup rehearsal ещё нужен. |
| P30 | Подтверждена | 29 mandatory classes с positive-total/pass/no-skips guard, backend/shared suite и diagnostics artifacts. |
| P31 | Подтверждена physical/process | Redis/Rabbit/Media, PG commit-ack loss, три OCR checkpoints, import races и selective DLQ replay. |
| P32 | Выполнена как experiment | Read/write/import/OCR metrics/plans; facets72→13 SQL, p95 1138→413ms в measured runs. Production budgets/soak не заявлены. |
| P33 | Выполнена в доступном стеке | Bounded metrics/readiness/OTLP subscriptions, gauge и selective replay; observed_age ограничен 6.35 semantics, deployment collector/alerts ещё нужен. |
| P34 | Independent review завершён | [Report](shops-independent-review.md), открытых доказанных source blockers в рассмотренном scope нет. |
| P35 | Пакет подготовлен | [Release packet](shops-release-readiness.md) связывает G1–G8, evidence, risks, migration/drain/stop/recovery. Production rollout не выполнялся. |

Локальный Release suite: 2064 passed, 177 infrastructure skipped, 0 failed, 13 test
projects. Это offline result; mandatory live доказан отдельным CI выше. Build
содержит analyzer/XML/nullable warnings; warning-free результат не заявлен.

## Ворота подготовки в доступном контуре

| Gate | Evidence и внешний предел |
| --- | --- |
| G1 Модульность | Architecture checks без исключений, owner maps/ports и independent SQL/DI audit. |
| G2 Защита агрегатов | Domain/PG invariants, private child state, constraints/concurrency. |
| G3 Единая запись | 45 direct cases, owner transactions, private envelopes, SQL-flush rollback и ACK-loss recovery. |
| G4 Асинхронная корректность | Three OCR checkpoints, import ordering/hash/resume, Media/cache/DLQ recovery/idempotency. |
| G5 Чтение | Semantic PG suite, complete photos/owners, map limit, optimized facets и измеренные SQL counts. |
| G6 Обязательный live contour | 29 classes/148 tests без skips, семь fault cases и полный CI SUCCESS. |
| G7 Переход | Synthetic populated upgrade и legacy PG/MinIO rehearsal; actual production backup/topology/client acceptance — до rollout. |
| G8 Эксплуатация | Signals/replay/recovery/runbooks и experiments готовы; actual load budgets/collector/long soak — deployment acceptance. |

Без deployment access нельзя закрыть actual backup rehearsal, legacy envelope/topology
inventory и draining, real client HTTP acceptance, production load/SLO/collector
dashboard, multi-replica Solo review. Это явно перечисленный внешний остаток, а не
необнаруженный writer или обещание выполненного rollout.

Документы: [P28](shops-p28-write-protocol-audit.md), [operations](shops-operations-runbook.md),
[performance](shops-performance-experiment.md), [faults](shops-fault-gate.md),
[migration rollout](shops-migration-rollout.md), [release packet](shops-release-readiness.md).
