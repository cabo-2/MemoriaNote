using System;
using Newtonsoft.Json;

namespace MemoriaNote
{
    /// <summary>Serializes configuration using the existing JSON contract.</summary>
    /// <typeparam name="TConfiguration">The persisted configuration type.</typeparam>
    public sealed class JsonConfigurationSerializer<TConfiguration>
        : IConfigurationSerializer<TConfiguration>
        where TConfiguration : class
    {
        /// <inheritdoc/>
        public string Serialize(TConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            return JsonConvert.SerializeObject(configuration, Formatting.Indented);
        }

        /// <inheritdoc/>
        public TConfiguration Deserialize(string serializedConfiguration)
        {
            if (serializedConfiguration == null)
                throw new ArgumentNullException(nameof(serializedConfiguration));

            try
            {
                return JsonConvert.DeserializeObject<TConfiguration>(
                        serializedConfiguration) ??
                    throw new ConfigurationFormatException(
                        "The configuration did not contain an object.");
            }
            catch (JsonException exception)
            {
                throw new ConfigurationFormatException(
                    "The configuration JSON is invalid.",
                    exception);
            }
        }
    }
}
