namespace CoffeePeek.Moderation.Application.Features.Import.PatchImportContacts;

public record PatchImportContactsCommand(
    Guid Id,
    string? Instagram,
    string? Phone,
    string? Website,
    string? OpeningHours);
