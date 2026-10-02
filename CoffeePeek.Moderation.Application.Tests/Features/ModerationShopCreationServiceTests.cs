using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Moderation.Application.Features.Shop.CreateShop;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Moderation.Application.Tests.Features;

public class ModerationShopCreationServiceTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_PreservesContactsAndScheduleSemantics()
    {
        ModerationShop? persistedShop = null;
        var repository = new Mock<IModerationShopRepository>();
        repository
            .Setup(r => r.AddAsync(It.IsAny<ModerationShop>()))
            .Callback<ModerationShop>(shop => persistedShop = shop)
            .Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var command = new SendCoffeeShopToModerationCommand
        {
            Name = "Test Cafe",
            Address = "Test address",
            CityId = Guid.NewGuid(),
            ShopContact = new ShopContactDto
            {
                PhoneNumber = "+375291234567",
                InstagramLink = "https://instagram.com/test-cafe",
                Email = "hello@test.cafe",
                SiteLink = "https://test.cafe"
            },
            Schedules =
            [
                new ScheduleDto(DayOfWeek.Monday, false,
                    [new ShopScheduleIntervalDto { OpenTime = TimeSpan.FromHours(9), CloseTime = TimeSpan.FromHours(18) }]),
                // IsClosed is authoritative even if a client accidentally leaves an interval in the payload.
                new ScheduleDto(DayOfWeek.Tuesday, true,
                    [new ShopScheduleIntervalDto { OpenTime = TimeSpan.FromHours(9), CloseTime = TimeSpan.FromHours(18) }]),
                new ScheduleDto(DayOfWeek.Wednesday, true, null)
            ]
        };

        var service = new ModerationShopCreationService(repository.Object, unitOfWork.Object);
        await service.Create(command, _ct);

        persistedShop.Should().NotBeNull();
        persistedShop!.Location!.Address.Should().Be(command.Address);
        persistedShop.Location.IsAddressValidated.Should().BeFalse();
        persistedShop!.Contact.Should().BeEquivalentTo(command.ShopContact);
        persistedShop.Schedules.Should().HaveCount(3);
        var monday = persistedShop.Schedules.Single(s => s.DayOfWeek == DayOfWeek.Monday);
        monday.IsClosed.Should().BeFalse();
        monday.Intervals.Should().ContainSingle()
            .Which.Should().Be(new ModerationShopScheduleInterval(
                TimeSpan.FromHours(9), TimeSpan.FromHours(18)));
        persistedShop.Schedules.Single(s => s.DayOfWeek == DayOfWeek.Tuesday)
            .Intervals.Should().BeEmpty();
        persistedShop.Schedules.Single(s => s.DayOfWeek == DayOfWeek.Wednesday)
            .Intervals.Should().BeEmpty();
    }
}
