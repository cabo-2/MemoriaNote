using System;
using MemoriaNote.Models;

namespace MemoriaNote.Persistence
{
    /// <summary>Classifies an atomic compare-and-update of one page body.</summary>
    public enum PageTextUpdateStatus
    {
        /// <summary>The expected body matched and the replacement was persisted.</summary>
        Success,

        /// <summary>The page no longer exists.</summary>
        PageNotFound,

        /// <summary>The page body changed after it was read.</summary>
        Conflict
    }

    /// <summary>Represents the result of atomically replacing one page body.</summary>
    public sealed class PageTextUpdateResult
    {
        PageTextUpdateResult(PageTextUpdateStatus status, Page page)
        {
            Status = status;
            Page = page;
        }

        /// <summary>Gets the classified update status.</summary>
        public PageTextUpdateStatus Status { get; }

        /// <summary>Gets the persisted page when the update succeeded.</summary>
        public Page Page { get; }

        /// <summary>Creates a successful update result.</summary>
        public static PageTextUpdateResult Succeeded(Page page)
        {
            return new PageTextUpdateResult(
                PageTextUpdateStatus.Success,
                page ?? throw new ArgumentNullException(nameof(page)));
        }

        /// <summary>Creates a failed update result.</summary>
        public static PageTextUpdateResult Failed(PageTextUpdateStatus status)
        {
            if (status == PageTextUpdateStatus.Success)
                throw new ArgumentOutOfRangeException(nameof(status));

            return new PageTextUpdateResult(status, null);
        }
    }
}
