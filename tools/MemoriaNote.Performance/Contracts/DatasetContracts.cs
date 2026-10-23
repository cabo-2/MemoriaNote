namespace MemoriaNote.Performance.Contracts;

internal sealed class DatasetManifestDocument
{
    public string SchemaVersion { get; set; } = string.Empty;

    public string DatasetId { get; set; } = string.Empty;

    public string DatasetVersion { get; set; } = string.Empty;

    public List<string> Languages { get; set; } = [];

    public DatasetSource Source { get; set; } = new();

    public List<DatasetLicense> Licenses { get; set; } = [];

    public DatasetSelection Selection { get; set; } = new();

    public DatasetTransform Transform { get; set; } = new();

    public PreparedDataset Prepared { get; set; } = new();

    public DatasetStatistics? Statistics { get; set; }
}

internal sealed class DatasetSource
{
    public string? Kind { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Revision { get; set; } = string.Empty;

    public string? ArchiveSha256 { get; set; }

    public string? ContentRoot { get; set; }

    public List<string>? SparsePaths { get; set; }

    public string? ContentSha256 { get; set; }
}

internal sealed class DatasetLicense
{
    public string Expression { get; set; } = string.Empty;

    public string AppliesTo { get; set; } = string.Empty;

    public string LicenseFile { get; set; } = string.Empty;

    public string Attribution { get; set; } = string.Empty;
}

internal sealed class DatasetSelection
{
    public string Description { get; set; } = string.Empty;

    public int? Seed { get; set; }

    public List<string>? IncludeGlobs { get; set; }

    public List<string>? ExcludedPathSegments { get; set; }

    public List<DatasetScale> Scales { get; set; } = [];
}

internal sealed class DatasetScale
{
    public string ScaleId { get; set; } = string.Empty;

    public int PageCount { get; set; }
}

internal sealed class DatasetTransform
{
    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string? OutputEncoding { get; set; }

    public string? Newline { get; set; }

    public string? PageNameStrategy { get; set; }

    public List<string>? TitleFields { get; set; }

    public List<string>? TagFields { get; set; }
}

internal sealed class PreparedDataset
{
    public string RelativePath { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public int PageCount { get; set; }

    public long TotalBytes { get; set; }
}

internal sealed class DatasetStatistics
{
    public int SourceMarkdownCount { get; set; }

    public long SourceMarkdownBytes { get; set; }

    public int EligiblePageCount { get; set; }

    public int ExcludedPageCount { get; set; }

    public int SelectedPageCount { get; set; }

    public long SelectedBodyBytes { get; set; }

    public int FrontMatterTitleCount { get; set; }

    public int HeadingTitleCount { get; set; }

    public int PathTitleCount { get; set; }
}
