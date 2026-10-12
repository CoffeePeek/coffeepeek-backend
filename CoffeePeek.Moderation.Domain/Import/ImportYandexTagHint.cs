using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;

namespace CoffeePeek.Moderation.Domain.Import;

public readonly record struct ImportYandexTagHint(
    string Label,
    string? Slug,
    ImportCoffeeFocus? Focus);
