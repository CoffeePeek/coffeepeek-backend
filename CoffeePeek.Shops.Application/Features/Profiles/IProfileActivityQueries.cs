using CoffeePeek.Contract.Dtos.ProfileActivity;

namespace CoffeePeek.Shops.Application.Features.Profiles;

public interface IProfileActivityQueries
{
    Task<ProfileActivityDto> GetAsync(Guid authorId, ProfileActivityMonth month, CancellationToken ct = default);
}
