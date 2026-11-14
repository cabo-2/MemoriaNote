#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;

namespace MemoriaNote.Integration.Validation;

/// <summary>Identifies revision 3 of the first-party Core validation integration contract.</summary>
public static class CoreIntegrationContract
{
    /// <summary>The current validation integration contract revision.</summary>
    public const int Revision = 3;

    /// <summary>The deterministic body-search conformance corpus revision.</summary>
    public const int BodySearchConformanceCorpusRevision = 2;

    /// <summary>The NuGet package identity that carries this contract.</summary>
    public const string PackageId = "MemoriaNote.Core";

    /// <summary>Returns the exact assembly, source, format, and candidate provenance.</summary>
    /// <returns>The immutable runtime integration descriptor.</returns>
    public static CoreIntegrationDescriptor GetDescriptor()
    {
        var assembly = typeof(CoreIntegrationContract).Assembly;
        var metadata = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value);
        var targetFramework = assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()
            ?.FrameworkName ?? "unknown";
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        return new CoreIntegrationDescriptor(
            PackageId,
            RequiredMetadata(metadata, "MemoriaNote.PackageVersion"),
            informationalVersion,
            RequiredMetadata(metadata, "MemoriaNote.SourceCommit"),
            targetFramework,
            Revision,
            notebookFormatVersion: 2,
            BodySearchConformanceCorpusRevision,
            [
                DerivedDataKind.PageTags,
                DerivedDataKind.Fts5Unicode61,
                DerivedDataKind.Fts5Trigram
            ],
            [
                BodySearchCandidate.Scan,
                BodySearchCandidate.Fts5Unicode61,
                BodySearchCandidate.Fts5Trigram
            ]);
    }

    private static string RequiredMetadata(
        IReadOnlyDictionary<string, string?> metadata,
        string key)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Core assembly metadata '{key}' is missing.");
        return value;
    }
}

/// <summary>Reports the independently versioned identities used by Core integration consumers.</summary>
public sealed class CoreIntegrationDescriptor
{
    internal CoreIntegrationDescriptor(
        string packageId,
        string packageVersion,
        string assemblyInformationalVersion,
        string sourceCommit,
        string targetFramework,
        int contractRevision,
        int notebookFormatVersion,
        int bodySearchConformanceCorpusRevision,
        IReadOnlyList<DerivedDataKind> derivedDataCandidates,
        IReadOnlyList<BodySearchCandidate> bodySearchCandidates)
    {
        PackageId = packageId;
        PackageVersion = packageVersion;
        AssemblyInformationalVersion = assemblyInformationalVersion;
        SourceCommit = sourceCommit;
        TargetFramework = targetFramework;
        ContractRevision = contractRevision;
        NotebookFormatVersion = notebookFormatVersion;
        BodySearchConformanceCorpusRevision = bodySearchConformanceCorpusRevision;
        DerivedDataCandidates = Array.AsReadOnly([.. derivedDataCandidates]);
        BodySearchCandidates = Array.AsReadOnly([.. bodySearchCandidates]);
    }

    /// <summary>Gets the package ID.</summary>
    public string PackageId { get; }

    /// <summary>Gets the exact package version generated for this source commit.</summary>
    public string PackageVersion { get; }

    /// <summary>Gets the assembly informational version.</summary>
    public string AssemblyInformationalVersion { get; }

    /// <summary>Gets the full 40-character Memoria Note source commit.</summary>
    public string SourceCommit { get; }

    /// <summary>Gets the target framework compiled into the assembly.</summary>
    public string TargetFramework { get; }

    /// <summary>Gets the validation integration contract revision.</summary>
    public int ContractRevision { get; }

    /// <summary>Gets the canonical notebook format version produced by this package.</summary>
    public int NotebookFormatVersion { get; }

    /// <summary>Gets the deterministic body-search conformance corpus revision.</summary>
    public int BodySearchConformanceCorpusRevision { get; }

    /// <summary>Gets the explicit derived-data candidates in this contract revision.</summary>
    public IReadOnlyList<DerivedDataKind> DerivedDataCandidates { get; }

    /// <summary>Gets the explicit body-search candidates in this contract revision.</summary>
    public IReadOnlyList<BodySearchCandidate> BodySearchCandidates { get; }
}
