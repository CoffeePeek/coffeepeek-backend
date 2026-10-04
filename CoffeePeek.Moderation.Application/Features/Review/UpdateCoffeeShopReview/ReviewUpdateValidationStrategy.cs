using CoffeePeek.Shared.Validation;
using CoffeePeek.Shared.Validation.Review;

namespace CoffeePeek.Moderation.Application.Features.Review.UpdateCoffeeShopReview;

public class ReviewUpdateValidationStrategy : BaseReviewValidationStrategy, IValidationStrategy<UpdateCoffeeShopReviewCommand>
{
    public ValidationResult Validate(UpdateCoffeeShopReviewCommand entity)
    {
        var userIdValidation = ValidateUserId(entity.UserId);
        if (!userIdValidation.IsValid)
        {
            return userIdValidation;
        }

        var headerValidation = ValidateHeader(entity.Header);
        if (!headerValidation.IsValid) return headerValidation;

        var commentValidation = ValidateComment(entity.Comment?.Trim()!);
        if (!commentValidation.IsValid)
        {
            return commentValidation;
        }

        if (entity.Rating is null)
            return ValidationResult.Invalid("Rating is required.");

        if (entity.Photos is not null)
        {
            if (entity.Photos.Count > 20)
                return ValidationResult.Invalid("At most 20 photos are allowed.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var photo in entity.Photos)
            {
                if (photo is null || string.IsNullOrWhiteSpace(photo.StorageKey) || !keys.Add(photo.StorageKey))
                    return ValidationResult.Invalid("Photo storage keys must be nonempty and unique.");
            }
        }

        if (!IsValidRating(entity.Rating.Coffee))
        {
            return ValidationResult.Invalid($"RatingCoffee must be a valid number between {MinRating} and {MaxRating}");
        }

        if (!IsValidRating(entity.Rating.Service))
        {
            return ValidationResult.Invalid($"RatingService must be a valid number between {MinRating} and {MaxRating}");
        }

        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (!IsValidRating(entity.Rating.Place))
        {
            return ValidationResult.Invalid($"RatingPlace must be a valid number between {MinRating} and {MaxRating}");
        }

        return ValidationResult.Valid;
    }
}
