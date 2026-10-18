# P5: инвентаризация старых фотографий

Read-only инвентаризация из PR 15 [общего плана](photo-delivery-improvement-plan.md) выполнена в production: проверены 45 eligible originals, 45 399 435 байт, без missing/corrupt и metadata mismatches. Затем выполнен пилот пяти фото. Владелец разрешил обработать оставшийся архив и включить варианты всем. Свежая проверка выполняется через `rollout-inventory` с отдельным checkpoint; [production runbook](photo-delivery-production.md).

## Границы данных

Community читает собственные `CanonicalCheckIns` / `CheckInPhotos`. `ShopPhotos`, legacy bridge и галереи не становятся источником привязок. Внутренний `POST /internal/check-in-photo-backfill/candidates` возвращает до 100 привязок с keyset cursor `(SortAtUtc, CheckInId, PhotoId)` по убыванию. Cutoff `BeforeUtc` фиксируется для прохода; `RecentSinceUtc` отделяет последние публикации. Фазы: `RecentPublicShop` → `OtherShop` → `Home` → `PublicShopArchive`. Public означает действующий approved revision и видимую кофейню, как в обычном Community public query.

Это наблюдение живых данных, не snapshot и не delta stream. Поздняя транзакция или смена видимости могут переместить фото между фазами. SQLite checkpoint исключает повторный подсчёт PhotoId, отдельно считает повторные наблюдения и изменённые source versions. Для сверки после завершения нужен новый полный проход с новым checkpoint, без старого cursor. Идентификаторы хранятся на диске; память не растёт с размером архива.

Media проверяет точную привязку PhotoId/CheckInId/OwnerId/StorageKey/SourceVersion, `Confirmed`, приватный CheckIn bucket, отсутствие gallery attachment и tombstone. Допускается только канонический неизменяемый ключ `check-ins/{PhotoId:N}{extension}`: это результат verified claim/copy, отличный от случайного presigned upload key. Публичный Shop source получает `needs_transfer`, другой приватный ключ — `provenance_unverified`. Инвентаризация не копирует такие объекты, не исправляет metadata и не меняет bucket policy.

## Проверка содержимого

`POST /internal/check-in-photo-backfill/inspect?verifyContent=true` принимает массив до 100 кандидатов. Оба маршрута требуют доверенный service header, имеют `no-store`, не опубликованы Gateway и не предназначены для браузера. Для доступа к сервисам нужен закрытый сетевой путь. Service secret передаётся из окружения; storage credentials оператору CLI не нужны.

Reader использует отдельную read-only identity `CheckInPhotoReadOptions` и действующую readiness-проверку приватности. На eligible source выполняется один GET без HEAD, переходов по redirect и операций записи. Metadata-only режим закрывает ответ без чтения тела и сообщает `unverified`, а не доказанную целостность. Проверка содержимого ограничена 32 MiB, 40 MP, 30 секундами на объект и одним общим decode slot на процесс Media. Содержимое полностью декодируется для первого кадра текущего preview recipe; digest вычисляется из прочитанных байтов. Это проверка декодируемости, не визуальное утверждение качества renderer.

Статусы `missing`, `corrupt`, `unsupported`, `oversized`, `length_mismatch`, `source_changed` различаются. Только storage `404 NoSuchKey` означает отсутствие объекта. Ошибки identity, bucket, сети и readiness дают `unavailable`; CLI останавливает страницу, сохраняя предыдущий checkpoint. Ответы ошибок, private keys и secret не выводятся в отчёт/консоль.

## Запуск оператором

Требуется Python 3.12 (stdlib). Сначала установить `PHOTO_BACKFILL_SERVICE_SECRET` через локальный менеджер секретов. Не помещать его в аргументы, историю команд и файлы отчёта. Пример для shell; адреса заменить на доступные приватные origins:

```sh
python dev/photo-variants/photo_backfill.py inventory \
  --shops-url http://localhost:5243 --media-url http://localhost:5025 \
  --checkpoint /private/photo-inventory.sqlite --report /private/photo-inventory.json

python dev/photo-variants/photo_backfill.py inventory --resume \
  --shops-url http://localhost:5243 --media-url http://localhost:5025 \
  --checkpoint /private/photo-inventory.sqlite --report /private/photo-inventory.json
```

По умолчанию команда читает одну страницу из пяти фото и завершается. `--max-pages` (до 1000), `--page-size` (до 100), `--interval-seconds` (минимум 1) задают ограниченный проход. Media inspections разбиваются на группы по пять. `--before` принимает UTC ISO timestamp, `--recent-days` по умолчанию 30. При `--resume` нельзя менять origins, режим проверки, page size и cutoff; сохранённое окно дат остаётся прежним. `--metadata-only` подходит для первичной проверки привязок, но не для расчёта завершённой проверки содержимого.

Checkpoint SQLite атомарно фиксирует cursor, dedup IDs и агрегаты одной страницы после успешных HTTP-проверок. Ошибка не продвигает страницу. Параллельный устаревший процесс не перезаписывает новый checkpoint/report. Между HTTP чтением и SQL commit нет распределённого snapshot: повторный полный проход остаётся обязательным. SQLite и JSON хранить приватно — они содержат служебные ID, origins и статистику, хотя не содержат object keys. Не использовать один путь для обоих файлов.

## Как читать отчёт

`complete=false` означает частичную выборку, не размер всего архива. `totals.scanned` считает уникальные фото; `observedRows`, `duplicateObservations`, `changedBindings` объясняют повторения между фазами. Отчёт разделяет eligibility/source/job status, declared/verified bytes и metadata mismatches. Объявленная длина не подменяет фактически прочитанные байты.

Для прогноза storage используются только content-verified источники с совпавшим SHA-256 и полным Ready manifest текущего recipe (шесть вариантов). Размеры вариантов взяты из metadata, измеренных worker при записи; эта команда не перечитывает все производные объекты. Средний фактический суммарный размер умножается на оставшиеся verified eligible фото без terminal Failed/Cancelled. Это дополнительное место для вариантов в исследованной популяции, без retention старых recipes, запаса диска и служебных накладных расходов. Без подходящих Ready samples прогноз `null`; не переносить среднее с нерепрезентативной выборки на весь архив.

Прогноз времени остаётся `null`, пока оператор не передаст измеренную на пилоте скорость `--pilot-photos-per-second`. Для metadata-only режима time forecast отключён. В отчёте хранится основание оценки; синтетические тестовые числа не являются измерением архива.

## Проверки перед первым архивным проходом

Локально покрыты keyset/границы/фазы, HTTP auth, отсутствие записей, ограничения содержимого, ошибки storage, checkpoint/resume/dedup и запуск CLI отдельным процессом с синтетическим HTTP. CI обязательно выполняет `CheckInPhotoBackfillCandidatesLiveTests` на PostgreSQL 17 и Python tests. Перед увеличением batch на реальном архиве измерить SQL `EXPLAIN (ANALYZE, BUFFERS)`/latency для фаз и cursor с одинаковыми timestamps; bounded response не гарантирует ограниченное время SQL scan. При необходимости индекс добавляется отдельной проверенной миграцией. Проверить реальные missing/corrupt выборки, storage headroom и качество renderer; затем переходить к управляемому backfill.
