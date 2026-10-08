using System.Text.Json;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Application.Services;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Moderation.Application.Tests;

public sealed class PublicModerationServiceTests
{
    [Fact]
    public async Task PayloadAsync_StartsIndependentCatalogLookupsConcurrently()
    {
        var started = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<Guid[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lookup = new Mock<IPublicAddressLookup>();
        lookup.Setup(x => x.ResolveIdsAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref started) == 4) allStarted.TrySetResult();
                return release.Task;
            });

        var pending = new PublicModerationService(lookup.Object).PayloadAsync(
            new(Tags: ["tag"], Roasters: ["roaster"], Equipments: ["equipment"], BrewMethods: ["method"]),
            TestContext.Current.CancellationToken);

        try
        {
            await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            release.TrySetResult([Guid.NewGuid()]);
        }

        await pending;
        started.Should().Be(4);
    }

    [Fact]
    public async Task PublicSubmissions_RoundTripSlugsAndRemoveAggregateIdsIncludingPhotoOwners()
    {
        var ids = new[] { "shops", "cities", "roasters", "beans", "equipments", "brew-methods", "shop-tags" }
            .ToDictionary(x => x, _ => Guid.NewGuid());
        var lookup = new Mock<IPublicAddressLookup>();
        lookup.Setup(x => x.ResolveIdsAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string kind, string[] slugs, CancellationToken _) => slugs.Select(_ => ids[kind]).ToArray());
        lookup.Setup(x => x.GetByIdsAsync(It.IsAny<string>(), It.IsAny<Guid[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string kind, Guid[] owners, CancellationToken _) => owners.Select(id => new PublicAddressMetadata(id, kind + "-slug", "/" + kind + "/" + kind + "-slug", 1)).ToArray());
        var service = new PublicModerationService(lookup.Object);
        var ct = TestContext.Current.CancellationToken;
        var payload = await service.PayloadAsync(new(Tags: ["shop-tags-slug"], Roasters: ["roasters-slug"], Equipments: ["equipments-slug"], BrewMethods: ["brew-methods-slug"]), ct);
        payload.TagIds.Should().Equal(ids["shop-tags"]);
        payload.RoasterIds.Should().Equal(ids["roasters"]);
        payload.EquipmentIds.Should().Equal(ids["equipments"]);
        payload.BrewMethodIds.Should().Equal(ids["brew-methods"]);
        var workflow = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var changes = await service.ChangesAsync([new(workflow, ids["shops"], actor, ShopChangeSection.Tags,
            payload, ModerationStatus.Pending, actor, null, null, DateTime.UtcNow, null)], ct);
        changes[0].Payload.Tags.Should().Equal("shop-tags-slug");
        changes[0].Shop!.Slug.Should().Be("shops-slug");
        var shops = await service.ShopsAsync([new ModerationShopDto { Id = workflow, Name = "Cafe", UserId = actor,
            CityId = ids["cities"], PublishedShopId = ids["shops"], CoffeeBeanIds = [ids["beans"]],
            EquipmentIds = [ids["equipments"]], RoasterIds = [ids["roasters"]], BrewMethodIds = [ids["brew-methods"]] }], ct);
        shops[0].Beans.Should().Equal("beans-slug");
        shops[0].PublishedShop!.Slug.Should().Be("shops-slug");
        var json = JsonSerializer.Serialize(new { changes, shops }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var id in ids.Values.Append(actor)) json.Should().NotContain(id.ToString());
        json.Should().Contain(workflow.ToString()).And.NotContain("ownerId");
    }
}
