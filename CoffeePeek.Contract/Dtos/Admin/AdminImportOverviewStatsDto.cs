namespace CoffeePeek.Contract.Dtos.Admin;

public record AdminImportOverviewStatsDto(
    int Pending,
    int Published,
    int Rejected,
    int Skipped,
    int InFeed);
