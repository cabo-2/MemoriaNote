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
        public static T Create<T>(string name, params string[] tags) where T : IContent, new()
        {
            var content = new T();
            content.Rowid = 0; // auto increment
            content.Guid = Guid.NewGuid();
            content.Name = name;
            content.Index = 1;
            content.ContentType = nameof(T);
            if (tags == null)
                content.TagList = null;
            else
            {
                content.TagList = new List<string>();
                content.TagList.AddRange(tags);
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

        public int Rowid { get; set; }
        [NotMapped]
        public Guid Guid { get; set; }
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
        public string Name { get; set; }
        public int Index { get; set; }
        [NotMapped]
        public List<string> TagList { get; set; }
        public string Tags
        {
            get
            {
                if (TagList == null)
                    return null;
                return string.Join (";", TagList);
            }
            set
            {
                if (value == null)
                    TagList = null;
                else
                    TagList = value.Split (';').ToList ();
            }
        }
        public string ContentType { get; set; }
        public override string ToString () {
            if (Name != null)
                if (Index == 1)
                    return Name;
                else
                    return Name + Index.ToIndexString ();
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
                   (value.Name == null ? 5 : value.Name.GetHashCode ()) ^
                   (value.Index.GetHashCode ()) ^
                   (value.TagList == null ? 2 : GetOrderIndependentHashCode (value.TagList)) ^
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
