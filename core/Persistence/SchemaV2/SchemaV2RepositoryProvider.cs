using System;
using MemoriaNote.Persistence.Repositories;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Supplies the operational repositories for notebook format version 2.</summary>
internal sealed class SchemaV2RepositoryProvider : IWritableNotebookRepositoryProvider
{
    private static readonly NotebookRepositoryDescriptor SchemaDescriptor = new(
        NotebookFormatVersion.Version2,
        NotebookRepositoryCapabilities.OperationalRead |
        NotebookRepositoryCapabilities.OperationalWrite);

    private readonly SchemaV2MetadataRepository _metadataRepository = new();
    private readonly SchemaV2PageRepository _pageRepository;
    private readonly SchemaV2BodySearchRepository _bodySearchRepository = new();

    internal SchemaV2RepositoryProvider(IClock clock)
    {
        _pageRepository = new SchemaV2PageRepository(
            clock ?? throw new ArgumentNullException(nameof(clock)));
    }

    public NotebookRepositoryDescriptor Descriptor => SchemaDescriptor;

    public INotebookMetadataReader MetadataReader => _metadataRepository;

    public INotebookMetadataWriter MetadataWriter => _metadataRepository;

    public INotebookPageReader PageReader => _pageRepository;

    public INotebookBodySearchReader BodySearchReader => _bodySearchRepository;

    public INotebookPageWriter PageWriter => _pageRepository;
}
