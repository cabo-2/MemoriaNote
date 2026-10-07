using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MemoriaNote.Cli.UserConfig
{
    internal enum UserConfigurationLoadStatus
    {
        Missing,
        Loaded
    }

    internal sealed class UserConfigurationRevision :
        IEquatable<UserConfigurationRevision>
    {
        readonly string _fingerprint;

        UserConfigurationRevision(string fingerprint)
        {
            _fingerprint = fingerprint;
        }

        internal static UserConfigurationRevision Missing { get; } =
            new UserConfigurationRevision(null);

        internal bool IsMissing => _fingerprint == null;

        internal static UserConfigurationRevision FromContent(byte[] content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            return new UserConfigurationRevision(
                Convert.ToHexString(SHA256.HashData(content)));
        }

        public bool Equals(UserConfigurationRevision other)
        {
            return other != null &&
                string.Equals(
                    _fingerprint,
                    other._fingerprint,
                    StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as UserConfigurationRevision);
        }

        public override int GetHashCode()
        {
            return _fingerprint == null
                ? 0
                : StringComparer.Ordinal.GetHashCode(_fingerprint);
        }
    }

    internal sealed class UserConfigurationLoadResult
    {
        UserConfigurationLoadResult(
            UserConfiguration configuration,
            UserConfigurationLoadStatus status,
            UserConfigurationRevision revision)
        {
            Configuration = configuration;
            Status = status;
            Revision = revision ?? throw new ArgumentNullException(nameof(revision));
        }

        internal UserConfiguration Configuration { get; }

        internal UserConfigurationLoadStatus Status { get; }

        internal UserConfigurationRevision Revision { get; }

        internal static UserConfigurationLoadResult Missing { get; } =
            new UserConfigurationLoadResult(
                null,
                UserConfigurationLoadStatus.Missing,
                UserConfigurationRevision.Missing);

        internal static UserConfigurationLoadResult FromConfiguration(
            UserConfiguration configuration,
            UserConfigurationRevision revision)
        {
            if (revision == null)
                throw new ArgumentNullException(nameof(revision));
            if (revision.IsMissing)
            {
                throw new ArgumentException(
                    "A loaded user configuration requires a content revision.",
                    nameof(revision));
            }

            return new UserConfigurationLoadResult(
                configuration ?? throw new ArgumentNullException(nameof(configuration)),
                UserConfigurationLoadStatus.Loaded,
                revision);
        }
    }

    internal enum UserConfigurationSaveStatus
    {
        Saved,
        Conflict
    }

    internal sealed class UserConfigurationSaveResult
    {
        UserConfigurationSaveResult(
            UserConfigurationSaveStatus status,
            UserConfigurationLoadResult current)
        {
            Status = status;
            Current = current ?? throw new ArgumentNullException(nameof(current));
        }

        internal UserConfigurationSaveStatus Status { get; }

        internal UserConfigurationLoadResult Current { get; }

        internal static UserConfigurationSaveResult Saved(
            UserConfigurationLoadResult current)
        {
            return new UserConfigurationSaveResult(
                UserConfigurationSaveStatus.Saved,
                current);
        }

        internal static UserConfigurationSaveResult Conflict(
            UserConfigurationLoadResult current)
        {
            return new UserConfigurationSaveResult(
                UserConfigurationSaveStatus.Conflict,
                current);
        }
    }

    internal interface IUserConfigurationStore
    {
        UserConfigurationLoadResult Load();

        UserConfigurationSaveResult Save(
            UserConfiguration configuration,
            UserConfigurationRevision expectedRevision);
    }

    internal class UserConfigurationFormatException : Exception
    {
        internal UserConfigurationFormatException(string message)
            : base(message)
        {
        }

        internal UserConfigurationFormatException(
            string message,
            Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal sealed class UnsupportedUserConfigurationVersionException :
        UserConfigurationFormatException
    {
        internal UnsupportedUserConfigurationVersionException(long formatVersion)
            : base($"The user configuration format version '{formatVersion}' is not supported.")
        {
            FormatVersion = formatVersion;
        }

        internal long FormatVersion { get; }
    }

    internal sealed class UserConfigurationSaveException : Exception
    {
        internal UserConfigurationSaveException(
            string message,
            Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal sealed class UserConfigurationStore : IUserConfigurationStore
    {
        const int LockRetryCount = 200;
        const int LockRetryDelayMilliseconds = 25;

        const UnixFileMode PrivateDirectoryMode =
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute;

        const UnixFileMode PrivateFileMode =
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite;

        static readonly Encoding StrictUtf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        readonly string _applicationDataDirectory;
        readonly string _configurationPath;
        readonly string _lockPath;
        readonly Action<string> _afterCommit;

        internal UserConfigurationStore(ApplicationPaths applicationPaths)
            : this(applicationPaths, null)
        {
        }

        internal UserConfigurationStore(
            ApplicationPaths applicationPaths,
            Action<string> afterCommit)
        {
            if (applicationPaths == null)
                throw new ArgumentNullException(nameof(applicationPaths));

            _applicationDataDirectory = applicationPaths.ApplicationDataDirectory;
            _configurationPath = UserConfigurationContract.GetPath(applicationPaths);
            _lockPath = Path.Combine(
                _applicationDataDirectory,
                $".{UserConfigurationContract.FileName}.lock");
            _afterCommit = afterCommit;
        }

        public UserConfigurationLoadResult Load()
        {
            RejectSymbolicLink(_configurationPath, "configuration");
            if (Directory.Exists(_configurationPath))
            {
                throw new UserConfigurationFormatException(
                    $"The user configuration path is not a file: {_configurationPath}");
            }
            if (!File.Exists(_configurationPath))
                return UserConfigurationLoadResult.Missing;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(_configurationPath);
            }
            catch (FileNotFoundException)
            {
                return UserConfigurationLoadResult.Missing;
            }
            catch (DirectoryNotFoundException)
            {
                return UserConfigurationLoadResult.Missing;
            }

            if (HasUtf8ByteOrderMark(bytes))
            {
                throw new UserConfigurationFormatException(
                    "The user configuration must not contain a UTF-8 byte-order mark.");
            }

            string content;
            try
            {
                content = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new UserConfigurationFormatException(
                    "The user configuration is not valid UTF-8.",
                    exception);
            }

            if (content.Contains('\r'))
            {
                throw new UserConfigurationFormatException(
                    "The user configuration must use LF line endings.");
            }
            if (!content.EndsWith('\n'))
            {
                throw new UserConfigurationFormatException(
                    "The user configuration must end with a line feed.");
            }

            return UserConfigurationLoadResult.FromConfiguration(
                UserConfigurationCodec.Deserialize(content),
                UserConfigurationRevision.FromContent(bytes));
        }

        public UserConfigurationSaveResult Save(
            UserConfiguration configuration,
            UserConfigurationRevision expectedRevision)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            if (expectedRevision == null)
                throw new ArgumentNullException(nameof(expectedRevision));

            var serialized = UserConfigurationCodec.Serialize(configuration);
            EnsureApplicationDataDirectory();
            using var coordinationLock = AcquireCoordinationLock();

            var current = Load();
            if (!current.Revision.Equals(expectedRevision))
                return UserConfigurationSaveResult.Conflict(current);

            var temporaryPath = Path.Combine(
                _applicationDataDirectory,
                $".{UserConfigurationContract.FileName}.{Guid.NewGuid():N}.tmp");
            var backupPath = Path.Combine(
                _applicationDataDirectory,
                $".{UserConfigurationContract.FileName}.{Guid.NewGuid():N}.bak");
            try
            {
                WriteTemporaryFile(temporaryPath, serialized);
                if (current.Status == UserConfigurationLoadStatus.Missing)
                {
                    try
                    {
                        File.Move(temporaryPath, _configurationPath);
                    }
                    catch (IOException) when (File.Exists(_configurationPath))
                    {
                        return UserConfigurationSaveResult.Conflict(Load());
                    }
                }
                else
                {
                    try
                    {
                        File.Replace(temporaryPath, _configurationPath, backupPath);
                    }
                    catch (IOException) when (!File.Exists(_configurationPath))
                    {
                        return UserConfigurationSaveResult.Conflict(Load());
                    }
                }

                return VerifyCommittedConfiguration(
                    configuration,
                    current,
                    backupPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        UserConfigurationSaveResult VerifyCommittedConfiguration(
            UserConfiguration expected,
            UserConfigurationLoadResult previous,
            string backupPath)
        {
            UserConfigurationLoadResult saved;
            try
            {
                _afterCommit?.Invoke(_configurationPath);
                saved = Load();
                if (saved.Status != UserConfigurationLoadStatus.Loaded ||
                    !SemanticallyEquals(expected, saved.Configuration))
                {
                    throw new InvalidDataException(
                        "The saved user configuration did not match the requested values.");
                }
            }
            catch (Exception verificationException)
            {
                try
                {
                    RestorePreviousConfiguration(previous, backupPath);
                }
                catch (Exception restoreException)
                {
                    throw new UserConfigurationSaveException(
                        "The saved user configuration could not be verified or restored. " +
                        $"A recovery file may remain at '{backupPath}'.",
                        new AggregateException(
                            verificationException,
                            restoreException));
                }

                throw new UserConfigurationSaveException(
                    "The saved user configuration could not be verified. " +
                    "The previous state was restored.",
                    verificationException);
            }

            if (File.Exists(backupPath))
            {
                try
                {
                    File.Delete(backupPath);
                }
                catch (Exception cleanupException) when (File.Exists(backupPath))
                {
                    try
                    {
                        RestorePreviousConfiguration(previous, backupPath);
                    }
                    catch (Exception restoreException)
                    {
                        throw new UserConfigurationSaveException(
                            "The previous user configuration backup could not be " +
                            $"removed or restored. A recovery file may remain at '{backupPath}'.",
                            new AggregateException(
                                cleanupException,
                                restoreException));
                    }

                    throw new UserConfigurationSaveException(
                        "The previous user configuration backup could not be removed. " +
                        "The previous state was restored.",
                        cleanupException);
                }
            }
            return UserConfigurationSaveResult.Saved(saved);
        }

        void RestorePreviousConfiguration(
            UserConfigurationLoadResult previous,
            string backupPath)
        {
            if (previous.Status == UserConfigurationLoadStatus.Missing)
            {
                if (File.Exists(_configurationPath))
                    File.Delete(_configurationPath);
            }
            else
            {
                if (!File.Exists(backupPath))
                {
                    throw new FileNotFoundException(
                        "The previous user configuration backup is missing.",
                        backupPath);
                }

                if (File.Exists(_configurationPath))
                    File.Replace(backupPath, _configurationPath, null);
                else
                    File.Move(backupPath, _configurationPath);
            }

            var restored = Load();
            if (!restored.Revision.Equals(previous.Revision))
            {
                throw new InvalidDataException(
                    "The restored user configuration did not match the previous revision.");
            }
        }

        FileStream AcquireCoordinationLock()
        {
            RejectSymbolicLink(_lockPath, "coordination lock");
            if (Directory.Exists(_lockPath))
            {
                throw new IOException(
                    $"The user configuration coordination lock is not a file: {_lockPath}");
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var stream = new FileStream(
                        _lockPath,
                        CreateFileOptions(FileMode.OpenOrCreate, FileAccess.ReadWrite));
                    try
                    {
                        RestrictFilePermissions(_lockPath);
                        return stream;
                    }
                    catch
                    {
                        stream.Dispose();
                        throw;
                    }
                }
                catch (IOException) when (attempt < LockRetryCount)
                {
                    Thread.Sleep(LockRetryDelayMilliseconds);
                }
            }
        }

        void EnsureApplicationDataDirectory()
        {
            if (Directory.Exists(_applicationDataDirectory))
                return;

            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(_applicationDataDirectory);
            }
            else
            {
                Directory.CreateDirectory(
                    _applicationDataDirectory,
                    PrivateDirectoryMode);
            }
        }

        static void WriteTemporaryFile(string path, string content)
        {
            using var stream = new FileStream(
                path,
                CreateFileOptions(FileMode.CreateNew, FileAccess.Write));
            var bytes = StrictUtf8.GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }

        static FileStreamOptions CreateFileOptions(
            FileMode mode,
            FileAccess access)
        {
            var options = new FileStreamOptions
            {
                Mode = mode,
                Access = access,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = PrivateFileMode;
            return options;
        }

        static void RestrictFilePermissions(string path)
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, PrivateFileMode);
        }

        static void RejectSymbolicLink(string path, string description)
        {
            var file = new FileInfo(path);
            if (file.LinkTarget != null)
            {
                throw new UserConfigurationFormatException(
                    $"The user configuration {description} cannot be a symbolic link: {path}");
            }
        }

        static bool SemanticallyEquals(
            UserConfiguration expected,
            UserConfiguration actual)
        {
            if (expected.FormatVersion != actual.FormatVersion ||
                expected.Editor.Selection != actual.Editor.Selection ||
                !string.Equals(
                    expected.Editor.Variable,
                    actual.Editor.Variable,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    expected.Editor.Executable,
                    actual.Editor.Executable,
                    StringComparison.Ordinal) ||
                expected.Editor.Arguments.Count != actual.Editor.Arguments.Count)
            {
                return false;
            }

            for (var index = 0; index < expected.Editor.Arguments.Count; index++)
            {
                if (!string.Equals(
                    expected.Editor.Arguments[index],
                    actual.Editor.Arguments[index],
                    StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        static bool HasUtf8ByteOrderMark(byte[] bytes)
        {
            return bytes.Length >= 3 &&
                bytes[0] == 0xef &&
                bytes[1] == 0xbb &&
                bytes[2] == 0xbf;
        }
    }
}
