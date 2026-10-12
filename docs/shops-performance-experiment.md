# P32: воспроизводимые измерения чтения и записи

Это эксперименты на synthetic fixtures, не production SLO. Данные о целевой нагрузке,
latency budgets и реальном распределении отсутствуют. CI artifacts сохраняют runtime,
CPU, checkout SHA, JSON measurements и SQL EXPLAIN ANALYZE/BUFFERS plans.

## Чтение

ShopsReadPerformanceLiveTests загружает PostgreSQL 17: 20 001 активная кофейня с фото,
50 owner assignments, 1000 approved visits, две перекрывающиеся зоны, 1000 опубликованных
продуктов, 2000 offers и 40 taste options. Каждый query прогревается, затем выполняется
32 раза, concurrency 4, новый DbContext на вызов. SQL counts и semantics проверяются
assertions: полные photos/owners, стабильное число SQL для страниц 1/50, same-offer
filters, truncation sentinel/20 000 clustered points, full zone counts и primary markers.

Baseline выполнен в [CI 0295699a](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37921729495)
до изменения facets. Класс passed; весь run failed из-за OCR Photos fixture.

| Query | SQL | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| Profile | 5 | 34.53 | 81.29 | 119.20 |
| Admin page 1 | 9 | 215.27 | 322.58 | 322.81 |
| Admin page 50 | 9 | 269.21 | 338.98 | 345.57 |
| Owner page 50 | 8 | 57.05 | 205.74 | 232.09 |
| Discovery shops page 1 | 8 | 107.70 | 204.32 | 242.67 |
| Discovery shops page 50 | 8 | 113.97 | 136.23 | 235.47 |
| Discovery coffees page 50 | 8 | 102.43 | 149.02 | 221.89 |
| Facets | 72 | 658.37 | 1137.52 | 1220.68 |
| Map clusters | 1 | 280.86 | 410.08 | 562.67 |
| Map zones | 4 | 476.33 | 963.32 | 1033.14 |
| Map markers | 5 | 288.90 | 563.98 | 668.19 |

Facets — подтверждённый query-count bottleneck: старый count на каждую option.
516bfc32 заменяет scalar/array option loops одной aggregate query на группу,
Distinct(productId, code) сохраняет counts legacy duplicate arrays. Brew matching
оставлен с прежней same-variant семантикой. InMemory fallback нужен только для
провайдера, не умеющего primitive-array unnest. Semantic PG suite и повторный
experiment в [CI cb386808](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37923822811)
прошли. Facets: 72→13 SQL (-81.9%), p50 658.37→264.94ms, p95
1137.52→413.20ms, p99 1220.68→416.75ms; process batch allocations
92,218,496→22,787,440 bytes (-75.3%). Concurrency/data/query semantics прежние.

| Optimized run query | SQL | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| Profile | 5 | 37.87 | 80.07 | 88.41 |
| Admin page 1 | 9 | 191.84 | 270.16 | 292.29 |
| Admin page 50 | 9 | 246.30 | 526.95 | 695.05 |
| Owner page 50 | 8 | 30.77 | 43.64 | 50.73 |
| Discovery shops page 1 | 8 | 95.56 | 105.49 | 108.13 |
| Discovery shops page 50 | 8 | 78.88 | 117.86 | 125.51 |
| Discovery coffees page 50 | 8 | 123.54 | 136.05 | 137.23 |
| Facets | 13 | 264.94 | 413.20 | 416.75 |
| Map clusters | 1 | 294.86 | 583.04 | 697.24 |
| Map zones | 4 | 520.35 | 1722.15 | 2443.57 |
| Map markers | 5 | 242.09 | 341.60 | 469.79 |

Warm-up artifact содержит 13 facets plans: catalog/total, три прежних brew counts,
семь grouped counts и price range. Grouped counts 0.187–3.176ms server execution,
root temp read/write blocks=0. На этом fixture non-brew counts непусты лишь для
taste; все classification distributions/worst cases этим экспериментом не покрыты.
Plans/raw values опубликованы в shops-read-performance artifact11613607306.

У неизменённой map-zone query p95 в новом run вырос 963.32→1722.15ms, admin50
338.98→526.95ms. Это разные CI runners и маленькая выборка; причинная latency
регрессия facets refactor из этих цифр не установлена. Значения не скрыты: для
production budget нужна повторная стабильная нагрузка. SQL counts/семантика
не регрессировали; новых индексов и дополнительного map refactor нет.

