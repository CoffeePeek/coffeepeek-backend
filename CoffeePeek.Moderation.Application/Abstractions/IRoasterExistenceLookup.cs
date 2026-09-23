namespace CoffeePeek.Moderation.Application.Abstractions;

public interface IRoasterExistenceLookup
{
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
}
