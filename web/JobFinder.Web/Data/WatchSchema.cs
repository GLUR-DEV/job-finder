using System.Data;
using Microsoft.EntityFrameworkCore;

namespace JobFinder.Web.Data;

/// <summary>
/// Brings an older watch.db up to date. The database is created with <c>EnsureCreated</c>, which never adds columns to a
/// database that already exists, so columns added in later versions are added here. Doing it in place keeps the ntfy
/// topic and the settings a person already has, so nothing needs deleting or re-subscribing.
/// </summary>
public static class WatchSchema
{
    // Columns that came after the first version of the watch, and the SQL that adds each to an older table.
    private static readonly (string Name, string Definition)[] Added =
    {
        ("Source", "TEXT NOT NULL DEFAULT 'seek'"),
        ("District", "TEXT NOT NULL DEFAULT ''"),
        ("SkipPromoted", "INTEGER NOT NULL DEFAULT 0"),
        ("QuickApplyOnly", "INTEGER NOT NULL DEFAULT 0"),
        ("PayListedOnly", "INTEGER NOT NULL DEFAULT 0"),
    };

    public static void Upgrade(WatchDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        var wasClosed = conn.State != ConnectionState.Open;
        if (wasClosed) conn.Open();
        try
        {
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(\"Watch\")";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) have.Add(reader.GetString(reader.GetOrdinal("name")));
            }

            foreach (var (name, definition) in Added)
            {
                if (have.Contains(name)) continue;
                using var alter = conn.CreateCommand();
                alter.CommandText = $"ALTER TABLE \"Watch\" ADD COLUMN \"{name}\" {definition}";
                alter.ExecuteNonQuery();
            }
        }
        finally
        {
            if (wasClosed) conn.Close();
        }
    }
}
