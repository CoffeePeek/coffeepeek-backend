using System;
using System.IO;
using FluentAssertions;

namespace CoffeePeek.Shops.Application.Tests;

public class ContainerListenAddressTests
{
    [Fact]
    public void ShopsDockerfile_BindsAllInterfacesOnPort8080()
    {
        var dockerfile = File.ReadAllText(Path.Combine(FindRepoRoot(), "CoffeePeek.ShopsService", "ShopsService.Dockerfile"));

        dockerfile.Should().Contain("ASPNETCORE_URLS=http://+:8080");
        dockerfile.Should().NotContain("http://[::]:8080");
        dockerfile.Should().Contain("ASPNETCORE_HTTP_PORTS=8080");
    }

    [Fact]
    public void ProductionCompose_ForcesPort8080AndExplicitShopsAddress()
    {
        var compose = File.ReadAllText(Path.Combine(FindRepoRoot(), "deploy", "docker-compose.yml"));

        compose.Should().Contain("ASPNETCORE_URLS: http://+:8080");
        compose.Should().Contain("ASPNETCORE_HTTP_PORTS: \"8080\"");
        compose.Should().Contain("http://shops:8080");
    }

    [Fact]
    public void SharedSerilogConfig_DoesNotEscalateTransientHealthProbeFailuresToError()
    {
        var serilogSetup = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "CoffeePeek.Shared.Web", "Logging", "SerilogExtensions.cs"));

        serilogSetup.Should().Contain("\"Yarp.ReverseProxy.Health\", LogEventLevel.Error");
        serilogSetup.Should().NotContain("\"Yarp.ReverseProxy.Health\", LogEventLevel.Warning");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CoffeePeek.slnx")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root (CoffeePeek.slnx).");
    }
}
