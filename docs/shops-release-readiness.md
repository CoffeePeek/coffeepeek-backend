# P35: пакет готовности ShopsService к выпуску

Срез PR #340, 9 октября 2026 года. Реализация и доступные проверки перечислены в
[P01–P35 status](shops-implementation-status.md), [P28 audit](shops-p28-write-protocol-audit.md)
и [independent review](shops-independent-review.md). Выпуск не выполняется этим PR.
[CI cb386808](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37923822811) полностью SUCCESS:
29 mandatory PG classes/148 tests без skips, семь faults, backend regression,
legacy PG/MinIO rehearsal и Python checks. Independent review завершён; открытых
доказанных source blockers в рассмотренном scope нет. Это проверка доступного
контура, не разрешение на production rollout.

## Связь с воротами плана

| Gate | Проверяемое доказательство |
| --- | --- |
| G1 | Owner modules/ports, пустые HTTP→store и cross-module write baselines, architecture tests, independent SQL/DI audit. |
| G2 | Domain invariants/private children, per-shop primary, city FK, menu CHECK/upsert; whole-packet validation и PG concurrency. |
| G3 | Wolverine outer transactions, 45 direct write cases, owner import/OCR scopes, after-flush rollback и commit-ack loss. |
| G4 | Three OCR checkpoints, stale/late replies, snapshot ordering/hash/resume, idempotent cache/Media/DLQ replay. |
| G5 | Полные profile/admin/owner projections, same-offer filters/facets, 20 001 shops/map truncation и measured query counts. |
| G6 | 29 mandatory PG classes: positive-total/pass/no-skips TRX; настоящие broker/cache/storage/process faults; backend/shared regression. |
| G7 | Populated old-schema upgrade, idempotent scripts/preflight, legacy PG/MinIO transfer/cleanup, явный no-overlap window для menu/photo writers. |
| G8 | Bounded production metrics/readiness/OTLP sources, selective replay, OCR recovery, claims runbook, published synthetic experiments. |

Green CI кода cb386808 и закрытые independent findings подтверждают доступную часть этих
ворот. Production data/load/topology/collector acceptance ниже остаётся обязательной
проверкой конкретного deployment; synthetic fixtures её не заменяют.

## Порядок переключения

1. Зафиксировать SHA всех пяти сервисов, версии DB/broker и queue/exchange/binding
   inventory, ready/unacked/DLQ и старые message headers. Проверить старые menu events
   без JobId/revision и снимки без ExpectedInputRevision: они требуют ручной сверки,
   новая версия не подставляет текущую revision и не применяет их автоматически.
2. Восстановить backup в isolated environment, выполнить city/menu/photo preflight,
   измерить locks/WAL/index/backfill duration на копии фактического объёма. Не удалять
   ambiguous/orphan rows автоматически. Записать postflight counts/IDs/metadata.
3. Применить Media ReserveShopPhotoAttachments migration и запустить новый Media
   claim/finalization consumer с Gallery=1/Menu=2/Roaster=3. Проверить readiness и
   стабильные claimed IDs на canary до включения новых Shops writers.
4. Перекрыть все Shops write ingresses, включая internal import, direct service calls
   и старые workers; завершить in-flight transactions и контролируемо drain messages.
   Running OCR с неизвестным provider outcome разобрать по recovery runbook, не
   повторять paid call автоматически. ResultStored восстановить тем же job.
5. Остановить старые Shops/menu/photo writers. Rolling overlap для menu и Community
   photos запрещён. Применить [Shops migration runbook](shops-migration-rollout.md).
   Для legacy archives выполнить photo expansion перед transfer, cleanup только после
   доказанного завершения переноса по [Community transition](shops-community-photo-transition.md).
6. Запустить одну реплику Shops: текущий Wolverine Solo не проверен для нескольких.
   Обновить связанные Moderation readers/producers и перейти на observed
   ExpectedInputRevision; регенерированные adapters входят в release build.
7. Проверить Gateway admin/owner/user authorization и established routes, nullable
   isOpen для пустого UTC schedule, active app release/download URL, profile conditional
   write, menu manual write/OCR stored apply, public privacy и Media photo access.
8. Открыть ingress постепенно. Проверить PG readiness, sample freshness, durable
   backlog/DLQ, cache invalidation, claims и provider timeout/job age. Подключить
   фактический OTLP collector, dashboards/alerts и согласованные latency/load budgets.

## Остановка и восстановление

Остановить увеличение трафика при invalid preflight, failed readiness, росте DLQ/
invalidation/claim errors, stale metric samples или нарушении согласованного бюджета.
Сохранить correlation/job/message IDs и evidence; не публиковать сырой OCR/user payload.
Исправить dependency, затем replay только выбранных IDs по [operations](shops-operations-runbook.md).
Abandoned claims освобождаются только по [operator procedure](runbooks/shops-photo-claims.md).

До первой записи новой модели rollback application возможен лишь после сверки schema
compatibility. После новых CheckInPhotos, per-shop primary или menu revisions старый
writer уже не представляет всё состояние. Это точка запрета простого rollback image.
Предпочтителен forward fix при остановленной записи; restore backup требует явного
учёта новых записей. Destructive Down и автокоррекция данных не являются recovery.

## Допустимый остаток и внешние проверки

- Legacy profile PUT без If-Match совместим, но не предотвращает последовательную
  stale запись; concurrent EF lost update предотвращается. Обязательный заголовок —
  отдельная версия API, не скрытое изменение этого PR.
- Wolverine 6.35 не хранит durable enqueue age без изменения retention policy.
  observed_age измеряет только непрерывные успешные samples с nonzero backlog;
  resets/fetch estimates и sample freshness описаны в operations.
- Exactly-once billing OCR, cross-service atomic Media/Shops commit и автоматический
  claim reaper не заявлены. Есть stable IDs, durable finalization и operator recovery.
- Synthetic p95/p99, короткий pool=4 business burst и queue drain опубликованы как
  experiment. Длительный soak, production capacity/targets, actual backup rehearsal,
  deployed legacy envelopes/topology и collector dashboards здесь не проверялись.

Эти действия требуют доступа и данных deployment. Merge/deploy, изменение production
данных, replicas или SLA не выполняются автоматически при подготовке пакета.
