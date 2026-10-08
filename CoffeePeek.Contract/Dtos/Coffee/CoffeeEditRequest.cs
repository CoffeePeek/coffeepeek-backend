using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed record CoffeeEditRequest(CoffeeContentDto Content, string[] CountryCodes,
    string[] ProtectedFields, string Status, long Version);
