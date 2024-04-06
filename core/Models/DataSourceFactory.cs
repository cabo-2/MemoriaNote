using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MemoriaNote
{
    /// <summary>
    /// Represents a class that manages data sources and provides methods to add, remove, and search for data sources.
    /// </summary>
    public class DataSourceFactory
    {
        protected static DataSourceFactory _instance = null;
        /// <summary>
        /// Gets the instance of the DataSourceFactory class. If the instance is null, it will be loaded using the Load method.
        /// </summary>
        /// <returns>The singleton instance of the DataSourceFactory class.</returns>
        public static DataSourceFactory Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();

                return _instance;
            }
        }

        /// <summary>
        /// Gets or sets the list of DataSourceTracker instances representing different data sources.
        /// </summary>
        protected List<DataSourceTracker> Sources { get; set; } = new List<DataSourceTracker>();

        /// <summary>
        /// Adds a new data source to the list of data sources.
        /// If the data source with the same name and tag already exists, it will be replaced with the new one.
        /// </summary>
        /// <param name="dataSource">The data source to add.</param>
        /// <returns>A cloned instance of the added data source.</returns>
        public IDataSource Add(string dataSource)
        {
            if (FindFromDataSource(dataSource) != null)
                throw new ArgumentException(nameof(dataSource));

            DataSourceTracker value = DataSourceTracker.Create(dataSource);
            if (!string.IsNullOrWhiteSpace(value.Tag) && FindFromNameTag(value.Name, value.Tag) != null)
                RemoveNameTag(value.Name, value.Tag);

            Sources.Add(value);
            return value.Clone();
        }

        /// <summary>
        /// Removes a data source with the specified name and tag from the list of data sources.
        /// Throws ArgumentNullException if the name or tag is null.
        /// </summary>
        /// <param name="name">The name of the data source to remove.</param>
        /// <param name="tag">The tag of the data source to remove.</param>
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
        /// <summary>
        /// Removes a data source with the specified data source from the list of data sources.
        /// Throws ArgumentNullException if the data source is null.
        /// </summary>
        /// <param name="dataSource">The data source to remove.</param>
        public void RemoveDataSource(string dataSource)
        {
            if (dataSource == null)
                throw new ArgumentNullException(nameof(dataSource));

            var list = (from s in Sources
                        where !(s.DataSource == dataSource)
                        select s).ToList();
            Sources = list;
        }
        /// <summary>
        /// Removes all data sources with the specified name from the list of data sources.
        /// Throws ArgumentNullException if the name is null.
        /// </summary>
        /// <param name="name">The name of the data sources to remove.</param>
        public void RemoveName(string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            var list = (from s in Sources
                        where !(s.Name == name)
                        select s).ToList();
            Sources = list;
        }

        /// <summary>
        /// Finds and returns an IDataSource instance from the list of data sources based on the specified name and tag.
        /// If the name or tag is null, throws ArgumentNullException.
        /// The method checks if there is a DataSourceTracker in the Sources list where the Name matches the specified name
        /// and the Tag matches the specified tag or if the CreateTime of the DataSourceTracker matches the specified tag.
        /// If a matching DataSourceTracker is found, a cloned instance of it is returned as an IDataSource.
        /// If no match is found, null is returned.
        /// </summary>
        /// <param name="name">The name of the data source to search for.</param>
        /// <param name="tag">The tag of the data source to search for.</param>
        /// <returns>A cloned instance of the found data source or null if not found.</returns>
        public IDataSource FindFromNameTag(string name, string tag)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (tag == null)
                throw new ArgumentNullException(nameof(tag));

            return Sources.FirstOrDefault(t => t.Name == name && (t.Tag == tag || t.CreateTime.ToDateString() == tag))?.Clone();
        }
        /// <summary>
        /// Finds and returns an IDataSource instance from the list of data sources based on the specified data source.
        /// If the data source is null, throws ArgumentNullException.
        /// The method checks if there is a DataSourceTracker in the Sources list where the DataSource matches the specified data source.
        /// If a matching DataSourceTracker is found, a cloned instance of it is returned as an IDataSource.
        /// If no match is found, null is returned.
        /// </summary>
        /// <param name="dataSource">The data source to search for.</param>
        /// <returns>A cloned instance of the found data source or null if not found.</returns>
        public IDataSource FindFromDataSource(string dataSource)
        {
            if (dataSource == null)
                throw new ArgumentNullException(nameof(dataSource));

            return Sources.Where(s => s.DataSource == dataSource).FirstOrDefault()?.Clone();
        }
        /// <summary>
        /// Finds and returns a collection of IDataSource instances from the list of data sources based on the specified name.
        /// If the name is null, throws ArgumentNullException.
        /// The method searches for DataSourceTrackers in the Sources list where the Name matches the specified name.
        /// If matching DataSourceTrackers are found, clones of them are added to a collection of IDataSource instances.
        /// The collection is ordered in descending order based on the CreateTime of the DataSourceTrackers.
        /// Returns the collection if it contains elements, otherwise returns null.
        /// </summary>
        /// <param name="name">The name of the data sources to search for.</param>
        /// <returns>A collection of cloned instances of the found data sources or null if not found.</returns>
        public IEnumerable<IDataSource> FindFromName(string name)
        {
            var list = (from s in Sources
                        where s.Name == name
                        orderby s.CreateTime descending
                        select s.Clone()).ToList();

            return list.Count > 0 ? list : null;
        }

        /// <summary>
        /// Saves the list of data sources to a JSON file located at the specified path.
        /// If the directory of the path does not exist, it creates the directory.
        /// The data sources are sorted first by name in ascending order, and then by CreateTime in descending order.
        /// The sorted list is serialized to a JSON file using Newtonsoft.Json and saved to the specified path.
        /// </summary>
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

        /// <summary>
        /// Loads the data sources from a JSON file located at the specified path.
        /// If the file doesn't exist, a new DataSourceFactory instance is created, its data sources are saved to the file,
        /// and the instance is returned.
        /// If deserialization from the file fails, logs the error details, creates a new DataSourceFactory instance,
        /// saves its data sources to the file, and returns the new instance.
        /// </summary>
        /// <returns>A DataSourceFactory instance with loaded data sources or a new instance if loading fails.</returns>
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
                value.Sources = JsonConvert.DeserializeObject<List<DataSourceTracker>>(File.ReadAllText(path));
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