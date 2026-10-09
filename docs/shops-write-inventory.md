# P01. Исходное состояние и реестр путей записи ShopsService

Срез: 8 октября 2026 года, исходный `HEAD` — `f0cacf555699f051dbda26f79fdad92043db02ba`. Область инвентаризации — исходные файлы `CoffeePeek.ShopsService`, `CoffeePeek.Shops.Application`, `CoffeePeek.Shops.Infrastructure` и `CoffeePeek.Shops.Persistance`, без сгенерированного кода и миграций. Это карта входов и точек сохранения для следующих задач плана, а не утверждение о корректности каждого сценария.

## Принятая база и сохранность работы

До начала P01 рабочее дерево уже содержало исправление частичного сохранения профиля при ответе 400: старые `AdminCoffeeShopHandlers.cs` и `OwnerCoffeeShopHandlers.cs` удалены, их типы разнесены по отдельным файлам, изменён `ShopProfileApplier.cs`, добавлен `ShopProfileAtomicityTests.cs`. Также уже существовали незакоммиченные `docs/shops-architecture-audit.md` и `docs/shops-modularization-plan.md`. Это результат **предыдущей работы**; P01 его не меняет и не объявляет дефектом из-за текущего `git diff`. Остальные незакоммиченные изменения, которые появятся при параллельном выполнении плана, следует атрибутировать их авторам отдельно.

Историческая проверка после указанного исправления: Domain — 206, Application — 238, Infrastructure — 305, Host — 78 успешных тестов; всего **827 passed, 24 skipped, 0 failed**. Числа взяты из [архитектурного аудита](shops-architecture-audit.md), не являются результатом нового прогона P01. Тогда 24 проверки требовали настроенного live PostgreSQL; существовали предупреждения сборки nullable, XML documentation и xUnit. Локальный SDK .NET 10 установлен. Во время P01 тесты повторно не запускались: параллельные агенты меняют общее рабочее дерево, поэтому их результат нельзя было бы однозначно привязать к этому исходному срезу. Базовый повторяемый запуск после фиксации версии: `dotnet test CoffeePeek.Backend.ci.slnf -c Debug`; отдельно запускать четыре проекта Shops и обязательный набор live PG.

Принятые исходные контракты нужно сохранить до отдельного решения о совместимости:

- `PUT /api/admin/shops/{id}` и `PUT /api/owner/coffee-shops/{id}`: невалидный поздний раздел профиля отвечает 400 и не меняет уже проверенные поля. Тест `ShopProfileAtomicityTests.InvalidPatch_RemainsUnchangedEvenWhenPipelineSaves` проверяет состояние EF InMemory после явного сохранения, но **не** запускает настоящий Wolverine + PostgreSQL pipeline.
- `null` в patch-секциях профиля означает «не менять», пустой массив расписания/справочников — «очистить»; пример есть в `ShopProfileAtomicityTests.ValidPatch_PersistsAndPreservesNullVersusEmptyCollections`.
- `docs/api/examples` вместе с `CoffeePeek.ShopsService.Tests/ApiExamplesContractTests.cs` фиксируют текущую сериализацию публичного каталога напитков и check-in/feed ответов. Эти примеры не доказывают атомарность записи.
- Публичный адрес сущности имеет ревизию; `AdminPublicAddressesController` требует `expectedRevision=0` при инициализации и положительную ревизию при переименовании. Сценарии `OwnerSlugLiveTests` проходят только при включённом live PG.

Из предыдущего аудита уже исправлен F01 (частичное сохранение профиля). Открыты F02 — неполные административные фото/справочники, F03 — удалённое посещение в `VisitedOnly`, F04 — удаление используемого города, F05 — устаревший OCR восстанавливает фото, F06–F09 — неодинаковые и слабые правила меню/агрегатов, F10 — неполный источник карты не помечается усечённым, F11 — гонка инвалидирования кеша. Их приоритеты и доказательства остаются в [аудите](shops-architecture-audit.md); P01 не подтверждает исправление ни одного из них.

## HTTP: все обнаруженные входы изменения данных

Маршруты ниже — маршрут контроллера плюс атрибут метода. Большинство вызывает `IMessageBus.InvokeAsync`; исключения отмечены. `POST` сам по себе не означает запись: `api/v1/discovery/search`, `api/v1/coffee-shops/search`, `api/v1/coffees/search`, `api/v1/coffees/facets` и `api/admin/coffee-zones/candidates` являются запросами чтения.

