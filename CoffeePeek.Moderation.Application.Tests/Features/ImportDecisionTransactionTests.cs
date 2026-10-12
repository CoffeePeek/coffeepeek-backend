using CoffeePeek.Moderation.Application.Features.Import.DecideImportDuplicate;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Moderation.Application.Tests.Features;

public class ImportDecisionTransactionTests
{
    [Fact]
    public async Task AlreadyDecidedSuggestion_DoesNotLoadOrMutateCandidates()
    {
        var suggestion = ShopImportDuplicateSuggestion.Create(Guid.NewGuid(), Guid.NewGuid(), new(90, 1, []));
        suggestion.Reject(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var suggestions = new Mock<IShopImportDuplicateSuggestionRepository>();
        suggestions.Setup(x => x.GetByIdAsync(suggestion.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suggestion);
        var candidates = new Mock<IShopImportCandidateRepository>(MockBehavior.Strict);
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);

        var (response, outbound) = await DecideImportDuplicateHandler.Handle(
            new(suggestion.Id, true, Guid.NewGuid()), suggestions.Object, candidates.Object, uow.Object,
            TestContext.Current.CancellationToken);

        response.IsSuccess.Should().BeFalse();
        outbound.Should().BeNull();
    }

    [Fact]
    public async Task LateDomainFailure_PropagatesToWolverineForRollback()
    {
        var left = Candidate("node/1");
        var right = Candidate("node/2");
        var suggestion = ShopImportDuplicateSuggestion.Create(left.Id, right.Id, new(90, 1, []));
        var suggestions = new Mock<IShopImportDuplicateSuggestionRepository>();
        suggestions.Setup(x => x.GetByIdAsync(suggestion.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suggestion);
        var candidates = new Mock<IShopImportCandidateRepository>();
        candidates.Setup(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback(() => suggestion.Reject(Guid.NewGuid(), DateTimeOffset.UtcNow))
            .ReturnsAsync(new Dictionary<Guid, ShopImportCandidate> { [left.Id] = left, [right.Id] = right });
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);

        Func<Task> decide = () => DecideImportDuplicateHandler.Handle(new(suggestion.Id, true, Guid.NewGuid()),
            suggestions.Object, candidates.Object, uow.Object, TestContext.Current.CancellationToken);

        await decide.Should().ThrowAsync<DomainException>();
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ShopImportCandidate Candidate(string externalId) => ShopImportCandidate.FromOsm(
        new(externalId, "Cafe", "Address", 53, 27, null, null, null, null, null, null, null, null,
            new Dictionary<string, string> { ["amenity"] = "cafe" }), DateTimeOffset.UtcNow);
}
