# P28: актуальная матрица write protocol

Срез PR #340, 9 октября 2026 года. Старый список из 146 вхождений заменён текущим. Контрольный поиск находит 37 SaveChangesAsync references (включая override/делегирование), 12 cache removals и три SetAsync references избранного. Все 12 Redis removals находятся в трёх durable consumers; application writers больше не удаляют Redis непосредственно.

HTTP→store baseline и cross-module write baseline пусты и проверяются architecture suite. [Реестр входов](shops-write-inventory.md) связывает HTTP/messages с владельцами. [Independent review](shops-independent-review.md) проверил прямой SQL, scopes, old events, API/auth и границы. [CI cb386808](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37923822811) полностью SUCCESS, включая все семь fault cases. Отсутствующий Include Photos в старом fixture исправлен; stored result, photos и committed invalidation теперь подтверждены после process restart.

| Путь / граница | Validate / tracking / commit / retry | Live evidence |
| --- | --- | --- |
| Profile/admin/owner/visibility/gallery/tags/owner/import enrichment | Domain validation перед apply, единая ProfileRevision, EF concurrency → 409; publish invalidation внутри transaction. Gallery early flush для SQL readback не является commit. | Profile/revision/visibility/outbox; physical Redis и commit-ack gate |
| Five zone writers | Domain polygon invariants, early SQL readback под outer EF transaction; late error откатывает geometry/status/overrides и outbox. | CoffeeZoneWriteProtocolLiveTests:10 |
| CityRepository delete | FK Restrict и concurrency checks; known FK violation →409, только операция удаления; flush остаётся под command transaction. | CityReferenceLiveTests |
| Manual menu / attach/remove/OCR request | Весь пакет проверен до mutation; SQL upsert + revision атомарны; tracked state перечитан; empty photos не создают menu/job. | ShopMenuManualWriteLiveTests:5, direct boundary |
| Approved menu/profile и snapshot consumers | Business replay ID, ExpectedInputRevision, controlled tracking clear только для постоянного отказа; старое approval/snapshot без revision не перезаписывает live menu. | ShopChangeRequestMenu/Outbox, direct snapshot tests |
| MenuOcrJobStore | Fresh scope на переход, row locks, собственный commit. Menu service использует тот же enrolled IDbContextOutbox; FlushOutgoingMessages после commit. Provider вне transaction. | MenuOcrJobLiveTests:4; три OCR process checkpoints passed |
| Coffee product/tag/classification | Application coordinator; узкие SQL Storage; early flush/savepoint только для известной uniqueness race. Exception не превращается в произвольный partial commit. | Direct boundary:45, child concurrency:6 |
| Coffee import | Own transaction/advisory ordering; payload hash, stable RunId, Failed record и повтор same payload; hash mismatch/stale snapshot отклоняются. | CoffeeImportRecoveryLiveTests:3 |
| PublicAddresses | Commands под Wolverine, owner port, advisory/savepoint; dry-run не пишет, outer transaction не завершается внутри service. | Direct boundary initialize/rename/backfill |
| Favorites / import failure | Commands под Wolverine; idempotent unique insert/savepoint; посторонние unique errors не подавляются. | Direct boundary six cases |
| Community / review reports | Owner repository SQL locks и additive CheckInPhotos; late error rollback; idempotent consumer/cache delivery. | Community races/migration/limits/votes/reports |
| AppDistribution | Settings GET read-only; commands commit перед ответом; redirect analytics durable + idempotent fallback, unconfirmed при двойном отказе. | AppDownloadRedirectDurabilityLiveTests:4 |
| Cache consumers | Strict failures пробрасываются в Wolverine. Redis delete replay безопасен; business write уже committed. | Physical fault gate; selected DLQ replay оставляет unrelated failure |

В [CI 4129904f](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37918432384) эти обязательные классы до process gate прошли без skips, включая 45 direct cases. ACK-loss scenario и DLQ test тоже прошли; весь run красный из-за двух проверок незагруженных Photos в OCR fixture. Позже весь обязательный contour прошёл в CI cb386808; текущий P28 подтверждён source audit и runtime gate.

Ранние saves нужны для DB constraints/savepoint или SQL readback. Generated adapters начинают внешнюю EF transaction до handler и коммитят до ответа; зависимые adapters включены в release build. Особые owners — import lifecycle и short OCR scopes — перечислены явно.

