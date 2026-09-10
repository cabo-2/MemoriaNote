using System;
using System.Collections.Generic;

namespace MemoriaNote
{
    /// <summary>
    /// Interface for content objects with properties and methods related to content management
    /// </summary>
    public interface IContent
    {
        /// <summary>
        /// Unique identifier for the content
        /// </summary>
        int Rowid { get; set; }
        Guid Guid { get; set; }
        string Uuid { get; set; }
        
        /// <summary>
        /// Name of the content
        /// </summary>
        string Name { get; set; }
        
        /// <summary>
        /// Index of the content
        /// </summary>
        int Index { get; set; }
        
        /// <summary>
        /// Dictionary to store tags related to the content
        /// </summary>
        Dictionary<string, string> TagDict { get; set; }
        
        /// <summary>
        /// Tags for the content
        /// </summary>
        string Tags { get; set; }
        
        /// <summary>
        /// Type of content
        /// </summary>
        string ContentType { get; set; }
        
        /// <summary>
        /// Timestamp for creation of the content
        /// </summary>
        DateTime CreateTime { get; set; }

        /// <summary>
        /// Timestamp for last update of the content
        /// </summary>
        DateTime UpdateTime { get; set; }
        
        /// <summary>
        /// Flag to indicate if the content has been erased
        /// </summary>
        bool IsErased { get; set; }

        /// <summary>
        /// Gets or sets the normalized data source of the note that owns the content.
        /// </summary>
        string OwnerDataSource { get; set; }
        
        /// <summary>
        /// Parent object of the content
        /// </summary>
        object Parent { get; set; }

        /// <summary>
        /// Determines whether this entity and another content entity have the same
        /// non-empty page identifier.
        /// </summary>
        /// <param name="other">The content entity to compare.</param>
        /// <returns>
        /// True when both entities represent the same page; otherwise, false. Two distinct
        /// entities with empty identifiers are never equal.
        /// </returns>
        bool EntityEquals(IContent other);
        
        /// <summary>
        /// Method to convert the content object to a concrete class
        /// </summary>
        /// <returns></returns>
        Content GetContent();
    }
}
