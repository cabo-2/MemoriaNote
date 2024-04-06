using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ComponentModel.DataAnnotations;
namespace MemoriaNote
{
    /// <summary>
    /// Represents a data source tracker that implements the IDataSource interface.
    /// This class is used to track metadata related to a data source, such as name, title, version, description, author, etc.
    /// </summary>
    [DataContract]
    public class DataSourceTracker : IDataSource
    {
        /// <summary>
        /// A data member representing the name of the data source tracker.
        /// </summary>
        [DataMember] public string Name { get; set; }

        /// <summary>
        /// A data member representing the title of the data source tracker.
        /// </summary>
        [DataMember] public string Title { get; set; }

        /// <summary>
        /// A data member representing the version of the data source tracker.
        /// </summary>
        [DataMember] public string Version { get; set; }

        /// <summary>
        /// A data member representing the description of the data source tracker.
        /// </summary>
        [DataMember] public string Description { get; set; }

        /// <summary>
        /// A data member representing the author of the data source tracker.
        /// </summary>
        [DataMember] public string Author { get; set; }

        /// <summary>
        /// A data member representing whether the data source tracker is read-only or not.
        /// </summary>
        [DataMember] public bool ReadOnly { get; set; }

        /// <summary>
        /// A data member representing the tag associated with the data source tracker.
        /// </summary>
        [DataMember] public string Tag { get; set; }

        /// <summary>
        /// A data member representing the creation time of the data source tracker. Defaults to the current date and time.
        /// </summary>
        [DataMember] public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// A data member representing the data source of the data source tracker.
        /// </summary>
        [DataMember] public string DataSource { get; set; }

        /// <summary>
        /// Creates a new instance of the DataSourceTracker class with the specified name, data source, and optional tag.
        /// </summary>
        /// <param name="name">The name of the data source tracker.</param>
        /// <param name="dataSource">The data source of the data source tracker.</param>
        /// <param name="tag">The tag associated with the data source tracker (optional).</param>
        /// <returns>A new DataSourceTracker instance with the specified name, data source, and tag.</returns>
        public static DataSourceTracker Create(string name, string dataSource, string tag = null) =>
                                                   new DataSourceTracker() { Name = name, Tag = tag, DataSource = dataSource };

        /// <summary>
        /// Creates a new instance of the DataSourceTracker class with the specified data source. 
        /// Retrieves additional metadata values from the database using the data source as the database connection string.
        /// </summary>
        /// <param name="dataSource">The data source for the DataSourceTracker.</param>
        /// <returns>A new DataSourceTracker instance with metadata values obtained from the database.</returns>
        public static DataSourceTracker Create(string dataSource)
        {
            var value = new DataSourceTracker() { DataSource = dataSource };
            using (NoteDbContext db = new NoteDbContext(value.DataSource))
            {
                value.Name = NoteKeyValue.Get(db, NoteKeyValue.Name);
                value.Title = NoteKeyValue.Get(db, NoteKeyValue.Title);
                value.Version = NoteKeyValue.Get(db, NoteKeyValue.Version);
                value.Description = NoteKeyValue.Get(db, NoteKeyValue.Description);
                value.Author = NoteKeyValue.Get(db, NoteKeyValue.Author);
                var readOnly = NoteKeyValue.Get(db, NoteKeyValue.ReadOnly);
                value.ReadOnly = bool.Parse(readOnly ?? "false");
                value.Tag = NoteKeyValue.Get(db, NoteKeyValue.Tag);
                var createTime = NoteKeyValue.Get(db, NoteKeyValue.CreateTime);
                if (createTime != null)
                    value.CreateTime = DateTime.Parse(createTime);
                else
                    value.CreateTime = default(DateTime);
            }
            return value;
        }

