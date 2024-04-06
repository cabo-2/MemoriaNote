using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Microsoft.Extensions.Logging;

namespace MemoriaNote
{
    /// <summary>
    /// This class represents the Configuration for the MemoriaNote application and inherits from ConfigurationBase. 
    /// It is marked with the DataContract attribute to indicate that it is serializable.
    /// </summary>
    [DataContract]
    public class Configuration : ConfigurationBase
    {
        /// <summary>
        /// Represents a list of data sources used by the application
        /// </summary>
        [DataMember]
        [Reactive]
        public List<string> DataSources { get; set; } = new List<string>();

        /// <summary>
        /// Represents the workgroup builder used by the application
        /// </summary>
        [DataMember]
        [Reactive]
        public WorkgroupBuilder Workgroup { get; set; }

        public Configuration()
        { }

        /// <summary>
        /// Represents the Logging settings for the application
        /// Inherits from ConfigurationBase and is marked with DataContract attribute to indicate serializability
        /// </summary>
        [DataMember]
        [Reactive]
        public LoggingSetting Logging { get; set; } = new LoggingSetting();

        /// <summary>
        /// Represents the detailed Logging configurations
        /// </summary>
        [DataContract]
        public class LoggingSetting : ConfigurationBase
        {
            /// <summary>
            /// Represents the type of Logger used for logging
            /// </summary>
            [DataMember]
            [Reactive]
            public LoggerType Logger { get; set; }

            /// <summary>
            /// Represents the file path where logs are stored
            /// </summary>
            [DataMember]
            [Reactive]
            public string LogFilePath { get; set; }
        }

        /// <summary>
        /// Represents the Search settings for the application 
        /// Inherits from ConfigurationBase and is marked with DataContract attribute to indicate serializability
        /// </summary>
        [DataMember]
        [Reactive]
        public SearchSetting Search { get; set; } = new SearchSetting();

        /// <summary>
        /// Represents the detailed Search configurations
        /// </summary>
        [DataContract]
        public class SearchSetting : ConfigurationBase
        {
            /// <summary>
            /// Represents the maximum number of search history entries to keep
            /// </summary>
            [DataMember]
            [Reactive]
            public int MaxHistoryCount { get; set; } = 10;

            /// <summary>
            /// Represents the maximum number of search results to display
            /// </summary>
            [DataMember]
            [Reactive]
            public int MaxViewResultCount { get; set; } = 1000;
        }

        /// <summary>
        /// Gets the default configuration values for a specified type T.
        /// Adds the default data source path to the list of data sources and generates a default workgroup using WorkgroupBuilder.
        /// </summary>
        /// <typeparam name="T">The type of Configuration</typeparam>
        /// <returns>The default configuration values for the specified type T</returns>
        protected T GetDefault<T>() where T : Configuration, new()
        {
            var config = Activator.CreateInstance(typeof(T)) as T;

            config.DataSources.Add(DefaultDataSourcePath);
            config.Workgroup = WorkgroupBuilder.Generate(DefaultWorkgroupName, config.DataSources);

            SetDefault(config);

            return config;
        }

        /// <summary>
        /// Sets the default values for the specified type T.
        /// This method can be overridden to provide custom default settings.
        /// </summary>
        /// <typeparam name="T">The type of Configuration</typeparam>
        /// <param name="value">The configuration object for which default values need to be set</param>
        protected virtual void SetDefault<T>(T value) where T : Configuration, new()
        {
        }

        /// <summary>
        /// Saves the current configuration to a JSON file.
        /// Creates the directory if it doesn't exist.
        /// </summary>
        public virtual void Save()
        {
            var path = ConfigurationPath;
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        /// <summary>
        /// Loads the configuration from a JSON file.
        /// If the file doesn't exist, creates a new default configuration and saves it.
        /// </summary>
        /// <typeparam name="T">The type of Configuration</typeparam>
        /// <returns>The loaded configuration</returns>
        public virtual T Load<T>() where T : Configuration, new()
        {
            var path = ConfigurationPath;
            if (!File.Exists(path))
            {
                var value = new T().GetDefault<T>();
                value.Save();
                return value;
            }

            try
            {
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
            }
            catch
            {
                return new T().GetDefault<T>();
            }
        }

        // Windows: C:\Users\<user>\AppData\Roaming\MemoriaNote\configuration.json
        // Linux:  /home/<user>/.config/MemoriaNote/configuration.json
        
        /// <summary>
        /// Represents the name of the application
        /// </summary>
        public static string ApplicationName => "MemoriaNote";
        
        /// <summary>
        /// Represents the filename of the configuration file
        /// </summary>
        public virtual string ConfigurationFilename => "configuration.json";

        /// <summary>
        /// Represents the directory where application data is stored
        /// </summary>
        public virtual string ApplicationDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationName);
        
        /// <summary>
        /// Represents the full path to the configuration file
        /// </summary>
        public string ConfigurationPath => Path.Combine(ApplicationDataDirectory, ConfigurationFilename);

        /// <summary>
        /// Represents the filename of the data sources file
        /// </summary>
        public virtual string DataSourcesFilename => "data-sources.json";
        
        /// <summary>
        /// Represents the full path to the data sources file
        /// </summary>
        public string DataSourcesPath => Path.Combine(ApplicationDataDirectory, DataSourcesFilename);

        /// <summary>
        /// Represents the default name of the data source file
        /// </summary>
        protected virtual string DefaultDataSourceName => "Notepad.db";
        
        /// <summary>
        /// Represents the default path to the data source file
        /// </summary>
        public string DefaultDataSourcePath => Path.Combine(ApplicationDataDirectory, DefaultDataSourceName);

        /// <summary>
        /// Represents the default workgroup name for notes
        /// </summary>
        [DataMember]
        public string DefaultWorkgroupName { get; set; } = "My Notes";

        /// <summary>
        /// Represents the default title for notes
        /// </summary>
        [DataMember]
        public string DefaultNoteTitle { get; set; } = "Notepad";

        /// <summary>
        /// Represents the default name for notes
        /// </summary>
        [DataMember]
        public string DefaultNoteName { get; set; } = "note";

        public static string AllNotesSearchString => "All Notes Search";
        public static string SelectedNoteSearchString => "Selected Note Search";
        public static string GoogleSearchUrl => "https://www.google.com";
        public static int MaxAllNoteSearchCount => 10;

        /// <summary>
        /// Represents the singleton instance of the configuration
        /// </summary>
        protected static Configuration _instance;

        /// <summary>
        /// Gets or sets the singleton instance of the configuration
        /// </summary>
        public static Configuration Instance
        {
            get => _instance;
            set => _instance = value;
        }

        /// <summary>
        /// Creates an instance of type T and loads the configuration from a JSON file
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T Create<T>() where T : Configuration, new() => new T().Load<T>();
    }

    /// <summary>
    /// Abstract base class for configuration classes.
    /// Inherits from ReactiveObject to support reactive programming.
    /// </summary>
    public abstract class ConfigurationBase : ReactiveObject
    {
    }
}
