using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public sealed class LocalStore : IJobStore
{
    private readonly string connectionString;
    private readonly SemaphoreSlim gate = new(1, 1);
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = false };
    public LocalStore(string root)
    {
        PrivateFiles.EnsureDirectory(root);
        PrivateFiles.RejectLinks(root, recursive: true);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "jobs.db"), Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        using var connection = Open();
        using (var version = connection.CreateCommand())
        {
            version.CommandText = "PRAGMA user_version;";
            if (Convert.ToInt32(version.ExecuteScalar()) > 8) throw new InvalidDataException("Dữ liệu được tạo bởi phiên bản app mới hơn. Không được hạ schema.");
        }
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA secure_delete=ON; CREATE TABLE IF NOT EXISTS jobs (id TEXT PRIMARY KEY, requestId TEXT NOT NULL UNIQUE, created TEXT NOT NULL, payload TEXT NOT NULL); CREATE TABLE IF NOT EXISTS settings (id INTEGER PRIMARY KEY CHECK(id=1), payload TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "PRAGMA user_version;";
        if (Convert.ToInt32(cmd.ExecuteScalar()) < 8)
        {
            // All rows and the schema marker commit together. DPAPI failure must leave
            // the old checkpoint readable, not silently reset jobs or Google destinations.
            using var transaction = connection.BeginTransaction();
            foreach (var table in new[] { "jobs", "settings" })
            {
                using var read = connection.CreateCommand(); read.Transaction = transaction;
                read.CommandText = "SELECT id,payload FROM " + table;
                var rows = new List<(object Id, string Payload)>();
                using (var reader = read.ExecuteReader())
                    while (reader.Read()) rows.Add((reader.GetValue(0), reader.GetString(1)));
                foreach (var row in rows)
                {
                    var plaintext = DecodePayload(row.Payload, allowLegacy: true);
                    using var validatedJson = JsonDocument.Parse(plaintext);
                    if (table == "jobs")
                    {
                        var migrated = JsonSerializer.Deserialize<VideoJob>(plaintext, Json) ?? throw new InvalidDataException("Job không hợp lệ.");
                        migrated.MigrateWorkflow();
                        if (!validatedJson.RootElement.TryGetProperty("IncludeSourceChannel", out _) && migrated.SheetWriteStarted)
                            migrated.IncludeSourceChannel = false; // Reconcile the old atomic four-cell receipt as originally sent.
                        plaintext = JsonSerializer.Serialize(migrated, Json);
                    }
                    else
                    {
                        var migrated = JsonSerializer.Deserialize<AppSettings>(plaintext, Json) ?? throw new InvalidDataException("Settings không hợp lệ.");
                        migrated.NormalizeFolders(); plaintext = JsonSerializer.Serialize(migrated, Json);
                    }
                    using var write = connection.CreateCommand(); write.Transaction = transaction;
                    write.CommandText = "UPDATE " + table + " SET payload=$payload WHERE id=$id";
                    write.Parameters.AddWithValue("$payload", EncodePayload(plaintext)); write.Parameters.AddWithValue("$id", row.Id);
                    write.ExecuteNonQuery();
                }
            }
            using var version = connection.CreateCommand(); version.Transaction = transaction;
            version.CommandText = "PRAGMA user_version=8;"; version.ExecuteNonQuery(); transaction.Commit();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); VACUUM; PRAGMA wal_checkpoint(TRUNCATE);"; cmd.ExecuteNonQuery();
        }
        PrivateFiles.ProtectFile(Path.Combine(root, "jobs.db"));
    }
    private const string PayloadPrefix = "dpapi-v1:";
    public static string DecodePayload(string payload, bool allowLegacy = false) => payload.StartsWith(PayloadPrefix, StringComparison.Ordinal)
        ? Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(payload[PayloadPrefix.Length..]), null, DataProtectionScope.CurrentUser))
        : allowLegacy ? payload : throw new InvalidDataException("Payload chưa được mã hóa hoặc đã thay đổi. App không tự bỏ dữ liệu để chạy tiếp.");
    private static string EncodePayload(string payload) => PayloadPrefix + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(payload), null, DataProtectionScope.CurrentUser));
    private SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); return c; }
    public async Task SaveAsync(VideoJob job, CancellationToken ct = default)
    {
        // Serialize before yielding: UI/worker cannot change the persisted snapshot mid-write.
        var json = EncodePayload(JsonSerializer.Serialize(job, Json));
        await gate.WaitAsync(ct);
        try
        {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO jobs(id,requestId,created,payload) VALUES($id,$request,$created,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload;";
            cmd.Parameters.AddWithValue("$id", job.Id); cmd.Parameters.AddWithValue("$request", job.RequestId);
            cmd.Parameters.AddWithValue("$created", job.Created.ToString("O")); cmd.Parameters.AddWithValue("$payload", json);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<VideoJob>> ListAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT id,requestId,payload FROM jobs ORDER BY created, id;";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            var list = new List<VideoJob>();
            while (await reader.ReadAsync(ct))
            {
                var job = JsonSerializer.Deserialize<VideoJob>(DecodePayload(reader.GetString(2)), Json) ?? throw new InvalidDataException("Job không hợp lệ.");
                if (job.Id != reader.GetString(0) || job.RequestId != reader.GetString(1)) throw new InvalidDataException("Checkpoint không khớp định danh công việc.");
                list.Add(job);
            }
            return list;
        }
        finally { gate.Release(); }
    }
    public async Task<AppSettings> SettingsAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT payload FROM settings WHERE id=1;";
            var settings = await cmd.ExecuteScalarAsync(ct) is string s ? JsonSerializer.Deserialize<AppSettings>(DecodePayload(s), Json)! : new AppSettings();
            settings.NormalizeFolders(); return settings;
        }
        finally { gate.Release(); }
    }
    public async Task SavePendingOperationsAsync(IReadOnlyList<OperationNotice> pending, CancellationToken ct = default)
    {
        var snapshot = pending.ToList();
        await gate.WaitAsync(ct);
        try
        {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT payload FROM settings WHERE id=1;";
            var current = await cmd.ExecuteScalarAsync(ct) is string json
                ? JsonSerializer.Deserialize<AppSettings>(DecodePayload(json), Json) ?? throw new InvalidDataException("Settings không hợp lệ.") : new AppSettings();
            // Notification persistence never replays an older snapshot of Google destinations.
            current.PendingOperations = snapshot;
            cmd.CommandText = "INSERT INTO settings(id,payload) VALUES(1,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload;";
            cmd.Parameters.AddWithValue("$payload", EncodePayload(JsonSerializer.Serialize(current, Json)));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { gate.Release(); }
    }
    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default)
    {
        settings.NormalizeFolders();
        var json = EncodePayload(JsonSerializer.Serialize(settings, Json));
        await gate.WaitAsync(ct);
        try
        {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO settings(id,payload) VALUES(1,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload;";
            cmd.Parameters.AddWithValue("$payload", json); await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { gate.Release(); }
    }
}
public sealed class DpapiSecretStore(string root) : ISecretStore
{
    private string FilePath(string key)
    {
        if (key.Length is < 1 or > 150 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ArgumentException("Khóa credential không hợp lệ.");
        PrivateFiles.EnsureDirectory(root);
        var path = Path.Combine(root, key + ".dpapi"); PrivateFiles.RejectLinks(path);
        if (File.Exists(path)) PrivateFiles.ProtectFile(path);
        return path;
    }
    public async Task<string?> ReadAsync(string key, CancellationToken ct = default)
    {
        var path = FilePath(key); if (!File.Exists(path)) return null;
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(await File.ReadAllBytesAsync(path, ct), null, DataProtectionScope.CurrentUser));
    }
    public async Task WriteAsync(string key, string value, CancellationToken ct = default)
    {
        var path = FilePath(key);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await stream.WriteAsync(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser), ct);
                await stream.FlushAsync(ct); stream.Flush(true);
            }
            PrivateFiles.ProtectFile(temporary); PrivateFiles.RejectLinks(path);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public Task DeleteAsync(string key, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); File.Delete(FilePath(key)); return Task.CompletedTask; }
}

