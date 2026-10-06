using System;
using MemoriaNote;

namespace MemoriaNote.Application
{
    /// <summary>
    /// Contains a loaded workspace and its composed application service.
    /// </summary>
    public sealed class ApplicationSession
    {
        /// <summary>
        /// Initializes an application session.
        /// </summary>
        /// <param name="workspace">The loaded workspace.</param>
        /// <param name="applicationService">The composed application service.</param>
        public ApplicationSession(
            Workspace workspace,
            IMemoriaNoteApplicationService applicationService)
        {
            Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            ApplicationService = applicationService ??
                throw new ArgumentNullException(nameof(applicationService));
        }

        /// <summary>Gets the loaded workspace.</summary>
        public Workspace Workspace { get; }

        /// <summary>Gets the composed application service.</summary>
        public IMemoriaNoteApplicationService ApplicationService { get; }
    }
}
