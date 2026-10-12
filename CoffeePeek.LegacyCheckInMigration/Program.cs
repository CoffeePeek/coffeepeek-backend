using CoffeePeek.LegacyCheckInMigration;

// No service host, outbox or API: run only while application writers are stopped.
try
{
    await LegacyMigrationRunner.RunAsync(MigrationSettings.FromEnvironment());
    Console.WriteLine("Legacy check-ins migrated; obsolete schema removed. No backups created.");
    return 0;
}
catch (Exception exception)
{
    // Never echo connection strings, user content or object keys.
    Console.Error.WriteLine(MigrationFailure.Describe(exception));
    return 1;
}
