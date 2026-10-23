using MemoriaNote.DatasetPreparation.Contracts;

namespace MemoriaNote.DatasetPreparation.Tests;

internal static class ManifestFactory
{
    internal static DatasetManifest Create(params int[] pageCounts)
    {
        return new DatasetManifest
        {
            SchemaVersion = "2",
            DatasetId = "dotnet-docs-en",
            DatasetVersion = "test-1",
            Languages = ["en"],
            Source = new GitDatasetSource
            {
                Kind = "git",
                Name = "dotnet/docs",
                Url = "https://github.com/dotnet/docs.git",
                Revision = new string('a', 40),
                ContentRoot = "docs",
                SparsePaths = ["docs", "LICENSE"],
                ContentSha256 = new string('b', 64)
            },
            Licenses =
            [
                new DatasetLicense
                {
                    Expression = "CC-BY-4.0",
                    AppliesTo = "prepared/**/*",
                    LicenseFile = "LICENSES.md",
                    Attribution = "Microsoft and .NET contributors"
                }
            ],
            Selection = new DatasetSelection
            {
                Description = "Test selection.",
                IncludeGlobs = ["**/*.md"],
                ExcludedPathSegments = ["includes", "samples", "snippets"],
                Scales = pageCounts.Select(count => new DatasetScale
                {
                    ScaleId = "pages-" + count,
                    PageCount = count
                }).ToList()
            },
            Transform = new DatasetTransform
            {
                Name = "dotnet-docs-markdown",
                Version = "1",
                OutputEncoding = "utf-8-no-bom",
                Newline = "lf",
                PageNameStrategy = "repository-relative-path-v1",
                TitleFields = ["title"],
                TagFields = ["ms.topic"]
            },
            Prepared = new PreparedDataset
            {
                RelativePath = "prepared",
                Sha256 = new string('c', 64),
                PageCount = pageCounts.Max(),
                TotalBytes = 1
            }
        };
    }
}
