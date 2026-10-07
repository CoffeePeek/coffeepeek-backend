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
    Console.Error.WriteLine($"Migration stopped ({exception.GetType().Name}). Legacy cleanup was not completed. Fix the cause and rerun.");
    return 1;
}