## Контрольный список references

Следующий поиск воспроизводит список; это ссылки вызовов/объявлений, а не количество команд:

```text
rg -n '\bSaveChangesAsync\(|\bRemoveAsync\(|\bRemoveByPattern(?:Async)?\(|\bSetAsync\(' CoffeePeek.Shops.Application CoffeePeek.Shops.Infrastructure CoffeePeek.Shops.Persistance CoffeePeek.ShopsService -g '*.cs' -g '!**/Internal/Generated/**' -g '!**/Migrations/**' -g '!**/bin/**' -g '!**/obj/**'
```

```text
CoffeePeek.Shops.Persistance\Menu\MenuOcrJobStore.cs:184:        await db.SaveChangesAsync(ct);
CoffeePeek.Shops.Persistance\PublicAddresses\PublicAddressService.cs:156:        await db.SaveChangesAsync(ct);
CoffeePeek.Shops.Persistance\PublicAddresses\PublicAddressService.cs:202:        if (!dryRun) await db.SaveChangesAsync(ct);
CoffeePeek.Shops.Persistance\Repositories\CityRepository.cs:30:            await dbContext.SaveChangesAsync(ct);
CoffeePeek.Shops.Infrastructure\Consumers\ShopProfileCacheInvalidationRequestedHandler.cs:34:                await cache.RemoveAsync(CacheKey.Shop.Detail(shopId), ct);
CoffeePeek.Shops.Infrastructure\Consumers\ShopProfileCacheInvalidationRequestedHandler.cs:35:            await cache.RemoveByPatternAsync(CacheKey.Shop.SearchPattern(), ct);
CoffeePeek.Shops.Infrastructure\Consumers\ShopProfileCacheInvalidationRequestedHandler.cs:36:            await cache.RemoveByPatternAsync(CacheKey.Shop.ListPattern(), ct);
CoffeePeek.Shops.Infrastructure\Consumers\ShopProfileCacheInvalidationRequestedHandler.cs:37:            await cache.RemoveAsync(CacheKey.Shop.PublicPlatformStats(), ct);
CoffeePeek.Shops.Infrastructure\Consumers\ShopChangeRequestApprovedHandler.cs:109:        await dbContext.SaveChangesAsync(ct);
CoffeePeek.Shops.Persistance\Menu\ApplyApprovedShopMenuChangeService.cs:38:            await db.SaveChangesAsync(ct);
CoffeePeek.Shops.Infrastructure\Consumers\DrinkCatalogCacheInvalidationRequestedHandler.cs:29:            await cache.RemoveAsync(CacheKey.Shop.MenuDrinks(), ct);
CoffeePeek.Shops.Infrastructure\Consumers\DrinkCatalogCacheInvalidationRequestedHandler.cs:30:            await cache.RemoveByPatternAsync(CacheKey.Shop.DetailPattern(), ct);
CoffeePeek.Shops.Infrastructure\Consumers\CheckInModerationDecidedHandler.cs:28:        await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Infrastructure\Consumers\CheckInCacheInvalidationRequestedHandler.cs:36:                await cache.RemoveAsync(CacheKey.Shop.Detail(shopId), ct);
CoffeePeek.Shops.Infrastructure\Consumers\CheckInCacheInvalidationRequestedHandler.cs:39:                await cache.RemoveAsync(CacheKey.Shop.Visited(userId), ct);
CoffeePeek.Shops.Infrastructure\Consumers\CheckInCacheInvalidationRequestedHandler.cs:40:                await cache.RemoveAsync(CacheKey.Shop.VisitStatistics(userId), ct);
CoffeePeek.Shops.Infrastructure\Consumers\CheckInCacheInvalidationRequestedHandler.cs:42:            await cache.RemoveByPatternAsync(CacheKey.Shop.ListPattern(), ct);
CoffeePeek.Shops.Infrastructure\Consumers\CheckInCacheInvalidationRequestedHandler.cs:43:            await cache.RemoveAsync(CacheKey.Shop.PublicPlatformStats(), ct);
CoffeePeek.Shops.Infrastructure\Consumers\CheckInCacheInvalidationRequestedHandler.cs:44:            await cache.RemoveByPatternAsync(CacheKey.Shop.SearchPattern(), ct);
CoffeePeek.Shops.Infrastructure\Consumers\ApplyShopMenuSnapshotEventHandler.cs:37:            await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Persistance\Configuration\ShopsDbContext.cs:37:        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();
CoffeePeek.Shops.Persistance\Configuration\ShopsDbContext.cs:39:    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
CoffeePeek.Shops.Persistance\Configuration\ShopsDbContext.cs:54:                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
CoffeePeek.Shops.Persistance\Configuration\ShopsDbContext.cs:60:                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
CoffeePeek.Shops.Persistance\Discovery\DiscoveryFavoritesStore.cs:34:    public async Task SetAsync(Guid userId, string kind, string slug, bool favorite, CancellationToken ct)
CoffeePeek.Shops.Persistance\Discovery\DiscoveryFavoritesStore.cs:52:        try { await db.SaveChangesAsync(ct); }
CoffeePeek.Shops.Application\Features\Review\Reports\ResolveReviewReportHandler.cs:26:            await uow.SaveChangesAsync(ct);
CoffeePeek.Shops.Persistance\CoffeeCatalog\DiscoveryClassificationStore.cs:41:        try { await db.SaveChangesAsync(ct); }
CoffeePeek.Shops.Persistance\CoffeeCatalog\DiscoveryClassificationStore.cs:52:        await db.SaveChangesAsync(ct);
CoffeePeek.Shops.Persistance\CoffeeCatalog\DiscoveryClassificationStore.cs:61:        await db.SaveChangesAsync(ct);
CoffeePeek.Shops.Persistance\CoffeeCatalog\DiscoveryClassificationStore.cs:117:        try { await db.SaveChangesAsync(ct); }
CoffeePeek.Shops.Persistance\CoffeeCatalog\CoffeeProductStorage.cs:18:        try { await db.SaveChangesAsync(ct); }
CoffeePeek.Shops.Persistance\CoffeeCatalog\CoffeeImportStorage.cs:42:        await db.SaveChangesAsync(ct);
CoffeePeek.Shops.Persistance\CoffeeCatalog\CoffeeImportStorage.cs:52:            await db.SaveChangesAsync(ct);
CoffeePeek.Shops.Persistance\CoffeeCatalog\CoffeeCatalogTagStorage.cs:19:        await db.SaveChangesAsync(ct);
CoffeePeek.Shops.Persistance\CoffeeCatalog\CoffeeCatalogTagStorage.cs:24:        await db.SaveChangesAsync(ct);
CoffeePeek.Shops.Application\Features\CheckIn\UpdateUnifiedCheckInHandlers.cs:46:        await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Application\Features\CheckIn\UpdateUnifiedCheckInHandlers.cs:72:        await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Application\Features\Discovery\SetDiscoveryFavoriteHandler.cs:6:        favorites.SetAsync(command.UserId, command.Kind, command.Slug, command.Favorite, ct);
CoffeePeek.Shops.Application\Features\CheckIn\CreateUnifiedCheckInHandler.cs:72:        await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Application\Features\Discovery\IDiscoveryFavoritesStore.cs:8:    Task SetAsync(Guid userId, string kind, string slug, bool favorite, CancellationToken ct);
CoffeePeek.Shops.Application\Features\CoffeeZones\DeleteCoffeeZoneMembershipOverrideHandler.cs:24:            await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Application\Features\CoffeeZones\UpdateCoffeeZoneHandler.cs:27:        await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Application\Features\CoffeeZones\CreateCoffeeZoneHandler.cs:25:        await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Application\Features\CoffeeZones\SetCoffeeZoneStatusHandler.cs:30:        await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Application\Features\CoffeeZones\SetCoffeeZoneMembershipOverrideHandler.cs:43:        await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Application\Features\CoffeeShop\ShopProfile\AddPublishedShopPhotosHandler.cs:63:        await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Application\Features\Admin\Menu\UpdateAdminShopMenuHandler.cs:76:        await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Application\Features\AppDownloads\PublishAndroidReleaseHandler.cs:27:            await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Application\Features\Admin\Menu\RemoveAdminShopMenuPhotosHandler.cs:49:        await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Application\Features\Admin\Menu\AttachAdminShopMenuPhotosHandler.cs:71:        await unitOfWork.SaveChangesAsync(ct);
CoffeePeek.Shops.Application\Features\Admin\Menu\ParseAdminShopMenuHandler.cs:46:        await unitOfWork.SaveChangesAsync(ct);
```
