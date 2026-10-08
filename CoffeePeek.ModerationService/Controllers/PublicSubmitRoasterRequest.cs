using System.ComponentModel.DataAnnotations;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Domain;

namespace CoffeePeek.ModerationService.Controllers;

public sealed record PublicSubmitRoasterRequest
{
    [Required, MaxLength(100)] public string Name { get; init; } = string.Empty;
    [MaxLength(BusinessConstants.MaxRoasterAboutLength)] public string? About { get; init; }
    [MaxLength(100)] public string? City { get; init; }
    public string? Address { get; init; }
    public string? InstagramLink { get; init; }
    public string? SiteLink { get; init; }
    public List<UploadedPhotoDto>? Photos { get; init; }
}
