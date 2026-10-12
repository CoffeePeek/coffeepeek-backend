# ShopsService: текущий реестр записи (P01/P28)

Срез 9 октября 2026 года, PR #340. Оба architecture baseline — HTTP→store и cross-module writes — пусты. Этот реестр описывает входы и владельцев commit; результаты выполнения находятся в [статусе плана](shops-implementation-status.md).

| Семейство входов | Владелец и граница записи | Проверка |
| --- | --- | --- |
| Admin/owner профиль, visibility, focus, владелец, теги, галерея | CoffeeShop domain methods; Wolverine EF/outbox commit; ExpectedRevision/If-Match, общая ProfileRevision и durable invalidation. | ShopProfileWolverineLiveTests, ShopProfileRevisionLiveTests, ShopVisibilityWolverineLiveTests, ShopProfileOutboxLiveTests |
| Admin Cities/Equipment/Beans/BrewMethods/ShopTags/Roasters | Owner repositories под command transaction. Используемый City — 409; local ShopEquipment primary; новые admin Roaster photos — Media claim/finalization. | CityReferenceLiveTests, ShopEquipmentLiveTests, ShopsDirectWriteBoundaryLiveTests |
| Coffee zones/membership overrides | Command → domain → early flush для SQL readback → общий commit. | CoffeeZonePolygonLiveTests, CoffeeZoneWriteProtocolLiveTests |
| Admin manual menu, photo attach/remove, OCR request | Menus methods, validate-before-mutate, Media claim перед записью, InputRevision и сохранённый MenuOcrJob. Empty photo batch — 400 без создания меню. | ShopMenuManualWriteLiveTests, ShopsDirectWriteBoundaryLiveTests |
| Admin OCR recovery | POST api/admin/menu-ocr-jobs/{jobId}/recover-unknown-outcome; Admin policy, Running ≥15 минут, row locks и короткая enrolled EF/outbox transaction. | ParseMenuOcrJobRequestedEventHandlerTests, process fault gate |
| Coffee products/tags/assignments | Application coordinators → узкие product/tag Storage adapters; early flush/savepoint без самостоятельного outer commit. | ShopsDirectWriteBoundaryLiveTests, CoffeeChildConcurrencyLiveTests |
| Classification/options | Wolverine commands → DiscoveryClassificationStore, общая EF transaction. | DiscoveryClassificationLiveTests, ShopsDirectWriteBoundaryLiveTests |
| PUT/DELETE Discovery favorite | SetDiscoveryFavoriteCommand; actor из auth; idempotent insert/delete и save под Wolverine. | ShopsDirectWriteBoundaryLiveTests |
| PublicAddresses initialize/rename/backfill | Commands → service → IPublicAddressOwnerWriter; advisory/savepoints, owner columns и revision. Dry-run не пишет. | ShopsDirectWriteBoundaryLiveTests, architecture tests |
| POST internal/coffee-import/snapshots | Application CoffeeImportService → CoffeeImportStorage; собственная короткая transaction, advisory ordering, payload hash, Failed/Applied lifecycle и стабильный RunId при replay. | CoffeeImportRecoveryLiveTests, DiscoveryQueryLiveTests |
| POST internal/coffee-import/failures | RecordCoffeeImportFailureCommand, Wolverine commit, idempotent report. Оба internal входа защищены отдельным import token. | ShopsDirectWriteBoundaryLiveTests |
| Community create/edit/delete/moderation/vote; review reports | Row/advisory locks; отдельные CheckInPhotos; durable invalidation и прежние Media IDs. | CheckInModerationRaceLiveTests, CheckInLimitsLiveTests, CheckInHelpfulOutboxLiveTests, ReviewReportCommitLiveTests |
| AppDistribution settings/releases/version | Commands → AppDistributionRepository → commit перед ответом. Settings GET не создаёт singleton. | AppDownloadRedirectDurabilityLiveTests |
| GET download redirect | Durable RecordAppDownloadRedirectCommand, idempotent inline fallback при enqueue failure; двойной отказ — unconfirmed. | AppDownloadRedirectDurabilityLiveTests, controller tests |
| Moderation approve/import publish/enrichment | Application coordinators и owner repositories; общая ProfileRevision при изменении существующего профиля. | profile/import/revision regression |
| ShopChangeRequestApprovedV1 | Business ID replay guard, menu revision, Menus owner service; постоянный отказ очищает tracking, transient пробрасывается. | ShopChangeRequestMenuLiveTests, ShopChangeRequestOutboxLiveTests |
| ApplyShopMenuSnapshotEvent | Published snapshot требует ExpectedInputRevision ≥0. Missing/stale/legacy — ручная сверка или ignore; accepted snapshot всегда повышает InputRevision. | ShopsDirectWriteBoundaryLiveTests, Moderation producer tests |
| ParseMenuOcrJobRequestedEvent | NonTransactional consumer, отдельные scopes claim/result/apply; provider вне SQL transaction. Apply использует enrolled outbox и commit перед invalidation delivery. | MenuOcrJobLiveTests, process fault gate |
| Legacy ParseMenuRequestedEvent | Published JobId → новый lifecycle; Published без JobId → quarantine. Draft OCR возвращает MenuParsedEvent и не пишет live menu. | ParseMenuRequestedEventHandlerTests |
| UserNameChangedEvent | Community-owned idempotent ExecuteUpdate под consumer transaction. | architecture/consumer regression |
| Profile/CheckIn/DrinkCatalog cache consumers | Strict Redis invalidation после business commit; retry/DLQ сохраняет Wolverine. | physical Redis fault gate, selective DLQ replay |

Прямой SQL включает manual menu upsert/revision, Community vote/limits/moderation, UserName ExecuteUpdate, PublicAddresses advisory/savepoints и import lifecycle. Early EF flush оставлен для readback и известных uniqueness races; он отдельно проверяется на невидимость из внешней сессии и rollback при позднем отказе.

См. [P28](shops-p28-write-protocol-audit.md), [cache readers](shops-cache-read-write-inventory.md), [независимый review](shops-independent-review.md). OCR process gate полностью прошёл в [CI cb386808](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37923822811): saved offer, Photos и post-commit invalidation после restart. Старый fixture Include исправлен; production item loss не установлен.
