using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace SmartFiler.Data
{
    /// <summary>
    /// Lightweight SQLite database context for SmartFiler.
    /// Opens a WAL-mode connection and creates all tables on first use.
    /// </summary>
    public sealed class SmartFilerDb : IDisposable
    {
        private static readonly string DefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SmartFiler",
            "smartfiler.db");

        /// <summary>
        /// The open SQLite connection. All repositories read from this property.
        /// </summary>
        public SqliteConnection Connection { get; }

        public SmartFilerDb(string? dbPath = null)
        {
            var path = dbPath ?? DefaultPath;
            var dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Connection = new SqliteConnection($"Data Source={path}");
            Connection.Open();

            using var pragma = Connection.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
        }

        /// <summary>
        /// Creates all tables if they do not already exist.
        /// Call once at application startup.
        /// </summary>
        public void EnsureCreated()
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS move_history (
    id              INTEGER PRIMARY KEY,
    source_path     TEXT    NOT NULL,
    dest_path       TEXT    NOT NULL,
    file_ext        TEXT,
    file_category   TEXT,
    project_folder  TEXT,
    batch_id        TEXT    NOT NULL,
    moved_at        TEXT    NOT NULL,
    undone          INTEGER DEFAULT 0
);

CREATE TABLE IF NOT EXISTS project_index (
    id              INTEGER PRIMARY KEY,
    folder_path     TEXT    NOT NULL,
    folder_name     TEXT    NOT NULL,
    last_modified   TEXT,
    scanned_at      TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS deferred_files (
    id              INTEGER PRIMARY KEY,
    file_path       TEXT    NOT NULL,
    deferred_at     TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS scan_stats (
    id              INTEGER PRIMARY KEY,
    scan_date       TEXT    NOT NULL,
    files_scanned   INTEGER,
    files_moved     INTEGER,
    files_deferred  INTEGER,
    files_deleted   INTEGER,
    bytes_moved     INTEGER,
    bytes_deleted   INTEGER
);

CREATE TABLE IF NOT EXISTS file_hashes (
    id              INTEGER PRIMARY KEY,
    file_path       TEXT    NOT NULL,
    file_size       INTEGER,
    hash            TEXT,
    is_partial      INTEGER DEFAULT 0,
    hashed_at       TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS aliases (
    id              INTEGER PRIMARY KEY,
    acronym         TEXT    NOT NULL,
    expansion       TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS batch_journal (
    id              INTEGER PRIMARY KEY,
    batch_id        TEXT    NOT NULL,
    source_path     TEXT    NOT NULL,
    dest_path       TEXT    NOT NULL,
    status          TEXT    NOT NULL,
    created_at      TEXT    NOT NULL
);
";
            cmd.ExecuteNonQuery();
        }

        public void Dispose()
        {
            Connection?.Dispose();
        }
    }
}
