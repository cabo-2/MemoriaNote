#nullable enable

using System;
using System.Text;
using MemoriaNote.Domain.Queries;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Compiles validated literal keywords into one parameterized FTS5 query value.</summary>
internal static class Fts5BodySearchQueryCompiler
{
    internal static string Compile(BodySearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = new StringBuilder();
        for (var index = 0; index < query.Keywords.Count; index++)
        {
            if (index > 0)
                result.Append(" AND ");
            result.Append('"');
            foreach (var character in query.Keywords[index])
            {
                result.Append(character);
                if (character == '"')
                    result.Append('"');
            }
            result.Append('"');
        }
        return result.ToString();
    }
}
