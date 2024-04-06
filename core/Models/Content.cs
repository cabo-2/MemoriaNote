using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace MemoriaNote
{
    /// <summary>
    /// Represents a class that implements the IContent interface and IEquatable interface for comparing content objects.
    /// The Content class is marked as serializable and complex type.
    /// </summary>
    /// <remarks>
    /// The Content class contains properties for unique identifiers, names, indexes, tags, content type, and timestamps.
    /// It also provides methods for creating new instances of content objects and copying properties from existing objects.
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
        /// Gets or sets the Index property which represents the index assigned to the content object.
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
        /// Gets or sets the Parent property which represents an object that is the parent of the content object.
        /// </summary>
        [NotMapped]
        public object Parent { get; set; }

        public bool EntityEquals(IContent other)
        {
            return EntityEquals(this, other);
        }

        /// <summary>
        /// Compares two objects that implement the IContent interface for equality based on their Guid property.
        /// Returns true if the objects are the same instance, or if their Guid properties are equal; otherwise, false.
        /// </summary>
        /// <param name="objA">The first object to compare.</param>
        /// <param name="objB">The second object to compare.</param>
        /// <returns>True if the objects are the same instance or if their Guid properties are equal; otherwise, false.</returns>
        public static bool EntityEquals(IContent objA, IContent objB)
        {
            // Optimization for a common success case.
            if (Object.ReferenceEquals(objA, objB))
                return true;

            // If parameter is null, return false.
            if (objA == null || objB == null)
                return false;

            // Check properties that this class declares.
            if (objA.Guid == objB.Guid)
                return true;

            return false;
        }

        public bool Equals(Content other)
        {
            return Equals(this, other);
        }

        /// <summary>
        /// Compares two objects implementing the IContent interface for equality based on their properties.
        /// Returns true if the objects are equal based on the EntityEquals method and have the same hash code; otherwise, false.
        /// </summary>
        /// <param name="objA">The first object implementing the IContent interface to compare.</param>
        /// <param name="objB">The second object implementing the IContent interface to compare.</param>
        /// <returns>True if the objects are equal based on EntityEquals and their hash codes match; otherwise, false.</returns>
        public static bool Equals(IContent objA, IContent objB)
        {
            if (EntityEquals(objA, objB))
                return objA.GetHashCode() == objB.GetHashCode();
            else
                return false;
        }

        public override bool Equals(object obj)
        {
            return this.Equals(obj as Content);
        }

        public override int GetHashCode()
        {
            return GetHashCode(this);
        }

        public static int GetHashCode(IContent value)
        {
            return 4 ^ (value.Rowid.GetHashCode()) ^
                   (value.Name == null ? 5 : value.Name.GetHashCode()) ^
                   (value.Index.GetHashCode()) ^
                   (value.TagDict == null ? 2 : value.TagDict.GetOrderIndependentHashCode()) ^
                   (value.CreateTime.GetHashCode()) ^
                   (value.UpdateTime.GetHashCode()) ^
                   (value.IsErased.GetHashCode());
        }

        public Content GetContent() => this;
    }
}
