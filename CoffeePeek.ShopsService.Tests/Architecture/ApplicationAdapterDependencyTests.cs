using System.Reflection;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;

namespace CoffeePeek.ShopsService.Tests.Architecture;

public class ApplicationAdapterDependencyTests
{
    private static readonly Assembly Application = typeof(UpdateAdminCoffeeShopHandler).Assembly;

    [Fact]
    public void CoffeeCatalogReaders_DoNotExposeWritesOrImportCommands()
    {
        Assert.Null(Application.GetType("CoffeePeek.Shops.Application.Features.CoffeeCatalog.ICoffeeCatalogStore"));
        string[] writePrefixes = ["Create", "Edit", "Archive", "Set", "Import", "Record"];
        foreach (var reader in new[] { typeof(IPublicCoffeeCatalogReader), typeof(IAdminCoffeeCatalogReader) })
            Assert.DoesNotContain(reader.GetMethods(), m => writePrefixes.Any(p => m.Name.StartsWith(p, StringComparison.Ordinal)));
        Assert.DoesNotContain(typeof(IPublicCoffeeCatalogReader).GetMethods(), m => m.Name.StartsWith("Admin", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplicationTypesAndCalls_DoNotDependOnConcreteAdapters()
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in Application.GetTypes())
        {
            foreach (var dependency in SignatureTypes(type))
            {
                if (IsForbidden(dependency))
                    violations.Add($"signature: {type.FullName} -> {dependency.FullName}");
            }
        }

        foreach (var (caller, callee) in IlDependencyScanner.GetCalls(Application))
        {
            if (callee.DeclaringType is { } target && IsForbidden(target))
                violations.Add($"call: {caller.DeclaringType?.FullName}.{caller.Name} -> {target.FullName}.{callee.Name}");
        }

        Assert.True(violations.Count == 0,
            "Application must use ports rather than concrete Persistence/Infrastructure/host/EF types:\n" +
            string.Join("\n", violations));
    }

    private static IEnumerable<Type> SignatureTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (type.BaseType is { } baseType)
            yield return baseType;
        foreach (var interfaceType in type.GetInterfaces())
            yield return interfaceType;
        foreach (var field in type.GetFields(flags))
            yield return field.FieldType;
        foreach (var property in type.GetProperties(flags))
            yield return property.PropertyType;
        foreach (var method in type.GetMethods(flags))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
                yield return parameter.ParameterType;
        }
        foreach (var constructor in type.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
                yield return parameter.ParameterType;
        }
    }

    private static bool IsForbidden(Type type)
    {
        if (type.HasElementType)
            return IsForbidden(type.GetElementType()!);
        if (type.IsGenericType && type.GetGenericArguments().Any(IsForbidden))
            return true;

        var assembly = type.Assembly.GetName().Name ?? string.Empty;
        return assembly.StartsWith("CoffeePeek.Shops.Persistance", StringComparison.Ordinal)
               || assembly.StartsWith("CoffeePeek.Shops.Infrastructure", StringComparison.Ordinal)
               || assembly.StartsWith("CoffeePeek.ShopsService", StringComparison.Ordinal)
               || assembly.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal);
    }
}
