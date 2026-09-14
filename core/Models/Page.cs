using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace MemoriaNote
{
    /// <summary>
    /// Represents a persisted page with its body and metadata.
    /// This class is marked as serializable for supporting serialization operations.
    /// </summary>
    [Serializable]
    public class Page : IEquatable<Page>
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
            return CreateAt(name, text, SystemClock.Instance.UtcNow.UtcDateTime, dir);
        }

        /// <summary>
        /// Creates a new page using an explicit UTC creation time.
        /// </summary>
        /// <param name="name">The name of the page.</param>
        /// <param name="text">The text content of the page.</param>
        /// <param name="createdUtc">The UTC creation time.</param>
        /// <param name="dir">Optional directory for the page.</param>
        /// <returns>The newly created page.</returns>
        public static Page CreateAt(
            string name,
            string text,
            DateTime createdUtc,
            string dir = null)
        {
            var page = new Page
            {
                Rowid = 0,
                Guid = Guid.NewGuid(),
                Name = name,
                Index = 1,
                // Preserve the existing persisted discriminator produced by the generic factory.
                ContentType = "T",
                CreateTime = createdUtc,
                IsErased = false,
                Text = text
            };
            page.UpdateTime = page.CreateTime;
            if (dir != null)
                page.TagDict.Add(PageTag.Dir, dir);

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
        /// Gets or sets the one-based display number that distinguishes dictionary senses
        /// with the same name using exact matching. The value is managed by Notebook page operations.
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
        /// Represents the text content of the page.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Determines whether this page and another page have the same non-empty page identifier.
        /// </summary>
        /// <param name="other">The Page object to compare with the current Page object.</param>
        /// <returns>True if the current Page object is equal to the specified Page object; otherwise, false.</returns>
        public bool Equals(Page other)
        {
            return PageIdentity.Equals(this, other);
        }

        /// <summary>
        /// Determines whether this page and another object are pages with the same non-empty
        /// identifier.
        /// </summary>
        /// <param name="obj">The object to compare with the current Page object.</param>
        /// <returns>
        /// True if the object is a page with the same identifier; otherwise, false.
        /// </returns>
        public override bool Equals(object obj)
        {
            return obj is Page other && PageIdentity.Equals(this, other);
        }

        /// <summary>
        /// Gets the hash code of this page's non-empty identifier.
        /// </summary>
        /// <returns>A hash code value for the current Page object.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the page has not been assigned an identifier.
        /// </exception>
        public override int GetHashCode()
        {
            return PageIdentity.GetHashCode(this);
        }

        /// <summary>
        /// Updates the last modified date and time of the Page object to the current UTC time.
        /// </summary>
        public void UpdateLastModified()
        {
            UpdateLastModified(SystemClock.Instance.UtcNow.UtcDateTime);
        }

        /// <summary>Updates the last-modified value to the specified UTC time.</summary>
        /// <param name="updatedUtc">The UTC update time.</param>
        public void UpdateLastModified(DateTime updatedUtc)
        {
            this.UpdateTime = updatedUtc;
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
