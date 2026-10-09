# P05: исполняемый baseline архитектурных границ ShopsService

Дата: 9 октября 2026 года. Источник истины для точных IL-вызовов и задачи удаления — `CoffeePeek.ShopsService.Tests/Architecture/CrossModuleWriteTests.cs` и `HostWriteBypassTests.cs`. Реестр в тестах сравнивается с фактическими вызовами **на равенство**: новая запись ломает проверку, исчезнувшая требует удалить старое исключение. `ApplicationAdapterDependencyTests` не имеет исключений. `TableOwnershipTests` сравнивает список владельцев с EF-моделью.

| Текущий прямой путь | Владелец операции | Количество зафиксированных IL-вызовов | Устранение |
| --- | --- | ---: | --- |
| Menus → `CoffeeShop.SetPriceRange` | Shops | 2 | P28: узкий Shops-порт и общий протокол записи; обе ветки уже увеличивают `ProfileRevision` |
| Community check-in → `ShopPhoto` | Shops сейчас; после разделения Community | 1 | P18/P29: отдельное фото посещения |
| Общий PublicAddresses writer → `IPublicSlugOwner.SetPublicSlug` | Соответствующий entity module | 3 | P20/P25: вызов owner-порта, общий компонент оставляет reservation/audit |

9 октября избранное, отчёты импорта, все пять записей классификации и initialize/rename/backfill PublicAddresses переведены в application commands. HTTP→store baseline пуст и заменён строгим `Assert.Empty`: новых исключений нет. Generated adapters держат общую EF/outbox-транзакцию до ответа; HTTP/auth и PostgreSQL commit/rollback проверяются отдельно. Межмодульные записи из таблицы выше ещё требуют устранения.

После P17 consumer делегирует применение меню сервису модуля Menus; прежние семь прямых вызовов удалены из baseline. Сканер также охватывает `Persistance.Menu.*`, чтобы новый Menus writer не мог незаметно менять агрегат Shops. Отдельный test-only fixture намеренно вызывает `CoffeeShop.SetPriceRange` из этого namespace и доказывает срабатывание детектора.

Сканер разбирает IL production-сборок, включая `MoveNext` async-методов и lambda bodies; проверяет фактические вызовы доменных mutators, `DbSet<T>` writes и HTTP → application write-port вызовы. Application проверяется также по типам полей, параметров, возвращаемых значений, базовых классов и интерфейсов: ссылки на конкретные Persistence/Infrastructure/host/EF запрещены. Композиционный root и generated Wolverine adapters не входят в правило HTTP-контроллеров; read-only Discovery projections проходят, поскольку не вызывают mutators. Границы определяются картой владельцев из [P03](shops-module-ownership.md), а не расположением `Roaster` в историческом namespace `CoffeeShopAggregate`.

Эта IL-проверка не доказывает отсутствие динамических вызовов, raw SQL, EF change tracking без явного mutator, косвенного вызова через неподходящий интерфейс или внешнего процесса. P28 должен отдельно сверять `SaveChanges`, `ExecuteSql*`, SQL/EF-проекции и фактические write paths; P34 — транзакции и HTTP/queue fixtures. Добавление нового прямого write после P05 нельзя оправдать отсутствием его в сканере: владелец модуля обязан расширить детектор либо перенести запись в порт.
