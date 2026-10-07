using System;
using System.IO;
using System.Text;

namespace MemoriaNote.Cli.UserConfig
{
    internal enum UserConfigurationLoadStatus
    {
        Missing,
        Loaded
    }

    internal sealed class UserConfigurationLoadResult
    {
        UserConfigurationLoadResult(
            UserConfiguration configuration,
            UserConfigurationLoadStatus status)
        {
            Configuration = configuration;
            Status = status;
        }

        internal UserConfiguration Configuration { get; }

        internal UserConfigurationLoadStatus Status { get; }

        internal static UserConfigurationLoadResult Missing { get; } =
            new UserConfigurationLoadResult(
                null,
                UserConfigurationLoadStatus.Missing);

        internal static UserConfigurationLoadResult FromConfiguration(
            UserConfiguration configuration)
        {
            return new UserConfigurationLoadResult(
                configuration ?? throw new ArgumentNullException(nameof(configuration)),
                UserConfigurationLoadStatus.Loaded);
        }
    }

    internal interface IUserConfigurationStore
    {
        UserConfigurationLoadResult Load();
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

    internal sealed class UserConfigurationStore : IUserConfigurationStore
    {
        static readonly Encoding StrictUtf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        readonly string _configurationPath;

        internal UserConfigurationStore(ApplicationPaths applicationPaths)
        {
            _configurationPath = UserConfigurationContract.GetPath(applicationPaths);
        }

        public UserConfigurationLoadResult Load()
        {
            RejectSymbolicLink();
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
                UserConfigurationCodec.Deserialize(content));
        }

        void RejectSymbolicLink()
        {
            var file = new FileInfo(_configurationPath);
            if (file.LinkTarget != null)
            {
                throw new UserConfigurationFormatException(
                    $"The user configuration cannot be a symbolic link: {_configurationPath}");
            }
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
