# P05: исполняемый baseline архитектурных границ ShopsService

Дата: 9 октября 2026 года. `CrossModuleWriteTests` и `HostWriteBypassTests` требуют **пустой** список нарушений без baseline исключений. Новая прямая запись ломает проверку. `ApplicationAdapterDependencyTests` не имеет исключений. `TableOwnershipTests` сравнивает список владельцев с EF-моделью.

Menus обновляет профиль через `IShopPriceRangeWriter`; Community-фото принадлежат `CheckInPhotos`; PublicAddresses вызывает Application owner-порт для автоматической allocation, rename и backfill. Writer каждого модуля принимает только свои типы. Последние три общих `SetPublicSlug` вызова из Persistence устранены.

9 октября избранное, отчёты импорта, все пять записей классификации и initialize/rename/backfill PublicAddresses переведены в application commands. HTTP→store baseline пуст и заменён строгим `Assert.Empty`. Generated adapters держат общую EF/outbox-транзакцию до ответа; HTTP/auth и PostgreSQL commit/rollback проверяются отдельно.

После P17 consumer делегирует применение меню сервису модуля Menus; прежние семь прямых вызовов удалены из baseline. Сканер также охватывает `Persistance.Menu.*`, чтобы новый Menus writer не мог незаметно менять агрегат Shops. Отдельный test-only fixture намеренно вызывает `CoffeeShop.SetPriceRange` из этого namespace и доказывает срабатывание детектора.

Сканер разбирает IL production-сборок, включая `MoveNext` async-методов и lambda bodies; проверяет фактические вызовы доменных mutators, `DbSet<T>` writes и HTTP → application write-port вызовы. Application проверяется также по типам полей, параметров, возвращаемых значений, базовых классов и интерфейсов: ссылки на конкретные Persistence/Infrastructure/host/EF запрещены. Композиционный root и generated Wolverine adapters не входят в правило HTTP-контроллеров; read-only Discovery projections проходят, поскольку не вызывают mutators. Границы определяются картой владельцев из [P03](shops-module-ownership.md), а не расположением `Roaster` в историческом namespace `CoffeeShopAggregate`.

Эта IL-проверка не доказывает отсутствие динамических вызовов, raw SQL, EF change tracking без явного mutator, косвенного вызова через неподходящий интерфейс или внешнего процесса. P28 должен отдельно сверять `SaveChanges`, `ExecuteSql*`, SQL/EF-проекции и фактические write paths; P34 — транзакции и HTTP/queue fixtures. Добавление нового прямого write после P05 нельзя оправдать отсутствием его в сканере: владелец модуля обязан расширить детектор либо перенести запись в порт.
