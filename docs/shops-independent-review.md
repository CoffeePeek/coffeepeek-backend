---
phase: shops-p28-p34-independent-review
reviewed: 2026-10-09T11:44:40Z
depth: deep
review_base: f0cacf555699f051dbda26f79fdad92043db02ba
reviewed_head: aafed224837d09da2aee736bf19ed7db0d5143e5
continuation_head: 4129904f988f0b6604e89c4990c7adb1adbb5538
p32_followup_head: 516bfc323292d72e0bba268b1c637e5bda1a7f6b
final_source_head: cb3868081d9f86e4c2aaa647580e000cd9a31ee1
runtime_evidence_head: cb3868081d9f86e4c2aaa647580e000cd9a31ee1
runtime_evidence_run: 37923822811
runtime_evidence_conclusion: success
baseline_runtime_evidence_run: 37921729495
working_tree_followup: false
files_reviewed: 168
files_reviewed_list:
  - CoffeePeek.Shops.Persistance/Discovery/DiscoveryQueryStore.Facets.cs
  - CoffeePeek.Shops.Persistance/Discovery/DiscoveryQueryStore.Coffees.cs
  - CoffeePeek.Shops.Persistance/Discovery/DiscoveryQueryStore.cs
  - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/DiscoveryQueryLiveTests.cs
  - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/AppDownloadRedirectDurabilityLiveTests.cs
  - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/ShopsWritePerformanceLiveTests.cs
  - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/WriteTransactionProbe.cs
  - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/ShopsDirectWriteBoundaryLiveTests.cs
  - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/ShopsReadPerformanceLiveTests.cs
  - CoffeePeek.Shops.Infrastructure.Tests/Persistence/CoffeeCatalogTests.cs
  - CoffeePeek.Shared.Persistence/Data/AuditInterceptor.cs
  - CoffeePeek.Shops.Persistance/Configuration/CheckInAuditInterceptor.cs
  - CoffeePeek.Shops.Persistance/Modules/ShopsModule.cs
  - CoffeePeek.Shops.Persistance/Modules/CoffeeCatalogModule.cs
  - CoffeePeek.Shops.Persistance/Repositories/QueryCoffeeDrinkRepository.cs
  - CoffeePeek.Shops.Infrastructure.Tests/Faults/FaultOcrStoredInterceptor.cs
  - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/ShopProfileOutboxLiveTests.cs
  - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/ShopsLiveDatabase.cs
  - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/LivePostgresServer.cs
  - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/FailOnceStrictCacheInvalidator.cs
  - CoffePeek.ServiceDefaults/Extensions.cs
  - Directory.Packages.props
  - CoffeePeek.Shared.Kernel/Exceptions/ConflictException.cs
  - CoffeePeek.Shared.Web/Handlers/GlobalExceptionHandler.cs
  - CoffeePeek.Shops.Application/Features/Admin/Shops/UpdateAdminCoffeeShopHandler.cs
  - CoffeePeek.Shops.Infrastructure.Tests/Faults/FaultProfileFlushInterceptor.cs
  - CoffeePeek.Shops.Infrastructure.Tests/Faults/ShopsFaultControl.cs
  - CoffeePeek.Shops.Infrastructure.Tests/Faults/ShopsFaultProcess.cs
  - CoffeePeek.Shops.Infrastructure.Tests/Faults/ShopsFaultWorkerTests.cs
  - CoffeePeek.Shops.Infrastructure/Observability/ShopsDurabilityMetrics.cs
  - .github/workflows/ci-cd.yml
  - CoffeePeek.Contract/Dtos/Menu/ShopMenuSnapshot.cs
  - CoffeePeek.Contract/Dtos/Menu/UpdateShopMenuRequest.cs
  - CoffeePeek.Contract/Events/Menu/ApplyShopMenuSnapshotEvent.cs
  - CoffeePeek.Contract/Events/Menu/ParseMenuRequestedEvent.cs
  - CoffeePeek.Contract/Events/Moderation/ImportShopEnrichmentEvent.cs
  - CoffeePeek.MediaService/Consumers/ShopPhotosAttachedHandler.cs
  - CoffeePeek.MediaService/Controllers/InternalShopPhotosController.cs
  - CoffeePeek.MediaService/Handlers/ClaimShopPhotosHandler.cs
  - CoffeePeek.MediaService/Repositories/PhotoRepository.cs
  - CoffeePeek.Moderation.Application/Features/Import/Menu/ImportCandidateMenuHandlers.cs
  - CoffeePeek.Moderation.Application/Features/Import/Menu/UpdateImportCandidateMenuCommand.cs
  - CoffeePeek.Moderation.Application/Features/Import/Menu/UpdateImportCandidateMenuHandler.cs
  - CoffeePeek.Moderation.Application/Features/Menu/MenuParseEvents.cs
  - CoffeePeek.Moderation.Application/Features/Shop/Menu/ModerationShopMenuHandlers.cs
  - CoffeePeek.Moderation.Application/Features/Shop/Menu/UpdateModerationShopMenuCommand.cs
  - CoffeePeek.Moderation.Application/Features/Shop/Menu/UpdateModerationShopMenuHandler.cs
  - CoffeePeek.Moderation.Domain/Aggregates/MenuDraftAggregate/MenuDraft.cs
  - CoffeePeek.Moderation.Domain/Aggregates/ModerationShopAggregate/ModerationShop.Actions.cs
  - CoffeePeek.Moderation.Domain/Aggregates/ShopImportCandidateAggregate/ShopImportCandidate.cs
  - CoffeePeek.ModerationService/Controllers/AdminImportController.cs
  - CoffeePeek.ModerationService/Controllers/ModerationShopsController.cs
  - CoffeePeek.Shared.Persistence/Data/UnitOfWork.cs
  - CoffeePeek.Shared.Persistence/Extensions/WolverineModule.cs
  - CoffeePeek.Shops.Application/Abstractions/TrustedUploadedShopPhotos.cs
  - CoffeePeek.Shops.Application/Features/Admin/Catalogs/Cities/DeleteCityHandler.cs
  - CoffeePeek.Shops.Application/Features/Admin/Catalogs/Roasters/CreateRoasterCommand.cs
  - CoffeePeek.Shops.Application/Features/Admin/Catalogs/Roasters/CreateRoasterHandler.cs
  - CoffeePeek.Shops.Application/Features/Admin/Catalogs/Roasters/UpdateRoasterHandler.cs
  - CoffeePeek.Shops.Application/Features/Admin/Menu/AttachAdminShopMenuPhotosHandler.cs
  - CoffeePeek.Shops.Application/Features/Admin/Menu/ParseAdminShopMenuHandler.cs
  - CoffeePeek.Shops.Application/Features/Admin/Menu/RemoveAdminShopMenuPhotosHandler.cs
  - CoffeePeek.Shops.Application/Features/Admin/Menu/UpdateAdminShopMenuHandler.cs
  - CoffeePeek.Shops.Application/Features/Admin/Shops/AdminPublishedShopMapper.cs
  - CoffeePeek.Shops.Application/Features/AppDownloads/PublishAndroidReleaseHandler.cs
  - CoffeePeek.Shops.Application/Features/CheckIn/CreateUnifiedCheckInHandler.cs
  - CoffeePeek.Shops.Application/Features/CheckIn/UpdateUnifiedCheckInHandlers.cs
  - CoffeePeek.Shops.Application/Features/CoffeeCatalog/CoffeeCatalogHandler.cs
  - CoffeePeek.Shops.Application/Features/CoffeeCatalog/CoffeeClassificationHandler.cs
  - CoffeePeek.Shops.Application/Features/CoffeeCatalog/CoffeeImportService.cs
  - CoffeePeek.Shops.Application/Features/CoffeeCatalog/CoffeeProductWriter.cs
  - CoffeePeek.Shops.Application/Features/CoffeeShop/GetCoffeeShop/GetCoffeeShopHandler.cs
  - CoffeePeek.Shops.Application/Features/CoffeeShop/SearchCoffeeShops/SearchCoffeeShopsHandler.cs
  - CoffeePeek.Shops.Application/Features/CoffeeShop/ShopProfile/AddPublishedShopPhotosHandler.cs
  - CoffeePeek.Shops.Application/Features/CoffeeShop/ShopProfile/RemovePublishedShopPhotosHandler.cs
  - CoffeePeek.Shops.Application/Features/CoffeeShop/ShopProfile/SetShopPrimaryEquipmentHandler.cs
  - CoffeePeek.Shops.Application/Features/CoffeeShop/ShopProfile/ShopProfileApplier.cs
  - CoffeePeek.Shops.Application/Features/CoffeeZones/CreateCoffeeZoneHandler.cs
  - CoffeePeek.Shops.Application/Features/CoffeeZones/DeleteCoffeeZoneMembershipOverrideHandler.cs
  - CoffeePeek.Shops.Application/Features/CoffeeZones/SetCoffeeZoneMembershipOverrideHandler.cs
  - CoffeePeek.Shops.Application/Features/CoffeeZones/SetCoffeeZoneStatusHandler.cs
  - CoffeePeek.Shops.Application/Features/CoffeeZones/UpdateCoffeeZoneHandler.cs
  - CoffeePeek.Shops.Application/Features/Review/Reports/ResolveReviewReportHandler.cs
  - CoffeePeek.Shops.Application/Services/ApplyShopMenuService.cs
  - CoffeePeek.Shops.Application/Services/CreateRoasterFromModerationService.cs
  - CoffeePeek.Shops.Application/Services/CreateShopFromImportService.cs
  - CoffeePeek.Shops.Application/Services/CreateShopFromModerationService.cs
  - CoffeePeek.Shops.Application/Services/EnrichShopFromImportService.cs
  - CoffeePeek.Shops.Domain/Aggregates/CheckInAggregate/CheckIn.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeProductAggregate/CoffeeProduct.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeProductAggregate/CoffeeProductSource.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeProductAggregate/CoffeeVariant.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/CoffeeShop.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/Roaster.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/ShopEquipment.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/ValueObjects/ShopSchedule.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/ValueObjects/ShopScheduleEvaluator.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/ValueObjects/ShopScheduleInterval.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeZoneAggregate/CoffeeZoneMembershipOverride.cs
  - CoffeePeek.Shops.Domain/Aggregates/MenuAggregate/MenuOcrJob.cs
  - CoffeePeek.Shops.Domain/Aggregates/MenuAggregate/ShopMenu.cs
  - CoffeePeek.Shops.Domain/Aggregates/MenuAggregate/ShopMenuItem.cs
  - CoffeePeek.Shops.Domain/Aggregates/MenuAggregate/ShopMenuPhoto.cs
  - CoffeePeek.Shops.Domain/Aggregates/ReviewAggregate/ReviewReport.cs
  - CoffeePeek.Shops.Domain/Entities/ShopPhoto.cs
  - CoffeePeek.Shops.Infrastructure.Tests/Faults/FaultOcrApplyFlushInterceptor.cs
  - CoffeePeek.Shops.Infrastructure.Tests/Faults/ShopsFaultHost.cs
  - CoffeePeek.Shops.Infrastructure.Tests/Faults/ShopsOcrFaultWorker.cs
  - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/MenuOcrJobLiveTests.cs
  - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/ShopsFaultRecoveryLiveTests.cs
  - CoffeePeek.Shops.Infrastructure/Consumers/ApplyShopMenuSnapshotEventHandler.cs
  - CoffeePeek.Shops.Infrastructure/Consumers/CheckInModerationDecidedHandler.cs
  - CoffeePeek.Shops.Infrastructure/Consumers/CoffeeShopApprovedEventHandler.cs
  - CoffeePeek.Shops.Infrastructure/Consumers/ImportCandidatePublishedEventHandler.cs
  - CoffeePeek.Shops.Infrastructure/Consumers/ImportShopEnrichmentEventHandler.cs
  - CoffeePeek.Shops.Infrastructure/Consumers/ModerationRoasterApproveHandler.cs
  - CoffeePeek.Shops.Infrastructure/Consumers/ParseMenuOcrJobRequestedEventHandler.cs
  - CoffeePeek.Shops.Infrastructure/Consumers/ParseMenuRequestedEventHandler.cs
  - CoffeePeek.Shops.Infrastructure/Consumers/ShopChangeRequestApprovedHandler.cs
  - CoffeePeek.Shops.Infrastructure/Consumers/ShopProfileCacheInvalidationRequestedHandler.cs
  - CoffeePeek.Shops.Infrastructure/Consumers/UserNameChangedEventHandler.cs
  - CoffeePeek.Shops.Infrastructure/DependencyInjection.cs
  - CoffeePeek.Shops.Infrastructure/Media/MediaUploadedPhotoLookup.cs
  - CoffeePeek.Shops.Persistance/CoffeeCatalog/CoffeeCatalogTagStorage.cs
  - CoffeePeek.Shops.Persistance/CoffeeCatalog/CoffeeImportStorage.cs
  - CoffeePeek.Shops.Persistance/CoffeeCatalog/CoffeeProductStorage.cs
  - CoffeePeek.Shops.Persistance/CoffeeCatalog/DiscoveryClassificationStore.cs
  - CoffeePeek.Shops.Persistance/Configuration/CheckInPhotoConfiguration.cs
  - CoffeePeek.Shops.Persistance/Configuration/CheckInPhotoSchema.cs
  - CoffeePeek.Shops.Persistance/Configuration/CoffeeCatalogConfiguration.cs
  - CoffeePeek.Shops.Persistance/Configuration/CoffeeShopConfiguration.cs
  - CoffeePeek.Shops.Persistance/Configuration/MenuOcrJobConfiguration.cs
  - CoffeePeek.Shops.Persistance/Configuration/RoasterConfiguration.cs
  - CoffeePeek.Shops.Persistance/Configuration/ShopMenuConfiguration.cs
  - CoffeePeek.Shops.Persistance/Configuration/ShopMenuItemConfiguration.cs
  - CoffeePeek.Shops.Persistance/Configuration/ShopMenuPhotoConfiguration.cs
  - CoffeePeek.Shops.Persistance/Configuration/ShopPhotoConfiguration.cs
  - CoffeePeek.Shops.Persistance/Configuration/ShopsDbContext.cs
  - CoffeePeek.Shops.Persistance/DependencyInjection.cs
  - CoffeePeek.Shops.Persistance/Discovery/DiscoveryFavoritesStore.cs
  - CoffeePeek.Shops.Persistance/Menu/ApplyApprovedShopMenuChangeService.cs
  - CoffeePeek.Shops.Persistance/Menu/MenuOcrJobStore.cs
  - CoffeePeek.Shops.Persistance/Migrations/20261008091000_ProtectCityReferences.cs
  - CoffeePeek.Shops.Persistance/Migrations/20261008092000_AssignShopEquipmentPrimary.cs
  - CoffeePeek.Shops.Persistance/Migrations/20261008094000_EnforceShopMenuItemValues.cs
  - CoffeePeek.Shops.Persistance/Migrations/20261009081229_SeparateCommunityCheckInPhotos.cs
  - CoffeePeek.Shops.Persistance/Modules/MenuModule.cs
  - CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressService.cs
  - CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressTransaction.cs
  - CoffeePeek.Shops.Persistance/Queries/CheckInEligibility.cs
  - CoffeePeek.Shops.Persistance/Queries/CoffeeShopQueries.cs
  - CoffeePeek.Shops.Persistance/Repositories/AdminCoffeeShopQueryRepository.cs
  - CoffeePeek.Shops.Persistance/Repositories/AppDistributionRepository.cs
  - CoffeePeek.Shops.Persistance/Repositories/CityRepository.cs
  - CoffeePeek.Shops.Persistance/Repositories/CoffeeShopRepository.cs
  - CoffeePeek.Shops.Persistance/Repositories/QueryCheckInRepository.cs
  - CoffeePeek.Shops.Persistance/Repositories/ShopMenuRepository.cs
  - CoffeePeek.ShopsService.Tests/Architecture/ApplicationAdapterDependencyTests.cs
  - CoffeePeek.ShopsService.Tests/Architecture/CrossModuleWriteTests.cs
  - CoffeePeek.ShopsService.Tests/Architecture/HostWriteBypassTests.cs
  - CoffeePeek.ShopsService.Tests/Architecture/IlDependencyScanner.cs
  - CoffeePeek.ShopsService/Controllers/AdminCoffeeShopsController.cs
  - CoffeePeek.ShopsService/Controllers/AdminMenuOcrRecoveryController.cs
  - CoffeePeek.ShopsService/Controllers/CoffeeShopsController.cs
  - CoffeePeek.ShopsService/Controllers/OwnerCoffeeShopsController.cs
  - CoffeePeek.ShopsService/Controllers/ShopPrimaryEquipmentController.cs
  - CoffeePeek.ShopsService/DependencyInjection.cs
  - CoffeePeek.ShopsService/InfrastructureExtensions.cs
