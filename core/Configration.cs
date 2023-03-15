using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using ReactiveUI;

namespace MemoriaNote
{
    [Serializable]
    public class Configuration : ConfigurationBase
    {
        protected List<string> _dataSources = new List<string>();
        public List<string> DataSources
        {
            get => _dataSources;
            set => this.RaiseAndSetIfChanged(ref _dataSources, value);
        }

        protected List<BookGroupBuilder> _bookGroupBuilders = new List<BookGroupBuilder>();
        public List<BookGroupBuilder> BookGroupBuilders
        {
            get => _bookGroupBuilders;
            set => this.RaiseAndSetIfChanged(ref _bookGroupBuilders, value);
        }

        public Configuration()
        {}

        protected SearchSetting _search = new SearchSetting();
        public SearchSetting Search
        {
            get => _search;
            set => this.RaiseAndSetIfChanged(ref _search, value);
        }
        public class SearchSetting : ConfigurationBase
        {
            protected int _maxHistoryCount = 10;
            public int MaxHistoryCount
            {
                get => _maxHistoryCount;
                set => this.RaiseAndSetIfChanged(ref _maxHistoryCount, value);
            }

            protected int _maxViewResultCount = 10000;
            public int MaxViewResultCount
            {
                get => _maxViewResultCount;
                set => this.RaiseAndSetIfChanged(ref _maxViewResultCount, value);
            }
        }

        public void Save () {
            File.WriteAllText (ConfigurationFilename, JsonConvert.SerializeObject (this, Formatting.Indented));
        }

        public static string AllNotesSearchString => "All Notes Search";
        public static string SelectedNoteSearchString => "Selected Note Search";
        public static string GoogleSearchUrl => "https://www.google.com";
        public static int MaxAllNoteSearchCount => 10;
        public static List<string> SearchMethods => new List<string>(new string[] { "Headline", "Full text" });

        public static string SystemNoteDataSource => "Memoria.db";
        public static string SystemNoteName => "Notepad";
        public static string ConfigurationFilename => "configuration.json";

        public static Configuration Default => new Configuration();

        protected static Configuration _instance;
        public static Configuration Instance { 
            get
            {
                if (_instance == null)
                    _instance = Configuration.Default;
                return _instance;
            }
            set
            {
                _instance = value;
            }
        }

        public static Configuration Create()
        {
            if (File.Exists(ConfigurationFilename))
            {
                try
                {
                    var config = JsonConvert.DeserializeObject<Configuration>
                                    (File.ReadAllText(ConfigurationFilename));
                    return config;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    return new Configuration();
                }
            }
            else
            {
                var config = new Configuration();
                config.DataSources.Add(SystemNoteDataSource);
                config.BookGroupBuilders.Add(BookGroupBuilder.GetSelectedNoteSearch(config.DataSources));
                config.BookGroupBuilders.Add(BookGroupBuilder.GetAllNotesSearch(config.DataSources));
                config.Save();
                return config;
            }
        }
    }

    public class ConfigurationBase : ReactiveObject
    {
    }
}
