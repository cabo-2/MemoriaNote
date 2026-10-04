using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Models;
using MemoriaNote.Persistence;

namespace MemoriaNote.Application
{
    /// <summary>Coordinates validated, single-field notebook metadata updates.</summary>
    public sealed class NotebookMetadataService : INotebookMetadataService
    {
        readonly INotebookMetadataRepository _repository;
        readonly NotebookMetadataValidationPolicy _validationPolicy;

        /// <summary>Initializes the metadata service.</summary>
        /// <param name="repository">The metadata persistence port.</param>
        public NotebookMetadataService(INotebookMetadataRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _validationPolicy = new NotebookMetadataValidationPolicy();
        }

        /// <inheritdoc/>
        public Task<NotebookMetadataResult> GetAsync(
            Domain.NotebookId notebookId,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));

            return _repository.LoadAsync(notebookId.ToString(), token);
        }

        /// <inheritdoc/>
        public async Task<NotebookMetadataUpdateResult> UpdateAsync(
            NotebookMetadataUpdateRequest request,
            CancellationToken token)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            token.ThrowIfCancellationRequested();
            var errors = _validationPolicy.Validate(request.Field, request.Value);
            if (errors.Count > 0)
                return NotebookMetadataUpdateResult.ValidationFailed(errors);

            var loaded = await _repository
                .LoadAsync(request.NotebookId.ToString(), token)
                .ConfigureAwait(false);
            if (loaded.HasIssues)
            {
                throw new InvalidDataException(
                    $"The notebook metadata is invalid: {request.NotebookId}");
            }

            if (HasSameValue(loaded.Metadata, request))
                return NotebookMetadataUpdateResult.Succeeded(loaded.Metadata, changed: false);

            var patch = CreatePatch(request);
            var updated = await _repository
                .UpdateAsync(request.NotebookId.ToString(), patch, token)
                .ConfigureAwait(false);
            if (updated.HasIssues)
            {
                throw new InvalidDataException(
                    $"The updated notebook metadata is invalid: {request.NotebookId}");
            }

            return NotebookMetadataUpdateResult.Succeeded(updated.Metadata, changed: true);
        }

        static bool HasSameValue(
            NotebookMetadata metadata,
            NotebookMetadataUpdateRequest request)
        {
            return request.Field switch
            {
                NotebookMetadataField.Name => Equal(metadata.Name, request.Value),
                NotebookMetadataField.Title => Equal(metadata.Title, request.Value),
                NotebookMetadataField.Description => Equal(metadata.Description, request.Value),
                NotebookMetadataField.Author => Equal(metadata.Author, request.Value),
                NotebookMetadataField.Tag => Equal(metadata.Tag, request.Value),
                NotebookMetadataField.ReadOnly =>
                    metadata.ReadOnly == (request.Value == "true"),
                _ => throw new ArgumentOutOfRangeException(nameof(request.Field))
            };
        }

        static NotebookMetadataPatch CreatePatch(NotebookMetadataUpdateRequest request)
        {
            var patch = new NotebookMetadataPatch();
            return request.Field switch
            {
                NotebookMetadataField.Name => patch.SetName(request.Value),
                NotebookMetadataField.Title => patch.SetTitle(request.Value),
                NotebookMetadataField.Description => patch.SetDescription(request.Value),
                NotebookMetadataField.Author => patch.SetAuthor(request.Value),
                NotebookMetadataField.Tag => patch.SetTag(request.Value),
                NotebookMetadataField.ReadOnly => patch.SetReadOnly(request.Value == "true"),
                _ => throw new ArgumentOutOfRangeException(nameof(request.Field))
            };
        }

        static bool Equal(string left, string right) =>
            string.Equals(left, right, StringComparison.Ordinal);
    }
}
