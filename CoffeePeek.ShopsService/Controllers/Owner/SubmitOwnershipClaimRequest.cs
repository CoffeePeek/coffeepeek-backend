using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;

namespace CoffeePeek.ShopsService.Controllers.Owner;

public sealed record SubmitOwnershipClaimRequest(
    [Required] BusinessType? BusinessType,
    [Required, StringLength(100)] string BusinessSlug,
    [Required, StringLength(320)] string Contact,
    [Required, StringLength(4000)] string Evidence, Guid[]? EvidencePhotoIds = null);
