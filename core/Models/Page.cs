using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace MemoriaNote
{
    [Serializable]
    public class Page : IContent, IEquatable<Page>
    {
        public static Page Create(string name, string text, params string[] tags)
        {
            var page = Content.Create<Page>(name, tags);
            page.Text = text;
            return page;
        }

        //[Key]
        //[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Rowid { get; set; }
        [NotMapped]
        public Guid Guid { get; set; }
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
        public string Name { get; set; }
        public int Index { get; set; }
        [NotMapped]
        public List<string> TagList { get; set; }
        [JsonIgnore]
        public string Tags
        {
            get
            {
                if (TagList == null || TagList.Count == 0)
                    return null;

                return JsonConvert.SerializeObject(TagList);
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    TagList = null;
                else
                    TagList = JsonConvert.DeserializeObject<List<string>>(value);
            }
        }
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
        public DateTime CreateTime { get; set; }
        public DateTime UpdateTime { get; set; }
        public bool IsErased { get; set; }
        [NotMapped, JsonIgnore]
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
