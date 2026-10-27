namespace CoffeePeek.Account.Persistence.Leaderboards;

internal sealed class LeaderboardTotal
{
    public Guid UserId { get; set; }
    public int Points { get; set; }
    public int Additions { get; set; }
    public int Edits { get; set; }
    public int CheckIns { get; set; }
}
