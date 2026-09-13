using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace MemoriaNote
{
    /// <summary>
    /// Represents the SQLite read-model row stored in the existing Contents table.
    /// </summary>
    /// <remarks>
    /// Pages are authoritative. SQLite triggers maintain these body-free rows for listing and
    /// heading search operations.
    /// </remarks>
    public sealed class PageSummaryRecord
    {
        /// <summary>Gets or sets the SQLite row identifier.</summary>
        public int Rowid { get; set; }

        /// <summary>Gets or sets the stable page identifier.</summary>
        [NotMapped]
        public Guid Guid { get; set; }

        /// <summary>Gets or sets the persisted UUID representation.</summary>
        public string Uuid
        {
            get => Guid.ToUuid();
            set => Guid = string.IsNullOrEmpty(value) ? Guid.Empty : Guid.Parse(value);
        }

        /// <summary>Gets or sets the persisted page name.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the one-based index within an exact-name group.</summary>
        public int Index { get; set; }

        /// <summary>Gets or sets the deserialized page tags.</summary>
        [NotMapped]
        public Dictionary<string, string> TagDict { get; set; } =
            new Dictionary<string, string>();

        /// <summary>Gets or sets the JSON representation stored in the existing Tags column.</summary>
        [JsonIgnore]
        public string Tags
        {
            get => TagDict == null || TagDict.Count == 0
                ? null
                : JsonConvert.SerializeObject(TagDict);
            set => TagDict = string.IsNullOrWhiteSpace(value)
                ? new Dictionary<string, string>()
                : JsonConvert.DeserializeObject<Dictionary<string, string>>(value);
        }

        /// <summary>Gets or sets the persisted content type.</summary>
        public string ContentType { get; set; }

        /// <summary>Gets or sets the persisted creation time.</summary>
        public DateTime CreateTime { get; set; }

        /// <summary>Gets or sets the persisted last-update time.</summary>
        public DateTime UpdateTime { get; set; }

        /// <summary>Gets or sets whether the page is marked as erased.</summary>
        public bool IsErased { get; set; }
    }
}
