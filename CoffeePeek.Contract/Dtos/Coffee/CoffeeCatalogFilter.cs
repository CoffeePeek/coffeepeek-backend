using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed class CoffeeCatalogFilter
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? RoasterSlug { get; set; }
    public string? Search { get; set; }
    public string[] Countries { get; set; } = [];
    public string? ProductKind { get; set; }
    public string? Processing { get; set; }
    public string? Brew { get; set; }
    public int? WeightGrams { get; set; }
    public string? Currency { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool AvailableOnly { get; set; } = true;
}