Map allocations за baseline batch: clusters 378 MB, zones 559 MB, markers 273 MB.
Это общие process allocations за 32 calls, не retained memory одного запроса.
Дополнительная map SQL refactor без production distribution/budget не выполнялась.

## Запись

ShopsWritePerformanceLiveTests использует production module ports и Wolverine EF/outbox,
64 независимых profile PUT-equivalent commands (concurrency 4), восемь последовательных
snapshots по 100 products/200 variants и 16 stored-result OCR applies (concurrency 4).
Business EF pool ограничен 4 connections; отдельный Wolverine pool имеет limit 100.
Эти числа нельзя выдавать за единый pool всей production системы.

WriteTransactionProbe измеряет от TransactionStarted до TransactionCommitted.
Проверяются 64 profile revisions, 100 products, восемь Applied runs, 16 Applied jobs/
offers, eventual zero Incoming/Outgoing/Scheduled и zero DLQ. Strict cache и public
address adapters — doubles; реальные Redis/broker faults проверяются отдельно.
Paid OCR provider не участвует: stored-result apply не удерживает provider transaction.

Baseline CI 0295699a (class passed):

| Write | clients/samples | p50 ms | p95/p99 ms | committed tx | tx p95/max ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| Profile | 4/64 | 78.15 | 3383.24/3384.68 | 64 | 801.82/801.85 |
| Import 100 products | 1/8 | 262.89 | 1058.83/1058.83 | 8 | 743.20/743.20 |
| Stored OCR apply | 4/16 | 47.78 | 277.11/277.11 | 16 | 267.39/267.39 |

Первый write включает cold runtime/code generation; отдельного warm-up нет.
Baseline import timing также включал подготовку клиентского payload; исправленный fixture
подготавливает snapshots до measurement и включает production CheckInAuditInterceptor.
Сравнивать этот baseline с новым результатом как чистую server optimization нельзя.
Queue assertions доказывают завершение короткого burst, не queue pressure длительного
soak или saturation всех production pools.

Исправленный write experiment CI cb386808 passed:

| Write | clients/samples | p50 ms | p95/p99 ms | committed tx | tx p95/max ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| Profile | 4/64 | 56.96 | 3495.51/3498.68 | 64 | 819.01/819.72 |
| Import 100 products | 1/8 | 334.17 | 1282.44/1282.44 | 8 | 1033.11/1033.11 |
| Stored OCR apply | 4/16 | 43.66 | 260.96/260.96 | 16 | 253.85/253.85 |

Read/write artifact SHA — PR merge be4c4a61fcc4bf36ec3e6abd8f23ab4b8fa994f0,
source cb386808. Write SQL count/EXPLAIN не сняты; transaction probe не охватывает
отдельные Wolverine storage transactions или время до завершения BEGIN.

## Воспроизведение и пределы

Каждый class обязателен через positive-total/no-skips TRX guard. Используются те же
live PG guards, что в [fault gate](shops-fault-gate.md). COFFEEPEEK_PERF_RESULTS задаёт
artifact directory; иначе создаётся cp-performance-* во временном каталоге.

```text
dotnet CoffeePeek.Shops.Infrastructure.Tests/bin/Release/net10.0/CoffeePeek.Shops.Infrastructure.Tests.dll -class CoffeePeek.Shops.Infrastructure.Tests.LivePostgres.ShopsReadPerformanceLiveTests -failSkips -noAutoReporters
dotnet CoffeePeek.Shops.Infrastructure.Tests/bin/Release/net10.0/CoffeePeek.Shops.Infrastructure.Tests.dll -class CoffeePeek.Shops.Infrastructure.Tests.LivePostgres.ShopsWritePerformanceLiveTests -failSkips -noAutoReporters
```

read-experiment.json и write-experiment.json находятся в shops-read-performance artifact;
warm-up SELECT/params и EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) сняты после ANALYZE.
Empirical p99 на 32 read/8 import/16 OCR samples не устойчив. Allocations включают
runner/GC, working set — snapshot, не peak. Разные CI runners дают шум latency.
Длительный soak, deployment budgets и реальные pool/queue capacity остаются выпускной
проверкой после получения нагрузки; они не заменены искусственным latency assertion.
