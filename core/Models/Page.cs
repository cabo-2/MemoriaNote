using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoriaNote
{
    [Serializable]
    public class Page : IContent, IEquatable<Page>
    {       
        public static Page Create(string noteid, string title, string text, params string[] tags)
        {
            var page = Content.Create<Page>(noteid, title, tags);
            page.Text = text;
            return page;
        }

        //[Key]
        //[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
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
                return string.Join(";", Tags);
            }
            set
            {
                if (value == null)
                    Tags = null;
                else
                    Tags = value.Split(';').ToList();
            }
        }
        public string Noteid { get; set; }
        public string ContentType { get; set; }
        [NotMapped]
        public string ViewTitle => ToString();
        public override string ToString()
        {
            if (Title != null)
                if (Index == 1)
                    return Title;
                else
                    return Title + Index.ToIndexString();
            else
                return "Rowid=" + Rowid;
        }
        public DateTime CreateTime { get; set; }
        public DateTime UpdateTime { get; set; }
        public bool IsErased { get; set; }
        [NotMapped]
        public object Parent { get; set; }

        public string Text { get; set; }

        public bool EntityEquals(IContent other)
        {
            return Content.EntityEquals(this, other);
        }

        public bool Equals(Page other)
        {
            if (EntityEquals(other))
                return this.GetHashCode() == other.GetHashCode();
            else
                return false;
        }

        public override bool Equals(object obj)
        {
            return this.Equals(obj as Page);
        }

        public override int GetHashCode()
        {
            return Content.GetHashCode(this) ^
                   (Text == null ? 4 : Text.GetHashCode());
        }

        static int GetOrderIndependentHashCode<T>(IEnumerable<T> source)
        {
            int hash = 0;
            foreach (T element in source)
            {
                hash = hash ^ EqualityComparer<T>.Default.GetHashCode(element);
            }
            return hash;
        }

        public void UpdateLastModified()
        {
            this.UpdateTime = DateTime.UtcNow;
        }

        public Content GetContent()
        {
            return Content.Create(this);
        }
    }
}
