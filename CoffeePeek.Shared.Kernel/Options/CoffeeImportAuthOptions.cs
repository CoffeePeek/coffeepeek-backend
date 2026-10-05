namespace CoffeePeek.Shared.Kernel.Options;

public sealed class CoffeeImportAuthOptions
{
    public const string Header = "X-Coffee-Import-Token";
    // Empty disables both internal import endpoints. Use the same dedicated secret in Media and Shops.
    public string Token { get; set; } = string.Empty;
}