findings:
  critical: 5
  warning: 4
  info: 0
  total: 9
current_open_findings:
  critical: 0
  warning: 0
status: issues_found
---

# Независимый аудит Shops: P28/P34 и G1–G8

Проверен исходный HEAD `aafed224` PR #340 относительно `f0cacf55`, затем выборочно перечитаны исправления вплоть до `cb386808`. Найдены пять конкретных BLOCKER в опубликованном меню и четыре WARNING в переходном API/достоверности harness. Их исходные координаты ниже относятся к проверенному HEAD либо явно обозначенному промежуточному дереву. **Все девять source-причин исправлены; доказанных открытых BLOCKER/WARNING в рассмотренной части текущего source нет.** Findings сохранены как audit history, поэтому frontmatter `issues_found` и исходные severity counts не означают девять открытых defects.

В продолжении рассмотрены P31/P33 physical PostgreSQL ACK loss, read-only sampler, OTLP subscriptions и selective DLQ, затем P32 write experiment, AppDistribution restart и facet aggregation. Reviewer независимо прочитал публичные логи CI `37921729495` на `0295699a` и итоговый **SUCCESS CI37923822811 на cb386808**. Все 29 mandatory PG классов прошли без skips, включая исправленную Fault matrix 7/7, DirectBoundary45, Outbox3, restarted AppDistribution4 и оба perf experiments. Legacy PG/MinIO rehearsal3 и Python43 также прошли. Исторические OCR Photos failures устранены загрузкой fixture; production item loss не доказан. Runtime pending для этих сценариев закрыт. Новых concrete findings последнего source-review нет. P33 остаётся частичной: наблюдаемый возраст backlog не равен возрасту старейшего durable envelope.

