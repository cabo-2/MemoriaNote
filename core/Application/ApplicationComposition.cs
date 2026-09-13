using System;

namespace MemoriaNote
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

            var resolver = new WorkspaceNoteContextResolver(() => workspace.Notebooks);
            var pageUseCase = new PageUseCase(resolver, new PageValidationPolicy());
            var searchUseCase = new SearchUseCase(
                noteId => resolver.Resolve(noteId)?.SearchRepository);
            var applicationService = new MemoriaNoteApplicationService(
                searchUseCase,
                pageUseCase);
            return new ApplicationSession(workspace, applicationService);
        }
    }
}
