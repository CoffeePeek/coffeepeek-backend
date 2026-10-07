using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Complaint text only; identity comes from authentication and the route.</summary>
public record SubmitReviewReportRequest
{
    [Required, MaxLength(2000)]
    public string Text { get; init; } = null!;
}
