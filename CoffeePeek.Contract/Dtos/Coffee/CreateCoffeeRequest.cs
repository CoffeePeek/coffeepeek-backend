using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed record CreateCoffeeRequest(Guid RoasterId, CoffeeContentDto Content, string[] CountryCodes);
