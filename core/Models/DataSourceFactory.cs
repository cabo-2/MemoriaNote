using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MemoriaNote
{
    public class DataSourceFactory
    {
        protected static DataSourceFactory _instance = null;
        public static DataSourceFactory Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();

                return _instance;
            }
        }

        protected List<DataSourceTracker> Sources { get; set; } = new List<DataSourceTracker>();

        public IDataSource Add(string dataSource)
        {
            if (FindFromDataSource(dataSource) != null)
                throw new ArgumentException(nameof(dataSource));

            Metadata meta = new Metadata(dataSource);
            if (!string.IsNullOrWhiteSpace(meta.Tag) && FindFromNameTag(meta.Name, meta.Tag) != null)
                RemoveNameTag(meta.Name, meta.Tag);

            DataSourceTracker value = DataSourceTracker.Create(meta.Name, dataSource, meta.Tag);
            Sources.Add(value);
            return value.Clone();
        }

        public void RemoveNameTag(string name, string tag)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (tag == null)
                throw new ArgumentNullException(nameof(tag));

            var list = (from s in Sources
                        where !(s.Name == name && s.Tag == tag)
                        select s).ToList();
            Sources = list;
        }
        public void RemoveDataSource(string dataSource)
        {
            if (dataSource == null)
                throw new ArgumentNullException(nameof(dataSource));

            var list = (from s in Sources
                        where !(s.DataSource == dataSource)
                        select s).ToList();
            Sources = list;
        }
        public void RemoveName(string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            var list = (from s in Sources
                        where !(s.Name == name)
                        select s).ToList();
            Sources = list;
        }

        public IDataSource FindFromNameTag(string name, string tag)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (tag == null)
                throw new ArgumentNullException(nameof(tag));

            return Sources.FirstOrDefault(t => t.Name == name && (t.Tag == tag || t.CreateTime.ToDateString() == tag))?.Clone();
        }
        public IDataSource FindFromDataSource(string dataSource)
        {
            if (dataSource == null)
                throw new ArgumentNullException(nameof(dataSource));

            return Sources.Where(s => s.DataSource == dataSource).FirstOrDefault()?.Clone();
        }
        public IEnumerable<IDataSource> FindFromName(string name)
        {
            var list = (from s in Sources
                        where s.Name == name
                        orderby s.CreateTime descending
                        select s.Clone()).ToList();

            return list.Count > 0 ? list : null;
        }

        public void Save()
        {
            var path = Configuration.Instance.DataSourcesPath;
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var sources = (from s in Sources
                           orderby s.Name
                           orderby s.CreateTime descending
                           select s).ToList();
            File.WriteAllText(path, JsonConvert.SerializeObject(sources, Formatting.Indented));
        }

        protected static DataSourceFactory Load()
        {
            var path = Configuration.Instance.DataSourcesPath;
            if (!File.Exists(path))
            {
                var value = new DataSourceFactory();
                value.Save();
                return value;
            }

            try
            {
                var value = new DataSourceFactory();
                value.Sources = JsonConvert.DeserializeObject<List<DataSourceTracker>>
                                (File.ReadAllText(path));
                return value;
            }
            catch (Exception e)
            {
                Log.Logger.Error(e.Message);
                Log.Logger.Error(e.StackTrace);

                var value = new DataSourceFactory();
                value.Save();
                return value;
            }
        }
    }
}