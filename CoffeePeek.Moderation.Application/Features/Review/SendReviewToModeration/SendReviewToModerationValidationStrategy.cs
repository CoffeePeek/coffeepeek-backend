using CoffeePeek.Shared.Validation;
using CoffeePeek.Shared.Validation.Review;

namespace CoffeePeek.Moderation.Application.Features.Review.SendReviewToModeration;

public class SendReviewToModerationValidationStrategy : BaseReviewValidationStrategy,
    IAsyncValidationStrategy<SendReviewToModerationCommand>
{
    public Task<ValidationResult> ValidateAsync(SendReviewToModerationCommand command, CancellationToken ct = default)
    {
        var syncResult = ValidateSyncProps(command);
        if (!syncResult.IsValid) 
            return Task.FromResult(syncResult);

        return Task.FromResult(ValidationResult.Valid);
    }
    
    private static ValidationResult ValidateSyncProps(SendReviewToModerationCommand command)
    {
        var validations = new List<ValidationResult>
        {
            ValidateUserId(command.UserId),
            ValidateHeader(command.Header),
            ValidateComment(command.Comment)
        };

        var firstError = validations.FirstOrDefault(v => !v.IsValid);
        if (firstError != null) return firstError;

        if (!IsValidRating(command.EffectiveRating.Coffee)) return ValidationResult.Invalid("RatingCoffee invalid");
        if (!IsValidRating(command.EffectiveRating.Service)) return ValidationResult.Invalid("RatingService invalid");
        if (!IsValidRating(command.EffectiveRating.Place)) return ValidationResult.Invalid("RatingPlace invalid");

        return ValidationResult.Valid;
    }
}