using System.Text.RegularExpressions;

namespace MemoriaNote.DatasetPreparation.Contracts;

internal static partial class ManifestValidator
{
    internal static void Validate(DatasetManifest manifest, bool allowUnlocked)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var issues = new List<string>();
        if (manifest.SchemaVersion != DatasetManifest.CurrentSchemaVersion)
            issues.Add($"schemaVersion must be '{DatasetManifest.CurrentSchemaVersion}'.");
        RequireIdentifier(manifest.DatasetId, "datasetId", issues);
        RequireIdentifier(manifest.DatasetVersion, "datasetVersion", issues);
        if (manifest.Languages.Count == 0 || manifest.Languages.Any(string.IsNullOrWhiteSpace))
            issues.Add("languages must contain at least one non-empty language.");
        if (manifest.Source.Kind != "git")
            issues.Add("source.kind must be 'git'.");
        RequireText(manifest.Source.Name, "source.name", issues);
        RequireHttpsUrl(manifest.Source.Url, "source.url", issues);
        if (!FullCommitRegex().IsMatch(manifest.Source.Revision))
            issues.Add("source.revision must be a full 40 or 64 character Git commit ID.");
        RequireRelativePath(manifest.Source.ContentRoot, "source.contentRoot", issues);
        if (manifest.Source.SparsePaths.Count == 0)
            issues.Add("source.sparsePaths must not be empty.");
        foreach (var path in manifest.Source.SparsePaths)
            RequireRelativePath(path, "source.sparsePaths", issues);
        ValidateHash(manifest.Source.ContentSha256, "source.contentSha256", allowUnlocked, issues);
        if (manifest.Licenses.Count == 0)
            issues.Add("licenses must not be empty.");
        foreach (var license in manifest.Licenses)
        {
            RequireText(license.Expression, "licenses.expression", issues);
            RequireText(license.AppliesTo, "licenses.appliesTo", issues);
            RequireRelativePath(license.LicenseFile, "licenses.licenseFile", issues);
            RequireText(license.Attribution, "licenses.attribution", issues);
        }
        RequireText(manifest.Selection.Description, "selection.description", issues);
        if (!manifest.Selection.IncludeGlobs.SequenceEqual(["**/*.md"], StringComparer.Ordinal))
            issues.Add("selection.includeGlobs must currently be exactly ['**/*.md'].");
        if (manifest.Selection.Scales.Count == 0)
            issues.Add("selection.scales must not be empty.");
        var lastCount = 0;
        foreach (var scale in manifest.Selection.Scales)
        {
            RequireIdentifier(scale.ScaleId, "selection.scales.scaleId", issues);
            if (scale.PageCount <= lastCount)
                issues.Add("selection scale pageCount values must be positive and increasing.");
            lastCount = scale.PageCount;
        }
        if (manifest.Transform.Name != "dotnet-docs-markdown")
            issues.Add("transform.name must be 'dotnet-docs-markdown'.");
        RequireIdentifier(manifest.Transform.Version, "transform.version", issues);
        if (manifest.Transform.OutputEncoding != "utf-8-no-bom")
            issues.Add("transform.outputEncoding must be 'utf-8-no-bom'.");
        if (manifest.Transform.Newline != "lf")
            issues.Add("transform.newline must be 'lf'.");
        if (manifest.Transform.PageNameStrategy != "repository-relative-path-v1")
            issues.Add("transform.pageNameStrategy must be 'repository-relative-path-v1'.");
        RequireRelativePath(manifest.Prepared.RelativePath, "prepared.relativePath", issues);
        ValidateHash(manifest.Prepared.Sha256, "prepared.sha256", allowUnlocked, issues);
        if (!allowUnlocked && manifest.Prepared.PageCount <= 0)
            issues.Add("prepared.pageCount must be positive.");
        if (!allowUnlocked && manifest.Prepared.TotalBytes <= 0)
            issues.Add("prepared.totalBytes must be positive.");
        if (!allowUnlocked)
        {
            if (manifest.Prepared.PageCount != lastCount)
                issues.Add("prepared.pageCount must equal the largest scale pageCount.");
            if (manifest.Statistics.SourceMarkdownCount !=
                manifest.Statistics.EligiblePageCount + manifest.Statistics.ExcludedPageCount)
            {
                issues.Add(
                    "statistics sourceMarkdownCount must equal eligiblePageCount plus excludedPageCount.");
            }
            if (manifest.Statistics.SelectedPageCount != manifest.Prepared.PageCount)
                issues.Add("statistics.selectedPageCount must equal prepared.pageCount.");
            if (manifest.Statistics.EligiblePageCount < manifest.Statistics.SelectedPageCount)
                issues.Add("statistics.eligiblePageCount cannot be smaller than selectedPageCount.");
            if (manifest.Statistics.FrontMatterTitleCount +
                manifest.Statistics.HeadingTitleCount +
                manifest.Statistics.PathTitleCount != manifest.Statistics.SelectedPageCount)
            {
                issues.Add("statistics title source counts must equal selectedPageCount.");
            }
        }

        if (issues.Count > 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, issues));
    }

    static void RequireText(string value, string name, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add($"{name} must not be empty.");
    }

    static void RequireIdentifier(string value, string name, ICollection<string> issues)
    {
        if (!IdentifierRegex().IsMatch(value))
            issues.Add($"{name} must be a lowercase identifier.");
    }

    static void RequireHttpsUrl(string value, string name, ICollection<string> issues)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            issues.Add($"{name} must be an absolute HTTPS URL.");
    }

    static void RequireRelativePath(string value, string name, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) ||
            value.Split(['/', '\\']).Any(segment => segment == ".."))
        {
            issues.Add($"{name} must be a safe relative path.");
        }
    }

    static void ValidateHash(
        string value,
        string name,
        bool allowUnlocked,
        ICollection<string> issues)
    {
        if (allowUnlocked && string.IsNullOrEmpty(value))
            return;
        if (!Sha256Regex().IsMatch(value))
            issues.Add($"{name} must be a lowercase SHA-256 value.");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex("^[0-9a-f]{40}([0-9a-f]{24})?$", RegexOptions.CultureInvariant)]
    private static partial Regex FullCommitRegex();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}
