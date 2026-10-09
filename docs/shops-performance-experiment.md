# P32: воспроизводимое измерение чтения

`ShopsReadPerformanceLiveTests` создаёт отдельную PostgreSQL 17 БД через существующие
live guards и загружает production domain objects: 20 001 активная кофейня с одним
фото, 50 owner assignments, 1000 одобренных посещений (по два на первые 500 кофеен),
две перекрывающиеся зоны, 1000 опубликованных продуктов, 2000 предложений и 40
дополнительных taste options. Это синтетический плотный набор; он не изображает
реальное географическое распределение или production статистику.

Измеряются профиль с каталогами, admin страницы 1/50, owner список 50, Discovery
страницы кофеен 1/50, страница кофе 50, facets и три zoom-режима карты. Для каждого
production query сначала выполняется warm-up, затем 32 вызова с concurrency 4 и
новым DbContext на каждый вызов. Публикуются ближайшие эмпирические p50/p95/p99,
SQL min/max, общие аллокации процесса за batch и working set после batch.
Аллокации включают runner/GC шум; working set не является peak memory. 32 samples
не дают статистически устойчивой оценки хвоста p99. Числа — эксперимент, не SLO.

Для всех SQL warm-up сохраняются точный SELECT, параметры и PostgreSQL
`EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` после `ANALYZE`. Сырые значения относятся
только к синтетическому набору. CI artifact `shops-read-performance` содержит планы,
`read-experiment.json`, runtime/число CPU и SHA тестируемого checkout; краткие
результаты также выводятся с префиксом `READ_PERFORMANCE`.

Обязательные assertions проверяют реальные фото в новых контекстах, полный owner
список, product/option counts, стабильное число SQL на повторных вызовах и одинаковое
число SQL для страниц 1 и 50. Карта обязана вернуть `IsTruncated=true`, 20 000 точек
в кластере после отсечения sentinel, полные counts двух зон и primary zone у markers.
Задержки не сравниваются с выдуманным бюджетом. CI запускает класс через тот же
positive-total/no-skips TRX guard; результат помечать принятым только после исполнения.

Для запуска нужен обычный live PG gate. `COFFEEPEEK_PERF_RESULTS` задаёт каталог
artifact; иначе файлы сохраняются в новом `cp-performance-*` каталоге временных
файлов. Для самостоятельного запуска собранного executable test assembly:

```text
dotnet CoffeePeek.Shops.Infrastructure.Tests/bin/Release/net10.0/CoffeePeek.Shops.Infrastructure.Tests.dll -class CoffeePeek.Shops.Infrastructure.Tests.LivePostgres.ShopsReadPerformanceLiveTests -failSkips -noAutoReporters
```

До/после оптимизации сравнивать одинаковые data/concurrency/runner/runtime и semantic
suite. Facets сейчас делает count для каждой option — кандидат для проверки после
baseline, а не заранее доказанная проблема latency. Импорт, OCR apply, длительности
write transactions, очередь/pool под длительной нагрузкой и согласованные production
бюджеты остаются отдельными частями P32.
