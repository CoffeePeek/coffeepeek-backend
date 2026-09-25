using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CoffeePeek.Shops.Application.Features.CheckIn.GetUserCheckIns;

/// <param name="From">Inclusive lower bound on VisitedAt (UTC), optional.</param>
/// <param name="To">Exclusive upper bound on VisitedAt (UTC), optional.</param>
public record GetUserCheckInsCommand(
    [property: JsonIgnore] Guid UserId,
    [Range(1, int.MaxValue)] int PageNumber,
    [Range(1, 100)] int PageSize,
    DateTime? From = null,
    DateTime? To = null);
