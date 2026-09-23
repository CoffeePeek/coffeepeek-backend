using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Shops.Application.Features.Review.GetReviewsByUserId;

public class GetReviewsByUserIdRequestHandler
{
    public async Task<Response<GetReviewsByUserIdResponse>> Handle(GetReviewsByUserIdQuery request,
        IReviewQueries reviewQueries,
        CancellationToken cancellationToken)
    {
        var (reviewDtos, total) = await reviewQueries.GetReviewsByUserId(
            request.UserId, request.PageNumber, request.PageSize, cancellationToken);

        var response = new GetReviewsByUserIdResponse(
            reviewDtos,
            TotalItems: total,
            TotalPages: (int)Math.Ceiling(total / (double)request.PageSize),
            request.PageNumber,
            request.PageSize);

        return Response<GetReviewsByUserIdResponse>.Success(response);
    }
}