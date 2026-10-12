namespace CoffeePeek.Moderation.Domain.Aggregates.MenuDraftAggregate;

public enum MenuDraftParseStatus
{
    None = 0,
    Pending = 1,
    Running = 2,
    Ready = 3,
    Failed = 4
}
