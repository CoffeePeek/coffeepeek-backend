using CoffeePeek.MediaService.Configuration;
using Npgsql;

namespace CoffeePeek.LegacyCheckInMigration;

public sealed record MigrationSettings(string Account, string Shops, string Moderation, string Media, MinIOOptions Storage)
{
    public static MigrationSettings FromEnvironment()
    {
        string Connection(string name, string database) => Environment.GetEnvironmentVariable($"LegacyMigration__{name}")
            ?? new NpgsqlConnectionStringBuilder
            {
                Host = Environment.GetEnvironmentVariable("PG_HOST") ?? "localhost",
                Port = int.Parse(Environment.GetEnvironmentVariable("PG_PORT") ?? "5432"),
                Database = database,
                Username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? throw new InvalidOperationException("POSTGRES_USER is required."),
                Password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? throw new InvalidOperationException("POSTGRES_PASSWORD is required.")
            }.ConnectionString;
        return new(Connection("Account", "cpaccountdb"), Connection("Shops", "cpshopsdb"),
            Connection("Moderation", "cpmoderationdb"), Connection("Media", "cpmediadb"), new MinIOOptions
            {
                Endpoint = Environment.GetEnvironmentVariable("MinIOOptions__Endpoint") ?? "http://localhost:9000",
                AccessKey = Environment.GetEnvironmentVariable("MinIOOptions__AccessKey") ?? Environment.GetEnvironmentVariable("MINIO_ROOT_USER") ?? "",
                SecretKey = Environment.GetEnvironmentVariable("MinIOOptions__SecretKey") ?? Environment.GetEnvironmentVariable("MINIO_ROOT_PASSWORD") ?? "",
                ShopBucketName = Environment.GetEnvironmentVariable("MinIOOptions__ShopBucketName") ?? "coffeepeek.shops",
                UserBucketName = Environment.GetEnvironmentVariable("MinIOOptions__UserBucketName") ?? "coffeepeek.avatars",
                CheckInBucketName = Environment.GetEnvironmentVariable("MinIOOptions__CheckInBucketName") ?? "coffeepeek.checkins"
            });
    }
}
