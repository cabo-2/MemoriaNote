using System;
using MemoriaNote;
using MemoriaNote.Compatibility;

namespace MemoriaNote.Application
{
    /// <summary>
    /// Composes the application use cases for a loaded workspace.
    /// </summary>
    public static class ApplicationComposition
    {
        /// <summary>
        /// Creates an application session for a loaded workspace.
        /// </summary>
        /// <param name="workspace">The workspace whose notes provide application contexts.</param>
        /// <returns>The workspace and its composed application service.</returns>
        public static ApplicationSession Compose(Workspace workspace)
        {
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));

            var resolver = new WorkspaceNotebookContextResolver(() => workspace.Notebooks);
            var pageUseCase = new PageUseCase(resolver, new PageValidationPolicy());
            var searchUseCase = new SearchUseCase(
                notebookId => resolver.Resolve(notebookId)?.SearchRepository);
            var applicationService = new MemoriaNoteApplicationService(
                searchUseCase,
                pageUseCase);
            return new ApplicationSession(workspace, applicationService);
        }
    }
}
