namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>Moderator audit-log action counts in the last 30 days.</summary>
public record AdminModeratorWorkloadDto(Guid ModeratorUserId, int Total, int Approved, int Rejected);
