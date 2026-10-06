using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed record StoredCoffeePhotoDto(Guid Id, string StorageKey, string FileName,
    string ContentType, long SizeBytes, string? SourceUrl = null);
