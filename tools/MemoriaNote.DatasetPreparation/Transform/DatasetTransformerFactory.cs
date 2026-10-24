namespace MemoriaNote.DatasetPreparation.Transform;

internal static class DatasetTransformerFactory
{
    internal static IDatasetTransformer Create(string transformName)
    {
        return transformName switch
        {
            "dotnet-docs-markdown" => new DotnetDocsTransformer(),
            "mdn-translated-content-markdown" => new MdnTranslatedContentTransformer(),
            _ => throw new InvalidDataException(
                $"Dataset transform '{transformName}' is not supported.")
        };
    }
}
