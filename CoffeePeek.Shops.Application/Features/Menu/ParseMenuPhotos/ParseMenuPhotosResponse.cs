using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ContractCategory = CoffeePeek.Contract.Enums.CoffeeDrinkCategory;
using ContractPriceRange = CoffeePeek.Contract.Enums.PriceRange;

namespace CoffeePeek.Shops.Application.Features.Menu.ParseMenuPhotos;

public record ParseMenuPhotosResponse(
    bool Success,
    string? Error,
    ContractPriceRange? SuggestedPriceRange,
    IReadOnlyList<ParsedMenuItemDto> Items,
    IReadOnlyList<UnmatchedMenuItemDto> Unmatched);
