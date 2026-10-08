using System.Text.Json;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class OpenStatusContractTests
{
    [Fact]
    public void PublicAndLegacyDtos_SerializeUnknownAsJsonNull()
    {
        object[] responses =
        [
            new ShortShopDto { Name = "Cafe", IsOpen = null },
            new ShopDto { Name = "Cafe", IsOpen = null },
            new CoffeeShopDetailsDto { Name = "Cafe", IsOpen = null },
            new PublicShopSummaryDto { Name = "Cafe", IsOpen = null }
        ];

        foreach (var response in responses)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, response.GetType(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("isOpen").ValueKind);
        }
    }
}
