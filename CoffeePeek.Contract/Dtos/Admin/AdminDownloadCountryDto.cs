namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>Download redirects for one country in the last 30 days.</summary>
public record AdminDownloadCountryDto(string Country, int Count);