Это независимый аудит production call chains и отказов, а не подтверждение прежних status/audit документов. Реестр scope выше включает файлы, прочитанные целиком либо в относящихся к проверяемому пути блоках, включая контракт, DI, EF, контроллер и тестовый harness. Обзор большого PR выборочный: он не означает чтение каждой строки всех изменённых файлов. Production/test файлы reviewer не изменял; commits, службы и контейнеры не запускал.

Реестр содержит 168 уникальных путей. Два исторических файла `ImportCandidateMenuHandlers.cs`/`ModerationShopMenuHandlers.cs` существуют в исходном aafed224 и позже разделены/удалены; их присутствие в scope отражает исходный review, а не наличие этих файлов в cb386808.

## Narrative Findings (AI reviewer)

### CR-01 — BLOCKER: OCR apply публикует событие вне своей SQL-транзакции

**File:** `C:/Users/User/RiderProjects/CoffeePeek-NET/CoffeePeek.Shops.Persistance/Modules/MenuModule.cs:19`; `CoffeePeek.Shops.Application/Services/ApplyShopMenuService.cs:132`; `CoffeePeek.Shops.Persistance/Menu/MenuOcrJobStore.cs:178–182`.

**Trigger:** обработать сохранённый успешный OCR, остановить процесс или сорвать commit после вызова `ApplyParseResultAsync`, но до commit `MenuOcrJobStore`.

**Evidence:** новый `ParseMenuOcrJobRequestedEventHandler` имеет `[NonTransactional]` и создаёт отдельные DI scopes для фаз. Baseline factory просто регистрирует `MenuOcrJobStore`; его ручная транзакция не enrolls обычный `IMessageBus`, разрешённый в новом scope. Сервис публикует invalidation до `SaveChanges/Commit`. Generated adapter priming `ScopedMessageContextHolder` касается scope самого consumer, а не новых scopes внутри worker. Автоматическая транзакция Wolverine не появляется от одного наличия `BeginTransaction` в произвольном scoped сервисе.

**Impact:** внешний наблюдатель получает событие о состоянии, которое ещё не зафиксировано и может откатиться. Mutation/job/outgoing не составляют одну durable транзакцию: нарушены P08/P28/G3. Наличие в текущем public GET кеша не доказано, поэтому конкретный stale-cache ответ не заявляется.

**Fix:** production factory должен enroll тот же `ShopsDbContext` в `IDbContextOutbox` и передать этот bus в apply-сервис. Outgoing persistence должна произойти внутри транзакции; SQL commit предшествует dispatch/flush. Recovery, изменяющий ParseStatus, также должен публиковать через этот outbox. Проверка: durable envelope виден своей транзакции до commit, снаружи отсутствует; kill/rollback не доставляет событие, restart после commit доставляет.

**Follow-up:** source fix перечитан: `MenuModule:21–28`, `MenuOcrJobStore:182–188`, recovery invalidation. CI029569 дал partial evidence до Photos fixture failure. В cb386808 Photos загружены; **CI37923822811 Fault 7/7 passed**, включая private-envelope/nonvisibility/no-early-invalidation assertions, kill/restart, сохранённый result/item и final invalidation. **Source-fixed / предусмотренный PG/crash regression passed; runtime pending закрыт.** Это не доказательство всех возможных production topology отказов.

### CR-02 — BLOCKER: unversioned snapshot затирает более свежие позиции и фотографии

**File:** `C:/Users/User/RiderProjects/CoffeePeek-NET/CoffeePeek.Contract/Events/Menu/ApplyShopMenuSnapshotEvent.cs:5–9`; `CoffeePeek.Shops.Infrastructure/Consumers/ApplyShopMenuSnapshotEventHandler.cs:17–23`; `CoffeePeek.Shops.Application/Services/ApplyShopMenuService.cs:67–83`.

