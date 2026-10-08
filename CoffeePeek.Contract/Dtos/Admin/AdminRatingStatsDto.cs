namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>Latest eligible check-in rating statistics. Averages rounded to 2 decimals; 0 when no eligible scores.</summary>
/// <param name="TotalCheckIns">Number of selected author/shop contributions.</param>
/// <param name="Distribution">Always 5 buckets, Stars 1..5; a contribution's star = its average rating rounded to the nearest integer.</param>
public record AdminRatingStatsDto(
    int TotalCheckIns,
    decimal AverageRating,
    decimal AveragePlace,
    decimal AverageService,
    decimal AverageCoffee,
    IReadOnlyList<AdminRatingBucketDto> Distribution);
