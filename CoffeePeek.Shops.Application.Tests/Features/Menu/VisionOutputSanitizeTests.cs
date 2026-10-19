using System;
using System.Linq;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Menu.ParseMenuPhotos;
using FluentAssertions;

namespace CoffeePeek.Shops.Application.Tests.Features.Menu;

public class VisionOutputSanitizeTests
{
    [Fact]
    public void AbsurdModelValues_DropEntireOffer()
    {
        // Text in a photo can steer the model; values must be bounded before they reach pricing and the DB.
        var line = new VisionDrinkLine(new string('x', 500), 99_999_999m, 7.5);

        ParseMenuPhotosHandler.Sanitize([line]).Should().BeEmpty();
    }

    [Fact]
    public void LineCount_IsCapped() =>
        ParseMenuPhotosHandler.Sanitize(Enumerable.Range(0, 1000).Select(i => new VisionDrinkLine($"d{i}", 5, 0.9)))
            .Should().HaveCount(200);
}