**Trigger:** задержать snapshot E1; выполнить более свежую ручную правку/удаление фото или применить E2; доставить E1 в новом envelope.

**Evidence:** новый контракт содержит только ShopId, Snapshot, ApplySuggestedPriceRange, UserId. Consumer безусловно вызывает apply. `ShopMenu.ApplyParsedItems` заменяет набор позиций, `ReplacePhotos` заменяет фотографии. Источник Moderation/ImportCandidate генерирует такие snapshots для уже опубликованных магазинов. Ожидаемой версии или business receipt нет. Durable inbox распознаёт envelope, но не делает старое бизнес-событие актуальным.

**Impact:** более поздние ручные данные утрачиваются; удалённая фотография возвращается. P16/P27/P28/G4/G7 не закрыты.

**Fix:** передавать фактически наблюдённую версию с producer, сравнивать её перед apply и повышать aggregate token при каждом принятом snapshot, включая parsed-only/empty. Использовать SQL/EF concurrency guard в той же транзакции. Unversioned legacy событию требуется явная ручная сверка; подстановка текущей версии не защищает от stale-события.

**Follow-up:** перечитаны nullable `ExpectedInputRevision`, consumer guard, безусловное повышение revision через `MarkManuallyCaptured`, передача поля от HTTP к command/event. CI029569 `ShopsDirectWriteBoundaryLiveTests` прошёл 45/45, включая null/current/stale snapshot и replay после ручной правки. **Source-fixed / targeted PG regression passed.** Этот regression не заменяет отдельное interleaving race после проверки до commit или replay реальных legacy producers внешней topology. Политика старого клиента описана в WR-01.

### CR-03 — BLOCKER: новые строки OCR-job и позиций считаются существующими EF-строками

**File:** `C:/Users/User/RiderProjects/CoffeePeek-NET/CoffeePeek.Shops.Persistance/Configuration/MenuOcrJobConfiguration.cs:18`; `CoffeePeek.Shops.Persistance/Configuration/ShopMenuItemConfiguration.cs:15`; `CoffeePeek.Shops.Application/Features/Admin/Menu/ParseAdminShopMenuHandler.cs:44–46`.

**Trigger A:** загрузить ранее сохранённое меню с фотографией, вызвать admin parse. **Trigger B:** применить сохранённый OCR-result с напитком, которого ещё нет в существующем меню.

**Evidence:** `MenuOcrJob.CreatePublished:45` и `ShopMenuItem.Create:39` задают ненулевой Guid. Baseline mapping оставляет generated-key convention; новый child добавляется через навигацию уже tracked parent (`ShopMenu.QueueOcrJob:245`, `MergeParsedItems:138`). Явного `Add` этого child нет. EF DetectChanges трактует заданный generated Guid как существующий child и выбирает Modified, затем UPDATE отсутствующей строки даёт `DbUpdateConcurrencyException`. Для MenuPhoto уже существует правильный `ValueGeneratedNever`; локальный обход `EntityState.Added` в approval adapter не исправляет общий OCR путь.

**Impact:** повторный parse существующего меню и OCR, добавляющий напиток, не сохраняются. Job остаётся ResultStored или новый job не создаётся. Seed целого Added graph и mock `IApplyShopMenuService` этот дефект не покрывают.

