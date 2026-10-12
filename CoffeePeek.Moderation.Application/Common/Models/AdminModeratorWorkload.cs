namespace CoffeePeek.Moderation.Application.Common.Models;

public record AdminModeratorWorkload(Guid ModeratorUserId, int Total, int Approved, int Rejected);
