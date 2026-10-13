#nullable enable

namespace CoffeePeek.Contract.Dtos.CheckIns;

public sealed record HomeMethodStatsDto(string? BrewMethodCode, int BrewCount,
    int DosedBrewCount, decimal TotalKnownDoseGrams, int RatedBrewCount, decimal? AverageTasteScore);
