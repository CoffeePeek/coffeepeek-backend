using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Services;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Tests;

public sealed class CheckInPhotoReadStartupTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealHostStartupValidationRequiresASeparateReaderBeforeAnyStorageScopeIsResolved(bool sharedIdentity)
    {
        const string administrativeIdentity = "private-admin-identity-never-echo";
        const string secret = "private-storage-password-never-echo";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production", ApplicationName = typeof(InfrastructureExtensions).Assembly.GetName().Name
        });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sentry:Dsn"] = "",
            ["PostgresCpOptions:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=options_test;Username=test;Password=test;Timeout=1",
            ["RabbitMqOptions:HostName"] = "127.0.0.1", ["RabbitMqOptions:Port"] = "1",
            ["RabbitMqOptions:Username"] = "guest", ["RabbitMqOptions:Password"] = "guest",
            ["GatewayAuth:SecretKey"] = "options-test-gateway-secret-at-least-32-characters",
            ["MediaPublicUrlOptions:PublicEndpoint"] = "http://127.0.0.1:9000",
            ["MediaPublicUrlOptions:ShopBucketName"] = "coffeepeek.shops",
            ["MediaPublicUrlOptions:AvatarBucketName"] = "coffeepeek.avatars",
            ["MinIOOptions:Endpoint"] = "http://127.0.0.1:9000",
            ["MinIOOptions:AccessKey"] = administrativeIdentity, ["MinIOOptions:SecretKey"] = secret,
            ["MinIOOptions:ShopBucketName"] = "coffeepeek.shops", ["MinIOOptions:UserBucketName"] = "coffeepeek.avatars",
            ["CheckInPhotoReadOptions:AccessKey"] = sharedIdentity ? administrativeIdentity : "dedicated-photo-reader",
            ["CheckInPhotoReadOptions:SecretKey"] = "private-reader-password-never-echo"
        });
        builder.AddApplication();
        await using var app = builder.Build();
        var startup = app.Services.GetRequiredService<IStartupValidator>();
        if (sharedIdentity)
        {
            var error = Assert.Throws<OptionsValidationException>(startup.Validate);
            Assert.Contains("separate read-only storage identity", error.Message);
            Assert.DoesNotContain(administrativeIdentity, error.ToString());
            Assert.DoesNotContain(secret, error.ToString());
        }
        else
        {
            startup.Validate();
            using var first = app.Services.CreateScope();
            using var second = app.Services.CreateScope();
            var reader = first.ServiceProvider.GetRequiredService<ICheckInPhotoReader>();
            Assert.Same(reader, first.ServiceProvider.GetRequiredService<ICheckInPhotoReader>());
            Assert.NotSame(reader, second.ServiceProvider.GetRequiredService<ICheckInPhotoReader>());
        }
    }
}
