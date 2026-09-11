using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote
{
    /// <summary>
    /// Classifies the outcome of a page application operation.
    /// </summary>
    public enum PageOperationStatus
    {
        /// <summary>The operation succeeded.</summary>
        Success,

        /// <summary>One or more input validation rules failed.</summary>
        ValidationFailed,

        /// <summary>The owning note is no longer in the current context.</summary>
        OwnerNotFound,

        /// <summary>The requested page no longer exists.</summary>
        PageNotFound,

        /// <summary>The owning note does not permit writes.</summary>
        ReadOnly
    }

    /// <summary>
    /// Identifies a page operation error without presentation-specific wording.
    /// </summary>
    public enum PageErrorCode
    {
        /// <summary>No page is currently open in the compatibility API.</summary>
        PageNotSelected,

        /// <summary>The page name is empty or whitespace.</summary>
        NameRequired,

        /// <summary>The requested page name is already in use.</summary>
        DuplicateName,

        /// <summary>The owning note is no longer in the current context.</summary>
        OwnerNotFound,

        /// <summary>The requested page no longer exists.</summary>
        PageNotFound,

        /// <summary>The owning note does not permit writes.</summary>
        ReadOnly
    }

    /// <summary>
    /// Represents an immutable, classifiable page operation outcome.
    /// </summary>
    public sealed class PageOperationResult
    {
        PageOperationResult(
            PageOperationStatus status,
            Page page,
            IEnumerable<PageErrorCode> errors)
        {
            Status = status;
            Page = page;
            Errors = Array.AsReadOnly((errors ?? Enumerable.Empty<PageErrorCode>()).ToArray());
        }

        /// <summary>
        /// Gets the classified outcome.
        /// </summary>
        public PageOperationStatus Status { get; }

        /// <summary>
        /// Gets the page returned by a successful read or mutation.
        /// </summary>
        public Page Page { get; }

        /// <summary>
        /// Gets the machine-readable errors in validation order.
        /// </summary>
        public IReadOnlyList<PageErrorCode> Errors { get; }

        /// <summary>
        /// Gets a value indicating whether the operation succeeded.
        /// </summary>
        public bool IsSuccess => Status == PageOperationStatus.Success;

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        /// <param name="page">The resulting page, when the operation returns one.</param>
        /// <returns>A successful result.</returns>
        public static PageOperationResult Succeeded(Page page = null)
        {
            return new PageOperationResult(
                PageOperationStatus.Success,
                page,
                Array.Empty<PageErrorCode>());
        }

        /// <summary>
        /// Creates a validation failure.
        /// </summary>
        /// <param name="errors">The validation error codes.</param>
        /// <returns>A validation failure.</returns>
        public static PageOperationResult ValidationFailed(
            IEnumerable<PageErrorCode> errors)
        {
            if (errors == null)
                throw new ArgumentNullException(nameof(errors));

            var materialized = errors.ToArray();
            if (materialized.Length == 0)
                throw new ArgumentException("At least one validation error is required.", nameof(errors));

            return new PageOperationResult(
                PageOperationStatus.ValidationFailed,
                null,
                materialized);
        }

        /// <summary>
        /// Creates a failure with one classified error.
        /// </summary>
        /// <param name="status">The non-success status.</param>
        /// <param name="error">The corresponding error code.</param>
        /// <returns>A classified failure.</returns>
        public static PageOperationResult Failed(
            PageOperationStatus status,
            PageErrorCode error)
        {
            if (status == PageOperationStatus.Success ||
                status == PageOperationStatus.ValidationFailed)
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            return new PageOperationResult(status, null, new[] { error });
        }
    }
}
