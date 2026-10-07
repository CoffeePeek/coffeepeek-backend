namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>Download redirects for one channel: all-time and last 30 days.</summary>
public record AdminDownloadChannelDto(string Channel, int Total, int Last30Days);
