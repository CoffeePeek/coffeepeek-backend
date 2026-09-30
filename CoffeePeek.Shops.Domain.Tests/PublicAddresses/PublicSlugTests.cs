using CoffeePeek.Shared.Domain.PublicAddresses;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.PublicAddresses;

public sealed class PublicSlugTests
{
    [Theory]
    [InlineData("Зёрна на Октябрьской", "zyorna-na-oktyabrskoy")]
    [InlineData("Кавярня Ў Ірыны", "kavyarnya-u-iryny")]
    [InlineData("Café — Straße & Ærø", "cafe-strasse-aero")]
    [InlineData("  Surf  Coffee!!! ", "surf-coffee")]
    [InlineData("Объект", "obekt")]
    [InlineData("☕", "place")]
    [InlineData("Admin", "place")]
    [InlineData("", "place")]
    // City/zone name regressions (RU and BY spellings); generation rules must not drift.
    [InlineData("Минск", "minsk")]
    [InlineData("Мінск", "minsk")]
    [InlineData("Гродно", "grodno")]
    [InlineData("Гродна", "grodna")]
    [InlineData("Могилёв", "mogilyov")]
    [InlineData("Магілёў", "magilyou")]
    [InlineData("Витебск", "vitebsk")]
    [InlineData("Віцебск", "vitsebsk")]
    [InlineData("Брест", "brest")]
    [InlineData("Брэст", "brest")]
    [InlineData("Верхний город", "verkhniy-gorod")]
    [InlineData("New", "place")]
    [InlineData("Search", "place")]
    [InlineData("Me", "place")]
    public void Generate_ProducesCanonicalSuggestion(string input, string expected) => PublicSlug.Generate(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Admin")]
    [InlineData("admin")]
    [InlineData("by-slug")]
    [InlineData("foo--bar")]
    [InlineData("-foo")]
    [InlineData("foo-")]
    [InlineData("foo/bar")]
    [InlineData("foo%2fbar")]
    [InlineData("café")]
    [InlineData("foo\n")]
    [InlineData("491e394e-d443-4383-8170-1ff039eaa821")]
    [InlineData("491e394ed443438381701ff039eaa821")]
    public void ManualValidation_RejectsNonCanonicalAndReservedInput(string? input) => PublicSlug.IsValid(input).Should().BeFalse();

    [Fact]
    public void Generation_NormalizesCombiningAccentsAndCyrillicYo()
    {
        PublicSlug.Generate("Cafe\u0301").Should().Be("cafe");
        PublicSlug.Generate("е\u0308").Should().Be("yo");
    }

    [Fact]
    public void Candidates_AreBoundedDeterministicAndOwnerSpecific()
    {
        var owner = Guid.Parse("491e394e-d443-4383-8170-1ff039eaa821");
        var name = new string('a', 99) + " - coffee";
        var initial = PublicSlug.Candidate(name, owner, 0);
        initial.Should().HaveLength(99);
        var first = PublicSlug.Candidate(name, owner, 1);
        first.Should().HaveLength(100).And.EndWith("-e98ba429");
        PublicSlug.Candidate("coffee", owner, 2).Should().Be("coffee-e98ba42921bb");
        PublicSlug.Candidate("coffee", owner, 3).Should().Be("coffee-e98ba42921bb6bc2");
        first.Should().Be(PublicSlug.Candidate(name, owner, 1));
        first.Should().NotBe(PublicSlug.Candidate(name, Guid.NewGuid(), 1));
        var retry = PublicSlug.Candidate(name, owner, int.MaxValue);
        retry.Should().HaveLength(100);
        PublicSlug.IsValid(retry).Should().BeTrue();
        retry.Should().NotContain(owner.ToString("N"));
        first.Should().NotContain(owner.ToString("N")[..8]);
        PublicSlug.IsValid(new string('a', 101)).Should().BeFalse();
    }
}
