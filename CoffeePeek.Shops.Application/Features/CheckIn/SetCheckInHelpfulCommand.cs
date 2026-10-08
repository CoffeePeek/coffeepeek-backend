using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public record SetCheckInHelpfulCommand(Guid Id, Guid UserId, bool IsHelpful);
