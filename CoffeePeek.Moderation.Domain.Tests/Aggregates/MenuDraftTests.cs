using CoffeePeek.Moderation.Domain.Aggregates.MenuDraftAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests.Aggregates;

public class MenuDraftTests
{
    private static MenuDraft Pending()
    {
        var menu = MenuDraft.CreateEmpty();
        menu.AttachPhotos([("menu.jpg", "image/jpeg", "menus/one", 100)], DateTime.UtcNow);
        return menu;
    }

    [Fact]
    public void ParseResult_AfterManualEdit_DoesNotOverwriteIt()
    {
        var menu = Pending();
        var requestId = menu.ParseRequestId;
        menu.ApplyManualItems([new() { Slug = "latte", Availability = 1, Price = 7 }], DateTime.UtcNow);

        menu.ApplyParseResult(true, null, null, [], [], DateTime.UtcNow, requestId).Should().BeFalse();
        menu.Items.Should().ContainSingle().Which.Price.Should().Be(7);
        menu.ParseStatus.Should().Be((int)MenuDraftParseStatus.Ready);
    }

    [Fact]
    public void ParseResult_OnlyCurrentRequestIsAcceptedOnce()
    {
        var menu = Pending();
        var previous = menu.ParseRequestId;
        menu.MarkParsePending();
        var current = menu.ParseRequestId;

        menu.ApplyParseResult(true, null, null, [], [], DateTime.UtcNow, previous).Should().BeFalse();
        menu.ApplyParseResult(true, null, null, [], [], DateTime.UtcNow, current).Should().BeTrue();
        menu.ApplyParseResult(false, "old error", null, [], [], DateTime.UtcNow, current).Should().BeFalse();
        menu.ParseStatus.Should().Be((int)MenuDraftParseStatus.Ready);
    }

    [Fact]
    public void ManualEdit_NewAbsentItem_ClearsPriceAndMarksManual()
    {
        var menu = MenuDraft.CreateEmpty();
        menu.ApplyManualItems([new() { Slug = "latte", Availability = 2, Price = 7 }], DateTime.UtcNow);

        var item = menu.Items.Should().ContainSingle().Subject;
        item.Price.Should().BeNull();
        item.Source.Should().Be((int)MenuDraftItemSource.Manual);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(7, 99)]
    public void InvalidManualEdit_IsAtomic(int price, int availability)
    {
        var menu = Pending();
        var requestId = menu.ParseRequestId;
        Action edit = () => menu.ApplyManualItems(
            [new() { Slug = "latte", Availability = 1 }, new() { Slug = "espresso", Availability = availability, Price = price }],
            DateTime.UtcNow);

        edit.Should().Throw<DomainException>();
        menu.Items.Should().BeEmpty();
        menu.ParseRequestId.Should().Be(requestId);
    }

    [Fact]
    public void Reparse_PreservesPricesPreviouslyEnteredManually()
    {
        var menu = Pending();
        menu.ApplyManualItems([new() { Slug = "latte", Availability = 1, Price = 9 }], DateTime.UtcNow);
        menu.MarkParsePending();

        menu.ApplyParseResult(true, null, null,
            [new() { Slug = "latte", Availability = 1, Price = 7 }], [], DateTime.UtcNow, menu.ParseRequestId)
            .Should().BeTrue();

        menu.Items.Should().ContainSingle().Which.Price.Should().Be(9);
    }

    [Fact]
    public void ManualEdit_NormalizesSlugBeforeMatchingExistingItem()
    {
        var menu = MenuDraft.CreateEmpty();
        menu.ApplyManualItems([new() { Slug = "latte", Availability = 1, Price = 7 }], DateTime.UtcNow);

        menu.ApplyManualItems([new() { Slug = " LATTE ", Availability = 1, Price = 9 }], DateTime.UtcNow);

        menu.Items.Should().ContainSingle().Which.Price.Should().Be(9);
    }

    [Fact]
    public void TooManyPhotos_RejectsWholeBatch()
    {
        var menu = Pending();
        Action attach = () => menu.AttachPhotos(Enumerable.Range(0, 4)
            .Select(i => ("menu.jpg", "image/jpeg", $"menus/{i}", 100L)).ToArray(), DateTime.UtcNow);

        attach.Should().Throw<DomainException>();
        menu.Photos.Should().ContainSingle();
    }
}
