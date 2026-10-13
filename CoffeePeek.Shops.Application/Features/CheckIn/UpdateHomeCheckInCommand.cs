using CoffeePeek.Contract.Dtos.CheckIns;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public sealed record UpdateHomeCheckInCommand(Guid Id, Guid UserId, UpdateHomeCheckInRequest Request);
