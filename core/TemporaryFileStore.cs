using System;
using System.Collections.Generic;
using System.IO;

namespace MemoriaNote
{
    /// <summary>Represents an exclusively reserved temporary file.</summary>
    public interface ITemporaryFile : IDisposable
    {
        /// <summary>Gets the reserved file path.</summary>
        string Path { get; }
    }

    /// <summary>Reserves and releases temporary files owned by one application process.</summary>
    public interface ITemporaryFileStore : IDisposable
    {
        /// <summary>Reserves a unique temporary file.</summary>
        /// <param name="fileName">An optional source file name used for the extension and stem.</param>
        /// <returns>A lease that deletes the reserved file when disposed.</returns>
        ITemporaryFile CreateFile(string fileName = null);
    }

    /// <summary>Creates temporary files beneath a dedicated working directory.</summary>
    public sealed class TemporaryFileStore : ITemporaryFileStore
    {
        readonly object _syncRoot = new object();
        readonly HashSet<TemporaryFileLease> _leases =
            new HashSet<TemporaryFileLease>();
        bool _disposed;

        /// <summary>Initializes a temporary-file store.</summary>
        /// <param name="workingDirectory">The directory in which files are reserved.</param>
        public TemporaryFileStore(string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                throw new ArgumentException(
                    "A temporary working directory is required.",
                    nameof(workingDirectory));
            }

            WorkingDirectory = System.IO.Path.GetFullPath(workingDirectory);
        }

        /// <summary>Gets the temporary working directory.</summary>
        public string WorkingDirectory { get; }

        /// <inheritdoc/>
        public ITemporaryFile CreateFile(string fileName = null)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                Directory.CreateDirectory(WorkingDirectory);

                while (true)
                {
                    var path = CreateCandidatePath(fileName);
                    try
                    {
                        using (new FileStream(
                            path,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None))
                        {
                        }

                        var lease = new TemporaryFileLease(path, Release);
                        _leases.Add(lease);
                        return lease;
                    }
                    catch (IOException) when (File.Exists(path))
                    {
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            TemporaryFileLease[] leases;
            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                _disposed = true;
                leases = new TemporaryFileLease[_leases.Count];
                _leases.CopyTo(leases);
            }

            foreach (var lease in leases)
                lease.Dispose();
        }

        string CreateCandidatePath(string fileName)
        {
            var randomName = System.IO.Path.GetRandomFileName().Replace(".", string.Empty);
            if (string.IsNullOrWhiteSpace(fileName))
                return System.IO.Path.Combine(WorkingDirectory, randomName + ".txt");

            var safeFileName = System.IO.Path.GetFileName(fileName);
            var stem = System.IO.Path.GetFileNameWithoutExtension(safeFileName);
            var extension = System.IO.Path.GetExtension(safeFileName);
            return System.IO.Path.Combine(
                WorkingDirectory,
                stem + "_" + randomName + extension);
        }

        void Release(TemporaryFileLease lease)
        {
            lock (_syncRoot)
                _leases.Remove(lease);

            if (File.Exists(lease.Path))
                File.Delete(lease.Path);
        }

        void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TemporaryFileStore));
        }

        sealed class TemporaryFileLease : ITemporaryFile
        {
            readonly Action<TemporaryFileLease> _release;
            bool _disposed;

            internal TemporaryFileLease(
                string path,
                Action<TemporaryFileLease> release)
            {
                Path = path;
                _release = release;
            }

            public string Path { get; }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _release(this);
            }
        }
    }
}
