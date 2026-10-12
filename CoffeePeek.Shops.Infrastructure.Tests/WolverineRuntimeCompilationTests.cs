using JasperFx.CodeGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace CoffeePeek.Shops.Infrastructure.Tests;

public sealed class WolverineRuntimeCompilationTests
{
    [Fact]
    public void LiveTestHost_RegistersRuntimeCompilerForAutoCodeGeneration()
    {
        using var host = Host.CreateDefaultBuilder()
            .UseWolverine(options => options.UseRuntimeCompilation())
            .Build();

        Assert.NotNull(host.Services.GetService<IAssemblyGenerator>());
    }
}
