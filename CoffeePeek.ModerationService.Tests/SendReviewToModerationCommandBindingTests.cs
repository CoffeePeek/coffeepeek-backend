using System.Text.Json;
using CoffeePeek.Moderation.Application.Features.Review.SendReviewToModeration;
using FluentAssertions;

namespace CoffeePeek.ModerationService.Tests;

// Regression test for a production 400 VALIDATION_FAILED on POST /api/ModerationReviews: the
// live client posts a flat body (ratingCoffee/ratingPlace/ratingService as top-level int fields)
// but SendReviewToModerationCommand used to declare a required nested RatingDto Rating
// constructor parameter, so ASP.NET Core's implicit-required-on-non-nullable-reference-type
// model binding rejected the request before the handler ever ran. These tests prove both the
// flat (live client) and legacy nested shapes deserialize and validate correctly through
// EffectiveRating.
public class SendReviewToModerationCommandBindingTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task FlatRatingFields_Deserialize_And_Validate_Successfully()
    {
        const string json = """
        {
            "shopId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
            "header": "Great coffee",
            "comment": "Loved the atmosphere and the espresso here.",
            "ratingCoffee": 3,
            "ratingPlace": 4,
            "ratingService": 3,
            "visitedAt": "2026-08-01T10:00:00Z"
        }
        """;

        var command = JsonSerializer.Deserialize<SendReviewToModerationCommand>(json, WebJsonOptions);
        command.Should().NotBeNull();

        command!.EffectiveRating.Coffee.Should().Be(3);
        command.EffectiveRating.Place.Should().Be(4);
        command.EffectiveRating.Service.Should().Be(3);

        command = command with { UserId = Guid.NewGuid(), UserName = "test-user" };

        var validationStrategy = new SendReviewToModerationValidationStrategy();
        var result = await validationStrategy.ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task NestedRatingObject_StillDeserializes_And_Validates_Successfully()
    {
        const string json = """
        {
            "shopId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
            "header": "Great coffee",
            "comment": "Loved the atmosphere and the espresso here.",
            "rating": {
                "coffee": 3,
                "place": 4,
                "service": 3
            }
        }
        """;

        var command = JsonSerializer.Deserialize<SendReviewToModerationCommand>(json, WebJsonOptions);
        command.Should().NotBeNull();

        command!.EffectiveRating.Coffee.Should().Be(3);
        command.EffectiveRating.Place.Should().Be(4);
        command.EffectiveRating.Service.Should().Be(3);

        command = command with { UserId = Guid.NewGuid(), UserName = "test-user" };

        var validationStrategy = new SendReviewToModerationValidationStrategy();
        var result = await validationStrategy.ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task FlatRatingFields_OutOfRange_FailsValidation()
    {
        const string json = """
        {
            "shopId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
            "header": "Great coffee",
            "comment": "Loved the atmosphere and the espresso here.",
            "ratingCoffee": 0,
            "ratingPlace": 4,
            "ratingService": 3
        }
        """;

        var command = JsonSerializer.Deserialize<SendReviewToModerationCommand>(json, WebJsonOptions);
        command.Should().NotBeNull();

        command = command! with { UserId = Guid.NewGuid(), UserName = "test-user" };

        var validationStrategy = new SendReviewToModerationValidationStrategy();
        var result = await validationStrategy.ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }
}
