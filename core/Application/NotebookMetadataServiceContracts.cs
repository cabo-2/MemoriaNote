using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Models;

namespace MemoriaNote.Application
{
    /// <summary>Identifies a user-editable notebook metadata field.</summary>
    public enum NotebookMetadataField
    {
        /// <summary>The notebook display name.</summary>
        Name,

        /// <summary>The notebook display title.</summary>
        Title,

        /// <summary>The optional notebook description.</summary>
        Description,

        /// <summary>The optional notebook author.</summary>
        Author,

        /// <summary>The optional notebook tag.</summary>
        Tag,

        /// <summary>The logical page-write protection setting.</summary>
        ReadOnly
    }

    /// <summary>Requests an update to exactly one user-editable metadata field.</summary>
    public sealed class NotebookMetadataUpdateRequest
    {
        /// <summary>Initializes a single-field metadata update.</summary>
        /// <param name="notebookId">The target notebook.</param>
        /// <param name="field">The field to update.</param>
        /// <param name="value">The proposed serialized value.</param>
        public NotebookMetadataUpdateRequest(
            NotebookId notebookId,
            NotebookMetadataField field,
            string value)
        {
            NotebookId = notebookId ?? throw new ArgumentNullException(nameof(notebookId));
            if (!Enum.IsDefined(field))
                throw new ArgumentOutOfRangeException(nameof(field));

            Field = field;
            Value = value;
        }

        /// <summary>Gets the target notebook.</summary>
        public NotebookId NotebookId { get; }

        /// <summary>Gets the field to update.</summary>
        public NotebookMetadataField Field { get; }

        /// <summary>Gets the proposed serialized value.</summary>
        public string Value { get; }
    }

    /// <summary>Contains the classified outcome of a notebook metadata update.</summary>
    public sealed class NotebookMetadataUpdateResult
    {
        NotebookMetadataUpdateResult(
            NotebookMetadata metadata,
            bool changed,
            IEnumerable<NotebookMetadataErrorCode> errors)
        {
            Metadata = metadata;
            Changed = changed;
            Errors = Array.AsReadOnly(
                (errors ?? throw new ArgumentNullException(nameof(errors))).ToArray());
        }

        /// <summary>Gets whether validation and any required write succeeded.</summary>
        public bool IsSuccess => Errors.Count == 0;

        /// <summary>Gets whether a value was persisted.</summary>
        public bool Changed { get; }

        /// <summary>Gets the resulting metadata snapshot after success.</summary>
        public NotebookMetadata Metadata { get; }

        /// <summary>Gets stable validation failures.</summary>
        public IReadOnlyList<NotebookMetadataErrorCode> Errors { get; }

        /// <summary>Creates a successful update result.</summary>
        /// <param name="metadata">The resulting metadata snapshot.</param>
        /// <param name="changed">Whether a value was persisted.</param>
        /// <returns>A successful result.</returns>
        public static NotebookMetadataUpdateResult Succeeded(
            NotebookMetadata metadata,
            bool changed)
        {
            return new NotebookMetadataUpdateResult(
                metadata ?? throw new ArgumentNullException(nameof(metadata)),
                changed,
                Array.Empty<NotebookMetadataErrorCode>());
        }

        /// <summary>Creates a validation failure result.</summary>
        /// <param name="errors">The stable validation errors.</param>
        /// <returns>A validation failure.</returns>
        public static NotebookMetadataUpdateResult ValidationFailed(
            IEnumerable<NotebookMetadataErrorCode> errors)
        {
            if (errors == null)
                throw new ArgumentNullException(nameof(errors));
            var materialized = errors.ToArray();
            if (materialized.Length == 0)
                throw new ArgumentException("At least one error is required.", nameof(errors));

            return new NotebookMetadataUpdateResult(null, false, materialized);
        }
    }

    /// <summary>Loads and updates notebook metadata independently of CLI presentation.</summary>
    public interface INotebookMetadataService
    {
        /// <summary>Loads the current metadata snapshot for one notebook.</summary>
        Task<NotebookMetadataResult> GetAsync(
            NotebookId notebookId,
            CancellationToken token);

        /// <summary>Validates and applies one metadata field update.</summary>
        Task<NotebookMetadataUpdateResult> UpdateAsync(
            NotebookMetadataUpdateRequest request,
            CancellationToken token);
    }
}
