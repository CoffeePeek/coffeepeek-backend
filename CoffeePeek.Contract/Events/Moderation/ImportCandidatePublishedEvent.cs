namespace CoffeePeek.Contract.Events.Moderation;

public record ImportCandidatePublishedEvent(IReadOnlyList<ImportCandidatePublishedItem> Items);
