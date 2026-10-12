namespace CoffeePeek.Moderation.Application.Features.Import.RefreshGoogleStatus;

public record RefreshGoogleStatusCommand(Guid Id, bool Force = false);
