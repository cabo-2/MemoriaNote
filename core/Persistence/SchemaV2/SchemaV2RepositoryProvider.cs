using System;
using MemoriaNote.Persistence.Repositories;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Identifies one internal schema v2 body-search composition.</summary>
internal enum SchemaV2BodySearchCandidate
{
    Scan,
    FtsUnicode61,
    FtsTrigram
}

/// <summary>Supplies the operational repositories for notebook format version 2.</summary>
internal sealed class SchemaV2RepositoryProvider : IWritableNotebookRepositoryProvider
{
    private static readonly NotebookRepositoryDescriptor SchemaDescriptor = new(
        NotebookFormatVersion.Version2,
        NotebookRepositoryCapabilities.OperationalRead |
        NotebookRepositoryCapabilities.OperationalWrite);

    private readonly SchemaV2MetadataRepository _metadataRepository = new();
    private readonly SchemaV2PageRepository _pageRepository;
    private readonly INotebookBodySearchReader _bodySearchRepository;

    internal SchemaV2RepositoryProvider(
        IClock clock,
        SchemaV2BodySearchCandidate bodySearchCandidate = SchemaV2BodySearchCandidate.Scan)
    {
        _pageRepository = new SchemaV2PageRepository(
            clock ?? throw new ArgumentNullException(nameof(clock)));
        _bodySearchRepository = CreateBodySearchReader(bodySearchCandidate);
    }

    internal static INotebookBodySearchReader CreateBodySearchReader(
        SchemaV2BodySearchCandidate bodySearchCandidate) => bodySearchCandidate switch
        {
            SchemaV2BodySearchCandidate.Scan => new SchemaV2BodySearchRepository(),
            SchemaV2BodySearchCandidate.FtsUnicode61 => new SchemaV2FtsBodySearchRepository(
                SchemaV2FtsBodySearchKind.Unicode61),
            SchemaV2BodySearchCandidate.FtsTrigram => new SchemaV2FtsBodySearchRepository(
                SchemaV2FtsBodySearchKind.Trigram),
            _ => throw new ArgumentOutOfRangeException(nameof(bodySearchCandidate))
        };

    public NotebookRepositoryDescriptor Descriptor => SchemaDescriptor;

    public INotebookMetadataReader MetadataReader => _metadataRepository;

    public INotebookMetadataWriter MetadataWriter => _metadataRepository;

    public INotebookPageReader PageReader => _pageRepository;

    public INotebookBodySearchReader BodySearchReader => _bodySearchRepository;

    public INotebookPageWriter PageWriter => _pageRepository;
}
