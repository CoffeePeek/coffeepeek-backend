using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public record UpdateUnifiedCheckInCommand(Guid Id, Guid UserId, UpdateUnifiedCheckInRequest Request);
