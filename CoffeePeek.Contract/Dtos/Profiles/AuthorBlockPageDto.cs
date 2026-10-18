namespace CoffeePeek.Contract.Dtos.Profiles;

public sealed record AuthorBlockPageDto(BlockedAuthorDto[] Items, string? NextCursor, string Revision);
