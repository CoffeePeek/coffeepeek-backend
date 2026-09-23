using System;
using System.Linq;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Menu.ParseMenuPhotos;
using FluentAssertions;

namespace CoffeePeek.Shops.Application.Tests.Features.Menu;

public class VisionOutputSanitizeTests
{
    [Fact]
    public void AbsurdModelValues_AreDroppedOrClamped()
    {
        // Text in a photo can steer the model; values must be bounded before they reach pricing and the DB.
        var line = new VisionDrinkLine(new string('x', 500), 99_999_999m, -5, 7.5);

        var clean = ParseMenuPhotosHandler.Sanitize([line]).Single();

        clean.RawName.Should().HaveLength(100);
        clean.Price.Should().BeNull();
        clean.VolumeMl.Should().BeNull();
        clean.Confidence.Should().Be(1);
    }

    [Fact]
    public void LineCount_IsCapped() =>
        ParseMenuPhotosHandler.Sanitize(Enumerable.Range(0, 1000).Select(i => new VisionDrinkLine($"d{i}", 5, 200, 0.9)))
            .Should().HaveCount(200);
}
