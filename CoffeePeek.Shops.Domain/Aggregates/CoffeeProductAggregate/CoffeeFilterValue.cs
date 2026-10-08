using System.Text.RegularExpressions;
using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeFilterValue : Entity<Guid>
{
    public static readonly string[] GroupCodes = ["brew", "caffeine", "roast", "acidity", "processing", "fermentation", "taste", "composition"];
    public string GroupCode { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    private CoffeeFilterValue() { }

    public CoffeeFilterValue(string groupCode, string code, string name, string? description, int sortOrder, Guid? id = null)
    {
        if (!GroupCodes.Contains(groupCode, StringComparer.Ordinal) || !IsValidCode(code))
            throw new DomainException("Invalid coffee filter group or code.");
        Id = id ?? Guid.NewGuid();
        GroupCode = groupCode; Code = code;
        Update(name, description, sortOrder, true);
    }

    public static bool IsValidCode(string? code) => code is not null && code.Length is > 0 and <= 100 &&
        Regex.IsMatch(code, "\\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant);

    public void Update(string name, string? description, int sortOrder, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 || description?.Length > 2000)
            throw new DomainException("Invalid coffee filter value name or description.");
        Name = name.Trim(); Description = description?.Trim(); SortOrder = sortOrder; IsActive = isActive;
    }
}
