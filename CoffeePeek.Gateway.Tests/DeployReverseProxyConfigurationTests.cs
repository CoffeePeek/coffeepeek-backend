using FluentAssertions;
using System.Text.RegularExpressions;

namespace CoffeePeek.Gateway.Tests;

public class DeployReverseProxyConfigurationTests
{
    [Fact]
    public void ProductionCompose_FallsBackToJwtSecretForAccountDeletionPepper()
    {
        var compose = File.ReadAllText(GetDeployPath("docker-compose.yml"));
        var envExample = File.ReadAllText(GetDeployPath(".env.example"));

        compose.Should().Contain("AccountDeletionOptions__TokenPepper: ${ACCOUNT_DELETION_TOKEN_PEPPER:-${JWT_SECRET_KEY}}");
        envExample.Should().Contain("ACCOUNT_DELETION_TOKEN_PEPPER=");
    }

    [Fact]
    public void ProductionCompose_UsesAspNetContainerPortForGatewayDestinations()
    {
        var compose = File.ReadAllText(GetDeployPath("docker-compose.yml"));

        compose.Should().Contain("ASPNETCORE_URLS: http://+:8080");
        compose.Should().Contain("ASPNETCORE_HTTP_PORTS: \"8080\"");
        compose.Should().Contain("ReverseProxy__Clusters__shops-cluster__Destinations__destination1__Address: http://shops:8080");
        compose.Should().Contain("AdminStatsOptions__ShopsServiceUrl: http://shops:8080");
        Regex.IsMatch(compose, @"http://shops:80(?!\d)").Should().BeFalse();
    }

    [Fact]
    public void ProductionCaddy_RoutesToGatewayContainerPort()
    {
        var caddyfile = File.ReadAllText(GetDeployPath("Caddyfile"));

        caddyfile.Should().Contain("reverse_proxy gateway:8080");
        Regex.IsMatch(caddyfile, @"gateway:80(?!\d)").Should().BeFalse();
    }

    [Fact]
    public void ProductionCaddy_ServesAndroidDownloadsFromReadOnlyHostMount()
    {
        var caddyfile = File.ReadAllText(GetDeployPath("Caddyfile"));
        var compose = File.ReadAllText(GetDeployPath("docker-compose.yml"));

        caddyfile.Should().Contain("handle_path /downloads/*");
        caddyfile.Should().Contain("root * /srv/downloads");
        compose.Should().Contain("/var/www/coffeepeek-downloads:/srv/downloads:ro");
    }

    [Fact]
    public void UpdateScript_WaitsForBackendServicesOnContainerPort()
    {
        var updateScript = File.ReadAllText(GetDeployPath("scripts", "update.sh"));

        updateScript.Should().Contain("http://${service}:8080${path}");
        updateScript.Should().NotContain("http://${service}${path}");
        updateScript.Should().Contain("ensure-secrets.sh");
    }

    [Theory]
    [InlineData("CoffeePeek.AccountService", "AccountService.Dockerfile")]
    [InlineData("CoffeePeek.ShopsService", "ShopsService.Dockerfile")]
    [InlineData("CoffeePeek.ModerationService", "ModerationService.Dockerfile")]
    [InlineData("CoffeePeek.MediaService", "MediaService.Dockerfile")]
    [InlineData("CoffeePeek.Gateway", "Gateway.Dockerfile")]
    public void ServiceDockerfiles_ExposeAspNetContainerPort(string projectDirectory, string dockerfileName)
    {
        var dockerfile = File.ReadAllText(GetRepoPath(projectDirectory, dockerfileName));

        dockerfile.Should().Contain("ASPNETCORE_URLS=http://+:8080");
        dockerfile.Should().Contain("ASPNETCORE_HTTP_PORTS=8080");
        dockerfile.Should().Contain("EXPOSE 8080");
        Regex.IsMatch(dockerfile, @"ASPNETCORE_URLS=http://\+:80(?!\d)").Should().BeFalse();
        Regex.IsMatch(dockerfile, @"EXPOSE 80(?!\d)").Should().BeFalse();
    }

    private static string GetDeployPath(params string[] segments)
    {
        return GetRepoPath(["deploy", .. segments]);
    }

    private static string GetRepoPath(params string[] segments)
    {
        var repoRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");

        return Path.GetFullPath(Path.Combine([repoRoot, .. segments]));
    }
}
