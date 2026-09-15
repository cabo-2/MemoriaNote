using System;
using Newtonsoft.Json;

namespace MemoriaNote.Cli
{
    /// <summary>
    /// Represents the JSON document exchanged with the external notebook metadata editor.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class NotebookMetadataEditDocument
    {
        /// <summary>Gets or sets the notebook name.</summary>
        [JsonProperty(Order = 0)]
        public string Name { get; set; }

        /// <summary>Gets or sets the notebook title.</summary>
        [JsonProperty(Order = 1)]
        public string Title { get; set; }

        /// <summary>Gets or sets the notebook format version.</summary>
        [JsonProperty(Order = 2)]
        public string Version { get; set; }

        /// <summary>Gets or sets the notebook description.</summary>
        [JsonProperty(Order = 3)]
        public string Description { get; set; }

        /// <summary>Gets or sets the notebook author.</summary>
        [JsonProperty(Order = 4)]
        public string Author { get; set; }

        /// <summary>Gets or sets whether the notebook is read-only.</summary>
        [JsonProperty(Order = 5)]
        public bool ReadOnly { get; set; }

        /// <summary>Gets or sets the notebook tag.</summary>
        [JsonProperty(Order = 6)]
        public string Tag { get; set; }

        /// <summary>Gets or sets the notebook creation time.</summary>
        [JsonProperty(Order = 7)]
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// Gets or sets the compatibility data-source value shown by the legacy editor document.
        /// </summary>
        /// <remarks>Changes to this value do not move the notebook or change its target.</remarks>
        [JsonProperty(Order = 8)]
        public string DataSource { get; set; }

        /// <summary>Creates an editable document from a metadata snapshot.</summary>
        /// <param name="metadata">The persisted metadata snapshot.</param>
        /// <returns>A document containing the current metadata and compatibility path.</returns>
        internal static NotebookMetadataEditDocument Create(NotebookMetadata metadata)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            return new NotebookMetadataEditDocument
            {
                Name = metadata.Name,
                Title = metadata.Title,
                Version = metadata.Version,
                Description = metadata.Description,
                Author = metadata.Author,
                ReadOnly = metadata.ReadOnly,
                Tag = metadata.Tag,
                CreateTime = metadata.CreateTime,
                DataSource = metadata.DatabasePath
            };
        }

        /// <summary>Converts editable metadata fields to a core update request.</summary>
        /// <returns>An immutable metadata update without the compatibility data source.</returns>
        internal NotebookMetadataUpdate ToUpdate()
        {
            return new NotebookMetadataUpdate(
                Name,
                Title,
                Version,
                Description,
                Author,
                ReadOnly,
                Tag,
                CreateTime);
        }
    }
}
