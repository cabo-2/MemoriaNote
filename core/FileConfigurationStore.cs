using System;
using System.Globalization;
using System.IO;
using System.Text;

// cspell:ignore mmssfffffff

namespace MemoriaNote
{
    /// <summary>
    /// Persists text-serialized configuration with atomic replacement and recovery.
    /// </summary>
    /// <typeparam name="TConfiguration">The persisted configuration type.</typeparam>
    public sealed class FileConfigurationStore<TConfiguration>
        : IConfigurationStore<TConfiguration>
        where TConfiguration : class
    {
        readonly string _configurationPath;
        readonly IConfigurationSerializer<TConfiguration> _serializer;
        readonly Func<TConfiguration> _createDefault;

        /// <summary>Initializes a file-backed configuration store.</summary>
        /// <param name="configurationPath">The configuration file path.</param>
        /// <param name="serializer">The external format serializer.</param>
        /// <param name="createDefault">Creates default configuration.</param>
        public FileConfigurationStore(
            string configurationPath,
            IConfigurationSerializer<TConfiguration> serializer,
            Func<TConfiguration> createDefault)
        {
            if (string.IsNullOrWhiteSpace(configurationPath))
                throw new ArgumentException(
                    "A configuration path is required.",
                    nameof(configurationPath));

            _configurationPath = Path.GetFullPath(configurationPath);
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _createDefault = createDefault ?? throw new ArgumentNullException(nameof(createDefault));
        }

        /// <inheritdoc/>
        public ConfigurationLoadResult<TConfiguration> Load()
        {
            if (!File.Exists(_configurationPath))
            {
                var created = CreateDefault();
                Save(created);
                return new ConfigurationLoadResult<TConfiguration>(
                    created,
                    ConfigurationLoadStatus.CreatedDefault);
            }

            try
            {
                var configuration = _serializer.Deserialize(
                    File.ReadAllText(_configurationPath));
                return new ConfigurationLoadResult<TConfiguration>(
                    configuration,
                    ConfigurationLoadStatus.Loaded);
            }
            catch (ConfigurationFormatException)
            {
                var recoveryPath = QuarantineInvalidConfiguration();
                var recovered = CreateDefault();
                Save(recovered);
                return new ConfigurationLoadResult<TConfiguration>(
                    recovered,
                    ConfigurationLoadStatus.RecoveredInvalid,
                    recoveryPath);
            }
        }

        /// <inheritdoc/>
        public void Save(TConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var serializedConfiguration = _serializer.Serialize(configuration);
            var directory = Path.GetDirectoryName(_configurationPath);
            Directory.CreateDirectory(directory);

            var temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_configurationPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                WriteTemporaryFile(temporaryPath, serializedConfiguration);
                if (File.Exists(_configurationPath))
                    File.Replace(temporaryPath, _configurationPath, null);
                else
                    File.Move(temporaryPath, _configurationPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        TConfiguration CreateDefault()
        {
            return _createDefault() ??
                throw new InvalidOperationException(
                    "The default configuration factory returned null.");
        }

        string QuarantineInvalidConfiguration()
        {
            var timestamp = DateTimeOffset.UtcNow.ToString(
                "yyyyMMdd'T'HHmmssfffffff'Z'",
                CultureInfo.InvariantCulture);
            var baseRecoveryPath = $"{_configurationPath}.corrupt-{timestamp}";
            var recoveryPath = baseRecoveryPath;
            var sequence = 1;
            while (File.Exists(recoveryPath))
            {
                recoveryPath = $"{baseRecoveryPath}.{sequence}";
                sequence++;
            }

            File.Move(_configurationPath, recoveryPath);
            return recoveryPath;
        }

        static void WriteTemporaryFile(string temporaryPath, string content)
        {
            using var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(content);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
    }
}
