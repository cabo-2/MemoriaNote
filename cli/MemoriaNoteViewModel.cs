using System;
using System.Text;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using NStack;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Logging;

namespace MemoriaNote.Cli
{

    /// <summary>
    /// This class represents the ViewModel for MemoriaNote application, inheriting from MemoriaNoteService
    /// </summary>
    [DataContract]
    public class MemoriaNoteViewModel : MemoriaNoteService
    {
        /// <summary>
        /// Initializes a view model from persisted CLI configuration.
        /// </summary>
        /// <param name="configuration">The persisted CLI configuration.</param>
        /// <param name="defaultNotebookDatabasePath">The default notebook database path.</param>
        public MemoriaNoteViewModel(
            ConfigurationCli configuration,
            string defaultNotebookDatabasePath)
            : base(configuration, defaultNotebookDatabasePath)
        {
            Configuration = configuration ??
                throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>Initializes a view model from an explicitly composed session.</summary>
        /// <param name="configuration">The persisted CLI configuration.</param>
        /// <param name="session">The composed application session.</param>
        /// <param name="logger">The inherited presentation-service logger.</param>
        public MemoriaNoteViewModel(
            ConfigurationCli configuration,
            ApplicationSession session,
            ILogger<MemoriaNoteService> logger)
            : base(session, logger)
        {
            Configuration = configuration ??
                throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>Gets the persisted CLI configuration for the current session.</summary>
        public ConfigurationCli Configuration { get; }
    }
}
