using System.ComponentModel.DataAnnotations;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Domain;

namespace CoffeePeek.ModerationService.Controllers;

public sealed record PublicCreateShopIssueRequest([Required, MaxLength(100)] string Shop, ShopIssueCategory Category,
    [MaxLength(BusinessConstants.MaxShopIssueReportDescriptionLength)] string? Description);
