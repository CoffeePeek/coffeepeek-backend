namespace CoffeePeek.Contract.Dtos.Admin;

public record AdminShopCoverageDto(
    int TotalShops,
    decimal? AverageCompletenessPercent,
    int ShopsWithCheckIns,
    int ShopsWithoutCheckIns);