        /// <summary>
        /// Validates the name of a note within a workgroup by checking if it is blank or already registered.
        /// </summary>
        /// <param name="note">The note to validate.</param>
        /// <param name="wg">The workgroup containing the note.</param>
        /// <param name="errors">A reference to a list of errors where any validation errors will be added.</param>
        /// <exception cref="ValidationException">Thrown when the name is blank or already registered in the workgroup.</exception>
        public void ValidateName(Note note, Workgroup wg, ref List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(this.Name))
            {
                errors.Add("Name cannot be blank");
                throw new ValidationException(nameof(Name));
            }
            foreach (var name in wg.Notes.Where(n => !n.Equals(note)).Select(n => n.Metadata.Name))
                if (this.Name == name)
                {
                    errors.Add("Name is already registered");
                    throw new ValidationException(nameof(Name));
                }
        }

        /// <summary>
        /// Validates the title of a note within a workgroup by checking if it is blank.
        /// </summary>
        /// <param name="note">The note to validate.</param>
        /// <param name="wg">The workgroup containing the note.</param>
        /// <param name="errors">A reference to a list of errors where any validation errors will be added.</param>
        /// <exception cref="ValidationException">Thrown when the title is blank.</exception>
        public void ValidateTitle(Note note, Workgroup wg, ref List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(this.Title))
            {
                errors.Add("Title cannot be blank");
                throw new ValidationException(nameof(Title));
            }
        }

        /// <summary>
        /// Creates a deep copy of the DataSourceTracker instance with the same values for all properties.
        /// </summary>
        /// <returns>A new instance of the DataSourceTracker class with identical property values.</returns>
        public IDataSource Clone() => new DataSourceTracker()
        {
            Name = this.Name,
            Title = this.Title,
            Version = this.Version,
            Description = this.Description,
            Author = this.Author,
            ReadOnly = this.ReadOnly,
            Tag = this.Tag,
            CreateTime = this.CreateTime,
            DataSource = this.DataSource
        };

        /// <summary>
        /// Overrides the default hash code generation method to calculate the hash code based on the specified properties of this instance.
        /// </summary>
        /// <returns>The hash code calculated using the properties Name, Title, Version, Description, Author, ReadOnly, Tag, CreateTime, and DataSource.</returns>
        public override int GetHashCode() => new { Name, Title, Version, Description, Author, ReadOnly, Tag, CreateTime, DataSource }.GetHashCode();

        /// <summary>
        /// Compares this instance with another instance of an IDataSource object to determine if they are equal based on their hash codes.
        /// </summary>
        /// <param name="other">The other IDataSource instance to compare with.</param>
        /// <returns>True if the hash codes of both instances are equal, indicating potential equality; otherwise, false.</returns>
        public bool Equals(IDataSource other) => this.GetHashCode() == other.GetHashCode();

        /// <summary>
        /// Copies the property values of this DataSourceTracker instance to another IDataSource instance.
        /// </summary>
        /// <param name="dest">The destination IDataSource instance to copy the values to.</param>
        public void CopyTo(IDataSource dest)
        {
            if (this.Name != dest.Name)
                dest.Name = this.Name;
            if (this.Title != dest.Title)
                dest.Title = this.Title;
            if (this.Version != dest.Version)
                dest.Version = this.Version;
            if (this.Description != dest.Description)
                dest.Description = this.Description;
            if (this.Author != dest.Author)
                dest.Author = this.Author;
            if (this.ReadOnly != dest.ReadOnly)
                dest.ReadOnly = this.ReadOnly;
            if (this.Tag != dest.Tag)
                dest.Tag = this.Tag;
            if (this.CreateTime != dest.CreateTime)
                dest.CreateTime = this.CreateTime;
            if (this.DataSource != dest.DataSource)
                dest.DataSource = this.DataSource;
        }

        public override string ToString()
        {
            if (Name != null)
            {
                if (Tag != null)
                    return $"{Name}:{Tag}";
                else
                    return $"{Name}:{CreateTime.ToDateString()}";
            }
            else
            {
                return base.ToString();
            }
        }
    }
}