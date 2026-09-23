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

        var checkIn = CheckIn.Create(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));

        var dto = checkIn.Adapt<CheckInDto>(config);

        dto.VisitedAt.Should().Be(checkIn.VisitedAt);
        dto.VisitedAt.Should().NotBe(checkIn.CreatedAtUtc);
        dto.CreatedAt.Should().Be(checkIn.CreatedAtUtc);
        dto.CreatedAt.Should().NotBe(default(DateTime));
    }

    [Fact]
    public void Adapt_CheckInToCheckInDto_MapsPhotosWithFullUrl()
    {
        var mediaOptions = new MediaPublicUrlOptions { PublicEndpoint = "https://media.coffeepeek.by" };
        var config = MapsterConfiguration.CreateConfig(mediaOptions);

        var checkIn = CheckIn.Create(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));
        checkIn.AddPhotos([new ShopPhoto("photo.jpg", "image/jpeg", "checkins/photo.jpg", 1024, checkIn.UserId)]);

        var dto = checkIn.Adapt<CheckInDto>(config);

        dto.Photos.Should().HaveCount(1);
        dto.Photos[0].StorageKey.Should().Be("checkins/photo.jpg");
        dto.Photos[0].FullUrl.Should().Be(
            $"https://media.coffeepeek.by/{mediaOptions.ShopBucketName}/checkins/photo.jpg");
    }

    [Fact]
    public void Adapt_CheckInToCheckInDto_MapsRating()
    {
        var config = MapsterConfiguration.CreateConfig(new MediaPublicUrlOptions());

        var checkIn = CheckIn.Create(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));
        checkIn.AssignRating(place: 5, service: 4, coffee: 3);

        var dto = checkIn.Adapt<CheckInDto>(config);

        dto.Rating.Place.Should().Be(5);
        dto.Rating.Service.Should().Be(4);
        dto.Rating.Coffee.Should().Be(3);
    }
}
