using System;
using System.Text.Json;
using CoffeePeek.Contract.Events.Menu;
using FluentAssertions;

namespace CoffeePeek.Shops.Application.Tests.Features.Menu;

public sealed class MenuOcrContractCompatibilityTests
{
    [Fact]
    public void LegacyParseResult_DeserializesWithAbsentJobId()
    {
        const string legacyJson = """
            {"SourceKind":2,"SourceId":"00000000-0000-0000-0000-000000000002",
             "PublishedShopId":null,"Success":true,"Error":null,"SuggestedPriceRange":null,
             "Items":[],"Unmatched":[],"CapturedAtUtc":"2026-10-08T00:00:00Z"}
            """;

        var message = JsonSerializer.Deserialize<MenuParsedEvent>(legacyJson);

        message.Should().NotBeNull();
        message!.JobId.Should().BeNull();
        typeof(MenuParsedEvent).FullName.Should().Be("CoffeePeek.Contract.Events.Menu.MenuParsedEvent");
    }

    [Fact]
    public void LegacyParseRequest_DeserializesWithAbsentJobMetadata()
    {
        const string legacyJson = """
            {"SourceKind":3,"SourceId":"00000000-0000-0000-0000-000000000001",
             "PublishedShopId":"00000000-0000-0000-0000-000000000001",
             "Photos":[{"FileName":"menu.jpg","ContentType":"image/jpeg","StorageKey":"menu/1","SizeBytes":10}],
             "RequestedByUserId":null}
            """;

        var message = JsonSerializer.Deserialize<ParseMenuRequestedEvent>(legacyJson);

        message.Should().NotBeNull();
        message!.JobId.Should().BeNull();
        message.MenuRevision.Should().BeNull();
        message.PhotoSetHash.Should().BeNull();
        message.Photos.Should().ContainSingle();
        typeof(ParseMenuRequestedEvent).FullName.Should().Be("CoffeePeek.Contract.Events.Menu.ParseMenuRequestedEvent");
    }
}
