using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public record CreateUnifiedCheckInCommand(CreateUnifiedCheckInRequest Request, Guid UserId, string Username);
