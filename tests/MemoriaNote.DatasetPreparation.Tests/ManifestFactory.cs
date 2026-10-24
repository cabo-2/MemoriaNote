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

    internal static DatasetManifest CreateMdn(params int[] pageCounts)
    {
        var manifest = Create(pageCounts);
        manifest.DatasetId = "mdn-translated-content-ja";
        manifest.Languages = ["ja"];
        manifest.Source.Name = "mdn/translated-content";
        manifest.Source.Url = "https://github.com/mdn/translated-content.git";
        manifest.Source.ContentRoot = "files/ja";
        manifest.Source.SparsePaths = ["files/ja", "LICENSE.md"];
        manifest.Selection.ExcludedPathSegments = ["conflicting", "orphaned"];
        manifest.Transform.Name = "mdn-translated-content-markdown";
        manifest.Transform.PageNameStrategy = "front-matter-slug-v1";
        manifest.Transform.TagFields = ["slug"];
        return manifest;
    }
}
