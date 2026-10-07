using System.Text.Json.Nodes;
using CoffeePeek.LegacyCheckInsMigration;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel.Options;
using Microsoft.Extensions.Options;
using Npgsql;

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("Legacy check-in backfill: dry-run|apply|reconcile --manifest <protected-file> [--writers-stopped --queues-drained]");
    Console.WriteLine("Inputs: CP_BACKFILL_SHOPS, MODERATION, ACCOUNT, MEDIA (connection strings); CP_BACKFILL_REDIS; CP_BACKFILL_MINIO_ENDPOINT, ACCESS_KEY, SECRET_KEY, SHOP_BUCKET, USER_BUCKET, PRIVATE_BUCKET.");
    return 0;
}
var mode = args[0];
var index = Array.IndexOf(args,"--manifest");
if (mode is not ("dry-run" or "apply" or "reconcile") || index < 0 || index+1 >= args.Length)
{ Console.Error.WriteLine("Invalid arguments. Use --help."); return 1; }
var path = Path.GetFullPath(args[index+1]);
if (mode == "apply" && (!args.Contains("--writers-stopped") || !args.Contains("--queues-drained")))
{ Console.Error.WriteLine("Apply requires stopped writers plus drained broker queues and durable inbox/outbox; pass --writers-stopped --queues-drained after backup."); return 1; }
var db = new Dictionary<string,NpgsqlConnection>();
JsonObject? manifest = null;
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(30));
Console.CancelKeyPress += (_,e) => { e.Cancel = true; deadline.Cancel(); };
var ct = deadline.Token;
string Input(string key) => Environment.GetEnvironmentVariable("CP_BACKFILL_"+key)
    ?? throw new InvalidOperationException("Missing explicit operator input: "+key);
