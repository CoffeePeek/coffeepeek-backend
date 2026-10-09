using Npgsql;

namespace CoffeePeek.LegacyCheckInMigration.Tests;

public sealed class MigrationFailureTests
{
    [Fact]
    public void PostgreSqlFailureShowsSchemaMetadataWithoutUserContent()
    {
        var error = new PostgresException("private user content", "ERROR", "ERROR", "23514",
            detail: "secret row values", tableName: "ShopMenuItems", columnName: "Price",
            constraintName: "CK_ShopMenuItems_Values");

        var diagnostic = MigrationFailure.Describe(error);

        Assert.Contains("SQLSTATE=23514", diagnostic);
        Assert.Contains("table=ShopMenuItems", diagnostic);
        Assert.Contains("column=Price", diagnostic);
        Assert.Contains("constraint=CK_ShopMenuItems_Values", diagnostic);
        Assert.DoesNotContain(error.MessageText, diagnostic);
        Assert.DoesNotContain(error.Detail!, diagnostic);
    }

    [Fact]
    public void OtherFailureDoesNotShowConnectionDetails()
    {
        var diagnostic = MigrationFailure.Describe(new IOException("Host=private;Password=secret"));

        Assert.Contains("IOException", diagnostic);
        Assert.DoesNotContain("private", diagnostic);
        Assert.DoesNotContain("secret", diagnostic);
    }
}
