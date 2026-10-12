#nullable enable

namespace CoffeePeek.Contract.Dtos.CheckIns;

public sealed record HomeBeanStatsDto(Guid? CoffeeProductId, string? BeanName, int BrewCount,
    int DosedBrewCount, decimal TotalKnownDoseGrams, int RatedBrewCount, decimal? AverageTasteScore);
