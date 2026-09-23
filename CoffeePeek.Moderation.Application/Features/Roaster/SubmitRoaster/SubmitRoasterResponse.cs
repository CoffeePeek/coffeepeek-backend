namespace CoffeePeek.Moderation.Application.Features.Roaster.SubmitRoaster;

public record SubmitRoasterResponse(Guid RoasterId, string Status, bool IsAddressValidated);
