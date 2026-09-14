using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MemoriaNote
{
    /// <summary>
    /// Represents the persisted settings for the Memoria Note application.
    /// </summary>
    [DataContract]
    public class Configuration
    {
        /// <summary>Represents the application's data sources.</summary>
        [DataMember]
        public List<string> DataSources { get; set; } = new List<string>();

        /// <summary>Represents the workspace settings.</summary>
        [DataMember(Name = "Workgroup")]
        public WorkspaceSettings Workspace { get; set; }

        /// <summary>Represents the logging settings.</summary>
        [DataMember]
        public LoggingSetting Logging { get; set; } = new LoggingSetting();

        /// <summary>Represents persisted logging options.</summary>
        [DataContract]
        public class LoggingSetting
        {
            /// <summary>Gets or sets the logger type.</summary>
            [DataMember]
            public LoggerType Logger { get; set; }

            /// <summary>Gets or sets the log file path.</summary>
            [DataMember]
            public string LogFilePath { get; set; }
        }

        /// <summary>Represents the search settings.</summary>
        [DataMember]
        public SearchSetting Search { get; set; } = new SearchSetting();

        /// <summary>Represents persisted search options.</summary>
        [DataContract]
        public class SearchSetting
        {
            /// <summary>Gets or sets the maximum search history count.</summary>
            [DataMember]
            public int MaxHistoryCount { get; set; } = 10;

            /// <summary>Gets or sets the maximum displayed result count.</summary>
            [DataMember]
            public int MaxViewResultCount { get; set; } = 1000;
        }

        /// <summary>Gets or sets the default workspace name.</summary>
        [DataMember(Name = "DefaultWorkgroupName")]
        public string DefaultWorkspaceName { get; set; } = "My Notes";

        /// <summary>Gets or sets the default notebook title.</summary>
        [DataMember(Name = "DefaultNoteTitle")]
        public string DefaultNotebookTitle { get; set; } = "Notepad";

        /// <summary>Gets or sets the default notebook name.</summary>
        [DataMember(Name = "DefaultNoteName")]
        public string DefaultNotebookName { get; set; } = "note";
    }
}
