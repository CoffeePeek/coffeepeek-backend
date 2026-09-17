using System.Net;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Validation;
using MapsterMapper;

namespace CoffeePeek.Moderation.Application.Features.Review.UpdateCoffeeShopReview;

public static class UpdateCoffeeShopReviewHandler
{
    public static async Task<(Response<UpdateCoffeeShopReviewResponse>, ModerationReviewEditedEvent?)> Handle(
        UpdateCoffeeShopReviewCommand command,
        IValidationStrategy<UpdateCoffeeShopReviewCommand> validationStrategy,
        IModerationReviewRepository reviewRepository,
        IMapper mapper,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var validationResult = validationStrategy.Validate(command);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.ErrorMessage!);

        var review = await reviewRepository.GetById(command.ReviewId, ct);

        if (review == null)
        {
            return (Response<UpdateCoffeeShopReviewResponse>.Error(
                HttpStatusCode.NotFound, "Review not found"), null);
        }

        if (review.UserId != command.UserId)
        {
            return (Response<UpdateCoffeeShopReviewResponse>.Error(
                HttpStatusCode.Forbidden, "You are not authorized to update this review"), null);
        }

        review.UpdateHeader(command.Header);
        review.UpdateComment(command.Comment);
        review.Rating.UpdateRating(
            command.Rating.Place,
            command.Rating.Service,
            command.Rating.Coffee);

        // Editing sends the review back through moderation; the published shops review is
        // hidden by the Shops-side ModerationReviewEditedHandler until a moderator re-approves.
        review.Resubmit(command.UserId);

        await unitOfWork.SaveChangesAsync(ct);

        var dto = mapper.Map<ModerationReviewDto>(review);

        return (Response<UpdateCoffeeShopReviewResponse>.Success(new UpdateCoffeeShopReviewResponse(review.Id)),
            new ModerationReviewEditedEvent(dto));
    }
}