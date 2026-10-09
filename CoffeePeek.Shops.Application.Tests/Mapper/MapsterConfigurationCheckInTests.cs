using System;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using FluentAssertions;
using Mapster;

namespace CoffeePeek.Shops.Application.Tests.Mapper;

public class MapsterConfigurationCheckInTests
{
    [Fact]
    public void Adapt_CheckInToCheckInDto_MapsCreatedAtFromCreatedAtUtc()
    {
        var config = MapsterConfiguration.CreateConfig(new MediaPublicUrlOptions());

        var checkIn = CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author", "visit", Rating.Create(5,4,3), DateTime.UtcNow.AddHours(-1));

        var dto = CoffeePeek.Shops.Application.Features.CheckIn.CheckInMapping.ToDto(checkIn);

        dto.VisitedAt.Should().Be(checkIn.VisitedAt);
        dto.VisitedAt.Should().NotBe(checkIn.CreatedAtUtc);
        dto.CreatedAtUtc.Should().Be(checkIn.CreatedAtUtc);
        dto.CreatedAtUtc.Should().NotBe(default(DateTime));
    }

    [Fact]
    public void Adapt_CheckInToCheckInDto_MapsPhotosWithFullUrl()
    {
        var mediaOptions = new MediaPublicUrlOptions { PublicEndpoint = "https://media.coffeepeek.by" };
        var config = MapsterConfiguration.CreateConfig(mediaOptions);

        var checkIn = CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author", "visit", Rating.Create(5,4,3), DateTime.UtcNow.AddHours(-1));
        checkIn.AddPhotos([new CheckInPhoto("photo.jpg", "image/jpeg", "checkins/photo.jpg", 1024, checkIn.UserId)]);

        var dto = CoffeePeek.Shops.Application.Features.CheckIn.CheckInMapping.ToDto(checkIn);

        dto.Photos.Should().HaveCount(1);
        dto.Photos[0].StorageKey.Should().Be("checkins/photo.jpg");
        dto.Photos[0].Url.Should().Be($"/api/v1/check-ins/{checkIn.Id}/photos/{dto.Photos[0].Id}");
    }

    [Fact]
    public void Adapt_CheckInToCheckInDto_MapsRating()
    {
        var config = MapsterConfiguration.CreateConfig(new MediaPublicUrlOptions());

        var checkIn = CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author", "visit", Rating.Create(5,4,3), DateTime.UtcNow.AddHours(-1));

        var dto = CoffeePeek.Shops.Application.Features.CheckIn.CheckInMapping.ToDto(checkIn);

        dto.Rating.Place.Should().Be(5);
        dto.Rating.Service.Should().Be(4);
        dto.Rating.Coffee.Should().Be(3);
    }
}
