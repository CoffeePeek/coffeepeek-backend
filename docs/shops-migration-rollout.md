# Проверка и выпуск схемы Shops (P29)

Состояние на 9 октября 2026 года: ShopsSchemaUpgradeLiveTests выполнил три mandatory PostgreSQL 17 cases в [CI 0295699a](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37921729495): populated previous schema, double idempotent script/old SQL writer compatibility, invalid city/menu preflight. CheckInPhotoMigrationLiveTests выполнил шесть photo cases; [CI fd8604bd](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37909817024) также прошёл legacy PostgreSQL/MinIO transfer/cleanup. Это синтетическая репетиция; восстановленная копия production данных и время/WAL/locks её миграций ещё не измерены. Этот документ не является разрешением на production-развёртывание.

## Preflight до миграций

1. Снять резервную копию Shops DB и зафиксировать точные версии работающих бинарников; выполнить восстановление копии в изолированном окружении для репетиции.
2. Выполнить `dev/check-city-references.sql` для городов. Миграция `ProtectCityReferences` останавливается, если уже есть осиротевшие ссылки.
3. Выполнить запрос поиска невалидных позиций из [правил меню](shops-menu-write-invariants.md). По согласованному решению миграция `EnforceShopMenuItemValues` сохраняет некорректные позиции, переводит их в `Unknown` и очищает цену и объём. Корректные позиции не меняются. `ci-deploy.sh` останавливает все application writers перед миграциями, в том числе при уже удалённых legacy check-in таблицах.
4. Проверить осиротевшие связи оборудования:

```sql
SELECT ce."CoffeeShopId", ce."EquipmentId"
FROM "CoffeeShopEquipments" AS ce
LEFT JOIN "Equipments" AS e ON e."Id" = ce."EquipmentId"
WHERE e."Id" IS NULL;
```

5. Снять размеры `ShopMenuItems`, `CoffeeShopEquipments`, `CoffeeShops`, число связанных городов и объём WAL до репетиции; измерить время добавления индексов и backfill на копии данных. Эти значения пока не измерены.

## Порядок и совместимость

**Меню требует остановки старых писателей, без rolling overlap.** До запуска первого нового
ShopsService остановить и вывести из обработки все экземпляры старого ShopsService и старых
menu workers, дождаться завершения их in-flight ручных записей и сообщений. Старый бинарник
обновляет `ShopMenuItems` без изменения `ShopMenus.InputRevision`; если он пишет параллельно
новому OCR worker, новый job может принять устаревший снимок как текущий и перезаписать ручную
правку. После применения `EnforceShopMenuItemValues` старый бинарник к тому же может получить
ошибку CHECK на прежде разрешённой нулевой цене/объёме. Не возвращать старые menu writers
после миграции; rollback требует остановки записи, сверки данных и отдельного плана, а не
просто перезапуска прежнего образа.

Миграции идут в порядке: `AddMenuOcrJobs` → `ProtectCityReferences` → `AssignShopEquipmentPrimary` → `AddShopProfileRevision` → `EnforceShopMenuItemValues` → `SeparateCommunityCheckInPhotos`. Автотест `ShopsCombinedMigrationTests` фиксирует этот порядок и отсутствие расхождения EF snapshot. CI live PostgreSQL gate применяет миграции к чистой тестовой БД и выполняет профиль, меню, город, оборудование, OCR и outbox сценарии.

`AssignShopEquipmentPrimary` сохраняет старую `Equipments.IsPrimary` на переходное окно и даёт ей default `false`, чтобы старый бинарник продолжал читать/писать каталог, а новый мог создавать оборудование. Триггер заполняет новую категорию у старых вставок в `CoffeeShopEquipments`; составной FK проверяет соответствие категории. Новое per-shop `IsPrimary` начинается с `false`: старый глобальный флаг нельзя однозначно перенести на конкретную кофейню. Перед включением нового UI выбор основных устройств нужно проверить отдельно.

Публикация новых фотографий зависит от другой БД: сперва применить Media migration `20261008211500_ReserveShopPhotoAttachments`, затем запустить MediaService с новым внутренним claim endpoint и consumer, и только после этого ShopsService. Если новый ShopsService обратиться к прежнему MediaService, публикация фото должна получить отказ, а не сохранить непроверенный ключ. Повтор claim/finalization и lost reply/receiver restart выполнены с настоящими Media DB/MinIO/RabbitMQ в fault gate. Совместимость с фактически развёрнутыми прежними бинарниками и топологией всё ещё требует deployment inventory; повтор test composition этого не доказывает.

Menu CHECK устанавливается после согласованного исправления некорректных старых строк в той же транзакции. Новые записи до установки ограничения должны проходить доменную проверку; после установки обход приложения блокируется самой БД. Старые бинарники, которые создают нулевую цену или объём у присутствующего напитка, **несовместимы** с новым ограничением; их нужно вывести из записи до миграции. Для `ProfileRevision` API допускает запросы без `If-Match`, но EF concurrency token предотвращает незаметную потерю конкурентной правки.

Photo expansion и порядок legacy transfer описаны в [Community transition](shops-community-photo-transition.md). Тест upgrade сохраняет сравнимые JSON archive values, старые menu offers/IDs и два shop assignments с неопределимым global primary=false. City preflight не меняет archive/migration history; menu repair меняет только доступность, цену, объём невалидных позиций и производный completeness score. Constraints получают SQLSTATE 23514/23503. Это old SQL writer window, не запуск прежнего binary.

## Ворота и откат

На копии старой БД последовательно применить миграции, выполнить SQL preflight/postflight, запустить live контрактные тесты и проверить переход с остановленным старым menu writer. Совместное окно старого и нового бинарника проверять только для оборудования, где предусмотрены legacy-колонка и триггер. Затем повторить на чистой БД и сравнить EF snapshot. Если время блокировок или объём backfill неприемлемы, вынести отдельные expand/backfill/validate/contract миграции до выпуска.

Откат приложения допускается лишь в пределах документированного mixed-version окна. `Down` у миграций не считается способом восстановления данных после записи новой версии: статус OCR, per-shop выбор основного оборудования и ревизии профиля могут быть потеряны или не представимы в старой модели. При ошибке данных предпочтителен forward fix с проверенной резервной копией.
