# ShopsService: cache readers и invalidation (P08)

Срез PR #340, 9 октября 2026 года. Фактических cache-aside readers для перечисленных ниже шести каталогов больше нет: каждый запрос читает DB через repository. После committed write следующий свежий контекст получает актуальные данные, без late-fill старого Redis snapshot.

| Исторический ключ | Reader | Текущий механизм |
| --- | --- | --- |
| city:list:all | GetCitiesRequestHandler | Прямое чтение DB |
| equipment:list:all | GetAllEquipmentHandler | Прямое чтение DB |
| coffeebean:list:all | GetAllBeansHandler | Прямое чтение DB |
| brewmethod:list:all | GetAllBrewMethodsHandler | Прямое чтение DB |
| shop:tags:catalog | GetShopTagsHandler | Прямое чтение DB |
| shop:menu:drinks | GetMenuDrinksHandler | Прямое чтение DB |
| shop:detail:v3:*, shop:search:v3:*, shop:list:city:*, shop:public:platform-stats | Get/Set cache-aside reader в текущем ShopsService не найден | Сохраняются post-commit invalidation consumers для legacy keys |

Late-fill гонка раньше позволяла reader после cache miss сохранить старый SQL snapshot уже после commit+DELETE другой команды. Удаление cache-aside из реальных catalog readers устраняет этот механизм. Старый ключ может оставаться до TTL, но перечисленные readers его не обслуживают. Ни Redis contents, ни гипотетические внешние readers не объявлены источником истины для этих API.

Все 12 production RemoveAsync/RemoveByPatternAsync references находятся в ShopProfileCacheInvalidationRequestedHandler, CheckInCacheInvalidationRequestedHandler и DrinkCatalogCacheInvalidationRequestedHandler. Они используют strict invalidator, не подавляют Redis failure и запускаются после business commit через durable inbox/outbox. SetAsync references из P28 относятся к Discovery favorites API/store, не к Redis fill.

CatalogReadFreshnessTests и CityCatalogFreshReadLiveTests защищают повторное чтение. ShopProfileOutboxLiveTests проверяет настоящий PG commit/retry и selective DLQ replay. Physical ShopsFaultRecoveryLiveTests выключает Redis TCP, убивает процесс после DELETE до ack и восстанавливает сохранённое invalidation без второго PUT. Rabbit/Media delivery проверяется отдельным сценарием этого gate.

Доказательства и remaining OCR gate — в [статусе](shops-implementation-status.md), [P28](shops-p28-write-protocol-audit.md), [fault contour](shops-fault-gate.md).
