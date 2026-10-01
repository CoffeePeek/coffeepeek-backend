using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.AppDownloads;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.AppDownloads;

public class AppVersionHandlersTests
{
    private const string PlayUrl = "https://play.google.com/store/apps/details?id=by.coffeepeek.app";
    private const string AppStoreUrl = "https://apps.apple.com/app/coffeepeek/id123456789";
    private const string ApkUrl = "https://cdn.coffeepeek.by/android/coffeepeek-1.2.0.apk";

    private readonly Mock<IAppDistributionRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly AppDistributionSettings _settings = AppDistributionSettings.CreateDefault();
    private readonly CancellationToken _ct = CancellationToken.None;

    public AppVersionHandlersTests()
    {
        _repo.Setup(r => r.GetOrCreateSettingsAsync(_ct)).ReturnsAsync(_settings);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("windows", null)]
    [InlineData("android", "huawei")]
    [InlineData("ios", "apk")]
    public async Task GetVersion_UnknownPlatformOrChannel_ReturnsBadRequest(string platform, string channel)
    {
        var result = await GetAppVersionHandler.Handle(new GetAppVersionQuery(platform, channel), _repo.Object, _ct);

        result.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetVersion_NotConfigured_ReturnsNotFound()
    {
        _settings.UpdateGooglePlay(PlayUrl, true);

        var result = await GetAppVersionHandler.Handle(new GetAppVersionQuery("android"), _repo.Object, _ct);

        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetVersion_AndroidWithPlayAndApk_PrefersPlay()
    {
        _settings.UpdateGooglePlay(PlayUrl, true);
        _settings.SetApkEnabled(true);
        _settings.UpdateAndroidVersionPolicy(120, 100);
        _repo.Setup(r => r.GetActiveAndroidReleaseAsync(_ct)).ReturnsAsync(CreateRelease(120));

        var result = await GetAppVersionHandler.Handle(new GetAppVersionQuery("android"), _repo.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(new AppVersionDto(120, 100, PlayUrl));
    }

    [Fact]
    public async Task GetVersion_ApkChannel_ReportsActiveReleaseEvenWhenPlayIsOn()
    {
        _settings.UpdateGooglePlay(PlayUrl, true);
        _settings.SetApkEnabled(true);
        _settings.UpdateAndroidVersionPolicy(120, 100);
        _repo.Setup(r => r.GetActiveAndroidReleaseAsync(_ct)).ReturnsAsync(CreateRelease(140));

        var result = await GetAppVersionHandler.Handle(new GetAppVersionQuery("android", "APK"), _repo.Object, _ct);

        result.Data.Should().Be(new AppVersionDto(140, 100, ApkUrl));
    }

    [Fact]
    public async Task GetVersion_ApkOnlyWithoutChannel_FallsBackToApk()
    {
        _settings.SetApkEnabled(true);
        _repo.Setup(r => r.GetActiveAndroidReleaseAsync(_ct)).ReturnsAsync(CreateRelease(140));

        var result = await GetAppVersionHandler.Handle(new GetAppVersionQuery("android"), _repo.Object, _ct);

        result.Data.Should().Be(new AppVersionDto(140, 0, ApkUrl));
    }

    [Fact]
    public async Task GetVersion_MinAboveDownloadableBuild_IsCappedToAvoidUpdateLoop()
    {
        _settings.SetApkEnabled(true);
        _settings.UpdateAndroidVersionPolicy(150, 150);
        _repo.Setup(r => r.GetActiveAndroidReleaseAsync(_ct)).ReturnsAsync(CreateRelease(140));

        var result = await GetAppVersionHandler.Handle(new GetAppVersionQuery("android", "apk"), _repo.Object, _ct);

        result.Data.MinVersionCode.Should().Be(140);
    }

    [Fact]
    public async Task GetVersion_PlayChannelWhenPlayDisabled_ReturnsNotFound()
    {
        _settings.SetApkEnabled(true);
        _settings.UpdateAndroidVersionPolicy(120, 100);
        _repo.Setup(r => r.GetActiveAndroidReleaseAsync(_ct)).ReturnsAsync(CreateRelease(140));

        var result = await GetAppVersionHandler.Handle(new GetAppVersionQuery("android", "play"), _repo.Object, _ct);

        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetVersion_Ios_IsCaseInsensitiveAndReturnsAppStore()
    {
        _settings.UpdateAppStore(AppStoreUrl, true);
        _settings.UpdateIosVersionPolicy(30, 25);

        var result = await GetAppVersionHandler.Handle(new GetAppVersionQuery(" IOS "), _repo.Object, _ct);

        result.Data.Should().Be(new AppVersionDto(30, 25, AppStoreUrl));
    }

    [Fact]
    public async Task UpdatePolicy_Ios_SavesAndAudits()
    {
        var result = await UpdateAppVersionPolicyHandler.Handle(
            new UpdateAppVersionPolicyCommand(Guid.NewGuid(), "ios", 30, 25), _repo.Object, _uow.Object, _ct);

        result.Data.IosVersion.Should().Be(new AppVersionPolicyDto(30, 25));
        _repo.Verify(r => r.AddAuditLog(It.IsAny<AppDistributionAuditLog>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task UpdatePolicy_UnknownPlatform_ReturnsBadRequestWithoutSaving()
    {
        var result = await UpdateAppVersionPolicyHandler.Handle(
            new UpdateAppVersionPolicyCommand(Guid.NewGuid(), "windows", 30, 25), _repo.Object, _uow.Object, _ct);

        result.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AndroidRelease CreateRelease(int versionCode) => AndroidRelease.Create(
        "1.2.0",
        versionCode,
        ApkUrl,
        "coffeepeek-1.2.0.apk",
        10_000,
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        DateTime.UtcNow);
}
