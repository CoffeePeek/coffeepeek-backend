using Npgsql;

namespace CoffeePeek.LegacyCheckInMigration;

public static class MigrationFailure
{
    public static string Describe(Exception exception)
    {
        // PostgreSQL messages and details can contain row values; log only schema metadata.
        var diagnostic = exception is PostgresException postgres
            ? $"{exception.GetType().Name}; SQLSTATE={postgres.SqlState}; table={postgres.TableName}; column={postgres.ColumnName}; constraint={postgres.ConstraintName}"
            : exception.GetType().Name;
        return $"Migration stopped ({diagnostic}). Legacy cleanup was not completed. Fix the cause and rerun.";
    }
}