| Область | HTTP входы записи | Обработчик / фактический владелец изменения |
| --- | --- | --- |
| Профиль кофейни | `PUT /api/admin/shops/{id}`, `PATCH .../visibility`, `PATCH .../owner`, `PATCH .../focus`; `PUT /api/owner/coffee-shops/{id}` | `Features/Admin/Shops/*Handler.cs`, `Features/Owner/UpdateOwnerCoffeeShopHandler.cs`, `ShopProfileApplier.cs`; EF через `CoffeeShopRepository` |
| Галерея и теги кофейни | `PUT /api/admin/shops/{id}/photos/order`, `POST/DELETE .../photos`, `PUT /api/admin/shops/{shopId}/tags`; аналогичные `PUT .../photos/order`, `POST/DELETE .../photos` для `/api/owner/coffee-shops/{id}` | `Reorder*PhotosHandler`, `PublishedShopPhotosHandlers`, `SetShopTagsHandler`; фото дополнительно проверяются через Media |
| Меню кофейни | `POST/DELETE /api/admin/shops/{id}/menu/photos`, `POST .../menu/parse`, `PUT .../menu` | `Features/Admin/Menu/AdminShopMenuHandlers.cs`; `ApplyShopMenuService`; `MenuRepositories.cs`; parse отправляет асинхронное событие |
| Посещения и реакции | `POST /api/v1/check-ins`, `PUT /api/v1/check-ins/{id}`, `PUT .../visibility`, `DELETE /api/v1/check-ins/{id}`, `PUT/DELETE .../helpful`; `POST /api/v1/check-ins/{id}/reports`; `PUT /api/admin/check-in-reports/{reportId}/resolution` | `Features/CheckIn/*Handler.cs`, `Features/Review/Reports/*Handler.cs`; EF + SQL голосов |
| Избранное Discovery | `PUT/DELETE /api/v1/favorites/{kind}/{slug}` | `SetDiscoveryFavoriteCommand` → `IDiscoveryFavoritesStore.SetAsync`; ранний flush store обрабатывает конкурентный idempotent PUT, итоговый commit выполняет Wolverine перед 204 |
| Справочники Shops | `POST/PATCH/DELETE /api/admin/{cities,beans,brew-methods,equipments,roasters,shop-tags}`; `POST /api/admin/coffee-zones`, `PUT .../{id}`, `PATCH .../{id}/status`, `DELETE .../{id}`, `PUT/DELETE .../{zoneId}/membership/{shopId}` | `Features/Admin/Catalogs/*Handlers.cs`, `Features/Admin/ShopTags/*Handlers.cs`, `Features/CoffeeZones/*`, соответствующие репозитории |
| Каталог кофе | `POST/PATCH/DELETE /api/admin/coffees`; `POST/PATCH/DELETE /api/admin/roaster-tags`; `PUT /api/admin/roasters/{id}/tags`; `POST/PATCH/DELETE /api/admin/coffee-filter-values`; `PATCH /api/admin/coffees/{id}/classification`, `PATCH .../{id}/variants/{variantId}/classification`; `POST /api/admin/drinks`, `PUT /api/admin/drinks/{slug}` | `CoffeeCatalogHandler` делегирует в `ICoffeeCatalogStore`; `DiscoveryClassificationStore` и `DrinkCatalogHandlers` пишут свои модели. У store есть собственные `SaveChangesAsync` |
| Public addresses | `POST /api/admin/{entity}/public-addresses/{id}/initialize`, `PATCH .../{id}`, `POST .../backfill` при `dryRun=false`; `entity` ограничен shops, roasters, cities, coffee-zones, beans, equipments, brew-methods, shop-tags, coffees | `ChangePublicAddressCommand` / `BackfillPublicAddressesCommand` через Wolverine; `PublicAddressService` использует savepoint/advisory lock внутри общей EF/outbox-транзакции |
| Дистрибуция приложения | `PUT /api/admin/v1/app-downloads/android/google-play`, `/android/apk`, `/ios/app-store`, `/{platform}/version`; `POST .../android/releases`, `POST .../android/releases/{id}/publish` | `Features/AppDownloads/MutateAppDownloadsHandlers.cs` + `AppDistributionRepository` |
| Внутренний импорт кофе | `POST /internal/coffee-import/snapshots`, `POST .../failures` | snapshots через `ImportCoffeeSnapshotCommand` → `CoffeeCatalogStore.ImportAsync`; failures через `RecordCoffeeImportFailureCommand` → `ICoffeeImportWriter.RecordFailureAsync` внутри Wolverine EF/outbox-транзакции. Обе операции требуют выделенный заголовок с токеном; import lifecycle snapshots ещё требует унификации в P21/P28 |
| Чтение с записью | `GET /download/{android,apk,ios}` | `GetAppDownloadRedirectHandler` добавляет `AppDownloadRedirectEvent` и явно вызывает `SaveChangesAsync`. `GetOrCreateSettingsAsync` также добавляет tracked singleton, если его нет; другие GET app-downloads вызывают этот метод без явного сохранения — нужно проверить эффект Wolverine pipeline отдельно |

