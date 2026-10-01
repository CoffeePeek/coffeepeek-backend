using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.AppDistributionAggregate;

public class AppDistributionTests
{
    private const string ValidGooglePlayUrl = "https://play.google.com/store/apps/details?id=by.coffeepeek.app";
    private const string ValidAppStoreUrl = "https://apps.apple.com/app/coffeepeek/id123456789";
    private const string ValidApkUrl = "https://cdn.coffeepeek.by/android/coffeepeek-1.0.0.apk";
    private const string ValidSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void UpdateGooglePlay_WhenEnabled_RequiresPlayGoogleHost()
    {
        var settings = AppDistributionSettings.CreateDefault();

        var act = () => settings.UpdateGooglePlay("https://example.com/app", true);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void UpdateAppStore_WhenEnabled_RequiresAppsAppleHost()
    {
        var settings = AppDistributionSettings.CreateDefault();

        var act = () => settings.UpdateAppStore("https://apple.com/app", true);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void UpdateStoreChannels_WithValidUrls_EnablesChannels()
    {
        var settings = AppDistributionSettings.CreateDefault();

        settings.UpdateGooglePlay(ValidGooglePlayUrl, true);
        settings.UpdateAppStore(ValidAppStoreUrl, true);

        settings.AndroidGooglePlayEnabled.Should().BeTrue();
        settings.AndroidGooglePlayUrl.Should().Be(ValidGooglePlayUrl);
        settings.IosAppStoreEnabled.Should().BeTrue();
        settings.IosAppStoreUrl.Should().Be(ValidAppStoreUrl);
    }

    [Fact]
    public void CreateAndroidRelease_WithInvalidSha256_Throws()
    {
        var act = () => AndroidRelease.Create(
            "1.0.0",
            100,
            ValidApkUrl,
            "coffeepeek-1.0.0.apk",
            10_000,
            "bad-hash",
            DateTime.UtcNow);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(10, -1)]
    [InlineData(10, 11)]
    public void UpdateVersionPolicy_WithInvalidValues_Throws(int latest, int min)
    {
        var settings = AppDistributionSettings.CreateDefault();

        FluentActions.Invoking(() => settings.UpdateAndroidVersionPolicy(latest, min)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => settings.UpdateIosVersionPolicy(latest, min)).Should().Throw<DomainException>();
    }

    [Fact]
    public void UpdateVersionPolicy_WithValidValues_SetsPlatformIndependently()
    {
        var settings = AppDistributionSettings.CreateDefault();

        settings.UpdateAndroidVersionPolicy(120, 100);
        settings.UpdateIosVersionPolicy(30, 0);

        settings.AndroidLatestVersionCode.Should().Be(120);
        settings.AndroidMinVersionCode.Should().Be(100);
        settings.IosLatestVersionCode.Should().Be(30);
        settings.IosMinVersionCode.Should().Be(0);
    }

    [Fact]
    public void PublishAndUnpublish_ChangesActiveState()
    {
        var release = AndroidRelease.Create(
            "1.0.0",
            100,
            ValidApkUrl,
            "coffeepeek-1.0.0.apk",
            10_000,
            ValidSha256,
            DateTime.UtcNow);

        release.Publish();
        release.IsActive.Should().BeTrue();

        release.Unpublish();
        release.IsActive.Should().BeFalse();
    }
}
