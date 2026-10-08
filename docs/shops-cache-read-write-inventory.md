# ShopsService: фактические пути чтения и инвалидации кеша (P08)

Срез исходников от 8 октября 2026 года после P08. Таблица относится к вызовам внутри ShopsService, а не к гипотетическим внешним клиентам Redis. Ранее `RedisService.GetAsync` с фабрикой и отдельные `GetAsync`/`SetAsync` реализовывали cache-aside без проверки версии между чтением БД и записью в кеш; затронутые публичные readers теперь обходят эти ключи.

| Ключ | Чтение/заполнение в ShopsService | Инвалидация при записи | Состояние |
| --- | --- | --- | --- |
| `city:list:all` | `GetCitiesRequestHandler` | Create/Update/Delete City вызывают `City.ListPattern` | Чтение переведено на БД при каждом запросе; старый ключ может жить до TTL, но больше не обслуживается этим handler. Новая запись видна в новом контексте после commit. |
| `equipment:list:all` | `GetAllEquipmentHandler` теперь читает БД на каждом запросе | Create/Update/Delete Equipment | Ключ больше не обслуживается этим reader; тест проверяет обновление при следующем запросе. |
| `coffeebean:list:all` | `GetAllBeansHandler` теперь читает БД на каждом запросе | Create/Update/Delete Bean | Ключ больше не обслуживается этим reader; тест проверяет обновление при следующем запросе. |
| `brewmethod:list:all` | `GetAllBrewMethodsHandler` теперь читает БД на каждом запросе | Create/Update/Delete BrewMethod | Ключ больше не обслуживается этим reader; тест проверяет обновление при следующем запросе. |
| `shop:tags:catalog` | `GetShopTagsHandler` теперь читает БД на каждом запросе | Admin ShopTags | Ключ больше не обслуживается этим reader; тест проверяет обновление при следующем запросе. |
| `shop:menu:drinks` | `GetMenuDrinksHandler` теперь читает БД на каждом запросе | DrinkCatalogHandlers | Ключ больше не обслуживается этим reader; тест проверяет обновление при следующем запросе. |
| `shop:detail:v3:*`, `shop:search:v3:*`, `shop:list:city:*`, `shop:public:platform-stats` | В текущем ShopsService нет вызовов Get/Set для этих ключей; public stats читает БД | Много обработчиков, включая durable profile invalidation | Инвалидация существует, но активный cache-aside reader в коде не найден. Защиту от late-fill для этих ключей вводить без реального читателя бессмысленно. |

Исходная гонка была такой: запрос A получает cache miss и старый снимок из БД; команда B фиксирует изменение и удаляет ключ; затем A пишет старый снимок под тем же ключом. Прямое чтение БД во всех шести перечисленных справочниках устраняет late-fill через эти readers. `CatalogReadFreshnessTests` и `GetShopTagsHandlerTests` моделируют изменение снимка между двумя вызовами и подтверждают два обращения в repository. Эти unit-тесты не заменяют сквозную проверку HTTP и PostgreSQL при реальном commit. Старые ключи и writer-side инвалидации сохраняются для совместимости и могут оставаться в Redis до TTL; их наличие не влияет на ответы указанных обработчиков.

`ShopProfileCacheInvalidationRequested` проходит через PostgreSQL outbox и durable local queue; строгий `IStrictCacheInvalidator` пробрасывает ошибку Redis на retry. `ShopProfileOutboxLiveTests` проверяет этот путь с инъекцией `RedisConnectionException`, но не подтверждает доставку RabbitMQ или физическую перезагрузку Redis. Для перечисленных каталогов ответ теперь определяется чтением БД после commit, а не состоянием этих ключей в Redis.
