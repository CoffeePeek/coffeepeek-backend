using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace CoffeePeek.LegacyCheckInMigration;

public sealed class MigrationDatabase(NpgsqlConnection connection)
{
    public async Task<bool> ExistsAsync(string table) =>
        (bool)(await ScalarAsync("SELECT to_regclass(@name) IS NOT NULL", ("name", Quote(table))))!;

    public async Task<List<JsonObject>> RowsAsync(string table)
    {
        if (!await ExistsAsync(table)) return [];
        await using var command = new NpgsqlCommand($"SELECT row_to_json(t)::text FROM {Quote(table)} t", connection);
        await using var reader = await command.ExecuteReaderAsync();
        List<JsonObject> rows = [];
        while (await reader.ReadAsync()) rows.Add(JsonNode.Parse(reader.GetString(0))!.AsObject());
        return rows;
    }

    public async Task<int> InsertAsync(string table, JsonObject row)
    {
        var columns = string.Join(",", row.Select(x => Quote(x.Key)));
        await using var command = new NpgsqlCommand($"INSERT INTO {Quote(table)} ({columns}) SELECT {columns} FROM jsonb_populate_record(NULL::{Quote(table)}, @row) ON CONFLICT DO NOTHING", connection);
        command.Parameters.AddWithValue("row", NpgsqlDbType.Jsonb, row.ToJsonString());
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<int> ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(sql, parameters);
        return await command.ExecuteScalarAsync();
    }

    public Task MarkCompleteAsync() => ExecuteAsync("""
        CREATE TABLE IF NOT EXISTS "_LegacyCheckInMigration" ("Completed" boolean NOT NULL CHECK ("Completed"));
        INSERT INTO "_LegacyCheckInMigration" SELECT true WHERE NOT EXISTS (SELECT 1 FROM "_LegacyCheckInMigration");
        """);

    public async Task<bool> IsCompleteAsync() => await ExistsAsync("_LegacyCheckInMigration") &&
        (bool)(await ScalarAsync("SELECT EXISTS (SELECT 1 FROM \"_LegacyCheckInMigration\" WHERE \"Completed\")"))!;

    private NpgsqlCommand Command(string sql, (string Name, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private static string Quote(string name)
    {
        if (name.Length == 0 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Invalid migration identifier.");
        return $"\"{name}\"";
    }
}
