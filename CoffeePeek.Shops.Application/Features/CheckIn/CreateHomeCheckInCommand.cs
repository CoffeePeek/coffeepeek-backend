using CoffeePeek.Contract.Dtos.CheckIns;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public sealed record CreateHomeCheckInCommand(CreateHomeCheckInRequest Request, Guid UserId, string Username);
