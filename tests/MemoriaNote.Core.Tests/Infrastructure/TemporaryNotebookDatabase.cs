using Microsoft.Data.Sqlite;

namespace MemoriaNote.Core.Tests.Infrastructure;

/// <summary>Creates a fixed schema v1 database without using retired EF Core code.</summary>
internal sealed class TemporaryNotebookDatabase : IDisposable
{
    private bool _disposed;

    internal TemporaryNotebookDatabase()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "MemoriaNote.Tests",
            Guid.NewGuid().ToString("N"));
        DatabasePath = Path.Combine(DirectoryPath, "note.db");
        Directory.CreateDirectory(DirectoryPath);
    }

    internal string DatabasePath { get; }

    internal string DirectoryPath { get; }

    internal void CreateNotebook(string name, string title) =>
        CreateNotebook(name, title, DatabasePath);

    internal void CreateNotebook(string name, string title, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (File.Exists(databasePath))
            throw new ArgumentException("File exists", nameof(databasePath));

        var parent = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE "Contents" (
                "Uuid" TEXT NOT NULL CONSTRAINT "PK_Contents" PRIMARY KEY,
                "Rowid" INTEGER NOT NULL,
                "Name" TEXT NULL,
                "Index" INTEGER NOT NULL,
                "Tags" TEXT NULL,
                "ContentType" TEXT NULL,
                "CreateTime" TEXT NOT NULL,
                "UpdateTime" TEXT NOT NULL,
                "IsErased" INTEGER NOT NULL
            );
            CREATE TABLE "Metadata" (
                "Key" TEXT NOT NULL CONSTRAINT "PK_Metadata" PRIMARY KEY,
                "Value" TEXT NULL
            );
            CREATE TABLE "Pages" (
                "Rowid" INTEGER NOT NULL CONSTRAINT "PK_Pages" PRIMARY KEY AUTOINCREMENT,
                "Uuid" TEXT NOT NULL,
                "Name" TEXT NULL,
                "Index" INTEGER NOT NULL,
                "Tags" TEXT NULL,
                "ContentType" TEXT NULL,
                "CreateTime" TEXT NOT NULL,
                "UpdateTime" TEXT NOT NULL,
                "IsErased" INTEGER NOT NULL,
                "Text" TEXT NULL,
                CONSTRAINT "AK_Pages_Uuid" UNIQUE ("Uuid")
            );
            CREATE INDEX "IX_Contents_Name_Index" ON "Contents" ("Name", "Index");
            CREATE INDEX "IX_Pages_Name_Index" ON "Pages" ("Name", "Index");
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES
                ('20230404004629_InitialCreate', '7.0.3'),
                ('20230404004813_FtsIndexTable', '7.0.3');
            CREATE VIRTUAL TABLE FtsIndex
                USING fts5(Uuid, Name, Tags, Text, content='Pages', content_rowid='Rowid', tokenize='unicode61');
            CREATE TRIGGER Pages_Insert AFTER INSERT ON Pages BEGIN
                INSERT INTO FtsIndex(Rowid, Uuid, Name, Tags, Text)
                    VALUES(new.Rowid, new.Uuid, new.Name, new.Tags, new.Text);
                INSERT INTO Contents(Rowid, Uuid, Name, "Index", Tags, ContentType, CreateTime, UpdateTime, IsErased)
                    VALUES(new.Rowid, new.Uuid, new.Name, new."Index", new.Tags, new.ContentType, new.CreateTime, new.UpdateTime, new.IsErased);
            END;
            CREATE TRIGGER Pages_Delete AFTER DELETE ON Pages BEGIN
                INSERT INTO FtsIndex(FtsIndex, Rowid, Uuid, Name, Tags, Text)
                    VALUES('delete', old.Rowid, old.Uuid, old.Name, old.Tags, old.Text);
                DELETE FROM Contents WHERE Uuid = old.Uuid;
            END;
            CREATE TRIGGER Pages_Update AFTER UPDATE ON Pages BEGIN
                INSERT INTO FtsIndex(FtsIndex, Rowid, Uuid, Name, Tags, Text)
                    VALUES('delete', old.Rowid, old.Uuid, old.Name, old.Tags, old.Text);
                INSERT INTO FtsIndex(Rowid, Uuid, Name, Tags, Text)
                    VALUES(new.Rowid, new.Uuid, new.Name, new.Tags, new.Text);
                DELETE FROM Contents WHERE Uuid = old.Uuid;
                INSERT INTO Contents(Rowid, Uuid, Name, "Index", Tags, ContentType, CreateTime, UpdateTime, IsErased)
                    VALUES(new.Rowid, new.Uuid, new.Name, new."Index", new.Tags, new.ContentType, new.CreateTime, new.UpdateTime, new.IsErased);
            END;
            INSERT INTO Metadata(Key, Value) VALUES
                ('Name', $name),
                ('Title', $title),
                ('Version', '1'),
                ('Description', ''),
                ('Author', ''),
                ('ReadOnly', 'False'),
                ('Tag', ''),
                ('CreateTime', '20260101000000');
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$title", title);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        SqliteConnection.ClearAllPools();
        if (Directory.Exists(DirectoryPath))
            Directory.Delete(DirectoryPath, recursive: true);
        _disposed = true;
    }
}
