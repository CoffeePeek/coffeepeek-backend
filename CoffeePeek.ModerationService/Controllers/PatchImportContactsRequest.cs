namespace CoffeePeek.ModerationService.Controllers;

/// <summary>
/// Null / omitted field is left unchanged. Empty string clears the field.
/// </summary>
public record PatchImportContactsRequest(
    string? Instagram = null,
    string? Phone = null,
    string? Website = null,
    string? OpeningHours = null);
