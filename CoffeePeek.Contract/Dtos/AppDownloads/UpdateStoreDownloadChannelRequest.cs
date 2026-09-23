#nullable enable

using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Contract.Dtos.AppDownloads;

public record UpdateStoreDownloadChannelRequest(
    [MaxLength(2048)] string? Url,
    bool Enabled);
