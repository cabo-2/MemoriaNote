using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MemoriaNote.Serialization
{
    internal static class TagJsonCodec
    {
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        internal static string Serialize(Dictionary<string, string> tags)
        {
            if (tags == null || tags.Count == 0)
                return null;

            return JsonSerializer.Serialize(tags, Options);
        }

        internal static Dictionary<string, string> Deserialize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new Dictionary<string, string>();

            return JsonSerializer.Deserialize<Dictionary<string, string>>(value, Options);
        }
    }
}
