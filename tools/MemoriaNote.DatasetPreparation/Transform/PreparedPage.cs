namespace MemoriaNote.DatasetPreparation.Transform;

internal sealed record PreparedPage(
    string SourcePath,
    string SourceSha256,
    string Name,
    string FileName,
    string Title,
    string TitleSource,
    IReadOnlyList<string> Tags,
    string Body,
    int BodyBytes,
    string SelectionHash);
