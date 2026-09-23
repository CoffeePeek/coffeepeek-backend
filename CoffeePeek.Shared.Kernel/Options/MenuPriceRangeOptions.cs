namespace CoffeePeek.Shared.Kernel.Options;

public class MenuPriceRangeOptions
{
    public string Currency { get; set; } = "BYN";

    public decimal CheapBelow { get; set; } = 7.00m;

    public decimal ExpensiveAbove { get; set; } = 9.00m;
}
