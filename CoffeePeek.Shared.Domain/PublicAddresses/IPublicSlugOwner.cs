namespace CoffeePeek.Shared.Domain.PublicAddresses;

/// <summary>The current public URL is stored on its owning entity.</summary>
public interface IPublicSlugOwner
{
    Guid Id { get; }
    string? PublicSlug { get; }
    long SlugRevision { get; }
    void SetPublicSlug(string slug);
}
