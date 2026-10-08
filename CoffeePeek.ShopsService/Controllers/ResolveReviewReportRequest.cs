using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Explicit admin decision; omitted or null values are invalid.</summary>
public record ResolveReviewReportRequest
{
    /// <summary>True to remove the check-in, false to dismiss the complaint.</summary>
    [Required]
    public bool? DeleteCheckIn { get; init; }
}