try
{
    foreach (var service in new[] { "Account","Media","Moderation","Shops" })
    {
        var settings = new NpgsqlConnectionStringBuilder(Input(service.ToUpperInvariant())) {
            Pooling = false, ApplicationName = "coffeepeek-legacy-check-ins-v1", Options = "-c lock_timeout=30s" };
        if (db.Values.Any(c => new NpgsqlConnectionStringBuilder(c.ConnectionString).Database == settings.Database
            && new NpgsqlConnectionStringBuilder(c.ConnectionString).Host == settings.Host
            && new NpgsqlConnectionStringBuilder(c.ConnectionString).Port == settings.Port))
            throw new InvalidOperationException("Each service requires its own database.");
        var connection = new NpgsqlConnection(settings.ConnectionString); db[service] = connection;
        await connection.OpenAsync(ct);
        await using var lease = new NpgsqlCommand("SELECT pg_advisory_lock(261007019)",connection);
        await lease.ExecuteNonQueryAsync(ct);
    }
    await LegacyReviewBackfill.ValidateSchemaAsync(db,ct);
    if (mode == "dry-run")
    {
        if (File.Exists(path)) throw new InvalidOperationException("Refusing to replace a saved source inventory; use a new manifest path.");
        var inventory = await LegacyReviewBackfill.InventoryAsync(db,ct);
        manifest = new JsonObject { ["Version"] = 1, ["StartedAtUtc"] = DateTime.UtcNow.ToString("O"), ["Inventory"] = inventory,
            ["InventoryHash"] = LegacyReviewBackfill.Hash(inventory), ["Mappings"] = LegacyReviewBackfill.Map(inventory),
            ["CacheInvalidationPending"] = true, ["CountsCompleted"] = false };
        LegacyReviewBackfill.PrepareVotes(manifest); LegacyReviewBackfill.PreparePhotos(manifest);
        LegacyReviewBackfill.Save(path,manifest);
        Console.WriteLine("Dry-run saved source inventory and per-source mapping. No database/storage mutation.");
        Console.WriteLine("Visits: "+manifest["Mappings"]!.AsArray().Count+"; photos: "+manifest["Photos"]!.AsArray().Count+"; votes: "+manifest["Votes"]!.AsArray().Count);
        var blockers = manifest["Mappings"]!.AsArray().OfType<JsonObject>().Count(e => LegacyReviewBackfill.Text(e,"Outcome") is "unresolved" or "conflict")
            + manifest["PhotoOutcomes"]!.AsArray().OfType<JsonObject>().Count(e => LegacyReviewBackfill.Text(e,"Outcome") == "unresolved")
            + manifest["VoteOutcomes"]!.AsArray().OfType<JsonObject>().Count(e => LegacyReviewBackfill.Text(e,"Outcome") == "unresolved");
        Console.WriteLine("Release blockers: "+blockers+" (see exact source IDs/reasons in the manifest).");
        return blockers == 0 ? 0 : 2;
    }
    manifest = JsonNode.Parse(await File.ReadAllTextAsync(path,ct))!.AsObject();
    if (LegacyReviewBackfill.Int(manifest,"Version") != 1
        || LegacyReviewBackfill.Text(manifest,"InventoryHash") != LegacyReviewBackfill.Hash(manifest["Inventory"]!))
        throw new InvalidOperationException("Unsupported or changed source inventory.");
    await LegacyReviewBackfill.AssertArchivesAsync(manifest,db,ct);
    await LegacyReviewBackfill.AssertDrainedAsync(db,ct);
    var storageOptions = new MinIOOptions { Endpoint = Input("MINIO_ENDPOINT"), AccessKey = Input("MINIO_ACCESS_KEY"),
        SecretKey = Input("MINIO_SECRET_KEY"), ShopBucketName = Input("MINIO_SHOP_BUCKET"),
        UserBucketName = Input("MINIO_USER_BUCKET"), CheckInBucketName = Input("MINIO_PRIVATE_BUCKET") };
    using var storage = new MinIOStorageService(Options.Create(storageOptions),
        Options.Create(new MediaPublicUrlOptions { PublicEndpoint = storageOptions.Endpoint }));
    if (mode == "apply")
    {
        manifest["CacheInvalidationPending"] = true; LegacyReviewBackfill.Save(path,manifest);
        foreach (var entry in manifest["Mappings"]!.AsArray().OfType<JsonObject>())
            if (!LegacyReviewBackfill.Flag(entry,"PhotoPlanningUnresolved")) entry["PhotosUnresolved"] = false;
        await LegacyReviewBackfill.ApplyVisitsAsync(manifest,db,ct); LegacyReviewBackfill.Save(path,manifest);
        await LegacyReviewBackfill.ApplySnapshotsAsync(manifest,db,ct);
        await LegacyReviewBackfill.ApplyPhotosAsync(manifest,db,storage,path,ct);
        await LegacyReviewBackfill.ApplyVotesAsync(manifest,db,ct);
        await LegacyReviewBackfill.PublishAsync(manifest,db,ct);
        await LegacyReviewBackfill.ReconcileCountsAsync(manifest,db,ct); LegacyReviewBackfill.Save(path,manifest);
        await LegacyReviewBackfill.InvalidateAsync(manifest,Input("REDIS"),ct); LegacyReviewBackfill.Save(path,manifest);
    }
    var result = await LegacyReviewBackfill.ReconcileAsync(manifest,db,ct);
    await LegacyReviewBackfill.VerifyPhotoBytesAsync(manifest,storage,result["Failures"]!.AsArray(),ct);
    result["ReleaseReady"] = result["Failures"]!.AsArray().Count == 0;
    manifest["Reconciliation"] = result; LegacyReviewBackfill.Save(path,manifest);
    Console.WriteLine(result.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return LegacyReviewBackfill.Flag(result,"ReleaseReady") ? 0 : 2;
}
catch (Exception ex)
{
    if (manifest != null)
    {
        manifest["LastFailure"] = ex.GetType().Name;
        LegacyReviewBackfill.Save(path,manifest);
    }
    // Connection and object credentials must not appear in console logs.
    Console.Error.WriteLine("Backfill stopped: "+ex.GetType().Name+". No rollback/delete attempted; preserve manifest and inspect secure operator inputs.");
    return 1;
}
finally
{
    foreach (var connection in db.Values) await connection.DisposeAsync();
}