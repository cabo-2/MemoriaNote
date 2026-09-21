using System;
using MemoriaNote.Domain;

namespace MemoriaNote.Application
{
    /// <summary>Classifies the result of resolving a user-facing page selector.</summary>
    public enum PageTargetResolutionStatus
    {
        /// <summary>Exactly one page was resolved.</summary>
        Success,

        /// <summary>The owning notebook is not available.</summary>
        OwnerNotFound,

        /// <summary>No page matched the selector.</summary>
        PageNotFound,

        /// <summary>More than one page matched the selector.</summary>
        Conflict
    }

    /// <summary>Represents an immutable page target resolution result.</summary>
    public sealed class PageTargetResolution
    {
        PageTargetResolution(
            PageTargetResolutionStatus status,
            PageReference target)
        {
            Status = status;
            Target = target;
        }

        /// <summary>Gets the classified resolution status.</summary>
        public PageTargetResolutionStatus Status { get; }

        /// <summary>Gets the unique target when resolution succeeded.</summary>
        public PageReference Target { get; }

        /// <summary>Gets whether exactly one target was resolved.</summary>
        public bool IsSuccess => Status == PageTargetResolutionStatus.Success;

        /// <summary>Creates a successful resolution.</summary>
        /// <param name="target">The unique owner-qualified page target.</param>
        /// <returns>The successful result.</returns>
        public static PageTargetResolution Succeeded(PageReference target)
        {
            return new PageTargetResolution(
                PageTargetResolutionStatus.Success,
                target ?? throw new ArgumentNullException(nameof(target)));
        }

        /// <summary>Creates a failed resolution.</summary>
        /// <param name="status">The non-success resolution status.</param>
        /// <returns>The failed result.</returns>
        public static PageTargetResolution Failed(PageTargetResolutionStatus status)
        {
            if (status == PageTargetResolutionStatus.Success)
                throw new ArgumentOutOfRangeException(nameof(status));

            return new PageTargetResolution(status, null);
        }
    }

    /// <summary>Describes a page selector within one explicit notebook.</summary>
    public sealed class PageTargetRequest
    {
        /// <summary>Initializes a page target request.</summary>
        /// <param name="notebookId">The notebook to search.</param>
        /// <param name="selector">The exact-name or Page ID selector.</param>
        public PageTargetRequest(NotebookId notebookId, PageSelector selector)
        {
            NotebookId = notebookId ?? throw new ArgumentNullException(nameof(notebookId));
            Selector = selector ?? throw new ArgumentNullException(nameof(selector));
        }

        /// <summary>Gets the notebook to search.</summary>
        public NotebookId NotebookId { get; }

        /// <summary>Gets the page selector.</summary>
        public PageSelector Selector { get; }
    }
}