**Fix:** `ValueGeneratedNever` для application-assigned child Id, согласованный EF snapshot. Проверить Add через реальный загруженный parent и production apply. Официальная [документация EF Core](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-3.x/breaking-changes#detectchanges-honors-store-generated-key-values) описывает этот переход Modified/Added; документация проверена через Context7.

**Follow-up:** обе настройки и model snapshot перечитаны. CI029569 DirectWriteBoundary подтвердил enqueue через tracked existing parent; stored OCR write experiment сохранил 16 Applied jobs и 16 настоящих SQL items. Два crash cases также прошли item count/price/volume assertions до Photos fixture failure. **Source-fixed / targeted PG persistence evidence passed.** Mock/in-memory результат не использован как доказательство SQL.

### CR-04 — BLOCKER: отказ 400 оставляет созданное меню

**File:** `C:/Users/User/RiderProjects/CoffeePeek-NET/CoffeePeek.Shops.Application/Features/Admin/Menu/AttachAdminShopMenuPhotosHandler.cs:44–47`; `CoffeePeek.Shops.Application/Services/ApplyShopMenuService.cs:142–150`.

**Trigger:** существующий магазин без меню; attach request с `Photos=[]`.

**Evidence:** handler сначала вызывает `GetOrCreateAsync`, который добавляет новое меню в tracker, затем возвращает BadRequest. Return Response не вызывает rollback. Baseline generated adapter выполняет `SaveChanges` и commit после return handler (строки 79/82), даже когда Response содержит ошибку. Проверка Photos добавлена этим PR после мутации.

**Impact:** отвергнутый HTTP-запрос меняет persisted состояние. Нарушена validate-before-mutate семантика P28/G3.

**Fix:** проверять null/empty и доверенные upload bindings до `GetOrCreate`. PostgreSQL production-dispatch тест должен подтвердить 400 и отсутствие меню/outgoing.

**Follow-up:** ранняя проверка и перестановка claim перед `GetOrCreate` перечитаны. CI029569 DirectWriteBoundary содержит `MenuEntryPoints_RejectEmptyWithoutCreatingMenu_AndInsertTrackedParentJob` и прошёл 45/45. **Source-fixed / targeted PG no-mutation regression passed.** Проверяется production dispatch, не полный gateway HTTP request.

### CR-05 — BLOCKER: legacy draft OCR обходит guard опубликованного меню

**File:** `C:/Users/User/RiderProjects/CoffeePeek-NET/CoffeePeek.Shops.Infrastructure/Consumers/ParseMenuRequestedEventHandler.cs:75–104`; `CoffeePeek.Moderation.Application/Features/Menu/MenuParseEvents.cs:19–25`; `CoffeePeek.Shops.Application/Services/ApplyShopMenuService.cs:126–130`.

**Trigger:** queued OCR с SourceKind ModerationShop/ImportCandidate и PublishedShopId; затем удалить фото/изменить ручной ввод опубликованного меню; доставить старый draft event.

**Evidence:** quarantine проверяет только SourceKind.PublishedShop. Оба draft producers передают live ShopId. Legacy handler принимает другой SourceKind, находит published target и применяет результат без revision/hash/job guard; `ApplyParseResultAsync` добавляет фотографии из старого сообщения. Новый persisted-job protocol не участвует.

**Impact:** удалённое фото возвращается; старый ответ добавляет новые позиции после ручной правки, которую persisted-job protocol должен делать неприменимой. Это оставшийся альтернативный production writer, поэтому закрытие P16/P28/G4 неверно даже при зелёном job gate. ADR честно сохраняет legacy draft semantics, но такое исключение не доказывает безопасность published writer.

**Fix:** legacy draft OCR обновляет только свой draft посредством `MenuParsedEvent`; live ShopMenu изменяется через persisted job либо snapshot с наблюдённой ожидаемой версией. Проверка должна доставлять оба draft SourceKind после live revision/photo change и проверять неизменность live menu.

**Follow-up:** source diff удаляет весь прямой live-apply/save/retry блок и его writer dependencies. **Source-fixed.** Legacy draft consumer теперь возвращает только draft result; отдельный replay обоих SourceKind через реальную внешнюю topology reviewer не выполнял. Устаревшие draft responses, повторное billing самого draft OCR и старые исполняемые consumers остаются отдельными rollout ограничениями; это не открытый обход writer в рассмотренном новом consumer.

### WR-01 — WARNING: producer возвращает Success для snapshot, который гарантированно будет отброшен

**File:** `C:/Users/User/RiderProjects/CoffeePeek-NET/CoffeePeek.Moderation.Application/Features/Shop/Menu/UpdateModerationShopMenuHandler.cs:28–39`; `CoffeePeek.Moderation.Application/Features/Import/Menu/UpdateImportCandidateMenuHandler.cs:28–36`; `CoffeePeek.Shops.Infrastructure/Consumers/ApplyShopMenuSnapshotEventHandler.cs:17–20` (новое рабочее дерево).

**Trigger:** старый клиент обновляет меню уже опубликованного draft/candidate без нового ExpectedInputRevision.

**Evidence:** producers сохраняют draft и возвращают Success с event, содержащим null; исправленный consumer гарантированно возвращает без live apply. Старый запрос теперь получает 200 при заведомо неприменяемой live-правке.

**Impact:** скрытая деградация API при переходе; клиент не узнаёт, что требуется повтор с revision/ручная сверка. Сам отказ consumer безопасен для данных, но переход P34 нельзя считать прозрачным.

**Fix:** для опубликованного target отклонять missing/negative expected revision до `ReplaceMenuItems/Save`, сообщать, откуда прочитать актуальную версию. Старые уже queued envelopes безопасно quarantines с операционным способом reconciliation. Проверить old-request HTTP response и отсутствие draft/live/outgoing mutations.

**Follow-up:** в обоих producers перечитана проверка `ExpectedInputRevision is null or < 0` до изменения draft. Published target получает 400 с указанием источника актуальной версии. **Source-fixed.** PG snapshot consumer regression подтверждён отдельно, но настоящий старый HTTP клиент через Moderation/Gateway и отсутствие всех producer-side mutations этим review runtime не проверены. Rollout API перехода требует явной inventory/сверки клиентов.

### WR-02 — WARNING: ACK-retry worker ожидает Response там, где bus выбрасывает ConflictException

**File:** `C:/Users/User/RiderProjects/CoffeePeek-NET/CoffeePeek.Shops.Infrastructure.Tests/Faults/ShopsFaultWorkerTests.cs:54–61`; `CoffeePeek.Shops.Application/Features/Admin/Shops/UpdateAdminCoffeeShopHandler.cs:38–40` (новое рабочее дерево).

**Trigger:** сервер успешно зафиксировал профиль revision 1, его commit reply потерян; worker `postgres-retry` повторяет command с ExpectedRevision 0.

**Evidence:** handler явно бросает `ConflictException` со StatusCode 409. Вызов `bus.InvokeAsync<Response<AdminPublishedShopDto>>` происходит до ветки проверки checkpoint. Generated adapter откатывает текущую попытку и повторно бросает исключение; custom `ShopsFaultHost` не выполняет HTTP exception mapping. `GlobalExceptionHandler` преобразует exception только в HTTP pipeline. До `Assert.False(response.IsSuccess)` и marker `retry-conflict` управление не доходит.

**Impact:** child завершается с ошибкой, ACK recovery test ждёт недостижимый marker/падает по exited worker. Это дефект надёжности gate, а не доказательство небезопасного повторного production commit.

**Fix:** ветку `postgres-retry` выполнять до обычного `InvokeAsync`: `Assert.ThrowsAsync<ConflictException>(() => bus.InvokeAsync<Response<AdminPublishedShopDto>>(command, ct))`, проверить StatusCode 409, затем записать marker. Обычный успешный путь оставляет Response assertion. Проверку настоящего HTTP 409 выполнять отдельно; bus assertion не доказывает HTTP contract.

**Follow-up 4129904f:** ветка `postgres-retry` теперь вызывает `Assert.ThrowsAsync<ConflictException>`, проверяет exception.StatusCode 409 и пишет marker. Успешная ветка выполняет обычный Invoke/Response assertion. Название исправлено на `ExpectedRevisionProtectsCommandRetry`. **Source-fixed / ACK live scenario passed in CI029569.** HTTP contract не приписывается bus-тесту.

### WR-03 — WARNING: глобальные inbox counts не идентифицируют событие ACK-сценария

**File:** `C:/Users/User/RiderProjects/CoffeePeek-NET/CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/ShopsFaultRecoveryLiveTests.cs:111–119`, `:327–332` (новое рабочее дерево).

**Trigger:** другой checkpoint того же class fixture оставляет необработанный envelope; ACK-сценарий проверяет `IncomingCount > 0` или ожидает `IncomingCount == 0` для собственного нового магазина.

**Evidence:** helper считает всю `wolverine_incoming_envelopes` с `status <> 'Handled'`, без envelope ID, message_type или business ID. При наличии чужого pending envelope первый assertion остаётся true даже при отсутствии собственного invalidation. Последний assertion остаётся false после успешного удаления собственного Redis key, если чужая строка осталась. Redis key scoped к shop и проверяет нужный business эффект, но не делает глобальный count доказательством сохранения/дренирования собственного envelope.

**Impact:** тест способен ложно пройти проверку durable persistence конкретного события или ложно упасть на recovery чужой операции. Это особенно существенно для shared-fixture crash matrix; результат counts нельзя выдавать за доказательство своего event.

**Fix:** привязать assertions к message type и business/envelope ID текущего shop/job либо выделить отдельную БД на checkpoint. При ACK loss после остановки первого процесса подтвердить pending envelope именно текущего `ShopProfileCacheInvalidationRequested`; после restart подтвердить его обработку и собственный Redis key. Глобальный backlog допустим как отдельная диагностическая метрика.

**Follow-up 4129904f:** ACK-сценарий снимает baseline pending IDs после host-ready; interceptor сохраняет private-transaction incoming/outgoing IDs перед commit. `Assert.Single(...Except(baseline))` выделяет новый ID; внешнее соединение подтверждает его отсутствие до commit. После видимого server commit/drop/kill подтверждается сохранение именно этого ID. После restart ожидаются собственный Redis key и отсутствие этого ID в pending set, которое исключает Handled. **Source-fixed / ACK live scenario passed in CI029569.** Старые глобальные count-проверки других матриц не объявлены исправленными этой заменой.

### WR-04 — WARNING: write experiment заявляет единый PoolSize 4 при отдельном Wolverine storage pool

**File:** `C:/Users/User/RiderProjects/CoffeePeek-NET/CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/ShopsWritePerformanceLiveTests.cs:64–69`, `:120–121`; `CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/ShopsDirectWriteBoundaryLiveTests.cs:711` (первоначальный P32 source).

**Trigger:** выполнить write experiment и интерпретировать поле JSON `PoolSize=4` как лимит всех PostgreSQL соединений host, включая durable queue/outbox storage.

**Evidence:** `MaxPoolSize=4` и отдельный ApplicationName передаются только новым business EF contexts. Reused `CreateHost` регистрирует `PersistMessagesWithPostgresql(fixture.ConnectionString)` с исходной строкой, создавая отдельный storage pool с исходным пределом. Общая БД не означает общий лимит соединений. Scope pool capacity подтверждён через Context7 `/websites/npgsql_doc` и source registrations.

**Impact:** эксперимент ошибочно сообщает ограничение всей storage нагрузки четырьмя соединениями и способен стать неверным evidence для общей queue/pool pressure. Четыре business clients с четырьмя EF slots сами по себе также не доказывают pool wait/перегрузку.

**Fix:** использовать один согласованный datasource/connection budget для обеих регистраций либо явно разнести metadata `BusinessEfPoolSize=4` и отдельный Wolverine pool limit; результаты не превращать в доказательство общего host limit 4.

**Follow-up:** artifact пишет `BusinessEfPoolSize=4`, `SeparateWolverinePoolSize` берётся из исходной connection string. CI029569 и финальный CI37923822811 write experiment прошли 1/1 и напечатали metadata EF=4/Wolverine=100. **Source-fixed / metadata live evidence passed.** Дополнения production AuditInterceptor/prebuilt imports в cb386808 также имеют новый успешный runtime результат; measurement boundaries остаются ограниченными ниже.

## Что проверено и как это ограничивает G1–G8

| Ворота | Наблюдение этого review |
| --- | --- |
| G1 | Прослежены DI и межмодульные write ports, проверены IL architecture tests. Доступный бинарник проходит 10 architecture tests; это не аудит каждой dependency большого diff. |
| G2 | Коллекции профиля/вложенных schedules и child mutation каталога прочитаны. Выявлен альтернативный writer live меню CR-05. |
| G3 | CR-01/03/04 source-fixed; финальный cb CI подтверждает SQL no-mutation/enqueue/apply и все OCR crash cases/private envelopes/final invalidation. |
| G4 | CR-02/05 source-fixed; PG snapshot stale/new-envelope replay после manual edit прошёл. Legacy draft routing и mixed old/new topology требуют отдельного внешнего evidence. |
| G5 | Выборочно прослежены public/admin/owner read models, загрузка коллекций и фильтры hidden. 26 contract tests прошли на доступном бинарнике; полный HTTP/JSON/auth E2E не выполнен. |
| G6 | Финальный CI37923822811 SUCCESS, 29 mandatory PG classes passed без skips, Fault7/7. Legacy PG/MinIO3 и Python43 прошли; build/dependency audit/обычные tests также success. |
| G7 | City/menu-value/check-in-photo preflight и additive переход прочитаны; cb CI SchemaUpgrade3, CheckInPhotoMigration6, CityReference10 и legacy PG/MinIO transfer/cleanup3 passed. Это synthetic populated rehearsal, не production clone. Legacy primary-equipment флаг, API transition и runtime inventory остаются внешними задачами. |
| G8 | Финальный cb CI подтверждает ACK, OCR/media/Redis process recovery, selective DLQ/gauge, updated write/read experiments и AppDistribution host restart. P33 exact oldest age, реальные OTLP alerts, production budgets/soak и внешний rollout sign-off этим review не закрыты. |

Принятый `isOpen=null` при отсутствии расписания соответствует утверждённому ADR и не записан как новый defect. Сохранение неоднозначного legacy equipment flag и запрет destructive Down для community photos также не объявлены дефектами сами по себе. Authoritative snapshot для создания нового магазина отличается от stale snapshot записи существующего магазина; finding CR-02 относится к последнему.

## Выполненные проверки и ограничения

- Независимый call-chain audit production write/early-flush/commit/event путей, включая меню, shop approval/import, gallery/Media claim, Community, coffee import/catalog, cities, zones и public addresses.
- `dotnet test ... --no-build --filter ...` завершился **Test Run Aborted**: VSTest не подключился к testhost за 90 секунд. Это не pass.
- Прямой xUnit запуск `ShopsCompatibilityContractTests`: **26 total, 0 failed/errors/skipped/not run**. Прямой запуск `Architecture.*`: **10 total, 0 failed/errors/skipped/not run**.
- Оба успешных запуска использовали существующий Release бинарник с LastWriteTimeUtc **2026-10-09 09:38:21**, без rebuild. Они не подтверждают изменения общего рабочего дерева после этой сборки.
- Для EF/Wolverine использован Context7: `/dotnet/entityframework.docs`, `/jasperfx/wolverine`. Generated adapters прочитаны как свидетельство baseline commit ownership/scope; они не заменяют проверку regenerated production adapters.
- Сведения о CI run37909817024 на fd8604bd получены от координатора; reviewer не перепроверял полный лог этого run и не переносит его результат на aafed/текущее дерево.
- CI029569 `37921729495` независимо проверен через `gh run view --json` и публичный `--log`; ниже перечислены фактически выполненные классы. Reviewer не запускал эти PG сценарии локально и не приписывает себе их выполнение.
- Финальный CI37923822811 на точном cb386808 независимо проверен через run metadata и job113797898205 compact logs: conclusion success, все 29 PG success lines, Fault7/7, legacy3 и Python43. Это основной runtime evidence текущего source.

Локально не запускались PostgreSQL migrations/constraints, RabbitMQ/Redis delivery, process-kill recovery или production role-auth HTTP E2E. Сопоставленные CI результаты дают runtime evidence только своим source version/scenario. Исправленный source сам по себе не закрывает все восемь ворот.

## Продолжение P31/P33: physical PostgreSQL ACK loss и telemetry

**Scope:** изменения поверх `0c44900b`, исправления и selective DLQ/gauge в `4129904f`, source сверка cb386808 на 11:40 UTC и финальная независимая CI сверка на 11:44 UTC. Production sampler/DI/OTLP и P31/P33 harness перечитаны; OCR fixture уточнение рассматривается отдельно ниже. Reviewer не запускал новые tests и production/test файлы не менял.

### Physical ACK scenario

`SavedChanges` interceptor ставит барьер после business SQL, до Wolverine commit. После барьера добавляется downstream latency toxic; независимое прямое PostgreSQL соединение ждёт видимую revision 1, пока marker успешного client completion отсутствует. Затем proxy отключается и первый worker завершается. Это содержательно отличается от искусственного exception после успешного commit: сервер действительно commit, а reply задержан и соединение оборвано. Source assertions WR-02/03 исправлены в `4129904f` и перечитаны. CI029569 подтвердил ACK case: fault class имеет семь cases, пять passed и ровно два перечисленных failures, оба OCR Photos. Это вывод из полного class counters/failure list и его source cases, а не выдуманная отдельная строка Passed в логах.

Проверены lifecycle `HttpClient`/`HttpResponseMessage`, EF read contexts и child processes. Ответы и клиенты fault-control освобождаются; `IncomingCountAsync` владеет переданным context и освобождает его. `ShopsFaultProcess.DisposeAsync` идемпотентен, завершает дерево процесса и ждёт exit. В test finally удаляется toxic и включается proxy. Новые порт/pgpass/proxy относятся к изолированной CI инфраструктуре, production credentials/routing не изменяются. Локальные контейнеры или сервисы reviewer не запускал.

Исправленное название упоминает CommandRetry: worker вызывает bus и проверяет command revision guard после неопределённого commit. Фактический HTTP status/error mapping здесь не исполняется. ACK scenario passed как в CI029569, так и в полностью успешной финальной Fault matrix cb386808 (7/7).

### Selective DLQ replay и storage gauge

Перечитан `ShopProfileOutboxLiveTests.DeadLetterGauge_AndSelectiveReplay_KeepCommittedProfileAndUnselectedFailure` (`:118–181`) и его test-host registration. Два успешных profile commands повышают отдельные профильные ревизии до 1; controlled invalidator затем бросает только тестовый `InvalidOperationException`. Test-host policy отправляет такое исключение в настоящую PostgreSQL DLQ. Production Redis retry policy этим тестовым исключением не заменяется и не считается проверенной здесь.

Тест ждёт одну/две DLQ строки по тестовой причине, сохраняет selected/unselected envelope IDs и собирает `shops.durability.dead_letters` через `MeterListener.RecordObservableInstruments`. Sentinel -1 и ожидание измерения 2 не превращают отсутствие успешного sample в pass. Fixture создаёт собственную throwaway БД; таблица остаётся небольшой, поэтому pinned exact fallback ниже estimated threshold 10000 применим к этой проверке. Этот assertion не обещает точные counts большой production DLQ.

Pinned `DeadLetterEnvelopeQuery.MessageIds` поддержан installed 6.35.0. `MessageDatabase<T>.ReplayAsync` выполняет filtered UPDATE replayable, а не ждёт обработки. Test корректно ждёт собственный cache callback и отсутствие selected строки из DLQ после вызова, проверяет сохранность unselected строки и профильных revisions, затем повторяет selection уже отсутствующего ID. Это проверка безопасного повторного operator selection и selective recovery; она не доказывает business idempotency произвольного нового envelope. API semantics проверены через Context7 `/jasperfx/wolverine` и установленную RDBMS DLL.

Новых доказанных bugs в source не найдено. `ShopProfileOutboxLiveTests` прошёл **3/3, 0 failed/skipped** в обоих проверенных CI, включая selective DLQ/gauge на cb386808. Failed sampler poll/`sample_age`, `outbox.observed_age` и реальный OTLP collector в этом class не исполняются и его результат их не закрывает.

### Sampler: достоверность и ограничения

Установленный пакет **Wolverine 6.35.0** прочитан по локальным DLL через read-only `ilspycmd`; commit из NuGet nuspec — `c0b0ce61ca9ba886ff89666034f9b65715c22048`. Context7 `/jasperfx/wolverine` возвращает текущую документацию, поэтому сведения о наличии API сверены с установленной сборкой. У `PersistedCounts` нет `OldestOutgoing`. В pinned `OutgoingEnvelopeTable` нет enqueue timestamp по умолчанию; nullable `timestamp` добавляется только при `OutboxStaleTime`. Эта настройка меняет recovery/schema семантику и в репозитории не включена. `deliver_by` — deadline, его нельзя использовать как время постановки сообщения.

| Сигнал | Что source действительно показывает | Ограничение |
| --- | --- | --- |
| `shops.durability.dead_letters` | Последний успешный provider-reported `DeadLetter` | `FetchCountsAsync` использует estimates PostgreSQL для больших таблиц. |
| `shops.durability.outbox.observed_age` | Время с первого nonzero Outgoing в последовательности наблюдений этого процесса; успешный zero sample сбрасывает начало | Не oldest envelope age. Restart, 30-секундная дискретность, estimated counts и failed polls ограничивают интерпретацию непрерывности. |
| `shops.durability.sample_age` | Время после завершения последней успешной выборки | Failed poll сохраняет предыдущий snapshot; качество других gauges оценивается вместе с freshness. |

Pinned `FetchCountsAsync`: `Outgoing` и `DeadLetter` читаются через `estimateTableCount`. Если оценка **>=10000**, возвращается estimate; ниже порога выполняется `count(*)`. Для неинициализированных/нулевых statistics при непустой relation также используется exact fallback. Порог применяется к оценке, поэтому даже пустая после удаления большая таблица может ещё сообщать старый estimate. Это не всегда точные counts и не годится как exact assertion для произвольной таблицы. Description dead_letters это корректно оговаривает; аналогичное ограничение Outgoing должно сопровождать observed_age в runbook.

Sampler не читает message bodies, не меняет schema, retention, jobs или envelopes. `IMessageStore` принадлежит host; sampler его не освобождает. Собственный `Meter` и `PeriodicTimer` освобождаются. Observable callbacks читают атомарно опубликованный immutable snapshot; до первого успешного sample measurements отсутствуют, неизвестное состояние не превращается в искусственный zero. Poll sequential и не запускает перекрывающиеся выборки. Pinned `FetchCountsAsync` не принимает cancellation token: остановка ждёт провайдерскую выборку/её timeout; это API ограничение, мгновенная отмена SQL здесь не доказана.

### Registration, OTLP и безопасность

Production `AddInfrastructure` регистрирует hosted sampler после регистрации Wolverine; fault-host также его регистрирует для Shops DB. Общий `AddServiceDefaults` подключает OTLP при заданном `OTEL_EXPORTER_OTLP_ENDPOINT`; дополнительные Shops subscriptions накапливаются до построения telemetry providers.

Pinned runtime создаёт meter `Wolverine:<ServiceName>`, ActivitySource — `Wolverine`. Следовательно, `AddMeter("Wolverine*")` и `AddSource("Wolverine")` подписываются на фактические источники 6.35.0. Поддержка wildcard и additive регистрации проверена через Context7 `/open-telemetry/opentelemetry-dotnet`. Source-подписка не доказывает доступность коллектора, scrape/OTLP export или настройку alerts.

Новые Shops durability gauges не имеют user/job/shop labels. Pinned `Envelope.WriteTags` пишет type, message/conversation IDs, destination, payload size и tenant/saga/schedule IDs, без message body, JWT/паролей или произвольных HTTP headers. RabbitMQ endpoint URI у pinned transport имеет форму `rabbitmq://queue/<name>`/exchange/topic, без AMQP credentials. Нового доказанного разглашения приватных payload в этой области не найдено; runtime export конфигурация по-прежнему требует стендовой проверки.

**Остаток P33/G8:** exact oldest envelope age не реализован, observed backlog age — явно ограниченная замена. ACK и selective DLQ/gauge имеют runtime evidence финального cb CI. Freshness/failed poll, реальный OTLP collector/alerts и rollout/runbook действия им не покрыты. Результат старого run37916005811 reviewer отдельно не проверял и не переносит на новые commits.

## Финальный P32 source-review: write experiment

Перечитаны `ShopsWritePerformanceLiveTests`, `WriteTransactionProbe`, reused `CreateHost`, actual module registrations и production `CheckInAuditInterceptor`/base `AuditInterceptor`. Последняя сверка относится к cb386808. Новых доказанных findings после исправления WR-04 нет.

Fixture создаёт отдельную throwaway Shops DB, seed/host startup происходят вне измерения. Последняя scoped registration выбирает benchmark business context с EF pool=4, production audit interceptor и shared transaction probe; Wolverine durable inbox/outbox/local queue использует отдельную исходную connection string. Artifact честно разносит эти budgets. Host/context scopes останавливаются/освобождаются; тест не меняет production DI.

| Сценарий | Что измеряется | Ограничение интерпретации |
| --- | --- | --- |
| Profile 64, clients 4 | Изменение 64 разных shops с ExpectedRevision=0; invoke/save/commit/outbox path | Это не конфликтующие правки одной aggregate и не arrival-rate load. |
| Import 8 × 100, sequential | Первый snapshot создаёт 100 products, следующие семь обновляют те же SourceProductIds; все восемь runs Applied | Это 100 distinct products, не 800; clients=1. В cb JSON cloning/payload construction и timestamps подготовлены до timing. |
| Stored OCR 16, clients 4 | Actual `MenuOcrJobStore.ApplyStoredAsync` с production repos/apply/outbox, 16 persisted items и Applied jobs | Provider/network этапы не выполняются, не измеряется полный OCR lifecycle. |

Snapshot IDs используют корректные hex `0..7`; последовательные captured timestamps возрастают. После действий тест ждёт queue drain/no DLQ и проверяет business persistence. В этой отдельной свежей БД global counts допустимы для batch drain; это не идентификация произвольного события в shared fault matrix.

Latency stopwatch охватывает DI/EF pool wait/handler/save/commit/outbox flush и assertions внутри action; scheduler wait до старта action и подготовка import payload исключены. `WriteTransactionProbe` берёт timestamp после завершения BEGIN и заканчивает после COMMIT: это длительность открытой **business EF** транзакции, без acquisition/BEGIN round trip и post-commit dispatch; raw storage transactions отдельного Wolverine pool этим probe не измеряются. Nearest-rank percentile вычислен корректно, но при 8/16/64 samples P95/P99 часто равны max и не становятся стабильными production percentiles.

`BatchAllocatedBytes` — process-wide allocation delta, включая фоновую работу host/harness; WorkingSet — process sample, не request memory. Experiment не содержит warmup/soak, explicit pool-wait distribution, смешанный трафик, production budgets или SQL count/EXPLAIN по новым write scenarios. Его `Experimental=true` соответствует этим границам. CI029569 baseline и исправленный cb experiment прошли 1/1 и напечатали EF=4/Wolverine=100. Current audit/payload timing corrections подтверждены успешным cb runtime, но прежние численные baseline не переименованы в результаты исправленного benchmark. Новый write P95: profile 3495.5092 ms, import-100-products 1282.4376 ms, stored-OCR apply 260.9589 ms; это данные короткого CI experiment, не утверждённые production budgets.

## AppDistribution restart и facet aggregation

`AppDownloadRedirectDurabilityLiveTests` в cb386808 после graceful Stop создаёт новый host на той же PG DB. Новый query handler читает сохранённый iOS URL; другой case через новый repository scope читает active Android release Id/VersionCode/FileUrl. Это свежие DI/providers/readers, прежний host не выполняет фоновые consumers во время нового чтения. Resource cleanup новых hosts корректен. Это graceful host restart; process kill, HTTP redirect/gateway и реальная deployed topology этим тестом не исполняются. **Финальный cb CI class 4/4 passed, включая оба новых restarted-host assertions.**

Facet optimization (`DiscoveryQueryStore.Facets.cs:45–53`, `:69–86`) прослежена до `MatchingProducts`, `MatchingVariants`, catalog validation и `WithGroup`. Для каждой non-brew group убирается только её выбор, остальные filters/roaster/query/publication/offer predicates сохраняются. Scalar/array values группируются по Code после distinct пары ProductId/Code: одна product с duplicate legacy array code считается один раз, как прежний Contains predicate. Missing active code даёт 0; null/неактивные значения не добавляются в публичные options. Brew оставляет прежний predicate, включая omni/default purpose и совпадение price/weight/stock на одной variant. PriceRange/TotalItems не переключены на другую семантику. Нового доказанного semantic regression не найдено.

Новый PG assertion создаёт duplicate chocolate array и сохраняет self-exclusion/price-range checks (`DiscoveryQueryLiveTests:247–268`). **Финальный cb CI DiscoveryQuery9 и ReadPerformance1 passed:** новый `SelectMany/Distinct/GroupBy` исполнился в PostgreSQL, regression с duplicate array/self-exclusion прошёл. CI metrics facets: 72 SQL в baseline029 → 13 SQL в cb, P95 1137.5171 → 413.1963 ms (32 samples, 4 clients). Это два коротких CI runs; различие latency не объявлено изолированным causal эффектом optimization или production budget. SQL count и semantic regression подтверждают конкретную реализацию.

## Проверенное CI evidence и остаток

Основной независимый источник — [CI37923822811](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37923822811), точный HEAD `cb3868081d9f86e4c2aaa647580e000cd9a31ee1`, job113797898205. GitHub run и build_and_test conclusion **success**, job завершён 11:42:49 UTC. Build/dependency audit/обычные tests, mandatory PG step, legacy PG/MinIO rehearsal и Python/deployment configuration validation steps success. Build/push images и deploy_vps jobs **skipped**; это не deployed runtime или production replay.

Все предыдущие 26 PG классов перечисленные ниже повторно passed на cb, затем **ShopsFaultRecoveryLiveTests7, CityReferenceLiveTests10 и CityCatalogFreshReadLiveTests1** также напечатали ненулевые `required live tests passed`. Итого **29 mandatory PG classes, 0 failed/errors/skipped/not-run**. Fault7/7 включает исправленные OCR kill/restart Photos/final invalidation assertions; AppDistribution4 включает new-host reads; Outbox3 включает selective DLQ/gauge; DirectBoundary45 и ReadPerf1/WritePerf1 подтверждены на текущем source. Отдельный LegacyMigrationLiveTests — **3 passed**, transfer/photos/moderation/votes/reports/counters → cleanup/archive drop → rerun с PG/MinIO. Python — **Ran43 / OK**, coffee import configuration validated. Fixtures synthetic; реальные production data/topology не использованы.

### Исторический baseline и Photos fixture

Независимый источник — [CI37921729495](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37921729495), HEAD `0295699a5139df1146b0d56a1a7cb13de0451de4`. GitHub run conclusion **failure**. Build/audit/обычные tests прошли; реальный PG step остановился на Fault. Последующие cleanup rehearsal/deploy validation steps **skipped**, их pass не заявляется.

Все 26 классов до Fault напечатали ненулевой `required live tests passed`, с нулевыми failed/errors/skipped/not-run: DiscoveryClassification1, DiscoveryQuery9, CoffeeImportRecovery3, ShopProfileWolverine3, ShopProfileRevision5, ShopVisibilityWolverine1, ShopProfileOutbox3, DrinkCatalogWriteProtocol2, AppDownloadRedirectDurability4, ShopMenuManualWrite5, MenuOcrJob4, ShopEquipment5, ShopChangeRequestMenu1, ShopChangeRequestOutbox1, CheckInModerationRace4, CheckInPhotoMigration6, ShopsSchemaUpgrade3, CheckInLimits4, CheckInHelpfulOutbox1, ReviewReportCommit1, CoffeeZonePolygon1, CoffeeZoneWriteProtocol10, ShopsDirectWriteBoundary45, CoffeeChildConcurrency6, ShopsReadPerformance1, ShopsWritePerformance1.

Исторические Fault counters: **7 total, 5 passed, 2 failed, 0 skipped/not-run/errors**. Только `after-ocr-store` и `before-ocr-apply-commit` указаны как failures; старый stack trace указывает `ShopsFaultRecoveryLiveTests.cs:316` — `Assert.Single(current.Photos)`. Старый final query загружал только Items. Applied, stored-result item, persisted item/price/volume, provider-call count и private SQL/outbox probes прошли до этого assertion. **Production item loss не установлен; эта гипотеза снята.** Тогда final invalidation assertion после Photos в двух cases не достигнут. В cb386808 query включает Photos (`:299`), Photos assertion теперь на `:317`; **финальная Fault matrix7/7 прошла, включая ранее недостигнутый final invalidation assertion**.

**Открытые доказанные source blockers в рассмотренном scope: 0. Финальный cb386808 CI evidence получен; runtime pending для выполненных сценариев закрыт.** Остаток: внешний old/new consumer/client inventory и legacy envelope reconciliation/drain; реальные production migration/topology rehearsal; HTTP/auth/JSON проверки действующих клиентов; exact oldest durable age и OTLP/freshness alerts; production write/read budgets и long soak. Synthetic legacy PG/MinIO rehearsal прошёл и больше не pending, но production clone этим не объявлен проверенным. Ограничения не объявлены обнаруженными production bugs и не превращены в pass. Review не выдаёт merge/deploy/rollout sign-off.

_Reviewer: Codex, независимый gsd-code-reviewer. Изменён только этот report; production/test ownership оставлены координатору._
