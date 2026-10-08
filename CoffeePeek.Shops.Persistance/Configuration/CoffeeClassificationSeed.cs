using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

namespace CoffeePeek.Shops.Persistance.Configuration;

internal static class CoffeeClassificationSeed
{
    internal static CoffeeFilterValue[] Values() =>
    [
        new("brew", "filter", "Для фильтра", null, 10, Guid.Parse("a92ddce7-5e29-85a2-db4a-1a2f60784d34")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("brew", "espresso", "Для эспрессо", null, 20, Guid.Parse("6da24927-4805-5e2b-cb6e-c2c6eb5b2fa6")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("brew", "omni", "Универсальная обжарка", null, 30, Guid.Parse("d080c9bd-2948-0679-7ed5-003a64d02b39")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("caffeine", "regular", "Обычный", null, 10, Guid.Parse("5178c1b8-94dd-dce8-bd90-378136fb4e53")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("caffeine", "decaf", "Декаф", null, 20, Guid.Parse("badc0b70-4a55-c2f5-3946-8aae4aa4cd22")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("caffeine", "lowcaf", "Лоукаф", null, 30, Guid.Parse("ac497bdc-51a5-023b-285e-ab0eb8f99758")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("roast", "light", "Светлая", null, 10, Guid.Parse("32113231-a541-15c4-11e2-36f9329a42b9")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("roast", "medium", "Средняя", null, 20, Guid.Parse("2e0ba3a7-9d39-8d1c-e242-e205a08873ca")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("roast", "dark", "Тёмная", null, 30, Guid.Parse("254d85ed-42ef-41bd-1081-a7892f8a265e")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("acidity", "low", "Низкая", null, 10, Guid.Parse("29da4e1e-0b46-f6e4-f8eb-39de1fee4b43")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("acidity", "balanced", "Умеренная", null, 20, Guid.Parse("342ce57b-c707-e9d5-5ea2-81b00102202f")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("acidity", "bright", "Яркая", null, 30, Guid.Parse("19070462-1575-2b3e-5070-5f35559aff64")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("processing", "washed", "Мытая", null, 10, Guid.Parse("89331ac7-2e53-d3ce-00a8-283992ce95cc")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("processing", "natural", "Натуральная", null, 20, Guid.Parse("dbdaf974-bdaa-8594-0956-aa177d323f13")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("processing", "honey", "Хани", null, 30, Guid.Parse("aec4a4e2-dcbc-508e-e844-b2f0f3150cfb")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("processing", "wet-hulled", "Вет-халл", null, 40, Guid.Parse("59675992-d8db-d8eb-af54-30b405abcb07")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("fermentation", "anaerobic", "Анаэробная", null, 10, Guid.Parse("12a2e090-d43a-15bf-131e-61173de6d063")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("fermentation", "semi-anaerobic", "Полуанаэробная", null, 20, Guid.Parse("351f800f-93c5-31a2-2cef-1b5532f5d0ee")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("fermentation", "experimental", "Экспериментальная", null, 30, Guid.Parse("fca22118-6efa-c7be-94bc-7a78069250ff")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("taste", "chocolate", "Шоколад", null, 10, Guid.Parse("736fae16-cd48-ab03-10fd-01f9472d3168")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("taste", "nut", "Орехи", null, 20, Guid.Parse("c91f50c1-e453-8174-276d-3d245dcb2559")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("taste", "caramel", "Карамель", null, 30, Guid.Parse("a50087f0-7bdb-c65f-6dca-69ef7370f986")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("taste", "berry", "Ягоды", null, 40, Guid.Parse("ecb03f92-2df1-7150-8d3d-71b8e4ace8ee")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("taste", "citrus", "Цитрусы", null, 50, Guid.Parse("7a3aab3f-a00e-8949-d257-87d12347f5d3")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("taste", "tropical", "Тропические фрукты", null, 60, Guid.Parse("ce92527e-f69b-a294-6f72-fabaf422d238")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("taste", "floral", "Цветы", null, 70, Guid.Parse("216e51b2-c578-02b8-8096-7a1c324bdb17")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("taste", "spicy", "Пряности", null, 80, Guid.Parse("c7a3e9ea-a772-d015-f21f-0a954060e591")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("composition", "single-origin", "Моносорт", null, 10, Guid.Parse("81086b71-d50d-722b-87a9-2584d92cf93a")) { CreatedAtUtc = DateTime.UnixEpoch },
        new("composition", "blend", "Купаж", null, 20, Guid.Parse("ba0b140f-7c84-2836-c7ac-9653ccc59849")) { CreatedAtUtc = DateTime.UnixEpoch }
    ];

    internal static object[] RoasterTags() =>
    [
        new { Id = Guid.Parse("19ab1acf-0048-9c36-0be9-09f36cff9972"), Slug = "online-order", Name = "Онлайн-заказ", Description = "Подтверждён онлайн-заказ непосредственно у обжарщика; ссылка стороннего продавца сама по себе не подтверждает этот тег.", SortOrder = 10, IsActive = true, CreatedAtUtc = DateTime.UnixEpoch },
        new { Id = Guid.Parse("69f4da38-1229-6a98-123a-1b1ef5109cd7"), Slug = "pickup", Name = "Самовывоз", Description = "Подтверждена возможность забрать заказ у обжарщика.", SortOrder = 20, IsActive = true, CreatedAtUtc = DateTime.UnixEpoch },
        new { Id = Guid.Parse("f4e52e41-8b12-689f-762b-fe97c5005228"), Slug = "subscription", Name = "Подписка на кофе", Description = "Подтверждена действующая подписка на регулярные поставки кофе.", SortOrder = 30, IsActive = true, CreatedAtUtc = DateTime.UnixEpoch },
        new { Id = Guid.Parse("803fd248-2490-ff13-b849-1aee90ca948e"), Slug = "wholesale", Name = "Опт", Description = "Подтверждена работа с оптовыми заказами.", SortOrder = 40, IsActive = true, CreatedAtUtc = DateTime.UnixEpoch },
        new { Id = Guid.Parse("c847057e-c11a-7503-ce81-556d9e5f1f35"), Slug = "training", Name = "Обучение", Description = "Подтверждены доступные обучающие программы.", SortOrder = 50, IsActive = true, CreatedAtUtc = DateTime.UnixEpoch }
    ];
}
