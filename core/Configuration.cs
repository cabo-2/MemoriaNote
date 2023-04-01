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
    [DataContract]
    public class Configuration : ConfigurationBase
    {
        [DataMember][Reactive] public List<string> DataSources { get; set; } = new List<string>();
        [DataMember][Reactive] public WorkgroupBuilder Workgroup { get; set; }

        public Configuration()
        {}

        [DataMember][Reactive] public LoggingSetting Logging { get; set; } = new LoggingSetting();
        [DataContract]
        public class LoggingSetting : ConfigurationBase
        {
            [DataMember][Reactive] public LoggerType Logger { get; set; }
            [DataMember][Reactive] public string LogFilePath { get; set; }
        }

        [DataMember][Reactive] public SearchSetting Search { get; set; } = new SearchSetting();
        [DataContract]
        public class SearchSetting : ConfigurationBase
        {
            [DataMember][Reactive] public int MaxHistoryCount { get; set; } = 10;
            [DataMember][Reactive] public int MaxViewResultCount { get; set; } = 1000;
        }

        protected T GetDefault<T>() where T : Configuration, new()
        {
            var config = Activator.CreateInstance (typeof(T)) as T;

            config.DataSources.Add(DefaultDataSourcePath);
            config.Workgroup = WorkgroupBuilder.Generate(DefaultWorkgroupName, config.DataSources);

            SetDefault(config);

            return config;
        }

        protected virtual void SetDefault<T>(T value) where T : Configuration, new()
        {
        }

        public virtual void Save ()
        {
            var path = ConfigurationPath;
            var dir = Path.GetDirectoryName(path);
            if( !Directory.Exists(dir) ) {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText (path, JsonConvert.SerializeObject (this, Formatting.Indented));
        }

        public virtual T Load<T>() where T : Configuration, new()
        {
            var path = ConfigurationPath;
            if(!File.Exists(path)) 
            {
                var value = new T().GetDefault<T>(); 
                value.Save();
                return value;
            }
            
            try
            {             
                return JsonConvert.DeserializeObject<T>
                       (File.ReadAllText(path));
            }
            catch
            {
                return new T().GetDefault<T>();         
            }
        }

        // Windows: C:\Users\<user>\AppData\Roaming\MemoriaNote\configuration.json
        // Linux:  /home/<user>/.config/MemoriaNote/configuration.json
        public static string ApplicationName => "MemoriaNote";
        protected virtual string ConfigurationFilename => "configuration.json";        
        public virtual string ApplicationDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationName);
        public string ConfigurationPath => Path.Combine(ApplicationDataDirectory, ConfigurationFilename);

        protected virtual string DefaultDataSourceName => "Notepad.db";
        public string DefaultDataSourcePath => Path.Combine(ApplicationDataDirectory, DefaultDataSourceName);

        [DataMember]
        public string DefaultWorkgroupName { get; set; } = "My Notes";
        [DataMember]
        public string DefaultNoteTitle { get; set; } = "Notepad";
        [DataMember]
        public string DefaultNoteName { get; set; } = "note";

        public static string AllNotesSearchString => "All Notes Search";
        public static string SelectedNoteSearchString => "Selected Note Search";
        public static string GoogleSearchUrl => "https://www.google.com";
        public static int MaxAllNoteSearchCount => 10;

        protected static Configuration _instance;
        public static Configuration Instance { 
            get
            {
                if (_instance == null)
                    _instance = new Configuration().GetDefault<Configuration>();
                return _instance;
            }
            set
            {
                _instance = value;
            }
        }

        public static T Create<T>() where T : Configuration, new() => new T().Load<T>();
    }

    public abstract class ConfigurationBase : ReactiveObject
    {
    }
}
