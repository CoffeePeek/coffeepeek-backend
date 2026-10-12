using System.Text.Json;
using CoffeePeek.Contract.Enums;
using CoffeePeek.ModerationService.Telegram;

namespace CoffeePeek.ModerationService.Tests;

public class TelegramModerationPollerTests
{
    [Theory]
    [InlineData(123L, 123L, true)]
    [InlineData(999L, 123L, false)]
    [InlineData(123L, 999L, false)]
    public void CallbackAction_RequiresConfiguredSenderAndPrivateChat(long sender, long chat, bool allowed)
    {
        var id = Guid.NewGuid();
        using var callback = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            from = new { id = sender }, message = new { chat = new { id = chat } },
            data = $"a:{id:N}"
        }));

        Assert.Equal(allowed, TelegramModerationPoller.TryParseAction(callback.RootElement, 123,
            out var parsedId, out var decision));
        if (allowed)
        {
            Assert.Equal(id, parsedId);
            Assert.Equal(ModerationStatus.Approved, decision);
        }
    }

    [Fact]
    public void CallbackAction_RejectsForgedPayload()
    {
        using var callback = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            from = new { id = 123 }, message = new { chat = new { id = 123 } },
            data = $"x:{Guid.NewGuid():N}"
        }));

        Assert.False(TelegramModerationPoller.TryParseAction(callback.RootElement, 123, out _, out _));
    }
}
