namespace CoffeePeek.Moderation.Application.Features.Import.DecideImportDuplicate;

public record DecideImportDuplicateCommand(Guid Id, bool Accept, Guid ReviewerUserId);
