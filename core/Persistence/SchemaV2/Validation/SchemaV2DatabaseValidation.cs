using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote.Persistence.SchemaV2.Validation;

/// <summary>Describes one independently evaluated Schema V2 validation layer.</summary>
internal sealed record SchemaV2ValidationLayerResult(
    bool WasEvaluated,
    IReadOnlyList<string> Issues)
{
    internal bool IsValid => WasEvaluated && Issues.Count == 0;

    internal static SchemaV2ValidationLayerResult Evaluated(IEnumerable<string> issues) =>
        new(true, issues.ToArray());

    internal static SchemaV2ValidationLayerResult NotEvaluated { get; } =
        new(false, new string[0]);
}

/// <summary>Reports identity, schema, integrity, and authoritative-value validation separately.</summary>
internal sealed record SchemaV2DatabaseValidation(
    SchemaV2ValidationLayerResult Identity,
    SchemaV2ValidationLayerResult Schema,
    SchemaV2ValidationLayerResult Integrity,
    SchemaV2ValidationLayerResult AuthoritativeValues)
{
    internal bool IsValid =>
        Identity.IsValid &&
        Schema.IsValid &&
        Integrity.IsValid &&
        AuthoritativeValues.IsValid;

    internal IEnumerable<string> AllIssues =>
        Identity.Issues
            .Concat(Schema.Issues)
            .Concat(Integrity.Issues)
            .Concat(AuthoritativeValues.Issues);
}
