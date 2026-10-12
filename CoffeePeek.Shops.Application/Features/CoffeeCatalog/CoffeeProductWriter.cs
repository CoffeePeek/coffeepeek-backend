using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public sealed class CoffeeProductWriter(ICoffeeProductStorage storage, IAdminCoffeeCatalogReader reader) : ICoffeeProductWriter
{
    public async Task<AdminCoffeeDto> CreateAsync(CreateCoffeeRequest request, CancellationToken ct)
    {
        if (!await storage.RoasterExistsAsync(request.RoasterId, ct)) throw new CoffeeCatalogException("Roaster not found.", 404);
        await ValidateCountriesAsync(request.CountryCodes, ct);
        var product = new CoffeeProduct(request.RoasterId, CoffeeContentMapper.ToDomain(request.Content));
        product.SetPublicSlug(PublicSlug.Candidate(product.Name, product.Id, 1));
        product.ReplaceCountries(request.CountryCodes);
        storage.Add(product); await storage.FlushAsync(ct);
        return (await reader.AdminGetAsync(product.Id, ct))!;
    }

    public async Task<AdminCoffeeDto?> EditAsync(Guid id, CoffeeEditRequest request, CancellationToken ct)
    {
        var product = await storage.FindAsync(id, ct);
        if (product is null) return null;
        if (request.Version != product.Version) throw new CoffeeCatalogException("Coffee changed; reload before saving.", 409);
        await ValidateCountriesAsync(request.CountryCodes, ct);
        if (request.ProtectedFields is null || !Enum.TryParse<CoffeePublicationStatus>(request.Status, true, out var status) || !Enum.IsDefined(status))
            throw new CoffeeCatalogException("Invalid status or protected fields.");
        product.ApplyManualEdit(CoffeeContentMapper.ToDomain(request.Content), request.CountryCodes, request.ProtectedFields, status);
        await storage.FlushAsync(ct);
        return await reader.AdminGetAsync(id, ct);
    }

    public async Task<bool> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var product = await storage.FindAsync(id, ct);
        if (product is null) return false;
        product.SetStatus(CoffeePublicationStatus.Archived);
        await storage.FlushAsync(ct); return true;
    }

    private async Task ValidateCountriesAsync(string[] codes, CancellationToken ct)
    {
        if (codes is null || codes.Length > 30 ||
            (await storage.ExistingCountriesAsync(codes, ct)).Length != codes.Distinct().Count())
            throw new CoffeeCatalogException("Unknown coffee origin country.");
    }
}
