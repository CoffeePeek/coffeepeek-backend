namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>Count of items for a single UTC calendar day.</summary>
public record AdminDailyCountDto(DateOnly Date, int Count);
