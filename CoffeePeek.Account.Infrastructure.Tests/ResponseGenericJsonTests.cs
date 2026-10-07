using System.Text.Json;
using CoffeePeek.Contract.Dtos.Admin;
using CoffeePeek.Shared.Kernel.Response;
using FluentAssertions;

namespace CoffeePeek.Account.Infrastructure.Tests;

public class ResponseGenericJsonTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Deserialize_AdminStatsPayload_BindsTypedData()
    {
        const string json =
            """
            {
              "isSuccess": true,
              "message": "Operation successful",
              "data": {
                "totalCoffeeShops": 12,
                "totalReviews": 34,
                "newCoffeeShopsToday": 1,
                "newReviewsToday": 2,
                "pendingModerationShops": 3,
                "pendingModerationReviews": 4,
                "importPending": 5,
                "importPublished": 6,
                "importRejected": 7,
                "importSkipped": 8,
                "importInFeed": 9
              }
            }
            """;

        var payload = JsonSerializer.Deserialize<Response<AdminServiceStatsDto>>(json, WebOptions);

        payload.Should().NotBeNull();
        payload!.IsSuccess.Should().BeTrue();
        payload.Data.Should().NotBeNull();
        payload.Data.TotalCoffeeShops.Should().Be(12);
        payload.Data.ImportInFeed.Should().Be(9);
    }

    [Fact]
    public void RoundTrip_SuccessFactory_KeepsTypedData()
    {
        var original = Response<AdminServiceStatsDto>.Success(
            new AdminServiceStatsDto(TotalCoffeeShops: 42, TotalCheckIns: 7));

        var json = JsonSerializer.Serialize(original, WebOptions);
        var restored = JsonSerializer.Deserialize<Response<AdminServiceStatsDto>>(json, WebOptions);

        restored.Should().NotBeNull();
        restored!.IsSuccess.Should().BeTrue();
        restored.Data.Should().NotBeNull();
        restored.Data.TotalCoffeeShops.Should().Be(42);
        restored.Data.TotalCheckIns.Should().Be(7);
    }
}
