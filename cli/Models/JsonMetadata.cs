using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace MemoriaNote.Cli.Models
{
    public class JsonMetadata : IMetadata
    {
        public static JsonMetadata Create(IMetadata data)
        {
            var json = new JsonMetadata();
            json.CopyTo(data);
            return json;
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

        public string Name { get; set; }
        public string Title { get; set; }
        [JsonIgnore] public string Version { get; set; }       
        public string Description { get; set; }
        public string Author { get; set; }        
        public bool ReadOnly { get; set; }        
        [JsonIgnore] public List<string> TagList { get; set; }

        public void CopyTo(IMetadata data)
        {
            Name = data.Name;
            Title = data.Title;
            Version = data.Version;
            Description = data.Description;
            Author = data.Author;
            ReadOnly = data.ReadOnly;
            TagList = data.TagList;
        }
    }
}