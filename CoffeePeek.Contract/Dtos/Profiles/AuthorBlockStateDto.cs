namespace CoffeePeek.Contract.Dtos.Profiles;

public sealed record AuthorBlockStateDto(Guid AuthorId, bool IsBlocked, string Revision);
