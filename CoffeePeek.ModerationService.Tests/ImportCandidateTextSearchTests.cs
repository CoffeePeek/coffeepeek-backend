using CoffeeShop.Moderation.Persistence.Repositories;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests.Import;

public class ImportCandidateTextSearchTests
{
    [Fact]
    public void Matches_Lavazza_DoesNotIncludeCoffeeEmbassy()
    {
        ImportCandidateTextSearch.MatchesNameOrAddress(
                "lavazza",
                "Coffee Embassy",
                "праспект Міру, 1, Мінск")
            .Should().BeFalse();
    }

    [Fact]
    public void Matches_Lavazza_IsCaseInsensitiveOnName()
    {
        ImportCandidateTextSearch.MatchesNameOrAddress("lavazza", "LavAzza", "Минск")
            .Should().BeTrue();
    }

    [Fact]
    public void Matches_Lavazza_InAddress()
    {
        ImportCandidateTextSearch.MatchesNameOrAddress(
                "lavazza",
                "Галерея кофе с Lavazza",
                "Скрыганова 14, Минск")
            .Should().BeTrue();
    }

    [Fact]
    public void Matches_DoesNotUseExternalIdOrBrand()
    {
        ImportCandidateTextSearch.MatchesNameOrAddress("node/9683021090", "Coffee Embassy", "праспект Міру, 1")
            .Should().BeFalse();
    }

    [Fact]
    public void ToILikeContainsPattern_EscapesWildcards()
    {
        ImportCandidateTextSearch.ToILikeContainsPattern("100%").Should().Be("%100\\%%");
        ImportCandidateTextSearch.ToILikeContainsPattern("a_b").Should().Be("%a\\_b%");
    }
}
