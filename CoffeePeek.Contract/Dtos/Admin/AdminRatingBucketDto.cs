namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>Number of selected check-in scores whose rounded average rating equals <see cref="Stars"/>.</summary>
public record AdminRatingBucketDto(int Stars, int Count);
