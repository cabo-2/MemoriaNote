using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Transfer;

namespace MemoriaNote.Persistence
{
    /// <summary>Applies a preflighted set of text pages to one notebook atomically.</summary>
    public interface ITextPageImportRepository
    {
        /// <summary>Rechecks conflicts and applies or previews the complete import.</summary>
        Task<TextPageImportResult> ImportAsync(
            NotebookId notebookId,
            IReadOnlyCollection<TextPageImportItem> items,
            TextPageImportConflictPolicy conflictPolicy,
            bool dryRun,
            CancellationToken token);
    }
}