Источники: `CoffeePeek.ShopsService/Controllers`, `CoffeePeek.Shops.Application/Features`, `CoffeePeek.Shops.Persistance/{Repositories,CoffeeCatalog,Discovery,PublicAddresses}`. `CoffeeShopsController`, `MenuController`, `CheckInPhotosController` и публичные address controllers здесь имеют только читающие методы. Наличие `HttpPost` у поисковых endpoint не должно попадать в отчёт о мутациях.

## Входящие сообщения, фоновая обработка и внешние эффекты

`CoffeePeek.ShopsService/InfrastructureExtensions.cs` передаёт Application и Infrastructure assembly на сканирование Wolverine. `CoffeePeek.Shared.Persistence/Extensions/WolverineModule.cs` включает PostgreSQL persistence, durable inbox/outbox и EF transactions. Гарантии этой инфраструктуры нужно проверять для каждого конкретного обработчика: явный `SaveChangesAsync`, возвращённая ошибка и прямой вызов store ведут себя по-разному.

| Consumer / сообщение | Запись и рисковая граница |
| --- | --- |
| `ModerationShopApproveHandler` / `ModerationShopApprovedEvent` | `CreateShopFromModerationService` создаёт опубликованную кофейню, явно сохраняет EF; возвращает ответ Moderation |
| `ModerationRoasterApproveHandler` / `ModerationRoasterApprovedEvent` | `CreateRoasterFromModerationService` создаёт обжарщика, явно сохраняет EF |
| `ShopChangeRequestApprovedHandler` / `ShopChangeRequestApprovedV1` | Применяет изменения профиля, галереи и меню, создаёт `AppliedShopChangeRequest`, вызывает `dbContext.SaveChangesAsync`, очищает кеш; собственная обработка постоянных отказов и tracking |
| `ImportCandidatePublishedEventHandler` / `ImportCandidatePublishedEvent` | Пакетно создаёт кофейни через `CreateShopFromImportService`, возвращает список результатов; атомарность всего пакета нужно подтвердить |
| `ImportShopEnrichmentEventHandler` / `ImportShopEnrichmentEvent` | Обогащает опубликованные кофейни через `EnrichShopFromImportService`, явно сохраняет EF |
| `ParseMenuRequestedEventHandler` / `ParseMenuRequestedEvent` | Загружает фото, вызывает OCR, применяет результат к меню, сохраняет EF; возвращает `MenuParsedEvent`. Повтор сохранения не должен повторно оплачивать OCR. Устаревший снимок фото сейчас опасен (F05) |
| `ApplyShopMenuSnapshotEventHandler` / `ApplyShopMenuSnapshotEvent` | Применяет снимок меню и явно сохраняет EF; находится в `ParseMenuRequestedEventHandler.cs` |
| `CheckInModerationDecidedHandler` / `CheckInModerationDecidedEvent` | Применяет решение модерации к check-in и явно сохраняет EF |
| `UserNameChangedEventHandler` / `UserNameChangedEvent` | Прямой `ExecuteUpdateAsync` обновляет username во всех соответствующих check-in; EF ChangeTracker обходится |
| `CheckInCacheInvalidationRequestedHandler` / `CheckInCacheInvalidationRequested` | Меняет Redis, не таблицы Shops; durable invalidation и retry требуют отдельной проверки после commit |

Отдельного `BackgroundService`/`IHostedService` в исследованных исходниках Shops не обнаружено. Фоновая работа представлена Wolverine consumers и их очередями. Python-сборщик `dev/coffee-import/publish_coffees.py` создаёт внешний HTTP write в `/internal/coffee-import/snapshots` и `/failures`; это тоже часть цепочки импорта. `dev/hello-world.sh` проверяет только публичное чтение кофейни после операций Account, а не write path Shops.

## Прямой SQL и обходы общего правила сохранения

