using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace MemoriaNote
{
    /// <summary>
    /// Represents the persisted summary read model derived from a Page.
    /// Contents rows omit Page text and are maintained only by database triggers or explicit
    /// read model reconstruction.
    /// </summary>
    /// <remarks>
    /// The Content class contains properties for identifiers, names, indexes, tags, content
    /// type, and timestamps used by list and heading-search operations.
    /// </remarks>
    [Serializable]
    [ComplexType]
    public class Content : IContent, IEquatable<Content>
    {
        /// <summary>
        /// Creates a new instance of a class that implements IContent interface, initializes its properties with the provided values, and returns it.
        /// </summary>
        /// <typeparam name="T">The type of the content object to create.</typeparam>
        /// <param name="name">The name to assign to the content object.</param>
        /// <param name="dir">The directory path to assign as a tag to the content object. Default is null.</param>
        /// <returns>A new instance of the specified content type with the properties initialized based on the input values.</returns>
        public static T Create<T>(string name, string dir = null) where T : IContent, new()
        {
            var content = new T();
            content.Rowid = 0; // auto increment
            // Assign identity before the transient entity can be returned to callers.
            content.Guid = Guid.NewGuid();
            content.Name = name;
            content.Index = 1;
            content.ContentType = nameof(T);
            if (dir != null)
                content.TagDict.Add(PageTag.Dir, dir);

            content.CreateTime = DateTime.UtcNow;
            content.UpdateTime = content.CreateTime;
            content.IsErased = false;
            return content;
        }

        /// <summary>
        /// Creates a new instance of the Content class by copying the properties from an object that implements the IContent interface.
        /// </summary>
        /// <param name="content">The object that implements the IContent interface from which to copy the properties.</param>
        /// <returns>A new instance of the Content class with properties copied from the input object.</returns>
        public static Content Create(IContent content)
        {
            var value = new Content();
            value.Rowid = content.Rowid;
            value.Guid = content.Guid;
            value.Name = content.Name;
            value.Index = content.Index;
            value.ContentType = content.ContentType;
            value.Tags = content.Tags;
            value.CreateTime = content.CreateTime;
            value.UpdateTime = content.UpdateTime;
            value.IsErased = content.IsErased;
            value.OwnerDataSource = content.OwnerDataSource;
            value.Parent = content.Parent;
            return value;
        }

        /// <summary>
        /// Gets or sets the Rowid property which represents the unique identifier of the content object.
        /// </summary>
        public int Rowid { get; set; }

        /// <summary>
        /// Gets or sets the Guid property which represents the globally unique identifier of the content object.
        /// </summary>
        [NotMapped]
        public Guid Guid { get; set; }

        /// <summary>
        /// Gets or sets the Uuid property which represents the universally unique identifier of the content object as a string.
        /// When getting, returns the Uuid string representation of the Guid property.
        /// When setting, parses and assigns the Uuid string to the Guid property, or sets Guid.Empty if the input value is null or empty.
        /// </summary>
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
        /// Gets or sets the Name property which represents the name assigned to the content object.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the one-based display number that distinguishes dictionary senses
        /// with the same name using exact matching.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets the TagDict property which represents a dictionary of tags assigned to the content object.
        /// Tags are stored as key-value pairs.
        /// </summary>
        [NotMapped]
        public Dictionary<string, string> TagDict { get; set; }

        /// <summary>
        /// Gets or sets the Tags property which represents a JSON-serialized string of the TagDict dictionary.
        /// When getting, returns the JSON string representation of the TagDict dictionary.
        /// When setting, parses the input JSON string and assigns it to the TagDict dictionary property.
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
        /// Gets or sets the ContentType property which represents the type of content object.
        /// </summary>
        /// <remarks>
        /// The ContentType property is used to specify the type of content object. 
        /// It can be set to a string value indicating the specific type of content.
        /// </remarks>
        public string ContentType { get; set; }
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
        /// Gets or sets the CreateTime property which represents the date and time when the content object was created.
        /// </summary>
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// Gets or sets the UpdateTime property which represents the date and time when the content object was last updated.
        /// </summary>
        public DateTime UpdateTime { get; set; }

        /// <summary>
        /// Gets or sets the IsErased property which indicates whether the content object has been flagged as erased.
        /// </summary>
        public bool IsErased { get; set; }

        /// <summary>
        /// Gets or sets the normalized data source of the note that owns the content.
        /// </summary>
        [NotMapped, JsonIgnore]
        public string OwnerDataSource { get; set; }

        /// <summary>
        /// Gets or sets the Parent property which represents an object that is the parent of the content object.
        /// </summary>
        [NotMapped]
        public object Parent { get; set; }

        /// <inheritdoc/>
        public bool EntityEquals(IContent other)
        {
            return PageIdentity.Equals(this, other);
        }

        /// <summary>
        /// Compares two content entities by their non-empty page identifiers.
        /// </summary>
        /// <param name="objA">The first object to compare.</param>
        /// <param name="objB">The second object to compare.</param>
        /// <returns>
        /// True if the objects are the same instance or have the same non-empty page identifier;
        /// otherwise, false.
        /// </returns>
        public static bool EntityEquals(IContent objA, IContent objB)
        {
            return PageIdentity.Equals(objA, objB);
        }

        /// <inheritdoc/>
        public bool Equals(Content other)
        {
            return PageIdentity.Equals(this, other);
        }

        /// <summary>
        /// Compares two content entities by their non-empty page identifiers.
        /// </summary>
        /// <param name="objA">The first object implementing the IContent interface to compare.</param>
        /// <param name="objB">The second object implementing the IContent interface to compare.</param>
        /// <returns>True if the objects have the same entity identity; otherwise, false.</returns>
        public static bool Equals(IContent objA, IContent objB)
        {
            return PageIdentity.Equals(objA, objB);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is IContent other && PageIdentity.Equals(this, other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return PageIdentity.GetHashCode(this);
        }

        /// <summary>
        /// Gets the hash code of a content entity's non-empty page identifier.
        /// </summary>
        /// <param name="value">The content entity whose identifier is hashed.</param>
        /// <returns>The hash code of the entity's page identifier.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="value"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the entity has not been assigned a page identifier.
        /// </exception>
        public static int GetHashCode(IContent value)
        {
            return PageIdentity.GetHashCode(value);
        }

        public Content GetContent() => this;
    }
}
