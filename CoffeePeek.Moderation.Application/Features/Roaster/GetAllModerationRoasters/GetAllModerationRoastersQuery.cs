using System.ComponentModel.DataAnnotations;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Moderation.Application.Features.Roaster.GetAllModerationRoasters;

public record GetAllModerationRoastersQuery(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, 100)] int PageSize = 20,
    ModerationStatus? Status = null);
