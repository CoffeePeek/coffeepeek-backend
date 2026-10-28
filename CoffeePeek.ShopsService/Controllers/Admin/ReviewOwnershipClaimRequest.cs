using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.ShopsService.Controllers.Admin;

public sealed record ReviewOwnershipClaimRequest([StringLength(2000)] string? Reason = null);
