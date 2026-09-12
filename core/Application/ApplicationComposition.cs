using System;

namespace MemoriaNote
{
    /// <summary>
    /// Composes the application use cases for a loaded workgroup.
    /// </summary>
    public static class ApplicationComposition
    {
        /// <summary>
        /// Creates an application session for a loaded workgroup.
        /// </summary>
        /// <param name="workgroup">The workgroup whose notes provide application contexts.</param>
        /// <returns>The workgroup and its composed application service.</returns>
        public static ApplicationSession Compose(Workgroup workgroup)
        {
            if (workgroup == null)
                throw new ArgumentNullException(nameof(workgroup));

            var resolver = new WorkgroupNoteContextResolver(() => workgroup.Notes);
            var pageUseCase = new PageUseCase(resolver, new PageValidationPolicy());
            var searchUseCase = new SearchUseCase(
                noteId => resolver.Resolve(noteId)?.SearchRepository);
            var applicationService = new MemoriaNoteApplicationService(
                searchUseCase,
                pageUseCase);
            return new ApplicationSession(workgroup, applicationService);
        }
    }
}
