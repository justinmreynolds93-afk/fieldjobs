using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace FieldJobs.Data;

/// <summary>
/// Owns the single SQLite connection for the app. WPF runs the UI on one thread and
/// all data access is synchronous, so one shared connection keeps things simple.
/// </summary>
public static class Db
{
    private static SqliteConnection? _conn;
    private static readonly object _gate = new();

    public const int CurrentSchemaVersion = 2;

    public static SqliteConnection Connection
    {
        get
        {
            if (_conn is { State: ConnectionState.Open }) return _conn;
            lock (_gate)
            {
                if (_conn is { State: ConnectionState.Open }) return _conn;
                AppPaths.EnsureCreated();
                var cs = new SqliteConnectionStringBuilder
                {
                    DataSource = AppPaths.DatabaseFile,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Shared,
                    Pooling = false
                }.ToString();
                _conn = new SqliteConnection(cs);
                _conn.Open();
                _conn.Execute("PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;");
                return _conn;
            }
        }
    }

    public static void Close()
    {
        lock (_gate)
        {
            _conn?.Dispose();
            _conn = null;
        }
    }

    /// <summary>Creates the schema on first run and applies additive migrations on later runs.</summary>
    public static void Initialize()
    {
        // Map snake_case columns (job_number) to PascalCase properties (JobNumber).
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        var c = Connection;
        c.Execute(Schema); // every table is CREATE TABLE IF NOT EXISTS — safe on every run

        // Additive column migrations (idempotent — checked against the live schema each start).
        AddColumnIfMissing(c, "job_files", "invoice_id", "INTEGER");
        AddColumnIfMissing(c, "job_files", "doc_type", "TEXT NOT NULL DEFAULT 'file'");
        AddColumnIfMissing(c, "invoices", "date_sent", "TEXT");
        AddColumnIfMissing(c, "invoices", "sent_to", "TEXT");
        AddColumnIfMissing(c, "payments", "method", "TEXT");
        AddColumnIfMissing(c, "payments", "reference", "TEXT");

        // Indexes that depend on migrated columns — created after the columns exist.
        c.Execute("""
            CREATE INDEX IF NOT EXISTS ix_files_invoice ON job_files(invoice_id);
            """);

        var version = c.ExecuteScalar<long?>("SELECT version FROM schema_meta LIMIT 1") ?? 0;
        if (version < CurrentSchemaVersion)
            c.Execute("DELETE FROM schema_meta; INSERT INTO schema_meta(version) VALUES (@v);",
                new { v = CurrentSchemaVersion });

        SeedData.EnsureDefaults(c);
    }

    private static void AddColumnIfMissing(IDbConnection c, string table, string column, string definition)
    {
        var columns = c.Query($"PRAGMA table_info({table})")
            .Select(r => (string)((IDictionary<string, object>)r)["name"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!columns.Contains(column))
            c.Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }

    /// <summary>Runs <paramref name="work"/> inside a transaction, rolling back on error.</summary>
    public static void InTransaction(Action<IDbTransaction> work)
    {
        using var tx = Connection.BeginTransaction();
        try
        {
            work(tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS schema_meta (version INTEGER NOT NULL);

        CREATE TABLE IF NOT EXISTS settings (
            key   TEXT PRIMARY KEY,
            value TEXT
        );

        CREATE TABLE IF NOT EXISTS fee_schedule (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            label      TEXT NOT NULL,
            amount     REAL NOT NULL DEFAULT 0,
            sort_order INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS stage_templates (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            seq         INTEGER NOT NULL,
            stage_key   TEXT NOT NULL UNIQUE,
            name        TEXT NOT NULL,
            is_optional INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS checklist_templates (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            stage_key TEXT NOT NULL,
            text      TEXT NOT NULL,
            seq       INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS jobs (
            id             INTEGER PRIMARY KEY AUTOINCREMENT,
            job_number     TEXT,
            external_ref1   TEXT,
            external_ref2    TEXT,
            address_street TEXT,
            address_city   TEXT,
            address_zip    TEXT,
            client_name TEXT,
            project_type   TEXT NOT NULL DEFAULT 'Standard',
            date_assigned  TEXT,
            role_notes     TEXT,
            status         TEXT NOT NULL DEFAULT 'Not started',
            notes          TEXT,
            created_at     TEXT NOT NULL,
            updated_at     TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS stages (
            id             INTEGER PRIMARY KEY AUTOINCREMENT,
            job_id         INTEGER NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
            seq            INTEGER NOT NULL,
            stage_key      TEXT NOT NULL,
            name           TEXT NOT NULL,
            is_optional    INTEGER NOT NULL DEFAULT 0,
            status         TEXT NOT NULL DEFAULT 'Not started',
            date_completed TEXT,
            notes          TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_stages_job ON stages(job_id);

        CREATE TABLE IF NOT EXISTS checklist_items (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            stage_id   INTEGER NOT NULL REFERENCES stages(id) ON DELETE CASCADE,
            text       TEXT NOT NULL,
            is_checked INTEGER NOT NULL DEFAULT 0,
            seq        INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS ix_checklist_stage ON checklist_items(stage_id);

        CREATE TABLE IF NOT EXISTS job_files (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            job_id        INTEGER NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
            stage_id      INTEGER REFERENCES stages(id) ON DELETE SET NULL,
            stage_key     TEXT,
            invoice_id    INTEGER REFERENCES invoices(id) ON DELETE SET NULL,
            doc_type      TEXT NOT NULL DEFAULT 'file',
            original_name TEXT NOT NULL,
            stored_path   TEXT NOT NULL,
            size_bytes    INTEGER NOT NULL DEFAULT 0,
            date_added    TEXT NOT NULL,
            note          TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_files_job ON job_files(job_id);

        CREATE TABLE IF NOT EXISTS invoices (
            id             INTEGER PRIMARY KEY AUTOINCREMENT,
            job_id         INTEGER NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
            invoice_number TEXT NOT NULL,
            invoice_date   TEXT NOT NULL,
            date_sent      TEXT,
            sent_to        TEXT,
            stage_key      TEXT,
            stage_label    TEXT,
            status         TEXT NOT NULL DEFAULT 'Draft',
            notes          TEXT,
            created_at     TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_invoices_job ON invoices(job_id);

        CREATE TABLE IF NOT EXISTS invoice_lines (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            invoice_id  INTEGER NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
            description TEXT NOT NULL,
            qty         REAL NOT NULL DEFAULT 1,
            rate        REAL NOT NULL DEFAULT 0,
            seq         INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS ix_lines_invoice ON invoice_lines(invoice_id);

        CREATE TABLE IF NOT EXISTS payments (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            invoice_id   INTEGER NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
            payment_date TEXT NOT NULL,
            amount       REAL NOT NULL DEFAULT 0,
            method       TEXT,
            reference    TEXT,
            note         TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_payments_invoice ON payments(invoice_id);

        CREATE TABLE IF NOT EXISTS contacts (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            name       TEXT NOT NULL,
            role       TEXT,
            email      TEXT,
            phone      TEXT,
            is_primary INTEGER NOT NULL DEFAULT 0,
            sort_order INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS activity (
            id       INTEGER PRIMARY KEY AUTOINCREMENT,
            job_id   INTEGER REFERENCES jobs(id) ON DELETE CASCADE,
            ts       TEXT NOT NULL,
            kind     TEXT NOT NULL,
            summary  TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_activity_job ON activity(job_id);
        CREATE INDEX IF NOT EXISTS ix_activity_ts ON activity(ts);
        """;
}
