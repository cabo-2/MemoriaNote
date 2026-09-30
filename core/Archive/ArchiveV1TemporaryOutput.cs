using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Archive
{
    internal sealed class ArchiveV1TemporaryOutput : IDisposable
    {
        readonly bool _database;
        bool _disposed;

        ArchiveV1TemporaryOutput(string path, bool database)
        {
            Path = path;
            _database = database;
        }

        internal string Path { get; }

        internal static ArchiveV1TemporaryOutput CreateBeside(
            string destinationPath,
            bool database)
        {
            var fullDestinationPath = System.IO.Path.GetFullPath(destinationPath);
            var directory = System.IO.Path.GetDirectoryName(fullDestinationPath);
            if (string.IsNullOrEmpty(directory))
                throw new IOException("The destination directory could not be resolved.");

            var fileName = System.IO.Path.GetFileName(fullDestinationPath);
            while (true)
            {
                var candidate = System.IO.Path.Combine(
                    directory,
                    "." + fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (new FileStream(
                        candidate,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    {
                    }
                    return new ArchiveV1TemporaryOutput(candidate, database);
                }
                catch (IOException) when (File.Exists(candidate))
                {
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_database)
            {
                SqliteConnection.ClearAllPools();
                DeleteIfExists(Path + "-journal");
                DeleteIfExists(Path + "-shm");
                DeleteIfExists(Path + "-wal");
            }
            DeleteIfExists(Path);
        }

        static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
