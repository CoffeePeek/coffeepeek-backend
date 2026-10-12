namespace CoffeePeek.Moderation.Domain.Import;

public readonly record struct ImportGaps(
    bool Instagram,
    bool Phone,
    bool Website,
    bool Hours,
    bool Photo);
