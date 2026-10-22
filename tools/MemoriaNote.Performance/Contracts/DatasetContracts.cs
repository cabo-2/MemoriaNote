namespace MemoriaNote.Performance.Contracts;

internal sealed class DatasetManifestDocument
{
    internal const string CurrentSchemaVersion = "1";

    public string SchemaVersion { get; set; } = string.Empty;

    public string DatasetId { get; set; } = string.Empty;

    public string DatasetVersion { get; set; } = string.Empty;

    public List<string> Languages { get; set; } = [];

    public DatasetSource Source { get; set; } = new();

    public List<DatasetLicense> Licenses { get; set; } = [];

    public DatasetSelection Selection { get; set; } = new();

    public DatasetTransform Transform { get; set; } = new();

    public PreparedDataset Prepared { get; set; } = new();
}

internal sealed class DatasetSource
{
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Revision { get; set; } = string.Empty;

    public string? ArchiveSha256 { get; set; }
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
}

internal sealed class PreparedDataset
{
    public string RelativePath { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public int PageCount { get; set; }

    public long TotalBytes { get; set; }
}
