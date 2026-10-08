using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed record CreateRoasterTagRequest(string Slug, string Name, string? Description, int SortOrder = 0);
