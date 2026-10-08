using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public record ChangeUnifiedCheckInVisibilityCommand(Guid Id, Guid UserId, CheckInVisibility Visibility);
