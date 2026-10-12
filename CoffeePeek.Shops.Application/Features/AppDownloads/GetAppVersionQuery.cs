namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public record GetAppVersionQuery(string? Platform, string? Channel = null);
