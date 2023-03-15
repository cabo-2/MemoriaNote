using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoriaNote
{
    [Serializable]
    [ComplexType]
    public class Content : IContent, IEquatable<Content>
    {
        public static T Create<T>(string noteid, string title, params string[] tags) where T : IContent, new()
        {
            var content = new T();
            content.Rowid = 0; // auto increment
            content.Guid = Guid.NewGuid();
            content.Title = title;
            content.Index = 1;
            content.Noteid = noteid;
            content.ContentType = nameof(T);
            if (tags == null)
                content.Tags = null;
            else
            {
                content.Tags = new List<string>();
                content.Tags.AddRange(tags);
            }
            content.CreateTime = DateTime.UtcNow;
            content.UpdateTime = content.CreateTime;
            content.IsErased = false;
            return content;
        }

        public static Content Create(IContent content)
        {
            var value = new Content();
            value.Rowid = content.Rowid;
            value.Guid = content.Guid;
            value.Title = content.Title;
            value.Index = content.Index;
            value.Noteid = content.Noteid;
            value.ContentType = content.ContentType;
            value.TagsAsString = content.TagsAsString;
            value.CreateTime = content.CreateTime;
            value.UpdateTime = content.UpdateTime;
            value.IsErased = content.IsErased;
            value.Parent = content.Parent;
            return value;
        }

        public int Rowid { get; set; }
        [NotMapped]
        public Guid Guid { get; set; }
        public string GuidAsString
        {
            get => Guid.ToString("B");
            set
            {
                if (string.IsNullOrEmpty(value))
                    this.Guid = Guid.Empty;
                else
                    this.Guid = Guid.Parse(value);
            }
        }
        public string Title { get; set; }
        public int Index { get; set; }
        [NotMapped]
        public List<string> Tags { get; set; }
        public string TagsAsString
        {
            get
            {
                if (Tags == null)
                    return null;
                return string.Join (";", Tags);
            }
            set
            {
                if (value == null)
                    Tags = null;
                else
                    Tags = value.Split (';').ToList ();
            }
        }
        public string Noteid { get; set; }
        public string ContentType { get; set; }
        [NotMapped]
        public string ViewTitle => ToString ();
        public override string ToString () {
            if (Title != null)
                if (Index == 1)
                    return Title;
                else
                    return Title + Index.ToIndexString ();
            else
                return "Rowid=" + Rowid;           
        }
        public DateTime CreateTime { get; set; }
        public DateTime UpdateTime { get; set; }
        public bool IsErased { get; set; }
        [NotMapped]
        public object Parent { get; set; }

        public bool EntityEquals (IContent other) {
            return EntityEquals(this, other);
        }

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

        public bool Equals (Content other) {
            return Equals(this, other);
        }

        public static bool Equals(IContent objA, IContent objB)
        {
            if (EntityEquals(objA, objB))
                return objA.GetHashCode() == objB.GetHashCode();
            else
                return false;
        }

        public override bool Equals (object obj) {
            return this.Equals (obj as Content);
        }

        public override int GetHashCode()
        {
            return GetHashCode(this);
        }

        public static int GetHashCode (IContent value) {
            return 4 ^ (value.Rowid.GetHashCode ()) ^
                   (value.Title == null ? 5 : value.Title.GetHashCode ()) ^
                   (value.Index.GetHashCode ()) ^
                   (value.Tags == null ? 2 : GetOrderIndependentHashCode (value.Tags)) ^
                   (value.CreateTime.GetHashCode()) ^
                   (value.UpdateTime.GetHashCode()) ^
                   (value.IsErased.GetHashCode());
        }

        public static int GetOrderIndependentHashCode<T> (IEnumerable<T> source) {
            int hash = 0;
            foreach (T element in source) {
                hash = hash ^ EqualityComparer<T>.Default.GetHashCode (element);
            }
            return hash;
        }

        public Content GetContent() => this;
    }
}
