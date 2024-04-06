using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace MemoriaNote
{
    /// <summary>
    /// Represents a Page object which implements the IContent interface and IEquatable interface for comparing equality with other Page objects.
    /// This class is marked as serializable for supporting serialization operations.
    /// </summary>
    [Serializable]
    public class Page : IContent, IEquatable<Page>
    {
        /// <summary>
        /// Creates a new Page object with the specified name, text, and optional directory.
        /// </summary>
        /// <param name="name">The name of the page.</param>
        /// <param name="text">The text content of the page.</param>
        /// <param name="dir">Optional directory for the page.</param>
        /// <returns>The newly created Page object.</returns>
        public static Page Create(string name, string text, string dir = null)
        {
            var page = Content.Create<Page>(name, dir);
            page.Text = text;
            return page;
        }

        //[Key]
        //[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        /// <summary>
        /// Represents the unique identifier and corresponding UUID for a Page object.
        /// </summary>
        public int Rowid { get; set; }

        /// <summary>
        /// Represents the universally unique identifier (UUID) of a Page object.
        /// </summary>
        [NotMapped]
        public Guid Guid { get; set; }

        /// <summary>
        /// Gets or sets the UUID string representation for the Guid property.
        /// </summary>
        [JsonIgnore]
        public string Uuid
        {
            get => Guid.ToUuid();
            set
            {
                if (string.IsNullOrEmpty(value))
                    this.Guid = Guid.Empty;
                else
                    this.Guid = Guid.Parse(value);
            }
        }
        /// <summary>
        /// Represents the name of the page.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Represents the index of the page.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Represents a dictionary of tags associated with the page.
        /// </summary>
        [NotMapped]
        public Dictionary<string, string> TagDict { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Gets or sets the JSON representation of the TagDict property.
        /// </summary>
        [JsonIgnore]
        public string Tags
        {
            get
            {
                if (TagDict == null || TagDict.Count == 0)
                    return null;

                return JsonConvert.SerializeObject(TagDict);
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    TagDict = new Dictionary<string, string>();
                else
                    TagDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(value);
            }
        }
        /// <summary>
        /// Represents the content type of the page.
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// Returns a string representation of the Page object.
        /// If the Name property is not null, returns the Name.
        /// If the Index is 1, returns just the Name.
        /// Otherwise, returns the concatenated Name and Index as a string.
        /// If the Name is null, returns "Rowid=" followed by the Rowid value.
        /// </summary>
        /// <returns>A string representation of the Page object.</returns>
        public override string ToString()
        {
            if (Name != null)
                if (Index == 1)
                    return Name;
                else
                    return Name + Index.ToIndexString();
            else
                return "Rowid=" + Rowid;
        }

        /// <summary>
        /// Represents the date and time when the page was created.
        /// </summary>
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// Represents the date and time when the page was last updated.
        /// </summary>
        public DateTime UpdateTime { get; set; }

        /// <summary>
        /// Indicates whether the page has been erased.
        /// </summary>
        public bool IsErased { get; set; }
        /// <summary>
        /// Represents the parent object of the page, not mapped to the database and ignored during JSON serialization.
        /// </summary>
        [NotMapped, JsonIgnore]
        public object Parent { get; set; }

        /// <summary>
        /// Represents the text content of the page.
        /// </summary>
        public string Text { get; set; }

        public bool EntityEquals(IContent other)
        {
            return Content.EntityEquals(this, other);
        }

        /// <summary>
        /// Determines whether the current Page object is equal to another Page object by comparing their entities and hash codes.
        /// If the entities are equal, it further compares their hash codes for equality.
        /// </summary>
        /// <param name="other">The Page object to compare with the current Page object.</param>
        /// <returns>True if the current Page object is equal to the specified Page object; otherwise, false.</returns>
        public bool Equals(Page other)
        {
            if (EntityEquals(other))
                return this.GetHashCode() == other.GetHashCode();
            else
                return false;
        }

        /// <summary>
        /// Determines whether the current Page object is equal to another object by checking if the other object is a Page object.
        /// If it is a Page object, it further calls the Equals method to compare them.
        /// </summary>
        /// <param name="obj">The object to compare with the current Page object.</param>
        /// <returns>True if the current Page object is equal to the specified object and they are of the same type; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            return this.Equals(obj as Page);
        }

        /// <summary>
        /// Serves as a hash function for a Page object and is suitable for use in hashing algorithms and data structures like hash tables.
        /// The hash code is calculated based on the hash codes of the Content property and the Text property.
        /// </summary>
        /// <returns>A hash code value for the current Page object.</returns>
        public override int GetHashCode()
        {
            return Content.GetHashCode(this) ^
                   (Text == null ? 4 : Text.GetHashCode());
        }

        /// <summary>
        /// Updates the last modified date and time of the Page object to the current UTC time.
        /// </summary>
        public void UpdateLastModified()
        {
            this.UpdateTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Gets the content of the Page object by creating a Content object from it.
        /// </summary>
        /// <returns>The Content object created from the Page object.</returns>
        public Content GetContent()
        {
            return Content.Create(this);
        }
    }

    /// <summary>
    /// Represents a static class that defines the name of a directory for page tags.
    /// The Dir property returns the name of the directory as a string.
    /// </summary>
    public class PageTag
    {
        public static string Dir => nameof(Dir);
    }
}
