using System.Text.Json.Serialization;

namespace CoffeePeek.Shops.Application.Features.Review.SetReviewHelpful;

public record SetReviewHelpfulCommand(
    Guid ReviewId,
    bool IsHelpful,
    [property: JsonIgnore] Guid UserId);