| Файл | Операция | Проверка при унификации записи |
| --- | --- | --- |
| `Repositories/MenuRepositories.cs` | `INSERT ... ON CONFLICT DO UPDATE` через `ExecuteSqlInterpolatedAsync` для ручных позиций существующего меню | EF tracked-состояние после SQL, одинаковые инварианты create/update, атомарность с остальным меню |
| `Repositories/QueryCheckInRepository.cs` | `SELECT ... FOR UPDATE`, SQL CTE insert/delete helpful vote с обновлением `HelpfulCount` и `RowVersion`; `pg_advisory_xact_lock` для лимитов создания | реальная транзакция PostgreSQL, дубли, гонка edit/delete/moderation/vote; `SELECT` через ExecuteSqlInterpolated здесь служит блокировкой, а не бизнес-записью |
| `Consumers/UserNameChangedEventHandler.cs` | EF `ExecuteUpdateAsync` для массового обновления username | отсутствие доменного метода и ChangeTracker, commit и повторная доставка |
| `PublicAddresses/PublicAddressTransaction.cs` | `pg_advisory_xact_lock` через `ExecuteSqlRawAsync` | конкуренция и граница собственной транзакции public address service |
| `CoffeeCatalog/CoffeeCatalogStore*.cs`, `CoffeeCatalog/DiscoveryClassificationStore.cs`, `Discovery/DiscoveryFavoritesStore.cs` | EF-записи с **внутренним** `SaveChangesAsync` | единый владелец commit, rollback после возвращённой ошибки, идемпотентность и direct controller path |

По поиску production C# вне миграций и generated других `ExecuteSql*`, `ExecuteUpdateAsync`, `ExecuteDeleteAsync` не найдено; повторить поиск в P28 после добавления новых модулей. Seed, миграции и отдельный legacy migration tool меняют БД вне runtime путей сервиса и требуют собственной эксплуатационной процедуры, но не должны смешиваться с пользовательской транзакцией.

## Существующее покрытие, CI и непроверенные гарантии

Четыре проекта `CoffeePeek.Shops.Domain.Tests`, `.Application.Tests`, `.Infrastructure.Tests`, `CoffeePeek.ShopsService.Tests` включены в `CoffeePeek.Backend.ci.slnf`. В инфраструктурных тестах уже есть тесты consumer OCR/moderation, репозиториев, public address, меню, Discovery, check-in, cache invalidation, а в host — HTTP contract tests. Большая часть persistence-тестов использует EF InMemory или mocks; это полезно для правил, но не проверяет SQL, ограничения FK/unique, реальный commit и порядок доставки.

Есть opt-in набор в `CoffeePeek.Shops.Infrastructure.Tests/LivePostgres`: `DiscoveryClassificationLiveTests`, `DiscoveryQueryLiveTests`, `CheckInFeedLiveTests`, `OwnerSlugLiveTests`, `LegacyReviewBackfillLiveTests`, harness/guard tests. Без `COFFEEPEEK_LIVE_PG` реальные сценарии пропускаются. В `.github/workflows/ci-cd.yml` отдельный job с PostgreSQL 17 принудительно запускает **только** `DiscoveryClassificationLiveTests|DiscoveryQueryLiveTests`; общий `dotnet test` идёт без этого env, поэтому прочие live tests не становятся обязательной защитой. CI также проверяет Python collector, legacy transfer с MinIO, build и тесты. NuGet vulnerability audit сейчас имеет `continue-on-error: true`; его отчёт не является блокирующим gate.

Основные пробелы на момент P01:

1. Нет обязательного интеграционного сценария `HTTP → Wolverine generated adapter → PostgreSQL`, который проверяет 400 после поздней ошибки, отсутствие частичной записи и отсутствие исходящего события. Текущий `ShopProfileAtomicityTests` моделирует сохранение в EF InMemory.
2. Нет сквозной fault matrix для раннего `SaveChanges`, direct SQL, возвращённого `Response.Error`, пойманного исключения и consumer redelivery при том же/новом envelope.
3. Не проверены на PostgreSQL одновременно конкурентные create/edit/delete/vote/report, ограничения города, меню SQL upsert и точность полного набора связанных сущностей.
4. OCR тестируется в основном через mocks; для актуальности задания нужны тесты изменения фото/ручного меню между request и result, обратного порядка ответов, restart и commit failure без повторного внешнего OCR вызова.
5. Нет обязательного окружения RabbitMQ + Redis + Media для реальной проверки outbox/inbox, invalidation после commit и восстановления при падении зависимостей. `dev/hello-world.sh` не покрывает эти write flows.
6. Для входов, которые пишут непосредственно из controller/store, нужен отдельный тест повторного запроса и проверки согласованности с Wolverine-command путём. Для `GET /download/*` нужен тест ошибки записи аналитики и singleton initialization.

Рекомендуемый следующий артефакт P02/P03 — таблица по каждому входу: владелец модуля, aggregate/table, допустимый ответ при `null`/empty/invalid, точка commit, исходящее событие и business idempotency key. Для P04 уже известен минимальный набор исходных примеров: admin/owner invalid late patch; menu create/update с отсутствующей ценой; удалённый check-in и `VisitedOnly`; старый OCR result после удаления фото; конкурирующие helpful votes; import snapshot с повторным `SnapshotId`; public-address rename с неверной ревизией.
