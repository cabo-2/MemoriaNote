using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ComponentModel.DataAnnotations;
namespace MemoriaNote
{
    [DataContract]
    public class DataSourceTracker : IDataSource
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string Title { get; set; }
        [DataMember] public string Version { get; set; }       
        [DataMember] public string Description { get; set; }
        [DataMember] public string Author { get; set; }        
        [DataMember] public bool ReadOnly { get; set; } 
        [DataMember] public string Tag { get; set; }
        [DataMember] public DateTime CreateTime { get; set; } = DateTime.Now;
        [DataMember] public string DataSource { get; set; }

        public static DataSourceTracker Create(string name, string dataSource, string tag = null) =>
                                                   new DataSourceTracker() { Name = name, Tag = tag, DataSource = dataSource };

        public static DataSourceTracker Create(IDataSource source)
        {
            var tracker = new DataSourceTracker();
            source.CopyTo(tracker);
            return tracker;
        }

        public void ValidateName(Note note, Workgroup wg, ref List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(this.Name))
            {
                errors.Add("Name cannot be blank");
                throw new ValidationException(nameof(Name));
            }
            foreach(var name in wg.Notes.Where(n => !n.Equals(note)).Select(n => n.Metadata.Name))
                if (this.Name == name)
                {
                    errors.Add("Name is already registered");
                    throw new ValidationException(nameof(Name));
                }
        }

        public void ValidateTitle(Note note, Workgroup wg, ref List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(this.Title))
            {
                errors.Add("Title cannot be blank");
                throw new ValidationException(nameof(Title));
            }
        }  

        public IDataSource Clone() => new DataSourceTracker()
                                    {
                                        Name = this.Name, Title = this.Title, Version = this.Version, Description = this.Description,
                                        Author = this.Author, ReadOnly = this.ReadOnly, Tag = this.Tag, CreateTime = this.CreateTime, 
                                        DataSource = this.DataSource
                                    };
        public override int GetHashCode() => new { Name, Title, Version, Description, Author, ReadOnly, Tag, CreateTime, DataSource }.GetHashCode();
        public bool Equals(IDataSource other) => this.GetHashCode() == other.GetHashCode();
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