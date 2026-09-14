using System;

namespace MemoriaNote
{
    /// <summary>Identifies how persisted configuration was obtained.</summary>
    public enum ConfigurationLoadStatus
    {
        /// <summary>The existing configuration was loaded.</summary>
        Loaded,

        /// <summary>No configuration existed, so defaults were created.</summary>
        CreatedDefault,

        /// <summary>An invalid configuration was quarantined and defaults were created.</summary>
        RecoveredInvalid
    }

    /// <summary>Describes the result of loading persisted configuration.</summary>
    /// <typeparam name="TConfiguration">The persisted configuration type.</typeparam>
    public sealed class ConfigurationLoadResult<TConfiguration>
        where TConfiguration : class
    {
        /// <summary>Initializes a configuration load result.</summary>
        /// <param name="configuration">The loaded or default configuration.</param>
        /// <param name="status">The way the configuration was obtained.</param>
        /// <param name="recoveryArtifactPath">The quarantined path, if any.</param>
        public ConfigurationLoadResult(
            TConfiguration configuration,
            ConfigurationLoadStatus status,
            string recoveryArtifactPath = null)
        {
            Configuration = configuration ??
                throw new ArgumentNullException(nameof(configuration));
            Status = status;
            RecoveryArtifactPath = recoveryArtifactPath;
        }

        /// <summary>Gets the loaded or default configuration.</summary>
        public TConfiguration Configuration { get; }

        /// <summary>Gets the way the configuration was obtained.</summary>
        public ConfigurationLoadStatus Status { get; }

        /// <summary>Gets the quarantined file path, if recovery occurred.</summary>
        public string RecoveryArtifactPath { get; }
    }

    /// <summary>Loads and saves configuration independently of its format.</summary>
    /// <typeparam name="TConfiguration">The persisted configuration type.</typeparam>
    public interface IConfigurationStore<TConfiguration>
        where TConfiguration : class
    {
        /// <summary>Loads configuration, creating defaults when necessary.</summary>
        /// <returns>The configuration and recovery information.</returns>
        ConfigurationLoadResult<TConfiguration> Load();

        /// <summary>Saves the specified configuration.</summary>
        /// <param name="configuration">The configuration to save.</param>
        void Save(TConfiguration configuration);
    }

    /// <summary>Converts configuration to and from an external text format.</summary>
    /// <typeparam name="TConfiguration">The persisted configuration type.</typeparam>
    public interface IConfigurationSerializer<TConfiguration>
        where TConfiguration : class
    {
        /// <summary>Serializes configuration to text.</summary>
        /// <param name="configuration">The configuration to serialize.</param>
        /// <returns>The serialized representation.</returns>
        string Serialize(TConfiguration configuration);

        /// <summary>Deserializes configuration from text.</summary>
        /// <param name="serializedConfiguration">The serialized representation.</param>
        /// <returns>The deserialized configuration.</returns>
        /// <exception cref="ConfigurationFormatException">
        /// Thrown when the representation is invalid.
        /// </exception>
        TConfiguration Deserialize(string serializedConfiguration);
    }

    /// <summary>Represents invalid serialized configuration content.</summary>
    public sealed class ConfigurationFormatException : Exception
    {
        /// <summary>Initializes an invalid configuration exception.</summary>
        /// <param name="message">The error message.</param>
        public ConfigurationFormatException(string message) : base(message)
        {
        }

        /// <summary>Initializes an invalid configuration exception.</summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The format-specific exception.</param>
        public ConfigurationFormatException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
